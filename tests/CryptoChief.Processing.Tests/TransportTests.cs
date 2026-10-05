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
        req.Headers.GetValues("X-CC-Timestamp").Single().Should().MatchRegex("^[0-9]+$");
        req.Headers.GetValues("X-CC-Nonce").Single().Should().MatchRegex("^[0-9a-f]{32}$");
        req.Headers.GetValues("X-CC-Signature").Single().Should().MatchRegex("^v1=[0-9a-f]{64}$");
        req.Headers.Contains("Signature").Should().BeFalse();
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
    public async Task Contour_envelope_takes_the_code_from_details()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.PaymentRequired,
            "{\"data\":null,\"error\":{\"status\":402,\"name\":\"PaymentRequiredError\","
            + "\"message\":\"Top up the balance\",\"details\":{\"code\":\"INSUFFICIENT_FUNDS\"}}}"));
        var client = NewClient(handler);

        var ex = await FluentActions.Invoking(() => client.Payouts.InfoAsync("u-1"))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be(ErrorCodes.InsufficientFunds);
        ex.Which.HttpStatus.Should().Be(HttpStatusCode.PaymentRequired);
        ex.Which.Message.Should().Contain("Top up the balance");
        ex.Which.RawBody.Should().Contain("PaymentRequiredError");
    }

    [Fact]
    public async Task Contour_envelope_without_details_code_takes_the_error_name()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.BadRequest,
            "{\"data\":null,\"error\":{\"status\":400,\"name\":\"ValidationError\","
            + "\"message\":\"order_id is required\",\"details\":{}}}"));
        var client = NewClient(handler);

        var ex = await FluentActions.Invoking(() => client.Payouts.InfoAsync("u-1"))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be("ValidationError");
        ex.Which.Message.Should().Contain("order_id is required");
        ex.Which.IsRetryable.Should().BeFalse();
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

    private const string PayoutBody = "{\"uuid\":\"u-1\",\"order_id\":\"o-1\",\"status\":\"queue\"}";

    [Fact]
    public async Task A_500_is_attempted_once()
    {
        var attempts = 0;
        var handler = new CapturingHandler(_ =>
        {
            attempts++;
            return Resp(HttpStatusCode.InternalServerError,
                "{\"ok\":false,\"error\":\"INTERNAL_ERROR\",\"msg\":\"internal error\"}");
        });
        var client = NewClient(handler);

        var ex = await FluentActions.Invoking(() => client.Payouts.InfoAsync("u-1"))
            .Should().ThrowAsync<CryptoChiefApiException>();
        attempts.Should().Be(1);
        ex.Which.Code.Should().Be("INTERNAL_ERROR");
        ex.Which.HttpStatus.Should().Be(HttpStatusCode.InternalServerError);
        ex.Which.IsRetryable.Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Retries_502_503_504_until_the_budget_is_spent(HttpStatusCode status)
    {
        var attempts = 0;
        var handler = new CapturingHandler(_ =>
        {
            attempts++;
            return Resp(status, "");
        });
        var client = NewClient(handler);

        var ex = await FluentActions.Invoking(() => client.Payouts.InfoAsync("u-1"))
            .Should().ThrowAsync<CryptoChiefApiException>();
        attempts.Should().Be(4); // MaxRetries 3 + the first attempt
        ex.Which.HttpStatus.Should().Be(status);
        ex.Which.Code.Should().Be($"HTTP_{(int)status}");
        ex.Which.IsRetryable.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Retries_502_503_504_then_succeeds(HttpStatusCode status)
    {
        var attempts = 0;
        var handler = new CapturingHandler(_ =>
        {
            attempts++;
            return attempts < 3
                ? Resp(status, "{\"ok\":false,\"error\":\"SERVICE_ERROR\"}")
                : Resp(HttpStatusCode.OK, PayoutBody);
        });
        var client = NewClient(handler);

        var info = await client.Payouts.InfoAsync("u-1");
        info.Uuid.Should().Be("u-1");
        attempts.Should().Be(3);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(505)]
    [InlineData(520)]
    [InlineData(599)]
    public async Task Any_other_status_is_attempted_once(int status)
    {
        var attempts = 0;
        var handler = new CapturingHandler(_ =>
        {
            attempts++;
            return Resp((HttpStatusCode)status, "");
        });
        var client = NewClient(handler);

        var ex = await FluentActions.Invoking(() => client.Payouts.InfoAsync("u-1"))
            .Should().ThrowAsync<CryptoChiefApiException>();
        attempts.Should().Be(1);
        ex.Which.HttpStatus.Should().Be((HttpStatusCode)status);
        ex.Which.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public async Task Retries_a_network_error_then_succeeds()
    {
        var attempts = 0;
        var handler = new CapturingHandler(_ =>
        {
            attempts++;
            return attempts < 3
                ? throw new HttpRequestException("connection refused")
                : Resp(HttpStatusCode.OK, PayoutBody);
        });
        var client = NewClient(handler);

        var info = await client.Payouts.InfoAsync("u-1");
        info.Uuid.Should().Be("u-1");
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task A_network_error_is_retried_until_the_budget_is_spent()
    {
        var attempts = 0;
        var handler = new CapturingHandler(_ =>
        {
            attempts++;
            throw new HttpRequestException("connection reset");
        });
        var client = NewClient(handler);

        var ex = await FluentActions.Invoking(() => client.Payouts.InfoAsync("u-1"))
            .Should().ThrowAsync<CryptoChiefApiException>();
        attempts.Should().Be(4);
        ex.Which.Code.Should().Be(ErrorCodes.NetworkError);
        ex.Which.HttpStatus.Should().Be((HttpStatusCode)0);
        ex.Which.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public async Task Retries_a_timeout_then_succeeds()
    {
        var attempts = 0;
        var handler = new CapturingHandler(_ =>
        {
            attempts++;
            return attempts < 2
                ? throw new TaskCanceledException("timed out")
                : Resp(HttpStatusCode.OK, PayoutBody);
        });
        var client = NewClient(handler);

        var info = await client.Payouts.InfoAsync("u-1");
        info.Uuid.Should().Be("u-1");
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task Retries_a_failed_body_read_then_succeeds()
    {
        var attempts = 0;
        var handler = new CapturingHandler(_ =>
        {
            attempts++;
            return attempts < 2
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new BrokenContent() }
                : Resp(HttpStatusCode.OK, PayoutBody);
        });
        var client = NewClient(handler);

        var info = await client.Payouts.InfoAsync("u-1");
        info.Uuid.Should().Be("u-1");
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task A_failed_body_read_is_retried_until_the_budget_is_spent()
    {
        var attempts = 0;
        var handler = new CapturingHandler(_ =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new BrokenContent() };
        });
        var client = NewClient(handler);

        var ex = await FluentActions.Invoking(() => client.Payouts.InfoAsync("u-1"))
            .Should().ThrowAsync<CryptoChiefApiException>();
        attempts.Should().Be(4);
        ex.Which.Code.Should().Be(ErrorCodes.NetworkError);
        ex.Which.IsRetryable.Should().BeTrue();
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(429, false)]
    [InlineData(500, false)]
    [InlineData(501, false)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(504, true)]
    [InlineData(505, false)]
    [InlineData(520, false)]
    public void IsRetryable_is_true_for_502_503_504_only(int status, bool retryable)
    {
        new CryptoChiefApiException($"HTTP_{status}", (HttpStatusCode)status, "refused")
            .IsRetryable.Should().Be(retryable);
        new CryptoChiefApiException(ErrorCodes.ServiceError, (HttpStatusCode)status, "refused")
            .IsRetryable.Should().Be(retryable);
    }

    [Fact]
    public void IsRetryable_is_true_for_network_errors()
    {
        new CryptoChiefApiException(ErrorCodes.NetworkError, 0, "connection refused")
            .IsRetryable.Should().BeTrue();
        // A body that broke off mid-read keeps the status it arrived with.
        new CryptoChiefApiException(ErrorCodes.NetworkError, HttpStatusCode.OK, "connection reset")
            .IsRetryable.Should().BeTrue();
    }

    [Fact]
    public void IsRetryable_is_false_for_a_500_naming_network_error()
    {
        // A white-label install answers 500 with code NETWORK_ERROR when its own call fails.
        const string body = "{\"data\":null,\"error\":{\"status\":500,\"name\":\"ApplicationError\","
            + "\"message\":\"provider error: NETWORK_ERROR\",\"details\":{\"code\":\"NETWORK_ERROR\"}}}";
        var ex = new CryptoChiefApiException(ErrorCodes.NetworkError, HttpStatusCode.InternalServerError,
            "provider error: NETWORK_ERROR", body);
        ex.IsRetryable.Should().BeFalse();
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

    /// <summary>A response body whose connection drops while it is read.</summary>
    private sealed class BrokenContent : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await Task.Yield();
            throw new IOException("connection reset while reading the body");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
