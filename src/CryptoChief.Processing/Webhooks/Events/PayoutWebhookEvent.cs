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

/// <summary>
/// Funds swept off a deposit wallet, confirmed on chain. Event name:
/// <c>sweep.confirmed</c> - the only sweep event the platform emits.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no <c>sweep.broadcasted</c>: "we sent it" is not
/// something you can act on, and an event that means "maybe" is one more thing
/// to reconcile.
/// </para>
/// <para>
/// A <c>static_deposit.paid</c> tells you a customer paid you. This tells you
/// the money has finished moving into your own custody - until it fires, the
/// balance still sits on the deposit address. Reconciliation, treasury
/// reporting and "funds available to pay out" all key off this event, not off
/// the deposit.
/// </para>
/// <para>
/// Sweeps run on static deposit wallets AND on the transit wallets issued per
/// pay-in order; both deliver here, to the callback URL configured for the
/// wallet the funds left.
/// </para>
/// </remarks>
public sealed record SweepWebhookEvent
{
    /// <summary>Always <c>sweep.confirmed</c>.</summary>
    public string Event { get; init; } = string.Empty;

    /// <summary>The sweeper task. One sweep settles once - use it as your idempotency key.</summary>
    public string TaskId { get; init; } = string.Empty;

    /// <summary>Always <c>completed</c>. A sweep reaches you in no other state.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>The wallet the funds left - the address your customer paid into.</summary>
    public string WalletAddress { get; init; } = string.Empty;

    /// <summary>The master wallet they landed on.</summary>
    public string? ToAddress { get; init; }

    public string Network { get; init; } = string.Empty;
    public string? ChainFamily { get; init; }
    public string AssetSymbol { get; init; } = string.Empty;
    public string? AssetContract { get; init; }

    /// <summary><c>native</c> or <c>token</c>.</summary>
    public string? AssetType { get; init; }

    public string? AmountRaw { get; init; }
    public string? AmountHuman { get; init; }

    public string SweepTxHash { get; init; } = string.Empty;

    /// <summary>Set when the platform had to fund gas on the wallet before it could sweep.</summary>
    public string? GasPumpTxHash { get; init; }

    /// <summary>
    /// What makes this event true rather than hopeful, and never zero. It
    /// travels with the event rather than being implied by it: "confirmed" is
    /// not the same number on every chain, so if you run your own finality
    /// policy you need the count to apply it.
    /// </summary>
    public int SweepConfirmations { get; init; }

    /// <summary>
    /// When the chain was observed to hold the sweep. NOT the task's completion
    /// timestamp, which is stamped on every terminal outcome - failures
    /// included - and so says nothing about settlement.
    /// </summary>
    public string? ConfirmedAt { get; init; }

    /// <summary>What triggered it: <c>momentum</c>, <c>threshold</c> or <c>force</c>.</summary>
    public string? TypeWork { get; init; }

    /// <summary>What the sweep cost: network fee plus any gas or energy the platform fronted.</summary>
    public string? TotalFeeUsd { get; init; }
}
