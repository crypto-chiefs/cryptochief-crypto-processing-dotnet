namespace CryptoChief.Processing.Models;

public static class NativeOrderStatus
{
    public const string Delivered  = "delivered";
    public const string Refused    = "refused";
    public const string Unresolved = "unresolved";
}

/// <summary>
/// A price request for buying native coin (TRX, ETH, BNB, SOL, TON, ...) from the
/// platform's liquidity. <see cref="Amount"/> is in human units, e.g. "0.05".
/// </summary>
public sealed record NativeQuoteRequest
{
    /// <summary>The network the coin is sent on, e.g. "TRON".</summary>
    public required string Network { get; init; }

    /// <summary>The address the coin is sent to — any address; the purchase is billed to the project.</summary>
    public required string ReceiveAddress { get; init; }

    /// <summary>Amount of native coin to buy, human units as a string.</summary>
    public required string Amount { get; init; }
}

/// <summary>
/// A priced native-coin purchase offer. Billing-exempt — quoting never consumes a paid call.
/// The price covers the coins at the market rate and the fee of the platform's own transfer,
/// converted at <see cref="CoinUsd"/>; <see cref="TotalUsd"/> is the full sale price and
/// <see cref="Credits"/> is exactly what the project balance is charged.
/// </summary>
public sealed record NativeQuote
{
    /// <summary>Quote reference; pass it as <see cref="NativeBuyRequest.QuoteRef"/> to buy at this price.</summary>
    public string Ref { get; init; } = string.Empty;

    public string Network { get; init; } = string.Empty;
    public string ReceiveAddress { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;

    /// <summary><see cref="Amount"/> converted at <see cref="CoinUsd"/>, in USD.</summary>
    public string CoinPriceUsd { get; init; } = string.Empty;

    /// <summary>Fee of the platform's transfer that delivers the coin, in native coin.</summary>
    public string TransferFee { get; init; } = string.Empty;

    /// <summary><see cref="TransferFee"/> in USD.</summary>
    public string TransferFeeUsd { get; init; } = string.Empty;

    /// <summary><see cref="CoinPriceUsd"/> + <see cref="TransferFeeUsd"/> — the coin cost and the transfer fee, in USD.</summary>
    public string SubtotalUsd { get; init; } = string.Empty;

    /// <summary>Final sale price in USD — what the purchase costs in full.</summary>
    public string TotalUsd { get; init; } = string.Empty;

    /// <summary>Total price in credits — what the project balance is charged.</summary>
    public long Credits { get; init; }

    /// <summary>Coin/USD rate the USD figures are converted at.</summary>
    public string CoinUsd { get; init; } = string.Empty;

    /// <summary>RFC 3339 timestamp the quote stops being acceptable at.</summary>
    public string ExpiresAt { get; init; } = string.Empty;

    /// <summary>Seconds from the quote to <see cref="ExpiresAt"/> — about 90; a quote is single-use.</summary>
    public int ExpiresInSec { get; init; }
}

/// <summary>
/// A native-coin purchase: either the coin parameters — <see cref="Network"/>,
/// <see cref="ReceiveAddress"/> and <see cref="Amount"/> — or <see cref="QuoteRef"/> of a
/// quote to buy at its price.
/// </summary>
public sealed record NativeBuyRequest
{
    /// <summary>The network the coin is sent on, e.g. "TRON".</summary>
    public string? Network { get; init; }

    /// <summary>The address the coin is sent to — any address; the purchase is billed to the project.</summary>
    public string? ReceiveAddress { get; init; }

    /// <summary>Amount of native coin to buy, human units as a string.</summary>
    public string? Amount { get; init; }

    /// <summary><see cref="NativeQuote.Ref"/> of a quote to buy at its price.</summary>
    public string? QuoteRef { get; init; }
}

/// <summary>
/// A native-coin purchase order. On a <see cref="NativeOrderStatus.Refused"/> order only
/// <see cref="TxHash"/>, <see cref="TotalUsd"/> and <see cref="Credits"/> are absent (null);
/// the remaining price fields arrive as <c>""</c> / <c>"0.00"</c> rather than being omitted.
/// </summary>
public sealed record NativeOrder
{
    public long Id { get; init; }

    /// <summary>The <c>Idempotency-Key</c> the purchase was placed with.</summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>One of <see cref="NativeOrderStatus"/>.</summary>
    public string Status { get; init; } = string.Empty;

    public string Network { get; init; } = string.Empty;
    public string ReceiveAddress { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;

    /// <summary>Hash of the transaction that delivered the coin; null when nothing was sent.</summary>
    public string? TxHash { get; init; }

    /// <summary>Fee of the platform's transfer, in native coin; <c>""</c> on a refused order.</summary>
    public string? TransferFee { get; init; }

    /// <summary><see cref="TransferFee"/> in USD; <c>"0.00"</c> on a refused order.</summary>
    public string? TransferFeeUsd { get; init; }

    /// <summary><see cref="Amount"/> converted at <see cref="CoinUsd"/>, in USD; <c>"0.00"</c> on a refused order.</summary>
    public string? CoinPriceUsd { get; init; }

    /// <summary>Final sale price in USD; null when nothing was charged.</summary>
    public string? TotalUsd { get; init; }

    /// <summary>Credits charged to the project balance; null when nothing was charged.</summary>
    public long? Credits { get; init; }

    /// <summary>Coin/USD rate the USD figures are converted at; <c>""</c> on a refused order.</summary>
    public string? CoinUsd { get; init; }

    /// <summary>Whether the order is settled on the billing side.</summary>
    public bool Settled { get; init; }

    /// <summary>
    /// Set on a 409 <see cref="NativeOrderStatus.Unresolved"/> answer: the platform cannot tell
    /// whether the transfer happened. Do NOT retry such an order — read it with
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

    /// <summary>RFC 3339 timestamp the coin was sent at; null until delivered.</summary>
    public string? DeliveredAt { get; init; }
}
