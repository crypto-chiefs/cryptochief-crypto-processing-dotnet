namespace CryptoChief.Processing.Models;

/// <summary>Delivery statuses in <see cref="WebhookDelivery.Status"/>.</summary>
public static class WebhookDeliveryStatus
{
    /// <summary>Queued, not yet attempted (or waiting for a retry).</summary>
    public const string Pending    = "pending";
    /// <summary>A worker holds it right now.</summary>
    public const string InProgress = "in_progress";
    /// <summary>Your endpoint answered 2xx.</summary>
    public const string Delivered  = "delivered";
    /// <summary>Every attempt so far was refused or timed out.</summary>
    public const string Failed     = "failed";
    /// <summary>Superseded by a newer event before it was ever sent.</summary>
    public const string Cancelled  = "cancelled";
}

/// <summary>One POST the platform made to your endpoint. Newest first in <see cref="WebhookDelivery.AttemptHistory"/>.</summary>
public sealed record WebhookAttempt
{
    public int Attempt { get; init; }
    /// <summary><c>null</c> when nothing answered (DNS, connect, TLS, timeout); <see cref="Error"/> then holds the transport error.</summary>
    public int? HttpStatus { get; init; }
    public string? Error { get; init; }
    public long? DurationMs { get; init; }
    public string TargetUrl { get; init; } = string.Empty;
    /// <summary><c>null</c> for attempts recorded before the platform kept the time.</summary>
    public string? CreatedAt { get; init; }
    /// <summary>What your endpoint answered, as the platform saw it. Capped; see <see cref="ResponseTruncated"/>.</summary>
    public string? ResponseBody { get; init; }
    public string? ResponseContentType { get; init; }
    public bool ResponseTruncated { get; init; }
}

/// <summary>The body the platform sent. <see cref="Bytes"/> is the whole size even when <see cref="Body"/> was cut.</summary>
public sealed record WebhookPayload
{
    public string Body { get; init; } = string.Empty;
    public int Bytes { get; init; }
    public bool Truncated { get; init; }
}

/// <summary>
/// One outbound webhook, with every attempt the platform made and the body it sent.
/// <c>null</c> means "not recorded", distinct from zero or empty.
/// </summary>
public sealed record WebhookDelivery
{
    public string Uuid { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    /// <summary>The object the event was about — the order or static deposit uuid you already hold.</summary>
    public string Reference { get; init; } = string.Empty;
    public string TargetUrl { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int Attempts { get; init; }
    public int MaxAttempts { get; init; }
    /// <summary>How many times a resend was asked for, by API or from the dashboard.</summary>
    public int ResendCount { get; init; }
    public string? LastError { get; init; }
    public int? LastHttpStatus { get; init; }
    public string? NextAttemptAt { get; init; }
    public string? DeliveredAt { get; init; }
    public string CreatedAt { get; init; } = string.Empty;
    /// <summary>
    /// The NEWER event for the same object, when there is one. A superseded delivery
    /// cannot be resent — resend the latest event instead.
    /// </summary>
    public string? SupersededBy { get; init; }
    public IReadOnlyList<WebhookAttempt> AttemptHistory { get; init; } = Array.Empty<WebhookAttempt>();
    public WebhookPayload Payload { get; init; } = new();
}

/// <summary>
/// What a resend did. On this platform a resend is synchronous: the POST to your
/// endpoint happens before the answer comes back, so <c>Queued == true</c> arrives with
/// <see cref="Status"/> already <c>delivered</c> or <c>failed</c> for that attempt.
/// </summary>
public sealed record WebhookResendResult
{
    public string Uuid { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool Queued { get; init; }
    public int Attempts { get; init; }
    public int ResendCount { get; init; }
    /// <summary>Set when <see cref="Queued"/> is false: one of the <c>ErrorCodes.Delivery*</c> / <c>ResendTooSoon</c> codes.</summary>
    public string? Reason { get; init; }
    public string? SupersededBy { get; init; }
    public int? RetryAfterSeconds { get; init; }
}

/// <summary>
/// The resend of a static deposit's webhook. <see cref="Deliveries"/> has one entry — the
/// newest delivery for the deposit — kept as a list so the shape matches the white-label
/// platform, which may requeue several.
/// </summary>
public sealed record StaticDepositResendResult
{
    public string Uuid { get; init; } = string.Empty;
    public IReadOnlyList<WebhookResendResult> Deliveries { get; init; } = Array.Empty<WebhookResendResult>();
    public int Queued { get; init; }
    public int Total { get; init; }
}
