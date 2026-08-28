namespace CryptoChief.Processing.Models;

public static class SweepMode
{
    public const string Auto  = "auto";
    public const string Force = "force";
}

/// <summary>
/// Sweep status.
/// </summary>
/// <remarks>
/// A sweep is broadcast first and confirmed after: <c>Broadcasted</c> means the transaction
/// is out and not yet confirmed, <c>Completed</c> means the chain confirmed it. The platform
/// used to report <c>completed</c> at broadcast, so a sweep could read as settled while its
/// transaction was still unconfirmed or had been dropped.
/// <para><c>Skipped</c> is a sweep the platform decided against - almost always a balance
/// below the wallet's threshold. A normal outcome, not a failure.</para>
/// </remarks>
public static class SweepStatus
{
    public const string Pending     = "pending";
    public const string WaitingGas  = "waiting_gas";
    public const string Broadcasted = "broadcasted";
    public const string Completed   = "completed";
    public const string Failed      = "failed";
    public const string Skipped     = "skipped";
}

/// <summary>
/// Auto-sweep modes: <c>Off</c> is never swept on its own (a force sweep still works),
/// <c>Momentum</c> sweeps as soon as funds arrive, and <c>Threshold</c> sweeps once the
/// balance reaches the threshold. A held balance is re-checked periodically, so a wallet
/// that crosses the threshold through price movement alone is still swept.
/// </summary>
public static class SweepPolicyMode
{
    public const string Off       = "turned_off";
    public const string Momentum  = "momentum";
    public const string Threshold = "threshold";
}

/// <summary>
/// Who pays the gas for a sweep: <c>Client</c> the swept wallet itself, <c>Service</c> the
/// platform's service wallet, <c>Mix</c> the service wallet with the cost reclaimed from
/// the sweep.
/// </summary>
public static class SweepFeeMode
{
    public const string Client  = "client";
    public const string Service = "service";
    public const string Mix     = "mix";
}

/// <summary>
/// The two environments an order can belong to.
/// </summary>
/// <remarks>
/// A project may be allowed one or both; asking for testnet on a project that does not
/// permit it is refused with <c>TESTNET_NOT_ALLOWED</c> rather than quietly served on
/// mainnet, and a value that is neither is <c>ENVIRONMENT_INVALID</c> rather than a silent
/// fallback.
/// </remarks>
public static class Environment
{
    public const string Mainnet = "mainnet";
    public const string Testnet = "testnet";
}

public sealed record SweepHistoryQuery
{
    public string? Mode { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}

public sealed record SweepWalletHistoryQuery
{
    public required string Address { get; init; }
    public string? Mode { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}

public sealed record Sweep
{
    public string TaskId { get; init; } = string.Empty;
    public string? SweepTxHash { get; init; }
    public string? GasPumpTxHash { get; init; }

    /// <summary>One of the <see cref="SweepStatus"/> values.</summary>
    public string Status { get; init; } = string.Empty;

    public string WalletAddress { get; init; } = string.Empty;
    public string Chain { get; init; } = string.Empty;
    public string? ChainFamily { get; init; }
    public string? AssetSymbol { get; init; }
    public string? AssetType { get; init; }
    public string? AmountHuman { get; init; }

    /// <summary>What triggered this sweep: momentum, threshold or force.</summary>
    public string? TypeWork { get; init; }

    /// <summary>
    /// Confirmations seen on the sweep transaction, and when it reached the network's
    /// confirmation target. Read them with <see cref="Status"/>: <see cref="CompletedAt"/>
    /// is absent while the sweep is still in flight.
    /// </summary>
    public int? SweepConfirmations { get; init; }
    public string? CompletedAt { get; init; }

    /// <summary>
    /// Fees. <see cref="TotalFeeUsd"/> is the whole cost of the sweep; the gas-pump half is
    /// the funding transfer that pays for it on chains needing one. The <c>Real*</c> figures
    /// are what the chain actually charged, filled in once the transaction settles; the
    /// others are the estimate made up front.
    /// </summary>
    public string? TotalFeeUsd { get; init; }
    public string? GasPumpSource { get; init; }
    public string? GasPumpFeeHuman { get; init; }
    public string? GasPumpFeeUsd { get; init; }
    public string? SweepFeeHuman { get; init; }
    public string? SweepFeeUsd { get; init; }
    public string? RealGasPumpFeeHuman { get; init; }
    public string? RealGasPumpFeeUsd { get; init; }
    public string? RealSweepFeeHuman { get; init; }
    public string? RealSweepFeeUsd { get; init; }

    public string? CreatedAt { get; init; }

    /// <summary>
    /// Never populated. The API reports fees under the names above; this was a guess at a
    /// shape it does not send.
    /// </summary>
    [Obsolete("Never populated by the API")]
    public string? GasFeeHuman { get; init; }

    /// <inheritdoc cref="GasFeeHuman"/>
    [Obsolete("Never populated by the API")]
    public string? GasFeeFiat { get; init; }

    /// <inheritdoc cref="GasFeeHuman"/>
    [Obsolete("Never populated by the API")]
    public string? ServiceFeeFiat { get; init; }

    /// <summary>
    /// Never populated - sweeps carry <see cref="CreatedAt"/> and, once confirmed,
    /// <see cref="CompletedAt"/>.
    /// </summary>
    [Obsolete("Never populated by the API")]
    public string? UpdatedAt { get; init; }
}

/// <summary>A resolved set of sweep rules.</summary>
public sealed record SweepPolicy
{
    public string TypeWork { get; init; } = string.Empty;

    /// <summary>Meaningful only when <see cref="TypeWork"/> is threshold.</summary>
    public string? ThresholdAmountUsd { get; init; }

    public string FeeMode { get; init; } = string.Empty;

    /// <summary>
    /// Which layer the mode came from: wallet_network, wallet, project or default. Present
    /// on the effective policy, where the question arises.
    /// </summary>
    public string? Source { get; init; }
}

/// <summary>
/// What one wallet decides for itself. A null field is not overridden - it is inherited,
/// which no ordinary value can express.
/// </summary>
public sealed record SweepOverride
{
    /// <summary>
    /// Empty covers the address on every network it exists on; set, it covers that one
    /// network and takes precedence over the address-wide override.
    /// </summary>
    public string? NetworkCode { get; init; }

    public string? TypeWork { get; init; }
    public string? ThresholdAmountUsd { get; init; }
    public string? FeeMode { get; init; }

    /// <summary>Who wrote it: merchant or operator.</summary>
    public string? Source { get; init; }

    /// <summary>
    /// An operator pinned this policy. While it is set, a merchant write answers
    /// <c>SWEEP_SETTINGS_LOCKED</c> and changes nothing.
    /// </summary>
    public bool Locked { get; init; }
}

/// <summary>
/// Three layers, on purpose.
/// </summary>
/// <remarks>
/// <see cref="Effective"/> is what will actually happen, <see cref="Override"/> is what this
/// wallet decides for itself (null if it decides nothing), and <see cref="ProjectDefault"/>
/// is what it falls back to. Only the three together answer "is this value mine or
/// inherited" - the difference between changing it here and changing it on the project.
/// Inheritance is per field: a wallet can override the mode and keep inheriting the fee
/// mode.
/// </remarks>
public sealed record SweepSettings
{
    public string? WalletAddress { get; init; }
    public string? NetworkCode { get; init; }
    public SweepPolicy Effective { get; init; } = new();
    public SweepOverride? Override { get; init; }
    public SweepPolicy ProjectDefault { get; init; } = new();
}

/// <summary>
/// The body of /v1/sweeps/settings. A null address asks for the project's own default
/// rather than any wallet's policy.
/// </summary>
public sealed record SweepSettingsQuery
{
    public string? Address { get; init; }
    public string? NetworkCode { get; init; }
}

/// <summary>
/// A sweep-policy field being written.
/// </summary>
/// <remarks>
/// <see cref="Set"/> writes a value; <see cref="Inherit"/> stops overriding the field and
/// goes back to inheriting it. The API expresses the second by naming the field with no
/// value, which null cannot say here because it already means "not supplied - leave this
/// field alone". The two are different instructions: one changes nothing, the other resets
/// a value.
/// </remarks>
public sealed class SweepFieldWrite
{
    private SweepFieldWrite(string? value) => Value = value;

    /// <summary>The value being written, or null when the field is being reset.</summary>
    public string? Value { get; }

    /// <summary>Write this value.</summary>
    public static SweepFieldWrite Set(string value) => new(value);

    /// <summary>Stop overriding the field; inherit it again.</summary>
    public static SweepFieldWrite Inherit { get; } = new(null);
}

public sealed record SweepHistoryResponse
{
    public IReadOnlyList<Sweep> Items { get; init; } = Array.Empty<Sweep>();
    public HistoryMeta Meta { get; init; } = new();
}

public sealed record ForceSweepResponse
{
    public string Status { get; init; } = string.Empty;
}
