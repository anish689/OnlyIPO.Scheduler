# Release refresh monitoring

13 October 2026. Staging only; no production secrets or costs introduced.

The watchdog inspects all six scheduled partitions independently after refresh
completion and hourly at minute 41 UTC. A missing successful cycle after two
hours, or a latest failed partition, produces a nonzero check and one maintained
GitHub incident issue. Recovery comments and closes that incident. It reads only
Actions metadata, not ingestion logs, database credentials or account records.
Only the watchdog job gets issues:write, for this repository's operational issue.
It checks main code only and does not execute workflow-run artifacts.

Subscribe to repository/incident notifications and verify actual delivery.
GitHub scheduling can delay BOTH refresh and watchdog: this is best-effort
monitoring, not an independent availability guarantee. Keep external API monitoring
and review the issue/run history. A successful worker does not prove source data
is current, available or complete; provider observation dates remain separate.

The 13 October assessment found 179 populated, 28 unavailable and one failed
financial target in run 38312648961. Zetwerk's RHP returned 403. Keep the failure
visible and preserve last-good values. Obtain a corrected authorized document URL
from the provider; do not scrape around or bypass the restriction. No fabricated
financials or blanket success conversion is part of this fix.

Acceptance: unit tests cover per-partition failure, grace boundaries, stale/missing
runs, manual-run exclusion, wrong branch, future completion and recovery. After
merge, dispatch the watchdog and verify its incident and notification delivery.
Rollback: disable the watchdog workflow; ingestion and DB schema are unchanged.

## Verification and remaining release gates

Local verification on 13 October: seven monitoring tests passed; scheduler tests
reported 113 passed and one PostgreSQL concurrency test skipped because no isolated
test connection was supplied. CI supplies PostgreSQL and must pass that test before
merge. Local package vulnerability lookup could not reach NuGet; this is not a
clean dependency-audit result.

The owner confirmed there is no separate production database yet. Do not point a
production deployment at staging or copy staging user records into production.
The owner approved an encrypted staging backup and restore into a new isolated
local database. Execution remains pending a privately supplied export connection;
the available scheduler development connection targets localhost only.

Backup acceptance must include schema/migration parity, per-table row counts,
constraints/indexes, and application smoke checks against the isolated restore.
Store the encryption key separately from the backup, outside Git and logs. Record
backup age and measured restore duration before agreeing RPO/RTO. A successful
dump alone is not a successful recovery rehearsal.
