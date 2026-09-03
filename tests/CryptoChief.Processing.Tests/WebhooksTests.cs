using System.Net;
using System.Text;
using CryptoChief.Processing;
using CryptoChief.Processing.Errors;
using CryptoChief.Processing.Models;
using CryptoChief.Processing.Webhooks;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

/// <summary>
/// The outbound-webhook surface: reading a delivery with its attempts, the three routes, and
/// that a refusal is a CryptoChiefApiException with the machine code rather than a Queued=false result.
/// </summary>
public class WebhooksTests
{
    private const string DeliveryUuid = "44444444-4444-4444-8444-444444444444";

    private const string DeliveryBody =
        "{\"uuid\":\"" + DeliveryUuid + "\",\"event_type\":\"invoice.paid\",\"reference\":\"order-1\","
        + "\"target_url\":\"https://m.example/hook\",\"status\":\"failed\",\"attempts\":3,\"max_attempts\":10,\"resend_count\":1,"
        + "\"last_error\":\"HTTP 500\",\"last_http_status\":500,\"next_attempt_at\":null,\"delivered_at\":null,"
        + "\"created_at\":\"2026-09-03T10:00:00Z\",\"superseded_by\":null,"
        + "\"attempt_history\":["
        + "{\"attempt\":3,\"http_status\":500,\"error\":\"HTTP 500\",\"duration_ms\":120,\"target_url\":\"https://m.example/hook\","
        + "\"created_at\":\"2026-09-03T10:02:00Z\",\"response_body\":\"<html>oops\",\"response_content_type\":\"text/html\",\"response_truncated\":true},"
        + "{\"attempt\":2,\"http_status\":null,\"error\":\"dial tcp: connection refused\",\"duration_ms\":null,\"target_url\":\"https://m.example/hook\","
        + "\"created_at\":null,\"response_body\":null,\"response_content_type\":null,\"response_truncated\":false}],"
        + "\"payload\":{\"body\":\"{\\\"event\\\":\\\"invoice.paid\\\"}\",\"bytes\":24,\"truncated\":false}}";

    [Fact]
    public async Task Info_reads_attempts_and_keeps_null_as_not_recorded()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, DeliveryBody));
        var client = NewClient(handler);

        var d = await client.Webhooks.InfoAsync(DeliveryUuid);

        handler.Captured.Single().RequestUri!.AbsolutePath.Should().Be("/v1/webhooks/info");
        handler.CapturedBodies.Single().Should().Be("{\"uuid\":\"" + DeliveryUuid + "\"}");

        d.Status.Should().Be(WebhookDeliveryStatus.Failed);
        d.LastHttpStatus.Should().Be(500);
        d.DeliveredAt.Should().BeNull();
        d.SupersededBy.Should().BeNull();
        d.AttemptHistory.Should().HaveCount(2);
        var answered = d.AttemptHistory[0];
        var silent = d.AttemptHistory[1];
        answered.ResponseTruncated.Should().BeTrue();
        answered.ResponseContentType.Should().Be("text/html");
        // An attempt nothing answered has no status and no body — only the error.
        silent.HttpStatus.Should().BeNull();
        silent.ResponseBody.Should().BeNull();
        silent.CreatedAt.Should().BeNull();
        silent.Error.Should().Contain("connection refused");
        d.Payload.Bytes.Should().Be(24);
    }

    [Fact]
    public async Task ResendStaticDeposit_is_addressed_by_the_deposit_uuid()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"uuid\":\"dep-1\",\"deliveries\":[{\"uuid\":\"d-1\",\"event_type\":\"static_deposit.paid\",\"reference\":\"dep-1\","
            + "\"status\":\"delivered\",\"queued\":true,\"attempts\":2,\"resend_count\":1}],\"queued\":1,\"total\":1}"));
        var client = NewClient(handler);

        var result = await client.Webhooks.ResendStaticDepositAsync("dep-1");

        handler.Captured.Single().RequestUri!.AbsolutePath.Should().Be("/v1/static-deposits/resend");
        handler.CapturedBodies.Single().Should().Be("{\"uuid\":\"dep-1\"}");
        result.Queued.Should().Be(1);
        result.Deliveries.Single().Queued.Should().BeTrue();
        result.Deliveries.Single().ResendCount.Should().Be(1);
    }

    [Fact]
    public async Task Refusal_is_an_ApiException_with_the_code()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.Conflict,
            "{\"ok\":false,\"error\":\"DELIVERY_SUPERSEDED\",\"msg\":\"not the latest; resend invoice.paid instead\",\"superseded_by\":\"invoice.paid\"}"));
        var client = NewClient(handler);

        var act = () => client.Webhooks.ResendAsync(DeliveryUuid);

        var ex = (await act.Should().ThrowAsync<CryptoChiefApiException>()).Which;
        ex.Code.Should().Be(ErrorCodes.DeliverySuperseded);
        ex.HttpStatus.Should().Be(HttpStatusCode.Conflict);
        ex.Message.Should().Contain("invoice.paid");
    }

    [Fact]
    public void Delivery_header_name()
    {
        WebhookVerifier.DeliveryHeader.Should().Be("X-Webhook-Delivery");
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
            MaxRetries        = 0,
            InitialRetryDelay = TimeSpan.FromMilliseconds(1),
            MaxRetryDelay     = TimeSpan.FromMilliseconds(5),
        }, new HttpClient(handler), null);

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> reply)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Captured { get; } = new();
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
