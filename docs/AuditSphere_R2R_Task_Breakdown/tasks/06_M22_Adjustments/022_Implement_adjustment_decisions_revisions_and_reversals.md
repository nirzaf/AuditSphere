---
id: "T022"
work_package: "R2R-06"
modules: [22]
status: "NOT_STARTED"
depends_on: ["T021"]
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
# T022 — Implement adjustment decisions, revisions and reversals

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Original work-package order](../../reference/05_Execution_Coordination_and_Handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Keep technical approval, management disposition and external-book reflection distinct.

**Original work package:** `R2R-06` — M22 journals, independent technical/management decisions, reflection and adjusted snapshots
**Original package exit:** Accepted/rejected/partial/reflected/unknown and replacement-base tests; no double application.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T021 — Build arbitrary-line adjustment drafts and submission](021_Build_arbitrary_line_adjustment_drafts_and_submission.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/03_Shared_Domain_Persistence_CQRS_Blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/04_Lifecycle_Lineage_and_Module_Ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/06_Contract_Clarifications_and_Resource_Limits.md)
- [22 Adjustments and Journals Contract](../../modules/22_Adjustments_and_Journals_Contract.md)

## Sequential work

1. Implement independent technical approve/return and evidence-backed signed-in/offline management acceptance/rejection.
2. Represent partial management acceptance without applying an unbalanced subset; obtain a fresh balanced revision where required.
3. Preserve exact reviewed content and append supersession/reversal history, with reason and correct decision actor/recorder.
4. Expose separate technical/management panels and explicit revise/reverse actions, never role-switch shortcuts.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 22: exact models, invariants and state transitions](../../modules/22_Adjustments_and_Journals_Contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 22: exact mappings, keys, indexes and migration proposals](../../modules/22_Adjustments_and_Journals_Contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `ReviewAdjustmentCommand(JournalId, DecisionInputDto, Meta)` | `MutationReceiptDto` | Exact submitted revision, independent technical reviewer; return/void requires reason. |
| `RecordAdjustmentManagementDecisionCommand(ManagementJournalDecisionDto, Meta)` | `MutationReceiptDto` | Technical eligibility, actual client-management authority or explicitly permitted offline recorder; exact decision evidence. |
| `ReviseAdjustmentCommand(OriginalJournal: Ref, AdjustmentDraftDto, Meta)` | `MutationReceiptDto` | Original retained; new base/version and reason; no inherited approval/reflection. |
| `ReverseAdjustmentCommand(OriginalJournal: Ref, NewJournalNumber, AccountingDate, EvidenceRefs[], Meta)` | `MutationReceiptDto` | Authorized open/amendment context, balanced reversal lines derived from original; no duplicate reversal; fresh review. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 22: exact input/output DTO fields and supporting request rules](../../modules/22_Adjustments_and_Journals_Contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/01_Command_Query_Ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Immutable technical/management decisions and amendment chains.
- Decision/rework UI and source-safe reversal behavior.

**Direct consumers unlocked by this task:**

- [T023 — Build reflection treatment, sealed plans and adjusted TB snapshots](023_Build_reflection_treatment_sealed_plans_and_adjusted_TB_snapshots.md)

- [Module 22: exact producer/consumer boundaries](../../modules/22_Adjustments_and_Journals_Contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 22: required component tree, state and forms](../../modules/22_Adjustments_and_Journals_Contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Same-person review is rejected even across multiple roles.
- Rejected or partial decisions do not become eligible through status shortcuts.
- Reversal and amendment preserve exact prior evidence and require fresh applicability.

**Source-named test families (proposed unless current source confirms them):**

- Module 22: `AdjustmentEligibilityTests`, `AdjustmentPlanLineageTests`, `AdjustmentJournalEditorTests`, `R2R22AdjustmentJourneys`. [Exact source cases](../../modules/22_Adjustments_and_Journals_Contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

### Original acceptance criteria primarily owned here

| Original ID | Exact wording — not shortened |
|---|---|
| VP-038-AC04 | Unbalanced/mixed-context lines, same-person approval and duplicate inclusion are rejected; amendments preserve prior versions and do not alter source or firm ledgers. |

**Related original stories:** `VP-038`. [Full preserved wording and production interpretation](../../reference/08_Original_52_Acceptance_Criteria.md).

**Related integration journeys:** [R2R-AT-04](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-04), [R2R-AT-05](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-05), [R2R-AT-11](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-11).

### Audit misstatement evaluation and SAD schedule requirements
- Record all identified audit differences with supporting workpapers (`AWP-19-01`).
- Obtain and record management's response to each proposed adjustment (`AWP-19-02`).
- Calculate the impact of unadjusted differences on FS line items (`AWP-19-03`).
- Prepare Summary of Unadjusted Differences (SUD) / Summary of Audit Differences (SAD) (`AWP-19-04`).
- Evaluate aggregate unadjusted differences against materiality (`AWP-19-05`).
- Assess whether remaining differences affect the audit conclusion (`AWP-19-06`).

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-026 | Primary |
| AS-AUD-026-AC01 | Covered |
| AS-AUD-026-AC02 | Covered |
| AS-AUD-026-AC03 | Covered |
| AS-AUD-026-AC04 | Covered |
| AS-AUD-026-AC05 | Covered |
| AS-AUD-026-AC06 | Covered |
| AS-AUD-026-AC12 | Covered |
| AS-AUD-026-AC13 | Covered |
| AWP-19-01 | Covered |
| AWP-19-02 | Covered |
| AWP-19-03 | Covered |
| AWP-19-04 | Covered |
| AWP-19-05 | Covered |
| AWP-19-06 | Covered |

Source procedures (preserved wording):

- `AWP-19-02` Obtain management's response to proposed adjustments.
- `AWP-19-03` Recalculate the financial statement impact of each difference.
- `AWP-19-04` Update the adjusted and unadjusted misstatement schedule.
- `AWP-19-05` Compare total unadjusted differences with materiality.

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
