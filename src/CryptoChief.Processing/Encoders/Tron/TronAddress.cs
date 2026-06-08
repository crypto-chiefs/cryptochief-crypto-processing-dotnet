using System.Security.Cryptography;
using CryptoChief.Processing.Encoders.Evm;

namespace CryptoChief.Processing.Encoders.Tron;

/// <summary>Converts between TRON address forms: base58 "T..." ↔ EVM 20-byte hex ↔ 21-byte 0x41-prefixed hex.</summary>
public static class TronAddress
{
    /// <summary>Base58 "T..." → 0x41-prefixed 21-byte hex. Validates the SHA-256 double-hash checksum.</summary>
    public static string ToHex(string base58Address)
    {
        var decoded = Base58.Decode(base58Address.Trim());
        if (decoded.Length != 25)
            throw new FormatException($"cryptochief/tron: decoded length {decoded.Length}, want 25");

        var payload = decoded[..21];
        var sum = decoded[21..];
        if (payload[0] != 0x41)
            throw new FormatException($"cryptochief/tron: leading byte 0x{payload[0]:x2}, want 0x41");
        var want = Sha256D(payload);
        if (!want.AsSpan(0, 4).SequenceEqual(sum))
            throw new FormatException("cryptochief/tron: checksum mismatch");
        return "0x" + Hex.ToLower(payload);
    }

    /// <summary>EVM (20 bytes) or 0x41-prefixed (21 bytes) hex → base58 "T...".</summary>
    public static string FromHex(string hexAddress)
    {
        var s = hexAddress.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        var raw = Hex.Decode(s);

        byte[] payload;
        switch (raw.Length)
        {
            case 20:
                payload = new byte[21];
                payload[0] = 0x41;
                raw.CopyTo(payload, 1);
                break;
            case 21:
                if (raw[0] != 0x41)
                    throw new FormatException(
                        $"cryptochief/tron: 21-byte input must start with 0x41, got 0x{raw[0]:x2}");
                payload = raw;
                break;
            default:
                throw new FormatException(
                    $"cryptochief/tron: want 20- or 21-byte hex address, got {raw.Length} bytes");
        }
        var sum = Sha256D(payload);
        var output = new byte[25];
        payload.CopyTo(output, 0);
        Array.Copy(sum, 0, output, 21, 4);
        return Base58.Encode(output);
    }

    private static byte[] Sha256D(byte[] data)
    {
#if NET8_0_OR_GREATER
        var first = SHA256.HashData(data);
        return SHA256.HashData(first);
#else
        using var sha = SHA256.Create();
        var first = sha.ComputeHash(data);
        return sha.ComputeHash(first);
#endif
    }
}
