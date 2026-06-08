using CryptoChief.Processing.Encoders.Evm;
using CryptoChief.Processing.Encoders.Solana;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class BorshTests
{
    [Fact]
    public void U64_is_little_endian() =>
        Hex.ToLower(Borsh.U64(1).ToBytes())
            .Should().Be("0100000000000000");

    [Fact]
    public void String_has_4_byte_length_prefix() =>
        Hex.ToLower(Borsh.String("hi").ToBytes())
            .Should().Be("020000006869");

    [Fact]
    public void Bool_is_one_byte()
    {
        Hex.ToLower(Borsh.Bool(true).ToBytes()).Should().Be("01");
        Hex.ToLower(Borsh.Bool(false).ToBytes()).Should().Be("00");
    }

    [Fact]
    public void Option_none_is_zero_byte() =>
        Hex.ToLower(Borsh.Option(null).ToBytes()).Should().Be("00");

    [Fact]
    public void Option_some_prefixes_with_one() =>
        Hex.ToLower(Borsh.Option(Borsh.U8(7)).ToBytes()).Should().Be("0107");

    [Theory]
    [InlineData("initialize")]
    [InlineData("transfer")]
    [InlineData("swap")]
    [InlineData("set_authority")]
    public void Anchor_discriminator_is_sha256_of_global_prefixed_method(string method)
    {
        var got = AnchorInstruction.Discriminator(method);
#if NET8_0_OR_GREATER
        var want = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("global:" + method));
#else
        using var sha = System.Security.Cryptography.SHA256.Create();
        var want = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("global:" + method));
#endif
        got.Should().Equal(want[..8]);
    }

    [Fact]
    public void Anchor_instruction_appends_args_after_discriminator()
    {
        var data = AnchorInstruction.Encode("initialize", Borsh.U64(42));
        data.Length.Should().Be(16);
        var disc = AnchorInstruction.Discriminator("initialize");
        data[..8].Should().Equal(disc);
        Hex.ToLower(data[8..]).Should().Be("2a00000000000000");
    }
}
