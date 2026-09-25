---
id: "T071"
work_package: "AUD-19"
modules: []
status: "NOT_STARTED"
depends_on: ["T060"]
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
# T071 — Implement tax and statutory liabilities audit workpapers

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Traceability ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Execute Section 13 Tax & Statutory Liabilities audit procedures (workpaper testing, not tax filing): current income tax computation audit review (`AWP-13-01`), deferred tax calculation review against temporary differences and recognition criteria (`AWP-13-02`), tax payments verification to bank statements / official tax receipts (`AWP-13-03`), indirect taxes (VAT/GST) and withholding taxes reconciliation to returns (`AWP-13-04`), review of tax assessments, disputes, and contingency disclosures (`AWP-13-05`).

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T060](../../00_INDEX.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [Baseline Architecture and ADRs](../../reference/01_Baseline_Architecture_and_ADRs.md)
- [Audit Workflow Traceability Ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)
- [Preserved Audit Workflow Source](../../source/ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md)

## Sequential work

1. Reperform current corporate income tax computation (accounting profit to taxable profit adjustments, tax rates, reliefs) (`AWP-13-01`).
2. Review deferred tax assets and liabilities calculation, temporary differences, and probability of future taxable profit (`AWP-13-02`).
3. Verify tax payments made during the year to bank statements and official tax payment challans/receipts (`AWP-13-03`).
4. Reconcile statutory indirect tax (VAT/GST) and withholding tax accounts to statutory filings and turnover (`AWP-13-04`).
5. Review tax assessment orders, tax authority queries, and evaluate provisions for tax disputes and contingencies (`AWP-13-05`).
6. Review tax expense and deferred tax disclosures in the financial statement notes (`AWP-13-06`).

## 1. Domain Modeling (`.Domain`)

Pure calculators: `TaxAuditCalculator`, `DeferredTaxReperformer`. Models: `TaxAuditWorkpaper`, `TaxComputationReview`, `TaxAssessmentReview`.

## 2. Persistence & Migrations (`.Infrastructure`)

Tax audit test tables, tax computation schedules, and dispute contingency records under engagement scope.

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `SaveTaxAuditWorkpaperCommand`, `ReviewTaxComputationCommand`, `ReviewDeferredTaxCommand`, `GetTaxAuditSummaryQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes expenses (T067), payroll (T068), and planning (T060); feeds tax adjustments to T022 and disclosures to T032.

## 5. Blazor UI Architecture (`.Web`)

Tax & Statutory Liabilities view under `/app/audit/fieldwork/tax` with tax computation review tab, deferred tax schedule, statutory tax reconciliation, and dispute assessment log.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-020 | Primary |
| AS-AUD-020-AC01 | Covered |
| AS-AUD-020-AC02 | Covered |
| AS-AUD-020-AC03 | Covered |
| AS-AUD-020-AC04 | Covered |
| AS-AUD-020-AC05 | Covered |
| AS-AUD-020-AC06 | Covered |
| AS-AUD-020-AC07 | Covered |
| AWP-13-01 | Covered |
| AWP-13-02 | Covered |
| AWP-13-03 | Covered |
| AWP-13-04 | Covered |
| AWP-13-05 | Covered |
| AWP-13-06 | Covered |
| AWP-13-07 | Covered |

Source procedures (preserved wording):

- `AWP-13-01` Obtain tax returns and tax computations.
- `AWP-13-02` Reconcile tax balances with the GL/TB.
- `AWP-13-03` Check tax payments against bank statements.
- `AWP-13-04` Review outstanding tax liabilities and penalties.
- `AWP-13-05` Review correspondence with tax authorities.
- `AWP-13-06` Check tax provisions and current-year tax expense.
- `AWP-13-07` Check relevant tax disclosures in the financial statements.

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
