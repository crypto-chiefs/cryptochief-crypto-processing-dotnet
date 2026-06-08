using System.Numerics;
using CryptoChief.Processing.Amounts;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class AmountTests
{
    [Theory]
    [InlineData("1.5", 18, "1500000000000000000")]
    [InlineData("0.0001", 8, "10000")]
    [InlineData("1", 0, "1")]
    [InlineData("0.5", 6, "500000")]
    [InlineData(".25", 4, "2500")]
    [InlineData("100", 6, "100000000")]
    [InlineData("0", 18, "0")]
    public void HumanToBase_known_values(string human, int decimals, string expected)
    {
        Amount.HumanToBase(human, decimals).Should()
            .Be(BigInteger.Parse(expected));
    }

    [Theory]
    [InlineData("1.123456789", 6, "1123456")]
    public void HumanToBase_truncates_excess_precision(string human, int decimals, string expected) =>
        Amount.HumanToBase(human, decimals).Should().Be(BigInteger.Parse(expected));

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("1.0e10")]
    [InlineData("1.0E10")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    public void HumanToBase_rejects_bad_input(string human) =>
        FluentActions.Invoking(() => Amount.HumanToBase(human, 18))
            .Should().Throw<FormatException>();

    [Theory]
    [InlineData("1500000000000000000", 18, "1.5")]
    [InlineData("0", 18, "0")]
    [InlineData("10000", 8, "0.0001")]
    [InlineData("1", 8, "0.00000001")]
    [InlineData("1000000", 6, "1")]
    public void BaseToHuman_known_values(string baseUnits, int decimals, string expected) =>
        Amount.BaseToHuman(BigInteger.Parse(baseUnits), decimals).Should().Be(expected);

    [Theory]
    [InlineData("0.05", "50000000")]
    [InlineData("1", "1000000000")]
    [InlineData("1.234567891", "1234567891")]
    public void NanoTon_converts_correctly(string human, string expected) =>
        Amount.NanoTon(human).Should().Be(expected);

    [Fact]
    public void Round_trip_preserves_value()
    {
        var original = "12345.6789012345";
        var baseUnits = Amount.HumanToBase(original, 10);
        var roundTrip = Amount.BaseToHuman(baseUnits, 10);
        roundTrip.Should().Be(original);
    }
}
