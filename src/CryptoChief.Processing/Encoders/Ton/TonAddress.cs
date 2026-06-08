using System.Globalization;
using CryptoChief.Processing.Encoders.Evm;

namespace CryptoChief.Processing.Encoders.Ton;

/// <summary>
/// Parsed TON address. Three forms: user-friendly bounceable (EQ…/kQ…),
/// user-friendly non-bounceable (UQ…/0Q…), and raw (workchain:hex).
/// </summary>
public sealed record TonAddress
{
    public sbyte Workchain { get; init; }
    public byte[] Hash { get; init; } = new byte[32];
    public bool Bounceable { get; init; } = true;
    public bool Testnet { get; init; }

    public static TonAddress Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new FormatException("cryptochief/ton: empty address");
        var s = input.Trim();
        var colon = s.IndexOf(':');
        return colon > 0 ? ParseRaw(s, colon) : ParseUserFriendly(s);
    }

    private static TonAddress ParseRaw(string s, int colon)
    {
        if (!int.TryParse(s[..colon], NumberStyles.Integer, CultureInfo.InvariantCulture, out var wc))
            throw new FormatException($"cryptochief/ton: bad raw workchain {s[..colon]}");
        if (wc is < -128 or > 127)
            throw new FormatException($"cryptochief/ton: workchain {wc} out of int8 range");
        var hashHex = s[(colon + 1)..];
        if (hashHex.Length != 64)
            throw new FormatException($"cryptochief/ton: hash hex length {hashHex.Length}, want 64");
        return new TonAddress
        {
            Workchain = (sbyte)wc,
            Hash = Hex.Decode(hashHex),
            Bounceable = true,
        };
    }

    private static TonAddress ParseUserFriendly(string s)
    {
        if (s.Length != 48)
            throw new FormatException($"cryptochief/ton: user-friendly length {s.Length}, want 48");
        // URL-safe base64 without padding; some wallets emit standard variant.
        byte[] raw;
        try { raw = Convert.FromBase64String(UrlSafeToStandard(s)); }
        catch (FormatException)
        {
            try { raw = Convert.FromBase64String(s + Padding(s.Length)); }
            catch (FormatException ex) { throw new FormatException($"cryptochief/ton: base64 decode: {ex.Message}", ex); }
        }
        if (raw.Length != 36)
            throw new FormatException($"cryptochief/ton: decoded length {raw.Length}, want 36");
        var want = Crc16Xmodem(raw, 0, 34);
        var got = (ushort)((raw[34] << 8) | raw[35]);
        if (want != got)
            throw new FormatException("cryptochief/ton: CRC mismatch");

        var tag = raw[0];
        var hash = new byte[32];
        Array.Copy(raw, 2, hash, 0, 32);
        return new TonAddress
        {
            Workchain = (sbyte)raw[1],
            Hash = hash,
            Bounceable = (tag & 0x40) == 0,
            Testnet = (tag & 0x80) != 0,
        };
    }

    private static string UrlSafeToStandard(string s)
    {
        var standard = s.Replace('-', '+').Replace('_', '/');
        return standard + Padding(s.Length);
    }
    private static string Padding(int len) => (len % 4) switch
    {
        2 => "==",
        3 => "=",
        _ => "",
    };

    public override string ToString()
    {
        byte tag = Bounceable ? (byte)0x11 : (byte)0x51;
        if (Testnet) tag |= 0x80;
        var buf = new byte[36];
        buf[0] = tag;
        buf[1] = (byte)Workchain;
        Hash.CopyTo(buf, 2);
        var crc = Crc16Xmodem(buf, 0, 34);
        buf[34] = (byte)(crc >> 8);
        buf[35] = (byte)crc;
        return Convert.ToBase64String(buf).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public string ToRaw() => $"{Workchain}:{Hex.ToLower(Hash)}";

    internal static ushort Crc16Xmodem(byte[] data, int offset, int length)
    {
        ushort crc = 0;
        for (var i = 0; i < length; i++)
        {
            crc ^= (ushort)(data[offset + i] << 8);
            for (var j = 0; j < 8; j++)
            {
                if ((crc & 0x8000) != 0) crc = (ushort)((crc << 1) ^ 0x1021);
                else crc <<= 1;
            }
        }
        return crc;
    }
}
