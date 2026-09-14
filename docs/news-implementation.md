# Phase 20: News Ingestion and Review

Issue: https://github.com/anish689/OnlyIPO.Scheduler/issues/13
Branch: `codex/13-news`.
Design: https://github.com/anish689/only-ipo-web/issues/39 (approved).
API: https://github.com/anish689/OnlyIPO/issues/39
Frontend: https://github.com/anish689/only-ipo-web/issues/41

The authoritative guide is
[API news implementation](https://github.com/anish689/OnlyIPO/blob/codex/39-news/docs/news-implementation.md).
Apply the API's AddNews and AddNewsReviewAudit migrations first. Existing IPO,
RHP and price commands are unchanged. No Upstox token is needed in news modes.

```sh
DOTNET_ENVIRONMENT=Development dotnet run --project src/IPOOnly.Scheduler -- --news-once
DOTNET_ENVIRONMENT=Development dotnet run --project src/IPOOnly.Scheduler -- --news-worker
DOTNET_ENVIRONMENT=Development dotnet run --project src/IPOOnly.Scheduler -- --news-review
```

No sources are pre-enabled. `docs/news-source-template.sql` is an operator-only
disabled-source template, not permission to republish. Record source terms,
commercial/public-use permission, allowed fields, preview behavior and expiry
before activation. Do not put feed secrets in version control.

All accepted RSS/Atom items start PendingReview. Review exact source, relevance,
date and company identity before publishing using the hash printed by --news-review:

```sh
DOTNET_ENVIRONMENT=Development dotnet run --project src/IPOOnly.Scheduler -- --news-publish ARTICLE_ID --hash CONTENT_HASH --note 'Reviewer: verified original source and relevance' --ipo-slug EXACT_SLUG
DOTNET_ENVIRONMENT=Development dotnet run --project src/IPOOnly.Scheduler -- --news-withdraw ARTICLE_ID --note 'Reviewer: withdrawn after source correction'
```

Company slug is optional. If supplied it must match an existing IPO exactly.
Publication and an append-only review audit are transactional; changed content
hashes are rejected. Feed corrections return published entries to review. No
full article downloads, publisher image harvesting, inferred summaries, fuzzy
company matches, paid feeds or automatic public publication were added.

Source-specific HTTP/XML failures are isolated. Downloads use a 30-second deadline,
5 MB decompressed limit, public DNS connections, HTTPS and no redirects/proxy.
At most two due sources per check are processed sequentially. Poll interval is
configured per source (15-1440 minutes), separate worker checks once per minute.
The first 200 entries are considered; no lossless-history promise for larger feeds.

Tests: `dotnet test`. Local review URL: http://localhost:5173/news.
The source-permission gate and a real approved-feed publication test remain open;
a successful zero-source run is not proof of live news delivery. Keep PR open for
local review, not automatic merge.
