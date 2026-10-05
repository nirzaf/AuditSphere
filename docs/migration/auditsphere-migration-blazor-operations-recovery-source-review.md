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
- The focused Angular unit suite passed **4/4**, including signed-in staff
  administrator-access denial, the 10/25/50 row counts, page transitions and
  table semantics. Full Angular CI passed
  **489/489** across 94 test files. The production Angular build passed at
  349.67 kB raw initial size; it retains the existing 7.53 kB commercial
  settings stylesheet warning against the 4 kB warning budget.
- The PostgreSQL-backed API-host browser cohort passed **2/2**:
  `AngularOperationsRecoveryJourneyTests` and
  `AngularFirmScopeRevocationParityJourneyTests`. The new journey verified
  26-row paging, a 390px no-overflow viewport, redaction of request bytes,
  administrator cancel and re-arm actions, exact persisted states and
  append-only actor events, plus a 403 and no operation data for ordinary
  staff. No worker ran in this disposable fixture.
- The built-in Development browser loaded `/ui/app/operations` read-only. Its
  signed-in staff identity received a clear explanation that firm-wide
  AuditSphere Administrator access is required, with a safe next step to
  switch accounts or ask an administrator to review access. No operation data
  or commands were exposed and no business action was submitted. The
  authorized interactive action path was verified against the isolated
  PostgreSQL browser fixture above.

## Remaining gaps

This source row remains `PARTIAL`. The local slice does not close the complete
expired-grant, cross-firm, concurrent command, unknown transport outcome,
quarantine restart approval, operational rollback, or assistive-technology
acceptance matrices. The broader AS-PAR-002 retry/idempotency and authorization
crosswalk remains open. No Application, Domain, Infrastructure, API or EF
model behavior changed in this slice. The full solution regression and EF
pending-model check were not rerun; the latest complete solution checkpoint
remains the earlier **1001/1001** result at `ead85032`. The rollback Web host
and Blazor sources remain required until the source/action and external
retirement gates pass.
