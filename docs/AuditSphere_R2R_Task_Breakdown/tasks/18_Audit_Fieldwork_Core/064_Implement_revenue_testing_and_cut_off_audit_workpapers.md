---
id: "T064"
work_package: "AUD-18"
modules: []
status: "NOT_STARTED"
depends_on: ["T058", "T060"]
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
# T064 — Implement revenue testing and cut-off audit workpapers

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Traceability ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Execute Section 5 Revenue procedures: revenue population reconciliation to GL (`AWP-05-01`), 3-way matching sample testing (sales order, dispatch note, sales invoice, price approval) (`AWP-05-02`), sales cut-off verification around year-end (`AWP-05-03`), review of post-year-end credit notes and cancellations (`AWP-05-04`), contract revenue and performance obligation review (`AWP-05-05`).

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T058](../../00_INDEX.md), [T060](../../00_INDEX.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [Baseline Architecture and ADRs](../../reference/01_Baseline_Architecture_and_ADRs.md)
- [Audit Workflow Traceability Ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)
- [Preserved Audit Workflow Source](../../source/ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md)

## Sequential work

1. Reconcile revenue transaction listing to general ledger and trial balance (`AWP-05-01`).
2. Execute substantive testing on selected sales samples: verify sales orders, dispatch notes, approved price lists, and sales invoices (3-way match) (`AWP-05-02`).
3. Perform sales cut-off testing on dispatches and billings immediately before and after year-end (`AWP-05-03`).
4. Inspect credit notes issued after period end for reversals of year-end sales (`AWP-05-04`).
5. Review customer contracts for IFRS 15 / framework performance obligations and deferred revenue recognition (`AWP-05-05`).

## 1. Domain Modeling (`.Domain`)

Models: `RevenueAuditWorkpaper`, `RevenueSampleTest`, `SalesCutOffTest`, `CreditNoteReviewRecord`.

## 2. Persistence & Migrations (`.Infrastructure`)

Revenue audit records, 3-way match verification flags, cut-off date registers, and exception linkages.

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `SaveRevenueAuditWorkpaperCommand`, `RecordRevenueSampleTestCommand`, `RecordSalesCutOffTestCommand`, `GetRevenueAuditSummaryQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes sampling engine (T058) and materiality (T060); feeds revenue misstatements to T022.

## 5. Blazor UI Architecture (`.Web`)

Revenue audit view under `/app/audit/fieldwork/revenue` with revenue streams breakdown, 3-way match verification grid, cut-off analyzer, and credit note inspection log.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-012 | Primary |
| AS-AUD-012-AC01 | Covered |
| AS-AUD-012-AC02 | Covered |
| AS-AUD-012-AC03 | Covered |
| AS-AUD-012-AC04 | Covered |
| AS-AUD-012-AC05 | Covered |
| AS-AUD-012-AC06 | Covered |
| AS-AUD-012-AC07 | Covered |
| AS-AUD-012-AC08 | Covered |
| AWP-05-01 | Covered |
| AWP-05-02 | Covered |
| AWP-05-03 | Covered |
| AWP-05-04 | Covered |
| AWP-05-05 | Covered |
| AWP-05-06 | Covered |
| AWP-05-07 | Covered |
| AWP-05-08 | Covered |

Source procedures (preserved wording):

- `AWP-05-01` Obtain sales listing and reconcile total revenue to GL/TB.
- `AWP-05-02` Perform analytical review of monthly and annual sales.
- `AWP-05-03` Select sales samples based on value and risk.
- `AWP-05-04` Check invoices to customer orders/delivery documents.
- `AWP-05-05` Verify quantity, price, calculation and accounting entry.
- `AWP-05-06` Check selected subsequent receipts where relevant.
- `AWP-05-07` Review significant credit notes after year-end.
- `AWP-05-08` Perform revenue cut-off testing before and after year-end.

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
