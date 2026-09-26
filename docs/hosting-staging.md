# Hosting phase H1: free-only one-shot execution

Tracking: https://github.com/anish689/OnlyIPO/issues/47
Authoritative release runbook: OnlyIPO API repository,
`docs/hosting-phase-h1-release.md`.

`.github/workflows/staging-refresh.yml` requires main, a `staging` GitHub
environment, its two scoped secrets, and the explicit repository opt-in variable.
It cannot run automatically merely because this PR merges. Keep opt-in false
until migrations, least-privilege connection, token and account quota are checked.

Only the scheduled workflow may write the staging catalogue. Concurrency protects
this workflow, not other processes. Use Supabase session pooler port 5432 for
tracking's session advisory lock, TLS verification and small Npgsql pool size.
Do not use transaction mode or the Supabase administrator as the runtime role.

The staging connection secret must include `SSL Mode=VerifyFull;Root
Certificate=/tmp/onlyipo-supabase-ca.crt;Maximum Pool Size=5`. The workflow
downloads the public CA from the link supplied by the project's Supabase
Database Settings and checks its pinned SHA-256 and expiry before the worker
receives credentials. A changed or unavailable certificate fails the run;
review a provider rotation before updating the pin. Never disable TLS validation
to work around a failure. Render uses a different path, `/etc/secrets/`, so do
not reuse its connection string for Actions.

This follow-up is part of anish689/OnlyIPO#47. The runtime staging roles and
15 migrations have been verified; no hosted scheduler run has completed yet.

Two daily IPO refreshes; tracking manual initially. Ten-minute timeout. PDF and
financial ingestion disabled in the free recurring job; those need a bounded
manual plan. Monitor failures and stale data. Never copy production users or
print secrets. Do not make this private repository public for free runner time.
