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

Meta has more than twenty webhook fields and keeps adding to them. The ones this library has
typed events for arrive as those; anything else arrives as `UnknownEvent` carrying the raw
`value` object, so a capability change or a security alert leaves a trace rather than
vanishing:

```csharp
builder.Services.AddWhatsAppWebhookHandler<Unhandled, UnknownEvent>();
```

The same event is where a field this library *does* know lands when it arrives shaped in a
way the library could not read — including one that bound cleanly and yielded no event at all.
A handler for it is the one place to learn that anything is being discarded.

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
            MessageDeliveryStatus.Failed => orders.MarkUnreachableAsync(status.CallbackData!, status.Errors[0].Code, ct),
            _ => Task.CompletedTask,
        };
}
```

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

Meta repeats deliveries of its own accord, and repeats every delivery a handler failed for up
to seven days. Handlers have to be idempotent; the cheapest way to make them so is to
recognise a repeat and drop it, and the body is already a perfectly good key:

```csharp
var key = WhatsAppWebhookParser.DeliveryKey(body);
```

The SHA-256 of the raw body, as 64 hex characters — which is a column with a unique index on
it. Insert; if it collides, this delivery has been handled, so answer `200` and stop. Two
genuinely different deliveries cannot collide, because the body carries the message id and the
timestamp.

Take it over the bytes exactly as they arrived, before anything re-serializes them: a
reindented body is a different key.

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
| `account_alerts`, `business_capability_update`, `message_template_components_update`, `template_category_update`, `security` | Optional | Useful to log; nothing here has to act on them. All arrive as `UnknownEvent`. |
| `partner_solutions`, `history`, `smb_app_state_sync`, `smb_message_echoes`, `automatic_events`, `payment_configuration_update` | Solution Partners only | Only meaningful to an approved partner onboarding customers, or with a regional payments product. |

The token also has to carry the right permissions, or a field can be subscribed and still stay
silent: `whatsapp_business_messaging` for `messages`, and `whatsapp_business_management` for
every other field.

