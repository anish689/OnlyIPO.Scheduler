# Incident 32: financial refresh document failure

14 October 2026, India time.

## Diagnosis

Run 38458415270 completed open/upcoming/closed/listed/tracking successfully.
Financials attempted 33 due companies: four populated, 28 without validated
coverage, and one failure. Zetwerk's Upstox RHP URL returned HTTP 403. Existing
financial observations were retained. There was no database, token or API-wide
failure in the inspected run. The earlier watchdog overdue warnings cleared
after the scheduled catalogue jobs completed; financials remained unhealthy.

## Source repair

The provider still supplies
`https://assets.upstox.com/ipo/documents/rhp/Zetwerk-RHP_compressed.pdf`.
SEBI publishes the issuer's RHP at:
https://www.sebi.gov.in/filings/public-issues/oct-2026/zetwerk-manufacturing-businesses-limited-rhp_105195.html

The linked PDF is dated 11 October 2026, identifies Zetwerk Manufacturing
Businesses Limited, CIN U74999KA2017PLC128777, and contains 762 pages.
Its SHA-256 is
`9efc3e117e11dacef58152c2ba2c78166fb4d0f97a45a804786ec5f053efc8ec`.

An exact-source configuration entry permits this reviewed official copy only
after the original document returns 403 or 404. No URL guessing, credential
change, automatic search, or source restriction bypass is performed. The official
copy uses the existing HTTPS host allowlist, no redirects and 30 MiB bound.
Ordinary documents retain a 45-second deadline; reviewed replacements get a
bounded three-minute download/parse window after configuration validation.
Parent cancellation remains effective. Its checksum must match before parsing. Citations identify
the document actually parsed. Upstox structured financials still take precedence.

401, 429, server errors, unknown document URLs, changed checksums and unsafe
replacement URLs remain errors. A readable document does not guarantee supported
financial table coverage: ambiguous layouts remain unpublished, not invented.

## Verification and operations

Eight new regression cases cover replacement success (403/404), unchanged
auth/rate-limit/server-error behavior, checksum mismatch, unsafe destinations and
exact-source matching. Local scheduler tests: 121 passed, one database test skipped;
CI must execute that database test before merge. The full official-PDF dry run
verified the checksum and finished in 3.6 seconds without database access. It
yielded zero validated observations: this layout is not supported by the current
conservative parser. The repair resolves document retrieval, not Zetwerk's missing
financial coverage. That coverage remains unavailable and existing values remain
unchanged. A hosted financial-only refresh is required before claiming job recovery.

PR #33 passed CI (122 scheduler tests, seven monitoring tests) and was merged.
Hosted run 38507909261 then attempted 208 targets: 179 populated, 28 unavailable,
one failed. The official SEBI replacement returned HTTP 200 but its 15.4 MiB body
exceeded the original 45-second deadline from the GitHub runner. No checksum or
parse was reached. This evidence motivates the bounded three-minute window for
reviewed replacements only. API health returned 200 during the refresh (26.8s).

The watchdog intentionally does not let a manual refresh prove scheduled cadence.
After a successful manual repair test, leave the incident open until a scheduled
run succeeds in the due window. Record notification delivery separately.

Rollback: revert this source replacement and handler change. Do not delete existing
financial rows. If SEBI revises the document, review the new filing and checksum
before updating the mapping; do not disable checksum validation to get a green run.
