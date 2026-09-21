# AuditSphereOps — current state and active slice

This file records observed repository state only. The authoritative build contract is [`docs/SPECIFICATION.md`](../SPECIFICATION.md) v5.0. The public repository intentionally excludes tenant identifiers, user principals, credentials, tokens, secret values, and private-provider URLs.

## Current verified baseline

| Item | Observed value |
|---|---|
| Source implementation checkpoint | `master@40b25d7` |
| Remote | `origin/master` points to `40b25d7` after the method-acceptance, approved-component package and stale-run publication gate pushes |
| SDK | .NET 10; repository solution targets `net10.0` |
| Database | PostgreSQL 18.6 on loopback port 5433 for development only |
| Build | `dotnet build AuditSphereOps.slnx --no-restore --configuration Release` — passed, 0 warnings/errors |
| Tests | 196/196 passed, 0 skipped against PostgreSQL 18.6 |
| Migrations | 52 applied; latest `20260920230951_ClientPeriodAmendmentLineage` |
| Model drift | `dotnet ef migrations has-pending-model-changes` — no changes |
| Restore drill | `scripts/db/restore-drill.sh` — passed; 52 migrations reconciled |
| Production effects | Disabled locally; no production acceptance claimed |

## Implemented local capability

- Firm/client/engagement scope authorization, durable operations, trial-balance intake, audit planning, evidence submissions, package generation, records/archive lineage, recovery quarantine, and provider safety fences.
- Client accounting profiles, periods, books, chart-of-accounts mappings, versioned trial-balance profiles, signed-net/debit-credit normalization, and atomic multi-entity batches.
- Typed GL import, bounded paged reads, account-by-account trial-balance-to-GL completeness bridges, reconciliation workbenches, generation-bound ECL/inventory/specialist/analytical/journal-risk workbenches, and typed asset/payroll/loan/equity/related-party/tax/going-concern forecast schedules.
- Equity, notes, comparatives, closed-period restatement lineage, restricted same-currency consolidation, journal lineage, and close checks.
- Immutable package-review decisions for management, accounting, and partner stages. Decisions are bound to the exact package revision, generation and hash; evidence modes remain separated; append-only database protection is enforced.
- Consolidation perimeter approval requires an independently accepted group capability profile for the selected method; capability-preparer self-approval is rejected. Component approval requires current management, accounting and partner decisions for the exact package.
- Consolidation run manifests include component package hashes; approval recomputes current package, intercompany and group-journal inputs and blocks stale runs until a new run is built.
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
- [ ] Extend advanced accounting methods only where an approved method and test fixtures exist: mixed currency, complex ownership, acquisition, NCI, and advanced eliminations. The enabled first profile remains deliberately fail-closed.
- [x] Link accounting evidence to reviewed audit workpapers and expose account-area UI for the typed specialist schedules.
- [x] Add the accounting dashboard and cross-workflow navigation for accounting, roll-forward and release handoffs.
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
dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web
scripts/db/restore-drill.sh
```

The restore evidence is written to [`docs/evidence/restore-drill-latest.json`](../evidence/restore-drill-latest.json). The latest rehearsal restored 52 migrations through `20260920230951_ClientPeriodAmendmentLineage`; it is loopback-only and explicitly reports that external checkpoint custody and production RPO/RTO were not run.

## Resume rule

Before starting another slice, re-read `AGENTS.md`, this file, `docs/execution/status.json`, the Git worktree and current remote head. Preserve unrelated dirty work. Implement the smallest coherent local slice, verify it on PostgreSQL, commit and push that slice, then refresh the observed evidence. Never rewrite history or claim an external gate from local evidence.
