namespace CryptoChief.Processing.Models;

public static class PayInMode
{
    public const string Fiat   = "fiat";
    public const string Crypto = "crypto";
}

public static class PayInStatus
{
    public const string WaitingAssetSelect = "waiting_asset_select";
    public const string Pending            = "pending";
    public const string Processing         = "processing";
    public const string Process            = "process";
    public const string Paid               = "paid";
    public const string Cancel             = "cancel";
    public const string Expired            = "expired";
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
    /// ones - a value of <see cref="Environment"/>. Omit to use the project's own default.
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
    public int? AccuracyPaymentPercent { get; init; }

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

    public bool IsTerminal => Status switch
    {
        PayInStatus.Paid or PayInStatus.Cancel or PayInStatus.Expired => true,
        _ => false,
    };

    public bool Succeeded => Status == PayInStatus.Paid;
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
