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

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Original work-package order](../../reference/auditsphere-r2r-reference-execution-coordination-and-handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Accept a complete supported statement set and distinguish restatements from estimate changes.

**Original work package:** `R2R-09` — M24 cash/equity schedules, notes, policy-edition rules, restatements and review
**Original package exit:** Complete supported statement set including required disclosures; subsequent-event/treatment decisions recorded.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T030 — Build source-backed cash-flow schedules](auditsphere-r2r-task-t030-source-backed-cash-flow-schedules.md)
- [T031 — Build equity-movement schedules](auditsphere-r2r-task-t031-equity-movement-schedules.md)
- [T032 — Build disclosure notes and reviewed applicability](auditsphere-r2r-task-t032-disclosure-notes-reviewed-applicability.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/auditsphere-r2r-reference-shared-domain-persistence-cqrs-blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/auditsphere-r2r-reference-lifecycle-lineage-and-module-ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/auditsphere-r2r-reference-contract-clarifications-and-resource-limits.md)
- [02 Standards and Source Register](../../reference/auditsphere-r2r-reference-standards-and-source-register.md)
- [24 Financial Statements Contract](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md)

## Sequential work

1. Review exact cash/equity schedule revisions through the shared ScheduleKind contract and require current required-note decisions.
2. Create reasoned restatement cases linked to original and revised outputs and professional treatment evidence.
3. Recheck all current inputs before independent statement/restatement approval; preserve as-issued artifacts and decisions.
4. Build review/restatement comparison and record subsequent-event/authorization decisions where required by the approved profile.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 24: exact models, invariants and state transitions](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 24: exact mappings, keys, indexes and migration proposals](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `ReviewSupplementaryScheduleCommand(ScheduleKind, ScheduleRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Allowlisted kind; exact current schedule, independent reviewer and documented reconciliation. |
| `ReviewStatementSetCommand(StatementSetRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Every blocking validation passes, manifest remains current, appropriate independent reviewer. |
| `CreateRestatementCaseCommand(RestatementDraftDto, Meta)` | `MutationReceiptDto` | Original preserved; supported change classification and period impact; estimate change not silently retrospective. |
| `ApproveRestatementCaseCommand(CaseId, RevisedStatementSetRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Distinct reviewer, exact original/revised manifests, balanced impacts and complete required disclosure. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 24: exact input/output DTO fields and supporting request rules](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/auditsphere-r2r-tracker-command-query-ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Reviewed complete statement-set reference for Module 25.
- Restatement approval/history and final Module 24 acceptance evidence.

**Direct consumers unlocked by this task:**

- [T034 — Persist financial-package composition and revisions](../10_M25_Packages/auditsphere-r2r-task-t034-financial-package-composition-revisions.md)

- [Module 24: exact producer/consumer boundaries](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 24: required component tree, state and forms](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Errors, policy transitions and estimate changes follow approved distinct period treatment.
- Changed support or comparative inputs require fresh review.
- No automated professional conclusion or claimed universal framework coverage.

**Source-named test families (proposed unless current source confirms them):**

- Module 24: `StatementLayoutAndPolicyTests`, `CashEquityComparativeTests`, `StatementRevisionLineageTests`, `StatementDesignerTests`, `DisclosureEditorTests`, `R2R24StatementJourneys`. [Exact source cases](../../modules/auditsphere-r2r-module-24-financial-statements-contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

### Original acceptance criteria primarily owned here

| Original ID | Exact wording — not shortened |
|---|---|
| VP-040-AC03 | Changing source, mapping, layout or comparative selection creates a new output revision and stales the previous current review. |
| VP-041-AC03 | Approved note/support edits preserve prior revision and invalidate current package review; totals tie to current statement context. |

**Related original stories:** `VP-040`, `VP-041`. [Full preserved wording and production interpretation](../../reference/auditsphere-r2r-reference-original-52-acceptance-criteria.md).

**Related integration journeys:** [R2R-AT-14](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-14), [R2R-AT-15](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-15), [R2R-AT-16](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-16), [R2R-AT-17](../../coverage/auditsphere-r2r-tracker-integration-journeys.md#r2r-at-17).

**Golden fixtures:** [GOLD-R2R-01](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-01), [GOLD-R2R-04](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-04), [GOLD-R2R-05](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-05). Fixture use requires the original policy approval; the numbers are synthetic QA values.

### Audit financial statements tie-out and cross-casting requirements
- Agree all financial statement line items to the audited Trial Balance (`AWP-18-01`).
- Check statement of financial position and profit/loss (`AWP-18-02`).
- Check cash flow statement and statement of changes in equity (`AWP-18-03`).
- Check comparative figures with prior-year audited FS and restatements (`AWP-18-05`).
- Check related-party, tax and going-concern disclosures (`AWP-18-07`).

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-025 | Primary |
| AS-AUD-025-AC01 | Covered |
| AS-AUD-025-AC02 | Covered |
| AS-AUD-025-AC03 | Covered |
| AS-AUD-025-AC05 | Covered |
| AS-AUD-025-AC07 | Covered |
| AWP-18-01 | Covered |
| AWP-18-02 | Covered |
| AWP-18-03 | Covered |
| AWP-18-05 | Covered |
| AWP-18-07 | Covered |

Source procedures (preserved wording):

- `AWP-18-01` Agree final financial statements to the audited Trial Balance.

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
