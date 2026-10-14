# Financial refresh and fallback strategy

14 October 2026. Scope: financial statements, not prices or subscription multiples.

## Decision

Keep the existing independent financial worker and source precedence for version 1,
subject to the hosted recovery check. Do not treat a green job as proof that every
company has financial coverage. Introduce persisted refresh outcomes and document
caching as a separate optimization; do not add another high-frequency full crawl.
No new hosting, billing, database migration or production cutover is part of this
document. Proposed items below are not implemented by this documentation change.

### Hosted validation result

PR #34 passed CI and was merged before validation run 38597101383 on 14 October.
That run attempted 29 due issuers: zero populated, 28 unavailable, one failed.
Zetwerk's original URL still returned 403; the official SEBI transfer then raised
IOException after roughly 31 seconds. This was not the three-minute deadline.
Logs report only the exception class, so the underlying connection/read failure
cannot be narrowed further from this evidence. Existing observations were retained.

Therefore the current fallback is NOT yet proven reliable on hosted runners.
Keep incident #32 open. Do not keep increasing timeouts or mark the error as empty
coverage to obtain a green workflow. Next implementation priority: bounded retry
of transient official-document transport errors within the existing deadline,
sanitized diagnostic codes, then persistent refresh outcomes and document caching.
Source access restrictions, checksum failures and parser ambiguity must not be
treated as transient download errors. Local PDF success is insufficient evidence
for hosted readiness. Run link:
https://github.com/anish689/OnlyIPO.Scheduler/actions/runs/38597101383

## Implemented today

- Scheduled workflow: 09:47 and 18:47 IST, best effort. Financials is a separate
  matrix job; API requests never wait for its completion.
- Existing validated financial observations fetched within 24 hours are skipped.
  Missing/unsupported records have no successful-observation timestamp and are
  therefore retried each cycle. Source changes do not currently invalidate this
  24-hour check immediately.
- Upstox structured statements first, consolidated then standalone when the
  provider confirms empty coverage. Provider authentication/network/schema errors
  are not silently converted into an RHP fallback.
- If structured coverage is empty, use the supplied RHP on an allowed HTTPS host.
  Only an exact reviewed broken-URL mapping may select an official replacement
  after document HTTP 403/404; checksum verification is mandatory.
- Ordinary RHP requests have a 45-second deadline. Reviewed replacements have a
  three-minute bounded download/parse window; parent cancellation still applies.
  The 30 MiB cap and no-redirect policy remain. HTTP header acquisition is still
  subject to the configured HttpClient timeout.
- Validate periods, units, basis and values. Unsupported/ambiguous tables produce
  unavailable coverage, not guessed numbers. Preserve citations internally.
- Fetch/parse before opening a write transaction. Publish a coherent validated
  snapshot atomically with short lock/statement timeouts. Empty/failing results
  never delete existing observations. Concurrency behavior is covered in CI.
- Stop the batch on provider authentication/rate-limit errors; ordinary per-company
  failures are isolated and reported. The watchdog monitors scheduled execution
  by partition, not the age of financial statements or completeness of coverage.

## Recommended optimization

Use the existing two daily scheduler opportunities to select only due records.
Store eligibility independently from successful financial rows, so a failed or
empty fetch cannot overwrite a good snapshot or look like a successful refresh.

| Outcome / company state | Proposed next action |
| --- | --- |
| New issuer or new RHP URL/version | Eligible at the next worker cycle |
| Successful structured data, active/recent IPO | Check daily; publish only validated changes |
| Successful structured data, older listed issuer | Check weekly; post-listing prices keep their own cadence |
| Validated RHP with unchanged content | Reuse parsed result; do not download/parse the full PDF daily |
| Structured data confirmed absent | Retry daily for active IPOs, weekly for older issuers |
| Unsupported PDF layout | Await changed content or parser version; weekly source recheck, no repeated parsing of identical bytes |
| Document 403/404 without reviewed alternative | Preserve prior data, flag source issue, retry after 24 hours with a seven-day cap on repeated backoff |
| Transient network/5xx | At most two bounded retries with jitter; persist next attempt if still failing |
| 429 | Respect Retry-After when supplied; defer provider work rather than retrying every company |
| API 401/403 | Stop provider work, alert for credential repair; do not fall back around invalid credentials |

These intervals are proposed operating defaults, not claims about provider
publication frequency. New document/source versions must override a later retry
date. Manual repair may force a single issuer after review, without resetting
the cadence evidence for scheduled jobs.

## Minimal persistence model

Add an application-owned refresh-state table keyed by IPO and source family:
`last_attempt_at`, `last_success_at`, `next_attempt_at`, `outcome`,
`consecutive_failures`, `source_url`, `source_hash`, `parser_version`,
`etag`, `last_modified`, `lease_owner`, `lease_expires_at` and a sanitized error code.
Keep financial observations separate. Never store credentials or response bodies
in error columns. Scheduler gets scoped write access; API gets only the coverage
metadata it needs. Migrate through the API repository's normal migration process.

Claim a bounded batch with an expiring lease, then release DB locks before HTTP
requests. Publish observations and successful state in one transaction. Failed
attempts update state without touching observations. Recover abandoned leases;
use a per-source concurrency limit and a worker deadline. Start serial as today;
increase concurrency only after provider limits and DB load tests justify it.

Cache approved documents by SHA-256 with source URL, retrieval time, parser version
and validated issuer identity. Use conditional requests where supported. A 304 is
usable only with the matching cached artifact; changed content must be revalidated.
Keep the cache private, bounded and outside Git. Proposed retention: 30 days for
raw PDFs with a storage cap; retain provenance and validated facts longer. Verify
source terms before introducing hosted document storage. No storage is provisioned
by this plan. A missing cache entry causes a normal bounded re-download.

## Fallback policy

1. Structured Upstox financials, with explicit schema and issuer checks.
2. Current official RHP, only after confirmed empty structured coverage.
3. Reviewed official copy of the same RHP for a broken document URL. Exact source
   match and checksum must pass; do not guess URLs or automatically trust search.
4. Last-good validated snapshot, with honest coverage/freshness metadata.
5. If none exists, display unavailable. Never synthesize figures or mix financial
   years, standalone/consolidated bases, or document versions to fill a table.

RHP takes precedence over DRHP. Do not silently substitute an older DRHP when an
RHP exists but is inaccessible. If provisional DRHP financials are introduced
later, they require explicit labeling, separate provenance and a reviewed policy;
that behavior is not enabled today.

## Monitoring and UI

Track attempted/populated/unchanged/unavailable/deferred/failed counts, last
successful scheduled cycle, source error rates, oldest due item and coverage.
Distinguish infrastructure/authentication failures from unsupported source layouts.
Alert once per actionable incident with recovery; do not page on every known
unsupported PDF. Until separate coverage monitoring exists, keep current failure
signals rather than suppressing them to make jobs green.

UI reads only published DB snapshots. Use statement period and basis as primary
context; retrieval time must not imply the underlying financial year changed.
During a failed refresh retain values and expose an unobtrusive availability note
where needed. Do not replace missing financials with zero or show an indefinite
spinner while a background parser runs.

## Acceptance and rollout

- Unit tests: precedence, exact replacement match, checksums, unsafe URL rejection,
  cancellation, missing units/periods/basis, and last-good retention.
- DB tests: concurrent API-style reads/writes and rollback on failed replacement.
- Hosted smoke: repaired financial run finishes without source-transfer failure;
  inspect its coverage counts, not only the green badge.
- Scheduled acceptance: next due automatic cycle succeeds; watchdog recovery is
  verified independently of a manual run. GitHub delivery remains best effort.
- Future checkpoint/cache work: test restart/lease recovery, source-version
  invalidation, Retry-After, stale cache, and migrations/rollback in staging first.

Current incident evidence and the exact reviewed SEBI source are recorded in
[incident-32-rhp-recovery.md](incident-32-rhp-recovery.md). Missing Zetwerk financial
coverage is distinct from a repaired HTTP/download failure.
