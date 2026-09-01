# Changelog

Notable changes, newest first. Versions follow [Semantic Versioning](https://semver.org),
and each released version is a bare tag on `master`.

## 0.4.0

Two small things `0.3.0` computed inside and did not hand out, so a consumer kept its own copy
of both.

### Added

- **`Template.Placeholders()`**, the substitutions the body expects: the names for a named
  template, `{{1}}` through `{{n}}` for a numbered one, each once and in a fixed order. It is
  the answer to "what does this template want" — the line an operator is shown when a
  broadcast file does not fit, the list printed beside a template name, the count stored in
  somebody's table — and it needs the same reading of `{{…}}` that `Validate` was already
  doing internally, which is why a second parser had grown up outside the library. Both now
  run on the one function, so what a caller is shown and what is enforced cannot disagree. The
  body only: a text header carries at most one placeholder and so does each URL button, and
  each is numbered separately from the body, so one flat list would claim the header's `{{1}}`
  and the body's `{{1}}` were the same value. `Validate` still checks all of them together.
- **`Template.QuickReplyIndexes()`**, where each quick-reply button sits among *all* the
  template's buttons. `Buttons` carries the kinds and anyone can count, which is the trouble:
  everyone does, and by quick replies alone, because that agrees with the payload's own
  ordinal for as long as the template has nothing else in it. Add a URL button between two
  quick replies and the positions shift by one in silence — either the whole wave comes back
  with a bare `100`, or the payload lands on the neighbouring button and someone who tapped
  "Not interested" is recorded as having asked to be unsubscribed for good.

## 0.3.0

`0.2.0` was about fields Meta sends that were being dropped. This one is about knowledge of
the Cloud API that the library either kept to itself or never held at all, so that every
consumer wrote it out again — and got it slightly wrong, at the scale of a broadcast.

### Added

- **`WhatsAppError.Classify()`**, what a Cloud API error code means, in the Cloud API's own
  terms: `Transient`, `RateLimited`, `RecipientUnreachable`, `AccountBlocked`,
  `RequestRejected`, and `Unknown` for a code invented last week. Whoever catches a
  `WhatsAppApiException` knows `Error.Code` and nothing else, and the question it answers —
  retry, or is this recipient hopeless, or is the problem the account rather than the person —
  has one right answer, and Meta has it. The table already existed, as an `internal` in the
  retry path; it is now public, and `ThrottlePolicy.ShouldRetry` is a caller of it rather than
  a second copy beside it. It takes a `WhatsAppError`, not an exception, because a message
  Meta accepts and then fails to deliver reports its code on the webhook, in
  `MessageStatusChanged.Errors`, where the same decision has to be made and no exception ever
  arrives. `131042` — the business is not eligible to send, usually an unpaid invoice — is
  `AccountBlocked` and not a bad number: it was being swept into a general "4xx is hopeless"
  rule, where one billing problem marked 434 live contacts as failures in nine minutes.
  `131050`, the customer who opted out, was declared and used in no decision at all; it is now
  the unreachable recipient it always was.
- **`Template.Validate(TemplateMessage)`**, the values checked against the template that is
  about to be filled with them, without a call to Meta. A mismatch is rejected on every single
  message, so the first wave of a broadcast burns whole. It reports what is missing and what
  is extra rather than a yes or a no, because the report goes to an operator with a file to
  fix. It knows the parts that are not guessable: numbered placeholders are counted by the
  **highest index**, not by how many appear, so a body reading `only {{2}}` expects two
  values; a name repeated in a named template is one substitution; the format comes from the
  template's own `ParameterFormat` rather than from what the values happen to look like; a
  button's index is its position among **all** the buttons, so a URL button between two quick
  replies is index 1 and calling it a `quick_reply` is a bare `100` every time; a quick reply
  may be left unfilled, since the payload is the sender's; and an authentication template
  still takes exactly one body value though Meta writes its body itself.
- **`MediaKinds.For(mimeType)`**, which of the five kinds of attachment a media type is.
  `MediaLimits.For` already walked exactly this classification and returned a number instead
  of the answer, so every sender wrote the rule out again — including the one case nobody
  guesses, that `image/webp` is a sticker and not an image. The limit is now picked through
  the mapping rather than beside it. `IncomingMediaKind` is reused deliberately: what arrives
  and what is sent are the same five kinds.
- **A raw string beside the last three parsed enums that had none**:
  `AccountUpdated.RawQualityRating`, `AccountUpdated.RawCurrentLimit` and
  `Template.RawCategory`. Nine places got one in `0.2.0`; these were missed. Digging a single
  field back out of `AccountUpdated.Json` is exactly what that release was getting away from,
  and a Graph read has no raw body to dig in at all.

### Changed

- **The recipient's number is normalised before it is sent.** `To` went out exactly as it was
  handed over, and how the Cloud API feels about a leading `+` is a question for Meta, not for
  every caller in turn — so numbers are stored in E.164 and the `+` was being stripped by hand
  downstream, because nobody wants to find out with a wave of two thousand messages. The
  client now strips the punctuation of a written-down number — `+`, spaces, hyphens, brackets
  and dots — and refuses anything else with an `ArgumentException` naming it, rather than
  letting Meta answer with a bare `100`. The per-recipient rate limit is keyed on the same
  normalised form, so `+7 700 000 00 01` and `77000000001` no longer get an allowance each.

## 0.2.0

Everything here came out of putting `0.1.1` into a production service: fields Meta sends that
were being thrown away, several of them without a word, and one place where the library's
multi-tenancy stopped short of the webhook. A second pass then read the wire model property by
property, asking of each one where it reaches the caller — which is where the two round trips
that quietly erased data at Meta came from.

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
- **`TemplateStatusChanged.Recommendation`**, and `Details` now reads `rejection_info.reason`
  as well as `other_info`. A rejection put its explanation in `rejection_info`, which was not
  read at all, so an operator saw a bare `INVALID_FORMAT` and no hint of what to change. When
  Meta sends both they are kept both, joined by a newline: coalescing them would drop
  whichever sentence the operator happened to need.
- **`WhatsAppError.Title`**. Meta sends `title` on the errors attached to a delivery status,
  and on some of them it is the whole of what it says.
- **The structural limits on an interactive message are checked before the send**, with an
  exception naming the field and its actual length. Button and row counts were already
  checked; the lengths were not, and Meta answers every one of them with a bare `100` that
  says nothing about which of a dozen strings it objected to. Every body, every header, every
  footer, button and row titles and identifiers, row descriptions, section titles and count,
  at Meta's currently documented values — and for every interactive type, not only the reply
  buttons and the list: a call-to-action, a Flow and a location request carry the same fields
  under the same limits and were going out unchecked.

- **`IncomingMessage.Identity`**, the notice WhatsApp attaches to a message when the sender's
  identity key has changed — a reinstalled app, a new handset. It only arrives for accounts
  that switched the check on, and an account switches it on precisely to act on this; it was
  not in the wire model at all, so it could not even surface as an `UnknownEvent`, because the
  message around it parsed perfectly well. Meta's own SDK types two of its three fields as
  strings where the platform documentation shows a boolean and a number, so both spellings are
  read.

- **`UnsupportedMessage.Errors`**, the whole array rather than its first entry. `Error` stays
  as the first of them, which is usually the only one.

- **`Template.UnknownComponents`**, the `type` of every component this library has no typed
  form for — a carousel, a limited-time offer, whatever Meta adds next. See *Changed* for what
  it protects.

- **A raw string beside every parsed enum that had none.** A value Meta invented last week
  parses to `Unknown`, and on the webhook the raw body is there to fall back on; a Graph read
  has nothing of the kind, so the string was simply gone. Added on `PhoneNumber` (nine of
  them), `Template.RawQualityScore`, `TemplateQualityChanged`, `PhoneNumberQualityChanged`,
  `ConversationDataPoint` and `PricingDataPoint.RawType` — the same treatment
  `PricingDataPoint.RawCategory` and `Template.RawStatus` already had.

- **`Contact.RawBirthday`**, for the partial dates a vCard allows and a `DateOnly` cannot hold.
  A card arriving with `--05-21` lost the field silently, and forwarding that card on now
  keeps it.

- **`RateLimitScope.RedactedKey`** is public. It was internal, so the Redis package could not
  reach it — see *Fixed*.

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

- **Account-level events carry the time they happened.** A template verdict, a ban, a Flow
  alert and a display-name decision have no timestamp of their own: `entry.time` is the only
  one in the payload, and it was not read. Every one of those events was stamped with the year
  one, which reads as data corruption rather than as a missing field — and nothing downstream
  could order them or measure how late they arrived. A message or status missing its own
  timestamp now falls back to it too.

- **Template analytics reads every page.** It is an ordinary Graph edge and pages like one,
  and only the first page was read — with no error and no log line, so ten templates over
  ninety days quietly reported whatever fitted in one page. Every figure in a spend or click
  report could be understated by an amount nothing revealed.

- **Reading a business profile and writing it back no longer clears its category.** A
  `vertical` this library has not been taught parses to `Unknown`, and `Unknown` is written as
  the empty string, which is Meta's documented way to *clear* the field — so an unrelated edit
  to the About text erased the category customers see. An `Unknown` that came from a read is
  now left alone; an `Unknown` set by hand still clears, as documented.

- **`success: false` is no longer read as success.** Around a dozen calls whose entire answer
  is that field — subscribing to webhooks, publishing a Flow, registering a number, updating
  a profile or a template — deserialized it and never looked. A subscription that silently
  never happened is the worst of these: the endpoint stays healthy-looking and simply receives
  nothing forever. A body without the field is still accepted, so only an explicit refusal
  raises.

- **The Redis limiter no longer logs a customer's phone number in full.** Its one warning
  logged the scope through `ToString`, which spells a pair scope out with the number in it —
  against the README's own promise that log lines redact the recipient. It logs the redacted
  key, as everything else already did.

- **A location's address is sent even without a name.** WhatsApp only shows the address under
  a name, but that is its display rule to apply; dropping the field meant a location received
  and forwarded on lost it, while the same address inside a template parameter went out
  untouched.

### Changed

- **`account_update` no longer arrives as `UnknownEvent`.** A handler registered for
  `UnknownEvent` to catch it must move to `AccountUpdated`.

- **`UnsupportedMessage.Error` is computed from `Errors`** and can no longer be assigned. Code
  that reads it is unaffected; a test that built the event by hand with `Error = …` sets
  `Errors` instead.

- **Editing a template that carries a component this library cannot model is refused.**
  Components are replaced wholesale on an update, so reading such a template and writing it
  back — a typo fix in the body — silently erased the carousel or the limited-time offer at
  Meta, and there was no way to see that it had happened. The template still reads back, with
  the missing pieces named in `Template.UnknownComponents`, and `UpdateAsync` throws rather
  than writing it back without them.

These three, and the `success: false` and location-address entries above, are the breaks in an
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
