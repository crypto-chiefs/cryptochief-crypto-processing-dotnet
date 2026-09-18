using System.Net;
using System.Text;
using System.Text.Json;
using CryptoChief.Processing;
using CryptoChief.Processing.Errors;
using CryptoChief.Processing.Http;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class HmacV1TransportTests
{
    private const long T = 1789430400;

    private const string WalletBody =
        "{\"type\":\"master\",\"address\":\"TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7\","
        + "\"chain_family\":\"TRON\",\"frozen\":false}";

    [Theory]
    [InlineData("wallets_info")]
    [InlineData("wallets_freeze_same_body_as_wallets_info")]
    public async Task Sends_hmac_v1_headers_equal_to_gateway_vector(string name)
    {
        var v = HmacV1Vectors.Get(name);
        var handler = new SnapshotHandler(_ => Resp(HttpStatusCode.OK, WalletBody));
        var client = NewClient(handler, v.Merchant, v.ApiKey, "https://test/");
        client.Transport.Clock = () => DateTimeOffset.FromUnixTimeSeconds(T);
        client.Transport.NonceFactory = () => v.Nonce;

        var address = "TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7";
        if (v.Path == "/v1/wallets/info") await client.Wallets.InfoAsync(address);
        else await client.Wallets.FreezeAsync(address);

        var req = handler.Requests.Should().ContainSingle().Subject;
        req.Uri.AbsolutePath.Should().Be(v.Path);
        req.Body.Should().Equal(v.BodyBytes);
        req.Header("Merchant").Should().Be(v.Merchant);
        req.Header("X-CC-Timestamp").Should().Be(v.Timestamp);
        req.Header("X-CC-Nonce").Should().Be(v.Nonce);
        req.Header("X-CC-Signature").Should().Be("v1=" + v.Signature);
        req.Headers.Should().NotContainKey("Signature");
    }

    [Theory]
    [InlineData(" {0} ")]
    [InlineData("\t{0} \t")]
    public async Task Merchant_with_surrounding_spaces_and_tabs_signs_as_trimmed_value(string format)
    {
        var v = HmacV1Vectors.Get("wallets_info");
        var handler = new SnapshotHandler(_ => Resp(HttpStatusCode.OK, WalletBody));
        var client = NewClient(handler, string.Format(format, v.Merchant), v.ApiKey, "https://test/");
        client.Transport.Clock = () => DateTimeOffset.FromUnixTimeSeconds(T);
        client.Transport.NonceFactory = () => v.Nonce;

        await client.Wallets.InfoAsync("TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7");

        var req = handler.Requests.Should().ContainSingle().Subject;
        req.Header("Merchant").Should().Be(v.Merchant);
        req.Header("X-CC-Signature").Should().Be("v1=" + v.Signature);
    }

    [Fact]
    public async Task Base_url_path_prefix_is_not_signed()
    {
        var v = HmacV1Vectors.Get("wallets_info");
        var handler = new SnapshotHandler(_ => Resp(HttpStatusCode.OK, WalletBody));
        var client = NewClient(handler, v.Merchant, v.ApiKey, "https://test/prefix/");
        client.Transport.Clock = () => DateTimeOffset.FromUnixTimeSeconds(T);
        client.Transport.NonceFactory = () => v.Nonce;

        await client.Wallets.InfoAsync("TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7");

        var req = handler.Requests.Should().ContainSingle().Subject;
        req.Uri.AbsolutePath.Should().Be("/prefix/v1/wallets/info");
        req.Header("X-CC-Signature").Should().Be("v1=" + v.Signature);
    }

    [Fact]
    public async Task Query_is_taken_from_the_request_url()
    {
        var v = HmacV1Vectors.Get("query");
        var handler = new SnapshotHandler(_ => Resp(HttpStatusCode.OK, "{}"));
        var client = NewClient(handler, v.Merchant, v.ApiKey, "https://test/");
        client.Transport.Clock = () => DateTimeOffset.FromUnixTimeSeconds(T);
        client.Transport.NonceFactory = () => v.Nonce;

        await client.Transport.SendAsync(v.Path + "?" + v.Query, new { }, CancellationToken.None);

        var req = handler.Requests.Should().ContainSingle().Subject;
        req.Uri.Query.Should().Be("?" + v.Query);
        req.Body.Should().Equal(v.BodyBytes);
        req.Header("X-CC-Signature").Should().Be("v1=" + v.Signature);
    }

    [Fact]
    public async Task Get_with_a_query_is_signed_and_sent_as_the_vector()
    {
        var v = HmacV1Vectors.Get("get_query_empty_body");
        var handler = new SnapshotHandler(_ => Resp(HttpStatusCode.OK, "{}"));
        var client = NewClient(handler, v.Merchant, v.ApiKey, "https://test/");
        client.Transport.Clock = () => DateTimeOffset.FromUnixTimeSeconds(T);
        client.Transport.NonceFactory = () => v.Nonce;

        await client.RequestAsync<JsonElement>(HttpMethod.Get, v.Path + "?" + v.Query);

        var req = handler.Requests.Should().ContainSingle().Subject;
        req.Method.Should().Be(HttpMethod.Get);
        req.Uri.AbsolutePath.Should().Be(v.Path);
        req.Uri.Query.Should().Be("?" + v.Query);
        req.Body.Should().BeEmpty();
        req.Headers.Should().NotContainKey("Content-Type");
        req.Header("X-CC-Signature").Should().Be("v1=" + v.Signature);
    }

    [Fact]
    public async Task Method_is_sent_and_signed_upper_cased()
    {
        var handler = new SnapshotHandler(_ => Resp(HttpStatusCode.OK, "{}"));
        var client = NewClient(handler, "M-1", "K-1", "https://test/");

        await client.RequestAsync<JsonElement>(new HttpMethod("pAtCh"), "/v1/wallets/label", new { label = "x" });

        var req = handler.Requests.Should().ContainSingle().Subject;
        req.Method.Method.Should().Be("PATCH");
        req.Header("X-CC-Signature").Should().Be("v1=" + GatewaySignature(req, "M-1", "K-1"));
    }

    [Fact]
    public async Task Get_cannot_carry_a_body()
    {
        var handler = new SnapshotHandler(_ => Resp(HttpStatusCode.OK, "{}"));
        var client = NewClient(handler, "M-1", "K-1", "https://test/");

        await FluentActions.Invoking(() => client.RequestAsync<JsonElement>(HttpMethod.Get, "/v1/x", new { a = 1 }))
            .Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    // The path is signed as the server reads it — percent-decoded — while the escaped spelling
    // goes on the wire. The query is signed as the URL carries it.
    [Theory]
    [InlineData("/v1/orders/payout%2F8814", "")]
    [InlineData("/v1/a%20b/c", "")]
    [InlineData("/v1/a%2Fb%20c/ключ", "q=%2F&s=a%20b&t=ключ")]
    [InlineData("/v1/ключ/%E2%82%AC", "coin=%D0%A2%D0%A0%D0%9A")]
    public async Task Path_is_signed_percent_decoded_and_query_raw(string path, string query)
    {
        var handler = new SnapshotHandler(_ => Resp(HttpStatusCode.OK, "{}"));
        var client = NewClient(handler, "M-1", "K-1", "https://test/");
        var target = query.Length > 0 ? path + "?" + query : path;

        await client.RequestAsync<JsonElement>(target, new { a = 1 });

        var req = handler.Requests.Should().ContainSingle().Subject;
        // What the server reads: the wire path decoded, the query as sent.
        var serverPath = Uri.UnescapeDataString(req.Uri.AbsolutePath);
        serverPath.Should().Be(Uri.UnescapeDataString(path));
        req.Header("X-CC-Signature").Should().Be("v1=" + GatewaySignature(req, "M-1", "K-1"));
    }

    [Theory]
    [InlineData("/v1/a%2")]
    [InlineData("/v1/%zz/b")]
    [InlineData("/v1/b%")]
    public async Task Path_that_is_not_percent_encoding_is_rejected(string path)
    {
        var handler = new SnapshotHandler(_ => Resp(HttpStatusCode.OK, "{}"));
        var client = NewClient(handler, "M-1", "K-1", "https://test/");

        await FluentActions.Invoking(() => client.RequestAsync<JsonElement>(path, new { }))
            .Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Idempotency_key_is_sent_and_signed()
    {
        var v = HmacV1Vectors.Get("idempotency_key_payout_execute");
        var handler = new SnapshotHandler(_ => Resp(HttpStatusCode.OK, "{}"));
        var client = NewClient(handler, v.Merchant, v.ApiKey, "https://test/");
        client.Transport.Clock = () => DateTimeOffset.FromUnixTimeSeconds(T);
        client.Transport.NonceFactory = () => v.Nonce;

        await client.WithIdempotencyKey(v.IdempotencyKey).RequestAsync<JsonElement>(v.Path, new
        {
            OrderId = "po-1",
            Amount = "10.5",
            Network = "TRON_MAINNET",
            Coin = "USDT",
            ToAddress = "TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7",
        });

        var req = handler.Requests.Should().ContainSingle().Subject;
        Encoding.UTF8.GetString(req.Body).Should().Be(v.Body);
        req.Header("Idempotency-Key").Should().Be(v.IdempotencyKey);
        req.Header("X-CC-Signature").Should().Be("v1=" + v.Signature);
    }

    [Fact]
    public async Task Idempotency_key_reaches_every_service_call()
    {
        var handler = new SnapshotHandler(_ => Resp(HttpStatusCode.OK, WalletBody));
        var client = NewClient(handler, "M-1", "K-1", "https://test/");
        var idempotent = client.WithIdempotencyKey("payout-2026-09-16-0001");

        await idempotent.Wallets.InfoAsync("TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7");
        await client.Wallets.InfoAsync("TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7");

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Header("Idempotency-Key").Should().Be("payout-2026-09-16-0001");
        handler.Requests[0].Header("X-CC-Signature").Should()
            .Be("v1=" + GatewaySignature(handler.Requests[0], "M-1", "K-1", "payout-2026-09-16-0001"));
        handler.Requests[1].Headers.Should().NotContainKey("Idempotency-Key");
        handler.Requests[1].Header("X-CC-Signature").Should()
            .Be("v1=" + GatewaySignature(handler.Requests[1], "M-1", "K-1"));
    }

    [Fact]
    public async Task Empty_idempotency_key_sends_no_header()
    {
        var handler = new SnapshotHandler(_ => Resp(HttpStatusCode.OK, WalletBody));
        var client = NewClient(handler, "M-1", "K-1", "https://test/");

        await client.WithIdempotencyKey("").Wallets.InfoAsync("TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7");
        await client.WithIdempotencyKey(null).Wallets.InfoAsync("TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7");

        handler.Requests.Should().HaveCount(2);
        foreach (var req in handler.Requests)
        {
            req.Headers.Should().NotContainKey("Idempotency-Key");
            req.Header("X-CC-Signature").Should().Be("v1=" + GatewaySignature(req, "M-1", "K-1"));
        }
    }

    // The server trims spaces and tabs before it verifies, so an untrimmed key would be signed
    // in a form it never sees.
    [Theory]
    [InlineData(" key")]
    [InlineData("key ")]
    [InlineData("\tkey")]
    [InlineData("key\t")]
    [InlineData("key\nnext")]
    [InlineData("ключ")]
    public void Idempotency_key_that_cannot_be_sent_as_it_is_is_rejected(string key)
    {
        var handler = new SnapshotHandler(_ => Resp(HttpStatusCode.OK, "{}"));
        var client = NewClient(handler, "M-1", "K-1", "https://test/");

        FluentActions.Invoking(() => client.WithIdempotencyKey(key)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Idempotency_key_view_shares_the_clock_offset()
    {
        var calls = 0;
        var handler = new SnapshotHandler(_ => ++calls == 1
            ? Resp(HttpStatusCode.Unauthorized, OutOfRangeBody(T + 1000))
            : Resp(HttpStatusCode.OK, WalletBody));
        var client = NewClient(handler, "M-1", "K-1", "https://test/", maxRetries: 0);
        client.Transport.Clock = () => DateTimeOffset.FromUnixTimeSeconds(T);
        var idempotent = client.WithIdempotencyKey("k-1");

        await idempotent.Wallets.InfoAsync("TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7");

        idempotent.Transport.ClockOffsetSeconds.Should().Be(1000);
        client.Transport.ClockOffsetSeconds.Should().Be(1000);
    }

    [Fact]
    public async Task Retry_recomputes_timestamp_nonce_and_signature()
    {
        var calls = 0;
        var handler = new SnapshotHandler(_ => ++calls < 3
            ? Resp(HttpStatusCode.InternalServerError, "{\"ok\":false,\"error\":\"SERVICE_ERROR\"}")
            : Resp(HttpStatusCode.OK, WalletBody));
        var client = NewClient(handler, "M-1", "K-1", "https://test/");
        var now = T;
        client.Transport.Clock = () => DateTimeOffset.FromUnixTimeSeconds(now++);

        await client.Wallets.InfoAsync("TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7");

        handler.Requests.Should().HaveCount(3);
        handler.Requests.Select(r => r.Header("X-CC-Timestamp")).Should()
            .Equal(T.ToString(), (T + 1).ToString(), (T + 2).ToString());
        handler.Requests.Select(r => r.Header("X-CC-Nonce")).Should().OnlyHaveUniqueItems()
            .And.AllSatisfy(n => n.Should().MatchRegex("^[0-9a-f]{32}$"));
        handler.Requests.Select(r => r.Header("X-CC-Signature")).Should().OnlyHaveUniqueItems();
        handler.Requests.Select(r => Encoding.UTF8.GetString(r.Body)).Distinct().Should().ContainSingle();

        foreach (var req in handler.Requests)
            req.Header("X-CC-Signature").Should().Be(Recompute(req, "M-1", "K-1"));
    }

    [Fact]
    public async Task Body_is_the_serialized_request_with_exact_large_integers()
    {
        var handler = new SnapshotHandler(_ => Resp(HttpStatusCode.OK, "{}"));
        var client = NewClient(handler, "M-1", "K-1", "https://test/");

        await client.Transport.SendAsync("/v1/payments/history",
            new { page = 9007199254740993L, max = long.MaxValue, amount = 1.10m, note = (string?)null },
            CancellationToken.None);

        var req = handler.Requests.Should().ContainSingle().Subject;
        Encoding.UTF8.GetString(req.Body).Should()
            .Be("{\"page\":9007199254740993,\"max\":9223372036854775807,\"amount\":1.10}");
        req.Headers.Should().NotContainKey("Signature");
        req.Header("X-CC-Signature").Should().Be(Recompute(req, "M-1", "K-1"));
    }

    [Fact]
    public async Task Null_body_is_sent_empty_and_signed_as_empty()
    {
        var handler = new SnapshotHandler(_ => Resp(HttpStatusCode.OK, "{}"));
        var client = NewClient(handler, "M-1", "K-1", "https://test/");

        await client.Transport.SendAsync("/v1/credits/balance", null, CancellationToken.None);

        var req = handler.Requests.Should().ContainSingle().Subject;
        req.Body.Should().BeEmpty();
        req.Header("X-CC-Signature").Should().Be(Recompute(req, "M-1", "K-1"));
    }

    [Fact]
    public async Task Timestamp_out_of_range_corrects_clock_offset_and_retries_once()
    {
        var calls = 0;
        var handler = new SnapshotHandler(_ => ++calls == 1
            ? Resp(HttpStatusCode.Unauthorized, OutOfRangeBody(T + 1000))
            : Resp(HttpStatusCode.OK, WalletBody));
        // No retry budget: the clock correction does not use it.
        var client = NewClient(handler, "M-1", "K-1", "https://test/", maxRetries: 0);
        client.Transport.Clock = () => DateTimeOffset.FromUnixTimeSeconds(T);

        await client.Wallets.InfoAsync("TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7");

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Header("X-CC-Timestamp").Should().Be(T.ToString());
        handler.Requests[1].Header("X-CC-Timestamp").Should().Be((T + 1000).ToString());
        handler.Requests[1].Header("X-CC-Nonce").Should().NotBe(handler.Requests[0].Header("X-CC-Nonce"));
        handler.Requests[1].Header("X-CC-Signature").Should().Be(Recompute(handler.Requests[1], "M-1", "K-1"));
        client.Transport.ClockOffsetSeconds.Should().Be(1000);

        // The offset stays for later requests.
        await client.Wallets.InfoAsync("TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7");
        handler.Requests.Should().HaveCount(3);
        handler.Requests[2].Header("X-CC-Timestamp").Should().Be((T + 1000).ToString());
    }

    [Fact]
    public async Task Timestamp_out_of_range_twice_is_raised()
    {
        var handler = new SnapshotHandler(_ =>
            Resp(HttpStatusCode.Unauthorized, OutOfRangeBody(T - 900)));
        var client = NewClient(handler, "M-1", "K-1", "https://test/");
        client.Transport.Clock = () => DateTimeOffset.FromUnixTimeSeconds(T);

        var ex = await FluentActions
            .Invoking(() => client.Wallets.InfoAsync("TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7"))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be(ErrorCodes.SignatureTimestampOutOfRange);
        ex.Which.HttpStatus.Should().Be(HttpStatusCode.Unauthorized);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Timestamp_out_of_range_without_server_time_is_not_retried()
    {
        var handler = new SnapshotHandler(_ => Resp(HttpStatusCode.Unauthorized,
            "{\"ok\":false,\"error\":\"SIGNATURE_TIMESTAMP_OUT_OF_RANGE\",\"msg\":\"out of range\"}"));
        var client = NewClient(handler, "M-1", "K-1", "https://test/");

        var ex = await FluentActions
            .Invoking(() => client.Wallets.InfoAsync("TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7"))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be(ErrorCodes.SignatureTimestampOutOfRange);
        handler.Requests.Should().HaveCount(1);
        client.Transport.ClockOffsetSeconds.Should().Be(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "SIGNATURE_REPLAYED", "X-CC-Nonce has already been used")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "PAYLOAD_TOO_LARGE", "request body exceeds 8388608 bytes")]
    public async Task Signature_refusals_map_to_error_codes_without_retry(
        HttpStatusCode status, string code, string msg)
    {
        var handler = new SnapshotHandler(_ => Resp(status,
            $"{{\"ok\":false,\"error\":\"{code}\",\"msg\":\"{msg}\"}}"));
        var client = NewClient(handler, "M-1", "K-1", "https://test/");

        var ex = await FluentActions
            .Invoking(() => client.Wallets.InfoAsync("TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7"))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be(code);
        ex.Which.HttpStatus.Should().Be(status);
        handler.Requests.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Contour_timestamp_out_of_range_corrects_clock_offset_and_retries_once(bool topLevelServerTime)
    {
        var calls = 0;
        var handler = new SnapshotHandler(_ => ++calls == 1
            ? Resp(HttpStatusCode.Unauthorized, ContourOutOfRangeBody(T + 1000, topLevelServerTime))
            : Resp(HttpStatusCode.OK, WalletBody));
        var client = NewClient(handler, "M-1", "K-1", "https://test/", maxRetries: 0);
        client.Transport.Clock = () => DateTimeOffset.FromUnixTimeSeconds(T);

        await client.Wallets.InfoAsync("TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7");

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Header("X-CC-Timestamp").Should().Be(T.ToString());
        handler.Requests[1].Header("X-CC-Timestamp").Should().Be((T + 1000).ToString());
        handler.Requests[1].Header("X-CC-Nonce").Should().NotBe(handler.Requests[0].Header("X-CC-Nonce"));
        handler.Requests[1].Header("X-CC-Signature").Should().Be(Recompute(handler.Requests[1], "M-1", "K-1"));
        client.Transport.ClockOffsetSeconds.Should().Be(1000);
    }

    [Fact]
    public async Task Contour_timestamp_out_of_range_twice_is_raised()
    {
        var handler = new SnapshotHandler(_ =>
            Resp(HttpStatusCode.Unauthorized, ContourOutOfRangeBody(T - 900, topLevelServerTime: true)));
        var client = NewClient(handler, "M-1", "K-1", "https://test/");
        client.Transport.Clock = () => DateTimeOffset.FromUnixTimeSeconds(T);

        var ex = await FluentActions
            .Invoking(() => client.Wallets.InfoAsync("TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7"))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be(ErrorCodes.SignatureTimestampOutOfRange);
        ex.Which.HttpStatus.Should().Be(HttpStatusCode.Unauthorized);
        ex.Which.Message.Should().Contain("X-CC-Timestamp differs from server time by more than 300 seconds");
        handler.Requests.Should().HaveCount(2);
        client.Transport.ClockOffsetSeconds.Should().Be(-900);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "UnauthorizedError", "SIGNATURE_REPLAYED", "X-CC-Nonce has already been used")]
    [InlineData(HttpStatusCode.Unauthorized, "UnauthorizedError", "INVALID_SIGNATURE", "Invalid signature")]
    [InlineData(HttpStatusCode.BadRequest, "ValidationError", "BAD_AUTH_HEADERS", "Invalid X-CC-Nonce")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "RequestEntityTooLargeError", "PAYLOAD_TOO_LARGE", "Request body is larger than 1048576 bytes")]
    public async Task Contour_signature_refusals_map_to_error_codes_without_retry(
        HttpStatusCode status, string name, string code, string message)
    {
        var handler = new SnapshotHandler(_ => Resp(status,
            $"{{\"data\":null,\"error\":{{\"status\":{(int)status},\"name\":\"{name}\","
            + $"\"message\":\"{message}\",\"details\":{{\"code\":\"{code}\"}}}}}}"));
        var client = NewClient(handler, "M-1", "K-1", "https://test/");

        var ex = await FluentActions
            .Invoking(() => client.Wallets.InfoAsync("TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7"))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be(code);
        ex.Which.HttpStatus.Should().Be(status);
        ex.Which.Message.Should().Contain(message);
        handler.Requests.Should().HaveCount(1);
        client.Transport.ClockOffsetSeconds.Should().Be(0);
    }

    [Fact]
    public void Error_code_constants_match_gateway()
    {
        ErrorCodes.BadAuthHeaders.Should().Be("BAD_AUTH_HEADERS");
        ErrorCodes.SignatureTimestampOutOfRange.Should().Be("SIGNATURE_TIMESTAMP_OUT_OF_RANGE");
        ErrorCodes.InvalidSignature.Should().Be("INVALID_SIGNATURE");
        ErrorCodes.SignatureReplayed.Should().Be("SIGNATURE_REPLAYED");
        ErrorCodes.PayloadTooLarge.Should().Be("PAYLOAD_TOO_LARGE");
    }

    // The signature over the request as the gateway reads it: path decoded, query as sent.
    private static string GatewaySignature(
        Snapshot req, string merchant, string apiKey, string idempotencyKey = "")
    {
        var stringToSign = GatewayHmacV1.StringToSign(
            req.Header("X-CC-Timestamp"), req.Header("X-CC-Nonce"), req.Method.Method,
            Uri.UnescapeDataString(req.Uri.AbsolutePath), req.Uri.Query.TrimStart('?'),
            merchant, idempotencyKey, req.Body)!;
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign))).ToLowerInvariant();
    }

    private static string Recompute(Snapshot req, string merchant, string apiKey) =>
        RequestSigner.SignHmacV1(apiKey, new HmacV1Input
        {
            Timestamp = req.Header("X-CC-Timestamp"),
            Nonce = req.Header("X-CC-Nonce"),
            Method = req.Method.Method,
            Path = req.Uri.AbsolutePath,
            Query = req.Uri.Query.TrimStart('?'),
            Merchant = merchant,
            Body = req.Body,
        });

    private static string OutOfRangeBody(long serverTime) =>
        "{\"ok\":false,\"error\":\"SIGNATURE_TIMESTAMP_OUT_OF_RANGE\","
        + "\"msg\":\"X-CC-Timestamp differs from server time by more than 300 seconds\","
        + $"\"server_time\":{serverTime}}}";

    private static string ContourOutOfRangeBody(long serverTime, bool topLevelServerTime) =>
        "{\"data\":null,\"error\":{\"status\":401,\"name\":\"UnauthorizedError\","
        + "\"message\":\"X-CC-Timestamp differs from server time by more than 300 seconds\","
        + $"\"details\":{{\"code\":\"SIGNATURE_TIMESTAMP_OUT_OF_RANGE\",\"server_time\":{serverTime}}}}}"
        + (topLevelServerTime ? $",\"server_time\":{serverTime}}}" : "}");

    private static HttpResponseMessage Resp(HttpStatusCode code, string body) =>
        new(code) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
            { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") } } };

    private static CryptoChiefClient NewClient(
        HttpMessageHandler handler, string merchant, string apiKey, string baseUrl,
        int maxRetries = 3) =>
        new(new CryptoChiefClientOptions
        {
            MerchantId        = merchant,
            ApiKey            = apiKey,
            BaseUrl           = baseUrl,
            MaxRetries        = maxRetries,
            InitialRetryDelay = TimeSpan.FromMilliseconds(1),
            MaxRetryDelay     = TimeSpan.FromMilliseconds(5),
        }, new HttpClient(handler), null);

    private sealed record Snapshot(
        HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, byte[] Body)
    {
        public string Header(string name) => Headers[name];
    }

    private sealed class SnapshotHandler(Func<HttpRequestMessage, HttpResponseMessage> reply)
        : HttpMessageHandler
    {
        public List<Snapshot> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(
                h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            var body = request.Content is null
                ? Array.Empty<byte>()
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new Snapshot(request.Method, request.RequestUri!, headers, body));
            return reply(request);
        }
    }
}
