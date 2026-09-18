namespace CryptoChief.Processing.Models;

public static class TxType
{
    public const string Native   = "native";
    public const string Token    = "token";
    public const string Contract = "contract";
}

public static class TxStatus
{
    public const string Signed       = "signed";
    public const string Broadcasting = "broadcasting";
    public const string Broadcasted  = "broadcasted";
    public const string Confirmed    = "confirmed";
    public const string Failed       = "failed";
    public const string Expired      = "expired";
}

public sealed record SolanaAccount
{
    public required string Pubkey { get; init; }
    public bool IsSigner { get; init; }
    public bool IsWritable { get; init; }
}

public sealed record ContractCall
{
    public required string To { get; init; }
    public string? Value { get; init; }
    public string Data { get; init; } = string.Empty;
    public IReadOnlyList<SolanaAccount>? Accounts { get; init; }
    public bool? Bounce { get; init; }
}

public sealed record SignTransactionRequest
{
    public required string Network { get; init; }
    public required string FromAddress { get; init; }
    public required string Type { get; init; }

    public string? ToAddress { get; init; }

    /// <summary>Base units (e.g. wei) — NOT human amount.</summary>
    public string? Value { get; init; }

    public string? Contract { get; init; }
    public IReadOnlyList<ContractCall>? Calls { get; init; }
    public string? UrlCallback { get; init; }
}

public sealed record SignTransactionResponse
{
    public string Uuid { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string SignedTxHex { get; init; } = string.Empty;
    public string TxHash { get; init; } = string.Empty;
    public string ExpiresAt { get; init; } = string.Empty;
    public string ChainFamily { get; init; } = string.Empty;
    public string? Network { get; init; }
}

public sealed record EstimateTransactionRequest
{
    public required string Network { get; init; }
    public required string FromAddress { get; init; }
    public required string Type { get; init; }

    public string? ToAddress { get; init; }

    /// <summary>Base units (e.g. wei) — NOT human amount.</summary>
    public string? Value { get; init; }

    /// <summary>Token contract address; <see cref="TxType.Token"/> only.</summary>
    public string? Contract { get; init; }
}

/// <summary>
/// A fee quote for a transaction that is neither signed nor broadcast and leaves no record.
/// </summary>
public sealed record EstimateTransactionResponse
{
    public string Network { get; init; } = string.Empty;
    public string ChainFamily { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string FromAddress { get; init; } = string.Empty;
    public string ToAddress { get; init; } = string.Empty;

    /// <summary>Network fee in the chain's native coin, human units.</summary>
    public string EstimatedFee { get; init; } = string.Empty;

    /// <summary><see cref="EstimatedFee"/> in USD; empty when the rate is unavailable.</summary>
    public string EstimatedFeeFiat { get; init; } = string.Empty;

    /// <summary>
    /// Total native coin the from-wallet must hold for the transfer to go through:
    /// fee + value for a native transfer, fee alone for a token one.
    /// </summary>
    public string Required { get; init; } = string.Empty;

    /// <summary><see cref="Required"/> in USD; empty when the rate is unavailable.</summary>
    public string RequiredFiat { get; init; } = string.Empty;

    /// <summary>
    /// TRON only: the fee expected to actually be charged given the from-wallet's current
    /// energy pool (staked, delegated or rented energy is spent before TRX is burnt).
    /// Not a guarantee — the pool can run out before the transaction is broadcast;
    /// <see cref="EstimatedFee"/> stays the gross figure.
    /// </summary>
    public string? FeeExpected { get; init; }

    /// <summary>TRON only: the on-chain fee cap (TRX) written into the transaction.</summary>
    public string? FeeLimit { get; init; }

    /// <summary>TRON only: energy units the transaction needs.</summary>
    public long? Energy { get; init; }

    /// <summary>TRON only: TRX the energy costs when burnt rather than covered by a pool.</summary>
    public string? EnergyFee { get; init; }

    /// <summary>TRON only: TRX the bandwidth costs.</summary>
    public string? BandwidthFee { get; init; }

    /// <summary>
    /// TRON only: TRX for activating a new address — sent on a native transfer to an address
    /// that does not exist on chain yet. <see cref="EnergyFee"/> + <see cref="BandwidthFee"/> +
    /// <see cref="ActivationFee"/> add up to <see cref="EstimatedFee"/>.
    /// </summary>
    public string? ActivationFee { get; init; }
}

public sealed record ExecuteTransactionRequest
{
    public required string Uuid { get; init; }
    public string? SignedTxHex { get; init; }
}

public sealed record TransactionInfo
{
    public string Uuid { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Network { get; init; }
    public string? ChainFamily { get; init; }
    public string FromAddress { get; init; } = string.Empty;
    public string? ToAddress { get; init; }
    public string? Type { get; init; }
    public string? Value { get; init; }
    public string? Coin { get; init; }
    public string? Contract { get; init; }
    public string? TxHash { get; init; }
    public string? SignedTxHex { get; init; }
    public string? ExpiresAt { get; init; }
    public ulong? Nonce { get; init; }
    public string? ActualFee { get; init; }
    public string? ActualFeeFiat { get; init; }

    /// <summary>
    /// Confirmations of the transaction. Grows while it is <see cref="TxStatus.Broadcasted"/>.
    /// Always sent. On <see cref="TxStatus.Confirmed"/>, at least <see cref="RequiredConfirmations"/>.
    /// </summary>
    public int? Confirmations { get; init; }

    /// <summary>
    /// Confirmations the network requires. On reaching it the transaction is
    /// <see cref="TxStatus.Confirmed"/>. Always sent.
    /// </summary>
    public int? RequiredConfirmations { get; init; }

    public string? CreatedAt { get; init; }
    public string? UpdatedAt { get; init; }
    public string? Error { get; init; }

    public bool IsTerminal => Status switch
    {
        TxStatus.Confirmed or TxStatus.Failed or TxStatus.Expired => true,
        _ => false,
    };

    public bool Succeeded => Status == TxStatus.Confirmed;
}

public sealed record TransactionHistoryResponse
{
    public IReadOnlyList<TransactionInfo> Items { get; init; } = Array.Empty<TransactionInfo>();
    public HistoryMeta Meta { get; init; } = new();
}
