# AuditSphereOps — current state and active slice

This file records observed repository state only. The authoritative build contract is [`docs/SPECIFICATION.md`](../SPECIFICATION.md) v5.0. The public repository intentionally excludes tenant identifiers, user principals, credentials, tokens, secret values, and private-provider URLs.

## Current verified baseline

| Item | Observed value |
|---|---|
| Branch/head | `master@ab7c4fe001a1b342880aae6bbca1bbc157c9716a` |
| Remote | `origin/master` points to the same head after the latest push |
| SDK | .NET 10; repository solution targets `net10.0` |
| Database | PostgreSQL 18.6 on loopback port 5433 for development only |
| Build | `dotnet build AuditSphereOps.slnx --no-restore` — passed, 0 warnings/errors |
| Tests | 186/186 passed, 0 skipped against PostgreSQL 18.6 |
| Migrations | 44 applied; latest `20260920195800_FinancialPackageReviewDecisions` |
| Model drift | `dotnet ef migrations has-pending-model-changes` — no changes |
| Restore drill | `scripts/db/restore-drill.sh` — passed; 44 migrations reconciled |
| Production effects | Disabled locally; no production acceptance claimed |

## Implemented local capability

- Firm/client/engagement scope authorization, durable operations, trial-balance intake, audit planning, evidence submissions, package generation, records/archive lineage, recovery quarantine, and provider safety fences.
- Client accounting profiles, periods, books, chart-of-accounts mappings, versioned trial-balance profiles, signed-net/debit-credit normalization, and atomic multi-entity batches.
- Typed GL import, bounded paged reads, account-by-account trial-balance-to-GL completeness bridges, reconciliation workbenches, ECL/inventory/specialist/analytical/journal-risk workbenches, and entity package projections.
- Equity, notes, comparatives, closed-period restatement lineage, restricted same-currency consolidation, journal lineage, and close checks.
- Immutable package-review decisions for management, accounting, and partner stages. Decisions are bound to the exact package revision, generation and hash; evidence modes remain separated; append-only database protection is enforced.
- Blazor status surfaces for the implemented workflows, including period restatement and truthful release/package gate state.

## Remaining local implementation work

These are product gaps, not claims of production readiness:

- [ ] Add streaming-scale GL intake with bounded memory and durable resumability.
- [ ] Add complete user-facing management/audit/partner package-review and release workflow surfaces.
- [ ] Extend advanced accounting methods only where an approved method and test fixtures exist: mixed currency, complex ownership, acquisition, NCI, and advanced eliminations.
- [ ] Deepen specialist schedules and account-area workpapers where the workflow stories still require more than the current bounded workbench.
- [ ] Add the remaining dashboard, roll-forward, and cross-workflow navigation needed for operational usability.
- [ ] Re-run the focused and full verification suite after each coherent slice and update this file plus `status.json` with observed results.

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

The restore evidence is written to [`docs/evidence/restore-drill-latest.json`](../evidence/restore-drill-latest.json). It is loopback-only and explicitly reports that external checkpoint custody and production RPO/RTO were not run.

## Resume rule

Before starting another slice, re-read `AGENTS.md`, this file, `docs/execution/status.json`, the Git worktree and current remote head. Preserve unrelated dirty work. Implement the smallest coherent local slice, verify it on PostgreSQL, commit and push that slice, then refresh the observed evidence. Never rewrite history or claim an external gate from local evidence.
