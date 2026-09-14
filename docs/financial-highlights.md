# Financial ingestion operator notes

Issue #15; API issue #41 and frontend issue #43. Phase 21 continuation.
Full contract and review evidence: [backend guide](https://github.com/anish689/OnlyIPO/blob/codex/financial-highlights/docs/financial-highlights.md).

Upstox consolidated annual income statements take priority, then standalone. Only
insufficient coverage falls back to the conservative RHP parser. Request/schema
failures retain last-known-good data. An HTTP 200 with no history is not usable data.
Apply API migration `20260914132659_AddIpoFinancials` before enabling this feature.

Development mode loads existing `ipoonly-scheduler-local` user-secrets. No token goes
in appsettings or git. `Financials:Enabled` defaults false. Preview reads sources and
the existing IPO identity, but writes nothing:

```sh
DOTNET_ENVIRONMENT=Development Financials__Slug=qualiance-international-limited-ipo dotnet run --project src/IPOOnly.Scheduler -- --financials-preview
```

Persist one company's validated set:

```sh
DOTNET_ENVIRONMENT=Development Financials__Enabled=true Financials__Slug=qualiance-international-limited-ipo dotnet run --project src/IPOOnly.Scheduler -- --financials-once
```

Replace the slug with an existing Upstox-sourced IPO. Commands have a three-minute
deadline and nonzero failure exit code. Regular IPO sync also supports the feature
when explicitly enabled, skipping successful sets younger than 24 hours. Empty
coverage has no negative cache yet; consider provider traffic before enabling it.
Only Qualiance and Maharaja & Speedex were backfilled for the local review.

RHP supports explicit same-page annual statements, basis/units/full dates and aligned
numeric rows. Unsupported/scanned layouts, conflicting/footnoted values and unapproved
hosts are withheld, not guessed. The regression suite exercises PDF extraction with
a controlled PDF; it does not certify every issuer's document layout. Page citations
are stored. Existing RHP offer facts are unaffected. No DRHP financial publication.

Run `dotnet test --no-restore`. Tests include source ordering, malformed responses,
negative/zero figures, bounded downloads and fallback parsing. The PostgreSQL table
and API integration tests live in the backend repo. News remains default-off and
no paid services or OS schedules were created. Stop for local review before merge.
