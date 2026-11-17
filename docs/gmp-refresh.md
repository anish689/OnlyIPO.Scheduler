# GMP Refresh: Free Sample Provider

Status: implementation only, off by default. No hosted workflow or schedule enabled.
Related: OnlyIPO#90 and only-ipo-web#108. API phase plan is the authoritative rollout gate.

## Source and limits

[GMP Today JSON](https://gmptoday.in/api/gmp.json), documented at
https://gmptoday.in/api/, is a CC BY 4.0 sample of three IPOs, refreshed hourly.
It permits commercial reuse with a visible gmptoday.in credit and link. Terms checked
18 November 2026. Do not scrape the full board or assume the sample covers every IPO.
The feed's timestamp is not an individual quote timestamp. Re-check licence/provenance
before activation and periodically; a changed licence/contract fails closed.

## Commands

Read-only provider check, no database opened:

```sh
dotnet run --project src/IPOOnly.Scheduler -- --gmp-preview
```

Write mode (only after migration and role-specific grants are approved):

```sh
dotnet run --project src/IPOOnly.Scheduler -- --gmp-once
```

Write mode requires `Gmp__Enabled=true`, `ConnectionStrings__IPOOnlyDatabase`, and
explicit `Gmp__Mappings__0__IpoSlug` / `Gmp__Mappings__0__FeedSlug` pairs. Use the
scheduler DB role, never postgres or the API role. `DeploymentTarget=production`
also applies the existing production connection guard. No secrets are logged.

Mappings require a human review of issuer identity and issue terms. Feed opening date
and upper price band must match stored terms exactly. No automatic fuzzy name matching.
Run with no mappings is rejected; there are no demo mappings in deployed configuration.

## Failure and concurrency behaviour

- One fixed HTTPS fetch, no redirects/retry loop: 20-second timeout, 1 MB response limit.
- Whole command has a two-minute deadline; no long-lived worker or background web request.
- One advisory lock prevents overlapping GMP writes. Two-second lock and five-second
  statement timeouts keep a contended refresh from waiting indefinitely.
- A transaction publishes the batch atomically; failures roll back. Normal IPO reads
  and watchlist writes remain independent. No financial refresh coupling.
- Newer usable reports upsert idempotently. Older reports cannot overwrite newer data.
- A newer matching but invalid report removes display approval. A missing rotating
  sample leaves the earlier timestamp intact; the API expires it after 24 hours.
- Median/range/source-count/date/price/URL validation rejects mismatched or disputed entries.
- Zero and negative quotes are retained when genuine. Null is never converted to zero.
- Source summary counts and sanitized failure class are logged, not credentials or bodies.
- An unavailable GMP feed never makes the API unavailable or blocks the overall product release.

## Proposed scheduling, not activated

Begin with an independent staging job at 09:17 and 17:17 IST (03:47 and 11:47 UTC),
one source request per run, no workflow auto-retries. Only after measured need consider
hourly polling; the provider itself changes hourly. Use existing free GitHub Actions
allowance with concurrency cancellation disabled and a short job timeout. Never wake
the web API to refresh GMP. Account for all existing jobs before enabling to avoid quota
overrun; this phase creates no paid resource and no scheduled trigger.

Missing samples are expected, not pages. Alert on sustained command failures and
licence/contract changes; show user-facing unavailable/stale states. Provider-wide failure
can disable GMP independently. Do not make this an API readiness-health dependency.

## Test evidence

Parser tests cover contract/licence/freshness, identity, zero/negative/extreme values,
future/pre-opening rows, unsafe URLs, disagreement and one-request 429 handling.
GmpStoreTests uses a new isolated PostgreSQL schema to verify negative precision,
idempotence, missing-sample expiry preservation, older-report protection, invalidation,
watchlist/read concurrency, bounded lock failure, refresh exclusion and atomic rollback.
CI already supplies ONLYIPO_TEST_DATABASE; no hosted credentials are required.
The full local scheduler suite passed 157 tests with none skipped using a newly
created local test database; that disposable database was removed afterwards.

Live preview on 18 November read three real samples; it opened no database. No sample
qualified for post-opening display at that point. Real mapped ingestion remains an
activation check, not evidence supplied by the fictional test fixtures.
