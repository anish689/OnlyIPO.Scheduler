# Development and release workflow

Maintained process; reviewed 27 November 2026. Owner: Stronghandz / repository maintainer. Applies to the three YourIPO repositories: frontend `only-ipo-web`, API/schema `OnlyIPO`, and ingestion/backup `OnlyIPO.Scheduler`. Current release evidence is owned by the [API status index](https://github.com/anish689/OnlyIPO/blob/main/docs/current-status.md).

## Intake and ready criteria

Create/select an issue before implementation. Describe the problem, user impact, acceptance criteria, affected repositories/configuration/data, dependencies, verification and rollback. Use the available type/priority/area labels consistently; repository-specific label differences are not permission to skip traceability. Do not reopen historical backlogs merely because their original “create issues” instructions remain in an archive.

Choose a focused branch from current main (`feat/`, `fix/`, `chore/`; scheduler automation uses `codex/` where established). Preserve unrelated working changes. Link cross-repository work explicitly and identify deployment ordering. A plan or approval is not evidence execution succeeded.

## Implementation and documentation

Keep domain, persistence, HTTP, UI and ingestion responsibilities separate. User/account data belongs only to authorized roles; market ingestion does not inherit email/account permissions. Do not add paid services, production DNS cutover, mass mail or recurring availability traffic under an unrelated change.

Update the owning maintained guide in the same PR as behavior/configuration changes. Include endpoint/DTO, generated client, feature default, schema/grant, secret-name-only, operational and rollback implications as applicable. Dated acceptance/incident records remain history; new facts go into a new checkpoint and the current index. Use [documentation standards](documentation-standards.md).

No credentials, private account data, raw email tokens, backups or recovery keys in Git, screenshots, PR bodies or CI artifacts. Examples use placeholders and local/disposable targets. No completed connection strings or broad provider permission upgrades for convenience.

## Review and verification

Every PR links its issue and explains what changed, why, how it was checked and remaining risks. Review the final diff, not abandoned approaches. Required CI must pass on the exact head before merge. Record the actual reviewer/self-review and evidence honestly; a solo-developer self-review is not independent approval.

Use checks proportionate to the change, plus existing required CI:

| Change | Required evidence |
| --- | --- |
| Documentation | Full inventory/classification, local links/anchors and source/configuration/command references, examples and secret-safe diff, whitespace review; existing repository CI |
| Frontend behavior | Lint, unit tests/build; routing/isolation/audit checks when affected; relevant desktop/mobile/accessibility/loading/error flows |
| API behavior | Build/unit tests; HTTP/auth/ownership/negative paths; real disposable PostgreSQL integration when persistence changes; container CI |
| Scheduler/backup | Build/tests, workflow/monitoring policy checks; isolated PostgreSQL concurrency/restore/encryption tests when affected |
| Schema/security | Reviewed migration/grants/RLS/negative cases, compatible old/new code, verified backup/restore, rollback and staged acceptance |
| Email/provider | Mocked deterministic cases plus approved-recipient hosted acceptance, distinct submission/receipt/inbox/opt-out evidence; no speculative resends |

Document passed/failed/skipped counts and environment/revision. Local fixtures, browser emulation, owner-assisted checks and real hosted observations are distinct. Green CI is not proof of OAuth, inbox receipt, source completeness, domain cutover or continuous uptime. Do not weaken a failing test/security gate merely to merge; track justified exceptions with owner approval, scope and expiry.

## Promotion and rollback

Stage a reviewed change first, verify the exact source and provider artifact, then promote to the gated production preview when authorized. API/schema changes precede dependent frontend activation. Keep environment-specific identities and disabled features separate; code alignment does not require schema/flag parity during staged rollout.

The current production preview, broad accounts, optional email features and final domain each have explicit gates. A documentation change does not authorize activation. Netlify remains paused and recurring availability testing remains stopped. Existing automatic staging builds may occur on merge; record that rather than claiming no deployment happened. Do not enable production auto-deploy or spend to accelerate checks.

Retain compatible rollback artifacts; back up before database-changing releases and rehearse restoration in isolation. No destructive down migration or production restore solely for a test. Following promotion, perform a bounded sanity check appropriate to the changed behavior and update the dated release status. Domain `youripo.in` and public launch are deliberately last.

## Done criteria and ongoing ownership

An issue is complete when acceptance is satisfied, PR is reviewed/merged, relevant deployment/validation is recorded if in scope, documentation/indexes are updated, and remaining work has explicit owner/issues rather than hidden assumptions. Distinguish implemented, deployed, enabled, accepted and public-released states.

Review supported runtime/dependency lifecycles, quotas, backup runs/key custody/restore, scheduled refresh completeness and disabled feature scope at each release. Monthly independent recovery rehearsal is an operating responsibility, not a new automated website probe. First scheduled observations are recorded separately from manual acceptance. No checklist grants legal certification or an uptime/recovery guarantee.
