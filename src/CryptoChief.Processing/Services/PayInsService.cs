using CryptoChief.Processing.Models;

namespace CryptoChief.Processing.Services;

public sealed class PayInsService
{
    private readonly CryptoChiefClient _client;
    internal PayInsService(CryptoChiefClient client) => _client = client;

    public Task<PayIn> CreateAsync(CreatePayInRequest request, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<PayIn>(
            "/v1/payments/order/create", request, cancellationToken);

    /// <summary>Commit the customer's coin/network choice on a waiting_asset_select order.</summary>
    public Task<PayIn> SelectAssetAsync(SelectAssetRequest request, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<PayIn>(
            "/v1/payments/asset/select", request, cancellationToken);

    public Task<PayIn> ResetAssetAsync(string uuid, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<PayIn>(
            "/v1/payments/asset/reset", new { uuid }, cancellationToken);

    public Task<PayIn> CancelAsync(string uuid, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<PayIn>(
            "/v1/payments/order/cancel", new { uuid }, cancellationToken);

    public Task<PayIn> InfoAsync(string uuid, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<PayIn>(
            "/v1/payments/order/info", new { uuid }, cancellationToken);

    public Task<PayInHistoryResponse> HistoryAsync(HistoryQuery query, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<PayInHistoryResponse>(
            "/v1/payments/history", query, cancellationToken);
}
