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

    /// <summary>
    /// Every coin and token the platform supports, on every network — regardless of what
    /// the project has enabled.
    /// </summary>
    /// <remarks>
    /// Use it to build a "which assets could we turn on" picker. For what the project can
    /// be paid in right now — the list that governs orders, sweeps and payouts — use
    /// <see cref="ContractsAvailableAsync"/>.
    /// <para>Platform-wide, so there is nothing to filter by project. Mainnet and testnet
    /// assets arrive in one list: read <see cref="AvailableContract.IsTest"/> to tell them
    /// apart.</para>
    /// </remarks>
    public Task<AvailableContractsResponse> ContractsListAsync(
        CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<AvailableContractsResponse>(
            "/v1/blockchain/contracts/list", new { }, cancellationToken);

    /// <summary>
    /// The chains the platform's blockchain scanner is currently connected to.
    /// </summary>
    /// <remarks>
    /// Infrastructure-level information: which chains the platform can read blocks from
    /// right now. It is not the project's asset catalogue — for what the project can
    /// actually be paid in, use <see cref="ContractsAvailableAsync"/>.
    /// <para>The API answers with a bare JSON array, not an <c>items</c> envelope. An empty
    /// answer reaches the wire as <c>null</c> rather than <c>[]</c>, and comes back here as
    /// an empty list either way — never null.</para>
    /// </remarks>
    public async Task<IReadOnlyList<SupportedBlockchain>> BlockchainsListAsync(
        CancellationToken cancellationToken = default) =>
        await _client.Transport.SendAsync<IReadOnlyList<SupportedBlockchain>?>(
            "/v1/blockchains/list", new { }, cancellationToken).ConfigureAwait(false)
        ?? Array.Empty<SupportedBlockchain>();

    /// <summary>
    /// On-chain balances for a set of addresses on one chain.
    /// </summary>
    /// <remarks>A bare JSON array, and an empty one may arrive as <c>null</c>; the result
    /// is an empty list in that case, never null.</remarks>
    public async Task<IReadOnlyList<WalletBalanceRow>> WalletBalanceAsync(
        string chain,
        IEnumerable<string> addresses,
        IEnumerable<string>? contracts = null,
        CancellationToken cancellationToken = default)
    {
        var body = contracts is null
            ? (object)new { chain, addresses = addresses.ToArray() }
            : new { chain, addresses = addresses.ToArray(), contracts = contracts.ToArray() };
        return await _client.Transport.SendAsync<IReadOnlyList<WalletBalanceRow>?>(
            "/v1/blockchain/wallet/balance", body, cancellationToken).ConfigureAwait(false)
            ?? Array.Empty<WalletBalanceRow>();
    }

    /// <summary>
    /// Confirmation count, fee and block of one transaction hash on one chain.
    /// </summary>
    /// <remarks>A bare JSON array, and an empty one may arrive as <c>null</c>; the result
    /// is an empty list in that case, never null.</remarks>
    public async Task<IReadOnlyList<TxStatusRow>> TransactionStatusAsync(
        string chain, string hash, CancellationToken cancellationToken = default) =>
        await _client.Transport.SendAsync<IReadOnlyList<TxStatusRow>?>(
            "/v1/blockchain/transaction/status", new { chain, hash }, cancellationToken)
            .ConfigureAwait(false)
        ?? Array.Empty<TxStatusRow>();
}
