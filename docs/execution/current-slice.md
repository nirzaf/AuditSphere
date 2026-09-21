# AuditSphereOps — current state and active slice

This file records observed repository state only. The authoritative build contract is [`docs/SPECIFICATION.md`](../SPECIFICATION.md) v5.0. The public repository intentionally excludes tenant identifiers, user principals, credentials, tokens, secret values, and private-provider URLs.

## Current verified baseline

| Item | Observed value |
|---|---|
| Source implementation checkpoint | `master@aef442f` |
| Remote | `origin/master` includes source checkpoint `aef442f` |
| SDK | .NET 10; repository solution targets `net10.0` |
| Database | PostgreSQL 18.6 on loopback port 5433 for development only |
| Build | `dotnet build AuditSphereOps.slnx --no-restore --configuration Release` — passed, 0 warnings/errors |
| Tests | 205/205 passed, 0 skipped against PostgreSQL 18.6 |
| Migrations | 60 applied; latest `20260921052800_ClientAccountingDimensionDefinitions` |
| Model drift | `dotnet ef migrations has-pending-model-changes` — no changes |
| Restore drill | `scripts/db/restore-drill.sh` — passed; 60 migrations reconciled |
| Production effects | Disabled locally; no production acceptance claimed |

## Implemented local capability

- Firm/client/engagement scope authorization, durable operations, trial-balance intake, audit planning, evidence submissions, package generation, records/archive lineage, recovery quarantine, and provider safety fences.
- Client accounting profiles, periods, books, chart-of-accounts mappings, versioned trial-balance profiles, signed-net/debit-credit normalization, and atomic multi-entity batches.
- Client accounting setup/reporting objects default blank currency input to QAR; raw source/import currencies and FX policy currencies remain explicit.
- Client-scoped accounting dimension definitions cover branch, cost centre, department, project and intercompany counterparty codes; nonblank GL dimension values are rejected unless defined for the client, including bounded streaming imports.
- Client chart hierarchy parent lookups are restricted to the target chart version; cross-version parent references and posting-account parents are rejected.
- Draft taxonomy nodes can be added incrementally with parents from the same taxonomy version; cross-version parents and cycles are rejected.
- Trial-balance CSV/XLSX imports require and persist the selected reporting period, optional reporting book and normalized basis; import validation checks period currency/basis/book scope, and completeness/TB reconciliation rejects sources with mismatched stored context. Legacy direct fixtures remain nullable for additive migration compatibility.
- Typed GL import, bounded paged reads, account-by-account trial-balance-to-GL completeness bridges, reconciliation workbenches, generation-bound ECL/inventory/specialist/analytical/journal-risk workbenches, and typed asset/payroll/loan/equity/related-party/tax/going-concern forecast schedules.
- Equity, notes, comparatives, closed-period restatement lineage, restricted same-currency consolidation, journal lineage, and close checks.
- Immutable package-review decisions for management, accounting, and partner stages. Decisions are bound to the exact package revision, generation and hash; evidence modes remain separated; append-only database protection is enforced.
- Consolidation perimeter approval requires an independently accepted group capability profile for the selected method; capability-preparer self-approval is rejected. Component approval requires current management, accounting and partner decisions for the exact package.
- Consolidation component submissions must also match the package's period basis, taxonomy version and mapping-version ID; the deterministic run manifest preserves that exact component lineage.
- Consolidation run manifests include component package hashes; approval recomputes current package, intercompany and group-journal inputs and blocks stale runs until a new run is built.
- The bounded foreign-operation profile pins an approved rate set and translation policy to the scope, requires maker/checker approval for each foreign component translation, preserves per-line source/FX lineage in the deterministic manifest, and blocks missing or stale rates/packages. Full FX remeasurement/reserve, NCI, acquisition, ownership-change, nested-group and complex-elimination methods remain disabled pending approved method-specific fixtures.
- The enabled FX profile accepts only explicit `DIRECT` rates; unsupported inverse semantics are rejected rather than silently multiplied with the wrong direction.
- Group membership changes advance a durable group revision; consolidation scopes pin that revision and reject approval/calculation after a perimeter change while preserving historical memberships, scopes and runs.
- Component, translation, intercompany-match and group-journal commands recheck the pinned group revision and fail closed instead of writing stale-scope evidence after a perimeter change.
- Structured records exports preserve the related group perimeter, membership, scope, component, ownership/intercompany, consolidation journal/run, exchange-rate and translation lineage without copying unrelated client workpapers.
- Approved-prior-scope group roll-forward preserves the exact prior run hash, approved FX lineage hash/reserve and recurring-elimination manifest; prior journals are lineage only and are not auto-applied.
- Accounting evidence links are typed, scope-checked and bound to reviewed audit procedure results; the staff evidence queue exposes only the actor's explicit client scope, and records export preserves typed accounting lineage.
- Period close runs under a row lock, blocks matching financial packages without current management/accounting/partner approval, and authorized reopen creates an immutable append-only amendment record with the new working revision.
- Controlled roll-forward creates a new draft period, copies prior reporting books as drafts, and creates a hash/evidence-bound opening bridge without copying prior approvals.
- The accounting evidence queue links reviewed accounting evidence to the exact associated audit workpaper, and the accounting dashboard exposes scoped period status plus roll-forward/restatement handoffs.
- Client-safe validated-package view and signed-in management acknowledgement are available at the restricted client portal route. The portal exposes statement totals and package metadata only; internal review history and workpapers remain staff-only.
- An internal package-review queue lists only current validated packages in the actor's authorized client/engagement scopes and routes reviewers to the exact-version package surface.
- A validated financial package can now become a release candidate only through the existing guarded approval/release path; the candidate records `FINANCIAL_PACKAGE`, exact package revision/generation/hash, current management/accounting/partner decisions, and the normal checkpoint gate.
- The completion screen exposes package-candidate preparation only to partner/administrator actors after all three current package reviews are approved; repeated preparation reuses the exact candidate.
- ECL and inventory evidence records capture the reconciliation source hash and client input generation; review blocks when either source lineage or generation is stale.
- Bounded GL chunk intake with canonical content digests, transactional batch locking, idempotent retries, contiguous finalization and persisted accepted-count reconciliation.
- Source-bound GL reconciliations reject a sealed batch whose reporting period or currency differs from the selected period; PostgreSQL regression coverage passes.
- Reconciliation items are bound to the exact source currency, reject future item dates and missing dispositions, normalize accepted currency codes, and preserve as-of ageing; PostgreSQL regression coverage passes.
- GL completeness calculation can be enqueued as a local durable operation, with sealed-source revision fencing, operation completion lineage and repeat-enqueue idempotency; PostgreSQL regression coverage passes.
- Financial-package builds can be enqueued as local durable calculations, fenced to the approved mapping revision and finalized plan, committed atomically with operation completion, and re-enqueued idempotently; PostgreSQL regression coverage passes.
- Financial-package records inherit the source dataset's selected reporting period, optional book and normalized basis, validate period dates and book/basis/currency lineage, persist the context with scope FKs, and include it in the deterministic package hash; legacy direct fixtures remain nullable for additive compatibility.
- Context-bound adjustment journals inherit and persist the validated source dataset's period, book, basis and currency, reject a book from another period, and preserve that reporting context through management decisions, posting, reversal and source-reflection lineage; PostgreSQL regression coverage passes.
- Financial-package rendering can be enqueued as a local durable calculation, fenced to the exact package revision, and records the deterministic artifact digest for later byte verification; PostgreSQL regression coverage passes.
- The PostgreSQL-backed accounting benchmark exercises four clients, 2,000 transactions, 8,000 GL lines, parallel enqueueing, two concurrent durable workers, a 32-line group calculation, six-decimal/high-magnitude amounts and paged reads; one observed run measured enqueue 147.8 ms, worker processing 134.4 ms, first page 43.8 ms and group calculation 3.2 ms.
- Blazor status surfaces for the implemented workflows, including period restatement and truthful release/package gate state.

## Remaining local implementation work

These are product gaps, not claims of production readiness:

- [x] Complete the service-level release handoff from exact package-review decisions to a package-bound release candidate; client management acknowledgement and the staff review queue remain available.
- [x] Bind ECL and inventory review applicability to the exact reconciliation source and client generation.
- [x] Require explicit depreciation method/useful life and persist the calculated closing balance for asset schedules.
- [x] Add typed payroll, loan, equity, related-party, tax and going-concern forecast inputs with evidence-bound approval.
- [x] Link typed accounting evidence to reviewed audit procedure results and expose a scoped evidence queue; preserve the links in records exports.
- [x] Govern package-aware period close and immutable authorized reopen/amendment lineage.
- [x] Add controlled entity-period roll-forward with draft book copies and explicit opening-balance evidence.
- [x] Add the bounded approved foreign-operation translation profile with pinned rate/policy inputs, maker/checker review, source/FX lineage and stale-input blocking.
- [ ] Extend advanced accounting methods only where an approved method and test fixtures exist: full FX remeasurement/reserve, complex ownership, acquisition, NCI, nested groups, and advanced eliminations. The enabled first profile remains deliberately fail-closed.
- [x] Link accounting evidence to reviewed audit workpapers and expose account-area UI for the typed specialist schedules.
- [x] Add the accounting dashboard and cross-workflow navigation for accounting, roll-forward and release handoffs.
- [x] Include accounting/group dependencies in structured records exports while retaining the existing records-profile and legal-hold gates.
- [x] Carry approved group opening consolidation lineage across scope versions without duplicating prior journals.
- [x] Reject component, translation, intercompany-match and group-journal writes against a changed group revision.
- [x] Bind GL reconciliation sources to the selected reporting period and currency; reject cross-period or cross-currency batches.
- [x] Bind reconciliation items to the selected source currency; reject future dates and missing dispositions while preserving as-of ageing.
- [x] Route GL completeness calculation through the existing durable operation infrastructure with source revision fencing and idempotent retries.
- [x] Route financial-package builds through the existing durable operation infrastructure with mapping/plan fencing and idempotent retries.
- [x] Bind context-bound financial packages to the selected client reporting period, optional book, basis and currency, including the package hash and period-date validation.
- [x] Bind context-bound adjustment journals to the validated client reporting period, optional book, basis and currency; reject cross-period books and preserve the context through journal lineage.
- [x] Validate nonblank GL dimension values against client-scoped definitions and default accounting setup/reporting currency to QAR without defaulting source evidence.
- [x] Route financial-package rendering through the existing durable operation infrastructure with exact package-revision fencing and deterministic artifact-digest verification.
- [x] Benchmark representative accounting workloads before production acceptance; the current local workload evidence is recorded above and does not establish production capacity or RPO/RTO.
- [x] Re-run focused tests, full tests, build, migration drift and restore drill for the current coherent slice; repeat this checklist for the next slice.

## External acceptance gates

Local code and PostgreSQL evidence cannot close these gates:

| Gate | Required evidence | Status |
|---|---|---|
| P1 | Live Entra OIDC, runtime identity fixtures, wrong-tenant/disabled denial | `BLOCKED_EXTERNAL` |
| P2 | Selected-resource SharePoint/Graph upload, download, versioning and reconciliation | `BLOCKED_EXTERNAL` |
| P3 | Independently administered release checkpoint store and capability evidence | `BLOCKED_EXTERNAL` |
| P4 | Approved Purview records profile, reviewer fixtures and observed protection behavior | `BLOCKED_EXTERNAL` |
| P5 | Approved signing methodology and exact-byte signature lineage | `BLOCKED_EXTERNAL` |
| P7 | Custodially separate restore rehearsal with measured production RPO/RTO | `BLOCKED_EXTERNAL` |
| P8 | Production secret custody, telemetry, capacity and no-sensitive-log evidence | `BLOCKED_EXTERNAL` |
| P9 | Independent human review and protected-merge evidence | `BLOCKED_EXTERNAL` |
| P10 | Full §47 real-tenant acceptance cycle and professional sign-off | `BLOCKED_EXTERNAL` |

No fixture, local adapter, documentation statement, or browser login is treated as a substitute for the required external evidence.

## Verification commands

```text
dotnet build AuditSphereOps.slnx --no-restore
dotnet test AuditSphereOps.slnx --no-build
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore --filter 'FullyQualifiedName~AccountingBenchmarkTests'
dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web
scripts/db/restore-drill.sh
```

The restore evidence is written to [`docs/evidence/restore-drill-latest.json`](../evidence/restore-drill-latest.json). The latest rehearsal restored 60 migrations through `20260921052800_ClientAccountingDimensionDefinitions`; it is loopback-only and explicitly reports that external checkpoint custody and production RPO/RTO were not run.

## Resume rule

Before starting another slice, re-read `AGENTS.md`, this file, `docs/execution/status.json`, the Git worktree and current remote head. Preserve unrelated dirty work. Implement the smallest coherent local slice, verify it on PostgreSQL, commit and push that slice, then refresh the observed evidence. Never rewrite history or claim an external gate from local evidence.
