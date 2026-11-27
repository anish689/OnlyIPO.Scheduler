# YourIPO scheduler and production backups

Separate .NET market-data worker for YourIPO, plus isolated encrypted application-backup tooling. Maintained entry point; source reviewed 27 November 2026. Start with the [documentation index](docs/documentation-index.md) and [operations guide](docs/operations-guide.md).

Companion repositories: [API/schema](https://github.com/anish689/OnlyIPO) and [frontend](https://github.com/anish689/only-ipo-web). [Current release status](https://github.com/anish689/OnlyIPO/blob/main/docs/current-status.md) owns application deployment/launch evidence.

## Runtime and local use

The worker currently targets `net8.0`, with SDK 8.0.424 in [global.json](global.json). This is an observed dependency, **not a supported release recommendation**: .NET 8 support ended 10 November 2026. [Coordinated upgrade issue #109](https://github.com/anish689/OnlyIPO/issues/109) remains open.

```sh
dotnet restore IPOOnly.Scheduler.sln
dotnet build IPOOnly.Scheduler.sln --no-restore
dotnet test IPOOnly.Scheduler.sln --no-build
```

Use a separate local PostgreSQL database prepared with API migrations. Privately configure `ConnectionStrings:IPOOnlyDatabase` and `Upstox:AnalyticsToken` in .NET user-secrets for `src/IPOOnly.Scheduler`, or equivalent environment variables. Development mode loads the local secrets. Never print completed connections or provider tokens.

```sh
DOTNET_ENVIRONMENT=Development DocumentEnrichment__Enabled=false dotnet run --project src/IPOOnly.Scheduler -- --run-once
```

This command fetches provider data and **writes the configured database**. Verify the local destination and absence of another writer first. Running without a one-shot flag starts the local periodic worker; it is not how hosted production is scheduled. Source defaults include a ten-minute interval, 45-second maximum jitter, page size 30 and open/upcoming/closed/listed statuses. General document enrichment defaults true locally but is explicitly false in hosted refresh workflows.

## Hosted operations

[Staging/production isolation](docs/production-isolation.md) and [refresh readiness](docs/production-refresh-readiness.md) describe the six serial partitions and their separate main-only environments. Staging requests daily starts at 04:17/13:17 UTC; production at 07:07/16:07 UTC. Activation flags, GitHub delays and watchdog evidence are distinct from cron configuration. Do not deploy a duplicate continuous writer or a Render keepalive.

Financials use consolidated then standalone Upstox statements, with validated RHP fallback when coverage is insufficient. Only document-scoped RHP 403/404/410 are unavailable coverage; systemic errors remain failures. Last-good observations remain and missing values are not invented. A successful job does not certify every issuer's source data. News/general document enrichment are not part of the hosted scheduled partition set.

## Production recovery

[Production backups](docs/production-backups.md) run independently of market ingestion using a dedicated audited read-only identity. The first automated workflow was manually accepted on 27 November, including an independent Drive-download/decrypt/restore. Daily scheduling and 7-daily/4-weekly retention are enabled; the first scheduled trigger remains a separate pending observation at that checkpoint.

Snapshots include committed application/job results, not GitHub logs, external PDFs or full Supabase platform/configuration recovery. Encryption precedes upload; private recovery keys never enter CI/Drive. Existing standard public-repository runners and existing Drive storage are used; no paid service/billing is enabled.

## Contributing and validation

Use [the development workflow](docs/development-workflow.md): issue, focused branch, reviewable PR, checks and recorded promotion/rollback. CI includes scheduler tests with PostgreSQL, monitoring policy checks and fictional isolated PG17 backup/restore/encryption tests. Do not call a skipped database test a pass or run live production work to validate documentation.
