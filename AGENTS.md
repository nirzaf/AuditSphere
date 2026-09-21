# AuditSphereOps Agent Instructions

Short pointer; full authoritative contracts live in `docs/SPECIFICATION.md` (v5.0) and `docs/AuditSphere_Accounting_module.md`. Do not paste the full specification into this file.

---

## 1. System Objectives & Architecture

AuditSphereOps is an audit, accounting, and assurance operations platform built as an ASP.NET Core modular monolith:
- **`AuditSphereOps.Domain/`**: Pure business models and invariants (`Practice/`, `Accounting/`, `Consolidation/`, `Audit/`, `Reviews/`).
- **`AuditSphereOps.Application/`**: Pure deterministic calculators, commands, queries, and durable operation orchestrators.
- **`AuditSphereOps.Infrastructure/`**: EF Core 10, Npgsql, PostgreSQL migrations, local durable workers, and provider adapters.
- **`AuditSphereOps.Web/`**: Blazor Web App (Interactive Server), staff workbenches (`/app/accounting`, `/app/consolidation`, etc.), and restricted client portal (`/portal`).

### Three Strict Financial Boundaries
1. **Firm's Own Books (`Practice/FirmLedger`)**: Firm CRM, billing, time tracking, and firm financial ledger.
2. **Client Accounting Workspace (`Accounting/`)**: Client-owned source TB/GL, client charts of accounts, approved taxonomy mappings, reporting/audit adjustments, and entity financial packages.
3. **Group Consolidation Workspace (`Consolidation/`)**: Approved component packs, consolidation perimeter, currency translation, and elimination journals. **Never** mutates component client books.

> **Scope Boundary:** This is an import-first preparation, audit, and consolidation workspace, **not** an operational client ERP. Exclude client sales/purchase/inventory operations, payroll execution, and payment initiation.

---

## 2. Technology Stack & Local Environment

- **Runtime & Framework:** .NET 10 SDK (`dotnet`), ASP.NET Core Blazor Interactive Server, EF Core 10 + Npgsql 10.
- **Database:** PostgreSQL 18.6 (Docker-free local dev on port `5433`).
  - **macOS:** User-local Homebrew at `/opt/homebrew/var/postgresql@18`; run `scripts/db/restore-drill.sh` or `pg_ctl -D /opt/homebrew/var/postgresql@18 -o "-p 5433" start`.
  - **Windows:** Local binaries in `C:\Users\DELL\.pgsql18`; run `scripts\db\status.ps1`, `start.ps1`, `stop.ps1`, `init-cluster.ps1` with PowerShell.
- **Configuration & Toggles:**
  - Optional overrides: `AUDITSPHERE_TEST_CONNECTION` or `PGSQL_HOME`.
  - Local dev: `ExternalEffects.Enabled=false` and `AllowSimulationAdapters=true`.
  - Production external gates (Entra OIDC, SharePoint/Graph, Purview, cryptographic signing) require live infrastructure; record as `BLOCKED_EXTERNAL`, never fake pass.

---

## 3. Core Invariants & Engineering Rules

- **Scope-Checked Authorization:** Every command, query, and queue must enforce explicit `RoleGrant` scope (`FIRM_WIDE`, `CLIENT`, `ENGAGEMENT`, or `GROUP`). An engagement-only grant must never widen to sibling engagements or clients.
- **Immutability & Lineage:** Sealed datasets, approved mappings, applied journals, issued packages, and review decisions are append-only. Changes require new revisions/amendments; never overwrite historical evidence.
- **Pure Calculators:** Keep financial and consolidation calculation engines free of EF Core, network calls, system clocks, and non-deterministic IDs.
- **Durable Operations:** Use the local durable operation infrastructure for long-running work (GL completeness, financial package calculation, rendering) with source/mapping revision fencing, idempotent retries, and explicit cancellation dispositions.
- **Fail-Closed Methodology:** Missing exchange rates, unsupported valuation methods, unapproved perimeters, or stale source inputs must fail closed and block approval/release. Never default to zero or arbitrary values.
- **No Autonomous Audit Opinions:** The platform computes differences, evaluates risk indicators, and enforces gates; human practitioners make professional conclusions and sign-offs.

---

## 4. Absolute Prohibitions ("Never" Rules)

- **Never** introduce Frappe, ERPNext, a Python backend, a React frontend, or a second ERP.
- **Never** add Kubernetes, microservices-per-module, message brokers (Kafka/RabbitMQ), or autonomous application-level AI decision-makers.
- **Never** request or use tenant-wide Microsoft Graph scopes; use selected-resource permissions only.
- **Never** make autonomous professional audit conclusions or bypass required human review gates.
- **Never** perform destructive git rewrites or commit sensitive credentials/tenant secrets.

---

## 5. Development & Verification Workflow

Per change: deliver the smallest coherent vertical slice with guarded transactions and scope-checked authorization. Update `docs/execution/status.json` and `docs/execution/current-slice.md` only with observed facts.

### Standard Verification Sequence
```bash
# 1. Build solution (Release)
dotnet build src/AuditSphereOps.Web/AuditSphereOps.Web.csproj --no-restore --configuration Release

# 2. Run full test suite (PostgreSQL-backed)
dotnet test AuditSphereOps.slnx --no-build

# 3. Check for pending EF Core model changes
dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web

# 4. Optional: Run representative accounting benchmark
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore --filter 'FullyQualifiedName~AccountingBenchmarkTests'

# 5. Optional: Run database restore drill (macOS)
scripts/db/restore-drill.sh
```

---

## 6. Authoritative Reference Pointers

- **Authoritative System Spec:** `docs/SPECIFICATION.md` (v5.0 build contract). Read intro + §§1–12, 22, 24, 27–33, 41–47 first; then specific sections for the active issue.
- **Accounting & Consolidation Roadmap:** `docs/AuditSphere_Accounting_module.md` (detailed gap analysis, AC-01 to AC-28 user stories, and implementation roadmap).
- **Execution Ledger:** `docs/execution/status.json` & `docs/execution/current-slice.md` (pointers to active slice, local verified evidence, and external blockers; re-read repo reality on resume).

