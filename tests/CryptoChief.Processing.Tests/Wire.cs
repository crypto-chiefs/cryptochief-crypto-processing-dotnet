using System.Text;
using System.Text.Json;
using CryptoChief.Processing.Http;
using CryptoChief.Processing.Webhooks;
using FluentAssertions;

namespace CryptoChief.Processing.Tests;

/// <summary>Assertions on request bodies and signatures as sent.</summary>
internal static class Wire
{
    /// <summary>
    /// Same JSON value: object members in any order, strings compared decoded, numbers compared by
    /// their literal text (no float rounding).
    /// </summary>
    public static void ShouldBeJson(this string actual, string expected)
    {
        using var a = JsonDocument.Parse(actual);
        using var e = JsonDocument.Parse(expected);
        SameJson(a.RootElement, e.RootElement).Should()
            .BeTrue($"body {actual} should be the JSON value {expected}");
    }

    public static bool SameJson(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var am = Members(a);
                var bm = Members(b);
                if (am is null || bm is null || am.Count != bm.Count) return false;
                foreach (var (name, value) in am)
                    if (!bm.TryGetValue(name, out var other) || !SameJson(value, other)) return false;
                return true;
            }
            case JsonValueKind.Array:
            {
                var aa = a.EnumerateArray().ToList();
                var ba = b.EnumerateArray().ToList();
                return aa.Count == ba.Count && aa.Zip(ba).All(p => SameJson(p.First, p.Second));
            }
            case JsonValueKind.String:
                return a.GetString() == b.GetString();
            case JsonValueKind.Number:
                return a.GetRawText() == b.GetRawText();
            default:
                return true;
        }
    }

    // Null when a name repeats.
    private static Dictionary<string, JsonElement>? Members(JsonElement obj)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in obj.EnumerateObject())
            if (!map.TryAdd(p.Name, p.Value)) return null;
        return map;
    }

    /// <summary>
    /// The request carries <c>X-CC-Timestamp</c>, <c>X-CC-Nonce</c> and an <c>X-CC-Signature</c> that
    /// matches <paramref name="body"/>; no <c>Signature</c> header.
    /// </summary>
    public static void ShouldBeSignedHmacV1(HttpRequestMessage req, string body, string merchant, string apiKey)
    {
        req.Headers.Contains("Signature").Should().BeFalse();
        var timestamp = req.Headers.GetValues("X-CC-Timestamp").Should().ContainSingle().Subject;
        var nonce = req.Headers.GetValues("X-CC-Nonce").Should().ContainSingle().Subject;
        timestamp.Should().MatchRegex("^[0-9]+$");
        nonce.Should().MatchRegex("^[0-9a-f]{32}$");

        var expected = RequestSigner.SignHmacV1(apiKey, new HmacV1Input
        {
            Timestamp = timestamp,
            Nonce = nonce,
            Method = req.Method.Method,
            Path = req.RequestUri!.AbsolutePath,
            Query = req.RequestUri.Query.TrimStart('?'),
            Merchant = merchant,
            Body = Encoding.UTF8.GetBytes(body),
        });
        req.Headers.GetValues("X-CC-Signature").Should().ContainSingle().Which.Should().Be("v1=" + expected);
    }

    /// <summary>Headers of a webhook signed now with <paramref name="apiKey"/>.</summary>
    public static Dictionary<string, IEnumerable<string>> SignedWebhookHeaders(
        string apiKey, byte[] body, string deliveryId = "7c9e6679-7425-40de-944b-e07fc1f90ae7")
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new Dictionary<string, IEnumerable<string>>
        {
            [WebhookVerifier.TimestampHeader] = new[] { timestamp.ToString() },
            [WebhookVerifier.DeliveryHeader] = new[] { deliveryId },
            [WebhookVerifier.SignatureHeader] = new[] { RequestSigner.SignWebhookV1(apiKey, timestamp, deliveryId, body) },
        };
    }
}
