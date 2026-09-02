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
    private const string HeaderSignature = "Signature";

    private readonly HttpClient _http;
    private readonly CryptoChiefClientOptions _options;
    private readonly ILogger _logger;
    private static readonly Random Jitter = new();

    public CryptoChiefHttpTransport(HttpClient http, CryptoChiefClientOptions options, ILogger logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<TResponse> SendAsync<TResponse>(
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        var raw = await SendRawAsync(path, body, cancellationToken).ConfigureAwait(false);
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
        SendRawAsync(path, body, cancellationToken);

    private async Task<byte[]> SendRawAsync(string path, object? body, CancellationToken ct)
    {
        var canonical = CanonicalJson.Encode(body);
        var sig = RequestSigner.Sign(canonical, _options.ApiKey);
        var url = $"{_options.BaseUrl.TrimEnd('/')}{path}";

        Exception? last = null;
        var attempts = _options.MaxRetries + 1;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (attempt > 0)
            {
                var delay = BackoffDelay(attempt,
                    _options.InitialRetryDelay, _options.MaxRetryDelay);
                _logger.LogDebug("cryptochief retry: attempt={Attempt} delay={Delay} path={Path}",
                    attempt, delay, path);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new ByteArrayContent(canonical)
                {
                    Headers =
                    {
                        ContentType = new MediaTypeHeaderValue("application/json"),
                    },
                },
            };
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.TryAddWithoutValidation(HeaderMerchant, _options.MerchantId);
            req.Headers.TryAddWithoutValidation(HeaderSignature, sig);
            req.Headers.UserAgent.ParseAdd(_options.UserAgent);

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

            var apiErr = ParseApiError(status, respBody);
            if ((int)status >= 500 && attempt + 1 < attempts)
            {
                last = apiErr;
                continue;
            }
            throw apiErr;
        }
        throw last ?? new CryptoChiefException("cryptochief: retry budget exhausted");
    }

    private static CryptoChiefApiException ParseApiError(HttpStatusCode status, byte[] body)
    {
        string? error = null, msg = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("msg", out var m) && m.ValueKind == JsonValueKind.String)
                    msg = m.GetString();
                if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                    error = e.GetString();
            }
        }
        catch (JsonException) { }

        // The gateway writes two envelope shapes. When it refuses a request itself the
        // machine code is in "error" and "msg" is an English sentence; when it relays a
        // refusal from an upstream service "error" is the generic SERVICE_ERROR marker
        // and the machine code is in "msg". Resolve both to the code.
        var code = !string.IsNullOrEmpty(error) && error != ErrorCodes.ServiceError
            ? error
            : msg;
        if (string.IsNullOrEmpty(code)) code = error;
        if (string.IsNullOrEmpty(code)) code = $"HTTP_{(int)status}";

        // The sentence, where there is one, stays the human-readable message.
        var message = !string.IsNullOrEmpty(msg) ? msg : error;

        return new CryptoChiefApiException(
            code!, status, message ?? code!, Truncate(body, 8 * 1024));
    }

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
