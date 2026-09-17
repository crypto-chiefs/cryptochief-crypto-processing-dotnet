using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CryptoChief.Processing.Errors;
using CryptoChief.Processing.Http;
using CryptoChief.Processing.Models;
using CryptoChief.Processing.Webhooks;
using CryptoChief.Processing.Webhooks.Events;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CryptoChief.Processing.Tests;

// Kestrel on 127.0.0.1: a gateway mock that checks HMAC v1 as the specification describes, and a
// webhook receiver built on ASP.NET Core request headers and the raw request body.
public class RealHttpTests
{
    private const string Merchant = "M-1";
    private const string ApiKey = "K-1";

    [Fact]
    public async Task Client_requests_pass_gateway_hmac_v1_check()
    {
        await using var gateway = await GatewayMock.StartAsync();
        using var http = new HttpClient();
        var client = NewClient(http, gateway.BaseUrl);

        var balance = await client.Credits.BalanceAsync();
        var wallet = await client.Wallets.GenerateAsync(new GenerateWalletRequest
        {
            WalletType = WalletType.Static,
            ChainFamily = "EVM",
            Label = "Shop EU <&> Ж",
            CallbackUrl = "https://shop.test/hook?a=1&b=2",
        });

        balance.UsdBalance.Should().Be("2.50");
        wallet.Address.Should().Be("0xdead");

        gateway.Requests.Should().HaveCount(2);
        foreach (var r in gateway.Requests)
        {
            r.Status.Should().Be(200, r.Refusal);
            r.Headers.ContainsKey("Signature").Should().BeFalse();
        }

        gateway.Requests[0].Body.Should().Equal("{}"u8.ToArray());
        Encoding.UTF8.GetString(gateway.Requests[1].Body).ShouldBeJson(
            "{\"wallet_type\":\"static\",\"chain_family\":\"EVM\",\"label\":\"Shop EU <&> Ж\","
            + "\"callback_url\":\"https://shop.test/hook?a=1&b=2\"}");
    }

    [Fact]
    public async Task Client_corrects_its_clock_from_gateway_server_time()
    {
        await using var gateway = await GatewayMock.StartAsync();
        using var http = new HttpClient();
        var client = NewClient(http, gateway.BaseUrl);
        client.Transport.Clock = () => DateTimeOffset.UtcNow.AddSeconds(-1000);

        await client.Credits.BalanceAsync();

        gateway.Requests.Select(r => r.Status).Should().Equal(401, 200);
        gateway.Requests[0].Refusal.Should().Be("SIGNATURE_TIMESTAMP_OUT_OF_RANGE");
        client.Transport.ClockOffsetSeconds.Should().BeInRange(995, 1005);
    }

    [Fact]
    public async Task Gateway_refuses_md5_signature_without_hmac_headers()
    {
        await using var gateway = await GatewayMock.StartAsync();
        using var http = new HttpClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, gateway.BaseUrl + "/v1/credits/balance")
        {
            Content = new ByteArrayContent("{}"u8.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } },
        };
        req.Headers.TryAddWithoutValidation("Merchant", Merchant);
        req.Headers.TryAddWithoutValidation("Signature", "0123456789abcdef0123456789abcdef");
        using var resp = await http.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("BAD_AUTH_HEADERS");

        var client = NewClient(http, gateway.BaseUrl);
        await client.Credits.BalanceAsync();
        gateway.Requests.Select(r => r.Status).Should().Equal(400, 200);
    }

    [Fact]
    public async Task Gateway_mock_refuses_a_body_that_differs_from_the_signed_one()
    {
        await using var gateway = await GatewayMock.StartAsync();
        using var http = new HttpClient(new TamperingHandler { InnerHandler = new HttpClientHandler() });
        var client = NewClient(http, gateway.BaseUrl);

        var ex = await FluentActions.Invoking(() => client.Wallets.SetLabelAsync("0xdead", "Shop EU"))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be(ErrorCodes.InvalidSignature);
        gateway.Requests.Should().ContainSingle().Which.Refusal.Should().Be("INVALID_SIGNATURE");
    }

    public static TheoryData<string> TransmissibleVectors()
    {
        var data = new TheoryData<string>();
        foreach (var v in WebhookV1Vectors.List)
        {
            var values = v.ReceivedHeaders().SelectMany(h => h.Value);
            if (values.All(x => x.All(c => c is >= ' ' and <= '~' or '\t'))) data.Add(v.Name);
        }
        return data;
    }

    [Fact]
    public void Most_vectors_travel_over_http()
    {
        TransmissibleVectors().Count().Should().BeGreaterThan(40);
    }

    [Theory]
    [MemberData(nameof(TransmissibleVectors))]
    public async Task Receiver_on_request_headers_and_raw_body_answers_vector(string name)
    {
        var v = WebhookV1Vectors.Get(name);
        await using var receiver = await WebhookReceiver.StartAsync(v.ApiKey);
        using var http = new HttpClient();

        foreach (var route in new[] { "/headers", "/func" })
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, receiver.BaseUrl + route)
            {
                Content = new ByteArrayContent(v.BodyBytes) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } },
            };
            foreach (var h in v.ReceivedHeaders())
                req.Headers.TryAddWithoutValidation(h.Key, h.Value).Should().BeTrue();
            req.Headers.TryAddWithoutValidation("X-Test-Now", v.Now.ToString(CultureInfo.InvariantCulture));

            using var resp = await http.SendAsync(req);
            var answer = await resp.Content.ReadAsStringAsync();

            var expected = v.Expect == "ok" ? "ok" : v.ExpectedException!.Name;
            answer.Should().Be(expected, $"{route} for {name}");
            resp.StatusCode.Should().Be(v.Expect == "ok" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Receiver_decodes_events_signed_now_over_http()
    {
        await using var receiver = await WebhookReceiver.StartAsync(ApiKey);
        using var http = new HttpClient();

        foreach (var name in new[] { "payout_paid", "sweep_confirmed", "body_not_utf8", "body_large_65536_bytes" })
        {
            var body = WebhookV1Vectors.Get(name).BodyBytes;
            var headers = Wire.SignedWebhookHeaders(ApiKey, body, "dlv_" + name.Replace('_', '-'));

            using var req = new HttpRequestMessage(HttpMethod.Post, receiver.BaseUrl + "/payout")
            {
                Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } },
            };
            foreach (var (key, values) in headers) req.Headers.TryAddWithoutValidation(key, values);
            using var resp = await http.SendAsync(req);
            var answer = await resp.Content.ReadAsStringAsync();

            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            answer.Should().Be(name switch
            {
                "sweep_confirmed" => "sweep.confirmed",
                "body_not_utf8" => "not-json",
                _ => "payout.paid",
            });
        }

        receiver.Deliveries.Should().Equal("dlv_payout-paid", "dlv_sweep-confirmed", "dlv_body-not-utf8", "dlv_body-large-65536-bytes");
    }

    [Fact]
    public async Task Receiver_in_the_example_shape_answers_a_verified_body_it_cannot_decode()
    {
        await using var receiver = await WebhookReceiver.StartAsync(ApiKey);
        using var http = new HttpClient();

        var cases = new (string Body, int Status, string Answer)[]
        {
            ("not json at all", 400, "undecodable"),
            ("[1,2,3]", 400, "undecodable"),
            ("{\"event\":\"payout.paid\",\"confirmations\":{\"a\":1}}", 400, "undecodable"),
            ("null", 400, "undecodable"),
            ("{\"event\":\"payout.paid\",\"uuid\":\"p-1\"}", 200, "payout.paid"),
        };

        foreach (var (text, status, answer) in cases)
        {
            var body = Encoding.UTF8.GetBytes(text);
            using var resp = await PostSignedAsync(http, receiver.BaseUrl + "/typed", body,
                Wire.SignedWebhookHeaders(ApiKey, body, "dlv_typed"));

            ((int)resp.StatusCode).Should().Be(status, text);
            (await resp.Content.ReadAsStringAsync()).Should().Be(answer, text);
        }

        // A signature the key does not confirm stays a separate answer from a body that will not decode.
        var refused = "not json at all"u8.ToArray();
        using var bad = await PostSignedAsync(http, receiver.BaseUrl + "/typed", refused,
            Wire.SignedWebhookHeaders("other-key", refused, "dlv_typed"));

        bad.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await bad.Content.ReadAsStringAsync()).Should().Be(nameof(WebhookSignatureException));
    }

    private static async Task<HttpResponseMessage> PostSignedAsync(HttpClient http, string url, byte[] body,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } },
        };
        foreach (var (key, values) in headers) req.Headers.TryAddWithoutValidation(key, values);
        return await http.SendAsync(req);
    }

    [Fact]
    public async Task Receiver_refuses_legacy_signature_header_over_http()
    {
        await using var receiver = await WebhookReceiver.StartAsync(ApiKey);
        using var http = new HttpClient();
        var body = WebhookV1Vectors.Get("payout_paid").BodyBytes;

        using var req = new HttpRequestMessage(HttpMethod.Post, receiver.BaseUrl + "/payout")
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } },
        };
        req.Headers.TryAddWithoutValidation("Signature", "0123456789abcdef0123456789abcdef");
        req.Headers.TryAddWithoutValidation("X-Webhook-Signature", "0123456789abcdef0123456789abcdef");
        req.Headers.TryAddWithoutValidation("X-Webhook-Delivery", "7c9e6679-7425-40de-944b-e07fc1f90ae7");
        using var resp = await http.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Be(nameof(WebhookHeadersException));
    }

    [Theory]
    [InlineData("X-CC-Timestamp")]
    [InlineData("X-Webhook-Delivery")]
    [InlineData("X-CC-Signature")]
    public async Task Receiver_on_request_headers_refuses_an_empty_repeated_header_over_http(string repeated)
    {
        await using var receiver = await WebhookReceiver.StartAsync(ApiKey);
        var body = WebhookV1Vectors.Get("payout_paid").BodyBytes;
        var headers = Wire.SignedWebhookHeaders(ApiKey, body, "dlv_empty-repeat");
        var now = headers.Single(h => h.Key == WebhookVerifier.TimestampHeader).Value.Single();

        var extra = new List<string> { repeated + ":" };
        (await RawPostAsync(receiver.BaseUrl, "/headers", body, headers, extra, now))
            .Should().Be("401 " + nameof(WebhookHeadersException));
        (await RawPostAsync(receiver.BaseUrl, "/headers", body, headers, new List<string>(), now))
            .Should().Be("200 ok");
        (await RawPostAsync(receiver.BaseUrl, "/func", body, headers, extra, now))
            .Should().Be("200 ok", "a value by name does not show an empty copy of a header");
    }

    // Two header lines with one name cannot be sent through HttpClient: it joins the values into one line.
    private static async Task<string> RawPostAsync(string baseUrl, string path, byte[] body,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers, List<string> extraLines, string now)
    {
        var uri = new Uri(baseUrl);
        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync(uri.Host, uri.Port);
        await using var stream = tcp.GetStream();

        var head = new StringBuilder();
        head.Append("POST ").Append(path).Append(" HTTP/1.1\r\n");
        head.Append("Host: ").Append(uri.Authority).Append("\r\n");
        head.Append("Content-Type: application/json\r\n");
        head.Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        head.Append("Connection: close\r\n");
        head.Append("X-Test-Now: ").Append(now).Append("\r\n");
        foreach (var (name, values) in headers)
            foreach (var value in values)
                head.Append(name).Append(": ").Append(value).Append("\r\n");
        foreach (var line in extraLines) head.Append(line).Append("\r\n");
        head.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
        await stream.WriteAsync(body);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var response = await reader.ReadToEndAsync();
        var split = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var responseHead = response.Substring(0, split);
        var text = response.Substring(split + 4);
        if (responseHead.Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase))
        {
            var decoded = new StringBuilder();
            var pos = 0;
            while (true)
            {
                var eol = text.IndexOf("\r\n", pos, StringComparison.Ordinal);
                var size = int.Parse(text.AsSpan(pos, eol - pos), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (size == 0) break;
                decoded.Append(text, eol + 2, size);
                pos = eol + 2 + size + 2;
            }
            text = decoded.ToString();
        }
        return responseHead.Substring(9, 3) + " " + text;
    }

    private static CryptoChiefClient NewClient(HttpClient http, string baseUrl) =>
        new(new CryptoChiefClientOptions
        {
            MerchantId = Merchant,
            ApiKey = ApiKey,
            BaseUrl = baseUrl,
            MaxRetries = 0,
        }, http, null);

    private static async Task<(WebApplication App, string BaseUrl)> StartAsync(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        map(app);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, address.TrimEnd('/'));
    }

    private static async Task Answer(HttpContext ctx, int status, string text)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "text/plain";
        await ctx.Response.WriteAsync(text);
    }

    private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    private static bool IsNonceChar(char c) => c is (>= '0' and <= '9') or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_' or '-';

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request)
    {
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms);
        return ms.ToArray();
    }

    private sealed class TamperingHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsByteArrayAsync(ct);
            var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(body).Replace("Shop EU", "Shop US"));
            request.Content = new ByteArrayContent(tampered) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } };
            return await base.SendAsync(request, ct);
        }
    }

    internal sealed record Received(Dictionary<string, string[]> Headers, byte[] Body, int Status, string? Refusal);

    // Checks requests as signature-hmac-v1.md describes; independent of RequestSigner.
    private sealed class GatewayMock : IAsyncDisposable
    {
        private readonly ConcurrentDictionary<string, bool> _nonces = new();
        private WebApplication _app = null!;

        public string BaseUrl { get; private set; } = "";
        public List<Received> Requests { get; } = new();

        public static async Task<GatewayMock> StartAsync()
        {
            var mock = new GatewayMock();
            (mock._app, mock.BaseUrl) = await RealHttpTests.StartAsync(app => app.Run(mock.HandleAsync));
            return mock;
        }

        private async Task HandleAsync(HttpContext ctx)
        {
            var body = await ReadBodyAsync(ctx.Request);
            var headers = ctx.Request.Headers.ToDictionary(h => h.Key, h => h.Value.Select(x => x ?? "").ToArray(), StringComparer.OrdinalIgnoreCase);
            var (status, code, serverTime) = Check(ctx.Request, headers, body);

            lock (Requests) Requests.Add(new Received(headers, body, status, code));
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            if (code is not null)
            {
                var st = serverTime is { } t ? $",\"server_time\":{t}" : "";
                await ctx.Response.WriteAsync($"{{\"ok\":false,\"error\":\"{code}\",\"msg\":\"{code}\"{st}}}");
                return;
            }
            await ctx.Response.WriteAsync(ctx.Request.Path.Value switch
            {
                "/v1/credits/balance" => "{\"credits_balance\":25000000,\"usd_balance\":\"2.50\",\"is_postpaid\":false}",
                "/v1/wallets/info" or "/v1/wallets/generate" or "/v1/wallets/label" =>
                    "{\"type\":\"static\",\"address\":\"0xdead\",\"chain_family\":\"EVM\",\"frozen\":false,"
                    + "\"master_wallet_address\":\"0xbeef\",\"callback_url\":null,\"label\":\"Shop EU\"}",
                // Everything else: what the server read, for the low-level request tests.
                _ => JsonSerializer.Serialize(new
                {
                    method = ctx.Request.Method,
                    path = ctx.Request.Path.Value,
                    query = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value!.Substring(1) : "",
                    idempotency_key = ctx.Request.Headers["Idempotency-Key"].ToString(),
                    body = Encoding.UTF8.GetString(body),
                }),
            });
        }

        // The gateway's own check, over the request as this server read it.
        private (int Status, string? Code, long? ServerTime) Check(HttpRequest request, Dictionary<string, string[]> headers, byte[] body)
        {
            var received = new List<KeyValuePair<string, string>>();
            foreach (var header in request.Headers)
                foreach (var value in header.Value)
                    received.Add(new(header.Key, value ?? ""));

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var outcome = GatewayHmacV1.Check(
                new GatewayHmacV1.SignedRequest(
                    request.Method,
                    request.Path.Value ?? "",
                    request.QueryString.HasValue ? request.QueryString.Value!.Substring(1) : "",
                    body,
                    received),
                ApiKey, now);

            switch (outcome)
            {
                case GatewayHmacV1.BadAuthHeaders:
                    return (400, "BAD_AUTH_HEADERS", null);
                case GatewayHmacV1.TimestampOutOfRange:
                    return (401, "SIGNATURE_TIMESTAMP_OUT_OF_RANGE", now);
                case GatewayHmacV1.InvalidSignature:
                    return (401, "INVALID_SIGNATURE", null);
            }

            var merchant = headers["Merchant"][0].Trim(' ', '\t');
            var nonce = headers["X-CC-Nonce"][0].Trim(' ', '\t');
            if (!_nonces.TryAdd(merchant + "\n" + nonce, true))
                return (401, "SIGNATURE_REPLAYED", null);
            return (200, null, null);
        }

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }

    private sealed class WebhookReceiver : IAsyncDisposable
    {
        private WebApplication _app = null!;

        public string BaseUrl { get; private set; } = "";
        public List<string> Deliveries { get; } = new();

        public static async Task<WebhookReceiver> StartAsync(string apiKey)
        {
            var receiver = new WebhookReceiver();
            (receiver._app, receiver.BaseUrl) = await RealHttpTests.StartAsync(app =>
            {
                // Clock from X-Test-Now: the vectors are signed at a fixed time.
                static WebhookVerifyOptions Options(HttpRequest req) => new()
                {
                    Now = () => DateTimeOffset.FromUnixTimeSeconds(long.Parse(req.Headers["X-Test-Now"]!, CultureInfo.InvariantCulture)),
                };

                app.MapPost("/headers", async ctx =>
                {
                    var body = await ReadBodyAsync(ctx.Request);
                    try
                    {
                        WebhookVerifier.Verify(apiKey, body, ctx.Request.Headers, Options(ctx.Request));
                        await Answer(ctx, 200, "ok");
                    }
                    catch (WebhookVerificationException ex)
                    {
                        await Answer(ctx, 401, ex.GetType().Name);
                    }
                });

                app.MapPost("/func", async ctx =>
                {
                    var req = ctx.Request;
                    var body = await ReadBodyAsync(req);
                    if (WebhookVerifier.TryVerify(apiKey, body, name => req.Headers[name], Options(req)))
                    {
                        await Answer(ctx, 200, "ok");
                        return;
                    }
                    try
                    {
                        WebhookVerifier.Verify(apiKey, body, name => req.Headers[name], Options(req));
                        await Answer(ctx, 500, "TryVerify and Verify disagree");
                    }
                    catch (WebhookVerificationException ex)
                    {
                        await Answer(ctx, 401, ex.GetType().Name);
                    }
                });

                // The shape of examples/WebhookServer and the README: verify, decode into the
                // event type, and answer a refused signature apart from a body that will not decode.
                app.MapPost("/typed", async ctx =>
                {
                    var req = ctx.Request;
                    var body = await ReadBodyAsync(req);
                    try
                    {
                        var evt = WebhookVerifier.VerifyAndDecode<PayoutWebhookEvent>(apiKey, body, req.Headers);
                        await Answer(ctx, 200, evt.Event ?? "no-event");
                    }
                    catch (WebhookVerificationException ex)
                    {
                        await Answer(ctx, 401, ex.GetType().Name);
                    }
                    catch (Exception ex) when (ex is JsonException or CryptoChiefException)
                    {
                        await Answer(ctx, 400, "undecodable");
                    }
                });

                app.MapPost("/payout", async ctx =>
                {
                    var req = ctx.Request;
                    var body = await ReadBodyAsync(req);
                    try
                    {
                        var evt = WebhookVerifier.VerifyAndDecode<JsonElement>(apiKey, body, req.Headers);
                        lock (receiver.Deliveries) receiver.Deliveries.Add(req.Headers[WebhookVerifier.DeliveryHeader].ToString());
                        await Answer(ctx, 200, evt.TryGetProperty("event", out var e) ? e.GetString()! : "no-event");
                    }
                    catch (WebhookVerificationException ex)
                    {
                        await Answer(ctx, 401, ex.GetType().Name);
                    }
                    catch (JsonException)
                    {
                        lock (receiver.Deliveries) receiver.Deliveries.Add(req.Headers[WebhookVerifier.DeliveryHeader].ToString());
                        await Answer(ctx, 200, "not-json");
                    }
                });
            });
            return receiver;
        }

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }
}
