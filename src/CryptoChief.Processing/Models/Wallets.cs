namespace CryptoChief.Processing.Models;

public static class WalletType
{
    public const string Master  = "master";
    public const string Transit = "transit";
    public const string Static  = "static";
}

public sealed record GenerateWalletRequest
{
    /// <summary>One of <c>master</c>, <c>transit</c>, <c>static</c>.</summary>
    public required string WalletType { get; init; }

    public required string ChainFamily { get; init; }

    public string? MasterWalletAddress { get; init; }

    /// <summary>Deposit webhook. Static wallets only.</summary>
    public string? CallbackUrl { get; init; }

    /// <summary>
    /// Human-readable name for the wallet, up to 255 characters. Applies to every wallet
    /// type — master, transit and static alike — not only static ones.
    /// </summary>
    /// <remarks>
    /// Null leaves the wallet unnamed and keeps the field off the wire; an empty string is
    /// a name of no characters, which the platform has to reject, not the "no name" the
    /// caller meant.
    /// </remarks>
    public string? Label { get; init; }
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

    /// <summary>
    /// The master this wallet sweeps to, or null when it has none — a master wallet itself,
    /// or a wallet not yet bound. Change it with <c>WalletsService.RebindMasterAsync</c>.
    /// </summary>
    /// <remarks>
    /// The API always sends the key and uses null for "none", never an empty string and
    /// never an absent key, so null here is an answer rather than a gap in the response.
    /// </remarks>
    public string? MasterWalletAddress { get; init; }

    /// <summary>
    /// The deposit webhook, or null when none is set. Always null on a transit wallet —
    /// only static wallets carry one. Change it with
    /// <c>WalletsService.SetCallbackUrlAsync</c>.
    /// </summary>
    /// <remarks>
    /// Null is an answer, not a gap: the API always sends the key and uses null for "none",
    /// never an empty string and never an absent key.
    /// </remarks>
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
