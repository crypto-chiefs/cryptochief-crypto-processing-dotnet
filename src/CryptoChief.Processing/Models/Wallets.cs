namespace CryptoChief.Processing.Models;

public static class WalletType
{
    public const string Master  = "master";
    public const string Transit = "transit";
    public const string Static  = "static";
}

public sealed record GenerateWalletRequest
{
    public required string WalletType { get; init; }
    public required string ChainFamily { get; init; }
    public string? MasterWalletAddress { get; init; }
    public string? CallbackUrl { get; init; }
}

public sealed record WalletCoinBalance
{
    public string Address { get; init; } = string.Empty;
    public string Chain { get; init; } = string.Empty;
    public string Coin { get; init; } = string.Empty;
    public string? Contract { get; init; }
    public int Decimals { get; init; }
    public string Value { get; init; } = string.Empty;
    public string HumanValue { get; init; } = string.Empty;
    public string? AmountUsd { get; init; }
    public long? Timestamp { get; init; }
}

public sealed record Wallet
{
    public string Address { get; init; } = string.Empty;
    public string ChainFamily { get; init; } = string.Empty;
    public string? Type { get; init; }
    public string? WalletType { get; init; }
    public bool Frozen { get; init; }
    public string? MasterWalletAddress { get; init; }
    public string? CallbackUrl { get; init; }

    /// <summary>Base64 RSA-OAEP/SHA-256 ciphertext. Decrypt via <c>WalletsService.DecryptPrivateKey</c>.</summary>
    public string? PrivateKeyEncrypted { get; init; }

    public string? CreatedAt { get; init; }

    public IReadOnlyList<WalletCoinBalance>? Coins { get; init; }
    public string? TotalBalanceUsd { get; init; }
}

public sealed record ListWalletsResponse
{
    public IReadOnlyList<Wallet> Items { get; init; } = Array.Empty<Wallet>();
}
