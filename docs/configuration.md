# Configuration

There are three ways to configure the client, and they can be combined.

**The conventional section.** `WhatsApp` is found by name, so there is nothing to pass in:

```csharp
builder.Services.AddWhatsApp();
```

**A section of your own name**, for a host that keeps its settings somewhere else. What is
passed in is the only thing read — a `WhatsApp` section elsewhere in the file is ignored:

```csharp
builder.Services.AddWhatsApp(builder.Configuration.GetSection("Messaging:WhatsAppCloud"));
```

**In code**, for settings that are not configuration at all — a value computed at startup, or
an application that has no `appsettings.json`:

```csharp
builder.Services.AddWhatsApp(o =>
{
    o.AccessToken = vault.Read("whatsapp-token");
    o.PhoneNumberId = "106540352242922";
});
```

The delegate runs after whichever section was bound, so **what is set in code wins and the
rest still comes from configuration**. That is what makes pinning one value practical:

```csharp
// Everything from the WhatsApp section, except the API version.
builder.Services.AddWhatsApp(o => o.GraphApiVersion = "v27.0");
```

If there is no section — a console application, a test, a bare `ServiceCollection` with no
`IConfiguration` at all — the delegate is simply the whole configuration, and nothing
complains about the section that is not there.

## One phone number

Write the settings in the section. There is no tenant to name and nothing to enumerate.

```jsonc
{
  "WhatsApp": {
    "PhoneNumberId": "106540352242922",
    "WhatsAppBusinessAccountId": "102290129340398",
    "GraphApiVersion": "v26.0",
    "RateLimits": { "MessagesPerSecond": 80 }
  }
}
```

```csharp
await whatsApp.Messages.SendTextAsync(customer, "hello", cancellationToken: ct);
```

## Several phone numbers

Add an entry under `Tenants` per number, keyed by the name you will ask for it by. **Each
entry inherits everything set alongside it and overrides what it sets itself**, so the app
secret, the API version and the limits are written once and only the credentials are repeated:

```jsonc
{
  "WhatsApp": {
    // Shared by every tenant below.
    "WhatsAppBusinessAccountId": "102290129340398",
    "GraphApiVersion": "v26.0",
    "AppSecret": "...",
    "WebhookVerifyToken": "...",
    "RateLimits": { "MessagesPerSecond": 80 },

    "Tenants": {
      "acme": {
        "PhoneNumberId": "106540352242922"
      },
      "globex": {
        "PhoneNumberId": "115540352242911",
        // This number has been upgraded and the other has not.
        "RateLimits": { "MessagesPerSecond": 1000 }
      }
    }
  }
}
```

```csharp
await whatsApp.For("acme").Messages.SendTextAsync(customer, "hello", cancellationToken: ct);
```

Three things worth knowing about the multi-tenant shape:

- **The default tenant is still registered**, holding whatever sits outside `Tenants` — which
  is what the webhook endpoint reads `AppSecret` and `WebhookVerifyToken` from. Leaving it
  without an access token is deliberate: a forgotten `For(...)` then fails saying so, rather
  than sending as whichever tenant happened to be first.
- **One webhook endpoint is enough, whatever the apps.** Numbers on one Meta app share an app
  secret, so `app.MapWhatsAppWebhook("/whatsapp")` serves all of them and each event carries
  its own `PhoneNumberId`. Tenants on *separate* apps have separate secrets: give each its own
  `AppSecret` and map `app.MapWhatsAppWebhookForTenants("/whatsapp")`, which works out from
  each delivery whose secret to check it against — see
  [Receiving messages](webhooks.md#one-endpoint-for-every-tenant).
- **Credentials are not required in configuration.** A tenant listed here without an access
  token is legal, because a SaaS supplies tokens from its own store; it fails on its first
  call, naming itself. If that is your case, see [below](#credentials-from-somewhere-other-than-configuration).

If you would rather have one shape whatever the number of tenants, a single entry under
`Tenants` works exactly as well — it just costs naming the tenant on every call.

### Naming the section, even the conventional one

`AddWhatsApp()` finds the tenants when they are asked for, which is enough for most hosts and
is what lets a tenant added to configuration later work without a restart. Passing the section
— whatever it is called — makes it read them at registration instead:

```csharp
builder.Services.AddWhatsApp(builder.Configuration.GetSection("WhatsApp"));
```

Two things follow from enumerating them up front, and neither is possible without it:

- **A tenant whose settings are invalid fails startup**, rather than failing on its first
  call. A `GraphApiVersion` of `latest` in one tenant's entry is then a boot failure naming
  that tenant.
- **A configuration reload reaches the options.** Change a limit in a mounted config map and
  the next call paces to it.

Both overloads bind identically; nothing else about the section changes.

## Where the tokens go

An access token is worth exactly as much as the password it replaces, and `appsettings.json`
is committed. Keep them out of it.

In development, use user-secrets — they live in your profile, outside the repository:

```bash
cd samples/Wapper.Sample
dotnet user-secrets init
dotnet user-secrets set "WhatsApp:AccessToken" "EAAJB..."
dotnet user-secrets set "WhatsApp:AppSecret" "..."
```

The keys are the same paths, so a tenant's token is one level deeper:

```bash
dotnet user-secrets set "WhatsApp:Tenants:acme:AccessToken" "EAAJB..."
```

`WebApplication.CreateBuilder` loads them automatically in the `Development` environment, and
they override `appsettings.json` without appearing in it. They are **not encrypted** — plain
JSON under `%APPDATA%\Microsoft\UserSecrets` (`~/.microsoft/usersecrets` elsewhere) — so they
keep secrets out of source control, not off the machine.

In production, the same keys come from environment variables, with `__` for `:`:

```bash
WhatsApp__AccessToken=EAAJB...
WhatsApp__Tenants__acme__AccessToken=EAAJB...
```

or from a secret store — Key Vault, Secrets Manager, whichever — added as a configuration
provider. Because tenants are keyed by name rather than by position, the key of a given
tenant's token never changes when another tenant is added, renamed or removed.

Tokens that expire, rotate, or live in your own database are not a configuration problem at
all; see below.

## Every setting

Anything here can be set in the section, in a tenant's entry, or on `WhatsAppOptions` in code
— and in that order of increasing precedence. Only `AccessToken` and `PhoneNumberId` are
needed to send.

| Setting | Default | What it is for |
|---|---|---|
| `AccessToken` | — | The bearer token. |
| `PhoneNumberId` | — | The business phone number messages are sent from. |
| `WhatsAppBusinessAccountId` | — | Templates, phone numbers, Flows, analytics and subscriptions. |
| `AppId` | — | Uploading a business profile picture or a template header sample. Also keys the app-level rate limit, so several tenants on one app back off together. |
| `AppSecret` | — | Verifying webhook signatures. Deliveries are refused without it. |
| `WebhookVerifyToken` | — | Answering the subscription handshake. |
| `GraphApiVersion` | `v26.0` | Moves forward — or stays put — without a new package. |
| `BaseAddress` | `https://graph.facebook.com/` | A proxy or a test server. Must be https unless loopback. |
| `Timeout` | 100 s | Per HTTP call. Does not include time spent waiting for a rate limit permit. |
| `MediaDownloadHosts` | Meta's CDNs | The hosts a media download may present the token to. Configuration adds to the defaults. |
| `RateLimits:Enabled` | `true` | Turn off only when something in front of the client paces already. |
| `RateLimits:MessagesPerSecond` | 80 | Raise to 1000 once the number reports `ThroughputLevel.High`. |
| `RateLimits:PairInterval` / `PairBurst` | 6 s / 45 | One message per recipient per six seconds, with a burst. |
| `RateLimits:BusinessAccountRequestsPerHour` | 200 | 5000 once the account has a registered number. |
| `RateLimits:MaxWait` | 30 s | The longest a call waits for a permit before `WhatsAppRateLimitedException`. |
| `RateLimits:MaxRetries` | 4 | Spread over Meta's `4^X` seconds: 1, 4, 16 and 64. |
| `RateLimits:UsagePercentThreshold` | 100 | Start holding back when `X-App-Usage` reports this much of the allowance spent. |

The default tenant's settings are validated at startup, so a `Timeout` of zero or a
`GraphApiVersion` of `latest` fails the host rather than the first send. A named tenant is
validated when it is first used, unless the section was
[named](#naming-the-section-even-the-conventional-one), which validates every tenant at
startup too.

Credentials are the exception either way, and deliberately: demanding them in configuration
would make the arrangement below impossible.

## Credentials from somewhere other than configuration

A configuration section is a fixed list, written at deploy time. A SaaS onboarding accounts
through Embedded Signup does not have one — tenants appear while the process is running, and
their tokens expire and rotate. Replace the credential lookup instead:

```csharp
builder.Services.AddWhatsApp();
builder.Services.AddSingleton<IWhatsAppCredentialsProvider, TenantCredentials>();
```

```csharp
public sealed class TenantCredentials(IMemoryCache cache, ITenantStore store)
    : IWhatsAppCredentialsProvider
{
    // Called on every request, so anything talking to a database caches.
    public async ValueTask<WhatsAppCredentials> GetCredentialsAsync(string tenant, CancellationToken ct) =>
        await cache.GetOrCreateAsync($"wa:{tenant}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

            var row = await store.FindAsync(tenant, ct)
                ?? throw new WhatsAppConfigurationException($"No WhatsApp account is onboarded for '{tenant}'.");

            return new WhatsAppCredentials
            {
                AccessToken = row.AccessToken,
                PhoneNumberId = row.PhoneNumberId,
                WhatsAppBusinessAccountId = row.BusinessAccountId,
            };
        }) ?? throw new WhatsAppConfigurationException($"No credentials for '{tenant}'.");
}
```

`For("anything")` then works without the tenant having been registered at startup — the
per-tenant clients are built lazily and cached. The `WhatsApp` section still supplies
everything that is not a credential: the API version, the limits, the webhook secrets.

