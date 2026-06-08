using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using CryptoChief.Processing.Encoders.Tron;

namespace CryptoChief.Processing.Encoders.Solana;

/// <summary>A Borsh-typed value. Anchor instruction layout: [discriminator:8][args: Borsh].</summary>
public abstract class BorshValue
{
    public abstract void WriteTo(BinaryWriter w);
    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        WriteTo(bw);
        bw.Flush();
        return ms.ToArray();
    }
}

/// <summary>Borsh value factories.</summary>
public static class Borsh
{
    public static BorshValue U8(byte n) => new BorshU8Impl(n);
    public static BorshValue U16(ushort n) => new BorshU16Impl(n);
    public static BorshValue U32(uint n) => new BorshU32Impl(n);
    public static BorshValue U64(ulong n) => new BorshU64Impl(n);
    public static BorshValue U128(BigInteger n) => new BorshU128Impl(n);

    public static BorshValue I8(sbyte n) => new BorshU8Impl(unchecked((byte)n));
    public static BorshValue I16(short n) => new BorshU16Impl(unchecked((ushort)n));
    public static BorshValue I32(int n) => new BorshU32Impl(unchecked((uint)n));
    public static BorshValue I64(long n) => new BorshU64Impl(unchecked((ulong)n));

    public static BorshValue Bool(bool b) => new BorshBoolImpl(b);
    public static BorshValue String(string s) => new BorshStringImpl(s);

    public static BorshValue Bytes(byte[] b) => new BorshBytesImpl(b);

    /// <summary>Anchor <c>[u8; N]</c> — fixed length, no prefix.</summary>
    public static BorshValue FixedBytes(byte[] b, int n) => new BorshFixedBytesImpl(b, n);

    /// <summary>Solana 32-byte pubkey from base58 string or raw bytes.</summary>
    public static BorshValue Pubkey(object pk) => new BorshPubkeyImpl(pk);

    /// <summary>Borsh <c>Option&lt;T&gt;</c> — null → 0x00; value → 0x01 + inner.</summary>
    public static BorshValue Option(BorshValue? inner) => new BorshOptionImpl(inner);

    /// <summary>Borsh <c>Vec&lt;T&gt;</c> — 4-byte length + elements.</summary>
    public static BorshValue Vec(IEnumerable<BorshValue> items) => new BorshVecImpl(items.ToList());

    /// <summary>Heterogeneous tuple — fields in order with no length prefix.</summary>
    public static BorshValue Struct(params BorshValue[] fields) => new BorshStructImpl(fields);

    private sealed class BorshU8Impl(byte n) : BorshValue
    {
        public override void WriteTo(BinaryWriter w) => w.Write(n);
    }
    private sealed class BorshU16Impl(ushort n) : BorshValue
    {
        public override void WriteTo(BinaryWriter w)
        {
            Span<byte> b = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(b, n);
            w.Write(b);
        }
    }
    private sealed class BorshU32Impl(uint n) : BorshValue
    {
        public override void WriteTo(BinaryWriter w)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, n);
            w.Write(b);
        }
    }
    private sealed class BorshU64Impl(ulong n) : BorshValue
    {
        public override void WriteTo(BinaryWriter w)
        {
            Span<byte> b = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(b, n);
            w.Write(b);
        }
    }
    private sealed class BorshU128Impl(BigInteger n) : BorshValue
    {
        public override void WriteTo(BinaryWriter w)
        {
            if (n.Sign < 0) throw new ArgumentOutOfRangeException(nameof(n), "u128 negative");
            var max = BigInteger.One << 128;
            if (n >= max) throw new ArgumentOutOfRangeException(nameof(n), "u128 overflow");
            var raw = n.ToByteArray(isUnsigned: true, isBigEndian: false);
            Span<byte> b = stackalloc byte[16];
            raw.CopyTo(b);
            w.Write(b);
        }
    }
    private sealed class BorshBoolImpl(bool v) : BorshValue
    {
        public override void WriteTo(BinaryWriter w) => w.Write(v ? (byte)1 : (byte)0);
    }
    private sealed class BorshStringImpl(string s) : BorshValue
    {
        public override void WriteTo(BinaryWriter w)
        {
            var b = Encoding.UTF8.GetBytes(s);
            Span<byte> len = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(len, (uint)b.Length);
            w.Write(len);
            w.Write(b);
        }
    }
    private sealed class BorshBytesImpl(byte[] b) : BorshValue
    {
        public override void WriteTo(BinaryWriter w)
        {
            Span<byte> len = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(len, (uint)b.Length);
            w.Write(len);
            w.Write(b);
        }
    }
    private sealed class BorshFixedBytesImpl(byte[] b, int n) : BorshValue
    {
        public override void WriteTo(BinaryWriter w)
        {
            if (b.Length != n)
                throw new ArgumentException($"BorshFixedBytes: expected {n} bytes, got {b.Length}");
            w.Write(b);
        }
    }
    private sealed class BorshPubkeyImpl(object pk) : BorshValue
    {
        public override void WriteTo(BinaryWriter w)
        {
            var raw = DecodePubkey(pk);
            w.Write(raw);
        }
        internal static byte[] DecodePubkey(object pk) => pk switch
        {
            byte[] b when b.Length == 32 => b,
            byte[] b => throw new ArgumentException($"solana pubkey: want 32 bytes, got {b.Length}"),
            string s => DecodeFromBase58(s),
            _ => throw new ArgumentException($"solana pubkey: unsupported type {pk?.GetType()}"),
        };
        private static byte[] DecodeFromBase58(string s)
        {
            var raw = Base58.Decode(s);
            if (raw.Length != 32)
                throw new ArgumentException($"solana pubkey: decoded length {raw.Length}, want 32");
            return raw;
        }
    }
    private sealed class BorshOptionImpl(BorshValue? inner) : BorshValue
    {
        public override void WriteTo(BinaryWriter w)
        {
            if (inner is null) { w.Write((byte)0); return; }
            w.Write((byte)1);
            inner.WriteTo(w);
        }
    }
    private sealed class BorshVecImpl(IReadOnlyList<BorshValue> items) : BorshValue
    {
        public override void WriteTo(BinaryWriter w)
        {
            Span<byte> len = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(len, (uint)items.Count);
            w.Write(len);
            foreach (var it in items) it.WriteTo(w);
        }
    }
    private sealed class BorshStructImpl(IReadOnlyList<BorshValue> fields) : BorshValue
    {
        public override void WriteTo(BinaryWriter w)
        {
            foreach (var f in fields) f.WriteTo(w);
        }
    }
}

/// <summary>Anchor program-instruction encoder.</summary>
public static class AnchorInstruction
{
    /// <summary>8-byte SHA-256 discriminator: <c>SHA-256("global:&lt;method&gt;")[..8]</c>.</summary>
    public static byte[] Discriminator(string method)
    {
        var raw = Encoding.UTF8.GetBytes("global:" + method);
#if NET8_0_OR_GREATER
        var hash = SHA256.HashData(raw);
#else
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(raw);
#endif
        var output = new byte[8];
        Array.Copy(hash, 0, output, 0, 8);
        return output;
    }

    /// <summary>Discriminator + Borsh-encoded args.</summary>
    public static byte[] Encode(string method, params BorshValue[] args)
    {
        using var ms = new MemoryStream();
        ms.Write(Discriminator(method), 0, 8);
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        foreach (var a in args) a.WriteTo(bw);
        bw.Flush();
        return ms.ToArray();
    }
}
