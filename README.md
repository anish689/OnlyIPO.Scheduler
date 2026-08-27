# OnlyIPO Scheduler

Standalone .NET scheduler for ingesting IPO data into the OnlyIPO backend database.

This repository is intentionally separate from the React frontend and ASP.NET API repositories so ingestion can evolve, deploy, and run on its own cadence.

## Repositories

- Frontend: https://github.com/anish689/only-ipo-web
- Backend API: https://github.com/anish689/OnlyIPO
- Scheduler: https://github.com/anish689/OnlyIPO.Scheduler

## Security

Do not commit broker tokens, database passwords, or `.env` files. Use .NET user secrets or environment variables for local development.
