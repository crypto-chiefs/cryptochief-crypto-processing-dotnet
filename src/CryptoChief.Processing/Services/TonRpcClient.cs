using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using CryptoChief.Processing.Encoders.Ton;
using CryptoChief.Processing.Internal;

namespace CryptoChief.Processing.Services;

internal sealed class TonRpcClient
{
    private readonly string _baseUrl;
    private readonly string _merchantId;
    private readonly HttpClient _http;
    private readonly string _userAgent;
    private readonly ConcurrentDictionary<string, string> _cache = new();

    public TonRpcClient(string merchantId, string baseUrl, HttpClient http, string userAgent)
    {
        _merchantId = merchantId;
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? CryptoChiefClientOptions.DefaultTonRpcBaseUrl
            : baseUrl.TrimEnd('/');
        _http = http;
        _userAgent = userAgent;
    }

    public async Task<string> LookupJettonWalletAsync(
        string jettonMaster, string owner, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(jettonMaster) || string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("jettonMaster and owner required");

        var key = owner + "|" + jettonMaster;
        if (_cache.TryGetValue(key, out var cached)) return cached;

        try
        {
            var via = await JettonWalletViaRunMethodAsync(jettonMaster, owner, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(via))
            {
                _cache.TryAdd(key, via);
                return via;
            }
        }
        catch { }

        var fallback = await JettonWalletViaIndexAsync(jettonMaster, owner, ct).ConfigureAwait(false);
        _cache.TryAdd(key, fallback);
        return fallback;
    }

    // Returns false on any error — caller treats that as "use the higher gas budget" (safe default).
    public async Task<bool> HasJettonWalletAsync(
        string jettonMaster, string owner, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var q = $"owner_address={Uri.EscapeDataString(owner)}"
                  + $"&jetton_address={Uri.EscapeDataString(jettonMaster)}&limit=1";
            using var resp = await GetAsync($"/jetton/wallets?{q}", cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return false;
            using var doc = await JsonDocument.ParseAsync(
                await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false), cancellationToken: cts.Token).ConfigureAwait(false);
            return doc.RootElement.TryGetProperty("jetton_wallets", out var arr)
                && arr.ValueKind == JsonValueKind.Array
                && arr.GetArrayLength() > 0;
        }
        catch { return false; }
    }

    private async Task<string?> JettonWalletViaRunMethodAsync(
        string jettonMaster, string owner, CancellationToken ct)
    {
        var ownerCell = new TonCell().StoreAddress(TonAddress.Parse(owner));
        var ownerBoc = Convert.ToBase64String(ownerCell.ToBoc());

        var body = new
        {
            address = jettonMaster,
            method = "get_wallet_address",
            stack = new[] { new { type = "slice", value = ownerBoc } },
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(body, JsonDefaults.Options);
        using var resp = await PostAsync("/runGetMethod", json, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;

        var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.TryGetProperty("exit_code", out var ec) && ec.GetInt32() != 0) return null;
        if (!root.TryGetProperty("stack", out var stack) || stack.ValueKind != JsonValueKind.Array
            || stack.GetArrayLength() == 0)
            return null;

        // We don't ship a BoC parser; defer to the index endpoint to extract the wallet address.
        return null;
    }

    private async Task<string> JettonWalletViaIndexAsync(
        string jettonMaster, string owner, CancellationToken ct)
    {
        var q = $"owner_address={Uri.EscapeDataString(owner)}"
              + $"&jetton_address={Uri.EscapeDataString(jettonMaster)}&limit=1";
        using var resp = await GetAsync($"/jetton/wallets?{q}", ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"cryptochief/ton: /jetton/wallets HTTP {(int)resp.StatusCode}");

        var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;
        if (!root.TryGetProperty("jetton_wallets", out var arr) || arr.GetArrayLength() == 0)
            throw new InvalidOperationException(
                $"cryptochief/ton: no Jetton wallet found for owner {owner} on master {jettonMaster}"
                + " — the owner has never received this Jetton.");
        var rawAddr = arr[0].GetProperty("address").GetString()!;
        if (root.TryGetProperty("address_book", out var book) &&
            book.ValueKind == JsonValueKind.Object &&
            book.TryGetProperty(rawAddr, out var info) &&
            info.TryGetProperty("user_friendly", out var uf) &&
            uf.ValueKind == JsonValueKind.String &&
            !string.IsNullOrEmpty(uf.GetString()))
            return uf.GetString()!;
        return rawAddr;
    }

    private string UrlFor(string path) =>
        $"{_baseUrl}/ton-v3/{_merchantId}/{path.TrimStart('/')}";

    private async Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, UrlFor(path));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.UserAgent.ParseAdd(_userAgent);
        return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> PostAsync(string path, byte[] body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, UrlFor(path))
        {
            Content = new ByteArrayContent(body)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
            },
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.UserAgent.ParseAdd(_userAgent);
        return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
    }
}
