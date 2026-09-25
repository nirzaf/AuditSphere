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

Execute Section 17 Subsequent Events review up to the audit report date through the preserved source procedures: Review post-year-end bank statements and transactions (`AWP-17-01`), Review significant sales, purchases and payments after year-end (`AWP-17-02`), Review board/management meeting minutes (`AWP-17-03`), Check new loans, investments or major asset purchases (`AWP-17-04`), Review litigation and significant legal developments (`AWP-17-05`), and Discuss significant events with management (`AWP-17-06`). From that evidence, Determine whether events require adjustment or disclosure (`AWP-17-07`) and Ensure relevant events are reflected in the financial statements (`AWP-17-08`), then record audit acceptance, execution/traceability reconciliation and operator handover (`AS-AUD-028-AC10`) against the declared acceptance inputs in the audit acceptance and handover gate below.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T073](../../00_INDEX.md), [T074](../../00_INDEX.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [Baseline Architecture and ADRs](../../reference/01_Baseline_Architecture_and_ADRs.md)
- [Audit Workflow Traceability Ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)
- [Preserved Audit Workflow Source](../../source/ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md)

## Sequential work

1. Review post-year-end bank statements and transactions (`AWP-17-01`).
2. Review significant sales, purchases and payments after year-end (`AWP-17-02`).
3. Review board/management meeting minutes (`AWP-17-03`).
4. Check new loans, investments or major asset purchases (`AWP-17-04`).
5. Review litigation and significant legal developments (`AWP-17-05`).
6. Discuss significant events with management (`AWP-17-06`).
7. Classify each identified event from those procedures as Adjusting (Type 1 — conditions existing at the balance sheet date) or Non-Adjusting (Type 2 — conditions arising after it), and Determine whether events require adjustment or disclosure (`AWP-17-07`).
8. Ensure relevant events are reflected in the financial statements: adjusting events in the reported amounts, non-adjusting events in the note disclosures (`AWP-17-08`).
9. Enforce reviewed-through-date coverage matching the planned audit report date, then record audit acceptance, execution/traceability reconciliation and operator handover (`AS-AUD-028-AC10`) under the gate below.

## Audit acceptance and handover gate

Final audit acceptance (`AS-AUD-028-AC10`) is a human professional decision. The software records it only when every declared acceptance input is present and evidenced; a missing input blocks the gate and no acceptance is implied.

| Declared acceptance input | Required evidence |
|---|---|
| Reviewed-through date equals the planned audit report date | Reviewed-through-date record agreed to the engagement's planned report date |
| Procedures `AWP-17-01`…`AWP-17-06` executed and dispositioned | Per-procedure result with evidence reference and preparer |
| Event classification decisions recorded (`AWP-17-07`) | Adjusting / Non-Adjusting decision per identified event, with the deciding practitioner |
| Financial-statement reflection verified (`AWP-17-08`) | Adjusting entries present in the reported amounts, or non-adjusting disclosure evidence |
| Execution/traceability reconciliation | Every claimed source ID mapped to an observed result in the traceability ledger |
| Operator handover record | Named receiving operator, date, and the exact accepted artifact set |

Dependencies: [T073](../../00_INDEX.md) and [T074](../../00_INDEX.md) must be COMPLETED with reviewed handoff evidence before this gate is attempted. Unresolved or unverified inputs fail closed; the platform never forms the audit conclusion.

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
