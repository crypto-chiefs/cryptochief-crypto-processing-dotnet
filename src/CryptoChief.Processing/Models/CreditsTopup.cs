namespace CryptoChief.Processing.Models;

public sealed record CreditsTopupRequest
{
    /// <summary>Positive decimal amount to top up, USD-pegged, max 100000. E.g. <c>"25.00"</c>.</summary>
    public required string Amount { get; init; }

    /// <summary><c>"USDT"</c> or <c>"USDC"</c>.</summary>
    public required string Currency { get; init; }

    /// <summary>Optional absolute http(s) URL the browser is redirected to after payment.</summary>
    public string? UrlSuccess { get; init; }

    /// <summary>Optional absolute http(s) URL the browser is redirected to on payment error.</summary>
    public string? UrlError { get; init; }
}

public sealed record CreditsTopup
{
    /// <summary>Billing invoice id. Wire field <c>invoice_id</c>.</summary>
    public long InvoiceId { get; init; }

    /// <summary>Hosted payment page URL (QR code, network selection, live status).</summary>
    public string PaymentLink { get; init; } = string.Empty;

    public string Amount { get; init; } = string.Empty;

    public string Currency { get; init; } = string.Empty;

    /// <summary><c>"pending"</c> on creation.</summary>
    public string Status { get; init; } = string.Empty;

    public string? OrderUuid { get; init; }

    /// <summary>Unix seconds the payment link expires at, when the server reports it.</summary>
    public long? ExpiredAt { get; init; }
}
