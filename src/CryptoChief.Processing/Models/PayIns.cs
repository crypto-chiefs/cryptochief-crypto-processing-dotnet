namespace CryptoChief.Processing.Models;

public static class PayInMode
{
    public const string Fiat   = "fiat";
    public const string Crypto = "crypto";
}

public static class PayInStatus
{
    public const string WaitingAssetSelect  = "waiting_asset_select";
    public const string Pending             = "pending";
    public const string Processing          = "processing";
    public const string Process             = "process";
    public const string Paid                = "paid";
    /// <summary>Final: paid, but less than invoiced - a wildcard-accuracy or multi-payment order.</summary>
    public const string PaidLess            = "paid_less";
    /// <summary>Final: paid more than invoiced - a wildcard-accuracy or multi-payment order.</summary>
    public const string PaidOver            = "paid_over";
    public const string Cancel              = "cancel";
    public const string Expired             = "expired";
    /// <summary>
    /// A multi-payment order (<see cref="CreatePayInRequest.IsPaymentMultiple"/>) that has
    /// received less than the invoiced amount so far; the remainder is payable until
    /// one hour past <c>expired_at</c>.
    /// </summary>
    public const string WrongAmountWaiting  = "wrong_amount_waiting";
}

public sealed record CreatePayInRequest
{
    public required string OrderId { get; init; }
    public required string UserId { get; init; }
    public required string Mode { get; init; }
    public string? ToAddress { get; init; }

    /// <summary>
    /// Pin the transit deposit wallet of THIS order to the given master wallet of the
    /// project - the address the funds are swept to. The order's asset/network chain family
    /// must match the master wallet's; a foreign or mismatched address is rejected with 400.
    /// Omit for the project-default behaviour.
    /// </summary>
    public string? MasterWalletAddress { get; init; }

    /// <summary>
    /// Constrain the asset the platform PICKS for this order to the real chains or the test
    /// ones - a value of <see cref="PayInEnvironment"/>. Omit to use the project's own default.
    /// </summary>
    /// <remarks>
    /// It changes nothing when <see cref="Asset"/> names a concrete network - that is the
    /// caller's choice. It matters in fiat mode and when the network is ANY, where the
    /// platform selects the asset and an unconstrained pick could put a real payment on a
    /// test network.
    /// </remarks>
    public string? Environment { get; init; }

    public int? LifetimeSec { get; init; }
    public string? UrlCallback { get; init; }
    public string? UrlSuccess { get; init; }
    public string? UrlError { get; init; }
    public string? AdditionalData { get; init; }

    /// <summary>
    /// How close the paid amount must be to the invoiced one, in percent: 0 to 15, default 5.
    /// The wildcard <c>-1</c> accepts any amount - the order then closes as
    /// <c>paid</c> / <c>paid_less</c> / <c>paid_over</c> by the direction of the difference.
    /// </summary>
    public int? AccuracyPaymentPercent { get; init; }

    /// <summary>
    /// Let the invoice be paid by several transactions. An underpayment moves the order to
    /// <see cref="PayInStatus.WrongAmountWaiting"/> instead of closing it, and the remainder
    /// is payable until one hour past <c>expired_at</c>. Omit for the default (single payment).
    /// </summary>
    public bool? IsPaymentMultiple { get; init; }

    public string? AmountFiat { get; init; }
    public string? Currency { get; init; }
    public string? CourseSource { get; init; }
    public AssetsPolicy? Assets { get; init; }

    public string? AmountCrypto { get; init; }
    public Asset? Asset { get; init; }
}

public sealed record CoinOption
{
    public string ChainFamily { get; init; } = string.Empty;
    public string Coin { get; init; } = string.Empty;
    public string Network { get; init; } = string.Empty;
    public string? Contract { get; init; }
}

public sealed record PayIn
{
    public string Type { get; init; } = string.Empty;
    public string Uuid { get; init; } = string.Empty;
    public string OrderId { get; init; } = string.Empty;
    public string? UserId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Mode { get; init; }
    public string? AmountCrypto { get; init; }
    public string? AmountFiat { get; init; }
    public string? Currency { get; init; }
    public string? PaymentCoin { get; init; }
    public string? PaymentNetwork { get; init; }
    public string? ToAddress { get; init; }
    public IReadOnlyList<CoinOption>? Coins { get; init; }
    public string? PaymentLink { get; init; }
    public string? UrlCallback { get; init; }
    public string? UrlSuccess { get; init; }
    public string? UrlError { get; init; }
    public string? AdditionalData { get; init; }
    public bool? CanCancel { get; init; }
    public string? ExpiredAt { get; init; }
    public string? CreatedAt { get; init; }
    public string? UpdatedAt { get; init; }

    /// <summary>
    /// Present only on orders created with <c>is_payment_multiple</c> - the invoice can be
    /// paid by several transactions.
    /// </summary>
    public bool? IsPaymentMultiple { get; init; }

    /// <summary>Total received so far, in crypto. Only on multi-payment orders.</summary>
    public string? ReceivedAmountCrypto { get; init; }

    /// <summary>What is still left to pay, in crypto. Only on multi-payment orders.</summary>
    public string? RemainingAmountCrypto { get; init; }

    /// <summary>The individual payments received. Only on multi-payment orders.</summary>
    public IReadOnlyList<PayInPayment>? Payments { get; init; }

    public bool IsTerminal => Status switch
    {
        PayInStatus.Paid or PayInStatus.PaidLess or PayInStatus.PaidOver
            or PayInStatus.Cancel or PayInStatus.Expired => true,
        _ => false,
    };

    public bool Succeeded => Status == PayInStatus.Paid;
}

/// <summary>One payment towards a multi-payment invoice; see <see cref="PayIn.Payments"/>.</summary>
public sealed record PayInPayment
{
    [System.Text.Json.Serialization.JsonPropertyName("txid")]
    public string? TxId { get; init; }
    public string? AmountCrypto { get; init; }
    public int? Confirmations { get; init; }
    public string? Status { get; init; }
    public string? SeenAt { get; init; }
}

public sealed record SelectAssetRequest
{
    public required string Uuid { get; init; }
    public required string Coin { get; init; }
    public required string Network { get; init; }

    /// <summary>
    /// Pin the order's transit deposit wallet to the given project master wallet; see
    /// <see cref="CreatePayInRequest.MasterWalletAddress"/>. A value here overrides one
    /// supplied at order create.
    /// </summary>
    public string? MasterWalletAddress { get; init; }
}

public sealed record PayInHistoryResponse
{
    public IReadOnlyList<PayIn> Items { get; init; } = Array.Empty<PayIn>();
    public HistoryMeta Meta { get; init; } = new();
}
