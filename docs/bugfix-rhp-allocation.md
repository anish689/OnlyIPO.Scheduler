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
