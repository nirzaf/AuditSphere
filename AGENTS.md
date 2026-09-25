# AuditSphereOps Agent Instructions







Short pointer; full authoritative contracts live in `docs/auditsphere-requirements-system-specification-current.md` (v5.0) and `docs/auditsphere-accounting-module-requirements-current.md`. Do not paste the full specification into this file.







---







## 1. System Objectives & Architecture







AuditSphereOps is an audit, accounting, and assurance operations platform built as an ASP.NET Core modular monolith across five projects:



- **`AuditSphereOps.Domain/`**: Pure business models and invariants grouped by business capability (`Practice/`, `Accounting/` including consolidation and FX domain records, `Audit/`, `Reviews/`, `Completion/`, `Documents/`, `Security/`, etc.).



- **`AuditSphereOps.Application/`**: Capability services, queries, deterministic calculators, and durable-operation contracts/orchestrators.



- **`AuditSphereOps.Infrastructure/`**: EF Core 10, Npgsql 10, PostgreSQL migrations, durable-operation storage, and provider adapters.



- **`AuditSphereOps.Web/`**: Blazor Web App (Interactive Server), staff workbenches (`/app/accounting`, `/app/consolidation`, etc.), and restricted client portal (`/portal`).



- **`AuditSphereOps.Worker/`**: BackgroundService host executing durable operations (general, processing, records).







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



  - Production external gates (Entra OIDC, selected-resource SharePoint/Graph and any separately approved live release checkpoint) require live infrastructure; record them as `BLOCKED_EXTERNAL`, never fake pass. Purview and eSignature provider integrations are out of product scope: preserve exact uploaded signed-document evidence, SHA-256 identities, human decisions and release manifests without claiming provider acceptance.







---







## 3. Core Invariants & Engineering Rules







- **Scope-Checked Authorization:** Every command, query, and queue must enforce explicit `RoleGrant` scope (`FIRM_WIDE`, `CLIENT`, `ENGAGEMENT`, or `GROUP`). An engagement-only grant must never widen to sibling engagements or clients.



- **Immutability & Lineage:** Sealed datasets, approved mappings, applied journals, issued packages, and review decisions are append-only. Changes require new revisions/amendments; never overwrite historical evidence.



- **Pure Calculators:** Keep financial and consolidation calculation engines free of EF Core, network calls, system clocks, and non-deterministic IDs.



- **Durable Operations:** Use the local durable operation infrastructure for long-running work (GL completeness, financial package calculation, rendering) with source/mapping revision fencing, idempotent retries, and explicit cancellation dispositions.



- **Fail-Closed Methodology:** Missing exchange rates, unsupported valuation methods, unapproved perimeters, or stale source inputs must fail closed and block approval/release. Never default to zero or arbitrary values.



- **No Autonomous Audit Opinions:** The platform computes differences, evaluates risk indicators, and enforces gates; human practitioners make professional conclusions and sign-offs.



- **Code Map & Documentation Authority:** Before changing a business capability, consult `docs/architecture/auditsphere-architecture-code-map.md`; `docs/architecture/auditsphere-architecture-current-architecture.md` is the implementation-authority companion to this file. Preserved requirement/blueprint sources under `docs/task_breakdown/source/` are `HISTORICAL_SOURCE` and never implementation authority. Volatile project facts (test counts, verified SHA, migration count, blockers) live only in `docs/execution/status.json`.



- **Capability-Focused Files:** Large services and the `AuditSphereDbContext` are partial classes split into capability files (`ConsolidationService.<Capability>.cs`, `AuditSphereDbContext.<Module>.cs`, `ClientAccountingTests.<Capability>.cs`). Keep public APIs stable and put new operations in the matching capability file; use business-semantic file names.



- **Web Composes Application:** Razor components compose Application commands/queries; do not add new business-state mutations directly through `DbContext`. When substantially modifying an existing page, move complex reads or business operations into a named Application query/service if that reduces page responsibility. Do not bulk-refactor unaffected pages.







---







## 4. Absolute Prohibitions ("Never" Rules)







- **Never** introduce Frappe, ERPNext, a Python backend, a React frontend, or a second ERP.



- **Never** add Kubernetes, microservices-per-module, message brokers (Kafka/RabbitMQ), or autonomous application-level AI decision-makers.



- **Never** request or use tenant-wide Microsoft Graph scopes; use selected-resource permissions only.



- **Never** make autonomous professional audit conclusions or bypass required human review gates.



- **Never** perform destructive git rewrites or commit sensitive credentials/tenant secrets.







---







## 5. Development & Verification Workflow







Per change: deliver the smallest coherent vertical slice with guarded transactions and scope-checked authorization. Update `docs/execution/status.json` and `docs/execution/auditsphere-execution-current-slice.md` only with observed facts.







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







- **Documentation Navigation:** Before broad documentation or requirements work, read [`docs/auditsphere-docs-index.md`](docs/auditsphere-docs-index.md) for the central directory, authority hierarchy, and reading order.



- **Authoritative System Spec:** `docs/auditsphere-requirements-system-specification-current.md` (v5.0 build contract). Read intro + §§1–12, 22, 24, 27–33, 41–47 first; then specific sections for the active issue.



- **Accounting & Consolidation Roadmap:** `docs/auditsphere-accounting-module-requirements-current.md` (detailed gap analysis, AC-01 to AC-28 user stories, and implementation roadmap).



- **Execution Ledger:** `docs/execution/status.json` & `docs/execution/auditsphere-execution-current-slice.md` (pointers to active slice, local verified evidence, and external blockers; re-read repo reality on resume).







---







## 7. GitHub Wiki Deployment Guidelines







### One guide, updated only when necessary







- Use the [repository GitHub Wiki](https://github.com/nirzaf/AuditSphere/wiki) for operator-facing deployment guidance. Read its current index and relevant pages before editing; update the existing canonical page in place. Create a page only for a genuinely missing topic, then link it from the existing index. Do not create per-release copies or duplicate instructions across Wiki pages, repository docs, or `AGENTS.md`; link to the authoritative detail instead.



- Check documentation impact when prerequisites, configuration keys/defaults, deployment commands, migrations, setup screens, permissions, verification, backup/recovery, or upgrade procedures change. Update only affected sections when the existing guidance becomes inaccurate or incomplete. If it remains correct, make no Wiki edit; avoid cosmetic rewrites, timestamp-only changes, and repeated changelog entries.



- Verify guidance against the current checkout, especially `docs/auditsphere-requirements-system-specification-current.md` §§30 and 45, `src/AuditSphereOps.Web/appsettings.json`, `src/AuditSphereOps.Web/Components/Pages/Microsoft365Setup.razor`, and the actual startup, worker, and deployment scripts. `docs/auditsphere-m365-onboarding-user-stories.md` describes proposed requirements: check implementation before presenting any step as available. Link to the applicable source revision; do not copy the specification or configuration files wholesale.







### Make deployment easy to follow







- Use plain language and short numbered steps. Start with prerequisites, supported OS/hosting profile, required access, and a clear distinction between local simulation and production. Give each step an action, where to run it, its expected result, and what to do if it fails; mark optional and administrator-only steps explicitly.



- Cover the shortest supported path: toolchain and PostgreSQL setup → safe configuration → restore/build → approved database migration → web/worker startup → health and sign-in checks → Microsoft 365 setup and capability verification. Link to focused troubleshooting, upgrades, credential rotation, backup/restore, and rollback guidance. Use only existing, verified commands with explicit working directory, shell, and target environment; label untested steps and missing capabilities honestly.



- Keep local trust authentication, simulation adapters, and development defaults out of production instructions. Preserve selected-resource permissions, credential separation, release/records checks, and recovery fences. Never recommend bypassing a startup guard or merely toggling external effects to claim readiness. A saved setup draft is not Microsoft consent, verified SharePoint access, or production acceptance; missing live prerequisites remain `BLOCKED_EXTERNAL`.







### Never publish sensitive information







- Treat Wiki text, history, attachments, screenshots, links, and examples as potentially public. Never include passwords, client secrets, access/refresh tokens, bootstrap proofs or hashes, private keys, connection strings containing credentials, signed/preauthenticated URLs, production configuration exports, client/financial data, or personal information. Replace deployment-specific tenant/app/user/site IDs, private hostnames, and resource locations with descriptive placeholders even when they are not authentication secrets.



- Show configuration key names and obvious placeholders such as `<TENANT_ID>` and `<SECRET_FROM_APPROVED_STORE>`, not real values. Explain how the operator supplies values privately through .NET user secrets for development or an approved production secret store/role-specific mount. Do not instruct users to paste secrets into the Wiki, Git, issues, screenshots, shared logs, or literal shell commands that retain them in history.



- Before publication, inspect the complete changed content, examples, URLs, and attachments for sensitive material and validate links and command accuracy. Use available secret scanning as an additional check, not a guarantee. If an exposure is found, stop publication and notify the owner privately without repeating the value; removal from the current page does not remove history, and exposed credentials require revocation/rotation through the approved incident process.







### Publishing and evidence







- Wiki changes are separate from the main repository; editing local documentation does not publish a Wiki update. Publish only within explicit owner authorization using the existing approved access method, with a minimal diff and a concise reason. Do not change Wiki visibility, permissions, or repository protections to obtain access.



- If Wiki access or publication authority is unavailable, report the affected topic and blocker without claiming it was updated. After an authorized update, verify the rendered page and navigation; report the actual page URL, material change, and checks performed. Record a tested version/date only when supported by new evidence, and distinguish local verification from live deployment acceptance.
