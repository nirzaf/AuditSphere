---
id: "T001"
work_package: "R2R-00"
modules: []
status: "NOT_STARTED"
depends_on: []
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
# T001 — Approve scope and inventory the current implementation

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Original work-package order](../../reference/auditsphere-r2r-reference-execution-coordination-and-handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Create the approved, current-code baseline before any feature implementation; this task is not a new repository audit already performed.

**Original work package:** `R2R-00` — Pin current SHA; inventory existing models/services/migrations/UI/tests; reconcile ADRs and scope conflicts
**Original package exit:** Existing/new symbol ledger, policy approvals, preserved baseline tests and dependency licence decisions.

## Before starting

This is the first task. It may perform read-only inventory and obtain approval. Do not start implementation before the owner approves the blueprint/scope.

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/auditsphere-r2r-reference-shared-domain-persistence-cqrs-blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/auditsphere-r2r-reference-lifecycle-lineage-and-module-ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/auditsphere-r2r-reference-contract-clarifications-and-resource-limits.md)
- [01 Baseline Architecture and ADRs](../../reference/auditsphere-r2r-reference-baseline-architecture-and-adrs.md)
- [02 Standards and Source Register](../../reference/auditsphere-r2r-reference-standards-and-source-register.md)

## Sequential work

1. Obtain the owner decision for STE-R2R-ARCH-001 and preserve its seven-module scope and exclusions.
2. Inspect the current checkout, AGENTS.md, specifications, migrations, services, Razor routes and tests. Record the actual SHA; the attached baseline is historical.
3. Build an EXISTING / EXTEND / NEW / DECISION ledger. Preserve firm books, client reporting and group consolidation as distinct responsibilities.
4. Identify every conflict with the older Purview/signature requirements; record a scoped decision instead of disabling release checks.

## 1. Domain Modeling (`.Domain`)

Use the source module’s aggregate/entity/value-object identities and lifecycle exactly where applicable. Classify each symbol as EXISTING, EXTEND, NEW or DECISION before editing. Preserve raw records, reviewed revisions and source-specific eligibility; do not introduce duplicate balances or reinterpret legacy state strings silently.

This foundation/coordination task changes only the shared contracts expressly described in its work steps. It does not independently create a new business aggregate.

## 2. Persistence & Migrations (`.Infrastructure`)

Implement only the persistence delta needed by this task. Locate existing mappings and transaction owners first. Preserve composite scope FKs, restrictive deletes, immutable history and revision concurrency; initial mapping extraction must show no model diff. Any new schema change uses the source’s proposed migration suffix convention with a real generated timestamp, prior-schema tests and a reviewed backfill/quarantine plan.

## 3. Application & CQRS Contracts (`.Application`)

No new public command/query is assigned to this task. Configure, verify or connect the contracts of the owning tasks. Any additional request name requires a recorded contract decision rather than an agent-generated assumption.

Full one-owner registry: [111 command/query contracts](../../coverage/auditsphere-r2r-tracker-command-query-ownership.md).

## 4. Inter-Module Lineage & Boundaries

Use predecessor outputs by exact identity/revision/manifest, not by selecting a convenient latest row. Data handoff is through the owning application contracts, not copied mutable module state. New required set members must invalidate dependent current applicability; retain the historical decision and result.

**Required outputs:**

- Approved scope reference and conflict register.
- Current SHA, preserved baseline result references and reuse/new-symbol ledger.

**Direct consumers unlocked by this task:**

- [T002 — Approve architecture, contracts and transaction ownership](auditsphere-r2r-task-t002-architecture-contracts-transaction-ownership.md)

## 5. Blazor UI Architecture (`.Web`)

Extend the source-listed existing routes before adding a parallel workspace. Use immutable DTOs in per-circuit scoped UI state, separate EditContexts, EventCallbacks, server field errors and save/discard/cancel dirty guards. No circuit-owned DbContext, cross-user static state, optimistic approval or fake completion. Recheck selection tokens and clear protected content on route/scope loss.

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- No proposed symbol is represented as an existing implementation without a source locator.
- No existing feature is rebuilt solely because this task starts NOT_STARTED.
- Owner approval is recorded before this task unlocks implementation.

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, bUnit for component behavior and Playwright for actual Blazor journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

### Audit workflow integration scope
- Reconcile the 20-section, 165-procedure audit workflow backlog (`auditsphere-audit-source-workflow-gap-closure-user-stories-historical.md`) against current code.
- Enforce strict separation between client accounting (R2R) and external audit: audit consumes exact R2R source revisions but never creates duplicate chart, period, or ledger masters.
- Exclude client operational ERP, payroll execution, and statutory tax filing from AuditSphere scope.
- Enforce human professional judgement boundaries: the platform evaluates formulas and enforces workflow gates, but never issues autonomous audit opinions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-001 | Primary |
| AS-AUD-001-AC01 | Covered |
| AS-AUD-001-AC02 | Covered |
| AS-AUD-001-AC03 | Covered |
| AS-AUD-001-AC04 | Covered (Reference) |
| AS-AUD-001-AC05 | Covered |
| AS-AUD-001-AC06 | Covered (Blocked external) |
| AS-AUD-001-AC07 | Covered |

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
