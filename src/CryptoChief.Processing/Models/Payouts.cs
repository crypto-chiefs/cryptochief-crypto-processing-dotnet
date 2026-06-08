using System.Text.Json.Serialization;

namespace CryptoChief.Processing.Models;

public static class PayoutStatus
{
    public const string Queue      = "queue";
    public const string Process    = "process";
    public const string Paid       = "paid";
    public const string Failed     = "failed";
    public const string SystemFail = "system_fail";
    public const string Expired    = "expired";
    public const string Cancel     = "cancel";
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
    public string? Amount { get; init; }
    public string? Coin { get; init; }
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
