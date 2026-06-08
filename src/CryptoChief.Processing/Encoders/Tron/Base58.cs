using System.Numerics;

namespace CryptoChief.Processing.Encoders.Tron;

/// <summary>Bitcoin / TRON base58 (no separators, no version handling).</summary>
public static class Base58
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    private static readonly sbyte[] Map = BuildMap();

    private static sbyte[] BuildMap()
    {
        var m = new sbyte[128];
        Array.Fill(m, (sbyte)-1);
        for (var i = 0; i < Alphabet.Length; i++) m[Alphabet[i]] = (sbyte)i;
        return m;
    }

    public static string Encode(ReadOnlySpan<byte> bytes)
    {
        var zeros = 0;
        while (zeros < bytes.Length && bytes[zeros] == 0) zeros++;

        var num = new BigInteger(bytes.ToArray(), isUnsigned: true, isBigEndian: true);
        var output = new List<char>(bytes.Length * 2);
        var base58 = new BigInteger(58);
        while (num > 0)
        {
            num = BigInteger.DivRem(num, base58, out var rem);
            output.Add(Alphabet[(int)rem]);
        }
        for (var i = 0; i < zeros; i++) output.Add(Alphabet[0]);
        output.Reverse();
        return new string(output.ToArray());
    }

    public static byte[] Decode(string input)
    {
        if (string.IsNullOrEmpty(input))
            throw new FormatException("base58: empty input");

        var zeros = 0;
        while (zeros < input.Length && input[zeros] == Alphabet[0]) zeros++;

        var num = BigInteger.Zero;
        var base58 = new BigInteger(58);
        foreach (var c in input)
        {
            if (c >= 128 || Map[c] < 0)
                throw new FormatException($"base58: invalid char {c}");
            num = num * base58 + new BigInteger(Map[c]);
        }
        var body = num.IsZero ? Array.Empty<byte>() : num.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (zeros == 0) return body;
        var padded = new byte[zeros + body.Length];
        body.CopyTo(padded, zeros);
        return padded;
    }
}
