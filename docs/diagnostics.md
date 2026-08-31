# Logging, tracing and testing

## Logging and tracing

Retries, and the holds put on a budget after Meta rejects a call, are logged through `ILogger`
under the `Wapper` category — retries at `Information`, holds and usage-header warnings at
`Warning`. A send that takes sixty seconds says why.

Calls are also traced. One span covers the whole logical call, waits and retries included,
because that is what the caller experienced; the individual HTTP attempts appear underneath it
if the host instruments `HttpClient` as well.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(WhatsAppDiagnostics.ActivitySourceName)
        .AddHttpClientInstrumentation());
```

Spans are named for the operation — `messages.send_template`, `templates.create`,
`flows.publish` — never for an id, so they can be aggregated. They carry the tenant, the
business phone number, and on failure the Cloud API's error code; a retried call also carries
`wapper.attempts`. The recipient is deliberately absent: it is a customer's phone number, and
a trace backend is not a place to put one.

Nothing is emitted at all until something subscribes, so leaving this alone costs a null check
per call.

## Testing code that uses Wapper

Everything the client exposes is an interface — `IWhatsAppClient`, `IMessagesApi`,
`IMediaApi`, `ITemplatesApi` and the rest — and each resource group is registered in the
container on its own, so a class that only sends can take `IMessagesApi` and be handed a fake.
The event types are records with settable properties, built by hand in a test as easily as by
the parser.

Every delay in the library goes through `TimeProvider`, so a test that wants to see a retry
registers a `FakeTimeProvider` and winds it forward instead of waiting sixty seconds.

