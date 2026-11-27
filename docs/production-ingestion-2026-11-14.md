# Production ingestion acceptance: 14 November 2026

<!-- historical-status -->
> **Historical record / plan.** Preserved for its original revision and dates; this is not current deployment instruction or proof of present readiness. Use the [documentation index](documentation-index.md) and current release status. Do not execute old mutations or resume stopped jobs from this record.


Updated 15 November: five partitions passed; financials completed with one
reported source failure. Not approval for public release or automatic scheduling.

## Scope and isolation

- Repository: `anish689/OnlyIPO.Scheduler`; environment: `production`.
- Deployment policy allows branch `main` only, not tags.
- Workflow: `production-refresh.yml`, manual dispatch only, sequential partitions.
- Tested worker revision: `bd5e09ff0f04552d6d721104606a84ed35687c97`.
- Both `PRODUCTION_DATABASE_CONNECTION` and `PRODUCTION_UPSTOX_TOKEN` names
  verified. Values were entered/transferred privately, never committed or logged.
- Database is Supabase production `vzbrqhpqawyenwxauztq`, with the
  `youripo_scheduler` runtime identity and pool limit three. The provider token
  is shared with staging only with the owner's explicit approval.
- Pinned Supabase CA, certificate verification, production identity guard and
  main-only workflow guard remain enabled. No grants were broadened.
- Staging, DNS, billing, production authentication and email settings unchanged.

## Manual run evidence

| Partition | Run | Outcome |
| --- | --- | --- |
| Open | [43559209268](https://github.com/anish689/OnlyIPO.Scheduler/actions/runs/43559209268) | Passed; fetched 0, upserted 0 |
| Upcoming | [43574696282](https://github.com/anish689/OnlyIPO.Scheduler/actions/runs/43574696282) | Passed; fetched 22, upserted 22 |
| Closed | [43575085557](https://github.com/anish689/OnlyIPO.Scheduler/actions/runs/43575085557) | Passed; fetched 0, upserted 0 |
| Listed | [43575197124](https://github.com/anish689/OnlyIPO.Scheduler/actions/runs/43575197124) | Passed; fetched/upserted 152 |
| Tracking | [43626654114](https://github.com/anish689/OnlyIPO.Scheduler/actions/runs/43626654114) | Passed; 152 eligible/updated, 0 unmatched, 0 failed |
| Financials | [43626661597](https://github.com/anish689/OnlyIPO.Scheduler/actions/runs/43626661597) | Failed with partial success; 174 attempted, 152 populated, 21 unavailable, 1 failed |

Read-only production verification on 15 November found 174 IPOs and 1,608 saved
Upstox financial observations across 152 companies. Latest financial retrieval:
2026-11-14T19:05:43.989758Z. Antala Industries' RHP returned HTTP 403; existing
values were retained and other companies continued. No access-control bypass,
fabricated values, or failure reclassification was introduced. A reviewed
official document replacement remains pending. Source coverage is not the same
as successful job execution, and zero/unavailable results must not become zero
financial figures. No production automatic schedule or watchdog was enabled.

Zero-row partitions reflect the provider response, not invented records or proof
of coverage outside that response. IPO enrichment is disabled for catalogue
partitions; financial enrichment is a separate bounded job. No emails are sent.

## Availability and tests

The production Cloudflare home displayed 22 upcoming IPOs after ingestion, with
real company names and unavailable values explicitly marked Awaited. A direct
home request during the upcoming run timed out after 90 seconds with no bytes.
Subsequent health and home requests returned HTTP 200 in 0.97 and 1.31 seconds.
This is an unresolved availability observation, not proof of database contention
or a permanent recovery. Previous edge challenges and free-tier cold starts
remain release risks; a green scheduler job does not certify frontend uptime.

Local scheduler suite: 138 passed, zero failed/skipped after running against a
new isolated PostgreSQL 15 cluster (private Unix socket, no TCP listener).
The initial no-database run skipped one test; the complete rerun exercised
financial reads/watchlist writes during refresh, bounded lock contention, and
retention of old values after replacement failure. The temporary server was
stopped afterwards. Watchdog-policy tests: seven passed. Local NuGet
vulnerability lookup warned NU1900; this is not a completed dependency
vulnerability audit. Hosted Actions also warns of Node 20 action-runtime
deprecation; the completed runs succeeded, but action upgrades need a separate
tested change.

## Release gates

Complete all six partitions and inspect persisted counts/freshness, financial
availability and retained-data fallback. Then review a production-specific
schedule and watchdog, free Actions allowance, and failure-notification delivery.
Do not infer production health from staging runs or enable duplicate writers.

Final-domain DNS/TLS, authentication/recovery, real-data desktop/mobile checks,
proxy reliability, off-machine backup/key custody and owner go-live approval
remain separate gates. Keep News, reminders and public account access disabled
until their production acceptance is complete. No staging account data was
copied into production.
