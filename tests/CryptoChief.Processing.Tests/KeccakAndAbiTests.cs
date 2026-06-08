using System.Numerics;
using CryptoChief.Processing.Encoders.Evm;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class KeccakAndAbiTests
{
    [Fact]
    public void Keccak256_of_empty_string()
    {
        var hash = Keccak256.Hash(ReadOnlySpan<byte>.Empty);
        Hex.ToLower(hash).Should().Be(
            "c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470");
    }

    [Fact]
    public void Keccak256_of_abc()
    {
        var hash = Keccak256.Hash(System.Text.Encoding.UTF8.GetBytes("abc"));
        Hex.ToLower(hash).Should().Be(
            "4e03657aea45a94fc7d47ba826c8d667c0d1e6e33a64a036ec44f58fa12d6c45");
    }

    [Fact]
    public void EvmAbi_selector_transfer()
    {
        var sel = EvmAbi.Selector("transfer(address,uint256)");
        Hex.ToLower(sel).Should().Be("a9059cbb");
    }

    [Fact]
    public void EvmAbi_selector_balanceOf()
    {
        var sel = EvmAbi.Selector("balanceOf(address)");
        Hex.ToLower(sel).Should().Be("70a08231");
    }

    [Fact]
    public void EvmAbi_encodes_erc20_transfer()
    {
        var calldata = EvmAbi.EncodeCallHex(
            "transfer(address,uint256)",
            "0xae967917c465db8578ca9024c205720b1a3651a9",
            new BigInteger(1000));
        calldata.Should().Be(
            "0xa9059cbb000000000000000000000000ae967917c465db8578ca9024c205720b1a3651a9"
            + "00000000000000000000000000000000000000000000000000000000000003e8");
    }

    [Fact]
    public void EvmAbi_normalises_aliases_in_selector() =>
        EvmAbi.Selector("transfer(address,uint)")
            .Should().BeEquivalentTo(EvmAbi.Selector("transfer(address,uint256)"));

    [Fact]
    public void EvmAbi_strips_parameter_names() =>
        EvmAbi.Selector("transfer(address to, uint256 amount)")
            .Should().BeEquivalentTo(EvmAbi.Selector("transfer(address,uint256)"));

    [Fact]
    public void EvmAbi_rejects_arg_count_mismatch() =>
        FluentActions.Invoking(() => EvmAbi.EncodeCallHex("transfer(address,uint256)", "0x0"))
            .Should().Throw<ArgumentException>();
}
