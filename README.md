# OnlyIPO Scheduler

Standalone .NET scheduler for ingesting IPO data into the OnlyIPO backend database.

This repository is intentionally separate from the React frontend and ASP.NET API repositories so ingestion can evolve, deploy, and run on its own cadence.

## Repositories

- Frontend: https://github.com/anish689/only-ipo-web
- Backend API: https://github.com/anish689/OnlyIPO
- Scheduler: https://github.com/anish689/OnlyIPO.Scheduler

## Security

Do not commit broker tokens, database passwords, or `.env` files. Use .NET user secrets or environment variables for local development.

## Local Setup

The scheduler targets `.NET 8` to align with the OnlyIPO backend API and the current long-term support development runtime.

Set local secrets:

```bash
dotnet user-secrets set "Upstox:AnalyticsToken" "<upstox-token>" --project src/IPOOnly.Scheduler
dotnet user-secrets set "ConnectionStrings:IPOOnlyDatabase" "Host=localhost;Port=5432;Database=ipoonly;Username=ipoonly;Password=<LOCAL_DB_PASSWORD>" --project src/IPOOnly.Scheduler
```

Run a one-time sync:

```bash
DOTNET_ENVIRONMENT=Development dotnet run --project src/IPOOnly.Scheduler -- --run-once
```

Run as a long-lived scheduler:

```bash
DOTNET_ENVIRONMENT=Development dotnet run --project src/IPOOnly.Scheduler
```

## Configuration

`Scheduler:Statuses` defaults to `open`, `upcoming`, `closed`, and `listed`.

`Scheduler:PageSize` defaults to `30`, matching the Upstox maximum page size.

`Scheduler:SyncIntervalMinutes` defaults to `10`, with a small jitter so repeated runs do not hit the provider at perfectly fixed boundaries.

## Data Flow

1. Fetch IPO pages from Upstox by status.
2. Store raw page payloads in `IpoSourceSnapshots`.
3. Fetch detail data for each returned IPO id.
4. Store raw detail payloads in `IpoSourceSnapshots`.
5. Map Upstox values into the existing OnlyIPO `ipos` read model.
6. Populate normalized child data:
   - `IpoTimelineEvents`
   - `IpoDocuments`
   - `IpoSubscriptionSnapshots`
7. Upsert by `Slug`, using the Upstox IPO id as the provider key.

`DOTNET_ENVIRONMENT=Development` is required for local runs that depend on .NET user-secrets.

Subscription category behavior:

- `Overall` comes from Upstox when present.
- `Retail`, `QIB`, `NII`, and `Employee` are currently stored as `NotProvidedBySource` because the current Upstox DTO does not provide category-wise values.

The React app never receives the Upstox token. Public application data should continue to flow from the backend API.

## Verification

```bash
dotnet restore IPOOnly.Scheduler.sln
dotnet build IPOOnly.Scheduler.sln
dotnet test IPOOnly.Scheduler.sln
```

## Provider Notes

Upstox documents the IPO list endpoint, IPO detail endpoint, and standard API rate limits here:

- https://upstox.com/developer/api-documentation/get-ipos/
- https://upstox.com/developer/api-documentation/get-ipo-details/
- https://upstox.com/developer/api-documentation/rate-limiting/

Before public redistribution of broker-derived IPO data, get written confirmation that the intended display and caching model is permitted.
