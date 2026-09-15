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
/// Confirmation counts on a payout: per source, per service operation and the payout's own,
/// which is the lowest among its sources; and the network's finality depth every source has
/// to reach before the payout is paid. All of them are optional on the wire.
/// </summary>
public class PayoutsTests
{
    private const string ApiKey = "K-1";

    // Two sources on their way to a depth of 12: one confirmed 7 times, one broadcast and not
    // yet counted - so the payout itself reads 0 and is not paid. The gas top-up has its own count.
    private const string ConfirmingPayoutBody = """
        {"uuid":"p-1","order_id":"o-1","user_id":"u-1","status":"confirm_check",
         "amount_requested":"1.5","amount_to_receive":"1.5","to_address":"0xdest",
         "fee_info":{"fee_mode":"service","limit_currency":"USD"},
         "sources":[
           {"address":"0xa","network":"ETH_MAINNET","coin":"USDT","amount_crypto":"1","need_refuel":true,
            "refuel_amount":"0.001","estimated_fee":"0.0004","estimated_fee_fiat":"1.10",
            "txid":"0xt1","confirmations":7},
           {"address":"0xb","network":"ETH_MAINNET","coin":"USDT","amount_crypto":"0.5","need_refuel":false,
            "refuel_amount":"0","estimated_fee":"0.0004","estimated_fee_fiat":"1.10",
            "txid":"0xt2"}
         ],
         "service_operations":[
           {"type":"gas_refuel","context":"payout_prepare","status":"done","network":"ETH_MAINNET","coin":"ETH",
            "amount_native":"0.001","from_address":"0xmaster","to_address":"0xa",
            "estimated_fee":"0.0001","estimated_fee_fiat":"0.30","confirmations":12}
         ],
         "confirmations":0,
         "required_confirmations":12,
         "created_at":"2026-09-14T10:00:00Z","completed_at":null}
        """;

    // Paid: every source reached the depth of 12, so the lowest count is 12 as well.
    private const string PaidPayoutBody = """
        {"uuid":"p-1","order_id":"o-1","user_id":"u-1","status":"paid",
         "amount_requested":"1.5","amount_to_receive":"1.5","to_address":"0xdest",
         "fee_info":{"fee_mode":"service","limit_currency":"USD"},
         "sources":[
           {"address":"0xa","network":"ETH_MAINNET","coin":"USDT","amount_crypto":"1","need_refuel":true,
            "refuel_amount":"0.001","estimated_fee":"0.0004","estimated_fee_fiat":"1.10",
            "txid":"0xt1","confirmations":15},
           {"address":"0xb","network":"ETH_MAINNET","coin":"USDT","amount_crypto":"0.5","need_refuel":false,
            "refuel_amount":"0","estimated_fee":"0.0004","estimated_fee_fiat":"1.10",
            "txid":"0xt2","confirmations":12}
         ],
         "service_operations":[
           {"type":"gas_refuel","context":"payout_prepare","status":"done","network":"ETH_MAINNET","coin":"ETH",
            "amount_native":"0.001","from_address":"0xmaster","to_address":"0xa",
            "estimated_fee":"0.0001","estimated_fee_fiat":"0.30","confirmations":20}
         ],
         "confirmations":12,
         "required_confirmations":12,
         "created_at":"2026-09-14T10:00:00Z","completed_at":"2026-09-14T10:05:00Z"}
        """;

    // Queued: nothing has been sent, so no count exists anywhere and every count key is absent.
    // The depth is known from creation.
    private const string QueuedPayoutBody = """
        {"uuid":"p-2","order_id":"o-2","user_id":"u-1","status":"queue",
         "amount_requested":"2","amount_to_receive":"2","to_address":"0xdest",
         "sources":[
           {"address":"0xa","network":"TRON_MAINNET","coin":"USDT","amount_crypto":"2","need_refuel":false,
            "refuel_amount":"0","estimated_fee":"1","estimated_fee_fiat":"0.30"}
         ],
         "service_operations":[
           {"type":"gas_refuel","context":"payout_prepare","status":"planned","network":"TRON_MAINNET","coin":"TRX",
            "amount_native":"10","from_address":"0xmaster","to_address":"0xa"}
         ],
         "required_confirmations":20,
         "created_at":"2026-09-14T10:00:00Z"}
        """;

    [Fact]
    public async Task Info_reads_confirmations_of_the_payout_its_sources_and_service_operations()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, ConfirmingPayoutBody));

        var payout = await NewClient(handler).Payouts.InfoAsync("p-1");

        handler.Captured.Single().RequestUri!.AbsolutePath.Should().Be("/v1/payout/info");
        payout.Sources.Should().HaveCount(2);
        payout.Sources![0].Confirmations.Should().Be(7);
        // Broadcast, not yet counted: the source says nothing, the payout counts it as 0.
        payout.Sources[1].Confirmations.Should().BeNull();
        payout.Confirmations.Should().Be(0);

        var op = payout.ServiceOperations.Should().ContainSingle().Subject;
        op.Type.Should().Be("gas_refuel");
        op.Status.Should().Be("done");
        op.Coin.Should().Be("ETH");
        op.Confirmations.Should().Be(12);
    }

    [Fact]
    public async Task A_payout_in_a_block_below_the_depth_is_not_paid_yet()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, ConfirmingPayoutBody));

        var payout = await NewClient(handler).Payouts.InfoAsync("p-1");

        // One source already has 7 confirmations, but paid waits for every source to reach 12.
        payout.Status.Should().Be(PayoutStatus.ConfirmCheck);
        payout.RequiredConfirmations.Should().Be(12);
        payout.Succeeded.Should().BeFalse();
        payout.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public async Task Info_before_any_transaction_has_no_confirmations_but_knows_the_depth()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, QueuedPayoutBody));

        var payout = await NewClient(handler).Payouts.InfoAsync("p-2");

        // Absent is not 0: nothing has been observed on chain yet.
        payout.Confirmations.Should().BeNull();
        payout.Sources.Should().ContainSingle().Which.Confirmations.Should().BeNull();
        payout.ServiceOperations.Should().ContainSingle().Which.Confirmations.Should().BeNull();
        payout.RequiredConfirmations.Should().Be(20);
    }

    [Fact]
    public async Task Execute_replay_and_history_items_carry_the_same_counts()
    {
        var executeHandler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, PaidPayoutBody));
        var replay = await NewClient(executeHandler).Payouts.ExecuteAsync(new ExecutePayoutRequest
        {
            OrderId     = "o-1",
            UserId      = "u-1",
            Network     = "ETH_MAINNET",
            Coin        = "USDT",
            Amount      = "1.5",
            ToAddress   = "0xdest",
            UrlCallback = "https://m.example/hook",
        });
        replay.Succeeded.Should().BeTrue();
        replay.Confirmations.Should().Be(12);
        replay.RequiredConfirmations.Should().Be(12);
        replay.Sources!.Select(s => s.Confirmations).Should().Equal(15, 12);

        var historyHandler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"items\":[" + ConfirmingPayoutBody + "," + QueuedPayoutBody + "],\"meta\":{\"total\":2,\"page\":1,\"page_size\":20}}"));
        var page = await NewClient(historyHandler).Payouts.HistoryAsync(new HistoryQuery());

        historyHandler.Captured.Single().RequestUri!.AbsolutePath.Should().Be("/v1/payout/history");
        page.Items.Select(p => p.Confirmations).Should().Equal(0, null);
        page.Items.Select(p => p.RequiredConfirmations).Should().Equal(12, 20);
        page.Items[0].ServiceOperations![0].Confirmations.Should().Be(12);
        page.Items[1].Sources![0].Confirmations.Should().BeNull();
    }

    [Fact]
    public async Task A_paid_payout_final_without_a_block_count_reads_the_depth()
    {
        // A network whose scanner reports finality without a block count (Solana finalized) is
        // published as the depth, so a paid payout still reads at or above it.
        const string body = """
            {"uuid":"p-3","order_id":"o-3","user_id":"u-1","status":"paid",
             "amount_requested":"5","amount_to_receive":"5","to_address":"DestSol",
             "sources":[
               {"address":"SrcSol","network":"SOLANA_MAINNET","coin":"USDC","amount_crypto":"5",
                "txid":"5sig","confirmations":32}
             ],
             "confirmations":32,
             "required_confirmations":32,
             "created_at":"2026-09-14T10:00:00Z","completed_at":"2026-09-14T10:01:00Z"}
            """;
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, body));

        var payout = await NewClient(handler).Payouts.InfoAsync("p-3");

        payout.Succeeded.Should().BeTrue();
        payout.Sources.Should().ContainSingle().Which.Confirmations.Should().Be(32);
        payout.Confirmations.Should().Be(32);
        payout.RequiredConfirmations.Should().Be(32);
        payout.Confirmations.Should().BeGreaterThanOrEqualTo(payout.RequiredConfirmations!.Value);
    }

    [Fact]
    public async Task Sources_read_the_wire_fields_amount_crypto_and_txid()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, PaidPayoutBody
            .Replace("\"txid\":\"0xt1\",", "\"txid\":\"0xt1\",\"fee_paid\":\"0.00031\",\"fee_paid_fiat\":\"0.85\",")));

        var payout = await NewClient(handler).Payouts.InfoAsync("p-1");

        var src = payout.Sources![0];
        src.Address.Should().Be("0xa");
        src.Network.Should().Be("ETH_MAINNET");
        src.Coin.Should().Be("USDT");
        src.AmountCrypto.Should().Be("1");
        src.TxId.Should().Be("0xt1");
        src.NeedRefuel.Should().BeTrue();
        src.RefuelAmount.Should().Be("0.001");
        src.EstimatedFee.Should().Be("0.0004");
        src.EstimatedFeeFiat.Should().Be("1.10");
        src.FeePaid.Should().Be("0.00031");
        src.FeePaidFiat.Should().Be("0.85");
        payout.Sources.Select(s => s.AmountCrypto).Should().Equal("1", "0.5");
#pragma warning disable CS0618
        src.Amount.Should().BeNull();
#pragma warning restore CS0618
    }

    [Fact]
    public async Task A_paid_payout_without_the_depth_reads_null()
    {
        // A payout recorded without a depth carries no required_confirmations.
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, """
            {"uuid":"p-4","order_id":"o-4","user_id":"u-1","status":"paid",
             "amount_requested":"1","amount_to_receive":"1","to_address":"0xdest",
             "sources":[
               {"address":"0xa","network":"ETH_MAINNET","coin":"USDT","amount_crypto":"1",
                "txid":"0xt1","confirmations":1}
             ],
             "confirmations":1,
             "created_at":"2026-08-01T10:00:00Z","completed_at":"2026-08-01T10:01:00Z"}
            """));

        var payout = await NewClient(handler).Payouts.InfoAsync("p-4");

        payout.Status.Should().Be(PayoutStatus.Paid);
        payout.Succeeded.Should().BeTrue();
        payout.IsTerminal.Should().BeTrue();
        payout.Sources.Should().ContainSingle().Which.Confirmations.Should().Be(1);
        payout.Confirmations.Should().Be(1);
        payout.RequiredConfirmations.Should().BeNull();
    }

    [Fact]
    public void Status_constants_match_the_wire_names()
    {
        new[]
        {
            PayoutStatus.Queue, PayoutStatus.Refueling, PayoutStatus.RefuelConfirmed,
            PayoutStatus.Sending, PayoutStatus.Broadcasting, PayoutStatus.InMempool,
            PayoutStatus.ConfirmCheck, PayoutStatus.Paid, PayoutStatus.Failed,
            PayoutStatus.SystemFail, PayoutStatus.Expired, PayoutStatus.Cancel,
        }.Should().Equal(
            "queue", "refueling", "refuel_confirmed", "sending", "broadcasting", "in_mempool",
            "confirm_check", "paid", "failed", "system_fail", "expired", "cancel");
    }

    [Theory]
    [InlineData(PayoutStatus.Queue)]
    [InlineData(PayoutStatus.Refueling)]
    [InlineData(PayoutStatus.RefuelConfirmed)]
    [InlineData(PayoutStatus.Sending)]
    [InlineData(PayoutStatus.Broadcasting)]
    [InlineData(PayoutStatus.InMempool)]
    [InlineData(PayoutStatus.ConfirmCheck)]
    public void In_flight_statuses_are_not_terminal(string status)
    {
        var payout = new PayoutInfo { Status = status };

        payout.IsTerminal.Should().BeFalse();
        payout.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Only_the_payout_wait_defaults_to_90_minutes()
    {
        new PollOptions().Timeout.Should().Be(TimeSpan.FromMinutes(10));
        PollOptions.PayoutTimeout.Should().Be(TimeSpan.FromMinutes(90));

        PollingExtensions.PayoutOptions(null).Timeout.Should().Be(TimeSpan.FromMinutes(90));
        PollingExtensions.PayoutOptions(null).Interval.Should().Be(new PollOptions().Interval);

        var own = new PollOptions { Timeout = TimeSpan.FromMinutes(3) };
        PollingExtensions.PayoutOptions(own).Should().BeSameAs(own);
    }

    [Fact]
    public async Task Waiting_passes_confirm_check_and_returns_paid()
    {
        var bodies = new Queue<string>(new[] { QueuedPayoutBody, ConfirmingPayoutBody, ConfirmingPayoutBody, PaidPayoutBody });
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, bodies.Dequeue()));

        var final = await NewClient(handler).WaitForPayoutAsync("p-1",
            new PollOptions { Interval = TimeSpan.FromMilliseconds(1), Timeout = TimeSpan.FromSeconds(10) });

        handler.Captured.Should().HaveCount(4);
        final.Status.Should().Be(PayoutStatus.Paid);
    }

    [Fact]
    public async Task Waiting_times_out_in_confirm_check_with_the_last_snapshot()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, ConfirmingPayoutBody));

        var act = () => NewClient(handler).WaitForPayoutAsync("p-1",
            new PollOptions { Interval = TimeSpan.FromMilliseconds(10), Timeout = TimeSpan.FromSeconds(2) });

        var ex = (await act.Should().ThrowAsync<TimeoutException>()).Which;
        handler.Captured.Should().NotBeEmpty();
        var last = ex.Data["LastSnapshot"].Should().BeOfType<PayoutInfo>().Subject;
        last.Status.Should().Be(PayoutStatus.ConfirmCheck);
        last.Confirmations.Should().Be(0);
        last.RequiredConfirmations.Should().Be(12);
    }

    [Fact]
    public async Task A_platform_without_the_depth_reads_null()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            PaidPayoutBody.Replace("\"required_confirmations\":12,", "")));

        var payout = await NewClient(handler).Payouts.InfoAsync("p-1");

        payout.Succeeded.Should().BeTrue();
        payout.Confirmations.Should().Be(12);
        payout.RequiredConfirmations.Should().BeNull();
    }

    [Fact]
    public void Paid_webhook_carries_the_counts_and_the_depth_every_source_reached()
    {
        var body = WithEvent(PaidPayoutBody, "payout.paid");

        var evt = WebhookVerifier.VerifyAndDecode<PayoutWebhookEvent>(ApiKey, body, Sign(body));

        evt.Event.Should().Be("payout.paid");
        evt.Confirmations.Should().Be(12);
        evt.RequiredConfirmations.Should().Be(12);
        var sources = evt.Sources!.Value.EnumerateArray().ToList();
        sources.Select(s => s.GetProperty("confirmations").GetInt32())
            .Should().OnlyContain(n => n >= evt.RequiredConfirmations);
        evt.ServiceOperations!.Value[0].GetProperty("confirmations").GetInt32().Should().Be(20);
    }

    [Fact]
    public void Paid_webhook_for_a_payout_confirmed_before_the_depth_was_recorded_has_no_depth()
    {
        var body = WithEvent(PaidPayoutBody.Replace("\"required_confirmations\":12,", ""), "payout.paid");

        var evt = WebhookVerifier.VerifyAndDecode<PayoutWebhookEvent>(ApiKey, body, Sign(body));

        evt.Event.Should().Be("payout.paid");
        evt.RequiredConfirmations.Should().BeNull();
        evt.Confirmations.Should().Be(12);
    }

    [Fact]
    public void System_fail_webhook_without_transactions_has_no_confirmations()
    {
        var body = WithEvent(QueuedPayoutBody.Replace("\"status\":\"queue\"", "\"status\":\"system_fail\""),
            "payout.system_fail");

        var evt = WebhookVerifier.VerifyAndDecode<PayoutWebhookEvent>(ApiKey, body, Sign(body));

        evt.Status.Should().Be(PayoutStatus.SystemFail);
        evt.Confirmations.Should().BeNull();
        evt.RequiredConfirmations.Should().Be(20);
        evt.Sources!.Value[0].TryGetProperty("confirmations", out _).Should().BeFalse();
        evt.ServiceOperations!.Value[0].TryGetProperty("confirmations", out _).Should().BeFalse();
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
