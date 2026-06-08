using System.Net;
using System.Text;
using CryptoChief.Processing;
using CryptoChief.Processing.Errors;
using CryptoChief.Processing.Models;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class TransportTests
{
    [Fact]
    public async Task Outgoing_request_carries_merchant_and_signature_headers()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"uuid\":\"u-1\",\"order_id\":\"o-1\",\"status\":\"queue\"}"));
        var client = NewClient(handler);

        await client.Payouts.ExecuteAsync(new ExecutePayoutRequest
        {
            OrderId     = "o-1",
            UserId      = "u-7",
            Network     = "ETH_SEPOLIA",
            Coin        = "ETH",
            Amount      = "0.0001",
            ToAddress   = "0xRecipient",
            UrlCallback = "https://app/cb",
        });

        handler.Captured.Should().HaveCount(1);
        var req = handler.Captured[0];
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.AbsolutePath.Should().Be("/v1/payout/execute");
        req.Headers.GetValues("Merchant").Should().ContainSingle().Which.Should().Be("M-1");
        req.Headers.GetValues("Signature").Single().Should().MatchRegex("^[a-f0-9]{32}$");
    }

    [Fact]
    public async Task Maps_envelope_error_to_typed_exception()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.BadRequest,
            "{\"ok\":false,\"error\":\"SERVICE_ERROR\",\"msg\":\"INSUFFICIENT_FUNDS\"}"));
        var client = NewClient(handler);

        var ex = await FluentActions.Invoking(() => client.Payouts.InfoAsync("u-1"))
            .Should().ThrowAsync<CryptoChiefApiException>();
        ex.Which.Code.Should().Be(ErrorCodes.InsufficientFunds);
        ex.Which.HttpStatus.Should().Be(HttpStatusCode.BadRequest);
        ex.Which.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public async Task Retries_5xx_then_succeeds()
    {
        var attempts = 0;
        var handler = new CapturingHandler(_ =>
        {
            attempts++;
            return attempts < 3
                ? Resp(HttpStatusCode.InternalServerError, "{\"ok\":false,\"error\":\"SERVICE_ERROR\"}")
                : Resp(HttpStatusCode.OK, "{\"uuid\":\"u-1\",\"order_id\":\"o-1\",\"status\":\"queue\"}");
        });
        var client = NewClient(handler);

        var info = await client.Payouts.InfoAsync("u-1");
        info.Uuid.Should().Be("u-1");
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task Does_not_retry_4xx()
    {
        var attempts = 0;
        var handler = new CapturingHandler(_ =>
        {
            attempts++;
            return Resp(HttpStatusCode.BadRequest, "{\"ok\":false,\"error\":\"INVALID_PARAMS\"}");
        });
        var client = NewClient(handler);

        await FluentActions.Invoking(() => client.Payouts.InfoAsync("u-1"))
            .Should().ThrowAsync<CryptoChiefApiException>();
        attempts.Should().Be(1);
    }

    private static HttpResponseMessage Resp(HttpStatusCode code, string body) =>
        new(code) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
            { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") } } };

    private static CryptoChiefClient NewClient(HttpMessageHandler handler) =>
        new(new CryptoChiefClientOptions
        {
            MerchantId        = "M-1",
            ApiKey            = "K-1",
            BaseUrl           = "https://test/",
            MaxRetries        = 3,
            InitialRetryDelay = TimeSpan.FromMilliseconds(1),
            MaxRetryDelay     = TimeSpan.FromMilliseconds(5),
        }, new HttpClient(handler), null);

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> reply)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Captured { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured.Add(request);
            return Task.FromResult(reply(request));
        }
    }
}
