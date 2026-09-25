---
id: "T075"
work_package: "AUD-20"
modules: []
status: "NOT_STARTED"
depends_on: ["T073", "T074"]
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
# T075 — Implement subsequent events audit review

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Traceability ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Execute Section 17 ISA 560 Subsequent Events review up to the report date: management inquiries regarding post-balance-sheet events (`AWP-17-01`), review of subsequent board minutes, interim accounts, and legal correspondence (`AWP-17-02`), review of subsequent cash receipts and disbursements (`AWP-17-03`), classify identified events as Adjusting or Non-Adjusting (`AWP-17-04`), verify appropriate adjustments or footnote disclosures in the financial statements (`AWP-17-05`), and record audit acceptance, execution/traceability reconciliation and operator handover (`AS-AUD-028-AC10`).

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T073](../../00_INDEX.md), [T074](../../00_INDEX.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [Baseline Architecture and ADRs](../../reference/01_Baseline_Architecture_and_ADRs.md)
- [Audit Workflow Traceability Ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)
- [Preserved Audit Workflow Source](../../source/ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md)

## Sequential work

1. Record management inquiries regarding events after the reporting period up to the audit report date (`AWP-17-01`).
2. Inspect post-balance-sheet minutes of meetings of directors and shareholders, latest interim financial statements, and lawyer inquiry letters (`AWP-17-02`).
3. Review subsequent cash receipts, disbursements, and journal entries for unrecorded liabilities or asset impairments (`AWP-17-03`).
4. Classify identified subsequent events: Type 1 (Adjusting — conditions existing at balance sheet date) vs Type 2 (Non-adjusting — conditions arising subsequent to balance sheet date) (`AWP-17-04`).
5. Verify that adjusting events are reflected in the financial statements and non-adjusting events are adequately disclosed in notes (`AWP-17-05`).
6. Enforce reviewed-through-date coverage matching the planned audit report date (`AWP-17-06`).

## 1. Domain Modeling (`.Domain`)

Models: `SubsequentEventsWorkpaper`, `SubsequentEventItem`, `EventClassification` (Adjusting / NonAdjusting), `ReviewedThroughDateRecord`.

## 2. Persistence & Migrations (`.Infrastructure`)

Subsequent events register, minutes inspection logs, classification records, and release gate prerequisite flags.

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `SaveSubsequentEventsReviewCommand`, `RecordSubsequentEventItemCommand`, `ClassifySubsequentEventCommand`, `GetSubsequentEventsSummaryQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes analytical review (T073) and going concern (T074); feeds subsequent event adjustments to T022 and release clearance to T040.

## 5. Blazor UI Architecture (`.Web`)

Subsequent Events view under `/app/audit/completion/subsequent-events` with events register, meeting minutes tracker, adjusting/non-adjusting classification card, and reviewed-through-date indicator.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-024 | Primary |
| AS-AUD-024-AC01 | Covered |
| AS-AUD-024-AC02 | Covered |
| AS-AUD-024-AC03 | Covered |
| AS-AUD-024-AC04 | Covered |
| AS-AUD-024-AC05 | Covered |
| AS-AUD-024-AC06 | Covered |
| AS-AUD-024-AC07 | Covered |
| AS-AUD-024-AC08 | Covered |
| AS-AUD-024-AC09 | Covered |
| AS-AUD-024-AC10 | Covered |
| AS-AUD-028 | Primary |
| AS-AUD-028-AC10 | Covered |
| AWP-17-01 | Covered |
| AWP-17-02 | Covered |
| AWP-17-03 | Covered |
| AWP-17-04 | Covered |
| AWP-17-05 | Covered |
| AWP-17-06 | Covered |
| AWP-17-07 | Covered |
| AWP-17-08 | Covered |

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
