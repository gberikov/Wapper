# Flows

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

