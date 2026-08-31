# Uncovered endpoints

## Calling something this library does not cover

An endpoint with no typed API is an inconvenience, not a wall. `Raw` sends anything the Cloud
API accepts with the tenant's credentials, the configured version and base address, the four
rate limit budgets, the retry policy and the same typed exceptions:

```csharp
var catalogues = await whatsApp.Raw.SendAsync(
    new RawRequest
    {
        Method = HttpMethod.Get,
        Path = "{waba_id}/product_catalogs",
        Kind = RawCallKind.Management,
        Operation = "catalogs.list",
    },
    ct);

foreach (var catalogue in catalogues.GetProperty("data").EnumerateArray())
{
    logger.LogInformation("{Id}", catalogue.GetProperty("id").GetString());
}
```

`{phone_number_id}`, `{waba_id}` and `{app_id}` are filled in from the tenant's credentials,
so the same path works for every tenant of a multi-tenant host. `Kind` says which budget the
call spends, so it is paced with everything else — this is the whole reason to use it rather
than a second `HttpClient` beside this one, which would pace against nothing and walk both
into Meta's limits.

Pass a `JsonTypeInfo<T>` from a `JsonSerializerContext` of your own to read the response into
a type instead of a `JsonElement`. It is asked for rather than inferred because these packages
are trim- and AOT-compatible, and a reflection-based overload would quietly break both.

Two things it does not do: it checks nothing before sending, so Meta's bare `100` is all you
get back, and anything you interpolate into the path is yours to `Uri.EscapeDataString`. Prefer
a typed API wherever one exists.

## What is not covered yet

The Cloud API is wide, and this release types the parts most applications reach for. Everything
below is reachable through [`Raw`](#calling-something-this-library-does-not-cover) today; what
is missing is the typed, validated, documented version.

- **Commerce — catalogue, single- and multi-product messages.** The send half is easy; the
  catalogue behind it is managed through Commerce Manager and the Marketing API rather than
  anything WhatsApp-shaped, so typing the send alone would give you a message with nothing to
  put in it. The order that comes back *is* typed, because it arrives whether or not you use
  the API.
- **Carousel and limited-time-offer templates.** A `Template` here is one header, one body,
  one footer and one buttons block, which is what Meta allows everywhere else. A carousel is
  an array of cards each with components of its own, and a limited-time offer adds an expiry
  component plus parameters at send time. Both need a second shape of the model rather than
  another property, and a template that gets it wrong is rejected at review a day later.
- **Template library, archiving, and template groups.** Small and self-contained; simply not
  reached yet.
- **QR codes and short links, and conversational components** (ice breakers, commands, the
  welcome message that `WelcomeRequest` answers). Also small, also just not reached yet.
- **Blocking users.** A recent endpoint that is still moving; worth waiting for it to settle
  rather than shipping a signature that changes.
- **The Calling API.** Not a few endpoints — call permissions, SIP configuration, WebRTC
  signalling, its own webhook field and its own error codes. It is a package of its own, and
  bolting it onto `IMessagesApi` would be the wrong shape.
- **Reading the WhatsApp Business Account itself** (`GET /{waba_id}`): name, currency,
  timezone, verification and Marketing Messages Lite eligibility. Two dozen fields, most about
  billing and partner ownership, and none of them needed to send a message — a one-line `Raw`
  read serves the applications that want it.
- **Partner-facing endpoints** — Embedded Signup, credit line sharing, `partner_solutions`,
  `history`, the `smb_*` sync fields. These need advanced access granted through App Review to
  an approved Solution Partner, so they cannot be exercised, let alone tested, without that
  status.
- **Webhook fields with no typed event** — `account_alerts`, `business_capability_update`,
  `security` and the rest arrive as `UnknownEvent` carrying their body, so nothing is lost;
  they are simply not modelled.

If one of these is blocking you, open an issue naming it — `Raw` is meant to keep you moving,
not to be the answer forever.

