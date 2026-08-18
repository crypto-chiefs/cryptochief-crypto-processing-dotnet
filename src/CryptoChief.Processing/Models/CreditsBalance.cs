using System.Text.Json.Serialization;

namespace CryptoChief.Processing.Models;

public sealed record CreditsBalance
{
    /// <summary>Current balance in credits (10,000,000 credits = 1 USD). Wire field <c>credits_balance</c>.</summary>
    [JsonPropertyName("credits_balance")]
    public long Balance { get; init; }

    /// <summary>Pre-formatted USD balance with 2 decimals. Can be negative on postpaid, e.g. <c>"-1.52"</c>.</summary>
    public string UsdBalance { get; init; } = string.Empty;

    public bool IsPostpaid { get; init; }

    /// <summary>Effective debt limit in credits (postpaid only, 0 for prepaid).</summary>
    public long DebtLimitCredits { get; init; }

    /// <summary>Whether gas-paying operations (<c>/v1/transaction/execute</c> etc.) would pass the balance gate.</summary>
    public bool CanExecuteGasOperations { get; init; }

    /// <summary>Minimum credits required for gas-paying operations.</summary>
    public long GasOpsMinCredits { get; init; }

    /// <summary>RFC 3339 timestamp the balance was read at.</summary>
    public string Timestamp { get; init; } = string.Empty;
}
