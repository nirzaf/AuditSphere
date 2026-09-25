# AuditSphereOps







[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)



[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-18.6-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)



[![EF Core](https://img.shields.io/badge/EF%20Core-10.0-0078D4?logo=nuget&logoColor=white)](https://learn.microsoft.com/ef/core/)



[![Blazor](https://img.shields.io/badge/Blazor-Interactive%20Server-512BD4?logo=blazor&logoColor=white)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)



[![Tests](https://img.shields.io/badge/Tests-PostgreSQL--backed-brightgreen)](tests/)



[![Migrations](https://img.shields.io/badge/Migrations-EF%20Core-blue)](src/AuditSphereOps.Infrastructure/Persistence/Migrations/)



![License](https://img.shields.io/badge/License-Informational-lightgrey)







**AuditSphereOps** is a professional audit, accounting, and assurance operations platform — an enterprise **.NET 10 modular monolith** built for accounting practices, CPA firms, audit engagements, and multi-entity group consolidation. It governs the entire engagement lifecycle: lead qualification, proposal generation, client acceptance, practice time and billing, trial-balance and general-ledger intake, financial statement production, audit fieldwork, review, controlled package signing/release, records retention, and multi-currency consolidation.







> **Status: Specification-Driven Modular Monolith.** Core domain modules, practice management, period/book/basis-bound trial balance intake, bounded resumable GL intake, client-scoped accounting dimensions, chart/taxonomy hierarchy controls, QAR setup defaults, GL service-date lineage, opening/movement completeness, deterministic journal-risk indicators, exact-journal difference-impact classification, governed difference correction states, audit planning, bank reconciliation proofs, package production, archive lineage, provider safety fences, recovery quarantine, queued-operation cancellation, governed financial-package review decisions, exact-byte reviewed package artifacts, deterministic formula-free XLSX/DOCX exports, safe legacy period/chart backfill, package-bound release candidates, source-bound accounting-evidence freshness, typed specialist schedules, explicit asset-schedule methodology, client-safe management views, authorized review queues, source-bound reconciliation-item aging, durable GL/package processing, context-bound adjustment journals, analytical-review replay lineage, valuation differences linked to scoped adjustments, versioned FX ranges and approved rate-rule enforcement, deterministic advanced-consolidation safety cores, and sealed GL source-bound schedule control totals are implemented and verified locally (verified per [`docs/execution/status.json`](docs/execution/status.json), the single authority for test counts, verified SHA, migration count and external blockers, against PostgreSQL 18.6). Production external gates (Entra OIDC, selected-resource SharePoint/Graph, and live release checkpoint) require live infrastructure and remain recorded as `BLOCKED_EXTERNAL`, never fake-passed. Purview and eSignature provider integrations are out of product scope: exact uploaded signed-document evidence, SHA-256 identities, human decisions, and release manifests are preserved without claiming provider acceptance.







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







1. **Complete Professional Lifecycle, Not a Scaffold:** Deliver dependency-ordered vertical slices with working screens, commands, persistence, scoped authorization, append-only audit evidence, automated tests, documents, and operating procedures.



2. **Dual-Scope Professional Capability:** Full support for both **Client Accounting Services (CAS / Record-to-Report)** and **Financial-Statement Assurance Audits** within a single unified workspace.



3. **Exact-Version Approvals and Release Safety:** Synchronous, release-safe generation; version-bound document approvals; durable operations with at-most-once external effects, reconciliation, and recovery quarantine.



4. **Native Practice Management:** Integrated CRM (leads, proposals, acceptance), staff time budgets, billing artifacts (invoices, credit notes, receipts, allocations), and an immutable firm financial ledger — without converting the platform into an operational client ERP.



5. **Microsoft 365 Dependencies & Preserved Evidence:** Microsoft Entra ID (identity) and Microsoft Graph + SharePoint Online (selected-resource documents) remain external boundaries governed by safety fences. Purview and eSignature provider integrations are out of product scope: exact uploaded signed-document evidence, SHA-256 identities, human decisions, and release manifests are preserved without claiming provider acceptance.



6. **Honest Evidence:** Every claim maps directly to executed test evidence in `docs/execution/status.json`; external blockers are recorded as blocked, never simulated.







---







## Three Strict Financial Boundaries







AuditSphereOps enforces absolute isolation between three distinct accounting domains:







```



┌─────────────────────────────────────────────────────────────────────────────┐



│                      THREE STRICT FINANCIAL BOUNDARIES                      │



├───────────────────────────────┬─────────────────────────────┬───────────────┤



│ 1. FIRM'S OWN BOOKS           │ 2. CLIENT ACCOUNTING        │ 3. GROUP      │



│    (Practice / FirmLedger)    │    WORKSPACE (Accounting/)  │    CONSOLID.  │



├───────────────────────────────┼─────────────────────────────┼───────────────┤



│ • Firm CRM, proposals, billing│ • Client-owned source TB/GL │ • Multi-entity│



│ • Staff time tracking & rates │ • Client charts of accounts │   perimeter   │



│ • Client invoicing & receipts │ • Mappings & adjustments    │ • Component   │



│ • Firm financial ledger       │ • Entity financial packages │   packs       │



│ • Closed-period locks         │ • Specialist schedules      │ • Eliminations│



│                               │ • Audit fieldwork evidence  │ • Translation │



├───────────────────────────────┴─────────────────────────────┴───────────────┤



│ SCOPE BOUNDARY: This is an import-first preparation, audit, and             │



│ consolidation platform, NOT an operational client ERP. Excludes client sales/│



│ purchase/inventory order processing, payroll execution, and payment initiation│



└─────────────────────────────────────────────────────────────────────────────┘



```







1. **Firm's Own Books (`Practice/FirmLedger`):** The firm's internal financial reality: client billing, WIP time tracking, rate cards, invoices, receipts, and an immutable double-entry general ledger with period locks and balance integrity.



2. **Client Accounting Workspace (`Accounting/`):** Client-owned financial datasets: trial-balance imports, general ledger intake, client charts of accounts, taxonomy mappings, proposed/agreed adjustments, reconciliation proofs, and draft/validated financial statement packages.



3. **Group Consolidation Workspace (`Consolidation/`):** Multi-entity reporting: approved component packages, consolidation perimeter definitions, intercompany matching and eliminations, foreign currency translation (CTA reserves), and functional currency remeasurement. **Never** mutates component client books.







---







## Architecture & Data Flow







AuditSphereOps is structured as a **clean-architecture modular monolith** on ASP.NET Core with Blazor Interactive Server:







```



┌─────────────────────────────────────────────────────────────────────────────┐



│                    USER INTERFACE & CLIENT PORTAL (Web)                     │



│  Blazor Interactive Server · Razor Components · Dynamic Local Autosave      │



│  Staff Workbenches (/app/accounting, /app/consolidation, /app/finance, etc.)│



│  Client Portal (/portal) · Health Checks (/health/live, /health/ready)      │



└──────────────────────────────────────┬──────────────────────────────────────┘



                                       │



┌──────────────────────────────────────┴──────────────────────────────────────┐



│                           APPLICATION CORE LAYER                            │



│  ActorContext · RoleGrant Scope Authorization (FIRM_WIDE/CLIENT/ENGAGEMENT) │



│  ClientAccountingService · TrialBalanceDatasetQuery · AdjustmentJournal     │



│  AccountingAnalysisService · FinancialStatementService · Consolidation      │



│  Pure Calculators (Statement, Remeasurement, Translation, Eliminations)     │



│  Package Assembly · OpenXML/PDFsharp Renderers · Durable Handlers           │



└──────────────────────────────────────┬──────────────────────────────────────┘



                                       │



┌──────────────────────────────────────┴──────────────────────────────────────┐



│                    INFRASTRUCTURE PERSISTENCE & ADAPTERS                    │



│  EF Core 10 · Npgsql 10 · AuditSphereDbContext · Snake_Case Naming          │



│  Exact numeric(19,6) Money Policy · Append-Only DB Triggers & Constraints   │



│  PostgresOperationStore · PostgreSQL Migrations · Provider Adapters         │



└──────────────────────────────────────┬──────────────────────────────────────┘



                                       │



                    ┌──────────────────┴──────────────────┐



                    ▼                                     ▼



        ┌───────────────────────┐             ┌───────────────────────┐



        │     POSTGRESQL 18     │             │    DURABLE WORKERS    │



        │  Loopback port 5433   │             │  general · processing │



        │  EF Core Migrations   │             │  records deployments  │



        │  Disposable Schemas   │             │  SKIP LOCKED Leasing  │



        └───────────────────────┘             └───────────────────────┘



                                       │



           ════════════════════════════╪════════════════════════════



                     EXTERNAL GATED BOUNDARIES (FAIL-CLOSED)



           Microsoft Entra ID · Microsoft Graph · SharePoint Online



           Exact Signed-Document Evidence · SHA-256 Release Manifests



```







### Domain Modules



- **Security:** `ActorContext`, `RoleGrant` resolution, scoped authorization policies, and permission checks.



- **Practice:** CRM, leads, proposals, client acceptance, time tracking, fee budgeting, billing, invoices, receipts, and firm financial ledger.



- **Acceptance:** Client risk assessments, QAR setup defaults, independence confirmations, and engagement acceptance sign-offs.



- **Engagements:** Engagement setup, periods, assigned staff teams, and scope governance.



- **Accounting:** Client profiles, charts of accounts, trial balances, general ledgers, adjustment journals, specialist schedules, and financial statements.



- **Consolidation:** Group perimeters, component packages, currency translation/remeasurement, and intercompany eliminations.



- **Audit:** Fieldwork, audit programs, procedures, testing workpapers, sample selection, journal-risk indicators, bank reconciliations, and audit differences.



- **Reviews:** Stage-bound financial package reviews, partner review decisions, and review point governance.



- **Completion:** Representation letters, audit sign-offs, and controlled package release handoffs.



- **Documents & Records:** Document metadata, upload/download fences, Microsoft 365 onboarding, exact uploaded signed-document evidence, SHA-256 identities, and immutable archive manifests.







---







## Record-to-Report (R2R) Modules (20–26)







AuditSphereOps provides native, production-grade implementations for the complete Record-to-Report (R2R) lifecycle defined under `docs/task_breakdown`:







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







The Blazor Interactive Server front-end (`src/AuditSphereOps.Web`) delivers role-gated workbenches with client-side draft auto-save and responsive feedback:







| Route | Workbench | Description & Key Features |



|---|---|---|



| `/app` | **Portfolio Overview** | Scoped engagement portfolio, client index, search, and activity counters. |



| `/app/practice/leads` | **Practice Leads** | CRM pipeline, lead qualification, and opportunity conversion. |



| `/app/practice/time` | **Practice Time** | Staff time tracking against assigned tasks, rate snapshots, and independent manager approval queues. |



| `/app/finance` | **Practice Finance** | Firm billing, client invoicing, credit notes, receipts, and firm financial ledger. |



| `/app/accounting` | **Accounting Workspace** | Client profile configuration, reporting periods, books, and setup status. |



| `/app/accounting/evidence` | **Accounting Evidence Queue** | Engagement-scoped evidence tracker, source-bound links, and audit procedure bindings. |



| `/app/accounting/mappings` | **COA & Taxonomy Mappings** | Multi-tier chart versioning, account hierarchy, and chart-to-taxonomy mapping workbenches. |



| `/app/accounting/journals` | **Adjustment Journals** | Context-bound adjustment journals, draft updates, balance checks, and posting workflows. |



| `/app/accounting/differences`| **Audit Differences** | Gross absolute and signed difference summaries, journal-impact payloads, and resolution status. |



| `/app/accounting/rollforward`| **Period Roll-forward** | Safe roll-forward of opening balances, chart revisions, and prior-period mapping lineage. |



| `/app/accounting/reviews` | **Package Reviews** | Multi-stage review queue (management, accounting, partner) for validated financial packages. |



| `/app/accounting/restatements`| **Period Restatements** | Retrospective accounting restatement drafts, version comparison, and restatement journals. |



| `/app/consolidation` | **Group Consolidation** | Group perimeter management, component package pins/replacements, and elimination journals. |



| `/app/operations` | **Durable Operations** | Live monitor for background operations, leases, retries, and manual queue cancellations. |



| `/app/administration` | **Administration** | Firm-wide configuration, user roles, security policies, and system preferences. |



| `/portal` | **Client Portal** | Restricted client-facing portal for PBC file uploads, review status, and draft representations. |



| `/health/live`, `/health/ready` | **Health & Readiness** | ASP.NET Core health probes with live Npgsql database connectivity verification. |







---







## Key Engineering Invariants







- **Exact Money Arithmetic:** Money is strictly `numeric(19,6)` in PostgreSQL arithmetic and C# `decimal`. Zero floating-point drift, zero silent rounding.



- **Append-Only Evidence:** Completed results, sealed datasets, posted billing history, approved mappings, applied journals, and audit evidence cannot be rewritten or deleted. Immutability is enforced at the database level using PostgreSQL triggers (`trial_balance_rows` sealed trigger, `materiality_approvals` protection) and check constraints.



- **Durable Outbox with Exactly-Once Discipline:** Long-running operations (GL completeness, statement calculation, package rendering) execute via a database-backed durable outbox. Uses PostgreSQL `SKIP LOCKED` worker claims with leases (60 s lease, 20 s renewal, 5 attempts, persisted exponential backoff), immutable request/attempt records, and `RESULT_UNCERTAIN` recovery quarantine.



- **Scope-Checked Authorization Everywhere:** Every command, query, and API call enforces an explicit `RoleGrant` scope (`FIRM_WIDE`, `CLIENT`, `ENGAGEMENT`, or `GROUP`). An engagement-only grant can never widen to sibling clients or engagements.



- **Pure Calculation Engines:** All financial statement calculations, group consolidation eliminations, currency translations, and remeasurements are pure deterministic functions without EF Core, network, system clocks, or non-deterministic IDs.



- **Document & Export Integrity:** Exported spreadsheets are OpenXML formula-free and macro-free; generated PDFs use PDFsharp/MigraDoc with OFL Noto Sans and byte-stable layout; CSV exports neutralize formulas (`=`, `+`, `-`, `@`) to prevent CSV injection.



- **Fail-Closed Methodology:** Missing exchange rates, unsupported valuation methods, unapproved perimeters, or stale source inputs fail closed and block release. Never default to zero or arbitrary assumptions.



- **No Autonomous Audit Opinions:** The platform computes differences, evaluates risk indicators, and enforces gates; human practitioners make professional conclusions and sign-offs.



- **Never-Fake External Effects:** `ExternalEffects.Enabled=false` locally; simulation handlers exist for unit/integration tests only (`AllowSimulationAdapters=true`). Live external gates (Entra OIDC, selected-resource SharePoint/Graph, and live release checkpoints) require production infrastructure and remain recorded as `BLOCKED_EXTERNAL`. Purview and eSignature provider integrations are out of product scope: exact uploaded signed-document evidence, SHA-256 identities, human decisions, and release manifests are preserved without claiming provider acceptance.







---







## Technology Baseline







| Component | Choice / Version | Purpose |



|---|---|---|



| **Runtime & SDK** | .NET 10 (pinned via `global.json`: `10.0.300`) | Core runtime and compilation toolchain |



| **Language** | C# 14 | Primary language with strict nullability and pattern matching |



| **Web Framework** | ASP.NET Core Blazor (Interactive Server) | Enterprise web application & client portal |



| **ORM & Data Provider** | Entity Framework Core 10.0.12 + Npgsql 10.0.0 | High-performance PostgreSQL persistence |



| **Database Engine** | PostgreSQL 18.6 | User-local development on port `5433`; managed cloud in prod |



| **Document Processing**| DocumentFormat.OpenXml & PDFsharp / MigraDoc | Byte-stable, formula-free document generation |



| **Test Frameworks** | xUnit, Microsoft.Playwright | PostgreSQL-backed integration, API, and E2E browser tests |







---







## Solution Layout







```



AuditSphere/



├── src/



│   ├── AuditSphereOps.Domain/          # Pure domain models, entities, and business invariants



│   ├── AuditSphereOps.Application/     # ActorContext, scope authorization, pure calculators, renderers, orchestrators



│   ├── AuditSphereOps.Infrastructure/  # EF Core DbContext, PostgreSQL mappings, migrations, provider adapters



│   ├── AuditSphereOps.Web/             # Blazor Web App (Interactive Server), workbenches, client portal, health probes



│   └── AuditSphereOps.Worker/          # Durable-operation background worker (general, processing, records)



├── tests/



│   ├── AuditSphereOps.Domain.Tests/    # PostgreSQL-backed domain and integration test suites



│   ├── AuditSphereOps.Api.Tests/       # HTTP API & transfer security tests



│   └── AuditSphereOps.E2E.Tests/       # Playwright end-to-end user journeys & grant revocation tests



├── docs/



│   ├── auditsphere-requirements-system-specification-current.md                # Authoritative v5.0 build contract



│   ├── auditsphere-accounting-module-requirements-current.md# AC-01 to AC-28 user stories & gap analysis



│   ├── task_breakdown/                 # Record-to-Report detailed task breakdowns (Modules 20–26)



│   └── execution/                      # status.json and auditsphere-execution-current-slice.md execution ledgers



├── scripts/db/                         # Database lifecycle scripts (start, stop, status, restore-drill)



├── global.json                         # SDK version pin (10.0.300, rollForward=disable)



└── Directory.Packages.props            # Central Package Management (CPM)



```







---







## Getting Started







### Prerequisites







- **.NET SDK 10.0.300** (`global.json` enforces the exact SDK version)



- **PostgreSQL 18.6** accessible locally on port `5433`



- **PowerShell** (Windows) or **Zsh / Bash** (macOS / Linux)







### Toolchain Verification







```bash



# 1. Verify .NET SDK version



dotnet --version



# Expected output: 10.0.300







# 2. Restore pinned local dotnet-ef tool



dotnet tool restore



```







---







## Local Database Setup (No Admin, Docker-Free)







AuditSphereOps utilizes a user-local PostgreSQL 18.6 development cluster running on port **5433** with trust authentication on loopback (`127.0.0.1`). Defaults: databases `auditsphere` and `auditsphere_tests`.







### macOS (Homebrew)







```bash



# Start the PostgreSQL 18 cluster on port 5433



pg_ctl -D /opt/homebrew/var/postgresql@18 -o "-p 5433" start







# Check cluster status



pg_ctl -D /opt/homebrew/var/postgresql@18 status







# Run the database restore drill



scripts/db/restore-drill.sh



```







### Windows (PowerShell)







```powershell



# Check PostgreSQL status and ensure development databases exist



powershell -NoProfile -ExecutionPolicy Bypass -File scripts\db\status.ps1







# Start the cluster idempotently



powershell -NoProfile -ExecutionPolicy Bypass -File scripts\db\start.ps1







# Fast cluster shutdown



powershell -NoProfile -ExecutionPolicy Bypass -File scripts\db\stop.ps1



```







*Configuration Overrides:* `PGSQL_HOME`, `PGSQL_DATA`, `PGSQL_PORT`, `AUDITSPHERE_TEST_CONNECTION`. The scripts automatically validate the PostgreSQL major version.







---







## Build, Migrate, Test & Benchmark







### Standard Verification Sequence







Execute this sequence to verify the codebase against the local PostgreSQL instance:







```bash



# 1. Build the entire solution in Release mode



dotnet build src/AuditSphereOps.Web/AuditSphereOps.Web.csproj --configuration Release







# 2. Verify EF Core model changes against the applied migrations



dotnet ef migrations has-pending-model-changes \



  --project src/AuditSphereOps.Infrastructure \



  --startup-project src/AuditSphereOps.Web \



  --configuration Release







# 3. Run the complete PostgreSQL-backed domain and integration test suite



dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-build







# 4. Run API security and transfer tests



dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj --no-build







# 5. Run the representative accounting benchmark



dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \



  --no-restore \



  --filter 'FullyQualifiedName~AccountingBenchmarkTests'



```







The hosted GitHub Actions workflow is a **mandatory build gate only**: pinned SDK check, locked restore, Release build and `dotnet ef migrations has-pending-model-changes`. It does not execute the test suites, so a green CI badge is not test acceptance — the PostgreSQL-backed suites above are the test evidence and run locally (or in an explicitly configured test runner) against a real PostgreSQL 18.6 instance.







### Database Test Isolation







The integration test suite connects to `127.0.0.1:5433` and the `auditsphere_tests` database. Every test dynamically provisions a **uniquely named disposable PostgreSQL schema**, applies all migrations, runs assertions, and drops the schema upon completion. There is **no InMemory provider fallback**; every query and trigger runs against genuine PostgreSQL.







---







## Durable Operations & Worker Architecture







Background and long-running operations are processed via the outbox pattern in `AuditSphereOps.Worker`:







- **Operation Types:** GL completeness bridge calculation, financial package builds, exact-byte rendering, and archive bundle assembly.



- **Worker Deployment Roles:**



  - `general`: Orchestration, notification dispatch, and standard background work.



  - `processing`: Heavy computational loads (GL digestion, trial balance sealing, statement calculation).



  - `records`: Purview archive assembly and document package rendering.



- **Fault Recovery:** Workers lease tasks via PostgreSQL `FOR UPDATE SKIP LOCKED`. If a worker dies, expired leases are reclaimed after 60 seconds. Tasks failing after 5 attempts enter a quarantined disposition for human review.







---







## Verification & Evidence Discipline







AuditSphereOps operates under strict evidence-driven development principles:







- **`docs/execution/status.json`:** The single source of truth for repository reality: specification SHA-256 hash, baseline commit, active slice, verified test evidence, external blockers, and permitted actions.



- **`docs/execution/auditsphere-execution-current-slice.md`:** Concise serialization of the active vertical slice, verified local state, and operational runbook.



- **Zero-Warning Tolerance:** All code compiles with `0 Warning(s)` and `0 Error(s)` in Release mode under .NET 10.







---







## Implementation Roadmap & External Gates







Work is sequenced in dependency-ordered work packages (P0–P10 per [docs/execution/auditsphere-execution-pending-tasks.md](docs/execution/auditsphere-execution-pending-tasks.md)):







| Phase | Gate / Objective | Status | Description |



|---|---|---|---|



| **P0** | Core Domain & Ledger Foundation | `LOCAL_VERIFIED` | Modular monolith architecture, Practice, Accounting, and Consolidation cores. |



| **P1** | Live Entra OIDC Authentication | `BLOCKED_EXTERNAL` | Requires live Azure AD / Entra ID tenant registration. |



| **P2** | Live SharePoint/Graph Documents | `BLOCKED_EXTERNAL` | Requires live Microsoft 365 tenant with selected-resource scopes. |



| **P3** | External Release Checkpoint Store | `BLOCKED_EXTERNAL` | Requires production cryptographic checkpoint infrastructure. |



| **P4** | Records Retention & Manifest Lineage | `LOCAL_VERIFIED` | Preserves exact uploaded document evidence, SHA-256 identities, and immutable archive manifests (Purview provider integration excluded from product scope). |



| **P5** | Document Signing Evidence Lineage | `LOCAL_VERIFIED` | Preserves exact uploaded signed-document evidence, SHA-256 identities, and human decisions without claiming external eSignature provider integration (excluded from product scope). |



| **P6** | Records / Archive Residual Hardening | `LOCAL_VERIFIED` | Immutable archive schema, lineage manifests, and structured exports. |



| **P7** | Cross-Store Recovery (RPO/RTO) | `BLOCKED_EXTERNAL` | Requires multi-region cloud backup infrastructure. |



| **P8** | Production Observability & Secrets | `BLOCKED_EXTERNAL` | Requires Azure Key Vault and production OpenTelemetry collector. |



| **P9** | Independent Review Governance | `BLOCKED_EXTERNAL` | Requires independent partner sign-off and branch protections. |



| **P10**| Real-Tenant Acceptance (§47) | `BLOCKED_EXTERNAL` | Full operational pilot on customer-authorized tenant. |







---







## Documentation & Specifications







The complete documentation directory, authority hierarchy, and reading guide is maintained in the central documentation index:







👉 **[`docs/auditsphere-docs-index.md`](docs/auditsphere-docs-index.md)**







| Document | Description |



|---|---|



| [docs/auditsphere-docs-index.md](docs/auditsphere-docs-index.md) | **Central Documentation Index** — global authority hierarchy, reading order, and document directory. |



| [docs/auditsphere-requirements-system-specification-current.md](docs/auditsphere-requirements-system-specification-current.md) | **Authoritative v5.0 Build Contract** — complete architecture, data models, and acceptance tests. |



| [docs/auditsphere-accounting-module-requirements-current.md](docs/auditsphere-accounting-module-requirements-current.md) | **Accounting & Consolidation Roadmap** — gap analysis, AC-01 to AC-28 user stories, and acceptance criteria. |



| [docs/architecture/auditsphere-architecture-current-architecture.md](docs/architecture/auditsphere-architecture-current-architecture.md) | **Current Architecture** — modular monolith structure, boundaries, and dependency guards. |



| [docs/architecture/auditsphere-architecture-code-map.md](docs/architecture/auditsphere-architecture-code-map.md) | **Architecture Code Map** — capability map and documentation authority index. |



| [docs/architecture/auditsphere-architecture-document-naming-policy.md](docs/architecture/auditsphere-architecture-document-naming-policy.md) | **Document Naming Policy** — naming conventions and rules for repository documentation. |



| [docs/task_breakdown/](docs/task_breakdown/) | **R2R Task Breakdown** — detailed task definitions and technical contracts for Modules 20–26. |



| [docs/execution/status.json](docs/execution/status.json) | **Execution Ledger** — live machine-readable progress pointer and verification evidence. |



| [docs/execution/auditsphere-execution-current-slice.md](docs/execution/auditsphere-execution-current-slice.md) | **Active Slice Runbook** — verified local state, tested SHA, and execution notes. |



| [AGENTS.md](AGENTS.md) | **Engineering Instructions & Invariants** — boundaries, safety rules, and development guidelines. |







---







## Contributing







All contributions follow the specification execution protocol:







1. **Consult the Spec First:** Review `docs/auditsphere-requirements-system-specification-current.md` (§§1–12, 22, 24, 27–33, 41–47) and `AGENTS.md` before making changes.



2. **One Vertical Slice at a Time:** Deliver the smallest coherent vertical slice in dependency order. Never weaken an existing authorization check or mandatory security control.



3. **Prove with Evidence:** Code must build with `0 Warning(s)` and pass targeted tests against the local PostgreSQL 18.6 cluster. Migrations must preserve append-only history.



4. **Honest Recording:** Update `docs/execution/status.json` and `docs/execution/auditsphere-execution-current-slice.md` with observed facts only. Never record an external gate as passed without live evidence.



5. **Respect Architecture Prohibitions:** Never introduce MediatR, Frappe, ERPNext, Python backends, React frontends, message brokers, or autonomous audit decision engines.
