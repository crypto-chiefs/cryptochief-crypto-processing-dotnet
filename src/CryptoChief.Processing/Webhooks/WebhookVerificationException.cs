using CryptoChief.Processing.Errors;

namespace CryptoChief.Processing.Webhooks;

/// <summary>A webhook refused by <see cref="WebhookVerifier"/>. Respond with 401.</summary>
public abstract class WebhookVerificationException : CryptoChiefException
{
    private protected WebhookVerificationException(string message) : base("cryptochief: webhook refused: " + message) { }
}

/// <summary><c>X-CC-Timestamp</c>, <c>X-Webhook-Delivery</c> or <c>X-CC-Signature</c> is missing,
/// repeated or not in format.</summary>
public sealed class WebhookHeadersException : WebhookVerificationException
{
    internal WebhookHeadersException(string message) : base(message) { }
}

/// <summary><c>X-CC-Timestamp</c> differs from the current time by more than the tolerance.</summary>
public sealed class WebhookTimestampException : WebhookVerificationException
{
    internal WebhookTimestampException(string message) : base(message) { }
}

/// <summary><c>X-CC-Signature</c> does not match the body, timestamp and delivery id.</summary>
public sealed class WebhookSignatureException : WebhookVerificationException
{
    internal WebhookSignatureException(string message) : base(message) { }
}
