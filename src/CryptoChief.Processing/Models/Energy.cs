namespace CryptoChief.Processing.Models;

public static class EnergyOrderStatus
{
    /// <summary>The energy is delegated to the receive address. Final.</summary>
    public const string Delivered  = "delivered";
    /// <summary>The order did not happen and nothing was charged; <see cref="EnergyOrder.Error"/> says why. Final.</summary>
    public const string Refused    = "refused";
    /// <summary>The supplier's answer never arrived — the energy may already be delegated. Do NOT retry; follow the order.</summary>
    public const string Unresolved = "unresolved";
    /// <summary>The order is placed and waiting for a supplier to take it.</summary>
    public const string Placed     = "placed";
    /// <summary>A supplier reserved the order; delegation is in progress.</summary>
    public const string Reserved   = "reserved";
    /// <summary>The charge was returned to the credits balance. Final.</summary>
    public const string Refunded   = "refunded";
}

/// <summary>
/// A price request for renting TRON energy. <see cref="Energy"/> and <see cref="DurationSec"/>
/// left null take the platform defaults.
/// </summary>
public sealed record EnergyQuoteRequest
{
    /// <summary>The address the energy is delegated to — the sender of the transfer it pays for.</summary>
    public required string ReceiveAddress { get; init; }

    /// <summary>Energy units to rent.</summary>
    public long? Energy { get; init; }

    /// <summary>Rental duration, seconds.</summary>
    public int? DurationSec { get; init; }
}

/// <summary>
/// A priced energy rental offer. Billing-exempt — quoting never consumes a paid call.
/// <c>Burn*</c> fields price the same transfer paid by burning TRX, so
/// <c>Saving*</c> is what the rent saves against it.
/// </summary>
/// <remarks>
/// The USD and credits figures (<see cref="PriceUsd"/>, <see cref="Credits"/>,
/// <see cref="TrxUsd"/>, <see cref="BurnPriceUsd"/>, <see cref="BurnPriceCredits"/>,
/// <see cref="SavingUsd"/>, <see cref="SavingCredits"/>) are null when no TRX rate is
/// available — the server omits them rather than sending a zero that would read as
/// "free".
/// </remarks>
public sealed record EnergyQuote
{
    /// <summary>Quote reference; pass it as <see cref="EnergyRentRequest.QuoteRef"/> to rent at this price.</summary>
    public string Ref { get; init; } = string.Empty;

    public string ReceiveAddress { get; init; } = string.Empty;
    public long Energy { get; init; }
    public int DurationSec { get; init; }

    /// <summary>Rent price in sun (1 TRX = 1,000,000 sun).</summary>
    public long PriceSun { get; init; }

    /// <summary>Rent price in TRX, human units.</summary>
    public string PriceTrx { get; init; } = string.Empty;

    /// <summary><see cref="PriceTrx"/> in USD; null when no TRX rate is available.</summary>
    public string? PriceUsd { get; init; }

    /// <summary>Rent price in credits (10,000,000 credits = 1 USD) — what the project balance is
    /// charged; null when no TRX rate is available.</summary>
    public long? Credits { get; init; }

    /// <summary>TRX/USD rate the USD figures are converted at; null when no rate is available.</summary>
    public string? TrxUsd { get; init; }

    /// <summary>On-chain state of the receive address, e.g. whether it is already activated.</summary>
    public string RecipientState { get; init; } = string.Empty;

    /// <summary>Cost of the same transfer paid by burning TRX, in sun.</summary>
    public long BurnPriceSun { get; init; }

    /// <summary>Cost of burning, in TRX.</summary>
    public string BurnPriceTrx { get; init; } = string.Empty;

    /// <summary>Cost of burning, in USD; null when no TRX rate is available.</summary>
    public string? BurnPriceUsd { get; init; }

    /// <summary>Cost of burning converted to credits at the same rate; null when no TRX rate is available.</summary>
    public long? BurnPriceCredits { get; init; }

    /// <summary>What the rent saves against burning, in TRX.</summary>
    public string SavingTrx { get; init; } = string.Empty;

    /// <summary>What the rent saves, in USD; null when no TRX rate is available.</summary>
    public string? SavingUsd { get; init; }

    /// <summary>What the rent saves, in credits; null when no TRX rate is available.</summary>
    public long? SavingCredits { get; init; }

    /// <summary>RFC 3339 timestamp the quote stops being acceptable at.</summary>
    public string ExpiresAt { get; init; } = string.Empty;

    /// <summary>Seconds from the quote to <see cref="ExpiresAt"/>.</summary>
    public int ExpiresInSec { get; init; }
}

/// <summary>
/// An energy rental order. The <c>Idempotency-Key</c> header of the rent call is required and
/// deduplicates retries: re-sending it returns this same order.
/// </summary>
public sealed record EnergyRentRequest
{
    /// <summary>The address the energy is delegated to — the sender of the transfer it pays for.
    /// Required unless <see cref="QuoteRef"/> is given; with a quote the quoted terms win.</summary>
    public string? ReceiveAddress { get; init; }

    /// <summary>Energy units to rent; null takes the platform default.</summary>
    public long? Energy { get; init; }

    /// <summary>Rental duration, seconds; null takes the platform default.</summary>
    public int? DurationSec { get; init; }

    /// <summary><see cref="EnergyQuote.Ref"/> of a quote to rent at its price.</summary>
    public string? QuoteRef { get; init; }
}

/// <summary>
/// An energy rental order. <see cref="PriceUsd"/>, <see cref="Credits"/> and
/// <see cref="TrxUsd"/> are absent when nothing was charged — a
/// <see cref="EnergyOrderStatus.Refused"/> order.
/// </summary>
public sealed record EnergyOrder
{
    /// <summary>The order's numeric id (a JSON number on the wire).</summary>
    public long Id { get; init; }

    /// <summary>The <c>Idempotency-Key</c> the rent was placed with.</summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>One of <see cref="EnergyOrderStatus"/>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>The address the energy is delegated to — the sender of the transfer it pays for.</summary>
    public string ReceiveAddress { get; init; } = string.Empty;

    public long Energy { get; init; }
    public int DurationSec { get; init; }

    /// <summary>Rent price in sun (1 TRX = 1,000,000 sun).</summary>
    public long PriceSun { get; init; }

    /// <summary>Rent price in TRX, human units.</summary>
    public string PriceTrx { get; init; } = string.Empty;

    /// <summary><see cref="PriceTrx"/> in USD; null when nothing was charged.</summary>
    public string? PriceUsd { get; init; }

    /// <summary>Credits charged to the project balance; null when nothing was charged.</summary>
    public long? Credits { get; init; }

    /// <summary>TRX/USD rate the USD figures are converted at; null when nothing was charged.</summary>
    public string? TrxUsd { get; init; }

    /// <summary>Energy units actually delegated to <see cref="ReceiveAddress"/>; null until delivered.</summary>
    public long? DeliveredEnergy { get; init; }

    /// <summary>Whether the order is settled on the billing side.</summary>
    public bool Settled { get; init; }

    /// <summary>
    /// Set on a 409 <see cref="EnergyOrderStatus.Unresolved"/> answer: the platform cannot tell
    /// whether the delegation happened. Do NOT retry such an order — read it with
    /// OrderAsync instead.
    /// </summary>
    public bool NeedsAttention { get; init; }

    /// <summary>Why the order was refused, when it was.</summary>
    public string? Error { get; init; }

    /// <summary>Machine-readable refusal code (<c>error_code</c> on the wire), set on orders that
    /// arrived with a non-2xx status; null on a delivered order.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>RFC 3339 timestamp the order was placed at.</summary>
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>RFC 3339 timestamp the energy was delegated at; null until delivered.</summary>
    public string? DeliveredAt { get; init; }
}
