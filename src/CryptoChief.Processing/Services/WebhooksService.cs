using CryptoChief.Processing.Models;

namespace CryptoChief.Processing.Services;

/// <summary>
/// Reads and re-fires the platform's OUTBOUND webhooks — the deliveries made to your
/// endpoint. (Verifying INCOMING webhooks is <c>WebhookVerifier</c>.)
/// </summary>
/// <remarks>
/// <para>A delivery is named by the uuid the platform put on it in the
/// <c>X-Webhook-Delivery</c> header (<see cref="Webhooks.WebhookVerifier.DeliveryHeader"/>).
/// It is the same across every attempt and resend of that delivery — the natural
/// idempotency key for your receiver — and it is the only handle there is: the API has no
/// listing of deliveries, and the payload names the order, not the delivery. Keep it when
/// you log an incoming webhook.</para>
/// </remarks>
public sealed class WebhooksService
{
    private readonly CryptoChiefClient _client;
    internal WebhooksService(CryptoChiefClient client) => _client = client;

    /// <summary>
    /// One delivery by the uuid from its <c>X-Webhook-Delivery</c> header. A delivery that
    /// is not this project's is <c>NOT_FOUND</c>, the same as one that does not exist.
    /// </summary>
    public Task<WebhookDelivery> InfoAsync(string deliveryUuid, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<WebhookDelivery>(
            "/v1/webhooks/info", new { uuid = deliveryUuid }, cancellationToken);

    /// <summary>Send one delivery to your endpoint again, right now.</summary>
    /// <remarks>
    /// <para>Refused with an <c>ApiException</c> whose <c>Code</c> is:</para>
    /// <list type="bullet">
    /// <item><c>DELIVERY_SUPERSEDED</c> (409) — a newer event exists for the same object.
    /// Re-sending <c>invoice.in_mempool</c> after <c>invoice.paid</c> would tell your system
    /// the order went backwards, so only the latest event may be resent. Permanent; the newer
    /// event's name is in the message.</item>
    /// <item><c>DELIVERY_IN_FLIGHT</c> (409) — a worker is delivering it right now, or it is
    /// already scheduled for an automatic retry. Try again in a moment.</item>
    /// <item><c>RESEND_TOO_SOON</c> (429) — resent under a minute ago; <c>Retry-After</c> is set.</item>
    /// </list>
    /// <para>A successful manual delivery is billed as <c>/v1/webhook/resend</c>; a refused one is not.</para>
    /// </remarks>
    public Task<WebhookResendResult> ResendAsync(string deliveryUuid, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<WebhookResendResult>(
            "/v1/webhooks/resend", new { uuid = deliveryUuid }, cancellationToken);

    /// <summary>
    /// Re-fire the NEWEST webhook of one static deposit, named by the deposit's own uuid —
    /// for when you have the deposit and not the delivery. Older events of the deposit are
    /// superseded and are not resent.
    /// </summary>
    /// <remarks>
    /// Refused with <c>NO_DELIVERIES</c> (409) when the deposit is yours but no webhook was
    /// ever queued for it: it arrived on a static wallet with no <c>callback_url</c>. The
    /// per-delivery refusals of <see cref="ResendAsync"/> apply as well.
    /// </remarks>
    public Task<StaticDepositResendResult> ResendStaticDepositAsync(string depositUuid, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<StaticDepositResendResult>(
            "/v1/static-deposits/resend", new { uuid = depositUuid }, cancellationToken);
}
