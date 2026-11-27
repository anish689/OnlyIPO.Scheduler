# Staging and production scheduler isolation

Maintained runbook; reviewed 27 November 2026. Owner: scheduler/release maintainer. The [historical manual-only setup](history/production-isolation-2026-11-22.md) is retained for audit. [Operations](operations-guide.md) and [current release status](https://github.com/anish689/OnlyIPO/blob/main/docs/current-status.md) take precedence for current use.

## Separate boundaries

`staging` and `production` GitHub environments permit main deployments, use separate database connections and runtime ingestion roles, and have separate concurrency groups and watchdog incident identifiers. The production environment's secrets are `PRODUCTION_DATABASE_CONNECTION` and `PRODUCTION_UPSTOX_TOKEN`; staging uses its workflow-defined staging secrets. The owner explicitly approved reuse of the provider's single account token. Database identities remain isolated and cannot read users, sessions, notification preferences or email tables.

Production's reviewed connection targets the Supabase session pooler in ap-south-1, database postgres, identity `youripo_scheduler.vzbrqhpqawyenwxauztq`, maximum pool size 3, verified TLS and the workflow-installed trusted CA. Keep the completed connection private; quote delimiter-containing passwords correctly. Never substitute postgres/API credentials or switch off certificate verification to resolve a failure.

`production-backup` is a different main-only environment and role. Its read-only account-data access is necessary for recovery and must never be copied to the market scheduler. [Backup documentation](production-backups.md) defines its secrets, narrow Google Drive scope, key separation and grant/RLS audit.

## Current operation versus activation

Both refresh workflows are implemented and production scheduled acceptance/owner watchdog notification were recorded in November. Repository enable flags control schedules/watchdogs; manual dispatch is separately available. The prior claim “no production watchdog yet” is superseded. Exact timing, budgets and grace periods live in [workflow contract](operations-guide.md).

Before activating a new environment or changing permissions, verify correct schema/identity/TLS, one manual run of each partition and data preservation, then a real scheduled cycle and alert receipt. Existing acceptance is dated, not a guarantee of current source completeness. Do not use a website deployment or green build as evidence of job execution.

To pause automatic refresh, set the relevant schedule variable false; this does not cancel an in-flight job or erase data. Use reviewed corrective code/credentials and bounded reruns for recovery. Never reset production data to match staging, run a duplicate periodic writer or add API keepalive traffic. Public launch/domain/email activation are outside scheduler rollout.
