# AuditSphere — Test Strategy and Definition of Done

**Status: CURRENT.** Applies to human developers and AI coding agents. `AGENTS.md` §3 is the norm for local/CI selection; this document defines evidence and risk-based test design. Never equate CI omission, skipped tests or a discovered-zero test run with a pass.

## 1. Execution tiers and ownership

For browser-visible changes, use the built-in browser as the primary dynamic verification where available: open the target page, exercise the relevant flow, check responsive rendering and inspect console/network errors. Use computer use only when the built-in browser is insufficient. Full automated browser journeys remain in CI/CD.

| Tier | Trigger and owner | What runs | What a PASS proves |
| --- | --- | --- | --- |
| **Local fast feedback** | Developer/agent once after a coherent behavior slice | Only affected compilation/contract validation and smallest meaningful existing/new test cohort. For docs-only changes, no application test. | The explicitly selected behavior/checks only. |
| **Push/PR fast CI** | `.github/workflows/ci.yml` (PR and `master` push) | Full Angular build/specs, pinned locked-restore Release solution build, database-free .NET architecture/rounding guards, EF/OpenAPI, publication and documentation checks. | That workflow's checks for its exact commit; **not** complete .NET regression. |
| **Full regression CI** | `.github/workflows/release-verification.yml` (nightly or manual release candidate) | Unfiltered Domain, API and Playwright .NET suites with isolated PostgreSQL, Angular full specs, build/contracts and aggregate `release-gate`; every job checks out the single immutable commit SHA resolved at workflow start. | Complete **local/synthetic automated** regression for the tested SHA, **not** live tenant or professional approval. |
| **External/professional acceptance** | Approved operator and independent reviewer | Real authorized tenant/provider operations and human professional decisions under approved gates. | Only precisely recorded real-world observations and approvals. |

**Efficiency rule:** Local agents must **not** run full `.NET` solution tests, all Angular specs or all E2E by default; use the narrowest credible proving tests after a coherent slice. Full unfiltered regression runs belong only in `.github/workflows/release-verification.yml`, against that workflow's resolved immutable commit SHA; do not run full suites locally. Cached restore/build artifacts may be reused when sound. Fast CI is deliberately not expanded into full PostgreSQL/E2E for every PR.

**Current release precondition:** `.github/workflows/release.yml` requires both `verify-ci-passed` and `verify-release-source` for the exact SHA; the latter consumes retained `release-verification.yml` evidence. A full-regression release claim still requires inspecting the retained full suite result for that same SHA. Adding a new automatic production gate or changing triggers is a separate owner decision; do not imply one is already implemented.

## 2. Definition of Done — by verification level

A **developer slice** is ready for review when all applicable conditions hold:

1. Accepted behavior, scope/non-goals and smallest coherent diff are clear; no unrelated changes or dependencies.
2. Changed code compiles at the relevant boundary, or the missing build is explicitly `NOT RUN` with reason; a docs-only change needs relevant docs/diff validation only.
3. The smallest existing test cohort that can establish changed behavior passes. Add **only** missing tests for distinct material risk; failures, no tests discovered and unavailable PostgreSQL are not passes.
4. For authorization, accounting, approval, immutable evidence, idempotency and concurrency changes, relevant adverse-path evidence is mandatory at the proper enforcement layer; block/mark incomplete if unavailable rather than skip a security or financial invariant for speed.
5. Relevant EF model drift, HTTP/OpenAPI and Angular contract/decode checks run when those contracts change; the general CI workflow covers its unconditional version.
6. `git diff --check` passes; report exact commands executed, results, omissions, assumptions and unresolved risks. Record execution status only from observations, not from a checklist.

A **PR** is CI-verified only when actual fast `ci.yml` checks pass for the PR SHA; focused local checks do not replace it. A **full-regression release candidate** is regression-verified only with a successful unfiltered `release-verification.yml` run for the **exact same SHA**. The existing `release.yml` additionally requires `verify-ci-passed` and `verify-release-source` before its release job, as described above. A production or tenant capability still needs separate external/professional acceptance. Neither local counts nor a nightly run for a different SHA satisfies those gates; no new workflow or deployment authorization is introduced here.

## 3. Minimal effective test design

**Do not optimize test *count* at the expense of undetected material failures.** Optimize overlap and total execution time; retain the smallest set that credibly protects independent behavior and invariants.

1. **Reuse before writing:** inspect affected code, current test and code map. When a refactor retains the observable contract and existing tests already cover it, **zero new tests** is expected.
2. **One distinct behavior per focused assertion set:** consolidate equivalent inputs into parameterized tests and reusable synthetic fixtures. Avoid testing an identical guard in unit, API and browser layers unless the second layer proves a separate integration contract.
3. **Place tests where faults are observable:** pure arithmetic/rounding → Domain; grant and transactional authorization, append-only DB guards, revision conflict/replay → Application with PostgreSQL; cookie/session/error mapping → API; decoded payload, permissions rendering, unknown-result UI → Angular; cross-role workflow wiring → a representative E2E only where other layers cannot prove it.
4. **Risk-driven negative evidence:** where affected, include current permitted scope, denied/sibling scope, stale/revoked grant, independent checker, outdated version, duplicate request, missing external outcome, rollback and historical evidence. **These are coverage dimensions, not an automatic separate test for each method, route or DTO.** Reuse generic proven security tests for shared logic, with focused additions for unique exceptions.
5. **No duplicate journeys:** prefer one representative E2E for a materially distinct user workflow; do not generate one E2E per endpoint or status code. Test Angular decoders' malformed/oversized responses only when relevant and not already established.
6. **No blind deletion:** an existing test may be consolidated only if equivalence, same invariant protection and risk of regressions are demonstrated in review. Never delete, disable, skip or weaken tests to satisfy a numeric target, suppress a bug or make CI green.
7. **No rerun storms:** after a coherent change run selected tests once; after a fix rerun failing/affected cases; expand only for a new plausible regression. Full suite runs in CI. A newly created test must actually execute and assert expected behavior from approved requirements, not implementation-derived expectations.

**Example lean sets:** docs typo → zero application tests; a pure rounding rule → existing boundary theory; a scoped journal posting change → selected DB-backed permission, balance, stale/replay and rollback cases, often grouped in one fixture; a decoder change → one targeted Angular spec using parameterized invalid payloads; a new multi-actor approval path → a relevant existing integration journey, or one new one only if not otherwise proved.

## 4. Existing testing infrastructure

- Restored suites (STE-NXT-001; ADR-0008 `SUPERSEDED`) include `tests/AuditSphereOps.Domain.Tests/`, `tests/AuditSphereOps.Api.Tests/`, `tests/AuditSphereOps.E2E.Tests/`, and Angular specs. Do not run the historical restore-from-deletion command again; it could overwrite later work.
- The .NET integration/API suites use owned synthetic fixtures and PostgreSQL `auditsphere_tests`; never use the development or production database. `PgTestSchema` and `OwnedHost` are existing fixtures. Use the exact test-project and selector from the code map.
- `./scripts/agent-check` currently supports bounded `engagement-ui`, `review-permissions`, `accounting-calculation` packets and optional layers. Preflight is not a test run; `--run` is explicit. See `docs/testing/auditsphere-testing-guide-agent-capability-checks-current.md`. A passing packet does not prove omitted or external criteria.
- CI full verification runs complete projects without `--filter`, with an isolated PostgreSQL service per .NET matrix job. Angular `test:ci` is run in CI. No suite, test class or discover-zero result is credited unless it actually executed.
- Keep all counts/commits/run IDs and evidence in `docs/execution/status.json` under project rules. Report omitted local tests as `NOT RUN`, CI awaiting completion as `CI PENDING`, and uncertain live gates as `BLOCKED_EXTERNAL`. Local simulations never promote external readiness.

## 5. Independent and external gates

A full automated CI PASS **does not** certify Entra tenant consent, Graph/SharePoint site grants, live release, source-provider correctness, legal/compliance or professional audit opinions. Those require the named owner, authorized environment, exact evidence, appropriate independent approval and the relevant current requirements/ADR. Record unresolved gates as `BLOCKED_EXTERNAL`; do not bypass maker/checker, immutable records or exact financial correctness for faster delivery.
