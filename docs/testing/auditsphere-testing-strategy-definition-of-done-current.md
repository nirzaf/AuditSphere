# AuditSphere — Test Strategy and Definition of Done

**Status: CURRENT.** Applies to human developers and AI coding agents. `AGENTS.md` §3 is the norm for local/CI selection; this document defines evidence and risk-based test design. Never equate CI omission, skipped tests or a discovered-zero test run with a pass.

## 1. Execution tiers and ownership

For browser-visible changes, use the built-in browser as the primary dynamic verification where available: open the target page, exercise the relevant flow, check responsive rendering and inspect console/network errors. Use computer use only when the built-in browser is insufficient. Full automated browser journeys remain in CI/CD.

For changed Angular workflows, use the scoped [rendered accessibility checklist](auditsphere-testing-checklist-angular-accessibility-current.md). Record per-surface observations and source identity in `docs/execution/status.json`; a browser accessibility tree or automated rule pass does not replace the required knowledgeable human screen-reader review.

| Tier | Trigger and owner | What runs | What a PASS proves |
| --- | --- | --- | --- |
| **Local fast feedback** | Developer/agent once after a coherent behavior slice | Only affected compilation/contract validation and smallest meaningful existing/new test cohort. For docs-only changes, no application test. | The explicitly selected behavior/checks only. |
| **Push/PR fast CI** | `.github/workflows/ci.yml` (PR, `master` push and manual dispatch) | Full Angular build/specs, pinned locked-restore Release solution build, database-free .NET architecture/rounding and OpenAPI semantic guards, EF/OpenAPI drift, publication and documentation checks. | That workflow's checks for its exact commit; **not** complete .NET regression. |
| **Full regression CI** | `.github/workflows/release-verification.yml` (nightly or manual release candidate) | The initial `resolve-ref` job checks out the requested branch/tag and resolves its commit; all downstream verification jobs check out that immutable SHA. They run unfiltered Domain, API and Playwright .NET suites with isolated PostgreSQL, Angular full specs, build/contracts and aggregate `release-gate`. | Complete **local/synthetic automated** regression for the tested SHA, **not** live tenant or professional approval. |
| **External/professional acceptance** | Approved operator and independent reviewer | Real authorized tenant/provider operations and human professional decisions under approved gates. | Only precisely recorded real-world observations and approvals. |

Acceptance image builds use `scripts/diagnostics/image-build-memory.sh` for bounded read-only host, cgroup and BuildKit memory samples. Probe or temporary-file failure records unavailable telemetry while preserving the original build command's exit. The focused `scripts/diagnostics/test-image-build-memory.sh` adapter harness checks exit preservation, output/privacy and cleanup; it does not establish live Linux memory behavior. Actual limits, peaks and memory events need source-bound CI logs before a capacity diagnosis. The observer changes no build limits, retries, test expectations or release gates, and reports do not establish an image or full-regression pass. Its `scripts/diagnostics/image-build-process-memory.sh` helper privately classifies compiler arguments and emits only bounded process identity, validated start ticks and numeric status/smaps counters. The existing Docker probe receives helper source on explicit stdin. Filesystem fixtures and the stdin adapter detect missing memory counters and private-argument leakage; empty or unavailable process data is not a zero-memory result. OS anonymous/file counters remain separate from managed/native heap allocation and kernel-victim attribution.

Full regression jobs stream per-test console progress and retain TRX/blame sequence evidence. A single case stalled for 15 minutes terminates its test host and fails the gate; memory dumps are disabled. An interrupted run is incomplete and cannot establish full regression acceptance.

A catalogue response does not establish that dependent retained evidence has loaded. Native successor reviewer journeys must observe the exact authorized proof GET and completed response before asserting approval controls; keep unchecked assent, independent authority and exact retained hashes as separate assertions. Controlled response delay may exercise that ordering without changing application deadlines or expected results.

**Efficiency rule:** Local agents must **not** run full `.NET` solution tests, all Angular specs or all E2E by default; use the narrowest credible proving tests after a coherent slice. The combined unfiltered Domain/API/PostgreSQL/browser release gate belongs in `.github/workflows/release-verification.yml` (fast `ci.yml` also runs the full Angular spec suite), against that workflow's resolved immutable commit SHA; do not run full suites locally. Cached restore/build artifacts may be reused when sound. Fast CI is deliberately not expanded into full PostgreSQL/E2E for every PR.

Angular ESLint is a separate static check in the existing fast and full CI
workflows. For local lint feedback, use the `lint:files` script with explicit
changed production paths; an inline template is linted through its owning
TypeScript file. Do not run the full `lint:ci` tree locally during routine
development. A lint result does not replace builds, behavior tests, browser
verification or the full CI suites.

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

Session-revocation journeys must account for the shell clearing protected controls during session refresh. Revalidate the protected route when those controls may already be gone, and retain denial, private-data removal and direct API refusal assertions. A controlled reproduction must preserve the original assertions and exclude temporary fixtures from publication.

Relational catalog candidates without fixture rows are unverified. Seed a valid positive control before claiming a scoped-parent refusal, and attribute the denial to its actual constraint or publication guard. The staffing receipt schema test exercises both foreign parent references and retained bytes; its owned-template trigger counterfactual establishes detection without changing product migrations or weakening assertions. The library-version regression and upgrade-refusal checks cover a scoped document reference and preservation of inconsistent history when migration fails. Published and superseded library metadata probes preserve native publication/supersession controls and exact retained bytes; upgrade/draft-only downgrade and retained-publication downgrade refusal are separate checks. Physical-movement parent probes use native registration/transfer and a same-firm insertion control; migration checks preserve clean history and lookup indexes and reject inconsistent history atomically. Procedure/physical-link probes retain native registration/link and foreign-command denials, a nonduplicating same-firm SQL control, both foreign parents, inconsistent child client/engagement fields and exact retained bytes. Their clean upgrade/downgrade and inconsistent-history refusal check original constraints and indexes separately. Procedure/client-evidence probes use native synthetic received transfers and independent identities, native command denials, a nonduplicating SQL control and foreign procedure/upload or mismatched child client/engagement/request references. Exact link bytes, request identity and received hashes remain asserted. Clean upgrade/downgrade preserve links/uploads and original indexes; inconsistent retained request/upload lineage refuses upgrade with prior constraints/history intact. Ad hoc history probes retain native Insert/Edit wordings, foreign-command denials, nonduplicating same-firm insertion/revision controls, both foreign procedure references, insertion client/engagement mismatches and exact retained bytes. Clean upgrade/downgrade and inconsistent-history refusal preserve original constraints and indexes. Trigger presence alone does not prove mutation behavior or restricted-role enforcement.

Adjustment-source probes retain native draft/post/plan controls with exact balanced amounts, foreign-command denials, nonduplicating SQL controls, foreign base datasets or mismatched client fields and exact journal/plan/source headers and line bytes. Clean upgrade/downgrade preserve financial evidence, original indexes and prior constraints; inconsistent retained sources refuse upgrade atomically. Broader accounting methodology, restricted-role, mutation and professional acceptance remain separate.

**Example lean sets:** docs typo → zero application tests; a pure rounding rule → existing boundary theory; a scoped journal posting change → selected DB-backed permission, balance, stale/replay and rollback cases, often grouped in one fixture; a decoder change → one targeted Angular spec using parameterized invalid payloads; a new multi-actor approval path → a relevant existing integration journey, or one new one only if not otherwise proved.

## 4. Existing testing infrastructure

- Restored suites (STE-NXT-001; ADR-0008 `SUPERSEDED`) include `tests/AuditSphereOps.Domain.Tests/`, `tests/AuditSphereOps.Api.Tests/`, `tests/AuditSphereOps.E2E.Tests/`, and Angular specs. Do not run the historical restore-from-deletion command again; it could overwrite later work.
- The .NET integration/API suites use owned synthetic fixtures and PostgreSQL `auditsphere_tests`; never use the development or production database. `PgTestSchema` and `OwnedHost` are existing fixtures. Use the exact test-project and selector from the code map.
- `./scripts/agent-check` currently supports bounded `engagement-ui`, `review-permissions`, `accounting-calculation` packets and optional layers. Preflight is not a test run; `--run` is explicit. See `docs/testing/auditsphere-testing-guide-agent-capability-checks-current.md`. A passing packet does not prove omitted or external criteria.
- CI full verification runs complete projects without `--filter`, with an isolated PostgreSQL service per .NET matrix job. Angular `test:ci` is run in CI. No suite, test class or discover-zero result is credited unless it actually executed.
- Keep all counts/commits/run IDs and evidence in `docs/execution/status.json` under project rules. Report omitted local tests as `NOT RUN`, CI awaiting completion as `CI PENDING`, and uncertain live gates as `BLOCKED_EXTERNAL`. Local simulations never promote external readiness.

## 5. Independent and external gates

For monetary-unit sampling, use bounded ordinary fixtures to compare historical selection and explanation behavior. Exercise extreme amount/interval ratios only against the bounded implementation; never use the old interval-addition loop as a performance baseline for those inputs. Numeric-limit checks must distinguish a valid final crossing from a controlled unsupported-range or precision refusal.

A full automated CI PASS **does not** certify Entra tenant consent, Graph/SharePoint site grants, live release, source-provider correctness, legal/compliance or professional audit opinions. Those require the named owner, authorized environment, exact evidence, appropriate independent approval and the relevant current requirements/ADR. Record unresolved gates as `BLOCKED_EXTERNAL`; do not bypass maker/checker, immutable records or exact financial correctness for faster delivery.

### Operational evidence record (WU-038)

Use `docs/spec/completion-operational-evidence.json` as the incomplete starting record. Copy it into ignored `artifacts/completion/` before collecting current-candidate evidence; populating a tracked template in place makes the checkout dirty and cannot prove a clean source. Record actual source, tool versions, exits, feeds, scan output and measurements rather than editing a summary to imply success.

Run an existing scanner through the bounded capture helper from the repository root with the pinned SDK/Node and already-restored dependencies. It does not install packages, restore projects, change configuration or perform recovery:

```bash
mkdir -p artifacts/completion
python3 scripts/verification/capture-operational-scan.py --scan nuget > artifacts/completion/nuget-capture-result.json
# Other fixed adapters: --scan npm or --scan gitleaks.
python3 scripts/verification/verify-operational-evidence.py artifacts/completion/operational-evidence.json
```

Copy the capture result's `scanRecord` into the matching `securityScans` entry of your ignored record copy; retain its referenced envelope/report files. `CAPTURED` means the actual invocation/report was retained, not that security acceptance passed. The helper binds source/input/configuration hashes, tool version, actual exit and feed identity where available, with bounded output/time and child cleanup. It stores stderr's hash/count, not its potentially sensitive text. Dirty/changed source, unavailable tools/feeds, unsupported reports and diagnostic uncertainty remain unverified; npm's audit JSON does not establish registry identity. NuGet audit-source warnings must not become a zero-findings pass ([warning definitions](https://github.com/NuGet/docs.microsoft.com-nuget/blob/main/docs/concepts/Auditing-Packages.md#warning-codes)). Never replace an unknown result with an older clean scan.

The full release-verification workflow also captures npm after its existing Angular install and NuGet after its existing solution restore/build. Source-bound `release-verification-operational-scan-npm-*` and `release-verification-operational-scan-nuget-*` artifacts retain the helper result, native capture/report and requested-ref/resolved-commit metadata. Helper diagnostics are capped separately; underlying scanner stderr stays hash/count only. These captures leave the existing test gates intact and record `NOT_ASSESSED`, including when collection fails. Review the evidence with the operational verifier; collection is not a security pass.

The verifier performs read-only Git/source and bounded, hash-bound JSON checks. It compares supported scanner findings and typed measurement artifacts with the declared record, rejects unsafe paths/symlinks and vacuous counts, and preserves local restore as local-only. Exit codes are `0` for a structurally complete evidence record, `1` for invalid input and `2` for incomplete evidence. A matching reference does not authenticate a human approval or establish operational/production acceptance. Unsupported report shapes or absent feed/capture provenance remain unknown; approved targets, measured recovery and independent custody stay separate gates. Actual findings and focused verification belong in `docs/execution/status.json`.

Sampling completion checks must exercise positive/adverse/new-positive review ordering, new test revisions, pending results and unchanged-count source changes through SRM and dependent decisions. Verify concurrent decisions using database barriers rather than timing guesses. Upgrade fixtures preserve legacy rows exactly and exercise conservative timestamp ties, sequenced new decisions and refusal to discard retained ordering. Required evidence-reference checks cover direct commands and actual HTTP binding, including meaningful physical references.
