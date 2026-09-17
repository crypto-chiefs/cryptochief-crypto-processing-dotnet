using System.Reflection;
using System.Text;
using System.Text.Json;
using CryptoChief.Processing.Errors;
using CryptoChief.Processing.Http;
using CryptoChief.Processing.Webhooks;
using CryptoChief.Processing.Webhooks.Events;
using FluentAssertions;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class WebhookVerifierTests
{
    private const long T = 1789430400;
    private const string Key = "test_api_key_123";
    private const string Delivery = "7c9e6679-7425-40de-944b-e07fc1f90ae7";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"event\":\"payout.paid\",\"uuid\":\"p-1\"}");

    public enum HeaderSource { Func, Enumerable, StringValues, Pairs, HttpHeaders }

    public static TheoryData<string> VectorNames()
    {
        var data = new TheoryData<string>();
        foreach (var v in WebhookV1Vectors.List) data.Add(v.Name);
        return data;
    }

    public static TheoryData<string, HeaderSource> VectorsBySource()
    {
        var data = new TheoryData<string, HeaderSource>();
        foreach (var v in WebhookV1Vectors.List)
            foreach (var source in Enum.GetValues<HeaderSource>())
                data.Add(v.Name, source);
        return data;
    }

    [Fact]
    public void Vector_file_is_the_reference_copy()
    {
        WebhookV1Vectors.ComputeFileSha256().Should().Be(WebhookV1Vectors.FileSha256);
        File.ReadAllBytes(WebhookV1Vectors.FilePath).Should().NotContain((byte)'\r');
        WebhookV1Vectors.List.Should().HaveCount(54);
        WebhookV1Vectors.List.GroupBy(v => v.Expect).ToDictionary(g => g.Key, g => g.Count())
            .Should().BeEquivalentTo(new Dictionary<string, int>
            {
                ["ok"] = 22, ["bad_headers"] = 26, ["bad_signature"] = 4, ["timestamp_out_of_range"] = 2,
            });
    }

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void Vector_string_to_sign_and_signature(string name)
    {
        var v = WebhookV1Vectors.Get(name);

        RequestSigner.BodySha256(v.BodyBytes).Should().Be(v.BodySha256);
        RequestSigner.WebhookV1StringToSign(v.Timestamp, v.DeliveryId, v.BodyBytes).Should().Be(v.StringToSign);
        RequestSigner.SignWebhookV1(v.ApiKey, v.Timestamp, v.DeliveryId, v.BodyBytes).Should().Be(v.Signature);
    }

    [Theory]
    [MemberData(nameof(VectorsBySource))]
    public void Vector_verify(string name, HeaderSource source)
    {
        var v = WebhookV1Vectors.Get(name);
        var headers = v.ReceivedHeaders();
        var body = v.BodyBytes;

        var verify = () => Verify(source, v.ApiKey, body, headers, v.Options);
        var tryVerify = TryVerify(source, v.ApiKey, body, headers, v.Options);

        if (v.ExpectedException is null)
        {
            verify.Should().NotThrow();
            tryVerify.Should().BeTrue();
        }
        else
        {
            verify.Should().Throw<WebhookVerificationException>()
                .Which.Should().BeOfType(v.ExpectedException);
            tryVerify.Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("payin_invoice_paid_with_nulls")]
    [InlineData("payout_paid")]
    [InlineData("static_deposit_paid")]
    [InlineData("sweep_confirmed")]
    [InlineData("transaction_failed")]
    public void Vector_event_bodies_decode(string name)
    {
        var v = WebhookV1Vectors.Get(name);
        var headers = ToEnumerable(v.ReceivedHeaders());

        object evt = name switch
        {
            "payin_invoice_paid_with_nulls" => WebhookVerifier.VerifyAndDecode<PayInWebhookEvent>(v.ApiKey, v.BodyBytes, headers, v.Options),
            "payout_paid" => WebhookVerifier.VerifyAndDecode<PayoutWebhookEvent>(v.ApiKey, v.BodyBytes, headers, v.Options),
            "static_deposit_paid" => WebhookVerifier.VerifyAndDecode<StaticDepositWebhookEvent>(v.ApiKey, v.BodyBytes, headers, v.Options),
            "sweep_confirmed" => WebhookVerifier.VerifyAndDecode<SweepWebhookEvent>(v.ApiKey, v.BodyBytes, headers, v.Options),
            _ => WebhookVerifier.VerifyAndDecode<TransactionWebhookEvent>(v.ApiKey, v.BodyBytes, headers, v.Options),
        };

        var expectedEvent = System.Text.Json.JsonDocument.Parse(v.BodyBytes).RootElement.GetProperty("event").GetString();
        evt.GetType().GetProperty("Event")!.GetValue(evt).Should().Be(expectedEvent);
    }

    [Fact]
    public void Refused_webhook_is_not_decoded()
    {
        var v = WebhookV1Vectors.Get("signature_from_other_body");
        FluentActions.Invoking(() => WebhookVerifier.VerifyAndDecode<PayoutWebhookEvent>(
                v.ApiKey, v.BodyBytes, ToEnumerable(v.ReceivedHeaders()), v.Options))
            .Should().Throw<WebhookSignatureException>();
    }

    [Fact]
    public void Json_null_body_is_not_an_event()
    {
        var body = "null"u8.ToArray();
        FluentActions.Invoking(() => WebhookVerifier.VerifyAndDecode<PayoutWebhookEvent>(
                Key, body, Signed(body, T), At(T)))
            .Should().Throw<CryptoChiefException>().WithMessage("*decoded as null*");
    }

    // A receiver that catches only WebhookVerificationException around VerifyAndDecode answers 5xx
    // on a verified body it cannot decode, and the platform retries 5xx.
    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"event\":\"payout.paid\",\"confirmations\":{\"a\":1}}")]
    [InlineData("null")]
    public void Undecodable_body_past_verification_does_not_throw_a_verification_exception(string text)
    {
        var body = Encoding.UTF8.GetBytes(text);

        var thrown = Record.Exception(() => WebhookVerifier.VerifyAndDecode<PayoutWebhookEvent>(
            Key, body, Signed(body, T), At(T)));

        thrown.Should().NotBeNull();
        thrown.Should().NotBeAssignableTo<WebhookVerificationException>();
        thrown.Should().Match<Exception>(e => e is JsonException || e is CryptoChiefException);
        WebhookVerifier.TryVerify(Key, body, Signed(body, T), At(T)).Should().BeTrue();
    }

    [Fact]
    public void Header_names_are_case_insensitive()
    {
        var sig = RequestSigner.SignWebhookV1(Key, T, Delivery, Body);
        var lower = new Dictionary<string, string>
        {
            ["x-cc-timestamp"] = T.ToString(),
            ["x-webhook-delivery"] = Delivery,
            ["x-cc-signature"] = sig,
        };

        WebhookVerifier.TryVerify(Key, Body, lower, At(T)).Should().BeTrue();
        WebhookVerifier.TryVerify(Key, Body,
            lower.Select(p => new KeyValuePair<string, IEnumerable<string>>(p.Key, new[] { p.Value })), At(T)).Should().BeTrue();
        WebhookVerifier.TryVerify(Key, Body,
            lower.ToDictionary(p => p.Key, p => new StringValues(p.Value)), At(T)).Should().BeTrue();

        using var req = new HttpRequestMessage();
        foreach (var (name, value) in lower) req.Headers.TryAddWithoutValidation(name, value);
        WebhookVerifier.TryVerify(Key, Body, req.Headers, At(T)).Should().BeTrue();
    }

    [Fact]
    public void One_header_under_two_spellings_is_repeated()
    {
        var sig = RequestSigner.SignWebhookV1(Key, T, Delivery, Body);
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("X-CC-Timestamp", T.ToString()),
            new("X-Webhook-Delivery", Delivery),
            new("X-CC-Signature", sig),
            new("x-cc-signature", sig),
        };

        FluentActions.Invoking(() => WebhookVerifier.Verify(Key, Body, pairs, At(T)))
            .Should().Throw<WebhookHeadersException>();
        FluentActions.Invoking(() => WebhookVerifier.Verify(Key, Body,
                pairs.Select(p => new KeyValuePair<string, IEnumerable<string>>(p.Key, new[] { p.Value })), At(T)))
            .Should().Throw<WebhookHeadersException>();
    }

    // A key of nothing, or of spaces and tabs only, is no key: there is nothing to verify with,
    // and the signer refuses to sign with one.
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("  \t \t ")]
    public void Blank_api_key_is_rejected_before_headers(string apiKey)
    {
        var none = new Dictionary<string, string>();
        var signed = Headers(T.ToString(), Delivery, RequestSigner.SignWebhookV1(Key, T, Delivery, Body));

        FluentActions.Invoking(() => WebhookVerifier.Verify(apiKey, Body, none, At(T)))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => WebhookVerifier.Verify(apiKey, Body, signed, At(T)))
            .Should().Throw<ArgumentException>();
        WebhookVerifier.TryVerify(apiKey, Body, none, At(T)).Should().BeFalse();
        WebhookVerifier.TryVerify(apiKey, Body, signed, At(T)).Should().BeFalse();
        FluentActions.Invoking(() => RequestSigner.SignWebhookV1(apiKey, T, Delivery, Body))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Null_api_key_is_rejected_before_headers()
    {
        FluentActions.Invoking(() => WebhookVerifier.Verify(null!, Body, _ => null, At(T)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_key_of_other_whitespace_is_a_key()
    {
        var sig = RequestSigner.SignWebhookV1("\n", T, Delivery, Body);
        WebhookVerifier.TryVerify("\n", Body, Headers(T.ToString(), Delivery, sig), At(T)).Should().BeTrue();
    }

    // A leading zero is a rewrite of the header that leaves the signature over the canonical
    // number matching, so it has to be refused by format.
    [Theory]
    [InlineData("01789430400")]
    [InlineData("0001789430400")]
    [InlineData("00")]
    public void Timestamp_with_a_leading_zero_is_a_header_error(string timestamp)
    {
        var sig = RequestSigner.SignWebhookV1(Key, T, Delivery, Body);
        FluentActions.Invoking(() => WebhookVerifier.Verify(Key, Body, Headers(timestamp, Delivery, sig), At(T)))
            .Should().Throw<WebhookHeadersException>();
        WebhookVerifier.TryVerify(Key, Body, Headers(timestamp, Delivery, sig), At(T)).Should().BeFalse();
    }

    [Theory]
    [InlineData("9223372036854775808")]
    [InlineData("99999999999999999999")]
    [InlineData("١٧٨٩٤٣٠٤٠٠")]
    [InlineData("1789430400 1")]
    public void Timestamp_not_an_int64_decimal_is_a_header_error(string timestamp)
    {
        var sig = RequestSigner.SignWebhookV1(Key, T, Delivery, Body);
        FluentActions.Invoking(() => WebhookVerifier.Verify(Key, Body, Headers(timestamp, Delivery, sig), At(T)))
            .Should().Throw<WebhookHeadersException>();
    }

    [Fact]
    public void Timestamp_zero()
    {
        var sig = RequestSigner.SignWebhookV1(Key, T, Delivery, Body);
        FluentActions.Invoking(() => WebhookVerifier.Verify(Key, Body, Headers("0", Delivery, sig), At(T)))
            .Should().Throw<WebhookTimestampException>();
        FluentActions.Invoking(() => WebhookVerifier.Verify(Key, Body, Headers("0", Delivery, sig), At(100)))
            .Should().Throw<WebhookHeadersException>();
    }

    [Fact]
    public void Tolerance_is_configurable_in_whole_seconds()
    {
        var sig = RequestSigner.SignWebhookV1(Key, T, Delivery, Body);
        var headers = Headers(T.ToString(), Delivery, sig);

        WebhookVerifier.TryVerify(Key, Body, headers, At(T + 10, TimeSpan.FromSeconds(10))).Should().BeTrue();
        FluentActions.Invoking(() => WebhookVerifier.Verify(Key, Body, headers, At(T - 11, TimeSpan.FromSeconds(10))))
            .Should().Throw<WebhookTimestampException>();
        FluentActions.Invoking(() => WebhookVerifier.Verify(Key, Body, headers, At(T + 11, TimeSpan.FromSeconds(10.9))))
            .Should().Throw<WebhookTimestampException>();
        WebhookVerifier.TryVerify(Key, Body, headers, At(T + 3600, TimeSpan.FromHours(1))).Should().BeTrue();
    }

    [Fact]
    public void Zero_or_negative_tolerance_is_the_default()
    {
        var sig = RequestSigner.SignWebhookV1(Key, T, Delivery, Body);
        var headers = Headers(T.ToString(), Delivery, sig);

        foreach (var tolerance in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-5) })
        {
            WebhookVerifier.TryVerify(Key, Body, headers, At(T + 300, tolerance)).Should().BeTrue();
            FluentActions.Invoking(() => WebhookVerifier.Verify(Key, Body, headers, At(T + 301, tolerance)))
                .Should().Throw<WebhookTimestampException>();
        }
        WebhookVerifier.DefaultTolerance.Should().Be(TimeSpan.FromSeconds(300));
        new WebhookVerifyOptions().Tolerance.Should().Be(TimeSpan.FromSeconds(300));
    }

    [Fact]
    public void Default_clock_is_the_current_time()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = RequestSigner.SignWebhookV1(Key, now, Delivery, Body);

        WebhookVerifier.TryVerify(Key, Body, Headers(now.ToString(), Delivery, sig)).Should().BeTrue();
        WebhookVerifier.TryVerify(Key, Body, Headers(now.ToString(), Delivery, sig), new WebhookVerifyOptions()).Should().BeTrue();

        var stale = RequestSigner.SignWebhookV1(Key, now - 3600, Delivery, Body);
        FluentActions.Invoking(() => WebhookVerifier.Verify(Key, Body, Headers((now - 3600).ToString(), Delivery, stale)))
            .Should().Throw<WebhookTimestampException>();
    }

    [Fact]
    public void Checks_run_headers_then_timestamp_then_signature()
    {
        var wrong = RequestSigner.SignWebhookV1("other", T, Delivery, Body);

        FluentActions.Invoking(() => WebhookVerifier.Verify(Key, Body, Headers((T + 1000).ToString(), "bad.id", wrong), At(T)))
            .Should().Throw<WebhookHeadersException>();
        FluentActions.Invoking(() => WebhookVerifier.Verify(Key, Body, Headers((T + 1000).ToString(), Delivery, wrong), At(T)))
            .Should().Throw<WebhookTimestampException>();
        FluentActions.Invoking(() => WebhookVerifier.Verify(Key, Body, Headers(T.ToString(), Delivery, wrong), At(T)))
            .Should().Throw<WebhookSignatureException>();
    }

    [Fact]
    public void Func_source_sees_a_repeated_header_comma_joined()
    {
        var sig = RequestSigner.SignWebhookV1(Key, T, Delivery, Body);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-CC-Timestamp"] = T.ToString(),
            ["X-Webhook-Delivery"] = Delivery,
            ["X-CC-Signature"] = sig + "," + sig,
        };

        FluentActions.Invoking(() => WebhookVerifier.Verify(Key, Body, n => values.GetValueOrDefault(n), At(T)))
            .Should().Throw<WebhookHeadersException>();
    }

    public static TheoryData<string, bool> EmptyRepeats()
    {
        var data = new TheoryData<string, bool>();
        foreach (var name in new[] { WebhookVerifier.TimestampHeader, WebhookVerifier.DeliveryHeader, WebhookVerifier.SignatureHeader })
        {
            data.Add(name, true);
            data.Add(name, false);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(EmptyRepeats))]
    public void Empty_copy_of_a_header_is_a_repeat(string name, bool emptyFirst)
    {
        var sig = RequestSigner.SignWebhookV1(Key, T, Delivery, Body);
        var headers = Headers(T.ToString(), Delivery, sig);
        var value = headers[name].Single();
        headers[name] = emptyFirst ? new[] { "", value } : new[] { value, "" };
        var list = headers.Select(h => new KeyValuePair<string, List<string>>(h.Key, h.Value.ToList())).ToList();

        foreach (var source in new[] { HeaderSource.Enumerable, HeaderSource.StringValues, HeaderSource.Pairs, HeaderSource.HttpHeaders })
        {
            FluentActions.Invoking(() => Verify(source, Key, Body, list, At(T)))
                .Should().Throw<WebhookHeadersException>(source.ToString());
            TryVerify(source, Key, Body, list, At(T)).Should().BeFalse(source.ToString());
        }
    }

    [Fact]
    public void Func_source_over_string_values_does_not_see_an_empty_copy()
    {
        var sig = RequestSigner.SignWebhookV1(Key, T, Delivery, Body);
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)
        {
            [WebhookVerifier.TimestampHeader] = T.ToString(),
            [WebhookVerifier.DeliveryHeader] = Delivery,
            [WebhookVerifier.SignatureHeader] = new StringValues(new[] { "", sig }),
        };

        FluentActions.Invoking(() => WebhookVerifier.Verify(Key, Body, headers, At(T)))
            .Should().Throw<WebhookHeadersException>();
        WebhookVerifier.TryVerify(Key, Body, n => headers.TryGetValue(n, out var v) ? (string?)v : null, At(T))
            .Should().BeTrue("StringValues converted to a string drops empty values");
    }

    [Fact]
    public void Legacy_signature_headers_are_not_read()
    {
        var headers = new Dictionary<string, string>
        {
            ["Signature"] = "0123456789abcdef0123456789abcdef",
            ["X-Webhook-Signature"] = "0123456789abcdef0123456789abcdef",
            ["X-Webhook-Delivery"] = Delivery,
            ["X-CC-Timestamp"] = T.ToString(),
        };

        FluentActions.Invoking(() => WebhookVerifier.Verify(Key, Body, headers, At(T)))
            .Should().Throw<WebhookHeadersException>();
    }

    [Fact]
    public void Sign_rejects_what_the_receiver_cannot_verify()
    {
        FluentActions.Invoking(() => RequestSigner.SignWebhookV1("", T, Delivery, Body))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => RequestSigner.SignWebhookV1(Key, 0, Delivery, Body))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => RequestSigner.WebhookV1StringToSign(-1, Delivery, Body))
            .Should().Throw<ArgumentOutOfRangeException>();

        foreach (var id in new[] { "", "dlv.1", "dlv\r1", "dlv\n", new string('a', 129), "идентификатор" })
        {
            FluentActions.Invoking(() => RequestSigner.WebhookV1StringToSign(T, id, Body))
                .Should().Throw<ArgumentException>();
            FluentActions.Invoking(() => RequestSigner.SignWebhookV1(Key, T, id, Body))
                .Should().Throw<ArgumentException>();
        }
        RequestSigner.WebhookV1StringToSign(T, new string('a', 128), Body).Should().StartWith("CC-HMAC-SHA256-WEBHOOK-V1\n");
    }

    [Fact]
    public void Public_surface()
    {
        WebhookVerifier.DeliveryHeader.Should().Be("X-Webhook-Delivery");
        WebhookVerifier.TimestampHeader.Should().Be("X-CC-Timestamp");
        WebhookVerifier.SignatureHeader.Should().Be("X-CC-Signature");
        RequestSigner.WebhookV1Scope.Should().Be("CC-HMAC-SHA256-WEBHOOK-V1");

        typeof(WebhookHeadersException).BaseType.Should().Be(typeof(WebhookVerificationException));
        typeof(WebhookTimestampException).BaseType.Should().Be(typeof(WebhookVerificationException));
        typeof(WebhookSignatureException).BaseType.Should().Be(typeof(WebhookVerificationException));
        typeof(WebhookVerificationException).BaseType.Should().Be(typeof(CryptoChiefException));

        var verifier = typeof(WebhookVerifier).GetMembers(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name).ToHashSet();
        verifier.Should().NotContain(new[] { "GetSignature", "VerifyParsed", "TryVerifyParsed", "WebhookSignatureHeader" });
        foreach (var m in typeof(WebhookVerifier).GetMethods(BindingFlags.Public | BindingFlags.Static)
                     .Where(m => m.Name is "Verify" or "TryVerify" or "VerifyAndDecode"))
        {
            m.GetParameters().Select(p => p.Name).Should().Equal("apiKey", "rawBody", m.GetParameters()[2].Name, "options");
            m.GetParameters()[1].ParameterType.Should().Be(typeof(ReadOnlySpan<byte>));
            m.GetParameters()[2].ParameterType.Should().NotBe(typeof(string));
        }

        typeof(RequestSigner).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name)
            .Should().NotContain(new[] { "Sign", "SignJson", "Canonicalize" });
        typeof(CryptoChiefClientOptions).GetProperty("HmacOnly").Should().BeNull();
        typeof(CryptoChiefClient).Assembly.GetType("CryptoChief.Processing.Internal.CanonicalJson").Should().BeNull();
    }

    private static WebhookVerifyOptions At(long now, TimeSpan? tolerance = null) => new()
    {
        Now = () => DateTimeOffset.FromUnixTimeSeconds(now),
        Tolerance = tolerance ?? TimeSpan.FromSeconds(300),
    };

    private static Dictionary<string, IEnumerable<string>> Signed(byte[] body, long timestamp) =>
        Headers(timestamp.ToString(), Delivery, RequestSigner.SignWebhookV1(Key, timestamp, Delivery, body));

    private static Dictionary<string, IEnumerable<string>> Headers(string timestamp, string delivery, string signature) => new()
    {
        [WebhookVerifier.TimestampHeader] = new[] { timestamp },
        [WebhookVerifier.DeliveryHeader] = new[] { delivery },
        [WebhookVerifier.SignatureHeader] = new[] { signature },
    };

    private static List<KeyValuePair<string, IEnumerable<string>>> ToEnumerable(
        IReadOnlyList<KeyValuePair<string, List<string>>> headers) =>
        headers.Select(h => new KeyValuePair<string, IEnumerable<string>>(h.Key, h.Value)).ToList();

    private static void Verify(HeaderSource source, string apiKey, byte[] body,
        IReadOnlyList<KeyValuePair<string, List<string>>> headers, WebhookVerifyOptions options)
    {
        switch (source)
        {
            case HeaderSource.Func:
                var joined = headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
                WebhookVerifier.Verify(apiKey, body, n => joined.TryGetValue(n, out var s) ? s : null, options);
                break;
            case HeaderSource.Enumerable:
                WebhookVerifier.Verify(apiKey, body, ToEnumerable(headers), options);
                break;
            case HeaderSource.StringValues:
                WebhookVerifier.Verify(apiKey, body,
                    headers.ToDictionary(h => h.Key, h => new StringValues(h.Value.ToArray())), options);
                break;
            case HeaderSource.Pairs:
                WebhookVerifier.Verify(apiKey, body,
                    headers.SelectMany(h => h.Value.Select(v => new KeyValuePair<string, string>(h.Key, v))).ToList(), options);
                break;
            case HeaderSource.HttpHeaders:
                using (var req = new HttpRequestMessage())
                {
                    foreach (var h in headers)
                        foreach (var value in h.Value)
                            req.Headers.TryAddWithoutValidation(h.Key, value).Should().BeTrue();
                    WebhookVerifier.Verify(apiKey, body, req.Headers, options);
                }
                break;
        }
    }

    private static bool TryVerify(HeaderSource source, string apiKey, byte[] body,
        IReadOnlyList<KeyValuePair<string, List<string>>> headers, WebhookVerifyOptions options)
    {
        switch (source)
        {
            case HeaderSource.Func:
                var joined = headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
                return WebhookVerifier.TryVerify(apiKey, body, n => joined.TryGetValue(n, out var s) ? s : null, options);
            case HeaderSource.Enumerable:
                return WebhookVerifier.TryVerify(apiKey, body, ToEnumerable(headers), options);
            case HeaderSource.StringValues:
                return WebhookVerifier.TryVerify(apiKey, body,
                    headers.ToDictionary(h => h.Key, h => new StringValues(h.Value.ToArray())), options);
            case HeaderSource.Pairs:
                return WebhookVerifier.TryVerify(apiKey, body,
                    headers.SelectMany(h => h.Value.Select(v => new KeyValuePair<string, string>(h.Key, v))).ToList(), options);
            default:
                using (var req = new HttpRequestMessage())
                {
                    foreach (var h in headers)
                        foreach (var value in h.Value)
                            req.Headers.TryAddWithoutValidation(h.Key, value);
                    return WebhookVerifier.TryVerify(apiKey, body, req.Headers, options);
                }
        }
    }
}
