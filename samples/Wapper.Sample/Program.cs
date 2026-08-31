using Wapper;
using Wapper.AspNetCore;
using Wapper.Media;
using Wapper.Messages;
using Wapper.Raw;
using Wapper.Sample;
using Wapper.Webhooks;

// A small backend that sends on request and answers what comes back. Fill in appsettings.json
// (or the WhatsApp__* environment variables), expose the app on a public https URL, point the
// webhook in the Meta app dashboard at /whatsapp, and subscribe once with POST /subscribe.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWhatsApp(builder.Configuration.GetSection(WhatsAppOptions.SectionName));

// One handler per event type. A handler for a base type — IncomingMessage, WhatsAppEvent —
// sees everything of that shape as well.
builder.Services.AddWhatsAppWebhookHandler<Conversation, TextMessage>();
builder.Services.AddWhatsAppWebhookHandler<Conversation, InteractiveReply>();
builder.Services.AddWhatsAppWebhookHandler<Conversation, LocationMessage>();
builder.Services.AddWhatsAppWebhookHandler<Attachments, MediaMessage>();
builder.Services.AddWhatsAppWebhookHandler<Deliveries, MessageStatusChanged>();
builder.Services.AddWhatsAppWebhookHandler<OptOuts, MarketingPreferenceChanged>();
builder.Services.AddWhatsAppWebhookHandler<Audit, UnknownEvent>();

var app = builder.Build();

// The verification handshake on GET, deliveries on POST. Signatures are checked against the
// raw body before anything is parsed.
app.MapWhatsAppWebhook("/whatsapp");

// Nothing arrives until the app is subscribed to the account. Once is enough.
app.MapPost("/subscribe", async (IWhatsAppClient whatsApp, CancellationToken ct) =>
{
    await whatsApp.Account.SubscribeAsync(ct);
    return Results.NoContent();
});

// Sending. Every send returns the message id to match delivery statuses against, and takes
// an optional callbackData that comes back on those statuses untouched.

app.MapPost("/send/text", (SendText request, IWhatsAppClient whatsApp, CancellationToken ct) =>
    Outbound.SendAsync(
        () => whatsApp.Messages.SendTextAsync(request.To, request.Text, cancellationToken: ct)));

app.MapPost("/send/template", (SendOrderConfirmation request, IWhatsAppClient whatsApp, CancellationToken ct) =>
    Outbound.SendAsync(() => whatsApp.Messages.SendTemplateAsync(
        request.To,
        new TemplateMessage
        {
            // An approved template with two named placeholders: {{first_name}} and
            // {{order_number}}. See "Managing templates" in the README for creating one.
            Name = "order_confirmation",
            Language = "en_US",
            Components =
            [
                TemplateComponent.Body(
                    TemplateParameter.FromText(request.FirstName, name: "first_name"),
                    TemplateParameter.FromText(request.OrderNumber, name: "order_number")),
            ],
        },
        callbackData: request.OrderNumber,
        cancellationToken: ct)));

app.MapPost("/send/buttons", (SendChoice request, IWhatsAppClient whatsApp, CancellationToken ct) =>
    Outbound.SendAsync(() => whatsApp.Messages.SendButtonsAsync(
        request.To,
        new ButtonMessage
        {
            Body = request.Question,
            Buttons = [.. request.Choices.Select(choice => new ReplyButton { Id = choice, Title = choice })],
        },
        cancellationToken: ct)));

app.MapPost("/send/document", async (SendDocument request, IWhatsAppClient whatsApp, CancellationToken ct) =>
{
    // Upload first, then send by id. The size limit for the type is checked before a byte
    // goes up, and the id stays valid for 30 days.
    await using var file = File.OpenRead(request.Path);
    var mediaId = await whatsApp.Media.UploadAsync(file, "application/pdf", Path.GetFileName(request.Path), ct);

    return await Outbound.SendAsync(() => whatsApp.Messages.SendDocumentAsync(
        request.To,
        MediaSource.FromId(mediaId),
        caption: request.Caption,
        fileName: Path.GetFileName(request.Path),
        cancellationToken: ct));
});

app.MapPost("/send/location-request", (SendText request, IWhatsAppClient whatsApp, CancellationToken ct) =>
    Outbound.SendAsync(
        () => whatsApp.Messages.SendLocationRequestAsync(request.To, request.Text, cancellationToken: ct)));

// Reading the WhatsApp Business Account itself is one of the things this library has no typed
// API for. Raw sends it anyway, with the same credentials, pacing, retries and exceptions --
// which is the point: a missing endpoint should not mean a second HttpClient beside this one,
// pacing against nothing.
app.MapGet("/account", async (IWhatsAppClient whatsApp, CancellationToken ct) =>
{
    var account = await whatsApp.Raw.SendAsync(
        new RawRequest
        {
            Method = HttpMethod.Get,
            // Filled in from the tenant's credentials, so this works for every tenant.
            Path = "{waba_id}?fields=name,currency,timezone_id,business_verification_status",
            Kind = RawCallKind.Management,
            Operation = "account.get",
        },
        ct);

    return Results.Json(account);
});

app.Run();
