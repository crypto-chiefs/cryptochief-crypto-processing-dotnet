using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CryptoChief.Processing.Errors;
using CryptoChief.Processing.Internal;
using Microsoft.Extensions.Logging;

namespace CryptoChief.Processing.Http;

internal sealed class CryptoChiefHttpTransport
{
    private const string HeaderMerchant = "Merchant";
    private const string HeaderTimestamp = "X-CC-Timestamp";
    private const string HeaderNonce = "X-CC-Nonce";
    private const string HeaderHmacSignature = "X-CC-Signature";
    private const string HeaderIdempotencyKey = "Idempotency-Key";

    private readonly HttpClient _http;
    private readonly CryptoChiefClientOptions _options;
    private readonly ILogger _logger;
    private static readonly Random Jitter = new();

    // The transport this one is a view of; itself when it is not a view. Clock, nonce factory
    // and clock offset live there and are shared by every view.
    private readonly CryptoChiefHttpTransport _root;

    // Idempotency-Key sent and signed with every request; empty sends no header.
    private readonly string _idempotencyKey;

    // Seconds added to the local clock for X-CC-Timestamp; set from server_time.
    private long _clockOffsetSeconds;
    private Func<DateTimeOffset> _clock = static () => DateTimeOffset.UtcNow;
    private Func<string> _nonceFactory = RequestSigner.NewNonce;

    internal Func<DateTimeOffset> Clock
    {
        get => _root._clock;
        set => _root._clock = value;
    }

    internal Func<string> NonceFactory
    {
        get => _root._nonceFactory;
        set => _root._nonceFactory = value;
    }

    internal long ClockOffsetSeconds => Interlocked.Read(ref _root._clockOffsetSeconds);

    /// <summary>The <c>Idempotency-Key</c> this transport sends; empty when it sends none.</summary>
    internal string IdempotencyKey => _idempotencyKey;

    public CryptoChiefHttpTransport(HttpClient http, CryptoChiefClientOptions options, ILogger logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
        _root = this;
        _idempotencyKey = string.Empty;
    }

    private CryptoChiefHttpTransport(CryptoChiefHttpTransport source, string idempotencyKey)
    {
        _http = source._http;
        _options = source._options;
        _logger = source._logger;
        _root = source._root;
        _idempotencyKey = idempotencyKey;
    }

    /// <summary>
    /// A view over the same HTTP client and clock offset that sends and signs
    /// <c>Idempotency-Key</c>. An empty key returns a view that sends none.
    /// </summary>
    /// <exception cref="ArgumentException">The key cannot be sent as it is.</exception>
    internal CryptoChiefHttpTransport WithIdempotencyKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return new CryptoChiefHttpTransport(this, string.Empty);
        if (!IsSendableIdempotencyKey(key!))
            throw new ArgumentException(
                "cryptochief: Idempotency-Key must be printable ASCII without a leading or "
                + $"trailing space or tab: \"{key}\"", nameof(key));
        return new CryptoChiefHttpTransport(this, key!);
    }

    // Printable ASCII, no space at either edge; a tab is out everywhere. The server trims
    // spaces and tabs before it verifies the signature, so an untrimmed value would be
    // signed in a form it never sees.
    private static bool IsSendableIdempotencyKey(string key)
    {
        if (key[0] == ' ' || key[key.Length - 1] == ' ') return false;
        foreach (var c in key)
        {
            if (c is < ' ' or > '~') return false;
        }
        return true;
    }

    public Task<TResponse> SendAsync<TResponse>(
        string path,
        object? body,
        CancellationToken cancellationToken) =>
        SendAsync<TResponse>(HttpMethod.Post, path, body, cancellationToken);

    public async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        var raw = await SendRawAsync(method, path, body, cancellationToken).ConfigureAwait(false);
        if (raw is null or { Length: 0 })
            return default!;
        try
        {
            return JsonSerializer.Deserialize<TResponse>(raw, JsonDefaults.Options)!;
        }
        catch (JsonException ex)
        {
            throw new CryptoChiefException(
                $"cryptochief: decode {path} response: {ex.Message}", ex);
        }
    }

    public Task SendAsync(string path, object? body, CancellationToken cancellationToken) =>
        SendRawAsync(HttpMethod.Post, path, body, cancellationToken);

    public Task SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken) =>
        SendRawAsync(method, path, body, cancellationToken);

    private async Task<byte[]> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (path is null || !path.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException($"cryptochief: request path must start with \"/\": \"{path}\"", nameof(path));

        var httpMethod = UpperMethod(method);
        var payload = EncodeBody(body);
        if (payload.Length > 0 && IsBodyless(httpMethod))
            throw new ArgumentException(
                $"cryptochief: a {httpMethod.Method} request cannot carry a body", nameof(body));

        var url = $"{_options.BaseUrl.TrimEnd('/')}{path}";
        // The servers sign the percent-decoded path — the form they read — while the escaped
        // spelling is what goes on the wire.
        var routePath = PercentDecode(StripQuery(path));
        var merchant = _options.MerchantId.Trim(' ', '\t');

        Exception? last = null;
        var attempts = _options.MaxRetries + 1;
        var clockCorrected = false;
        var skipDelay = false;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (attempt > 0 && !skipDelay)
            {
                var delay = BackoffDelay(attempt,
                    _options.InitialRetryDelay, _options.MaxRetryDelay);
                _logger.LogDebug("cryptochief retry: attempt={Attempt} delay={Delay} path={Path}",
                    attempt, delay, path);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            skipDelay = false;

            using var req = new HttpRequestMessage(httpMethod, url);
            // A body needs application/json; without one the header would be a lie.
            if (payload.Length > 0)
            {
                req.Content = new ByteArrayContent(payload)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
                };
            }
            else if (!IsBodyless(httpMethod))
            {
                req.Content = new ByteArrayContent(payload);
            }
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.TryAddWithoutValidation(HeaderMerchant, merchant);
            req.Headers.UserAgent.ParseAdd(_options.UserAgent);
            AddHmacV1Headers(req, routePath, merchant, payload);

            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                last = new CryptoChiefApiException(
                    ErrorCodes.NetworkError, 0, ex.Message);
                if (((CryptoChiefApiException)last).IsRetryable && attempt + 1 < attempts) continue;
                throw last;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                last = new CryptoChiefApiException(
                    ErrorCodes.NetworkError, 0, "request timed out");
                if (attempt + 1 < attempts) continue;
                throw last;
            }

            byte[] respBody;
            try
            {
#if NET8_0_OR_GREATER
                respBody = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
#else
                respBody = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
            }
            finally { resp.Dispose(); }

            _logger.LogDebug("cryptochief response: path={Path} status={Status} bytes={Bytes}",
                path, (int)resp.StatusCode, respBody.Length);

            var status = resp.StatusCode;
            if ((int)status >= 200 && (int)status < 300)
                return respBody;

            var apiErr = ParseApiError(status, respBody, out var serverTime);
            if (apiErr.Code == ErrorCodes.SignatureTimestampOutOfRange
                && serverTime is { } st && !clockCorrected)
            {
                clockCorrected = true;
                var offset = st - Clock().ToUnixTimeSeconds();
                Interlocked.Exchange(ref _root._clockOffsetSeconds, offset);
                _logger.LogDebug("cryptochief clock offset: offset={Offset}s path={Path}", offset, path);
                last = apiErr;
                attempts++;
                skipDelay = true;
                continue;
            }
            if ((int)status >= 500 && attempt + 1 < attempts && !IsOrderBody(respBody))
            {
                last = apiErr;
                continue;
            }
            throw apiErr;
        }
        throw last ?? new CryptoChiefException("cryptochief: retry budget exhausted");
    }

    // Serialized once; every attempt sends and signs these bytes. Null body: empty body.
    private static byte[] EncodeBody(object? body) =>
        body is null
            ? Array.Empty<byte>()
            : JsonSerializer.SerializeToUtf8Bytes(body, JsonDefaults.Options);

    private void AddHmacV1Headers(HttpRequestMessage req, string routePath, string merchant, byte[] body)
    {
        var input = new HmacV1Input
        {
            Timestamp = (Clock().ToUnixTimeSeconds() + ClockOffsetSeconds)
                .ToString(CultureInfo.InvariantCulture),
            Nonce = NonceFactory(),
            Method = req.Method.Method,
            Path = routePath,
            Query = QueryOf(req.RequestUri!),
            Merchant = merchant,
            IdempotencyKey = _idempotencyKey,
            Body = body,
        };
        var signature = RequestSigner.SignHmacV1(_options.ApiKey, input);

        if (_idempotencyKey.Length > 0)
            req.Headers.TryAddWithoutValidation(HeaderIdempotencyKey, _idempotencyKey);
        req.Headers.TryAddWithoutValidation(HeaderTimestamp, input.Timestamp);
        req.Headers.TryAddWithoutValidation(HeaderNonce, input.Nonce);
        req.Headers.TryAddWithoutValidation(HeaderHmacSignature,
            RequestSigner.HmacV1SignaturePrefix + signature);
    }

    // The route ends at the query or the fragment; neither is part of the path the server
    // reads, and a fragment is not sent at all.
    private static string StripQuery(string path)
    {
        var i = path.IndexOfAny(QueryOrFragment);
        return i < 0 ? path : path.Substring(0, i);
    }

    private static readonly char[] QueryOrFragment = { '?', '#' };

    /// <summary>The path as the server reads it: %-sequences decoded, everything else as it is.</summary>
    /// <exception cref="ArgumentException">A <c>%</c> is not followed by two hex digits.</exception>
    private static string PercentDecode(string path)
    {
        var percent = path.IndexOf('%');
        if (percent < 0) return path;
        for (var i = percent; i >= 0; i = path.IndexOf('%', i + 1))
        {
            if (i + 2 >= path.Length || !IsHex(path[i + 1]) || !IsHex(path[i + 2]))
                throw new ArgumentException(
                    $"cryptochief: request path is not valid percent-encoding: \"{path}\"", nameof(path));
        }
        return Uri.UnescapeDataString(path);
    }

    private static bool IsHex(char c) =>
        c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

    /// <summary>The method as signed and sent: <c>a</c>–<c>z</c> upper-cased.</summary>
    private static HttpMethod UpperMethod(HttpMethod method)
    {
        var upper = RequestSigner.UpperAsciiMethod(method.Method);
        return upper == method.Method ? method : new HttpMethod(upper);
    }

    private static bool IsBodyless(HttpMethod method) =>
        method.Method is "GET" or "HEAD";

    private static string QueryOf(Uri uri)
    {
        var query = uri.IsAbsoluteUri ? uri.Query : QueryOf(uri.OriginalString);
        return query.StartsWith("?", StringComparison.Ordinal) ? query.Substring(1) : query;
    }

    private static string QueryOf(string url)
    {
        var hash = url.IndexOf('#');
        if (hash >= 0) url = url.Substring(0, hash);
        var i = url.IndexOf('?');
        return i < 0 ? string.Empty : url.Substring(i);
    }

    // A non-2xx body that carries an order view ("id" + "status", as energy rent / native buy
    // send on a refused order) is a settled business outcome, not a transient failure — retrying
    // it would only wait. The service layer recovers the order from CryptoChiefApiException.RawBody.
    private static bool IsOrderBody(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("id", out _)
                && root.TryGetProperty("status", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Error bodies:
    //   gateway: {"ok":false,"error":"CODE","msg":"...","server_time":...}
    //            {"ok":false,"error":"SERVICE_ERROR","msg":"CODE"}
    //   contour: {"data":null,"error":{"status":...,"name":"...","message":"...",
    //             "details":{"code":"CODE","server_time":...}},"server_time":...}
    //   order:   the order view with "error_code" (machine code) and "error" (human text)
    private static CryptoChiefApiException ParseApiError(HttpStatusCode status, byte[] body, out long? serverTime)
    {
        string? code = null, message = null;
        serverTime = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                serverTime = ReadUnixSeconds(root, "server_time");
                if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    // Contour. Without details.code the error name is the code.
                    var details = error.TryGetProperty("details", out var d) ? d : default;
                    code = ReadString(details, "code") ?? ReadString(error, "name");
                    message = ReadString(error, "message");
                    serverTime ??= ReadUnixSeconds(details, "server_time");
                }
                else
                {
                    // Gateway. Order bodies carry the machine code in "error_code" with "error"
                    // holding the human text; plain envelopes carry the code in "error" — for
                    // SERVICE_ERROR it is in "msg".
                    var err = ReadString(root, "error");
                    var msg = ReadString(root, "msg");
                    code = ReadString(root, "error_code")
                        ?? (err is not null && err != ErrorCodes.ServiceError ? err : msg ?? err);
                    message = msg ?? err;
                }
            }
        }
        catch (JsonException) { }

        code ??= $"HTTP_{(int)status}";
        return new CryptoChiefApiException(
            code, status, message ?? code, Truncate(body, 8 * 1024));
    }

    private static string? ReadString(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
        && v.GetString() is { Length: > 0 } s
            ? s
            : null;

    private static long? ReadUnixSeconds(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt64(out var seconds)
            ? seconds
            : null;

    private static string Truncate(byte[] body, int max)
    {
        try
        {
            return body.Length <= max
                ? Encoding.UTF8.GetString(body)
                : Encoding.UTF8.GetString(body, 0, max) + "...";
        }
        catch
        {
            return Convert.ToBase64String(body, 0, Math.Min(body.Length, max));
        }
    }

    private static TimeSpan BackoffDelay(int attempt, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        if (baseDelay <= TimeSpan.Zero) baseDelay = TimeSpan.FromMilliseconds(200);
        if (maxDelay <= TimeSpan.Zero) maxDelay = TimeSpan.FromSeconds(5);
        var shifted = attempt <= 30
            ? TimeSpan.FromTicks(baseDelay.Ticks << (attempt - 1))
            : maxDelay;
        if (shifted <= TimeSpan.Zero || shifted > maxDelay) shifted = maxDelay;
        var ms = Jitter.Next(0, (int)Math.Min(int.MaxValue, shifted.TotalMilliseconds) + 1);
        return TimeSpan.FromMilliseconds(ms);
    }
}
