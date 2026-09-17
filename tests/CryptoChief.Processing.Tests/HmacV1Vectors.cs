using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CryptoChief.Processing.Http;

namespace CryptoChief.Processing.Tests;

/// <summary>testdata/hmac_v1_vectors.json — the reference vectors from the API signature specification, used as is.</summary>
internal static class HmacV1Vectors
{
    public const string FileSha256 = "a87df4921399dc14c7ceaa7e4c0dfa02495ad0400a5e722adfc0d3e3c1e064fe";

    public static string FilePath { get; } =
        Path.Combine(AppContext.BaseDirectory, "testdata", "hmac_v1_vectors.json");

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
        [JsonPropertyName("method")] public string Method { get; set; } = "";
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("query")] public string Query { get; set; } = "";
        [JsonPropertyName("merchant")] public string Merchant { get; set; } = "";
        [JsonPropertyName("idempotency_key")] public string IdempotencyKey { get; set; } = "";
        [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
        [JsonPropertyName("nonce")] public string Nonce { get; set; } = "";
        [JsonPropertyName("body")] public string Body { get; set; } = "";
        [JsonPropertyName("body_sha256")] public string BodySha256 { get; set; } = "";
        [JsonPropertyName("string_to_sign")] public string StringToSign { get; set; } = "";
        [JsonPropertyName("signature")] public string Signature { get; set; } = "";
        [JsonPropertyName("now")] public long Now { get; set; }
        [JsonPropertyName("expect")] public string Expect { get; set; } = "";
        [JsonPropertyName("headers")] public Dictionary<string, List<string>>? Headers { get; set; }

        public byte[] BodyBytes => Encoding.UTF8.GetBytes(Body);

        public HmacV1Input ToInput() => new()
        {
            Timestamp = Timestamp,
            Nonce = Nonce,
            Method = Method,
            Path = Path,
            Query = Query,
            Merchant = Merchant,
            IdempotencyKey = IdempotencyKey,
            Body = BodyBytes,
        };

        /// <summary>The request the gateway sees: the signed defaults, then the record's
        /// overrides. An empty list removes the header, two values repeat it.</summary>
        /// <param name="applyOverrides">False builds the request without the record's one
        /// defect — the same record then passes the check.</param>
        public GatewayHmacV1.SignedRequest ToRequest(bool applyOverrides = true) =>
            new(Method, Path, Query, BodyBytes, ReceivedHeaders(applyOverrides));

        private List<KeyValuePair<string, string>> ReceivedHeaders(bool applyOverrides)
        {
            var defaults = new List<KeyValuePair<string, List<string>>>
            {
                new("Merchant", new() { Merchant }),
                new(GatewayHmacV1.TimestampHeader, new() { Timestamp }),
                new(GatewayHmacV1.NonceHeader, new() { Nonce }),
                new(GatewayHmacV1.SignatureHeader, new() { RequestSigner.HmacV1SignaturePrefix + Signature }),
            };
            if (IdempotencyKey.Length > 0)
                defaults.Add(new(GatewayHmacV1.IdempotencyKeyHeader, new() { IdempotencyKey }));
            if (Body.Length > 0)
                defaults.Add(new(GatewayHmacV1.ContentTypeHeader, new() { "application/json" }));

            var overrides = new Dictionary<string, List<string>>(
                applyOverrides && Headers is not null ? Headers : new Dictionary<string, List<string>>(),
                StringComparer.OrdinalIgnoreCase);

            var received = new List<KeyValuePair<string, string>>();
            foreach (var (name, values) in defaults)
            {
                if (overrides.Remove(name, out var replacement))
                {
                    foreach (var value in replacement) received.Add(new(name, value));
                    continue;
                }
                foreach (var value in values) received.Add(new(name, value));
            }
            // A header the record adds that no default carries — "Signature", say.
            foreach (var (name, values) in overrides)
                foreach (var value in values)
                    received.Add(new(name, value));
            return received;
        }

        public override string ToString() => Name;
    }
}
