using CryptoChief.Processing.Models;

namespace CryptoChief.Processing.Services;

public sealed class PayoutsService
{
    private readonly CryptoChiefClient _client;
    internal PayoutsService(CryptoChiefClient client) => _client = client;

    /// <summary>Preview fees and source(s) without locking funds.</summary>
    public Task<EstimatePayoutResponse> EstimateAsync(
        EstimatePayoutRequest request, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<EstimatePayoutResponse>(
            "/v1/payout/estimate", request, cancellationToken);

    /// <summary>Create and dispatch a payout. Idempotent on <c>OrderId</c>.</summary>
    public Task<PayoutInfo> ExecuteAsync(
        ExecutePayoutRequest request, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<PayoutInfo>(
            "/v1/payout/execute", request, cancellationToken);

    public Task<PayoutInfo> InfoAsync(string uuid, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<PayoutInfo>(
            "/v1/payout/info", new { uuid }, cancellationToken);

    public Task<PayoutHistoryResponse> HistoryAsync(
        HistoryQuery query, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<PayoutHistoryResponse>(
            "/v1/payout/history", query, cancellationToken);

    /// <summary>Preview a batch of up to 50 payouts. Per-item errors come back in <c>items[]</c>.</summary>
    public Task<BatchExecuteResponse> BatchEstimateAsync(
        BatchExecuteRequest request, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<BatchExecuteResponse>(
            "/v1/payout/batch/estimate", request, cancellationToken);

    /// <summary>Create up to 50 payouts. Locked sequentially server-side — do not parallelize.</summary>
    public Task<BatchExecuteResponse> BatchExecuteAsync(
        BatchExecuteRequest request, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<BatchExecuteResponse>(
            "/v1/payout/batch/execute", request, cancellationToken);
}
