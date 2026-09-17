# AuditSphereOps — implementation checklist

Dependency-ordered per spec Appendix G and the "first proving slice". One item at a time: build + PostgreSQL-backed tests + owner-authorized review/commit before moving on. The agent leaves changes uncommitted unless explicitly asked to commit. Statuses: ✅ done · 🟡 partial · ⬜ not started · 🚫 blocked (external).

| # | Item | Spec ref | Acceptance evidence required | Status |
|---|------|----------|------------------------------|--------|
| 1 | Toolchain pinned, 6-project solution, PG 18.6, runnable bootstrap | ND-01, A01/B01 | Clean build; tests run on real PG | ✅ `671d349` |
| 2 | Accounting persistence integrity: composite scope FKs, append-only TB rows, posted-journal line immutability, single-claim worker guard | ND-03, VT | FK violation rejected; row UPDATE/DELETE rejected; concurrent workers never double-process | ✅ |
| 3 | Durable outbox: claim/lease/attempt fence/idempotency-key uniqueness | ND-03, ADR-06, §29 | Two workers claim distinct ops; retry does not duplicate side effects | Locally verified; uncommitted; provider simulation only |
| 4 | Synthetic client/identity + authorization decision enforced inside commands | ND-02, A02/B02, §8 | Cross-firm/client access denied by command, not only UI | ⬜ |
| 5 | TB import (CSV) → validate → AJ post → re-upload source bridge | NT-09/10, AT-07/10 | Reflected journal never re-applied; profit stays 175,000 | ⬜ |
| 6 | Practice CRM commands (lead→opportunity→proposal→client) | ND-05, AT-01–06 | Commercial/professional separation enforced | ⬜ |
| 7 | Time/budget approval + correction chain | ND-06 | Corrections supersede, never rewrite approved time | ⬜ |
| 8 | Billing artifacts (invoice→receipt→allocation) | ND-07, §41 | Balanced expected fixtures; no invoice-history rewrite | ⬜ |
| 9 | Firm ledger (accounts, periods, draft→posted immutable, source uniqueness) | ND-08, NT-22 | Period race rejected; posted journals immutable | ⬜ |
| 10 | Document snapshots (exact-byte hash, unique version per doc) | ND-04, §11 | Duplicate snapshot identity rejected; hash verified | ⬜ |
| 11 | Approvals bound to revision + generation; stale rejected | §22, AT-19/26 | Change after approval invalidates it; stale approval fails release | ⬜ |
| 12 | Release gate (manifest digest, external checkpoint) | §24, AT-25/26 | Wrong-version report blocked; no checkpoint, no release | ⬜ |
| 13 | Blazor staff/portal screens over implemented commands | ND-10, §43 | Real screens driving real commands | ⬜ |
| 14 | CI, backup/restore drill, readiness with migration check | ND-11, §45 | Pipeline green; restore rehearsal documented | ⬜ |
| 15 | Entra OIDC, SharePoint grants, Purview profile, methodology/signing approvals, independent review + protected merge | §47.2 | Owner-supplied tenant/professional evidence | 🚫 |
| 16 | Full-cycle acceptance run on real tenant | §47, §1.4 | All 120 scenarios observed as specified | 🚫 |

## Item log

- **#1 — done** (commit `671d349`, 2026-09-17): six net10.0 projects; EF Core 10.0.12/Npgsql 10; PostgreSQL 18.6 user-local; `scripts/db` operational; 6/6 tests green.
- **#2 — done** (2026-09-17): migration `20260917091412_AccountingIntegrity` adds composite scope FKs (dataset→engagement/client within the same firm, rows→dataset, journals→dataset/engagement, lines→journal), an append-only trigger on `trial_balance_rows` (UPDATE/DELETE rejected — corrections mean a new dataset), and a freeze trigger on `adjustment_lines` once the journal leaves Draft. 6 new PostgreSQL tests: orphan row FK, unknown engagement FK, cross-firm client FK, append-only rows, frozen journal lines, and a two-worker concurrency test proving one claim per dataset. **Bug found by tests and fixed:** the first trigger version returned OLD on UPDATE, silently discarding Draft-phase line edits.
- **#3 — locally implemented/verified** (2026-09-17, uncommitted): migration `20260917104422_DurableOutbox`; one durable operation state machine, scoped payload-bound idempotency, atomic SKIP LOCKED claims, renewable leases, monotonic attempt fences, append-only evidence, and safe uncertain-result reconciliation. Trial-balance validation now uses the typed local handler without changing its accounting outcomes. Build: 0 warnings/errors. Database profile: 29/29; full suite: 40/40, 0 skipped. Tests cover simulated provider success followed by local failure, stale completion/renewal, renewal waiting past lease expiry, changed inputs, cancellation, constraints, rollback, and migration refusal with legacy operations. Applied only to local `auditsphere`; real provider/tenant acceptance and independent review remain outstanding.
- **Next permitted step:** owner-authorized review/commit of #3. No commit, PR, merge, production deployment, or checklist #4 implementation was performed.