---
id: "T003"
work_package: "R2R-00"
modules: []
status: "BLOCKED"
depends_on: ["T002"]
owner: "Codex implementation coordinator"
reviewer: ""
review_decision: ""
reviewed_commit: ""
evidence_ref: ""
approval_ref: ""
blocked_reason: "The core numeric convention is recorded in ADR-0014. Qualified methodology-owner and independent-review signoff is still required for FX sources/effective periods, framework editions, consolidation methods and all eight GOLD-R2R fixtures. That evidence is absent; no amounts or accounting treatments will be inferred."
branch: ""
issue_pr: ""
updated_at: "2026-10-10T13:14:02+00:00"
---
# T003 — Approve accounting policies, resource bounds and golden fixtures

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Original work-package order](../../reference/auditsphere-r2r-reference-execution-coordination-and-handover.md#section-6-1)

**Current assessment (2026-10-10): BLOCKED.** T002's architecture variation is accepted and recorded separately. T003 still needs a named, qualified methodology owner and independent reviewer to approve the professional accounting policies and exact GOLD-R2R fixture outcomes. No sign-off or fixture approval is present. Do not infer accounting treatment or numerical expected values from code or synthetic tests.

## Outcome

Obtain the professional and numerical policy decisions needed by all downstream tasks.

**Open decision register:** R2R-specific standards, effective-period and consolidation-profile approvals and all eight GOLD-R2R fixture approvals need qualified ownership and independent review. ADR-0014 already closes six-decimal/ToEven arithmetic; ADR-0015 closes reporting-context identity, not its implementation. Retain the existing enforced NFR request bounds as the delegated engineering default. A new versioned resource policy and production capacity acceptance remain technical/operations evidence work; mapping existing constants does not require an accountant to reapprove them. T004 inventory and transaction analysis still require the completed, reviewed T003 handoff. No ADR or engineering delegation supplies professional sign-off or independent fixture review.

**Original work package:** `R2R-00` — Pin current SHA; inventory existing models/services/migrations/UI/tests; reconcile ADRs and scope conflicts
**Original package exit:** Existing/new symbol ledger, policy approvals, preserved baseline tests and dependency licence decisions.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- [T002 — Approve architecture, contracts and transaction ownership](auditsphere-r2r-task-t002-architecture-contracts-transaction-ownership.md)

**Shared file ownership:** the coordinator serializes migrations, DbContext snapshots, public DTOs and shared policy edits. A task owns only the requests listed below; consume other requests through their owner.

## Required reading

- [00 Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [03 Shared Domain Persistence CQRS Blazor](../../reference/auditsphere-r2r-reference-shared-domain-persistence-cqrs-blazor.md)
- [04 Lifecycle Lineage and Module Ports](../../reference/auditsphere-r2r-reference-lifecycle-lineage-and-module-ports.md)
- [06 Contract Clarifications and Resource Limits](../../reference/auditsphere-r2r-reference-contract-clarifications-and-resource-limits.md)
- [01 Baseline Architecture and ADRs](../../reference/auditsphere-r2r-reference-baseline-architecture-and-adrs.md)
- [02 Standards and Source Register](../../reference/auditsphere-r2r-reference-standards-and-source-register.md)

## Sequential work

1. Reuse ADR-0014 for R2R-ADR-03's technical six-decimal/ToEven convention. A qualified methodology owner and independent reviewer must still approve FX sources/effective periods, framework editions and supported methods, and R2R-ADR-09 consolidation profiles; do not record these as approved without that evidence.
2. Review all eight GOLD-R2R fixtures independently; retain their identities and exact expected values separately from existing approved fixtures.
3. Map section 10.1's proposed resource limits against existing enforced NFR constants; retain the stricter existing behavior without changing limits in this documentation slice. Record unimplemented bounds as code/evidence gaps. Any changed capacity target needs operations evidence; do not present it as an agreed professional or production policy.
4. Separate source-derived standards research from current professional approval. Do not infer tax balances, new methods, market FX rates or a universal compliance claim.

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

- Versioned policy approvals and approved/blocked fixture register.
- Resource-policy decision and supported-method boundaries.

**Direct consumers unlocked by this task:**

- [T004 — Make command and query boundaries explicit as static services without changing transaction behavior](../01_CQRS/auditsphere-r2r-task-t004-command-query-boundary.md)

## 5. Current presentation boundary

Use the existing Angular/API route and typed DTOs; any future policy editor must retain independent review, server field errors, explicit dirty-state handling and scope/session invalidation. No browser-owned DbContext, cross-user state, optimistic professional approval or fake completion. The original Blazor architecture remains historical reference only under ADR-0002.

## 6. Edge Cases, Security & Verification

**Required verification checks — not executed in this task pack:**

- No test obtains its expected financial answer by calling the implementation under test.
- Six-decimal storage and ToEven rounding are retained unless an approved change explicitly replaces them.
- Missing policies or rates stop the affected feature; they are never defaulted to convenient values.

Use pure xUnit for deterministic rules, real PostgreSQL for persistence/concurrency, Angular component specs for component behavior and Playwright for actual Angular browser journeys where applicable. Record the subset executed for this task. Deferred consumer tests stay explicitly unverified until their scheduled integration gate; a task completion is not automatic whole-module acceptance.

**Golden fixtures:** [GOLD-R2R-01](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-01), [GOLD-R2R-02](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-02), [GOLD-R2R-03](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-03), [GOLD-R2R-04](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-04), [GOLD-R2R-05](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-05), [GOLD-R2R-06](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-06), [GOLD-R2R-07](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-07), [GOLD-R2R-08](../../coverage/auditsphere-r2r-tracker-golden-fixtures.md#gold-r2r-08). Fixture use requires the original policy approval; the numbers are synthetic QA values.

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
