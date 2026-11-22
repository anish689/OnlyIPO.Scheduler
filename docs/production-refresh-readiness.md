# Production freshness operation

Prepared 22 November 2026 for issue #49. Activation and hosted acceptance are
separate from merging this change. Do not infer successful execution from a
configured cron or a green local test.

## Isolation and cost

`production-refresh.yml` runs only main with the existing production environment
and production-only database/provider secrets. The production role, TLS checks,
three-connection cap, retained-data fallback and disabled watchlist reads remain
unchanged. No API wake-up pings, Render changes, DNS changes or emails are added.

Repository `anish689/OnlyIPO.Scheduler` was verified public. These workflows use
standard `ubuntu-latest` runners, free for public repositories under
[GitHub's billing policy](https://docs.github.com/en/billing/concepts/product-billing/github-actions).
No larger runner, artifact upload or paid service is introduced. Reassess costs
before changing repository visibility, runner class or storage behavior.

## Schedule and rollout

Repository variable `PRODUCTION_SCHEDULER_ENABLED` must equal `true` to run the
schedule or watchdog. Missing/false disables both. Manual refresh remains
available independently for recovery. Task `all` runs six partitions serially;
the existing individual task choices still work.

Requested starts are 07:07 and 16:07 UTC (12:37 and 21:37 IST), daily. These are
separated from staging's 04:17/13:17 UTC cycles. Six jobs total at most 130 minutes
of execution budget: catalogue 10/10/10/20, tracking 30, financials 50. Partitions
do not cancel each other on failure; runs share non-cancelling production
concurrency. Manual dispatch or GitHub queue delays can still postpone work.
SkipFresh avoids replacing fresh financial observations unnecessarily.

1. Merge reviewed, green CI. Keep the repository variable off initially.
2. Dispatch `all` from main. Inspect all six job outcomes and persisted source
   timestamps/counts. Investigate provider/auth/database failures without
   bypassing restrictions. One company's missing RHP is a coverage exception,
   not permission to suppress systemic failures or fabricate values.
3. Set the repository variable only after manual acceptance. Observe a real
   scheduled cycle; manual success cannot satisfy the watchdog's schedule gate.
4. Verify watchdog reporting and owner GitHub notification subscription/delivery.
   GitHub issue creation does not prove mailbox delivery.
5. To stop scheduling, set the variable false. This does not cancel an already
   running job or delete data. Do not roll back good persisted data automatically.

## Watchdog

The production watchdog checks GitHub job metadata after refresh and hourly at
minute 53. It reads no database credentials and makes no calls to the app or
provider. A three-hour grace accommodates serial work and dispatch delay.
It checks all six partitions of the production scheduled workflow on main,
using its own incident title and marker. Staging or manual success cannot hide
a missed production cycle. Repeated failures update the existing bot-owned
incident instead of opening duplicates; successful due-window completion closes
it. Source coverage and database freshness remain separate acceptance checks.

Schedules are best-effort and may be delayed or disabled after repository
inactivity; see [GitHub schedule behavior](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#schedule).
A watchdog on the same platform cannot detect a total GitHub scheduling outage.
No reliability SLA or continuous uptime is claimed. The owner-paused Codex
availability monitor remains unrelated and paused.

## Issuer document coverage classification

Acceptance run 44991226480 populated 155 of 180 financial targets, with 24
unavailable and one RHP HTTP 403. Issue #51 classifies only document-scoped
403/404/410 as unavailable coverage, retaining stored data and sanitized
per-company logs. It does not suppress provider auth/rate limits, RHP 5xx,
database errors or identity failures. Unavailable coverage is not fresh data.
