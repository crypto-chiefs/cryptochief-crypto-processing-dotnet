using CryptoChief.Processing;
using CryptoChief.Processing.Amounts;
using CryptoChief.Processing.Chains;
using CryptoChief.Processing.Errors;
using CryptoChief.Processing.Models;
using CryptoChief.Processing.Polling;

var merchantId = Environment.GetEnvironmentVariable("MERCHANT_ID")
    ?? throw new InvalidOperationException("MERCHANT_ID not set");
var apiKey = Environment.GetEnvironmentVariable("API_KEY")
    ?? throw new InvalidOperationException("API_KEY not set");
var toAddress = Environment.GetEnvironmentVariable("TO_ADDRESS")
    ?? throw new InvalidOperationException("TO_ADDRESS not set");

var client = new CryptoChiefClient(merchantId, apiKey);

var assets = await client.Blockchain.ContractsAvailableAsync(Chain.EthSepolia);
Console.WriteLine($"Enabled assets on Sepolia: {string.Join(", ", assets.Items.Select(a => a.Coin))}");

var estimate = await client.Payouts.EstimateAsync(new EstimatePayoutRequest
{
    Network   = Chain.EthSepolia,
    Coin      = "ETH",
    Amount    = "0.0001",
    ToAddress = toAddress,
});
Console.WriteLine($"Recipient will receive: {estimate.AmountToReceive} ETH");
Console.WriteLine($"Estimated fee: {estimate.FeeInfo?.EstimatedFiat ?? "-"} USD");

try
{
    var payout = await client.Payouts.ExecuteAsync(new ExecutePayoutRequest
    {
        OrderId     = $"order-{Guid.NewGuid():N}",
        UserId      = "user-7",
        Network     = Chain.EthSepolia,
        Coin        = "ETH",
        Amount      = "0.0001",
        ToAddress   = toAddress,
        UrlCallback = "https://your.app/webhooks/payout",
    });
    Console.WriteLine($"Payout uuid: {payout.Uuid}");

    var final = await client.WaitForPayoutAsync(payout.Uuid,
        new PollOptions { Interval = TimeSpan.FromSeconds(5), Timeout = TimeSpan.FromMinutes(5) });
    Console.WriteLine($"Final: {final.Status} (tx={final.TxId ?? "-"})");
}
catch (CryptoChiefApiException ex) when (ex.Code == ErrorCodes.InsufficientFunds)
{
    Console.WriteLine("Top up the wallet and re-run.");
}
