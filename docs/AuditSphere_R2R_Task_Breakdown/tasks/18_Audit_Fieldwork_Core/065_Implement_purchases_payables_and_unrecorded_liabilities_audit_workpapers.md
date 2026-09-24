---
id: "T065"
work_package: "AUD-18"
modules: []
status: "NOT_STARTED"
depends_on: ["T058", "T059", "T060"]
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
# T065 — Implement purchases, payables and unrecorded liabilities audit workpapers

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Traceability ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Execute Section 6 Purchases & Payables procedures: aged creditors listing reconciliation to GL (`AWP-06-01`), supplier statement reconciliations (`AWP-06-04`), search for unrecorded liabilities across post-year-end payments, unentered invoices and unmatched GRNs (`AWP-06-05`), purchases cut-off testing (`AWP-06-06`), debit balances review (`AWP-06-07`).

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T058](../../00_INDEX.md), [T059](../../00_INDEX.md), [T060](../../00_INDEX.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [Baseline Architecture and ADRs](../../reference/01_Baseline_Architecture_and_ADRs.md)
- [Audit Workflow Traceability Ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)
- [Preserved Audit Workflow Source](../../source/ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md)

## Sequential work

1. Reconcile aged payables subledger to general ledger/TB (`AWP-06-01`, `AWP-06-02`).
2. Perform supplier statement reconciliations for material suppliers and investigate reconciling items (`AWP-06-04`).
3. Execute rigorous search for unrecorded liabilities: inspect subsequent bank payments, post-year-end invoices, and unmatched Goods Received Notes (GRN) (`AWP-06-05`).
4. Perform purchases and inventory cut-off testing on goods received and invoices entered around year-end (`AWP-06-06`).
5. Review debit balances in creditors ledger and evaluate recoverability or reclassification to assets (`AWP-06-07`).

## 1. Domain Modeling (`.Domain`)

Models: `PayablesAuditWorkpaper`, `SupplierStatementReconciliation`, `UnrecordedLiabilitySearchItem`, `PurchasesCutOffTest`.

## 2. Persistence & Migrations (`.Infrastructure`)

Payables audit tables, supplier statement proof records, unrecorded liability register, and invoice match logs.

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `SavePayablesAuditWorkpaperCommand`, `ReconcileSupplierStatementCommand`, `RecordUnrecordedLiabilitySearchCommand`, `GetPayablesAuditSummaryQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes sampling (T058), confirmations (T059), and materiality (T060); feeds unrecorded liabilities to T022.

## 5. Blazor UI Architecture (`.Web`)

Purchases & Payables audit view under `/app/audit/fieldwork/payables` with supplier ageing summary, statement reconciliation tool, unrecorded liabilities search grid, and cut-off sheet.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-013 | Primary |
| AS-AUD-013-AC01 | Covered |
| AS-AUD-013-AC02 | Covered |
| AS-AUD-013-AC03 | Covered |
| AS-AUD-013-AC04 | Covered |
| AS-AUD-013-AC05 | Covered |
| AS-AUD-013-AC06 | Covered |
| AS-AUD-013-AC07 | Covered |
| AS-AUD-013-AC08 | Covered |
| AS-AUD-013-AC09 | Covered |
| AWP-06-01 | Covered |
| AWP-06-02 | Covered |
| AWP-06-03 | Covered |
| AWP-06-04 | Covered |
| AWP-06-05 | Covered |
| AWP-06-06 | Covered |
| AWP-06-07 | Covered |
| AWP-06-08 | Covered |

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
