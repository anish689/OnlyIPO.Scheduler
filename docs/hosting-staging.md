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

Two daily IPO refreshes; tracking manual initially. Ten-minute timeout. PDF and
financial ingestion disabled in the free recurring job; those need a bounded
manual plan. Monitor failures and stale data. Never copy production users or
print secrets. Do not make this private repository public for free runner time.
