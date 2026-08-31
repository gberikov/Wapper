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
