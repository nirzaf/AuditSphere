# AuditSphereOps — implementation in progress

The build contract is `AuditSphereOps_NET_Codex_Implementation_Specification.md` (v5.0) at repo root. The v5 specification already requires .NET 10 (SDK 10.0.300) and PostgreSQL 18; an earlier note claiming .NET 10 was a deviation was incorrect. The local server is now PostgreSQL 18.6, matching the spec's required major version.

## Verified locally (as of 2026-09-17)

- SDK 10.0.300; six projects targeting net10.0 (five application projects, one test project).
- EF Core 10.0.12, Npgsql EF provider 10.0.0, `dotnet-ef` tool 10.0.12.
- PostgreSQL 18.6 development cluster at `C:\Users\DELL\.pgsql18`, loopback + trust auth (development only), port 5433. The former 16.8 cluster is stopped and untouched at `C:\Users\DELL\.pgsql16` for rollback; its data was migrated via `pg_dump`/`pg_restore`, not an in-place binary upgrade.
- Applied migrations on `auditsphere`: `20260917073959_InitialCreate` and `20260917075145_TrialBalanceValidation` (42 public tables, `numeric(19,6)` preserved).
- Five arithmetic unit tests. The AJ test illustrates double application; it does **not** enforce persisted journal idempotency or source reflection.
- One PostgreSQL integration test (AT-07 slice): seed an unbalanced raw TB, invoke the actual worker processing method, read rejection through a fresh `DbContext`, verify unchanged source amounts, and confirm a second invocation does not reprocess a terminal dataset.
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

## Validation worker

The worker processes pending raw datasets in one database transaction, records `Accepted` or `Rejected`, and preserves rows. `Accepted` means only the implemented basic validation — never professional approval or release readiness. It requires `ConnectionStrings__AuditSphere` and a Development/Test host environment; apply migrations to the target database first (web startup never applies them).

## Database scripts

`scripts/db/pg-common.ps1` (shared settings + helpers), `status.ps1`, `start.ps1` (idempotent), `stop.ps1`, `init-cluster.ps1`. They refuse a wrong major version, pass SQL through a temp file (PowerShell 5.1 strips quoted identifiers from native arguments), and require `-ExecutionPolicy Bypass` per invocation on this workstation.

## Not complete or production-enabled

The web host exposes status/health endpoints only — no lifecycle screens or Entra authorization. The schema has no foreign keys yet; only one check constraint and scoped unique indexes exist. The worker uses a table-level lock and loads one TB into memory; not workload-tested. Row immutability after validation, import handoff, release gates, durable external operations, source reflection, billing/ledger workflows, recovery and tenant proof remain unfinished. Health checks prove connectivity only.

No remote, issue, PR, independent review, merge or deployment has been verified. Handoff pointer: `docs/execution/status.json`.
