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
    public async Task Gateway_envelope_puts_the_machine_code_in_Code()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.BadRequest,
            "{\"ok\":false,\"error\":\"LABEL_TOO_LONG\","
            + "\"msg\":\"label is longer than 255 characters\"}"));
        var client = NewClient(handler);

        var ex = await FluentActions
            .Invoking(() => client.Wallets.SetLabelAsync("0xabc", new string('x', 256)))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be(ErrorCodes.LabelTooLong);
        ex.Which.HttpStatus.Should().Be(HttpStatusCode.BadRequest);
        // The sentence stays available as the human-readable half.
        ex.Which.Message.Should().Contain("label is longer than 255 characters");
        // Nothing is lost: the raw body still carries both fields.
        ex.Which.RawBody.Should().Contain("LABEL_TOO_LONG")
            .And.Contain("label is longer than 255 characters");
    }

    [Fact]
    public async Task Gateway_code_matches_the_switch_a_caller_writes()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.BadRequest,
            "{\"ok\":false,\"error\":\"LABEL_TOO_LONG\","
            + "\"msg\":\"label is longer than 255 characters\"}"));
        var client = NewClient(handler);

        var branch = "none";
        try
        {
            await client.Wallets.SetLabelAsync("0xabc", new string('x', 256));
        }
        catch (CryptoChiefApiException ex)
        {
            branch = ex.Code switch
            {
                ErrorCodes.LabelTooLong        => "label-too-long",
                ErrorCodes.InsufficientFunds   => "insufficient-funds",
                _                              => "unmatched",
            };
        }

        branch.Should().Be("label-too-long");
    }

    [Fact]
    public async Task Upstream_envelope_still_takes_the_code_from_msg()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.BadRequest,
            "{\"ok\":false,\"error\":\"SERVICE_ERROR\",\"msg\":\"wallet_not_found\"}"));
        var client = NewClient(handler);

        var ex = await FluentActions
            .Invoking(() => client.Wallets.SetLabelAsync("0xabc", "treasury"))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be("wallet_not_found");
        ex.Which.Message.Should().Contain("wallet_not_found");
    }

    [Fact]
    public async Task Envelope_without_msg_falls_back_to_error()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.BadRequest,
            "{\"ok\":false,\"error\":\"SERVICE_ERROR\"}"));
        var client = NewClient(handler);

        var ex = await FluentActions.Invoking(() => client.Payouts.InfoAsync("u-1"))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be(ErrorCodes.ServiceError);
    }

    [Fact]
    public async Task Bodyless_refusal_falls_back_to_the_http_status()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.BadGateway, ""));
        var client = NewClient(handler);

        var ex = await FluentActions.Invoking(() => client.Payouts.InfoAsync("u-1"))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be("HTTP_502");
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
