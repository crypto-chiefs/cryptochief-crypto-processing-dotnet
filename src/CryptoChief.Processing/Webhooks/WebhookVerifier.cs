using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using CryptoChief.Processing.Errors;
using CryptoChief.Processing.Http;
using CryptoChief.Processing.Internal;
using Microsoft.Extensions.Primitives;

namespace CryptoChief.Processing.Webhooks;

/// <summary>
/// Verifies inbound webhooks signed with HMAC-SHA256 v1.
/// </summary>
/// <remarks>
/// <code>
/// string_to_sign = "CC-HMAC-SHA256-WEBHOOK-V1" \n X-CC-Timestamp \n X-Webhook-Delivery \n hex(sha256(body))
/// X-CC-Signature = "v1=" + hex(hmac_sha256(key = apiKey, message = string_to_sign))
/// </code>
/// Checks, in order:
/// <list type="number">
/// <item><see cref="TimestampHeader"/>, <see cref="DeliveryHeader"/> and <see cref="SignatureHeader"/>
/// each present once and in format; values trimmed of spaces and tabs only; CR or LF is
/// rejected — otherwise <see cref="WebhookHeadersException"/>.</item>
/// <item>|now − timestamp| ≤ tolerance — otherwise <see cref="WebhookTimestampException"/>.</item>
/// <item>HMAC compared in constant time, hex in any case — otherwise <see cref="WebhookSignatureException"/>.</item>
/// </list>
/// The body is the raw bytes as received, before any JSON parsing. A body held as a string:
/// <c>Encoding.UTF8.GetBytes(body)</c>.
/// </remarks>
public static class WebhookVerifier
{
    /// <summary>
    /// Delivery id, 1–128 characters <c>[A-Za-z0-9_-]</c>. The same on every attempt and resend
    /// of one delivery — use it as the receiver's idempotency key and as the argument of
    /// <c>client.Webhooks.InfoAsync</c> / <c>ResendAsync</c>.
    /// </summary>
    public const string DeliveryHeader = "X-Webhook-Delivery";

    /// <summary>Unix time of the attempt's signature, seconds, decimal without leading zeros.</summary>
    public const string TimestampHeader = "X-CC-Timestamp";

    /// <summary><c>v1=</c> + 64 hex characters.</summary>
    public const string SignatureHeader = "X-CC-Signature";

    /// <summary>Default allowed difference between <see cref="TimestampHeader"/> and the receiver's clock.</summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromSeconds(300);

    /// <summary>IP addresses the processing platform delivers webhooks from. Whitelist at your edge.</summary>
    public static readonly IReadOnlyList<string> SenderIps = new[]
    {
        "164.90.231.203",
        "104.248.248.64",
    };

    /// <summary>
    /// Verifies a webhook. <paramref name="header"/> returns a header value by name, or null when
    /// the header is absent.
    /// </summary>
    /// <remarks>
    /// One string cannot show every copy of a repeated header: an empty copy is not seen, so a
    /// request with it may pass. ASP.NET Core: pass <c>req.Headers</c> to the overload that takes
    /// header collections, not <c>name =&gt; req.Headers[name]</c>.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> is empty or only spaces and tabs.</exception>
    /// <exception cref="WebhookHeadersException">A signature header is missing, repeated or not in format.</exception>
    /// <exception cref="WebhookTimestampException">The timestamp is outside the tolerance.</exception>
    /// <exception cref="WebhookSignatureException">The signature does not match.</exception>
    public static void Verify(string apiKey, ReadOnlySpan<byte> rawBody, Func<string, string?> header,
        WebhookVerifyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        VerifyCore(apiKey, rawBody, name => header(name) is { } v ? new string?[] { v } : Array.Empty<string?>(), options);
    }

    /// <summary>Verifies a webhook. Header names are compared case-insensitively; every value under
    /// every spelling of a name counts.</summary>
    /// <inheritdoc cref="Verify(string, ReadOnlySpan{byte}, Func{string, string}, WebhookVerifyOptions)" path="/exception"/>
    public static void Verify(string apiKey, ReadOnlySpan<byte> rawBody,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers, WebhookVerifyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(headers);
        VerifyCore(apiKey, rawBody, name => Collect(headers, name, static v => v), options);
    }

    /// <summary>Verifies a webhook. Header names are compared case-insensitively; every value under
    /// every spelling of a name counts. ASP.NET Core: <c>req.Headers</c>.</summary>
    /// <inheritdoc cref="Verify(string, ReadOnlySpan{byte}, Func{string, string}, WebhookVerifyOptions)" path="/exception"/>
    public static void Verify(string apiKey, ReadOnlySpan<byte> rawBody,
        IEnumerable<KeyValuePair<string, StringValues>> headers, WebhookVerifyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(headers);
        VerifyCore(apiKey, rawBody, name => Collect(headers, name, static v => v), options);
    }

    /// <summary>Verifies a webhook. Header names are compared case-insensitively; a name given
    /// twice counts as a repeated header.</summary>
    /// <inheritdoc cref="Verify(string, ReadOnlySpan{byte}, Func{string, string}, WebhookVerifyOptions)" path="/exception"/>
    public static void Verify(string apiKey, ReadOnlySpan<byte> rawBody,
        IEnumerable<KeyValuePair<string, string>> headers, WebhookVerifyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(headers);
        VerifyCore(apiKey, rawBody, name => Collect(headers, name, static v => new string?[] { v }), options);
    }

    /// <summary>Verifies a webhook from <see cref="HttpHeaders"/>; values are read without parsing.</summary>
    /// <inheritdoc cref="Verify(string, ReadOnlySpan{byte}, Func{string, string}, WebhookVerifyOptions)" path="/exception"/>
    public static void Verify(string apiKey, ReadOnlySpan<byte> rawBody, HttpHeaders headers,
        WebhookVerifyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(headers);
        VerifyCore(apiKey, rawBody, name => Collect(headers.NonValidated, name, static v => v), options);
    }

    /// <summary>Non-throwing <see cref="Verify(string, ReadOnlySpan{byte}, Func{string, string}, WebhookVerifyOptions)"/>.
    /// False on any refusal and on an <paramref name="apiKey"/> that is empty or only spaces and tabs.</summary>
    /// <inheritdoc cref="Verify(string, ReadOnlySpan{byte}, Func{string, string}, WebhookVerifyOptions)" path="/remarks"/>
    public static bool TryVerify(string apiKey, ReadOnlySpan<byte> rawBody, Func<string, string?> header,
        WebhookVerifyOptions? options = null)
    {
        try { Verify(apiKey, rawBody, header, options); return true; }
        catch (Exception ex) when (ex is WebhookVerificationException or ArgumentException) { return false; }
    }

    /// <inheritdoc cref="TryVerify(string, ReadOnlySpan{byte}, Func{string, string}, WebhookVerifyOptions)" path="/summary"/>
    public static bool TryVerify(string apiKey, ReadOnlySpan<byte> rawBody,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers, WebhookVerifyOptions? options = null)
    {
        try { Verify(apiKey, rawBody, headers, options); return true; }
        catch (Exception ex) when (ex is WebhookVerificationException or ArgumentException) { return false; }
    }

    /// <inheritdoc cref="TryVerify(string, ReadOnlySpan{byte}, Func{string, string}, WebhookVerifyOptions)" path="/summary"/>
    public static bool TryVerify(string apiKey, ReadOnlySpan<byte> rawBody,
        IEnumerable<KeyValuePair<string, StringValues>> headers, WebhookVerifyOptions? options = null)
    {
        try { Verify(apiKey, rawBody, headers, options); return true; }
        catch (Exception ex) when (ex is WebhookVerificationException or ArgumentException) { return false; }
    }

    /// <inheritdoc cref="TryVerify(string, ReadOnlySpan{byte}, Func{string, string}, WebhookVerifyOptions)" path="/summary"/>
    public static bool TryVerify(string apiKey, ReadOnlySpan<byte> rawBody,
        IEnumerable<KeyValuePair<string, string>> headers, WebhookVerifyOptions? options = null)
    {
        try { Verify(apiKey, rawBody, headers, options); return true; }
        catch (Exception ex) when (ex is WebhookVerificationException or ArgumentException) { return false; }
    }

    /// <inheritdoc cref="TryVerify(string, ReadOnlySpan{byte}, Func{string, string}, WebhookVerifyOptions)" path="/summary"/>
    public static bool TryVerify(string apiKey, ReadOnlySpan<byte> rawBody, HttpHeaders headers,
        WebhookVerifyOptions? options = null)
    {
        try { Verify(apiKey, rawBody, headers, options); return true; }
        catch (Exception ex) when (ex is WebhookVerificationException or ArgumentException) { return false; }
    }

    /// <summary>Verifies the webhook, then deserializes the raw body.</summary>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> is empty or only spaces and tabs.</exception>
    /// <exception cref="WebhookVerificationException">The webhook is refused.</exception>
    /// <exception cref="JsonException">The body is not JSON of <typeparamref name="T"/>.</exception>
    /// <exception cref="CryptoChiefException">The body is JSON <c>null</c>.</exception>
    /// <inheritdoc cref="Verify(string, ReadOnlySpan{byte}, Func{string, string}, WebhookVerifyOptions)" path="/remarks"/>
    public static T VerifyAndDecode<T>(string apiKey, ReadOnlySpan<byte> rawBody, Func<string, string?> header,
        WebhookVerifyOptions? options = null)
    {
        Verify(apiKey, rawBody, header, options);
        return Decode<T>(rawBody);
    }

    /// <inheritdoc cref="VerifyAndDecode{T}(string, ReadOnlySpan{byte}, Func{string, string}, WebhookVerifyOptions)" path="/*[not(self::remarks)]"/>
    public static T VerifyAndDecode<T>(string apiKey, ReadOnlySpan<byte> rawBody,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers, WebhookVerifyOptions? options = null)
    {
        Verify(apiKey, rawBody, headers, options);
        return Decode<T>(rawBody);
    }

    /// <inheritdoc cref="VerifyAndDecode{T}(string, ReadOnlySpan{byte}, Func{string, string}, WebhookVerifyOptions)" path="/*[not(self::remarks)]"/>
    public static T VerifyAndDecode<T>(string apiKey, ReadOnlySpan<byte> rawBody,
        IEnumerable<KeyValuePair<string, StringValues>> headers, WebhookVerifyOptions? options = null)
    {
        Verify(apiKey, rawBody, headers, options);
        return Decode<T>(rawBody);
    }

    /// <inheritdoc cref="VerifyAndDecode{T}(string, ReadOnlySpan{byte}, Func{string, string}, WebhookVerifyOptions)" path="/*[not(self::remarks)]"/>
    public static T VerifyAndDecode<T>(string apiKey, ReadOnlySpan<byte> rawBody,
        IEnumerable<KeyValuePair<string, string>> headers, WebhookVerifyOptions? options = null)
    {
        Verify(apiKey, rawBody, headers, options);
        return Decode<T>(rawBody);
    }

    /// <inheritdoc cref="VerifyAndDecode{T}(string, ReadOnlySpan{byte}, Func{string, string}, WebhookVerifyOptions)" path="/*[not(self::remarks)]"/>
    public static T VerifyAndDecode<T>(string apiKey, ReadOnlySpan<byte> rawBody, HttpHeaders headers,
        WebhookVerifyOptions? options = null)
    {
        Verify(apiKey, rawBody, headers, options);
        return Decode<T>(rawBody);
    }

    private static T Decode<T>(ReadOnlySpan<byte> rawBody) =>
        JsonSerializer.Deserialize<T>(rawBody, JsonDefaults.Options)
        ?? throw new CryptoChiefException("cryptochief: webhook body decoded as null");

    private delegate IReadOnlyList<string?> HeaderValues(string name);

    private static void VerifyCore(string apiKey, ReadOnlySpan<byte> rawBody, HeaderValues header,
        WebhookVerifyOptions? options)
    {
        if (RequestSigner.IsBlankApiKey(apiKey))
            throw new ArgumentException("API key is required", nameof(apiKey));

        if (!TrySingle(header(TimestampHeader), out var tsValue) || !TryParseTimestamp(tsValue, out var timestamp))
            throw new WebhookHeadersException($"{TimestampHeader} is missing, repeated or not a decimal number without leading zeros");

        if (!TrySingle(header(DeliveryHeader), out var deliveryId) || !RequestSigner.IsValidDeliveryId(deliveryId))
            throw new WebhookHeadersException($"{DeliveryHeader} is missing, repeated or not 1-128 characters [A-Za-z0-9_-]");

        if (!TrySingle(header(SignatureHeader), out var sigValue) || !TryParseSignature(sigValue, out var given))
            throw new WebhookHeadersException($"{SignatureHeader} is missing, repeated or not v1= followed by 64 hex characters");

        var tolerance = options?.Tolerance ?? DefaultTolerance;
        if (tolerance <= TimeSpan.Zero) tolerance = DefaultTolerance;
        var tol = tolerance.Ticks / TimeSpan.TicksPerSecond;
        var now = (options?.Now ?? DefaultNow)().ToUnixTimeSeconds();
        if (timestamp < now - tol || timestamp > now + tol)
            throw new WebhookTimestampException($"{TimestampHeader} is outside the allowed {tol} seconds");

        if (timestamp <= 0)
            throw new WebhookHeadersException($"{TimestampHeader} is not positive");

        var expected = RequestSigner.WebhookMac(apiKey, RequestSigner.WebhookV1StringToSign(timestamp, deliveryId, rawBody));
        if (!CryptographicOperations.FixedTimeEquals(expected, given))
            throw new WebhookSignatureException($"{SignatureHeader} does not match");
    }

    private static readonly Func<DateTimeOffset> DefaultNow = static () => DateTimeOffset.UtcNow;

    private static readonly char[] SpaceTab = { ' ', '\t' };

    private static bool TrySingle(IReadOnlyList<string?> values, out string value)
    {
        value = string.Empty;
        if (values.Count != 1 || values[0] is not { } raw) return false;
        var v = raw.Trim(SpaceTab);
        if (v.IndexOf('\r') >= 0 || v.IndexOf('\n') >= 0) return false;
        value = v;
        return true;
    }

    private static bool TryParseTimestamp(string s, out long value)
    {
        value = 0;
        if (s.Length == 0) return false;
        if (s.Length > 1 && s[0] == '0') return false;
        foreach (var c in s)
        {
            if (c is < '0' or > '9') return false;
            var digit = c - '0';
            if (value > (long.MaxValue - digit) / 10) return false;
            value = value * 10 + digit;
        }
        return true;
    }

    private static bool TryParseSignature(string s, out byte[] mac)
    {
        mac = Array.Empty<byte>();
        if (!s.StartsWith(RequestSigner.HmacV1SignaturePrefix, StringComparison.Ordinal)) return false;
        var hex = s.AsSpan(RequestSigner.HmacV1SignaturePrefix.Length);
        if (hex.Length != 64) return false;
        var bytes = new byte[32];
        for (var i = 0; i < 32; i++)
        {
            var hi = HexValue(hex[2 * i]);
            var lo = HexValue(hex[2 * i + 1]);
            if (hi < 0 || lo < 0) return false;
            bytes[i] = (byte)((hi << 4) | lo);
        }
        mac = bytes;
        return true;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private static IReadOnlyList<string?> Collect<TValue>(
        IEnumerable<KeyValuePair<string, TValue>> headers, string name, Func<TValue, IEnumerable<string?>?> values)
    {
        var result = new List<string?>(1);
        foreach (var pair in headers)
        {
            if (!string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (values(pair.Value) is { } vs) result.AddRange(vs);
        }
        return result;
    }
}

/// <summary>Options for <see cref="WebhookVerifier"/>.</summary>
public sealed class WebhookVerifyOptions
{
    /// <summary>Allowed difference between <c>X-CC-Timestamp</c> and <see cref="Now"/>, whole seconds.
    /// Zero or negative: <see cref="WebhookVerifier.DefaultTolerance"/>.</summary>
    public TimeSpan Tolerance { get; set; } = WebhookVerifier.DefaultTolerance;

    /// <summary>Current time source. Null: <see cref="DateTimeOffset.UtcNow"/>.</summary>
    public Func<DateTimeOffset>? Now { get; set; }
}
