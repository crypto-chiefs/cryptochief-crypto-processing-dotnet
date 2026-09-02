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

    /// <summary>
    /// Every pay-in that used one deposit address — the same orders
    /// <c>PayInsService.HistoryAsync</c> returns, narrowed to a single wallet.
    /// </summary>
    /// <remarks>
    /// Useful when a payer says they sent funds and you have the address but not the order:
    /// a deposit wallet can serve several orders over its lifetime, and this is the list of
    /// them.
    /// <para>Only orders belonging to the project are returned — an address it does not own
    /// yields an empty page rather than an error.</para>
    /// </remarks>
    public Task<PayInHistoryResponse> HistoryAsync(WalletHistoryQuery query, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<PayInHistoryResponse>(
            "/v1/wallets/history", query, cancellationToken);

    /// <summary>Toggles the frozen flag. Read <see cref="Wallet.Frozen"/> to know the new state.</summary>
    public Task<Wallet> FreezeAsync(string address, CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<Wallet>(
            "/v1/wallets/freeze", new { address }, cancellationToken);

    /// <summary>
    /// Re-point a transit or static wallet at another master wallet of the same project.
    /// Returns the wallet as it stands afterwards.
    /// </summary>
    /// <remarks>
    /// This moves no money. It changes where the next sweep settles — including sweeps
    /// already queued, which will land on the new master — while anything already swept
    /// stays on the previous one.
    /// <para>Idempotent: a wallet already bound to that master answers 200 and changes
    /// nothing. A master wallet cannot be re-pointed, and the new master must be of the
    /// same chain family and not frozen.</para>
    /// </remarks>
    /// <param name="address">The transit or static wallet to re-point.</param>
    /// <param name="masterWalletAddress">The master wallet it should sweep to from now on.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Wallet> RebindMasterAsync(
        string address,
        string masterWalletAddress,
        CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<Wallet>(
            "/v1/wallets/rebind-master",
            new { address, master_wallet_address = masterWalletAddress },
            cancellationToken);

    /// <summary>
    /// Set or clear the deposit webhook of a static wallet after it was created. Returns
    /// the wallet as it stands afterwards.
    /// </summary>
    /// <remarks>
    /// An empty <paramref name="callbackUrl"/> clears the webhook. That is a value, not an
    /// omission, so it goes on the wire as <c>"callback_url": ""</c> rather than being left
    /// out — the two say different things to the platform.
    /// <para>Static wallets only: master and transit wallets are refused with 400. A
    /// deposit already announced is not announced again to the new URL.</para>
    /// </remarks>
    /// <param name="address">The static wallet whose webhook is being written.</param>
    /// <param name="callbackUrl">The new webhook URL, or an empty string to clear it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Wallet> SetCallbackUrlAsync(
        string address,
        string callbackUrl,
        CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<Wallet>(
            "/v1/wallets/callback-url",
            new
            {
                address,
                // Never null: null would be dropped from the body by the serializer, and an
                // absent callback_url is not the "clear it" the caller asked for.
                callback_url = string.IsNullOrEmpty(callbackUrl) ? string.Empty : callbackUrl,
            },
            cancellationToken);

    /// <summary>
    /// Set or clear the human-readable name of a wallet after it was created. Returns the
    /// wallet as it stands afterwards.
    /// </summary>
    /// <remarks>
    /// An empty <paramref name="label"/> clears the name. That is a value, not an omission,
    /// so it goes on the wire as <c>"label": ""</c> rather than being left out — the two say
    /// different things to the platform.
    /// <para>Every wallet type can be renamed — master, transit and static alike — unlike
    /// the deposit webhook, which is static-only. A name over 255 characters is refused with
    /// <c>LABEL_TOO_LONG</c>.</para>
    /// </remarks>
    /// <param name="address">The wallet being renamed.</param>
    /// <param name="label">The new name, or an empty string to clear it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Wallet> SetLabelAsync(
        string address,
        string label,
        CancellationToken cancellationToken = default) =>
        _client.Transport.SendAsync<Wallet>(
            "/v1/wallets/label",
            new
            {
                address,
                // Never null: null would be dropped from the body by the serializer, and an
                // absent label is not the "clear it" the caller asked for.
                label = string.IsNullOrEmpty(label) ? string.Empty : label,
            },
            cancellationToken);

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
