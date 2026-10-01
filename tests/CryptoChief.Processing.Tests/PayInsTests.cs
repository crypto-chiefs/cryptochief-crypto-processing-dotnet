using System.Net;
using System.Text;
using CryptoChief.Processing;
using CryptoChief.Processing.Models;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class PayInsTests
{
    [Fact]
    public async Task Info_maps_multi_payment_fields_and_payments()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"type\":\"payin\",\"uuid\":\"5b0c7a52-8a1e-4c0e-9d7b-2f3e4a5b6c7d\",\"order_id\":\"ORD-1\",\"user_id\":\"u-1\","
            + "\"status\":\"wrong_amount_waiting\",\"mode\":\"fixed\",\"amount_crypto\":\"25.000000\",\"amount_fiat\":\"25.00\","
            + "\"currency\":\"USD\",\"payment_coin\":\"USDT\",\"payment_network\":\"TRON_MAINNET\","
            + "\"to_address\":\"TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7\",\"can_cancel\":false,"
            + "\"expired_at\":\"2026-09-15T01:00:00Z\",\"created_at\":\"2026-09-15T00:00:00Z\",\"updated_at\":\"2026-09-15T00:03:12Z\","
            + "\"is_payment_multiple\":true,\"received_amount_crypto\":\"20.000000\",\"remaining_amount_crypto\":\"5.000000\","
            + "\"payments\":["
            + "{\"txid\":\"a1b2\",\"amount_crypto\":\"12.000000\",\"confirmations\":19,\"status\":\"confirmed\",\"seen_at\":\"2026-09-15T00:02:00Z\"},"
            + "{\"txid\":\"c3d4\",\"amount_crypto\":\"8.000000\",\"confirmations\":3,\"status\":\"confirming\",\"seen_at\":\"2026-09-15T00:03:00Z\"}]}"));
        var client = NewClient(handler);

        var invoice = await client.PayIns.InfoAsync("5b0c7a52-8a1e-4c0e-9d7b-2f3e4a5b6c7d");

        handler.Captured.Single().RequestUri!.AbsolutePath.Should().Be("/v1/payments/order/info");

        invoice.Status.Should().Be(PayInStatus.WrongAmountWaiting);
        invoice.IsTerminal.Should().BeFalse();
        invoice.IsPaymentMultiple.Should().BeTrue();
        invoice.ReceivedAmountCrypto.Should().Be("20.000000");
        invoice.RemainingAmountCrypto.Should().Be("5.000000");
        invoice.Payments.Should().HaveCount(2);
        var first = invoice.Payments![0];
        first.TxId.Should().Be("a1b2");
        first.AmountCrypto.Should().Be("12.000000");
        first.Confirmations.Should().Be(19);
        first.Status.Should().Be("confirmed");
        first.SeenAt.Should().Be("2026-09-15T00:02:00Z");
        invoice.Payments![1].TxId.Should().Be("c3d4");
    }

    [Fact]
    public async Task Info_without_multi_payment_fields_reads_nulls()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"type\":\"payin\",\"uuid\":\"p-1\",\"order_id\":\"o-1\",\"status\":\"paid\"}"));
        var client = NewClient(handler);

        var invoice = await client.PayIns.InfoAsync("p-1");

        invoice.IsTerminal.Should().BeTrue();
        invoice.Succeeded.Should().BeTrue();
        invoice.IsPaymentMultiple.Should().BeNull();
        invoice.ReceivedAmountCrypto.Should().BeNull();
        invoice.RemainingAmountCrypto.Should().BeNull();
        invoice.Payments.Should().BeNull();
    }

    // paid_less / paid_over close the order, so polling must stop on them; whether an
    // underpaid order counts as success is the merchant's call - Succeeded stays Paid only.
    [Theory]
    [InlineData(PayInStatus.Paid, true)]
    [InlineData(PayInStatus.PaidLess, true)]
    [InlineData(PayInStatus.PaidOver, true)]
    [InlineData(PayInStatus.Cancel, true)]
    [InlineData(PayInStatus.Expired, true)]
    [InlineData(PayInStatus.WrongAmountWaiting, false)]
    [InlineData(PayInStatus.Pending, false)]
    public void Terminal_statuses(string status, bool terminal) =>
        new PayIn { Status = status }.IsTerminal.Should().Be(terminal);

    [Fact]
    public void Paid_less_is_not_a_success() =>
        new PayIn { Status = PayInStatus.PaidLess }.Succeeded.Should().BeFalse();

    private static HttpResponseMessage Resp(HttpStatusCode code, string body) =>
        new(code) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
            { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") } } };

    private static CryptoChiefClient NewClient(HttpMessageHandler handler) =>
        new(new CryptoChiefClientOptions
        {
            MerchantId        = "M-1",
            ApiKey            = "K-1",
            BaseUrl           = "https://test/",
            MaxRetries        = 0,
            InitialRetryDelay = TimeSpan.FromMilliseconds(1),
            MaxRetryDelay     = TimeSpan.FromMilliseconds(5),
        }, new HttpClient(handler), null);

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> reply)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Captured { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured.Add(request);
            if (request.Content is not null) await request.Content.ReadAsStringAsync(cancellationToken);
            return reply(request);
        }
    }
}
