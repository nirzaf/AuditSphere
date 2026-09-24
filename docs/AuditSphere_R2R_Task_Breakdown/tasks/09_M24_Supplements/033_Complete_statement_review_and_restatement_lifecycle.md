---
id: "T033"
work_package: "R2R-09"
modules: [24]
status: "NOT_STARTED"
depends_on: ["T030", "T031", "T032"]
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
# T033 — Complete statement review and restatement lifecycle

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Original work-package order](../../reference/05_Execution_Coordination_and_Handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Accept a complete supported statement set and distinguish restatements from estimate changes.

**Original work package:** `R2R-09` — M24 cash/equity schedules, notes, policy-edition rules, restatements and review
**Original package exit:** Complete supported statement set including required disclosures; subsequent-event/treatment decisions recorded.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T030 — Build source-backed cash-flow schedules](030_Build_source_backed_cash_flow_schedules.md)
- [T031 — Build equity-movement schedules](031_Build_equity_movement_schedules.md)
- [T032 — Build disclosure notes and reviewed applicability](032_Build_disclosure_notes_and_reviewed_applicability.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/03_Shared_Domain_Persistence_CQRS_Blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/04_Lifecycle_Lineage_and_Module_Ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/06_Contract_Clarifications_and_Resource_Limits.md)
- [02 Standards and Source Register](../../reference/02_Standards_and_Source_Register.md)
- [24 Financial Statements Contract](../../modules/24_Financial_Statements_Contract.md)

## Sequential work

1. Review exact cash/equity schedule revisions through the shared ScheduleKind contract and require current required-note decisions.
2. Create reasoned restatement cases linked to original and revised outputs and professional treatment evidence.
3. Recheck all current inputs before independent statement/restatement approval; preserve as-issued artifacts and decisions.
4. Build review/restatement comparison and record subsequent-event/authorization decisions where required by the approved profile.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 24: exact models, invariants and state transitions](../../modules/24_Financial_Statements_Contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 24: exact mappings, keys, indexes and migration proposals](../../modules/24_Financial_Statements_Contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `ReviewSupplementaryScheduleCommand(ScheduleKind, ScheduleRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Allowlisted kind; exact current schedule, independent reviewer and documented reconciliation. |
| `ReviewStatementSetCommand(StatementSetRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Every blocking validation passes, manifest remains current, appropriate independent reviewer. |
| `CreateRestatementCaseCommand(RestatementDraftDto, Meta)` | `MutationReceiptDto` | Original preserved; supported change classification and period impact; estimate change not silently retrospective. |
| `ApproveRestatementCaseCommand(CaseId, RevisedStatementSetRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Distinct reviewer, exact original/revised manifests, balanced impacts and complete required disclosure. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 24: exact input/output DTO fields and supporting request rules](../../modules/24_Financial_Statements_Contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/01_Command_Query_Ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Reviewed complete statement-set reference for Module 25.
- Restatement approval/history and final Module 24 acceptance evidence.

**Direct consumers unlocked by this task:**

- [T034 — Persist financial-package composition and revisions](../10_M25_Packages/034_Persist_financial_package_composition_and_revisions.md)

- [Module 24: exact producer/consumer boundaries](../../modules/24_Financial_Statements_Contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 24: required component tree, state and forms](../../modules/24_Financial_Statements_Contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Errors, policy transitions and estimate changes follow approved distinct period treatment.
- Changed support or comparative inputs require fresh review.
- No automated professional conclusion or claimed universal framework coverage.

**Source-named test families (proposed unless current source confirms them):**

- Module 24: `StatementLayoutAndPolicyTests`, `CashEquityComparativeTests`, `StatementRevisionLineageTests`, `StatementDesignerTests`, `DisclosureEditorTests`, `R2R24StatementJourneys`. [Exact source cases](../../modules/24_Financial_Statements_Contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

### Original acceptance criteria primarily owned here

| Original ID | Exact wording — not shortened |
|---|---|
| VP-040-AC03 | Changing source, mapping, layout or comparative selection creates a new output revision and stales the previous current review. |
| VP-041-AC03 | Approved note/support edits preserve prior revision and invalidate current package review; totals tie to current statement context. |

**Related original stories:** `VP-040`, `VP-041`. [Full preserved wording and production interpretation](../../reference/08_Original_52_Acceptance_Criteria.md).

**Related integration journeys:** [R2R-AT-14](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-14), [R2R-AT-15](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-15), [R2R-AT-16](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-16), [R2R-AT-17](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-17).

**Golden fixtures:** [GOLD-R2R-01](../../coverage/04_Golden_Fixture_Tracking.md#gold-r2r-01), [GOLD-R2R-04](../../coverage/04_Golden_Fixture_Tracking.md#gold-r2r-04), [GOLD-R2R-05](../../coverage/04_Golden_Fixture_Tracking.md#gold-r2r-05). Fixture use requires the original policy approval; the numbers are synthetic QA values.

### Audit financial statements tie-out and cross-casting requirements
- Agree all financial statement line items to the final adjusted trial balance (`AWP-18-01`).
- Perform mathematical accuracy and cross-casting verification across all statements, notes, and schedules (`AWP-18-02`).
- Verify comparative figures agree with prior-year audited financial statements (`AWP-18-03`).
- Review accounting policies for consistency with the prior period and compliance with framework standards (`AWP-18-05`).
- Verify that the statement of cash flows is consistent with balance sheet and profit-and-loss movements (`AWP-18-07`).

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-025 | Primary |
| AS-AUD-025-AC01 | Covered |
| AS-AUD-025-AC02 | Covered |
| AS-AUD-025-AC03 | Covered |
| AS-AUD-025-AC04 | Covered |
| AS-AUD-025-AC07 | Covered |
| AWP-18-01 | Covered |
| AWP-18-02 | Covered |
| AWP-18-03 | Covered |
| AWP-18-05 | Covered |
| AWP-18-07 | Covered |

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
