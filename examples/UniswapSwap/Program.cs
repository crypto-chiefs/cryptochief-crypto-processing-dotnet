using System.Numerics;
using CryptoChief.Processing;
using CryptoChief.Processing.Amounts;
using CryptoChief.Processing.Chains;
using CryptoChief.Processing.Models;
using CryptoChief.Processing.Polling;
using CryptoChief.Processing.Services;

// A Uniswap V2 swap is two transactions, not one. The router moves TOKEN_IN out
// of your wallet with transferFrom, so it needs an ERC-20 allowance first —
// approve, let it confirm, then swap. A swap signed before the approve is mined
// reserves the same nonce and reverts.
//
//   MERCHANT_ID=... API_KEY=... WALLET=0x... MIN_OUT=1234.5 dotnet run
//
// Set BROADCAST=1 to actually send both; without it this stops after signing
// the approve.

var client = new CryptoChiefClient(
    Environment.GetEnvironmentVariable("MERCHANT_ID")!,
    Environment.GetEnvironmentVariable("API_KEY")!);

const string router  = "0x7a250d5630B4cF539739dF2C5dAcb4c659F2488D";
const string tokenIn = "0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2"; // WETH
const string tokenOut= "0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48"; // USDC

var recipient = Environment.GetEnvironmentVariable("WALLET")!;
var broadcast = Environment.GetEnvironmentVariable("BROADCAST") is not null;

var amountIn = Amount.HumanToBase("0.01", 18);

// Slippage floor, in TOKEN_OUT base units. Zero accepts whatever the pool
// returns, which on a public mempool hands the trade to the first sandwich bot
// that sees it — so it is required rather than defaulted.
var minOut = Environment.GetEnvironmentVariable("MIN_OUT")
    ?? throw new InvalidOperationException("set MIN_OUT (human units of TOKEN_OUT)");
var amountOutMin = Amount.HumanToBase(minOut, 6); // USDC has 6 decimals
if (amountOutMin.IsZero) Console.WriteLine("MIN_OUT=0 — no slippage protection on this swap");

// The allowance the router needs before it can move TOKEN_IN.
var approve = await client.Transactions.SignEvmCallAsync(new EvmCallRequest
{
    Network     = Chain.EthMainnet,
    FromAddress = recipient,
    Contract    = tokenIn,
    Method      = "approve(address,uint256)",
    Args        = new object?[] { router, amountIn },
    UrlCallback = "https://your.app/webhooks/transaction",
});
Console.WriteLine($"Signed approve. UUID: {approve.Uuid}");

if (!broadcast)
{
    Console.WriteLine("BROADCAST unset — stopping after the approve signature.");
    return;
}

await client.Transactions.ExecuteAsync(new ExecuteTransactionRequest { Uuid = approve.Uuid });
var approved = await client.WaitForTransactionAsync(approve.Uuid);
if (approved.Status != TxStatus.Confirmed)
    throw new InvalidOperationException($"approve did not confirm: status={approved.Status}");
Console.WriteLine($"Approve confirmed: {approved.TxHash}");

// Signed only now: the nonce comes from chain state, and the deadline is
// measured from here rather than from before the wait.
var deadline = new BigInteger(DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds());
var path     = new[] { tokenIn, tokenOut };

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

await client.Transactions.ExecuteAsync(new ExecuteTransactionRequest { Uuid = signed.Uuid });
var final = await client.WaitForTransactionAsync(signed.Uuid);
Console.WriteLine($"Terminal: status={final.Status} tx={final.TxHash}");
