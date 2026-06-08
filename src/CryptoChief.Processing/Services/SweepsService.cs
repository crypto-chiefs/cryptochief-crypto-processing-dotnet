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
}
