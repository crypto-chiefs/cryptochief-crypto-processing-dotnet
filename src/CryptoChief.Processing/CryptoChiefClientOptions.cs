using System.Security.Cryptography;

namespace CryptoChief.Processing;

/// <summary>Options for <see cref="CryptoChiefClient"/>.</summary>
public sealed class CryptoChiefClientOptions
{
    public const string DefaultBaseUrl = "https://api-processing.crypto-chief.com";
    public const string DefaultTonRpcBaseUrl = "https://rpc.crypto-chief.com";

    public string MerchantId { get; set; } = string.Empty;

    /// <summary>API key from the dashboard. Used as the signing secret — keep server-side.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = DefaultBaseUrl;
    public string TonRpcBaseUrl { get; set; } = DefaultTonRpcBaseUrl;
    public string UserAgent { get; set; } = $"cryptochief-dotnet/{CryptoChiefClient.Version}";

    /// <summary>HTTP timeout per request. Default 60 s — fits batch payout's longer hold time.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Retries on transport failure and 5xx. 0 disables.</summary>
    public int MaxRetries { get; set; } = 3;

    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>RSA private key used to decrypt the <c>private_key_encrypted</c> field on generated wallets.</summary>
    public RSA? RsaPrivateKey { get; set; }

    public CryptoChiefClientOptions LoadRsaPrivateKeyFromFile(string path)
    {
        RsaPrivateKey = Rsa.RsaKeyLoader.LoadPrivateKeyFromFile(path);
        return this;
    }

    public CryptoChiefClientOptions LoadRsaPrivateKeyFromPem(string pem)
    {
        RsaPrivateKey = Rsa.RsaKeyLoader.LoadPrivateKeyFromPem(pem);
        return this;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(MerchantId))
            throw new InvalidOperationException("CryptoChiefClientOptions.MerchantId is required.");
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("CryptoChiefClientOptions.ApiKey is required.");
        if (string.IsNullOrWhiteSpace(BaseUrl))
            throw new InvalidOperationException("CryptoChiefClientOptions.BaseUrl is required.");
        if (MaxRetries < 0)
            throw new InvalidOperationException("CryptoChiefClientOptions.MaxRetries cannot be negative.");
    }
}
