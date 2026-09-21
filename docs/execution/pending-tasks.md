# AuditSphereOps — pending work checklist

**Repository:** `nirzaf/AuditSphere`
**Authoritative specification:** [`docs/SPECIFICATION.md`](../SPECIFICATION.md) v5.0
**Requirements backlog:** [`docs/requirements/AuditSphere_Audit_Workflow_Gap_Closure_User_Stories.md`](../requirements/AuditSphere_Audit_Workflow_Gap_Closure_User_Stories.md)
**Source implementation checkpoint:** `master@74cf48e`

This is a resume checklist, not an acceptance certificate. Tenant identifiers, credentials, tokens, secret values, and private-provider URLs are deliberately absent from the public repository.

## Verified local baseline

- [x] .NET 10 solution builds with zero warnings/errors.
- [x] PostgreSQL 18.6 development profile is used on loopback port 5433.
- [x] 206/206 PostgreSQL-backed tests pass with 0 skipped.
- [x] 65 migrations are applied; latest is `20260921090305_PreserveAnalyticalReviewConclusion`.
- [x] EF model has no pending migration changes.
- [x] Loopback restore drill passes and writes secret-free evidence to `docs/evidence/restore-drill-latest.json`.
- [x] Generated Graphify output and local agent state are ignored and removed from Git tracking.
- [x] Public documentation no longer stores tenant-specific identity, site, app, or account identifiers.

## Local accounting and workflow follow-up

- [x] Client accounting profiles, periods, books, COA mappings and versioned TB profiles.
- [x] Capability profiles enforce supported service kinds and matching client/group scopes.
- [x] Atomic multi-entity TB batches, typed GL imports and bounded completeness bridges.
- [x] TB CSV/XLSX imports persist the selected reporting period, optional book and basis and reject mismatched context at import, completeness and reconciliation boundaries.
- [x] Reconciliation, ECL, inventory, specialist, analytical and journal-risk workbenches.
- [x] Analytical reviews preserve QAR-defaulted reporting currency, deterministic input snapshots/replay hashes, movement and seasonality flags; journal-risk evidence preserves sample selection, management explanation and corroboration fields.
- [x] ECL differences are calculated against an explicit booked amount; ECL and inventory assessments retain same-engagement proposed-journal lineage. Approved golden fixtures and boundary cases remain pending.
- [x] Analytical-review approvals require a persisted reviewer conclusion linked to the exact replay snapshot/hash; missing conclusions are rejected after freshness checks.
- [x] Entity package projections, equity, notes, comparatives, restatement lineage and bounded same-currency consolidation.
- [x] Journal lineage, close checks and immutable management/accounting/partner package-review decisions.
- [x] Require an independently accepted group capability profile before approving a consolidation perimeter.
- [x] Require current management, accounting and partner package reviews before approving a consolidation component.
- [x] Bind each consolidation component to the exact package period basis, taxonomy version and mapping-version lineage and include it in the deterministic run manifest.
- [x] Recompute consolidation inputs at run approval and require a new run when a component, match or group journal changes.
- [x] Pin each consolidation scope to the group-membership revision and reject approval/calculation after a perimeter change without rewriting historical scopes or runs.
- [x] Reject component, translation, intercompany-match and group-journal writes after a pinned group revision changes.
- [x] Preserve group perimeter, consolidation, exchange-rate and translation dependencies in structured records exports.
- [x] Carry approved group opening run, FX and recurring-elimination lineage to a new scope without auto-applying prior journals.
- [x] Add the bounded approved foreign-operation translation profile with pinned rate/policy inputs, maker/checker review, source/FX lineage and stale-input blocking.
- [x] Reject unsupported FX rate directions in the enabled bounded translation profile.
- [x] Add bounded, resumable GL chunk intake with canonical digest, idempotent retry and contiguous finalization.
- [x] Bind source-bound GL reconciliations to the selected reporting period and currency; reject cross-period or cross-currency batches.
- [x] Bind reconciliation items to the selected source currency; reject future dates and missing dispositions while preserving as-of ageing.
- [x] Route GL completeness calculation through the existing durable operation infrastructure with source revision fencing and idempotent retries.
- [x] Route financial-package builds through the existing durable operation infrastructure with mapping/plan fencing and idempotent retries.
- [x] Bind context-bound financial packages to the selected client reporting period, optional book, basis and currency, including period-date validation and deterministic hash lineage.
- [x] Bind context-bound adjustment journals to the validated client reporting period, optional book, basis and currency; reject cross-period books and preserve journal context lineage.
- [x] Route financial-package rendering through the existing durable operation infrastructure with exact package-revision fencing and deterministic artifact-digest verification.
- [x] Require financial-package mappings to match the approved taxonomy statement section; persist individual statement cross-cast, accounting-equation, equity/profit, comparative-consistency and note-to-face validation results and render key package-lineage identifiers.
- [x] Benchmark representative accounting workloads before production acceptance; the current test covers four clients, 2,000 transactions, 8,000 GL lines, two concurrent workers, a 32-line group calculation, six-decimal/high-magnitude amounts and paged reads. Observed timings are local capacity evidence only.
- [x] Add a client-safe validated-package view and signed-in management acknowledgement route with cross-client denial coverage.
- [x] Add an authorized staff package-review queue with exact-version stage status and direct package handoff.
- [x] Bind validated financial-package release candidates to current management, accounting and partner decisions through the existing guarded approval/release path.
- [x] Add the completion-screen action that prepares a package-bound release candidate only after current package reviews are approved.
- [x] Bind ECL and inventory review decisions to the exact reconciliation source hash and client input generation.
- [x] Bind specialist and analytical review decisions to the exact client input generation.
- [x] Require explicit depreciation method/useful life and persist the calculated closing balance for asset schedules.
- [x] Add typed payroll, loan, equity, related-party, tax and going-concern forecast inputs with evidence-bound approval.
- [x] Link typed accounting evidence to reviewed audit procedure results, expose the scoped evidence queue and archive accounting lineage.
- [x] Gate period close on current package reviews and persist immutable authorized reopen/amendment revisions.
- [x] Add controlled entity-period roll-forward with draft book copies and explicit opening-balance evidence.
- [x] Client-scoped GL dimension definitions validate nonblank branch/cost-centre/department/project/intercompany values; blank accounting setup/reporting currency defaults to QAR while source/import currency remains explicit.
- [x] Direct and streaming GL imports preserve optional service dates through canonical digests and archive lineage.
- [x] GL completeness bridges optionally bind the approved prior-period TB, persist per-account opening-plus-movement residuals and disclose missing opening or malformed journal evidence.
- [x] Zero-adjustment/source-reflection plans preserve source balances, apply posted journals exactly once by reflection state, and stale when a source decision changes.
- [x] Source-bound reconciliation approval re-checks source digest and client generation and marks changed inputs stale.
- [x] Chart hierarchy parent lookups are scoped to the target chart version and reject cross-version and posting-account parents.
- [x] Draft taxonomy nodes support incremental same-version parents and reject cross-version parents/cycles.
- [x] Link accounting evidence to reviewed audit workpapers and expose accounting period handoffs from the dashboard.
- [ ] Add approved-method fixtures for full FX remeasurement/reserve, complex ownership, acquisition, NCI, nested groups and advanced eliminations before implementing those methods.
- [x] Add deeper accounting evidence/workpaper links and account-area UI around the typed specialist schedules.
- [x] Add the accounting dashboard and cross-workflow navigation around accounting, roll-forward and release workflows.
- [x] Re-run focused tests, full tests, build, migration drift and restore drill for the current coherent slice; repeat for the next slice.

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
