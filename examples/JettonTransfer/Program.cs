using CryptoChief.Processing;
using CryptoChief.Processing.Amounts;
using CryptoChief.Processing.Chains;
using CryptoChief.Processing.Services;

var client = new CryptoChiefClient(
    Environment.GetEnvironmentVariable("MERCHANT_ID")!,
    Environment.GetEnvironmentVariable("API_KEY")!);

var amount = Amount.HumanToBase("12.5", 6); // USDT decimals = 6

var signed = await client.Transactions.JettonTransferAsync(new JettonTransferRequest
{
    Network      = Chain.TonMainnet,
    FromAddress  = Environment.GetEnvironmentVariable("TON_WALLET")!,
    JettonMaster = "EQCxE6mUtQJKFnGfaROTKOt1lZbDiiX1kCixRv7Nw2Id_sDs",
    Recipient    = Environment.GetEnvironmentVariable("RECIPIENT")!,
    Amount       = amount,
    Memo         = "Order #4242",
    UrlCallback  = "https://your.app/webhooks/transaction",
});

Console.WriteLine($"Signed Jetton transfer. UUID: {signed.Uuid}");
Console.WriteLine($"Expires:                    {signed.ExpiresAt}");
