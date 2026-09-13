# RHP Allocation Correctness Bug Fix

Issue: https://github.com/anish689/OnlyIPO.Scheduler/issues/11
Related frontend: https://github.com/anish689/only-ipo-web/pull/38
Date: 13 September 2026. Bug fix, not a new phase.

## Verified Cause
The previous regex searched up to 80 characters AFTER an investor-category name.
In the Qualiance RHP, percentages precede their categories. It attributed the next
35% individual-bidder clause to NII. The correct NII statement is not less than
15%. Source: https://qualiance.com/api/uploads/ir/rhp_qualiance.pdf#page=44
(PDF page 44, printed page 41). The existing fresh issue value, up to 35,52,000
equity shares, is present on PDF page 1 and was unchanged.

The apparent `p.44` debris was independently traced to frontend citation formatting,
not the PDF extraction. Page references now remain in separate source links.

## Parser Changes
IpoOfferDocumentParser accepts explicit category-first or percentage-first
allocation statements referring to the Net Offer/Issue. It no longer searches
arbitrary prose after a category. Qualifiers (not less/more than) are retained.
Whitespace including PDF nonbreaking spaces is normalized. Conflicting extracted
statements are withheld, rather than selecting the first one. Allocation facts
are stamped RhpAllocationStatementV2. RHP-first selection remains unchanged.

No parser can guarantee correctness for every prospectus layout. Tables and
unsupported prose may now yield fewer facts; those need source review or a future
layout-aware extractor. Individual bidder terminology is not silently mapped to
the retail category. These tests do not certify every existing offer/organization
fact or every issuer document.

## Local Data Repair
Back up IpoDocumentFacts, then run docs/bugfix-rhp-allocation.sql with psql and
ON_ERROR_STOP=1. This transaction marks only Available legacy allocation facts
NeedsReview, and applies a guarded, source-reviewed correction for Qualiance NII.
The script is repeatable, does not delete facts or alter other IPO information.
Local result: 78 legacy rows quarantined; one corrected and restored to Available;
77 remain hidden pending re-extraction. Backup is outside git under
../output/rhp-facts-before-fix.csv in the shared workspace.

Deploy/run the corrected scheduler before resuming enrichment. The old scheduler
must not be used to refresh these facts because it can republish the faulty output.
Normal document enrichment replaces facts using the preferred RHP/DRHP. Never
mass-approve NeedsReview rows without re-extraction or source review.

## Validation
31 scheduler tests pass, including eight additional cases: percentage-before-
category, unrelated category percentages, subscription prose, wrong denominator,
invalid percentage, conflicting statements, and PDF whitespace. All 78 backend
and 81 frontend tests pass; frontend lint and build pass. The actual Qualiance PDF
was re-extracted using PdfPig and the revised parser: NII not less than 15%,
QIB not more than 50%, fresh issue unchanged. No paid API or schema change.

Review locally at http://localhost:5173/ipos/qualiance-international-limited-ipo.
API health: http://localhost:5089/health. No new Postman endpoint required.
Do not merge until local review is complete.

## Live Refresh Follow-up
The user authorized refresh, retesting and merge on 13 September 2026.
Run locally with DOTNET_ENVIRONMENT=Development so existing user-secrets load.
An Upstox connection reset interrupted the first attempt. A subsequent pass
completed open/upcoming/closed before stalling on an issuer PDF response body.
HeadersRead only bounds the headers with HttpClient.Timeout, not the body.

DocumentEnrichment.DownloadTimeoutSeconds now defaults to 30 (validated 1-300).
A linked deadline covers headers and body; streaming enforces MaxDocumentBytes
without buffering an unbounded download. A document-specific timeout logs a skip
and preserves existing facts; caller cancellation still propagates. Three new
tests cover stalled body, caller cancellation and oversized response. Scheduler
suite now has 34 passing tests. No new service, secret or database schema.

An attempted listed-only configuration did not exclude the default status list;
no such override is retained. The final invocation refreshes all four statuses.
Repeated Upstox connection resets also exposed missing transient GET retries.
The client now retries connection failures and HTTP 5xx at most three attempts,
with bounded backoff. Invalid credentials are not retried; cancellation stops
backoff. Four regression tests cover recovery, invalid credentials, cancellation
and retry exhaustion. Scheduler suite now contains 38 tests.

Live PDF cross-check: Rentomojo RHP matches extracted QIB not more than 50%, NII
not less than 15%, retail not less than 35%; offer for sale up to 27,365,529 shares
and fresh issue up to INR 1,500 million. Source:
https://www.axiscapital.co.in/contents/Rentomojo%20Limited%20-%20RHP-1788498517.pdf
PDF pages 4, 360, 4, 1 and 359 respectively. Qualiance also re-extracted correctly.
SQL checks found zero published legacy allocations, zero published facts without
valid page/source/value evidence, and no company mixing available document types.
Final run totals and merge evidence are recorded in the PR validation comments.

## Completed Refresh Validation
Final full invocation exited 0: 159 fetched/upserted (10 open, 29 upcoming,
14 closed, 106 listed). Older records not returned by this run were preserved.
86 documents were processed, including documents yielding no supported facts;
62 were skipped for unavailable, invalid, oversized or timed-out sources.
11 IPOs did not enter document enrichment because no preferred document existed.
This is not a claim that all issuer PDFs were successfully downloaded.

Final database: 101 Available allocation facts, 84 Available offer facts, and
5 legacy allocation facts still NeedsReview. Zero published facts lack valid
page/source/value evidence; zero legacy-parser allocations are published.
Qualiance NII remains not less than 15% after the full refresh.
Post-refresh suites: frontend 81, API 78, scheduler 38 tests passed (197 total).
Frontend lint/build passed. Public routes and protected-detail redirects smoke
tested against local data. Google OAuth was not re-certified in this run.
No price-tracking run was requested here; this invocation refreshes IPO/source
documents, not daily listed-price candles.

Local logs: /tmp/onlyipo-final-refresh.log. Test app: http://localhost:5173/.
User authorized merge after these checks; PR comments record final merge results.
