using System.Net;
using System.Text;
using CryptoChief.Processing.Http;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class CurrenciesTests
{
    [Fact]
    public async Task Fiats_decodes_a_bare_top_level_array()
    {
        // The fiat list answers with an array, not an {"items":[...]} envelope.
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, """
            [
              {"code":"JMD","name":"Jamaican Dollar"},
              {"code":"KYD","name":"Cayman Islands Dollar"},
              {"code":"SEK","name":"Swedish Krona"}
            ]
            """));
        var client = NewClient(handler);

        var fiats = await client.Currencies.FiatsAsync();

        var req = handler.Captured.Should().ContainSingle().Subject;
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.AbsolutePath.Should().Be("/v1/currencies/fiats");
        req.Headers.GetValues("Merchant").Should().ContainSingle().Which.Should().Be("M-1");

        // Empty body, still signed.
        handler.CapturedBodies.Should().ContainSingle().Which.Should().Be("{}");
        req.Headers.GetValues("Signature").Single().Should()
            .Be(RequestSigner.Sign(Encoding.UTF8.GetBytes("{}"), "K-1"));

        fiats.Should().HaveCount(3);
        fiats[0].Code.Should().Be("JMD");
        fiats[0].Name.Should().Be("Jamaican Dollar");
        fiats[2].Code.Should().Be("SEK");
        fiats[2].Name.Should().Be("Swedish Krona");
    }

    [Fact]
    public async Task Cryptos_decodes_the_tickers_and_the_per_exchange_map()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, """
            {
              "by_exchange": {
                "binance": ["0G","1000CAT","1000SATS"],
                "bybit": ["0G","1INCH","AAVE"],
                "exmo": ["AAVE","ADA"]
              },
              "count": 5,
              "quote": "USDT",
              "tickers": ["0G","1000CAT","1000SATS","1INCH","AAVE"]
            }
            """));
        var client = NewClient(handler);

        var cryptos = await client.Currencies.CryptosAsync();

        var req = handler.Captured.Should().ContainSingle().Subject;
        req.RequestUri!.AbsolutePath.Should().Be("/v1/currencies/cryptos");
        handler.CapturedBodies.Should().ContainSingle().Which.Should().Be("{}");
        req.Headers.GetValues("Signature").Single().Should()
            .Be(RequestSigner.Sign(Encoding.UTF8.GetBytes("{}"), "K-1"));

        cryptos.Quote.Should().Be("USDT");
        cryptos.Count.Should().Be(5);
        cryptos.Tickers.Should().Equal("0G", "1000CAT", "1000SATS", "1INCH", "AAVE");

        // Exchange names are data, not property names: they reach the caller verbatim,
        // untouched by the snake_case policy that renames the fields around them.
        cryptos.ByExchange.Should().HaveCount(3);
        cryptos.ByExchange.Keys.Should().BeEquivalentTo("binance", "bybit", "exmo");
        cryptos.ByExchange["binance"].Should().Equal("0G", "1000CAT", "1000SATS");
        cryptos.ByExchange["exmo"].Should().Equal("AAVE", "ADA");
    }

    [Fact]
    public async Task Cryptos_survives_an_empty_exchange_map()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"by_exchange\":{},\"count\":0,\"quote\":\"USDT\",\"tickers\":[]}"));

        var cryptos = await NewClient(handler).Currencies.CryptosAsync();

        cryptos.ByExchange.Should().BeEmpty();
        cryptos.Tickers.Should().BeEmpty();
        cryptos.Count.Should().Be(0);
        cryptos.Quote.Should().Be("USDT");
    }

    [Fact]
    public async Task Fiats_reads_a_null_body_as_an_empty_list()
    {
        // The service builds its answer with `var list []T`, so "no currencies" marshals
        // as JSON null, not []. A method promising a list hands back an empty one.
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, "null"));

        var fiats = await NewClient(handler).Currencies.FiatsAsync();

        fiats.Should().NotBeNull();
        fiats.Should().BeEmpty();
    }

    [Fact]
    public async Task Cryptos_reads_a_null_body_as_empty_collections()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, "null"));

        var cryptos = await NewClient(handler).Currencies.CryptosAsync();

        cryptos.Should().NotBeNull();
        cryptos.Tickers.Should().NotBeNull();
        cryptos.Tickers.Should().BeEmpty();
        cryptos.ByExchange.Should().NotBeNull();
        cryptos.ByExchange.Should().BeEmpty();
        cryptos.Count.Should().Be(0);
        cryptos.Quote.Should().BeEmpty();
    }

    [Fact]
    public async Task Cryptos_reads_null_collections_inside_the_body_as_empty_ones()
    {
        // The envelope survives while the lists inside it go null — the same
        // `var list []T` on the fields rather than on the whole answer.
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"by_exchange\":null,\"count\":0,\"quote\":\"USDT\",\"tickers\":null}"));

        var cryptos = await NewClient(handler).Currencies.CryptosAsync();

        cryptos.Tickers.Should().NotBeNull();
        cryptos.Tickers.Should().BeEmpty();
        cryptos.ByExchange.Should().NotBeNull();
        cryptos.ByExchange.Should().BeEmpty();
        cryptos.Quote.Should().Be("USDT");
    }

    [Fact]
    public async Task Cryptos_reads_a_null_ticker_list_nested_in_the_exchange_map()
    {
        // One exchange the platform currently carries nothing from: the key is present,
        // its list is null. Enumerating it must not throw, and the other exchanges must
        // survive the repair intact.
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, """
            {
              "by_exchange": {"binance": null, "exmo": ["AAVE","ADA"]},
              "count": 2,
              "quote": "USDT",
              "tickers": ["AAVE","ADA"]
            }
            """));

        var cryptos = await NewClient(handler).Currencies.CryptosAsync();

        cryptos.ByExchange.Keys.Should().BeEquivalentTo("binance", "exmo");
        cryptos.ByExchange["binance"].Should().NotBeNull();
        cryptos.ByExchange["binance"].Should().BeEmpty();
        cryptos.ByExchange["exmo"].Should().Equal("AAVE", "ADA");
        cryptos.Tickers.Should().Equal("AAVE", "ADA");
    }

    private static HttpResponseMessage Resp(HttpStatusCode code, string body) =>
        new(code) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
            { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") } } };

    private static CryptoChiefClient NewClient(HttpMessageHandler handler) =>
        new(new CryptoChiefClientOptions
        {
            MerchantId        = "M-1",
            ApiKey            = "K-1",
            BaseUrl           = "https://test/",
            MaxRetries        = 0,
            InitialRetryDelay = TimeSpan.FromMilliseconds(1),
            MaxRetryDelay     = TimeSpan.FromMilliseconds(5),
        }, new HttpClient(handler), null);

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> reply)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Captured { get; } = new();

        /// <summary>Request bodies read at send time — the transport disposes the request after sending.</summary>
        public List<string> CapturedBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured.Add(request);
            CapturedBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return reply(request);
        }
    }
}
