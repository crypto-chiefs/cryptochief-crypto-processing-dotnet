using CryptoChief.Processing.Models;

namespace CryptoChief.Processing.Services;

public sealed class WithdrawalsService
{
    private readonly CryptoChiefClient _client;
    internal WithdrawalsService(CryptoChiefClient client) => _client = client;

    public Task<Withdrawal> InfoAsync(string uuid, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<Withdrawal>(
            "/v1/withdrawal/info", new { uuid }, cancellationToken);

    public Task<WithdrawalHistoryResponse> HistoryAsync(HistoryQuery query, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<WithdrawalHistoryResponse>(
            "/v1/withdrawal/history", query, cancellationToken);
}
