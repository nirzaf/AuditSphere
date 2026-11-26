# Audit-fix continuation history through F01 (2026-11-27)

The following source identity and per-slice observations are moved verbatim from the current handoff to keep the active handoff within its documented size budget. They remain historical evidence and do not establish current exact-source CI or external acceptance.


- **Source identity:** baseline `62059aa2e622bcb6245a211380021f5cf315cd97`; initial product `9704395dd1870b5232822a4f708fb21a33a7535c`; before F27, fetched `HEAD` and `origin/master` both equaled `ba32d995662cae21f060d66239068c77b0c2da01`. Preserve the three concurrent UI spec edits.
- **F29 — IMPLEMENTED/pushed `0e9ec0e4`:** single-writer completion state, bounded ownership, crash recovery/origin reconciliation; runner 18/18, state 12/12, py_compile pass. Fast CI `45809967010` cancelled.
- **F16 — IMPLEMENTED/pushed `03612bf1`:** strict TRX result/counter/identity checks; tests 11/11, py_compile and retained 246-result TRX pass. Fast CI `45812614632` cancelled.
- **Parent baseline:** Full CI 45804859081 passed only for 62059aa2e622bcb6245a211380021f5cf315cd97.
- **F02 — IMPLEMENTED:** post-lock authority rechecks; isolated PG 31/31 on `2275b1ad`; exact CI pending.
- **F15 — IMPLEMENTED:** matched-file timestamp fix `ae3ba2e5`, fixture repair `0e2dda98`; `45820700549` pending. Local check `BLOCKED_TEST_ENV` (PowerShell .NET 10 reference link).
- **F14 / ARCX-003 — IMPLEMENTED:** current item-content and pinned historical-version routes; drift denied; provider filter 1/1; `45820700549` pending.
- **F03 — IMPLEMENTED:** atomic expense/journal transitions and cancelled-row SQL guards; PG 8/8, Angular 5/5, EF clean. No history repair.
- **F08 / PRA-109 — IMPLEMENTED:** safe report currency for nonchargeable cost; analytics passed. PRA-098 remains gated.
- **F09 / PRA-099 / PRA-103 — IMPLEMENTED:** unsafe budget actuals are null; safe rows retained; nonchargeable charge-out is zero. PG, Angular, OpenAPI/docs pass; no FX/migration.
- **F06 — IMPLEMENTED/pushed `acbc6935`:** import validator requires posting + ACTIVE accounts; isolated Domain/API 1/1. `RETIRED` fixture was inactive before publication; no incident claimed. No migration/OpenAPI; fast pending, images skipped.
- **F04 — IMPLEMENTED:** immutable v1 legacy proofs; v2 opening + movement − closing; forward migration, no production migration. API 1/1, Angular 13/13 and 18/18, OpenAPI/EF pass. Fast `45860751072` failed: nine concurrent `resources.spec.ts` timeouts (2,139/2,148); no full/provider acceptance.
- **F05 — IMPLEMENTED/pushed `d65d6aa`:** package cash-flow/note/schedule use exact nullable book; UI supplies mapping context, server resolves dataset/book. Wrong-book blocks review; stale source remains stale. PG 2/2, Angular 41/41, OpenAPI pass; E2E not run locally. Fast `45865335871` cancelled.
- **F11 — IMPLEMENTED/pushed `ce34f359`; fast `45868828698` cancelled:** explicit `periodId` scopes balances/control; missing/overlap refuses; UI clears stale results. PG Domain 2/2, API 1/1, Angular 7/7, OpenAPI pass; no browser/migration.
- **F13 — IMPLEMENTED/pushed `1d7d2f75`; fast `45870716189` CANCELLED:** corrupt hash/JSON/null fails the queue after scope recheck; UI blocks review. PG Domain 1/1 via read-fault seam, Angular 8/8. No exact-source CI pass.
- **F12 — IMPLEMENTED/pushed `9704395d`; fast `45873866146` cancelled:** generic TB/GL prepare/execute are blocked; old proofs immutable/noncurrent; native AR/AP/bank separate. API 3/3, Domain 1/1, Angular 1/1. AC-REC-07–10 `BLOCKED_DECISION`; no migration/OpenAPI/browser/production check. [Exact commands](auditsphere-audit-f12-evidence-20261126.md).
- **F10 — ALREADY_FIXED:** `119121d9`; application/API, EF, UI and OpenAPI enforce trimmed 50/50/100 bounds. Boundary/no-persistence Domain 1/1, workspace 10/10, OpenAPI current. No edit/migration.
- **F30 — ALREADY_FIXED:** `e52d7352`; CSV/XLSX signed and source-side magnitudes bounded. Required Domain 4/4; API overflow refusal 1/1 with no rows. No schema/edit.
- **F18 — ALREADY_FIXED:** `33d86585`; endpoint and service require trimmed 1–4,000 rationale. API 1/1 covered refusal/no history and exact retained text; OpenAPI current. No edit.
- **F17 — ALREADY_FIXED:** `bc69c7f6`; `PracticeTime.touchTask/touchDraft` and `Drafts.saveForSession` fence snapshots to firm/actor/generation/invalidation, cancel timers, and reset forms on session change. Existing keys and safe command recovery remain. Spec on `4d33278e`: 20/20, including queued stale callbacks, same-identity refresh and destroy. No edit/browser check.
- **F27 IMPLEMENTED/pushed `efeed02c`:** app/UI/EF pair bounds; PG2/UI11/EF pass; target `BLOCKED_CONTEXT`, Feb29 rollover open; `45880703868` cancelled, current `45883992897` pending. [Evidence](auditsphere-audit-f27-evidence-20261126.md).
- **F31 IMPLEMENTED/pushed `2279e44f`:** bundle cap before persistence; PG2; `45882050472` cancelled, current `45883992897` pending. [Evidence](auditsphere-audit-f31-evidence-20261126.md).
- **PLAT-002 IMPLEMENTED/pushed `865df51b`:** worker docs corrected; docs validator/diff PASS; F22/Azure untouched.
- **M365-014 IMPLEMENTED/pushed `6a36c301`:** 262,144-byte actual-read cap in all 3; API15/15; docs PASS; CI `45883992897` pending. [Evidence](auditsphere-audit-m365-014-evidence-20261126.md).
- **PRA-015 IMPLEMENTED/pushed `d1db39c0`:** local rate lookup reuse; PostgreSQL reads 2/2/6/6 → 1/2/2/3; required tests 1/1 and 7/7. Fast `45886662450` pending. [Evidence](auditsphere-audit-pra-015-evidence-20261126.md).
- **F01 IMPLEMENTED:** existing match-reservation work in `bd32b3aa` was verified; the focused PostgreSQL test exposed tracked-parent deletion during refresh. Refresh is now in-place; test 1/1 and EF drift pass. [Evidence](auditsphere-audit-f01-evidence-20261126.md).
