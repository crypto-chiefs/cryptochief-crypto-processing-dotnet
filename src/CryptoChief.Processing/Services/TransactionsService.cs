using CryptoChief.Processing.Models;

namespace CryptoChief.Processing.Services;

public sealed partial class TransactionsService
{
    internal readonly CryptoChiefClient Client;
    internal TransactionsService(CryptoChiefClient client) => Client = client;

    /// <summary>Build and sign a transaction without broadcasting. TTL: EVM 10m, UTXO 15m, TRON 45s, Solana 60s, XRP 90s, TON 300s.</summary>
    public Task<SignTransactionResponse> SignAsync(
        SignTransactionRequest request, CancellationToken cancellationToken = default) =>
        Client.Transport.SendAsync<SignTransactionResponse>(
            "/v1/transaction/signature", request, cancellationToken);

    /// <summary>
    /// Quote the network fee of a transaction without signing or broadcasting it.
    /// <see cref="TxType.Contract"/> is refused with
    /// <see cref="Errors.ErrorCodes.ContractEstimateUnsupported"/>.
    /// </summary>
    public Task<EstimateTransactionResponse> EstimateAsync(
        EstimateTransactionRequest request, CancellationToken cancellationToken = default) =>
        Client.Transport.SendAsync<EstimateTransactionResponse>(
            "/v1/transaction/estimate", request, cancellationToken);

    /// <summary>Broadcast a previously-signed transaction by uuid.</summary>
    public Task<TransactionInfo> ExecuteAsync(
        ExecuteTransactionRequest request, CancellationToken cancellationToken = default) =>
        Client.Transport.SendAsync<TransactionInfo>(
            "/v1/transaction/execute", request, cancellationToken);

    public Task<TransactionInfo> InfoAsync(string uuid, CancellationToken cancellationToken = default) =>
        Client.Transport.SendAsync<TransactionInfo>(
            "/v1/transaction/info", new { uuid }, cancellationToken);

    public Task<TransactionHistoryResponse> HistoryAsync(
        HistoryQuery query, CancellationToken cancellationToken = default) =>
        Client.Transport.SendAsync<TransactionHistoryResponse>(
            "/v1/transaction/history", query, cancellationToken);
}
