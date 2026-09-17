# AuditSphereOps

.NET 10 modular monolith for audit, accounting & assurance operations (Blazor Interactive Server + EF Core 10 + PostgreSQL 18.6 + Entra/Graph/SharePoint/Purview as external managed dependencies).

> Authoritative build contract: `AuditSphereOps_NET_Codex_Implementation_Specification.md` (v5.0) at repo root.
> Implementation status and tested commands: `docs/execution/current-slice.md`.

## Solution

- `AuditSphereOps.slnx` — .NET 10 (`global.json` 10.0.300)
- `src/AuditSphereOps.Domain` — entities: Security, Practice (CRM/time/billing/firm ledger), Acceptance, Engagements, Documents, Accounting, Audit, Reviews, Completion, Records
- `src/AuditSphereOps.Application` — `ActorContext`, scope/role authorization, TB calculator (Appendix D fixture)
- `src/AuditSphereOps.Infrastructure` — `AuditSphereDbContext` (snake_case, `numeric(19,6)`) + `Persistence/Migrations`
- `src/AuditSphereOps.Web` — Blazor Interactive Server host; `/health/live`, `/health/ready` (Npgsql probe)
- `src/AuditSphereOps.Worker` — durable-operation roles (general/processing/records)
- `tests/AuditSphereOps.Domain.Tests` — arithmetic unit tests + one PostgreSQL integration test

## Local database (no admin, Docker-free)

Development cluster: PostgreSQL **18.6**, user-local at `C:\Users\DELL\.pgsql18`, loopback only, trust auth (development only, never production), port **5433**.

```powershell
# Execution policy requires the bypass flag on this workstation (per invocation, not a system change).
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\db\status.ps1   # version + databases + migrations
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\db\start.ps1    # idempotent start + ensure databases
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\db\stop.ps1     # fast shutdown
```

Override the defaults with `PGSQL_HOME`, `PGSQL_DATA`, `PGSQL_RUN`, `PGSQL_LOG`, `PGSQL_PORT`.

## Build, migrate, test

```powershell
& 'C:\Users\DELL\.dotnet\dotnet.exe' tool restore
& 'C:\Users\DELL\.dotnet\dotnet.exe' build AuditSphereOps.slnx
& 'C:\Users\DELL\.dotnet\dotnet.exe' ef database update --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web
& 'C:\Users\DELL\.dotnet\dotnet.exe' test AuditSphereOps.slnx
```

Applied migrations: `20260917073959_InitialCreate`, `20260917075145_TrialBalanceValidation` (42 public tables).
External effects are fenced locally (`ExternalEffects.Enabled=false`): Entra tenant, selected SharePoint grants and the Purview records profile remain **blocked** until owner evidence — never fake-passed.
