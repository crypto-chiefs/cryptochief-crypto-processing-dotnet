using System.Net;
using System.Text;
using System.Text.Json;
using CryptoChief.Processing.Http;
using CryptoChief.Processing.Models;
using CryptoChief.Processing.Services;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

// Request bodies of methods with optional fields. A null or unset optional field is absent
// from the body, as in 0.9.0; the expected bodies are compared as JSON values.
public class RequestBodyTests
{
    private const string Tron = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";
    private const string Evm = "0x742d35cc6634c0532925a3b844bc454e4438f44e";

    private static readonly Dictionary<string, (Func<CryptoChiefClient, Task> Call, string Body)> Cases = new()
    {
        ["sweeps_update_settings_address_only"] = (
            c => c.Sweeps.UpdateSettingsAsync(Tron),
            """{"address":"TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t"}"""),
        ["sweeps_update_settings_explicit_nulls"] = (
            c => c.Sweeps.UpdateSettingsAsync(Tron, typeWork: null, thresholdAmountUsd: null, feeMode: null, networkCode: null, gasSource: null),
            """{"address":"TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t"}"""),
        ["sweeps_update_settings_inherit_all"] = (
            c => c.Sweeps.UpdateSettingsAsync(Tron, SweepFieldWrite.Inherit, SweepFieldWrite.Inherit, SweepFieldWrite.Inherit, "TRON", SweepFieldWrite.Inherit),
            """{"address":"TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t","fields":["type_work","threshold_amount_usd","fee_mode","gas_source"],"network_code":"TRON"}"""),
        ["sweeps_update_settings_inherit_one"] = (
            c => c.Sweeps.UpdateSettingsAsync(Tron, feeMode: SweepFieldWrite.Inherit),
            """{"address":"TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t","fields":["fee_mode"]}"""),
        ["sweeps_update_settings_set_and_inherit"] = (
            c => c.Sweeps.UpdateSettingsAsync(Tron, typeWork: SweepFieldWrite.Set(SweepPolicyMode.Momentum), thresholdAmountUsd: SweepFieldWrite.Inherit, networkCode: ""),
            """{"address":"TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t","fields":["type_work","threshold_amount_usd"],"type_work":"momentum"}"""),
        ["sweeps_update_settings_all"] = (
            c => c.Sweeps.UpdateSettingsAsync(Tron, SweepFieldWrite.Set(SweepPolicyMode.Threshold), SweepFieldWrite.Set("25.50"),
                SweepFieldWrite.Set(SweepFeeMode.Client), "TRON", SweepFieldWrite.Set(SweepGasSource.Native)),
            """{"address":"TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t","fee_mode":"client","fields":["type_work","threshold_amount_usd","fee_mode","gas_source"],"gas_source":"native","network_code":"TRON","threshold_amount_usd":"25.50","type_work":"threshold"}"""),
        ["sweeps_settings_null_query"] = (
            c => c.Sweeps.SettingsAsync(null),
            "{}"),
        ["sweeps_settings_null_fields"] = (
            c => c.Sweeps.SettingsAsync(new SweepSettingsQuery { Address = null, NetworkCode = null }),
            "{}"),
        ["sweeps_history_null_filters"] = (
            c => c.Sweeps.HistoryAsync(new SweepHistoryQuery { Mode = null, Status = null, Search = null, Page = null, PageSize = null }),
            "{}"),
        ["sweeps_wallet_history_null_filters"] = (
            c => c.Sweeps.WalletHistoryAsync(new SweepWalletHistoryQuery { Address = Tron, Mode = null, Status = null, Search = null, Page = null, PageSize = null }),
            """{"address":"TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t"}"""),
        ["wallets_label_null_clears"] = (
            c => c.Wallets.SetLabelAsync(Evm, null!),
            """{"address":"0x742d35cc6634c0532925a3b844bc454e4438f44e","label":""}"""),
        ["wallets_callback_url_null_clears"] = (
            c => c.Wallets.SetCallbackUrlAsync(Tron, null!),
            """{"address":"TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t","callback_url":""}"""),
        ["wallets_rebind_master_null"] = (
            c => c.Wallets.RebindMasterAsync(Evm, null!),
            """{"address":"0x742d35cc6634c0532925a3b844bc454e4438f44e"}"""),
        ["wallets_generate_null_optionals"] = (
            c => c.Wallets.GenerateAsync(new GenerateWalletRequest { WalletType = WalletType.Transit, ChainFamily = "EVM", MasterWalletAddress = null, CallbackUrl = null, Label = null }),
            """{"chain_family":"EVM","wallet_type":"transit"}"""),
        ["wallets_generate_empty_optionals"] = (
            c => c.Wallets.GenerateAsync(new GenerateWalletRequest { WalletType = WalletType.Static, ChainFamily = "EVM", MasterWalletAddress = "", CallbackUrl = "", Label = "" }),
            """{"callback_url":"","chain_family":"EVM","label":"","master_wallet_address":"","wallet_type":"static"}"""),
        ["wallets_history_null_filters"] = (
            c => c.Wallets.HistoryAsync(new WalletHistoryQuery { Address = Tron, DateFrom = null, DateTo = null, Page = null, PageSize = null }),
            """{"address":"TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t"}"""),
        ["blockchain_contracts_available_null_network"] = (
            c => c.Blockchain.ContractsAvailableAsync(null),
            "{}"),
        ["blockchain_wallet_balance_null_contracts"] = (
            c => c.Blockchain.WalletBalanceAsync("ETH", new[] { Evm }, null),
            """{"addresses":["0x742d35cc6634c0532925a3b844bc454e4438f44e"],"chain":"ETH"}"""),
        ["payins_create_null_optionals"] = (
            c => c.PayIns.CreateAsync(new CreatePayInRequest
            {
                OrderId = "o-1", UserId = "u-1", Mode = PayInMode.Fiat, ToAddress = null, MasterWalletAddress = null,
                Environment = null, LifetimeSec = null, UrlCallback = null, UrlSuccess = null, UrlError = null,
                AdditionalData = null, AccuracyPaymentPercent = null, AmountFiat = null, Currency = null,
                CourseSource = null, Assets = null, AmountCrypto = null, Asset = null,
            }),
            """{"mode":"fiat","order_id":"o-1","user_id":"u-1"}"""),
        ["payins_create_nested_nulls"] = (
            c => c.PayIns.CreateAsync(new CreatePayInRequest
            {
                OrderId = "o-1", UserId = "u-1", Mode = PayInMode.Fiat,
                Assets = new AssetsPolicy { Allow = null, Exclude = new[] { new Asset(), null! } },
                Asset = new Asset { Network = null, Coin = "ETH" },
            }),
            """{"asset":{"coin":"ETH"},"assets":{"exclude":[{},null]},"mode":"fiat","order_id":"o-1","user_id":"u-1"}"""),
        ["payins_select_asset_null_master"] = (
            c => c.PayIns.SelectAssetAsync(new SelectAssetRequest { Uuid = "u", Coin = "USDT", Network = "TRON", MasterWalletAddress = null }),
            """{"coin":"USDT","network":"TRON","uuid":"u"}"""),
        ["payins_history_null_filters"] = (
            c => c.PayIns.HistoryAsync(new HistoryQuery { Page = null, PageSize = null, Status = null, Coin = null, Network = null, DateFrom = null, DateTo = null }),
            "{}"),
        ["payins_history_null_query_sends_empty_body"] = (
            c => c.PayIns.HistoryAsync(null!),
            ""),
        ["payouts_estimate_null_optionals"] = (
            c => c.Payouts.EstimateAsync(new EstimatePayoutRequest
            {
                Network = "ETH", Coin = "ETH", Amount = "0.1", ToAddress = Evm, FromAddresses = null,
                AllowMultipleSources = null, AutoConvert = null, AutoConvertPolicy = null, MaxFeeAmountFiat = null, Memo = null,
            }),
            """{"amount":"0.1","coin":"ETH","network":"ETH","to_address":"0x742d35cc6634c0532925a3b844bc454e4438f44e"}"""),
        ["payouts_execute_null_optionals"] = (
            c => c.Payouts.ExecuteAsync(Payout() with
            {
                FromAddresses = null, AllowMultipleSources = null, AutoConvert = null, AutoConvertPolicy = null, MaxFeeAmountFiat = null, Memo = null,
            }),
            """{"amount":"0.0001","coin":"ETH","network":"ETH_SEPOLIA","order_id":"o-1","to_address":"0x742d35cc6634c0532925a3b844bc454e4438f44e","url_callback":"https://a/cb","user_id":"u-7"}"""),
        ["payouts_batch_null_callback"] = (
            c => c.Payouts.BatchEstimateAsync(new BatchExecuteRequest { UrlCallback = null, Items = new[] { Payout() with { Memo = null } } }),
            """{"items":[{"amount":"0.0001","coin":"ETH","network":"ETH_SEPOLIA","order_id":"o-1","to_address":"0x742d35cc6634c0532925a3b844bc454e4438f44e","url_callback":"https://a/cb","user_id":"u-7"}]}"""),
        ["transactions_sign_null_optionals"] = (
            c => c.Transactions.SignAsync(new SignTransactionRequest
            {
                Network = "ETH", FromAddress = Evm, Type = TxType.Native, ToAddress = null, Value = null, Contract = null, Calls = null, UrlCallback = null,
            }),
            """{"from_address":"0x742d35cc6634c0532925a3b844bc454e4438f44e","network":"ETH","type":"native"}"""),
        ["transactions_sign_call_null_optionals"] = (
            c => c.Transactions.SignAsync(new SignTransactionRequest
            {
                Network = "ETH", FromAddress = Evm, Type = TxType.Contract,
                Calls = new[] { new ContractCall { To = Evm, Value = null, Data = null!, Accounts = null, Bounce = null } },
            }),
            """{"calls":[{"to":"0x742d35cc6634c0532925a3b844bc454e4438f44e"}],"from_address":"0x742d35cc6634c0532925a3b844bc454e4438f44e","network":"ETH","type":"contract"}"""),
        ["transactions_execute_null_signed_tx"] = (
            c => c.Transactions.ExecuteAsync(new ExecuteTransactionRequest { Uuid = "u", SignedTxHex = null }),
            """{"uuid":"u"}"""),
        ["transactions_ton_call_null_optionals"] = (
            c => c.Transactions.SignTonCallAsync(new TonCallRequest
            {
                Network = "TON", FromAddress = "EQCxE6mUtQJKFnGfaROTKOt1lZbDiiX1kCixRv7Nw2Id_sDs",
                Contract = "EQCxE6mUtQJKFnGfaROTKOt1lZbDiiX1kCixRv7Nw2Id_sDs", BodyCell = Array.Empty<byte>(),
                Value = null, Bounce = null, UrlCallback = null,
            }),
            """{"calls":[{"data":"","to":"EQCxE6mUtQJKFnGfaROTKOt1lZbDiiX1kCixRv7Nw2Id_sDs","value":"0"}],"from_address":"EQCxE6mUtQJKFnGfaROTKOt1lZbDiiX1kCixRv7Nw2Id_sDs","network":"TON","type":"contract"}"""),
        ["credits_topup_null_urls"] = (
            c => c.Credits.TopupAsync(new CreditsTopupRequest { Amount = "10", Currency = "USD", UrlSuccess = null, UrlError = null }),
            """{"amount":"10","currency":"USD"}"""),
        ["currencies_convert_null_provider"] = (
            c => c.Currencies.FiatToCryptoAsync(new ConvertRequest { Provider = null, From = "USD", To = "BTC", Amount = "100" }),
            """{"amount":"100","from":"USD","to":"BTC"}"""),
        ["static_deposits_history_null_filters"] = (
            c => c.StaticDeposits.HistoryAsync(new StaticDepositHistoryQuery
            {
                Address = null, Status = null, Coin = null, Network = null, DateFrom = null, DateTo = null, Page = null, PageSize = null,
            }),
            "{}"),
    };

    public static IEnumerable<object[]> CaseNames() => Cases.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task Body_matches_the_published_wire_form(string name)
    {
        var (call, expected) = Cases[name];
        var handler = new BodyHandler();
        var client = new CryptoChiefClient(new CryptoChiefClientOptions
        {
            MerchantId = "M-1",
            ApiKey = "K-1",
            BaseUrl = "https://test/",
            MaxRetries = 0,
        }, new HttpClient(handler), null);

        await call(client);

        var body = handler.Bodies.Should().ContainSingle().Subject;
        if (expected.Length == 0)
        {
            body.Should().BeEmpty();
        }
        else
        {
            Encoding.UTF8.GetString(body).ShouldBeJson(expected);
            NullMembers(JsonDocument.Parse(body).RootElement, "$").Should().BeEmpty();
        }

        handler.Signatures.Should().ContainSingle().Which.Should().Be(handler.ExpectedSignatures.Single());
        handler.HasMd5Header.Should().BeFalse();
    }

    private static ExecutePayoutRequest Payout() => new()
    {
        OrderId = "o-1", UserId = "u-7", Network = "ETH_SEPOLIA", Coin = "ETH", Amount = "0.0001", ToAddress = Evm, UrlCallback = "https://a/cb",
    };

    private static IEnumerable<string> NullMembers(JsonElement e, string at)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.Null) yield return $"{at}.{p.Name}";
                    foreach (var n in NullMembers(p.Value, $"{at}.{p.Name}")) yield return n;
                }
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in e.EnumerateArray())
                    foreach (var n in NullMembers(item, $"{at}[{i++}]")) yield return n;
                break;
        }
    }

    private sealed class BodyHandler : HttpMessageHandler
    {
        public List<byte[]> Bodies { get; } = new();
        public List<string> Signatures { get; } = new();
        public List<string> ExpectedSignatures { get; } = new();
        public bool HasMd5Header { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Bodies.Add(body);
            Signatures.Add(string.Join(",", request.Headers.GetValues("X-CC-Signature")));
            ExpectedSignatures.Add("v1=" + RequestSigner.SignHmacV1("K-1", new HmacV1Input
            {
                Timestamp = request.Headers.GetValues("X-CC-Timestamp").Single(),
                Nonce = request.Headers.GetValues("X-CC-Nonce").Single(),
                Method = request.Method.Method,
                Path = request.RequestUri!.AbsolutePath,
                Query = request.RequestUri.Query.TrimStart('?'),
                Merchant = "M-1",
                Body = body,
            }));
            HasMd5Header |= request.Headers.Contains("Signature");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("null"u8.ToArray()) };
        }
    }
}
