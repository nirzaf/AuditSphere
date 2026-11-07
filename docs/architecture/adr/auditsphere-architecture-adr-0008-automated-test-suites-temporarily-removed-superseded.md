# ADR-0008: Automated test suites are temporarily removed

**Status: SUPERSEDED** by the restoring change (STE-NXT-001, 2026-10-08) · Date recorded: 2026-10-08 · Decider: repository owner

## Context
Commit `47ca0b05` ("chore: remove all test cases", merged in PR #30 on 2026-10-08) deleted the Domain, Api and E2E test-case files, every Angular `*.spec.ts`, and the browser-journey launcher and seed script, "for now". Fixtures, project files and `xunit.runner.json` remain. Commit `aa9ff73` replaced the test-dependent CI steps: the Angular job no longer runs `npm run test:ci`, and the OpenAPI drift gate now uses `tools/AuditSphereOps.OpenApiEmitter` instead of a test. The commit immediately before the removal (`2713b58`) reported the Domain suite passing after a template-database speed-up.

## Decision
Operate without automated tests until the owner decides to restore them. The commit message states the reason only as "for now"; the owner should record the actual reason here.

## Consequences
- No behaviour change after `47ca0b05` has automated regression protection. Gates that cite test classes (the STE coverage table, the code map's Tests column, `status.json` entries) describe evidence that no longer exists in the tree.
- The Definition of Done changes: see `docs/testing/auditsphere-testing-strategy-definition-of-done-current.md`. No agent may report "tests pass" or mark a slice verified on the basis of tests.
- Restoring is mechanical. Only deleted files are brought back, so later edits elsewhere are not reverted:
  ```bash
  git checkout 47ca0b05^ -- $(git diff --name-only --diff-filter=D 47ca0b05^ 47ca0b05)
  ```
  then revert the CI edits in `aa9ff73` that removed `npm run test:ci` (keep the OpenAPI emitter if preferred). Tests written against code from before the removal may need updating for changes made since.
- Exit criterion: suites restored, CI running them, and this ADR marked `SUPERSEDED` by the restoring change.
  Status at restoration: the Angular specs run in CI (`ui-build`); the .NET suites run locally on PostgreSQL 5433, as they did before the removal, and are not in the hosted workflow. Moving them into CI needs a PostgreSQL service and a run-time budget, which the owner decides.
