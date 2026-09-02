using System.Net;

namespace CryptoChief.Processing.Errors;

/// <summary>Base class for every exception raised by this SDK.</summary>
public class CryptoChiefException : Exception
{
    public CryptoChiefException(string message) : base(message) { }
    public CryptoChiefException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>A typed error returned by the Crypto Chief API. Switch on <see cref="Code"/>; see <see cref="ErrorCodes"/>.</summary>
public sealed class CryptoChiefApiException : CryptoChiefException
{
    /// <summary>The machine-readable code, the stable string to compare against
    /// <see cref="ErrorCodes"/>. Resolved from whichever half of the envelope carries it:
    /// the <c>error</c> field for a refusal the gateway decided itself, the <c>msg</c>
    /// field for one it relayed from an upstream service as <c>SERVICE_ERROR</c>. Falls
    /// back to <c>HTTP_&lt;status&gt;</c> when the body carries neither.</summary>
    public string Code { get; }

    /// <summary>The HTTP status the refusal arrived with.</summary>
    public HttpStatusCode HttpStatus { get; }

    /// <summary>The response body as received, truncated at 8 KiB. Holds both envelope
    /// fields verbatim.</summary>
    public string? RawBody { get; }

    public CryptoChiefApiException(string code, HttpStatusCode status, string message, string? rawBody = null)
        : base(BuildMessage(code, status, message))
    {
        Code = code;
        HttpStatus = status;
        RawBody = rawBody;
    }

    /// <summary>True if plausibly transient — 5xx or network errors.</summary>
    public bool IsRetryable =>
        (int)HttpStatus >= 500 || Code == ErrorCodes.NetworkError;

    private static string BuildMessage(string code, HttpStatusCode status, string message)
    {
        if ((int)status == 0)
            return $"cryptochief: {code}";
        if (!string.IsNullOrEmpty(message) && message != code)
            return $"cryptochief: {(int)status} {code}: {message}";
        return $"cryptochief: {(int)status} {code}";
    }
}
