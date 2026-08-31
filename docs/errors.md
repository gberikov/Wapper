# Errors

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

