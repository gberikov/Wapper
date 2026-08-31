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
`value` object, so an account being offboarded or a customer opting out of marketing leaves a
trace rather than vanishing:

```csharp
builder.Services.AddWhatsAppWebhookHandler<Unhandled, UnknownEvent>();
```

The same event is where a field this library *does* know lands when it arrives shaped in a
way the library could not read, so a handler for it is the one place to learn that anything
is being discarded.

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

## Without ASP.NET Core

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
| `account_update` | Recommended | Policy violations, offboarding, deletion. Arrives as `UnknownEvent`, which is still better than finding out when sends start failing. |
| `account_alerts`, `business_capability_update`, `message_template_components_update`, `template_category_update`, `security` | Optional | Useful to log; nothing here has to act on them. All arrive as `UnknownEvent`. |
| `partner_solutions`, `history`, `smb_app_state_sync`, `smb_message_echoes`, `automatic_events`, `payment_configuration_update` | Solution Partners only | Only meaningful to an approved partner onboarding customers, or with a regional payments product. |

The token also has to carry the right permissions, or a field can be subscribed and still stay
silent: `whatsapp_business_messaging` for `messages`, and `whatsapp_business_management` for
every other field.

