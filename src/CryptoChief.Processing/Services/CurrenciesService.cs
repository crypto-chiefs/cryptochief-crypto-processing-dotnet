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

    /// <summary>
    /// Every fiat currency the platform can price an order in and quote a rate against.
    /// </summary>
    /// <remarks>The API answers with a bare JSON array, not an <c>items</c> envelope. An
    /// empty answer reaches the wire as <c>null</c> rather than <c>[]</c>, and comes back
    /// here as an empty list either way — never null.</remarks>
    public async Task<IReadOnlyList<FiatCurrency>> FiatsAsync(
        CancellationToken cancellationToken = default) =>
        await _client.Transport.SendAsync<IReadOnlyList<FiatCurrency>?>(
            "/v1/currencies/fiats", new { }, cancellationToken).ConfigureAwait(false)
        ?? Array.Empty<FiatCurrency>();

    /// <summary>
    /// Every crypto ticker the platform has a rate for, against USDT, and which exchange
    /// each one comes from.
    /// </summary>
    /// <remarks>
    /// Rate availability only: a ticker here can be quoted, which does not mean the
    /// platform takes deposits, sweeps or payouts in it. For that, read
    /// <c>BlockchainService.ContractsAvailableAsync</c>.
    /// <para>An empty answer — the whole body, the ticker list, or one exchange's tickers —
    /// reaches the wire as <c>null</c> rather than <c>[]</c>. It comes back here as an empty
    /// collection either way, so <see cref="CryptoCurrencies.Tickers"/> and
    /// <see cref="CryptoCurrencies.ByExchange"/> are safe to enumerate without a null
    /// check.</para>
    /// </remarks>
    public async Task<CryptoCurrencies> CryptosAsync(
        CancellationToken cancellationToken = default) =>
        await _client.Transport.SendAsync<CryptoCurrencies?>(
            "/v1/currencies/cryptos", new { }, cancellationToken).ConfigureAwait(false)
        ?? new CryptoCurrencies();
}
