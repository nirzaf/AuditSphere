# AuditSphereOps — implementation checklist

Dependency-ordered checklist for the [v5.0 specification](../SPECIFICATION.md) and the [workflow-gap backlog](../requirements/AuditSphere_Audit_Workflow_Gap_Closure_User_Stories.md).

**Current baseline:** `master@49f1a88`; PostgreSQL 18.6; 45 migrations; 188/188 tests passed; 0 skipped.
**Status meanings:** ✅ locally verified · 🟡 partial/local follow-up · ⬜ not started · 🚫 external gate

| # | Work package | Status |
|---:|---|---|
| 1 | Toolchain, solution, PostgreSQL bootstrap and migration-aware readiness | ✅ |
| 2 | Scope authorization, integrity constraints and append-only accounting evidence | ✅ |
| 3 | Durable outbox, leases, idempotency and recovery-safe worker fencing | ✅ |
| 4 | Practice CRM, time/budget, billing and bounded firm ledger | ✅ |
| 5 | Trial-balance intake, adjustment/source reflection and package calculations | ✅ |
| 6 | Document snapshots, approvals, release gates and exact-version binding | ✅ |
| 7 | Staff/client Blazor route catalog and truthful persisted workflow surfaces | ✅ |
| 8 | Audit planning, populations, workpapers, findings and completion primitives | ✅ |
| 9 | Records/archive model, repository binding, provider fences and recovery quarantine | ✅ |
| 10 | Archive version lineage, legal-hold blocking and evidence-safe downgrade | ✅ |
| 11 | Client accounting profiles, periods/books, COA, TB profiles and entity batches | ✅ |
| 12 | Typed GL imports, bounded completeness bridges, reconciliations and specialist workbenches | ✅ |
| 13 | Entity packages, equity/notes/comparatives, restatement lineage and bounded same-currency consolidation | ✅ |
| 14 | Journal lineage, close checks and immutable package-review decisions | ✅ |
| 15 | Bounded streaming GL intake and durable resumability | ✅ |
| 16 | Client-safe management package view and staff package-review/release workflow surfaces | 🟡 |
| 17 | Advanced/mixed-currency and complex group accounting methods with approved fixtures | 🟡 |
| 18 | Remaining account-area schedules, dashboards and roll-forward usability | 🟡 |
| P1 | Live Entra OIDC and runtime identity fixtures | 🚫 |
| P2 | Selected-resource SharePoint/Graph provider acceptance | 🚫 |
| P3 | External release checkpoint store and capability evidence | 🚫 |
| P4 | Purview records profile, reviewer fixtures and behavior evidence | 🚫 |
| P5 | Approved signing methodology and signature lineage | 🚫 |
| P6 | Records/archive residual hardening | ✅ |
| P7 | Cross-store recovery and production RPO/RTO | 🚫 |
| P8 | Production secrets, observability and capacity | 🚫 |
| P9 | Independent review and protected merge governance | 🚫 |
| P10 | Full §47 real-tenant acceptance cycle | 🚫 |

## Required evidence for every local slice

- Scope and authorization enforced inside commands/queries, not only in the UI.
- Immutable or version-bound evidence for submitted professional results.
- PostgreSQL-backed targeted tests plus the full suite when shared behavior changes.
- Build with zero warnings/errors, migration drift check and restore rehearsal when persistence changes.
- Updated `docs/execution/current-slice.md` and `docs/execution/status.json` containing observed facts only.
- A small coherent commit pushed to `origin/master` when the slice is verified and the user has requested incremental pushes.

## External acceptance rule

Do not convert 🚫 to ✅ from local tests, fixtures, simulation adapters, a portal screenshot, a login page, or a status document. Each external gate requires its named owner, authorized environment, exact observed evidence and independent review where specified.
