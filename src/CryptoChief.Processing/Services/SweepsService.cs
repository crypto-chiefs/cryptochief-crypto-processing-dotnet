using CryptoChief.Processing.Models;

namespace CryptoChief.Processing.Services;

public sealed class SweepsService
{
    private readonly CryptoChiefClient _client;
    internal SweepsService(CryptoChiefClient client) => _client = client;

    /// <summary>Force an immediate transit→master sweep for one address.</summary>
    public Task<ForceSweepResponse> ForceAsync(string address, string network, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<ForceSweepResponse>(
            "/v1/sweeps/force", new { address, network_code = network }, cancellationToken);

    public Task<SweepHistoryResponse> HistoryAsync(SweepHistoryQuery query, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<SweepHistoryResponse>(
            "/v1/sweeps/history", query, cancellationToken);

    public Task<SweepHistoryResponse> WalletHistoryAsync(SweepWalletHistoryQuery query, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<SweepHistoryResponse>(
            "/v1/sweeps/wallet/history", query, cancellationToken);

    /// <summary>
    /// The auto-sweep policy in force for one wallet, together with what it overrides and
    /// what it inherits. A null address asks for the project's own default.
    /// </summary>
    /// <remarks>
    /// Scoped to the caller's own wallets: an address that is not the project's answers
    /// <c>WALLET_NOT_FOUND</c>.
    /// </remarks>
    public Task<SweepSettings> SettingsAsync(SweepSettingsQuery? query = null, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<SweepSettings>(
            "/v1/sweeps/settings", query ?? new SweepSettingsQuery(), cancellationToken);

    /// <summary>
    /// Write a wallet's auto-sweep policy. Returns the settings as they stand afterwards,
    /// so the caller sees what the write resolved to without asking again.
    /// </summary>
    /// <remarks>
    /// A null argument leaves that field alone; <see cref="SweepFieldWrite.Inherit"/> stops
    /// overriding it. Inheritance is per field, so writing the mode leaves the fee mode as
    /// it was.
    /// <para>Refusals are named: <c>TYPE_WORK_INVALID</c>, <c>FEE_MODE_INVALID</c>,
    /// <c>THRESHOLD_INVALID</c>, <c>THRESHOLD_MUST_BE_POSITIVE</c>,
    /// <c>THRESHOLD_REQUIRED_FOR_THRESHOLD_MODE</c>, and <c>SWEEP_SETTINGS_LOCKED</c> when
    /// an operator has pinned the policy.</para>
    /// </remarks>
    public Task<SweepSettings> UpdateSettingsAsync(
        string address,
        SweepFieldWrite? typeWork = null,
        SweepFieldWrite? thresholdAmountUsd = null,
        SweepFieldWrite? feeMode = null,
        string? networkCode = null,
        CancellationToken cancellationToken = default)
    {
        var fields = new List<string>();
        if (typeWork is not null) fields.Add("type_work");
        if (thresholdAmountUsd is not null) fields.Add("threshold_amount_usd");
        if (feeMode is not null) fields.Add("fee_mode");

        var body = new
        {
            address,
            network_code = string.IsNullOrEmpty(networkCode) ? null : networkCode,
            fields = fields.Count == 0 ? null : fields,
            type_work = typeWork?.Value,
            threshold_amount_usd = thresholdAmountUsd?.Value,
            fee_mode = feeMode?.Value,
        };

        return _client.Transport.SendAsync<SweepSettings>(
            "/v1/sweeps/settings/update", body, cancellationToken);
    }
}
