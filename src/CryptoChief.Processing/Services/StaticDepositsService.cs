using CryptoChief.Processing.Models;

namespace CryptoChief.Processing.Services;

public sealed class StaticDepositsService
{
    private readonly CryptoChiefClient _client;
    internal StaticDepositsService(CryptoChiefClient client) => _client = client;

    public Task<StaticDeposit> InfoAsync(string uuid, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<StaticDeposit>(
            "/v1/static-deposit/info", new { uuid }, cancellationToken);

    public Task<StaticDepositHistoryResponse> HistoryAsync(
        StaticDepositHistoryQuery query, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<StaticDepositHistoryResponse>(
            "/v1/static-deposit/history", query, cancellationToken);
}
