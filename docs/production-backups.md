# Production application backups

Implements issue #63. Rollout is gated: merging these files does not activate
backups. Use only standard GitHub-hosted runners in this public repository and
existing personal Google Drive storage; no paid plan, larger runner, Google Cloud
billing upgrade, Actions artifact storage or database service is required.

## What is protected

The full production `public` application schema and its committed data are
exported using a consistent PostgreSQL snapshot. This includes stored scheduler
results: IPO details, dates, document metadata/extracted facts, source and
subscription snapshots, financials, listed-price history and news, alongside
stored accounts, watchlists, preferences and email-delivery records. A snapshot
does not include later job output. Sequence state has PostgreSQL's normal
nontransactional semantics; restore validation checks table data/schema/RLS.

This is **not a full Supabase platform backup**. Managed schemas, Storage objects,
external PDFs, provider configuration/secrets and GitHub workflow logs are outside
the archive. Source/workflows remain in GitHub; deployment and key inventories
remain separate. A backup succeeding does not prove upstream jobs fetched all
expected market data.

## Execution and recovery design

- Daily at **19:43 UTC / 01:13 IST the following day**, after the final scheduled
  refresh window, plus manual runs before database-changing releases. GitHub
  schedules can be delayed or disabled for repository inactivity; this is a daily
  target, not a guaranteed 24-hour recovery objective or an availability SLA.
- Main branch only, separate `production-backup` environment, serial concurrency,
  25-minute timeout, fixed production project/host, verified TLS CA. No Render
  wake-up requests, application emails or production restores.
- Dedicated `youripo_backup` login can only select application data. It has no
  role memberships, RLS bypass or admin powers. Explicit all-row SELECT policies
  are audited before export. New tables require reviewed grants/policies; a
  missing policy/permission fails the run instead of creating a partial backup.
  Accessible application SECURITY DEFINER functions also fail preflight.
- Every dump is restored in a disposable PostgreSQL 17 instance listening only
  on a private Unix socket. Table counts, migration IDs, columns/defaults/indexes/
  constraints and RLS policies must match. Source roles are recreated as NOLOGIN
  without source privileges/passwords for rehearsal; the encrypted manifest
  records password-free role metadata for a separately reviewed recovery.
- `age` encrypts the dump and private manifest before transfer. CI holds only the
  public encryption recipient, never the recovery key. Plaintext is temporary on
  the ephemeral runner, excluded from Actions logs/artifacts/caches and removed.
  Disk deletion is cleanup, not a claim of cryptographic secure erasure.
- Filename example:
  `youripo-production-2026-11-27T194300Z-r123456-a1.tar.age`.
  Date/time is UTC; run ID and attempt prevent name collisions.
- Upload to a dedicated app-created, owner-only Drive folder using only
  `drive.file` OAuth scope. Download the ciphertext and match SHA-256 and length
  before marking it verified. No sharing links, broad Drive scope or recovery
  keys in Drive. Failed/ambiguous uploads stay unverified and cannot trigger
  retention. Safe reads have bounded backoff; ambiguous writes are not retried.
- Retain the newest verified copy for each of **7 distinct daily dates and 4
  distinct ISO weeks**, taking their union. The current good copy always remains.
  Only this job's verified files in its exact folder are eligible; existing
  manual backups and foreign/pending files are never selected. Retention starts
  as dry-run. Once enabled, old copies go to Drive Trash (not permanent deletion);
  Trash can consume storage until Google removes it. Storage exhaustion fails
  visibly rather than enabling paid storage.
- The backup watchdog checks the daily deadline after a two-hour grace period
  and maintains one GitHub incident. A green/skipped workflow is insufficient:
  the verified-upload step must have succeeded. Watchdog failure and missed
  schedules require owner attention; subscribe to Actions/incident notifications
  and inspect the history. GitHub notification delivery is not guaranteed.

## Private setup and activation

1. Review and merge the PR after CI. Create GitHub environment
   `production-backup` allowing only branch `main`, no tags. Keep all activation
   flags absent/false. Existing scheduler environment and credentials stay intact.
2. Review `scripts/backup/production-backup-role.sql` against production project
   `vzbrqhpqawyenwxauztq`. Apply as owner. Set a unique generated password and
   enable LOGIN privately, never in SQL-editor history or chat. Store only this
   password as environment secret `BACKUP_DB_PASSWORD`. Do not use `postgres`,
   `youripo_api` or `youripo_scheduler` credentials. Reapply/review after schema
   changes and confirm the next backup succeeds.
3. Use Google's supported [Personal mode onboarding](https://console.cloud.google.com/auth/overview/personal)
   to create a dedicated owner-only desktop client, then enable the Drive API in
   its unbilled/Standard Tier project. Personal mode can authorize only its creator
   and avoids the seven-day Testing token expiry; it is a one-time project choice
   and cannot be selected for an existing External project. Do not select the
   AI-agent option for this deterministic backup script. Request **only**
   `https://www.googleapis.com/auth/drive.file`, with offline access. Do not reuse
   a website sign-in client or authorize all Drive files. If a future organization
   needs multiple operators, review a separate supported authorization setup;
   never substitute an expiring External/Testing token for unattended operation.
   Never enable Scaled Tier or billing.
4. Use `scripts/backup/authorize_drive.py` locally with the privately downloaded
   client JSON. It validates state/PKCE, stores credentials directly in the
   `production-backup` GitHub environment, creates a new private folder and saves
   its ID. It does not print tokens or touch existing manual backup folders.
   The owner may need to complete Google consent privately in the opened browser.
   Keep the original client JSON in a private recovery store if reauthorization
   may be needed; do not commit it or put it in the backup folder unencrypted.
5. Save the **public** age recipient as environment variable
   `BACKUP_AGE_RECIPIENT`; the matching private key must already be stored
   separately in the owner's password manager. Existing verified backup key can
   be reused. Set repository `PRODUCTION_BACKUP_ENABLED=true` for the first manual
   run; keep `PRODUCTION_BACKUP_SCHEDULE_ENABLED=false` and
   `BACKUP_RETENTION_ENABLED=false`.
6. Dispatch the workflow from main. Verify its isolated restore, private Drive
   metadata and downloaded ciphertext checksum. Independently download that new
   encrypted archive and decrypt/restore locally using the owner's separately
   held key. Do not overwrite production. Confirm key/recipient match and access
   independently of GitHub's credential storage.
7. Only after live acceptance, set repository
   `PRODUCTION_BACKUP_SCHEDULE_ENABLED=true`. Keep retention dry-run until the
   reviewed selection is acceptable, then set environment
   `BACKUP_RETENTION_ENABLED=true`. Confirm the first scheduled backup and watchdog
   run. Record timestamps/run links, no private data, in the release evidence.
8. Periodically (at least monthly and after schema/key changes) repeat an
   independent decryption/restore and verify recovery-key custody. Review Google
   storage, scheduled-run history, token health and permissions. Take a verified
   manual backup before schema/data-changing releases.

Secrets: `BACKUP_DB_PASSWORD`, `DRIVE_CLIENT_ID`, `DRIVE_CLIENT_SECRET`,
`DRIVE_REFRESH_TOKEN`. Environment variables: `BACKUP_AGE_RECIPIENT`,
`BACKUP_DRIVE_FOLDER_ID`, `BACKUP_RETENTION_ENABLED`. Repository variables:
`PRODUCTION_BACKUP_ENABLED`, `PRODUCTION_BACKUP_SCHEDULE_ENABLED`.

To pause, set `PRODUCTION_BACKUP_ENABLED=false`; leave the watchdog enabled so
missed protection remains visible unless intentionally retiring the service.
Never delete the last good archive or reset credentials to hide an incident.
On OAuth revocation/token expiration, reauthorize this same narrow client,
preserve the same folder/old copies, and complete a new manual verified backup.
An explicit new-folder helper option is only for first setup; do not silently
create another folder on every credential rotation.

## Verification

Activation evidence (27 November 2026): implementation PR #64 passed backup and
scheduler CI and merged as `ab973303985291ff3c87f1376ef1987e47edebc6`.
[Manual production backup run](https://github.com/anish689/OnlyIPO.Scheduler/actions/runs/45987871052)
completed its isolated restore and encrypted Drive download verification. A
separate owner-browser download was decrypted with the independently held key
and restored locally: 32 application tables, 20 migrations, counts/schema/RLS
matched. [Watchdog acceptance](https://github.com/anish689/OnlyIPO.Scheduler/actions/runs/45988886723)
passed. The dedicated environment is main-branch-only, the Google OAuth audience
is Personal and the project has no billing account. Daily scheduling and
retention were enabled after these checks. The first scheduled invocation is
due 27 November at 19:43 UTC / 28 November at 01:13 IST; it had not occurred at
this checkpoint. This manual acceptance is not a claim of historical daily
coverage, future uptime or a guaranteed recovery time.

`BACKUP_TEST_PG_BIN=/path/to/postgresql/17/bin python3 -m unittest discover -s scripts/backup -p 'test_*.py' -v`

The isolated rehearsal uses fictional data, proves concurrent commits do not
change the snapshot, rejects missing RLS policies and accidental write grants,
restores real PostgreSQL data/schema/policies, and decrypts the resulting age
archive. Unit checks cover permissions, scope/redirect restrictions, rate-limit
handling, corrupt offsite download rejection, retention and activation gates.
`node --test scripts/backup/health.test.mjs` checks missed/skipped/stale runs.

References: [GitHub Actions billing](https://docs.github.com/en/actions/concepts/billing-and-usage),
[Drive narrow scopes](https://developers.google.com/workspace/drive/api/guides/api-specific-auth),
[Google OAuth offline-token limitations](https://developers.google.com/identity/protocols/oauth2),
[Google owner-only Personal mode](https://support.google.com/cloud/answer/15544987),
[Drive quotas/Standard Tier](https://developers.google.com/workspace/drive/api/guides/quotas),
[PostgreSQL 17 pg_dump](https://www.postgresql.org/docs/17/app-pgdump.html).
