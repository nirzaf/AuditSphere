---
id: "T062"
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
# T062 — Implement trade receivables and allowance audit workpapers

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Traceability ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Execute Section 3 Trade Receivables and ECL procedures: aged debtors analysis (`AWP-03-01`), circularisation and confirmations (`AWP-03-04`, `AWP-03-05`), subsequent collections testing (`AWP-03-07`), sales cut-off testing (`AWP-03-09`), ECL calculation reperformance and provision adequacy assessment (`AWP-03-08`).

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T058](../../00_INDEX.md), [T059](../../00_INDEX.md), [T060](../../00_INDEX.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [Baseline Architecture and ADRs](../../reference/01_Baseline_Architecture_and_ADRs.md)
- [Audit Workflow Traceability Ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)
- [Preserved Audit Workflow Source](../../source/ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md)

## Sequential work

1. Reconcile aged receivables subledger to general ledger/TB (`AWP-03-01`, `AWP-03-02`).
2. Perform debtor circularisation, confirm balances, and execute alternative procedures for non-replies (`AWP-03-04`, `AWP-03-05`).
3. Test subsequent cash receipts against post-year-end bank statements to verify recoverability (`AWP-03-07`).
4. Execute sales and receivables cut-off testing on invoices and dispatch notes before and after year-end (`AWP-03-09`).
5. Reperform Expected Credit Loss (ECL) calculation under IFRS 9 / local framework, test loss rate matrices, and evaluate provision adequacy (`AWP-03-08`).
6. Review customer credit balances for reclassification to liabilities (`AWP-03-03`).

## 1. Domain Modeling (`.Domain`)

Pure calculators: `EclProvisionCalculator`, `AgeingAnalyzer`. Models: `ReceivablesAuditWorkpaper`, `DebtorTestItem`, `EclEvaluationRecord`.

## 2. Persistence & Migrations (`.Infrastructure`)

Receivables audit tables, debtor circularisation status, subsequent cash match links, and ECL matrix storage.

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `SaveReceivablesAuditWorkpaperCommand`, `RecordDebtorTestCommand`, `EvaluateEclProvisionCommand`, `GetReceivablesAuditSummaryQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes sampling engine (T058), confirmations (T059), and materiality (T060); feeds audit differences to T022.

## 5. Blazor UI Architecture (`.Web`)

Trade Receivables workbench under `/app/audit/fieldwork/receivables` with aged profile breakdown, circularisation tracker, subsequent receipts matcher, and ECL model evaluator.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-009 | Primary |
| AS-AUD-009-AC01 | Covered |
| AS-AUD-009-AC02 | Covered |
| AS-AUD-009-AC03 | Covered |
| AS-AUD-009-AC04 | Covered |
| AS-AUD-009-AC05 | Covered |
| AS-AUD-009-AC06 | Covered |
| AS-AUD-009-AC07 | Covered |
| AS-AUD-009-AC08 | Covered |
| AS-AUD-009-AC09 | Covered |
| AS-AUD-010 | Primary |
| AS-AUD-010-AC01 | Covered |
| AS-AUD-010-AC02 | Covered |
| AS-AUD-010-AC03 | Covered |
| AS-AUD-010-AC04 | Covered |
| AS-AUD-010-AC05 | Covered |
| AS-AUD-010-AC06 | Covered |
| AS-AUD-010-AC07 | Covered |
| AWP-03-01 | Covered |
| AWP-03-02 | Covered |
| AWP-03-03 | Covered |
| AWP-03-04 | Covered |
| AWP-03-05 | Covered |
| AWP-03-06 | Covered |
| AWP-03-07 | Covered |
| AWP-03-08 | Covered |
| AWP-03-09 | Covered |

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
