---
id: "T038"
work_package: "R2R-11"
modules: [20]
status: "NOT_STARTED"
depends_on: ["T037"]
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
# T038 — Implement close readiness and controlled period close

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Original work-package order](../../reference/05_Execution_Coordination_and_Handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Complete the close contracts declared during Module 20 after downstream accounting evidence exists.

**Original work package:** `R2R-11` — M20 close/amendment and entity package→review→release/archive boundary integration
**Original package exit:** End-to-end entity lifecycle, fresh amendment and prior-period opening bridge without historical mutation.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T037 — Implement exact-package decisions, downloads and release readiness](../10_M25_Packages/037_Implement_exact_package_decisions_downloads_and_release_readiness.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/03_Shared_Domain_Persistence_CQRS_Blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/04_Lifecycle_Lineage_and_Module_Ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/06_Contract_Clarifications_and_Resource_Limits.md)
- [20 Accounting Contract](../../modules/20_Accounting_Contract.md)

## Sequential work

1. Collect the exact required package/reconciliation/difference/policy readiness set and its membership revisions.
2. Enter Closing with appropriate source/write fences; cancel only with an attributable reason.
3. Recheck current decisions, pending mutating operations and close manifest atomically before Closed.
4. Build close panel/blocker links and ensure entity close does not depend on completion of group close.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

- [Module 20: exact models, invariants and state transitions](../../modules/20_Accounting_Contract.md#domain)

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

- [Module 20: exact mappings, keys, indexes and migration proposals](../../modules/20_Accounting_Contract.md#persistence)

## 3. Application & CQRS Contracts (`.Application`)

**Owned requests — exact signatures/return types retained from the blueprint:**

| Command / query and input | TResponse | Validator and authoritative handler checks |
|---|---|---|
| `StartPeriodCloseCommand(Context, Meta)` | `MutationReceiptDto` | Freeze new source acceptance while evaluating latest close basis; no outstanding modifying operation. |
| `CancelPeriodCloseCommand(Context, Meta)` | `MutationReceiptDto` | Closing only, mandatory reason; changes close attempt rather than discarding evidence. |
| `CloseReportingPeriodCommand(Context, ExpectedCloseManifest: Ref, DecisionInputDto, Meta)` | `MutationReceiptDto` | Current approved package, resolved required reconciliations/differences and eligible policy; recompute manifest atomically. |
| `GetPeriodCloseReadinessQuery(Context)` | `ReadinessDto` | Named blockers, owner, exact target and safe link; quantities derived from same manifest. |

Every row uses `IRequest<CommandResult<TResponse>>` and a correspondingly named `AbstractValidator<TRequest>`. DTO fields are defined in the linked module contract; common Meta/Context/Ref/result semantics are in the shared contract. Validate asynchronously, then reauthorize and recheck database-dependent invariants inside the single owned transaction.

- [Module 20: exact input/output DTO fields and supporting request rules](../../modules/20_Accounting_Contract.md#cqrs)

Full one-owner registry: [111 command/query contracts](../../coverage/01_Command_Query_Ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Current close manifest and immutable period-close decision.
- Closing/cancel/closed UI and inter-module readiness proof.

**Direct consumers unlocked by this task:**

- [T039 — Implement period amendments and controlled opening roll-forward](039_Implement_period_amendments_and_controlled_opening_roll_forward.md)

- [Module 20: exact producer/consumer boundaries](../../modules/20_Accounting_Contract.md#lineage)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

- [Module 20: required component tree, state and forms](../../modules/20_Accounting_Contract.md#blazor)

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Source acceptance and period close cannot both succeed from conflicting readiness.
- New required adjustment/reconciliation/note cannot slip between proof and close.
- Closed period refuses further accounting writes except an authorized amendment.

**Source-named test families (proposed unless current source confirms them):**

- Module 20: `ReportingContextInvariantTests`, `ReportingContextPersistenceTests`, `AccountingContextEditorTests`, `R2R20AccountingJourneys`. [Exact source cases](../../modules/20_Accounting_Contract.md#verification).

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

**Related original stories:** `VP-034`. [Full preserved wording and production interpretation](../../reference/08_Original_52_Acceptance_Criteria.md).

**Related integration journeys:** [R2R-AT-01](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-01), [R2R-AT-21](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-21), [R2R-AT-26](../../coverage/03_Integration_Journey_Tracking.md#r2r-at-26).

### Audit close readiness and program completion checks
- Enforce clearance of all ReviewPoints and queries across all audit workpapers before close readiness (`AWP-20-03`).
- Enforce complete audit program coverage: verify that no audit procedure remains unexecuted without an approved N/A justification (`AWP-20-04`).

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-027 | Primary |
| AS-AUD-027-AC03 | Covered |
| AS-AUD-027-AC04 | Covered |
| AWP-20-03 | Covered |
| AWP-20-04 | Covered |

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
