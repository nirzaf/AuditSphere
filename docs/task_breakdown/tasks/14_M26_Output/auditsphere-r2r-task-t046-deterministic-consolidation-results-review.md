---
id: "T046"
work_package: "R2R-14"
modules: [26]
status: "NOT_STARTED"
depends_on: ["T045"]
owner: ""
reviewer: ""
review_decision: ""
reviewed_commit: ""
evidence_ref: ""
approval_ref: ""
blocked_reason: ""
branch: ""
issue_pr: ""
updated_at: ""
---
# T046 — Build deterministic consolidation results and independent review

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Original work-package order](../../reference/auditsphere-r2r-reference-execution-coordination-and-handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Produce one canonical group result from complete exact component, rate and journal inputs.

**Original work package:** `R2R-14` — M26 elimination journals, deterministic group runs, independent review and group packages
**Original package exit:** Investment/equity and intercompany golden results, source nonmutation, group-only artifact scope.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T045 — Build balanced elimination journals and independent review](auditsphere-r2r-task-t045-balanced-elimination-journals-review.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/auditsphere-r2r-reference-shared-domain-persistence-cqrs-blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/auditsphere-r2r-reference-lifecycle-lineage-and-module-ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/auditsphere-r2r-reference-contract-clarifications-and-resource-limits.md)
- [26 Consolidation Contract](../../modules/auditsphere-r2r-module-26-consolidation-contract.md)

## Sequential work

1. Capture full effective perimeter/component/rate/journal membership and policy/calculator versions.
2. Calculate component, translated, alignment, elimination and final columns with source contributions.
3. Recompute the complete input manifest under review/publication locks; stale runs remain historical, not current.
4. Provide group reports/drill-down with permitted summaries and no ungranted raw-file disclosures.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 26: exact models, invariants and state transitions](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 26: exact mappings, keys, indexes and migration proposals](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `BuildConsolidationRunCommand(ConsolidationRunInputDto, Meta)` | `OperationTicketDto` | Full effective member/component/journal set, approved policy, no unsupported profile or unexplained required mismatch. |
| `ReviewConsolidationRunCommand(RunRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Recompute current complete input manifest; validate columns/equations; independent group review. |
| `GetConsolidationReportQuery(RunRef, PageRequest)` | `ConsolidationReportDto` | Component/alignment/elimination/group totals, input manifest and currentness; stale current report not asserted approved. |
| `GetGroupSourceDrillDownQuery(RunRef, OutputLineId, PageRequest)` | `PageDto<GroupContributionDto>` | Group-permitted contributions; raw component download requires separate component authorization. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 26: exact input/output DTO fields and supporting request rules](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/auditsphere-r2r-tracker-command-query-ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Independently reviewed ConsolidationRun and canonical group DTO.
- Group validation/reporting and exact lineage.

**Direct consumers unlocked by this task:**

- [T047 — Assemble reviewed group packages through the shared artifact kernel](auditsphere-r2r-task-t047-assemble-group-packages-artifact-kernel.md)

- [Module 26: exact producer/consumer boundaries](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 26: required component tree, state and forms](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- GOLD-R2R-06 ends at assets/equity 8,000 with liabilities zero.
- Concurrent component replacement cannot approve an old run.
- New components or journals change membership identity.

**Source-named test families (proposed unless current source confirms them):**

- Module 26: `ConsolidationPerimeterTests`, `ConsolidationCurrencyAndEliminationTests`, `ConsolidationInputManifestTests`, `ConsolidationWorkspaceTests`, `R2R26GroupJourneys`. [Exact source cases](../../modules/auditsphere-r2r-module-26-consolidation-contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

### Original acceptance criteria primarily owned here

| Original ID | Exact wording — not shortened |
|---|---|
| VP-045-AC04 | A component/rate/perimeter change stales dependent elimination approval and preserves the previous decision and journal revision. |
| VP-046-AC01 | Given compatible reviewed components and approved adjustments, when the supported fixture is consolidated, then consolidated = translated components + approved group adjustments/eliminations. |
| VP-046-AC02 | Statement equations and reconciliation columns agree with fixed expected fixture values; unresolved required inputs prevent a ready-for-review state. |
| VP-046-AC03 | Group output review binds to the exact perimeter/component/rate/elimination revisions; edits require fresh review. |

**Related original stories:** `VP-043`, `VP-044`, `VP-045`, `VP-046`. [Full preserved wording and production interpretation](../../reference/auditsphere-r2r-reference-original-52-acceptance-criteria.md).

**Related integration journeys:** [R2R-AT-22](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-22), [R2R-AT-23](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-23), [R2R-AT-24](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-24), [R2R-AT-25](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-25).

**Golden fixtures:** [GOLD-R2R-06](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-06), [GOLD-R2R-07](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-07). Fixture use requires the original policy approval; the numbers are synthetic QA values.

## Completion checklist

<!-- COMPLETION-CHECKLIST -->
- [ ] Current checkout, existing symbols and applicable approvals were inspected; scope conflicts are resolved or the task is BLOCKED.
- [ ] All hard dependencies are COMPLETED and their exact contracts/evidence were consumed.
- [ ] The task-specific work and every applicable invariant/owned request are implemented or proven already implemented; no placeholder outcome remains.
- [ ] Applicable migrations, validation, authorization, concurrency and source/history preservation checks have observed results.
- [ ] Required task-level tests pass with named expected/observed outcomes; future integration tests remain explicitly tracked instead of claimed complete.
- [ ] The independent reviewer accepted the exact reviewed commit and evidence; downstream owners received the handoff.
<!-- END-COMPLETION-CHECKLIST -->

## Evidence and handoff record

| Field | Value to record |
|---|---|
| Inspected baseline and reused symbols | Not recorded |
| Code commit / schema / deployed build if applicable | Not recorded |
| Requirement → assertion → command/run → observed result | Not recorded |
| Policy / scope approval reference | Not recorded |
| Known limitations / exact blocker | Not recorded |
| Exported contract / manifest / artifact references for consumers | Not recorded |
| Reviewer and acceptance decision | Not recorded |

**Tracking-only note:** filling these fields or running the status helper is not proof that tests ran, a professional approval, merge authorization or permission to perform a tenant operation.
