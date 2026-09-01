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

## What a code means

`Error.Code` is the only field Meta calls stable, and what each code *means* is Meta's
knowledge, not the caller's. `Classify()` answers it:

```csharp
catch (WhatsAppApiException exception)
{
    var failure = exception.Error.Classify();
}
```

| `Kind` | What Meta is saying | What it says about the next send |
|---|---|---|
| `Transient` | Something on Meta's side went wrong. | The same call may well succeed. Already retried by the client. |
| `RateLimited` | A budget is exhausted. `Budget` names which. | `CanRetry` separates the ones that clear in seconds from a spam restriction or a per-user marketing limit, which do not. |
| `RecipientUnreachable` | This recipient will not receive this message, ever. | Every other recipient is unaffected. |
| `AccountBlocked` | The account or its credentials cannot send at all. | Every recipient fails the same way until a human fixes it. |
| `RequestRejected` | The call will never be accepted as it stands. | The message, the template or the parameters have to change. |
| `Unknown` | A code this library has no rule for, and Meta did not mark it transient. | Log it with `TraceId` and look at it. Nothing is guessed. |

The distinction that earns its keep is the last two rows against the third. `131042` — the
business is not eligible to send, which is usually an unpaid invoice — is `AccountBlocked`,
not a bad number: every recipient in flight fails identically while the invoice is unpaid,
and marking them unreachable is how one billing problem eats a contact list.

It classifies a `WhatsAppError`, not an exception, because a message Meta accepts and then
fails to deliver reports its code on the webhook, where there is no exception to catch and
the same decision still has to be made:

```csharp
public sealed class Deliveries(IBroadcast broadcast) : IWhatsAppEventHandler<MessageStatusChanged>
{
    public async Task HandleAsync(MessageStatusChanged status, CancellationToken ct)
    {
        foreach (var error in status.Errors)
        {
            switch (error.Classify())
            {
                case { Kind: WhatsAppFailureKind.RecipientUnreachable }:
                    await broadcast.GiveUpOnAsync(status.RecipientId, error, ct);
                    break;

                case { Kind: WhatsAppFailureKind.AccountBlocked }:
                    // Not the recipient. Whether that means pausing the run or paging
                    // somebody is your decision, and only yours.
                    await broadcast.RaiseAsync(error, ct);
                    break;

                case { CanRetry: true }:
                    await broadcast.RequeueAsync(status.MessageId, ct);
                    break;
            }
        }
    }
}
```

The outcomes are named for what the Cloud API does, not for any one application's funnel.
Whether an unreachable recipient should be dropped from a list, a run paused or an operator
woken is the caller's decision — and it is the client's own retry table, so what a caller
reads and what the client did cannot drift apart.

Pure and offline, so it can be run over a list of failed recipients or in a test.

