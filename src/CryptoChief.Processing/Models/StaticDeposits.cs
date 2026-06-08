namespace CryptoChief.Processing.Models;

public static class StaticDepositStatus
{
    public const string InMempool    = "in_mempool";
    public const string ConfirmCheck = "confirm_check";
    public const string Paid         = "paid";
    public const string Dropped      = "dropped";
    public const string Reorged      = "reorged";
}

public sealed record StaticDeposit
{
    public string Uuid { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Network { get; init; } = string.Empty;
    public string? ChainFamily { get; init; }
    public string Coin { get; init; } = string.Empty;
    public string? Contract { get; init; }
    public int? Decimals { get; init; }
    public string ToAddress { get; init; } = string.Empty;
    public string? FromAddress { get; init; }
    public string? TxHash { get; init; }
    public long? BlockNumber { get; init; }
    public string Amount { get; init; } = string.Empty;
    public string? AmountFiat { get; init; }
    public int? Confirmations { get; init; }
    public int? RequiredConfirmations { get; init; }
    public bool FoundInMempool { get; init; }
    public string? LogType { get; init; }
    public string? CreatedAt { get; init; }
    public string? UpdatedAt { get; init; }
    public string? ConfirmedAt { get; init; }
    public string? PaidAt { get; init; }
}

public sealed record StaticDepositHistoryQuery
{
    public string? Address { get; init; }
    public string? Status { get; init; }
    public string? Coin { get; init; }
    public string? Network { get; init; }
    public string? DateFrom { get; init; }
    public string? DateTo { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}

public sealed record StaticDepositHistoryResponse
{
    public IReadOnlyList<StaticDeposit> Items { get; init; } = Array.Empty<StaticDeposit>();
    public HistoryMeta Meta { get; init; } = new();
}
