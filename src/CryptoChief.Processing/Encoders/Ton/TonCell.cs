using System.Numerics;
using System.Text;

namespace CryptoChief.Processing.Encoders.Ton;

/// <summary>
/// Minimal TON cell builder. Covers what the Jetton / NFT / text-comment
/// helpers need: uint, bit, address, VarUInteger 16 (coins), Maybe Ref,
/// Either inline-or-ref, snake-string. Emits a well-formed BoC.
/// </summary>
public sealed class TonCell
{
    private readonly List<bool> _bits = new();
    private readonly List<TonCell> _refs = new();

    public int BitLength => _bits.Count;
    public int RefCount => _refs.Count;

    public TonCell StoreBit(bool b) { _bits.Add(b); return this; }

    public TonCell StoreUInt(BigInteger n, int bits)
    {
        if (bits is < 0 or > 256) throw new ArgumentOutOfRangeException(nameof(bits));
        if (n.Sign < 0) throw new ArgumentOutOfRangeException(nameof(n));
        for (var i = bits - 1; i >= 0; i--)
            _bits.Add(((n >> i) & 1) == 1);
        return this;
    }

    public TonCell StoreUInt(ulong n, int bits) => StoreUInt(new BigInteger(n), bits);

    public TonCell StoreRef(TonCell child)
    {
        if (_refs.Count >= 4) throw new InvalidOperationException("ton cell: refs > 4");
        _refs.Add(child);
        return this;
    }

    public TonCell StoreAddress(TonAddress? addr)
    {
        if (addr is null)
        {
            // addr_none$00
            _bits.Add(false); _bits.Add(false);
            return this;
        }
        // addr_std$10 anycast=0 wc:int8 hash:bits256
        _bits.Add(true); _bits.Add(false);
        _bits.Add(false);
        for (var i = 7; i >= 0; i--) _bits.Add((((byte)addr.Workchain) >> i & 1) == 1);
        foreach (var b in addr.Hash)
            for (var i = 7; i >= 0; i--) _bits.Add(((b >> i) & 1) == 1);
        return this;
    }

    /// <summary>VarUInteger 16: 4-bit length + that many bytes. The TON Coins type.</summary>
    public TonCell StoreCoins(BigInteger amount)
    {
        if (amount.Sign < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount.IsZero)
        {
            StoreUInt(0, 4);
            return this;
        }
        var raw = amount.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length > 15) throw new ArgumentOutOfRangeException(nameof(amount), "VarUInt16 overflow");
        StoreUInt((ulong)raw.Length, 4);
        foreach (var b in raw)
            for (var i = 7; i >= 0; i--) _bits.Add(((b >> i) & 1) == 1);
        return this;
    }

    public TonCell StoreMaybeRef(TonCell? child)
    {
        if (child is null) { _bits.Add(false); return this; }
        _bits.Add(true);
        StoreRef(child);
        return this;
    }

    /// <summary>UTF-8 string in canonical "snake" form (chained refs when bytes exceed cell capacity).</summary>
    public TonCell StoreStringSnake(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        StoreSnakeBytes(this, bytes);
        return this;
    }

    private static void StoreSnakeBytes(TonCell into, ReadOnlySpan<byte> data)
    {
        var fittable = (1023 - into._bits.Count) / 8;
        var here = Math.Min(fittable, data.Length);
        for (var i = 0; i < here; i++)
        {
            var b = data[i];
            for (var j = 7; j >= 0; j--) into._bits.Add(((b >> j) & 1) == 1);
        }
        if (here < data.Length)
        {
            var child = new TonCell();
            StoreSnakeBytes(child, data[here..]);
            into.StoreRef(child);
        }
    }

    /// <summary>Serialize this cell + ref subtree as a Bag-of-Cells byte sequence.</summary>
    public byte[] ToBoc()
    {
        // BoC requires cells listed so every ref index is strictly greater
        // than the storing cell's index. Pre-order DFS gives that for the
        // single-parent trees used by the helpers.
        var order = new List<TonCell>();
        var seen = new HashSet<TonCell>(ReferenceEqualityComparer.Instance);
        Visit(this);
        var indexOf = new Dictionary<TonCell, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < order.Count; i++) indexOf[order[i]] = i;

        var refSize = ByteSize(Math.Max(order.Count - 1, 1));
        var totalDataLen = 0;
        var serialised = new List<byte[]>(order.Count);
        foreach (var c in order)
        {
            var raw = SerialiseCell(c, indexOf, refSize);
            serialised.Add(raw);
            totalDataLen += raw.Length;
        }
        var offsetSize = ByteSize(Math.Max(totalDataLen, 1));

        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0xB5, 0xEE, 0x9C, 0x72 }, 0, 4);
        ms.WriteByte((byte)refSize);
        ms.WriteByte((byte)offsetSize);
        WriteBE(ms, (ulong)order.Count, refSize);
        WriteBE(ms, 1, refSize);
        WriteBE(ms, 0, refSize);
        WriteBE(ms, (ulong)totalDataLen, offsetSize);
        WriteBE(ms, 0, refSize);
        foreach (var s in serialised) ms.Write(s, 0, s.Length);
        return ms.ToArray();

        void Visit(TonCell c)
        {
            if (!seen.Add(c)) return;
            order.Add(c);
            foreach (var r in c._refs) Visit(r);
        }
    }

    private static byte[] SerialiseCell(TonCell c, Dictionary<TonCell, int> idx, int refSize = 1)
    {
        var refs = c._refs;
        var bits = c._bits.Count;
        var fullBytes = bits / 8;
        var spareBits = bits % 8;
        var dataLen = fullBytes + (spareBits > 0 ? 1 : 0);

        using var ms = new MemoryStream();
        ms.WriteByte((byte)refs.Count);
        ms.WriteByte((byte)(fullBytes * 2 + (spareBits > 0 ? 1 : 0)));

        if (dataLen > 0)
        {
            var data = new byte[dataLen];
            for (var i = 0; i < bits; i++)
                if (c._bits[i]) data[i / 8] |= (byte)(1 << (7 - i % 8));
            if (spareBits > 0)
            {
                var pos = bits;
                data[pos / 8] |= (byte)(1 << (7 - pos % 8));
            }
            ms.Write(data, 0, data.Length);
        }
        foreach (var r in refs)
            WriteBE(ms, (ulong)idx[r], refSize);
        return ms.ToArray();
    }

    private static void WriteBE(Stream s, ulong value, int size)
    {
        for (var i = size - 1; i >= 0; i--)
            s.WriteByte((byte)((value >> (8 * i)) & 0xFF));
    }

    private static int ByteSize(int n)
    {
        var bits = 0;
        while (n > 0) { bits++; n >>= 1; }
        return Math.Max(1, (bits + 7) / 8);
    }
}
