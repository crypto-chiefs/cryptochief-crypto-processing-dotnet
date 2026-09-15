using System.Net;
using System.Text;
using CryptoChief.Processing.Http;
using CryptoChief.Processing.Internal;
using CryptoChief.Processing.Models;
using CryptoChief.Processing.Polling;
using CryptoChief.Processing.Webhooks;
using CryptoChief.Processing.Webhooks.Events;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

/// <summary>
/// Confirmation count and threshold on a signed transaction. Unlike a payout, both are always
/// sent: the count is 0 until the transaction is in a block, then grows while it is
/// broadcasted until it reaches the threshold (at least 1) and the transaction is confirmed.
/// </summary>
public class TransactionsTests
{
    private const string ApiKey = "K-1";

    private static string TxBody(string status, int confirmations, int required, string extra = "") =>
        "{\"uuid\":\"tx-1\",\"status\":\"" + status + "\",\"network\":\"ETH_MAINNET\",\"chain_family\":\"evm\","
        + "\"type\":\"native\",\"from_address\":\"0xa\",\"to_address\":\"0xb\",\"value\":\"1000\",\"tx_hash\":\"0xh\","
        + "\"confirmations\":" + confirmations + ",\"required_confirmations\":" + required + ","
        + extra
        + "\"expires_at\":\"2026-09-14T10:10:00Z\",\"created_at\":\"2026-09-14T10:00:00Z\"}";

    [Fact]
    public async Task Info_reads_the_count_it_was_confirmed_at_and_the_threshold_applied()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            TxBody(TxStatus.Confirmed, 12, 12, "\"completed_at\":\"2026-09-14T10:03:00Z\",")));

        var tx = await NewClient(handler).Transactions.InfoAsync("tx-1");

        handler.Captured.Single().RequestUri!.AbsolutePath.Should().Be("/v1/transaction/info");
        tx.Succeeded.Should().BeTrue();
        tx.Confirmations.Should().Be(12);
        tx.RequiredConfirmations.Should().Be(12);
    }

    [Fact]
    public async Task Execute_reports_zero_before_the_transaction_is_in_a_block_and_the_threshold()
    {
        var executeHandler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, TxBody(TxStatus.Broadcasted, 0, 12)));
        var broadcast = await NewClient(executeHandler).Transactions.ExecuteAsync(
            new ExecuteTransactionRequest { Uuid = "tx-1" });

        broadcast.IsTerminal.Should().BeFalse();
        broadcast.Confirmations.Should().Be(0);
        broadcast.RequiredConfirmations.Should().Be(12);
    }

    [Fact]
    public async Task A_broadcasted_transaction_in_a_block_reads_its_count_and_is_not_settled()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, TxBody(TxStatus.Broadcasted, 5, 12)));

        var tx = await NewClient(handler).Transactions.InfoAsync("tx-1");

        tx.Status.Should().Be(TxStatus.Broadcasted);
        tx.Confirmations.Should().Be(5);
        tx.RequiredConfirmations.Should().Be(12);
        tx.IsTerminal.Should().BeFalse();
        tx.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task History_reads_the_count_of_each_item_whatever_its_status()
    {
        var historyHandler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"items\":[" + TxBody(TxStatus.Broadcasted, 3, 20) + ","
            + TxBody(TxStatus.Failed, 0, 1, "\"error_reason\":\"tx reverted\",") + ","
            + TxBody(TxStatus.Expired, 0, 20) + "],\"meta\":{\"total\":3,\"page\":1,\"page_size\":20}}"));
        var page = await NewClient(historyHandler).Transactions.HistoryAsync(new HistoryQuery());

        historyHandler.Captured.Single().RequestUri!.AbsolutePath.Should().Be("/v1/transaction/history");
        page.Items.Select(t => t.Status).Should().Equal(TxStatus.Broadcasted, TxStatus.Failed, TxStatus.Expired);
        page.Items.Select(t => t.Confirmations).Should().Equal(3, 0, 0);
        page.Items.Select(t => t.RequiredConfirmations).Should().Equal(20, 1, 20);
    }

    [Fact]
    public async Task Waiting_returns_on_the_confirmed_status_not_on_a_count_above_zero()
    {
        // Not in a block, in a block, shallower after a reorganisation, then at the depth.
        var bodies = new Queue<string>(new[]
        {
            TxBody(TxStatus.Broadcasted, 0, 12),
            TxBody(TxStatus.Broadcasted, 4, 12),
            TxBody(TxStatus.Broadcasted, 2, 12),
            TxBody(TxStatus.Confirmed, 12, 12, "\"completed_at\":\"2026-09-14T10:03:00Z\","),
        });
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, bodies.Dequeue()));

        var final = await NewClient(handler).WaitForTransactionAsync("tx-1",
            new PollOptions { Interval = TimeSpan.FromMilliseconds(1), Timeout = TimeSpan.FromSeconds(10) });

        handler.Captured.Should().HaveCount(4);
        final.Status.Should().Be(TxStatus.Confirmed);
        final.Confirmations.Should().Be(12);
        final.RequiredConfirmations.Should().Be(12);
    }

    [Fact]
    public async Task A_confirmed_transaction_final_without_a_block_count_reads_the_threshold()
    {
        // A network whose scanner reports finality without a block count (Solana finalized) is
        // published as the threshold applied, so a confirmed transaction reads at or above it.
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            TxBody(TxStatus.Confirmed, 32, 32, "\"completed_at\":\"2026-09-14T10:01:00Z\",")
                .Replace("\"network\":\"ETH_MAINNET\",\"chain_family\":\"evm\"",
                         "\"network\":\"SOLANA_MAINNET\",\"chain_family\":\"solana\"")));

        var tx = await NewClient(handler).Transactions.InfoAsync("tx-1");

        tx.Succeeded.Should().BeTrue();
        tx.Network.Should().Be("SOLANA_MAINNET");
        tx.Confirmations.Should().Be(32);
        tx.RequiredConfirmations.Should().Be(32);
        tx.Confirmations.Should().BeGreaterThanOrEqualTo(tx.RequiredConfirmations!.Value);
    }

    [Fact]
    public async Task A_platform_without_the_fields_reads_null_not_zero()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"uuid\":\"tx-1\",\"status\":\"broadcasted\",\"network\":\"ETH_MAINNET\",\"from_address\":\"0xa\"}"));

        var tx = await NewClient(handler).Transactions.InfoAsync("tx-1");

        tx.Confirmations.Should().BeNull();
        tx.RequiredConfirmations.Should().BeNull();
    }

    [Fact]
    public void Terminal_webhooks_carry_both_fields()
    {
        var confirmed = WithEvent(TxBody(TxStatus.Confirmed, 15, 12, "\"completed_at\":\"2026-09-14T10:03:00Z\","),
            "transaction.confirmed");
        var expired = WithEvent(TxBody(TxStatus.Expired, 0, 12), "transaction.expired");

        var ok = WebhookVerifier.VerifyAndDecode<TransactionWebhookEvent>(ApiKey, confirmed, Sign(confirmed));
        var gone = WebhookVerifier.VerifyAndDecode<TransactionWebhookEvent>(ApiKey, expired, Sign(expired));

        ok.Event.Should().Be("transaction.confirmed");
        ok.Confirmations.Should().Be(15);
        ok.RequiredConfirmations.Should().Be(12);
        gone.Event.Should().Be("transaction.expired");
        gone.Confirmations.Should().Be(0);
        gone.RequiredConfirmations.Should().Be(12);
    }

    private static byte[] WithEvent(string body, string eventName)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(body)!.AsObject();
        node["event"] = eventName;
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }

    private static string Sign(byte[] body) =>
        RequestSigner.Sign(CanonicalJson.Canonicalise(body), ApiKey);

    private static HttpResponseMessage Resp(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static CryptoChiefClient NewClient(HttpMessageHandler handler) =>
        new(new CryptoChiefClientOptions
        {
            MerchantId        = "M-1",
            ApiKey            = ApiKey,
            BaseUrl           = "https://test/",
            MaxRetries        = 0,
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
