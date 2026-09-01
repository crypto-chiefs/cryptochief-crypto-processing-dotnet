using System.Net;
using System.Text;
using CryptoChief.Processing.Chains;
using CryptoChief.Processing.Http;
using CryptoChief.Processing.Models;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class WalletsTests
{
    private const string StaticWalletBody =
        "{\"type\":\"static\",\"address\":\"0xdead\",\"chain_family\":\"EVM\",\"frozen\":false,"
        + "\"master_wallet_address\":\"0xbeef\",\"callback_url\":\"https://shop.test/hooks/deposit\","
        + "\"label\":\"Shop EU\"}";

    [Fact]
    public async Task Generate_sends_the_label_and_omits_it_when_unset()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"type\":\"master\",\"address\":\"0xbeef\",\"chain_family\":\"EVM\",\"frozen\":false,"
            + "\"master_wallet_address\":null,\"callback_url\":null,\"label\":\"Treasury EU\"}"));
        var client = NewClient(handler);

        var created = await client.Wallets.GenerateAsync(new GenerateWalletRequest
        {
            WalletType  = WalletType.Master,
            ChainFamily = ChainFamily.Evm,
            Label       = "Treasury EU",
        });

        // Generation answers with the name it stored, so a bulk create no longer hands back
        // a list of items indistinguishable from each other.
        created.Label.Should().Be("Treasury EU");

        var req = handler.Captured.Should().ContainSingle().Subject;
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.AbsolutePath.Should().Be("/v1/wallets/generate");

        // Canonical body: snake_case keys sorted lexicographically. The label rides on
        // every wallet type, not just static ones.
        const string wire = "{\"chain_family\":\"EVM\",\"label\":\"Treasury EU\","
            + "\"wallet_type\":\"master\"}";
        handler.CapturedBodies.Should().ContainSingle().Which.Should().Be(wire);
        req.Headers.GetValues("Signature").Single().Should()
            .Be(RequestSigner.Sign(Encoding.UTF8.GetBytes(wire), "K-1"));

        var handler2 = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"type\":\"master\",\"address\":\"0xbeef\",\"chain_family\":\"EVM\",\"frozen\":false,"
            + "\"master_wallet_address\":null,\"callback_url\":null,\"label\":null}"));
        var unnamed = await NewClient(handler2).Wallets.GenerateAsync(new GenerateWalletRequest
        {
            WalletType  = WalletType.Master,
            ChainFamily = ChainFamily.Evm,
        });

        unnamed.Label.Should().BeNull();

        // Unnamed must stay off the wire: "" is a name of no characters the platform has
        // to reject, not the "no name" the caller meant.
        handler2.CapturedBodies.Should().ContainSingle().Which
            .Should().Be("{\"chain_family\":\"EVM\",\"wallet_type\":\"master\"}");
    }

    [Fact]
    public async Task RebindMaster_posts_both_addresses_and_returns_the_wallet()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, StaticWalletBody));
        var client = NewClient(handler);

        var wallet = await client.Wallets.RebindMasterAsync("0xdead", "0xbeef");

        var req = handler.Captured.Should().ContainSingle().Subject;
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.AbsolutePath.Should().Be("/v1/wallets/rebind-master");
        req.Headers.GetValues("Merchant").Should().ContainSingle().Which.Should().Be("M-1");

        const string wire = "{\"address\":\"0xdead\",\"master_wallet_address\":\"0xbeef\"}";
        handler.CapturedBodies.Should().ContainSingle().Which.Should().Be(wire);
        req.Headers.GetValues("Signature").Single().Should()
            .Be(RequestSigner.Sign(Encoding.UTF8.GetBytes(wire), "K-1"));

        wallet.Type.Should().Be(WalletType.Static);
        wallet.Address.Should().Be("0xdead");
        wallet.ChainFamily.Should().Be(ChainFamily.Evm);
        wallet.Frozen.Should().BeFalse();
        wallet.MasterWalletAddress.Should().Be("0xbeef");
        wallet.CallbackUrl.Should().Be("https://shop.test/hooks/deposit");
        wallet.Label.Should().Be("Shop EU");
    }

    [Fact]
    public async Task SetCallbackUrl_writes_the_url()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK, StaticWalletBody));
        var client = NewClient(handler);

        var wallet = await client.Wallets
            .SetCallbackUrlAsync("0xdead", "https://shop.test/hooks/deposit");

        var req = handler.Captured.Should().ContainSingle().Subject;
        req.RequestUri!.AbsolutePath.Should().Be("/v1/wallets/callback-url");

        const string wire = "{\"address\":\"0xdead\","
            + "\"callback_url\":\"https://shop.test/hooks/deposit\"}";
        handler.CapturedBodies.Should().ContainSingle().Which.Should().Be(wire);
        req.Headers.GetValues("Signature").Single().Should()
            .Be(RequestSigner.Sign(Encoding.UTF8.GetBytes(wire), "K-1"));

        wallet.CallbackUrl.Should().Be("https://shop.test/hooks/deposit");
    }

    [Fact]
    public async Task SetCallbackUrl_sends_an_empty_string_rather_than_omitting_the_field()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"type\":\"static\",\"address\":\"0xdead\",\"chain_family\":\"EVM\",\"frozen\":false,"
            + "\"master_wallet_address\":\"0xbeef\",\"callback_url\":null,\"label\":\"Shop EU\"}"));
        var client = NewClient(handler);

        var wallet = await client.Wallets.SetCallbackUrlAsync("0xdead", "");

        // "" is the instruction to clear the webhook. Omitting the key says nothing at all,
        // so the empty string has to survive the serializer's null-dropping and reach the
        // wire — and the signature is over exactly that body.
        const string wire = "{\"address\":\"0xdead\",\"callback_url\":\"\"}";
        handler.CapturedBodies.Should().ContainSingle().Which.Should().Be(wire);
        handler.Captured[0].Headers.GetValues("Signature").Single().Should()
            .Be(RequestSigner.Sign(Encoding.UTF8.GetBytes(wire), "K-1"));

        // Cleared comes back as null, never as "".
        wallet.CallbackUrl.Should().BeNull();
    }

    [Fact]
    public async Task SetLabel_writes_the_name_of_any_wallet_type()
    {
        // A master wallet: the label endpoint takes every type, unlike callback-url.
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"type\":\"master\",\"address\":\"0xbeef\",\"chain_family\":\"EVM\",\"frozen\":false,"
            + "\"master_wallet_address\":null,\"callback_url\":null,\"label\":\"Treasury EU\"}"));
        var client = NewClient(handler);

        var wallet = await client.Wallets.SetLabelAsync("0xbeef", "Treasury EU");

        var req = handler.Captured.Should().ContainSingle().Subject;
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.AbsolutePath.Should().Be("/v1/wallets/label");
        req.Headers.GetValues("Merchant").Should().ContainSingle().Which.Should().Be("M-1");

        // Exactly the two fields, and nothing else rides along.
        const string wire = "{\"address\":\"0xbeef\",\"label\":\"Treasury EU\"}";
        handler.CapturedBodies.Should().ContainSingle().Which.Should().Be(wire);
        req.Headers.GetValues("Signature").Single().Should()
            .Be(RequestSigner.Sign(Encoding.UTF8.GetBytes(wire), "K-1"));

        wallet.Type.Should().Be(WalletType.Master);
        wallet.Label.Should().Be("Treasury EU");
    }

    [Fact]
    public async Task SetLabel_sends_an_empty_string_rather_than_omitting_the_field()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"type\":\"static\",\"address\":\"0xdead\",\"chain_family\":\"EVM\",\"frozen\":false,"
            + "\"master_wallet_address\":\"0xbeef\",\"callback_url\":null,\"label\":null}"));
        var client = NewClient(handler);

        var wallet = await client.Wallets.SetLabelAsync("0xdead", "");

        // "" is the instruction to clear the name. Omitting the key says nothing at all, so
        // the empty string has to survive the serializer's null-dropping and reach the wire
        // — and the signature is over exactly that body.
        const string wire = "{\"address\":\"0xdead\",\"label\":\"\"}";
        handler.CapturedBodies.Should().ContainSingle().Which.Should().Be(wire);
        handler.Captured[0].Headers.GetValues("Signature").Single().Should()
            .Be(RequestSigner.Sign(Encoding.UTF8.GetBytes(wire), "K-1"));

        // Cleared comes back as null, never as "".
        wallet.Label.Should().BeNull();
    }

    [Fact]
    public async Task Explicit_nulls_in_the_wallet_shape_decode_to_null()
    {
        // A master wallet: no master of its own, no deposit webhook, no name. The API sends
        // all three keys with null rather than dropping them, and none may blow up the
        // decoder.
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"type\":\"master\",\"address\":\"0xbeef\",\"chain_family\":\"EVM\",\"frozen\":true,"
            + "\"master_wallet_address\":null,\"callback_url\":null,\"label\":null}"));
        var client = NewClient(handler);

        var wallet = await client.Wallets.InfoAsync("0xbeef");

        wallet.Type.Should().Be(WalletType.Master);
        wallet.Frozen.Should().BeTrue();
        wallet.MasterWalletAddress.Should().BeNull();
        wallet.CallbackUrl.Should().BeNull();

        // Unnamed reads as null, never as "".
        wallet.Label.Should().BeNull();

        // A transit wallet always has callback_url null, master_wallet_address set. It can
        // still carry a name.
        var handler2 = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"type\":\"transit\",\"address\":\"0xcafe\",\"chain_family\":\"EVM\",\"frozen\":false,"
            + "\"master_wallet_address\":\"0xbeef\",\"callback_url\":null,\"label\":\"Payouts\"}"));
        var transit = await NewClient(handler2).Wallets.RebindMasterAsync("0xcafe", "0xbeef");

        transit.Type.Should().Be(WalletType.Transit);
        transit.MasterWalletAddress.Should().Be("0xbeef");
        transit.CallbackUrl.Should().BeNull();
        transit.Label.Should().Be("Payouts");
    }

    [Fact]
    public async Task List_carries_the_label_of_every_item()
    {
        // The list is where the name earns its keep: without it the items are told apart
        // only by address.
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"items\":["
            + "{\"type\":\"master\",\"address\":\"0xbeef\",\"chain_family\":\"EVM\",\"frozen\":false,"
            + "\"master_wallet_address\":null,\"callback_url\":null,\"label\":\"Treasury EU\"},"
            + "{\"type\":\"static\",\"address\":\"0xdead\",\"chain_family\":\"EVM\",\"frozen\":false,"
            + "\"master_wallet_address\":\"0xbeef\",\"callback_url\":null,\"label\":null}]}"));

        var list = await NewClient(handler).Wallets.ListAsync();

        list.Items.Should().HaveCount(2);
        list.Items[0].Label.Should().Be("Treasury EU");
        list.Items[1].Label.Should().BeNull();
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
