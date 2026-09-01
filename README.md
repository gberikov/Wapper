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

A runnable application showing the pieces together — sending, receiving, downloading what
arrives, and telling the errors apart — lives in [`samples/Wapper.Sample`](https://github.com/gberikov/Wapper/tree/master/samples/Wapper.Sample).

## Documentation

| Page | What is in it |
|---|---|
| [Configuration](https://github.com/gberikov/Wapper/blob/master/docs/configuration.md) | One phone number or many, what a tenant inherits, where the tokens go, and credentials from a database. |
| [Sending messages](https://github.com/gberikov/Wapper/blob/master/docs/sending.md) | Template messages, interactive messages, media. |
| [Receiving messages](https://github.com/gberikov/Wapper/blob/master/docs/webhooks.md) | The webhook endpoint, which fields to subscribe to, delivery statuses and opt-outs. |
| [Errors](https://github.com/gberikov/Wapper/blob/master/docs/errors.md) | Which exception means what, and what has already been retried by the time you see it. |
| [Managing templates](https://github.com/gberikov/Wapper/blob/master/docs/templates.md) | Creating and editing templates, media headers, one-time passcodes. |
| [Phone numbers](https://github.com/gberikov/Wapper/blob/master/docs/phone-numbers.md) | Quality and throughput, getting a number registered, the business profile. |
| [Flows](https://github.com/gberikov/Wapper/blob/master/docs/flows.md) | Building, publishing and sending a form the customer fills in. |
| [Analytics](https://github.com/gberikov/Wapper/blob/master/docs/analytics.md) | What the account sent, and what it was charged. |
| [Running in more than one instance](https://github.com/gberikov/Wapper/blob/master/docs/redis.md) | Sharing the rate limit budgets across replicas. |
| [Logging, tracing and testing](https://github.com/gberikov/Wapper/blob/master/docs/diagnostics.md) | What the client says about itself, and how to stand in for it in a test. |
| [Uncovered endpoints](https://github.com/gberikov/Wapper/blob/master/docs/raw.md) | The escape hatch for what this library does not model, and an honest list of the gaps. |
| [Changelog](https://github.com/gberikov/Wapper/blob/master/CHANGELOG.md) | What changed in each release, and what to do about it. |

## Getting started

```csharp
builder.Services.AddWhatsApp();
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

Name each one in configuration and ask for it by name:

```jsonc
{
  "WhatsApp": {
    "Tenants": {
      "acme":   { "AccessToken": "...", "PhoneNumberId": "106540352242922" },
      "globex": { "AccessToken": "...", "PhoneNumberId": "115540352242911" }
    }
  }
}
```

```csharp
await whatsApp.For("acme").Messages.SendTextAsync(customer, "hello", cancellationToken: ct);
```

The same `AddWhatsApp()` call registers them. See
[Configuration](https://github.com/gberikov/Wapper/blob/master/docs/configuration.md) for what a tenant inherits, where the tokens should
live, and what to do when they come from a database rather than a file.

### Receiving

Incoming messages, delivery statuses and everything else Meta reports arrive on a webhook
endpoint this library maps for you, with signature verification and typed handlers — see
[Receiving messages](https://github.com/gberikov/Wapper/blob/master/docs/webhooks.md).

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

Which rejections are retried, and which are not worth retrying, is in
[Errors](https://github.com/gberikov/Wapper/blob/master/docs/errors.md). Once the application runs as more than one process the counters
have to be shared — see [Running in more than one instance](https://github.com/gberikov/Wapper/blob/master/docs/redis.md).

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
- **Rate limit exceptions, log lines and spans redact the recipient's number**, keeping your
  own. `Scope.Key` still has it in full for code that deliberately wants it, and the Redis
  limiter keys a conversation by a digest of it rather than the number itself.
- **A request path stays under the Graph API version.** That holds for `Raw` too: a path that
  climbs out of it — however it is spelled — is refused rather than sent somewhere else.
- **A Flow's preview link is only fetched when asked for.** It needs no login and lasts thirty
  days: `Flows.GetAsync(id, includePreview: true, ...)`, or `GetPreviewAsync`.

## Licence

MIT. See [LICENSE](https://github.com/gberikov/Wapper/blob/master/LICENSE).
