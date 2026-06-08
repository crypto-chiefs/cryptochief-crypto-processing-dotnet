namespace CryptoChief.Processing.Models;

public sealed record Withdrawal
{
    public string Uuid { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Network { get; init; }
    public string? Coin { get; init; }
    public string? Contract { get; init; }
    public string Amount { get; init; } = string.Empty;
    public string? AmountFiat { get; init; }
    public string? FromAddress { get; init; }
    public string? ToAddress { get; init; }
    public string? TxHash { get; init; }
    public string? CreatedAt { get; init; }
    public string? UpdatedAt { get; init; }
    public string? ConfirmedAt { get; init; }
    public string? Error { get; init; }
}

public sealed record WithdrawalHistoryResponse
{
    public IReadOnlyList<Withdrawal> Items { get; init; } = Array.Empty<Withdrawal>();
    public HistoryMeta Meta { get; init; } = new();
}
