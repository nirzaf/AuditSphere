# AuditSphereOps — implementation in progress

The build contract is `AuditSphereOps_NET_Codex_Implementation_Specification.md` (v5.0) at repo root. The v5 specification already requires .NET 10 (SDK 10.0.300) and PostgreSQL 18; an earlier note claiming .NET 10 was a deviation was incorrect. The local server is now PostgreSQL 18.6, matching the spec's required major version.

## Verified locally (as of 2026-09-17)

- SDK 10.0.300; six projects targeting net10.0 (five application projects, one test project).
- EF Core 10.0.12, Npgsql EF provider 10.0.0, `dotnet-ef` tool 10.0.12.
- PostgreSQL 18.6 development cluster at `C:\Users\DELL\.pgsql18`, loopback + trust auth (development only), port 5433. The former 16.8 cluster is stopped and untouched at `C:\Users\DELL\.pgsql16` for rollback; its data was migrated via `pg_dump`/`pg_restore`, not an in-place binary upgrade.
- Four applied migrations on `auditsphere`: `InitialCreate`, `TrialBalanceValidation`, `AccountingIntegrity`, and `20260917104422_DurableOutbox`. Verified 46 public tables, zero durable-operation rows, and unchanged `numeric(19,6)` TB amounts after this local migration.
- Build: zero warnings/errors. Full suite: 40/40 passed, zero skipped; PostgreSQL profile: 29/29. The original 12 regressions remain, including five arithmetic tests; the AJ arithmetic test still does **not** establish persisted source reflection.
- Outbox tests cover concurrent enqueue/claim, locked-row skipping, scoped idempotency conflicts, atomic rollback, lease renewal/expiry, stale attempts, changed input/epoch, cancellation, retry exhaustion, append-only evidence, migration safety, and simulated provider-success/local-failure reconciliation. The provider fixture persists effects independently; it is not a live Microsoft adapter.
- Database-level control probe on 18.6: inserting an unbalanced dataset with `validation_status='Accepted'` is rejected by `ck_tb_validation_status`; a balanced insert is accepted; no residue after rollback.

## Run verification

```powershell
Set-Location 'C:\Users\DELL\repos\AuditSphere'
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\db\status.ps1
& 'C:\Users\DELL\.dotnet\dotnet.exe' tool restore
& 'C:\Users\DELL\.dotnet\dotnet.exe' build AuditSphereOps.slnx
& 'C:\Users\DELL\.dotnet\dotnet.exe' test AuditSphereOps.slnx
```

The database test requires PostgreSQL on 127.0.0.1:5433 and database `auditsphere_tests`; supply `AUDITSPHERE_TEST_CONNECTION` if credentials differ. It refuses other database/host names, creates a random schema, migrates it, and removes only that schema. A missing or wrong-version server fails the test — never an InMemory fallback.

## Durable outbox and validation worker

Checklist #3 is implemented, locally verified, committed as `cc6384c` and pushed to `origin/master` (private `nirzaf/AuditSphere`). The existing incomplete worker refactor was completed; `Worker` delegates to Application services through the Infrastructure persistence implementation. Pending raw datasets are discovered in batches of 25 and enqueued with a deterministic dataset/revision key. `ValidateTrialBalance.v1` is the only shipped handler: its validation result, audit event, and operation completion commit together. `Accepted` means basic validation, never professional approval or release readiness.

Startup requires `ConnectionStrings__AuditSphere`, `Worker__FirmId`, a Development/Test environment, and `ExternalEffects__Enabled=false`. `Worker__DeploymentEpoch` defaults to 1 and must match the firm's local safety state. Firm/client guards must exist; the migration provisions them for existing clients, and fixture creation provisions them atomically with new test clients. No worker creates missing guards opportunistically. No live provider is registered; test-only simulation handlers require Test plus explicit simulation enablement.

Claiming uses a short PostgreSQL `SKIP LOCKED` transaction. Defaults are a 60-second lease, renewal every 20 seconds, five execution attempts, and persisted exponential retry starting at 5 seconds, capped at 5 minutes before honoring any longer provider delay. Owner/token/state/unexpired-lease checks fence every publication; a new database-time check after waiting for a row lock prevents reviving an expired lease. Firm → client → dataset → operation is the local publication lock order.

Expired pre-effect claims can retry; started/unknown outcomes become `RESULT_UNCERTAIN`. A separately leased reconciliation reads the same deterministic target and verifies its digest. Missing/mismatched evidence blocks completion. Cancellation after an effect begins preserves uncertainty; a local token is not an exactly-once provider guarantee. Blocked/dead-letter operations remain visible in persistence and are not automatically reset; an authorized operator recovery UI is not part of this slice.

### Persistence dictionary for this slice

- `durable_operations`: UUID operation/firm/target/correlation IDs; optional client/engagement/originator IDs; immutable kind/schema/group/mode/authority, target revision (`bigint`), key (`varchar(200)`), bounded typed JSON, versioned length-prefixed request bytes (`bytea`) and SHA-256 hex digest (`varchar(64)`). This encoding is not the future approval-manifest canonicalization format.
- Mutable operation projection: explicit string state; monotonic `attempt_token` (`bigint`) and `attempt_count` (`integer`); UTC next-attempt/lease/completion times; nullable lease owner/result identity/result digest/error code; claimed epoch and reconciliation flag. Completed results and request identity cannot be rewritten.
- Named uniqueness: `ux_operation_firm_key`; engagement FK includes firm/client/engagement, with restrictive deletes. CHECK constraints enforce state, counters, scope, lease shape, and required completion evidence. Indexes support firm/group/state/due-time claims.
- `operation_attempts`: append-only UUID, operation FK, unique operation/token, owner, reconciliation flag, and UTC claim time. `operation_events`: append-only UUID, operation FK/token, stage/action kind, executor, UTC timestamp; originating actor and correlation remain on the immutable request. No credentials or provider exception text are recorded.
- `firm_safety_states`: firm UUID, `LOCAL_ONLY` or `RECOVERY_QUARANTINE`, positive deployment epoch. `client_safety_states`: client UUID, matching firm/client FK, positive input generation. These are coordination records, not completed human authorization or release-gate implementations.
- Request/attempt/event evidence has no automatic expiry/deletion path. Runtime SQL paths are confined to the operation store and typed validation handler; dedicated least-privilege database-role provisioning remains future hardening.

### Migration and recovery boundary

`DurableOutbox` upgrades only an empty legacy operation table; nonempty legacy history stops the migration before modification. Tests cover both previous-schema accounting preservation and this refusal. Downgrade refuses to discard operation evidence; a populated installation needs coordinated recovery, not an old binary against the new schema. Existing monetary precision and accounting migrations are unchanged. Apply migrations explicitly; web startup never applies them.

## Database scripts

`scripts/db/pg-common.ps1` (shared settings + helpers), `status.ps1`, `start.ps1` (idempotent), `stop.ps1`, `init-cluster.ps1`. They refuse a wrong major version, pass SQL through a temp file (PowerShell 5.1 strips quoted identifiers from native arguments), and require `-ExecutionPolicy Bypass` per invocation on this workstation.

## Not complete or production-enabled

The web host still exposes status/health endpoints only; human command/session authorization, CSV intake, source reflection, billing/ledger, lifecycle screens, release gates, real provider adapters, and full cross-store recovery remain unfinished. Accounting and outbox scoped FKs/checks and append-only triggers now exist; remaining module chains still need integrity work. TB rows reject UPDATE/DELETE, but this slice does not complete the future promoted-dataset insert freeze/import lifecycle. Validation retains a table-level row-writer lock and loads one TB into memory; no workload claim is made. Health checks still prove connectivity only. Checklist #4–14 were not started.

HEAD is `cc6384c87337f17048cc244fa2ca3512de015a5e` on `master`, in sync with `origin/master` (private `nirzaf/AuditSphere`). No PR, independent review, merge, production deployment, or live-provider action was performed. Only the known local development database was migrated. Next step: checklist #4 (synthetic identity + command-enforced authorization). Handoff pointer: `docs/execution/status.json`.
