using System.Net;
using System.Text;
using CryptoChief.Processing.Errors;
using CryptoChief.Processing.Http;
using CryptoChief.Processing.Models;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class EnergyTests
{
    private const string Tron = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";

    [Fact]
    public async Task Quote_posts_signed_body_omitting_unset_optionals_and_maps_all_fields()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"ref\":\"eq-1\",\"receive_address\":\"" + Tron + "\",\"energy\":65000,"
            + "\"duration_sec\":3600,\"price_sun\":6500000,\"price_trx\":\"6.5\","
            + "\"price_usd\":\"1.95\",\"credits\":19500000,\"trx_usd\":\"0.30\","
            + "\"recipient_state\":\"active\",\"burn_price_sun\":13600000,"
            + "\"burn_price_trx\":\"13.6\",\"burn_price_usd\":\"4.08\","
            + "\"burn_price_credits\":40800000,\"saving_trx\":\"7.1\","
            + "\"saving_usd\":\"2.13\",\"saving_credits\":21300000,"
            + "\"expires_at\":\"2026-09-18T12:05:00Z\",\"expires_in_sec\":300}"));
        var client = NewClient(handler);

        var quote = await client.Energy.QuoteAsync(new EnergyQuoteRequest { ReceiveAddress = Tron });

        var req = handler.Captured.Should().ContainSingle().Subject;
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.AbsolutePath.Should().Be("/v1/energy/quote");
        handler.CapturedBodies.Should().ContainSingle().Which
            .ShouldBeJson("{\"receive_address\":\"" + Tron + "\"}");
        Wire.ShouldBeSignedHmacV1(req, handler.CapturedBodies[0], "M-1", "K-1");

        quote.Ref.Should().Be("eq-1");
        quote.ReceiveAddress.Should().Be(Tron);
        quote.Energy.Should().Be(65_000);
        quote.DurationSec.Should().Be(3_600);
        quote.PriceSun.Should().Be(6_500_000);
        quote.PriceTrx.Should().Be("6.5");
        quote.PriceUsd.Should().Be("1.95");
        quote.Credits.Should().Be(19_500_000);
        quote.TrxUsd.Should().Be("0.30");
        quote.RecipientState.Should().Be("active");
        quote.BurnPriceSun.Should().Be(13_600_000);
        quote.BurnPriceTrx.Should().Be("13.6");
        quote.BurnPriceUsd.Should().Be("4.08");
        quote.BurnPriceCredits.Should().Be(40_800_000);
        quote.SavingTrx.Should().Be("7.1");
        quote.SavingUsd.Should().Be("2.13");
        quote.SavingCredits.Should().Be(21_300_000);
        quote.ExpiresAt.Should().Be("2026-09-18T12:05:00Z");
        quote.ExpiresInSec.Should().Be(300);
    }

    [Fact]
    public async Task Quote_without_a_rate_reads_the_dollar_and_credits_fields_as_null()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"ref\":\"eq-2\",\"receive_address\":\"" + Tron + "\",\"energy\":65000,"
            + "\"duration_sec\":3600,\"price_sun\":6500000,\"price_trx\":\"6.5\","
            + "\"recipient_state\":\"active\",\"burn_price_sun\":13600000,"
            + "\"burn_price_trx\":\"13.6\",\"saving_trx\":\"7.1\","
            + "\"expires_at\":\"2026-09-18T12:05:00Z\",\"expires_in_sec\":300}"));
        var client = NewClient(handler);

        var quote = await client.Energy.QuoteAsync(new EnergyQuoteRequest { ReceiveAddress = Tron });

        quote.PriceSun.Should().Be(6_500_000);
        // No rate on the server: the conversions are omitted and read back as null —
        // a zero would read as "free".
        quote.PriceUsd.Should().BeNull();
        quote.Credits.Should().BeNull();
        quote.TrxUsd.Should().BeNull();
        quote.BurnPriceUsd.Should().BeNull();
        quote.BurnPriceCredits.Should().BeNull();
        quote.SavingUsd.Should().BeNull();
        quote.SavingCredits.Should().BeNull();
    }

    [Fact]
    public async Task Rent_sends_the_idempotency_key_signed_and_reads_the_delivered_order()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"id\":4471,\"idempotency_key\":\"energy-42\",\"status\":\"delivered\","
            + "\"receive_address\":\"" + Tron + "\",\"energy\":65000,\"duration_sec\":3600,"
            + "\"price_sun\":6500000,\"price_trx\":\"6.5\",\"price_usd\":\"1.95\","
            + "\"credits\":19500000,\"trx_usd\":\"0.30\",\"delivered_energy\":65000,"
            + "\"settled\":true,\"needs_attention\":false,"
            + "\"created_at\":\"2026-09-18T12:00:00Z\",\"delivered_at\":\"2026-09-18T12:00:03Z\"}"));
        var client = NewClient(handler);

        var order = await client.WithIdempotencyKey("energy-42").Energy.RentAsync(new EnergyRentRequest
        {
            ReceiveAddress = Tron, Energy = 65_000, DurationSec = 3_600, QuoteRef = "eq-1",
        });

        var req = handler.Captured.Should().ContainSingle().Subject;
        req.RequestUri!.AbsolutePath.Should().Be("/v1/energy/rent");
        req.Headers.GetValues("Idempotency-Key").Should().ContainSingle().Which.Should().Be("energy-42");
        handler.CapturedBodies.Should().ContainSingle().Which.ShouldBeJson(
            "{\"receive_address\":\"" + Tron + "\",\"energy\":65000,"
            + "\"duration_sec\":3600,\"quote_ref\":\"eq-1\"}");
        SignedWithKey(req, handler.CapturedBodies[0], "energy-42").Should().BeTrue();

        order.Id.Should().Be(4471);
        order.IdempotencyKey.Should().Be("energy-42");
        order.Status.Should().Be(EnergyOrderStatus.Delivered);
        order.ReceiveAddress.Should().Be(Tron);
        order.Energy.Should().Be(65_000);
        order.DurationSec.Should().Be(3_600);
        order.PriceSun.Should().Be(6_500_000);
        order.PriceTrx.Should().Be("6.5");
        order.PriceUsd.Should().Be("1.95");
        order.Credits.Should().Be(19_500_000);
        order.TrxUsd.Should().Be("0.30");
        order.DeliveredEnergy.Should().Be(65_000);
        order.Settled.Should().BeTrue();
        order.NeedsAttention.Should().BeFalse();
        order.Error.Should().BeNull();
        order.ErrorCode.Should().BeNull();
        order.CreatedAt.Should().Be("2026-09-18T12:00:00Z");
        order.DeliveredAt.Should().Be("2026-09-18T12:00:03Z");
    }

    [Fact]
    public async Task Rent_with_a_quote_ref_alone_omits_the_receive_address()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"id\":4472,\"idempotency_key\":\"energy-45\",\"status\":\"delivered\","
            + "\"receive_address\":\"" + Tron + "\",\"energy\":65000,\"duration_sec\":3600,"
            + "\"price_sun\":6500000,\"price_trx\":\"6.5\",\"price_usd\":\"1.95\","
            + "\"credits\":19500000,\"trx_usd\":\"0.30\",\"delivered_energy\":65000,"
            + "\"settled\":true,\"needs_attention\":false,"
            + "\"created_at\":\"2026-09-18T12:00:00Z\",\"delivered_at\":\"2026-09-18T12:00:03Z\"}"));
        var client = NewClient(handler);

        await client.WithIdempotencyKey("energy-45").Energy
            .RentAsync(new EnergyRentRequest { QuoteRef = "eq-1" });

        handler.CapturedBodies.Should().ContainSingle().Which.ShouldBeJson("{\"quote_ref\":\"eq-1\"}");
    }

    [Fact]
    public async Task Rent_refused_recovers_the_order_from_the_502_without_retrying()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.BadGateway,
            "{\"id\":4473,\"idempotency_key\":\"energy-43\",\"status\":\"refused\","
            + "\"receive_address\":\"" + Tron + "\",\"energy\":65000,\"duration_sec\":3600,"
            + "\"price_sun\":6500000,\"price_trx\":\"6.5\","
            + "\"settled\":true,\"needs_attention\":false,"
            + "\"error\":\"no supplier could take this order; nothing was bought and nothing was charged\",\"error_code\":\"SUPPLIER_REFUSED\","
            + "\"created_at\":\"2026-09-18T12:00:00Z\"}"));
        var client = NewClient(handler);

        var order = await client.WithIdempotencyKey("energy-43").Energy
            .RentAsync(new EnergyRentRequest { ReceiveAddress = Tron });

        // A settled order is a business outcome, not a transient failure: no 5xx retry.
        handler.Captured.Should().ContainSingle();

        order.Id.Should().Be(4473);
        order.Status.Should().Be(EnergyOrderStatus.Refused);
        order.Settled.Should().BeTrue();
        // Nothing was charged: the charge fields are absent, not zero.
        order.Credits.Should().BeNull();
        order.PriceUsd.Should().BeNull();
        order.TrxUsd.Should().BeNull();
        order.DeliveredEnergy.Should().BeNull();
        order.DeliveredAt.Should().BeNull();
        order.Error.Should().Be("no supplier could take this order; nothing was bought and nothing was charged");
        order.ErrorCode.Should().Be("SUPPLIER_REFUSED");
    }

    [Fact]
    public async Task Rent_refused_for_short_credits_recovers_the_order_from_the_402()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.PaymentRequired,
            "{\"id\":4474,\"idempotency_key\":\"energy-46\",\"status\":\"refused\","
            + "\"receive_address\":\"" + Tron + "\",\"energy\":65000,\"duration_sec\":3600,"
            + "\"price_sun\":6500000,\"price_trx\":\"6.5\","
            + "\"settled\":true,\"needs_attention\":false,"
            + "\"error\":\"credits balance is short\",\"error_code\":\"INSUFFICIENT_CREDITS\","
            + "\"created_at\":\"2026-09-18T12:00:00Z\"}"));
        var client = NewClient(handler);

        var order = await client.WithIdempotencyKey("energy-46").Energy
            .RentAsync(new EnergyRentRequest { ReceiveAddress = Tron });

        order.Status.Should().Be(EnergyOrderStatus.Refused);
        order.ErrorCode.Should().Be(ErrorCodes.InsufficientCredits);
        order.Credits.Should().BeNull();
    }

    [Fact]
    public async Task Rent_unresolved_recovers_the_order_from_the_409()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.Conflict,
            "{\"id\":4475,\"idempotency_key\":\"energy-47\",\"status\":\"unresolved\","
            + "\"receive_address\":\"" + Tron + "\",\"energy\":65000,\"duration_sec\":3600,"
            + "\"price_sun\":6500000,\"price_trx\":\"6.5\",\"price_usd\":\"1.95\","
            + "\"credits\":19500000,\"trx_usd\":\"0.30\","
            + "\"settled\":false,\"needs_attention\":true,"
            + "\"error\":\"the order's outcome never came back and it will not be retried - it may already have been bought; ask us to check it\",\"error_code\":\"SUPPLIER_UNKNOWN\","
            + "\"created_at\":\"2026-09-18T12:00:00Z\"}"));
        var client = NewClient(handler);

        // 409 + NeedsAttention: do NOT retry; follow the order with OrderAsync.
        var order = await client.WithIdempotencyKey("energy-47").Energy
            .RentAsync(new EnergyRentRequest { ReceiveAddress = Tron });

        order.Status.Should().Be(EnergyOrderStatus.Unresolved);
        order.NeedsAttention.Should().BeTrue();
        order.Settled.Should().BeFalse();
        // Charged, because the energy may already be delegated.
        order.Credits.Should().Be(19_500_000);
        order.ErrorCode.Should().Be("SUPPLIER_UNKNOWN");
    }

    [Fact]
    public async Task Rent_an_error_envelope_throws_and_retries_the_5xx()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.BadGateway,
            "{\"ok\":false,\"error\":\"SERVICE_ERROR\",\"msg\":\"ENERGY_UPSTREAM_DOWN\"}"));
        var client = NewClient(handler);

        var ex = await FluentActions.Invoking(() => client.WithIdempotencyKey("energy-48").Energy
                .RentAsync(new EnergyRentRequest { ReceiveAddress = Tron }))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be("ENERGY_UPSTREAM_DOWN");
        ex.Which.HttpStatus.Should().Be(HttpStatusCode.BadGateway);
        // No order in the body: the plain 5xx retry policy applies.
        handler.Captured.Should().HaveCount(4);
    }

    [Fact]
    public async Task Rent_a_402_envelope_without_an_order_throws()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.PaymentRequired,
            "{\"ok\":false,\"error\":\"INSUFFICIENT_CREDITS\",\"msg\":\"top up the credits balance\"}"));
        var client = NewClient(handler);

        var ex = await FluentActions.Invoking(() => client.WithIdempotencyKey("energy-49").Energy
                .RentAsync(new EnergyRentRequest { ReceiveAddress = Tron }))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be(ErrorCodes.InsufficientCredits);
    }

    [Fact]
    public async Task Rent_a_non_order_body_with_error_code_throws_with_that_code()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.Conflict,
            "{\"error_code\":\"QUOTE_EXPIRED\",\"error\":\"the quote is no longer held\"}"));
        var client = NewClient(handler);

        var ex = await FluentActions.Invoking(() => client.WithIdempotencyKey("energy-50").Energy
                .RentAsync(new EnergyRentRequest { QuoteRef = "eq-9" }))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be("QUOTE_EXPIRED");
        ex.Which.Message.Should().Contain("the quote is no longer held");
    }

    [Fact]
    public async Task Rent_without_an_idempotency_key_is_refused_locally()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, "{}"));
        var client = NewClient(handler);

        await FluentActions.Invoking(() => client.Energy.RentAsync(
                new EnergyRentRequest { ReceiveAddress = Tron }))
            .Should().ThrowAsync<ArgumentException>();

        handler.Captured.Should().BeEmpty();
    }

    [Fact]
    public async Task Order_posts_the_key_and_reads_the_same_order_shape()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"id\":4471,\"idempotency_key\":\"energy-42\",\"status\":\"unresolved\","
            + "\"receive_address\":\"" + Tron + "\",\"energy\":65000,\"duration_sec\":3600,"
            + "\"price_sun\":6500000,\"price_trx\":\"6.5\",\"price_usd\":\"1.95\","
            + "\"credits\":19500000,\"trx_usd\":\"0.30\","
            + "\"settled\":false,\"needs_attention\":true,"
            + "\"created_at\":\"2026-09-18T12:00:00Z\"}"));
        var client = NewClient(handler);

        var order = await client.Energy.OrderAsync("energy-42");

        var req = handler.Captured.Should().ContainSingle().Subject;
        req.RequestUri!.AbsolutePath.Should().Be("/v1/energy/order");
        handler.CapturedBodies.Should().ContainSingle().Which.ShouldBeJson("{\"key\":\"energy-42\"}");
        Wire.ShouldBeSignedHmacV1(req, handler.CapturedBodies[0], "M-1", "K-1");

        order.Id.Should().Be(4471);
        order.Status.Should().Be(EnergyOrderStatus.Unresolved);
        order.IdempotencyKey.Should().Be("energy-42");
        order.NeedsAttention.Should().BeTrue();
        order.Settled.Should().BeFalse();
        order.DeliveredEnergy.Should().BeNull();
    }

    private static bool SignedWithKey(HttpRequestMessage req, string body, string key)
    {
        var expected = RequestSigner.SignHmacV1("K-1", new HmacV1Input
        {
            Timestamp = req.Headers.GetValues("X-CC-Timestamp").Single(),
            Nonce = req.Headers.GetValues("X-CC-Nonce").Single(),
            Method = req.Method.Method,
            Path = req.RequestUri!.AbsolutePath,
            Query = req.RequestUri.Query.TrimStart('?'),
            Merchant = "M-1",
            IdempotencyKey = key,
            Body = Encoding.UTF8.GetBytes(body),
        });
        return req.Headers.GetValues("X-CC-Signature").Single() == expected;
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
