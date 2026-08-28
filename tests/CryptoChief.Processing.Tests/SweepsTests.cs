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
          "effective": {"type_work":"threshold","threshold_amount_usd":"250","fee_mode":"mix","source":"wallet"},
          "override": {"network_code":"","type_work":"threshold","threshold_amount_usd":"250","fee_mode":null,"source":"merchant","locked":false},
          "project_default": {"type_work":"momentum","fee_mode":"client"}
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
