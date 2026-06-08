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
    public string Code { get; }
    public HttpStatusCode HttpStatus { get; }
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
