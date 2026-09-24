---
id: "T057"
work_package: "AUD-17"
modules: []
status: "NOT_STARTED"
depends_on: ["T020", "T056"]
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
# T057 — Reconcile audit lead schedules and source populations

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Traceability ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Generate audit lead schedules bound to accepted R2R TB/GL snapshots, intake client source schedules, verify line-item reconciliation and mathematical tie-outs, report variances, and automatically stale audit schedules when source TB is replaced.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T020](../../00_INDEX.md), [T056](../../00_INDEX.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [Baseline Architecture and ADRs](../../reference/01_Baseline_Architecture_and_ADRs.md)
- [Audit Workflow Traceability Ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)
- [Preserved Audit Workflow Source](../../source/ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md)

## Sequential work

1. Generate audit lead schedules grouped by financial statement line and mapped to accepted R2R TB snapshot (`AcceptedSourceRevisionId`).
2. Build client source schedule intake (debtor listings, creditor listings, asset registers, stock sheets) with column mapping and schema validation.
3. Perform deterministic mathematical reconciliation between source schedule totals and lead schedule balances; surface exceptions.
4. Enforce automatic staleness invalidation on audit lead schedules whenever an upstream R2R TB revision is accepted.

## 1. Domain Modeling (`.Domain`)

Pure calculators: `LeadScheduleCalculator`, `PopulationReconciler`, `VarianceDetector`. Models: `AuditLeadSchedule`, `SourcePopulation`, `ReconciliationProof`.

## 2. Persistence & Migrations (`.Infrastructure`)

Table mappings for audit lead schedules with foreign key to R2R source revisions; indexing by `(EngagementId, FinancialStatementLineId, SourceRevisionId)`.

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `GenerateLeadScheduleCommand`, `IntakeSourcePopulationCommand`, `ReconcilePopulationCommand`, `GetLeadScheduleQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes R2R completeness proofs and source exports (T020) and engagement program (T056); feeds sampling and fieldwork workpapers.

## 5. Blazor UI Architecture (`.Web`)

Lead schedule workbench under `/app/audit/leads` with account group drill-down, intake upload zone, reconciliation difference badge, and stale alert banner.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-004 | Primary |
| AS-AUD-004-AC01 | Covered |
| AS-AUD-004-AC02 | Covered |
| AS-AUD-004-AC03 | Covered |
| AS-AUD-004-AC04 | Covered |
| AS-AUD-004-AC05 | Covered |
| AS-AUD-004-AC06 | Covered |
| AS-AUD-004-AC07 | Covered |
| AS-AUD-004-AC08 | Covered |
| AS-AUD-004-AC09 | Covered |

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
