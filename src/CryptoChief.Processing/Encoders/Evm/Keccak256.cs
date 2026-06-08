using System.Buffers.Binary;

namespace CryptoChief.Processing.Encoders.Evm;

// Keccak-256, the pre-NIST variant Ethereum uses (domain bit 0x01, not 0x06).
internal static class Keccak256
{
    public static byte[] Hash(ReadOnlySpan<byte> data)
    {
        var state = new ulong[25];
        const int rate = 136;

        var fullBlocks = data.Length / rate;
        for (var b = 0; b < fullBlocks; b++)
            AbsorbBlock(state, data.Slice(b * rate, rate));

        Span<byte> last = stackalloc byte[rate];
        var tail = data.Length - fullBlocks * rate;
        data.Slice(fullBlocks * rate, tail).CopyTo(last);
        last[tail] = 0x01;
        last[rate - 1] |= 0x80;
        AbsorbBlock(state, last);

        var hash = new byte[32];
        for (var i = 0; i < 4; i++)
            BinaryPrimitives.WriteUInt64LittleEndian(hash.AsSpan(i * 8), state[i]);
        return hash;
    }

    private static void AbsorbBlock(ulong[] state, ReadOnlySpan<byte> block)
    {
        for (var i = 0; i < block.Length / 8; i++)
            state[i] ^= BinaryPrimitives.ReadUInt64LittleEndian(block.Slice(i * 8, 8));
        KeccakF(state);
    }

    private static readonly ulong[] RoundConstants =
    {
        0x0000000000000001UL, 0x0000000000008082UL, 0x800000000000808AUL, 0x8000000080008000UL,
        0x000000000000808BUL, 0x0000000080000001UL, 0x8000000080008081UL, 0x8000000000008009UL,
        0x000000000000008AUL, 0x0000000000000088UL, 0x0000000080008009UL, 0x000000008000000AUL,
        0x000000008000808BUL, 0x800000000000008BUL, 0x8000000000008089UL, 0x8000000000008003UL,
        0x8000000000008002UL, 0x8000000000000080UL, 0x000000000000800AUL, 0x800000008000000AUL,
        0x8000000080008081UL, 0x8000000000008080UL, 0x0000000080000001UL, 0x8000000080008008UL,
    };

    private static void KeccakF(ulong[] s)
    {
        Span<ulong> c = stackalloc ulong[5];
        Span<ulong> d = stackalloc ulong[5];
        Span<ulong> b = stackalloc ulong[25];

        for (var round = 0; round < 24; round++)
        {
            for (var x = 0; x < 5; x++)
                c[x] = s[x] ^ s[x + 5] ^ s[x + 10] ^ s[x + 15] ^ s[x + 20];
            for (var x = 0; x < 5; x++)
                d[x] = c[(x + 4) % 5] ^ Rotl(c[(x + 1) % 5], 1);
            for (var x = 0; x < 5; x++)
                for (var y = 0; y < 5; y++)
                    s[x + 5 * y] ^= d[x];

            for (var x = 0; x < 5; x++)
                for (var y = 0; y < 5; y++)
                    b[y + 5 * ((2 * x + 3 * y) % 5)] = Rotl(s[x + 5 * y], RotationOffsets[x, y]);

            for (var x = 0; x < 5; x++)
                for (var y = 0; y < 5; y++)
                    s[x + 5 * y] = b[x + 5 * y] ^ ((~b[(x + 1) % 5 + 5 * y]) & b[(x + 2) % 5 + 5 * y]);

            s[0] ^= RoundConstants[round];
        }
    }

    private static ulong Rotl(ulong x, int n) => (x << n) | (x >> (64 - n));

    private static readonly int[,] RotationOffsets =
    {
        {  0, 36,  3, 41, 18 },
        {  1, 44, 10, 45,  2 },
        { 62,  6, 43, 15, 61 },
        { 28, 55, 25, 21, 56 },
        { 27, 20, 39,  8, 14 },
    };
}
