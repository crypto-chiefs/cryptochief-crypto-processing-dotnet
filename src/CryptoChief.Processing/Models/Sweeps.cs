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
/// transaction was still unconfirmed or had been dropped — which is why the pair to check
/// is <c>Completed</c> together with <c>Sweep.SweepConfirmations</c> above zero, and never
/// the presence of <c>Sweep.CompletedAt</c>.
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
/// Who funds the gas a sweep needs — and only when the deposit wallet cannot fund it
/// itself.
/// </summary>
/// <remarks>
/// A deposit wallet that already holds enough of the chain's native coin pays for its own
/// transfer, whatever the mode says. The mode decides one thing: who covers the shortfall
/// when it does not.
/// <list type="bullet">
/// <item><description><see cref="Client"/> — the shortfall comes from your own master
/// wallet.</description></item>
/// <item><description><see cref="Service"/> — the platform supplies it, and
/// <b>the cost is billed to your API credits</b>.</description></item>
/// <item><description><see cref="Mix"/> — <b>the platform default.</b> Tries
/// <see cref="Client"/> first and falls back to <see cref="Service"/> when the master
/// wallet cannot cover it.</description></item>
/// </list>
/// <para><see cref="Mix"/> is not "service wallet with the cost reclaimed from the sweep":
/// nothing is taken out of the swept funds to repay it. It is client-first with a service
/// fallback, and when it falls back you are billed as if you had chosen
/// <see cref="Service"/>.</para>
/// </remarks>
public static class SweepFeeMode
{
    /// <summary>The shortfall is funded from your own master wallet.</summary>
    public const string Client  = "client";

    /// <summary>
    /// The platform funds the shortfall, and the cost is billed to your API credits.
    /// </summary>
    public const string Service = "service";

    /// <summary>
    /// The platform default: <see cref="Client"/> first, falling back to
    /// <see cref="Service"/> when the master wallet cannot cover it.
    /// </summary>
    public const string Mix     = "mix";
}

/// <summary>
/// What is bought for a TRON sweep: <c>Native</c> burns the wallet's own TRX for the
/// energy, <c>Rented</c> has the platform supply it. Carried and ignored on every other
/// chain.
/// </summary>
/// <remarks>
/// It answers a different question from <see cref="SweepFeeMode"/> — what is bought, not
/// who covers the network fees — and the two are independent: energy can be supplied under
/// any fee mode, and it is billed to your API credits whichever one you chose.
/// <para><b>Not setting it is not the same as setting <c>native</c>.</b> A wallet that has
/// never chosen one gets the platform default, <see cref="Rented"/> — so energy is supplied
/// and billed to your credits without anybody having switched it on. Read
/// <c>SweepSettings.Effective.GasSource</c> to see what will actually happen; to have the
/// wallet burn its own TRX, write <see cref="Native"/> explicitly.</para>
/// </remarks>
public static class SweepGasSource
{
    public const string Native = "native";
    public const string Rented = "rented";
}

/// <summary>
/// The two environments an order can belong to.
/// </summary>
/// <remarks>
/// A project may be allowed one or both; asking for testnet on a project that does not
/// permit it is refused with <c>TESTNET_NOT_ALLOWED</c> rather than quietly served on
/// mainnet, and a value that is neither is <c>ENVIRONMENT_INVALID</c> rather than a silent
/// fallback.
/// <para>Named <c>PayInEnvironment</c> rather than <c>Environment</c> on purpose: this
/// namespace is imported wholesale, and a type called <c>Environment</c> would collide with
/// <see cref="System.Environment"/> in every file that does so.</para>
/// </remarks>
public static class PayInEnvironment
{
    public const string Mainnet = "mainnet";
    public const string Testnet = "testnet";
}

public sealed record SweepHistoryQuery
{
    /// <summary>
    /// One of the <see cref="SweepMode"/> values. Null includes both.
    /// </summary>
    public string? Mode { get; init; }

    /// <summary>
    /// One of the <see cref="SweepStatus"/> values. Null includes every status, the
    /// <see cref="SweepStatus.Skipped"/> ones among them.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// Substring match on the wallet address, the sweep or gas-pump transaction hash, and
    /// the task id.
    /// </summary>
    public string? Search { get; init; }

    public int? Page { get; init; }
    public int? PageSize { get; init; }
}

public sealed record SweepWalletHistoryQuery
{
    public required string Address { get; init; }

    /// <inheritdoc cref="SweepHistoryQuery.Mode"/>
    public string? Mode { get; init; }

    /// <inheritdoc cref="SweepHistoryQuery.Status"/>
    public string? Status { get; init; }

    /// <summary>
    /// Substring match on the sweep or gas-pump transaction hash and the task id. The
    /// wallet is already fixed by <see cref="Address"/>, so the address is not searched.
    /// </summary>
    public string? Search { get; init; }

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
    /// Confirmations seen on the sweep transaction — <c>0</c> until it is mined. Above
    /// zero is the settlement signal: the chain was observed holding the funds.
    /// </summary>
    public int? SweepConfirmations { get; init; }

    /// <summary>
    /// When the sweep reached a terminal outcome — <b>failures included</b>. Absent while
    /// it is still in flight.
    /// </summary>
    /// <remarks>
    /// <b>Not proof the sweep settled.</b> A failed sweep is not in flight either, so it
    /// carries a completion timestamp too; reading its presence as "the funds arrived"
    /// books a failure as money received.
    /// <para>To tell settlement apart, check <see cref="SweepConfirmations"/> is above
    /// zero. Or take <c>confirmed_at</c> off the <c>sweep.confirmed</c> webhook
    /// (<c>SweepWebhookEvent.ConfirmedAt</c>) — it exists as a separate field for exactly
    /// this reason, rather than reusing this one.</para>
    /// </remarks>
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
    /// Never populated - sweeps carry <see cref="CreatedAt"/> and, once the task reaches a
    /// terminal outcome, <see cref="CompletedAt"/>.
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

    /// <summary>
    /// Who funds a gas shortfall — one of the <see cref="SweepFeeMode"/> values, whose
    /// documentation says what each costs you. On the effective policy it is always a
    /// concrete value; the platform default is <see cref="SweepFeeMode.Mix"/>.
    /// </summary>
    public string FeeMode { get; init; } = string.Empty;

    /// <summary>
    /// What is bought for the transfer on TRON — one of the <see cref="SweepGasSource"/>
    /// values. On the effective policy it is always a concrete value, so this is the field
    /// to read to know whether energy will be rented and billed to your credits. On the
    /// project default, null means the project never chose one and the platform default
    /// <see cref="SweepGasSource.Rented"/> applies.
    /// </summary>
    public string? GasSource { get; init; }

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

    /// <summary>
    /// One of the <see cref="SweepFeeMode"/> values, or null when this layer does not
    /// decide it. Null is "inherited", never "nobody pays": a wallet inheriting nothing
    /// still gets the platform default <see cref="SweepFeeMode.Mix"/>, which falls back to
    /// the platform funding the gas and billing it to your API credits.
    /// </summary>
    public string? FeeMode { get; init; }

    /// <summary>
    /// One of the <see cref="SweepGasSource"/> values, or null when this layer does not
    /// decide it. Null is "inherited", never "switched off": a wallet inheriting nothing
    /// still gets the platform default <see cref="SweepGasSource.Rented"/>, which supplies
    /// energy and bills it to your credits. <c>SweepSettings.Effective.GasSource</c> is
    /// what will actually happen.
    /// </summary>
    public string? GasSource { get; init; }

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
