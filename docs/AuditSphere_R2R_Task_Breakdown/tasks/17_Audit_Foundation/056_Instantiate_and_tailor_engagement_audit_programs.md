---
id: "T056"
work_package: "AUD-17"
modules: []
status: "NOT_STARTED"
depends_on: ["T014", "T055"]
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
# T056 — Instantiate and tailor engagement audit programs

[Master index](../../00_INDEX.md) · [Status rules](../../00_INDEX.md#status-rules) · [Traceability ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Allow an audit engagement to adopt a published library version, tailor procedures (in-scope, out-of-scope, approved N/A with required justification), track procedure execution lifecycle states, link workpapers, and enforce ReviewPoints.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T014](../../00_INDEX.md), [T055](../../00_INDEX.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/00_Scope_and_Evidence_Boundaries.md)
- [Baseline Architecture and ADRs](../../reference/01_Baseline_Architecture_and_ADRs.md)
- [Audit Workflow Traceability Ledger](../../tracking/AUDIT_WORKFLOW_TRACEABILITY.md)
- [Preserved Audit Workflow Source](../../source/ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md)

## Sequential work

1. Instantiate engagement audit program from an approved published library version, preserving procedure IDs and source wording.
2. Implement tailoring rules: in-scope, out-of-scope, and N/A marking requiring mandatory justification and partner approval.
3. Build procedure execution state machine: `NOT_STARTED`, `IN_PROGRESS`, `REVIEWED`, `COMPLETED`.
4. Integrate ReviewPoint clearance and multi-user reviewer separation on every engagement procedure.

## 1. Domain Modeling (`.Domain`)

Engagement program models: `EngagementAuditProgram`, `EngagementProcedure`, `TailoringDecision`, `ProcedureStatus`, with strict reviewer separation invariants.

## 2. Persistence & Migrations (`.Infrastructure`)

Scoped persistence under `(ClientId, EngagementId)`, append-only audit trail for tailoring decisions, foreign keys to workpapers and ReviewPoints.

## 3. Application & CQRS Contracts (`.Application`)

Owned requests: `AdoptAuditProgramCommand`, `TailorProcedureCommand`, `UpdateProcedureStatusCommand`, `GetEngagementAuditProgramQuery`.

## 4. Inter-Module Lineage & Boundaries

Consumes active reporting context (T014) and library version (T055); produces tailored engagement program consumed by all audit fieldwork.

## 5. Blazor UI Architecture (`.Web`)

Engagement audit workbench under `/app/audit/programs/{EngagementId}` with section progress bars, tailoring modal, workpaper drawers, and ReviewPoint badges.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-003 | Primary |
| AS-AUD-003-AC01 | Covered |
| AS-AUD-003-AC02 | Covered |
| AS-AUD-003-AC03 | Covered |
| AS-AUD-003-AC04 | Covered |
| AS-AUD-003-AC05 | Covered |
| AS-AUD-003-AC06 | Covered |
| AS-AUD-003-AC07 | Covered |
| AS-AUD-003-AC08 | Covered |
| AS-AUD-003-AC09 | Covered |
| AS-AUD-003-AC10 | Covered |

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
