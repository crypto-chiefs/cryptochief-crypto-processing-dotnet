using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CryptoChief.Processing.Http;

/// <summary>
/// HMAC-SHA256 v1 signatures: API requests (<see cref="SignHmacV1"/>) and webhooks
/// (<see cref="SignWebhookV1"/>).
/// </summary>
public static class RequestSigner
{
    /// <summary>First line of the request string to sign.</summary>
    public const string HmacV1Scope = "CC-HMAC-SHA256-REQ-V1";

    /// <summary>First line of the webhook string to sign.</summary>
    public const string WebhookV1Scope = "CC-HMAC-SHA256-WEBHOOK-V1";

    /// <summary>Prefix of the <c>X-CC-Signature</c> header value.</summary>
    public const string HmacV1SignaturePrefix = "v1=";

    /// <summary>
    /// HMAC v1 string to sign: <c>CC-HMAC-SHA256-REQ-V1</c>, timestamp, nonce, METHOD, path,
    /// query, merchant, idempotency key, lowercase hex SHA-256 of the body — joined with
    /// <c>\n</c>, no trailing newline.
    /// </summary>
    /// <exception cref="ArgumentException">A field contains CR or LF.</exception>
    public static string HmacV1StringToSign(HmacV1Input input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var fields = new[]
        {
            input.Timestamp ?? string.Empty,
            input.Nonce ?? string.Empty,
            UpperAsciiMethod(input.Method ?? string.Empty),
            input.Path ?? string.Empty,
            input.Query ?? string.Empty,
            input.Merchant ?? string.Empty,
            input.IdempotencyKey ?? string.Empty,
        };

        var sb = new StringBuilder(HmacV1Scope);
        foreach (var field in fields)
        {
            if (field.IndexOfAny(LineBreaks) >= 0)
                throw new ArgumentException("HMAC v1 field contains CR or LF", nameof(input));
            sb.Append('\n').Append(field);
        }
        sb.Append('\n').Append(BodySha256(input.Body.Span));
        return sb.ToString();
    }

    /// <summary>
    /// HMAC v1 signature: lowercase hex <c>HMAC-SHA256(key = UTF-8 apiKey, message = HmacV1StringToSign(input))</c>.
    /// The <c>X-CC-Signature</c> header value is <c>"v1=" + signature</c>.
    /// </summary>
    /// <exception cref="ArgumentException">Blank API key, or a field contains CR or LF.</exception>
    public static string SignHmacV1(string apiKey, HmacV1Input input)
    {
        if (IsBlankApiKey(apiKey))
            throw new ArgumentException("API key is required", nameof(apiKey));

        var message = Encoding.UTF8.GetBytes(HmacV1StringToSign(input));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiKey));
        return ToHexLower(hmac.ComputeHash(message));
    }

    /// <summary>
    /// Webhook string to sign: <c>CC-HMAC-SHA256-WEBHOOK-V1</c>, timestamp, delivery id,
    /// lowercase hex SHA-256 of the body — joined with <c>\n</c>, no trailing newline.
    /// </summary>
    /// <param name="timestamp"><c>X-CC-Timestamp</c>: Unix time in seconds.</param>
    /// <param name="deliveryId"><c>X-Webhook-Delivery</c>: 1–128 characters <c>[A-Za-z0-9_-]</c>.</param>
    /// <param name="body">Body bytes exactly as sent.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timestamp"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="deliveryId"/> is not in the format.</exception>
    public static string WebhookV1StringToSign(long timestamp, string deliveryId, ReadOnlySpan<byte> body)
    {
        if (timestamp <= 0)
            throw new ArgumentOutOfRangeException(nameof(timestamp), timestamp, "Timestamp must be positive");
        if (!IsValidDeliveryId(deliveryId))
            throw new ArgumentException("Delivery id must be 1-128 characters [A-Za-z0-9_-]", nameof(deliveryId));

        return new StringBuilder(WebhookV1Scope)
            .Append('\n').Append(timestamp.ToString(CultureInfo.InvariantCulture))
            .Append('\n').Append(deliveryId)
            .Append('\n').Append(BodySha256(body))
            .ToString();
    }

    /// <summary>
    /// Webhook signature as the <c>X-CC-Signature</c> header value:
    /// <c>"v1=" + lowercase hex HMAC-SHA256(key = UTF-8 apiKey, message = WebhookV1StringToSign(...))</c>.
    /// </summary>
    /// <exception cref="ArgumentException">Blank API key, or <paramref name="deliveryId"/> is not in the format.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timestamp"/> is not positive.</exception>
    public static string SignWebhookV1(string apiKey, long timestamp, string deliveryId, ReadOnlySpan<byte> body)
    {
        if (IsBlankApiKey(apiKey))
            throw new ArgumentException("API key is required", nameof(apiKey));

        var message = WebhookV1StringToSign(timestamp, deliveryId, body);
        return HmacV1SignaturePrefix + ToHexLower(WebhookMac(apiKey, message));
    }

    /// <summary>
    /// The method as it is signed: <c>a</c>–<c>z</c> upper-cased, every other character as it is.
    /// An HTTP method is an RFC 9110 token; a Unicode mapping would rewrite characters outside
    /// that range (<c>ı</c> to <c>I</c>, <c>ß</c> to <c>SS</c>) and the signature would stop
    /// matching the server's.
    /// </summary>
    public static string UpperAsciiMethod(string method)
    {
        if (string.IsNullOrEmpty(method)) return string.Empty;

        char[]? upper = null;
        for (var i = 0; i < method.Length; i++)
        {
            if (method[i] is not (>= 'a' and <= 'z')) continue;
            upper ??= method.ToCharArray();
            upper[i] = (char)(method[i] - 32);
        }
        return upper is null ? method : new string(upper);
    }

    /// <summary>
    /// An API key of nothing, or of spaces and tabs only, is no key: signing with it is an error
    /// and the server refuses the request.
    /// </summary>
    public static bool IsBlankApiKey(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey)) return true;
        foreach (var c in apiKey!)
        {
            if (c is not (' ' or '\t')) return false;
        }
        return true;
    }

    internal static byte[] WebhookMac(string apiKey, string stringToSign)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiKey));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
    }

    internal static bool IsValidDeliveryId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128) return false;
        foreach (var c in value)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-'))
                return false;
        }
        return true;
    }

    /// <summary>Lowercase hex SHA-256 of the body bytes.</summary>
    public static string BodySha256(ReadOnlySpan<byte> body) => ToHexLower(SHA256.HashData(body));

    /// <summary>New <c>X-CC-Nonce</c>: 32 lowercase hex characters from 16 random bytes.</summary>
    public static string NewNonce() => ToHexLower(RandomNumberGenerator.GetBytes(16));

    private static readonly char[] LineBreaks = { '\r', '\n' };

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

/// <summary>Fields of the HMAC v1 string to sign.</summary>
public sealed class HmacV1Input
{
    /// <summary><c>X-CC-Timestamp</c>: Unix time in seconds, decimal.</summary>
    public string Timestamp { get; init; } = string.Empty;

    /// <summary><c>X-CC-Nonce</c>.</summary>
    public string Nonce { get; init; } = string.Empty;

    /// <summary>HTTP method; signed upper-cased over <c>a</c>–<c>z</c> only.</summary>
    public string Method { get; init; } = "POST";

    /// <summary>Route path from <c>/v1/</c>, percent-decoded as the server reads it,
    /// without query and base URL prefix.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Query string as the URL carries it, without <c>?</c>; empty when there is none.</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary><c>Merchant</c> header value.</summary>
    public string Merchant { get; init; } = string.Empty;

    /// <summary><c>Idempotency-Key</c> header value; empty when not sent.</summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>Body bytes exactly as sent.</summary>
    public ReadOnlyMemory<byte> Body { get; init; }
}
