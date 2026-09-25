---
id: "T068"
work_package: "AUD-19"
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
# T068 — Implement payroll audit workpapers

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Traceability ledger](../../tracking/auditsphere-audit-tracker-workflow-traceability.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Execute Section 9 Payroll audit procedures (audit workpapers only, not payroll processing): reconcile payroll register to GL and tax returns (`AWP-09-01`), substantive testing of employee sample (contracts, approved pay rates, gross-to-net recalculations, statutory deductions) (`AWP-09-02`), joiners and leavers testing (`AWP-09-03`), director and key management remuneration disclosures (`AWP-09-04`), payroll accruals and bonus/holiday pay provisions testing (`AWP-09-06`).

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T058](../../auditsphere-r2r-index-task-breakdown.md), [T060](../../auditsphere-r2r-index-task-breakdown.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [Baseline Architecture and ADRs](../../reference/auditsphere-r2r-reference-baseline-architecture-and-adrs.md)
- [Audit Workflow Traceability Ledger](../../tracking/auditsphere-audit-tracker-workflow-traceability.md)
- [Preserved Audit Workflow Source](../../source/auditsphere-audit-source-workflow-gap-closure-user-stories-historical.md)

## Sequential work

1. Reconcile monthly payroll summary registers to total payroll expense in GL and statutory tax returns (`AWP-09-01`).
2. Perform substantive sample testing of employees: agree basic salary to employment contract, test gross-to-net calculations and statutory deductions, and trace net pay to bank statements (`AWP-09-02`).
3. Test joiners and leavers during the year: verify commencement/termination letters, pro-rata pay calculations, and final settlements (`AWP-09-03`).
4. Verify director and key management remuneration against board resolutions and disclosure requirements (`AWP-09-04`).
5. Test year-end payroll accruals, holiday pay provisions, and performance bonuses (`AWP-09-06`).

## 1. Domain Modeling (`.Domain`)

Pure calculators: `PayrollAuditReperformer`. Models: `PayrollAuditWorkpaper`, `EmployeeSampleTest`, `JoinerLeaverAuditTest`, `DirectorRemunerationRecord`.

## 2. Persistence & Migrations (`.Infrastructure`)

Payroll audit test tables, sample test records, and reconciliations under client engagement boundary (no personal salary leaks).

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `SavePayrollAuditWorkpaperCommand`, `RecordEmployeeAuditTestCommand`, `RecordJoinerLeaverTestCommand`, `GetPayrollAuditSummaryQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes sampling (T058) and planning (T060); feeds payroll tax liabilities to T071 and differences to T022.

## 5. Blazor UI Architecture (`.Web`)

Payroll audit view under `/app/audit/fieldwork/payroll` with payroll reconciliation tab, employee sample testing grid, joiner/leaver audit log, and director disclosure tie-out.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-016 | Primary |
| AS-AUD-016-AC01 | Covered |
| AS-AUD-016-AC02 | Covered |
| AS-AUD-016-AC03 | Covered |
| AS-AUD-016-AC04 | Covered |
| AS-AUD-016-AC05 | Covered |
| AS-AUD-016-AC06 | Covered |
| AS-AUD-016-AC07 | Covered |
| AS-AUD-016-AC08 | Covered |
| AS-AUD-016-AC09 | Covered |
| AWP-09-01 | Covered |
| AWP-09-02 | Covered |
| AWP-09-03 | Covered |
| AWP-09-04 | Covered |
| AWP-09-05 | Covered |
| AWP-09-06 | Covered |
| AWP-09-07 | Covered |
| AWP-09-08 | Covered |

Source procedures (preserved wording):

- `AWP-09-01` Obtain annual/monthly payroll summary and reconcile to GL.
- `AWP-09-02` Select employees for detailed testing.
- `AWP-09-03` Check employment contracts and salary details.
- `AWP-09-04` Recalculate gross salary, allowances and deductions.
- `AWP-09-05` Agree selected salary payments to bank statements.
- `AWP-09-06` Test new employees and supporting employment documents.
- `AWP-09-07` Check terminated employees and final payments.
- `AWP-09-08` Review unusual changes in payroll or employee numbers.

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
