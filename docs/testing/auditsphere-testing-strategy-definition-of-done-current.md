# AuditSphere — Test Strategy and Definition of Done

**Status: CURRENT.** Applies to every agent and human change. Section 2 is the rule for reporting verification; section 4 is the strategy the restored suites implement.

## 1. Where things stand (2026-10-08)

- Automated test suites are **restored** (STE-NXT-001; ADR-0008 is `SUPERSEDED`): the Domain, API-host and Playwright projects and the Angular specs are back in the tree.
- CI (`.github/workflows/ci.yml`) runs the Angular build and its Vitest specs, the pinned-SDK locked restore, the Release build, EF model drift, standalone publication, the OpenAPI drift gate and the docs gates. The .NET test projects run **locally** against the PostgreSQL test database `auditsphere_tests` on port 5433, as they did before the removal; they are not part of the hosted workflow.
- A `dotnet test` result counts only when it ran against that test database. A discovery-only run, or a run that found no tests, proves nothing and must not be reported as a pass.

## 2. Definition of Done

A change is done only when **all** of these hold, and the report says which were run:

1. **Builds and tests:** every command in `AGENTS.md` §3 succeeds with zero warnings and zero errors (Release), including `dotnet test` and `npm --prefix src/AuditSphereOps.Ui run test:ci`. Report the pass and fail counts of each.
2. **Schema:** no pending EF model changes; any new migration is additive and named for the business change.
3. **Contract:** if an endpoint shape changed, `contracts/auditsphere-openapi.json` is regenerated and `scripts/contracts/verify-openapi.sh` passes; the Angular decoder matches.
4. **Invariants reviewed in the diff:** scope check present in each new command/query; no new update path on append-only data; stale-input fence (`ExpectedRevision`/`ExpectedVersion`) on revisioned writes; no clock/network/EF in calculators; fail-closed defaults.
5. **Tests for new behaviour:** each new command or query has tests for the allowed role, a wrong role, a sibling-scope identifier, a stale revision and a replay where those apply. Each new decoder has tests for a malformed and an over-bound payload.
6. **Manual evidence, labelled as manual:** for behaviour changes that the tests cannot show, a browser or API walk-through against local PostgreSQL with the exact steps, identities and observed results, recorded in `docs/execution/auditsphere-execution-current-slice.md` as `MANUAL_OBSERVATION` (a label introduced by this document), never as `PASS_LOCAL_SLICE`.
7. **Truthful status:** `docs/execution/status.json` changes only with observed facts. No status moves to `LOCAL_VERIFIED`, `PASS` or `READY_FOR_REVIEW` on the strength of this section alone.
8. **Report:** files changed, commands run with results, commands not run and why, assumptions, open questions.

## 3. Restoring the suites (completed, STE-NXT-001)

The suites were restored from the last commit before their removal:

```bash
git checkout 47ca0b05^ -- $(git diff --name-only --diff-filter=D 47ca0b05^ 47ca0b05)
```

This brought back only the files the removal deleted (Domain, API-host and E2E tests, Angular specs and the browser-journey scripts). Do **not** run it again: it would overwrite later edits to those files. Failures found after the restore were fixed in the code or in the test, and each fix states which one changed, in the commit message.

The `npm run test:ci` step is back in the `ui-build` job of `ci.yml`. The .NET test projects are not added to the hosted workflow; doing so needs a PostgreSQL service and a run-time budget, which is an owner decision.

## 4. Test layers and rules

| Layer | Tool | What it proves | Rule |
| --- | --- | --- | --- |
| Pure calculators (materiality, sampling, FX, consolidation, rates) | xUnit, no database | Arithmetic, ranges, rounding, determinism | Every policy range and rounding boundary has a test at, inside and outside the bound |
| Application commands and queries | xUnit on a cloned, migrated PostgreSQL template (`PgTestSchema`) | Scope isolation, gates, append-only triggers, idempotent receipts | Each command: allowed role, wrong role, sibling-scope ID, stale revision, replay |
| HTTP boundary | `AuditSphereOps.Api.Tests` (`StandaloneApiApplicationFactory`) | Session policy, rate limits, error shape, size limits | Every new endpoint: `401` unauthenticated, `403/404` out of scope, `413` where JSON |
| Angular | Vitest specs beside each feature | Decoders, `can*` rendering, unknown-outcome handling | Each decoder rejects malformed and over-bound payloads |
| Journeys | Playwright (`AuditSphereOps.E2E.Tests`, `OwnedHost`) | Multi-role flows across gates | One journey per specification flow 3.1–3.5 |

Test names describe behaviour (`Engagement_letter_is_refused_without_partner_risk_clearance`), never `UnitTest1`. Each test owns its data; no shared mutable fixtures across classes.

## 5. External gates

Local tests and simulations never close `BLOCKED_EXTERNAL` gates (P1–P10, M365-ADMIN). Those need the named human owner, an authorized environment, observed evidence and independent review (`docs/execution/auditsphere-execution-pending-tasks.md` §5).
