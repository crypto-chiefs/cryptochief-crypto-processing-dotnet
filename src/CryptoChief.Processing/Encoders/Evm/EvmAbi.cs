using System.Globalization;
using System.Numerics;
using System.Text;
using CryptoChief.Processing.Encoders.Tron;

namespace CryptoChief.Processing.Encoders.Evm;

/// <summary>
/// Solidity ABI encoder. Supports <c>uint/int&lt;M&gt;</c>, <c>address</c>,
/// <c>bool</c>, <c>bytes</c>, <c>bytes&lt;N&gt;</c>, <c>string</c>, and
/// fixed/dynamic arrays.
/// </summary>
public static class EvmAbi
{
    public static byte[] EncodeCall(string signature, params object?[] args)
    {
        var (_, types) = ParseSignature(signature);
        if (types.Count != args.Length)
            throw new ArgumentException(
                $"signature has {types.Count} args, got {args.Length}", nameof(args));
        var selector = Selector(signature);
        var body = EncodeTuple(types, args);
        var output = new byte[4 + body.Length];
        selector.AsSpan(0, 4).CopyTo(output);
        body.CopyTo(output.AsSpan(4));
        return output;
    }

    public static string EncodeCallHex(string signature, params object?[] args)
    {
        var raw = EncodeCall(signature, args);
        return "0x" + Hex.ToLower(raw);
    }

    public static byte[] Selector(string signature)
    {
        var canon = CanonicalSig(signature);
        var hash = Keccak256.Hash(Encoding.UTF8.GetBytes(canon));
        var sel = new byte[4];
        Array.Copy(hash, 0, sel, 0, 4);
        return sel;
    }

    private static (string Name, List<string> Types) ParseSignature(string sig)
    {
        var open = sig.IndexOf('(');
        var close = sig.LastIndexOf(')');
        if (open < 0 || close < 0 || close < open)
            throw new ArgumentException($"cryptochief/evm: bad signature {sig}");
        var name = sig[..open].Trim();
        if (name.Length == 0)
            throw new ArgumentException("cryptochief/evm: signature missing name");
        var body = sig[(open + 1)..close].Trim();
        if (body.Length == 0) return (name, new List<string>());
        var types = body.Split(',')
            .Select(p =>
            {
                p = p.Trim();
                var space = p.IndexOf(' ');
                if (space >= 0) p = p[..space].Trim();
                return ExpandAlias(p);
            })
            .ToList();
        return (name, types);
    }

    private static string CanonicalSig(string sig)
    {
        var open = sig.IndexOf('(');
        var close = sig.LastIndexOf(')');
        if (open < 0 || close < 0 || close < open) return sig.Replace(" ", "");
        var name = sig[..open].Trim();
        var parts = sig[(open + 1)..close].Trim().Split(',');
        var clean = parts.Select(p =>
        {
            p = p.Trim();
            var space = p.IndexOf(' ');
            if (space >= 0) p = p[..space].Trim();
            return ExpandAlias(p);
        });
        return $"{name}({string.Join(",", clean)})";
    }

    private static string ExpandAlias(string t)
    {
        var i = t.LastIndexOf('[');
        if (i > 0) return ExpandAlias(t[..i]) + t[i..];
        return t switch
        {
            "uint" => "uint256",
            "int"  => "int256",
            "byte" => "bytes1",
            _ => t,
        };
    }

    private enum Kind { Uint, Int, Address, Bool, Bytes, BytesN, String, Array }

    private sealed class AbiType
    {
        public Kind Kind { get; init; }
        public int Size { get; init; }
        public AbiType? Element { get; init; }

        public bool IsDynamic =>
            Kind switch
            {
                Kind.Bytes or Kind.String => true,
                Kind.Array => Size < 0 || Element!.IsDynamic,
                _ => false,
            };
    }

    private static AbiType ParseType(string t)
    {
        t = t.Trim();
        if (t.Length == 0) throw new ArgumentException("empty type");

        if (t[^1] == ']')
        {
            var open = t.LastIndexOf('[');
            if (open < 0) throw new ArgumentException($"malformed type {t}");
            var inner = ParseType(t[..open]);
            var span = t.Substring(open + 1, t.Length - open - 2);
            var size = -1;
            if (span.Length > 0)
            {
                if (!int.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size < 0)
                    throw new ArgumentException($"bad array size {span} in {t}");
            }
            return new AbiType { Kind = Kind.Array, Size = size, Element = inner };
        }

        if (t.StartsWith("uint", StringComparison.Ordinal))
            return new AbiType { Kind = Kind.Uint, Size = ParseIntBits(t[4..], "uint") };
        if (t.StartsWith("int", StringComparison.Ordinal))
            return new AbiType { Kind = Kind.Int, Size = ParseIntBits(t[3..], "int") };
        if (t == "address") return new AbiType { Kind = Kind.Address };
        if (t == "bool")    return new AbiType { Kind = Kind.Bool };
        if (t == "string")  return new AbiType { Kind = Kind.String };
        if (t == "bytes")   return new AbiType { Kind = Kind.Bytes };

        if (t.StartsWith("bytes", StringComparison.Ordinal))
        {
            if (!int.TryParse(t[5..], NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                || n < 1 || n > 32)
                throw new ArgumentException($"invalid fixed bytes type {t}");
            return new AbiType { Kind = Kind.BytesN, Size = n };
        }
        throw new ArgumentException($"unsupported type {t}");
    }

    private static int ParseIntBits(string s, string kind)
    {
        if (s.Length == 0) return 256;
        if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var bits))
            throw new ArgumentException($"invalid {kind} width {s}");
        if (bits <= 0 || bits > 256 || bits % 8 != 0)
            throw new ArgumentException($"invalid {kind} width {bits}");
        return bits;
    }

    private static byte[] EncodeTuple(IList<string> specs, IReadOnlyList<object?> values)
    {
        var types = specs.Select(ParseType).ToArray();
        return EncodeComponents(types, values);
    }

    private static byte[] EncodeComponents(IReadOnlyList<AbiType> types, IReadOnlyList<object?> args)
    {
        var tails = new byte[types.Count][];
        for (var i = 0; i < types.Count; i++) tails[i] = EncodeOne(types[i], args[i]);

        var headSize = 32 * types.Count;
        var cursor = headSize;
        var offsets = new int[types.Count];
        for (var i = 0; i < types.Count; i++)
            if (types[i].IsDynamic) { offsets[i] = cursor; cursor += tails[i].Length; }

        var output = new byte[cursor];
        var pos = 0;
        for (var i = 0; i < types.Count; i++)
        {
            if (types[i].IsDynamic)
            {
                Uint256Bytes(new BigInteger(offsets[i])).CopyTo(output.AsSpan(pos));
                pos += 32;
            }
            else
            {
                tails[i].CopyTo(output.AsSpan(pos));
                pos += tails[i].Length;
            }
        }
        for (var i = 0; i < types.Count; i++)
            if (types[i].IsDynamic)
            {
                tails[i].CopyTo(output.AsSpan(pos));
                pos += tails[i].Length;
            }
        return output;
    }

    private static byte[] EncodeOne(AbiType t, object? v)
    {
        switch (t.Kind)
        {
            case Kind.Uint:    return Uint256Bytes(ToBigUint(v, t.Size));
            case Kind.Int:     return Int256Bytes(ToBigInt(v, t.Size));
            case Kind.Address: return PadLeft(NormaliseEvmAddress(AsString(v, "address")), 32);
            case Kind.Bool:
                var b = new byte[32];
                if (v is bool bb && bb) b[31] = 1;
                else if (v is not bool) throw new ArgumentException($"bool: want bool, got {v?.GetType()}");
                return b;
            case Kind.BytesN:
                var fb = ToBytes(v);
                if (fb.Length != t.Size)
                    throw new ArgumentException($"bytes{t.Size}: expected {t.Size} bytes, got {fb.Length}");
                var pn = new byte[32];
                fb.CopyTo(pn, 0);
                return pn;
            case Kind.Bytes:   return EncodeDynBytes(ToBytes(v));
            case Kind.String:  return EncodeDynBytes(Encoding.UTF8.GetBytes(AsString(v, "string")));
            case Kind.Array:
                var items = ToObjectList(v);
                if (t.Size >= 0 && items.Count != t.Size)
                    throw new ArgumentException(
                        $"fixed array T[{t.Size}]: expected {t.Size} items, got {items.Count}");
                var inner = Enumerable.Repeat(t.Element!, items.Count).ToArray();
                var body = EncodeComponents(inner, items);
                if (t.Size < 0)
                {
                    var output = new byte[32 + body.Length];
                    Uint256Bytes(new BigInteger(items.Count)).CopyTo(output.AsSpan(0, 32));
                    body.CopyTo(output.AsSpan(32));
                    return output;
                }
                return body;
        }
        throw new ArgumentException($"unsupported kind {t.Kind}");
    }

    private static byte[] EncodeDynBytes(byte[] data)
    {
        var rounded = ((data.Length + 31) / 32) * 32;
        var output = new byte[32 + rounded];
        Uint256Bytes(new BigInteger(data.Length)).CopyTo(output.AsSpan(0, 32));
        data.CopyTo(output.AsSpan(32));
        return output;
    }

    private static byte[] Uint256Bytes(BigInteger n)
    {
        var output = new byte[32];
        if (n.Sign == 0) return output;
        if (n.Sign < 0)
        {
            var two256 = BigInteger.One << 256;
            n += two256;
        }
        var raw = n.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length > 32) raw = raw[(raw.Length - 32)..];
        raw.CopyTo(output, 32 - raw.Length);
        return output;
    }

    private static byte[] Int256Bytes(BigInteger n)
    {
        if (n.Sign >= 0) return Uint256Bytes(n);
        var two256 = BigInteger.One << 256;
        return Uint256Bytes(n + two256);
    }

    private static byte[] PadLeft(byte[] addr, int width)
    {
        var output = new byte[width];
        addr.CopyTo(output, width - addr.Length);
        return output;
    }

    private static BigInteger ToBigUint(object? v, int bits)
    {
        var n = ToBigInt(v, bits);
        if (n.Sign < 0)
            throw new ArgumentException($"uint{bits}: negative value {n}");
        var max = BigInteger.One << bits;
        if (n >= max)
            throw new ArgumentException($"uint{bits}: value {n} exceeds max");
        return n;
    }

    private static BigInteger ToBigInt(object? v, int bits) => v switch
    {
        BigInteger bi => bi,
        sbyte sb => new BigInteger(sb),
        byte b => new BigInteger(b),
        short s => new BigInteger(s),
        ushort us => new BigInteger(us),
        int i => new BigInteger(i),
        uint ui => new BigInteger(ui),
        long l => new BigInteger(l),
        ulong ul => new BigInteger(ul),
        string s => ParseBigIntString(s),
        _ => throw new ArgumentException($"integer: unsupported type {v?.GetType()}"),
    };

    private static BigInteger ParseBigIntString(string s)
    {
        s = s.Trim();
        if (s.Length == 0) throw new ArgumentException("empty integer string");
        // Leading "0" forces the high bit to be read as unsigned.
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return BigInteger.Parse("0" + s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return BigInteger.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static byte[] ToBytes(object? v) => v switch
    {
        byte[] b => b,
        ReadOnlyMemory<byte> rom => rom.ToArray(),
        Memory<byte> m => m.ToArray(),
        string s => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("0X", StringComparison.Ordinal)
            ? Hex.Decode(s[2..])
            : Encoding.UTF8.GetBytes(s),
        _ => throw new ArgumentException($"bytes: unsupported type {v?.GetType()}"),
    };

    private static string AsString(object? v, string what) =>
        v as string ?? throw new ArgumentException($"{what}: want string, got {v?.GetType()}");

    private static List<object?> ToObjectList(object? v)
    {
        if (v is null) throw new ArgumentException("array: null");
        if (v is System.Collections.IEnumerable e and not string)
        {
            var list = new List<object?>();
            foreach (var item in e) list.Add(item);
            return list;
        }
        throw new ArgumentException($"array: unsupported type {v.GetType()}");
    }

    private static byte[] NormaliseEvmAddress(string s)
    {
        s = s.Trim();
        if (s.Length == 0) throw new ArgumentException("address: empty");

        if (s.Length >= 30 && (s[0] == 'T' || s[0] == 't')
            && !s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var hex = TronAddress.ToHex(s);
            var raw = Hex.Decode(hex[2..]);
            if (raw.Length == 21 && raw[0] == 0x41) return raw[1..];
            if (raw.Length == 20) return raw;
            throw new ArgumentException($"address: unexpected TRON length {raw.Length}");
        }

        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        if (s.Length == 42 && s.StartsWith("41", StringComparison.Ordinal)) s = s[2..];
        if (s.Length != 40)
            throw new ArgumentException($"address: want 20 hex bytes, got {s.Length} chars");
        return Hex.Decode(s);
    }
}
