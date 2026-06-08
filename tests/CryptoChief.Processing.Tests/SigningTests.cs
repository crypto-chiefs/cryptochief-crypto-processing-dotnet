using System.Text;
using CryptoChief.Processing.Http;
using CryptoChief.Processing.Internal;
using CryptoChief.Processing.Webhooks;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class SigningTests
{
    private const string ApiKey = "test-api-key";

    [Fact]
    public void Sign_empty_body_uses_md5_of_apikey_only()
    {
        var sig = RequestSigner.Sign(ReadOnlySpan<byte>.Empty, ApiKey);
        sig.Should().HaveLength(32).And.MatchRegex("^[a-f0-9]+$");
    }

    [Fact]
    public void CanonicalJson_is_byte_stable_under_property_reordering()
    {
        var a = new { z = 1, a = 2 };
        var b = new { a = 2, z = 1 };
        CanonicalJson.Encode(a).Should().Equal(CanonicalJson.Encode(b));
    }

    [Fact]
    public void CanonicalJson_sorts_keys_lexicographically()
    {
        var input = new { z = 1, a = 2, m = new { c = 3, b = 4 } };
        var bytes = CanonicalJson.Encode(input);
        var json = Encoding.UTF8.GetString(bytes);
        json.Should().Be("{\"a\":2,\"m\":{\"b\":4,\"c\":3},\"z\":1}");
    }

    [Fact]
    public void Sign_then_verify_roundtrip_succeeds()
    {
        var body = new { order_id = "ord-1", amount = "0.5", network = "ETH_SEPOLIA" };
        var canonical = CanonicalJson.Encode(body);
        var sig = RequestSigner.Sign(canonical, ApiKey);

        FluentActions.Invoking(() => WebhookVerifier.Verify(ApiKey, canonical, sig))
            .Should().NotThrow();
    }

    [Fact]
    public void Webhook_verify_rejects_tampered_body()
    {
        var body = Encoding.UTF8.GetBytes("{\"a\":1}");
        var canonical = CanonicalJson.Canonicalise(body);
        var sig = RequestSigner.Sign(canonical, ApiKey);
        var tampered = Encoding.UTF8.GetBytes("{\"a\":2}");

        WebhookVerifier.TryVerify(ApiKey, tampered, sig).Should().BeFalse();
    }

    [Fact]
    public void Webhook_verify_tolerates_property_reordering()
    {
        var bodyA = Encoding.UTF8.GetBytes("{\"a\":1,\"b\":2}");
        var bodyB = Encoding.UTF8.GetBytes("{\"b\":2,\"a\":1}");
        var canonical = CanonicalJson.Canonicalise(bodyA);
        var sig = RequestSigner.Sign(canonical, ApiKey);

        WebhookVerifier.TryVerify(ApiKey, bodyB, sig).Should().BeTrue();
    }
}
