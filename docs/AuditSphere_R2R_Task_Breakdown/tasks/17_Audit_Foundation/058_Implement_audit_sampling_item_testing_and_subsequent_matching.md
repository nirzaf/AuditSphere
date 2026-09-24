---
id: "T058"
work_package: "AUD-17"
modules: []
status: "NOT_STARTED"
depends_on: ["T057"]
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
# T058 — Implement audit sampling, item testing and subsequent matching

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Traceability ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Provide deterministic, reproducible audit sampling (monetary unit sampling, targeted high-value, random, stratified), structured test sheets, vouching evidence linkage, cut-off verification, subsequent cash/transaction matching, and finding generation.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T057](../../00_INDEX.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [Baseline Architecture and ADRs](../../reference/01_Baseline_Architecture_and_ADRs.md)
- [Audit Workflow Traceability Ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)
- [Preserved Audit Workflow Source](../../source/ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md)

## Sequential work

1. Build pure deterministic sampling calculator supporting MUS, key item stratification, random sampling with seeded reproducibility.
2. Create structured audit test sheets recording sample items, vouching attributes, document references, and testing conclusions.
3. Implement cut-off testing fields: transaction date, document date, shipping/receiving date, period-end indicator.
4. Implement subsequent cash/payment matching: link sample transactions to post-year-end bank/GL entries to verify settlement.
5. Provide automatic finding and audit difference generation from test sheet exceptions.

## 1. Domain Modeling (`.Domain`)

Pure engines: `SamplingEngine`, `StratificationCalculator`. Models: `AuditSampleSet`, `SampleItemTest`, `CutOffTestRecord`, `SubsequentMatchRecord`.

## 2. Persistence & Migrations (`.Infrastructure`)

Storage for sample parameters, item test records, evidence attachment references, and finding linkages under engagement scope.

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `GenerateAuditSampleCommand`, `RecordItemTestResultCommand`, `RecordCutOffTestCommand`, `MatchSubsequentTransactionCommand`, `GetSampleSetQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes reconciled populations (T057); provides sampling foundation for all audit fieldwork tasks (T061–T072).

## 5. Blazor UI Architecture (`.Web`)

Sampling and testing UI under `/app/audit/sampling` with parameter configuration, sample preview, inline test sheet data grid, and finding creation button.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-005 | Primary |
| AS-AUD-005-AC01 | Covered |
| AS-AUD-005-AC02 | Covered |
| AS-AUD-005-AC03 | Covered |
| AS-AUD-005-AC04 | Covered |
| AS-AUD-005-AC05 | Covered |
| AS-AUD-005-AC06 | Covered |
| AS-AUD-005-AC07 | Covered |
| AS-AUD-005-AC08 | Covered |
| AS-AUD-005-AC09 | Covered |

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
