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

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Traceability ledger](../../tracking/auditsphere-audit-tracker-workflow-traceability.md)

**Status scope:** NOT_STARTED means this new task has not been assessed/executed under this breakdown. It does not assert that its underlying code is absent. First inspect and reuse the current implementation. No current repository progress has been imported.

## Outcome

Allow an audit engagement to adopt a published library version, tailor procedures (in-scope, out-of-scope, approved N/A with required justification), track procedure execution lifecycle states, link workpapers, and enforce ReviewPoints.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T014](../../auditsphere-r2r-index-task-breakdown.md), [T055](../../auditsphere-r2r-index-task-breakdown.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [Baseline Architecture and ADRs](../../reference/auditsphere-r2r-reference-baseline-architecture-and-adrs.md)
- [Audit Workflow Traceability Ledger](../../tracking/auditsphere-audit-tracker-workflow-traceability.md)
- [Preserved Audit Workflow Source](../../source/auditsphere-audit-source-workflow-gap-closure-user-stories-historical.md)

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

## Implementation notes (status unchanged)

Repository evidence observed on 2026-10-17. These notes do not change `status`, the completion checklist or the handoff table above. They are not an independent review and do not make this card complete.

- **Spec link:** `docs/task_breakdown/source/auditsphere-audit-specification-working-process-atomic-proposed.md` (the spec header links AS-AUD-002 / T055 and AS-AUD-003 / T056). The spec says T055/T056 completion keeps the repository's dependency and independent-review rules.
- **Implementing commits on `master`:** `8b0c3303` feat(audit): adopt exact published programme versions (spec Atomic Task 02, exact programme adoption); docs follow-up `fa60da52`.
- **Recorded focused checks** (`docs/execution/status.json` → `targetedVerification.atomicAuditWorkingProcessTask02`; `docs/execution/auditsphere-execution-current-slice.md`): Domain adoption regression 1/1; API adoption/options regression 1/1; Angular fieldwork spec 12/12; E2E project compilation passed; API and Web Release builds passed; OpenAPI drift and `git diff --check` passed. The browser journey was not run.
- **Hosted CI:** `8b0c3303` cancelled; head `master` `fa60da52` fails at `Build · .NET Release` (OpenAPI contract drift).
- **Dependencies:** T056 depends on T014 (NOT_STARTED) and T055 (NOT_STARTED).
- **Review:** no independent reviewer decision is recorded for these commits.
