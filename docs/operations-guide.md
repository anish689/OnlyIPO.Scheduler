# Scheduler operations and data quality

Maintained guide; source reviewed 27 November 2026. Owner: scheduler/release maintainer. This supersedes the dated [technical handover](OnlyIPO-Technical-Handover.md) for current operation. Read [production isolation](production-isolation.md), [backup runbook](production-backups.md) and [cross-repository status](https://github.com/anish689/OnlyIPO/blob/main/docs/current-status.md) together.

## Pipeline and ownership

Upstox list/detail reads become stored source snapshots and the normalized IPO read model, timelines/documents/subscription observations. Tracking resolves listed instruments and daily prices. Financials store validated annual observations with source, basis/units and citations. The API owns migrations; this worker must not use database-owner credentials or infer a missing schema is permission to create it.

The React application calls the API, not Upstox or unapproved data providers. No GMP adapter or broker-application workflow is released. Retain evidence of source rights and do not assume a public quote endpoint grants redistribution permission. No paid provider is introduced.

## Execution modes

| Mode | Effect / boundary |
| --- | --- |
| `--run-once` | One catalogue/detail sync for configured statuses; writes data |
| `--tracking-once` | Bounded recent listed-price/instrument refresh; writes data |
| `--financials-preview` | Inspect one selected company's validated candidates without persistence; still makes provider calls |
| `--financials-once` | Persist one selected financial set; feature/slug settings required |
| `--financials-backfill` | Sequential bounded financial batch; SkipFresh avoids recent successful work |
| no one-shot argument | Periodic worker with configured interval/jitter; local option, not a second hosted writer |
| News modes | Explicit collection/review/publish/withdraw tooling; not part of hosted release schedule |

See [Program.cs](../src/IPOOnly.Scheduler/Program.cs), [SchedulerOptions](../src/IPOOnly.Scheduler/SchedulerOptions.cs) and [appsettings](../src/IPOOnly.Scheduler/appsettings.json). Missing token/connection, incorrect production destination or excessive connection-pool settings must fail rather than fall back to staging.

## Workflow contract

| Workflow | Requested UTC schedule | Identity/activation | Failure boundary |
| --- | --- | --- | --- |
| staging-refresh | 04:17 and 13:17 daily | main, staging environment, `STAGING_SCHEDULER_ENABLED` | Serial six partitions; fail-fast false |
| production-refresh | 07:07 and 16:07 daily | main, production environment, `PRODUCTION_SCHEDULER_ENABLED` | Separate concurrency; manual task/all supported |
| staging-refresh-watchdog | hourly :41 and refresh completion | staging flag; Actions metadata only | Two-hour grace; all six scheduled partitions |
| production-refresh-watchdog | hourly :53 and refresh completion | production flag; Actions metadata only | Three-hour grace; production-only incident |
| production-backup | 19:43 daily | separate main-only production-backup environment and enable/schedule flags | 25-minute budget; validated snapshot/restore/encrypted offsite verification |
| production-backup-watchdog | 22:13 daily and backup completion | schedule flag; no database credentials | Checks actual verified-upload success, not merely green/skipped run |

These are requested starts, not guarantees or continuous availability monitoring. Manual runs do not satisfy scheduled-refresh acceptance. Concurrent production refreshes serialize rather than cancel; maximum per-partition budgets are 10/10/10/20/30/50 minutes. All status-array entries are overridden deliberately because configuration binding can retain defaults. Watchlist reads are explicitly disabled in hosted tracking. News and general document enrichment are off; financial RHP fallback is a distinct enabled partition.

## Financial coverage and failure handling

`FinancialBatch` distinguishes populated, unavailable and failed. RHP-specific 403/404/410 retain values and count unavailable coverage without failing the whole job solely for that issuer. Upstox/provider authentication errors, 429, RHP 5xx, database/identity/schema errors and transfer failures are not blanket-converted into success. Never scrape around a restriction, fabricate values or silently select an unreviewed replacement URL.

Consolidated annual statements take priority, then standalone. Only insufficient valid coverage enters the conservative RHP fallback. Explicit reviewed replacement URLs must match configured identity/checksum; validation failure retains last-good data. Successful sets less than 24 hours old can be skipped. Historical backfill counts describe that run, not today's catalogue or freshness.

When a workflow fails: inspect the affected partition and sanitized source code/status, distinguish access/token/rate-limit/database/coverage causes, honor provider backoff, and keep previously valid data. Verify corrected source/credentials privately, then dispatch only the necessary bounded task. Record whether the watchdog observed scheduled recovery and whether the owner received an alert. Do not print credentials or raw personal payloads.

## Release and recovery

Source code, enabled repository variables, provider run status and persisted completeness are separate evidence. Production scheduling was enabled and scheduled watchdog recovery/owner notification accepted in the 22–24 November release evidence; the older manual-only plan is superseded. Re-read actual run history for a fresh readiness decision instead of assuming schedules have remained healthy.

Backup activation/first manual recovery was accepted 27 November; the first daily cron run is not yet evidence at that checkpoint. Follow the backup runbook for lost OAuth authorization, missing RLS grants, quota exhaustion, retention and isolated restore. No backup decrypt key in GitHub/Drive, no production restore during tests, no paid capacity escalation. Account deletion reconciliation is owned by the API runbook.
