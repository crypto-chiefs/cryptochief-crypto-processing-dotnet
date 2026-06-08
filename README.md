# Crypto Chief .NET SDK — Crypto Processing API Client

[![NuGet](https://img.shields.io/nuget/v/CryptoChief.Processing.svg)](https://www.nuget.org/packages/CryptoChief.Processing)
[![Downloads](https://img.shields.io/nuget/dt/CryptoChief.Processing.svg)](https://www.nuget.org/packages/CryptoChief.Processing)
[![SDK Docs](https://img.shields.io/badge/docs-SDK%20guide-2ea44f)](https://docs-sdk.crypto-chief.com/processing/dotnet)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**Crypto Chief .NET SDK** is the official C#/.NET client library for the
[Crypto Chief](https://crypto-chief.com/processing/) **crypto processing API** — a unified
crypto payment gateway for accepting crypto payments, sending crypto payouts
(single and mass), signing on-chain transactions, managing wallets, and
verifying webhooks across **Ethereum, Tron, TON, Solana, Bitcoin and 20+ more
blockchains**.

Drop it into any ASP.NET Core, Worker Service, console, or function app to
add cryptocurrency payment processing — stablecoin (USDT / USDC) payouts,
pay-ins, swaps, and smart-contract calls — with typed `record` requests,
`BigInteger` amounts, `Task<T>` async, `CancellationToken` cooperation, and
first-class `IHttpClientFactory` integration.

- One-line setup; thread-safe `CryptoChiefClient` ready for the DI container.
- Typed `record` request/response DTOs for every documented endpoint.
- **Contract calls without hand-encoded calldata** — Solidity ABI for EVM
  and TRON, Anchor + Borsh for Solana, Jetton / NFT / comment helpers for TON.
- **Local RSA decryption** of generated wallet private keys (opt-in).
- Stable error codes via a typed `CryptoChiefApiException`, automatic retry
  on 5xx + transport faults with exponential-with-jitter backoff.
- Arbitrary-precision amounts via `System.Numerics.BigInteger` — no `double`,
  ever.
- Webhook verification + typed events (`PayoutWebhookEvent`,
  `TransactionWebhookEvent`, `PayInWebhookEvent`, `StaticDepositWebhookEvent`).
- Polling helpers: `await client.WaitForPayoutAsync(uuid)` blocks until
  terminal.
- Targets **.NET 8.0** (LTS) and **.NET 6.0** (LTS).

## Install

```bash
dotnet add package CryptoChief.Processing
```

## Quick start

```csharp
using CryptoChief.Processing;
using CryptoChief.Processing.Chains;
using CryptoChief.Processing.Models;

var client = new CryptoChiefClient("MERCHANT_ID", "API_KEY");

var estimate = await client.Payouts.EstimateAsync(new EstimatePayoutRequest
{
    Network   = Chain.EthSepolia,
    Coin      = "ETH",
    Amount    = "0.0001",
    ToAddress = "0xRecipient...",
});
Console.WriteLine($"recipient receives {estimate.AmountToReceive}");
```

Both credentials come from the dashboard → Integration tab. The API key is
the **signing secret** — keep it server-side.

## Dependency injection (ASP.NET Core / Worker)

```csharp
using Microsoft.Extensions.DependencyInjection;
using CryptoChief.Processing;

builder.Services.AddCryptoChief(o =>
{
    o.MerchantId = builder.Configuration["CryptoChief:MerchantId"]!;
    o.ApiKey     = builder.Configuration["CryptoChief:ApiKey"]!;
    o.LoadRsaPrivateKeyFromFile("rsa_private.pem"); // optional
});
```

Or bind from `IConfiguration`:

```csharp
builder.Services.AddCryptoChief(builder.Configuration.GetSection("CryptoChief"));
```

The registration uses `IHttpClientFactory`, so the client respects HTTP
connection pooling and any `IHttpClientBuilder` policies (Polly, logging,
named handlers) you add downstream.

## What you can do with it

| Domain | Service | Key methods |
|---|---|---|
| Single payout (incl. auto-convert swap) | `client.Payouts` | `EstimateAsync`, `ExecuteAsync`, `InfoAsync`, `HistoryAsync` |
| Mass payout (up to 50 items) | `client.Payouts` | `BatchEstimateAsync`, `BatchExecuteAsync` |
| Two-phase sign / broadcast for arbitrary txs | `client.Transactions` | `SignAsync`, `ExecuteAsync`, `InfoAsync`, `HistoryAsync` |
| EVM / TRON contract calls (incl. ERC-20 / TRC-20) | `client.Transactions` | `SignEvmCallAsync`, `SignTronCallAsync`, `Erc20TransferAsync` |
| Solana programs | `client.Transactions` | `SignAnchorCallAsync`, `SignSolanaCallAsync` |
| TON contract calls (Jetton / NFT / text) | `client.Transactions` | `JettonTransferAsync`, `NftTransferAsync`, `SendTonCommentAsync`, `SignTonCallAsync` |
| Accept incoming payments | `client.PayIns` | `CreateAsync`, `SelectAssetAsync`, `ResetAssetAsync`, `CancelAsync`, `InfoAsync`, `HistoryAsync` |
| Wallet management + RSA decrypt | `client.Wallets` | `GenerateAsync`, `ListAsync`, `InfoAsync`, `FreezeAsync`, `DecryptPrivateKey` |
| Treasury sweeps | `client.Sweeps` | `ForceAsync`, `HistoryAsync`, `WalletHistoryAsync` |
| Withdrawals (read-only) | `client.Withdrawals` | `InfoAsync`, `HistoryAsync` |
| Static-deposit history | `client.StaticDeposits` | `InfoAsync`, `HistoryAsync` |
| On-chain queries | `client.Blockchain` | `ContractsAvailableAsync`, `WalletBalanceAsync`, `TransactionStatusAsync` |
| Fiat ↔ crypto rate quote | `client.Currencies` | `FiatToCryptoAsync`, `CryptoToFiatAsync` |

## Payout with confirmation

```csharp
using CryptoChief.Processing.Errors;
using CryptoChief.Processing.Polling;

try
{
    var payout = await client.Payouts.ExecuteAsync(new ExecutePayoutRequest
    {
        OrderId     = "order-42",               // idempotency key — safe to retry
        UserId      = "u-7",
        Network     = Chain.EthSepolia,
        Coin        = "ETH",
        Amount      = "0.0001",
        ToAddress   = "0xRecipient...",
        UrlCallback = "https://your.app/webhooks/payout",
    });

    var final = await client.WaitForPayoutAsync(payout.Uuid, new PollOptions
    {
        Interval = TimeSpan.FromSeconds(5),
        Timeout  = TimeSpan.FromMinutes(5),
    });
    if (final.Succeeded)
        Console.WriteLine($"paid: tx={final.TxId}");
}
catch (CryptoChiefApiException ex) when (ex.Code == ErrorCodes.InsufficientFunds)
{
    // top up and try again
}
```

## Two-phase sign + execute

`Transactions.SignAsync` builds and signs a transaction **without
broadcasting**. The TTL of the signed reservation varies by chain (EVM 10 m,
UTXO 15 m, TRON 45 s, Solana 60 s, XRP 90 s, TON 300 s) — call `ExecuteAsync`
before it expires.

```csharp
using CryptoChief.Processing.Amounts;
using CryptoChief.Processing.Models;

var wei = Amount.HumanToBase("0.0001", 18);

var signed = await client.Transactions.SignAsync(new SignTransactionRequest
{
    Network     = Chain.EthSepolia,
    FromAddress = "0xYourWallet...",
    Type        = TxType.Native,
    ToAddress   = "0xRecipient...",
    Value       = wei.ToString(), // base units (wei)
    UrlCallback = "https://your.app/webhooks/transaction",
});

await client.Transactions.ExecuteAsync(new ExecuteTransactionRequest { Uuid = signed.Uuid });
```

## Contract calls — the easy way

Most real-world transactions are smart-contract calls (token transfers, DEX
swaps, Anchor program instructions, Jetton transfers). You **never** have to
encode the `data` field by hand: give the library a typed description, get
back a signed reservation.

### EVM — Uniswap V2 swap

```csharp
using System.Numerics;
using CryptoChief.Processing.Services;

var amountIn     = Amount.HumanToBase("0.01", 18);
var amountOutMin = BigInteger.Zero;
var deadline     = new BigInteger(DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds());
var path         = new[] { tokenIn, tokenOut };

var signed = await client.Transactions.SignEvmCallAsync(new EvmCallRequest
{
    Network     = Chain.EthMainnet,
    FromAddress = "0xYourWallet...",
    Contract    = "0x7a250d5630B4cF539739dF2C5dAcb4c659F2488D", // V2 router
    Method      = "swapExactTokensForTokens(uint256,uint256,address[],address,uint256)",
    Args        = new object?[] { amountIn, amountOutMin, path, "0xYourWallet...", deadline },
    UrlCallback = "https://your.app/webhooks/transaction",
});
```

The encoder supports `uint/int<M>`, `address`, `bool`, `bytes`, `bytes<N>`,
`string`, and fixed / dynamic arrays of any of those. Argument values accept
`BigInteger`, plain `int`/`long`/`uint`/`ulong`, decimal / hex strings,
`byte[]`, and `IEnumerable<T>` of those. Function-name aliases
(`uint` → `uint256`) and parameter names (`uint256 amount`) are normalised
before hashing.

ERC-20 / TRC-20 transfers have a one-liner:

```csharp
var amount = Amount.HumanToBase("12.5", 6); // USDT decimals = 6

await client.Transactions.Erc20TransferAsync(new Erc20TransferRequest
{
    Network       = Chain.EthMainnet,
    FromAddress   = "0xYourWallet...",
    TokenContract = "0xdAC17F958D2ee523a2206206994597C13D831ec7",
    Recipient     = "0x...",
    Amount        = amount,
});
```

### TRON — same encoder, base58 addresses

TRON shares the EVM ABI. `SignEvmCallAsync` (or its alias `SignTronCallAsync`)
accepts both base58 (`T...`) and 0x41-prefixed hex addresses transparently:

```csharp
await client.Transactions.SignTronCallAsync(new EvmCallRequest
{
    Network     = Chain.TronMainnet,
    FromAddress = "TYourWallet...",
    Contract    = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", // USDT TRC-20 base58
    Method      = "transfer(address,uint256)",
    Args        = new object?[] { "TRecipient...", amount },
});
```

Need to convert addresses outside a call?
`CryptoChief.Processing.Encoders.Tron.TronAddress.ToHex` /
`TronAddress.FromHex` are public.

### Solana — Anchor program call

Anchor programs use an 8-byte SHA-256 discriminator (`global:<method>`)
followed by Borsh-encoded arguments. The SDK builds both:

```csharp
using CryptoChief.Processing.Encoders.Solana;
using CryptoChief.Processing.Models;

var signed = await client.Transactions.SignAnchorCallAsync(new AnchorCallRequest
{
    Network     = Chain.SolanaMainnet,
    FromAddress = "YourWallet...",
    Program     = "YourProgramId...",
    Method      = "initialize",
    Args = new[]
    {
        Borsh.U64(1_000_000),
        Borsh.String("hello"),
        Borsh.Pubkey("Recipient..."),
    },
    Accounts = new[]
    {
        new SolanaAccount { Pubkey = "YourWallet...", IsSigner = true,  IsWritable = true },
        new SolanaAccount { Pubkey = "DataAcct...",   IsSigner = false, IsWritable = true },
        new SolanaAccount { Pubkey = "11111111111111111111111111111111", IsSigner = false, IsWritable = false },
    },
});
```

Borsh primitives: `Borsh.U8/16/32/64/128`, `Borsh.I8/16/32/64`, `Borsh.Bool`,
`Borsh.String`, `Borsh.Bytes`, `Borsh.FixedBytes`, `Borsh.Pubkey`,
`Borsh.Option`, `Borsh.Vec`, `Borsh.Struct`.

Non-Anchor program? Pass pre-built instruction bytes with
`SignSolanaCallAsync(new SolanaCallRequest { InstructionData = ..., Accounts = ... })`.

### TON — Jetton / NFT / comment in one call

TON contract bodies are program-specific cells with no Solidity-style ABI,
so the SDK encodes them for you behind high-level helpers. You describe the
operation in human terms.

```csharp
using CryptoChief.Processing.Services;

var amount = Amount.HumanToBase("0.5", 6); // USDT Jetton has 6 decimals

var signed = await client.Transactions.JettonTransferAsync(new JettonTransferRequest
{
    Network      = Chain.TonMainnet,
    FromAddress  = "EQYourWallet...",
    JettonMaster = "EQCxE6mUtQJKFnGfaROTKOt1lZbDiiX1kCixRv7Nw2Id_sDs", // USDT
    Recipient    = "EQRecipient...",
    Amount       = amount,
    Memo         = "Order #4242", // optional — wallets show this as the comment
    // AttachedTon empty → SDK picks 0.07 TON if the receiver already has a
    // Jetton wallet for this token, 0.15 TON if a new one must be deployed.
});
```

The sender's Jetton wallet address and gas budget are resolved
automatically. If you've already pre-resolved them, pass
`JettonWalletAddress` and `AttachedTon` explicitly and no network lookup
happens.

NFT transfer and text comments use the same pattern:

```csharp
using CryptoChief.Processing.Amounts;

await client.Transactions.NftTransferAsync(new NftTransferRequest
{
    Network     = Chain.TonMainnet,
    FromAddress = "EQYourWallet...",
    NftItem     = "EQItemAddr...",
    NewOwner    = "EQRecipient...",
    AttachedTon = Amount.NanoTon("0.05"),
});

await client.Transactions.SendTonCommentAsync(new TonCommentRequest
{
    Network     = Chain.TonMainnet,
    FromAddress = "EQYourWallet...",
    Recipient   = "EQRecipient...",
    Text        = "Thanks for the coffee!",
    AmountTon   = Amount.NanoTon("1"),
});
```

For non-Jetton / non-NFT contracts, build the body cell yourself and pass
the bytes to the lower-level `SignTonCallAsync`. `TonAddress.Parse` is
provided for offline address validation / round-tripping the `EQ.../UQ...`
forms.

## Accepting payments (invoices / pay-ins)

A pay-in is an invoice the customer pays in crypto. Use `PayInMode.Fiat` to
quote the customer in fiat (e.g. `$10`) and let them pick the coin/network at
payment time, or `PayInMode.Crypto` to fix the exact coin/amount up front.

```csharp
using CryptoChief.Processing.Chains;
using CryptoChief.Processing.Models;
using CryptoChief.Processing.Polling;

// FIAT — customer is shown the asset menu
var invoice = await client.PayIns.CreateAsync(new CreatePayInRequest
{
    OrderId     = $"order-{Guid.NewGuid():N}",
    UserId      = "user-7",
    Mode        = PayInMode.Fiat,
    AmountFiat  = "10.00",
    Currency    = "USD",
    LifetimeSec = 3600,
    UrlCallback = "https://your.app/webhooks/invoice",
    UrlSuccess  = "https://your.app/checkout/success",
    UrlError    = "https://your.app/checkout/error",
});

// Either send the customer to invoice.PaymentLink (hosted page)
// or implement your own checkout — list invoice.Coins and call SelectAsset:
if (invoice.Status == PayInStatus.WaitingAssetSelect)
{
    invoice = await client.PayIns.SelectAssetAsync(new SelectAssetRequest
    {
        Uuid    = invoice.Uuid,
        Coin    = "USDT",
        Network = Chain.TronMainnet,
    });
    // invoice.ToAddress and invoice.AmountCrypto are now populated.
}

// Block until paid / cancelled / expired.
var final = await client.WaitForPayInAsync(invoice.Uuid,
    new PollOptions { Interval = TimeSpan.FromSeconds(10), Timeout = TimeSpan.FromMinutes(30) });
Console.WriteLine($"final: {final.Status} ({final.AmountCrypto} {final.PaymentCoin})");
```

CRYPTO mode fixes the asset up front — no asset-selection step:

```csharp
var invoice = await client.PayIns.CreateAsync(new CreatePayInRequest
{
    OrderId      = "order-1",
    UserId       = "user-7",
    Mode         = PayInMode.Crypto,
    AmountCrypto = "10",
    Asset        = new Asset { Coin = "USDT", Network = Chain.TronMainnet },
    UrlCallback  = "https://your.app/webhooks/invoice",
});
// invoice.ToAddress is the deposit address — show it to the customer.
```

Inbound webhooks land on `UrlCallback` carrying a `PayInWebhookEvent` —
verify with `WebhookVerifier` (see below).

## Wallets and RSA-encrypted private keys

When the API generates a wallet it returns the private key encrypted with
the RSA public key you uploaded in the dashboard (Project Settings → RSA
Key). The SDK can decrypt it locally:

```bash
# one-time setup: generate a keypair and upload rsa_public.pem to the dashboard
openssl genrsa -out rsa_private.pem 2048
openssl rsa -in rsa_private.pem -pubout -out rsa_public.pem
```

```csharp
var client = new CryptoChiefClient(new CryptoChiefClientOptions
{
    MerchantId = "...",
    ApiKey     = "...",
}.LoadRsaPrivateKeyFromFile("./rsa_private.pem"));
// Or LoadRsaPrivateKeyFromPem("-----BEGIN...");

var w = await client.Wallets.GenerateAsync(new GenerateWalletRequest
{
    WalletType  = WalletType.Master,
    ChainFamily = ChainFamily.Evm,
});

// w.PrivateKeyEncrypted is base64 RSA-OAEP / SHA-256 ciphertext.
var privHex = client.Wallets.DecryptPrivateKey(w.PrivateKeyEncrypted!);
// privHex is the chain-native hex form — keep it safe.
```

`LoadRsaPrivateKeyFromPem`/`File` accepts both PKCS#1 (`openssl genrsa`
default) and PKCS#8 (`-----BEGIN PRIVATE KEY-----`).

If you skip the option, `WalletsService.DecryptPrivateKey` throws a
`CryptoChiefException` and the rest of the SDK continues to work —
decryption is purely opt-in.

## Webhooks

Outbound webhooks are signed with the same algorithm used for outgoing
requests. The library ships a verifier and typed events:

```csharp
using CryptoChief.Processing.Webhooks;
using CryptoChief.Processing.Webhooks.Events;

app.MapPost("/webhooks/payout", async (HttpRequest req) =>
{
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    var sig = req.Headers[WebhookVerifier.SignatureHeader].ToString();
    try
    {
        var evt = WebhookVerifier.VerifyAndDecode<PayoutWebhookEvent>(apiKey, ms.ToArray(), sig);
        // process evt...
        return Results.Ok();
    }
    catch
    {
        return Results.Unauthorized();
    }
});
```

For finer-grained control:

```csharp
if (!WebhookVerifier.TryVerify(apiKey, body, signature))
    return Results.Unauthorized();
```

`WebhookVerifier.SenderIps` lists the addresses webhooks are delivered from
— whitelist them at your edge for defence in depth.

Typed event payloads: `PayoutWebhookEvent`, `TransactionWebhookEvent`,
`PayInWebhookEvent`, `StaticDepositWebhookEvent`.

## Error handling

Errors from the API are thrown as `CryptoChiefApiException` with a stable
`Code` field:

```csharp
using CryptoChief.Processing.Errors;

try
{
    await client.Payouts.ExecuteAsync(req);
}
catch (CryptoChiefApiException ex)
{
    switch (ex.Code)
    {
        case ErrorCodes.InsufficientFunds:       /* need top-up */ break;
        case ErrorCodes.AssetNotEnabled:         /* unsupported coin/network */ break;
        case ErrorCodes.DebtLimitExceeded:       /* postpaid debt cap hit */ break;
        case ErrorCodes.FromWalletNotOwned:      /* wallet doesn't belong to this project */ break;
        case ErrorCodes.AlreadyExecuted:         /* duplicate execute */ break;
        case ErrorCodes.BatchDuplicateOrderId:   /* batch validation */ break;
        default:                                 /* anything else */ break;
    }
}
```

`ex.IsRetryable` tells you whether the operation is plausibly transient
(5xx, network).

## Amounts

**Never use `double` or `decimal` for crypto amounts.** The full base-unit
range exceeds either type's precision. Use `Amount.HumanToBase` /
`Amount.BaseToHuman` (backed by `System.Numerics.BigInteger`):

```csharp
var wei = Amount.HumanToBase("1.5", 18);
// wei = BigInteger 1500000000000000000

var human = Amount.BaseToHuman(wei, 18);
// human = "1.5"
```

The API accepts both human strings (the `amount` field on most endpoints)
and base-unit integer strings (the `value` field on `/transaction/signature`).
`HumanToBase` is precise to the last digit; sub-base-unit precision is
truncated to match every blockchain client's behaviour.

For TON specifically, `Amount.NanoTon("0.05")` returns the nanoTON decimal
string that `AttachedTon` / `ForwardTonAmount` expect.

## Configuration

```csharp
var options = new CryptoChiefClientOptions
{
    MerchantId        = "...",
    ApiKey            = "...",
    BaseUrl           = "https://api-processing.crypto-chief.com", // default
    Timeout           = TimeSpan.FromSeconds(60),
    MaxRetries        = 3,
    InitialRetryDelay = TimeSpan.FromMilliseconds(200),
    MaxRetryDelay     = TimeSpan.FromSeconds(5),
    UserAgent         = "my-service/1.0",
};
options.LoadRsaPrivateKeyFromFile("./rsa_private.pem"); // optional

var client = new CryptoChiefClient(options);
```

**Test mode** is a per-project toggle in the dashboard, not a separate base
URL — point a test-mode project's credentials at the same client.

## Idempotency

`Payouts.ExecuteAsync` and `Payouts.BatchExecuteAsync` are idempotent on
`OrderId`: re-submitting the same `order_id` returns the same `uuid` rather
than creating a second payout. The library's automatic retry on 5xx relies
on this — your callers don't need any extra ceremony.

## Runnable examples

The `examples/` directory has runnable programs you can copy from:

- `Quickstart` — list enabled assets, estimate + execute + poll a payout.
- `InvoiceCreate` — accept an incoming crypto payment (FIAT or CRYPTO mode pay-in), select asset, wait for payment.
- `UniswapSwap` — V2 swap via one-line ABI encoding.
- `JettonTransfer` — TON Jetton transfer with auto-resolved wallet + memo.
- `WebhookServer` — ASP.NET Core minimal API that verifies inbound payout / transaction / invoice webhooks.

```bash
cd examples/Quickstart
MERCHANT_ID=... API_KEY=... TO_ADDRESS=0x... dotnet run
```

## FAQ — common crypto-processing tasks in C#

**How do I accept a crypto payment in .NET?**
Create a pay-in (invoice) with `client.PayIns.CreateAsync(...)`; the customer
gets a deposit address and you receive a signed webhook when it's paid. See
`PayInsService`.

**How do I send a crypto payout (withdrawal) in .NET?**
`client.Payouts.ExecuteAsync(...)` with `Coin` / `Network` / `Amount` /
`ToAddress`. Pass `OrderId` as an idempotency key and use
`client.WaitForPayoutAsync` to block until confirmed. Works for native coins
and ERC-20 / TRC-20 stablecoins (USDT, USDC).

**How do I send a mass / batch crypto payout?**
`client.Payouts.BatchExecuteAsync(...)` — up to 50 recipients in one signed
request, processed sequentially so the double-spend invariant holds.

**How do I call a smart contract (ERC-20, Uniswap) without encoding calldata?**
`client.Transactions.SignEvmCallAsync(...)`, or `Erc20TransferAsync` for a
token-transfer one-liner. Give it a Solidity signature plus args and the SDK
ABI-encodes the `data` field for you.

**How do I transfer USDT on TON (a Jetton) in .NET?**
`client.Transactions.JettonTransferAsync(...)` — pass the Jetton master,
recipient, and amount; the sender's Jetton wallet address and gas budget are
resolved automatically.

**How do I verify a Crypto Chief webhook signature?**
`WebhookVerifier.Verify(apiKey, body, signature)` or
`WebhookVerifier.VerifyAndDecode<T>(apiKey, body, signature)` for one-line
typed dispatch.

**Which blockchains does the crypto processing API support?**
Ethereum, BNB Smart Chain, Polygon, Tron, TON, Solana, Bitcoin, Litecoin,
Dogecoin, XRP, Avalanche, Arbitrum, Optimism and more — 25 chains in total.
The constants live in `CryptoChief.Processing.Chains.Chain`.

**How do I avoid floating-point rounding bugs with crypto amounts?**
Never use `double`. Convert with `Amount.HumanToBase` / `Amount.BaseToHuman`,
which are backed by `System.Numerics.BigInteger`.

## Documentation

**Full guides, tutorials, and recipes → [docs-sdk.crypto-chief.com/processing/dotnet](https://docs-sdk.crypto-chief.com/processing/dotnet)**

Reference material:

- REST / HTTP API: [docs-processing.crypto-chief.com](https://docs-processing.crypto-chief.com)
- NuGet package: [nuget.org/packages/CryptoChief.Processing](https://www.nuget.org/packages/CryptoChief.Processing)

## Contributing

PRs welcome. Please run `dotnet test` and `dotnet build -c Release` before
opening; new endpoints should come with a test that exercises the wire shape
through the in-memory `HttpMessageHandler` fixture in
`tests/CryptoChief.Processing.Tests/TransportTests.cs`.

## License

MIT — see [LICENSE](LICENSE).
