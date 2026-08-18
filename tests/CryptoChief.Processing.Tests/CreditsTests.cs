using System.Net;
using System.Text;
using CryptoChief.Processing;
using CryptoChief.Processing.Http;
using CryptoChief.Processing.Models;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class CreditsTests
{
    [Fact]
    public async Task Balance_posts_signed_empty_object_to_credits_balance()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"credits_balance\":25000000,\"usd_balance\":\"2.50\",\"is_postpaid\":false,"
            + "\"debt_limit_credits\":0,\"can_execute_gas_operations\":true,"
            + "\"gas_ops_min_credits\":3000000,\"timestamp\":\"2026-08-18T12:00:00Z\"}"));
        var client = NewClient(handler);

        await client.Credits.BalanceAsync();

        handler.Captured.Should().HaveCount(1);
        var req = handler.Captured[0];
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.AbsolutePath.Should().Be("/v1/credits/balance");
        req.Headers.GetValues("Merchant").Should().ContainSingle().Which.Should().Be("M-1");

        handler.CapturedBodies.Should().ContainSingle().Which.Should().Be("{}");
        req.Headers.GetValues("Signature").Single().Should()
            .Be(RequestSigner.Sign(Encoding.UTF8.GetBytes("{}"), "K-1"));
    }

    [Fact]
    public async Task Balance_maps_all_fields_including_negative_usd_balance()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"credits_balance\":-15200000,\"usd_balance\":\"-1.52\",\"is_postpaid\":true,"
            + "\"debt_limit_credits\":500000000,\"can_execute_gas_operations\":false,"
            + "\"gas_ops_min_credits\":3000000,\"timestamp\":\"2026-08-18T12:00:00Z\"}"));
        var client = NewClient(handler);

        var balance = await client.Credits.BalanceAsync();

        balance.Balance.Should().Be(-15_200_000);
        balance.UsdBalance.Should().Be("-1.52");
        balance.IsPostpaid.Should().BeTrue();
        balance.DebtLimitCredits.Should().Be(500_000_000);
        balance.CanExecuteGasOperations.Should().BeFalse();
        balance.GasOpsMinCredits.Should().Be(3_000_000);
        balance.Timestamp.Should().Be("2026-08-18T12:00:00Z");
    }

    [Fact]
    public async Task Topup_posts_signed_body_omitting_unset_optional_urls()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"invoice_id\":123456,\"payment_link\":\"https://pay.test/topup/abc\","
            + "\"amount\":\"25.00\",\"currency\":\"USDT\",\"status\":\"pending\"}"));
        var client = NewClient(handler);

        var topup = await client.Credits.TopupAsync(new CreditsTopupRequest
        {
            Amount   = "25.00",
            Currency = "USDT",
        });

        handler.Captured.Should().HaveCount(1);
        var req = handler.Captured[0];
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.AbsolutePath.Should().Be("/v1/credits/topup");
        req.Headers.GetValues("Merchant").Should().ContainSingle().Which.Should().Be("M-1");

        // Unset optional urls must be omitted from the wire, not sent as "".
        const string wire = "{\"amount\":\"25.00\",\"currency\":\"USDT\"}";
        handler.CapturedBodies.Should().ContainSingle().Which.Should().Be(wire);
        req.Headers.GetValues("Signature").Single().Should()
            .Be(RequestSigner.Sign(Encoding.UTF8.GetBytes(wire), "K-1"));

        // Optional response fields absent → null.
        topup.InvoiceId.Should().Be(123_456);
        topup.PaymentLink.Should().Be("https://pay.test/topup/abc");
        topup.Amount.Should().Be("25.00");
        topup.Currency.Should().Be("USDT");
        topup.Status.Should().Be("pending");
        topup.OrderUuid.Should().BeNull();
        topup.ExpiredAt.Should().BeNull();
    }

    [Fact]
    public async Task Topup_sends_optional_urls_and_maps_all_fields()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"invoice_id\":987654321,\"payment_link\":\"https://pay.test/topup/def\","
            + "\"amount\":\"100.00\",\"currency\":\"USDC\",\"status\":\"pending\","
            + "\"order_uuid\":\"0d5e3f1a-1111-2222-3333-444455556666\",\"expired_at\":1755523200}"));
        var client = NewClient(handler);

        var topup = await client.Credits.TopupAsync(new CreditsTopupRequest
        {
            Amount     = "100.00",
            Currency   = "USDC",
            UrlSuccess = "https://shop.test/billing/ok",
            UrlError   = "https://shop.test/billing/fail",
        });

        var req = handler.Captured.Should().ContainSingle().Subject;
        req.RequestUri!.AbsolutePath.Should().Be("/v1/credits/topup");

        // Canonical body: snake_case keys sorted lexicographically.
        const string wire = "{\"amount\":\"100.00\",\"currency\":\"USDC\","
            + "\"url_error\":\"https://shop.test/billing/fail\","
            + "\"url_success\":\"https://shop.test/billing/ok\"}";
        handler.CapturedBodies.Should().ContainSingle().Which.Should().Be(wire);
        req.Headers.GetValues("Signature").Single().Should()
            .Be(RequestSigner.Sign(Encoding.UTF8.GetBytes(wire), "K-1"));

        topup.InvoiceId.Should().Be(987_654_321);
        topup.PaymentLink.Should().Be("https://pay.test/topup/def");
        topup.Amount.Should().Be("100.00");
        topup.Currency.Should().Be("USDC");
        topup.Status.Should().Be("pending");
        topup.OrderUuid.Should().Be("0d5e3f1a-1111-2222-3333-444455556666");
        topup.ExpiredAt.Should().Be(1_755_523_200);
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

        /// <summary>Request bodies read at send time — the transport disposes the request after sending.</summary>
        public List<string> CapturedBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured.Add(request);
            CapturedBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return reply(request);
        }
    }
}
