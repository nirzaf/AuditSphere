---
id: "T061"
work_package: "AUD-18"
modules: []
status: "NOT_STARTED"
depends_on: ["T026", "T059", "T060"]
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
# T061 — Implement cash and bank audit workpapers

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Traceability ledger](../../tracking/auditsphere-audit-tracker-workflow-traceability.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Execute Section 2 Cash & Bank audit procedures: test bank reconciliations and outstanding items (`AWP-02-02`), reconcile bank confirmation certificates (`AWP-02-03`), retranslate foreign currency balances (`AWP-02-04`), verify cash cut-off (`AWP-02-05`), inspect petty cash (`AWP-02-06`), and verify restricted cash disclosures (`AWP-02-07`).

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T026](../../auditsphere-r2r-index-task-breakdown.md), [T059](../../auditsphere-r2r-index-task-breakdown.md), [T060](../../auditsphere-r2r-index-task-breakdown.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [Baseline Architecture and ADRs](../../reference/auditsphere-r2r-reference-baseline-architecture-and-adrs.md)
- [Audit Workflow Traceability Ledger](../../tracking/auditsphere-audit-tracker-workflow-traceability.md)
- [Preserved Audit Workflow Source](../../source/auditsphere-audit-source-workflow-gap-closure-user-stories-historical.md)

## Sequential work

1. Build Cash & Bank lead schedule and account verification workpapers (`AWP-02-01`).
2. Perform auditor substantive testing of client bank reconciliations (vouch outstanding lodgements and unpresented cheques to post-year-end bank statements) (`AWP-02-02`).
3. Reconcile independent bank confirmation responses to general ledger and client reconciliations (`AWP-02-03`).
4. Verify foreign currency bank balance translations using approved period-end closing rates (`AWP-02-04`).
5. Execute cash cut-off testing on transfers and cheques issued/received around period end (`AWP-02-05`).
6. Record petty cash verification/count evidence and evaluate restricted cash/lien disclosures (`AWP-02-06`, `AWP-02-07`).

## 1. Domain Modeling (`.Domain`)

Models: `CashAuditWorkpaper`, `BankReconciliationAuditTest`, `BankTransferCutOffTest`, `PettyCashAuditRecord`.

## 2. Persistence & Migrations (`.Infrastructure`)

Tables for bank audit test records, supporting statement hashes, exception logs, and linkage to client reconciliation schedules.

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `SaveCashAuditWorkpaperCommand`, `TestBankReconciliationCommand`, `RecordCashCutOffTestCommand`, `GetCashAuditSummaryQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes client reconciliations (T026), confirmations (T059), and planning materiality (T060); feeds SAD differences to T022.

## 5. Blazor UI Architecture (`.Web`)

Cash & Bank audit view under `/app/audit/fieldwork/cash` with bank account tab strip, reconciliation reperformance tool, confirmation tie-out, and cut-off sheet.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-008 | Primary |
| AS-AUD-008-AC01 | Covered |
| AS-AUD-008-AC02 | Covered |
| AS-AUD-008-AC03 | Covered |
| AS-AUD-008-AC04 | Covered |
| AS-AUD-008-AC05 | Covered |
| AS-AUD-008-AC06 | Covered |
| AS-AUD-008-AC07 | Covered |
| AS-AUD-008-AC08 | Covered |
| AS-AUD-008-AC09 | Covered |
| AWP-02-01 | Covered |
| AWP-02-02 | Covered |
| AWP-02-03 | Covered |
| AWP-02-04 | Covered |
| AWP-02-05 | Covered |
| AWP-02-06 | Covered |
| AWP-02-07 | Covered |
| AWP-02-08 | Covered |

Source procedures (preserved wording):

- `AWP-02-01` Obtain bank reconciliation for all bank accounts at year-end.
- `AWP-02-02` Agree bank ledger balances with the Trial Balance.
- `AWP-02-03` Agree bank reconciliation balances with year-end bank statements.
- `AWP-02-04` Obtain direct bank confirmations and reconcile confirmed balances.
- `AWP-02-05` Check outstanding cheques, deposits and other reconciling items.
- `AWP-02-06` Investigate old or unusual outstanding items.
- `AWP-02-07` Test selected bank transactions to supporting documents.
- `AWP-02-08` Review subsequent bank statements for unusual transactions.

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
