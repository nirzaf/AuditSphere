# AuditSphereOps

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-18.6-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![EF Core](https://img.shields.io/badge/EF%20Core-10.0-0078D4?logo=nuget&logoColor=white)](https://learn.microsoft.com/ef/core/)
[![Blazor](https://img.shields.io/badge/Blazor-Interactive%20Server-512BD4?logo=blazor&logoColor=white)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
[![Tests](https://img.shields.io/badge/Tests-PostgreSQL--backed-brightgreen)](tests/)
[![Migrations](https://img.shields.io/badge/Migrations-EF%20Core-blue)](src/AuditSphereOps.Infrastructure/Persistence/Migrations/)
![License](https://img.shields.io/badge/License-Informational-lightgrey)

**AuditSphereOps** is a professional audit, accounting, and assurance operations platform — an enterprise **.NET 10 modular monolith** for accounting practices, CPA firms, audit engagements, and multi-entity group consolidation. It governs the full engagement lifecycle: lead qualification, proposals, client acceptance, practice time and billing, trial-balance/GL intake, financial statement production, audit fieldwork, review, controlled package signing/release, records retention, and multi-currency consolidation.

> **Status: Specification-Driven Modular Monolith.** Implemented and verified locally (per [`docs/execution/status.json`](docs/execution/status.json), the single authority for test counts, verified SHA, migration count, and external blockers, against PostgreSQL 18.6): core domain modules and practice management; period/book/basis-bound trial-balance intake with deterministic sealing, safe legacy period/chart backfill, and sealed GL source-bound schedule control totals; bounded resumable GL intake with service-date lineage and opening/movement completeness; client-scoped accounting dimensions, chart/taxonomy hierarchy controls, and QAR setup defaults; deterministic calculators and governed workflows (journal-risk indicators, exact-journal difference-impact classification, governed difference correction states, audit planning, bank reconciliation proofs, source-bound reconciliation-item aging, analytical-review replay lineage, valuation differences linked to scoped adjustments, typed specialist schedules with explicit asset-schedule methodology); package production and release control (exact-byte reviewed artifacts, deterministic formula-free XLSX/DOCX exports, package-bound release candidates, archive lineage, governed financial-package review decisions, authorized review queues, client-safe management views, durable GL/package processing, context-bound adjustment journals, queued-operation cancellation, recovery quarantine, provider safety fences, source-bound accounting-evidence freshness); versioned FX ranges and approved rate-rule enforcement with deterministic advanced-consolidation safety cores; stale-route re-authorization hardening across staff and client workbenches; a prototype-inspired Blazor UI modernization across the shared staff/client shell, MudBlazor route families and a whole-application responsive sweep with scoped staff navigation search that re-authorizes every hit against its destination route's decision; a source-backed administrator project progress tracker; guarded Microsoft tenant consent sessions; a least-privilege M365 tenant administration boundary; and bounded read-only Microsoft directory search. Production external gates (Entra OIDC, selected-resource SharePoint/Graph, live release checkpoint) require live infrastructure and remain `BLOCKED_EXTERNAL`, never fake-passed. Purview and eSignature provider integrations are out of product scope: exact uploaded signed-document evidence, SHA-256 identities, human decisions, and release manifests are preserved without claiming provider acceptance.

---

## Table of Contents

- [Project Objectives](#project-objectives)
- [Three Strict Financial Boundaries](#three-strict-financial-boundaries)
- [Architecture & Data Flow](#architecture--data-flow)
- [Record-to-Report (R2R) Modules (20–26)](#record-to-report-r2r-modules-2026)
- [Web Workbenches & Navigation Directory](#web-workbenches--navigation-directory)
- [Key Engineering Invariants](#key-engineering-invariants)
- [Technology Baseline](#technology-baseline)
- [Solution Layout](#solution-layout)
- [Getting Started](#getting-started)
- [Local Database Setup (No Admin, Docker-Free)](#local-database-setup-no-admin-docker-free)
- [Build, Migrate, Test & Benchmark](#build-migrate-test--benchmark)
- [Durable Operations & Worker Architecture](#durable-operations--worker-architecture)
- [Verification & Evidence Discipline](#verification--evidence-discipline)
- [Implementation Roadmap & External Gates](#implementation-roadmap--external-gates)
- [Documentation & Specifications](#documentation--specifications)
- [Contributing](#contributing)

---

## Project Objectives

1. **Complete Professional Lifecycle, Not a Scaffold:** Dependency-ordered vertical slices with working screens, commands, persistence, scoped authorization, append-only audit evidence, automated tests, documents, and operating procedures.
2. **Dual-Scope Professional Capability:** Both **Client Accounting Services (CAS / Record-to-Report)** and **Financial-Statement Assurance Audits** in one unified workspace.
3. **Exact-Version Approvals and Release Safety:** Synchronous release-safe generation; version-bound document approvals; durable operations with at-most-once external effects, reconciliation, and recovery quarantine.
4. **Native Practice Management:** CRM (leads, proposals, acceptance), staff time budgets, billing artifacts (invoices, credit notes, receipts, allocations), and an immutable firm ledger — without becoming an operational client ERP.
5. **Microsoft 365 Dependencies & Preserved Evidence:** Entra ID (identity) and Graph + SharePoint Online (selected-resource documents) remain fenced external boundaries; the Purview/eSignature scope rule from the status note above applies.
6. **Honest Evidence:** Every claim maps to executed test evidence in `docs/execution/status.json`; external blockers are recorded as blocked, never simulated.

---

## Three Strict Financial Boundaries

Absolute isolation is enforced between three accounting domains — in the Domain model, services, and database. This is an **import-first** preparation, audit, and consolidation platform, **not** an operational client ERP: client sales/purchase/inventory order processing, payroll execution, and payment initiation are excluded.

1. **Firm's Own Books (`Practice/FirmLedger`):** Client billing, WIP time tracking and rate cards, invoices, credit notes, receipts, and an immutable double-entry firm general ledger with closed-period locks and balance integrity.
2. **Client Accounting Workspace (`Accounting/`):** Client-owned source TB/GL imports, client charts of accounts, approved taxonomy mappings, proposed/agreed adjustments, reconciliation proofs, specialist schedules, audit fieldwork evidence, and draft/validated entity financial statement packages.
3. **Group Consolidation Workspace (`Consolidation/`):** Approved component packages, consolidation perimeter definitions, intercompany 1-to-1 matching and elimination journals, foreign currency translation (CTA reserves), and functional currency remeasurement. **Never** mutates component client books.

---

## Architecture & Data Flow

AuditSphereOps is structured as a **clean-architecture modular monolith** on ASP.NET Core with Blazor Interactive Server:

```
   ┌───────────────────────────────────────────────┐
   │                   BROWSERS                    │
   │  staff workbenches (/app/*) · client portal   │
   │           (/portal) · health probes           │
   └────────────────────┬──────────────────────────┘
                        │ Blazor Interactive Server (SignalR)
┌───────────────────────▼──────────────────────────────┐
│                  AuditSphereOps.Web                  │
│   Blazor Web App (Interactive Server) · MudBlazor    │
│   workbenches · restricted client portal (/portal)   │
│      route/refresh re-authorization · composes       │
│     Application commands & queries · no business     │
│           mutations through the DbContext            │
│     health probes (/health/live, /health/ready)      │
└───────────────────────┬──────────────────────────────┘
                        │ scoped commands & queries (RoleGrant-checked)
                        ▼
┌───────────────────────▼──────────────────────────────┐   ┌───────────────────────┐
│              AuditSphereOps.Application              │──▶│ AuditSphereOps.Domain │
│     ActorContext · RoleGrant scope authorization     │   │     Pure models &     │
│      (FIRM_WIDE · CLIENT · ENGAGEMENT · GROUP)       │   │ invariants grouped by │
│   Capability services · durable operation handlers   │   │      capability:      │
│   Pure calculators (statements, FX, eliminations)    │   │ Practice · Accounting │
│             OpenXML & PDFsharp renderers             │   │ Consolidation · Audit │
│                                                      │   │ Reviews · Completion  │
│                                                      │   │ Documents · Security  │
│                                                      │   │   (no EF · no I/O ·   │
│                                                      │   │      no clocks)       │
└───────────────────────┬──────────────────────────────┘   └───────────────────────┘
                        │ persists via Infrastructure · writes the durable outbox
                        ▼
┌───────────────────────▼──────────────────────────────┐   ┌───────────────────────┐
│            AuditSphereOps.Infrastructure             │   │ AuditSphereOps.Worker │
│    EF Core 10 · Npgsql 10 · AuditSphereDbContext     │◀──│BackgroundService host │
│             (capability partial classes)             │   │   groups: general ·   │
│              numeric(19,6) money policy              │   │ processing · records  │
│          append-only triggers & constraints          │   │                       │
│    PostgreSQL migrations · PostgresOperationStore    │   │FOR UPDATE SKIP LOCKED │
│                   (durable outbox)                   │   │operation claims · 60 s│
│                                                      │   │lease · 20 s renewal · │
│                                                      │   │ 5 attempts · backoff  │
│                                                      │   │   RESULT_UNCERTAIN    │
│                                                      │   │  recovery quarantine  │
└───────────────────────┬──────────────────────────────┘   └───────────────────────┘
                        │
                        ▼
┌───────────────────────▼──────────────────────────────┐
│                   POSTGRESQL 18.6                    │
│   dev loopback 127.0.0.1:5433 · EF Core migrations   │
│      uniquely-named disposable per-test schemas      │
│           (no InMemory provider fallback)            │
└──────────────────────────────────────────────────────┘

 EXTERNAL GATED BOUNDARIES — fail-closed, BLOCKED_EXTERNAL until live infrastructure:
   Entra OIDC sign-in (database session-epoch-bound cookies) · SharePoint/Graph
   documents on Sites.Selected + exact per-site grants · read-only Graph directory
   reader (User.Read.All) behind its separate consent gate · SHA-256 signed-document
   evidence and release manifests · Purview/eSignature providers: out of product scope

 THREE STRICT FINANCIAL BOUNDARIES — isolated in Domain, services, and database:
   Firm's own books (Practice/FirmLedger) ≠ Client accounting (Accounting/) ≠
   Group consolidation (Consolidation/) — consolidation consumes approved component
   packs only and never mutates component client books
```

Project reference direction (enforced by the architecture guard tests):

```
  Domain         → nothing (pure)
  Application    → Domain
  Infrastructure → Application, Domain
  Web            → Application, Infrastructure, Domain
  Worker         → Application, Infrastructure, Domain
```

### Domain Modules

- **Security:** `ActorContext`, `RoleGrant` resolution, scoped authorization policies, permission checks.
- **Practice:** CRM, leads, proposals, client acceptance, time, fee budgets, billing, invoices, receipts, firm ledger.
- **Acceptance:** Risk assessments, QAR setup defaults, independence confirmations, acceptance sign-offs.
- **Engagements:** Setup, periods, assigned staff teams, scope governance.
- **Accounting:** Client profiles, charts of accounts, trial balances, GLs, adjustment journals, specialist schedules, financial statements.
- **Consolidation:** Group perimeters, component packages, translation/remeasurement, eliminations.
- **Audit:** Fieldwork, programs, procedures, workpapers, sampling, journal-risk indicators, bank reconciliations, differences.
- **Reviews:** Stage-bound package reviews, partner review decisions, review point governance.
- **Completion:** Representation letters, audit sign-offs, controlled package release handoffs.
- **Documents & Records:** Document metadata, upload/download fences, M365 onboarding, exact signed-document evidence, SHA-256 identities, immutable archive manifests.

---

## Record-to-Report (R2R) Modules (20–26)

Native, production-grade implementations for the R2R lifecycle defined under `docs/task_breakdown`:

| Module | Scope & Core Capabilities | Key Services & APIs |
|---|---|---|
| **Module 20: Accounting Workspace, Setup & Profiles** | Client accounting profiles with concurrency-fenced revisions, fiscal calendars, reporting periods, books, multi-tier chart of accounts versioning, hierarchy navigation with parent resolution and direct child counts, dimension definitions, and taxonomy overlays. | `ClientAccountingService`<br>• `ReviseProfileAsync`<br>• `GetChartRevisionAccountsAsync`<br>• `CreateChartOfAccountsDraftAsync` |
| **Module 21: Trial Balance Intake & GL Completeness** | Safe streaming CSV/XLSX intake, deterministic sealing, paged source row queries with prefix filtering, RFC 4180 CSV exports with CSV injection protection (`=`, `+`, `-`, `@`), bounded resumable GL batches with chunk digests, and GL opening/movement completeness bridges. | `TrialBalanceDatasetQuery`<br>`TrialBalanceImportService`<br>• `GetTrialBalanceRowsAsync`<br>• `ExportTrialBalanceCsvAsync`<br>• `IngestTrialBalanceAsync` |
| **Module 22: Adjustments & Journal Governance** | Draft journals with arbitrary balanced lines, revision concurrency guards, in-place draft updates, audit/client origins, dual-role management decisions, reversal drafts, CSV instruction exports, and reviewer-gated posting with separation of duties. | `AdjustmentJournalService`<br>• `UpdateDraftAsync`<br>• `PostJournalAsync`<br>• `RecordManagementDecisionAsync` |
| **Module 23: Reconciliations, ECL & Specialist Schedules** | Source-bound bank and AR/AP aging reconciliations, transparent proof calculations with item summation and residual checks, correction journal linking, ECL provision matrix assessments, lower-of-cost-and-NRV inventory valuations, and specialist schedules (PPE, payroll, loans, equity, tax, cash forecast). | `AccountingAnalysisService`<br>• `CalculateReconciliationProofAsync`<br>• `LinkReconciliationCorrectionAsync`<br>• `AssessEclProvisionAsync` |
| **Module 24: Financial Statements & Taxonomy Alignment** | Versioned chart-to-taxonomy mapping drafts and reviews, multi-tier allocation validation, approved taxonomy enforcement, and pure deterministic financial statement calculators. | `FinancialStatementService`<br>`FinancialStatementCalculator`<br>• `CalculateFinancialStatements`<br>• `ApproveMappingRevisionAsync` |
| **Module 25: Financial Package Assembly & Rendering** | Versioned financial packages, durable outbox worker builds, exact-byte rendered HTML/PDF artifacts, OpenXML formula-free/macro-free XLSX workbooks, and immutable partner review decisions. | `FinancialStatementService`<br>`PackageRenderingService`<br>• `EnqueuePackageCalculationAsync`<br>• `RecordReviewDecisionAsync` |
| **Module 26: Group Consolidation & Multi-Currency** | Perimeter membership, draft scope versions, component pins and replacements, intercompany 1-to-1 matching and eliminations, foreign operation currency translation and remeasurement with rate policies and historical rate rules, and consolidated reporting. | `ConsolidationService`<br>`CurrencyTranslationService`<br>`CurrencyRemeasurementService`<br>• `ReplaceComponentAsync`<br>• `TranslateForeignOperationAsync` |

---

## Web Workbenches & Navigation Directory

Role-gated Blazor workbenches (`src/AuditSphereOps.Web`) with client-side draft auto-save:

| Route | Workbench | Description |
|---|---|---|
| `/app` | **Portfolio Overview** | Scoped engagement portfolio, client index, search, activity counters. |
| `/app/practice/leads` | **Practice Leads** | CRM pipeline, lead qualification, opportunity conversion. |
| `/app/practice/time` | **Practice Time** | Time tracking on assigned tasks, rate snapshots, independent manager approval. |
| `/app/finance` | **Practice Finance** | Firm billing, invoicing, credit notes, receipts, firm ledger. |
| `/app/accounting` | **Accounting Workspace** | Client profiles, reporting periods, books, setup status. |
| `/app/accounting/evidence` | **Evidence Queue** | Engagement-scoped evidence tracker, source-bound links, procedure bindings. |
| `/app/accounting/mappings` | **COA & Taxonomy Mappings** | Chart versioning, account hierarchy, mapping workbenches. |
| `/app/accounting/journals` | **Adjustment Journals** | Context-bound journals, draft updates, balance checks, posting workflows. |
| `/app/accounting/differences` | **Audit Differences** | Absolute/signed difference summaries, journal-impact payloads, resolution status. |
| `/app/accounting/rollforward` | **Period Roll-forward** | Opening-balance roll-forward, chart revisions, prior-period mapping lineage. |
| `/app/accounting/reviews` | **Package Reviews** | Multi-stage review queue (management, accounting, partner). |
| `/app/accounting/restatements` | **Period Restatements** | Retrospective restatement drafts, version comparison, restatement journals. |
| `/app/accounting/remeasurement` | **Currency Remeasurement** | Source-evidence-linked workpapers with draft autosave and independent approval. |
| `/app/consolidation` | **Group Consolidation** | Perimeter management, component pins/replacements, elimination journals. |
| `/app/operations` | **Durable Operations** | Live monitor for operations, leases, retries, manual cancellations. |
| `/app/administration` | **Administration** | Firm-wide configuration, user roles, security policies, preferences. |
| `/app/administration/microsoft365` | **M365 Tenant Administration** | Least-privilege tenant administration boundary: guarded consent session status, bounded read-only directory search, capability verification (live consent remains externally gated). |
| `/app/administration/microsoft365/tenant-connection` | **M365 Tenant Connection** | Selected-site connection test, tenant summary and read-only current-verification bar for enabled Microsoft permission checks. |
| `/app/administration/project-progress` | **Project Progress** | Administrator-only tracker: four-state bars and completed-card percentages derived from published task cards; unmeasured areas shown as untracked, never zero. |
| `/app/audit/library` | **Audit Program Library** | Versioned programs with procedure counts and per-version readback. |
| `/setup/microsoft365` | **M365 Setup** | Bootstrap-claim onboarding draft (Entra/SharePoint verification externally gated). |
| `/auth/access-not-assigned` | **Access Not Assigned** | Boundary page for authenticated users without an applicable role grant. |
| `/portal` | **Client Portal** | Restricted portal: PBC uploads, review status, draft representations. |
| `/health/live`, `/health/ready` | **Health & Readiness** | Health probes with live Npgsql database connectivity verification. |

---

## Key Engineering Invariants

- **Exact Money Arithmetic:** `numeric(19,6)` in PostgreSQL and C# `decimal`. Zero floating-point drift, zero silent rounding.
- **Append-Only Evidence:** Sealed datasets, posted billing history, approved mappings, applied journals, issued packages, and audit evidence cannot be rewritten or deleted — enforced by PostgreSQL triggers (`trial_balance_rows` sealed trigger, `materiality_approvals` protection) and check constraints.
- **Durable Outbox with Exactly-Once Discipline:** `SKIP LOCKED` worker claims with leases (60 s lease, 20 s renewal, 5 attempts, persisted exponential backoff), immutable request/attempt records, and `RESULT_UNCERTAIN` recovery quarantine.
- **Scope-Checked Authorization Everywhere:** Every command, query, and API call enforces an explicit `RoleGrant` scope (`FIRM_WIDE`, `CLIENT`, `ENGAGEMENT`, `GROUP`); an engagement-only grant never widens to sibling clients or engagements. Shell navigation search results are re-authorized per hit against the destination route's decision.
- **Least-Privilege Microsoft Graph:** Tenant-wide scopes are never requested. The approved core exception is a read-only directory reader (`User.Read.All`) behind its separately consented gate; document access stays on `Sites.Selected` with exact per-site grants; optional mutations remain disabled pending separate gates (see [`docs/architecture/auditsphere-m365-tenant-administration-permissions.md`](docs/architecture/auditsphere-m365-tenant-administration-permissions.md)).
- **Pure Calculation Engines:** Statement calculations, eliminations, translations, and remeasurements are pure deterministic functions — no EF Core, network, system clocks, or non-deterministic IDs.
- **Document & Export Integrity:** OpenXML spreadsheets are formula-free and macro-free; PDFs use PDFsharp/MigraDoc with OFL Noto Sans and byte-stable layout; CSV exports neutralize formulas (`=`, `+`, `-`, `@`).
- **Fail-Closed Methodology:** Missing exchange rates, unsupported valuation methods, unapproved perimeters, or stale source inputs fail closed and block release — never defaulting to zero or arbitrary assumptions.
- **No Autonomous Audit Opinions:** The platform computes differences, evaluates risk indicators, and enforces gates; human practitioners make professional conclusions and sign-offs.
- **Never-Fake External Effects:** `ExternalEffects.Enabled=false` locally; simulation handlers exist for tests only (`AllowSimulationAdapters=true`). Live gates stay `BLOCKED_EXTERNAL` without live infrastructure; the Purview/eSignature out-of-scope rule applies as stated above.

---

## Technology Baseline

| Component | Choice / Version | Purpose |
|---|---|---|
| **Runtime & SDK** | .NET 10 (`global.json` pin `10.0.300`, `rollForward=disable`) | Core runtime and compilation toolchain |
| **Language** | C# 14 | Strict nullability and pattern matching |
| **Web Framework** | ASP.NET Core Blazor (Interactive Server) | Web application & client portal |
| **UI Component Library** | MudBlazor 9.10.0 (Web project only) | Component system for workbenches and portal; project reference direction guard-tested |
| **ORM & Data Provider** | Entity Framework Core 10.0.12 + Npgsql 10.0.0 | PostgreSQL persistence |
| **Database Engine** | PostgreSQL 18.6 | User-local development on port `5433`; managed cloud in prod |
| **Document Processing** | DocumentFormat.OpenXml & PDFsharp / MigraDoc | Byte-stable, formula-free document generation |
| **Test Frameworks** | xUnit, Microsoft.Playwright | PostgreSQL-backed integration, API, and E2E browser tests |

---

## Solution Layout

```
AuditSphere/
├── src/
│   ├── AuditSphereOps.Domain/          # Pure domain models, entities, business invariants
│   ├── AuditSphereOps.Application/     # ActorContext, scope authorization, calculators, renderers, orchestrators
│   ├── AuditSphereOps.Infrastructure/  # EF Core DbContext, mappings, migrations, provider adapters
│   ├── AuditSphereOps.Web/             # Blazor Web App (Interactive Server), workbenches, portal, health probes
│   └── AuditSphereOps.Worker/          # Durable-operation worker (general, processing, records)
├── tests/
│   ├── AuditSphereOps.Domain.Tests/    # PostgreSQL-backed domain, integration & architecture guard tests
│   ├── AuditSphereOps.Api.Tests/       # HTTP API & transfer security tests
│   └── AuditSphereOps.E2E.Tests/       # Playwright journeys & grant revocation tests
├── docs/
│   ├── auditsphere-accounting-module-requirements-current.md  # Current STE functional requirements
│   ├── architecture/                   # Current architecture authority, code map, naming policy
│   ├── task_breakdown/                 # R2R task breakdowns (Modules 20–26)
│   ├── testing/                        # E2E automation strategy, test case catalog, scope/tenant-admin acceptance cases
│   └── execution/                      # status.json + current-slice/pending-tasks ledgers
├── scripts/db/                         # Database lifecycle (start, stop, status, restore-drill)
├── global.json                         # SDK version pin (10.0.300, rollForward=disable)
└── Directory.Packages.props            # Central Package Management (CPM)
```

---

## Getting Started

### Prerequisites

- **.NET SDK 10.0.300** (`global.json` enforces the exact SDK version)
- **PostgreSQL 18.6** accessible locally on port `5433`
- **PowerShell** (Windows) or **Zsh / Bash** (macOS / Linux)

```bash
dotnet --version    # Expected output: 10.0.300
dotnet tool restore # Restores the pinned local dotnet-ef tool
```

---

## Local Database Setup (No Admin, Docker-Free)

A user-local PostgreSQL 18.6 development cluster runs on port **5433** with trust authentication on loopback (`127.0.0.1`). Defaults: databases `auditsphere` and `auditsphere_tests`.

### macOS (Homebrew)

```bash
pg_ctl -D /opt/homebrew/var/postgresql@18 -o "-p 5433" start   # Start the cluster
pg_ctl -D /opt/homebrew/var/postgresql@18 status               # Check cluster status
scripts/db/restore-drill.sh                                    # Restore drill
```

### Windows (PowerShell)

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\db\status.ps1   # Status + ensure dev databases
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\db\start.ps1    # Idempotent cluster start
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\db\stop.ps1     # Fast cluster shutdown
```

*Configuration Overrides:* `PGSQL_HOME`, `PGSQL_DATA`, `PGSQL_PORT`, `AUDITSPHERE_TEST_CONNECTION`. The scripts automatically validate the PostgreSQL major version.

---

## Build, Migrate, Test & Benchmark

```bash
# 1. Build the entire solution in Release mode (restore first if needed)
dotnet build AuditSphereOps.slnx --no-restore --configuration Release

# 2. Run the complete PostgreSQL-backed solution test suite
dotnet test AuditSphereOps.slnx --no-build --configuration Release

# 3. Verify EF Core model changes against the applied migrations
dotnet ef migrations has-pending-model-changes \
  --project src/AuditSphereOps.Infrastructure \
  --startup-project src/AuditSphereOps.Web \
  --configuration Release

# 4. Optional: run the representative accounting benchmark
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore --configuration Release \
  --filter 'FullyQualifiedName~AccountingBenchmarkTests'
```

The hosted GitHub Actions workflow is a **mandatory build gate plus documentation health checks**: pinned SDK check, locked restore, Release build, `has-pending-model-changes`, and the Markdown health/filename validators plus the narrative volatile-metrics guard (`docs-health` job). It does not execute the test suites, so a green CI badge is not test acceptance — the suites above are the test evidence and run locally (or in an explicitly configured test runner) against real PostgreSQL 18.6.

### Database Test Isolation

The suite connects to `127.0.0.1:5433` / `auditsphere_tests`. Every test provisions a **uniquely named disposable PostgreSQL schema**, applies all migrations, asserts, and drops the schema. **No InMemory provider fallback** — every query and trigger runs against genuine PostgreSQL.

---

## Durable Operations & Worker Architecture

Long-running work runs through the outbox pattern in `AuditSphereOps.Worker`:

- **Operation Types:** GL completeness bridge, financial package builds, exact-byte rendering, PBC evidence transfer and reconciliation, archive bundle assembly.
- **Deployment Roles:** `general` (orchestration, notifications, standard background work) · `processing` (GL digestion, TB sealing, statement calculation) · `records` (archive bundle assembly and document package rendering; Purview is out of product scope).
- **Fault Recovery:** Leases via PostgreSQL `FOR UPDATE SKIP LOCKED`; expired leases are reclaimed after 60 seconds; tasks failing after 5 attempts enter a quarantined disposition for human review.

---

## Verification & Evidence Discipline

- **`docs/execution/status.json`** — single source of truth: specification SHA-256, baseline commit, active slice, verified test evidence, external blockers, permitted actions.
- **`docs/execution/auditsphere-execution-current-slice.md`** — active vertical slice, verified local state, operational runbook.
- **Zero-Warning Tolerance** — Release builds under .NET 10 must report `0 Warning(s)` and `0 Error(s)`.

---

## Implementation Roadmap & External Gates

Dependency-ordered work packages (P0–P10 per [docs/execution/auditsphere-execution-pending-tasks.md](docs/execution/auditsphere-execution-pending-tasks.md)):

| Phase | Gate / Objective | Status |
|---|---|---|
| **P0** | Core Domain & Ledger Foundation — modular monolith, Practice/Accounting/Consolidation cores | `LOCAL_VERIFIED` |
| **P1** | Live Entra OIDC Authentication — requires live Entra ID tenant registration | `BLOCKED_EXTERNAL` |
| **P2** | Live SharePoint/Graph Documents — requires live M365 tenant, selected-resource scopes | `BLOCKED_EXTERNAL` |
| **P3** | External Release Checkpoint Store — requires production cryptographic checkpoint infrastructure | `BLOCKED_EXTERNAL` |
| **P4** | Records Retention & Manifest Lineage — exact uploaded document evidence, SHA-256 identities, immutable archive manifests | `LOCAL_VERIFIED` |
| **P5** | Document Signing Evidence Lineage — uploaded signed-document evidence and human decisions (no external eSignature provider claim) | `LOCAL_VERIFIED` |
| **P6** | Records / Archive Residual Hardening — immutable archive schema, lineage manifests, structured exports | `LOCAL_VERIFIED` |
| **P7** | Cross-Store Recovery (RPO/RTO) — requires multi-region cloud backup infrastructure | `BLOCKED_EXTERNAL` |
| **P8** | Production Observability & Secrets — requires Azure Key Vault and production OpenTelemetry collector | `BLOCKED_EXTERNAL` |
| **P9** | Independent Review Governance — requires independent partner sign-off and branch protections | `BLOCKED_EXTERNAL` |
| **P10** | Real-Tenant Acceptance (§47) — full operational pilot on customer-authorized tenant | `BLOCKED_EXTERNAL` |

---

## Documentation & Specifications

Authority hierarchy and full reading guide: **[`docs/auditsphere-docs-index.md`](docs/auditsphere-docs-index.md)**.

| Document | Description |
|---|---|
| [Current architecture](docs/architecture/auditsphere-architecture-current-architecture.md) | Implementation architecture and engineering boundaries. |
| [docs/auditsphere-accounting-module-requirements-current.md](docs/auditsphere-accounting-module-requirements-current.md) | **STE Functional Requirements** — current commercial, audit lifecycle and firm operations contract. |
| [docs/architecture/auditsphere-architecture-current-architecture.md](docs/architecture/auditsphere-architecture-current-architecture.md) | **Current Architecture** — monolith structure, boundaries, dependency guards. |
| [docs/architecture/auditsphere-architecture-code-map.md](docs/architecture/auditsphere-architecture-code-map.md) | **Architecture Code Map** — capability map and documentation authority index. |
| [docs/architecture/auditsphere-architecture-document-naming-policy.md](docs/architecture/auditsphere-architecture-document-naming-policy.md) | **Document Naming Policy** — naming conventions for repository documentation. |
| [docs/architecture/auditsphere-m365-tenant-administration-permissions.md](docs/architecture/auditsphere-m365-tenant-administration-permissions.md) | **M365 Tenant Administration Permission Boundary** — least-privilege Graph capabilities, consent gates, scope prohibitions. |
| [docs/task_breakdown/](docs/task_breakdown/) | **R2R Task Breakdown** — task definitions for Modules 20–26. |
| [docs/execution/status.json](docs/execution/status.json) | **Execution Ledger** — machine-readable progress and verification evidence. |
| [docs/execution/auditsphere-execution-current-slice.md](docs/execution/auditsphere-execution-current-slice.md) | **Active Slice Runbook** — verified local state, tested SHA, execution notes. |
| [AGENTS.md](AGENTS.md) | **Engineering Instructions & Invariants** — boundaries, safety rules, development guidelines. |

---

## Contributing

1. **Consult the Spec First:** Review the current STE requirements, `docs/architecture/auditsphere-architecture-current-architecture.md` and `AGENTS.md` before changes.
2. **One Vertical Slice at a Time:** Smallest coherent slice in dependency order; never weaken an existing authorization check or mandatory security control.
3. **Prove with Evidence:** `0 Warning(s)` Release builds and targeted tests against the local PostgreSQL 18.6 cluster; migrations must preserve append-only history.
4. **Honest Recording:** Update `docs/execution/status.json` and `docs/execution/auditsphere-execution-current-slice.md` with observed facts only; never record an external gate as passed without live evidence.
5. **Respect Architecture Prohibitions:** Never introduce MediatR, Frappe, ERPNext, Python backends, React frontends, message brokers, or autonomous audit decision engines.
