using CryptoChief.Processing;
using CryptoChief.Processing.Webhooks;
using CryptoChief.Processing.Webhooks.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCryptoChief(o =>
{
    o.MerchantId = builder.Configuration["CryptoChief:MerchantId"]!;
    o.ApiKey     = builder.Configuration["CryptoChief:ApiKey"]!;
});

var app = builder.Build();

var apiKey = builder.Configuration["CryptoChief:ApiKey"]!;

app.MapPost("/webhooks/payout", async (HttpRequest req) =>
{
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    var body = ms.ToArray();
    var sig = req.Headers[WebhookVerifier.SignatureHeader].ToString();
    try
    {
        var evt = WebhookVerifier.VerifyAndDecode<PayoutWebhookEvent>(apiKey, body, sig);
        app.Logger.LogInformation(
            "payout {Uuid}: {Status} (event={Event}, tx={ToAddress})",
            evt.Uuid, evt.Status, evt.Event, evt.ToAddress);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "webhook rejected");
        return Results.Unauthorized();
    }
});

app.MapPost("/webhooks/transaction", async (HttpRequest req) =>
{
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    var body = ms.ToArray();
    var sig = req.Headers[WebhookVerifier.SignatureHeader].ToString();
    try
    {
        var evt = WebhookVerifier.VerifyAndDecode<TransactionWebhookEvent>(apiKey, body, sig);
        app.Logger.LogInformation(
            "tx {Uuid}: {Status} on {Network} (hash={TxHash})",
            evt.Uuid, evt.Status, evt.Network, evt.TxHash);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "webhook rejected");
        return Results.Unauthorized();
    }
});

app.MapPost("/webhooks/invoice", async (HttpRequest req) =>
{
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    var body = ms.ToArray();
    var sig = req.Headers[WebhookVerifier.SignatureHeader].ToString();
    try
    {
        var evt = WebhookVerifier.VerifyAndDecode<PayInWebhookEvent>(apiKey, body, sig);
        app.Logger.LogInformation(
            "invoice {Uuid}: {Status} (event={Event}, paid={Amount} {Coin})",
            evt.Uuid, evt.Status, evt.Event, evt.FactAmountCrypto ?? evt.AmountCrypto, evt.PaymentCoin);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "webhook rejected");
        return Results.Unauthorized();
    }
});

// Sweep - your money finishing its move into your own custody.
//
// A static_deposit.paid told you a customer paid. THIS says the funds have been
// swept off the deposit address and the sweep is confirmed on chain. Until it
// fires the balance still sits on the deposit wallet, so treasury reporting and
// "available to pay out" should key off this, not the deposit.
//
// Fires once per sweep, on confirmation only. Sweeps run on static deposit
// wallets and on per-order transit wallets alike; both arrive here.
app.MapPost("/webhooks/sweep", async (HttpRequest req) =>
{
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    var body = ms.ToArray();
    var sig = req.Headers[WebhookVerifier.SignatureHeader].ToString();
    try
    {
        var evt = WebhookVerifier.VerifyAndDecode<SweepWebhookEvent>(apiKey, body, sig);
        app.Logger.LogInformation(
            "sweep {TaskId}: {Amount} {Asset} {From} -> {Master} (tx={TxHash}, confirmations={Confirmations}, trigger={TypeWork}, fee_usd={Fee})",
            evt.TaskId, evt.AmountHuman, evt.AssetSymbol, evt.WalletAddress, evt.ToAddress,
            evt.SweepTxHash, evt.SweepConfirmations, evt.TypeWork, evt.TotalFeeUsd);

        // TaskId is the idempotency key: one sweep settles once. Seeing it
        // twice means a redelivery - acknowledge and stop.
        // if (await treasury.AlreadyRecordedAsync(evt.TaskId)) return Results.Ok();

        // The event only ever arrives confirmed, but apply your own finality
        // policy here if you have one - "confirmed" is not the same number on
        // every chain.
        // await treasury.RecordSettledAsync(evt.TaskId, evt.AssetSymbol, evt.AmountHuman, evt.SweepTxHash);
        // await ledger.MoveToAvailableAsync(CustomerFor(evt.WalletAddress), evt.AssetSymbol, evt.AmountHuman);
        // await costs.RecordAsync(evt.TaskId, evt.TotalFeeUsd);  // sweeps are not free

        return Results.Ok();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "webhook rejected");
        return Results.Unauthorized();
    }
});

app.Run();
