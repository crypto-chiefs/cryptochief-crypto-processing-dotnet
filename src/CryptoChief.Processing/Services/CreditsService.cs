using CryptoChief.Processing.Models;

namespace CryptoChief.Processing.Services;

public sealed class CreditsService
{
    private readonly CryptoChiefClient _client;
    internal CreditsService(CryptoChiefClient client) => _client = client;

    /// <summary>
    /// Current project credits balance. Billing-exempt — never consumes a paid call — and
    /// answers even at zero or negative balance. Rate-limited to 60 req/min per project.
    /// </summary>
    public Task<CreditsBalance> BalanceAsync(CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<CreditsBalance>(
            "/v1/credits/balance", new { }, cancellationToken);

    /// <summary>
    /// Create a credits top-up invoice and get a hosted payment link (QR, network selection,
    /// live status). Billing-exempt — never consumes a paid call. Rate-limited to 60 req/min per project.
    /// </summary>
    public Task<CreditsTopup> TopupAsync(
        CreditsTopupRequest request, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<CreditsTopup>(
            "/v1/credits/topup", request, cancellationToken);
}
