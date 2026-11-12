# AuditSphere testing strategy and Definition of Done

**Status: CURRENT · Consolidated testing policy**

`AGENTS.md` controls test selection, safety, and the local/CI boundary. This document combines the strategy, E2E guidance, and test-catalog navigation; it does not authorize full local suites or convert partial evidence into release acceptance.

## Verification tiers

1. **Focused local checks:** select the smallest existing named test, build, contract, or validator that covers the changed behavior. Add a minimal regression test when the invariant is not already covered. Do not run full .NET, Angular, or Playwright suites locally.
2. **Browser checks:** for a user-visible change, use the built-in browser when available to exercise the affected workflow, inspect visible results, responsive layout, console and network errors, and persistence/authorization where observable. State exactly what was and was not exercised.
3. **CI/CD:** `.github/workflows/ci.yml` is the fast gate. `.github/workflows/release-verification.yml` runs complete unfiltered Domain, Angular, and Playwright/PostgreSQL cohorts and the complete API suite via verified process-isolated shards against a resolved immutable SHA. Every required job and the aggregate must pass on the same SHA for `FULL_CI_PASS`.
4. **External/professional acceptance:** tenant consent, provider behavior, human audit/accounting decisions, accessibility acceptance, and deployment are separate evidence leaves. Tests and green CI do not supply them.

## Risk-based coverage

| Change risk | Minimum evidence |
|---|---|
| Domain/calculation | Existing focused test plus requirement-derived boundary cases; no database, clock, network, or randomness in pure calculators |
| Angular decoder or interaction | One affected `.spec.ts`; production build when template or compilation risk warrants it |
| API, persistence, or transaction | Focused API/Domain test against the dedicated local `auditsphere_tests` database; never development or production data |
| Authorization, scope, or session | Positive and negative cases for exact firm/client/engagement/group scope, revocation, stale session, and direct endpoint access as relevant |
| Immutable evidence, money, concurrency, or migration | Focused source-immutability, exact-value/revision, replay, race, and migration checks for the changed invariant |
| API contract | `bash scripts/contracts/verify-openapi.sh`; update the checked-in contract only from the owning API source |
| Documentation | `git diff --check`, Markdown health, filename, narrative, and any specific manifest validator affected by the edit |

Use `./scripts/agent-check --list` to find an existing bounded capability packet. Prefer an existing test file and exact `--filter`/Angular `--include` selection. Restore dependencies only for a fresh environment or changed lock/toolchain. Do not use stale `--no-build` outputs.

## E2E and browser workflow design

E2E journeys should exercise observable user outcomes at a supported boundary with isolated PostgreSQL data, exact source identity, and cleanup. Assertions cover the workflow's original security, scope, persistence, and professional boundary checks. Do not replace a meaningful failure with a weaker selector or status assertion; correct the test only when evidence shows the observed UI contract or timing changed, and retain the unique invariants.

For offboarding and reconciliation, verify the UI decoder matches the server contract, unknown evidence stays unknown, fee authority is client-scoped, amounts and invoice identity remain exact, bounded previews disclose omitted findings and link to the source workspace, and the digest changes when any included source fact changes. Existing closeout receipts must remain replayable by their exact request identity while stale new requests fail closed.

## Result vocabulary

- `NOT_RUN`: no relevant execution was observed.
- `BLOCKED_TEST_ENV`: an applicable test could not start because its approved test environment was unavailable; assertions did not run.
- `FAST_CI_PASS`: the complete fast workflow succeeded on the exact recorded commit. This is not a full regression pass.
- `FULL_CI_PASS`: the complete release workflow and aggregate succeeded on one resolved immutable SHA.
- `BLOCKED_EXTERNAL`: a dependent acceptance requires unavailable tenant, provider, professional, human, or operator evidence.
- `FAIL`: an observed check failed and remains unresolved; identify the exact step and evidence.

Every report names the source SHA, exact command or workflow, result, and deferred checks. Never claim browser verification without an actual browser session or claim production readiness from a compile, focused test, or static trace.
