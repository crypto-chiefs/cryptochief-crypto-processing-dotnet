using System.Text.Json;
using CryptoChief.Processing;
using CryptoChief.Processing.Errors;
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

// Each handler verifies the raw body bytes against X-CC-Timestamp, X-Webhook-Delivery and
// X-CC-Signature before parsing JSON. A redelivery carries the same X-Webhook-Delivery.
//
// Two ways a delivery can fail, answered apart: a signature the key does not confirm is 401,
// a verified body that does not decode into the event type is 400. Both are decided answers -
// letting either escape the handler would answer 5xx, which the platform retries.
app.MapPost("/webhooks/payout", async (HttpRequest req) =>
{
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    var body = ms.ToArray();
    try
    {
        var evt = WebhookVerifier.VerifyAndDecode<PayoutWebhookEvent>(apiKey, body, req.Headers);
        app.Logger.LogInformation(
            "payout {Uuid}: {Status} (event={Event}, tx={ToAddress}, confirmations={Confirmations}/{Required})",
            evt.Uuid, evt.Status, evt.Event, evt.ToAddress, evt.Confirmations, evt.RequiredConfirmations);
        return Results.Ok();
    }
    catch (WebhookVerificationException ex)
    {
        app.Logger.LogWarning("webhook {Delivery} refused: {Reason}",
            req.Headers[WebhookVerifier.DeliveryHeader].ToString(), ex.Message);
        return Results.Unauthorized();
    }
    catch (Exception ex) when (ex is JsonException or CryptoChiefException)
    {
        app.Logger.LogWarning("webhook {Delivery} verified but not decodable: {Reason}",
            req.Headers[WebhookVerifier.DeliveryHeader].ToString(), ex.Message);
        return Results.BadRequest();
    }
});

app.MapPost("/webhooks/transaction", async (HttpRequest req) =>
{
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    var body = ms.ToArray();
    try
    {
        var evt = WebhookVerifier.VerifyAndDecode<TransactionWebhookEvent>(apiKey, body, req.Headers);
        app.Logger.LogInformation(
            "tx {Uuid}: {Status} on {Network} (hash={TxHash}, confirmations={Confirmations}/{Required})",
            evt.Uuid, evt.Status, evt.Network, evt.TxHash, evt.Confirmations, evt.RequiredConfirmations);
        return Results.Ok();
    }
    catch (WebhookVerificationException ex)
    {
        app.Logger.LogWarning("webhook {Delivery} refused: {Reason}",
            req.Headers[WebhookVerifier.DeliveryHeader].ToString(), ex.Message);
        return Results.Unauthorized();
    }
    catch (Exception ex) when (ex is JsonException or CryptoChiefException)
    {
        app.Logger.LogWarning("webhook {Delivery} verified but not decodable: {Reason}",
            req.Headers[WebhookVerifier.DeliveryHeader].ToString(), ex.Message);
        return Results.BadRequest();
    }
});

app.MapPost("/webhooks/invoice", async (HttpRequest req) =>
{
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    var body = ms.ToArray();
    try
    {
        var evt = WebhookVerifier.VerifyAndDecode<PayInWebhookEvent>(apiKey, body, req.Headers);
        app.Logger.LogInformation(
            "invoice {Uuid}: {Status} (event={Event}, paid={Amount} {Coin})",
            evt.Uuid, evt.Status, evt.Event, evt.FactAmountCrypto ?? evt.AmountCrypto, evt.PaymentCoin);
        return Results.Ok();
    }
    catch (WebhookVerificationException ex)
    {
        app.Logger.LogWarning("webhook {Delivery} refused: {Reason}",
            req.Headers[WebhookVerifier.DeliveryHeader].ToString(), ex.Message);
        return Results.Unauthorized();
    }
    catch (Exception ex) when (ex is JsonException or CryptoChiefException)
    {
        app.Logger.LogWarning("webhook {Delivery} verified but not decodable: {Reason}",
            req.Headers[WebhookVerifier.DeliveryHeader].ToString(), ex.Message);
        return Results.BadRequest();
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
    try
    {
        var evt = WebhookVerifier.VerifyAndDecode<SweepWebhookEvent>(apiKey, body, req.Headers);
        app.Logger.LogInformation(
            "sweep {TaskId}: {Amount} {Asset} {From} -> {Master} (tx={TxHash}, confirmations={Confirmations}/{Required}, trigger={TypeWork}, fee_usd={Fee})",
            evt.TaskId, evt.AmountHuman, evt.AssetSymbol, evt.WalletAddress, evt.ToAddress,
            evt.SweepTxHash, evt.SweepConfirmations, evt.RequiredConfirmations, evt.TypeWork, evt.TotalFeeUsd);

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
    catch (WebhookVerificationException ex)
    {
        app.Logger.LogWarning("webhook {Delivery} refused: {Reason}",
            req.Headers[WebhookVerifier.DeliveryHeader].ToString(), ex.Message);
        return Results.Unauthorized();
    }
    catch (Exception ex) when (ex is JsonException or CryptoChiefException)
    {
        app.Logger.LogWarning("webhook {Delivery} verified but not decodable: {Reason}",
            req.Headers[WebhookVerifier.DeliveryHeader].ToString(), ex.Message);
        return Results.BadRequest();
    }
});

app.Run();
