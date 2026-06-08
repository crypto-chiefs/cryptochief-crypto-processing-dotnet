using CryptoChief.Processing;
using CryptoChief.Processing.Chains;
using CryptoChief.Processing.Models;
using CryptoChief.Processing.Polling;

var client = new CryptoChiefClient(
    Environment.GetEnvironmentVariable("MERCHANT_ID")
        ?? throw new InvalidOperationException("MERCHANT_ID not set"),
    Environment.GetEnvironmentVariable("API_KEY")
        ?? throw new InvalidOperationException("API_KEY not set"));

var mode = Environment.GetEnvironmentVariable("MODE") ?? "fiat";

PayIn invoice = mode switch
{
    "crypto" => await CreateCryptoInvoice(client),
    _        => await CreateFiatInvoice(client),
};

Console.WriteLine($"invoice uuid:   {invoice.Uuid}");
Console.WriteLine($"order id:       {invoice.OrderId}");
Console.WriteLine($"status:         {invoice.Status}");
Console.WriteLine($"payment link:   {invoice.PaymentLink ?? "-"}");
Console.WriteLine($"deposit addr:   {invoice.ToAddress ?? "-"}");
Console.WriteLine($"expires at:     {invoice.ExpiredAt ?? "-"}");

if (invoice.Status == PayInStatus.WaitingAssetSelect && invoice.Coins is { Count: > 0 })
{
    Console.WriteLine();
    Console.WriteLine("customer must pick one of:");
    foreach (var c in invoice.Coins)
        Console.WriteLine($"  - {c.Coin} on {c.Network}");

    var pick = invoice.Coins[0];
    Console.WriteLine($"\nSelecting {pick.Coin}/{pick.Network} on the customer's behalf for demo...");
    invoice = await client.PayIns.SelectAssetAsync(new SelectAssetRequest
    {
        Uuid    = invoice.Uuid,
        Coin    = pick.Coin,
        Network = pick.Network,
    });
    Console.WriteLine($"after select: status={invoice.Status} addr={invoice.ToAddress} amount={invoice.AmountCrypto}");
}

if (Environment.GetEnvironmentVariable("WAIT") == "1")
{
    Console.WriteLine("\nWaiting for payment...");
    var final = await client.WaitForPayInAsync(invoice.Uuid, new PollOptions
    {
        Interval = TimeSpan.FromSeconds(10),
        Timeout  = TimeSpan.FromMinutes(30),
    });
    Console.WriteLine($"final status: {final.Status}");
    Console.WriteLine($"paid amount:  {final.AmountCrypto} {final.PaymentCoin}");
}

static Task<PayIn> CreateFiatInvoice(CryptoChiefClient client) =>
    client.PayIns.CreateAsync(new CreatePayInRequest
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

static Task<PayIn> CreateCryptoInvoice(CryptoChiefClient client) =>
    client.PayIns.CreateAsync(new CreatePayInRequest
    {
        OrderId      = $"order-{Guid.NewGuid():N}",
        UserId       = "user-7",
        Mode         = PayInMode.Crypto,
        AmountCrypto = "10",
        Asset        = new Asset { Coin = "USDT", Network = Chain.TronMainnet },
        LifetimeSec  = 3600,
        UrlCallback  = "https://your.app/webhooks/invoice",
    });
