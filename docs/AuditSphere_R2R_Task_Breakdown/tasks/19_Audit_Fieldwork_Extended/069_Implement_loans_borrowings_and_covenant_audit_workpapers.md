---
id: "T069"
work_package: "AUD-19"
modules: []
status: "NOT_STARTED"
depends_on: ["T059", "T060"]
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
# T069 — Implement loans, borrowings and covenant audit workpapers

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Traceability ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Execute Section 10 Loans & Borrowings procedures: loan agreements inspection and confirmation with lenders (`AWP-10-01`, `AWP-10-02`), loan schedule roll-forward (`AWP-10-03`), interest expense and accrued interest reperformance (`AWP-10-04`), debt covenant compliance testing (`AWP-10-05`), security and pledged asset disclosure review (`AWP-10-06`), current vs non-current debt classification verification (`AWP-10-07`).

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T059](../../00_INDEX.md), [T060](../../00_INDEX.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [Baseline Architecture and ADRs](../../reference/01_Baseline_Architecture_and_ADRs.md)
- [Audit Workflow Traceability Ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)
- [Preserved Audit Workflow Source](../../source/ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md)

## Sequential work

1. Obtain and inspect loan agreements, facility letters, and repayment terms (`AWP-10-01`).
2. Reconcile confirmed balances from lenders to loan ledgers and TB (`AWP-10-02`).
3. Build loan roll-forward schedule: opening balance, new drawdowns, repayments, closing balance (`AWP-10-03`).
4. Reperform interest expense, amortised cost (effective interest rate), and accrued interest calculations (`AWP-10-04`).
5. Perform debt covenant compliance testing across all financial covenants; flag covenant breaches (`AWP-10-05`).
6. Review pledged assets/mortgages and verify current vs non-current liability classification (`AWP-10-06`, `AWP-10-07`).

## 1. Domain Modeling (`.Domain`)

Pure calculators: `LoanInterestCalculator`, `CovenantComplianceCalculator`. Models: `LoanAuditWorkpaper`, `LoanFacilityRecord`, `CovenantAuditTest`.

## 2. Persistence & Migrations (`.Infrastructure`)

Loan audit tables, covenant calculation records, and security register mappings.

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `SaveLoanAuditWorkpaperCommand`, `ReperformLoanInterestCommand`, `TestDebtCovenantsCommand`, `GetLoanAuditSummaryQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes confirmations (T059) and planning (T060); feeds covenant breach flags to Going Concern (T074).

## 5. Blazor UI Architecture (`.Web`)

Loans & Borrowings view under `/app/audit/fieldwork/loans` with debt facility register, roll-forward schedule, covenant compliance matrix, and current/non-current split analyzer.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-017 | Primary |
| AS-AUD-017-AC01 | Covered |
| AS-AUD-017-AC02 | Covered |
| AS-AUD-017-AC03 | Covered |
| AS-AUD-017-AC04 | Covered |
| AS-AUD-017-AC05 | Covered |
| AS-AUD-017-AC06 | Covered |
| AS-AUD-017-AC07 | Covered |
| AS-AUD-017-AC08 | Covered |
| AS-AUD-017-AC09 | Covered |
| AWP-10-01 | Covered |
| AWP-10-02 | Covered |
| AWP-10-03 | Covered |
| AWP-10-04 | Covered |
| AWP-10-05 | Covered |
| AWP-10-06 | Covered |
| AWP-10-07 | Covered |
| AWP-10-08 | Covered |
| AWP-10-09 | Covered |

Source procedures (preserved wording):

- `AWP-10-01` Obtain loan/borrowing schedule and agree it to GL.
- `AWP-10-02` Agree opening balances to prior-year financial statements.
- `AWP-10-03` Obtain bank/financier confirmation.
- `AWP-10-04` Check new loans against agreements and bank receipts.
- `AWP-10-05` Check repayments against bank statements.
- `AWP-10-06` Recalculate interest expense and accrued interest.
- `AWP-10-07` Check current and non-current classification.
- `AWP-10-08` Review loan terms, security and covenant requirements where applicable.
- `AWP-10-09` Check subsequent repayments.

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
