namespace CryptoChief.Processing.Models;

public static class SweepMode
{
    public const string Auto  = "auto";
    public const string Force = "force";
}

public sealed record SweepHistoryQuery
{
    public string? Mode { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}

public sealed record SweepWalletHistoryQuery
{
    public required string Address { get; init; }
    public string? Mode { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}

public sealed record Sweep
{
    public string TaskId { get; init; } = string.Empty;
    public string? SweepTxHash { get; init; }
    public string Status { get; init; } = string.Empty;
    public string WalletAddress { get; init; } = string.Empty;
    public string Chain { get; init; } = string.Empty;
    public string? ChainFamily { get; init; }
    public string? AssetSymbol { get; init; }
    public string? AssetType { get; init; }
    public string? AmountHuman { get; init; }
    public string? GasFeeHuman { get; init; }
    public string? GasFeeFiat { get; init; }
    public string? ServiceFeeFiat { get; init; }
    public string? CreatedAt { get; init; }
    public string? UpdatedAt { get; init; }
}

public sealed record SweepHistoryResponse
{
    public IReadOnlyList<Sweep> Items { get; init; } = Array.Empty<Sweep>();
    public HistoryMeta Meta { get; init; } = new();
}

public sealed record ForceSweepResponse
{
    public string Status { get; init; } = string.Empty;
}
