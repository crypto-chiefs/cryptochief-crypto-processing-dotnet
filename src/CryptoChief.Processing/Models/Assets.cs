namespace CryptoChief.Processing.Models;

public sealed record Asset
{
    public string? Network { get; init; }
    public string? Coin { get; init; }
}

public sealed record AssetsPolicy
{
    public IReadOnlyList<Asset>? Allow { get; init; }
    public IReadOnlyList<Asset>? Exclude { get; init; }
}
