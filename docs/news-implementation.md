# Phase 20: News Ingestion and Review

Issue: https://github.com/anish689/OnlyIPO.Scheduler/issues/13
Branch: `codex/13-news`.
Design: https://github.com/anish689/only-ipo-web/issues/39 (approved).
API: https://github.com/anish689/OnlyIPO/issues/39
Frontend: https://github.com/anish689/only-ipo-web/issues/41

The authoritative guide is
[API news implementation](https://github.com/anish689/OnlyIPO/blob/codex/39-news/docs/news-implementation.md).
Apply the API's AddNews and AddNewsReviewAudit migrations first. Existing IPO,
RHP and price commands are unchanged. RSS/review need no Upstox token; the Upstox
adapter alone requires a news-capable token.

```sh
DOTNET_ENVIRONMENT=Development dotnet run --project src/IPOOnly.Scheduler -- --news-once
DOTNET_ENVIRONMENT=Development dotnet run --project src/IPOOnly.Scheduler -- --news-worker
DOTNET_ENVIRONMENT=Development dotnet run --project src/IPOOnly.Scheduler -- --news-review
```

No sources are pre-enabled. `docs/news-source-template.sql` is an operator-only
disabled-source template, not permission to republish. Record source terms,
commercial/public-use permission, allowed fields, preview behavior and expiry
before activation. Do not put feed secrets in version control.

All accepted RSS/Atom and Upstox items start PendingReview. Review exact source, relevance,
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

Source-specific HTTP/XML failures are isolated. RSS downloads use a 30-second deadline,
5 MB decompressed limit, public DNS connections, HTTPS and no redirects/proxy.
At most two due sources per check are processed sequentially. Poll interval is
configured per source (15-1440 minutes), separate worker checks once per minute.
The first 200 entries are considered; no lossless-history promise for larger feeds.

Tests: `dotnet test`. Local review URL: http://localhost:5173/news.
The source-permission gate and a real approved-feed publication test remain open;
a successful zero-source run is not proof of live news delivery. Keep PR open for
local review, not automatic merge.

## Upstox Adapter, 14 September 2026

`News/UpstoxNewsFeed.cs` implements the documented `GET /v2/news` JSON adapter.
`NewsFeedRouter` selects it only for the exact source URL
`https://api.upstox.com/v2/news`; RSS remains supported. No API or frontend
response change is necessary. The existing source approval and article review
gates are unchanged; do not set rights approval merely because HTTP succeeds.

Read-only diagnostic (run from `src/IPOOnly.Scheduler` so appsettings are found):

```sh
DOTNET_ENVIRONMENT=Development dotnet run -- --news-upstox-check
```

It reads existing Upstox instrument mappings, fetches and validates articles in
memory, and prints only counts. It does not insert, approve or publish news.
Use user-secrets `Upstox:NewsToken` if a separate token is needed; otherwise the
adapter uses the existing `Upstox:AnalyticsToken`. Tokens never go in feed URLs.

The adapter requests only mapped company instruments, not account holdings or
positions. It batches 30 keys, follows up to 100 pages per API constraints within
a stricter 30-request/90-second whole-run budget and a 300-instrument limit. Limit
overflow fails without partial persistence, requiring job partitioning before
scaling. 429, 5xx and auth errors defer to the next due run rather than retrying
immediately. Each response is capped at 5 MB; redirects and proxies are disabled.

Publication timestamps are milliseconds, accepted within the last seven days.
Unexpected instruments, malformed envelopes and inconsistent pagination fail
closed. Invalid article fields/dates/links are counted as rejected. Only HTTPS
`upstox.com` links are accepted. Repeated URLs are deduplicated across instruments;
conflicting versions reject the batch. Thumbnail images are not downloaded.
Company associations still require exact manual review before publication.

Live diagnostic result on 14 September: 108 mapped instruments, four requests,
HTTP 200, 11 unique eligible articles and zero rejected. Three of the initial
sample companies had no coverage. This proves token and retrieval compatibility,
not coverage of every IPO or permission to redistribute. Unlisted/upcoming IPOs
without mapped instruments are not covered by this adapter.

Reference: https://upstox.com/developer/api-documentation/get-news/
Public reuse confirmation is still required before activation. Use the disabled
template values in `docs/news-source-template.sql` after permission review, then
run news-once, inspect pending articles, publish exact reviewed hashes, and verify
http://localhost:5173/news?kind=news. No paid subscription was started.

Validation: all 71 scheduler tests passed, including the 13 new Upstox contract,
batching, pagination budget, deduplication, URL/date and failure-path cases. Existing
IPO/RHP/price tests remain green. The live check did not write any news database rows.
