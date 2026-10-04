# Hosting phase H1: free-only one-shot execution

## Refresh availability safeguard: 4 October 2026

Financial downloads and PDF parsing run on the separate GitHub Actions worker,
before a database replacement transaction starts. The API only reads persisted
data; it does not wait for the whole financial job. A validated company snapshot
is replaced atomically, so failure retains previous values. The worker processes
companies sequentially with a small connection pool; no maintenance mode or
catalogue-wide transaction is used.

Snapshot serialization now uses FOR NO KEY UPDATE, permitting concurrent foreign
key checks such as watchlist inserts. Transaction-local limits bound lock waits
to 2 seconds, each SQL statement to 5 seconds, and idle transactions to 10 seconds.
These settings do not alter API connections or global PostgreSQL configuration.
114 tests pass, including a real PostgreSQL isolated-schema test for concurrent
watchlist insertion, reads/activity writes during contention, lock timeout and
rollback after a failed replacement. CI runs the same test with PostgreSQL 15.
This reduces application-induced blocking; free-host cold starts, outages and
shared-database resource limits still prevent a zero-downtime guarantee.

PostgreSQL lock compatibility reference:
https://www.postgresql.org/docs/current/explicit-locking.html#LOCKING-ROWS

## Financial-loading bugfix: 4 October 2026

Scheduled IPO/price runs through 3 October passed, but financial ingestion was
explicitly disabled. The workflow now includes a separate `financials` partition
and manual task using the existing Upstox-first, validated RHP-fallback pipeline.
It has a 45-minute application deadline and 50-minute job ceiling. Successful
sets younger than 24 hours are skipped; empty or failed sources retain existing
values and remain eligible for retry. Missing coverage is not fabricated.
PRs #26 and #27 are merged, with 113 tests and CI passing. First backfill
37144383009 populated 167 companies before a source 403 stopped processing.
Rerun [37174791115](https://github.com/anish689/OnlyIPO.Scheduler/actions/runs/37174791115)
passed in 2m25s: 167 fresh sets skipped, 36 attempted, 11 populated, 25 unavailable,
zero failed. Total coverage is 178 of 203 companies, not universal coverage.
The earlier 403 did not recur. Source-specific diagnostics now identify failures;
provider auth errors and any rate limit stop processing, while a denied individual
RHP records a failed company and continues. Failed batches still exit nonzero.
No new credentials, privileges or paid service.
The older PDF/financial-disabled notes below describe the initial H1 release;
general document-fact enrichment remains disabled, financial RHP fallback does not.

Tracking: https://github.com/anish689/OnlyIPO/issues/47
Authoritative release runbook: OnlyIPO API repository,
`docs/hosting-phase-h1-release.md`.

`.github/workflows/staging-refresh.yml` requires main, a `staging` GitHub
environment and its two scoped secrets. Manual dispatch on main is allowed
without the repository opt-in variable; recurring runs require the explicit
`STAGING_SCHEDULER_ENABLED=true` variable.
Current state (4 October 2026): opt-in is true and the workflow is active.
Manual IPO run 36320177270 and tracking run 36419871496 passed before activation.
The latter updated 128 of 129 candidates, with one unmatched instrument and zero
failures. Automatic runs 37140114102 and 37114621361 on 3 October also passed.
Observed start times can be hours later than nominal cron times. On a new
environment, keep opt-in false until equivalent checks pass.

Only the scheduled workflow may write the staging catalogue. Concurrency protects
this workflow, not other processes. Use Supabase session pooler port 5432 for
tracking's session advisory lock, TLS verification and small Npgsql pool size.
Do not use transaction mode or the Supabase administrator as the runtime role.

The staging connection secret must include `SSL Mode=VerifyFull;Root
Certificate=/tmp/onlyipo-supabase-ca.crt;Maximum Pool Size=5`. The workflow
downloads the public CA from the link supplied by the project's Supabase
Database Settings and checks its pinned SHA-256 and expiry before the worker
receives credentials. A changed or unavailable certificate fails the run;
review a provider rotation before updating the pin. Never disable TLS validation
to work around a failure. Render uses a different path, `/etc/secrets/`, so do
not reuse its connection string for Actions.

This follow-up is part of anish689/OnlyIPO#47. The runtime staging roles and
15 migrations have been verified. Manual IPO run 36320177270 passed all four
status partitions on 27 September 2026.

The initial manual run reached its ten-minute limit while successfully fetching
provider records. Writes are incremental, so that run must not be described as
a completed catalogue refresh. IPO refresh now runs four sequential jobs, one
per status (open, upcoming, closed, listed), each with a ten-minute limit.
Fail-fast is disabled so one failed status does not prevent the others. All four
must pass before calling an IPO refresh complete. Tracking remains one job.
The first partitioned run (36303892724) exposed a configuration-binding bug:
the binder appended configured statuses to the prepopulated options array.
Defaults now live only in appsettings.json, with startup validation and five
regression cases for scoped and full-catalogue binding. Failed/timed-out runs
are not release validation, even when they have written some valid records.
The maximum IPO run budget is 40 runner minutes, plus 30 for tracking and 50
for financials. These are ceilings, not observed runtimes.
The repository was verified as already PUBLIC on 28 September 2026; no visibility
change was made. Standard ubuntu-latest runners are used, not paid larger runners.

## Recurring refresh and restricted tracking bugfix

The approved timetable is 09:47 and 18:47 IST daily (04:17 and 13:17 UTC).
Each scheduled execution refreshes open, upcoming, closed and listed IPOs,
then prices for listings in the last 90 days and due financial sets. GitHub schedules are best-effort,
not an exact-time guarantee. The variable remains the recurring-run kill switch;
manual dispatch remains available when it is false.

Tracking previously failed because its query referenced WatchlistItems. The
staging workflow now sets Tracking__IncludeWatchlisted=false and the generated
query does not reference that table at all. No private user data privileges are
added. Older watched companies are therefore NOT refreshed in staging; existing
full-access installations retain their prior behavior by default. A disabled
SQL predicate alone would not remove PostgreSQL's permission requirement.

Tracking run 36373948986 exceeded the initial 10-minute ceiling without a
completion summary and is not a successful validation. The follow-up adds
candidate/progress diagnostics (no secrets or user records) and a 25-minute
application deadline inside a 30-minute job limit. A busy advisory lock exits
nonzero instead of treating skipped work as successful. Replacement run
36419871496 completed successfully in 12m55s before recurring activation.

## Where it runs and how to operate it

GitHub creates a temporary standard Ubuntu runner for each job, checks out main,
installs .NET 8, publishes the application, verifies the Supabase CA and executes
`--run-once` for each IPO status or `--tracking-once` for prices. The runner exits
when the job ends. There is no always-on scheduler on Netlify or Render and no
external cron pinger. Actions connects directly to Upstox and the restricted
Supabase staging role; the frontend reads the stored results through the API.

Open GitHub Actions > Staging data refresh (opt-in) > Run workflow, select main,
then `ipo` or `tracking` for a manual refresh. Inspect all job conclusions and
the final summary, not just partial database writes. Use repository Settings >
Secrets and variables > Actions to set `STAGING_SCHEDULER_ENABLED=false` to stop
future recurring jobs; that does not cancel a job already running.

Secrets live in the main-only `staging` environment: database connection and
Upstox token. Rotate expired credentials there, never in source or logs. Runs
are serialized by the workflow concurrency group; do not point local writers
at the same staging database. Enable failure notifications on the owner account.

PDF and
financial ingestion disabled in the free recurring job; those need a bounded
manual plan. Monitor failures and stale data. Never copy production users or
print secrets. Do not change repository visibility to obtain free runner time.
