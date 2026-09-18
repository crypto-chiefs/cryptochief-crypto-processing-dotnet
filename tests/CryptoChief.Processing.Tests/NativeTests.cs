using System.Net;
using System.Text;
using CryptoChief.Processing.Errors;
using CryptoChief.Processing.Http;
using CryptoChief.Processing.Models;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class NativeTests
{
    private const string Tron = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";

    [Fact]
    public async Task Quote_posts_the_signed_body_and_maps_all_fields()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"ref\":\"nq-1\",\"network\":\"TRON\",\"receive_address\":\"" + Tron + "\","
            + "\"amount\":\"0.05\",\"coin_price_usd\":\"0.015\",\"transfer_fee\":\"0.0003\","
            + "\"transfer_fee_usd\":\"0.00009\",\"subtotal_usd\":\"0.01509\","
            + "\"total_usd\":\"0.0196\",\"credits\":196000,\"coin_usd\":\"0.30\","
            + "\"expires_at\":\"2026-09-18T12:01:30Z\",\"expires_in_sec\":90}"));
        var client = NewClient(handler);

        var quote = await client.Native.QuoteAsync(new NativeQuoteRequest
        {
            Network = "TRON", ReceiveAddress = Tron, Amount = "0.05",
        });

        var req = handler.Captured.Should().ContainSingle().Subject;
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.AbsolutePath.Should().Be("/v1/native/quote");
        handler.CapturedBodies.Should().ContainSingle().Which.ShouldBeJson(
            "{\"network\":\"TRON\",\"receive_address\":\"" + Tron + "\",\"amount\":\"0.05\"}");
        Wire.ShouldBeSignedHmacV1(req, handler.CapturedBodies[0], "M-1", "K-1");

        quote.Ref.Should().Be("nq-1");
        quote.Network.Should().Be("TRON");
        quote.ReceiveAddress.Should().Be(Tron);
        quote.Amount.Should().Be("0.05");
        quote.CoinPriceUsd.Should().Be("0.015");
        quote.TransferFee.Should().Be("0.0003");
        quote.TransferFeeUsd.Should().Be("0.00009");
        quote.SubtotalUsd.Should().Be("0.01509");
        quote.TotalUsd.Should().Be("0.0196");
        quote.Credits.Should().Be(196_000);
        quote.CoinUsd.Should().Be("0.30");
        quote.ExpiresAt.Should().Be("2026-09-18T12:01:30Z");
        quote.ExpiresInSec.Should().Be(90);
    }

    [Fact]
    public async Task Buy_sends_the_idempotency_key_signed_and_reads_the_delivered_order()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"id\":101,\"idempotency_key\":\"native-42\",\"status\":\"delivered\","
            + "\"network\":\"TRON\",\"receive_address\":\"" + Tron + "\",\"amount\":\"0.05\","
            + "\"tx_hash\":\"abc123\",\"transfer_fee\":\"0.0003\",\"transfer_fee_usd\":\"0.00009\","
            + "\"coin_price_usd\":\"0.015\",\"total_usd\":\"0.0196\",\"credits\":196000,"
            + "\"coin_usd\":\"0.30\",\"settled\":true,\"needs_attention\":false,"
            + "\"created_at\":\"2026-09-18T12:00:00Z\",\"delivered_at\":\"2026-09-18T12:00:03Z\"}"));
        var client = NewClient(handler);

        var order = await client.WithIdempotencyKey("native-42").Native.BuyAsync(new NativeBuyRequest
        {
            QuoteRef = "nq-1",
        });

        var req = handler.Captured.Should().ContainSingle().Subject;
        req.RequestUri!.AbsolutePath.Should().Be("/v1/native/buy");
        req.Headers.GetValues("Idempotency-Key").Should().ContainSingle().Which.Should().Be("native-42");
        handler.CapturedBodies.Should().ContainSingle().Which.ShouldBeJson(
            "{\"quote_ref\":\"nq-1\"}");
        SignedWithKey(req, handler.CapturedBodies[0], "native-42").Should().BeTrue();

        order.Id.Should().Be(101);
        order.IdempotencyKey.Should().Be("native-42");
        order.Status.Should().Be(NativeOrderStatus.Delivered);
        order.Network.Should().Be("TRON");
        order.ReceiveAddress.Should().Be(Tron);
        order.Amount.Should().Be("0.05");
        order.TxHash.Should().Be("abc123");
        order.TransferFee.Should().Be("0.0003");
        order.TransferFeeUsd.Should().Be("0.00009");
        order.CoinPriceUsd.Should().Be("0.015");
        order.TotalUsd.Should().Be("0.0196");
        order.Credits.Should().Be(196_000);
        order.CoinUsd.Should().Be("0.30");
        order.Settled.Should().BeTrue();
        order.NeedsAttention.Should().BeFalse();
        order.Error.Should().BeNull();
        order.ErrorCode.Should().BeNull();
        order.CreatedAt.Should().Be("2026-09-18T12:00:00Z");
        order.DeliveredAt.Should().Be("2026-09-18T12:00:03Z");
    }

    [Fact]
    public async Task Buy_posts_the_coin_parameters_when_no_quote_ref_is_given()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"id\":102,\"idempotency_key\":\"native-43\",\"status\":\"delivered\","
            + "\"network\":\"TRON\",\"receive_address\":\"" + Tron + "\",\"amount\":\"0.05\","
            + "\"tx_hash\":\"def456\",\"transfer_fee\":\"0.0003\",\"transfer_fee_usd\":\"0.00009\","
            + "\"coin_price_usd\":\"0.015\",\"total_usd\":\"0.0196\",\"credits\":196000,"
            + "\"coin_usd\":\"0.30\",\"settled\":true,\"needs_attention\":false,"
            + "\"created_at\":\"2026-09-18T12:00:00Z\",\"delivered_at\":\"2026-09-18T12:00:03Z\"}"));
        var client = NewClient(handler);

        var order = await client.WithIdempotencyKey("native-43").Native.BuyAsync(new NativeBuyRequest
        {
            Network = "TRON", ReceiveAddress = Tron, Amount = "0.05",
        });

        handler.CapturedBodies.Should().ContainSingle().Which.ShouldBeJson(
            "{\"network\":\"TRON\",\"receive_address\":\"" + Tron + "\",\"amount\":\"0.05\"}");
        order.Status.Should().Be(NativeOrderStatus.Delivered);
    }

    [Fact]
    public async Task Buy_refused_recovers_the_order_from_the_502_without_retrying()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.BadGateway,
            "{\"id\":103,\"idempotency_key\":\"native-44\",\"status\":\"refused\","
            + "\"network\":\"TRON\",\"receive_address\":\"" + Tron + "\",\"amount\":\"0.05\","
            + "\"transfer_fee\":\"\",\"transfer_fee_usd\":\"0.00\",\"coin_price_usd\":\"0.00\","
            + "\"coin_usd\":\"\",\"settled\":true,\"needs_attention\":false,"
            + "\"error\":\"we cannot fund that sale from our own wallet right now; nothing was bought and nothing was charged\",\"error_code\":\"INSUFFICIENT_LIQUIDITY\","
            + "\"created_at\":\"2026-09-18T12:00:00Z\"}"));
        var client = NewClient(handler);

        var order = await client.WithIdempotencyKey("native-44").Native
            .BuyAsync(new NativeBuyRequest { Network = "TRON", ReceiveAddress = Tron, Amount = "0.05" });

        // A settled order is a business outcome, not a transient failure: no 5xx retry.
        handler.Captured.Should().ContainSingle();

        order.Id.Should().Be(103);
        order.Status.Should().Be(NativeOrderStatus.Refused);
        order.Settled.Should().BeTrue();
        // Nothing was sent or charged: only tx_hash / total_usd / credits are absent;
        // the rest of the price fields arrive as "" / "0.00", not omitted.
        order.TxHash.Should().BeNull();
        order.TotalUsd.Should().BeNull();
        order.Credits.Should().BeNull();
        order.TransferFee.Should().Be("");
        order.TransferFeeUsd.Should().Be("0.00");
        order.CoinPriceUsd.Should().Be("0.00");
        order.CoinUsd.Should().Be("");
        order.DeliveredAt.Should().BeNull();
        order.Error.Should().Be("we cannot fund that sale from our own wallet right now; nothing was bought and nothing was charged");
        order.ErrorCode.Should().Be("INSUFFICIENT_LIQUIDITY");
    }

    [Fact]
    public async Task Buy_refused_for_short_credits_recovers_the_order_from_the_402()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.PaymentRequired,
            "{\"id\":104,\"idempotency_key\":\"native-46\",\"status\":\"refused\","
            + "\"network\":\"TRON\",\"receive_address\":\"" + Tron + "\",\"amount\":\"0.05\","
            + "\"transfer_fee\":\"\",\"transfer_fee_usd\":\"0.00\",\"coin_price_usd\":\"0.00\","
            + "\"coin_usd\":\"\",\"settled\":true,\"needs_attention\":false,"
            + "\"error\":\"credits balance is short\",\"error_code\":\"INSUFFICIENT_CREDITS\","
            + "\"created_at\":\"2026-09-18T12:00:00Z\"}"));
        var client = NewClient(handler);

        var order = await client.WithIdempotencyKey("native-46").Native
            .BuyAsync(new NativeBuyRequest { QuoteRef = "nq-1" });

        order.Status.Should().Be(NativeOrderStatus.Refused);
        order.ErrorCode.Should().Be(ErrorCodes.InsufficientCredits);
        order.Credits.Should().BeNull();
    }

    [Fact]
    public async Task Buy_unresolved_recovers_the_order_from_the_409()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.Conflict,
            "{\"id\":105,\"idempotency_key\":\"native-47\",\"status\":\"unresolved\","
            + "\"network\":\"TRON\",\"receive_address\":\"" + Tron + "\",\"amount\":\"0.05\","
            + "\"transfer_fee\":\"0.0003\",\"transfer_fee_usd\":\"0.00009\","
            + "\"coin_price_usd\":\"0.015\",\"total_usd\":\"0.0196\",\"credits\":196000,"
            + "\"coin_usd\":\"0.30\",\"settled\":false,\"needs_attention\":true,"
            + "\"error\":\"the transfer's outcome never came back, so the order will not be retried - it may already have been sent; ask us to check it\",\"error_code\":\"SEND_UNKNOWN\","
            + "\"created_at\":\"2026-09-18T12:00:00Z\"}"));
        var client = NewClient(handler);

        // 409 + NeedsAttention: do NOT retry; follow the order with OrderAsync.
        var order = await client.WithIdempotencyKey("native-47").Native
            .BuyAsync(new NativeBuyRequest { QuoteRef = "nq-1" });

        order.Status.Should().Be(NativeOrderStatus.Unresolved);
        order.NeedsAttention.Should().BeTrue();
        order.Settled.Should().BeFalse();
        // Charged, because the coins may already be sent.
        order.Credits.Should().Be(196_000);
        order.ErrorCode.Should().Be("SEND_UNKNOWN");
    }

    [Fact]
    public async Task Buy_an_error_envelope_throws_and_retries_the_5xx()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.BadGateway,
            "{\"ok\":false,\"error\":\"SERVICE_ERROR\",\"msg\":\"NATIVE_UPSTREAM_DOWN\"}"));
        var client = NewClient(handler);

        var ex = await FluentActions.Invoking(() => client.WithIdempotencyKey("native-48").Native
                .BuyAsync(new NativeBuyRequest { QuoteRef = "nq-1" }))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be("NATIVE_UPSTREAM_DOWN");
        ex.Which.HttpStatus.Should().Be(HttpStatusCode.BadGateway);
        // No order in the body: the plain 5xx retry policy applies.
        handler.Captured.Should().HaveCount(4);
    }

    [Fact]
    public async Task Buy_a_402_envelope_without_an_order_throws()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.PaymentRequired,
            "{\"ok\":false,\"error\":\"INSUFFICIENT_CREDITS\",\"msg\":\"top up the credits balance\"}"));
        var client = NewClient(handler);

        var ex = await FluentActions.Invoking(() => client.WithIdempotencyKey("native-49").Native
                .BuyAsync(new NativeBuyRequest { QuoteRef = "nq-1" }))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be(ErrorCodes.InsufficientCredits);
    }

    [Fact]
    public async Task Buy_a_quote_conflict_envelope_throws_with_its_code()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.Conflict,
            "{\"ok\":false,\"error\":\"QUOTE_EXPIRED\",\"msg\":\"the quote is no longer held\"}"));
        var client = NewClient(handler);

        var ex = await FluentActions.Invoking(() => client.WithIdempotencyKey("native-50").Native
                .BuyAsync(new NativeBuyRequest { QuoteRef = "nq-9" }))
            .Should().ThrowAsync<CryptoChiefApiException>();

        ex.Which.Code.Should().Be("QUOTE_EXPIRED");
        ex.Which.HttpStatus.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Buy_without_an_idempotency_key_is_refused_locally()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, "{}"));
        var client = NewClient(handler);

        await FluentActions.Invoking(() => client.Native.BuyAsync(
                new NativeBuyRequest { QuoteRef = "nq-1" }))
            .Should().ThrowAsync<ArgumentException>();

        handler.Captured.Should().BeEmpty();
    }

    [Fact]
    public async Task Order_posts_the_key_and_reads_the_same_order_shape()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"id\":101,\"idempotency_key\":\"native-42\",\"status\":\"unresolved\","
            + "\"network\":\"TRON\",\"receive_address\":\"" + Tron + "\",\"amount\":\"0.05\","
            + "\"coin_price_usd\":\"0.015\",\"total_usd\":\"0.0196\",\"credits\":196000,"
            + "\"coin_usd\":\"0.30\",\"settled\":false,\"needs_attention\":true,"
            + "\"created_at\":\"2026-09-18T12:00:00Z\"}"));
        var client = NewClient(handler);

        var order = await client.Native.OrderAsync("native-42");

        var req = handler.Captured.Should().ContainSingle().Subject;
        req.RequestUri!.AbsolutePath.Should().Be("/v1/native/order");
        handler.CapturedBodies.Should().ContainSingle().Which.ShouldBeJson("{\"key\":\"native-42\"}");
        Wire.ShouldBeSignedHmacV1(req, handler.CapturedBodies[0], "M-1", "K-1");

        order.Id.Should().Be(101);
        order.Status.Should().Be(NativeOrderStatus.Unresolved);
        order.IdempotencyKey.Should().Be("native-42");
        order.NeedsAttention.Should().BeTrue();
        order.Settled.Should().BeFalse();
        order.TxHash.Should().BeNull();
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
        return req.Headers.GetValues("X-CC-Signature").Single() == "v1=" + expected;
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
