using System.Net;
using System.Text;
using CryptoChief.Processing.Models;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

/// <summary>
/// Manual withdrawals: the status names the platform actually sends (<c>completed</c> and
/// <c>failed</c>, not a payout's <c>paid</c> and <c>system_fail</c>), and the confirmation count
/// that keeps a withdrawal in <c>confirm_check</c> until its network's finality depth.
/// </summary>
public class WithdrawalsTests
{
    // In a block, 3 of the 12 confirmations the network needs: still confirm_check.
    private const string ConfirmingBody = """
        {"uuid":"w-1","status":"confirm_check",
         "from_address":"0xmaster","to_address":"0xdest","amount":"100.5",
         "network":"ETH_MAINNET","coin":"USDT",
         "need_refuel":true,"refuel_tx_hash":"0xrefuel","refuel_status":"done",
         "tx_hash":"0xwd","confirmations":3,"required_confirmations":12,
         "estimated_fee_fiat":"1.20","fee_mode":"service",
         "created_at":"2026-09-14T10:00:00Z"}
        """;

    // Just sent: the hash is known, the transaction has not been seen in a block, so there is no
    // count at all - the key is absent, not 0.
    private const string SentBody = """
        {"uuid":"w-2","status":"confirm_check",
         "from_address":"0xmaster","to_address":"0xdest","amount":"5",
         "network":"ETH_MAINNET","coin":"ETH","need_refuel":false,
         "tx_hash":"0xwd2","required_confirmations":12,
         "estimated_fee_fiat":"0.40","fee_mode":"client",
         "created_at":"2026-09-14T10:00:00Z"}
        """;

    // Completed at the depth: the count reached 12, completed_at is the moment it did.
    private const string CompletedBody = """
        {"uuid":"w-1","status":"completed",
         "from_address":"0xmaster","to_address":"0xdest","amount":"100.5",
         "network":"ETH_MAINNET","coin":"USDT",
         "need_refuel":true,"refuel_tx_hash":"0xrefuel","refuel_status":"done",
         "tx_hash":"0xwd","confirmations":12,"required_confirmations":12,
         "estimated_fee_fiat":"1.20","actual_fee_fiat":"1.18","fee_mode":"service",
         "created_at":"2026-09-14T10:00:00Z","completed_at":"2026-09-14T10:07:00Z"}
        """;

    [Fact]
    public async Task Info_in_a_block_below_the_depth_is_confirm_check_and_not_completed()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, ConfirmingBody));

        var wd = await NewClient(handler).Withdrawals.InfoAsync("w-1");

        handler.Captured.Single().RequestUri!.AbsolutePath.Should().Be("/v1/withdrawal/info");
        wd.Status.Should().Be(WithdrawalStatus.ConfirmCheck);
        wd.Confirmations.Should().Be(3);
        wd.RequiredConfirmations.Should().Be(12);
        // A count above zero is not settlement.
        wd.Succeeded.Should().BeFalse();
        wd.IsTerminal.Should().BeFalse();
        wd.CompletedAt.Should().BeNull();
        wd.ActualFeeFiat.Should().BeNull();
        wd.NeedRefuel.Should().BeTrue();
        wd.RefuelTxHash.Should().Be("0xrefuel");
        wd.RefuelStatus.Should().Be("done");
        wd.TxHash.Should().Be("0xwd");
        wd.FeeMode.Should().Be("service");
        wd.EstimatedFeeFiat.Should().Be("1.20");
    }

    [Fact]
    public async Task Info_before_the_transaction_is_in_a_block_has_no_confirmations_but_knows_the_depth()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, SentBody));

        var wd = await NewClient(handler).Withdrawals.InfoAsync("w-2");

        wd.Status.Should().Be(WithdrawalStatus.ConfirmCheck);
        wd.Confirmations.Should().BeNull();
        wd.RequiredConfirmations.Should().Be(12);
        wd.NeedRefuel.Should().BeFalse();
        wd.RefuelTxHash.Should().BeNull();
        wd.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Info_completed_at_the_depth()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, CompletedBody));

        var wd = await NewClient(handler).Withdrawals.InfoAsync("w-1");

        wd.Status.Should().Be(WithdrawalStatus.Completed);
        wd.Succeeded.Should().BeTrue();
        wd.IsTerminal.Should().BeTrue();
        wd.Confirmations.Should().Be(12);
        wd.RequiredConfirmations.Should().Be(12);
        wd.Confirmations.Should().BeGreaterThanOrEqualTo(wd.RequiredConfirmations!.Value);
        wd.CompletedAt.Should().Be("2026-09-14T10:07:00Z");
        wd.ActualFeeFiat.Should().Be("1.18");
        // The fields the model used to read the completion moment and the failure from are not
        // on the wire.
        wd.ConfirmedAt.Should().BeNull();
        wd.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task History_items_carry_the_count_when_seen_and_the_depth_always()
    {
        const string body = """
            {"items":[
              {"uuid":"w-q","status":"queue","from_address":"0xmaster","to_address":"0xdest",
               "amount":"1","network":"TRON_MAINNET","coin":"USDT","need_refuel":true,
               "required_confirmations":20,"estimated_fee_fiat":"0.30","fee_mode":"mix",
               "created_at":"2026-09-14T09:00:00Z"},
              {"uuid":"w-m","status":"in_mempool","from_address":"bc1qmaster","to_address":"bc1qdest",
               "amount":"0.01","network":"BTC_MAINNET","coin":"BTC","need_refuel":false,
               "tx_hash":"btctx","required_confirmations":3,"estimated_fee_fiat":"0.90","fee_mode":"client",
               "created_at":"2026-09-14T09:10:00Z"},
              {"uuid":"w-c","status":"confirm_check","from_address":"bc1qmaster","to_address":"bc1qdest",
               "amount":"0.02","network":"BTC_MAINNET","coin":"BTC","need_refuel":false,
               "tx_hash":"btctx2","confirmations":1,"required_confirmations":3,
               "estimated_fee_fiat":"0.90","fee_mode":"client","created_at":"2026-09-14T09:20:00Z"},
              {"uuid":"w-f","status":"failed","from_address":"0xmaster","to_address":"0xdest",
               "amount":"50","network":"BSC_MAINNET","coin":"BNB","need_refuel":false,
               "error_reason":"TX_CONFIRM_TIMEOUT","required_confirmations":15,
               "estimated_fee_fiat":"0.08","fee_mode":"client","created_at":"2026-09-14T09:30:00Z"},
            """ + CompletedBody + """
            ],"meta":{"total":5,"page":1,"page_size":20,"total_pages":1}}
            """;
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, body));

        var page = await NewClient(handler).Withdrawals.HistoryAsync(new HistoryQuery());

        handler.Captured.Single().RequestUri!.AbsolutePath.Should().Be("/v1/withdrawal/history");
        page.Items.Select(w => w.Status).Should().Equal(
            WithdrawalStatus.Queue, WithdrawalStatus.InMempool, WithdrawalStatus.ConfirmCheck,
            WithdrawalStatus.Failed, WithdrawalStatus.Completed);
        page.Items.Select(w => w.Confirmations).Should().Equal(null, null, 1, null, 12);
        page.Items.Select(w => w.RequiredConfirmations).Should().Equal(20, 3, 3, 15, 12);

        page.Items.Where(w => w.IsTerminal).Select(w => w.Uuid).Should().Equal("w-f", "w-1");
        page.Items.Where(w => w.Succeeded).Select(w => w.Uuid).Should().Equal("w-1");

        var failed = page.Items[3];
        failed.ErrorReason.Should().Be("TX_CONFIRM_TIMEOUT");
        failed.Error.Should().BeNull();
        failed.CompletedAt.Should().BeNull();
        page.Meta.Total.Should().Be(5);
    }

    [Fact]
    public async Task Info_after_the_transaction_left_a_block_reads_zero_and_stays_confirm_check()
    {
        // The transaction was in a block and dropped out of it: the count is 0, not absent.
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            ConfirmingBody.Replace("\"confirmations\":3,", "\"confirmations\":0,")));

        var wd = await NewClient(handler).Withdrawals.InfoAsync("w-1");

        wd.Status.Should().Be(WithdrawalStatus.ConfirmCheck);
        wd.Confirmations.Should().Be(0);
        wd.RequiredConfirmations.Should().Be(12);
        wd.Succeeded.Should().BeFalse();
        wd.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public async Task A_platform_without_the_depth_reads_null()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            ConfirmingBody.Replace("\"confirmations\":3,\"required_confirmations\":12,", "")));

        var wd = await NewClient(handler).Withdrawals.InfoAsync("w-1");

        wd.Status.Should().Be(WithdrawalStatus.ConfirmCheck);
        wd.Confirmations.Should().BeNull();
        wd.RequiredConfirmations.Should().BeNull();
    }

    [Theory]
    [InlineData("queue", false)]
    [InlineData("refueling", false)]
    [InlineData("refuel_confirmed", false)]
    [InlineData("sending", false)]
    [InlineData("broadcasting", false)]
    [InlineData("in_mempool", false)]
    [InlineData("confirm_check", false)]
    [InlineData("completed", true)]
    [InlineData("failed", true)]
    // Not sent by the API; kept terminal for compatibility.
    [InlineData("cancelled", true)]
    // A payout's names are not a withdrawal's: the platform never sends these for a withdrawal.
    [InlineData("paid", false)]
    [InlineData("system_fail", false)]
    public void Terminal_statuses(string status, bool terminal)
    {
        var wd = new Withdrawal { Status = status };

        wd.IsTerminal.Should().Be(terminal);
        wd.Succeeded.Should().Be(status == "completed");
    }

    [Fact]
    public void Status_constants_are_the_wire_names()
    {
        new[]
        {
            WithdrawalStatus.Queue, WithdrawalStatus.Refueling, WithdrawalStatus.RefuelConfirmed,
            WithdrawalStatus.Sending, WithdrawalStatus.Broadcasting, WithdrawalStatus.InMempool,
            WithdrawalStatus.ConfirmCheck, WithdrawalStatus.Completed, WithdrawalStatus.Failed,
            WithdrawalStatus.Cancelled,
        }.Should().Equal(
            "queue", "refueling", "refuel_confirmed", "sending", "broadcasting", "in_mempool",
            "confirm_check", "completed", "failed", "cancelled");
    }

    private static HttpResponseMessage Resp(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured.Add(request);
            return Task.FromResult(reply(request));
        }
    }
}
