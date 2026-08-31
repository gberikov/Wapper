# Changelog

Notable changes, newest first. Versions follow [Semantic Versioning](https://semver.org),
and each released version is a bare tag on `master`.

## 0.2.0

Everything here came out of putting `0.1.1` into a production service: fields Meta sends that
were being thrown away, several of them without a word, and one place where the library's
multi-tenancy stopped short of the webhook.

### Added

- **`AccountUpdated`**, a typed event for the `account_update` webhook field: policy
  violations, restrictions, scheduled disablement, deletion, and offboarding. `Event` is the
  enum to branch on, with `ViolationType`, `Restrictions`, `BanState` and `BanDate` carrying
  the detail, and `Json` holding the raw body for the half of this field that is only
  meaningful to a Solution Partner. `phone_number` is read both as an object and as a bare
  string — Meta sends both, and its own test delivery sends the string.
- **`MapWhatsAppWebhookForTenants`**, one endpoint for tenants on more than one Meta app.
  `MapWhatsAppWebhook` reads its app secret from the tenant it was mapped with, which cannot
  work when each tenant has its own: the tenant is not known until the body has been read, and
  the body is not to be believed until the signature has been checked. The new mode reads the
  routing fields out of the unverified body with a forward-only scan, resolves them through
  the new **`IWhatsAppWebhookTenantResolver`**, and checks the signature against that tenant's
  secret. A forged identifier only ever selects a secret that does not match, so it can cause
  a refusal and never an acceptance. A delivery covering tenants on different apps is refused
  rather than verified against whichever came first, and a number matching no tenant is
  refused with a log line naming it. The default resolver matches the numbers and accounts in
  configuration; a host whose tenants live in a database registers its own, the same way it
  replaces `IWhatsAppCredentialsProvider`. `MapWhatsAppWebhook` is untouched.
- **`WhatsAppWebhookParser.DeliveryKey`**, the SHA-256 of the raw body as hex. Meta repeats
  deliveries of its own accord and repeats a failed one for up to seven days; this is the key
  to put a unique index on, so a repeat collides instead of being handled twice. The
  documentation warned that handlers have to be idempotent and offered nothing to make them so.
- **`WhatsAppWebhookParser.ReadOrigins`**, the routing scan itself, so a queue consumer or an
  Azure Function can route the way the endpoint does.
- **`IncomingMessage.IsFrequentlyForwarded`**. Meta reports an ordinary forward and a message
  forwarded more than five hops down a chain separately, and they mean different things — the
  second is what a hoax or a viral scam looks like. Both were being collapsed into
  `IsForwarded`, which keeps its meaning of any forward at all.
- **`MarketingPreferenceChanged.Category`**, the kind of message the customer's decision
  covers. `marketing_messages` today; Meta has said there will be more.
- **`TemplateStatusChanged.Recommendation`**, and `Details` now falls back to
  `rejection_info.reason`. A rejection put its explanation in `rejection_info`, which was not
  read at all, so an operator saw a bare `INVALID_FORMAT` and no hint of what to change.
- **`WhatsAppError.Title`**. Meta sends `title` on the errors attached to a delivery status,
  and on some of them it is the whole of what it says.
- **The structural limits on an interactive message are checked before the send**, with an
  exception naming the field and its actual length. Button and row counts were already
  checked; the lengths were not, and Meta answers every one of them with a bare `100` that
  says nothing about which of a dozen strings it objected to. Both bodies, both headers, both
  footers, button and row titles and identifiers, row descriptions, section titles and count,
  at Meta's currently documented values.

### Fixed

- `user_preferences` is read in both of the shapes Meta sends it: the `user_preferences`
  array, and the flat form with the fields on `value` itself. Only the array was read, so a
  marketing opt-out in the flat form vanished with no error and no `UnknownEvent` — and the
  cost of missing one is messages to somebody who asked for none.
- Three silent drops now arrive as `UnknownEvent`, which is what the README and the webhook
  documentation have always promised: a `messages` change with no `metadata.phone_number_id`,
  a message or status missing its identifiers, and any field that bound cleanly and produced
  no event at all. Meta always sends those fields, so this was quiet — and quiet is the worst
  of the failure modes, because a customer's message could disappear with nowhere left to
  notice.
- **A media message Meta could not fetch keeps its explanation.** The one case where incoming
  media arrives with no id is the case Meta attaches `131052` — *"Media download error"* — to,
  and the error was being dropped. It reaches the handler on the `UnsupportedMessage`.

### Changed

- **`account_update` no longer arrives as `UnknownEvent`.** A handler registered for
  `UnknownEvent` to catch it must move to `AccountUpdated`. This is the only break in an
  otherwise additive release.

### Documented

- **A media download has no ceiling, and the caller has to impose one.** An upload is measured
  against Meta's limits and refused before it is sent; a download is a stream, and
  `MediaContent.FileSize` and the `Content-Length` behind it are what the server said rather
  than a promise about what will arrive. Nothing said so anywhere.

## 0.1.1

- The README links absolutely, so they resolve on the NuGet package page.

## 0.1.0

First published release.
