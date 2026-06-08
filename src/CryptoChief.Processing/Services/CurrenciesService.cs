using CryptoChief.Processing.Models;

namespace CryptoChief.Processing.Services;

public sealed class CurrenciesService
{
    private readonly CryptoChiefClient _client;
    internal CurrenciesService(CryptoChiefClient client) => _client = client;

    public Task<ConvertResponse> FiatToCryptoAsync(
        ConvertRequest request, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<ConvertResponse>(
            "/v1/currencies/convert/fiat-crypto", request, cancellationToken);

    public Task<ConvertResponse> CryptoToFiatAsync(
        ConvertRequest request, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<ConvertResponse>(
            "/v1/currencies/convert/crypto-fiat", request, cancellationToken);
}
