---
id: "T073"
work_package: "AUD-20"
modules: []
status: "NOT_STARTED"
depends_on: ["T029", "T060"]
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
# T073 — Implement substantive and final analytical review

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Traceability ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Execute Section 15 Analytical Review through the preserved source procedures: Compare current-year results with prior year (`AWP-15-01`), Analyse monthly revenue and expense trends (`AWP-15-02`), Compare gross profit and net profit margins (`AWP-15-03`), Analyse significant movements in major accounts (`AWP-15-04`), Calculate relevant ratios and key performance indicators (`AWP-15-05`), and Review receivable, payable and inventory days where applicable (`AWP-15-06`). Investigate significant or unexpected fluctuations (`AWP-15-07`) and Obtain management explanations and supporting evidence (`AWP-15-08`), then evaluate overall reasonableness of the financial statements as a professional judgement.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T029](../../00_INDEX.md), [T060](../../00_INDEX.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [Baseline Architecture and ADRs](../../reference/01_Baseline_Architecture_and_ADRs.md)
- [Audit Workflow Traceability Ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)
- [Preserved Audit Workflow Source](../../source/ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md)

## Sequential work

1. Compare current-year results with prior year across statement lines and ratios (`AWP-15-01`).
2. Analyse monthly revenue and expense trends and period-over-period movements (`AWP-15-02`).
3. Compare gross profit and net profit margins against prior year and budget (`AWP-15-03`).
4. Analyse significant movements in major accounts (`AWP-15-04`).
5. Calculate relevant ratios and key performance indicators (Current Ratio, Quick Ratio, Gross Margin, Operating Margin, Debt-to-Equity, Asset Turnover) from the financial statements (`AWP-15-05`).
6. Review receivable, payable and inventory days where applicable (`AWP-15-06`).
7. Investigate significant or unexpected fluctuations exceeding approved materiality thresholds (`AWP-15-07`).
8. Obtain management explanations and supporting evidence for each investigated fluctuation (`AWP-15-08`).
9. Formulate the overall reasonableness conclusion on the financial statements as a human professional judgement.

## 1. Domain Modeling (`.Domain`)

Pure calculators: `FinancialRatioCalculator`, `AnalyticalVarianceDetector`. Models: `AnalyticalReviewWorkpaper`, `FinancialRatioRecord`, `VarianceInvestigationRecord`.

## 2. Persistence & Migrations (`.Infrastructure`)

Analytical review tables, ratio definition stores, variance investigation notes, and partner review sign-offs.

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `ComputeAnalyticalReviewCommand`, `RecordVarianceInvestigationCommand`, `FinalizeAnalyticalReviewCommand`, `GetAnalyticalReviewSummaryQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes canonical statements (T029) and planning materiality (T060); feeds final analytics to Going Concern (T074) and Audit Release (T040).

## 5. Blazor UI Architecture (`.Web`)

Analytical Review workbench under `/app/audit/analytics` with financial ratio cards, interactive variance waterfall, investigation editor, and overall reasonableness conclusion sign-off.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-022 | Primary |
| AS-AUD-022-AC01 | Covered |
| AS-AUD-022-AC02 | Covered |
| AS-AUD-022-AC03 | Covered |
| AS-AUD-022-AC04 | Covered |
| AS-AUD-022-AC05 | Covered |
| AS-AUD-022-AC06 | Covered |
| AS-AUD-022-AC07 | Covered |
| AS-AUD-022-AC08 | Covered |
| AS-AUD-022-AC09 | Covered |
| AS-AUD-022-AC10 | Covered |
| AWP-15-01 | Covered |
| AWP-15-02 | Covered |
| AWP-15-03 | Covered |
| AWP-15-04 | Covered |
| AWP-15-05 | Covered |
| AWP-15-06 | Covered |
| AWP-15-07 | Covered |
| AWP-15-08 | Covered |

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
