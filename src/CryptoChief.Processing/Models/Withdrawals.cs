namespace CryptoChief.Processing.Models;

/// <summary>Status of a manual withdrawal.</summary>
/// <remarks>
/// <see cref="ConfirmCheck"/> lasts until <c>Withdrawal.Confirmations</c> reaches
/// <c>Withdrawal.RequiredConfirmations</c>, then the withdrawal is <see cref="Completed"/>.
/// </remarks>
public static class WithdrawalStatus
{
    /// <summary>Waiting to be processed.</summary>
    public const string Queue           = "queue";

    /// <summary>The source wallet is being topped up with native coin for gas.</summary>
    public const string Refueling       = "refueling";

    /// <summary>Gas is in place; the transfer is about to be sent.</summary>
    public const string RefuelConfirmed = "refuel_confirmed";

    /// <summary>The transfer is being signed and sent.</summary>
    public const string Sending         = "sending";

    /// <summary>Handed off for broadcast, hash not known yet (EVM networks).</summary>
    public const string Broadcasting    = "broadcasting";

    /// <summary>In the mempool, not in a block yet (UTXO networks).</summary>
    public const string InMempool       = "in_mempool";

    /// <summary>Sent; waiting for <c>Withdrawal.RequiredConfirmations</c>.</summary>
    public const string ConfirmCheck    = "confirm_check";

    /// <summary>Terminal: the transaction reached <c>Withdrawal.RequiredConfirmations</c>.</summary>
    public const string Completed       = "completed";

    /// <summary>Terminal: <c>Withdrawal.ErrorReason</c> says why.</summary>
    public const string Failed          = "failed";

    /// <summary>Not sent by the API.</summary>
    public const string Cancelled       = "cancelled";
}

/// <summary>A manual withdrawal from one of the project's wallets. Withdrawals send no webhooks.</summary>
public sealed record Withdrawal
{
    public string Uuid { get; init; } = string.Empty;

    /// <summary>One of <see cref="WithdrawalStatus"/>.</summary>
    public string Status { get; init; } = string.Empty;

    public string? Network { get; init; }
    public string? Coin { get; init; }

    /// <summary>Human-readable amount in coin units, e.g. <c>100.5</c>.</summary>
    public string Amount { get; init; } = string.Empty;

    /// <summary>The project wallet the funds leave from.</summary>
    public string? FromAddress { get; init; }

    public string? ToAddress { get; init; }

    /// <summary>Hash of the withdrawal transaction; absent until it is known.</summary>
    public string? TxHash { get; init; }

    /// <summary>Whether the source wallet needed a gas top-up before the transfer.</summary>
    public bool NeedRefuel { get; init; }

    /// <summary>Hash of the gas top-up transaction, when there was one.</summary>
    public string? RefuelTxHash { get; init; }

    /// <summary>Progress of the gas top-up, e.g. <c>sent</c> or <c>done</c>; absent when there was none.</summary>
    public string? RefuelStatus { get; init; }

    /// <summary>
    /// Confirmations of the withdrawal transaction. Absent before the first block. <c>0</c> while no
    /// confirmations are counted yet, including after the transaction left a block (status stays
    /// <see cref="WithdrawalStatus.ConfirmCheck"/>). On
    /// <see cref="WithdrawalStatus.Completed"/>, at least <see cref="RequiredConfirmations"/>.
    /// </summary>
    public int? Confirmations { get; init; }

    /// <summary>Confirmations the network requires. Always sent.</summary>
    public int? RequiredConfirmations { get; init; }

    /// <summary>Why the withdrawal failed; absent otherwise.</summary>
    public string? ErrorReason { get; init; }

    /// <summary>Fee estimated at creation, in USD.</summary>
    public string? EstimatedFeeFiat { get; init; }

    /// <summary>Fee actually paid, in USD, gas top-up included; absent until the withdrawal completes.</summary>
    public string? ActualFeeFiat { get; init; }

    /// <summary>Who paid the network fee: <c>client</c>, <c>service</c> or <c>mix</c>.</summary>
    public string? FeeMode { get; init; }

    public string? CreatedAt { get; init; }

    /// <summary>When the withdrawal became <see cref="WithdrawalStatus.Completed"/>; absent otherwise.</summary>
    public string? CompletedAt { get; init; }

    /// <summary>Not sent; always <c>null</c>.</summary>
    public string? Contract { get; init; }

    /// <summary>Not sent; always <c>null</c>.</summary>
    public string? AmountFiat { get; init; }

    /// <summary>Not sent; always <c>null</c>.</summary>
    public string? UpdatedAt { get; init; }

    /// <summary>Not sent; always <c>null</c>. Use <see cref="CompletedAt"/>.</summary>
    public string? ConfirmedAt { get; init; }

    /// <summary>Not sent; always <c>null</c>. Use <see cref="ErrorReason"/>.</summary>
    public string? Error { get; init; }

    /// <summary><see cref="WithdrawalStatus.Completed"/>, <see cref="WithdrawalStatus.Failed"/> or <see cref="WithdrawalStatus.Cancelled"/>.</summary>
    public bool IsTerminal => Status switch
    {
        WithdrawalStatus.Completed or WithdrawalStatus.Failed or WithdrawalStatus.Cancelled => true,
        _ => false,
    };

    /// <summary><see cref="WithdrawalStatus.Completed"/>.</summary>
    public bool Succeeded => Status == WithdrawalStatus.Completed;
}

public sealed record WithdrawalHistoryResponse
{
    public IReadOnlyList<Withdrawal> Items { get; init; } = Array.Empty<Withdrawal>();
    public HistoryMeta Meta { get; init; } = new();
}
