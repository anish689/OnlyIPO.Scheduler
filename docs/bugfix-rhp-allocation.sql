-- Back up IpoDocumentFacts before running. Only legacy allocation facts are quarantined.
-- New parser runs replace these records from the preferred document.
BEGIN;
UPDATE "IpoDocumentFacts"
SET "ValidationStatus" = 'NeedsReview', "UpdatedAtUtc" = now()
WHERE "FactGroup" = 'issue-allocation'
  AND "ExtractionMethod" = 'RhpFirstPatternParser'
  AND "ValidationStatus" = 'Available';

-- Manually checked against Qualiance RHP PDF page 44 (printed page 41).
-- Guarded by issuer, exact source, key and old method; idempotent on repeat runs.
UPDATE "IpoDocumentFacts" f
SET "Value" = 'not less than 15%', "PageNumber" = 44,
    "ValidationStatus" = 'Available', "ExtractionMethod" = 'ManualSourceReview',
    "ExtractedAtUtc" = now(), "UpdatedAtUtc" = now()
FROM ipos i
WHERE i."Id" = f."IpoId" AND i."Slug" = 'qualiance-international-limited-ipo'
  AND f."FactKey" = 'nii-allocation'
  AND f."SourceDocumentType" = 'RHP'
  AND f."SourceDocumentUrl" = 'https://qualiance.com/api/uploads/ir/rhp_qualiance.pdf'
  AND f."ExtractionMethod" = 'RhpFirstPatternParser';
COMMIT;
