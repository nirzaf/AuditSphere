---
id: "T004"
work_package: "R2R-01"
modules: []
status: "NOT_STARTED"
depends_on: ["T003"]
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
# T004 — Make command and query boundaries explicit as static services without changing transaction behavior

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Original work-package order](../../reference/auditsphere-r2r-reference-execution-coordination-and-handover.md#section-6-1)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Document and verify the existing static service boundaries for one representative command and query, preserving one transaction owner and the current wire behavior.

**Original work package:** `R2R-01` — static command and query services, async validation, explicit DTO mapping, one transaction-owner registry, Angular component specs
**Original package exit:** One representative existing command/query migrated without behavior change or nested transaction.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T003 — Approve accounting policies, resource bounds and golden fixtures](../00_Baseline/auditsphere-r2r-task-t003-accounting-policies-resource-bounds-golden-fixtures.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/auditsphere-r2r-reference-shared-domain-persistence-cqrs-blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/auditsphere-r2r-reference-lifecycle-lineage-and-module-ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/auditsphere-r2r-reference-contract-clarifications-and-resource-limits.md)
- [01 Baseline Architecture and ADRs](../../reference/auditsphere-r2r-reference-baseline-architecture-and-adrs.md)

## Sequential work

1. Choose one existing accounting command and query and record their owning static capability service and API boundary.
2. Trace the write path and document its single transaction owner; preserve the existing guarded service and do not add a facade or dependency.
3. Keep operation identity, current authority, mutation, invalidation and evidence in the same authoritative commit.
4. Add an architecture check for the selected transaction-owner invariant and keep domain projects independent of EF/UI/provider types (ADR-0001).

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

- A transaction-owner registry and a verified representative service/API boundary.
- Before/after behavior evidence for the pilot.

**Direct consumers unlocked by this task:**

- [T005 — Implement asynchronous validation and explicit DTO mapping](auditsphere-r2r-task-t005-asynchronous-validation-dto-mapping.md)

## 5. Presentation boundary

No presentation rewrite is part of this task. Preserve the current API contract and Angular UI; the legacy Blazor host remains rollback/reference per ADR-0002.

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- Existing business outcomes and wire error codes are preserved.
- No nested transaction is created.
- A timeout/retry does not write idempotency evidence outside the business transaction.

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, Angular component specs for component behavior and Playwright for actual Angular browser journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

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
