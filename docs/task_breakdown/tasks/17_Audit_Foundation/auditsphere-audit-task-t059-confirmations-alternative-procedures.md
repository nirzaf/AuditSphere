---
id: "T059"
work_package: "AUD-17"
modules: []
status: "BLOCKED"
depends_on: ["T058"]
owner: "Codex implementation coordinator"
reviewer: ""
review_decision: ""
reviewed_commit: ""
evidence_ref: ""
approval_ref: ""
blocked_reason: "The Legal confirmation taxonomy slice is implemented in this change, but full T059 acceptance is still blocked by incomplete hard dependency T058 and other listed gaps: ledger-derived balance reconciliation, difference explanation, SAD/ReviewPoint links, structured response origin, reviewer identity, response hash, aging, and procedure-to-test linkage."
branch: ""
issue_pr: ""
updated_at: "2026-10-10T17:46:04+00:00"
---
# T059 — Manage audit confirmations and alternative procedures

[Master index](../../auditsphere-r2r-index-task-breakdown.md) · [Status rules](../../auditsphere-r2r-index-task-breakdown.md#status-rules) · [Traceability ledger](../../tracking/auditsphere-audit-tracker-workflow-traceability.md)

**Current assessment (2026-10-10): BLOCKED.** The Legal confirmation taxonomy slice is implemented, but the full task remains blocked by incomplete hard dependency T058 and the residual criteria below. Do not mark the whole task complete based on the taxonomy slice.

### Verified local slice: Legal confirmation taxonomy

- `AuditConfirmationAreaCodes` defines confirmation categories separately from the 18 audit-program `AuditAreaCodes`; `LEGAL` is supported and `LEGAL_SERVICES` and other unknown categories fail closed.
- Single-case and batch application/API paths validate the same controlled taxonomy. Angular renders the server-provided category choices instead of accepting arbitrary free text.
- Evidence: `src/AuditSphereOps.Domain/Audit/Fieldwork.cs`, `src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.Confirmations.cs`, `src/AuditSphereOps.Api/Ui/UiEndpoints.Confirmations.cs`, `src/AuditSphereOps.Ui/src/app/features/audit/confirmations.ts`, `tests/AuditSphereOps.Domain.Tests/AuditConfirmationCommandIsolationTests.cs`, and `tests/AuditSphereOps.E2E.Tests/AngularConfirmationJourneyTests.cs`.
- Current-tree verification passed on 2026-10-10: Domain `AuditConfirmationCommandIsolationTests.ConfirmationTaxonomy_KeepsLegalSeparateAndRejectsUnknownCategories` and E2E `AngularConfirmationJourneyTests.NativeSignalForms_ObservedDispatch_IndependentAlternativeReview_AndSessionRevocation`; suite totals and source commit are recorded in `docs/execution/status.json`.

### Remaining full-task blockers

- Reconcile confirmations to the authoritative client ledger balance and preserve reconciling items and explanations.
- Refuse an `AGREED` decision when the confirmed amount differs from the booked amount without an approved explanation.
- Link exceptions to SAD and ReviewPoints; structure direct versus client-forwarded response origin and retain the contact-validation reviewer.
- Persist response-document SHA-256 lineage, expose response aging, and link alternative procedures to the tests performed.
- Complete hard dependency T058 before promoting this card to active implementation or review.

## Outcome

Track external confirmation requests (bank, debtors, creditors, loans, legal), log dispatch and response receipt, compute variances against client balances, enforce alternative testing documentation for non-replies, and record partner clearance.

## Before starting

Hard dependencies must be COMPLETED, with reviewed handoff evidence:

- Dependencies: [T058](../../auditsphere-r2r-index-task-breakdown.md)

## Required reading

- [Scope and Evidence Boundaries](../../reference/auditsphere-r2r-reference-scope-and-evidence-boundaries.md)
- [Baseline Architecture and ADRs](../../reference/auditsphere-r2r-reference-baseline-architecture-and-adrs.md)
- [Audit Workflow Traceability Ledger](../../tracking/auditsphere-audit-tracker-workflow-traceability.md)
- [Preserved Audit Workflow Source](../../source/auditsphere-audit-source-workflow-gap-closure-user-stories-historical.md)

## Sequential work

1. Build external confirmation registry covering Bank, Receivables, Payables, Debt, and Legal confirmations.
2. Record confirmation lifecycle: preparation, client authorization, dispatch date, receipt date, status (`PREPARED`, `SENT`, `RECEIVED`, `NON_RESPONSE`).
3. Reconcile confirmed balance against client ledger balance; calculate difference and record explanation/evidence.
4. Enforce mandatory alternative procedures documentation for unconfirmed accounts before procedure sign-off.
5. Link confirmation exceptions directly to audit difference evaluation (SAD) and ReviewPoints.

## 1. Domain Modeling (`.Domain`)

Confirmation models: `ConfirmationBatch`, `ConfirmationRequest`, `ConfirmationResponse`, `AlternativeProcedureRecord` with variance invariants.

## 2. Persistence & Migrations (`.Infrastructure`)

Confirmation tables with third-party details, contact info, response document hash references, and audit-senior review sign-offs.

## 3. Application service and API contracts (`.Application` / `.Api`)

Keep the existing static capability-service pattern and stable API contracts; do not introduce a mediator layer. Existing paths include batch creation, dispatch evidence, response review and alternative-procedure recording. Extend those contracts only for the residual criteria above.

## 4. Inter-Module Lineage & Boundaries

Consumes sample selections (T058); feeds Cash & Bank (T061), Receivables (T062), Payables (T065), and Loans (T069).

## 5. Angular UI (`.Ui`)

Continue the Angular confirmation workspace under `/ui/app/audit/confirmations`, using bounded decoders, server-computed capability flags, explicit draft recovery, revision fencing and session-revocation clearing. Add response aging and reconciliation detail only with corresponding authorized API projections.

## 6. Edge Cases, Security & Verification

- Enforce scope-checked authorization (`RoleGrant` with `ENGAGEMENT` scope).
- Prevent data leakage between unrelated client engagements.
- Preserve immutable audit evidence; changes require new revisions.
- Software enforces calculations and rules; human practitioners make professional audit conclusions.

## Audit workflow source traceability

| Source | Coverage |
|---|---|
| AS-AUD-006 | Primary |
| AS-AUD-006-AC01 | Covered |
| AS-AUD-006-AC02 | Covered |
| AS-AUD-006-AC03 | Covered |
| AS-AUD-006-AC04 | Covered |
| AS-AUD-006-AC05 | Covered |
| AS-AUD-006-AC06 | Covered |
| AS-AUD-006-AC07 | Covered |
| AS-AUD-006-AC08 | Covered |
| AS-AUD-006-AC09 | Covered |

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
