---
id: "T024"
work_package: "R2R-07"
modules: [23]
status: "NOT_STARTED"
depends_on: ["T023"]
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
# T024 — Build reconciliation schedules and typed source items

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Original work-package order](../../reference/05_Execution_Coordination_and_Handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Prepare bank/subledger reconciliations with explicit source basis, as-of date and item semantics.

**Original work package:** `R2R-07` — M23 bank/subledger source schedules, typed items, correction links, proof/review/rework
**Original package exit:** Raw and adjusted-basis proofs agree without duplicate corrections; zero unexplained residual.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T023 — Build reflection treatment, sealed plans and adjusted TB snapshots](../06_M22_Adjustments/023_Build_reflection_treatment_sealed_plans_and_adjusted_TB_snapshots.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/03_Shared_Domain_Persistence_CQRS_Blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/04_Lifecycle_Lineage_and_Module_Ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/06_Contract_Clarifications_and_Resource_Limits.md)
- [23 Reconciliations Contract](../../modules/23_Reconciliations_Contract.md)

## Sequential work

1. Reuse source-bound reconciliation models and exact evidence/snapshot references.
2. Define ledger/supporting side, sign convention, currency and dated Timing/Correction/Informational item treatment.
3. Persist draft revisions and guarded source relationships; server-derive ledger totals instead of accepting caller totals.
4. Build register/source selectors and item grid with dirty tracking, evidence links and version display.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 23: exact models, invariants and state transitions](../../modules/23_Reconciliations_Contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 23: exact mappings, keys, indexes and migration proposals](../../modules/23_Reconciliations_Contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `CreateReconciliationCommand(ReconciliationDraftDto, Meta)` | `MutationReceiptDto` | Current scoped basis, explicit sign/as-of/currency, required support reference and account coverage. |
| `SaveReconciliationDraftCommand(ReconciliationEditDto, Meta)` | `MutationReceiptDto` | Draft/returned state; valid item types/sides/dates and no duplicate item identity. |
| `GetReconciliationRegisterQuery(Context, KindFilter, AsOfFilter, PageRequest)` | `PageDto<ReconciliationSummaryDto>` | Current and historic state, residual and responsible persons; scope applied before totals. |
| `GetReconciliationWorkspaceQuery(ScheduleId)` | `ReconciliationWorkspaceDto` | Header/items, live evidence/reflection applicability, formula, proof and review history. |
| `GetReconciliationSourceRowsQuery(ScheduleId, SourceSide, PageRequest)` | `PageDto<ReconciliationSourceRowDto>` | Exact ledger/subledger/statement rows and extraction provenance. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 23: exact input/output DTO fields and supporting request rules](../../modules/23_Reconciliations_Contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/01_Command_Query_Ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Typed reconciliation schedule and source/provenance DTOs.
- Editable schedule UI and scoped source drill-down.

**Direct consumers unlocked by this task:**

- [T025 — Implement reconciliation proof and correction-journal handoff](025_Implement_reconciliation_proof_and_correction_journal_handoff.md)

- [Module 23: exact producer/consumer boundaries](../../modules/23_Reconciliations_Contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 23: required component tree, state and forms](../../modules/23_Reconciliations_Contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Reject cross-period/currency/scope sources and invalid item dates.
- Manual supporting figures remain identified as manual with evidence.
- Draft edits do not overwrite prior approved proofs.

**Source-named test families (proposed unless current source confirms them):**

- Module 23: `ReconciliationProofTests`, `ReconciliationRevisionTests`, `ReconciliationEditorTests`, `R2R23ReconciliationJourneys`. [Exact source cases](../../modules/23_Reconciliations_Contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

**Related original stories:** `VP-039`. [Full preserved wording and production interpretation](../../reference/08_Original_52_Acceptance_Criteria.md).

**Related integration journeys:** [R2R-AT-01](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-01), [R2R-AT-02](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-02), [R2R-AT-13](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-13).

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
