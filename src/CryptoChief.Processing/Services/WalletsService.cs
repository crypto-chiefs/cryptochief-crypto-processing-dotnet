using CryptoChief.Processing.Errors;
using CryptoChief.Processing.Models;
using CryptoChief.Processing.Rsa;

namespace CryptoChief.Processing.Services;

public sealed class WalletsService
{
    private readonly CryptoChiefClient _client;
    internal WalletsService(CryptoChiefClient client) => _client = client;

    public Task<Wallet> GenerateAsync(GenerateWalletRequest request, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<Wallet>(
            "/v1/wallets/generate", request, cancellationToken);

    public Task<ListWalletsResponse> ListAsync(CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<ListWalletsResponse>(
            "/v1/wallets/list", new { }, cancellationToken);

    public Task<Wallet> InfoAsync(string address, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<Wallet>(
            "/v1/wallets/info", new { address }, cancellationToken);

    /// <summary>Toggles the frozen flag. Read <see cref="Wallet.Frozen"/> to know the new state.</summary>
    public Task<Wallet> FreezeAsync(string address, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<Wallet>(
            "/v1/wallets/freeze", new { address }, cancellationToken);

    /// <summary>
    /// Decrypt <see cref="Wallet.PrivateKeyEncrypted"/> with the RSA private key
    /// configured on the client. Returns the chain-native hex private key.
    /// </summary>
    public string DecryptPrivateKey(string encrypted)
    {
        var key = _client.Options.RsaPrivateKey
            ?? throw new CryptoChiefException(
                "cryptochief: RSA private key not configured — set CryptoChiefClientOptions.RsaPrivateKey "
                + "or call LoadRsaPrivateKeyFromFile/LoadRsaPrivateKeyFromPem before constructing the client.");
        return RsaDecrypt.DecryptOaepSha256(key, encrypted);
    }
}
