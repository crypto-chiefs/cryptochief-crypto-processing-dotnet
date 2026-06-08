using CryptoChief.Processing.Encoders.Tron;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class TronTests
{
    private const string UsdtBase58 = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";
    private const string UsdtHex    = "0x41a614f803b6fd780986a42c78ec9c7f77e6ded13c";

    [Fact]
    public void Base58_round_trip()
    {
        var raw = new byte[] { 0x00, 0x01, 0x02, 0xff };
        var encoded = Base58.Encode(raw);
        var decoded = Base58.Decode(encoded);
        decoded.Should().Equal(raw);
    }

    [Fact]
    public void TronToHex_well_known_usdt() =>
        TronAddress.ToHex(UsdtBase58).Should().Be(UsdtHex);

    [Fact]
    public void HexToTron_round_trip() =>
        TronAddress.FromHex(UsdtHex).Should().Be(UsdtBase58);

    [Fact]
    public void TronToHex_rejects_checksum_mismatch() =>
        FluentActions.Invoking(() => TronAddress.ToHex("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6u"))
            .Should().Throw<FormatException>();
}
