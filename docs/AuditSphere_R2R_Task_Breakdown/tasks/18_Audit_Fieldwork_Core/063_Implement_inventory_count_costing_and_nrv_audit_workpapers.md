---
id: "T063"
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
# T063 — Implement inventory count, costing and nrv audit workpapers

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Traceability ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Execute Section 4 Inventory procedures: physical stock count attendance sheet and auditor test counts (`AWP-04-01`, `AWP-04-02`), inventory roll-forward and cut-off (`AWP-04-03`), costing recalculation (FIFO/weighted average) against purchase invoices (`AWP-04-04`), NRV testing against post-year-end sales prices (`AWP-04-06`), slow-moving and obsolete stock allowance review (`AWP-04-05`), and third-party inventory confirmations (`AWP-04-07`).

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T058](../../00_INDEX.md), [T060](../../00_INDEX.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [Baseline Architecture and ADRs](../../reference/01_Baseline_Architecture_and_ADRs.md)
- [Audit Workflow Traceability Ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)
- [Preserved Audit Workflow Source](../../source/ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md)

## Sequential work

1. Record physical inventory count attendance observations, count sheet controls, and auditor test counts (floor-to-sheet and sheet-to-floor) (`AWP-04-01`, `AWP-04-02`).
2. Perform inventory roll-forward or roll-back between count date and balance sheet date (`AWP-04-03`).
3. Test unit costs against recent supplier invoices and recalculate inventory valuation under approved method (`AWP-04-04`).
4. Perform Net Realisable Value (NRV) testing by comparing cost to post-year-end sales invoices (`AWP-04-06`).
5. Evaluate slow-moving, damaged, and obsolete stock and determine adequacy of inventory write-down provisions (`AWP-04-05`).
6. Obtain confirmations for inventory held by third parties or consigned goods (`AWP-04-07`).

## 1. Domain Modeling (`.Domain`)

Pure calculators: `NrvCalculator`, `CostingReperformer`. Models: `InventoryAuditWorkpaper`, `StockCountTestSheet`, `NrvTestItem`, `ObsoleteInventoryReview`.

## 2. Persistence & Migrations (`.Infrastructure`)

Inventory audit tables, count sheets, test count records, and NRV comparison data.

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `SaveInventoryAuditWorkpaperCommand`, `RecordStockCountTestCommand`, `RecordNrvTestCommand`, `GetInventoryAuditSummaryQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes sampling engine (T058) and planning materiality (T060); feeds inventory findings to T022.

## 5. Blazor UI Architecture (`.Web`)

Inventory audit view under `/app/audit/fieldwork/inventory` with count attendance tab, test counts grid, unit costing tie-out, and NRV deficiency highlight.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-011 | Primary |
| AS-AUD-011-AC01 | Covered |
| AS-AUD-011-AC02 | Covered |
| AS-AUD-011-AC03 | Covered |
| AS-AUD-011-AC04 | Covered |
| AS-AUD-011-AC05 | Covered |
| AS-AUD-011-AC06 | Covered |
| AS-AUD-011-AC07 | Covered |
| AS-AUD-011-AC08 | Covered |
| AS-AUD-011-AC09 | Covered |
| AWP-04-01 | Covered |
| AWP-04-02 | Covered |
| AWP-04-03 | Covered |
| AWP-04-04 | Covered |
| AWP-04-05 | Covered |
| AWP-04-06 | Covered |
| AWP-04-07 | Covered |
| AWP-04-08 | Covered |

Source procedures (preserved wording):

- `AWP-04-01` Obtain the year-end inventory listing and agree it to the GL.
- `AWP-04-02` Attend/observe physical inventory count where applicable.
- `AWP-04-03` Perform auditor test counts and reconcile differences.
- `AWP-04-04` Check inventory quantities against count sheets/final listing.
- `AWP-04-05` Test inventory costs to purchase invoices or supporting records.
- `AWP-04-06` Review slow-moving, damaged and obsolete inventory.
- `AWP-04-07` Compare cost with NRV where applicable.
- `AWP-04-08` Test purchases and goods received around year-end for cut-off.

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
