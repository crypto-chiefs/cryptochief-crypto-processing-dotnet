using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CryptoChief.Processing.Internal;

// Canonical JSON used by the signing algorithm: serialize → parse back to a
// tree → re-emit with object keys sorted lexicographically. The round-trip
// makes signatures invariant to property order.
internal static class CanonicalJson
{
    public static byte[] Encode<T>(T value, JsonSerializerOptions? options = null)
    {
        if (value is null) return Array.Empty<byte>();

        var opts = options ?? JsonDefaults.Options;
        var first = JsonSerializer.SerializeToUtf8Bytes(value, opts);
        var node = JsonNode.Parse(first, documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        return Canonicalise(node);
    }

    public static byte[] Canonicalise(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty) return Array.Empty<byte>();
        var node = JsonNode.Parse(body.ToArray());
        return Canonicalise(node);
    }

    private static byte[] Canonicalise(JsonNode? node)
    {
        if (node is null) return Encoding.UTF8.GetBytes("null");

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default,
            Indented = false,
            SkipValidation = false,
        });
        WriteSorted(writer, node);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteSorted(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                return;
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var kv in obj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(kv.Key);
                    WriteSorted(writer, kv.Value);
                }
                writer.WriteEndObject();
                return;
            case JsonArray arr:
                writer.WriteStartArray();
                foreach (var item in arr) WriteSorted(writer, item);
                writer.WriteEndArray();
                return;
            case JsonValue val:
                val.WriteTo(writer);
                return;
        }
    }
}
