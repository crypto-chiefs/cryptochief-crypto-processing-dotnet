using System.Net;
using System.Text.Json;
using CryptoChief.Processing.Models;
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

        // Canonical body: snake_case keys sorted lexicographically. On this endpoint the
        // search runs over the wallet address, both transaction hashes and the task id.
        const string wire = "{\"mode\":\"auto\",\"search\":\"0x77EDde\",\"status\":\"skipped\"}";
        handler.CapturedBodies.Should().ContainSingle().Which.Should().Be(wire);

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
        handler.CapturedBodies.Should().ContainSingle().Which.Should().Be(wire);
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
        handler.CapturedBodies.Should().ContainSingle().Which.Should().Be("{\"page\":2}");
    }

    [Fact]
    public async Task History_tells_a_broadcast_sweep_from_a_settled_one()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, """
            {"items":[
              {"task_id":"t1","status":"broadcasted","wallet_address":"0xa","chain":"ETH_MAINNET",
               "sweep_confirmations":2,"type_work":"threshold","total_fee_usd":"1.20"},
              {"task_id":"t2","status":"completed","wallet_address":"0xb","chain":"ETH_MAINNET",
               "sweep_confirmations":12,"completed_at":"2026-08-28T10:00:00Z","real_sweep_fee_usd":"0.98"}
            ],"meta":{"total":2,"page":1,"page_size":50}}
            """));
        var client = NewClient(handler);

        var page = await client.Sweeps.HistoryAsync(new SweepHistoryQuery { PageSize = 50 });

        var inFlight = page.Items[0];
        var settled = page.Items[1];
        inFlight.Status.Should().Be(SweepStatus.Broadcasted);
        inFlight.SweepConfirmations.Should().Be(2);
        // Still in flight: there is no settlement moment to report yet.
        inFlight.CompletedAt.Should().BeNull();
        inFlight.TypeWork.Should().Be("threshold");
        inFlight.TotalFeeUsd.Should().Be("1.20");
        settled.Status.Should().Be(SweepStatus.Completed);
        settled.CompletedAt.Should().Be("2026-08-28T10:00:00Z");
        settled.RealSweepFeeUsd.Should().Be("0.98");
    }

    [Fact]
    public async Task History_stamps_completed_at_on_a_failed_sweep_too()
    {
        // The sweeper stamps completed_at at every terminal outcome, failures included —
        // a failed sweep is not "in flight" either. Reading its presence as settlement
        // books a failure as money received; the confirmation count is what separates them.
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

        // The settlement test: status completed AND confirmations above zero.
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
