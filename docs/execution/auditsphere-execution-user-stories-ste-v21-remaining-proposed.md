# STE v2.1 — Remaining Technical Stories

**Status: PROPOSED.** Each story comes from a gap verified in the [requirement verification table](auditsphere-execution-tracker-ste-v21-gap-verification-current.md) at `e0afe18`. Existing backlog items (AS-PAR-002, AS-PAR-009, Angular parity rows, P-gates) stay in [pending tasks](auditsphere-execution-pending-tasks.md) and are not repeated here.

Every story is sized for one agent session. Before starting, read only the files under **Read first**. "Done when" always includes the Definition of Done in `docs/testing/auditsphere-testing-strategy-definition-of-done-current.md`.

---

## STE-NXT-001 — Restore the automated test suites

*Requires the owner's go-ahead (ADR-0008).*

*Status (2026-10-08): done, with one open gap. The 457 deleted files are restored from `2713b58c`; `ci.yml` runs `npm run test:ci` again; ADR-0008 is `SUPERSEDED`. Results on `f7304708`: build 0 warnings and 0 errors; Domain 798 of 798 passed; API host 219 of 219 passed (same product code); Angular 112 spec files and 607 tests passed; E2E 211 of 226 passed. The 15 failing E2E journeys fail the same way at `2713b58c`, before the removal, so no commit after the removal caused them. They were not rewritten, so the acceptance criterion "zero failures" for the E2E project is not met until the owner fixes or retires them. Code and test changes made while triaging: the expiry race in the actor resolver (code); the naming guard's conventional exceptions now include `CLAUDE.md` (test constant, matching the policy); the fee and plan fixtures gained fields that `b972296e` made required (test); one unnamed status locator in the accounting-scope journey now selects its status by text (test). The `.NET` suites are not in the hosted workflow.*

**Story.** As the engineering team, we want the Domain, Api, E2E and Angular suites back in the tree and in CI, so that every later story has executable acceptance criteria.

**Read first:** ADR-0008; `docs/testing/auditsphere-testing-strategy-definition-of-done-current.md` §3; `.github/workflows/ci.yml`.

**Tasks.**
1. Restore deleted files only: `git checkout 47ca0b05^ -- $(git diff --name-only --diff-filter=D 47ca0b05^ 47ca0b05)`.
2. Re-add `npm run test:ci` to the `ui-build` job; keep the OpenAPI emitter gate.
3. Run the suites locally against PostgreSQL `5433`; fix failures caused by commits after the removal, stating for each whether code or test changed.
4. Restore the test-case catalog links that `aa9ff73` turned into plain text.

**Acceptance criteria.**
- `dotnet test AuditSphereOps.slnx --no-build --configuration Release` discovers and runs the restored Domain, Api and E2E classes with zero failures (E2E may be filtered if no browser runner is available; say so).
- `npm --prefix src/AuditSphereOps.Ui run test:ci` runs the restored specs with zero failures.
- `ci.yml` runs the Angular specs again.

**Constraints.** Do not rewrite tests to make them pass without explaining the behavioural change. Do not alter the fixtures' cloned-template mechanism (`PgTestSchema`).

**Done when.** ADR-0008 is marked `SUPERSEDED` and `AGENTS.md` §3 drops the "suites absent" paragraph.

---

## STE-NXT-002 — Make the agent context small and correct

*Status (2026-10-08): done. The size budgets are enforced by the docs gate in `validate-markdown-documentation.py` (status.json under 100 KB; the current slice under 60 KB). `status.json` is 29.7 KB and the current slice 39.8 KB. The moved bytes sit in two archives, checked byte for byte. Blank-line runs are collapsed under `docs/` except the byte-preserved source and reference folders, `docs/evidence/` and code fences. The docs validator checks backticked repository paths and rejects absolute `file://` links; proposed and historical documents are exempt from the path check. Fixed: 8 dead paths, 7 absolute links and one stale test citation. Finding: `docs/execution/auditsphere-execution-report-spk-04-ledger-split-current.md`.*

**Story.** As a coding agent, I want the start-here documents to fit in a session and point only at files that exist, so that I spend context on code instead of padding and dead links.

**Read first:** `docs/architecture/auditsphere-architecture-index-agent-context-current.md` §3; the three `scripts/docs/*.py` validators.

**Tasks.**
1. Collapse runs of blank lines to one in every Markdown file under `docs/` (content otherwise byte-identical).
2. Move `status.json` history keys (`verification`, `localEvidence`, `documentationWork`) verbatim into `docs/execution/status-archive-<yyyy-mm>.json`; keep `status.json` with the pointer keys (`verifiedCommit`, `active`, `remainingLocalWork`, `externalGates`, `evidenceBoundary`, …) and an `archive` key naming the moved file. No script parses these fields; the docs validator only requires `status.json` to exist.
3. Move all but the newest sections of `auditsphere-execution-current-slice.md` verbatim into a dated archive Markdown file named under the naming policy.
4. Fix or remove the 35 dead references listed by a link-and-path check (removed tests, the missing `auditsphere-ui-mudblazor-conventions-migration-current.md`, absolute `file:///Users/...` links).
5. Add a backticked-path check to `validate-markdown-documentation.py` so dead `docs/`, `src/`, `scripts/` and `tests/` paths fail CI.

**Acceptance criteria.**
- `status.json` under 100 KB and the current-slice file under 60 KB; nothing deleted (archives contain the moved bytes).
- The new path check passes; all four docs gates in `AGENTS.md` §3 pass.

**Constraints.** Move, never summarise or rewrite evidence. Keep every basename globally unique.

---

## STE-NXT-003 — Rate-card administration and the STE charge-out baseline

*Status (2026-10-08): implemented. `RateCardWorkspaceQuery` returns each role, activity and currency with its approved and draft versions, approval computed for the actor, and the STE baseline. The endpoints moved to `UiEndpoints.RateCards.cs`; `features/practice/rate-cards.ts` is routed under `staffGuard` and listed in `SpaRoutes`; the OpenAPI contract is regenerated. Tests: baseline drafts and idempotence, approver and preparer flags, and scope refusal (`PracticeTimeTests`, `SteChargeOutRateBaselineTests`); the page (7 Angular tests). The commercial E2E journeys, including the quotation preview, passed in the full run on `f7304708`; no journey covers the rate-card page itself.*

**Highest functional priority:** without it a fresh deployment cannot price a quotation.

**Story.** As a firm-wide administrator, I want to draft, review and approve charge-out rate cards, and to load the STE QAR baseline as drafts, so that quotations, budgets and time capture have approved rates without database edits.

**Read first:** `src/AuditSphereOps.Application/Practice/PracticeTimeService.cs` (`ReviseRateCardAsync`, `ApproveRateCardAsync`, `ResolveRateAsync`); `SteChargeOutRateBaseline.cs`; `src/AuditSphereOps.Api/Ui/UiEndpoints.Time.cs` (endpoint pattern); `src/AuditSphereOps.Ui/src/app/features/practice/time.ts` (screen pattern); `docs/architecture/auditsphere-angular-conventions-current.md`.

**Tasks.**
1. Application: add a named `RateCardWorkspaceQuery` returning current versions per role/activity/currency with state, preparer, approver and server-computed `canRevise` / `canApprove`.
2. API (`UiEndpoints.RateCards.cs`): `GET /api/ui/practice/rate-cards`, `POST /api/ui/practice/rate-cards` (draft revision with `ExpectedRevision`), `POST /api/ui/practice/rate-cards/{id}/approve`, `POST /api/ui/practice/rate-cards/ste-baseline` (calls `SteChargeOutRateBaseline.InitializeDraftsAsync`).
3. Regenerate `contracts/auditsphere-openapi.json`.
4. Angular: `features/practice/rate-cards.ts` with a bounded decoder, a draft form and an approve action; route under `staffGuard`; add to `SpaRoutes`.

**Acceptance criteria.**
- Loading the baseline creates four drafts — Engagement Partner 1,000, Audit Manager 750, Audit Supervisor 500, Audit Associate 200 QAR/hour, activity `General` — and none is approved.
- Running it twice creates no duplicates.
- The preparer of a card cannot approve it; a different firm-wide approver can.
- After approval, a new quotation line can select the approved card.
- A non-firm-wide user receives a scope refusal on every endpoint.

**Constraints.** Rates are `decimal` and travel as exact strings. Do not auto-approve. Reuse the existing approval roles in `PracticeTimeService`.

---

## STE-NXT-004 — Practical materiality rounding in Angular

*Status (2026-10-08): implemented. The criterion figures are tested (53,421.00 to 53,000.00 accepted; 56,093.00 refused) in `MaterialityPracticalRoundingTests`, and the Senior and Staff preparers are refused in `PlanningResourcesAndMaterialityTests.PracticalRounding`. "Auditor" is not a role in this system. The server flag `CanApplyPracticalRounding` uses the rounding command's own authorization and requires the current, source-bound, unapproved draft. The form is prefilled with the computed thresholds and, after success, shows the effective, computed and delta figures. Tests: the plan page (three Angular tests) and the flag for a Manager and a Partner (`PlanningResourcesAndMaterialityTests`). The bounds and rationale refusals were already tested.*

**Story.** As an Audit Manager, I want to round the computed planning materiality, tolerable error and SAD threshold within ±5 % with a rationale, so that the Partner approves practical figures (specification §4.2.4: 53,421 → 53,000).

**Read first:** `src/AuditSphereOps.Application/Audit/MaterialityPracticalRounding.cs`; `MaterialityEngineService.cs` (`ApplyPracticalRoundingAsync`, `RoundingRoles`); `AuditPlanWorkspaceQuery.cs`; `src/AuditSphereOps.Api/Ui/UiEndpoints.AuditPlan.cs`; `src/AuditSphereOps.Ui/src/app/features/audit/plan.ts`.

**Tasks.**
1. Add `CanApplyPracticalRounding` to `AuditPlanWorkspace`, computed with the same authorization as `ApplyPracticalRoundingAsync` (Manager or SeniorManager on the engagement) and only for a current, source-bound draft assessment.
2. In `plan.ts`, show a rounding form when the flag is true: three decimal-string inputs prefilled with computed values, rationale required; post to `POST /api/ui/materiality/{assessmentId}/rounding`; show the server message on refusal.
3. Show the effective (rounded) figures, the computed figures and each delta percentage after success.

**Acceptance criteria.**
- PM 53,421.00 → 53,000.00 with a rationale succeeds and creates a new effective draft; the Partner's approve action then targets that draft.
- A value more than 5 % from the computed one is refused with the server's message.
- SAD > TE or TE > PM is refused.
- An empty rationale is refused.
- Preparers and Partners do not see the form.

**Constraints.** Angular never decides eligibility; it renders the flag. No client-side percentage check is authoritative.

---

## STE-NXT-005 — Partner early compliance lock in Angular

*Status (2026-10-08): implemented on the completion page. A successful lock is tested in `completion.spec.ts` (POST body, then reload to FROZEN with the frozen time). Readiness loads for a scheduled file; a Partner-only refusal hides the control; blockers are listed; the lock posts the digest-bound payload; a stale refusal reloads readiness and keeps the Partner's reason; the frozen time is shown from the new `frozenAt` field. Tests: five component tests. Not executed in this change: an end-to-end lock against the database.*

**Story.** As the Engagement Partner, I want to lock a signed engagement file before day 60 after reviewing its archive readiness, so that the specification's "manual Partner command" (§4.4.3) is available.

**Read first:** `src/AuditSphereOps.Application/Records/FileFreezeService.cs` (`GetArchiveReadinessAsync`, `RequestEarlyComplianceLockAsync`, `ArchiveReadinessView`); `src/AuditSphereOps.Api/Ui/UiEndpoints.Completion.cs` (`early-lock`, `early-lock/readiness`); `src/AuditSphereOps.Ui/src/app/features/audit/completion.ts` (freeze section).

**Tasks.**
1. In the completion freeze section, for a `SCHEDULED` freeze, load `GET /api/ui/engagements/{id}/early-lock/readiness` (Partner only; a `403/404` means hide the control).
2. Show blockers; when none, show the digest-bound confirmation: checkbox, rationale, lock button posting `{ expectedRevision, partnerConfirmed, rationale, archiveReadinessDigest }`.
3. On success reload the workspace; show `FROZEN` and the frozen time.

**Acceptance criteria.**
- A Partner with no blockers locks the file; state becomes `FROZEN` before the due date.
- If readiness changed after it was loaded, the command is refused as stale and the UI reloads readiness.
- Non-Partners never see the control.
- An already-frozen file shows no lock action.

**Constraints.** Send `expectedRevision` as the digit string the API expects (`EarlyLockInput.ExpectedRevision` is a string); check how `ArchiveReadinessView.Revision` is serialized and decode it without precision loss. Never retry the lock automatically after an unknown outcome.

---

## STE-NXT-006 — Show the advance-invoice preparation state

*Status (2026-10-08): implemented. NOT_APPLICABLE renders when there is no agreement (`fee-agreement.spec.ts`). The `AutomaticFeeInvoices:Enabled` key is tested (`FeeAgreementConfigurationTests`), and false maps to PENDING_AUTOMATION_DISABLED (`CommercialWorkflowTests`). The decoder accepts only the nine server states, with a bounded message; each state renders beside the advance milestone with `audit-status`; an unknown state shows the page's standard decode error. Tests: the nine states decode and render, and an unknown state is refused. Not executed in this change: the `AutomaticFeeInvoices:Enabled=false` case against a letter-backed agreement in a database.*

**Story.** As a finance user, I want the fee workspace to say why the 50 % advance draft does or does not exist yet, so that "automation disabled" is not mistaken for a fault.

**Read first:** `src/AuditSphereOps.Application/Practice/FeeAgreementWorkspaceQuery.cs` (`AdvanceInvoicePreparationStates`, `AdvanceInvoicePreparation`); `src/AuditSphereOps.Ui/src/app/features/commercial/fee-agreement.ts` (`decodeFeeAgreement`).

**Tasks.** Decode `advancePreparation { state, message }` with the state constrained to the nine known values; render the state with `audit-status` and the server message beside the advance milestone.

**Acceptance criteria.**
- Each of the nine states renders its server message.
- An unknown state value fails decoding (bounded decoder), and the page shows its standard decode error.
- With `AutomaticFeeInvoices:Enabled=false`, a letter-backed agreement shows `PENDING_AUTOMATION_DISABLED`.

---

## STE-NXT-007 — Reconcile the requirements copy with approved deviations

*Status (2026-10-08): done, pending owner confirmation of the cited ADRs. The docs gate checks the approved-deviations section, the ADR citations and the original-wording markers (`validate-markdown-documentation.py`). Limitation: it detects removal of the listed markers, not every possible deletion. The header architecture row keeps the original wording, marked superseded (ADR-0002). The portal box and the §4.1.5 bullets keep their original text and are annotated (ADR-0003). An "Approved deviations" section lists ADR-0002 to ADR-0007 and the ADR-0009 draft, all `PROPOSED`.*

**Story.** As an agent reading the requirements, I want the document to state where the implementation intentionally differs, so that I do not "fix" approved behaviour back to the original wording.

**Read first:** `docs/requirements/auditsphere-accounting-module-requirements-current.md` (header table, §3.1 portal box, §4.1.5); ADR register.

**Tasks.** Correct the header's architecture line (ADR-0002). Add an "Approved deviations" section listing ADR-0002 to ADR-0007 with one line each. Annotate the portal-password lines with a pointer to ADR-0003 (keep the original text, marked as superseded).

**Acceptance criteria.** Docs gates pass; no requirement text deleted.

*Depends on:* owner approval of the cited ADRs.

---

## STE-NXT-008 — Verify the two unchecked requirement rows

*Status (2026-10-08): done for the evidence, not for the link. Both rows were checked with file and line evidence; 4.1.4 holds. A later review found that the AR Test and Audit Workprogram links on the split dashboard pass their context in query parameters (area, line, section, period, revision, return path) that neither destination reads, STE-NXT-013 has since made the links carry their context.*

**Story.** As the reviewer of STE coverage, I want the last unchecked rows examined, so that the verification table has no blind spots.

**Read first:** verification table rows 4.1.4 (template choice in Angular) and 4.3.1 (split dashboard); `features/commercial/proposal.ts`; `features/engagements/statement-contracts.ts`; `features/audit/fieldwork.ts`.

**Tasks.** For each row, record whether the Angular screen offers the service-route template choice and whether the statement view shows P&L above balance sheet with current, prior and variance columns and per-line `[AR Test]` and `[Audit Workprogram]` entry points. Update the table with file and line evidence; open a story for any gap.

---

## STE-NXT-009 — Decide how end-of-service benefits are recorded

*Status: decided 2026-10-09 under the owner's delegated decision: option (b), a monthly provision accrual entered by a person through the ledger maker/checker, with the basis recorded. Recorded in ADR-0010 (APPROVED 2026-10-09 for the recording mechanism). Posting waits for the accounting treatment, which is not yet confirmed. The follow-up story is STE-NXT-014. No code in this step.*

*Decided; the code belongs to STE-NXT-014.*

**Context.** Specification §3.5 lists "Staff Salaries, End of Service, & Benefits" among firm expenses. `FirmExpenseCategories` has `RENT`, `SALARIES`, `PETTY_CASH`, `UTILITIES`, `OTHER`; no end-of-service accrual exists.

**Options.** (a) Add an `END_OF_SERVICE` expense category posted like other expenses. (b) Treat it as a monthly accrual journal to a provision account through the existing ledger maker/checker. (c) Leave it under `SALARIES`/`OTHER` and record that choice.

**Done when.** The choice is recorded as an ADR; if (a) or (b), a follow-up story with acceptance criteria exists.

---

## STE-NXT-010 — Decide whether PBT normalization is in scope

*Status: decided 2026-10-09 under the owner's delegated decision: Option A, no normalization, as the methodology note `docs/execution/auditsphere-audit-report-normalized-pbt-spk-03-proposed.md` recommends. Recorded in ADR-0011 (APPROVED 2026-10-09). The code already matches.*

*Decided under the owner's delegation, informed by spike SPK-03.*

**Context.** Specification §4.2.4 says "Normalized Profit Before Tax". `MaterialityEngineService` labels its PBT benchmark "mapped balances, excluding tax; no normalization applied".

**Done when.** Either an ADR records "no normalization; the Manager uses a mapped-line benchmark or adjusts the rate", or a story defines the normalization adjustments, who approves them and how they are evidenced.

---

## STE-NXT-011 — Protect the released archive with an independent read-only copy (SPK-01 option 2)

*Decided 2026-10-09: not approved for build now. The release store stays the authority (ADR-0009). Building this needs a live tenant to verify read-only enforcement (BLOCKED_EXTERNAL) and a separate review of the client-sites worker's scope before any archive write. No code.*

**Story.** As a compliance administrator, I want the released bundle and its manifest copied to a read-only archive library, with each copied file verified by SHA-256, so that the provider holds a protected copy of the exact issued bytes.

**Read first:** `docs/architecture/adr/auditsphere-architecture-adr-0009-archive-authority-release-store-proposed.md`; `docs/execution/auditsphere-execution-report-spk-01-archive-immutability-proposed.md` §2 (option 2); `src/AuditSphereOps.Application/Documents/ClientSharePointSites.cs`; `src/AuditSphereOps.Application/Records/FileFreezeService.cs`.

**Tasks.**
1. Design the archive library, its grants (a compliance read group only) and the worker that creates it under the existing owner-approved client-sites exception.
2. After the freeze, copy the five-part bundle and manifest; verify each copy's SHA-256 against the released bytes; persist the copy's provider identity.
3. Record the provider state as `OBSERVED` only after a read-back of the library's permissions matches the expected state. Otherwise keep `BLOCKED_EXTERNAL`.
4. Make the copy idempotent per release digest. Never retry an unknown provider outcome blindly.

**Acceptance criteria.**
- Every copied file hashes to the released SHA-256.
- A staff identity assigned to the client site cannot read the archive library.
- An unknown or failed provider result leaves the state `BLOCKED_EXTERNAL`.
- Repeating the copy for the same release digest creates no second copy.

**Constraints.** Stay within the capability model: no new Microsoft scope. The client-sites certificate is never mounted in the API, the Web host or the document worker.

*Depends on:* owner approval of option 2 and of ADR-0009.

---

## STE-NXT-012 — Show the local, provider and lifecycle archive states separately

**Story.** As a Partner or compliance administrator, I want the archive state shown as separate items, so that a local freeze is never presented as provider protection.

**Read first:** STE-GAP-008 section F in `docs/execution/auditsphere-execution-user-stories-ste-v21-gap-closure-current.md`; `src/AuditSphereOps.Application/Acceptance/EngagementLifecycleQuery.cs` (`ComplianceWarningFor`); `src/AuditSphereOps.Ui/src/app/features/engagements/engagement-lifecycle.html`.

**Tasks.**
1. Add to the lifecycle response a `localArchiveState` (`SCHEDULED`, `FROZEN`, `AMENDMENT_OPEN`) and a `providerProtectionState` (the `ExternalReadOnlyStates` value), next to the canonical stage and the existing compliance warning.
2. Regenerate `contracts/auditsphere-openapi.json` and update the decoder in `engagement-lifecycle-contracts.ts`.
3. Render the local state, the provider state and the warning as separate items. Never show "protected" or "immutable" for a provider state other than `OBSERVED`.

**Acceptance criteria.**
- A frozen file with `ExternalReadOnly = BLOCKED_EXTERNAL` shows the local state `FROZEN` and the provider state `BLOCKED_EXTERNAL` side by side.
- The words "immutable" and "protected" never describe a provider state other than `OBSERVED`.
- An unknown provider state fails decoding.

**Constraints.** Computed from the freeze record, never stored as a status column (ADR-0004).

---

## STE-NXT-013 — Make the statement analysis links reach their screens

*Status: implemented 2026-10-08. The links send the keys the screens read (area, line, period, revision, return path). The analytical screen pre-fills only after the server confirms the statement revision, refuses a stale link, and offers a validated return to the statement. Fieldwork focuses the risks recorded against the line. Tests: `core/statement-link.spec.ts`, `analytical-preparation.spec.ts`, `fieldwork.spec.ts`.*

**Story.** As a reviewer working from the split dashboard, I want the AR Test and Audit Workprogram links to open the screen already pointed at that line, so that I do not re-enter the area, line, period and revision.

**Read first:** `src/AuditSphereOps.Ui/src/app/features/engagements/statements.html` (the links, about lines 197 to 223 and 374 to 386); `src/AuditSphereOps.Ui/src/app/features/accounting/analytical-preparation.ts` (reads only `route.paramMap`); `src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.ts` (reads no query parameters); `src/AuditSphereOps.Ui/src/app/app.routes.ts` (`analysis/new`, `audit-fieldwork`).

**Tasks.**
1. On the analytical preparation screen, read `area`, `line`, `section`, `periodStart`, `periodEnd`, `revision` and `returnUrl` from the query string and pre-fill the form.
2. On the audit fieldwork screen, read `area` and `line` and open the workprogram for that line.
3. Compare the link's `revision` with the statement basis on the server; refuse a stale revision and show the mismatch instead of pre-filling.
4. After save, return to `returnUrl`.

**Acceptance criteria.**
- AR Test from a statement line opens analytical preparation with area, line, period and revision filled in.
- Audit Workprogram from a statement line opens the workprogram for that area and line.
- A stale revision in the link shows a refusal and fills nothing.
- After save, the user returns to the statement line they came from.
- Tests cover the pre-fill, the stale-revision refusal and the return path.

**Constraints.** Query parameters are context only; the server re-derives the statement basis and the user never supplies the revision as authority.

---

---

## STE-NXT-014 — Record end-of-service accruals as a provision

*Status: open and blocked. Follow-up of ADR-0010 (approved 2026-10-09). No posting may happen until the accounting treatment is named by a qualified accountant and confirmed by the owner. That confirmation has not been given.*

**Story.** As the firm's finance manager, I want the end-of-service obligation accrued each month from an entered basis, so that the provision and the period expense show the obligation before anyone leaves.

**Read first:** `docs/architecture/adr/auditsphere-architecture-adr-0010-end-of-service-accrual-proposed.md`; the firm ledger services and the manual journal maker and checker; `FirmExpenseCategories`.

**Tasks.**
1. Add a provision account for end-of-service benefits to the firm chart.
2. Add a monthly accrual journal: a maker enters the amount, the period, the calculation basis (method, inputs, date) and a reason; a different checker approves it through the existing maker and checker.
3. Show the cumulative provision on the firm balance sheet and the period expense in operating expenses.

**Acceptance criteria.**
- A maker cannot approve their own accrual.
- An accrual cannot be posted without a basis and a reason.
- Posted accruals are append-only; a correction is a reversing journal.
- The provision and the period expense appear on the firm statements; the expense is not shown as salaries paid.
- The platform stores no computed estimate and presents none as a professional conclusion.

**Constraints.** The platform computes nothing for the estimate. The owner confirms the accounting treatment before the first posting.

*Depends on:* the accounting treatment, which is not yet confirmed. ADR-0010 is approved.
