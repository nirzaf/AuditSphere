---
id: "T023"
work_package: "R2R-06"
modules: [22]
status: "NOT_STARTED"
depends_on: ["T022"]
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
# T023 — Build reflection treatment, sealed plans and adjusted TB snapshots

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Original work-package order](../../reference/auditsphere-r2r-reference-execution-coordination-and-handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Produce the single effective adjustment calculation consumed by reconciliation and reporting.

**Original work package:** `R2R-06` — M22 journals, independent technical/management decisions, reflection and adjusted snapshots
**Original package exit:** Accepted/rejected/partial/reflected/unknown and replacement-base tests; no double application.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T022 — Implement adjustment decisions, revisions and reversals](auditsphere-r2r-task-t022-adjustment-decisions-revisions-reversals.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/auditsphere-r2r-reference-shared-domain-persistence-cqrs-blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/auditsphere-r2r-reference-lifecycle-lineage-and-module-ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/auditsphere-r2r-reference-contract-clarifications-and-resource-limits.md)
- [22 Adjustments and Journals Contract](../../modules/auditsphere-r2r-module-22-adjustments-journals-contract.md)

## Sequential work

1. Assess each journal against the selected base: Not Reflected, Reflected, Partially Reflected or Unknown.
2. Build eligibility from technical and management decisions plus explicit reflection evidence and required purpose rules.
3. Seal exact plan membership and create adjusted snapshots from raw plus eligible not-reflected contributions.
4. Show raw/delta/adjusted values and reasons for exclusion; propagate new-AJE and replacement-base invalidation.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 22: exact models, invariants and state transitions](../../modules/auditsphere-r2r-module-22-adjustments-journals-contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 22: exact mappings, keys, indexes and migration proposals](../../modules/auditsphere-r2r-module-22-adjustments-journals-contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `AssessJournalReflectionCommand(JournalReflectionDto, Meta)` | `MutationReceiptDto` | Base and journal match; source refs valid; ambiguous/partial remains blocked for final inclusion. |
| `SealAdjustmentPlanCommand(AdjustmentPlanInputDto, Meta)` | `MutationReceiptDto` | Recompute complete eligible journal/reflection set and generation; unresolved treatment blocks. |
| `BuildAdjustedTrialBalanceCommand(PlanRef, Meta)` | `OperationTicketDto` | Current sealed plan; deterministic snapshot, idempotent key and no raw-source mutation. |
| `GetAdjustedTrialBalanceQuery(SnapshotRef, AccountFilter, PageRequest)` | `PageDto<AdjustedBalanceRowDto>` | Raw, reporting delta, adjusted, source account, journal contributions and mapping coverage. |
| `GetAdjustmentEligibilityQuery(Context)` | `AdjustmentEligibilityDto` | Exact membership revision, eligible/excluded/blocked journals and reason codes. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 22: exact input/output DTO fields and supporting request rules](../../modules/auditsphere-r2r-module-22-adjustments-journals-contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/auditsphere-r2r-tracker-command-query-ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Effective adjustment plan and immutable adjusted TB.
- Eligibility/currentness DTO and exact plan/source manifest.

**Direct consumers unlocked by this task:**

- [T024 — Build reconciliation schedules and typed source items](../07_M23_Reconciliations/auditsphere-r2r-task-t024-reconciliation-schedules-typed-source-items.md)

- [Module 22: exact producer/consumer boundaries](../../modules/auditsphere-r2r-module-22-adjustments-journals-contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 22: required component tree, state and forms](../../modules/auditsphere-r2r-module-22-adjustments-journals-contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Reflected contributes zero additional amount; unknown/partial reflection blocks final treatment.
- New accepted AJE changes set membership and invalidates dependent output.
- Replacement TB already containing the AJE is not adjusted twice.

**Source-named test families (proposed unless current source confirms them):**

- Module 22: `AdjustmentEligibilityTests`, `AdjustmentPlanLineageTests`, `AdjustmentJournalEditorTests`, `R2R22AdjustmentJourneys`. [Exact source cases](../../modules/auditsphere-r2r-module-22-adjustments-journals-contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

### Original acceptance criteria primarily owned here

| Original ID | Exact wording — not shortened |
|---|---|
| VP-038-AC01 | Given a balanced journal, when independently reviewed and management-accepted, then its effect is included once in the selected reporting layer. |
| VP-038-AC02 | Given a replacement TB already containing that journal, when marked reflected with evidence, then additional effect is zero and no double counting occurs. |
| VP-038-AC03 | Unknown/partial reflection blocks final reporting inclusion until resolved; changed source or journal revision stales the relevant decision. |

**Related original stories:** `VP-038`. [Full preserved wording and production interpretation](../../reference/auditsphere-r2r-reference-original-52-acceptance-criteria.md).

**Related integration journeys:** [R2R-AT-10](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-10), [R2R-AT-11](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-11), [R2R-AT-12](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-12).

**Golden fixtures:** [GOLD-R2R-01](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-01), [GOLD-R2R-08](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-08). Fixture use requires the original policy approval; the numbers are synthetic QA values.

### Audit adjusted TB verification and reflection requirements
- Ensure all agreed adjustments are posted and reflected in the final TB (`AWP-19-07`).

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-026 | Primary |
| AS-AUD-026-AC07 | Covered |
| AS-AUD-026-AC08 | Covered |
| AS-AUD-026-AC09 | Covered |
| AS-AUD-026-AC10 | Covered |
| AS-AUD-026-AC11 | Covered |
| AWP-19-07 | Covered |

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
