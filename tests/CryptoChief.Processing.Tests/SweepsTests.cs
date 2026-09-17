using System.Net;
using System.Text;
using System.Text.Json;
using CryptoChief.Processing.Http;
using CryptoChief.Processing.Models;
using CryptoChief.Processing.Webhooks;
using CryptoChief.Processing.Webhooks.Events;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class SweepsTests
{
    private const string SettingsBody = """
        {
          "wallet_address": "0xabc",
          "network_code": "ETH_MAINNET",
          "effective": {"type_work":"threshold","threshold_amount_usd":"250","fee_mode":"mix","gas_source":"native","source":"wallet"},
          "override": {"network_code":"","type_work":"threshold","threshold_amount_usd":"250","fee_mode":null,"gas_source":null,"source":"merchant","locked":false},
          "project_default": {"type_work":"momentum","fee_mode":"client","gas_source":"native"}
        }
        """;

    [Fact]
    public async Task Settings_returns_three_distinguishable_layers()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, SettingsBody));
        var client = NewClient(handler);

        var settings = await client.Sweeps.SettingsAsync(new SweepSettingsQuery { Address = "0xabc" });

        handler.Captured[0].RequestUri!.AbsolutePath.Should().Be("/v1/sweeps/settings");
        settings.Effective.TypeWork.Should().Be(SweepPolicyMode.Threshold);
        settings.Effective.ThresholdAmountUsd.Should().Be("250");
        settings.Effective.Source.Should().Be("wallet");

        // An inherited field reads as null on the override while the effective policy still
        // has a value. That difference is the point of the three-layer shape.
        settings.Override.Should().NotBeNull();
        settings.Override!.FeeMode.Should().BeNull();
        settings.Override.TypeWork.Should().Be("threshold");
        settings.Override.Locked.Should().BeFalse();
        settings.ProjectDefault.TypeWork.Should().Be(SweepPolicyMode.Momentum);
    }

    [Fact]
    public async Task Update_writes_only_the_fields_it_was_given()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, SettingsBody));
        var client = NewClient(handler);

        await client.Sweeps.UpdateSettingsAsync("0xabc",
            typeWork: SweepFieldWrite.Set(SweepPolicyMode.Threshold),
            thresholdAmountUsd: SweepFieldWrite.Set("250"));

        var body = JsonDocument.Parse(handler.CapturedBodies[0]).RootElement;
        handler.Captured[0].RequestUri!.AbsolutePath.Should().Be("/v1/sweeps/settings/update");
        body.GetProperty("type_work").GetString().Should().Be("threshold");
        body.GetProperty("threshold_amount_usd").GetString().Should().Be("250");
        // Sending fee_mode at all would rewrite it; untouched means absent.
        body.TryGetProperty("fee_mode", out _).Should().BeFalse();
        body.GetProperty("fields").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("type_work", "threshold_amount_usd");
    }

    [Fact]
    public async Task Inherit_names_the_field_and_sends_no_value()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, SettingsBody));
        var client = NewClient(handler);

        await client.Sweeps.UpdateSettingsAsync("0xabc", typeWork: SweepFieldWrite.Inherit);

        var body = JsonDocument.Parse(handler.CapturedBodies[0]).RootElement;
        // The API's way of saying "inherit this again": named, with no value. null cannot
        // express it because it already means "not supplied".
        body.GetProperty("fields").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("type_work");
        body.TryGetProperty("type_work", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Gas_source_reads_in_all_three_layers_and_null_means_inherited()
    {
        // The trap this shape exists to expose: nobody ever chose a gas source for this
        // wallet — the override says null and the project decides nothing either — and yet
        // energy WILL be rented and billed to API credits, because "rented" is the platform
        // default. Only the effective layer says so.
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, """
            {
              "wallet_address": "TQrY8bYc2yQ8sM8nJ1sZ9c2Zx7L2wq7pQb",
              "network_code": "TRON_MAINNET",
              "effective": {"type_work":"momentum","fee_mode":"mix","gas_source":"rented","source":"default"},
              "override": {"network_code":"","type_work":"momentum","threshold_amount_usd":null,"fee_mode":null,"gas_source":null,"source":"merchant","locked":false},
              "project_default": {"type_work":"momentum","fee_mode":"mix"}
            }
            """));
        var client = NewClient(handler);

        var settings = await client.Sweeps.SettingsAsync(new SweepSettingsQuery
        {
            Address     = "TQrY8bYc2yQ8sM8nJ1sZ9c2Zx7L2wq7pQb",
            NetworkCode = "TRON_MAINNET",
        });

        // Effective is always concrete — this is the field to read.
        settings.Effective.GasSource.Should().Be(SweepGasSource.Rented);
        settings.Effective.Source.Should().Be("default");

        // Null on the override is "this layer does not decide", not "switched off". It has
        // to survive as null, because any value here would be a lie about what was chosen.
        settings.Override.Should().NotBeNull();
        settings.Override!.GasSource.Should().BeNull();

        // Same again on the project: absent is undecided, and the platform default wins.
        settings.ProjectDefault.GasSource.Should().BeNull();

        // And a wallet that did choose one reads it back concretely on both layers.
        var chosen = await NewClient(new CapturingHandler(_ => Resp(HttpStatusCode.OK, SettingsBody)))
            .Sweeps.SettingsAsync(new SweepSettingsQuery { Address = "0xabc" });
        chosen.Effective.GasSource.Should().Be(SweepGasSource.Native);
        chosen.ProjectDefault.GasSource.Should().Be(SweepGasSource.Native);
    }

    [Fact]
    public async Task Update_writes_gas_source_and_names_it_in_the_fields_mask()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, SettingsBody));
        var client = NewClient(handler);

        await client.Sweeps.UpdateSettingsAsync("TQrY8bYc2yQ8sM8nJ1sZ9c2Zx7L2wq7pQb",
            gasSource: SweepFieldWrite.Set(SweepGasSource.Native));

        var body = JsonDocument.Parse(handler.CapturedBodies[0]).RootElement;
        handler.Captured[0].RequestUri!.AbsolutePath.Should().Be("/v1/sweeps/settings/update");

        // Opting out of rented energy is an explicit write: omitting the field would leave
        // the stored value, and where nothing is stored that means "rented".
        body.GetProperty("gas_source").GetString().Should().Be("native");
        body.GetProperty("fields").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("gas_source");

        // The other fields are untouched, so they stay off the wire entirely.
        body.TryGetProperty("type_work", out _).Should().BeFalse();
        body.TryGetProperty("fee_mode", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Inherit_clears_gas_source_while_keeping_the_other_fields()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, SettingsBody));
        var client = NewClient(handler);

        await client.Sweeps.UpdateSettingsAsync("TQrY8bYc2yQ8sM8nJ1sZ9c2Zx7L2wq7pQb",
            feeMode: SweepFieldWrite.Set(SweepFeeMode.Client),
            gasSource: SweepFieldWrite.Inherit);

        var body = JsonDocument.Parse(handler.CapturedBodies[0]).RootElement;

        // Named in the mask with no value is what drops the override — the only way to
        // clear one field while keeping the others, so fee_mode still rides along.
        body.GetProperty("fields").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("fee_mode", "gas_source");
        body.TryGetProperty("gas_source", out _).Should().BeFalse();
        body.GetProperty("fee_mode").GetString().Should().Be("client");
    }

    [Fact]
    public async Task History_filters_on_status_and_search()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, """
            {"items":[{"task_id":"t1","status":"skipped","wallet_address":"0xa","chain":"ETH_MAINNET"}],
             "meta":{"total":1,"page":1,"page_size":20}}
            """));
        var client = NewClient(handler);

        var page = await client.Sweeps.HistoryAsync(new SweepHistoryQuery
        {
            Mode   = SweepMode.Auto,
            Status = SweepStatus.Skipped,
            Search = "0x77EDde",
        });

        var req = handler.Captured.Should().ContainSingle().Subject;
        req.RequestUri!.AbsolutePath.Should().Be("/v1/sweeps/history");

        // Body: snake_case keys. On this endpoint the
        // search runs over the wallet address, both transaction hashes and the task id.
        const string wire = "{\"mode\":\"auto\",\"search\":\"0x77EDde\",\"status\":\"skipped\"}";
        handler.CapturedBodies.Should().ContainSingle().Which.ShouldBeJson(wire);

        // Skipped is a normal outcome — a balance below the threshold — and asking for it
        // is the only way to see those, since an unfiltered page mixes them in.
        page.Items.Should().ContainSingle().Which.Status.Should().Be(SweepStatus.Skipped);
    }

    [Fact]
    public async Task Wallet_history_filters_alongside_the_address_it_requires()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"items\":[],\"meta\":{\"total\":0,\"page\":1,\"page_size\":20}}"));
        var client = NewClient(handler);

        await client.Sweeps.WalletHistoryAsync(new SweepWalletHistoryQuery
        {
            Address = "0x77EDde3213b70c9dd224C874c28f41B23B070f65",
            Status  = SweepStatus.Failed,
            Search  = "898cdbd0",
        });

        handler.Captured[0].RequestUri!.AbsolutePath.Should().Be("/v1/sweeps/wallet/history");
        const string wire = "{\"address\":\"0x77EDde3213b70c9dd224C874c28f41B23B070f65\","
            + "\"search\":\"898cdbd0\",\"status\":\"failed\"}";
        handler.CapturedBodies.Should().ContainSingle().Which.ShouldBeJson(wire);
    }

    [Fact]
    public async Task History_omits_the_filters_it_was_not_given()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"items\":[],\"meta\":{\"total\":0,\"page\":1,\"page_size\":20}}"));
        var client = NewClient(handler);

        await client.Sweeps.HistoryAsync(new SweepHistoryQuery { Page = 2 });

        // An empty status is a value the platform has to reject; "every status" is said by
        // leaving the key out.
        handler.CapturedBodies.Should().ContainSingle().Which.ShouldBeJson("{\"page\":2}");
    }

    [Fact]
    public async Task History_tells_a_broadcast_sweep_from_a_settled_one()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, """
            {"items":[
              {"task_id":"t1","status":"broadcasted","wallet_address":"0xa","chain":"ETH_MAINNET",
               "sweep_confirmations":2,"required_confirmations":12,"completed_at":"2026-08-28T09:58:00Z",
               "type_work":"threshold","total_fee_usd":"1.20"},
              {"task_id":"t2","status":"completed","wallet_address":"0xb","chain":"ETH_MAINNET",
               "sweep_confirmations":12,"required_confirmations":12,"completed_at":"2026-08-28T10:00:00Z","real_sweep_fee_usd":"0.98"}
            ],"meta":{"total":2,"page":1,"page_size":50}}
            """));
        var client = NewClient(handler);

        var page = await client.Sweeps.HistoryAsync(new SweepHistoryQuery { PageSize = 50 });

        var inFlight = page.Items[0];
        var settled = page.Items[1];
        inFlight.Status.Should().Be(SweepStatus.Broadcasted);
        // In a block, below the depth: still broadcasted.
        inFlight.SweepConfirmations.Should().Be(2);
        inFlight.RequiredConfirmations.Should().Be(12);
        // Set at broadcast, so an in-flight sweep already has it.
        inFlight.CompletedAt.Should().Be("2026-08-28T09:58:00Z");
        inFlight.TypeWork.Should().Be("threshold");
        inFlight.TotalFeeUsd.Should().Be("1.20");
        settled.Status.Should().Be(SweepStatus.Completed);
        settled.SweepConfirmations.Should().Be(12);
        settled.RequiredConfirmations.Should().Be(12);
        settled.CompletedAt.Should().Be("2026-08-28T10:00:00Z");
        settled.RealSweepFeeUsd.Should().Be("0.98");
    }

    [Fact]
    public async Task History_counts_a_sweep_up_to_the_depth_and_above_zero_is_not_settlement()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, """
            {"items":[
              {"task_id":"t6","status":"broadcasted","wallet_address":"0xa","chain":"ETH_MAINNET",
               "sweep_confirmations":0,"required_confirmations":12},
              {"task_id":"t7","status":"broadcasted","wallet_address":"0xb","chain":"ETH_MAINNET",
               "sweep_confirmations":3,"required_confirmations":12},
              {"task_id":"t8","status":"completed","wallet_address":"0xc","chain":"ETH_MAINNET",
               "sweep_confirmations":12,"required_confirmations":12},
              {"task_id":"t9","status":"completed","wallet_address":"SolWallet","chain":"SOLANA_MAINNET",
               "sweep_confirmations":32,"required_confirmations":32}
            ],"meta":{"total":4,"page":1,"page_size":20}}
            """));

        var page = await NewClient(handler).Sweeps.HistoryAsync(new SweepHistoryQuery());

        page.Items.Select(s => s.RequiredConfirmations).Should().Equal(12, 12, 12, 32);

        // A count above zero books t7, still in flight, as money received.
        page.Items.Where(s => s.SweepConfirmations > 0).Select(s => s.TaskId)
            .Should().Equal("t7", "t8", "t9");

        // Completed with a count above zero; here it agrees with the count reaching the depth.
        page.Items.Where(s => s.Status == SweepStatus.Completed && s.SweepConfirmations > 0).Select(s => s.TaskId)
            .Should().Equal("t8", "t9");
        page.Items.Where(s => s.SweepConfirmations >= s.RequiredConfirmations).Select(s => s.TaskId)
            .Should().Equal("t8", "t9");

        // Finality without a block count (Solana finalized) is published as the depth, not as
        // 4294967295 - which would not even fit the int this field is.
        page.Items[3].SweepConfirmations.Should().Be(page.Items[3].RequiredConfirmations);
    }

    [Fact]
    public async Task History_from_a_platform_without_the_depth_reads_null()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, """
            {"items":[{"task_id":"t1","status":"completed","wallet_address":"0xa","chain":"ETH_MAINNET",
                       "sweep_confirmations":1}],
             "meta":{"total":1,"page":1,"page_size":20}}
            """));

        var page = await NewClient(handler).Sweeps.HistoryAsync(new SweepHistoryQuery());

        var sweep = page.Items.Should().ContainSingle().Subject;
        sweep.Status.Should().Be(SweepStatus.Completed);
        sweep.SweepConfirmations.Should().Be(1);
        sweep.RequiredConfirmations.Should().BeNull();
    }

    [Fact]
    public async Task History_sweep_completed_with_zero_confirmations_is_not_settled()
    {
        // An older record: completed with 0 confirmations was never seen in a block.
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, """
            {"items":[
              {"task_id":"t1","status":"completed","wallet_address":"0xa","chain":"ETH_MAINNET",
               "sweep_confirmations":0,"required_confirmations":12,
               "completed_at":"2026-08-01T10:00:00Z"},
              {"task_id":"t2","status":"completed","wallet_address":"0xb","chain":"ETH_MAINNET",
               "sweep_confirmations":12,"required_confirmations":12,
               "completed_at":"2026-08-01T10:05:00Z"}
            ],"meta":{"total":2,"page":1,"page_size":20}}
            """));

        var page = await NewClient(handler).Sweeps.HistoryAsync(new SweepHistoryQuery());

        var sweep = page.Items[0];
        sweep.Status.Should().Be(SweepStatus.Completed);
        sweep.SweepConfirmations.Should().Be(0);
        sweep.RequiredConfirmations.Should().Be(12);

        page.Items
            .Where(s => s.Status == SweepStatus.Completed && s.SweepConfirmations > 0)
            .Select(s => s.TaskId)
            .Should().Equal("t2");
    }

    private const string SweepConfirmedBody = """
        {"event":"sweep.confirmed","task_id":"898cdbd0-task","status":"completed",
         "wallet_address":"0x77EDde3213b70c9dd224C874c28f41B23B070f65","to_address":"0xmaster",
         "network":"ETH_MAINNET","chain_family":"evm","asset_symbol":"USDT","asset_type":"token",
         "asset_contract":"0xdAC17F958D2ee523a2206206994597C13D831ec7",
         "amount_raw":"25000000","amount_human":"25","sweep_tx_hash":"0xsweep",
         "sweep_confirmations":14,"required_confirmations":12,
         "confirmed_at":"2026-09-14T10:04:00Z","type_work":"momentum","total_fee_usd":"1.10"}
        """;

    [Fact]
    public void Sweep_confirmed_webhook_carries_the_depth_the_sweep_was_held_to()
    {
        var body = Encoding.UTF8.GetBytes(SweepConfirmedBody);

        var evt = WebhookVerifier.VerifyAndDecode<SweepWebhookEvent>("K-1", body, Sign(body));

        evt.Event.Should().Be("sweep.confirmed");
        evt.Status.Should().Be(SweepStatus.Completed);
        evt.SweepConfirmations.Should().Be(14);
        evt.RequiredConfirmations.Should().Be(12);
        evt.SweepConfirmations.Should().BeGreaterThanOrEqualTo(evt.RequiredConfirmations!.Value);
    }

    [Fact]
    public void Sweep_confirmed_webhook_from_an_older_sweep_service_has_no_depth()
    {
        // A sweep service built before sweeps waited for finality reports at the first block
        // and sends no required_confirmations. The event must still decode.
        var body = Encoding.UTF8.GetBytes(SweepConfirmedBody
            .Replace("\"sweep_confirmations\":14,\"required_confirmations\":12,", "\"sweep_confirmations\":1,"));

        var evt = WebhookVerifier.VerifyAndDecode<SweepWebhookEvent>("K-1", body, Sign(body));

        evt.TaskId.Should().Be("898cdbd0-task");
        evt.SweepConfirmations.Should().Be(1);
        evt.RequiredConfirmations.Should().BeNull();
    }

    private static Dictionary<string, IEnumerable<string>> Sign(byte[] body) =>
        Wire.SignedWebhookHeaders("K-1", body);

    [Fact]
    public async Task History_stamps_completed_at_on_a_failed_sweep_too()
    {
        // completed_at is set on failed and skipped sweeps too. Reading its presence as
        // settlement books a failure as money received; the status is what separates them.
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, """
            {"items":[
              {"task_id":"t3","status":"failed","wallet_address":"0xc","chain":"ETH_MAINNET",
               "sweep_confirmations":0,"completed_at":"2026-08-28T11:00:00Z"},
              {"task_id":"t4","status":"skipped","wallet_address":"0xd","chain":"ETH_MAINNET",
               "sweep_confirmations":0,"completed_at":"2026-08-28T11:05:00Z"},
              {"task_id":"t5","status":"completed","wallet_address":"0xe","chain":"ETH_MAINNET",
               "sweep_confirmations":12,"completed_at":"2026-08-28T11:10:00Z"}
            ],"meta":{"total":3,"page":1,"page_size":50}}
            """));

        var page = await NewClient(handler).Sweeps.HistoryAsync(new SweepHistoryQuery());

        page.Items.Should().OnlyContain(s => s.CompletedAt != null);

        var failed = page.Items[0];
        failed.Status.Should().Be(SweepStatus.Failed);
        failed.CompletedAt.Should().NotBeNull();
        failed.SweepConfirmations.Should().Be(0);

        var skipped = page.Items[1];
        skipped.Status.Should().Be(SweepStatus.Skipped);
        skipped.CompletedAt.Should().NotBeNull();

        // The settlement test: status completed with a count above zero.
        page.Items
            .Where(s => s.Status == SweepStatus.Completed && s.SweepConfirmations > 0)
            .Select(s => s.TaskId)
            .Should().Equal("t5");
    }

    [Fact]
    public async Task Environment_reaches_the_wire_and_is_omitted_when_unset()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"uuid\":\"u1\",\"order_id\":\"o1\",\"status\":\"pending\"}"));
        var client = NewClient(handler);

        await client.PayIns.CreateAsync(new CreatePayInRequest
        {
            OrderId = "o1",
            UserId = "u",
            Mode = PayInMode.Fiat,
            AmountFiat = "10",
            Currency = "USD",
            Environment = PayInEnvironment.Testnet,
        });
        JsonDocument.Parse(handler.CapturedBodies[0]).RootElement
            .GetProperty("environment").GetString().Should().Be("testnet");

        var handler2 = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"uuid\":\"u2\",\"order_id\":\"o2\",\"status\":\"pending\"}"));
        var client2 = NewClient(handler2);
        await client2.PayIns.CreateAsync(new CreatePayInRequest
        {
            OrderId = "o2",
            UserId = "u",
            Mode = PayInMode.Fiat,
            AmountFiat = "10",
            Currency = "USD",
        });
        // Unset must stay off the wire: an empty string is a value the platform has to
        // reject, not the "use the project default" the caller meant.
        JsonDocument.Parse(handler2.CapturedBodies[0]).RootElement
            .TryGetProperty("environment", out _).Should().BeFalse();
    }

    private static HttpResponseMessage Resp(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

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

        /// <summary>Bodies read at send time — the transport disposes the request afterwards.</summary>
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
