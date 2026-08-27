# OnlyIPO Technical Handover

Last updated: 27 August 2026

This document explains the current OnlyIPO application stack, local setup, repositories, URLs, key files, data flow, development workflow, and operating steps. It is written so the project can be run and maintained without relying on AI support.

## 1. System Overview

OnlyIPO is an Indian IPO discovery and tracking application.

The system currently has three separate repositories:

| Area | Local Path | GitHub Repository | Purpose |
| --- | --- | --- | --- |
| Frontend web app | `/Users/anishtaneja/Desktop/Projects/only-ipo-web` | `https://github.com/anish689/only-ipo-web` | React/Vite browser app and app-like mobile UI |
| Backend API | `/Users/anishtaneja/Desktop/Projects/OnlyIPO` | `https://github.com/anish689/OnlyIPO` | ASP.NET API, PostgreSQL persistence, public IPO endpoints |
| Scheduler | `/Users/anishtaneja/Desktop/Projects/OnlyIPO.Scheduler` | `https://github.com/anish689/OnlyIPO.Scheduler` | Standalone .NET scheduler that fetches IPO data from Upstox and writes to the backend database |

High-level flow:

```text
Upstox IPO API
  -> OnlyIPO.Scheduler
  -> PostgreSQL database, table: ipos
  -> OnlyIPO backend API
  -> only-ipo-web frontend
```

The frontend must never receive the Upstox token. The token belongs only in backend/scheduler configuration.

## 2. Current Local Test URLs

Backend API:

- Summary: `http://127.0.0.1:5087/api/v1/ipos/summary`
- Open IPO list: `http://127.0.0.1:5087/api/v1/ipos?status=open&pageSize=10`
- IPO detail example: `http://127.0.0.1:5087/api/v1/ipos/esds-software-solution-limited-ipo`

Frontend web app:

- Local browser URL: `http://localhost:5173/`
- LAN URL shown by Vite on this machine: `http://192.168.1.5:5173/`

Current verified backend data after live Upstox sync:

```json
{
  "openIpos": 11,
  "upcomingIpos": 33,
  "mainboardIpos": 70,
  "smeIpos": 78,
  "latestDataRefresh": "2026-08-27T12:29:45.218908+00:00"
}
```

## 3. Required Local Software

Current machine setup:

- macOS
- Git
- GitHub CLI: `gh`
- Node.js/npm
- .NET SDK `10.0.100`
- PostgreSQL running locally

Verify tools:

```bash
git --version
gh auth status
node --version
npm --version
dotnet --version
```

GitHub SSH/auth was verified earlier using:

```bash
ssh -T git@github.com
gh auth status
```

## 4. Start the Backend API Locally

Repository:

```bash
cd /Users/anishtaneja/Desktop/Projects/OnlyIPO
```

Run:

```bash
DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development \
dotnet run --project src/IPOOnly.Api/IPOOnly.Api.csproj --no-build --urls http://localhost:5087
```

Backend configuration file:

`/Users/anishtaneja/Desktop/Projects/OnlyIPO/src/IPOOnly.Api/appsettings.json`

Important connection string key:

```json
{
  "ConnectionStrings": {
    "IPOOnlyDatabase": "Host=localhost;Port=5432;Database=ipoonly;Username=ipoonly;Password=ipoonly_dev_password"
  }
}
```

Health check:

```bash
curl -i http://127.0.0.1:5087/health
```

IPO summary check:

```bash
curl -i http://127.0.0.1:5087/api/v1/ipos/summary
```

## 5. Start the Frontend Locally

Repository:

```bash
cd /Users/anishtaneja/Desktop/Projects/only-ipo-web
```

Install dependencies:

```bash
npm install
```

Run with backend API:

```bash
VITE_API_BASE_URL=http://127.0.0.1:5087 npm run dev -- --host 0.0.0.0
```

Open:

```text
http://localhost:5173/
```

Frontend `.env` example:

`/Users/anishtaneja/Desktop/Projects/only-ipo-web/.env.example`

```text
VITE_API_BASE_URL=http://localhost:5087
```

Frontend scripts:

```bash
npm run dev
npm run build
npm run lint
npm test
npm run generate:api
```

## 6. Run the Scheduler Locally

Repository:

```bash
cd /Users/anishtaneja/Desktop/Projects/OnlyIPO.Scheduler
```

The scheduler is a standalone .NET console/worker app. It targets `.NET 10` because this machine currently has only the .NET 10 SDK/reference packs installed.

Set required user-secrets:

```bash
dotnet user-secrets set "ConnectionStrings:IPOOnlyDatabase" "Host=localhost;Port=5432;Database=ipoonly;Username=ipoonly;Password=ipoonly_dev_password" --project src/IPOOnly.Scheduler/IPOOnly.Scheduler.csproj
dotnet user-secrets set "Upstox:AnalyticsToken" "<UPSTOX_TOKEN>" --project src/IPOOnly.Scheduler/IPOOnly.Scheduler.csproj
```

Do not put the Upstox token in:

- Git
- `appsettings.json`
- `.env`
- README files
- Screenshots
- Logs shared publicly

Run one live sync:

```bash
DOTNET_ENVIRONMENT=Development \
dotnet run --project src/IPOOnly.Scheduler/IPOOnly.Scheduler.csproj -- --run-once
```

Run continuously:

```bash
DOTNET_ENVIRONMENT=Development \
dotnet run --project src/IPOOnly.Scheduler/IPOOnly.Scheduler.csproj
```

The successful live run fetched and upserted `142` IPO records.

## 7. Backend API Endpoints

Base URL:

```text
http://127.0.0.1:5087
```

Endpoints:

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/api/v1/ipos` | Paginated IPO list |
| `GET` | `/api/v1/ipos/{slug}` | IPO detail by slug |
| `GET` | `/api/v1/ipos/summary` | Dashboard summary counts |
| `GET` | `/health` | API/database health |

Supported list query parameters:

| Parameter | Example | Notes |
| --- | --- | --- |
| `status` | `open` | Backend currently parses `open` and `upcoming` for filtering |
| `marketType` | `mainboard` or `sme` | Filters by IPO market type |
| `exchange` | `nse` or `bse` | Filters by exchange flag |
| `search` | `phonepe` | Company/search text |
| `sort` | `openDate` | Default is `openDate` |
| `direction` | `asc` or `desc` | Default is `asc` |
| `page` | `1` | Default is `1` |
| `pageSize` | `10` | Default is `10` |

Example:

```bash
curl -i 'http://127.0.0.1:5087/api/v1/ipos?status=open&pageSize=10'
```

## 8. Upstox Integration Details

Official Upstox endpoints used:

- IPO list: `GET https://api.upstox.com/v2/ipos`
- IPO detail: `GET https://api.upstox.com/v2/ipos/{id}`

Authentication:

```http
Authorization: Bearer <UPSTOX_TOKEN>
```

List parameters:

| Parameter | Used By Scheduler | Notes |
| --- | --- | --- |
| `status` | Yes | `open`, `upcoming`, `closed`, `listed` |
| `page_number` | Yes | Starts at `1` |
| `records` | Yes | Uses `30`, the documented maximum |
| `issue_type` | Not currently | Omitted to include both regular and SME |

Scheduler defaults:

```json
{
  "Scheduler": {
    "Statuses": [ "open", "upcoming", "closed", "listed" ],
    "PageSize": 30,
    "SyncIntervalMinutes": 10,
    "JitterMaxSeconds": 45,
    "RunOnStartup": true
  }
}
```

Important provider rules implemented:

- Fetch list pages by status.
- Fetch details for each Upstox IPO id.
- Use Upstox IPO id as `Slug` in the OnlyIPO database.
- Treat zero-valued price fields as unknown/null, not as real zero rupee values.
- Parse `registrar_info` as either string or object.
- Use parameterized PostgreSQL commands.
- Keep previous data available if a scheduled run fails.

Provider documentation:

- `https://upstox.com/developer/api-documentation/get-ipos/`
- `https://upstox.com/developer/api-documentation/get-ipo-details/`
- `https://upstox.com/developer/api-documentation/rate-limiting/`

Business/legal note:

Before showing or redistributing broker-derived IPO data publicly, get written confirmation that the intended display, caching, and redistribution model is permitted.

## 9. Database Model

Primary table:

```text
ipos
```

Important columns:

| Column | Purpose |
| --- | --- |
| `Id` | Internal UUID |
| `Slug` | Unique app slug; currently stores Upstox IPO id for live data |
| `CompanyName` | Display company name |
| `Status` | `Draft`, `Upcoming`, `Open`, `Closed`, `Listed`, `Withdrawn` |
| `MarketType` | `Mainboard` or `SME` |
| `Exchanges` | Integer flags: `NSE = 1`, `BSE = 2`, both = `3` |
| `IssueSize` | Issue size |
| `PriceBandMinimum` | Lower price band, nullable |
| `PriceBandMaximum` | Upper price band, nullable |
| `LotSize` | Lot size |
| `MinimumInvestment` | `PriceBandMaximum * LotSize`, nullable |
| `OpenDate` | IPO bidding open date |
| `CloseDate` | IPO bidding close date |
| `AllotmentDate` | Allotment date |
| `RefundDate` | Refund initiation date |
| `DematCreditDate` | Demat transfer date |
| `ListingDate` | Listing date |
| `Registrar` | Registrar name/detail |
| `OverallSubscription` | Total subscription |
| `DrhpDocumentUrl` | DRHP URL |
| `RhpDocumentUrl` | RHP URL |
| `SourceName` | `Upstox` or seed data source |
| `SourceUrl` | Provider detail URL |
| `SourceUpdatedAt` | Live sync timestamp |
| `CreatedAt` | Created timestamp |
| `UpdatedAt` | Updated timestamp |

Uniqueness:

```text
IX_ipos_Slug is unique
```

Scheduler upsert behavior:

```sql
ON CONFLICT ("Slug") DO UPDATE
```

This means rerunning the scheduler updates existing live rows instead of duplicating them.

## 10. Key Files

### Frontend

| File | Purpose |
| --- | --- |
| `/Users/anishtaneja/Desktop/Projects/only-ipo-web/package.json` | Scripts and React dependencies |
| `/Users/anishtaneja/Desktop/Projects/only-ipo-web/src/App.tsx` | App routing/layout entry |
| `/Users/anishtaneja/Desktop/Projects/only-ipo-web/src/main.tsx` | React bootstrap |
| `/Users/anishtaneja/Desktop/Projects/only-ipo-web/src/pages/Dashboard.tsx` | Main IPO dashboard screen |
| `/Users/anishtaneja/Desktop/Projects/only-ipo-web/src/pages/IpoDetailPage.tsx` | IPO detail screen |
| `/Users/anishtaneja/Desktop/Projects/only-ipo-web/src/api/client.ts` | API client configuration |
| `/Users/anishtaneja/Desktop/Projects/only-ipo-web/src/api/generated/schema.ts` | Generated OpenAPI types |
| `/Users/anishtaneja/Desktop/Projects/only-ipo-web/src/styles.css` | Global app styling |
| `/Users/anishtaneja/Desktop/Projects/only-ipo-web/.env.example` | API base URL example |

### Backend

| File | Purpose |
| --- | --- |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO/src/IPOOnly.Api/Program.cs` | API startup and dependency registration |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO/src/IPOOnly.Api/Endpoints/IpoEndpoints.cs` | Public IPO endpoints |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO/src/IPOOnly.Api/Contracts/IpoDtos.cs` | API response DTOs and mappings |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO/src/IPOOnly.Api/Validation/IpoQueryRequest.cs` | Query request shape |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO/src/IPOOnly.Api/Validation/IpoQueryRequestValidator.cs` | Query validation |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO/src/IPOOnly.Domain/Ipos/Ipo.cs` | IPO domain entity |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO/src/IPOOnly.Domain/Ipos/IpoStatus.cs` | IPO lifecycle enum |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO/src/IPOOnly.Domain/Ipos/MarketType.cs` | Mainboard/SME enum |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO/src/IPOOnly.Domain/Ipos/Exchange.cs` | NSE/BSE flag enum |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO/src/IPOOnly.Infrastructure/Persistence/IPOOnlyDbContext.cs` | EF Core database model |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO/src/IPOOnly.Infrastructure/Persistence/Migrations/20260826000000_InitialCreate.cs` | Initial database schema |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO/src/IPOOnly.Infrastructure/Ipos/EfIpoRepository.cs` | IPO query implementation |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO/src/IPOOnly.Infrastructure/Seed/SeedIpoDataProvider.cs` | Demo seed data |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO/src/IPOOnly.Api/appsettings.json` | Backend config |

### Scheduler

| File | Purpose |
| --- | --- |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO.Scheduler/IPOOnly.Scheduler.slnx` | Scheduler solution |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO.Scheduler/src/IPOOnly.Scheduler/Program.cs` | Host setup, options, DI, run-once mode, worker registration |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO.Scheduler/src/IPOOnly.Scheduler/SchedulerOptions.cs` | Scheduler interval/page/status config |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO.Scheduler/src/IPOOnly.Scheduler/IpoSyncService.cs` | Main sync orchestration |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO.Scheduler/src/IPOOnly.Scheduler/Upstox/UpstoxIpoClient.cs` | Authenticated Upstox HTTP client |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO.Scheduler/src/IPOOnly.Scheduler/Upstox/UpstoxDtos.cs` | Upstox response DTOs |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO.Scheduler/src/IPOOnly.Scheduler/Upstox/UpstoxIpoMapper.cs` | Upstox-to-OnlyIPO mapping logic |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO.Scheduler/src/IPOOnly.Scheduler/Persistence/IpoRepository.cs` | Parameterized PostgreSQL upsert |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO.Scheduler/src/IPOOnly.Scheduler/Persistence/IpoRecord.cs` | Scheduler persistence record |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO.Scheduler/tests/IPOOnly.Scheduler.Tests/UpstoxIpoMapperTests.cs` | Mapper regression tests |
| `/Users/anishtaneja/Desktop/Projects/OnlyIPO.Scheduler/.github/workflows/ci.yml` | GitHub Actions build/test workflow |

## 11. Release and Merge State

Important rule from owner:

Do not merge to `main` until local tests are run and the local test link or equivalent verification is shared with the owner. Wait for explicit confirmation before merging.

Current scheduler release state:

```text
Repository: /Users/anishtaneja/Desktop/Projects/OnlyIPO.Scheduler
Tracking issue: https://github.com/anish689/OnlyIPO.Scheduler/issues/3
Feature branch: codex/ipo-1-live-payload-fixes
```

Why this branch exists:

During the first live Upstox run, `registrar_info` came back as an object instead of a string. The fix updates the DTO/mapper to support both shapes and adds a regression test.

Verification required before merge:

```text
dotnet build IPOOnly.Scheduler.slnx --no-restore
dotnet test IPOOnly.Scheduler.slnx --no-build
DOTNET_ENVIRONMENT=Development dotnet run --project src/IPOOnly.Scheduler/IPOOnly.Scheduler.csproj -- --run-once
```

Result:

```text
Fetched 142, upserted 142.
```

Before merging:

1. Share local test links with owner.
2. Owner confirms data/app works locally.
3. CI passes.
4. Merge only after explicit owner approval.

## 12. Development Workflow

Use GitHub Issues for tracking.

Recommended flow:

1. Create or select an issue.
2. Pull latest `main`.
3. Create feature branch.
4. Implement.
5. Run local tests.
6. Share local URL/test evidence with owner.
7. Commit.
8. Push branch.
9. Create PR.
10. Wait for CI.
11. Wait for owner confirmation.
12. Merge.

Commands:

```bash
git switch main
git pull --ff-only origin main
git switch -c codex/<issue-key-short-description>
```

Commit style:

```bash
git commit -m "feat: short description" -m "Refs #<issue-number>"
git commit -m "fix: short description" -m "Refs #<issue-number>"
```

PR creation:

```bash
gh pr create --repo anish689/<repo-name> --base main --head <branch-name>
```

PR checks:

```bash
gh pr checks <pr-number> --repo anish689/<repo-name> --watch
```

Merge only after owner approval:

```bash
gh pr merge <pr-number> --repo anish689/<repo-name> --squash --delete-branch
```

## 13. Test Commands

Frontend:

```bash
cd /Users/anishtaneja/Desktop/Projects/only-ipo-web
npm run lint
npm run build
npm test
```

Backend:

```bash
cd /Users/anishtaneja/Desktop/Projects/OnlyIPO
DOTNET_ROLL_FORWARD=Major dotnet build
DOTNET_ROLL_FORWARD=Major dotnet test
```

Scheduler:

```bash
cd /Users/anishtaneja/Desktop/Projects/OnlyIPO.Scheduler
dotnet restore IPOOnly.Scheduler.slnx
dotnet build IPOOnly.Scheduler.slnx --no-restore
dotnet test IPOOnly.Scheduler.slnx --no-build
```

Live scheduler test:

```bash
DOTNET_ENVIRONMENT=Development \
dotnet run --project src/IPOOnly.Scheduler/IPOOnly.Scheduler.csproj -- --run-once
```

Then verify:

```bash
curl -i http://127.0.0.1:5087/api/v1/ipos/summary
curl -i 'http://127.0.0.1:5087/api/v1/ipos?status=open&pageSize=10'
```

## 14. Common Troubleshooting

### Frontend cannot load data

Check backend is running:

```bash
curl -i http://127.0.0.1:5087/api/v1/ipos/summary
```

Check frontend was started with:

```bash
VITE_API_BASE_URL=http://127.0.0.1:5087 npm run dev -- --host 0.0.0.0
```

### Scheduler says Upstox token is required

The scheduler loads user-secrets only in Development environment.

Run:

```bash
DOTNET_ENVIRONMENT=Development \
dotnet run --project src/IPOOnly.Scheduler/IPOOnly.Scheduler.csproj -- --run-once
```

Check whether secrets exist:

```bash
dotnet user-secrets list --project src/IPOOnly.Scheduler/IPOOnly.Scheduler.csproj
```

Do not paste secret values into tickets or commits.

### Scheduler cannot connect to database

Check PostgreSQL is running and the connection string matches the backend:

```bash
dotnet user-secrets set "ConnectionStrings:IPOOnlyDatabase" "Host=localhost;Port=5432;Database=ipoonly;Username=ipoonly;Password=ipoonly_dev_password" --project src/IPOOnly.Scheduler/IPOOnly.Scheduler.csproj
```

Check backend can read the same database:

```bash
curl -i http://127.0.0.1:5087/api/v1/ipos/summary
```

### Live Upstox payload shape changes

Symptoms:

```text
System.Text.Json.JsonException
The JSON value could not be converted...
```

Fix approach:

1. Identify the field from the exception path.
2. Adjust `UpstoxDtos.cs` to allow the real shape.
3. Update `UpstoxIpoMapper.cs`.
4. Add a regression test in `UpstoxIpoMapperTests.cs`.
5. Run build/test/live sync again.

## 15. Security Notes

Secrets currently required:

- Upstox analytics/access token
- PostgreSQL connection string/password

Safe storage options:

- .NET user-secrets for local development
- Environment variables for local terminal sessions
- Cloud secret manager for production later

Unsafe storage:

- Git commits
- `.env` committed to repo
- `appsettings.json` with real token
- PR bodies
- screenshots
- shared terminal logs

Rotate the Upstox token if it was accidentally shared outside a trusted local environment.

## 16. Next Recommended Work

Near-term:

1. Update backend list filtering to support `closed` and `listed` filters, because the scheduler now stores those statuses.
2. Add a backend endpoint or admin screen showing ingestion metadata.
3. Add provider raw payload hash or ingestion run table for auditability.
4. Add production scheduling strategy: cron, systemd timer, container job, or cloud scheduled task.
5. Decide where this handover document should live long term: scheduler repo, backend repo, or separate product docs repo.

Medium-term:

1. Add logo/company enrichment from a permitted source.
2. Add data freshness banners in the frontend.
3. Add observability: structured logs, sync duration, failures, fetched/upserted counts.
4. Add retry/backoff around Upstox requests.
5. Confirm written data redistribution permission from Upstox before public launch.

## 17. Quick Start Checklist

Start backend:

```bash
cd /Users/anishtaneja/Desktop/Projects/OnlyIPO
DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/IPOOnly.Api/IPOOnly.Api.csproj --no-build --urls http://localhost:5087
```

Run live scheduler sync:

```bash
cd /Users/anishtaneja/Desktop/Projects/OnlyIPO.Scheduler
DOTNET_ENVIRONMENT=Development dotnet run --project src/IPOOnly.Scheduler/IPOOnly.Scheduler.csproj -- --run-once
```

Start frontend:

```bash
cd /Users/anishtaneja/Desktop/Projects/only-ipo-web
VITE_API_BASE_URL=http://127.0.0.1:5087 npm run dev -- --host 0.0.0.0
```

Open app:

```text
http://localhost:5173/
```

Verify data:

```text
http://127.0.0.1:5087/api/v1/ipos/summary
http://127.0.0.1:5087/api/v1/ipos?status=open&pageSize=10
```
