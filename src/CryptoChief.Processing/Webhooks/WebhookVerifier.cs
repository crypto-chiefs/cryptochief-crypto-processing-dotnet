using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CryptoChief.Processing.Errors;
using CryptoChief.Processing.Http;
using CryptoChief.Processing.Internal;

namespace CryptoChief.Processing.Webhooks;

/// <summary>Verifies the signature on inbound webhooks (same algorithm as outgoing requests).</summary>
public static class WebhookVerifier
{
    public const string SignatureHeader = "Signature";

    /// <summary>IP addresses webhooks are delivered from. Whitelist at your edge.</summary>
    public static readonly IReadOnlyList<string> SenderIps = new[]
    {
        "164.90.231.203",
        "104.248.248.64",
    };

    /// <summary>Throws <see cref="CryptoChiefException"/> on signature mismatch.</summary>
    public static void Verify(string apiKey, ReadOnlySpan<byte> body, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new ArgumentException("API key is required", nameof(apiKey));
        if (body.IsEmpty || string.IsNullOrEmpty(signatureHeader))
            throw new CryptoChiefException("cryptochief: invalid webhook signature");

        byte[] canonical;
        try { canonical = CanonicalJson.Canonicalise(body); }
        catch (JsonException ex)
        {
            throw new CryptoChiefException(
                $"cryptochief: webhook body is not JSON: {ex.Message}", ex);
        }

        var expected = RequestSigner.Sign(canonical, apiKey);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var headerBytes = Encoding.UTF8.GetBytes(signatureHeader);
        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, headerBytes))
            throw new CryptoChiefException("cryptochief: invalid webhook signature");
    }

    /// <summary>Non-throwing variant.</summary>
    public static bool TryVerify(string apiKey, ReadOnlySpan<byte> body, string? signatureHeader)
    {
        try { Verify(apiKey, body, signatureHeader); return true; }
        catch (Exception ex) when (ex is CryptoChiefException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Verify + deserialize the JSON body in one call.</summary>
    public static T VerifyAndDecode<T>(string apiKey, ReadOnlySpan<byte> body, string? signatureHeader)
    {
        Verify(apiKey, body, signatureHeader);
        return JsonSerializer.Deserialize<T>(body, JsonDefaults.Options)
            ?? throw new CryptoChiefException("cryptochief: webhook body decoded as null");
    }
}
