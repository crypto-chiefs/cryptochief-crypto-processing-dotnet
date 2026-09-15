using System.Text.Json.Serialization;

namespace CryptoChief.Processing.Models;

public static class PayoutStatus
{
    /// <summary>Waiting to be processed.</summary>
    public const string Queue           = "queue";

    /// <summary>Not used by payouts.</summary>
    public const string Process         = "process";

    /// <summary>A source wallet is being topped up with native coin for gas.</summary>
    public const string Refueling       = "refueling";

    /// <summary>Gas is in place; the transfer is about to be sent.</summary>
    public const string RefuelConfirmed = "refuel_confirmed";

    /// <summary>The transfer is being signed and sent.</summary>
    public const string Sending         = "sending";

    /// <summary>Handed off for broadcast, hash not known yet (EVM networks).</summary>
    public const string Broadcasting    = "broadcasting";

    /// <summary>In the mempool, not in a block yet (UTXO networks).</summary>
    public const string InMempool       = "in_mempool";

    /// <summary>Sent; waiting for every source to reach <c>PayoutInfo.RequiredConfirmations</c>.</summary>
    public const string ConfirmCheck    = "confirm_check";

    /// <summary>Terminal: every source reached <c>PayoutInfo.RequiredConfirmations</c>.</summary>
    public const string Paid            = "paid";

    public const string Failed          = "failed";
    public const string SystemFail      = "system_fail";
    public const string Expired         = "expired";
    public const string Cancel          = "cancel";
}

public sealed record EstimatePayoutRequest
{
    public required string Network { get; init; }
    public required string Coin { get; init; }
    public required string Amount { get; init; }
    public required string ToAddress { get; init; }
    public IReadOnlyList<string>? FromAddresses { get; init; }
    public bool? AllowMultipleSources { get; init; }
    public bool? AutoConvert { get; init; }
    public AssetsPolicy? AutoConvertPolicy { get; init; }
    public string? MaxFeeAmountFiat { get; init; }
    public string? Memo { get; init; }
}

public sealed record ExecutePayoutRequest
{
    /// <summary>Idempotency key. Re-submitting with the same value returns the same uuid.</summary>
    public required string OrderId { get; init; }
    public required string UserId { get; init; }
    public required string Network { get; init; }
    public required string Coin { get; init; }
    public required string Amount { get; init; }
    public required string ToAddress { get; init; }
    public required string UrlCallback { get; init; }
    public IReadOnlyList<string>? FromAddresses { get; init; }
    public bool? AllowMultipleSources { get; init; }
    public bool? AutoConvert { get; init; }
    public AssetsPolicy? AutoConvertPolicy { get; init; }
    public string? MaxFeeAmountFiat { get; init; }
    public string? Memo { get; init; }
}

public sealed record PayoutFeeInfo
{
    public string? FeeMode { get; init; }
    public string? EstimatedFiat { get; init; }
    public string? EstimatedCoin { get; init; }
    public string? EstimatedAsset { get; init; }
}

public sealed record PayoutSource
{
    public string? Address { get; init; }
    public string? Network { get; init; }
    public string? Coin { get; init; }

    /// <summary>Amount sent from this source.</summary>
    public string? AmountCrypto { get; init; }

    [Obsolete("Not sent; use AmountCrypto")]
    public string? Amount { get; init; }

    /// <summary>Whether the source needs a gas top-up before sending.</summary>
    public bool? NeedRefuel { get; init; }

    public string? RefuelAmount { get; init; }
    public string? EstimatedFee { get; init; }
    public string? EstimatedFeeFiat { get; init; }

    /// <summary>Actual network fee; absent until the transaction is sent.</summary>
    public string? FeePaid { get; init; }

    public string? FeePaidFiat { get; init; }

    /// <summary>Hash of this source's transaction; absent until it is sent.</summary>
    [JsonPropertyName("txid")]
    public string? TxId { get; init; }

    /// <summary>Confirmations of this source's transaction; absent until it is on chain.</summary>
    public int? Confirmations { get; init; }
}

/// <summary>A transaction the platform makes for a payout, e.g. a gas top-up on a source wallet.</summary>
public sealed record PayoutServiceOperation
{
    /// <summary>What it is, e.g. <c>gas_refuel</c>.</summary>
    public string? Type { get; init; }

    /// <summary>What it was for, e.g. <c>payout_prepare</c>.</summary>
    public string? Context { get; init; }

    public string? Status { get; init; }
    public string? Network { get; init; }

    /// <summary>The network's native coin (ETH, TRX, ...).</summary>
    public string? Coin { get; init; }

    public string? AmountNative { get; init; }
    public string? FromAddress { get; init; }
    public string? ToAddress { get; init; }
    public string? EstimatedFee { get; init; }
    public string? EstimatedFeeFiat { get; init; }
    public string? FeePaid { get; init; }
    public string? FeePaidFiat { get; init; }

    [JsonPropertyName("txid")]
    public string? TxId { get; init; }

    /// <summary>Confirmations of this transaction; absent until it is on chain.</summary>
    public int? Confirmations { get; init; }
}

public sealed record EstimatePayoutResponse
{
    public string? Network { get; init; }
    public string? Coin { get; init; }
    public string? Amount { get; init; }
    public string? AmountToReceive { get; init; }
    public string? ToAddress { get; init; }
    public PayoutFeeInfo? FeeInfo { get; init; }
    public IReadOnlyList<PayoutSource>? Sources { get; init; }

    [JsonPropertyName("service_operations")]
    public IReadOnlyList<System.Text.Json.JsonElement>? ServiceOperations { get; init; }

    public bool AutoConvertApplied { get; init; }
}

public sealed record PayoutInfo
{
    public string Uuid { get; init; } = string.Empty;
    public string OrderId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Network { get; init; }
    public string? Coin { get; init; }
    public string? Amount { get; init; }
    public string? ToAddress { get; init; }

    [JsonPropertyName("txid")]
    public string? TxId { get; init; }

    public IReadOnlyList<PayoutSource>? Sources { get; init; }
    public IReadOnlyList<PayoutServiceOperation>? ServiceOperations { get; init; }

    /// <summary>Lowest confirmation count among <see cref="Sources"/>.</summary>
    public int? Confirmations { get; init; }

    /// <summary>
    /// Confirmations the network requires. The payout is <see cref="PayoutStatus.ConfirmCheck"/>
    /// until every source reaches it, then <see cref="PayoutStatus.Paid"/>. May be absent; on
    /// <see cref="PayoutStatus.Paid"/> with it present, <see cref="Confirmations"/> is at least this value.
    /// </summary>
    public int? RequiredConfirmations { get; init; }

    public string? UrlCallback { get; init; }
    public string? CreatedAt { get; init; }
    public string? UpdatedAt { get; init; }
    public string? Error { get; init; }

    public bool IsTerminal => Status switch
    {
        PayoutStatus.Paid or PayoutStatus.Failed or PayoutStatus.SystemFail
            or PayoutStatus.Expired or PayoutStatus.Cancel => true,
        _ => false,
    };

    public bool Succeeded => Status == PayoutStatus.Paid;
}

public sealed record BatchExecuteRequest
{
    public string? UrlCallback { get; init; }
    public required IReadOnlyList<ExecutePayoutRequest> Items { get; init; }
}

public sealed record BatchItemResult
{
    public int Index { get; init; }
    public string OrderId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Uuid { get; init; }
    public string? Error { get; init; }
}

public sealed record BatchExecuteResponse
{
    public string? BatchUuid { get; init; }
    public int Total { get; init; }
    public int Accepted { get; init; }
    public int Rejected { get; init; }
    public IReadOnlyList<BatchItemResult> Items { get; init; } = Array.Empty<BatchItemResult>();
}

public sealed record PayoutHistoryResponse
{
    public IReadOnlyList<PayoutInfo> Items { get; init; } = Array.Empty<PayoutInfo>();
    public HistoryMeta Meta { get; init; } = new();
}
