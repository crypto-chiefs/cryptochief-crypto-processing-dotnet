using System.Numerics;
using CryptoChief.Processing;
using CryptoChief.Processing.Amounts;
using CryptoChief.Processing.Chains;
using CryptoChief.Processing.Services;

var client = new CryptoChiefClient(
    Environment.GetEnvironmentVariable("MERCHANT_ID")!,
    Environment.GetEnvironmentVariable("API_KEY")!);

const string router  = "0x7a250d5630B4cF539739dF2C5dAcb4c659F2488D";
const string tokenIn = "0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2"; // WETH
const string tokenOut= "0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48"; // USDC

var amountIn     = Amount.HumanToBase("0.01", 18);
var amountOutMin = BigInteger.Zero;
var deadline     = new BigInteger(DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds());
var path         = new[] { tokenIn, tokenOut };
var recipient    = Environment.GetEnvironmentVariable("WALLET")!;

var signed = await client.Transactions.SignEvmCallAsync(new EvmCallRequest
{
    Network     = Chain.EthMainnet,
    FromAddress = recipient,
    Contract    = router,
    Method      = "swapExactTokensForTokens(uint256,uint256,address[],address,uint256)",
    Args        = new object?[] { amountIn, amountOutMin, path, recipient, deadline },
    UrlCallback = "https://your.app/webhooks/transaction",
});

Console.WriteLine($"Signed swap. UUID: {signed.Uuid}");
Console.WriteLine($"Expires:        {signed.ExpiresAt}");
Console.WriteLine("Call Execute() before TTL elapses to broadcast.");
