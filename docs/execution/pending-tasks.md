# AuditSphereOps — pending work checklist

**Repository:** `nirzaf/AuditSphere`
**Authoritative specification:** [`docs/SPECIFICATION.md`](../SPECIFICATION.md) v5.0
**Requirements backlog:** [`docs/requirements/AuditSphere_Audit_Workflow_Gap_Closure_User_Stories.md`](../requirements/AuditSphere_Audit_Workflow_Gap_Closure_User_Stories.md)
**Source implementation checkpoint:** `master@2aaa294`

This is a resume checklist, not an acceptance certificate. Tenant identifiers, credentials, tokens, secret values, and private-provider URLs are deliberately absent from the public repository.

## Verified local baseline

- [x] .NET 10 solution builds with zero warnings/errors.
- [x] PostgreSQL 18.6 development profile is used on loopback port 5433.
- [x] 192/192 PostgreSQL-backed tests pass with 0 skipped.
- [x] 50 migrations are applied; latest is `20260920223757_AddTypedSpecialistAreaInputs`.
- [x] EF model has no pending migration changes.
- [x] Loopback restore drill passes and writes secret-free evidence to `docs/evidence/restore-drill-latest.json`.
- [x] Generated Graphify output and local agent state are ignored and removed from Git tracking.
- [x] Public documentation no longer stores tenant-specific identity, site, app, or account identifiers.

## Local accounting and workflow follow-up

- [x] Client accounting profiles, periods, books, COA mappings and versioned TB profiles.
- [x] Atomic multi-entity TB batches, typed GL imports and bounded completeness bridges.
- [x] Reconciliation, ECL, inventory, specialist, analytical and journal-risk workbenches.
- [x] Entity package projections, equity, notes, comparatives, restatement lineage and bounded same-currency consolidation.
- [x] Journal lineage, close checks and immutable management/accounting/partner package-review decisions.
- [x] Add bounded, resumable GL chunk intake with canonical digest, idempotent retry and contiguous finalization.
- [x] Add a client-safe validated-package view and signed-in management acknowledgement route with cross-client denial coverage.
- [x] Add an authorized staff package-review queue with exact-version stage status and direct package handoff.
- [x] Bind validated financial-package release candidates to current management, accounting and partner decisions through the existing guarded approval/release path.
- [x] Add the completion-screen action that prepares a package-bound release candidate only after current package reviews are approved.
- [x] Bind ECL and inventory review decisions to the exact reconciliation source hash and client input generation.
- [x] Bind specialist and analytical review decisions to the exact client input generation.
- [x] Require explicit depreciation method/useful life and persist the calculated closing balance for asset schedules.
- [x] Add typed payroll, loan, equity, related-party, tax and going-concern forecast inputs with evidence-bound approval.
- [ ] Add approved-method fixtures for mixed currency, complex ownership, acquisition, NCI and advanced eliminations before implementing those methods.
- [ ] Add deeper audit workpaper/evidence links and account-area UI around the typed specialist schedules.
- [ ] Add the remaining dashboard, roll-forward and cross-workflow navigation.
- [ ] Re-run focused tests, full tests, build, migration drift and restore drill after every coherent slice.

## External gates

These items cannot be closed by local mocks, documentation, a browser login, or a developer-only simulation:

| Gate | Checklist | Status |
|---|---|---|
| P1 | [ ] Live Entra OIDC, runtime identity fixtures, wrong-tenant and disabled/revoked denial | `BLOCKED_EXTERNAL` |
| P2 | [ ] Selected-resource SharePoint/Graph upload, download, versioning, isolation and reconciliation | `BLOCKED_EXTERNAL` |
| P3 | [ ] Independently administered release checkpoint store, capability evidence and digest mismatch blocking | `BLOCKED_EXTERNAL` |
| P4 | [ ] Approved Purview records profile, reviewer fixtures, label application and protection/readback behavior | `BLOCKED_EXTERNAL` |
| P5 | [ ] Approved signing methodology, custody and exact-byte signature lineage | `BLOCKED_EXTERNAL` |
| P6 | [x] Records/archive residual hardening and evidence-safe downgrade behavior locally verified | `LOCAL_VERIFIED` |
| P7 | [ ] Custodially separate cross-store restore rehearsal with measured production RPO/RTO | `BLOCKED_EXTERNAL` |
| P8 | [ ] Production secret custody, observability, capacity and sensitive-log review | `BLOCKED_EXTERNAL` |
| P9 | [ ] Independent human review of the current head and protected-merge evidence | `BLOCKED_EXTERNAL` |
| P10 | [ ] Full §47 real-tenant acceptance cycle and professional sign-off | `BLOCKED_EXTERNAL` |

## Evidence boundaries

- Local PostgreSQL proves local persistence, constraints, migrations and deterministic behavior only.
- The loopback restore drill is not custodially separate and does not prove production RPO/RTO.
- Simulation adapters remain disabled for production configuration; no external effect is claimed from them.
- Professional conclusions, methodology approval, signing approval and independent review belong to named human owners.
- Do not add tenant-specific evidence to this repository. Store any authorized private evidence outside the public Git tree and record only a redacted status and retrieval procedure.

## Resume procedure

1. Re-read `AGENTS.md`, `docs/SPECIFICATION.md`, this checklist and the current GitHub head.
2. Inspect the worktree before editing; preserve unrelated dirty files.
3. Select one unchecked local item whose dependencies are satisfied.
4. Implement the smallest complete vertical slice with scope checks, immutable evidence and PostgreSQL tests.
5. Run build, focused tests, full tests, migration drift and restore drill as applicable.
6. Update `current-slice.md` and `status.json` with observed facts.
7. Commit and push the verified slice; do not rewrite history or claim an external gate.
