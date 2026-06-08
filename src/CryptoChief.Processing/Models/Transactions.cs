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
