using System.Security.Cryptography;
using System.Text;

namespace CryptoChief.Processing.Tests;

/// <summary>
/// The gateway's HMAC v1 check, as processing-api-gateway/internal/auth implements it.
/// <para>The verifying side the SDK is tested against: the vector-driven test and the mock
/// gateway the client talks to over HTTP both go through <see cref="Check"/>. The string to sign
/// is built here from the received request, not with the SDK's signer.</para>
/// </summary>
internal static class GatewayHmacV1
{
    public const string Scope = "CC-HMAC-SHA256-REQ-V1";
    public const string SignaturePrefix = "v1=";
    public const int Window = 300;

    public const string MerchantHeader = "Merchant";
    public const string TimestampHeader = "X-CC-Timestamp";
    public const string NonceHeader = "X-CC-Nonce";
    public const string SignatureHeader = "X-CC-Signature";
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string ContentTypeHeader = "Content-Type";

    // Outcomes, named as the vector file's "expect".
    public const string Ok = "ok";
    public const string BadAuthHeaders = "bad_auth_headers";
    public const string TimestampOutOfRange = "timestamp_out_of_range";
    public const string InvalidSignature = "invalid_signature";

    private const int TimestampMaxDigits = 18;

    /// <summary>What the server reads: <paramref name="Path"/> percent-decoded,
    /// <paramref name="Query"/> raw, the body as received, every header in order with one entry
    /// per repeat.</summary>
    internal sealed record SignedRequest(
        string Method,
        string Path,
        string Query,
        byte[] Body,
        IReadOnlyList<KeyValuePair<string, string>> Headers);

    /// <summary>The string to sign, or null when a field carries CR or LF.</summary>
    public static string? StringToSign(
        string timestamp, string nonce, string method, string path, string query,
        string merchant, string idempotencyKey, byte[] body)
    {
        var fields = new[] { timestamp, nonce, UpperAscii(method), path, query, merchant, idempotencyKey };
        foreach (var field in fields)
        {
            if (field.IndexOf('\r') >= 0 || field.IndexOf('\n') >= 0) return null;
        }
        return string.Join("\n", new[] { Scope }.Concat(fields).Append(Sha256Hex(body)));
    }

    /// <summary>Headers, timestamp window, key and signature; one of the outcome constants.
    /// Nonce replay is the caller's — the mock gateway keeps the used nonces.</summary>
    public static string Check(SignedRequest request, string apiKey, long now)
    {
        if (!Single(request.Headers, MerchantHeader, out var merchant)
            || !Single(request.Headers, TimestampHeader, out var timestamp)
            || !Single(request.Headers, NonceHeader, out var nonce)
            || !Single(request.Headers, SignatureHeader, out var signature)
            || !Single(request.Headers, IdempotencyKeyHeader, out var idempotencyKey))
            return BadAuthHeaders;

        if (merchant.Length == 0 || !IsDecimal(timestamp, TimestampMaxDigits) || !IsNonce(nonce))
            return BadAuthHeaders;

        if (!signature.StartsWith(SignaturePrefix, StringComparison.Ordinal)) return BadAuthHeaders;
        var hex = signature.Substring(SignaturePrefix.Length);
        if (hex.Length != 64 || !hex.All(IsHex)) return BadAuthHeaders;

        if (request.Body.Length > 0 && !IsJsonContentType(First(request.Headers, ContentTypeHeader)))
            return BadAuthHeaders;

        var stringToSign = StringToSign(timestamp, nonce, request.Method, request.Path, request.Query,
            merchant, idempotencyKey, request.Body);
        if (stringToSign is null) return BadAuthHeaders;

        if (Math.Abs(now - long.Parse(timestamp, System.Globalization.CultureInfo.InvariantCulture)) > Window)
            return TimestampOutOfRange;

        // A project whose api_key is empty or only spaces and tabs never passes.
        if (apiKey.Trim(' ', '\t').Length == 0) return InvalidSignature;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiKey));
        var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        return CryptographicOperations.FixedTimeEquals(expected, FromHex(hex)) ? Ok : InvalidSignature;
    }

    /// <summary>Upper-cases <c>a</c>–<c>z</c> and leaves every other character alone.</summary>
    public static string UpperAscii(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] is >= 'a' and <= 'z') chars[i] = (char)(chars[i] - 32);
        return new string(chars);
    }

    public static string Sha256Hex(byte[] body) =>
        Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();

    /// <summary>The only value of a header without surrounding spaces and tabs. False when the
    /// header is repeated; an absent header is an empty value.</summary>
    private static bool Single(IReadOnlyList<KeyValuePair<string, string>> headers, string name, out string value)
    {
        value = string.Empty;
        var count = 0;
        foreach (var (key, v) in headers)
        {
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (++count > 1) return false;
            value = v.Trim(' ', '\t');
        }
        return true;
    }

    private static string First(IReadOnlyList<KeyValuePair<string, string>> headers, string name)
    {
        foreach (var (key, value) in headers)
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return value;
        return string.Empty;
    }

    private static bool IsDecimal(string value, int maxLength) =>
        value.Length > 0
        && value.Length <= maxLength
        && !(value.Length > 1 && value[0] == '0')
        && value.All(c => c is >= '0' and <= '9');

    private static bool IsNonce(string value) =>
        value.Length is >= 16 and <= 64
        && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_' or '-');

    /// <summary><c>application/json</c> ignoring ASCII case, with parameters that parse.</summary>
    private static bool IsJsonContentType(string value)
    {
        var semicolon = value.IndexOf(';');
        var baseType = semicolon < 0 ? value : value.Substring(0, semicolon);
        if (!baseType.Trim(' ', '\t').Equals("application/json", StringComparison.OrdinalIgnoreCase)) return false;

        var rest = semicolon < 0 ? "" : value.Substring(semicolon);
        while (rest.Trim().Length > 0)
        {
            if (rest.Trim() == ";") break; // a trailing semicolon is ignored
            var consumed = ParameterLength(rest);
            if (consumed <= 0) return false;
            rest = rest.Substring(consumed);
        }
        return true;
    }

    // One media-type parameter: "; token=token" or "; token=\"quoted\"". Length consumed, or 0.
    private static int ParameterLength(string s)
    {
        var i = 0;
        while (i < s.Length && IsSpace(s[i])) i++;
        if (i >= s.Length || s[i] != ';') return 0;
        i++;
        while (i < s.Length && IsSpace(s[i])) i++;

        var nameStart = i;
        while (i < s.Length && IsToken(s[i])) i++;
        if (i == nameStart) return 0;

        while (i < s.Length && IsSpace(s[i])) i++;
        if (i >= s.Length || s[i] != '=') return 0;
        i++;
        while (i < s.Length && IsSpace(s[i])) i++;

        if (i < s.Length && s[i] == '"')
        {
            i++;
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length) i++;
                i++;
            }
            if (i >= s.Length) return 0;
            return i + 1;
        }

        var valueStart = i;
        while (i < s.Length && IsToken(s[i])) i++;
        return i == valueStart ? 0 : i;
    }

    private static bool IsSpace(char c) => c is ' ' or '\t';

    private static bool IsToken(char c) =>
        c is (>= '0' and <= '9') or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z')
            or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.'
            or '^' or '_' or '`' or '|' or '~';

    private static bool IsHex(char c) =>
        c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

    private static byte[] FromHex(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(2 * i, 2), 16);
        return bytes;
    }
}
