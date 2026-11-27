# Documentation ownership and lifecycle

Maintained standard; reviewed 27 November 2026. Owner: repository maintainer, coordinated by the Stronghandz release owner. This defines documentation practice, not certification against a formal standard.

## Source of truth

1. Source/contracts/options/workflows establish implemented behavior and defaults.
2. Non-secret provider configuration and exact deployed artifacts establish deployment/activation.
3. Dated validation records establish what was actually observed, by whom, and its limits.
4. Maintained runbooks explain safe operation of that evidence-backed state.
5. Historical plans/spikes/incidents preserve past decisions; they never override current controls.

The API [current status](https://github.com/anish689/OnlyIPO/blob/main/docs/current-status.md) is the shared release index. Each repository's documentation index covers every tracked prose document and identifies maintained references versus history. Public repositories link to private companion guides without copying private credentials/data; access requirements are expected.

## Required content

Maintained guides state purpose, owner, review date, scope and source references. Operational guides include prerequisites, environment/role and side effects, bounded steps, validation, failure/rollback, recovery and known limitations. Configuration references state defaults, allowed values, public versus secret ownership and activation prerequisites. Never duplicate actual secret values.

Acceptance records state timestamp, revision/artifact, environment, method and result, including skipped/unobserved work. Distinguish code shipped from feature enabled and user validation from agent observation. Do not keep growing test totals in evergreen README text; link the dated CI/run evidence. Record timezone explicitly (UTC storage/workflow schedules; IST where useful for users).

## Updating and preserving history

Update documents in the same issue/PR as the change. If an old operational entry point is replaced, archive its original record with a historical banner and link the replacement. Keep existing dates/results intact; fix navigational links without silently upgrading past failures to successes. A versioned history file is not an instruction to rerun old mutations or resume stopped services.

When a document is partially historical, label its old evidence section and place the maintained contract/status first. Classify design proposals and backlogs as plans until implementation is linked. Explain intentional environment differences instead of saying everything is “in sync.” Newly discovered release gaps must appear in the current checklist even if an earlier milestone was complete.

## Review checklist

- Resolve repository-relative files/anchors and cross-repository paths against current source. External/private links may need authentication; a syntactically valid URL is not a fresh availability check.
- Compare route/method/auth requirements, feature flags, SDK/runtime pins, migrations, workflow schedules and permission scopes against code.
- Check commands from the stated directory; quote placeholders and label writes/network calls. Never execute destructive or production commands simply to validate documentation.
- Validate Markdown fences/tables, JSON collection syntax, example ports/names and whitespace. Review public text for personal data and secrets without printing matches.
- Use official primary sources for changing provider/runtime requirements, with review dates. Do not repeat old quota/support facts as current guarantees.
- Preserve unrelated local edits. Keep generated browser evidence/private runbooks outside commits unless deliberately sanitized and relevant.

At every release review status, configuration and recovery guides; after a provider/schema/auth/email change review affected runbooks immediately. The PR author updates content, the actual reviewer validates it, and the release owner records go-live decisions. A solo maintainer records self-review honestly; CI and evidence supplement it rather than inventing a second reviewer.
