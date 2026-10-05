# Blazor Operations Recovery Source Review

**Status:** PARTIAL
**Legacy source SHA-256:** `a11b6ab3c0f7c57d618d8ed8ae1a249d055364c49834cc34ab61fc1875815b9d`

## Source and replacement

The reviewed source is
`src/AuditSphereOps.Web/Components/Pages/Operations.razor`, route
`/app/operations`. Its Angular replacement is `/app/operations` and
`/ui/app/operations` in `src/AuditSphereOps.Ui/src/app/features/admin/operations.ts`.
The same-origin API projection and commands are in
`src/AuditSphereOps.Api/Ui/UiEndpoints.Operations.cs`; firm-wide administrator
authorization, bounded redacted listing, cancellation, retry and quarantine
recovery remain owned by Application `OperationRecoveryService`.

## Behavior and parity evidence

- Both interfaces expose the current firm's operating mode and latest bounded
  operation list, redact request payloads and bytes, and show counts for the
  returned window rather than implying total backlog or worker health.
- Retry actions are restricted to the Application-provided retryable-state
  set. Cancellation is available only for queued states and requires a reason;
  the service refuses an active lease and records append-only actor evidence.
  Quarantine lifting remains visible only in quarantine and is guarded by the
  existing Application recovery policy.
- Angular now restores the legacy table's 10/25/50 page-size choices and
  previous/next navigation. The operations table has a caption and scoped
  column headers; the cancellation reason has an explicit associated label.
- If a retry POST is accepted but its browser acknowledgement is lost, the UI
  refreshes the exact operation projection and reconciles `RETRY_WAIT` as
  confirmed. It does not resend the command. Other mutation controls remain
  disabled while the outcome is unresolved; refresh remains available.
- The focused Angular unit suite passed **5/5**, including signed-in staff
  administrator-access denial, 10/25/50 paging, table semantics, and retry
  response-loss reconciliation. Full Angular CI passed **493/493** across 95
  test files. The production Angular build passed at
  349.67 kB raw initial size; it retains the existing 7.53 kB commercial
  settings stylesheet warning against the 4 kB warning budget.
- The PostgreSQL-backed API-host browser cohort passed **2/2**:
  `AngularOperationsRecoveryJourneyTests` and
  `AngularFirmScopeRevocationParityJourneyTests`. It verifies 26-row paging,
  no horizontal overflow or clipped header/search text at 1141, 1024, 700 and
  390px, request-byte redaction, administrator cancellation, and a retry whose
  successful server response is dropped. The refreshed row confirms
  `RETRY_WAIT`; the browser sends one POST and PostgreSQL contains exactly one
  actor-attributed retry event. Ordinary staff receives 403 with no operation
  data. At test commit `f8c702f1`, a focused rerun passed 1/1 in 32s with an
  assertion for the exact firm-wide Administrator denial alert as well as the
  existing no-data and 403 checks. No worker ran in the disposable fixture.
- The built-in Development browser loaded `/ui/app/operations` read-only. Its
  signed-in Staff preview displays: “Operations is limited to firm-wide
  AuditSphere Administrators. Your current account does not have that access;
  switch to an authorized administrator account or ask a firm administrator
  to review your access.” No operation rows or commands were exposed and no
  business action was submitted. The authorized interactive action path was
  verified against the isolated PostgreSQL browser fixture above.

## Remaining gaps

This source row remains `PARTIAL`. The local slice does not close the complete
expired-grant, cross-firm, concurrent command, non-accepted/ambiguous retry,
quarantine restart approval, operational rollback, or assistive-technology
acceptance matrices. The broader AS-PAR-002 retry/idempotency and authorization
crosswalk remains open. No Application, Domain, Infrastructure, API or EF
model behavior changed in this slice. The full solution regression and EF
pending-model check were not rerun; the latest complete solution checkpoint
remains the earlier **1001/1001** result at `ead85032`. The rollback Web host
and Blazor sources remain required until the source/action and external
retirement gates pass.
