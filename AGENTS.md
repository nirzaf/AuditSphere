# AuditSphereOps — AGENTS.md

**Mission:** implement the smallest correct change with the fastest trustworthy feedback. Optimize agent context, local test time and PR scope; never weaken client isolation, accounting integrity, immutable evidence or human audit approvals. Read this file at session start, then only the task-relevant sources.

## 1. Navigate before coding

1. Restate intent, existing behavior, non-goals, acceptance criteria and affected module. For a small fix, implement directly; plan only multi-boundary work.
2. Read the assigned story and `docs/architecture/auditsphere-architecture-code-map.md` before searching. Follow existing code paths, analogous commands, contracts and nearby tests. Prefer targeted `rg`/file reads, not repo-wide dumps.
3. **Authority:** `docs/auditsphere-docs-index.md` → relevant `CURRENT`/`APPROVED` requirements, `docs/architecture/auditsphere-architecture-current-architecture.md` and ADRs. `PROPOSED`, `HISTORICAL_SOURCE` and `SUPERSEDED` content does not authorize implementation.
4. **Task state:** `docs/execution/auditsphere-execution-index-task-board-current.md`. Read only relevant/newest sections of the current-slice handoff; query `docs/execution/status.json` with `jq` for specific fields. Historical verification is not today's verification.
5. If essential context is missing, ask a focused question or report `BLOCKED_CONTEXT`. If authority conflicts, report `BLOCKED_DECISION`. Do not guess paths, signatures, contracts or results.

Apply an approved ADR within its explicit scope without asking for the same decision again. The owner delegates reversible documentation corrections and routine engineering choices that comply with current requirements and approved ADRs: choose the smallest backward-compatible, fail-closed change and record its rationale and acceptance rule. This delegation does not supply professional policy, human review, release evidence or permission for external effects.

## 2. Technical boundaries

**Stack:** pinned .NET 10 / C# 14, ASP.NET Core API, Angular 22 / Material-CDK, EF Core 10 / Npgsql / PostgreSQL 18.6, .NET Worker and existing durable operations. Honor `global.json`, `.config/dotnet-tools.json`, `Directory.Packages.props`, NuGet locks and `src/AuditSphereOps.Ui/.nvmrc`.

| Directory under `src/` | Owns |
| --- | --- |
| `AuditSphereOps.Domain/` | Domain types, invariants, pure deterministic calculators |
| `AuditSphereOps.Application/` | Static commands/queries, authorization, financial workflows and durable-operation contracts |
| `AuditSphereOps.Infrastructure/` | EF Core, capability-split `AuditSphereDbContext`, migrations, providers |
| `AuditSphereOps.Api/` | Authenticated `/api/ui/*` endpoints, HTTP/OpenAPI contracts, Angular serving |
| `AuditSphereOps.Ui/` | Angular staff/client UI, same-origin API, server-projected `can*` states |
| `AuditSphereOps.Worker/` | Durable background work, retries and reconciliation |

Preserve three independent financial domains: firm's own ledger/billing, per-client accounting/TB/GL/adjustments, and group consolidation. Consolidation never writes component books; client books never post into the firm's ledger. Native client bookkeeping requires an explicitly approved service/cutover; import-first clients must continue working. VAT/tax is separately scoped. Inventory/procurement operations, payroll execution, payment initiation and external tax filing are not implied.

Use existing partial capability files (`<Service>.<Capability>.cs`, `AuditSphereDbContext.<Module>.cs`) and stable public APIs. Cross-layer work proceeds only as needed: Domain → Application → Infrastructure → API/OpenAPI → Angular. Never place business-state mutations/authorization solely in UI, or let Angular call Graph, access credentials or bypass API.

## 3. Fast, minimal implementation

- Change only what the requirement needs. Reuse nearby patterns/services/queries/calculators; no broad refactor, speculative abstraction, generic framework, duplicate workflow or unrelated formatting.
- Complete one coherent behavior per task/PR, including all inseparable layers. Keep diffs reviewable; stop and simplify on scope expansion.
- **Restore/install once:** `dotnet tool restore`, `dotnet restore AuditSphereOps.slnx --locked-mode` and `npm --prefix src/AuditSphereOps.Ui ci` only for a fresh environment or changed toolchain/lockfiles. Do not reinstall dependencies for every edit.
- Diagnose with the smallest applicable build/test, preferably once after the coherent edit. On failure rerun only the failed check after fixing it; don't rerun already-passing suites without relevant new changes.
- Never claim a simulation, TODO, proposed story, UI route or passing compile proves production readiness.

## 4. Test policy — local targeted; full suite in CI/CD

**Default:** full test suites run through CI/CD only. Do not run the complete .NET, Angular, or Playwright suites locally. For a coherent development slice, select the smallest credible local checks that cover the changed behavior and risk; run them once after the slice rather than after every edit.

| Change | Minimal local verification |
| --- | --- |
| Docs/comments/formatting | `git diff --check`; specific documentation validator if affected. Zero tests for nonbehavior changes. |
| Styles/markup only | Visual/targeted check; test only if interaction, a11y or contract behavior changed. |
| Calculator/domain rule | Existing focused xUnit class/method and a boundary assertion only if new behavior is uncovered. |
| Angular behavior/decoder | One affected `.spec.ts` with `--include`; Angular build when template/compilation risk warrants. Use the built-in browser for dynamic UI/endpoint/responsive checks when unit tests cannot establish behavior; full browser journeys run in CI/CD. |
| Command/API/persistence | Focused existing .NET test against dedicated test DB; only critical changed-behavior cases. |
| Authorization, ledger, approval, concurrency, migration, provider | Small focused positive/negative cohort covering the affected high-risk invariant. Never waive meaningful protection. |

Prefer existing bounded task packets when relevant:

```bash
./scripts/agent-check --list
./scripts/agent-check <capability>          # prerequisite check; runs no tests
./scripts/agent-check <capability> --run    # only the selected cohort
```

Otherwise select one named test, never the whole project:

```bash
# Substitute an existing class/method name.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~ChangedBehaviorTests'

# Substitute the existing affected Angular spec path.
npm --prefix src/AuditSphereOps.Ui run test:ci -- --include 'src/app/**/affected.spec.ts'

git diff --check
```

Use `tests/AuditSphereOps.Api.Tests/` or `tests/AuditSphereOps.E2E.Tests/` only when the modified HTTP/browser behavior needs that boundary. Do not use `--no-build` if changed source/test binaries were not rebuilt. PostgreSQL tests use `auditsphere_tests` on local port 5433 (or the approved isolated test setting), never development/production. Report `BLOCKED_TEST_ENV` if unavailable.

For browser-visible changes, prioritize the built-in browser for dynamic verification: open the target page, exercise the relevant flow, check responsive rendering and inspect console/network errors. Use computer use only when the built-in browser is insufficient. Record what was and was not verified.

Run additional checks only when impacted: EF model drift after model/migration changes, `bash scripts/contracts/verify-openapi.sh` after API contract changes, targeted .NET Release build for changed project, Angular build after substantive template changes. CI handles full solution build and general gates.

### Minimize the number of tests, not their value

- Extend a nearby existing behavior test before adding a new test file. Prefer one high-signal regression per changed behavior; add a distinct denial/boundary test where security, money, staleness or retries demand it. Parameterize equivalent cases rather than duplicating fixtures.
- Avoid unit/API/E2E copies of identical assertions, tests for trivial accessors, mass snapshots, implementation-mirroring tests, speculative case explosions and artificial coverage targets.
- Do not remove, skip, weaken or alter expected results to reduce test count. Consolidate genuine duplicates only when every unique invariant remains covered and the reduction is reviewed.
- New tests must derive expected results from requirements/accounting rules, not from whatever the new code happens to return. Test count is never a success metric.
- **Correctness over count:** Prioritize correctness against requirements over test quantity. Run relevant existing tests and add minimal, behavior-focused tests where they provide meaningful regression protection. Derive expected outcomes from requirements and established domain rules, not merely from the generated implementation. Do not weaken tests solely to make a failing patch pass. Validate end-to-end behavior where unit tests or mocks cannot establish correctness.

### CI/CD responsibilities (existing workflows)

- `.github/workflows/ci.yml`: fast push/PR gate with Angular build/specs, Release compilation, database-free guards, EF/OpenAPI drift, publication and docs. Passing this is not a full .NET regression pass.
- `.github/workflows/release-verification.yml`: full nightly / manually dispatched gate on an immutable SHA: complete unfiltered Domain, API, Playwright/PostgreSQL and Angular suites. Run complete regressions here, not after each local edit. Require successful current-commit release verification for releases or owner-designated high-risk gates.
- The release workflow is not currently required for PR merges. Never claim `FULL_CI_PASS` from `ci.yml` alone or from an earlier commit. To address a CI failure, rerun only the failing cohort in CI/CD; do not run full suites locally.
- Report tests executed with results, omitted/deferred CI suites and exact SHA. Distinguish `NOT_RUN`, `BLOCKED_TEST_ENV`, `FAST_CI_PASS`, `FULL_CI_PASS` and `BLOCKED_EXTERNAL`.

## 5. Product invariants — never compromise

- **Authorization:** Application commands/queries enforce current `FIRM_WIDE`, `CLIENT`, `ENGAGEMENT`, `GROUP` grants, session freshness and professional-work holds. Never widen sibling engagement/client scope; Angular `can*` flags are presentation only.
- **Immutability:** sealed datasets, approved mappings, posted journals, reviews, issued artifacts and release evidence retain append-only revision/hash lineage. No silent overwrite or self-approval.
- **Transaction safety:** revision/generation fences; appropriate existing lock order; idempotent retries and reconciliation of unknown external outcomes. Fail closed on stale/missing/unsupported evidence or rates.
- **Accounting:** balanced postings, correct money precision/rounding, controlled mapping/adjustment application, no invented balances/FX rates; consolidation does not modify client source data. Pure calculators have no EF, network, clocks or random IDs.
- **Audit:** professional opinions and final sign-offs are decisions by authorized humans, never generated autonomously.
- **M365:** obey `docs/architecture/auditsphere-m365-tenant-administration-permissions.md` and separately approved selected-client-sites exception. Least-privilege per-capability identities, verified grants and exact-resource boundaries; no broad tenant scopes or Entra administrator-role assignment. External effects remain off in local simulations; unverified live gates are `BLOCKED_EXTERNAL`.
- **External-provider boundaries:** Purview and eSignature provider integrations are out of scope. Preserve exact uploaded signed-document evidence, SHA-256 identities, human decisions and release manifests without claiming provider acceptance.

## 6. Prohibitions, approvals, completion

Do not introduce Frappe/ERPNext, a second ERP, React/Python backend, microservices, brokers, Kubernetes, MediatR/Wolverine/MassTransit, AutoMapper, generic repositories/UnitOfWork, event sourcing, ClamAV/antivirus or rate-limit infrastructure without separately approved scope. No incidental npm/NuGet packages. Never log/commit secrets, tokens, financial data or live tenant credentials.

Always try to work directly in the `master` branch. Commit and push each completed coherent slice directly to `master` as work progresses. Use another branch or worktree only when the user requests it or repository protections require it. Never force-push or rewrite history. Deployment, merges, production data/permission changes and destructive migrations require the owner's explicit authorization; green CI is not authorization for those actions.

**Owner-directed documentation reconciliation exception:** applies only when the owner explicitly names a documentation-reconciliation task and requests its dedicated branch/PR workflow. For that named task, use a dedicated `codex/` branch, preserve concurrent changes, and prepare a reviewable PR. Do not push this work directly to `master` or merge it without the exact owner codeword `AUDITSPHERE-MERGE-APPROVED`. Engineering delegation does not authorize professional audit/accounting judgments, Partner/client approval, legal/fee policy, posting, tenant permissions, certificate/legal/provider acceptance, deployment or irreversible actions. Keep each dependent gate with its qualified owner and required evidence; continue independent technical work.

On completion report changed files, behavior, focused verification and results, deferred CI, blockers and assumptions. Update `docs/execution/status.json` and current-slice handoff only with relevant observed evidence. Keep this file aligned with `docs/testing/auditsphere-testing-strategy-definition-of-done-current.md`. Wiki is the canonical operator deployment guide: update an existing page only for a material operator change and with explicit authorization; redact secrets and separate local from live instructions.

## 7. Autonomous completion assignment — standing continuation rule

Scope and execution detail: [`docs/architecture/auditsphere-autonomous-completion-remediation-spec.md`](docs/architecture/auditsphere-autonomous-completion-remediation-spec.md). Run state lives in ignored `.local/autonomous/` and is validated by `scripts/agent/verify-completion-state.py`.

The owner authorizes routine, reversible AuditSphere engineering and verification within the current approved scope without per-task review or repeated permission requests. Apply existing approved decisions, choose the smallest correct implementation, run focused checks, conduct technical review, follow the current allowed Git workflow, and continue to the next ready task.

A successful commit, focused test, fast CI result, partial verification or pending CI is not the end of the assignment. Maintain the autonomous checkpoint. While CI runs, complete independent work or monitor the exact run. On failure retrieve logs/artifacts, reproduce narrowly, repair and reverify. Do not ask the owner whether to fix an evidenced in-scope defect.

Use terminal/PowerShell and structured tools first. When insufficient, use the available built-in browser, then available computer-use tools, on the same authorized targets. Tool fallback never bypasses scope, consent, MFA, business review, unknown-outcome recovery or managed platform policy.

Separate implementation, local verification, browser verification, exact-source CI and external/professional acceptance. A human screen-reader review or tenant credential can block its dependent acceptance leaf, not all independent engineering. Never fabricate a real human decision or live-provider pass.

Preserve .NET/Angular/PostgreSQL architecture, financial boundaries, authorization, append-only evidence, exact monetary/revision values and idempotency. Reuse existing tests; full suites remain CI/CD-only. No test weakening, silent scope reduction, source-history rewriting or invented completion percentage.

When process/context limits intervene, atomically checkpoint task, diff, exact source, pending requests/CI and next action so the configured supervisor can resume without a new user instruction. Exit as completed only when the applicable completion function passes. If every remaining action needs unavailable external input, record WAITING_EXTERNAL truthfully, preserve resume state and issue one consolidated dependency report instead of repeated approval prompts.
