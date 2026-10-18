
# AuditSphereOps — Current State & Active Slice Handoff

**Status:** CURRENT

**Purpose:** Compact, authoritative handoff of the active implementation slice, recent verified changes, local environment state, and next actions.

**Authority:** Active execution handoff document. Volatile metrics (exact test counts, migration count, verified commit SHA, CI run IDs) belong exclusively to [`docs/execution/status.json`](status.json).

**Audience:** AI coding agents and human developers.

**Chronology:** Sections are newest first. An older section describes the state
when it was written; where a later section supersedes it, the later section is
current. The top-level `verifiedCommit` in `status.json` names the last commit on
which the full suite was run; per-slice records carry their own evidence.

> **Reading note:** Any older section that says the Web host remains present or that
> retirement is `NOT_READY` describes the state when it was written. The owner retired
> the host and merged that removal to `master` as `e7ebe27c`; the current tree contains
> only Angular.

## Reversible client archive history (2026-10-18)

Commit `9cdbfb19e512c8d9e849fe95cfd6f2d38a33b2c7` adds a reversible client archive state backed by append-only actor, UTC time and revision events. Archive/restore is client-grant authorized, locks the client row, checks the expected revision and does not change client status, engagement records, evidence or authorization. Authorized portfolio and global search retain archived clients and label them; the client profile offers a scoped archive/restore action. The migration refuses downgrade while lifecycle events remain and has not been applied to a shared or live database.

Focused verification passed: PostgreSQL Domain tests 2/2 for immutable archive/restore history and RelationshipManager search isolation on `9cdbfb19`; Angular client, portfolio and engagement workspace specs 26/26 on `f5f96d9e`; API/OpenAPI emitter Release build with 0 warnings/errors; OpenAPI drift; EF model drift; and all four documentation validators. Fast CI `39132751124` and full release verification `39132763882` failed on `9cdbfb19` because an engagement-spec client fixture lacked the new required archive fields. The fixture was updated without weakening assertions and pushed as `f5f96d9e`. The fast-CI rerun `39132894411` and exact-SHA full-release rerun `39132911253` were in progress at handoff. Full suites remain CI-only.

Built-in-browser acceptance is **NOT_VERIFIED**: the existing `127.0.0.1:5029` app renders only its workspace shell and serves stale code. I left the user's process and database untouched. ASPM-CRM-02 remains PARTIAL pending current-SHA CI completion and authenticated browser acceptance. Push also triggered image workflow `39132894382`, which only builds/pushes private GHCR acceptance images; no VPS deployment occurred. Other unrelated worktree edits (two legacy HTML deletions, an untracked architecture HTML file, and historical execution-record edits) were preserved and excluded from these commits.

## Firm receivables age all posted invoice categories (2026-10-18)

The ASPM-FIN-03 query regression now exercises posted firm invoices typed as `ADVANCE`, `FINAL`, `TIME`, and `OTHER`, with effective payment terms, posted cash, credits and approved allocation corrections evaluated at the UTC as-of date. The final-invoice row is a synthetic query fixture; it does not bypass the production balance-invoice creation gate. The time-invoice row is produced through the existing billing service from approved synthetic time. Client-bookkeeping invoices remain outside the firm receivables query.

Focused PostgreSQL verification passed 1/1 in `FirmReceivablesAgingQueryTests.ReportUsesApprovedTermsAndOnlyReceiptsCreditsAndAllocationsEffectiveByUtcAsOfDate` (43 seconds). This completes the FIN-03 source/query criterion; exact-SHA full release verification remains pending. The prior full release run `39117606930` on `9f15c482` recorded 1,368 passed and two failed Domain tests: the QAR 12,400 string-format fixture (corrected and focused-pass on `954a623f`) and an EF execution-strategy wrapper around the expected retained-history PostgreSQL `55000` refusal. The latter assertion now checks the underlying SQLSTATE and passed its focused test 1/1. I cancelled that stale run after the Domain failure; its API and E2E jobs did not complete and provide no pass evidence. Detailed run evidence is recorded in [`status.json`](status.json) and [`status-archive-2026-10.json`](status-archive-2026-10.json).

Fast CI run `39120222007` passed on `1f4e5430`: Docs Health, Angular and .NET Release all succeeded. Run `39119821556` had already passed the regenerated OpenAPI `effectiveStatus` contract on parent `9fad72d0`, correcting run `39119098499`'s drift failure on `954a623f`. The full release suite has not run on the current source. The documentation/status commit will trigger a fresh fast gate before full release verification. No full test suite was run locally.

## Client relationship dates and retained history (2026-10-18)

Commit `05406665c6d7e8ba1413b189d00d1cc1d5d12ba0` preserves revoked client relationships in the authorized hierarchy and reports `ACTIVE`, `FUTURE`, `EXPIRED`, or `REVOKED` using the current UTC date and the stored effective period. The legal-entity panel now displays the existing registration number and jurisdiction alongside legal name, tax identifier and entity type. Counterpart details remain omitted unless the actor has current authorization for both client records.

Focused evidence on this source: the PostgreSQL Domain tests `OrganizationRelationships_BuildsHierarchy_AndEnforcesCycleAndBoundaryInvariants` and `OrganizationRelationships_PreservesEffectivePeriodAndProjectsCurrentStatus` passed 2/2; the Angular `client-relationships.spec.ts` passed 8/8; `bash scripts/contracts/verify-openapi.sh` passed; and the Angular production build passed with the existing `commercial/settings.scss` component-style budget warning (7.53 kB against 4.00 kB). Built-in-browser verification remains **NOT_VERIFIED** because the existing port 5029 tabs render only the workspace shell with no route content; I left that process untouched. No client records or live provider settings were changed.

ASPM-CRM-02 remains PARTIAL: the product has no established client archival lifecycle or searchable archived-client history. Its direct fast CI run `39118460754` was cancelled after a newer commit; run `39119098499` failed OpenAPI contract drift on `954a623f` because `effectiveStatus` was missing from the generated contract. The contract was regenerated in `9fad72d0`; run `39119821556` passed on that parent SHA, and `39120222007` passed on `1f4e5430`. Full release run `39117606930` is pinned to `9f15c482`; its Domain job recorded the two failures described above and the run was cancelled before API/E2E completion. Detailed evidence is in [`status.json`](status.json#targetedVerification.aspmCrm02RelationshipHistory20261018). The previous lead-history slice's fast CI passed on commit `6819612a81c902bd278a1842b883e741b084edce` (run `39117697497`); its current browser acceptance remains unverified.

## QAR 20,000 commercial fee acceptance fixture (2026-10-18)

The existing PostgreSQL commercial-cycle regression now uses the commissioned QAR 20,000 audit-fee example: the approved agreement creates QAR 10,000 advance and balance obligations; a QAR 4,000 allocation leaves QAR 6,000 outstanding and `AdvanceCurrentlyPaidAsync` false; a separate QAR 6,000 allocation clears the advance gate. It continues through finance review/posting, exact-offer acceptance, idempotent payment references, partial balance settlement, reversal/recovery, and retained receipts/vouchers. This is a focused service/database acceptance check, not browser acceptance of activation or full product completion.

Focused verification: `dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~AgreedFee_AdvanceReceiptEmailAndBalance_FollowTheFiftyFiftyCycleOnceEach'` passed 1/1 on source commit `954a623f799b0871afc85937d43267726d74ee7c`. The QAR 20,000 / 4,000 / 6,000 fixture therefore corrects the amount assertion that failed in old full run `39117606930` on `9f15c482`. Fast CI run `39119098499` failed on the missing `effectiveStatus` OpenAPI schema; `9fad72d0` regenerated it, and fast runs `39119821556` and `39120222007` passed on `9fad72d0` and `1f4e5430`. The older full release Domain job recorded 1,368 passed and two failed; I cancelled the stale run before its API and Playwright E2E jobs completed. Detailed evidence is archived at [`status-archive-2026-10.json`](status-archive-2026-10.json#keys.targetedVerification.aspmCom03QarAcceptance20261018).

## Retained lead action history (2026-10-18)

ASPM-CRM-01 now retains append-only lead activity for creation, qualification, opportunity creation, proposal revision/review/marking as sent, acceptance or decline, and the exact client-conversion link. Each event is written with its business command; idempotent retries do not add duplicate events. The lead workspace projects source, current owner and creation time, plus a stable chronological history page capped at 200 events. Authorized staff can request older history with a lead-bound keyset cursor.

Existing leads receive only a clearly marked metadata backfill for source, creation time and owner. It does not invent historical actors or actions, and the workspace identifies this legacy-history gap. The criterion remains PARTIAL because historical commercial actions cannot be reconstructed and manual browser acceptance is unavailable.

Focused evidence on this source: the PostgreSQL Domain cohort passed 4/4, the Angular decoder/workspace spec passed 5/5, the API Release/OpenAPI emitter build passed with zero warnings and errors, OpenAPI drift and EF model-drift checks passed, and the Angular production build passed with the existing `commercial/settings.scss` budget warning (7.53 kB against 4.00 kB). Documentation checks and exact source SHA are recorded in [`status.json`](status.json#targetedVerification.aspmCrmLeadHistory20261018).

Built-in-browser acceptance is **NOT_VERIFIED**. The existing API on port 5029 still serves a blank main area and the browser console reports a missing hashed Angular chunk; I left that long-running process untouched. No live provider, client data, professional decision or production gate was changed or accepted. The full release workflow on the preceding source commit `de8b6655d250867262550e998cd6d5fe46388977` is not a pass: its Domain job failed while API and Playwright jobs remained in progress at the last observation. Current-source CI is still required.

## Pinned workflow task designation in queues (2026-10-18)

Commit `e4c5fa5f6def0077662b784650ac8d66f53ba956` projects each templated task's Required/Optional designation from its exact firm, pinned template revision and step. Manual tasks return no designation. The Angular My Work list, board and calendar show the value as plain text. It is informational only and does not gate task completion, lifecycle transitions or professional review.

Focused PostgreSQL coverage passed 1/1 in `EngagementWorkflowPlanTests.PlanPinsPublishedTemplateAndSchedule_AndRecoversRetryWithoutDuplicateTasks`; it verifies the old plan still reports Required/Optional after a later draft revision. The focused `work-queue.spec.ts` passed 18/18, including all three layouts and manual tasks. The OpenAPI emitter Release build passed with 0 warnings/errors, the generated contract is current, and the Angular production build passed with its existing `commercial/settings.scss` budget warning (7.53 kB against 4.00 kB). Fast CI run `39113115375` passed on the preceding source commit `aad8c8de`; full release verification run `39113751546` was dispatched against this exact source commit.

Built-in browser smoke check on the current Angular dev server at port 4301 used the existing synthetic API at port 5029. The queue rendered its manual synthetic task without a Required/Optional label. That API process predates the new response field, so positive template-backed labels were not browser-verified; they are covered by the Angular spec. No business records were changed. ASPM-WF-02 remains PARTIAL: required-task graph semantics, broader role/accessibility acceptance and full release verification remain open.

## Client portal invoice settlement projection — implementation packet (2026-10-18)

**Implementation commit:** `de8b6655d250867262550e998cd6d5fe46388977`, pushed to `master`.

- **Task and requirement:** implement the buildable projection slice of ASPM-COM-06 / COM-06 in the proposed complete-practice-management specification. The criterion requires distinct invoice lifecycle, posting/sending, cash collection, due/overdue and provider-delivery truth.
- **Authority and existing behavior:** the portal currently uses `ClientPortalFinanceQuery` and exact client/engagement grants; `BillingStates`, append-only `InvoicePaymentTermsRevision`, `FirmReceivablesAgingPolicy` and `FirmReceivablesAgingQuery` define invoice lifecycle, recorded cash, approved reversals, issued credits, effective due date and UTC-date aging. The criterion audit at ASPM-COM-06 confirms exact grant scope but identifies the missing state projection. Existing provider rules distinguish provider acceptance from delivery.
- **Delta:** expose actual `PostedAt` and `SentAt`, net recorded cash after approved allocation reversals, issued credit, outstanding amount, the latest effective independently approved due date and shared aging bucket. Do not present a draft/unposted/cancelled invoice as paid or overdue. Invoice-specific provider delivery is not currently linked to the durable mail record, so report it as untracked; do not infer it from `SENT`, `PROVIDER_ACCEPTED`, or receipt-mail state. Include the UTC as-of date used for aging.
- **Boundaries:** no invoice send, payment, credit, accounting, provider, configuration or authorization mutation; preserve exact client and engagement grants, existing audit 50/50 terms, immutable invoice/receipt/reversal history, and all unrelated firm-finance data. Preserve lifecycle status separately from collection and aging status; a credit-settled invoice is not cash-paid.
- **Code owners:** `Application/Documents/ClientPortalFinanceQuery.cs`, `Ui/src/app/features/portal/finance.ts`, generated OpenAPI contract, the existing `CommercialWorkflowTests` portal finance scenario and `home.spec.ts`; update the PM criterion audit and active handoff only with observed evidence.
- **Focused acceptance:** PostgreSQL behavior check for full cash payment, reversal-reopened partial amount, approved due date/overdue bucket, draft/unposted and delivery-untracked states while retaining sibling-scope denial; one focused Angular portal-finance spec validates decoder and truthful lifecycle/collection/aging labels. No full local suite; no live-provider acceptance inferred.
- **Gates:** full exact-SHA CI remains the complete regression gate. Browser-visible behavior must be exercised in the built-in browser where a current local host can be started without disturbing the user's server or business data. Provider delivery proof and broader COM-06 acceptance remain external/incomplete until an invoice-specific provider receipt exists.

**Observed result:** OpenAPI emitter Release build passed with 0 warnings/errors; OpenAPI drift check passed. The focused PostgreSQL Domain regression `PortalFinanceAndReceiptDownloads_RespectExactClientAndEngagementScope` passed 1/1: a real draft invoice remains `NOT_POSTED` with no posted/sent dates, no outstanding receivable or aging bucket; the posted invoice is cash-paid; an independently approved due date is retained; and an approved QAR 100 reversal reopens it as partially paid and 1–30 days overdue. The focused Angular `home.spec.ts` passed 7/7, including separate lifecycle/cash/credit/aging/delivery columns and the UTC as-of date. Angular production build passed with the existing `commercial/settings.scss` budget warning (7.53 kB against 4.00 kB). The built-in browser opened the existing local host at port 5029; `/portal` redirected the signed-in staff account to the firm workspace, so client-authorized invoice data and browser console/network acceptance were not established. No business records were changed. ASPM-COM-06 remains PARTIAL; full current-SHA CI and an invoice-specific delivery receipt are outstanding.

## Certification revocation versus reviewed staffing (2026-10-18)

Added a deterministic PostgreSQL interleaving regression on source commit `8a01261066f5e105bd993ac999a61b840d2cff29`. The test pauses an independent certification revocation while it holds the firm publication lock, observes a previously reviewed higher-level assignment waiting on that lock, lets the revocation commit, and confirms the assignment is refused with no new assignment, staffing receipt or staffing grant. Its exact result and CI history are retained in [`status.json`](status.json#targetedVerification.staffingCertificationRevocationRace20261018) and the referenced status archive.

ASPM-RES-02 remains PARTIAL: broader resource and release acceptance are still open. This backend transaction-ordering change has no new UI behavior; earlier synthetic-browser evidence for the certification status display remains scoped to that display only.

## Recorded resource availability in the selected range (2026-10-18)

The resource grid now returns and displays the exact saved leave, training and public-holiday entries that overlap its selected weeks, per staff member. Each entry shows its kind, inclusive start/end dates and recorded minutes per day. The projection exposes no private reason text and adds no inferred approval state. Existing aggregate capacity arithmetic and the firm-wide resource authorization boundary are unchanged.

The source API response, generated OpenAPI schema, selected-range decoder, and focused Domain/Angular/browser checks cover the new projection. Exact commands and counts are recorded in [`status.json`](status.json#targetedVerification.resourceAvailabilityProjection20261018). Fast CI run `39109639455` passed on the exact source commit; it is not the full release regression suite. The existing local browser server on port 5029 still serves a shell whose route content is blank; that manual check remains blocked by the stale local host rather than treated as acceptance. ASPM-RES-01 remains PARTIAL pending broad role, range-boundary, responsive/accessibility and current release acceptance.

## Phase- and risk-attributed resource allocations (2026-10-18)

Resource allocations now preserve separate planned-hour lines for each engagement, staff member, Sunday-based week, audit phase and optional risk area. The reviewed preview, request hash, durable receipt and PostgreSQL uniqueness/receipt guards bind the exact line. Different phase/risk lines coexist and all contribute to the weekly capacity total. Historical rows backfill to visible `UNASSIGNED` attribution, remain in capacity totals, and can be removed only through a reviewed zero-minute action for that exact legacy line; new positive allocations require an assignable phase. The Angular form requires the phase, normalizes a selected date to Sunday, and displays phase/risk on saved lines. Existing staffing and enabled-staff authorization checks remain in force.

Focused checks on source commit `5996f36d49bf89feba9c99a7fab7c567c5f41c95`: PostgreSQL Domain regression 1/1 (two lines, aggregate capacity, exact-line removal and retry); resource Angular spec 15/15; resource E2E cohort 4/4; API and E2E Release builds with 0 warnings/errors; EF model drift and OpenAPI checks pass. Angular production build passes with the existing `commercial/settings.scss` warning (7.53 kB vs 4.00 kB). Fast CI run `39108092465` was in progress when this handoff was written; release-wide suites remain CI/CD work.

Built-in browser inspection reached the existing local server on port 5029, where the Resources navigation selected but the workspace stayed blank because the stale shell referenced missing lazy chunk `chunk-Eamlq2bo.js`. I left that running process untouched, so the current source UI was not manually browser-verified. Broader date-boundary, role, responsive/accessibility acceptance and full current-SHA CI remain open. The focused allocation evidence is in `status.json` at `targetedVerification.practiceResourcePhaseRisk20261018`; the earlier eligibility-only record is preserved in the status archive.

## Correspondence contact effective-date enforcement (2026-10-18)

Purpose-specific recipient resolution now excludes active contacts whose validity interval does not overlap the requested UTC date, including explicitly selected overrides. Dispatch receipt creation checks the contact again at the exact recorded dispatch instant and refuses inactive, future-dated, or expired contacts. The existing PostgreSQL routing regression passed with expired/future normal routing, override and dispatch cases. ASPM-CRM-03 remains partial: DEC-09 routing precedence, ambiguity remedy, external provider delivery and acceptance at the actual send boundary remain open. Exact source commit and focused test metadata are in `status.json` under `targetedVerification.practiceContactValidity20261018`.

## Resource allocation eligibility regression (2026-10-18)

The resource allocation paths already require current staffing for the exact engagement and use a locked target identity that must remain enabled staff. A focused regression now proves a disabled assignee is refused at allocation preview, reviewed execution and the direct write command, with no allocation created; the existing certification receipt remains recoverable. ASPM-RES-01 remains partial because phase/risk-area planned allocations and broader availability dimensions are still open. Exact test metadata is in `status.json` under `targetedVerification.practiceResourceEligibility20261018`.

## Time-source billing reservation guard (2026-10-18)

Commit `5e9a1eb5` prevents an approved time entry from being corrected after its exact revision is reserved to an invoice. Billing also walks a corrected entry's `SupersedesId` ancestry and refuses it if any predecessor revision is already reserved, covering retained legacy chains. The focused PostgreSQL regression asserts the original invoice/allocation remain the only billing reservation and that the forbidden correction creates no child entry; it also attempts to bill a legacy correction descendant and verifies no second invoice is created. The named Domain test passed 1/1. Full suites and authenticated browser verification were not run for this server-side change. ASPM-RES-04 and ASPM-FIN-01 remain partial; milestone and other source reservation cases remain open. Fast CI is pending on the pushed commit.

## Workflow dependency graph bound (2026-10-18)

The existing dependency command checks the complete engagement graph before saving and refuses graphs beyond its 500-task/4,000-edge bounds. Focused PostgreSQL regressions now cover both overflow paths: a valid published-template graph with 501 tasks, and an acyclic 4,001-edge pinned graph created through the template and plan services. Both return `GateBlocked` with no dependency revisions, task-binding revision changes or dependency-change activity. The 4,001-edge test passed 1/1 locally. Current-SHA full release verification is running in CI; no full suite was run locally. `ASPM-WF-02` and PMCOMP-06 remain partial for broader acceptance. Exact test evidence is in `status.json` under `targetedVerification.workflowDependencyGraphLimit20261018`.

## Engagement workflow phase cues (2026-10-18)

The engagement workflow now independently reads the scoped saved Audit Plan milestone endpoint and displays fieldwork start in phase 3, report and filing dates in phase 6, and archive deadline in phase 7. Dates are displayed only when a saved plan exists; an explicit empty response remains empty, a role-denied response is reported as unavailable, and session/route changes clear cached dates. The panel states that a filing cutoff is not independently validated against jurisdictional law and keeps operational task deadlines separate. The existing plan/configuration page remains the source of the dates; no legal deadline calculator or approval was introduced.

Focused Angular workflow spec passes 3/3, including the populated schedule, explicit no-plan state and 403 denial state; the production build passes with only the existing `commercial/settings.scss` component-style budget warning. Built-in browser read-only verification on the local synthetic engagement observed HTTP 200 for its milestone, workflow and task reads, the no-saved-plan message, and no console errors or warnings. No business data was changed. Exact source commit and current checks are recorded under `targetedVerification.ste22F07WorkflowPhaseCues` in `status.json`. The feature remains partial pending an approved/versioned jurisdictional source mapping, legal validation, broader role/responsive/accessibility acceptance and current CI/CD.

The seven-phase engagement projection now shows each phase's nearest due date from its current, open or in-progress workflow tasks. The UI labels these as task deadlines; they are not statutory milestones and do not satisfy readiness gates. Canonical next-action links are now projected only when the caller has the responsible-role grant and, for staffed audit roles, the matching active engagement assignment. Finance Manager links also require firm-wide scope. A focused PostgreSQL regression covers nearest-date selection, task completion leaving the lifecycle blocked, and authorized versus denied roles. The Angular decoder spec, production build, API Release build and OpenAPI drift check pass; the production build retains the existing commercial stylesheet budget warning. The built-in browser rendered the local synthetic engagement, its blocked lifecycle, task/gate separation, and the scoped Commercial Manager action. The sample had no active dated tasks, so the date label itself was not exercised there; responsive and full role-by-role browser acceptance remain open. No business records were changed. Exact source and verification details are in the current execution tracker and the practice-management criterion audit.

## Practice-management ledger-boundary regression (2026-10-18)

NR-11 is complete for the named three-book and reporting cases. Firm-ledger posting and the actual firm billing-invoice post leave client books unchanged; a client sales-invoice post creates no firm journal/posting and leaves the firm analytics aggregate unchanged; and the approved consolidation workflow preserves exact component TB rows, imported GL, native-journal evidence and package manifests. The original PostgreSQL cohort passed 3/3 on `a67b296f`, and the new firm-billing/client-report extensions passed 2/2 in the current focused run.

NR-12 remains partial. The crosswalk now maps five v2.2/host switches to existing off-path API and worker tests, their retained-state boundaries, and uncovered UI/configuration checks. The Angular work-queue spec also pins the explicit 403 unavailable message and confirms denied responses do not render summary/task cards (2/2 tests passed on the current slice). Built-in browser opened `/app/work`, but the local API currently has workflow tasks enabled and returned its normal view with one synthetic task; the route was read-only and produced no console errors or warnings. Therefore the disabled route itself was not browser-verified. The matrix is evidence mapping, not a fresh full regression or deployed-configuration acceptance. PM-01 remains gated on an approved, versioned jurisdictional source mapping; saved Audit Plan milestones and operational task deadlines are separate projections.

## Practice time return and resubmission (2026-10-18)

RES-04 now has an independent return path with a mandatory reviewer reason. A returned entry can be resubmitted as a new review round on the same time-entry identity; its captured rate and billing classification remain unchanged. PostgreSQL retains submission, return and approval events append-only, and unapproved/returned time remains outside billable WIP. The current Angular time workspace displays the latest return reason and supports resubmission.

The previous schema did not retain the submitter for historical rows. The migration records known legacy submission timestamps as explicitly unattributed and backfills an approval actor only where the prior row stored one. It does not invent historical attribution. Downgrade refuses after any review event is retained.

The focused PostgreSQL workflow, immutable-history and migration-refusal regression passes. The Angular return/resubmission spec, API Release build, EF model-drift check and OpenAPI drift check pass; the Angular production build passes with its existing commercial stylesheet budget warning. Exact commands, counts, commit and warning details are in `status.json` under `completePracticeManagement.returnedTimeWorkflow`.

Built-in browser verification confirms the clean preview serves the current Angular shell and its fingerprinted assets without the old missing-chunk error. That preview returned 401 for `/api/ui/session`, so authenticated time-workspace behavior remains unverified. The already-running local instance on port 5029 still serves a stale bundle; it was left untouched. No business records were changed. Full CI and authenticated browser acceptance remain pending.

## Accounting and browser-journey repair (2026-10-18)

Commit `d5c5ca7ee436c86647c18115acae063a299273c1` fixes the saved purchase-invoice draft preview path: the preparer can supply and validate the approved AP role ID required by preview, and stale preview/assent state is cleared when the role or draft changes. The purchase-invoice draft query now uses a SQL-translatable latest-draft predicate. Search suggestions remain closed while loading, after results, or on errors; long audit-record textareas are constrained to their workspace. Several browser journeys and fixtures were aligned with the current accessible labels and current workflow contracts, without skipping tests.

Focused verification on the pushed source passed: purchase-invoice workflow Angular spec (9/9), workspace search Angular spec (4/4), Angular production build (existing `commercial/settings.scss` stylesheet budget warning), the sales invoice/supplier purchase-credit browser journey (1/1), and General Ledger acceptance journeys (2/2). The built-in browser smoke check used the current Angular dev server on port 4301 with the existing local API on port 5029 and synthetic data: workspace search returned two authorized Doha results, showed no overlapping suggestion list, and reported no console errors. It was read-only; no business records or access settings were changed. The long-running API/static bundle on port 5029 was left untouched.

The full unfiltered release workflow was dispatched against the exact pushed SHA; its run and result are recorded in `status.json`. This focused evidence does not replace full release verification or live-provider acceptance.

## PBC exact-version reviewer acceptance

Staff acceptance now requires the assigned, independently authorized reviewer to confirm the exact provider-verified upload ID, revision and content hash. The accepted request stores an immutable receipt binding that source to its request submission revision, provider identity/digest, reviewer and time. Database constraints validate the receipt against the request, received upload and completed transfer operation, and prevent rewriting the accepted decision or source. Legacy upload rows remain readable but cannot be accepted without matching current provider evidence.

Focused PBC Domain and PostgreSQL guard regressions passed, as did the PBC Angular contract spec; the API Release build and OpenAPI contract drift passed; Angular production build passed with the existing `settings.scss` budget warning. Commit `4bf84641` updates two PBC browser journeys to the current accessible control labels; both selected E2E journeys passed on that commit (2/2), including exact-version reviewer assent, receipt persistence and grant-revocation denial. Fast CI run `39093158735` passed on the same SHA: documentation health, Angular build/specs, .NET Release, architecture guards, EF drift, publication and OpenAPI. The built-in browser still has a stale local PBC bundle (lazy chunk 404), so manual browser acceptance remains unverified. Full release verification is not green: the earlier run on `9e2709b4` had Domain pass, API runner communication loss and E2E timeout with 24 failures before cancellation. Live-provider/business acceptance remains external. Exact evidence is in `status.json` under `targetedVerification.pbcExactVersionAcceptance20261018` and `testSuites.latestAttempt`.

## Certification administrative review integration

Exact-source applicability rules, append-only review persistence and independent administrator publication are implemented. Current evidence and commit identities are in `status.json` under `completePracticeManagement.certificationReview`. The read workspace, API and Angular review dialog have focused verification, including a synthetic browser verification/withdrawal flow. Staffing eligibility readers are integrated and focused checks pass; deterministic revocation/assignment concurrency and broader acceptance remain unfinished; existing assignment history is preserved and legacy qualifications are not automatically verified. Only the synthetic browser database received the review migration; live/development databases remain unchanged. Client portal write-gate corrections are committed separately, with focused evidence in the crosswalk.

## Resource range safety

Grid/allocation commands refuse unsupported dates and week counts before arithmetic. Focused Domain/API and contract checks pass; browser refusal and valid-range recovery pass. Certification entries explicitly remain unverified. Pure exact-source applicability checks pass; persistence, review actions and staffing integration remain unimplemented. Broader staffing acceptance remains open. See execution ledger.

## Resource capacity reconciliation

Analytics and the weekly grid preserve all configured weekly minutes using one daily allocator, including leave deductions. Focused pure/PostgreSQL checks pass; positive browser and broader acceptance remain open. See execution ledger.

## Retained firm expense paging and recovery

Firm books now exposes bounded expense pages and exact firm-scoped expense lookup for uncertain action recovery. Focused database, API and Angular checks pass; the positive browser finance journey remains pending because the synthetic browser identity has no finance assignment. No access grant changed. Exact source/results are in the execution ledger.

## Firm monthly date boundaries

Monthly P&L safely handles supported year-0001 and December-9999 periods, preserving null missing comparatives. Monthly series above the existing 1,200-row UI boundary are refused before activity calculation. The existing balanced-ledger regression passes; full acceptance remains open. Exact results are in the execution ledger.

## Task queue source lineage

Queue rows now expose retained source keys and exact template identities in all layouts. Cards also retain FSLI/mapping, PBC and reviewer context plus task activity. Focused lineage/component checks, builds and manual-source browser inspection passed; template browser and responsive acceptance remain open. See execution ledger.

## Queue prerequisite presentation

The queue now displays a separate blocked prerequisite label consistently in list, board and calendar views using existing server prerequisite facts. Missing viewer permission alone is not a blocker; completed history stays unchanged. Focused component checks and Angular/API builds passed; exact evidence is in `status.json`. Follow-up repaired omitted safety rows in the synthetic seed while preserving application guards. Browser creation, assignment, reviewed dependency, blocked display in all three layouts and clearing after predecessor completion now pass. Responsive and broader WF-06 acceptance remain unverified.

## Current allocated-cash gate and retained settlement history

Advance-dependent activation, lifecycle, portal eligibility, balance drafting and bundle gates now share a current-settlement predicate. A stored PAID marker alone is insufficient: the invoice must be posted and match client/currency/amount, with sufficient current net cash after approved reversals. Credit-adjusted balance settlement retains its existing policy. Staff reads project the current unpaid state while original paid milestone fields and receipt bytes remain immutable. Settlement publication reacquires the existing firm fence before checking cash and first publishing PAID evidence. No database trigger was weakened.

The focused commercial/reversal/activation cohort and the corrected split-receipt case have incremental passing evidence; exact code identity and limitations are in `status.json` under `completePracticeManagement.advanceCashGate`. Current-source full regression and the browser financial journey remain acceptance work. Continue the source-backed schedule and integration gaps, including the returned-time lifecycle in RES-04, plus the independent API/Angular parity backlog; this is not full product acceptance.

## Complete practice-management inventory and parity regression repair

The supplied complete-product specification is preserved under requirements and linked to the existing PM crosswalk. PMCOMP-00 source reconciliation is complete: the [criterion audit](auditsphere-execution-report-practice-management-criterion-audit-current.md) maps all 87 ASPM-qualified criteria to current source/test-body findings, residuals and legacy aliases. Test bodies were inspected, not executed, and release acceptance remains `NOT_VERIFIED`; CI, methodology, owner and live-provider gates remain open.

Full CI exposed an untranslatable task-assignee projection and two incorrect history/receipt assertions. The assignee query now deduplicates/orders an anonymous projection before constructing its DTO; portal history checks the last retained page; authorized sibling-client receipt recovery explicitly returns no foreign receipt. The affected focused cohort passed. Exact source/results and preceding CI failure evidence are in `status.json`. Continue criterion reconciliation and exact-SHA release verification; no production acceptance is claimed.

## API and Angular parity: task implementation verification complete (T025, T028)

Both remaining parity implementation tasks from the 51-task specification have been verified with targeted checks:
- **T025 (Resolve component replacement retention before exposing correction):** In-place destructive component replacement is declined in favor of immutable new scope versions preserving original scope and component evidence. The correction handoff in `overview.ts` and `component-correction.ts` enforces selecting the identical reporting period and group from authorized options. Verified via `component-correction.spec.ts`.
- **T028 (Complete external component-pack intake, review and pinning):** Five external pack operations (submit with exact digest/hashes, reconcile, return with reason, independent approval, and pinning to scope) are fully implemented and verified. Unit tests in `external-packs.spec.ts` and scope workspace integration in `scope-workspace.spec.ts` ensure exact SHA-256 digests, six-place decimal bounds, pagination, and independent review. The PostgreSQL integration test in `ConsolidationWorkspaceApiTests` validates concurrent review serialization and retained lineage.

All 51 parity tasks in `status.json` and `auditsphere-execution-tracker-api-angular-parity-current.md` now have `implementationStatus: IMPLEMENTED_VERIFICATION_PENDING`. Full acceptance remains subject to CI/CD release verification.

## API and Angular parity: complete member discovery

Matching counterparties can now be discovered through bounded pages and search. Exact seller/buyer resolution retains selections across pages, and the outside-party path rejects an eligible member even when off page. The shared API exception envelope is documented once at the UI group boundary. Focused checks and synthetic browser search passed; see `status.json` under `apiAngularParity.localVerification.matchingOptionsSlice`. Full acceptance remains pending the original task crosswalk and current-source CI/CD evidence.

## API and Angular parity: bounded scope continuation

Components and readiness now have independent bounded pages and complete counts. Exact component correction remains available when the selected component is outside the displayed page. The focused browser check loaded the current contract and retained evidence using the synthetic local database with external effects disabled. This does not establish multi-page browser or full workflow acceptance. Current verification evidence and remaining gates are in `status.json` under `apiAngularParity.localVerification.pagingSlice`. Continue the original parity specification; do not equate this slice with completion of all tasks.

## Documentation reconciliation — review branch

The [reconciliation report](auditsphere-execution-report-documentation-reconciliation-current.md) records corrected current instructions, approved ADR scopes and delegated engineering defaults. This documentation slice uses a dedicated branch and reviewable PR; no direct master push or merge without `AUDITSPHERE-MERGE-APPROVED`. Pre-existing API/Angular integration remains preserved and outside the documentation PR. D1-DRAFT-01 is an unapproved review packet, not a change to audit gates. Qualified T003/G16/EOS and external acceptance requirements remain open. Exact baseline and observed documentation checks are in `status.json` under `documentationReconciliation`; no application test or provider acceptance is claimed.


## API and Angular bidirectional parity — active implementation

The [parity implementation tracker](auditsphere-execution-tracker-api-angular-parity-current.md) covers the attached API/Angular specification independently of the R2R and audit-programme task identifiers. Current source contains the authorization, decimal precision, workflow entry, recovery, supplier selection, portal refresh/history and consolidation ownership slices. Code presence is not final functional acceptance; current checks and remaining tasks are recorded under `apiAngularParity` in [status.json](status.json).

The allocation-reversal history route and correction handoff are connected. Review corrections restore API startup/contract generation, forward independent history pages, preserve exact normalized source ordering, clear scope-specific forms and review assent, and allow independent approval after reconciliation. Intercompany option-bound commands require reporting-period membership and reject negative revision sentinels. The ownership query now applies filtering/paging before its final DTO projection. Oversized readiness is a visible unavailable subpanel, never a partial success; full paged readiness still needs implementation.

Focused API, Domain and Angular checks passed for the affected slices; the emitted contract and drift check passed. Built-in browser smoke verified the synthetic overview, scope, external-pack history/source lines and recovered ownership panel. This was read-only browser coverage, not the complete submission/review/pinning journey or responsive/live-provider acceptance. Exact code snapshot, cohorts and limitations are under `apiAngularParity.localVerification` in [status.json](status.json). PR #46 is merged; PR #47 awaits its updated-head CI and merge. Task statuses retain their remaining acceptance work.

## AS-AWP-EXEC Atomic Tasks 05–09 — procedure execution, evidence and truthful progress — 2026-10-17

Procedure submission and review now use an exact preview, fresh assent and an append-only command receipt; direct result and review writes fail closed. Submitted work retains a server-resolved evidence snapshot (stored link, physical file or manual reference) and a digest, distinct from later links. The native fieldwork page opens any authorized procedure for work, evidence selection and independent review, with lost-response recovery by receipt. Programme, area and fieldwork projections share the current-evidence evaluator; submitted or stale work is not counted as reviewed. Every one of the 165 source IDs retains the common workspace and a bounded existing-tool or manual-work support map.

Focused verification passed for Domain 9/9, support-map 1/1 and Angular 15/15. On `65a3f999`, fast CI passed; full CI passed Angular/contracts and is running unfiltered .NET suites. New API and synthetic browser journey evidence is pending those results. Task 10 remains blocked on D1; D2/D3 remain external. See [`status.json`](status.json#targetedVerification.atomicAuditWorkingProcessTasks05to09).

## Blazor rollback host retirement — merged — 2026-10-17

The owner authorized physical retirement of the Blazor rollback/reference host despite the canonical removal gate remaining `NOT_READY`. Branch `chore/retire-blazor-host` removes the legacy Web project, its solution and release-workflow entries, and the rollback-host-only E2E journey. The API-only owned host no longer exposes a rollback switch. Historical source reviews and the retirement inventory link to the pinned pre-removal commit; the discovery inventory is retained rather than emptied, and the UI inventory gate recognizes the retired source. The owner merged the branch to `master` as `e7ebe27c`; the retired host is no longer in the current tree.

Focused verification on the branch: Release solution build passed with 0 warnings/errors; focused API tests passed 15/15 and Domain architecture guards 3/3; documentation health, filenames, narrative metrics, UI inventory, retirement inventory, OpenAPI drift and `git diff --check` passed. No full suite, hosted CI, deployment, production rollback or external acceptance was run. Evidence is in [`status.json`](status.json#targetedVerification.atomicBlazorRetirement).

## R2R-07 and CP-11 — 2026-10-17

## AS-AWP-EXEC Atomic Task 02 — exact engagement programme adoption — 2026-10-17

An engagement can now inspect only published programme versions within its authorized scope, including exact source/content identities and explicit eligibility blockers. Adoption is separate from firm methodology publication and must submit the selected version's source hash and digest. The service verifies the immutable catalogue against the current source, creates all 165 controlled baseline procedures atomically, reports baseline and ad-hoc counts separately, safely replays the same adoption, and refuses a stale or different-version adoption without overwriting in-progress work. The Angular fieldwork screen now selects an explicit eligible version; the retained Web caller also uses the exact options projection.

Focused verification: Domain adoption regression 1/1; API adoption/options regression 1/1; Angular fieldwork spec 12/12; E2E test project compilation passed (0 warnings/errors); API and Web Release builds passed (0 warnings/errors); OpenAPI drift and `git diff --check` passed. No schema change was needed. The browser journey was not run because no authenticated local synthetic fixture was available. CI for `8b0c3303` found stale generated Angular source inventory; that inventory has been regenerated locally and is queued for the follow-up documentation commit. CI remains pending for the corrected tree. This does not constitute full-suite or professional methodology acceptance. Evidence is in [`status.json`](status.json#targetedVerification.atomicAuditWorkingProcessTask02).

> **Archive:** older dated handoff sections are retained verbatim in [`auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md`](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md). They are historical evidence; consult `status.json` and current authority documents for present state.
