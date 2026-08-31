# Managing templates

A template is the only message allowed outside the 24-hour customer service window, and it
has to be approved before it can be sent.

```csharp
var created = await whatsApp.Templates.CreateAsync(new Template
{
    Name = "order_confirmation",
    Language = "en_US",
    Category = TemplateCategory.Utility,
    ParameterFormat = TemplateParameterFormat.Named,
    Body = new TemplateBody
    {
        Text = "Thank you, {{first_name}}! Your order number is {{order_number}}.",
        Examples =
        [
            new TemplateParameterExample("Pablo", "first_name"),
            new TemplateParameterExample("860198-230332", "order_number"),
        ],
    },
    Buttons = [TemplateButton.Link("Track order", "https://example.com/orders/{{1}}", "1234")],
}, cancellationToken: ct);
```

`created.Status` is `Pending`: review takes up to a day. The outcome arrives on the webhook,
so handle it rather than polling.

```csharp
builder.Services.AddWhatsAppWebhookHandler<TemplateWatcher, TemplateStatusChanged>();
```

On a rejection, `Reason` is a category — `InvalidFormat` — and says nothing about what to
change. `Details` and `Recommendation` carry the review's own words: *"Your template has
parameters placed next to each other"*, *"Separate parameters with descriptive text"*. Put
both in front of whoever has to fix the template.

Prefer named parameters over numbered ones. Numbered placeholders are matched by position, so
inserting one renumbers everything after it — in the template *and* in every call site that
sends it.

Managing templates needs `WhatsAppBusinessAccountId` in configuration; sending messages does
not. These calls spend the account's management allowance (200 an hour, 5000 once a number is
registered), which the client paces separately from message throughput.

A template with an image, video or document header is reviewed against a sample, and the
sample goes up through a different endpoint than the media a message carries. It hands back a
*handle*, not a media id, and needs `WhatsApp:AppId`:

```csharp
await using var sample = File.OpenRead("hero.png");
var handle = await whatsApp.Templates.UploadHeaderSampleAsync(sample, "image/png", ct);

await whatsApp.Templates.CreateAsync(new Template
{
    Name = "seasonal_offer",
    Language = "en_US",
    Category = TemplateCategory.Marketing,
    Header = TemplateHeader.FromImage(handle),
    Body = new TemplateBody { Text = "Our summer range is in." },
}, cancellationToken: ct);
```

Reading a template back — `GetAsync`, or `ListAsync` — asks for every field, including the
quality score and the reason review turned it down, which Graph leaves out unless asked.

## One-time passcodes

An authentication template carries no text of its own — Meta writes the body and the footer in
every language it supports, which is the point of the category:

```csharp
await whatsApp.Templates.CreateAsync(
    Template.Authentication(
        "verification_code",
        "en_US",
        TemplateButton.AutofillOneTimePassword(
            [new TemplateApplication("com.example.app", "K2h6uSdG3xY")],
            autofillText: "Autofill"),
        codeExpirationMinutes: 10),
    cancellationToken: ct);
```

`AutofillOneTimePassword` fills the code straight into your Android app and falls back to
copying everywhere else, so it is always at least as good as `CopyOneTimePassword`. Get the
signature hash wrong and the code silently never arrives — Meta matches on it deliberately, so
a passcode cannot be autofilled into an impostor app.

Carousel and limited-time-offer templates, and catalogue buttons, are
[not covered yet](raw.md#what-is-not-covered-yet).

