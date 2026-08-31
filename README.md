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

Not covered yet: archiving and unarchiving.

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

## Licence

MIT. See [LICENSE](LICENSE).
