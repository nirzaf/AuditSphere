---
id: "T026"
work_package: "R2R-07"
modules: [23]
status: "NOT_STARTED"
depends_on: ["T025"]
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
# T026 — Implement reconciliation review, rework and export

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Original work-package order](../../reference/auditsphere-r2r-reference-execution-coordination-and-handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Complete independent reconciliation decisions and preserve histories on replacement or carry-forward.

**Original work package:** `R2R-07` — M23 bank/subledger source schedules, typed items, correction links, proof/review/rework
**Original package exit:** Raw and adjusted-basis proofs agree without duplicate corrections; zero unexplained residual.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T025 — Implement reconciliation proof and correction-journal handoff](auditsphere-r2r-task-t025-reconciliation-proof-correction-journals.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/auditsphere-r2r-reference-shared-domain-persistence-cqrs-blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/auditsphere-r2r-reference-lifecycle-lineage-and-module-ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/auditsphere-r2r-reference-contract-clarifications-and-resource-limits.md)
- [23 Reconciliations Contract](../../modules/auditsphere-r2r-module-23-reconciliations-specialist-schedules-contract.md)

## Sequential work

1. Require exact proof/source/evidence identity and zero unexplained residual at submit/review.
2. Implement independent review/return, successor revision and explicit manual carry-forward to a valid target context.
3. Recheck currentness after source/evidence replacement; leave old reviewed snapshots readable as history.
4. Export permitted proof inputs and totals and hand readiness to statements/close.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 23: exact models, invariants and state transitions](../../modules/auditsphere-r2r-module-23-reconciliations-specialist-schedules-contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 23: exact mappings, keys, indexes and migration proposals](../../modules/auditsphere-r2r-module-23-reconciliations-specialist-schedules-contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `SubmitReconciliationCommand(ScheduleId, ExpectedProof: Ref, Meta)` | `MutationReceiptDto` | Exact proof current, required answers/evidence and correction dispositions complete, residual zero. |
| `ReviewReconciliationCommand(ReconciliationReviewDto, Meta)` | `MutationReceiptDto` | Independent assigned reviewer; recompute proof/currentness; return/reject requires rationale. |
| `ReviseReconciliationCommand(Original: Ref, Reason, Meta)` | `MutationReceiptDto` | Preserve old schedule and review; new revision starts unapproved. |
| `CarryForwardReconciliationItemsCommand(Original: Ref, TargetContext, SelectedItemIds[], Meta)` | `MutationReceiptDto` | Explicit next-period basis; copy selected unresolved references only; no prior proof or approval. |
| `ExportReconciliationQuery(ScheduleRef, Format)` | `ExportTicketDto` | Authorized exact revision/proof, currency, evidence/source IDs and no misleading current claim. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 23: exact input/output DTO fields and supporting request rules](../../modules/auditsphere-r2r-module-23-reconciliations-specialist-schedules-contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/auditsphere-r2r-tracker-command-query-ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Approved/returned reconciliation lifecycle and immutable evidence.
- Proof export and required-reconciliation readiness contract.

**Direct consumers unlocked by this task:**

- [T027 — Build bounded statement-layout definitions and publication](../08_M24_Statements/auditsphere-r2r-task-t027-statement-layout-definitions-publication.md)

- [Module 23: exact producer/consumer boundaries](../../modules/auditsphere-r2r-module-23-reconciliations-specialist-schedules-contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 23: required component tree, state and forms](../../modules/auditsphere-r2r-module-23-reconciliations-specialist-schedules-contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Missing evidence, residuals, same-person review and stale inputs are rejected.
- Carry-forward does not copy an old approval into the new context.
- Raw and adjusted-basis proofs agree without duplicate corrections.

**Source-named test families (proposed unless current source confirms them):**

- Module 23: `ReconciliationProofTests`, `ReconciliationRevisionTests`, `ReconciliationEditorTests`, `R2R23ReconciliationJourneys`. [Exact source cases](../../modules/auditsphere-r2r-module-23-reconciliations-specialist-schedules-contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

### Original acceptance criteria primarily owned here

| Original ID | Exact wording — not shortened |
|---|---|
| VP-039-AC02 | An unexplained nonzero residual or missing required evidence blocks approval; proposed corrections cannot masquerade as already cleared timing items. |
| VP-039-AC03 | An accepted source/evidence replacement makes current reconciliation review stale; the previous approved snapshot remains viewable. |
| VP-039-AC04 | Item currency/date/scope validation and independent reviewer checks work; no bank feed, automated matching, payment initiation or tax integration is introduced. |

**Related original stories:** `VP-039`. [Full preserved wording and production interpretation](../../reference/auditsphere-r2r-reference-original-52-acceptance-criteria.md).

**Related integration journeys:** [R2R-AT-04](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-04), [R2R-AT-05](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-05), [R2R-AT-13](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-13).

**Golden fixtures:** [GOLD-R2R-03](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-03). Fixture use requires the original policy approval; the numbers are synthetic QA values.

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
