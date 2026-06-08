using CryptoChief.Processing.Models;

namespace CryptoChief.Processing.Services;

public sealed class BlockchainService
{
    private readonly CryptoChiefClient _client;
    internal BlockchainService(CryptoChiefClient client) => _client = client;

    public Task<AvailableContractsResponse> ContractsAvailableAsync(
        string? network = null, CancellationToken cancellationToken = default)
    {
        object body = network is null ? new { } : (object)new { network };
        return _client.Transport.SendAsync<AvailableContractsResponse>(
            "/v1/blockchain/contracts/available", body, cancellationToken);
    }

    public Task<IReadOnlyList<WalletBalanceRow>> WalletBalanceAsync(
        string chain,
        IEnumerable<string> addresses,
        IEnumerable<string>? contracts = null,
        CancellationToken cancellationToken = default)
    {
        var body = contracts is null
            ? (object)new { chain, addresses = addresses.ToArray() }
            : new { chain, addresses = addresses.ToArray(), contracts = contracts.ToArray() };
        return _client.Transport.SendAsync<IReadOnlyList<WalletBalanceRow>>(
            "/v1/blockchain/wallet/balance", body, cancellationToken);
    }

    public Task<IReadOnlyList<TxStatusRow>> TransactionStatusAsync(
        string chain, string hash, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<IReadOnlyList<TxStatusRow>>(
            "/v1/blockchain/transaction/status", new { chain, hash }, cancellationToken);
}
