using System.Text.Json;
using CryptoChief.Processing.Errors;
using CryptoChief.Processing.Internal;
using CryptoChief.Processing.Models;

namespace CryptoChief.Processing.Services;

public sealed class EnergyService
{
    private readonly CryptoChiefClient _client;
    internal EnergyService(CryptoChiefClient client) => _client = client;

    /// <summary>
    /// Price a TRON energy rental, including what the same transfer would cost burnt and what
    /// the rent saves. Billing-exempt — quoting never consumes a paid call.
    /// </summary>
    public Task<EnergyQuote> QuoteAsync(
        EnergyQuoteRequest request, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<EnergyQuote>(
            "/v1/energy/quote", request, cancellationToken);

    /// <summary>
    /// Rent TRON energy for <see cref="EnergyRentRequest.ReceiveAddress"/> — the sender of the
    /// transfer the energy pays for. Synchronous: by the time it answers, the energy is either
    /// delegated or the refusal reason is known. Charges the project credits balance.
    /// </summary>
    /// <remarks>
    /// The <c>Idempotency-Key</c> header is required (the server answers 400 without it), so
    /// call this on a keyed client:
    /// <code>
    /// await client.WithIdempotencyKey("energy-order-42").Energy.RentAsync(request);
    /// </code>
    /// A retry with the same key returns the same order rather than renting twice.
    /// <para>The answer is always an <see cref="EnergyOrder"/>:
    /// <c>delivered</c> (HTTP 200) — the energy is delegated; <c>refused</c> (HTTP 502, or 402
    /// when the credits balance is short) — nothing was charged and <see cref="EnergyOrder.Error"/>
    /// says why, retrying with a NEW key is safe; <c>unresolved</c> (HTTP 409,
    /// <see cref="EnergyOrder.NeedsAttention"/>) — the energy may already be delegated, do NOT
    /// retry, follow the order with <see cref="OrderAsync"/>.</para>
    /// <para>Failures with no order to report (an error envelope, a gateway page) throw a
    /// <see cref="CryptoChiefApiException"/> as usual.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">The client carries no <c>Idempotency-Key</c>.</exception>
    public async Task<EnergyOrder> RentAsync(
        EnergyRentRequest request, CancellationToken cancellationToken = default)
    {
        if (_client.Transport.IdempotencyKey.Length == 0)
            throw new ArgumentException(
                "cryptochief: energy rent requires an Idempotency-Key — call it on "
                + "client.WithIdempotencyKey(key)", nameof(request));
        try
        {
            return await _client.Transport.SendAsync<EnergyOrder>(
                "/v1/energy/rent", request, cancellationToken).ConfigureAwait(false);
        }
        catch (CryptoChiefApiException err)
        {
            var order = OrderFromError(err);
            if (order is not null)
                return order;
            throw;
        }
    }

    /// <summary>Read an energy rental order by the idempotency key it was placed with.</summary>
    public Task<EnergyOrder> OrderAsync(string key, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<EnergyOrder>(
            "/v1/energy/order", new { key }, cancellationToken);

    // A refused order answers 502 (or 402 when the credits balance is short) and an unresolved
    // one 409, each with the order itself as the body — a business outcome, not a transport
    // failure. Recover it; anything else (an error envelope, a gateway page) is rethrown.
    private static EnergyOrder? OrderFromError(CryptoChiefApiException err)
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
            return JsonSerializer.Deserialize<EnergyOrder>(raw, JsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
