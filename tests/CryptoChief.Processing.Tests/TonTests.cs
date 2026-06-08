using CryptoChief.Processing.Encoders.Ton;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class TonTests
{
    [Fact]
    public void Parse_user_friendly_round_trips()
    {
        const string usdtMaster = "EQCxE6mUtQJKFnGfaROTKOt1lZbDiiX1kCixRv7Nw2Id_sDs";
        var addr = TonAddress.Parse(usdtMaster);
        addr.ToString().Should().Be(usdtMaster);
        addr.Bounceable.Should().BeTrue();
        addr.Testnet.Should().BeFalse();
    }

    [Fact]
    public void Parse_raw_form_yields_same_hash()
    {
        const string usdtMaster = "EQCxE6mUtQJKFnGfaROTKOt1lZbDiiX1kCixRv7Nw2Id_sDs";
        var addr = TonAddress.Parse(usdtMaster);
        var raw = addr.ToRaw();
        raw.Should().StartWith("0:");
        var addr2 = TonAddress.Parse(raw);
        addr2.Hash.Should().Equal(addr.Hash);
    }

    [Fact]
    public void Parse_rejects_corrupted_crc() =>
        FluentActions.Invoking(() =>
            TonAddress.Parse("EQCxE6mUtQJKFnGfaROTKOt1lZbDiiX1kCixRv7Nw2Id_sDt"))
            .Should().Throw<FormatException>();

    [Fact]
    public void Text_comment_body_has_boc_magic()
    {
        var boc = TonMessages.BuildTextCommentBody("hello");
        boc.Should().NotBeEmpty();
        boc[0].Should().Be(0xB5);
    }
}
