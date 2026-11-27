# Contributing to YourIPO

Follow [development workflow](docs/development-workflow.md) and [documentation standards](docs/documentation-standards.md). Create/select an issue, use a focused branch, preserve unrelated edits and link the PR to acceptance criteria. Current deployment/release state comes from the [shared status index](https://github.com/anish689/OnlyIPO/blob/main/docs/current-status.md), not old phase plans.

For this documentation reconciliation the established local regression commands remain:

```sh
dotnet build IPOOnly.Scheduler.sln --no-restore
dotnet test IPOOnly.Scheduler.sln --no-build
```

For future documentation-only edits, use the documented link/source/example checks and existing required CI; mark irrelevant UI/live mutation checks not applicable with a reason. Functional changes also require affected contract/persistence/security/deployment tests and suitable browser acceptance. Do not add tests that merely restate text.

Review the final diff for secrets, unsupported claims and stale operational steps. Record actual tests and limitations. No paid service, production account/email activation, DNS cutover or resumed availability automation follows from a docs PR. Keep public launch gated until its current checklist is satisfied.
