# Scheduler runtime upgrade — 27 November 2026

Owner: application maintainer. Tracks [scheduler #68](https://github.com/anish689/OnlyIPO.Scheduler/issues/68) and the coordinated [API release gate #109](https://github.com/anish689/OnlyIPO/issues/109).

The worker and tests target .NET 10 LTS. `global.json` pins SDK 10.0.404 with patch-only roll-forward and no preview SDKs. Microsoft hosting/HTTP/syndication libraries use 10.0.15; Npgsql uses 10.0.4. CI and both existing refresh workflows read the SDK pin from the same file. See the [API upgrade record](https://github.com/anish689/OnlyIPO/blob/main/docs/runtime-upgrade-2026-11-27.md) for vendor compatibility references.

No source-ingestion logic, six-partition selection, refresh schedules, non-cancelling concurrency, TLS/CA settings, database roles, secrets, upstream tokens or email scope changes. Python backup tooling and its schedule/retention remain independent. No keepalive, paid runner or paid service is introduced.

Local Release build and all **144** scheduler tests passed, including real PostgreSQL financial-store concurrency checks in a newly created disposable local database; **zero skipped**. All **22** monitoring/workflow/backup-watchdog policy tests passed. Transitive NuGet vulnerability checking passed and is now a required CI restore gate. CI also runs the existing fictional PostgreSQL backup/encryption/restore tests.

Require the exact reviewed PR's CI before merging. Run the staging refresh workflow and verify its partition results before production acceptance; do not treat a main-branch merge as hosted acceptance. Preserve the pre-upgrade main revision `0832e6902cf84aea73a4c741bd5283b3b43ca8b2` for an emergency source rollback. No database migration or backup-format change is required. Recorded run IDs, conclusions and final release status belong in the shared [release index](https://github.com/anish689/OnlyIPO/blob/main/docs/current-status.md).

The first daily backup was manually accepted earlier on 27 November. Its first scheduled execution remains distinct and is due at 19:43 UTC / 01:13 IST the following day; this runtime upgrade does not manufacture or replace that evidence.
