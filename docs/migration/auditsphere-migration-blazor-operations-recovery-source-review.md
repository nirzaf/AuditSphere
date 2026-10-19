# Blazor Operations Recovery Source Review

**Status:** PARTIAL
**Legacy source SHA-256:** `a11b6ab3c0f7c57d618d8ed8ae1a249d055364c49834cc34ab61fc1875815b9d`

> Blazor references and retirement conditions below preserve the earlier review history. The current application is Angular/API; those statements do not authorize restoring the retired host. Current source evidence and remaining acceptance gates are in `docs/execution/status.json`.

## Concurrent exact cancellation follow-up

The PostgreSQL regression `OperatorCommandReceiptTests.ConcurrentCancelWithSameRequestWaitsAndPublishesOneExactReceipt` observes the second backend blocked by the first publication, then verifies identical retained receipts and one cancellation event/transition. This is focused same-request cancellation evidence only; other action/interleaving matrices and external operational drills remain open. Exact source, results and artifact paths are in `targetedVerification.operatorReceiptConcurrency20261019` in the execution ledger.

## Source and replacement

The reviewed source is
[src/AuditSphereOps.Web/Components/Pages/Operations.razor](https://github.com/nirzaf/AuditSphere/blob/c3905e078b44c84c5ec0ad4bbda6d11894acf42d/src/AuditSphereOps.Web/Components/Pages/Operations.razor), route
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
  no horizontal overflow or clipped header at 1141, 1024, 700 and
  390px, request-byte redaction, administrator cancellation, and a retry whose
  successful server response is dropped. The refreshed row confirms
  `RETRY_WAIT`; the browser sends one POST and PostgreSQL contains exactly one
  actor-attributed retry event. Ordinary staff receives 403 with no operation
  data. At test commit `f8c702f1`, a focused rerun passed 1/1 in 32s with an
  assertion for the exact firm-wide Administrator denial alert as well as the
  existing no-data and 403 checks. No worker ran in the disposable fixture.
- At code commit `cb4efe8f`, the focused API-host browser cohort passed 2/2,
  including expired Administrator grant handling under concurrent Angular
  requests. A stale session receives a safe 401/409 with no operation data;
  PostgreSQL confirms one revocation, one session-epoch increment, and one
  system-attributed expiry evidence row. The pending operation remains intact.
  `RoleAssignmentTests` and `OperationRecoveryTests` passed 16/16.
- At code commit `f7bc5e4e`, the focused Operations browser cohort passed 3/3
  in 56s. It verifies a current-firm Administrator cannot list, retry or cancel
  a foreign-firm operation by guessed ID: reads omit the foreign ID, kind and
  request bytes; both commands return 403 `scope.denied`; and PostgreSQL confirms
  the operation and event history are unchanged. The same cohort covers Staff
  denial and concurrent expiry of an Administrator grant with one event and one
  session-epoch increment.
- The built-in Development browser loaded `/ui/app/operations` read-only. Its
  signed-in Staff preview displays: “Operations is limited to firm-wide
  AuditSphere Administrators. Your current account does not have that access;
  switch to an authorized administrator account or ask a firm administrator
  to review your access.” No operation rows or commands were exposed and no
  business action was submitted. The authorized interactive action path was
  verified against the isolated PostgreSQL browser fixture above.

- At code commit `3a4acec2`, `AngularOperationsRecoveryJourneyTests` passed
  **1/1** in 56 seconds. The responsive assertion now distinguishes intentional
  screen-reader-only search guidance from visible header content. It checks the
  search input's accessible description and guidance text, and preserves page
  overflow and header bounds checks at 1141, 1024, 700 and 390px. The same journey
  retains operation paging, redaction, cancellation and retry assertions.
  Command: `dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj --configuration Release --maxcpucount:1 --filter 'FullyQualifiedName~AngularOperationsRecoveryJourneyTests' --logger 'trx;LogFileName=operations-accessible-search-repair.trx' --results-directory /tmp/auditsphere-parity-regression`.
  Evidence: `/tmp/auditsphere-parity-regression/operations-accessible-search-repair.trx`
  (local ephemeral report).

## Remaining gaps

This source row remains `PARTIAL`. Expired Administrator grant revocation and foreign-firm operation list/retry/cancel denial now have focused browser regressions. Other cross-firm capability boundaries, concurrent command, non-accepted/ambiguous retry, quarantine restart approval, operational rollback, and assistive-technology acceptance matrices remain open. The broader AS-PAR-002 retry/idempotency and authorization crosswalk remains open. This slice changed Application `RoleGrantExpiry` transaction handling; it did not change Domain models, API endpoints, EF mappings or schema. The full solution regression remains open. The historical **1001/1001** checkpoint at `ead85032` does not establish current-tree readiness: the later Domain run at `b410768b` recorded 1046 passed and 13 failed, followed by a focused receivables repair. See `docs/execution/status.json` for current regression evidence. This Operations rerun does not close those failures or the full-solution gate. The rollback Web host and Blazor sources remain required until the source/action and external retirement gates pass.

## Current safety findings under correction

At `14ae1025`, the source actions are represented, but operator recovery needs
additional backend fences: rearming uncertain or reconciliation-blocked work
must preserve reconciliation rather than execute an external effect again;
commands must reauthorize after lock waits; quarantine restart/lift must bind to
the current recovery session and external epoch; read projections need coherent
mode and final Administrator checks. Queue status alone cannot acknowledge an
original operator request. Corrections and targeted regressions are in progress;
live provider acceptance remains separate and this source stays PARTIAL.

## Original operator acknowledgement slice

The next additive command contract covers retry, queued cancellation, quarantine
lift and recovery restart approval. A preview binds the exact operation attempt,
state and reconciliation requirement, firm mode/epochs and current recovery
session to the acting Administrator/session. Fresh assent authorizes only that
operator transition. An immutable receipt commits with the transition and retains
its before/after evidence, intent hash and persisted timestamp; later worker
advancement cannot rewrite it. Receipt recovery rechecks current authority and
waits behind publication. This is an operator-transition acknowledgement, never
a provider-delivery or professional acceptance receipt. Existing internal callers
retain their guarded compatibility paths.

### Local verification — 2026-10-16

The candidate tree based on `5f8f374b` passed the following focused checks against
synthetic, disposable PostgreSQL fixtures:

- Release build: 0 warnings, 0 errors.
- `OperatorCommandReceiptTests`: 18/18 passed.
- `OperationRecoverySafetyApiTests`: 7/7 passed, including refusal of legacy
  aliases without reviewed assent and immutable evidence-table guards.
- `AngularOperationsCommandJourneyTests`: 8/8 passed in Chromium at 390 px,
  covering retry, cancellation, quarantine lift and restart approval, including
  lost responses and original-receipt recovery.
- Angular CI specs: 1,283/1,283 passed across 160 files. Production build passed;
  the existing `commercial/settings.scss` warning budget is unchanged.
- EF model drift, OpenAPI drift, documentation, filename, narrative-metric,
  inventory and `git diff --check` gates passed.
- Independent security review found no release-blocking issue.

The migration `20261016165646_OperatorCommandReceipts` was exercised only by the
owned test databases. It has not been applied to development, acceptance or live
databases. Commit-pinned GitHub release verification and deployment remain
pending. This closes only this operator-command source slice: the overall source
row remains `PARTIAL`, the Blazor retirement gate remains `NOT_READY`, and live
provider acceptance is not claimed.
