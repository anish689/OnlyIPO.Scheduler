# Hosting phase H1: free-only one-shot execution

Tracking: https://github.com/anish689/OnlyIPO/issues/47
Authoritative release runbook: OnlyIPO API repository,
`docs/hosting-phase-h1-release.md`.

`.github/workflows/staging-refresh.yml` requires main, a `staging` GitHub
environment and its two scoped secrets. Manual dispatch on main is allowed
without the repository opt-in variable; recurring runs require the explicit
`STAGING_SCHEDULER_ENABLED=true` variable.
It cannot run automatically merely because this PR merges. Keep opt-in false
until migrations, least-privilege connection, token and account quota are checked.

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
The maximum IPO run budget is 40 runner minutes, plus 10 for tracking.
The repository was verified as already PUBLIC on 28 September 2026; no visibility
change was made. Standard ubuntu-latest runners are used, not paid larger runners.

## Recurring refresh and restricted tracking bugfix

The approved timetable is 09:47 and 18:47 IST daily (04:17 and 13:17 UTC).
Each scheduled execution refreshes open, upcoming, closed and listed IPOs,
then prices for listings in the last 90 days. GitHub schedules are best-effort,
not an exact-time guarantee. The variable remains the kill switch; enable it
only after the restricted-role tracking manual smoke test passes.

Tracking previously failed because its query referenced WatchlistItems. The
staging workflow now sets Tracking__IncludeWatchlisted=false and the generated
query does not reference that table at all. No private user data privileges are
added. Older watched companies are therefore NOT refreshed in staging; existing
full-access installations retain their prior behavior by default. A disabled
SQL predicate alone would not remove PostgreSQL's permission requirement.

PDF and
financial ingestion disabled in the free recurring job; those need a bounded
manual plan. Monitor failures and stale data. Never copy production users or
print secrets. Do not change repository visibility to obtain free runner time.
