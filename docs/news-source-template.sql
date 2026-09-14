-- Operator template only: replace placeholders after reviewing a real source.
-- This deliberately creates a disabled/unapproved record and fetches nothing.
-- Supply a new UUID and use parameter binding in your database client.
INSERT INTO "NewsSources" (
  "Id", "Name", "Kind", "FeedUrl", "ArticleHost", "Enabled", "RightsApproved",
  "PermissionReference", "DescriptionAllowed", "PreviewAllowed", "RetentionDays", "PollMinutes"
) VALUES (
  :source_id, :source_name, :kind, :feed_url, :article_host, false, false,
  '', false, false, 30, 15
);

-- AFTER rights review only, in an explicit operator transaction:
-- UPDATE "NewsSources" SET "Enabled"=true, "RightsApproved"=true,
--   "PermissionReference"=:written_permission_reference,
--   "PermissionExpiresAtUtc"=:permission_expiry,
--   "DescriptionAllowed"=:description_allowed, "PreviewAllowed"=:preview_allowed
-- WHERE "Id"=:source_id;

-- Emergency unpublish from all fresh API reads:
-- UPDATE "NewsSources" SET "Enabled"=false WHERE "Id"=:source_id;
