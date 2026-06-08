namespace CryptoChief.Processing.Models;

public sealed record AvailableContract
{
    public string Network { get; init; } = string.Empty;
    public string Coin { get; init; } = string.Empty;
    public string? Contract { get; init; }
    public string? Type { get; init; }
    public int Decimals { get; init; }
}

public sealed record AvailableContractsResponse
{
    public IReadOnlyList<AvailableContract> Items { get; init; } = Array.Empty<AvailableContract>();
}

public sealed record WalletBalanceRow
{
    public string? Contract { get; init; }
    public string Address { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string HumanValue { get; init; } = string.Empty;
    public int Decimals { get; init; }
}

public sealed record TxStatusRow
{
    public int Confirmations { get; init; }
    public string? Fee { get; init; }
    public string? HumanFee { get; init; }
    public long? BlockNumber { get; init; }
    public string? Status { get; init; }
}
