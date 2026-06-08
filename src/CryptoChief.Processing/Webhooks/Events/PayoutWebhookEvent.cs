using System.Text.Json;

namespace CryptoChief.Processing.Webhooks.Events;

public sealed record PayoutWebhookEvent
{
    public string Event { get; init; } = string.Empty;
    public string Uuid { get; init; } = string.Empty;
    public string OrderId { get; init; } = string.Empty;
    public string? UserId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? AmountRequested { get; init; }
    public string? AmountToReceive { get; init; }
    public string? ToAddress { get; init; }
    public JsonElement? FeeInfo { get; init; }
    public JsonElement? Sources { get; init; }
    public JsonElement? ServiceOperations { get; init; }
    public string? CreatedAt { get; init; }
    public string? CompletedAt { get; init; }
    public string? ErrorReason { get; init; }
}

public sealed record TransactionWebhookEvent
{
    public string Event { get; init; } = string.Empty;
    public string Uuid { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Network { get; init; }
    public string? ChainFamily { get; init; }
    public string? Type { get; init; }
    public string? FromAddress { get; init; }
    public string? ToAddress { get; init; }
    public string? Value { get; init; }
    public string? Contract { get; init; }
    public string? TxHash { get; init; }
    public string? CreatedAt { get; init; }
    public string? CompletedAt { get; init; }
    public string? ErrorReason { get; init; }
}

public sealed record PayInWebhookEvent
{
    public string Event { get; init; } = string.Empty;
    public string Uuid { get; init; } = string.Empty;
    public string OrderId { get; init; } = string.Empty;
    public string? UserId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? PrevStatus { get; init; }
    public string? Mode { get; init; }
    public string? AmountCrypto { get; init; }
    public string? AmountFiat { get; init; }
    public string? FactAmountCrypto { get; init; }
    public string? FactAmountFiat { get; init; }
    public string? Currency { get; init; }
    public string? PaymentCoin { get; init; }
    public string? PaymentNetwork { get; init; }
    public string? ToAddress { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("txid")]
    public string? TxId { get; init; }
}

public sealed record StaticDepositWebhookEvent
{
    public string Event { get; init; } = string.Empty;
    public string Uuid { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Network { get; init; }
    public string? ChainFamily { get; init; }
    public string? Coin { get; init; }
    public string? Contract { get; init; }
    public int? Decimals { get; init; }
    public string? ToAddress { get; init; }
    public string? FromAddress { get; init; }
    public string? TxHash { get; init; }
    public string? Amount { get; init; }
    public string? AmountFiat { get; init; }
    public int? Confirmations { get; init; }
    public int? RequiredConfirmations { get; init; }
    public bool FoundInMempool { get; init; }
    public string? LogType { get; init; }
    public long? BlockNumber { get; init; }
    public string? CreatedAt { get; init; }
    public string? UpdatedAt { get; init; }
    public string? ConfirmedAt { get; init; }
    public string? PaidAt { get; init; }
}
