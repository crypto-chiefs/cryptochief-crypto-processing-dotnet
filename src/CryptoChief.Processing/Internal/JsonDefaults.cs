using System.Text.Json;
using System.Text.Json.Serialization;

namespace CryptoChief.Processing.Internal;

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = SnakeCaseNamingPolicy.Instance,
            DictionaryKeyPolicy = SnakeCaseNamingPolicy.Instance,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        o.Converters.Add(new JsonStringEnumConverter(SnakeCaseNamingPolicy.Instance, allowIntegerValues: false));
        return o;
    }
}
