# Production scheduler isolation

Updated: 18 October 2026. Production scheduling is not provisioned or enabled.

The canonical inventory is in the API repository's
[current status](https://github.com/anish689/OnlyIPO/blob/codex/production-isolation/docs/current-status.md)
and [production runbook](https://github.com/anish689/OnlyIPO/blob/codex/production-isolation/docs/production-isolation.md).
Historical incident documents retain their original observations.

## Current staging operation

GitHub Actions runs the one-shot scheduler; it is not a Render background worker.
The staging refresh workflow requests runs at 04:17 and 13:17 UTC daily
(09:47 and 18:47 India time). GitHub may delay scheduled starts.
Open, upcoming, closed, listed, tracking and financials run as six bounded
partitions, sequentially, without cancelling other partitions on failure.
News and general document enrichment are not scheduled.

The watchdog checks hourly at minute 41 and after refresh workflows, using a
two-hour grace period. It reports completeness of job execution, not guaranteed
availability of all provider financial data. Existing validated financial values
are retained when a refresh cannot obtain valid replacement data.

The 16 October scheduled refresh 38878001397 completed all six partitions.
Watchdog run 38896694151 subsequently passed. This does not certify future runs.

## Production gate

Do not reuse the staging database connection or staging runtime identity.
Provision a main-only production environment with separately scoped secrets and
a least-privilege ingestion role with no user/session-table access. Keep writes
disabled until the new production schema and permissions are verified.

Before adding an enabled production schedule, run each partition manually against
the new database and verify row freshness and non-destructive fallback. Adapt
the watchdog to a distinct workflow, environment and incident title so staging
success cannot conceal a production failure. A manual-only production refresh
workflow is prepared on the production-isolation branch, but not deployed or run.
It supports each of the six partitions, isolated concurrency, production-only
secret names, verified TLS, and a startup guard rejecting incorrect database
identities and pools exceeding three connections. On 18 October the approved
production environment was created and verified to allow only branch main, not
tags. Secret setup remains pending. There is no production watchdog yet.
Check Actions allowances before enabling
additional runs; no paid capacity is authorized.

## Secret handoff (owner entry only)

`PRODUCTION_DATABASE_CONNECTION` uses the production session pooler:

```text
Host=aws-0-ap-south-1.pooler.supabase.com;Port=5432;Database=postgres;Username=youripo_scheduler.vzbrqhpqawyenwxauztq;Password="<private scheduler password>";SSL Mode=VerifyFull;Root Certificate=/tmp/onlyipo-supabase-ca.crt;Maximum Pool Size=3;Minimum Pool Size=0;Timeout=15
```

Enter the real value only in GitHub's production environment secret field.
Quote passwords containing connection-string delimiters and double embedded
double quotes. Never put a completed connection string in Git or chat.
`PRODUCTION_UPSTOX_TOKEN` must be an owner-approved provider credential; staging
approval does not automatically authorize transferring that token to production.

Verification on 18 October: 13 connection-guard tests passed; scheduler suite
137 passed and one database concurrency test skipped without its test database.
Package vulnerability lookup reported NU1900; this run is not a completed
dependency vulnerability audit.
