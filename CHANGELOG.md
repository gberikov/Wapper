# Changelog

Notable changes, newest first. Versions follow [Semantic Versioning](https://semver.org),
and each released version is a bare tag on `master`.

## 0.2.0

Everything here came out of putting `0.1.1` into a production service: five places where the
webhook parser threw information away, four of them without saying so.

### Added

- **`AccountUpdated`**, a typed event for the `account_update` webhook field: policy
  violations, restrictions, scheduled disablement, deletion, and offboarding. `Event` is the
  enum to branch on, with `ViolationType`, `Restrictions`, `BanState` and `BanDate` carrying
  the detail, and `Json` holding the raw body for the half of this field that is only
  meaningful to a Solution Partner. `phone_number` is read both as an object and as a bare
  string — Meta sends both, and its own test delivery sends the string.
- **`MarketingPreferenceChanged.Category`**, the kind of message the customer's decision
  covers. `marketing_messages` today; Meta has said there will be more.
- **`TemplateStatusChanged.Recommendation`**, and `Details` now falls back to
  `rejection_info.reason`. A rejection put its explanation in `rejection_info`, which was not
  read at all, so an operator saw a bare `INVALID_FORMAT` and no hint of what to change.
- **`WhatsAppError.Title`**. Meta sends `title` on the errors attached to a delivery status,
  and on some of them it is the whole of what it says.

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

### Changed

- **`account_update` no longer arrives as `UnknownEvent`.** A handler registered for
  `UnknownEvent` to catch it must move to `AccountUpdated`. This is the only break in an
  otherwise additive release.

## 0.1.1

- The README links absolutely, so they resolve on the NuGet package page.

## 0.1.0

First published release.
