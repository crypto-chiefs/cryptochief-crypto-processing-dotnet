namespace CryptoChief.Processing.Encoders.Evm;

public static class Hex
{
    public static byte[] Decode(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var span = s.AsSpan();
        if (span.Length >= 2 && (span[0] == '0' && (span[1] == 'x' || span[1] == 'X')))
            span = span[2..];
        if ((span.Length & 1) != 0)
            throw new FormatException("hex: odd length");
#if NET8_0_OR_GREATER
        return Convert.FromHexString(span);
#else
        var bytes = new byte[span.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var hi = FromHexChar(span[i * 2]);
            var lo = FromHexChar(span[i * 2 + 1]);
            bytes[i] = (byte)((hi << 4) | lo);
        }
        return bytes;
#endif
    }

    public static string ToLower(ReadOnlySpan<byte> bytes)
    {
#if NET8_0_OR_GREATER
        return Convert.ToHexString(bytes).ToLowerInvariant();
#else
        var chars = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i * 2]     = ToHexCharLow(bytes[i] >> 4);
            chars[i * 2 + 1] = ToHexCharLow(bytes[i] & 0x0F);
        }
        return new string(chars);
#endif
    }

#if !NET8_0_OR_GREATER
    private static int FromHexChar(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => 10 + c - 'a',
        >= 'A' and <= 'F' => 10 + c - 'A',
        _ => throw new FormatException($"hex: invalid char {c}"),
    };

    private static char ToHexCharLow(int n) => (char)(n < 10 ? '0' + n : 'a' + n - 10);
#endif
}
