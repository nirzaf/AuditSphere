---
id: "T060"
work_package: "AUD-18"
modules: []
status: "NOT_STARTED"
depends_on: ["T056", "T057"]
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
# T060 — Implement audit planning, materiality and risk assessment

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Traceability ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Document Section 1 planning procedures: compute Overall Materiality (OM), Performance Materiality (PM), and Clearly Trivial Threshold (CTT); verify opening balance agreement (`AWP-01-04`); manage risk register and risk-to-procedure linkages (`AWP-01-07`, `AWP-01-08`); record engagement letter and independence declarations (`AWP-01-09`).

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T056](../../00_INDEX.md), [T057](../../00_INDEX.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [Baseline Architecture and ADRs](../../reference/01_Baseline_Architecture_and_ADRs.md)
- [Audit Workflow Traceability Ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)
- [Preserved Audit Workflow Source](../../source/ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md)

## Sequential work

1. Implement pure materiality calculator: benchmark selection (Revenue, PBT, Total Assets, Equity), percentage rules, OM, PM, and CTT.
2. Build opening balance audit workpaper agreeing prior-period audited figures with opening TB (`AWP-01-04`).
3. Create engagement risk register capturing financial-statement level and assertion-level risks (`AWP-01-07`).
4. Link identified risks to tailored audit procedures and planned substantive testing (`AWP-01-08`).
5. Record engagement acceptance, letter of engagement, and team independence confirmations (`AWP-01-09`).

## 1. Domain Modeling (`.Domain`)

Pure calculators: `MaterialityCalculator`. Models: `EngagementPlanningRecord`, `MaterialityAssessment`, `AuditRiskItem`, `OpeningBalanceVerification`.

## 2. Persistence & Migrations (`.Infrastructure`)

Planning persistence schema with partner sign-off, risk-to-procedure relationship tables, and benchmark audit trails.

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `SaveMaterialityAssessmentCommand`, `RecordOpeningBalanceVerificationCommand`, `UpsertAuditRiskItemCommand`, `GetAuditPlanningSummaryQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes engagement program (T056) and lead schedules (T057); provides materiality and risk thresholds for all audit fieldwork tasks.

## 5. Blazor UI Architecture (`.Web`)

Audit planning workbench under `/app/audit/planning` with interactive materiality calculator, opening balance tie-out grid, and risk assessment matrix.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-007 | Primary |
| AS-AUD-007-AC01 | Covered |
| AS-AUD-007-AC02 | Covered |
| AS-AUD-007-AC03 | Covered |
| AS-AUD-007-AC04 | Covered |
| AS-AUD-007-AC05 | Covered |
| AS-AUD-007-AC06 | Covered |
| AS-AUD-007-AC07 | Covered |
| AS-AUD-007-AC08 | Covered |
| AS-AUD-007-AC09 | Covered |
| AS-AUD-007-AC10 | Covered |
| AWP-01-01 | Covered |
| AWP-01-02 | Covered |
| AWP-01-03 | Covered |
| AWP-01-04 | Covered |
| AWP-01-05 | Covered |
| AWP-01-06 | Covered |
| AWP-01-07 | Covered |
| AWP-01-08 | Covered |
| AWP-01-09 | Covered |

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
