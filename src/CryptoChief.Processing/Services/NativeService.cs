using System.Text.Json;
using CryptoChief.Processing.Errors;
using CryptoChief.Processing.Internal;
using CryptoChief.Processing.Models;

namespace CryptoChief.Processing.Services;

public sealed class NativeService
{
    private readonly CryptoChiefClient _client;
    internal NativeService(CryptoChiefClient client) => _client = client;

    /// <summary>
    /// Price a native-coin purchase: the coins at the market rate plus the platform's transfer
    /// fee, charged to the project credits balance. Billing-exempt — quoting never consumes a
    /// paid call.
    /// </summary>
    public Task<NativeQuote> QuoteAsync(
        NativeQuoteRequest request, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<NativeQuote>(
            "/v1/native/quote", request, cancellationToken);

    /// <summary>
    /// Buy native coin for <see cref="NativeBuyRequest.ReceiveAddress"/> — any address; the
    /// purchase is billed to the project credits balance. Synchronous: by the time it answers,
    /// the coin is either sent or the refusal reason is known.
    /// </summary>
    /// <remarks>
    /// The <c>Idempotency-Key</c> header is required (the server answers 400 without it), so
    /// call this on a keyed client:
    /// <code>
    /// await client.WithIdempotencyKey("native-order-42").Native.BuyAsync(request);
    /// </code>
    /// A retry with the same key returns the same order rather than buying twice.
    /// <para>The answer is always a <see cref="NativeOrder"/>:
    /// <c>delivered</c> (HTTP 200) — the coin is sent and <see cref="NativeOrder.TxHash"/> is the
    /// transfer; <c>refused</c> (HTTP 502, or 402 when the credits balance is short) — nothing
    /// was charged and <see cref="NativeOrder.Error"/> says why, retrying with a NEW key
    /// re-attempts the purchase; <c>unresolved</c> (HTTP 409,
    /// <see cref="NativeOrder.NeedsAttention"/>) — the coin may already be sent, do NOT retry,
    /// follow the order with <see cref="OrderAsync"/>.</para>
    /// <para>Failures with no order to report (an error envelope — 409 <c>QUOTE_EXPIRED</c> /
    /// <c>QUOTE_ALREADY_USED</c> means quote again —, a gateway page) throw a
    /// <see cref="CryptoChiefApiException"/> as usual.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">The client carries no <c>Idempotency-Key</c>.</exception>
    public async Task<NativeOrder> BuyAsync(
        NativeBuyRequest request, CancellationToken cancellationToken = default)
    {
        if (_client.Transport.IdempotencyKey.Length == 0)
            throw new ArgumentException(
                "cryptochief: native buy requires an Idempotency-Key — call it on "
                + "client.WithIdempotencyKey(key)", nameof(request));
        try
        {
            return await _client.Transport.SendAsync<NativeOrder>(
                "/v1/native/buy", request, cancellationToken).ConfigureAwait(false);
        }
        catch (CryptoChiefApiException err)
        {
            var order = OrderFromError(err);
            if (order is not null)
                return order;
            throw;
        }
    }

    /// <summary>Read a native-coin purchase order by the idempotency key it was placed with.</summary>
    public Task<NativeOrder> OrderAsync(string key, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<NativeOrder>(
            "/v1/native/order", new { key }, cancellationToken);

    // A refused order answers 502 (or 402 when the credits balance is short) and an unresolved
    // one 409, each with the order itself as the body — a business outcome, not a transport
    // failure. Recover it; anything else (an error envelope, a gateway page) is rethrown.
    private static NativeOrder? OrderFromError(CryptoChiefApiException err)
    {
        if ((int)err.HttpStatus is not (402 or 409 or 502) || err.RawBody is not { Length: > 0 } raw)
            return null;
        try
        {
            using (var doc = JsonDocument.Parse(raw))
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("id", out _)
                    || !root.TryGetProperty("status", out _))
                    return null;
            }
            return JsonSerializer.Deserialize<NativeOrder>(raw, JsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
