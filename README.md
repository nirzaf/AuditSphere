# AuditSphereOps

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-18-4169E1)](https://www.postgresql.org/)
[![EF Core](https://img.shields.io/badge/EF%20Core-10-0078D4)](https://learn.microsoft.com/ef/core/)
[![License](https://img.shields.io/badge/license-informational-lightgrey)](LICENSE)

**AuditSphereOps** is a professional audit, accounting & assurance operations platform — a .NET 10 modular monolith covering the complete engagement lifecycle: client acceptance, practice management, trial-balance intake, financial-statement production, audit execution, review, controlled signing/release, and records retention.

> **Status: implementation in progress.** This repository is a specification-driven build, currently at checklist #14 of 14 dependency-ordered slices. A green UI or a mocked provider is **not** production evidence — external gates (Entra tenant, SharePoint grants, Purview profile, signing methodology) remain explicitly blocked until owner-authorized, and are never fake-passed.

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

- **`docs/execution/status.json`** — a pointer to current reality: specification SHA-256, baseline commit, active issue/branch/PR, implemented slice, test evidence, tenant evidence, open blockers, and the next permitted action. Re-read repo/GitHub state when resuming; the file never overrides actual reality.
- **`docs/execution/current-slice.md`** — the verified local state and concise serialization of what was actually executed.
- **Using the spec:** read `AuditSphereOps_NET_Codex_Implementation_Specification.md` (§§1–12, 22, 24, 27–33, 41–47) plus only the sections for the active issue.

As of the last verification pass: **93/93 tests passed** on PostgreSQL 18.6, fifteen migrations applied, local locked restore and build completed with zero warnings, the local restore rehearsal passed, readiness returned healthy with no pending migrations, and hosted CI run `35370149111` passed all steps. The Entra/SharePoint/Purview production gates remain **recorded blockers** pending owner-authorized evidence.

## Implementation roadmap

The build advances through **14 dependency-ordered checklist slices** (spec §31–§32), each landing as a reviewed PR with executed test evidence. Slices may not weaken controls, and a slice is accepted only with independent review evidence.

**Delivered (checklists #1–#12 plus initial #13/#14 controls and the local mapping/FS follow-up, verified locally — 93/93 tests, fifteen migrations):**

- Security & authorization integrity — `ActorContext`, scope/role matrix, firm→client→engagement guards, finance-role separation
- Trial-balance intake & validation engine — Appendix D fixture, database-level `ck_tb_validation_status` control
- Accounting integrity adjustments — source-reflection bridges, accounting-only scope
- Durable outbox & validation worker — `SKIP LOCKED` leases, reconciliation, recovery-safe migration
- Practice CRM workflow + safety invariants
- Practice time & budget workflow
- Billing artifacts — invoices, credit notes, receipts, allocations with balance limits, source-allocation uniqueness, posted-history immutability
- Bounded firm ledger — finance-role journal workflow, immutable balanced postings, source/retry uniqueness, reversals, period close/reopen, and database deferred-balance enforcement
- Document snapshots — scope-bound exact-byte SHA-256 capture, one snapshot per source version, legacy ambiguity refusal, and append-only database protection
- Revision/generation-bound approvals — immutable historical decisions, current applicability projection, and stale approval rejection
- Release-gate integrity — workpaper candidates bound to current approvals/generations/manifests, checkpoint-required immutable release events, scoped idempotency, and delivery outbox intent
- Mapping and financial-statement package foundation — approved versioned mappings, deterministic adjusted-TB snapshots/package lines, explicit review validations, and append-only artifact protection
- Initial scoped Blazor shell — live portfolio projections and a release command screen behind real authenticated-user-to-firm mapping; unauthenticated/local environments fail closed

**Remaining proof and scope (checklists #13–#14):**

- Financial-statement rendering, disclosures/cash-flow inputs, and the remaining entity foreign keys/data-model completion
- Live provider adapters (Entra/Graph/SharePoint/Purview) behind the external-effect fence
- Full UI surfaces and authorized operator recovery tooling
- Production-grade cross-store recovery

The external production gates (live Entra tenant, selected SharePoint grants, Purview records profile, signing methodology) stay **blocked** until owner-authorized evidence arrives; see `docs/execution/status.json`.

## Documentation

| Document | Purpose |
|---|---|
| `AuditSphereOps_NET_Codex_Implementation_Specification.md` | **Authoritative v5.0 build contract** — architecture, workflows, data model, acceptance tests, execution protocol |
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
