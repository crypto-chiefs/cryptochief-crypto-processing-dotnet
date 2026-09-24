using System.Net;
using System.Text;
using CryptoChief.Processing.Errors;
using CryptoChief.Processing.Models;
using CryptoChief.Processing.Polling;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

/// <summary>
/// EVM signature supersede: the cancelled status, the replaced uuids on the sign answer,
/// <c>error_reason</c> on the transaction, and the new error codes.
/// </summary>
public class TransactionsSupersedeTests
{
    private const string Old = "0c1d9f3e-5a7b-4c2e-9f1a-3b6d8e2f4a10";
    private const string New = "b4ee6a7a-f7c2-474d-b002-e83ebe3e78db";

    private static readonly SignTransactionRequest SignReq = new()
    {
        Network = "ETH_MAINNET", FromAddress = "0xa", Type = TxType.Native, ToAddress = "0xb", Value = "1",
    };

    [Fact]
    public void Cancelled_is_final()
    {
        TxStatus.Cancelled.Should().Be("cancelled");
        new TransactionInfo { Status = TxStatus.Cancelled }.IsTerminal.Should().BeTrue();
        new TransactionInfo { Status = TxStatus.Cancelled }.Succeeded.Should().BeFalse();
        foreach (var live in new[] { TxStatus.Signed, TxStatus.Broadcasting, TxStatus.Broadcasted })
            new TransactionInfo { Status = live }.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public async Task Waiting_returns_a_superseded_signature_at_once()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"uuid\":\"" + Old + "\",\"status\":\"cancelled\",\"error_reason\":\"SUPERSEDED_BY:" + New + "\"}"));

        var tx = await NewClient(handler).WaitForTransactionAsync(Old,
            new PollOptions { Interval = TimeSpan.FromMilliseconds(10), Timeout = TimeSpan.FromMilliseconds(300) });

        handler.Captured.Should().HaveCount(1);
        tx.Status.Should().Be(TxStatus.Cancelled);
        tx.ErrorReason.Should().Be("SUPERSEDED_BY:" + New);
    }

    [Fact]
    public async Task Sign_reads_the_replaced_signatures()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"uuid\":\"" + New + "\",\"status\":\"signed\",\"network\":\"ETH_MAINNET\",\"chain_family\":\"EVM\","
            + "\"signed_tx_hex\":\"0x02\",\"tx_hash\":\"0xabc\",\"expires_at\":\"2026-06-01T12:10:00Z\","
            + "\"superseded_uuids\":[\"" + Old + "\"]}"));

        var res = await NewClient(handler).Transactions.SignAsync(SignReq);

        res.SupersededUuids.Should().Equal(Old);
    }

    [Fact]
    public async Task Sign_without_replaced_signatures_reads_empty()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"uuid\":\"" + New + "\",\"status\":\"signed\"}"));

        var res = await NewClient(handler).Transactions.SignAsync(SignReq);

        res.SupersededUuids.Should().BeEmpty();
    }

    [Fact]
    public async Task Info_reads_the_nonce_gap_reason()
    {
        var reason = "NONCE_GAP: missing_nonce=7 blocking_uuid=" + Old;
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"uuid\":\"" + New + "\",\"status\":\"signed\",\"error_reason\":\"" + reason + "\"}"));

        var tx = await NewClient(handler).Transactions.InfoAsync(New);

        tx.ErrorReason.Should().Be(reason);
    }

    [Theory]
    [InlineData(ErrorCodes.NonceGap)]
    [InlineData(ErrorCodes.NonceAlreadyUsed)]
    public async Task Execute_surfaces_the_nonce_codes(string code)
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.BadRequest,
            "{\"error\":\"SERVICE_ERROR\",\"msg\":\"" + code + "\",\"ok\":false}"));

        var act = () => NewClient(handler).Transactions.ExecuteAsync(new ExecuteTransactionRequest { Uuid = New });

        var err = (await act.Should().ThrowAsync<CryptoChiefApiException>()).Which;
        err.Code.Should().Be(code);
        err.HttpStatus.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sign_is_refused_while_an_execute_is_unresolved()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.BadRequest,
            "{\"error\":\"SERVICE_ERROR\",\"msg\":\"PREVIOUS_EXECUTE_UNRESOLVED: uuid=" + Old + "\",\"ok\":false}"));

        var act = () => NewClient(handler).Transactions.SignAsync(SignReq);

        var err = (await act.Should().ThrowAsync<CryptoChiefApiException>()).Which;
        err.Code.Should().StartWith(ErrorCodes.PreviousExecuteUnresolved).And.EndWith(Old);
    }

    [Fact]
    public void The_nonce_codes_match_the_wire()
    {
        ErrorCodes.NonceGap.Should().Be("NONCE_GAP");
        ErrorCodes.NonceAlreadyUsed.Should().Be("NONCE_ALREADY_USED");
        ErrorCodes.PreviousExecuteUnresolved.Should().Be("PREVIOUS_EXECUTE_UNRESOLVED");
    }

    private static HttpResponseMessage Resp(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured.Add(request);
            return Task.FromResult(reply(request));
        }
    }
}
