using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CryptoChief.Processing.Webhooks;

namespace CryptoChief.Processing.Tests;

/// <summary>testdata/webhook_hmac_v1_vectors.json — the reference vectors from the API signature specification, used as is.</summary>
internal static class WebhookV1Vectors
{
    public const string FileSha256 = "15a6e1423708e8c3b9ec4fac7ee6eb383db56703605647e02308a29166722502";

    public static string FilePath { get; } =
        Path.Combine(AppContext.BaseDirectory, "testdata", "webhook_hmac_v1_vectors.json");

    public static IReadOnlyList<Vector> List { get; } =
        JsonSerializer.Deserialize<List<Vector>>(File.ReadAllBytes(FilePath))!;

    public static IReadOnlyDictionary<string, Vector> All { get; } = List.ToDictionary(v => v.Name);

    public static Vector Get(string name) => All[name];

    public static string ComputeFileSha256() =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FilePath))).ToLowerInvariant();

    internal sealed class Vector
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("api_key")] public string ApiKey { get; set; } = "";
        [JsonPropertyName("timestamp")] public long Timestamp { get; set; }
        [JsonPropertyName("delivery_id")] public string DeliveryId { get; set; } = "";
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("body_base64")] public string? BodyBase64 { get; set; }
        [JsonPropertyName("body_sha256")] public string BodySha256 { get; set; } = "";
        [JsonPropertyName("string_to_sign")] public string StringToSign { get; set; } = "";
        [JsonPropertyName("signature")] public string Signature { get; set; } = "";
        [JsonPropertyName("now")] public long Now { get; set; }
        [JsonPropertyName("expect")] public string Expect { get; set; } = "";
        [JsonPropertyName("headers")] public Dictionary<string, List<string>>? Headers { get; set; }

        public byte[] BodyBytes => BodyBase64 is not null
            ? Convert.FromBase64String(BodyBase64)
            : Encoding.UTF8.GetBytes(Body ?? "");

        /// <summary>Headers the receiver sees: the signed defaults, then the record's overrides.
        /// An empty list removes the header.</summary>
        public IReadOnlyList<KeyValuePair<string, List<string>>> ReceivedHeaders()
        {
            var headers = new List<KeyValuePair<string, List<string>>>
            {
                new(WebhookVerifier.TimestampHeader, new() { Timestamp.ToString() }),
                new(WebhookVerifier.DeliveryHeader, new() { DeliveryId }),
                new(WebhookVerifier.SignatureHeader, new() { Signature }),
            };
            if (Headers is null) return headers;
            return headers
                .Select(h => Headers.TryGetValue(h.Key, out var o) ? new KeyValuePair<string, List<string>>(h.Key, o) : h)
                .Where(h => h.Value.Count > 0)
                .ToList();
        }

        public WebhookVerifyOptions Options => new()
        {
            Tolerance = TimeSpan.FromSeconds(300),
            Now = () => DateTimeOffset.FromUnixTimeSeconds(Now),
        };

        public Type? ExpectedException => Expect switch
        {
            "ok" => null,
            "bad_headers" => typeof(WebhookHeadersException),
            "timestamp_out_of_range" => typeof(WebhookTimestampException),
            "bad_signature" => typeof(WebhookSignatureException),
            _ => throw new InvalidOperationException($"unknown expect {Expect}"),
        };

        public override string ToString() => Name;
    }
}
