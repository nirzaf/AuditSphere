# AuditSphereOps

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-18-4169E1)](https://www.postgresql.org/)
[![EF Core](https://img.shields.io/badge/EF%20Core-10-0078D4)](https://learn.microsoft.com/ef/core/)
[![License](https://img.shields.io/badge/license-informational-lightgrey)](LICENSE)

**AuditSphereOps** is a professional audit, accounting & assurance operations platform — a .NET 10 modular monolith covering the complete engagement lifecycle: client acceptance, practice management, trial-balance intake, financial-statement production, audit execution, review, controlled signing/release, and records retention.

> **Status: implementation in progress.** This repository is a specification-driven build. Core domain modules, practice management, period/book/basis-bound trial-balance and bounded resumable GL intake, client-scoped accounting dimensions, chart/taxonomy hierarchy controls, QAR setup defaults, GL service-date lineage, opening/movement completeness, deterministic journal-risk review indicators, exact-journal difference-impact classification, governed difference correction states and evaluation metadata, audit planning, typed bank reconciliation and confirmation workflows, package production, archive lineage, provider safety fences, recovery quarantine, queued-operation cancellation, governed financial-package review decisions, exact-byte reviewed package artifacts, package-bound release candidates, source-bound accounting-evidence freshness, typed specialist schedules, explicit asset-schedule methodology, the client-safe management package view, the authorized staff review queue, source-bound reconciliation-item and typed ageing validation, durable GL/package processing, context-bound adjustment journals, analytical-review replay lineage and reviewer conclusions, secured analytical-review aggregate summaries, booked-versus-calculated valuation differences linked to scoped proposed adjustments, versioned FX ranges and approved rate-rule enforcement, and deterministic advanced-consolidation safety cores are implemented and verified locally (**218/218 tests passing on PostgreSQL 18.6 across 79 migrations**). Live provider behavior, compliance approval, signing methodology, production recovery, approved advanced-method fixtures, and independent review remain explicitly blocked and are never fake-passed.

## Table of contents

- [Project objectives](#project-objectives)
- [Architecture](#architecture)
- [Solution layout](#solution-layout)
- [Key engineering invariants](#key-engineering-invariants)
- [Technology baseline](#technology-baseline)
- [Getting started](#getting-started)
- [Local database (no admin, Docker-free)](#local-database-no-admin-docker-free)
- [Build, migrate, test](#build-migrate-test)
- [Verification & evidence discipline](#verification--evidence-discipline)
- [Implementation roadmap](#implementation-roadmap)
- [Documentation](#documentation)
- [Contributing](#contributing)

## Project objectives

1. **Complete professional lifecycle, not a scaffold.** Deliver dependency-ordered vertical slices with working screens, commands, persistence, scoped authorization, append-only audit evidence, tests, documents and operating procedures.
2. **Exact-version approvals and release safety.** Synchronous, release-safe generation; version-bound document approvals; durable operations with at-most-once external effects, reconciliation and recovery quarantine.
3. **Native practice management.** CRM, time budgets, billing artifacts (invoices, credit notes, receipts, allocations) and a bounded firm ledger replace any reused ERP surface — without becoming a full ERP.
4. **Microsoft services as managed dependencies.** Entra ID (identity), Graph + SharePoint Online (documents), Purview (retention/records) stay external; the platform is dual-scope capable: accounting engagements and financial-statement audits.
5. **Honest evidence.** Every claim maps to executed test evidence in `docs/execution/status.json`; external blockers are recorded as blocked, never simulated.

## Architecture

Modular monolith on **ASP.NET Core with Blazor Interactive Server**, layered so domain logic stays persistence-agnostic and all external professional effects are fenced:

```
┌────────────────────────────────────────────────────────────┐
│  Web (Blazor Interactive Server)   Worker (durable ops)    │
│  /health/live · /health/ready      general/processing/    │
│  Npgsql readiness probe            records deployments     │
└──────────────┬──────────────────────────────┬──────────────┘
               │      Application layer       │
               │  ActorContext · scope/role   │
               │  authorization · TB engine · │
               │  durable-operation services  │
               └──────────────┬───────────────┘
                              │
               ┌──────────────┴───────────────┐
               │    Infrastructure (EF Core)  │
               │  AuditSphereDbContext         │
               │  snake_case · numeric(19,6)   │
               │  append-only audit evidence   │
               └──────────────┬───────────────┘
                              │
                    ┌─────────┴─────────┐
                    │  PostgreSQL 18    │
                    │  loopback :5433   │
                    │  (dev, trust auth)│
                    └───────────────────┘

  External (never faked, gated by ExternalEffects.Enabled=false):
  Microsoft Entra ID · Microsoft Graph · SharePoint Online · Microsoft Purview
```

**Domain modules:** Security, Practice (CRM / time / billing / firm ledger), Acceptance, Engagements, Documents, Accounting, Audit, Reviews, Completion, Records.

## Solution layout

| Project | Role |
|---|---|
| `src/AuditSphereOps.Domain` | Entities & invariants across all lifecycle modules |
| `src/AuditSphereOps.Application` | `ActorContext`, scope/role authorization, trial-balance calculator (Appendix D fixture), durable-operation services |
| `src/AuditSphereOps.Infrastructure` | `AuditSphereDbContext` (snake_case, `numeric(19,6)` monetary precision) + `Persistence/Migrations` |
| `src/AuditSphereOps.Web` | Blazor Interactive Server host; `/health/live`, `/health/ready` (Npgsql probe) |
| `src/AuditSphereOps.Worker` | Durable-operation deployment roles: general, processing, records |
| `tests/AuditSphereOps.Domain.Tests` | Arithmetic unit tests + one PostgreSQL integration test (refuses InMemory fallback) |

Shared configuration: `Directory.Build.props` (common compiler settings), `Directory.Packages.props` (central package versions), `global.json` (SDK 10.0.300 pin), `.config/dotnet-tools.json` (local `dotnet-ef`).

## Key engineering invariants

- **Money is `numeric(19,6)`,** persisted and calculated in PostgreSQL-validated arithmetic; a balanced 14-account fixture anchors trial-balance tests.
- **Durable outbox with exactly-once discipline:** deterministic dataset/revision keys, `SKIP LOCKED` claiming with leases (60 s lease, 20 s renewal, 5 attempts, persisted exponential backoff), immutable request/attempt/event evidence, digest-verified reconciliation, and `RESULT_UNCERTAIN` for proofless-after-effect outcomes.
- **Append-only evidence:** completed results, posted billing history, and operation events cannot be rewritten — enforced at the database with triggers and checks, not only in C#.
- **Scope-checked authorization everywhere:** firm → client → dataset/engagement lock ordering; finance-role separation and finance-profile gating on billing.
- **Firm-ledger integrity:** finance-role journal approval, exact balanced immutable postings, source/retry uniqueness, reversal links, and shared firm/period locks for posting and close.
- **Never-fake external effects:** `ExternalEffects.Enabled=false` locally; simulation handlers exist for tests only (Test environment + explicit `AllowSimulationAdapters=true`), and they are not live adapters.
- **Fail-closed migrations:** migrations refuse to discard append-only evidence, refuse ambiguous legacy billing data, and web startup never auto-applies them.

## Technology baseline

| Component | Version / choice |
|---|---|
| .NET SDK | 10.0.300 (pinned via `global.json`) |
| EF Core | 10.0.12 |
| Npgsql EF provider | 10.0.0 |
| PostgreSQL | 18.6 (user-local, development; production uses a managed instance) |
| UI | Blazor Web App, Interactive Server rendering |
| Identity | Microsoft Entra ID (live OIDC gated — see [status](#verification--evidence-discipline)) |

## Getting started

### Prerequisites

- .NET SDK **10.0.300** (`global.json` enforces the exact version)
- PostgreSQL 18.x reachable locally
- PowerShell (for the database helper scripts)

### Verify the toolchain

```powershell
dotnet --version        # must report 10.0.300
dotnet tool restore     # installs the pinned local dotnet-ef
```

## Local database (no admin, Docker-free)

A user-local PostgreSQL **18.6** development cluster — loopback only, trust auth, **development only, never production** — created by `scripts/db/init-cluster.ps1`. Defaults: port **5433**, databases `auditsphere` and `auditsphere_tests`.

```powershell
# Execution policy requires the bypass flag on this workstation (per invocation, not a system change).
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/db/status.ps1   # version + databases + migrations
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/db/start.ps1    # idempotent start + ensure databases
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/db/stop.ps1     # fast shutdown
```

Overrides: `PGSQL_HOME`, `PGSQL_DATA`, `PGSQL_RUN`, `PGSQL_LOG`, `PGSQL_PORT`. Scripts refuse a wrong major version.

## Build, migrate, test

```powershell
dotnet restore AuditSphereOps.slnx --locked-mode
dotnet build AuditSphereOps.slnx --no-restore
dotnet ef database update --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web
dotnet test AuditSphereOps.slnx
```

The integration test requires PostgreSQL at `127.0.0.1:5433` and the `auditsphere_tests` database; set `AUDITSPHERE_TEST_CONNECTION` if your credentials differ. It migrates a **random disposable schema** and cleans only what it created — a missing or wrong-version server fails the test; there is **no InMemory fallback**.

### Worker startup requirements

`ConnectionStrings__AuditSphere`, `Worker__FirmId`, Development/Test environment, `ExternalEffects__Enabled=false`, and `Worker__DeploymentEpoch` consistent with the firm's safety state. Firm/client safety guards must already exist; no worker creates them opportunistically.

## Verification & evidence discipline

- **`docs/execution/status.json`** — a public-safe pointer to current reality: specification SHA-256, baseline commit, active slice, local test evidence, external gates, and the next permitted action. Tenant identifiers, credentials, and secret values are intentionally excluded. Re-read repo/GitHub state when resuming; the file never overrides actual reality.
- **`docs/execution/current-slice.md`** — the verified local state and concise serialization of what was actually executed.
- **Using the spec:** read `docs/SPECIFICATION.md` (§§1–12, 22, 24, 27–33, 41–47) plus only the sections for the active issue.

As of the last verification pass at `master@bb891d2`: **218/218 tests passed** on PostgreSQL 18.6 with 0 skipped, 79 migrations applied (latest: `20260921204723_AddExchangeRateSetEffectiveRange`), local restore and build completed with zero warnings, the loopback restore rehearsal passed with 79 migrations, and the EF model has no pending changes. The verified slice includes QAR defaults for blank client accounting/reporting inputs, deterministic journal-risk review indicators, exact-journal difference-impact classification, governed difference correction states with materiality/qualitative metadata and stale-impact rejection, typed bank reconciliation sourced from independently approved ledger and statement schedules, queued-operation cancellation with an explicit administrator disposition, explicit receivable/payable ageing evidence, scoped audit-difference summaries, service-permissibility gating, versioned FX ranges with approved rate-rule enforcement, persisted FX/rounding lineage, and deterministic advanced-consolidation calculation cores. The complete local evidence is recorded in [`docs/execution/current-slice.md`](docs/execution/current-slice.md) and [`docs/execution/status.json`](docs/execution/status.json); live provider behavior, compliance approval, signing methodology, production recovery, approved advanced-method fixtures, and independent review remain recorded blockers.

## Implementation roadmap

The build advances through dependency-ordered work packages (P0–P10 per the [pending-work story](docs/execution/pending-tasks.md)), each landing as a reviewed PR with executed test evidence. Slices may not weaken controls, and a slice is accepted only with independent review evidence.

**Delivered (locally verified — 218/218 tests, 79 migrations):**

- **Security & authorization integrity:** `ActorContext`, scope/role matrix, firm→client→engagement guards, finance-role separation, in-command authorization for all planning and operational commands.
- **Trial-balance intake & validation engine:** Appendix D fixture, database-level `ck_tb_validation_status` control, source reflection bridges.
- **Durable outbox & validation worker:** `SKIP LOCKED` leases, reconciliation, recovery-safe migration, operator recovery controls (`/app/operations`).
- **Practice management:** CRM workflow (lead→opportunity→proposal→client), time & budget approval/correction chain, billing artifacts (invoices, credit notes, receipts, allocations), and bounded firm ledger with immutable postings.
- **Document snapshots & approvals:** Scope-bound exact-byte SHA-256 capture, revision/generation-bound approvals with stale rejection, release-gate candidates.
- **Financial-statement production:** Approved versioned mappings, deterministic adjusted-TB snapshots, opening/closing cash-flow bridge, disclosure validations, exact-byte package-artifact persistence with SHA-256 digest verification, and artifact-bound review decisions.
- **Trusted PBC upload transport:** Same-origin 8 MiB chunk staging outside webroot, capability-bound sequencing, reviewer-gated completion, and durable handoff.
- **Core entity catalog & questionnaire banks (§27.2, §13, §14):** `EngagementAssignment`, `EqrCase`, `WrittenRepresentation`, `SpecialistClearance`, `SourceReceipt`, `EvidenceLink`, `QuestionnaireTemplate`, `QuestionDefinition`; full seeding of 62 Client Evaluation (`CE-001`..`CE-097`) and 30 Review (`RV-001`..`RV-030`) question banks.
- **Audit planning lifecycle & scope integrity (§§19–23, 27.4–27.6, 42.3–42.4):** EF-owned models for `MaterialityAssessment`, `PopulationVersion`, `WorkpaperSubmission`, `AuditRisk`, `AuditProcedure`, `Finding`; composite scope foreign keys with `ON DELETE RESTRICT` across all evidence tables; database append-only triggers on materiality, populations, and workpaper submissions; freeze trigger on submitted workpapers.
- **Complete staff & client route catalog with UI ground truth:** Blazor Interactive Server screens for Portfolio, Clients, Engagements, Leads, Time, Invoices, Finance, Operations, Administration, Journals, Packages, Audit Plans, Populations, Workpapers, Findings, Assessments, Review Points, and Client Portal. All fabricated state, dummy IDs, and simulated approvals purged in favor of truthful persisted state.
- **Records/archive local model & version lineage (§25, PR #11, P6):** `RecordsProfile`, `ArchiveManifest`, `ArchiveManifestEntry`, structured export, `RecordsAction`, `LegalHold`, archive state machine, requested-vs-observed protection state, deterministic re-archive with predecessor/supersession version lineage (`predecessor_manifest_id`, `superseded_by_manifest_id`), legal-hold disposition blocking, approved profile version requirement, digest stability, and truthful archive UI.
- **Repository binding (§27.2, PR #11):** Tenant/site/drive/root binding records, document references tied to approved bindings, capability records, sync cursor model.
- **Provider safety fences (§43.8, PR #11):** Live Graph provider boundaries fail closed (`live-provider-not-approved`); production startup rejects simulation configuration; external effects remain disabled by default.
- **Recovery controls (§24.4/§45, PR #11):** Recovery sessions, recovery epoch, `RECOVERY_QUARANTINE`, authorized restart, stale-worker fencing, machine-readable restore evidence.
- **Client accounting and package governance:** Client accounting profiles, periods/books, chart-of-accounts mappings, versioned trial-balance profiles and multi-entity batches, typed GL imports, bounded resumable GL chunks with canonical digests and idempotent finalization, paged completeness bridges, source-bound reconciliations, generation-bound ECL/inventory/specialist/analytical evidence reviews, typed payroll/loan/equity/related-party/tax/forecast schedules, explicit asset-schedule depreciation method/useful-life and closing-balance controls, entity-package projections, restatement lineage, bounded same-currency consolidation, journal lineage, governed difference correction states and evaluation metadata, close checks, exact-byte rendered package artifacts, immutable management/accounting/partner package-review decisions bound to the exact artifact and package revision/generation/hash, completion-screen preparation of package-bound release candidates through the existing guarded release path, a client-safe validated-package management acknowledgement flow, and an authorized staff review queue.

**Remaining work — dependency-ordered (P1–P10):**

| Phase | Gate | Status |
|---|---|---|
| P1 | Live Entra OIDC + runtime identity fixtures | BLOCKED_EXTERNAL |
| P2 | Live bounded SharePoint/Graph document provider | BLOCKED_EXTERNAL |
| P3 | External release checkpoint store + capability evidence | BLOCKED_EXTERNAL |
| P4 | Purview records profile + reviewer fixtures + behavior evidence | BLOCKED_EXTERNAL |
| P5 | Approved signing methodology + signature lineage | BLOCKED_EXTERNAL |
| P6 | Records/archive residual hardening | LOCAL_VERIFIED |
| P7 | Cross-store recovery + production RPO/RTO | BLOCKED_EXTERNAL |
| P8 | Production secrets/observability/capacity | BLOCKED_EXTERNAL |
| P9 | Independent review + protected merge governance | BLOCKED_EXTERNAL |
| P10 | Full §47 real-tenant acceptance cycle | BLOCKED_EXTERNAL |

See [`docs/execution/pending-tasks.md`](docs/execution/pending-tasks.md) for the full dependency story and acceptance criteria.

The remaining external production gates (live Entra runtime identity/provider, Purview protection readback and compliance approval, signing methodology, recovery, and independent review) stay **blocked** until owner-authorized evidence arrives; see `docs/execution/status.json`.

## Documentation

| Document | Purpose |
|---|---|
| `docs/SPECIFICATION.md` | **Authoritative v5.0 build contract** — architecture, workflows, data model, acceptance tests, execution protocol |
| `docs/execution/status.json` | Live progress pointer: evidence, blockers, next step |
| `docs/execution/current-slice.md` | Verified local state + runbook for the current slice |
| `AGENTS.md` | Short agent pointer to the specification (never a full copy) |

## Contributing

This is a contract-driven build, so contributions follow the specification's execution protocol:

1. **Read first.** The spec intro and §§1–12, 22, 24, 27–33, 41–47, plus the sections relevant to your slice. `AGENTS.md` governs agent/repository safety.
2. **One slice at a time.** The smallest coherent vertical slice, in the dependency order of the checklist; a ticket may narrow work but never weaken a mandatory control or silently enable an unsupported service.
3. **Prove it.** `dotnet build` (zero warnings) + targeted `dotnet test`; use the PostgreSQL profile whenever the database is touched. Database changes ship as guarded EF Core migrations that never discard append-only evidence.
4. **Record honestly.** Update `docs/execution/status.json` and `docs/execution/current-slice.md` with observed facts only — never claim a check passed because a prior agent wrote that it should.
5. **Respect the fences.** No Frappe/ERPNext/Python backend/React frontend/second ERP, no second-wide-scope Graph permissions, no fake-passed external gates, no destructive repository rewrites.

External blockers (Entra tenant authorization, SharePoint grants, Purview profile, signing approvals) belong to the platform owner, not a code change — record them, don't simulate them.
