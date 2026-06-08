using System.Text;
using System.Text.Json;

namespace CryptoChief.Processing.Internal;

// Equivalent to JsonNamingPolicy.SnakeCaseLower (net8.0+); polyfilled here for net6.0.
internal sealed class SnakeCaseNamingPolicy : JsonNamingPolicy
{
    public static readonly SnakeCaseNamingPolicy Instance = new();

    public override string ConvertName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c))
            {
                var prev = name[i - 1];
                var next = i + 1 < name.Length ? name[i + 1] : '\0';
                if (char.IsLower(prev) || char.IsDigit(prev) ||
                    (char.IsUpper(prev) && next != '\0' && char.IsLower(next)))
                {
                    sb.Append('_');
                }
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
