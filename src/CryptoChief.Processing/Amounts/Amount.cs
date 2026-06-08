using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace CryptoChief.Processing.Amounts;

/// <summary>Converts between human-readable and base-unit amounts using <see cref="BigInteger"/>.</summary>
public static class Amount
{
    private static readonly Regex DecimalShape = new(@"^\d+(\.\d+)?$|^\.\d+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Convert a decimal human amount ("0.0001") to base units. Truncates sub-base-unit precision.</summary>
    public static BigInteger HumanToBase(string human, int decimals)
    {
        if (human is null) throw new ArgumentNullException(nameof(human));
        if (decimals < 0) throw new ArgumentOutOfRangeException(nameof(decimals));

        var s = human.Trim();
        if (s.Length == 0)
            throw new FormatException("cryptochief: empty amount");
        if (s.IndexOfAny(new[] { 'e', 'E' }) >= 0)
            throw new FormatException($"cryptochief: scientific notation not allowed: {human}");
        if (s[0] == '-')
            throw new FormatException($"cryptochief: negative amount not allowed: {human}");
        if (!DecimalShape.IsMatch(s))
            throw new FormatException($"cryptochief: invalid amount: {human}");

        var dot = s.IndexOf('.');
        string intPart, fracPart;
        if (dot < 0) { intPart = s; fracPart = string.Empty; }
        else
        {
            intPart = dot == 0 ? "0" : s[..dot];
            fracPart = s[(dot + 1)..];
        }

        if (fracPart.Length > decimals)
            fracPart = fracPart[..decimals];
        else if (fracPart.Length < decimals)
            fracPart = fracPart.PadRight(decimals, '0');

        var combined = (intPart + fracPart).TrimStart('0');
        if (combined.Length == 0) combined = "0";

        return BigInteger.Parse(combined, CultureInfo.InvariantCulture);
    }

    /// <summary>Convert base units to a decimal human string, trimming trailing zeros.</summary>
    public static string BaseToHuman(BigInteger baseUnits, int decimals)
    {
        if (decimals < 0) decimals = 0;
        var abs = BigInteger.Abs(baseUnits).ToString(CultureInfo.InvariantCulture);
        if (decimals == 0) return abs;
        if (abs.Length <= decimals)
            abs = new string('0', decimals - abs.Length + 1) + abs;

        var cut = abs.Length - decimals;
        var intPart = abs[..cut];
        var fracPart = abs[cut..].TrimEnd('0');
        return fracPart.Length == 0 ? intPart : $"{intPart}.{fracPart}";
    }

    /// <summary>Human TON ("0.05") → nanoTON decimal string ("50000000").</summary>
    public static string NanoTon(string humanTon) =>
        HumanToBase(humanTon, 9).ToString(CultureInfo.InvariantCulture);
}
