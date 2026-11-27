# Listed catalogue job timeout

<!-- historical-status -->
> **Historical record / plan.** Preserved for its original revision and dates; this is not current deployment instruction or proof of present readiness. Use the [documentation index](documentation-index.md) and current release status. Do not execute old mutations or resume stopped jobs from this record.


Issue: [#47](https://github.com/anish689/OnlyIPO.Scheduler/issues/47).

Production run [44248786510](https://github.com/anish689/OnlyIPO.Scheduler/actions/runs/44248786510)
was cancelled by GitHub's 10-minute job deadline on 18 November 2026. Its
annotation explicitly reports `The job has exceeded the maximum execution time
of 10m0s`. The earlier listed run on 14 November already took 9m37s, leaving
insufficient margin for the full historical catalogue. The open, upcoming and
closed production runs on 18 November passed in 53s, 1m39s and 30s respectively.

Both staging and production now assign a bounded 20-minute ceiling to `listed`.
Open/upcoming/closed remain 10 minutes, tracking 30 and financials 50. This is a
maximum execution budget, not an instruction to run longer or retry failures.
No parallelism, HTTP retry or provider throttle was increased. The production
workflow stays manual-only on main; staging stays serial. Environment secrets,
database permissions, TLS verification and disabled document enrichment are
unchanged. No paid service or increased billing limit is introduced.

This correction does not resolve the separate Render security challenge/429
incident, certify database snapshot parity, refresh market-price history, or
enable public release. An interrupted run may have committed partial progress;
rerunning the existing idempotent upsert path must not delete earlier data.
Do not report the cancelled run as successful because some rows were written.

Verification: run `node --test scripts/refresh-health.test.mjs
scripts/workflow-contract.test.mjs` and the normal .NET build/tests. The workflow
source contracts prevent the listed timeout from reverting and preserve
main-only/manual-only production, serial staging and isolated credentials. CI
must pass before merge. After merge, dispatch `listed` once from main and record
the outcome and API/UI freshness separately. If it still approaches the new
ceiling, profile provider/database timings before raising the limit again.

Rollback: revert the workflow change through a reviewed PR. No schema or stored
data rollback is required; existing values remain untouched by that revert.
