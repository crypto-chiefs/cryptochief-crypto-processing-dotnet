using System.Net;
using System.Text;
using CryptoChief.Processing.Chains;
using CryptoChief.Processing.Http;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class BlockchainTests
{
    [Fact]
    public async Task BlockchainsList_decodes_a_bare_top_level_array()
    {
        // The scanner answers with an array, not an {"items":[...]} envelope. A decoder
        // written for the envelope compiles either way and only fails here, on the wire.
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, """
            [
              {"name":"ETH_MAINNET","type":"evm"},
              {"name":"TRON_MAINNET","type":"tron"},
              {"name":"SOLANA_MAINNET","type":"solana"}
            ]
            """));
        var client = NewClient(handler);

        var chains = await client.Blockchain.BlockchainsListAsync();

        var req = handler.Captured.Should().ContainSingle().Subject;
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.AbsolutePath.Should().Be("/v1/blockchains/list");
        req.Headers.GetValues("Merchant").Should().ContainSingle().Which.Should().Be("M-1");

        // Nothing to filter by, but the empty object is still signed like every request.
        handler.CapturedBodies.Should().ContainSingle().Which.Should().Be("{}");
        req.Headers.GetValues("Signature").Single().Should()
            .Be(RequestSigner.Sign(Encoding.UTF8.GetBytes("{}"), "K-1"));

        chains.Should().HaveCount(3);
        chains[0].Name.Should().Be(Chain.EthMainnet);

        // The scanner's protocol family is lower-case and is not the upper-case
        // ChainFamily that assets and wallets are labelled with.
        chains[0].Type.Should().Be("evm");
        chains[1].Name.Should().Be(Chain.TronMainnet);
        chains[1].Type.Should().Be("tron");
        chains[2].Type.Should().Be("solana");
    }

    [Fact]
    public async Task BlockchainsList_decodes_an_empty_array()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, "[]"));

        var chains = await NewClient(handler).Blockchain.BlockchainsListAsync();

        chains.Should().BeEmpty();
    }

    [Fact]
    public async Task BlockchainsList_reads_a_null_body_as_an_empty_list()
    {
        // The service builds its answer with `var list []T`, so "no chains" marshals as
        // JSON null, not []. A method promising a list has to hand back an empty one —
        // not null for the caller's foreach to trip over, and not a decode error.
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, "null"));

        var chains = await NewClient(handler).Blockchain.BlockchainsListAsync();

        chains.Should().NotBeNull();
        chains.Should().BeEmpty();
    }

    [Fact]
    public async Task WalletBalance_and_TransactionStatus_read_a_null_body_as_an_empty_list()
    {
        // Same bare-array shape, same null, same promise.
        var balances = await NewClient(new CapturingHandler(_ => Resp(HttpStatusCode.OK, "null")))
            .Blockchain.WalletBalanceAsync(Chain.EthMainnet, new[] { "0x77EDde" });
        balances.Should().NotBeNull();
        balances.Should().BeEmpty();

        var status = await NewClient(new CapturingHandler(_ => Resp(HttpStatusCode.OK, "null")))
            .Blockchain.TransactionStatusAsync(Chain.EthMainnet, "0xdead");
        status.Should().NotBeNull();
        status.Should().BeEmpty();
    }

    [Fact]
    public async Task ContractsList_keeps_chain_family_is_test_and_a_native_empty_contract()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, """
            {"items":[
              {"network":"ETH_MAINNET","coin":"ETH","contract":"","chain_family":"EVM",
               "type":"native","is_test":false,"decimals":18},
              {"network":"TRON_MAINNET","coin":"USDT",
               "contract":"TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t","chain_family":"TRON",
               "type":"token","is_test":false,"decimals":6},
              {"network":"SOLANA_DEVNET","coin":"SOL","contract":"","chain_family":"SOLANA",
               "type":"native","is_test":true,"decimals":9}
            ]}
            """));
        var client = NewClient(handler);

        var catalogue = await client.Blockchain.ContractsListAsync();

        var req = handler.Captured.Should().ContainSingle().Subject;
        req.RequestUri!.AbsolutePath.Should().Be("/v1/blockchain/contracts/list");

        // Platform-wide: nothing to filter by project, and the empty object is signed.
        handler.CapturedBodies.Should().ContainSingle().Which.Should().Be("{}");
        req.Headers.GetValues("Signature").Single().Should()
            .Be(RequestSigner.Sign(Encoding.UTF8.GetBytes("{}"), "K-1"));

        catalogue.Items.Should().HaveCount(3);

        var eth = catalogue.Items[0];
        // "" is the answer for a native coin — not null, and not a decode failure.
        eth.Contract.Should().NotBeNull();
        eth.Contract.Should().BeEmpty();
        eth.Type.Should().Be("native");
        eth.ChainFamily.Should().Be(ChainFamily.Evm);
        eth.IsTest.Should().BeFalse();
        eth.Decimals.Should().Be(18);

        var usdt = catalogue.Items[1];
        usdt.Contract.Should().Be("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t");
        usdt.ChainFamily.Should().Be(ChainFamily.Tron);
        usdt.IsTest.Should().BeFalse();

        // Mainnet and testnet assets arrive in one list, so is_test is the only thing
        // telling them apart.
        var sol = catalogue.Items[2];
        sol.ChainFamily.Should().Be(ChainFamily.Solana);
        sol.IsTest.Should().BeTrue();
        sol.Contract.Should().BeEmpty();
    }

    [Fact]
    public async Task ContractsAvailable_carries_the_same_two_fields()
    {
        // Same item shape on both endpoints: the project's own list has to decode them too.
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, """
            {"items":[
              {"network":"ARBITRUM_SEPOLIA","coin":"ETH","contract":"","chain_family":"EVM",
               "type":"native","is_test":true,"decimals":18,
               "network_icon":"https://cdn.test/arbitrum.svg","coin_icon":"https://cdn.test/eth.svg"}
            ]}
            """));

        var available = await NewClient(handler).Blockchain.ContractsAvailableAsync(Chain.ArbitrumSepolia);

        var asset = available.Items.Should().ContainSingle().Subject;
        asset.ChainFamily.Should().Be(ChainFamily.Evm);
        asset.IsTest.Should().BeTrue();
        asset.Contract.Should().BeEmpty();
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
