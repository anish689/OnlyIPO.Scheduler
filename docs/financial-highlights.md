# Financial ingestion operator notes

Maintained guide; reviewed 27 November 2026. Owner: scheduler maintainer. [Financial source strategy](financial-refresh-strategy.md) retains design/incident evidence; [operations](operations-guide.md) describes current failure classification.

The hosted financials partition runs separately from catalogue/tracking with `Financials__Enabled=true` and `Financials__SkipFresh=true`; other hosted partitions disable financial enrichment. Successful sets younger than 24 hours can be skipped. Upstox consolidated annual statements take priority, followed by standalone; insufficient valid coverage may use a conservative RHP parser. Request/schema/identity errors never justify fabricated replacement data.

Preview one existing local IPO without writing:

```sh
DOTNET_ENVIRONMENT=Development Financials__Slug='existing-local-slug' dotnet run --project src/IPOOnly.Scheduler -- --financials-preview
```

Replace the placeholder before running; preview still calls the provider. To persist use `Financials__Enabled=true` with `--financials-once`; `--financials-backfill` processes the eligible catalogue sequentially. Verify the local database and provider-call budget first. Hosted manual reruns use the selected workflow task instead of a duplicate long-running process.

Single-company operations have a three-minute deadline; the batch has a 45-minute deadline within the hosted 50-minute job budget. API/provider auth failures and HTTP429 stop the batch. **RHP-specific 403/404/410 count as unavailable coverage**, retain stored data and allow other companies to proceed. RHP5xx, other transfer/schema/database failures remain failures. This supersedes older notes that every issuer RHP403 necessarily fails the batch.

The parser requires supported annual statement layout, basis, units and dates, keeps page/source citations, and withholds ambiguous/scanned/conflicting values. No DRHP financial publication or guessed zeros. General RHP document-fact enrichment is a separate feature and remains disabled in hosted schedules. Reviewed official replacement URLs require their configured checksum and source identity.

Tests cover source ordering, unavailable versus failure handling, signs/units/basis, bounded transfer, malformed responses, retained data and controlled PDF extraction. They do not certify every issuer layout or current upstream completeness. Use recorded run counts only for the timestamp/revision that produced them; see [current release status](https://github.com/anish689/OnlyIPO/blob/main/docs/current-status.md).
