# Phase 17: Upstox Daily Tracking

Story: https://github.com/anish689/OnlyIPO.Scheduler/issues/9
API story/migration: https://github.com/anish689/OnlyIPO/issues/33
Frontend: https://github.com/anish689/only-ipo-web/issues/28

Branch: `codex/9-upstox-tracking`. Apply API migration `20260908175314_AddPostListingTracking` before running this job.

Run once: `DOTNET_ENVIRONMENT=Development dotnet run --project src/IPOOnly.Scheduler -- --tracking-once`.

Enable the worker with `Tracking__Enabled=true` on the existing long-running scheduler. It runs on startup and every `Tracking:RefreshHours` (default 24). It is disabled by default until the migration is deployed. No new OS scheduler/service is installed.

`Upstox:MarketDataToken` overrides the existing `Upstox:AnalyticsToken`; the latter was verified locally for both required APIs. Secrets ID: `ipoonly-scheduler-local`. Connection string: `ConnectionStrings:IPOOnlyDatabase`. Do not expose credentials in browser code or source control.

`IMarketDataClient` uses official Upstox Instrument Search and Historical Candle V3. Exact ISIN/exchange matching takes priority; exact symbol/exchange is allowed only without ISIN. Search is fully paginated with a ten-page maximum. An incomplete/ambiguous result does not publish a mapping. Existing instrument keys cannot be silently replaced.

Recent IPOs (90 calendar days) and older watchlisted IPOs are refreshed. Today is determined in India; only prior-date daily candles are persisted. Incremental requests overlap five days. Older saved IPOs get a separate exact listing-date query. No assumed holiday candles or later-day listing prices are generated.

`ITrackingStore` separates orchestration from PostgreSQL. The adapter writes raw JSON/checksum snapshots and daily OHLCV in one transaction; instrument/date uniqueness handles repeated runs. A database advisory lock prevents overlapping tracking jobs. HTTP 429/5xx uses bounded retries; one IPO failure preserves prior prices and allows other IPOs to continue. Exit code 1 denotes per-IPO failures.

The raw source table is shared with IPO ingestion. Market snapshots use a `market:` record prefix and a distinct endpoint. The existing IPO job remains independently scheduled.

Local live verification on 9 September 2026: 109 candidates, 108 processed, one unmatched, zero failed; 2,800 candles. Repeat run kept 2,800 candles with zero duplicate instrument/date rows. The unresolved IPO was waterways-leisure-tourism-limited-ipo; do not guess its mapping.

Run `dotnet test` before publication. Parser tests cover prices/dates/volume, identity tests cover ambiguity and ISIN conflicts, and service tests check per-IPO failure isolation. The API suite tests the migration and unique index in PostgreSQL. Full runbook and SQL: API repo `docs/track-ipo-technical-guide.md`. No delivery/anchor/GMP data or paid providers in this phase.
