using CryptoChief.Processing.Http;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class HmacV1SigningTests
{
    public static TheoryData<string> VectorNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in HmacV1Vectors.All.Keys) data.Add(name);
        return data;
    }

    [Fact]
    public void Vector_file_is_the_reference_copy()
    {
        HmacV1Vectors.ComputeFileSha256().Should().Be(HmacV1Vectors.FileSha256);
        File.ReadAllBytes(HmacV1Vectors.FilePath).Should().NotContain((byte)'\r');
        HmacV1Vectors.List.Should().HaveCount(50);
        HmacV1Vectors.All.Should().HaveCount(HmacV1Vectors.List.Count);
        HmacV1Vectors.List.GroupBy(v => v.Expect).ToDictionary(g => g.Key, g => g.Count())
            .Should().BeEquivalentTo(new Dictionary<string, int>
            {
                ["ok"] = 21,
                ["bad_auth_headers"] = 24,
                ["invalid_signature"] = 3,
                ["timestamp_out_of_range"] = 2,
            });
    }

    // string_to_sign and signature hold in every record, refusals included.
    [Theory]
    [MemberData(nameof(VectorNames))]
    public void Vector_matches_gateway(string name)
    {
        var v = HmacV1Vectors.Get(name);

        RequestSigner.BodySha256(v.BodyBytes).Should().Be(v.BodySha256);
        RequestSigner.HmacV1StringToSign(v.ToInput()).Should().Be(v.StringToSign);
        RequestSigner.SignHmacV1(v.ApiKey, v.ToInput()).Should()
            .Be(RequestSigner.HmacV1SignaturePrefix + v.Signature);
    }

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void Vector_check_gives_the_expected_outcome(string name)
    {
        var v = HmacV1Vectors.Get(name);

        GatewayHmacV1.Check(v.ToRequest(), v.ApiKey, v.Now).Should().Be(v.Expect);
    }

    // Each refusal record carries exactly one defect: its headers, or a now outside the window.
    [Theory]
    [MemberData(nameof(VectorNames))]
    public void Vector_without_its_defect_passes(string name)
    {
        var v = HmacV1Vectors.Get(name);
        var now = v.Expect == GatewayHmacV1.TimestampOutOfRange
            ? long.Parse(v.Timestamp)
            : v.Now;

        GatewayHmacV1.Check(v.ToRequest(applyOverrides: false), v.ApiKey, now)
            .Should().Be(GatewayHmacV1.Ok);
    }

    [Fact]
    public void Method_is_upper_cased()
    {
        var v = HmacV1Vectors.Get("get_query_empty_body");
        var lower = WithMethod(v.ToInput(), "get");

        RequestSigner.HmacV1StringToSign(lower).Should().Be(v.StringToSign);
        RequestSigner.SignHmacV1(v.ApiKey, lower).Should()
            .Be(RequestSigner.HmacV1SignaturePrefix + v.Signature);
    }

    [Fact]
    public void Lowercase_method_vector_signs_as_upper_case()
    {
        var v = HmacV1Vectors.Get("method_lowercase");

        v.Method.Should().Be("post");
        v.StringToSign.Split('\n')[3].Should().Be("POST");
        RequestSigner.SignHmacV1(v.ApiKey, v.ToInput()).Should()
            .Be(RequestSigner.HmacV1SignaturePrefix + v.Signature);
    }

    // Only a-z is upper-cased: an HTTP method is an RFC 9110 token, and a Unicode mapping would
    // rewrite bytes the server leaves alone.
    [Theory]
    [InlineData("pOsT", "POST")]
    [InlineData("poßt", "POßT")]
    [InlineData("getſ", "GETſ")]
    [InlineData("пост", "пост")]
    [InlineData("ıd", "ıD")]
    public void Method_is_upper_cased_over_ascii_only(string method, string signedAs)
    {
        var v = HmacV1Vectors.Get("empty_body");

        RequestSigner.UpperAsciiMethod(method).Should().Be(signedAs);
        RequestSigner.HmacV1StringToSign(WithMethod(v.ToInput(), method))
            .Split('\n')[3].Should().Be(signedAs);
    }

    [Theory]
    [InlineData("timestamp")]
    [InlineData("nonce")]
    [InlineData("method")]
    [InlineData("path")]
    [InlineData("query")]
    [InlineData("merchant")]
    [InlineData("idempotency_key")]
    public void Field_with_cr_or_lf_is_rejected(string field)
    {
        foreach (var bad in new[] { "a\nb", "a\rb" })
        {
            var input = new HmacV1Input
            {
                Timestamp = field == "timestamp" ? bad : "1789430400",
                Nonce = field == "nonce" ? bad : "0123456789abcdef0123456789abcdef",
                Method = field == "method" ? bad : "POST",
                Path = field == "path" ? bad : "/v1/credits/balance",
                Query = field == "query" ? bad : "",
                Merchant = field == "merchant" ? bad : "M-1",
                IdempotencyKey = field == "idempotency_key" ? bad : "",
            };

            FluentActions.Invoking(() => RequestSigner.HmacV1StringToSign(input))
                .Should().Throw<ArgumentException>();
            FluentActions.Invoking(() => RequestSigner.SignHmacV1("K-1", input))
                .Should().Throw<ArgumentException>();
        }
    }

    [Fact]
    public void Body_may_contain_line_breaks()
    {
        var v = HmacV1Vectors.Get("body_whitespace_and_line_breaks");
        v.Body.Should().Contain("\r\n");
        RequestSigner.SignHmacV1(v.ApiKey, v.ToInput()).Should()
            .Be(RequestSigner.HmacV1SignaturePrefix + v.Signature);
    }

    // The signed value goes into X-CC-Signature as it is, and the gateway accepts the request.
    [Fact]
    public void Signed_header_value_passes_the_gateway_check()
    {
        var v = HmacV1Vectors.Get("empty_body");
        var signature = RequestSigner.SignHmacV1(v.ApiKey, v.ToInput());

        var request = new GatewayHmacV1.SignedRequest(v.Method, v.Path, v.Query, v.BodyBytes,
            new[]
            {
                new KeyValuePair<string, string>(GatewayHmacV1.MerchantHeader, v.Merchant),
                new KeyValuePair<string, string>(GatewayHmacV1.TimestampHeader, v.Timestamp),
                new KeyValuePair<string, string>(GatewayHmacV1.NonceHeader, v.Nonce),
                new KeyValuePair<string, string>(GatewayHmacV1.SignatureHeader, signature),
            });

        GatewayHmacV1.Check(request, v.ApiKey, v.Now).Should().Be(GatewayHmacV1.Ok);
    }

    // A key of nothing, or of spaces and tabs only, is no key: the signer refuses it and the
    // server refuses a request signed with one.
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("  \t \t ")]
    public void Blank_api_key_is_rejected(string apiKey)
    {
        var v = HmacV1Vectors.Get("empty_body");

        RequestSigner.IsBlankApiKey(apiKey).Should().BeTrue();
        FluentActions.Invoking(() => RequestSigner.SignHmacV1(apiKey, v.ToInput()))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => RequestSigner.SignWebhookV1(apiKey, 1789430400, "d-1", v.BodyBytes))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new CryptoChiefClient(new CryptoChiefClientOptions
        {
            MerchantId = "M-1", ApiKey = apiKey,
        })).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("  \t \t ")]
    public void Gateway_refuses_a_project_whose_key_is_blank(string apiKey)
    {
        var v = HmacV1Vectors.Get("empty_body");

        GatewayHmacV1.Check(v.ToRequest(), apiKey, v.Now).Should().Be(GatewayHmacV1.InvalidSignature);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("k")]
    public void A_key_that_is_not_blank_signs(string apiKey)
    {
        var v = HmacV1Vectors.Get("empty_body");

        RequestSigner.IsBlankApiKey(apiKey).Should().BeFalse();
        RequestSigner.SignHmacV1(apiKey, v.ToInput()).Should().MatchRegex("^v1=[0-9a-f]{64}$");
    }

    [Fact]
    public void NewNonce_is_32_lower_hex_and_random()
    {
        var a = RequestSigner.NewNonce();
        var b = RequestSigner.NewNonce();
        a.Should().MatchRegex("^[0-9a-f]{32}$");
        b.Should().MatchRegex("^[0-9a-f]{32}$");
        a.Should().NotBe(b);
    }

    private static HmacV1Input WithMethod(HmacV1Input input, string method) => new()
    {
        Timestamp = input.Timestamp,
        Nonce = input.Nonce,
        Method = method,
        Path = input.Path,
        Query = input.Query,
        Merchant = input.Merchant,
        IdempotencyKey = input.IdempotencyKey,
        Body = input.Body,
    };
}
