---
id: "T067"
work_package: "AUD-19"
modules: []
status: "NOT_STARTED"
depends_on: ["T058", "T060", "T066"]
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
# T067 — Implement expenses vouching and cut-off audit workpapers

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Traceability ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Execute Section 8 Expenses procedures: expense listing reconciliation to GL (`AWP-08-01`), analytical comparison against prior period (`AWP-08-02`), substantive vouching of material expenses to supplier invoices/receipts (`AWP-08-03`), classification testing including capital expenditure bridge to fixed assets (`AWP-08-04`), prepayments and accruals recalculation (`AWP-08-05`, `AWP-08-06`), expense cut-off testing (`AWP-08-07`), review of non-deductible items and fines (`AWP-08-08`).

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T058](../../00_INDEX.md), [T060](../../00_INDEX.md), [T066](../../00_INDEX.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [Baseline Architecture and ADRs](../../reference/01_Baseline_Architecture_and_ADRs.md)
- [Audit Workflow Traceability Ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)
- [Preserved Audit Workflow Source](../../source/ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md)

## Sequential work

1. Reconcile expense GL accounts to trial balance and lead schedule (`AWP-08-01`).
2. Perform analytical review across expense categories against prior year and budget, documenting explanations for significant variances (`AWP-08-02`).
3. Execute substantive vouching on expense samples to vendor invoices, contracts, and proof of payment (`AWP-08-03`).
4. Verify expense classification; bridge potential capital expenditure items to Fixed Assets additions (T066) (`AWP-08-04`).
5. Reperform prepayments and expense accruals calculations (`AWP-08-05`, `AWP-08-06`).
6. Perform expense cut-off testing and inspect records for legal fines, penalties, and non-deductible items (`AWP-08-07`, `AWP-08-08`).

## 1. Domain Modeling (`.Domain`)

Models: `ExpensesAuditWorkpaper`, `ExpenseVouchingItem`, `PrepaymentRecalculation`, `AccrualAuditTest`.

## 2. Persistence & Migrations (`.Infrastructure`)

Expense audit tables, vouching records, prepayments/accruals schedules, and capex exception flags.

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `SaveExpensesAuditWorkpaperCommand`, `RecordExpenseVouchingCommand`, `RecalculatePrepaymentsCommand`, `GetExpensesAuditSummaryQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes sampling (T058), planning (T060), and fixed assets (T066); feeds tax adjustments to T071 and differences to T022.

## 5. Blazor UI Architecture (`.Web`)

Expenses audit view under `/app/audit/fieldwork/expenses` with variance analytics, vouching test grid, accrual/prepayment calculators, and capex classification bridge.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-015 | Primary |
| AS-AUD-015-AC01 | Covered |
| AS-AUD-015-AC02 | Covered |
| AS-AUD-015-AC03 | Covered |
| AS-AUD-015-AC04 | Covered |
| AS-AUD-015-AC05 | Covered |
| AS-AUD-015-AC06 | Covered |
| AS-AUD-015-AC07 | Covered |
| AS-AUD-015-AC08 | Covered |
| AS-AUD-015-AC09 | Covered |
| AWP-08-01 | Covered |
| AWP-08-02 | Covered |
| AWP-08-03 | Covered |
| AWP-08-04 | Covered |
| AWP-08-05 | Covered |
| AWP-08-06 | Covered |
| AWP-08-07 | Covered |
| AWP-08-08 | Covered |
| AWP-08-09 | Covered |

Source procedures (preserved wording):

- `AWP-08-01` Obtain detailed expense listing and reconcile to GL/TB.
- `AWP-08-02` Perform analytical review against prior year and budget where available.
- `AWP-08-03` Identify significant and unusual expense movements.
- `AWP-08-04` Select samples based on value and risk.
- `AWP-08-05` Check invoices and supporting documentation.
- `AWP-08-06` Check management approval and payment evidence.
- `AWP-08-07` Verify correct accounting classification.
- `AWP-08-08` Check whether any capital expenditure has been incorrectly expensed.

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
