using System.Security.Cryptography;
using System.Text;

namespace CryptoChief.Processing.Http;

/// <summary>
/// Signs Crypto Chief API requests. Algorithm: <c>hex(md5(base64(canonicalJson(body)) + apiKey))</c>.
/// </summary>
public static class RequestSigner
{
    public static string Sign(ReadOnlySpan<byte> canonicalBody, string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new ArgumentException("API key is required", nameof(apiKey));

        var b64 = Convert.ToBase64String(canonicalBody);
        var concat = Encoding.UTF8.GetBytes(b64 + apiKey);
#if NET8_0_OR_GREATER
        var hash = MD5.HashData(concat);
#else
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(concat);
#endif
        return ToHexLower(hash);
    }

    private static string ToHexLower(byte[] bytes)
    {
#if NET8_0_OR_GREATER
        return Convert.ToHexString(bytes).ToLowerInvariant();
#else
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
#endif
    }
}
