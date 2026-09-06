# Changelog

Notable changes, newest first. Versions follow [Semantic Versioning](https://semver.org),
and each released version is a bare tag on `master`.

## Unreleased

The findings of a review of the transport, the limiter and the webhook parser, fixed
together. Nothing here changes what a call sends to Meta; it changes what happens around it
when Meta is slow, says no, or sends something new.

### Fixed

- **A verification code no longer appears in exception messages, retry log lines or trace
  spans.** `VerifyAsync` sends the code in the query string, and the transport was naming
  the whole path — code included — in every failure it reported. Everything that writes a
  request somewhere durable now writes the path without its query; the endpoint and the
  operation are still there to read. The same applies to whatever a `Raw` call puts in a
  query.
- **A penalty recorded while a call is waiting its turn now holds that call back too.** A
  call's wait was priced once and slept out with no second look, so a call queued for a
  second before the Cloud API rejected somebody else's — and the budget was held for ten —
  walked into the block the moment its second was up. Every reservation is now re-priced
  when its wait runs out, in every budget it spends, so a penalty on the application budget
  holds a call waiting on the pair allowance as well. Both limiters.
- **Calls queued during a hold are released at the sustained rate after it, not together.**
  The hold drains the bucket, so each of them owes its place in the queue on top of the hold;
  eighty of them used to be given the same wait and let go at once when it lifted.
- **A call that gives up waiting hands its permits back.** Cancelling the wait used to leave
  the permits spent, so the next caller paid for a call that never went. Cancelled
  reservations now return their permits in every budget, and in the in-memory limiter the
  queue behind them moves up. A cancellation while the Redis script is already running is
  awaited rather than abandoned, and the permits handed back afterwards. The shared limiter
  returns a permit only at the tail of the queue: handing back a place from the middle would
  give a newcomer the same turn as somebody already waiting for it, so an interior
  cancellation leaves its permit spent and it refills at the configured rate.
- **`MaxWait` is now the whole wait, not the first estimate of it.** A call re-prices its
  reservations when its wait runs out, and a penalty recorded meanwhile pushes them back —
  which used to extend the wait without limit, past the ceiling the caller set. Sleeps are
  capped by what is left of `MaxWait`, and a call still not due at that deadline is refused
  and hands its permits back. The deadline is checked against a fresh reading rather than a
  stale one: a Redis answer that took a moment to arrive may already be out of date, and a
  permit that has come due in the meantime is granted. Both limiters.
- **A short `KeyLifetime` can no longer expire a reservation that is still waiting.** Redis
  keys were kept alive for a penalty but not for the refill a granted call was waiting on,
  nor for a call waiting on a different budget of the same send — so a lifetime shorter than
  the wait dropped live state and handed the allowance out twice. Keys now outlive both, with
  a minute of grace for a delayed caller.
- **An idle budget is no longer forgotten before it has recovered.** The in-memory limiter
  dropped any bucket untouched for ten minutes, so a two-hundred-an-hour account allowance
  spent in full came back as a full two hundred eleven minutes later. A bucket now goes only
  when forgetting it changes nothing: full, not held, and with nobody queued.
- **HTTP/2 is actually requested.** The client was configured for it, but a request built
  by hand does not inherit `DefaultRequestVersion` and went out as HTTP/1.1. Every request,
  retry and download now carries the client's version and version policy.
- **A timeout while the response body is being read is reported as a timeout**, not as the
  caller's own cancellation, and is never retried: the headers arrived, so the call may have
  taken effect. A body cut off by the connection, and a body that is not the documented
  response, are each reported as a `WhatsAppException` with the cause inside it rather than
  as a bare `IOException` or `JsonException`. For a media download the timeout covers the
  round trip to the headers; reading the stream is bounded by the caller's own token.
- **A resumable upload no longer copies the file twice.** A seekable stream — a file, a
  `MemoryStream` — is sent from where it stands and rewound for a retry, never buffered. A
  stream that cannot be rewound is read into memory once, up to a ceiling of 100 MB, and
  refused before that rather than after the process has run out.
- **A message of a type this library does not know keeps everything it carried.** It used
  to become an `UnsupportedMessage` with the type name and nothing else. It is now an
  `UnknownMessage`: the envelope read as for any message, the `interactive.type` when it was
  an interactive reply of an unknown kind, and the message object itself as JSON.
  `UnsupportedMessage` is now only the documented `unsupported` type — something WhatsApp
  itself could not carry, with Meta's errors saying what — and a media message whose file
  could not be fetched.
- **One unreadable message or status no longer costs the ones beside it.** The items of
  `messages` and `statuses` are bound one at a time; an item Meta has reshaped is reported
  as an `UnknownEvent` carrying that item alone — with the phone number it arrived on — and
  its neighbours are delivered as the events they are.

### Added

- **`IWhatsAppUnparsedWebhookHandler`**, the place a signed delivery the parser could not
  read is handed to before it is acknowledged. Register one to keep the body somewhere
  durable and replay it later; without one the endpoint logs the error — never the body —
  and acknowledges, as before. A handler that throws fails the delivery so Meta repeats it.
- **Typed events for five more webhook fields**, each checked against Meta's reference page
  for the field: `AccountAlert` (`account_alerts`), `BusinessCapabilityChanged`
  (`business_capability_update`), `PhoneNumberSecurityChanged` (`security`),
  `TemplateCategoryChanged` (`template_category_update`) and `TemplateComponentsChanged`
  (`message_template_components_update`). Every enum keeps its raw string beside it.
- **`FlowReply.ReadResponse<T>(JsonTypeInfo<T>)`**, the answers of a submitted Flow as a
  type of your own, read without reflection.
- **Raw values on Flow statuses and health verdicts** — `FlowStatusChanged.RawStatus` and
  `RawPreviousStatus`, `Flow.RawStatus`, `FlowHealth.RawCanSendMessage` — so a value this
  library does not know is still readable, as it already was on the phone number events.
- **[What is covered](docs/coverage.md)**: the endpoints and webhook fields this library
  types, with their variants and tests, and what it does not.
- **A Native AOT smoke test**, `samples/Wapper.AotSmoke`, published ahead of time and run by
  CI on Linux: the container, source-generated JSON, the webhook endpoint with its signature
  check, parsing, dispatch and the client, in one trimmed binary. Building the libraries with
  `IsAotCompatible` only said the analysers found nothing; this runs it.
- **Benchmarks**, `benchmarks/Wapper.Benchmarks`, for webhook parsing, the in-memory limiter
  and upload buffering, with the figures and the machine they were taken on in
  [docs/performance.md](docs/performance.md).
- **Redis Cluster tests**, against a single node in cluster mode, for both the refusal
  without a hash tag and the working configuration with one.

### Changed

- **Redis Cluster** needs a hash tag in `KeyPrefix` (`{wapper}:rl:`), because every budget
  of one call is spent by one script. Without one the first paced call now fails with a
  `WhatsAppConfigurationException` naming the setting, instead of a warning about Redis
  being away and a silent fall back to per-process pacing. See [docs/redis.md](docs/redis.md).
- **The Redis budget hash carries four more fields**: the count of permits earned, the rate
  and burst, and how long a granted call needs the state kept. An older instance ignores
  them and a newer one treats their absence as a fresh budget, so the two run side by side
  through a rolling deployment — but the guarantees about cancellation and retention only
  hold once every instance is on this version.
- **After a rejection the client waits its own backoff plus `MaxWait`**, rather than the
  larger of the two, because the hold drains the bucket and the call owes its place in the
  queue on top of it.

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
