# Receiving messages

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

## What arrives when Meta sends something new

Meta adds message types, interactive reply types and webhook fields without warning. Nothing
this library cannot type is dropped; it lands in one of three places, each carrying what it
came with:

| What was new | Arrives as | Carries |
|---|---|---|
| A message `type` this library has no event for, or an `interactive.type` it does not know | `UnknownMessage` | The envelope like any message — `Id`, `From`, `Timestamp`, reply context — plus `Type`, `InteractiveType` and the message object in `Json`. |
| A `messages` or `statuses` item Meta has reshaped so it cannot be read | `UnknownEvent` with `Field = "messages"` | That item alone in `Json`, with `PhoneNumberId`. The items beside it are delivered as the events they are. |
| A webhook field with no typed event, or a known field shaped so it cannot be read at all | `UnknownEvent` | The whole `value` object in `Json`, under `Field`. |

`UnsupportedMessage` is something else: the documented `unsupported` type, meaning WhatsApp
itself could not carry what the customer sent, with Meta's own errors saying what. A media
message whose file Meta could not fetch arrives there too, under its own type.

To notice new shapes without logging customers' messages, handle the two fallbacks and log
only their metadata:

```csharp
builder.Services.AddWhatsAppWebhookHandler<NewShapes, UnknownMessage>();
builder.Services.AddWhatsAppWebhookHandler<NewFields, UnknownEvent>();
```

```csharp
public sealed class NewShapes(ILogger<NewShapes> log) : IWhatsAppEventHandler<UnknownMessage>
{
    public Task HandleAsync(UnknownMessage message, CancellationToken ct)
    {
        // The type and the subtype are Meta's vocabulary, safe to log. The body is the
        // customer's, so it goes to a store with a retention policy, not to a log line.
        log.LogWarning("Unknown message type {Type}/{Subtype} on {Number}",
            message.Type, message.InteractiveType, message.PhoneNumberId);
        return Task.CompletedTask;
    }
}
```

A delivery that fails signature verification never reaches any of this; one that verifies
but cannot be parsed at all — no `entry`, not JSON — goes to
[`IWhatsAppUnparsedWebhookHandler`](#deliveries-the-parser-refuses).

### Reading a Flow's answers

A submitted Flow arrives as `FlowReply`, and its answers in `ResponseJson` are shaped by the
Flow's own screens — the library cannot know them, but you do. Declare the shape and read it
without reflection, so it works trimmed and under Native AOT:

```csharp
public sealed record BookingAnswers(
    [property: JsonPropertyName("flow_token")] string FlowToken,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("guests")] int Guests);

[JsonSerializable(typeof(BookingAnswers))]
internal sealed partial class BookingJsonContext : JsonSerializerContext;
```

```csharp
public sealed class Bookings(IBookingService bookings) : IWhatsAppEventHandler<FlowReply>
{
    public async Task HandleAsync(FlowReply reply, CancellationToken ct)
    {
        var answers = reply.ReadResponse(BookingJsonContext.Default.BookingAnswers);
        if (answers is null) return;

        // The flow_token is what you sent the Flow with, and how the answers find the
        // customer and the thing they were doing.
        await bookings.ConfirmAsync(answers.FlowToken, answers.Date, answers.Guests, ct);
    }
}
```

## Deliveries the parser refuses

A delivery whose signature verifies but which the parser cannot read at all is acknowledged:
it did come from Meta, and answering with an error would have it redelivered for seven days
and refused every time. Acknowledging is also how it is lost for good — unless something
keeps the body first. Register that something:

```csharp
builder.Services.AddSingleton<IWhatsAppUnparsedWebhookHandler, UnparsedInbox>();
```

```csharp
public sealed class UnparsedInbox(IDeadLetters deadLetters) : IWhatsAppUnparsedWebhookHandler
{
    public Task HandleAsync(WhatsAppUnparsedWebhook delivery, CancellationToken ct) =>
        // Copy the body: the buffer belongs to the request. Store the error alongside, so a
        // later version of the parser can be pointed at the ones it now understands.
        deadLetters.WriteAsync(delivery.Tenant, delivery.Body.ToArray(), delivery.Error.Message, ct);
}
```

Once the handler returns, the delivery is acknowledged. If it throws — the store is down —
the delivery is failed so Meta sends it again, which is the one retry there is. Do not throw
to retry the parse: it fails the same way every time. Without a handler the endpoint logs the
error, never the body, and acknowledges.

Give the store a retention policy. The bodies are customers' messages, and seven days is what
Meta itself keeps them for.

## Delivery statuses

A send only says Meta accepted the message. Whether it was delivered — and why it was not —
arrives afterwards as a `MessageStatusChanged`, with the `callbackData` the send attached:

```csharp
public sealed class Deliveries(IOrders orders) : IWhatsAppEventHandler<MessageStatusChanged>
{
    public Task HandleAsync(MessageStatusChanged status, CancellationToken ct) =>
        status.Status switch
        {
            MessageDeliveryStatus.Delivered => orders.MarkNotifiedAsync(status.CallbackData!, ct),
            MessageDeliveryStatus.Failed => FailedAsync(status, ct),
            _ => Task.CompletedTask,
        };

    private Task FailedAsync(MessageStatusChanged status, CancellationToken ct) =>
        status.Errors.FirstOrDefault()?.Classify() switch
        {
            // The number is not on WhatsApp, or the customer opted out.
            { Kind: WhatsAppFailureKind.RecipientUnreachable } =>
                orders.MarkUnreachableAsync(status.CallbackData!, ct),

            // Everything else — an unpaid invoice, a locked account, a template that does not
            // fit its values — says nothing whatever about this customer.
            _ => orders.MarkNotSentAsync(status.CallbackData!, ct),
        };
}
```

A failed status is not a bad number. The code that came with it says which of the two it is,
and `Classify()` is what reads it: see [What a code means](errors.md#what-a-code-means). There
is no exception to catch here, which is exactly why it works on the error object.

`ConversationExpiresAt` is set on the status that opens a conversation and says when the
24-hour customer service window closes. It is `null` when Meta did not say.

## Customers opting out

A `MarketingPreferenceChanged` with `MarketingPreference.Stop` means every marketing template
to that customer will be accepted by the API and then fail on the status webhook with
`131050`. It is the one webhook that changes what you are allowed to send, so record it and
stop:

```csharp
builder.Services.AddWhatsAppWebhookHandler<OptOuts, MarketingPreferenceChanged>();
```

## Trouble with the account itself

`AccountUpdated` is where a policy violation, a restriction, a scheduled disablement or a
deletion arrives. There is no other notice: the next sign is sends failing.

```csharp
builder.Services.AddWhatsAppWebhookHandler<Compliance, AccountUpdated>();
```

`Event` is the one to branch on — `AccountViolation`, `AccountRestriction`, `DisabledUpdate`,
`AccountDeleted` — with `ViolationType`, `Restrictions` and `BanState` carrying the detail.
Meta sends about twenty events on this field and half of them only mean something to a
Solution Partner; those arrive with `Event` as `Unknown`, `RawEvent` naming them and `Json`
holding the body they came in.

## One endpoint for every tenant

`MapWhatsAppWebhook` checks every delivery against one tenant's app secret. That is right when
the numbers share a Meta app, because then they share the secret. A host whose customers are
onboarded through *different* apps has a secret each, and no single one of them can verify
everything arriving on the endpoint. Map this instead:

```csharp
app.MapWhatsAppWebhookForTenants("/whatsapp");
```

Each delivery is then matched to a tenant and checked against **that tenant's** `AppSecret`.
The default match is against the `PhoneNumberId` and `WhatsAppBusinessAccountId` in
configuration; a host whose tenants live in a database registers its own resolver, the same
way it replaces `IWhatsAppCredentialsProvider`:

```csharp
builder.Services.AddSingleton<IWhatsAppWebhookTenantResolver, TenantsFromDatabase>();
```

```csharp
public sealed class TenantsFromDatabase(IAccounts accounts) : IWhatsAppWebhookTenantResolver
{
    public async ValueTask<string?> ResolveAsync(WhatsAppWebhookOrigin origin, CancellationToken ct) =>
        // Called once per delivery, before anything in it is trusted. Cache it.
        await accounts.FindTenantAsync(origin.PhoneNumberId, origin.BusinessAccountId, ct);
}
```

Three things this mode has to decide, and does:

- **The body is read before it is verified.** There is no other order available: the signature
  cannot be checked without a secret, and nothing but the body says which secret. What makes
  it safe is what that reading is allowed to do — it picks a secret, and the signature still
  has to verify against it, so a forged `phone_number_id` only ever buys the sender a refusal.
  The read itself is a forward-only scan for two property names over a body already capped at
  a megabyte; it builds no object graph and never runs the parser.
- **A delivery covering tenants on different apps is refused.** Meta signs a delivery once,
  with one app's secret, so this is not a delivery it could have sent. Verifying it against
  the first tenant's secret would let the rest in on a signature that says nothing about them.
  Tenants that share a secret are fine, however many numbers the delivery names.
- **A `phone_number_id` that matches no tenant is refused**, with a log line naming it. An
  account-level delivery — a template verdict, an account update — carries no number at all,
  and falls back to the account on the entry.

The subscription handshake is still checked against the default tenant's `WebhookVerifyToken`:
a `GET` names no number, so there is nothing to resolve by. That token is one you choose
rather than one Meta issues, so sharing it costs nothing.

## Deliveries you have already seen

Meta repeats deliveries of its own accord, repeats every delivery a handler failed for up to
seven days, and sends the repeats to every app subscribed to the account. Handlers therefore
run more than once for the same event, and the question is what "once" should mean for
yours.

**Dropping a repeat is not the same as having handled it.** A table of delivery keys with a
unique index, where a collision means "seen, answer `200`", loses events: the key is written,
the handler throws, Meta retries, the retry collides and is dropped. A key only says the
delivery was *received*; whether its effects happened is a second fact, and it has to be kept
separately.

The shape that holds up is an inbox:

1. Verify the signature. Nothing below runs for a delivery that fails it.
2. Insert a row per **event** — not per delivery — with the event's key and a status of
   `received`, inside one transaction with whatever the event itself changes if that change
   is in the same database. A collision on the key means this event is already in the inbox:
   leave the row alone.
3. Answer `200`. The delivery is now yours; Meta's retry is no longer what keeps it.
4. Process the rows in `received`, marking each `done` when its effects have happened, and
   retry the ones that fail on your own schedule.

Step 2 is what a handler does; step 4 is a worker. A handler that does both — writes the row
and does the work — has to be sure that the work is idempotent, because the worker will see
the row again if the handler dies between the two.

The key is per event because a delivery is a batch, and one delivery can carry a message and
three statuses. For a message the key is its `Id`. For a status it is the message id **and**
the status: `sent`, `delivered` and `read` for one message arrive as three events, and a key
of the message id alone would keep the first and drop the other two.

```csharp
public sealed class Inbox(AppDbContext db) : IWhatsAppEventHandler<WhatsAppEvent>
{
    public async Task HandleAsync(WhatsAppEvent evt, CancellationToken ct)
    {
        var key = evt switch
        {
            IncomingMessage m => $"message:{m.Id}",
            MessageStatusChanged s => $"status:{s.MessageId}:{s.RawStatus}",
            _ => null, // account-level events: key on what they name, or let them repeat
        };
        if (key is null) return;

        db.Inbox.Add(new InboxRow { Key = key, ReceivedAt = DateTimeOffset.UtcNow, Status = "received" });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            // Already in the inbox, whether or not it has been processed yet. Nothing to do.
        }
    }
}
```

Two deliveries carrying the same event can arrive at the same moment — Meta sends retries to
every subscribed app, and a load balancer sends them to different replicas. The unique index
is what serialises them: one insert wins, the other collides. Do not check-then-insert.

Whatever the worker does for an event has to be safe to do twice — a state change conditioned
on the current state, an outbox row for the email rather than the email itself — because a
worker can die after doing it and before marking the row `done`. This is at-least-once with
idempotent effects; nothing here makes it exactly-once, and nothing can.

`WhatsAppWebhookParser.DeliveryKey(body)` is still there: the SHA-256 of the raw body, for
the case where the delivery as a whole is the unit — an audit log of what Meta sent, say.
It is per delivery, not per event, and taken over the bytes as they arrived: a reindented
body is a different key, and two deliveries carrying the same message are two keys.

### What stays where, and for how long

- **The request body** lives in the endpoint's buffer for the duration of the request; copy
  it to keep it.
- **A media id** on a `MediaMessage` is good for seven days. Download promptly, and download
  under your own token with a timeout: the library's timeout covers the round trip to the
  headers, and reading the stream is yours.
- **Redis, when the shared limiter is registered,** can go away. By default sends then pace
  per instance and Meta rejects the overshoot; set `FallBackToLocal = false` to fail sends
  instead. See [Running in more than one instance](redis.md).

### Three secrets, none of them in the repository

| Setting | Issued by | Used for |
|---|---|---|
| `AppSecret` | Meta, on the app dashboard | Verifying `X-Hub-Signature-256` on every delivery. Shared by every number on the app. |
| `WebhookVerifyToken` | You; any string | The one-time subscription handshake on `GET`. |
| `AccessToken` | Meta, a system user token | Every call the client makes. Needs `whatsapp_business_messaging` for messages and `whatsapp_business_management` for everything else. |

Put them in user secrets or environment variables, never in `appsettings.json` — see
[Where the tokens go](configuration.md#where-the-tokens-go).

## Without ASP.NET Core

`WhatsAppWebhookSignature.IsValid`, `WhatsAppWebhookParser.Parse`, `.ReadOrigins` and
`.DeliveryKey` are all in the `Wapper` package and take the raw body, so an Azure Function or
a queue consumer verifies, routes, deduplicates and parses the same way; only the endpoint and
the handler dispatch are ASP.NET Core's.

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

## Which fields to subscribe to

Subscribing to the app is only half of it: each **webhook field** is switched on separately in
the Meta app dashboard, under *WhatsApp → Configuration*. Subscribe to too few and the events
never arrive; there is nothing in the API that says so, and the endpoint looks healthy either
way.

| Field | | Why |
|---|---|---|
| `messages` | **Required** | The only one that carries anything you send or receive: incoming messages, *and* every delivery status, *and* the out-of-band errors. Without it the endpoint receives nothing at all. |
| `user_preferences` | **Strongly recommended** | Marketing opt-outs. Without it you keep sending marketing templates that are accepted and never delivered, which costs sends and drags the number's quality down. → `MarketingPreferenceChanged` |
| `message_template_status_update` | If you manage templates | The outcome of review. Creating a template only ever returns `Pending`; approval or rejection arrives here, up to a day later, and nowhere else. → `TemplateStatusChanged` |
| `phone_number_quality_update` | Recommended | Quality drops, messaging-limit changes, and the throughput upgrade that lets you raise `MessagesPerSecond` from 80 to 1000. → `PhoneNumberQualityChanged` |
| `message_template_quality_update` | Recommended | The warning before a template is paused. → `TemplateQualityChanged` |
| `flows` | If you use Flows | Status changes and the monitoring alerts that precede them. → `FlowStatusChanged`, `FlowAlert` |
| `phone_number_name_update` | If display names change | An approved change is the cue to register the number again — without that the new name never takes effect. → `PhoneNumberNameChanged` |
| `account_update` | Recommended | Policy violations, restrictions, offboarding, deletion. The only place any of that is reported; everything else surfaces as sends failing for reasons that read like a bug. → `AccountUpdated` |
| `template_category_update` | If you manage templates | A category change changes the price of every message sent with the template, and the advance notice arrives 24 hours before it. → `TemplateCategoryChanged` |
| `message_template_components_update` | If you manage templates | A template edited in WhatsApp Manager by somebody else is otherwise invisible until a send fails validation. → `TemplateComponentsChanged` |
| `security` | Recommended | A two-step verification PIN reset nobody on your side asked for. → `PhoneNumberSecurityChanged` |
| `account_alerts` | Optional | Messaging limit increases denied or deferred, Official Business Account decisions, a lost profile picture. → `AccountAlert` |
| `business_capability_update` | Optional | The messaging limit and phone number limits as numbers. → `BusinessCapabilityChanged` |
| `account_review_update`, `calls`, `automatic_events` | Optional | No typed event yet; all arrive as `UnknownEvent`. |
| `partner_solutions`, `history`, `smb_app_state_sync`, `smb_message_echoes`, `automatic_events`, `payment_configuration_update` | Solution Partners only | Only meaningful to an approved partner onboarding customers, or with a regional payments product. |

The token also has to carry the right permissions, or a field can be subscribed and still stay
silent: `whatsapp_business_messaging` for `messages`, and `whatsapp_business_management` for
every other field.

