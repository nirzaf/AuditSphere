---
id: "T074"
work_package: "AUD-20"
modules: []
status: "NOT_STARTED"
depends_on: ["T069", "T073"]
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
# T074 — Implement going concern audit evaluation

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Traceability ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Execute Section 16 ISA 570 Going Concern assessment: evaluate management's 12-month future cash flow forecast (`AWP-16-01`), perform sensitivity analysis on key forecast assumptions (`AWP-16-02`), assess liquidity position and debt covenant compliance (`AWP-16-03`), identify financial, operating, and other going concern risk indicators (`AWP-16-04`), document auditor's evaluation of mitigating factors and professional conclusion on material uncertainty (`AWP-16-05`), review going concern disclosures (`AWP-16-06`).

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T069](../../00_INDEX.md), [T073](../../00_INDEX.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [Baseline Architecture and ADRs](../../reference/01_Baseline_Architecture_and_ADRs.md)
- [Audit Workflow Traceability Ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)
- [Preserved Audit Workflow Source](../../source/ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md)

## Sequential work

1. Review management's going concern assessment covering at least 12 months from the financial statements date (`AWP-16-01`).
2. Evaluate future cash flow forecasts, test mathematical accuracy, and perform sensitivity analysis on revenue and cost assumptions (`AWP-16-02`).
3. Assess current liquidity position, borrowing facilities, and covenant compliance (linking to T069) (`AWP-16-03`).
4. Screen for ISA 570 risk indicators: negative operating cash flows, adverse key financial ratios, loss of key customers, legal claims (`AWP-16-04`).
5. Document auditor's evaluation of management's mitigating plans and record partner conclusion: no material uncertainty, material uncertainty identified, or going concern basis inappropriate (`AWP-16-05`).
6. Review adequacy of going concern disclosures in the notes and determine impact on auditor's report (`AWP-16-06`).

## 1. Domain Modeling (`.Domain`)

Pure calculators: `ForecastSensitivityCalculator`. Models: `GoingConcernAssessment`, `CashFlowForecastReview`, `GoingConcernRiskIndicator`, `GoingConcernConclusion`.

## 2. Persistence & Migrations (`.Infrastructure`)

Going concern tables, forecast sensitivity parameters, indicator evaluation logs, and engagement partner sign-off records.

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `SaveGoingConcernAssessmentCommand`, `RecordForecastReviewCommand`, `RecordGoingConcernConclusionCommand`, `GetGoingConcernSummaryQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes loan covenant testing (T069) and analytical review (T073); feeds going concern conclusion to Audit Release (T040).

## 5. Blazor UI Architecture (`.Web`)

Going Concern assessment view under `/app/audit/completion/going-concern` with forecast review tab, sensitivity stress test tool, indicator checklist, and partner conclusion card.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-023 | Primary |
| AS-AUD-023-AC01 | Covered |
| AS-AUD-023-AC02 | Covered |
| AS-AUD-023-AC03 | Covered |
| AS-AUD-023-AC04 | Covered |
| AS-AUD-023-AC05 | Covered |
| AS-AUD-023-AC06 | Covered |
| AS-AUD-023-AC07 | Covered |
| AS-AUD-023-AC08 | Covered |
| AS-AUD-023-AC09 | Covered |
| AS-AUD-023-AC10 | Covered |
| AWP-16-01 | Covered |
| AWP-16-02 | Covered |
| AWP-16-03 | Covered |
| AWP-16-04 | Covered |
| AWP-16-05 | Covered |
| AWP-16-06 | Covered |
| AWP-16-07 | Covered |
| AWP-16-08 | Covered |
| AWP-16-09 | Covered |

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
