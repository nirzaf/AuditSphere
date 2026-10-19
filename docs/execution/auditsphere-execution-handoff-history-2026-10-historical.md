# AuditSphereOps — Historical execution handoff (2026-10)

**Status:** HISTORICAL

**Purpose:** Preserve dated implementation narratives moved from the compact current-slice handoff. Current results and volatile test/run facts live in `status.json`.

## Angular quality follow-up — fieldwork safety and rendered acceptance (2026-10-19)

This section supersedes the earlier browser-tool limitations below. The built-in browser supports viewport emulation, reduced-motion media and bounded network observations. The named journal, evidence, planning, consolidation, fieldwork and audit-plan routes were inspected at narrow and desktop widths. Genuine browser zoom was checked with a temporary Chrome tab because native zoom controls were unavailable in the built-in browser. Both browsers and their emulation settings were restored after inspection.

The rendered review found that the wrapped sticky mobile header covered the first focused control after skip navigation. The header now scrolls with narrow layouts, and the main region has no extra mobile scroll offset. Built-in-browser and native-zoom retests confirmed the focused link is unobscured. The existing shell journey now checks this exact failure without replacing its staff/client navigation and revocation checks.

An independent source review confirmed the strictness, editor-lifetime, planning, consolidation and lint slices. Its two fieldwork findings are now corrected: command writers share the host exclusion boundary, and uncertain procedure submissions retain only their exact receipt identity across remounts. Recovery requires current authorized context, rejects mismatched receipts, restores no assent and blocks on unreadable checkpoints. Focused verification is tracked in `targetedVerification.angularQuality` in [status.json](status.json). Historical execution records were archived intact, with discoverable pointers and unresolved gates retained in the active ledger, to repair the documentation-size CI failure.

Task 07 remains partial. The local interactive browser uses the current UI with a pre-existing API whose source identity is unknown; this is separate from owned-fixture journeys that launch the current compiled API. Empty/blocked-state smoke checks do not establish record-backed planning, consolidation or fieldwork acceptance. A knowledgeable human screen-reader review and remaining record-backed accessibility states are still required by [the accessibility checklist](../testing/auditsphere-testing-checklist-angular-accessibility-current.md). No professional approval, provider acceptance, tenant configuration or deployment state was promoted.

Full release verification has been dispatched on the final immutable code commit. Full regressions remain CI-owned. Exact source identities, focused results, browser observations and CI runs are in the execution ledger; earlier pending/cancelled runs do not establish a current full pass.

Earlier Angular quality observations are preserved in [the historical handoff](auditsphere-execution-handoff-history-2026-10-historical.md); they do not override this follow-up.


## Source-linked package build from the product (CP-11 accepted-source cash-flow subpath) — 2026-10-14

The engagement-scoped preparer screen lists approved mappings, finalized adjustment plans, reporting periods and validated comparative packages. It supports a paged accepted-ledger line picker and submits exact decimal strings to the durable `BuildFinancialPackage.v1` operation. Input discovery is scope-authorized and the server still validates the selected current/comparative package. The routes are `GET /api/ui/engagements/{id}/financial-package-inputs` and `POST /api/ui/accounting/packages/builds`.

The build screen now polls a bounded status receipt for the exact operation. Only its originator can read that receipt; it returns no payload or error detail, and the package identity appears only after confirmed completion. Terminal failures stop polling and remain visibly distinct from queued or running work.

The synthetic browser journey passed 1/1: it completed the worker build, showed the package link, denied another reviewer the originator-only receipt (403), denied preparer source review, and allowed independent approval of persisted GL links. Cash, equity, note and adjusted trial balance cross-cast at six decimals. Exact-head CI [38607048224](https://github.com/nirzaf/AuditSphere/actions/runs/38607048224) passed docs health, Angular (127 files/722 specs), Release build, focused Domain (141) and API (8) checks, EF drift, API/browser journeys, standalone API publication and OpenAPI drift. The private API, worker and migration images were built and published by [38607048220](https://github.com/nirzaf/AuditSphere/actions/runs/38607048220). The previous failing head [38602425958](https://github.com/nirzaf/AuditSphere/actions/runs/38602425958) had four browser failures; this commit fixes them. See `status.json` under `targetedVerification.currentCashFlowPackageJourney`.

One CP-11 cash-flow subpath, not story completion. Manual-evidenced cash, equity lineage, other source/review paths and remaining CP-11 acceptance remain open. CP-12 remains blocked on approved indirect policy and qualified assessed-zero FX treatment. CP-14 now has optional accepted-GL note provenance; disclosure applicability, independent note review and NOTE_CONTENT remain open. Targeted CP-14: Domain 1/1, Angular 32/32, API Release 0 warnings/errors, EF drift none; full suites not run locally. Exact-head CI focused cohorts passed.

CP-11 retry fix `60f346d5`: predecessor-bound replay; focused PostgreSQL test 1/1. Exact-SHA release verification pending.

Commit 83d3472d also repairs five Domain tests that failed on the prior master: four package-review fixtures now seed the canonical validation population, and the FX test numbers cross-cast to adjusted cash. The exact-head focused Domain cohort passed 141/141; the full Domain suite has not run on this commit.

## STE v2.2 end-to-end story, first increment (F-15, F-01–F-05, F-09, F-11 label) — 2026-10-15

Source: the STE v2.2 end-to-end audit and completion story reviewed at `b05a6bf6`. This increment implements its first dependency-ordered handoff and the engineering-only P0/P1 deltas that need no professional decision. The overall project remains PARTIAL; no production readiness is claimed.

- **F-15 migration reversal.** `20261015000622_FinancialStatementReviewCompositionBinding.Down` renamed bundle index suffix `~5` to `~4` while `~4` still existed (42P07). It now reverses from the lowest free suffix upward. A downgrade refuses once any decision, release or bundle names a composition. The two hosted-failing tests (`RelationshipManagerAnnualPreparationTests.ClientScoped…`, `AcceptanceChecklistTests.ReviewedScopedDecision…`) pass locally on PostgreSQL 18.6.
- **F-01 cash projection parity.** The Angular build form now mirrors the server projection role: a cash movement reverses the ledger sign of its noncash counterpart, a noncash movement keeps it. Stored ledger amounts are sent unchanged; over-allocation and wrong-sign refusals remain. The form shows the projected statement effect. Specs cover equipment (+25,000 → −25,000), receivable collection (−270,000 → +270,000), borrowing (−40,000 → +40,000), principal repayment (+10,000 → −10,000), noncash, partial, compound and six-decimal cases.
- **F-02 portal composition approval.** The client package view names the sealed composition of the newest active, complete, current candidate (revision, composition, manifest digest and format fingerprints). The portal decision must name exactly that composition, carries an original request identity and the latest decision seen, and is recorded against the composition chain. Client managers download the exact sealed PDF through an authorized, antiforgery-checked endpoint whose fingerprint the browser verifies before saving. Legacy package-only acknowledgements stay historical evidence.
- **F-03 final-content readiness.** A signing candidate is created only when every presentation check passes (`statement-candidate-readiness.v1`). Note, disclosure and equity content checks pass only through a new append-only independent content review of the exact draft snapshot (preparer excluded, request receipts, stale-chain refusal). Equity evidence is labelled reviewed manual evidence (`EQUITY_EVIDENCE_REVIEW`), never ledger-derived. Formal decisions, client composition offers and release recheck that the candidate carries no unresolved check. Candidate banner now reads "awaiting formal decisions; not approved" (F-11 label).
- **F-04 latest-effective decisions.** Formal package decisions take the client-safety → package → grant lock order, recheck authority after waiting, recover by original request identity (same payload returns the row, different payload conflicts), refuse a stale expected latest decision, and record a database sequence and predecessor link. Progression and release use the latest-effective approval per stage recorded after its predecessor's; a return defeats earlier downstream approvals and a genuine re-approval is accepted.
- **F-05 lock order.** Cash-flow source review now takes the client safety row before the package row, matching pin, promotion and composition. The order is recorded in the architecture code map.
- **F-09 per-payment vouchers.** Every actual payment receives one immutable voucher (amount, date, reference, amount applied, balance remaining after it) and one dispatch intent; a missing profile or contact keeps the payment and the same reference later completes the voucher. The milestone settlement receipt (template v2) lists each constituent payment instead of presenting the last one as the whole milestone.
- Migration `20261015020548_PackageDecisionReceiptsContentReviewAndPaymentVouchers` is additive, adds an append-only trigger for content reviews and refuses a downgrade that would discard reviews, receipts or vouchers. It was applied only to disposable test databases.

Verification on this tree (local container, PostgreSQL 18.6 on port 5433, .NET SDK 10.0.300, Node 26.8.1): Release solution build 0 warnings / 0 errors; the exact CI Domain filter 145/145 and API filter 8/8; focused statement, bundle, gate, commercial and cash-flow cohorts passed; Angular `test:ci` 129 files / 752 tests; Angular production build passed with the existing `commercial/settings.scss` budget warning; EF model drift clear; OpenAPI regenerated and verified; Markdown, filename, narrative-metrics, UI-inventory and `git diff --check` passed. Full-suite results and their baseline comparison are recorded in `status.json` (`targetedVerification.ste22EndToEndIncrement1`).

Not done in this increment and still open: E2E/Playwright and authenticated browser journeys, the hosted full release workflow, F-06 annual comparatives and multi-book sources, F-07 lifecycle projection, F-08 purpose-based routing and response provenance, F-10 typed indirect cash-flow roles and assessed-zero states, remaining F-11 publication content, F-12 assigned fieldwork, F-13/F-14 release governance and live production qualification, and every professional decision packet (T003 profile, management signatory/capacity, document wording). Synthetic fixtures were completed with a balancing statement of financial position, a revaluation OCI movement, investing and financing cash events and a USD account at the unchanged QAR peg; they are engineering fixtures, not accountant-approved golden fixtures.

## Atomic Angular quality Tasks 06–07 — partial evidence (updated 2026-10-19)

Task 05 fieldwork procedure/sampling ownership is committed and pushed as `3194fe3b` (`feat(audit-fieldwork): add procedure state management and tailoring commands`). Its focused Angular spec and production build passed, and `git diff --check` passed on that source; full suites remain CI-owned. The build retained existing optional-chain warnings in completion and the commercial SCSS budget warning.

Task 06 is implemented on `5268f5fbd1f8be074f76cb89e20fc5d13f4b4b80` after approval of the exact dev-only dependency set. The flat ESLint config and Angular target lint production TypeScript, external templates and extracted inline templates without type-aware whole-project analysis. Explicit-file local lint and full-tree CI lint commands are available; lint runs in the existing fast and full CI Angular jobs. The clean install resolved the approved pins without force/legacy flags, `npm audit` reported zero vulnerabilities, and six in-memory valid/invalid probes passed for TypeScript, external HTML and short-filename inline templates. The five designated feature slices passed explicit-file lint. The first full lint found two accessibility-semantic issues; the mapping editor now handles bubbled Escape at the component host, and a button caption is no longer marked as a form label. Full production lint then passed in 2.82 seconds. A focused mapping-draft spec passed 10/10, and the production build passed with the previously observed optional-chain and SCSS budget warnings. Fast CI run [39168122489](https://github.com/nirzaf/AuditSphere/actions/runs/39168122489) passed Angular, Docs and .NET Release on `7c66eeed`; the Angular job includes clean install, lint, production build and full Angular specs. The .NET job completed its Release build, fast database-free guards, EF drift, publication and OpenAPI checks. Full release-verification suites were not run.

Task 07 remains `PARTIAL`. On current UI source `d733ec8c665156aabef7a3804602596757fe6311`, the built-in browser rendered the journal queue, accounting-evidence queue, engagement/planning, consolidation scope, fieldwork and audit-plan routes using local synthetic staff fixtures. At the available 1180×954 CSS viewport, `documentElement.scrollWidth` was 1165 on each sampled route. The browser did not expose viewport/zoom controls for narrow or 200%/400% checks. The API was served by the pre-existing process on port 5029; its source SHA and the embedded browser version were not available. No browser console errors occurred during this route matrix; HTTP status/network-panel evidence was not available.

A keyboard route check found one rendered defect: activating the expanded-sidebar Evidence link changed the route but left focus on the link. `src/AuditSphereOps.Ui/src/app/app.ts` now moves focus to `main#main` after successful route changes when a navigation dialog is not open. Enter navigation was retested in the built-in browser and focus landed on the new main region. The focused `app-navigation.spec.ts` passed 11/11. The compact-sidebar toggle changed state and was restored. Fieldwork tabs behaved as Material manual activation: ArrowRight moved focus to Client evidence and Space selected it. A limited status-chip contrast sample measured 7.24:1 (Active), 6.76:1 (Blocked) and 14.78:1 (Not started); labels state their meaning in text.

Journal and accounting-evidence queues had zero records, so editor validation, recovery and unknown-outcome behavior could not be exercised. Planning had no staff assignment or approved budget and kept review disabled. Fieldwork had no applicable procedures and displayed the existing program/materiality blockers. The synthetic consolidation scope showed one component, one journal and zero runs. No financial, professional-approval, access or provider command was submitted. Screen-reader operation, session revocation, narrow layout, zoom, reduced motion, complete contrast review and the full rendered matrix remain open. Fast CI run [39169080992](https://github.com/nirzaf/AuditSphere/actions/runs/39169080992) on `d733ec8c` had Docs and Angular pass; its .NET Release job was still running Restore and build at 2026-10-18 21:48 UTC. The full release-verification workflow was not run.

The stable procedure is [the Angular rendered accessibility checklist](../testing/auditsphere-testing-checklist-angular-accessibility-current.md). Task 06 verification and the compact Task 07 state are in `targetedVerification.angularQuality` in [status.json](status.json); the earlier detailed Task 07 snapshot is preserved under `targetedVerification.angularQualityBaseline20261018` in the [status archive](status-archive-2026-10.json). No whole-product accessibility or full-spec completion is claimed.


## Relationship Manager search matrix regression (2026-10-18)

The full Domain CI job identified a stale matrix assertion left behind by the client-archive change. The client workspace permits current client-scoped or explicit firm-wide RelationshipManager grants to read client summaries; a firm-wide grant therefore returns matching client identities as well as leads. The corrected existing test still asserts the exact result kinds and excludes professional engagement/PBC/finance results for that role. The search code map now distinguishes client and firm-wide grants from engagement-only grants. Application permissions and production code were not changed.

The previously failing matrix test passes against the isolated PostgreSQL test database after rebuilding. Test-value evidence: removing the supported Client result or adding unrelated professional result kinds would fail the exact equality assertion (static prediction; no counterfactual run). Exact baseline, test patch hash and prior CI failure are retained in `targetedVerification.searchRoleMatrix20261018` in [status.json](status.json). Full release verification was dispatched on the immutable implementation commit; its exact SHA and queued run are recorded in the same ledger entry. This focused test does not accept the complete application.

## Reversible client archive history (2026-10-18)

Commit `9cdbfb19e512c8d9e849fe95cfd6f2d38a33b2c7` adds a reversible client archive state backed by append-only actor, UTC time and revision events. Archive/restore is client-grant authorized, locks the client row, checks the expected revision and does not change client status, engagement records, evidence or authorization. Authorized portfolio and global search retain archived clients and label them; the client profile offers a scoped archive/restore action. The migration refuses downgrade while lifecycle events remain and has not been applied to a shared or live database.

Focused verification passed: PostgreSQL Domain tests 2/2 for immutable archive/restore history and RelationshipManager search isolation on `9cdbfb19`; Angular client, portfolio and engagement workspace specs 26/26 on `f5f96d9e`; API/OpenAPI emitter Release build with 0 warnings/errors; OpenAPI drift; EF model drift; and all four documentation validators. Fast CI `39132751124` and full release verification `39132763882` failed on `9cdbfb19` because an engagement-spec client fixture lacked the new required archive fields. The fixture was updated without weakening assertions and pushed as `f5f96d9e`. The fast-CI rerun `39132894411` and exact-SHA full-release rerun `39132911253` were in progress at handoff. Full suites remain CI-only.

Built-in-browser acceptance is **NOT_VERIFIED**: the existing `127.0.0.1:5029` app renders only its workspace shell and serves stale code. I left the user's process and database untouched. ASPM-CRM-02 remains PARTIAL pending current-SHA CI completion and authenticated browser acceptance. Push also triggered image workflow `39132894382`, which only builds/pushes private GHCR acceptance images; no VPS deployment occurred. Other unrelated worktree edits (two legacy HTML deletions, an untracked architecture HTML file, and historical execution-record edits) were preserved and excluded from these commits.

## Firm receivables age all posted invoice categories (2026-10-18)

The ASPM-FIN-03 query regression now exercises posted firm invoices typed as `ADVANCE`, `FINAL`, `TIME`, and `OTHER`, with effective payment terms, posted cash, credits and approved allocation corrections evaluated at the UTC as-of date. The final-invoice row is a synthetic query fixture; it does not bypass the production balance-invoice creation gate. The time-invoice row is produced through the existing billing service from approved synthetic time. Client-bookkeeping invoices remain outside the firm receivables query.

Focused PostgreSQL verification passed 1/1 in `FirmReceivablesAgingQueryTests.ReportUsesApprovedTermsAndOnlyReceiptsCreditsAndAllocationsEffectiveByUtcAsOfDate` (43 seconds). This completes the FIN-03 source/query criterion; exact-SHA full release verification remains pending. The prior full release run `39117606930` on `9f15c482` recorded 1,368 passed and two failed Domain tests: the QAR 12,400 string-format fixture (corrected and focused-pass on `954a623f`) and an EF execution-strategy wrapper around the expected retained-history PostgreSQL `55000` refusal. The latter assertion now checks the underlying SQLSTATE and passed its focused test 1/1. I cancelled that stale run after the Domain failure; its API and E2E jobs did not complete and provide no pass evidence. Detailed run evidence is recorded in [`status.json`](status.json) and [`status-archive-2026-10.json`](status-archive-2026-10.json).

Fast CI run `39120222007` passed on `1f4e5430`: Docs Health, Angular and .NET Release all succeeded. Run `39119821556` had already passed the regenerated OpenAPI `effectiveStatus` contract on parent `9fad72d0`, correcting run `39119098499`'s drift failure on `954a623f`. The full release suite has not run on the current source. The documentation/status commit will trigger a fresh fast gate before full release verification. No full test suite was run locally.


## Archived pre-dispatch-fence correspondence notes

## New payment artifact references (2026-10-19)

New RCT/PV references now include the full retained milestone/payment identity. Truncated UUIDv7 timestamp prefixes did not guarantee distinct references. Issued-document lookup branches continue to reuse existing artifacts; the focused fee cycle checks full references in filenames and DOCX contents, distinct advance/balance receipts and unchanged earlier bytes/hash. This is an opaque internal-reference correction, not an approved statutory numbering policy. Source and checks are in `targetedVerification.paymentMailRecipientGuard.referenceFollowUp` in [status.json](status.json).

## Payment-mail current-recipient guard (2026-10-19)

Receipts and payment vouchers now re-resolve the existing fee-agreement recipient policy before DISPATCHED. Operation/client/artifact/agreement/payment ownership must agree; changed mailboxes block delivery while retaining the original queued recipient, subject, body and bytes. The extended real-worker fee-cycle test proves denial, wrong-client refusal and explicit reviewed recovery after the original fallback is restored. Existing paid amounts, balance gates and reversals remain unchanged. No live mail or tenant action occurred.

ASPM-CRM-03 remains PARTIAL. Independent review identifies a remaining check-to-dispatch race: routing/contact mutation and dispatch need one shared authorization boundary without holding database locks across the provider call. Holding letters, exact routing snapshots, initiating-actor reauthorization, DEC-09 and live/current-source release acceptance remain open. The active full CI run predates this guard. The reference-prefix observation is resolved by the follow-up above. Exact checks and source are under `targetedVerification.paymentMailRecipientGuard` in [status.json](status.json).

## Grant-expiry mutation fence (2026-10-19)

Expiry revocation now cooperates with the existing firm fence, locks the user before grants and refreshes tracked values before the session-epoch increment. The ordinary no-expiry path stays read-only. Focused PostgreSQL checks cover expiry denial/evidence, nested staffing replacement, the real waiter chain, concurrent idempotency and stale tracked-user refresh. The first concurrency probe incorrectly required a direct root blocker; its correction accepts the observed chain through the already-blocked first waiter, retaining all revocation/epoch assertions.

This addresses the expiry-writer gap identified in the preceding correspondence review. It does not supply initiating-actor authorization for queued mail or close the full correspondence criterion. Current source, initial failures, passing rerun, artifacts and CI limits are in `targetedVerification.roleGrantExpiryFence` in [status.json](status.json). No full local suite, schema change or external action occurred.

## Holding-letter mail safety (2026-10-19)

New queues freeze Completion recipient IDs and hash; sends validate scope, current blockers and exact attachment bytes. Legacy queues require the unchanged mailbox and frozen body hash, without backfilling IDs. Focused new/legacy and refusal checks pass. Source, initial analyzer repair and limits are in `targetedVerification.holdingLetterMailSafety` in [status.json](status.json). Initiating-actor mail authorization, DEC-09, atomicity with every professional writer and live/full-release acceptance remain open.

## Atomic correspondence dispatch claim (2026-10-19)

Recipient/artifact validation and DISPATCHED now commit under the same firm fence used by routing assignment/revocation, contact updates and both creation entry points. The provider call starts after the transaction/context ends. PostgreSQL blocking observations and a provider-callback contact update verify the boundary; prior committed route revocation still prevents sending. Existing reference, fee-cycle and reviewed contact-recovery checks remain intact.

ASPM-CRM-03 remains PARTIAL: payment route snapshots, holding-letter checks, initiating-actor reauthorization/expiry fencing, DEC-09 and live/full-release acceptance remain open. The later grant-expiry follow-up above addresses that writer; broader initiating-actor mail authorization remains open. Exact source, focused results and CI are in `targetedVerification.paymentMailRecipientGuard` and `targetedVerification.angularQuality.task07.ci` in [status.json](status.json). The earlier notes are retained in the [handoff history](auditsphere-execution-handoff-history-2026-10-historical.md). Full suites ran only in CI; human screen-reader acceptance remains open.


## Operational cancellation and recovery (2026-10-19)

ASPM-WF-08 now includes current-scoped management cancellation of an active task after exact revision/reason review. Time, bindings, evidence and append-only activity remain; a cancelled required predecessor still blocks its dependent. Exact replay adds no event, and a lost response is observed through a scoped task lookup before any explicit retry. The queue retains a Cancelled view. Keyboard review/close/activity focus and narrow reflow have focused and built-in-browser evidence; migration downgrade refuses retained cancellation history. No shared/live migration or professional approval occurred.

ASPM-WF-08 remains PARTIAL: reopen, suspension, missed-period recovery and broader reconciliation are unfinished. Legacy WF-08 recurrence decisions are separate. The latest full CI run failed and incomplete API/E2E evidence; reported failures are under focused repair. Exact source, checks and limits are in `targetedVerification.workflowTaskCancellation` and `targetedVerification.angularQuality.task07.ci` in [status.json](status.json). No full local suite ran.

## Payment-recipient identity snapshots (2026-10-19)

New receipt/voucher queues retain the selected contact, routing and authority basis. Send-time checks reject a different identity even at the same mailbox. Legacy all-null rows retain current same-mailbox validation without backfilling identities. Focused worker fee-cycle checks pass with financial and reversal assertions intact; exact source and evidence are in `targetedVerification.paymentRecipientSnapshots` in [status.json](status.json). Initiating-actor authorization, DEC-09 and full/live acceptance remain open.

## Holding-letter mail safety (2026-10-19)

Focused new/legacy route, scope, exact attachment and blocker checks pass. Detailed behavior, original analyzer repair and limits are retained under `targetedVerification.holdingLetterMailSafety` in [status.json](status.json) and the [handoff history](auditsphere-execution-handoff-history-2026-10-historical.md). Full/live acceptance remains open.

## Grant-expiry mutation fence (2026-10-19)

Expiry now uses the shared firm/user/grant fence and refreshes tracked epochs. Focused denial, staffing and real concurrency checks pass; original failure evidence and the detailed note are retained in [status.json](status.json) under `targetedVerification.roleGrantExpiryFence` and the [handoff history](auditsphere-execution-handoff-history-2026-10-historical.md). Broader initiating-actor mail authorization and full release acceptance remain open.

## Approved component intercompany accessibility (2026-10-19)

The existing regular-scope journey now includes approved external component packs, a maker's exact signed match and an independent review through keyboard controls. Retained component source manifests remain unchanged; the match creates no journal or run. Native disclosure keyboard/focus checks and narrow reflow are evidenced separately. See `angularQuality.task07.consolidationFocusFollowUp` in [status.json](status.json). Internal-package/full-state and human assistive-technology acceptance remain open; this synthetic path supplies no professional or live-provider acceptance.

## Reviewed reopening of cancelled operational tasks (2026-10-19)

Current scoped management can reopen cancelled tasks without completed-work evidence using a reviewed reason and revision. The same task, binding, time and evidence remain; historical retries acknowledge their event without overwriting later state. Focused Domain/API/Angular and keyboard browser checks pass, including narrow reflow. The owned native review checked focus, fresh assent and explicit close without submission. Exact source, failed-check repairs, migration and limits are in `targetedVerification.workflowTaskReopening` in [status.json](status.json). Completed-work correction, suspension, missed-period recovery, full CI and human/live acceptance remain open. No shared/deployed database was migrated.

## Practice completion safeguards (2026-10-19)

Client archive/restore preserves keyboard focus across a confirmed reload without stealing deliberate focus or crossing ownership. Lost assignment replies resolve the exact task beyond the workspace window. Ambiguous effective staff-cost rates make cost-derived figures unavailable. Portal delegation revocation now serializes with active uploads and preserves retained revocation history. Focused Angular, API, PostgreSQL and browser checks pass; pre-fix archive focus loss was reproduced. Exact source/evidence is in `targetedVerification.practiceCompletionGuards20261019` in [status.json](status.json). Full current-source release, human accessibility, professional and live-provider acceptance remain open.

## Portal finance keyboard and CI wait checks (2026-10-19)

Billing tables now expose named keyboard-focusable scroll regions. A synthetic client-role journey and built-in browser verify Tab access, horizontal scrolling and retained focus; narrow-screen layout and separate lifecycle, collection, aging and untracked delivery states are covered. Planning and logout journeys now verify completed UI behavior without redundant transport waits. Focused checks pass; exact evidence, initial fixture corrections and active full release are in `targetedVerification.portalFinanceAccessibility20261019` and `targetedVerification.angularQuality.task07.ci` in [status.json](status.json). Human AT, full supported-state and release acceptance remain open.

## Scope-bound workflow receipt recovery (2026-10-19)

Late failed receipt reads no longer alter another task or invalidated session. Focused checks preserve the current draft, original recovery key, single dispatch and independent professional gates. The synthetic browser journey recovers the original server receipt after switching tasks; native keyboard/rendering checks pass. Evidence and limits are in `targetedVerification.workflowEvidenceRecoveryScope20261019` in [status.json](status.json). Full-state, human AT and current-source release acceptance remain open.

## Time workspace grant expiry (2026-10-19)

The time workspace now excludes expired grants before deriving work and review scopes. A direct Application query regression reproduced expired firm-wide authority widening a valid engagement grant, then passed after the fix. HTTP actor resolution already revoked expired grants; its session refusal and fresh-sign-in protections remain verified. Exact evidence and limits are in `targetedVerification.timeWorkspaceGrantExpiry20261019` in [status.json](status.json). Full release and broader time/accounting isolation acceptance remain open.

## Browser CI recovery and search interaction (2026-10-19)

Reported planning/profile failures now register real response capture before navigation or reload, and scroll the deferred editor's actual trigger rather than its enclosing region. Existing role denials, exact receipt identities, field restoration and non-duplication checks remain. Invoice response bodies are captured before browser disposal; finding receipt diagnostics preserve any unresolved API-versus-client failure. The affected planning, invoice, review-point and finding cases pass focused reruns. The earlier resource-planning hang did not reproduce; body completion now has a bounded action-specific failure instead of an indefinite wait. These passes do not resolve the unexplained full-API runner shutdown or establish a full regression pass.

A separate confirmed search defect retained an already-open Material suggestion panel over detailed results. The trigger is now explicitly closed on replacement, error and dismissal without changing scope or result navigation. The regression exercises the real Material panel; removing the closure makes it fail. Current checks and remaining human/full-CI gates are recorded in [status.json](status.json). The original search click and native built-in-browser destination now pass. A fresh full immutable-source run is active in GitHub Actions; API shutdown and prior hang causes remain unproven until broader verification.

## Role-dialog keyboard boundary check and milestone atomicity (2026-10-19)

The existing accessibility journey now checks both Tab-wrap boundaries and Escape focus restoration; focused and built-in-browser checks pass without changing a grant. Human screen-reader announcements and full supported-state acceptance remain open. See `targetedVerification.angularQuality.task07.roleDialogKeyboardFollowUp` in [status.json](status.json).

Milestone issuance now retains draft, source reservation and link in one owned or ambient transaction. Automatic publication acquires the exclusive firm fence before client/target locks. Focused concurrency, rollback and standing-authority checks pass; broader payment and source-consumption acceptance remains open. Drain or epoch-fence old workers before rollout; no deployment occurred. Evidence: `targetedVerification.feeMilestoneAtomicity20261019`.

## Milestone invoice source identity (2026-10-19)

New invoice numbers retain the full agreement identity so agreements sharing a UUID timestamp prefix do not block one another. Existing linked invoices and retained source allocations still recover their original numbers. Focused source/replay/legacy recovery and invoice role/mobile checks pass; the heading wraps long identities. Evidence and fixture limits are in `targetedVerification.feeInvoiceIdentity20261019` in [status.json](status.json). The atomicity follow-up above covers the tested advance race; broader milestone/payment concurrency and current-source release acceptance remain open.

## Firm report snapshot and exact recovery concurrency (2026-10-19)

Firm report reads now share one owned source snapshot, committed before current authorization; ambient transactions are refused untouched. Focused concurrency, session and prior-year reconciliation checks pass. The owned Angular expense-to-report/export journey and built-in report calculation also pass. See `targetedVerification.firmReportSnapshot20261019` in [status.json](status.json).

The existing recovery service now has focused PostgreSQL proof that identical concurrent cancellation requests return the same exact receipt and publish one transition/event set. This does not accept every recovery action or external drill. See `targetedVerification.operatorReceiptConcurrency20261019`.

Next: reproduce the source-inspected cross-actor cash-receipt and truncated-reference risks, preserving exact legacy recovery and current financial safeguards. No fix is claimed. Full CI is on the preceding source and cannot certify the report change.

## Payment source safety and evidence review accessibility (2026-10-19)

Milestone cash recording now holds one firm-first transaction across source resolution, receipt and allocation, with full agreement references and fail-closed legacy recovery. Focused regression evidence is in `targetedVerification.feePaymentSource20261019` in [status.json](status.json). The evidence-action keyboard regression reproduced missing focus, then passed after the bounded correction. Built-in preview/cancel/fresh assent and focused receipt/revocation checks pass; human AT and current-source full release acceptance remain open.

## Firm report and recovery concurrency (2026-10-19)

Focused source-snapshot, report/export and exact cancellation-receipt evidence is retained in [status.json](status.json) and the [handoff history](auditsphere-execution-handoff-history-2026-10-historical.md). Broader cash-source and current-release acceptance remain open.


## Prior payment, accessibility and report checks (2026-10-19)

Focused payment source, evidence-review keyboard, report and recovery concurrency evidence is retained in [status.json](status.json) and the [handoff history](auditsphere-execution-handoff-history-2026-10-historical.md). Human AT and full current-source release acceptance remain open.

## Repeated correction routing

Current rejected results remain visible on the latest retained rework task when automation is off; newer linked tasks avoid duplicate correction rows. Later submissions remain discoverable without changing source bindings or professional decisions. Focused Domain and browser checks passed; evidence and remaining human/full-release gates are in `status.json` under `reviewInboxHistory20261019`.

## Resource read authority

Firm-wide resource reads now require the planning role on an unscoped grant and recheck current authority after grid and workspace projection. Focused PostgreSQL checks cover last-read revocation, session changes, mixed grants and existing capacity/budget behavior. Exact evidence is in `status.json` under `resourceReadAuthority20261019`. Command-scope, browser and full-release acceptance remain open.

## Global resource command authority

Global resource changes and planning-console actions require a firm-wide planning-role grant, including retained receipt access. Direct allocation keeps exact engagement scope. Focused command, HTTP and historical receipt-upgrade checks passed. Evidence and remaining acceptance/release gates are in `status.json` under `resourceCommandAuthority20261019`.

## Resource grid keyboard accessibility

The populated resource grid now has a named keyboard scroll region. Focused component and both-route browser checks passed; built-in browser confirmed Tab entry, native arrow scrolling, visible focus and narrow document containment. Exact evidence and CI observations are in `status.json` under `resourceGridKeyboard20261019`. Human AT and full current-source release acceptance remain open.

## Time workspace read authority

Time projections recheck the current session and their original active grant scopes before returning. Deterministic same-epoch scope narrowing and session-change regressions reproduced stale successful reads before the fix, then passed with the existing focused HTTP time cohort. Evidence and remaining time/release limits are in `status.json` under `timeWorkspaceReadFence20261019`.

## Time rate, period and FSLI acceptance

The existing return/resubmit journey now changes the approved rate during review: the original entry retains its snapshot and a later entry captures the revised rate. Focused rate/correction and existing period/FSLI checks passed. Evidence and remaining client-book, field/state and release limits are in `status.json` under `timeRateAcceptance20261019`.

## Practice time and client-book isolation

Existing populated imported-source and native-ledger journeys now verify that independently reviewed billable time and corrections preserve exact client accounting rows and native balances. Focused checks passed; evidence and group/firm-ledger, field/state and release limits are in `status.json` under `timeClientBookIsolation20261019`.

## CRM authority and keyboard recovery

Opportunity creation rechecks actor/session and owner eligibility under the firm lock before replay or publication. Lead qualification and history paging now preserve intentional keyboard focus, and refresh clears stale confirmations. Focused Domain, component and both-route browser checks passed; built-in current-build evidence and remaining CRM/release limits are in `status.json` under `crmAuthorityAndFocus20261019`.
