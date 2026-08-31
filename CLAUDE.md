# Wapper

A modern .NET library for the WhatsApp Cloud API.

Everything in this repository — code, comments, commit messages, documentation,
issues and pull requests — is written in **English**.

## Git Workflow

This repository follows **git flow**. There is no direct pushing to long-lived branches.

### Branches

| Branch      | Role                                                                 |
|-------------|----------------------------------------------------------------------|
| `master`    | Released code only. Every commit is a release merge or a hotfix merge. |
| `develop`   | Integration branch. All finished work lands here first.               |
| `feature/*` | New functionality. Branches off `develop`, merges back into `develop`. |
| `fix/*`     | Bug fixes. Branches off `develop`, merges back into `develop`.         |
| `hotfix/*`  | Urgent production fixes. Branches off `master`, merges into `master` **and** `develop`. |

### Rules

- Never commit directly to `master` or `develop`; always work on a `feature/*`
  or `fix/*` branch and open a pull request.
- Merge with **merge commits**, never squash and never rebase a shared branch.
  The merge commit subject states what was merged and why, for example
  `Merge feature/webhook-dispatch into develop (typed webhook event handlers)`.
- After a release merge into `master`, immediately **back-merge** `master` into
  `develop` so the branches never drift.
- Delete the topic branch once it is merged.

### Release procedure

1. Merge `develop` into `master` through a pull request. The merge commit subject
   names the version being released.
2. Tag the merge commit on `master` with a bare SemVer tag: `0.1.0`, `1.2.3`.
   No `v` prefix — MinVer derives the package version straight from the tag.
3. Pushing the tag triggers the release workflow, which packs and publishes to
   NuGet.org via Trusted Publishing (OIDC).
4. Back-merge `master` into `develop`.

Tags are immutable: they cannot be deleted or moved. A published NuGet version
cannot be recalled, so the commit it was built from must stay reachable forever.

### Commit messages

Conventional Commits with a scope naming the affected area:

```
feat(messages): interactive list and button messages
fix(ratelimit): honour estimated_time_to_regain_access on 80007
docs(readme): multi-tenant credential provider example
test(webhooks): signature verification against the raw request body
chore(release): 0.2.0
```

## Design Decisions

Recorded here so they are not re-litigated:

- **Packages:** `Wapper.Abstractions` (contracts and DTOs), `Wapper` (client,
  throttling, retries), `Wapper.AspNetCore` (webhook endpoint and dispatch),
  `Wapper.RateLimiting.Redis` (distributed limiter state).
- **Target framework:** `net8.0` only. Trim- and AOT-compatible, so all
  serialization goes through a `JsonSerializerContext` — never reflection-based
  `System.Text.Json` overloads.
- **Client shape:** a facade `IWhatsAppClient` exposing resource groups
  (`.Messages`, `.Media`, `.Templates`, …). Each group is also resolvable from DI
  on its own.
- **Errors:** exceptions. Branch on `error.code`, never on the HTTP status code —
  Meta explicitly documents the status codes as unstable and `error_subcode` as
  deprecated since v16.0.
- **Rate limiting:** built in, not delegated to the caller. Four independent
  budgets, each with its own key and its own error code:

  | Budget | Limit | Key | Error |
  |---|---|---|---|
  | Cloud API throughput | 80 or 1000 msg/s (from `throughput.level`) | phone number | `130429` |
  | Pair rate limit | 1 per 6 s, burst of 45 | sender + recipient | `131056` |
  | WABA management | 200/h, or 5000/h once a number is registered | app + WABA | `80007` |
  | App platform limit | `200 × DAU`, undisclosed | app | `4` |

  The first three are paced proactively. The app-level budget has no published
  number, so it is handled reactively only — back off when the error arrives and
  steer by the `X-App-Usage` and `X-Business-Use-Case-Usage` headers.
- **Backoff:** `4^X` seconds, the only formula Meta publishes. The Cloud API does
  **not** send a `Retry-After` header; read it opportunistically but never rely on
  it. Prefer `estimated_time_to_regain_access` (in minutes) from
  `X-Business-Use-Case-Usage` when present.
- **Throttling behaviour:** calls wait asynchronously for a token and throw
  `WhatsAppRateLimitedException` once `MaxWait` elapses. Never block a thread.
- **Multi-tenancy:** first class. Credentials come from
  `IWhatsAppCredentialsProvider`; the default implementation reads `IOptions`
  from configuration, and a SaaS host substitutes its own database-backed one.
- **Graph API version:** defaults to `v26.0` and is overridable through
  configuration, so a consumer is never stranded when Meta retires a version.
- **Time:** every delay, window and backoff goes through `TimeProvider` so tests
  drive them with `FakeTimeProvider` instead of real clocks.
