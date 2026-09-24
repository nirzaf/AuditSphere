---
id: "T066"
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
# T066 — Implement fixed assets audit workpapers

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Traceability ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Execute Section 7 Fixed Assets procedures: Fixed Asset Register (FAR) reconciliation to GL (`AWP-07-01`), additions vouching (capital vs revenue, authorization, title) (`AWP-07-02`), disposals testing (proceeds, gain/loss calculation) (`AWP-07-03`), depreciation reperformance (`AWP-07-04`), physical inspection sample (`AWP-07-05`), impairment indicators review (`AWP-07-06`), charges and title deeds search (`AWP-07-07`).

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T058](../../00_INDEX.md), [T060](../../00_INDEX.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [Baseline Architecture and ADRs](../../reference/01_Baseline_Architecture_and_ADRs.md)
- [Audit Workflow Traceability Ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)
- [Preserved Audit Workflow Source](../../source/ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md)

## Sequential work

1. Reconcile Fixed Asset Register (FAR) roll-forward (cost, accumulated depreciation, NBV) to GL and prior-year audited balances (`AWP-07-01`).
2. Vouch fixed asset additions: inspect purchase invoices, board approvals, title deeds/ownership documents, and test capital vs revenue expenditure (`AWP-07-02`).
3. Test fixed asset disposals: verify authorization, recalculate gain/loss on disposal, and confirm removal from FAR (`AWP-07-03`).
4. Reperform depreciation calculations across asset classes using approved useful lives and methods (`AWP-07-04`).
5. Document sample physical inspection of major assets to verify existence and condition (`AWP-07-05`).
6. Assess impairment indicators (idle assets, obsolescence, damage) and review mortgages/charges registered against assets (`AWP-07-06`, `AWP-07-07`).

## 1. Domain Modeling (`.Domain`)

Pure calculators: `DepreciationCalculator`. Models: `FixedAssetsAuditWorkpaper`, `AssetAdditionTest`, `AssetDisposalTest`, `PhysicalVerificationRecord`.

## 2. Persistence & Migrations (`.Infrastructure`)

Fixed asset audit tables, roll-forward schedule records, depreciation test results, and title verification logs.

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `SaveFixedAssetsAuditWorkpaperCommand`, `RecordAssetAdditionTestCommand`, `RecordAssetDisposalTestCommand`, `ReperformDepreciationCommand`, `GetFixedAssetsAuditSummaryQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes sampling (T058) and planning (T060); feeds capex bridge to expenses (T067) and findings to T022.

## 5. Blazor UI Architecture (`.Web`)

Fixed Assets audit view under `/app/audit/fieldwork/fixed-assets` with FAR roll-forward grid, additions/disposals test tabs, depreciation recalculator, and physical inspection checklist.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-014 | Primary |
| AS-AUD-014-AC01 | Covered |
| AS-AUD-014-AC02 | Covered |
| AS-AUD-014-AC03 | Covered |
| AS-AUD-014-AC04 | Covered |
| AS-AUD-014-AC05 | Covered |
| AS-AUD-014-AC06 | Covered |
| AS-AUD-014-AC07 | Covered |
| AS-AUD-014-AC08 | Covered |
| AS-AUD-014-AC09 | Covered |
| AWP-07-01 | Covered |
| AWP-07-02 | Covered |
| AWP-07-03 | Covered |
| AWP-07-04 | Covered |
| AWP-07-05 | Covered |
| AWP-07-06 | Covered |
| AWP-07-07 | Covered |
| AWP-07-08 | Covered |
| AWP-07-09 | Covered |

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
