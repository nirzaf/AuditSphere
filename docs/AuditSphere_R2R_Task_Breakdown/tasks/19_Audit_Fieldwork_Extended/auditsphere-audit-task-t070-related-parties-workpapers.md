---
id: "T070"
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
# T070 — Implement related parties audit workpapers

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Traceability ledger](../../tracking/auditsphere-audit-tracker-workflow-traceability.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Execute Section 12 Related Parties procedures: compile and verify related parties register (`AWP-12-01`), completeness search through board minutes, shareholder registers, and declarations (`AWP-12-02`), related party transactions listing and reconciliation (`AWP-12-03`), arm's length terms and pricing evaluation (`AWP-12-04`), outstanding balances and guarantees verification (`AWP-12-05`), IAS 24 / framework disclosure review (`AWP-12-06`).

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T060](../../auditsphere-r2r-index-task-breakdown.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [Baseline Architecture and ADRs](../../reference/auditsphere-r2r-reference-baseline-architecture-and-adrs.md)
- [Audit Workflow Traceability Ledger](../../tracking/auditsphere-audit-tracker-workflow-traceability.md)
- [Preserved Audit Workflow Source](../../source/auditsphere-audit-source-workflow-gap-closure-user-stories-historical.md)

## Sequential work

1. Maintain verified related parties register (directors, shareholders, key management, affiliates) (`AWP-12-01`).
2. Perform completeness procedures: inspect board minutes, statutory filings, director interest declarations (`AWP-12-02`).
3. Reconcile related party transaction listings against sales, purchases, and loan accounts (`AWP-12-03`).
4. Examine terms, contracts, pricing, and business rationale for arm's length compliance (`AWP-12-04`).
5. Confirm year-end balances, commitments, and guarantees provided to/by related parties (`AWP-12-05`).
6. Review disclosure compliance under IAS 24 / applicable framework (`AWP-12-06`).

## 1. Domain Modeling (`.Domain`)

Models: `RelatedPartyRegistry`, `RelatedPartyTransactionAudit`, `RelatedPartyBalanceAudit`.

## 2. Persistence & Migrations (`.Infrastructure`)

Related party audit tables with relationship types, transaction classifications, and disclosure mappings.

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `SaveRelatedPartyRegisterCommand`, `RecordRelatedPartyTransactionAuditCommand`, `GetRelatedPartyAuditSummaryQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes planning (T060); feeds related party disclosures to Financial Statements & Disclosures (T032).

## 5. Blazor UI Architecture (`.Web`)

Related Parties view under `/app/audit/fieldwork/related-parties` with entity register, transaction ledger, pricing review notes, and disclosure completeness checklist.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-019 | Primary |
| AS-AUD-019-AC01 | Covered |
| AS-AUD-019-AC02 | Covered |
| AS-AUD-019-AC03 | Covered |
| AS-AUD-019-AC04 | Covered |
| AS-AUD-019-AC05 | Covered |
| AS-AUD-019-AC06 | Covered |
| AS-AUD-019-AC07 | Covered |
| AS-AUD-019-AC08 | Covered |
| AWP-12-01 | Covered |
| AWP-12-02 | Covered |
| AWP-12-03 | Covered |
| AWP-12-04 | Covered |
| AWP-12-05 | Covered |
| AWP-12-06 | Covered |
| AWP-12-07 | Covered |

Source procedures (preserved wording):

- `AWP-12-01` Obtain management's related-party listing.
- `AWP-12-02` Check directors, shareholders and key management records.
- `AWP-12-03` Review related-party transactions during the year.
- `AWP-12-04` Review significant related-party balances.
- `AWP-12-05` Confirm significant balances where appropriate.
- `AWP-12-06` Check whether transactions are properly recorded.
- `AWP-12-07` Verify required related-party disclosures in the financial statements.

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
