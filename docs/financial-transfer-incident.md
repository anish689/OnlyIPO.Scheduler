# Financial transfer incident: 15 October 2026

Scheduled run 38611846809 completed open, upcoming, closed, listed and tracking.
Financials attempted 29 companies: 28 unavailable and one failed, Zetwerk.
The provider RHP returned 403; the reviewed SEBI replacement returned 200 but
its response body failed with HttpIOException. This was not the three-minute
deadline. Watchdog run 38662259283 correctly kept incident #32 open.

## Remediation

- Retry an interrupted HTTP response body once after two seconds, using the same
  selected URL and existing overall cancellation deadline.
- Discard partial bytes and download afresh. Keep the 30 MiB limit, allowlist,
  reviewed replacement checksum, parsing validation and last-good retention.
- Do not retry authorization, rate-limit, checksum or schema failures.
- Print partition assessment in watchdog logs, in addition to its job summary.
  Keep failures visible until a successful scheduled cycle proves recovery.

## Validation

124 .NET tests passed, one database-concurrency test skipped without its test
database; seven watchdog tests passed. Added truncated-body recovery and repeated
failure tests, including checksum validation of the recovered bytes. Local NuGet
vulnerability lookup was unavailable; CI must perform its normal validation.
Hosted recovery is not yet established. A manual financial-only run can validate
the transfer fix; the watchdog intentionally requires a scheduled success.

No schema, credentials, schedule, source substitution or hosting-plan changes.
Unavailable coverage is not populated data and must not be reported as such.
