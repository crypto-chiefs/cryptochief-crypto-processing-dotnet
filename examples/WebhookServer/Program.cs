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

app.Run();
