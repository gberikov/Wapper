# Wapper

A modern .NET client for the [WhatsApp Cloud API](https://developers.facebook.com/documentation/business-messaging/whatsapp/).

Targets `net8.0`, trim- and AOT-compatible, and throttles itself so you do not
have to think about Meta's rate limits.

> **Status: under development.** The public API is still moving. Versions stay on
> `0.x` until the messaging and webhook surface has been used in anger.

## Packages

| Package | What it is for |
|---|---|
| `Wapper` | The client: sending messages, media, templates, with built-in throttling and retries. |
| `Wapper.Abstractions` | Contracts and payload types, with no client and no ASP.NET Core dependency. |
| `Wapper.AspNetCore` | A mapped webhook endpoint with signature verification and typed event dispatch. |
| `Wapper.RateLimiting.Redis` | Shared limiter state, for when the application runs in more than one instance. |

A runnable application showing the pieces below together — sending, receiving, downloading
what arrives, and telling the errors apart — lives in [`samples/Wapper.Sample`](samples/Wapper.Sample).

**Contents:** [Getting started](#getting-started) · [Sending a template](#sending-a-template-message) ·
[Interactive messages](#interactive-messages) · [Media](#media) · [Receiving messages](#receiving-messages) ·
[Rate limiting](#why-the-rate-limiting-matters) · [Errors](#errors) · [Managing templates](#managing-templates) ·
[Phone numbers](#checking-the-phone-number) · [Registration](#getting-a-number-onto-the-cloud-api) ·
[Business profile](#the-business-profile) · [Flows](#flows) · [Analytics](#analytics) ·
[Several instances](#running-in-more-than-one-instance) · [Configuration](#configuration) ·
[Testing](#testing-code-that-uses-wapper) · [Not covered yet](#what-is-not-covered-yet)

## Getting started

```csharp
builder.Services.AddWhatsApp(builder.Configuration.GetSection("WhatsApp"));
```

```jsonc
{
  "WhatsApp": {
    "AccessToken": "...",
    "PhoneNumberId": "106540352242922"
  }
}
```

```csharp
public sealed class Orders(IWhatsAppClient whatsApp)
{
    public async Task ConfirmAsync(string customer, string orderId, CancellationToken ct)
    {
        await whatsApp.Messages.SendButtonsAsync(customer, new ButtonMessage
        {
            Body = $"Order {orderId} is ready. Shall we send it?",
            Buttons =
            [
                new ReplyButton { Id = $"ship:{orderId}", Title = "Send it" },
                new ReplyButton { Id = $"hold:{orderId}", Title = "Not yet" },
            ],
        }, cancellationToken: ct);
    }
}
```

Sending waits for a rate limit permit and retries what Meta says is worth retrying, so
`SendButtonsAsync` either returns a `SentMessage` or tells you why it could not.

Every send takes an optional `callbackData`: up to 512 characters of your own that Meta hands
back untouched on every delivery status the message produces. It is how a status is matched to
your own records without keeping a table of message ids.

```csharp
await whatsApp.Messages.SendTextAsync(customer, "on its way", callbackData: orderId, cancellationToken: ct);
```

### More than one phone number

Register each one by name and ask for it by name:

```csharp
builder.Services.AddWhatsApp("acme", o => { o.AccessToken = "..."; o.PhoneNumberId = "..."; });

await whatsApp.For("acme").Messages.SendTextAsync(customer, "hello", cancellationToken: ct);
```

When the tokens live in a database rather than in configuration — the usual case for a
SaaS — replace the credential lookup instead:

```csharp
builder.Services.AddSingleton<IWhatsAppCredentialsProvider, MyTenantCredentials>();
```

### What the client says about itself

Retries, and the holds it puts on a budget after Meta rejects a call, are logged through
`ILogger` under the `Wapper` category — retries at `Information`, holds at `Warning`. A send
that takes sixty seconds is not silent about why. Nothing logged carries a customer's number
in full.

## Sending a template message

The most common send, because it is the only one allowed once the 24-hour customer service
window has closed. The values go in by component, matched to the placeholders the template
declares:

```csharp
await whatsApp.Messages.SendTemplateAsync(
    customer,
    new TemplateMessage
    {
        Name = "order_confirmation",
        Language = "en_US",
        Components =
        [
            TemplateComponent.Body(
                TemplateParameter.FromText("Pablo", name: "first_name"),
                TemplateParameter.FromText("860198-230332", name: "order_number")),
            TemplateComponent.UrlButton(0, "860198-230332"),
        ],
    },
    callbackData: orderId,
    cancellationToken: ct);
```

Leave `name:` out for a template with numbered placeholders, and keep the parameters in the
order the placeholders appear. A media header takes the file the customer will actually see,
per message — `TemplateParameter.FromImage(MediaSource.FromId(mediaId))` — where the template
itself only ever held a sample. `FromMoney`, `FromDateTime`, `FromLocation` and
`CopyCodeButton` cover the other placeholder kinds.

A template that has not been approved, or whose parameters do not match, is rejected with
one of the `132xxx` codes, and none of them is retried: see [Errors](#errors).

## Interactive messages

Buttons are above. Three is the most WhatsApp allows; a list carries up to ten choices:

```csharp
await whatsApp.Messages.SendListAsync(customer, new ListMessage
{
    Body = "When suits you?",
    ButtonText = "Pick a slot",
    Sections =
    [
        new ListSection
        {
            Title = "Tomorrow",
            Rows =
            [
                new ListRow { Id = "slot:0900", Title = "09:00" },
                new ListRow { Id = "slot:1400", Title = "14:00", Description = "Afternoon" },
            ],
        },
    ],
}, cancellationToken: ct);
```

What the customer taps comes back as an `InteractiveReply` carrying the `Id` you set. A
`CallToActionMessage` renders a link as a button, and `SendLocationRequestAsync` asks the
customer to share where they are — the answer arrives as a `LocationMessage`, exactly as an
unprompted one would.

## Media

Upload first, then send by id. A link works too, but Meta fetches it at send time, so a slow
host fails the send and the result is cached for ten minutes:

```csharp
await using var file = File.OpenRead("invoice.pdf");
var mediaId = await whatsApp.Media.UploadAsync(file, "application/pdf", "invoice.pdf", ct);

await whatsApp.Messages.SendDocumentAsync(
    customer,
    MediaSource.FromId(mediaId),
    caption: "Your invoice",
    fileName: "invoice-2026-08.pdf",
    cancellationToken: ct);
```

The size limits Meta publishes — 5 MB for an image, 16 MB for audio and video, 100 MB for a
document — are checked before a byte goes up. An uploaded id lives for 30 days.

What a customer sends arrives as a `MediaMessage` carrying an id and nothing else. Fetch the
bytes promptly — an id from a webhook expires after seven days — and dispose the result, which
owns the connection:

```csharp
public sealed class Attachments(IWhatsAppClient whatsApp) : IWhatsAppEventHandler<MediaMessage>
{
    public async Task HandleAsync(MediaMessage message, CancellationToken ct)
    {
        await using var media = await whatsApp.Media.DownloadAsync(message.MediaId, ct);
        await using var target = File.Create(Path.Combine("inbox", message.MediaId));
        await media.Content.CopyToAsync(target, ct);
    }
}
```

A media download is the one call that leaves the Graph API host, so where it goes is checked
before the token is attached: see [the last section](#a-few-things-the-client-refuses-to-do).

## Receiving messages

```csharp
builder.Services.AddWhatsAppWebhookHandler<Replier, TextMessage>();

app.MapWhatsAppWebhook("/whatsapp");
```

```csharp
public sealed class Replier(IWhatsAppClient whatsApp) : IWhatsAppEventHandler<TextMessage>
{
    public async Task HandleAsync(TextMessage message, CancellationToken ct)
    {
        await whatsApp.Messages.MarkAsReadAsync(message.Id, showTyping: true, ct);
        await whatsApp.Messages.SendTextAsync(message.From, $"You said: {message.Text}", cancellationToken: ct);
    }
}
```

The endpoint answers the subscription handshake, verifies `X-Hub-Signature-256` against the
raw body, and hands each event to the handlers registered for it. Register a handler for
`IncomingMessage` or `WhatsAppEvent` to see everything of that shape.

A delivery carries many events, and Meta's only retry is to send the whole delivery again. So
a handler that throws does not stop the events behind it from being offered — but the delivery
is still failed, because swallowing it would lose the message for good. **Handlers have to be
idempotent**, which they have to be anyway: Meta repeats deliveries of its own accord.

Meta has more than twenty webhook fields and keeps adding to them. The ones this library has
typed events for arrive as those; anything else arrives as `UnknownEvent` carrying the raw
`value` object, so an account being offboarded or a customer opting out of marketing leaves a
trace rather than vanishing:

```csharp
builder.Services.AddWhatsAppWebhookHandler<Unhandled, UnknownEvent>();
```

The same event is where a field this library *does* know lands when it arrives shaped in a
way the library could not read, so a handler for it is the one place to learn that anything
is being discarded.

### Delivery statuses

A send only says Meta accepted the message. Whether it was delivered — and why it was not —
arrives afterwards as a `MessageStatusChanged`, with the `callbackData` the send attached:

```csharp
public sealed class Deliveries(IOrders orders) : IWhatsAppEventHandler<MessageStatusChanged>
{
    public Task HandleAsync(MessageStatusChanged status, CancellationToken ct) =>
        status.Status switch
        {
            MessageDeliveryStatus.Delivered => orders.MarkNotifiedAsync(status.CallbackData!, ct),
            MessageDeliveryStatus.Failed => orders.MarkUnreachableAsync(status.CallbackData!, status.Errors[0].Code, ct),
            _ => Task.CompletedTask,
        };
}
```

`ConversationExpiresAt` is set on the status that opens a conversation and says when the
24-hour customer service window closes. It is `null` when Meta did not say.

### Customers opting out

A `MarketingPreferenceChanged` with `MarketingPreference.Stop` means every marketing template
to that customer will be accepted by the API and then fail on the status webhook with
`131050`. It is the one webhook that changes what you are allowed to send, so record it and
stop:

```csharp
builder.Services.AddWhatsAppWebhookHandler<OptOuts, MarketingPreferenceChanged>();
```

### Without ASP.NET Core

`WhatsAppWebhookSignature.IsValid` and `WhatsAppWebhookParser.Parse` are in the `Wapper`
package and take the raw body, so an Azure Function or a queue consumer verifies and parses
the same way; only the endpoint and the handler dispatch are ASP.NET Core's.

Nothing arrives at all until the app is subscribed to the account — the step that is easy to
forget and impossible to debug, because the endpoint looks perfectly healthy without it:

```csharp
await whatsApp.Account.SubscribeAsync(ct);
```

Two settings are required to receive anything, both from the Meta app dashboard:

```jsonc
{
  "WhatsApp": {
    "AppSecret": "...",
    "WebhookVerifyToken": "..."
  }
}
```

Without `AppSecret` the endpoint refuses every delivery — it is public, and an unverified
one could come from anyone.

Meta expects a fast answer (median under 250 ms) and retries anything that fails for up to
seven days, so put long work on a queue rather than in a handler.

## Why the rate limiting matters

The Cloud API enforces four independent budgets, each with its own key and its
own error code. Getting any of them wrong means dropped messages, and a naive
retry loop actively lengthens the outage — Meta counts the rejected calls too.

| Budget | Limit | Keyed by | Error |
|---|---|---|---|
| Cloud API throughput | 80 or 1000 msg/s | business phone number | `130429` |
| Pair rate limit | 1 per 6 s, burst of 45 | sender and recipient | `131056` |
| WABA management calls | 200/h, 5000/h once a number is registered | app and WABA | `80007` |
| App platform limit | undisclosed (`200 × daily active users`) | app | `4` |

Wapper paces the first three proactively and backs off the fourth reactively,
steering by the `X-App-Usage` and `X-Business-Use-Case-Usage` response headers.
Backoff follows `4^X` seconds, the formula Meta publishes. The Cloud API does not
send a `Retry-After` header, so nothing here depends on one.

## Errors

Everything the library raises derives from `WhatsAppException`:

| Exception | When | Retry? |
|---|---|---|
| `WhatsAppApiException` | Meta answered with an error object. `Error.Code` is the only field worth branching on; the HTTP status is recorded for diagnostics and documented by Meta as unstable. | Already retried if it was worth it |
| `WhatsAppRateLimitedException` | A budget was exhausted — either this client refused to wait longer than `MaxWait`, or Meta kept rejecting until the retries ran out. `Scope` says which budget, `RetryAfter` how long. | After `RetryAfter` |
| `WhatsAppConfigurationException` | A missing token, an unknown tenant, a malformed setting. | No |
| `WhatsAppException` | A timeout, a connection that never reached Meta, a response with no body. | Not automatically — nothing came back, so the message may have been accepted |
| `ArgumentException` | Something Meta would have answered with a bare `100`, caught before the call: a fourth button, a 140-character About, a media id that is really a path. | No |

By the time an `WhatsAppApiException` reaches you the retryable ones — throughput, pair and
account limits, `is_transient` server errors — have been retried already. What is left is
worth branching on:

```csharp
try
{
    await whatsApp.Messages.SendTextAsync(customer, text, cancellationToken: ct);
}
catch (WhatsAppApiException exception) when (exception.Code == WhatsAppErrorCodes.ReEngagementRequired)
{
    // The 24-hour window has closed. Only a template gets through now.
    await whatsApp.Messages.SendTemplateAsync(customer, reminder, cancellationToken: ct);
}
catch (WhatsAppApiException exception) when (exception.Code == WhatsAppErrorCodes.UserOptedOut)
{
    await optOuts.RecordAsync(customer, ct);
}
catch (WhatsAppRateLimitedException exception)
{
    await queue.RetryAfterAsync(exception.RetryAfter, ct);
}
```

`WhatsAppErrorCodes` names the codes the library acts on and the ones an application most
often does. `Error.TraceId` is what to quote in a ticket to Meta.

## Managing templates

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

### One-time passcodes

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
[not covered yet](#what-is-not-covered-yet).

## Checking the phone number

```csharp
var number = await whatsApp.PhoneNumbers.GetAsync(cancellationToken: ct);

if (number.Status is not PhoneNumberStatus.Connected)
{
    logger.LogError("{Number} is {Status} and cannot send.", number.DisplayPhoneNumber, number.Status);
}
```

Worth doing at startup. A number that is `Flagged`, `RateLimited` or `Restricted` fails every
send with an error that reads like a transient one, and no amount of retrying will help.

Graph returns only a handful of fields by default and leaves out the ones worth reading a
number for, so Wapper always asks for the full set — `status` and `throughput` included.

`number.Throughput` is the messages-per-second ceiling: `Standard` is 80, `High` is 1000. Meta
raises it as volume grows, and announces it on the webhook. `RateLimits.MessagesPerSecond`
defaults to the conservative 80; raise it once the number reports `High`, or a high-throughput
number will be paced twelve times slower than it is allowed to send.

```csharp
builder.Services.AddWhatsAppWebhookHandler<NumberWatcher, PhoneNumberQualityChanged>();
```

`PhoneNumberQualityEvent.Flagged` is the one to act on: it means quality has dropped and the
daily messaging limit will fall if nothing changes. Display name decisions arrive as
`PhoneNumberNameChanged`.

Numbers cannot be created or deleted through the API — that is WhatsApp Manager, Meta Business
Suite or Embedded Signup. `SetTwoStepPinAsync` is the exception, and the only way to set a new
PIN without knowing the old one.

## Getting a number onto the Cloud API

Adding a number to the account is WhatsApp Manager's job. Everything after that is the API's,
and registering is *only* the API's — WhatsApp Manager cannot do it:

```csharp
await whatsApp.PhoneNumbers.RequestVerificationCodeAsync(
    VerificationCodeMethod.Sms,
    cancellationToken: ct);

// The message spells the code "123-830"; the hyphen is stripped for you.
await whatsApp.PhoneNumbers.VerifyAsync(code, cancellationToken: ct);

// Sets the two-step PIN if the number has none yet.
await whatsApp.PhoneNumbers.RegisterAsync("150954", cancellationToken: ct);
```

Registering and deregistering share an allowance of **ten attempts per number per 72 hours**,
and Meta counts the failed ones. The eleventh returns `133016` and locks the number out for the
rest of the window, so Wapper never retries any of these three calls automatically — a retry
would spend an attempt, or in the case of `RequestVerificationCodeAsync` send a second code and
silently invalidate the first.

Pass a two-letter country code to keep data at rest in one region:

```csharp
await whatsApp.PhoneNumbers.RegisterAsync("150954", "DE", cancellationToken: ct);
```

Local storage cannot be moved or switched off in place: deregister, then register again.

Register a second time after a display name change is approved — `PhoneNumberNameChanged` with
`DisplayNameDecision.Approved` is the signal. Re-registering before approval does nothing.

## The business profile

What a recipient sees when they tap the business's name in a thread:

```csharp
await whatsApp.BusinessProfile.UpdateAsync(
    new BusinessProfile
    {
        About = "Butterflies, and the things butterflies need.",
        Email = "hello@butterflies.example",
        Vertical = BusinessVertical.Retail,
        Websites = ["https://www.butterflies.example"],
    },
    cancellationToken: ct);
```

The update merges: a property left `null` keeps its current value, and an empty string clears
it. Every length limit — 139 characters of About, 512 of description, two websites — is checked
before the call, because Meta rejects all of them with the same bare `100` that never says which
field it objected to.

The picture is the odd one out. It is set by uploading a file to Meta and writing back the
handle, so it goes through the Resumable Upload API — which is addressed to the Meta app rather
than to the phone number, wants the token under the `OAuth` scheme instead of `Bearer`, and is
the only thing in this library that needs `WhatsApp:AppId`:

```csharp
await using var picture = File.OpenRead("logo.png");
await whatsApp.BusinessProfile.SetPictureAsync(picture, "image/png", cancellationToken: ct);
```

Reading is the usual story: Graph answers a bare read with the messaging product and nothing
else, so Wapper always names the fields. The profile comes back wrapped in a one-element array
even though a number has exactly one, and an empty array means nobody has filled it in.

## Flows

A Flow is a form the customer fills in inside WhatsApp. Its life runs one way — draft,
published, deprecated — and only a draft can be deleted:

```csharp
var created = await whatsApp.Flows.CreateAsync(
    new FlowDefinition
    {
        Name = "Book a table",
        Categories = [FlowCategory.AppointmentBooking],
        Json = flowJson,
    },
    ct);

if (created.ValidationErrors.Count > 0)
{
    // The Flow exists. It will simply never publish.
}
```

**Read `ValidationErrors`.** A create or a JSON upload that Meta cannot make sense of still
answers `200` with `"success": true` and a new id; the reasons it will never publish are in
that list. Code that only watches for exceptions will believe a broken Flow is fine until
publishing fails.

`ValidationErrors` covers the Flow JSON. Everything else — an endpoint that is not set, a Meta
app that is not connected, a WABA that is not in good standing — is in `Health`, which
`GetAsync` fetches and `ListAsync` deliberately does not:

```csharp
var flow = await whatsApp.Flows.GetAsync(created.Id, cancellationToken: ct);

foreach (var entity in flow.Health!.Entities.Where(e => e.CanSendMessage != MessagingAvailability.Available))
{
    logger.LogWarning("{Entity}: {Errors}", entity.EntityType, entity.Errors);
}
```

The Flow JSON goes up on its own, as multipart form data rather than as a body:

```csharp
var errors = await whatsApp.Flows.UpdateJsonAsync(created.Id, flowJson, ct);
await whatsApp.Flows.PublishAsync(created.Id, ct);
```

Editing a published Flow drops it back to draft until it is published again. A published Flow
cannot be deleted — `DeprecateAsync` is how it is retired, and there is no way back from that.

Sending one is a message like any other. The `FlowToken` is the only thing tying a submission
back to the customer and the thing they were doing, so generate one per send and store what it
means:

```csharp
await whatsApp.Messages.SendFlowAsync(
    customer,
    new FlowMessage
    {
        FlowId = created.Id,
        FlowToken = $"booking:{bookingId}",
        ButtonText = "Book a table",
        Body = "Pick a time that suits you.",
        Screen = "BOOK",
    },
    cancellationToken: ct);
```

What the customer fills in comes back on the webhook as a `FlowReply`, whose `ResponseJson` is
the document the Flow's own screens produced — its shape is yours, so it is handed over as
written:

```csharp
builder.Services.AddWhatsAppWebhookHandler<Bookings, FlowReply>();
```

A Flow that talks to an endpoint needs one more thing, and will not run without it: the public
key Meta encrypts that traffic with.

```csharp
await whatsApp.PhoneNumbers.SetEncryptionKeyAsync(publicKeyPem, cancellationToken: ct);
```

Status changes arrive on the webhook, and so do the monitoring alerts that precede them:

```csharp
builder.Services.AddWhatsAppWebhookHandler<FlowWatcher, FlowStatusChanged>();
builder.Services.AddWhatsAppWebhookHandler<FlowWatcher, FlowAlert>();
```

An unhealthy endpoint gets the Flow throttled to ten sends an hour, and then blocked. The
`FlowAlert` is the warning; the `FlowStatusChanged` is what happened anyway.

## Analytics

Four metrics, all against the WhatsApp Business Account:

```csharp
var conversations = await whatsApp.Analytics.GetConversationsAsync(
    new ConversationAnalyticsQuery
    {
        Start = DateTimeOffset.UtcNow.AddDays(-30),
        End = DateTimeOffset.UtcNow,
        Granularity = AnalyticsGranularity.Day,
        Dimensions = [ConversationDimension.ConversationCategory, ConversationDimension.Country],
    },
    ct);
```

`GetMessagingAsync` counts messages sent and delivered. `GetConversationsAsync` counts
conversations and what they cost. `GetPricingAsync` counts delivered messages by the rate they
were charged at — and it is the only place volume tiers are visible, since no webhook reports
them. `GetTemplatesAsync` is per template: sent, delivered, read, and buttons pressed.

Things worth knowing:

- **Meta spells the same granularity two ways.** `DAY` and `MONTH` for messaging, `DAILY` and
  `MONTHLY` for conversations and pricing, and each rejects the other's word for it. One
  `AnalyticsGranularity` here, translated per metric.
- **A filter left unset means "all of them".** That is Meta's own default, so nothing is sent.
- **`Dimensions` decides what comes back.** Without them the answer is one number per time
  slice; the breakdown fields on the data points are only filled in for dimensions that were
  asked for.
- **Cost is not reported for an account billed through a Solution Partner**, and asking for
  cost and nothing else makes such an account answer with an explanation instead of a figure.
- **Template clicks and cost are not numbers.** Clicks are counted per button, and cost arrives
  as several figures at once — amount spent, cost per delivery, cost per click.
- **Lookback is a year**, and 90 days for templates. Ten templates per read.

A backwards range is refused here rather than sent: Meta answers one with an empty result,
which reads exactly like a quiet week.

## Running in more than one instance

Meta counts per phone number on its side. Three replicas each pacing themselves against the
full allowance send three times the rate and have two thirds of it rejected, so the counters
have to be shared:

```csharp
builder.Services.AddWhatsApp(builder.Configuration.GetSection("WhatsApp"));
builder.Services.AddWhatsAppRedisRateLimiting("localhost:6379");
```

The budgets then live in Redis, and a penalty recorded by one instance holds the others
back too. If Redis becomes unreachable the limiter logs and falls back to pacing that
instance alone — Meta rejects the overshoot, which the retry path already handles, rather
than a Redis blip becoming a messaging outage. Set `FallBackToLocal = false` to make it
fatal instead.

## Configuration

Everything under the `WhatsApp` section, or on `WhatsAppOptions` in code. Only the first two
are needed to send; nothing is needed at all when an `IWhatsAppCredentialsProvider` supplies
the credentials.

| Setting | Default | What it is for |
|---|---|---|
| `AccessToken` | — | The bearer token. |
| `PhoneNumberId` | — | The business phone number messages are sent from. |
| `WhatsAppBusinessAccountId` | — | Templates, phone numbers, Flows, analytics and subscriptions. |
| `AppId` | — | Uploading a business profile picture or a template header sample. Also keys the app-level rate limit, so several tenants on one app back off together. |
| `AppSecret` | — | Verifying webhook signatures. Deliveries are refused without it. |
| `WebhookVerifyToken` | — | Answering the subscription handshake. |
| `GraphApiVersion` | `v26.0` | Moves forward — or stays put — without a new package. |
| `BaseAddress` | `https://graph.facebook.com/` | A proxy or a test server. Must be https unless loopback. |
| `Timeout` | 100 s | Per HTTP call. Does not include time spent waiting for a rate limit permit. |
| `MediaDownloadHosts` | Meta's CDNs | The hosts a media download may present the token to. Configuration adds to the defaults. |
| `RateLimits:Enabled` | `true` | Turn off only when something in front of the client paces already. |
| `RateLimits:MessagesPerSecond` | 80 | Raise to 1000 once the number reports `ThroughputLevel.High`. |
| `RateLimits:PairInterval` / `PairBurst` | 6 s / 45 | One message per recipient per six seconds, with a burst. |
| `RateLimits:BusinessAccountRequestsPerHour` | 200 | 5000 once the account has a registered number. |
| `RateLimits:MaxWait` | 30 s | The longest a call waits for a permit before `WhatsAppRateLimitedException`. |
| `RateLimits:MaxRetries` | 4 | Spread over Meta's `4^X` seconds: 1, 4, 16 and 64. |
| `RateLimits:UsagePercentThreshold` | 100 | Start holding back when `X-App-Usage` reports this much of the allowance spent. |

Every setting is validated at startup, so a `Timeout` of zero or a `GraphApiVersion` of
`latest` fails the host rather than the first send.

## Testing code that uses Wapper

Everything the client exposes is an interface — `IWhatsAppClient`, `IMessagesApi`,
`IMediaApi`, `ITemplatesApi` and the rest — and each resource group is registered in the
container on its own, so a class that only sends can take `IMessagesApi` and be handed a fake.
The event types are records with settable properties, built by hand in a test as easily as by
the parser.

Every delay in the library goes through `TimeProvider`, so a test that wants to see a retry
registers a `FakeTimeProvider` and winds it forward instead of waiting sixty seconds.

## What is not covered yet

The Cloud API is wide, and this release types the parts most applications reach for. Not yet:

- **Commerce:** catalogue, single- and multi-product messages, and the order webhook is
  typed but the catalogue itself is not managed.
- **Templates:** carousel and limited-time-offer components, catalogue and Flow buttons,
  archiving, and the template library.
- **Phone numbers:** QR codes and short links, conversational components (ice breakers,
  commands, the welcome message that `WelcomeRequest` answers), blocking users, and the
  Calling API.
- **The account:** reading the WABA itself (name, currency, review status), credit lines,
  and the partner-facing endpoints.
- **Webhooks:** `account_update`, `business_capability_update`, `security` and the rest
  arrive as `UnknownEvent` with their body, rather than as typed events.

There is no raw escape hatch for an endpoint the library does not model yet; open an issue
naming the one you need.

## A few things the client refuses to do

The access token is a bearer token: it is worth exactly as much to whoever gets hold of it.

- **`BaseAddress` has to be https.** Loopback is exempt, so a local proxy or a test server does
  not need a certificate.
- **A media download only ever goes to Meta's hosts.** A media URL is not a Graph API address —
  Meta returns a host of its own choosing, and the download needs the token attached — so
  `MediaInfo.Url` is checked against `MediaDownloadHosts` (matched whole or on a label
  boundary) before the token goes anywhere near it. Add to that list if Meta starts using a
  host this release does not know.
- **An identifier stays one path segment.** A media, template, Flow or phone number id a
  caller hands in is data, and one shaped like `../123/message_templates?name=x` would
  otherwise turn a media delete into a template delete under your own token. Anything with a
  slash, a dot segment or a query character is refused before the call.
- **`WhatsAppCredentials.ToString()` leaves the token out**, so logging one — or anything
  holding one — does not put a working token in the log.
- **Rate limit exceptions and log lines redact the recipient's number**, keeping your own.
  `Scope.Key` still has it in full for code that deliberately wants it, and the Redis limiter
  keys a conversation by a digest of it rather than the number itself.
- **A Flow's preview link is only fetched when asked for.** It needs no login and lasts thirty
  days: `Flows.GetAsync(id, includePreview: true, ...)`, or `GetPreviewAsync`.

## Licence

MIT. See [LICENSE](LICENSE).
