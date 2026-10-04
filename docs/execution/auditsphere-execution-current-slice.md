# AuditSphereOps — Current State & Active Slice Handoff

**Status:** CURRENT

**Purpose:** Compact, authoritative handoff of the active implementation slice, recent verified changes, local environment state, and next actions.

**Authority:** Active execution handoff document. Volatile metrics (exact test counts, migration count, verified commit SHA, CI run IDs) belong exclusively to [`docs/execution/status.json`](status.json).

**Audience:** AI coding agents and human developers.

**Chronology:** Sections are newest first. An older section describes the state
when it was written; where a later section supersedes it, the later section is
current. The top-level `verifiedCommit` in `status.json` names the last commit on
which the full suite was run; per-slice records carry their own evidence.

## AS-PAR-002: client-scoped audit fieldwork command isolation

PostgreSQL-backed regressions seed sibling clients A and B and use real
client-B record identifiers. A Partner scoped only to A is denied audit-program
adoption, procedure applicability changes, result submission and result review;
the target procedure, result, workpaper and review state remains unchanged. The
confirmation lifecycle test denies A-scoped create, approval, dispatch, response,
response review, alternative work, alternative review and closure against B's
engagement and confirmation records; B's confirmation, response,
alternative-work and closure state remains unchanged. The difference test
denies A-scoped creation, aggregate reads, evaluation, correction-state changes
and journal linking against B's engagement and difference; the persisted
difference remains unchanged. The area-assessment case denies A-scoped
assessment creation, completion evaluation and review against B's engagement,
leaving B's assessment unchanged. The opening-balance case denies A-scoped record,
review and planning-summary requests for B and preserves B's verification. The
finding case also denies A-scoped creation, management-response recording and
management-letter designation for B's engagement, preserving B's finding state.
The planning case denies A-scoped materiality, risk, population and workpaper
creation, along with materiality approval and workpaper submission against B;
the existing materiality and workpaper remain unchanged.
The seven PostgreSQL-backed Release Domain regressions also passed together in a
combined focused run on the current master commit. Exact evidence is in
`status.json`. The full solution regression was not run for this slice. A Domain
suite attempt on the previous checkpoint stopped after approximately 13 minutes
without a runner summary, so no full-suite result is claimed. EF reports no pending
model changes. Exact evidence is in `status.json`. No shared Development database, tenant, or
production state was changed. AS-PAR-002 remains partial for other command
families and export/count paths, plus independent review.

## Parity hardening: workpaper navigation safety, management letter designation, and attribute strata sampling

This slice resolves key parity requirements across audit fieldwork, deliverable reporting, and sampling:
- **Workpaper Navigation Safety (GAP-01/UX10):** [WorkpaperEditor](file:///Users/qts/Repos/AuditSphere/src/AuditSphereOps.Ui/src/app/features/audit/workpaper.ts) guards in-flight and unacknowledged draft edits on navigation using `confirmNavigation()` with the shared [UnsavedChangesDialog](file:///Users/qts/Repos/AuditSphere/src/AuditSphereOps.Ui/src/app/core/unsaved-changes.ts) (Save, Discard, Stay), prevents browser window close via `@HostListener('window:beforeunload')`, and registers `canDeactivate: [unsavedChangesGuard]` on `/app/audit/workpapers/:id`.
- **Management Letter Finding Designation (GAP-02/R04):** [Finding](file:///Users/qts/Repos/AuditSphere/src/AuditSphereOps.Domain/Audit/Audit.cs) records maintain persisted designation decisions (`letter_designated_at`, `letter_designated_by_user_id`, `letter_recommendation`). [AuditDeliverableService](file:///Users/qts/Repos/AuditSphere/src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs) filters exclusively for designated matters with complete recommendations when generating client-facing management letters; internal-only findings are excluded from client deliverables.
- **Reviewer-Defined Attribute Strata Sampling (GAP-03/T20/T22):** [AuditSamplingEngine](file:///Users/qts/Repos/AuditSphere/src/AuditSphereOps.Domain/Audit/AuditSamplingEngine.cs) implements attribute strata sampling supporting reviewer-selected fields (account, currency, direction, month), guaranteed stratum coverage, and persisted run provenance.
- **Commercial Quotation Recovery & Shell Pending Outcomes:** [QuotationWorkspace](file:///Users/qts/Repos/AuditSphere/src/AuditSphereOps.Ui/src/app/features/commercial/quotation.ts) persists save intent with unique request IDs for recovery without duplicated fee actions, and [PendingOutcomes](file:///Users/qts/Repos/AuditSphere/src/AuditSphereOps.Ui/src/app/core/pending-outcomes.ts) banner surfaces unresolved commands.

All 476 Angular unit tests, 647 Domain tests, and 186 API tests passed. EF Core migrations are in sync with 0 model changes pending.

## AS-PAR-002: portfolio isolation, billing commands, and stale resource review

The same-firm sibling-client differential now checks `/api/ui/portfolio/workspace`
and CSV export before and after client B receives marker data. The complete
workspace response and export stay identical for both CLIENT- and
ENGAGEMENT-scoped staff. A PostgreSQL-backed Application test also submits
client B's real account, invoice, and receipt identifiers to BillingService
reads and mutations as a client-A-scoped finance user; each is denied and the
invoice, account, receipt, allocation, and credit-note state remains unchanged.

Resource planning now has a stale-review browser journey in both `/app` and
`/ui` modes. A concurrent allocation update makes the reviewed command return
409; the UI identifies the stale review, removes the grid while the request is
unresolved, reconciles an absent receipt, and reloads the current 2-hour value
only after explicit close. The command is dispatched once.

At source commit `cdf18c014919fbfa2a89d125b706d9055036f627`, the API isolation
cases passed 2/2, the billing Application test passed 1/1, the focused Angular
resource suite passed 14/14, and the stale-review Playwright journeys passed
2/2. The Angular production build passed with a 505.46 kB initial bundle,
5.46 kB above its 500 kB warning budget. EF reported no pending model changes.
The full solution regression was not run. Built-in-browser navigation to
localhost was blocked by its URL policy; the isolated Playwright journeys used
owned PostgreSQL databases. AS-PAR-002 remains partial for other command
families and export/count paths; no shared Development migration or production
effect was performed.

## Resource grid separates approved actual hours and stays readable on mobile

Resource week cells now show approved actual hours separately from planned hours
and capacity, and include the actual amount in their accessible name. Minimum
column widths prevent the week headers from collapsing; narrow viewports scroll
inside the grid without widening the whole page.

The focused Angular resource suite passed 13/13. The PostgreSQL-backed Playwright
resource journey passed 2/2 on canonical and `/ui` routes, including a seeded
approved-time entry and 390px no-overflow/contained-scroll checks. The built-in
browser showed a 343px grid viewport, 896px scrollable grid and 112px week column
at 390px, with no console warnings or errors. The production Angular build
succeeded in a temporary output directory with a 5.46kB initial-bundle warning
over its warning budget. The shared-tree E2E attempt stopped before the page due
to concurrent pending EF model changes; the same journey passed from a clean
snapshot. No shared Development data was changed. The full suite and wider
staffing/budget acceptance remain open.

## Accounting preparation navigation reflects exact role and scope

The analytical preparation page showed the generic scope-denial message for a user
without an eligible preparation grant. The engagement page also advertised the
preparation workflows to staff who could not open them. The shared Application
authorization gate now exposes the exact existing role and client/engagement scope
capability to the engagement query, and the Angular navigation only shows those
links when that capability is present. The analytical page gives safe guidance to
ask a firm administrator to review the local role and scope. Protected direct
routes continue to enforce the existing server-side checks; no role or scope
permission was widened.

The focused Application, API, Angular, Playwright and built-in browser checks
passed on isolated synthetic data. The full solution regression was not run on
this source commit; its latest complete result remains tied to the earlier commit
recorded in `status.json`. No shared Development database data was changed. Exact
verification evidence is recorded in `status.json` under the local evidence entry
for this slice.

## Native reconciliation and specialist schedule preparation

Native reconciliation creation (`/app/engagements/:id/reconciliation/new`) and
specialist schedule preparation (`/app/engagements/:id/specialists/new`) are now
implemented with typed Signal Forms, exact server previews, explicit reviewed assent,
retained immutable actor-owned request receipts, tab draft persistence and
lost-response receipt recovery. Reconciliation creation binds exact client source
datasets, accounts, period context, reasons and evidence references, rejecting
cross-scope or unreviewed requests. Specialist schedule preparation composes
existing IAS 21 / IAS 16 asset and liability schedules with exact six-decimal inputs,
preview calculations, immutable snapshots, and actor-owned receipt recovery.
Unresolved command responses fence further writes and offer receipt verification on reload.

PostgreSQL-backed API tests (`AccountingPreparationCreationApiTests`) passed 2/2,
Playwright E2E journey (`AngularAccountingPreparationCreationJourneyTests`) passed 1/1,
all 460/460 Angular unit tests passed, and the Release solution build and production
UI build passed with zero warnings or errors. EF reported no pending model changes.
The additive migration `20261004121805_NativeReconciliationAndSpecialistPreparation`
was exercised in isolated synthetic test databases; shared Development and production
databases were not migrated. Broader reconciliation revision forms and overall migration
acceptance remain open.

The browser check exposed that the engagement menu treated `reconciliation/new`,
`specialists/new` and `analysis/new` as single router segments. Commit `935bd79`
now splits each configured path into real route segments. The focused engagement
route contract passed 3/3, an isolated production Angular build passed, and the
built-in browser opened all three forms at their intended URLs. The shared
Development client had no configured accounting profile, reporting period or
accepted source, so those forms displayed the safe unavailable state; no shared
data was changed.

The complete Angular unit suite passed 461/461 on commit `935bd79`, using a
clean temporary UI snapshot so the other agent's in-progress application shell
and quotation changes were not included. The full E2E rerun against isolated
assets was stopped after 44 minutes without a test summary; it remains
unverified, and no whole-suite pass is claimed.

The API and Domain projects also passed 186/186 and 647/647, respectively, on
feature commit `815d79f`. The focused preparation Playwright journey passed 1/1
using a clean isolated UI build. The first full E2E attempt could not start its
owned hosts because a parallel Angular test run had cleared shared `dist`. A
second attempt used the isolated UI bundle but was stopped after about 44
minutes without a runner summary; the test host had used up to about 1.5 GB and
sustained CPU. No whole-solution pass is claimed. The additive migration was
tested only against disposable PostgreSQL databases; Development and production
were not migrated.

## Angular proposal-create retry fingerprint and full regression

At pushed code commit `f92ae6887f56e20ed857a638a507099829132826`, keyed proposal
creation stores a canonical SHA-256 fingerprint of the reviewed request, bound
to the firm, actor, request identity, opportunity and expected revision. Retry
reconciliation compares that immutable fingerprint rather than mutable proposal
fields, so later quotation edits cannot make changed request terms look like an
exact retry. PostgreSQL guards reject fingerprint mutation or deletion, and the
rollback refuses to remove the migration while fingerprints remain. The
Angular page continues to persist the reviewed intent before dispatch and
requires fresh review before retrying after an uncertain response.

The serial Release solution suite passed API 182/182, Domain 647/647 and E2E
206/206: **1,035 passed, zero failed and zero skipped**. The Angular production
build, 452/452 Angular unit tests, Release solution build (zero warnings or
errors), focused PostgreSQL fingerprint test, focused lost-response browser
journey and EF model-drift check also passed. Exact evidence is in
`status.json` under `angularCommercialProposalCreateRecoveryFullRegression`.
The additive migration was exercised in isolated test databases only; the
shared Development and production databases were not migrated.

Overall Angular migration acceptance remains PARTIAL. Remaining local
creation/planning/source-action parity, production-like canary, real
screen-reader and wider-locale acceptance, and live Microsoft gates remain
open. No production deployment or live Microsoft mutation was performed.

## Angular commercial create-recovery full regression

At source commit `9820d128e54925a4a2bff99a5b44c5fc7d4edc82`, the serial Release
solution test run passed API 182/182, Domain 646/646 and E2E 205/205: **1,033
passed, zero failed and zero skipped**. It includes the lead- and
opportunity-create lost-response recovery journeys. EF reported no pending
model changes. The Angular production build, 452 Angular unit tests and Release
solution build had passed on the same code state before this documentation-only
checkpoint. The commit was the verified `master` remote head when the run began.
Exact metrics are recorded in `status.json` under
`angularCommercialCreateRecoveryFullRegression`.

This closes the full-suite verification gap for those two create-recovery
slices. The Angular migration remains PARTIAL: other creation/planning/source
actions, broader screen-reader and locale acceptance, a production canary, and
live Microsoft gates remain open. No production deployment or live Microsoft
mutation was performed.

## Angular commercial opportunity creation recovery

At code commit `cdc8c8f`, opportunity creation saves its reviewed terms and
request identity to the current user’s browser draft before dispatch. On reload,
the page queries the lead’s persisted opportunities. If the exact request ID is
present, it clears the pending intent and reports the persisted result without
posting again. If it is absent, the unchanged intent remains available for an
explicit retry after fresh review, using the same request ID. The API already
binds that request ID to the exact opportunity fields and current actor. No
database migration was needed.

The Playwright journey covered both outcomes: a committed write with a dropped
response was reconciled from the list, and an uncommitted write was retried with
the same ID after the user re-reviewed its terms. Two PostgreSQL Domain
idempotency cases, all 452 Angular unit tests, Angular production build, Release
solution build and EF model check passed. Exact evidence is in `status.json`
under `angularCommercialOpportunityCreateRecovery`. The complete solution suite
was not run on this source commit; the prior 1,029/1,029 result remains tied to
`32ad074`. Recovery for other commercial commands and overall migration
acceptance remain open.

## Angular commercial lead creation recovery

At code commit `0e1f874`, Angular lead creation saves the complete request and a
client-generated request ID before dispatch. If the response is lost, the form
locks its fields and offers an explicit recovery action after reload. The API
reuses the request ID as the lead identity and returns the existing lead only
when firm and submitted fields match; changed or cross-firm reuse fails with a
generic idempotency conflict. Legacy callers without a request ID retain their
existing behavior. No database migration was needed.

The lost-response Playwright journey committed a synthetic lead, dropped its
first response, reloaded and retried the same request ID; it observed exactly
one lead row. The PostgreSQL idempotency test, API contract test, all 452
Angular unit tests, production Angular build, Release solution build and EF
model check passed. Exact evidence is recorded in `status.json` under
`angularCommercialLeadCreateRecovery`. The complete solution suite was not run
on this source commit; the previous 1,029/1,029 result remains attributed to
`32ad074`. Broader commercial command recovery, overall migration acceptance,
production canary, real assistive-technology checks and live Microsoft gates
remain open.

## Owner-approved Angular route cutover and Blazor runtime retirement

On 2026-10-04 the owner approved completing the local cutover. The standalone API
is the only supported runtime and serves Angular on canonical `/app`, `/portal`
and `/setup` routes. `AuditSphereOps.Web` remains as a Test-only regression
fixture; `ApiHost` rejects `legacyPresentation=true` in every other environment.
Operator setup guidance now uses the API, and the accounting browser journey
script builds Angular, applies migrations through API and launches API.

At code commit `32ad074`, the Angular production build and Release solution build
passed with zero warnings/errors. Retirement policy tests passed 4/4, production
security configuration tests passed 5/5, and the Test-host route-render smoke
passed 1/1. The complete PostgreSQL-backed solution suite then passed in one run:
API 182/182, Domain 644/644 and E2E 203/203 (**1,029 passed, zero failed, zero
skipped**). EF reported no pending model changes. The built-in browser rendered
the authenticated Accounting workspace at `http://localhost:5099/app/accounting`
with canonical `/app` links and its authorized client table. No business
mutation was submitted. Two earlier full-suite attempts exposed security tests
that still booted the retired Web host in Production; those tests now use the
standalone API factory, and the final complete run passed.

The production Angular build was then published with the API. Its 105 browser
files contain no Blazor/MudBlazor runtime names or references, and the API
dependency manifest contains no Web or MudBlazor dependency. The focused route
contract suite passed 3/3: every Angular route has an API SPA owner, every legacy
Razor route has an explicit native owner except server-owned root/access-denied
routes, and authentication routes stay outside the SPA.

US-047 local runtime retirement is complete; overall migration acceptance stays
PARTIAL. Production canary, real assistive-technology and wider-locale
acceptance, remaining source-action parity and live Microsoft gates remain
outside this local verification.

## Current Release regression and resource-planning browser journey

At `152790fb1709d7c886b49be539260d6a84201afd`, the full Release solution test
invocation passed API 178/178 and Domain 644/644. E2E reported 202 passed and
one failure: the Angular resource-planning journey on the `/ui` preview route
could not find its review confirmation after profile submission. The journey
now submits an actual profile change and awaits both HTTP 200 from the preview
endpoint and the named review panel before checking that assent is disabled.
The focused journey passed both `/ui` and `/app` variants (2/2), and the full
E2E project passed 203/203 at `7fe9603721ee9352f6fd40bad1f24c5d5a1ecc28`.
The Release solution build after this test change passed with zero warnings and
errors. API and Domain production code was unchanged by the E2E-only correction;
however, there is not yet a single clean all-project test invocation at the
latest commit. The latest clean all-project run remains 1022/1022 at `404b4b8`.
Full migration acceptance remains PARTIAL.

## Angular invoice receipts, allocations and credit notes

The Angular invoice workspace shows billing-account receipts and invoice credit
notes in on-demand pages of 100 rows, records reviewed receipts, allocates
receipts to posted invoices, and issues reviewed credit notes for FinanceManagers.
Separate timestamp-and-ID keyset cursors preserve rows sharing the same timestamp.
The API delegates to the existing `BillingService`; the Application query scopes
the invoice, billing account, receipt history and credit history. Exact decimal
strings, complete invoice balance totals, explicit review, CSRF and current
role/scope checks remain in force. After a lost response, the form stays disabled
until persisted state is refreshed and the unresolved draft is cleared. Evidence
for the Release build, Angular suite, PostgreSQL API journey, Playwright browser
journeys and EF model check is in `status.json` under
`angularBillingReceiptCreditWorkspace`.

The built-in Development browser was reloaded on the authenticated Angular
workspace. Its synthetic search returned no invoice to inspect, so no Development
billing rows were created; the invoice and paging flow was exercised in owned
PostgreSQL Playwright journeys. Wider US-019 parity, production canary,
assistive-technology acceptance and live Microsoft gates remain open.

## Current-source Angular regression and built-in browser check

At source `404b4b8`, the Angular production build and Release solution build
passed with zero warnings and errors. The full PostgreSQL-backed solution run
passed API 176/176, Domain 644/644 and E2E 202/202: 1022 passed, zero failed
and zero skipped. EF reports no pending model changes. Exact evidence is in
`status.json` under `angularCurrentSourceFullRegression`.

The built-in browser first showed the Mappings route's safe retry state while
the Development database had pending migrations. A private custom-format
database backup was verified before the local migration update. The database
is now at migration `20261003153640_NativeTenantSetupMetadataReceipts`: all 142
repository migrations applied, none pending. After restarting the Release API,
both liveness and readiness returned 200. The browser reload showed the
authenticated Angular Mappings page and its authorized empty state (0 mapping
versions, 0 chart-bound); no business mutation was submitted. The backup stays
outside the repository. Exact observations are in `status.json` under
`angularCurrentSourceFullRegression`.

This verifies the earlier local Development route and empty-state journey.
Production canary, live Microsoft checks, screen-reader and wider-locale
acceptance remain open; overall migration acceptance is PARTIAL.

The owner-approved route-ownership change is committed as `f4afcc5` and
`b293ca1`: canonical `/app` routing is now the API configuration default, while
an explicit `false` retains `/ui` preview routing. The Angular production build,
Release solution build, standalone API publish and EF model check passed;
`CanonicalAngularHostTests` passed 4/4. The publish contains the canonical
setting and fingerprinted Angular assets. The built-in browser verified the
authenticated `/app/accounting/mappings` route and the `/ui` preview alias, with
navigation following each prefix. No business mutation or production deployment
was performed. The Web project is now Test-only and refuses non-Test startup.
Evidence is recorded under `angularCanonicalRouteDefaultFlip` and
`angularDevelopmentCanonicalRouteOwnership` in `status.json`.

The shipped API now defaults to canonical Angular ownership. Production-like
canary, deployment retention of the previous fingerprinted build, live
Microsoft acceptance, assistive-technology acceptance and Blazor retirement
remain open, so overall migration acceptance remains PARTIAL. Local runtime
retirement was subsequently approved and is recorded above.

## Reviewed existing service-period engagement inspection

The authorized client-scoped preview now returns the matching existing
engagement ID when the service route and period already exist. Angular shows a
separate existing-engagement state with an inspection link and no duplicate
creation confirmation. The Application write path repeats the duplicate check
inside the client safety transaction and rejects the write.

Angular component coverage, PostgreSQL-backed Domain and API tests, both
Playwright route modes, the Angular production build, Release solution build and
EF model check passed. Exact counts and tested source commit are recorded in
`status.json` under `angularExistingEngagementInspection`. The wider solution
suite was not run for this slice. US-015 and migration acceptance remain
PARTIAL; no production cutover or Blazor retirement is claimed.

## Current-head regression attempt (incomplete)

On the current code source (`b012022`), the Angular production build and Release
solution build passed with zero warnings or errors. API and Domain tests passed.
The serial E2E stage continued for
more than 37 minutes without a terminal summary and was stopped; no E2E count
or aggregate full-suite result is claimed. EF reported no pending model changes.
The earlier 1021/1021 clean frozen pass remains tied to its recorded checkpoint
and is not attributed to this later accounting source. Full current-source
regression and overall migration acceptance remain open. Exact evidence is in
`status.json` under `angularCurrentHeadRegressionAttempt`.

## Angular accounting alias and dimension review safeguards

Accounting chart, account, alias and dimension forms now clear review assent
when a reviewed value changes. Alias and dimension reads show safe loading,
failure and retry states; their mutation controls stay disabled until the
current client/revision read succeeds. Responses are bounded and validated,
with an explicit truncation notice where the read is larger than the display
limit. A client-scoped Playwright journey verified safe 503 recovery, reviewed
creation, persistence and display after reload. Angular tests, the production
UI build, Release solution build, EF drift check and staged secret scan passed.
The built-in browser loaded the authenticated Angular route and verified chart
and dimension assent invalidation on a synthetic client without submitting a
write; no console errors appeared.
The current-source full solution suite has not been rerun; the last clean
whole-suite result applies to the earlier search-fix source state. The overall
migration and US-020 remain partial. Exact evidence is in `status.json` under
`angularAccountingAliasDimensionReview`.

## Angular global-search submit deduplication

Typing and submitting the same term could dispatch duplicate search reads when
Enter arrived near the typeahead debounce. The UI now cancels the pending timer
synchronously and guards repeated requests for the active term; a failed or
unsupported response clears the guard so the user can retry. The regression
journey asserts exactly one search request for fill plus Enter and covers both
canonical and `/ui` routing.

The Release solution build passed with zero warnings or errors, all 451 Angular
tests across 87 files passed, the production UI build passed, and both focused
Playwright route journeys passed. Gitleaks reported no staged leaks. The full
solution suite and EF drift check were not rerun for this UI-only slice; the
previous integrated full-suite attempt remains interrupted, and the overall
Angular migration, cutover and Blazor retirement remain open. Exact evidence is
in `status.json` under `angularGlobalSearchSubmissionDeduplication`.

## Integrated regression attempt on the current master

The Release solution build passed with zero warnings or errors after the
assessment-timeline and resource-grid changes. A fresh full solution test then
remained in the API test stage for more than twelve minutes without a summary;
the API host entered an idle wait and was interrupted. No API, Domain, E2E or
aggregate test counts are inferred from that attempt. The prior
`verifiedCommit` remains the last successful full-suite checkpoint. Details are
recorded under `integratedFullRegressionAttempt575c338` in `status.json`.

## Angular resource-grid range integrity

The grid now derives week headers from the server-returned normalized range, so
an empty staff list retains its selected week columns. The empty row spans the
full table width. Angular validates the 1–12 week bound and verifies that every
staff row and allocation aligns with the returned date range; malformed grids
fail closed.

All 451 Angular tests across 87 files, the production UI build, and both
canonical and `/ui` resource-planning browser journeys passed. The focused unit
case covers three weeks with no staff and checks the six-column empty row. The
full .NET suite and EF drift check were not rerun; wider planning acceptance and
Blazor retirement remain open. Evidence is in `status.json` under
`angularResourceGridRangeIntegrity`.

## Angular assessment specialist timeline

The assessment workspace now reads the latest 200 specialist request/result
events from immutable, actor-owned command receipts, scoped to the authorized
firm and client. It validates receipt identity against the retained specialist
review record and displays action, area, specialist, status, actor, time,
evidence reference and conditions. Current reviews also show their request and
clearance timestamps. No migration was needed.

The Release solution build, all 449 Angular unit tests, production Angular build,
focused PostgreSQL API projection test and both canonical and `/ui` Playwright
journeys passed. The new browser journey displayed a synthetic requested review
and HOLD result. The full .NET suite and EF drift check were not run for this
slice; the broader migration and retirement gates remain open. Exact evidence is
in `status.json` under `angularAssessmentSpecialistTimeline`.

## Angular SharePoint administration tab-draft recovery

Selected SharePoint resource and versioned folder-template forms now support
explicit bounded tab drafts. Recovery is tied to the current setup/resource or
template-catalog basis, restores editable values only, and always clears review
assent. The tenant connection route composes both setup and SharePoint guards;
refresh and unrelated writes ask before they can discard dirty fields. Same-tab
browser recovery was verified and created no template record.

The Angular suite (448 tests across 87 files), production build, Release solution
build, and focused SharePoint Playwright journey passed. The fresh whole-solution
test was stopped after more than twelve minutes without a test summary while the
API test host remained CPU-active in JIT compilation; it is not a pass. Exact
evidence and remaining migration boundaries are in `status.json`. This does not
close full migration, production cutover or Blazor retirement acceptance.

## Angular embedded commercial form draft recovery

Proposal, quotation, commercial-document and fee-agreement fields now have
explicit bounded tab drafts tied to current identity, session and authorized
workspace state. Recovery excludes assent, quotation previews, executable
requests and uploaded files. Proposal navigation checks all embedded editors
and refuses unresolved command outcomes. A successful proposal refresh retains
unsubmitted revision fields; a refused refresh clears protected content.

Angular tests and an isolated UI build passed as recorded in status.json.
Built-in browser recovery and three affected E2E journeys passed. Angular tests
and the isolated UI build passed as recorded in status.json. The captured whole
regression is terminal: API and Domain passed, while one E2E assertion failed
because its exact-text selector matched the same denial message twice. The
selector was corrected and its affected journey passed; the frozen artifact
manifest matched all 1,350 files. A whole current-source rerun remains open.

## Angular commercial settings draft recovery

Firm letterhead and approval-rule edits now have explicit bounded tab drafts,
identity/session and profile-version/rules-fingerprint binding, fresh authorized
recovery, unsaved-navigation protection and unload warnings. Drafts exclude
profile revision and review assent; recovered fields use the currently loaded
version. Editing fields invalidates their review confirmation. Saving one form
retains unsubmitted edits in the other form.

The Angular suite, isolated build and built-in synthetic browser journey passed;
results are in status.json. This closes pre-dispatch settings recovery only.
Quotation/document/fee-agreement drafts and durable commercial command recovery
remain open. The running regression retains its captured executable/UI assets;
these later settings changes are verified separately, and source guards may read
the current working tree.

## Angular firm safety and runtime parity

Administration now includes the persisted firm safety mode and exact deployment
epoch, allowlisted hosting environment, external-effects flag and simulation
adapter flag. A narrow Application query requires current firm-wide Administrator
authority before and after reading. The API supplies only selected typed flags;
no configuration dump or credential references reach the browser.

Missing safety state remains unknown rather than healthy. Recovery quarantine
explains the halt and links to operations; liveness and readiness links remain
separate from Microsoft/provider acceptance. Local builds, the Angular suite,
PostgreSQL/API tests and built-in browser verification passed as recorded in
status.json. The previous E2E recovery has now finished successfully, but its
artifact drift still prevents frozen-checkpoint attribution.

## Angular tenant setup acceptance extension

Automated API coverage now verifies exact reviewed metadata, CSRF and current
session authority, guessed identities, altered intents, replay, immutable event
storage and retained-evidence downgrade refusal. The new browser journey reloads
after publication and explicitly recovers the committed receipt without issuing
another mutation. Lost-response unit coverage preserves pending references and
prevents resend. Exact review receives keyboard focus.

The full Angular suite and affected browser/API journeys passed, with results in
status.json. Owned browser hosts now support isolated Release artifacts and an
explicit validated repository root, so concurrent runs need not replace each
other's executable files. Default host behavior is preserved. Full migration
regression, canary and retirement remain separate unfinished gates.

## Angular tenant setup editor

Safe tenant label and mail/records configuration flags now have a native Signal
Forms editor backed by Application preview, save and actor-owned receipt lookup.
Exact intent and current setup state are reviewed before publication. Current
firm-wide administrator authority, configured tenant binding, protected-state
checks, revision fencing and immutable evidence are enforced server-side.

Unsubmitted tab drafts retain only bounded local fields, never assent. Pending
submissions retain request references for explicit receipt reconciliation, never
automatic replay. Navigation protection and session-bound late-response checks
preserve unsaved edits and prevent stale responses from applying.

Focused builds, PostgreSQL tests, Angular tests, EF drift inspection and built-in
browser verification passed; evidence is recorded in status.json. The migration
was applied only through owned synthetic test schemas. The later acceptance extension above closes automated setup mutation journeys
and downgrade safeguards; wider migration acceptance remains open.

A later artifact check found drift in the original regression manifest. Its
running E2E recovery cannot establish the original frozen checkpoint. Earlier
API/Domain terminal results remain observed, but aggregate completion is not
claimed. The older recovery section below describes the initial restart only.

## Angular full regression recovery

The full API and Domain suites passed on the frozen migration checkpoint. User
interruption ended the original process before an E2E completion result was
recorded. After confirming the old process absent and the artifact manifest
unchanged, the unfinished E2E suite was restarted against the existing Release
binaries and UI assets. Its terminal result remains pending in status.json.

Concurrent commercial source edits and a new performance test are preserved and
are outside this compiled checkpoint. Aggregate regression completion, wider
migration acceptance and final Blazor retirement remain unverified. No shared
Development or production database migration was performed.

## Angular Microsoft tenant setup metadata

The tenant workspace now projects the saved friendly tenant label and mail/records
setup states through the current firm-wide administrator query. Angular displays
these in a separate saved-configuration section, explaining that they do not prove
consent, mail transport, document protection or release readiness. Immutable tenant
identity and independent capability verification remain unchanged. Unsupported
setup states fail decoding, absent metadata adds no invented state, and session
loss removes the protected metadata.

Local build, contract, PostgreSQL and browser evidence is recorded in status.json.
Built-in browser inspection used an owned synthetic database and observed saved
mail configuration alongside an independently disabled mail capability. No shared
Development database change or Microsoft operation was performed. Setup field
editing/recovery, wider administration parity, full regression and final migration
acceptance remain open.

## Angular reviewed resource planning and retained receipts

Profile, certification, availability and allocation commands now require an exact
server preview and explicit assent. Current authorization, enabled staff identity,
capacity, availability and staffing are checked under publication locks. Changed
review inputs refuse publication. Mutation and immutable request receipt commit
together; matching retries return the retained result without another mutation.
Receipt lookup waits for concurrent publication and remains actor/firm scoped.

Angular retains only a bounded request reference for reload recovery. Lost or
unverifiable responses fence further commands until lookup and acknowledgement;
no command is retried automatically. Unsubmitted edits remain in memory and other
forms survive acknowledgement. PostgreSQL guards reject receipt changes and
rollback over retained evidence.

Release and production UI builds, Angular tests, focused Domain/API tests and
canonical/preview browser journeys passed. Built-in browser verification exercised
preview, assent, receipt and acknowledgement without console warnings/errors.
Exact metrics and logs are in status.json. Only owned test databases were migrated;
shared Development and production were not migrated. No external provider action
was performed. Wider staffing/budget parity, full regression, cutover and Blazor
retirement remain open.

## Angular resource planning forms and keyboard parity

Resource planning now uses typed Signal Forms for staff profiles, certifications,
unavailability and weekly allocations. Bounded percentages/hours, real dates,
ordered absence ranges and currently offered identities are validated before
submission. Existing profiles populate the editor; persisted decimal percentages
remain editable. Accessible inline errors and a focusable error summary support
keyboard submission. The grid separately displays capacity, planned and approved
actual utilization, over-allocation and recorded engagement allocation details.

Independent unsubmitted forms survive another form's save. Leaving or refreshing
asks before discarding memory-only edits; session loss clears protected data and
fences late command results. Unknown additive outcomes block repeat submission
and explain the need for reconciliation rather than silently creating duplicates.
An allocation explicitly does not create authorization grants or change SharePoint
permissions. Existing Application authorization and calculations remain authoritative.

Production UI/Release builds, the Angular suite, focused PostgreSQL planning tests
and both canonical/preview browser journeys passed. Built-in browser verification
exercised synthetic profile save/reload/save with no console warnings/errors.
Observed metrics are in status.json. This does not complete planning acceptance:
the later reviewed-command slice supersedes its stale allocation and receipt
recovery gaps. Wider migration parity, full regression and cutover remain open.
No shared Development database migration or external provider operation was run.

## Angular assessment review and recovery forms

Native assessment actions now use typed Signal Forms with bounded validation,
accessible error summaries and exact server previews. Explicit assent applies only
to the current preview; cancelling or editing invalidates it. All five actions
compose the reviewed Application command and immutable receipt endpoints.

Only a dispatched request ID and hash are retained in tab storage. Answers,
evidence, professional notes and assent remain in memory. Lost responses and
reloads fence further actions until authorized receipt lookup and explicit
acknowledgement; no action is automatically retried. Unavailable storage prevents
dispatch. Navigation asks before discarding unsubmitted edits and blocks unresolved
requests. Historical and engagement-specific decisions remain read-only.

Production UI, Release build, Angular tests and focused browser checks passed.
The built-in browser verified a synthetic answer review, committed receipt and
refreshed persisted progress with no console warnings/errors. Focused backend and
model checks are recorded in status.json. Shared Development migration and live
Microsoft acceptance have not been performed. Broader assessment parity,
resource-grid acceptance and migration cutover/Blazor retirement remain open.

## Reviewed assessment commands and backend recovery

The Application assessment workspace now previews and executes answer revisions,
specialist review requests/results, Partner decisions and continuance through the
existing capability services. An exact review basis binds persisted evaluation,
question definitions, answers, effective clearances, target evidence and current
actor authority. Explicit assent is required; acceptance still requires the
existing professional readiness gates. Irrelevant or oversized action fields are
refused. No engagement is activated by these commands.

Execution holds firm, client and actor locks and publishes the local mutation and
append-only request receipt in one transaction. A matching committed receipt is
returned before checking changed evaluation state; changed intent cannot reuse
the request. Authorized actor-owned lookup waits for in-flight publication before
reporting absence. No timeout establishes failure and no unknown action is blindly
retried. Database guards validate current scoped authority and the exact result;
retained receipts prohibit migration rollback. Specialist request reuse selects
the same deterministic open review shown by the preview.

The new migration is exercised only in disposable PostgreSQL test schemas and
databases. Shared Development or production migration has not been performed.
Focused verification evidence is recorded in `status.json`; whole-suite and live
Microsoft acceptance remain separate. Existing endpoints stay available for
retained builds. Angular still needs typed forms, explicit preview/assent,
retained receipt references and navigation/recovery integration before this native
workflow is complete. Resource-grid parity, wider acceptance and Blazor retirement
remain open.

## Assessment command transaction ownership and current authority

Answer capture, specialist review requests/results, Partner decisions and
continuance now join a caller transaction when one exists. Standalone calls
retain their own commit/rollback boundary. A reviewed caller must roll back its
entire scope when a composed command fails; this enables the upcoming mutation
and immutable receipt to publish together.

Commands acquire firm, client and actor publication locks in that order, then
recheck persisted authority after waiting and again before publication. Owned
PostgreSQL tests cover rollback without answers, reviews, decisions, workspace
intent or a new continuance generation, plus revocation during each command's
firm-lock wait. Existing human decision, scope, generation and protected-state
rules remain enforced. The focused API and legacy/native browser checks remain
separate from whole-suite acceptance; observed results are in `status.json`.

Next: immutable reviewed assessment request receipts and authorized recovery,
then typed Signal Forms and navigation/draft parity. No receipt UI or persistent
request recovery is claimed by this transaction foundation.

## Native assessment detail and exact legacy selection

The Angular assessment now composes a named Application projection for client
identity, relationship status, selected professional decision, repository state
and persisted overall/section progress. Legacy decision-ID links retain their
exact selection. Historical and engagement-specific decisions remain read-only;
opening the current client evaluation does not substitute an old decision.
Pending status is not presented as a recorded professional conclusion.

Reads hold firm, client and actor publication locks, enforce current client
scope, and omit remote resource locations and provider diagnostics. Unknown,
foreign and revoked selections fail closed. Native decoding bounds generation,
revision, identity and progress; failed reads clear protected edits. Partner
confirmation is memory-only and tied to the exact evaluation and decision fields.
Changing reviewed fields clears assent; stale callbacks cannot change a new
route's busy state.

Focused local build, unit, PostgreSQL API and browser evidence is recorded in
`status.json`. Built-in verification used an owned disposable fixture. Remaining
assessment work includes typed Signal Forms, retained command receipts/recovery,
specialist timeline detail and broader navigation/accessibility acceptance. The
whole migration, resource grid and production cutover/retirement remain partial.

The earlier conversion whole regression was cancelled after concurrent source
and Release assembly changes invalidated its frozen manifest. Partial project
passes are retained separately; they do not replace the whole-suite checkpoint.

## Reviewed prospect-to-client conversion

The native proposal now links to an explicit Angular conversion review. The
workflow captures bounded legal identity fields, shows canonical-client reuse,
primary contact and pending portal intent, and requires fresh assent bound to the
exact server preview and current actor/proposal context. Restricted-profile text
is memory-only before submission and excluded from editable tab drafts.

Application execution composes the existing commercial conversion inside one
transaction with an append-only actor-owned receipt. Firm serialization precedes
proposal reads. Existing client metadata remains unchanged; stale contact/client
context and conflicting canonical identities fail closed. Unknown replies retain
a request reference and use authorized receipt lookup rather than retrying POST.
Conversion grants no portal access and does not activate an engagement or bypass
professional acceptance. The legacy rollback surface remains available.

Focused Angular, commercial/domain, API and canonical/preview browser checks
passed, as did Release/production builds and model-drift verification. The
built-in browser verified an owned synthetic conversion and acknowledgement;
its host/database were disposed. Exact evidence and recovered environment errors
are in `status.json`. Whole migration acceptance remains partial.

## Joined master regression checkpoint

The frozen whole-solution parent completed successfully. Domain, API and browser
projects all passed without failures or skips; the recorded source/assets/assembly
manifest matched after completion. Exact source revision and metrics are in
`status.json`. Later documentation and CI-only changes are separate from that
application checkpoint. This is local verification, not production or Microsoft
provider acceptance. Reviewed prospect-conversion drafts have been checked in
isolated temporary trees and are being integrated next; migration remains partial.

## Native reviewed staffing and receipt recovery

Angular staffing assignment and revocation now request an exact server preview,
then require separate assent bound to the full preview, current actor/session,
engagement and read context. The preview explains engagement role scope, current
certification, local grant/session effects and separate client-site reconciliation.
Unsupported replies fail closed. Competing edits/navigation are fenced during
review or unknown writes. The team projection hides stale content until receipt
verification and acknowledgement refresh current authorized state.

A bounded tab reference is saved before dispatch. Lost responses and reload use
actor-owned receipt lookup; no mutation is automatically retried. Protected state
clears on context loss and late callbacks cannot affect a different owner.
Focused Angular, API and canonical/preview browser journeys passed. Built-in
synthetic verification covered assignment and revocation receipts and refreshed
team state without console warnings/errors. Hosts and tabs were disposed. Detailed
counts and logs are in status.json; the full regression remains separately tracked.

Client creation follows accepted-proposal conversion in the existing architecture;
it must preserve commercial and professional acceptance boundaries. Reviewed
conversion/draft recovery, assessment parity, remaining module acceptance and
quality/cutover/retirement remain open. No live Microsoft effects or shared database
migration was performed by this UI slice.

## Reviewed staffing backend receipts

Application staffing preview now binds the exact action, target identity, level,
assignment, certification, local grant effects, client/engagement generations and
current actor/session. Assignment review states the owner-approved entire-client-
site Full Control consequence; Microsoft membership still reconciles separately.
Confirmed execution composes the existing staffing service inside one guarded
transaction and retains an immutable actor-owned receipt. Exact request replay
returns the original receipt; changed intent and stale review fail closed.
Receipt lookup uses a publication barrier before reporting absence.

The new persistence migration adds append-only evidence and a deferred assignment/
revocation linkage guard, with rollback refusal when retained evidence exists.
It is exercised only in disposable test schemas, not shared Development or
production. Observed build and focused PostgreSQL evidence is recorded in
status.json. Angular staffing still uses its existing command path; wiring native
preview/receipt recovery and its API/browser acceptance is the next migration work.
No complete migration or live Microsoft acceptance is claimed.

## Staffing transaction composition and publication fencing

Staffing assignment and revocation now join a caller-owned transaction, or own and
commit their transaction when called directly. A caller receiving refusal must
roll back its transaction. Firm/client safety locks serialize publication; locked
current actor authority and rank are checked again before mutation, and current
authority is checked before successful publication. Target identity locks protect
grant and session-epoch changes. Existing certification, exact scope, independent
grant preservation and selected-site membership reconciliation remain unchanged.

This is the transaction foundation for reviewed staffing receipts. Reviewed
staffing/revocation preview, request receipts, unknown-outcome UI recovery and wider
migration acceptance remain pending. Observed build/test evidence belongs in
status.json. No database migration or live Microsoft operation is introduced.

## Reviewed budget approval and retained recovery

Independent approval now previews exact draft lines, currency, version and client/
engagement generations. A separate confirmation binds the complete preview to the
current actor and session. Application execution composes the existing approval
policy and transaction, retains an immutable actor-owned receipt, and refuses
self-approval, stale review or changed intent. Unknown writes require explicit
receipt verification; reload never resends approval. Acknowledgement refreshes the
current approved projection rather than displaying an old draft as current.

Focused Angular, PostgreSQL, API and browser checks passed, together with Release
and production UI builds and clean EF model drift. Built-in synthetic browser
verification covered review, receipt and acknowledgement. Source and assemblies
remained frozen during checks; all consumer parents joined successfully. Detailed
counts, logs, source identity and retained earlier build failures are in status.json.
The new migration was exercised only on owned disposable databases: empty rollback/
reapply passed and retained evidence correctly prevented rollback.

Staffing/revocation request recovery, client creation, assessment parity, a new
whole regression and migration quality/cutover/retirement remain open. No shared
Development migration, live Microsoft or production acceptance is claimed.

## Planning action-specific authority

The planning projection exposes reviewed budget-preparation authority separately
from staffing authority, reusing the Application preparation policy. Angular
preparation fields, draft persistence, preview/confirmation and retained receipt
reconciliation use that permission and reject missing contract flags. Draft
approval remains a separate section with its existing independent backend policy;
Administrator-only staffing does not expose the preparation editor.

Release and production UI builds, Angular tests, affected PostgreSQL tests and
canonical/preview browser journeys passed. Built-in synthetic previews verified
Manager access and Administrator-only denial of preparation. EF reports no model
drift. Source, production assets and Release assemblies stayed frozen during
verification; preview hosts and tabs were disposed. The earlier editor-fixture
failure is retained in the ledger; explicit scoped Manager grants now represent
editor authority, and separate Administrator-only journeys preserve the denial.

Reviewed staffing/revocation and budget-approval request recovery remain open,
along with client creation, assessment parity and migration quality/cutover gates.
No full migration, live Microsoft or production acceptance is established.

## Staffing grant expiry enforcement

Staffing rank now uses current Application authorization, including exact scope,
grant expiry and session identity. An active lower role cannot revive an expired
higher role. Staffing replacement first retires expired target grants through the
existing expiry service, preserving append-only evidence and session invalidation
before creating a fresh engagement grant. Unexpired independently assigned grants
remain reusable.

The focused Release build and affected PostgreSQL tests passed. Earlier fixture
and active-grant uniqueness failures are retained in the execution ledger. This
changes no UI or schema and does not close reviewed staffing/revocation recovery,
budget approval recovery, action-specific UI authority, or migration cutover.

## Planning Signal Forms and exact-context assent

All planning controls now use Signal Forms. Staffing assent binds the current
identity, engagement, read lifetime, person and level. Draft-preparation assent
binds the exact preview, and approval assent binds the exact displayed draft.
Immediate command checks refuse stale assent before rendering catches up; public
field reset synchronizes native checkboxes. Staffing selection, preparation and
approval stay disabled during unknown writes, and revocation respects the shared
review fence. Existing Application authorization and commands remain the authority.

Select labels now use explicit association so their names exclude option text.
Observed production/Release builds, the Angular suite and affected canonical/preview
browser journeys passed. The built-in browser verified exact labels and native
checkbox clearing in an owned synthetic preview, now disposed. Initial test failures
and their correction are retained in `status.json`; assertions were preserved.

Backend reviewed staffing/revocation and budget-approval request recovery and
individual action authority projection remain open, followed by client creation,
assessment parity and migration quality/cutover/retirement. No schema or live
Microsoft changes were made; these focused results do not replace the earlier
whole-suite source boundary.

## Planning navigation and editable-field preservation

Engagement navigation now guards unsaved planning edits with keep, budget-only tab
save, or explicit discard. Staffing choices and approval assent are never treated
as saved budget fields. Failed storage keeps the editor open. Current identity,
engagement lifetime and authority fence late dialog results. Busy or unknown writes
must be reconciled before internal departure; tab departure warns without trapping
sign-out. Same-engagement hold paging preserves the editor without opening a dialog.

The production UI build, Release test-project build, Angular suite and affected
canonical/preview browser journeys completed successfully. Built-in browser checks
confirmed keep/save/return/restore in an owned synthetic preview, now disposed.
Detailed observed evidence and the prior whole-regression boundary are in
`status.json`. No schema or live Microsoft changes were made. Staffing/approval
request recovery, remaining planning forms and wider migration acceptance stay open.

## Native budget Signal Forms editor

Budget inputs now use Signal Forms with bounded field metadata and whole-minute
validation. Line changes replace the model immutably. Invalid touched fields show
an accessible validation summary. Review and unknown-outcome fences disable the
fields and tab-draft actions; explicit assent and immutable receipt recovery keep
their existing behavior. Approval remains a separate action.

The production UI build, Angular suite and affected budget/profile browser journeys
completed successfully. The built-in browser checked line changes, validation,
correction, exact rate review and disabled confirmation before assent in an owned
synthetic preview, which was disposed. See `status.json` for observed evidence.
No database schema or live Microsoft change was made. Staffing/approval request
parity, other planning forms, navigation guards and wider migration gates remain
open.

## Reviewed native budget preparation and retained request recovery

The planning UI now previews exact approved rates and calculated forecast values
before explicit draft-preparation assent. The Application workspace reuses the
existing budget service inside one guarded transaction. A new draft and its
append-only actor-owned request receipt commit together; a deferred database guard
checks the exact publication and current authority. A changed revision or rate
snapshot requires another review. Existing budget endpoints remain for older builds.

Before dispatch the UI saves only the request reference in tab storage. Unknown
responses fence additional changes; reload reads fresh current scope and reconciles
the retained receipt without resending. Acknowledgement clears recovery state.
Budget approval stays separate and requires a different authorized practitioner.
Projection refresh drops approval assent. Scope loss and destruction clear protected
review fields and late callbacks cannot restore a prior route visit.

Focused PostgreSQL, API, Angular and browser evidence is in `status.json`, including
the corrected initial SQL-schema mismatch. The built-in browser checked the receipt
flow with disposable synthetic records, followed by final acknowledgement-copy
regression checks. The additive migration was exercised only in owned test/preview
databases; no shared deployment or Microsoft effect occurred. The whole-suite run
on the earlier creation source has joined successfully and its frozen fingerprints
match. It establishes that earlier checkpoint only; this newer slice remains
separately verified with focused evidence.

Next: staffing/approval request parity, remaining planning Signal Forms, client
creation, assessment parity and migration quality/cutover/retirement acceptance.
The overall migration remains partial.

## Native planning editable budget drafts

Budget preparation reuses the tab draft service for explicit save, restore and
discard. Draft identity follows the exact engagement, firm, actor and session;
restoration requires the current authorized budget version and draft state.
Only bounded editable fields are stored: no approval assent, calculated cost,
request execution or professional acceptance is restored. Unknown command outcomes
fence these controls. Refused access clears protected fields.

Angular tests, production build and the built-in browser verified this local slice;
observed evidence is in `status.json`. Owned synthetic preview hosts and database
were disposed. Reviewed Signal Forms and server request receipt recovery remain
open, together with client creation, assessment parity and migration cutover gates.

## Planning protected editor clearing

Native planning now removes protected editable fields and review assent immediately
when a command reports unavailable access, and revalidates the scoped projection.
Malformed refreshed projections also clear protected editor content. Focused
component checks cover both cases; observed test and build results are in
`status.json`. Reviewed planning forms, revision-bound drafts and retained request
recovery remain open. The separate frozen creation regression has joined successfully with matching
fingerprints; its source boundary remains recorded in `status.json`.

## Reviewed native blocked-engagement creation

The client profile now links to a focused Signal Forms creation route. Current
client-wide Partner/Manager authority is required; engagement-only scope and
Administrator authority alone do not suffice. The server preview binds the exact
client revision, service, profile and period, and confirmation requires fresh
explicit review. Existing service-period shells require inspection. New shells
remain Draft with professional work blocked; activation remains independent.

Creation and immutable actor-owned request evidence commit together under the
client safety lock. Current authority is checked after locking and before commit.
A deferred database guard binds the evidence to the exact blocked shell, actor and
client revision. Same-intent retries retain the original receipt. Lookup waits for
an in-flight transaction before reporting absence. The additive migration permits
empty rollback/reapply but refuses rollback while creation evidence exists. Only
owned disposable databases were migrated.

Editable drafts are explicit, bounded, tab-only and revision-bound; they exclude
review assent. Unknown responses fence resubmission and navigation until receipt
reconciliation. An absent receipt permits fresh review of the identical request;
changed intent cannot reuse it. Denied commands and failed receipt reads remove
protected context and require a new authorized read. Route changes, session loss
and destruction fence late callbacks. The older API creation endpoint and Blazor
presentation remain compatible for rollback.

Focused Release, Angular, PostgreSQL, API, browser and model checks passed. The
built-in browser verified exact review, keyboard confirmation, immutable receipt,
mobile bounds and sign-out clearing using disposable synthetic data. Initial
compile and encoded-route corrections, final joined build fingerprints and exact
counts live in `status.json`. This does not complete wider migration acceptance.

## Reviewed native Partner activation and response recovery

The Angular engagement page now links to a focused Partner review route. Its
Application context displays exact service/profile/period metadata, engagement and
client revisions, current same-service acceptance and active holds. Administrator
authority alone cannot activate. A fresh explicit review is required before a
current unconditional acceptance and zero holds can release a draft engagement.

The existing activation command now composes an owned transaction without cached
safety/engagement state. Activation, the guarded revision change, existing portal
intent handling and immutable native request evidence commit together. Current
Partner scope, session epoch, acceptance and holds are rechecked. A retry returns
the original actor-owned receipt, and a late revocation rolls back the activation
and engagement together. The additive migration preserves legacy rows, guards
native publication at commit and refuses rollback while reviewed evidence exists.
It has been exercised only in owned disposable databases.

The native Signal Forms page stores no review assent. Tab recovery retains only
request identity; unknown responses fence new submission and navigation. Receipt
lookup waits behind any guarded activation transaction. An absent result permits
fresh inspection and a new explicit review of that same fixed action. Scope/session
loss, destruction and a return to the same route fence late callbacks. The retained
legacy activation endpoint remains available for older UI builds.

The built-in browser checked exact review, keyboard confirmation, the receipt,
responsive layout and sign-out clearing. Synthetic local acceptance is not a
professional or Microsoft acceptance. Focused checks, intermediate compile/route
test corrections and immutable build fingerprints are recorded in `status.json`.
The earlier contact-commit whole run has terminated with its retained-evidence
rollback-target and concurrent browser diagnostics test failures; both focused
repairs preserve their original safety assertions. It does not replace the last
successful whole-suite `verifiedCommit`.

The published activation source has now passed a whole regression in its separate
frozen worktree. Every cohort and the parent joined successfully, and source,
Angular asset and Release assembly fingerprints match their pre-run manifest.
This updates the whole-suite `verifiedCommit` for activation source only. The newer
reviewed creation slice has its own focused evidence. Its fresh whole run is now
running in a separate frozen worktree; no result is inferred before parent completion
and fingerprint comparison.
Exact source identities, observed counts and runtime evidence live in `status.json`.

Next: reviewed planning forms/drafts/recovery, client creation,
remaining assessment/source-action parity, wider quality and production-like
cutover/rollback acceptance, then Blazor retirement. The full migration remains
partial. The existing operator migration workflow still applies; no Wiki update,
shared Development migration or external Microsoft effect was performed.

## Native engagement metadata, clearance and hold history

The scoped Application projection now retains service-profile and UTC creation
metadata, exact revision strings, complete active/released hold totals and stable
bounded history pages. Client-profile navigation requires separate CLIENT authority.
Active holds display blocked professional work independently from the stored flag;
the clearance link leads to retained gate evidence. Older native builds retain the
unpaged bounded endpoint contract.

Same-engagement history revalidation hides protected content while preserving the
team/budget editor and entered values. A refusal, mismatched response, route or
session change removes it. Route lifetime fences stop callbacks from an earlier
visit, including a return to the same identity, from changing current busy state or
review. Unsupported activation acknowledgments remain unknown and cannot authorize
another blind submission.

The built-in browser checked metadata, responsive layout, page-size changes,
preserved edits and sign-out clearing in an owned synthetic database. The focused
PostgreSQL/API/Angular and corrected browser results, original failed attempts and
artifact fingerprints are recorded only in `status.json`. An existing browser
scope assertion now uses a thread-safe diagnostics collection; its isolation
assertions remain intact. The separate whole run exposed that earlier collection
race and cannot establish whole-suite acceptance.

Reviewed creation, activation and planning forms, scoped drafts and retained
request recovery remain the next eligible work. Full migration quality, production
cutover and Blazor retirement remain open. No shared migration, live Microsoft
operation or Wiki publication was performed.

## Contact publication and engagement parity continuation

The reviewed contact step is pushed to remote master; its publication evidence is
recorded in `status.json`. A curated engagement source audit now identifies missing
master metadata, complete hold counts/history, client-profile navigation authority
and late activation callback lifetime fences. Planning edits must survive ordinary
same-engagement history paging and clear when current authority is lost. The
[engagement action audit](angular-engagement-parity.json) distinguishes existing
commands from remaining form/draft/outcome acceptance.

The earlier full regression terminated with the known navigation-selector failures;
its source/assets/assemblies matched after every consumer joined. Those selectors
are corrected and pass on the published client/contact steps. A fresh full run on
the contact commit now consumes a separate clean, rebuilt and frozen worktree.
Exact outcomes and test/build/hash paths remain only in `status.json`. The latest
full successful acceptance remains the top-level `verifiedCommit`; an in-flight
rerun does not replace it.

Native engagement parity is the active next slice. Full migration, production
cutover, wider quality gates and Blazor retirement remain open.

## Native reviewed client contact creation

A focused native contact route replaces the inline Angular contact form. The
Application preview binds the current client safety revision and exact editable
intent. Confirmation shows any primary contacts being replaced and requires fresh
assent. Contact creation continues to grant no portal or Microsoft access.

The existing contact command now composes an owned transaction, reads locked
safety state without cached tracking, increments the revision and rechecks current
staff authority before commit. The reviewed workflow saves the contact and an
actor-owned append-only receipt together. Composite constraints bind its firm,
client, contact and actor. Database guards reject receipt mutation, inconsistent
publication and rollback while evidence remains.

Explicit tab drafts contain bounded editable fields only. Pending recovery retains
an immutable request identity across changed revisions, never stale fields or
assent. Lost responses fence new submissions until receipt verification; an absent
receipt permits fresh review of the identical original intent with the same
request. A changed intent cannot reuse it. Returning client/profile routes refetch
current authority. Retained older UI builds keep their existing contact endpoint.

Release, Angular, PostgreSQL, API, browser and model checks passed. The built-in
browser displayed a synthetic creation receipt, responsive mobile state and
sign-out content removal. All owned slice consumers joined, their source/assets/
assemblies still matched, and temporary hosts/tabs were disposed. Exact outcomes,
intermediate corrections and artifact paths belong to `status.json`.

The earlier whole regression remains frozen on its own commit with known
navigation-selector failures; it has no terminal whole-suite acceptance yet.
Engagement reviewed forms/drafts, remaining source-action parity, production
cutover and Blazor retirement remain open. No shared database migration or external
Microsoft action was performed. The existing operator migration workflow remains
applicable; no Wiki publication was made.

## Native client profile metadata and complete scoped pages

The client projection now preserves source master metadata, exact safety generation,
UTC onboarding time, complete authorized summary counts and independent bounded
contact/engagement pages. Restricted profile content is excluded. Client-level
commercial access never widens an engagement assignment; every contributing scope
is rechecked before delivery. Native links and refresh keep the existing command
and assessment authorities.

Validated page parameters survive reload. Superseded reads, mismatched identities
or page responses, session changes and old command callbacks cannot refill earlier
content. The existing portal-intent notice explains acceptance and invitation gates
without granting access or claiming Microsoft redemption. Existing contact and
blocked-engagement commands remain; full reviewed forms, drafts and command-outcome
recovery are the next client work.

Release and Angular builds/unit checks passed. API checks and built-in desktop/mobile
paging, reload and sign-out checks passed. Final focused PostgreSQL/browser checks passed after correcting the expired-grant
fixture while preserving its database constraint. Unpaged API requests retain the
original bounded window for older UI builds; the new view requests explicit pages.
Final source, assets and assemblies match their frozen manifest. Observed results
and intermediate failures belong in `status.json`. The isolated earlier portfolio whole-suite run has exposed
a shell selector matching both navigation and the configuration notice. The focused
selector is corrected in this slice; the earlier frozen run remains unchanged and
cannot support a whole-suite pass claim. Full migration acceptance remains partial.

## Recent portfolio table controls and frozen regression result

Candidate and package panels now independently page the already authorized recent
window with the preserved row-size choices. Presentation changes fetch no extra
records, cannot enlarge scope and do not change CSV semantics. Search/session changes
reset the table pages; empty or shortened windows clamp safely. Angular checks and
builds passed. The affected browser cohort passed after synchronizing the existing
sign-out test with its asynchronous cookie response; content removal and API denial
assertions remain. Built-in desktop/mobile checks passed against frozen assets.

The earlier frozen canonical whole-solution regression has completed successfully.
Source, production UI assets and assemblies matched before the owned worktree was
reused. Its source identity, terminal results and retained evidence are recorded in
`status.json`. Responsive shell and portfolio slices have separate newer evidence;
they are excluded from the earlier whole-suite claim. Full client/engagement parity,
quality gates, production cutover and retirement acceptance remain open.

## Native scoped portfolio summary, records and export

The Application projection now owns summary counts, bounded recent candidates and
financial packages, and a formula-safe scoped CSV. Every section uses one authorized
scope snapshot and rechecks all contributing grants before delivery. Client and
engagement relationships remain explicit; mid-read revocation refuses the whole
projection even when another valid grant remains. Export validates antiforgery and
refuses an oversized client set instead of silently truncating it.

The native view preserves refresh, operational shortcuts, candidate/package links,
creation timestamps, client page-size choices and current-search states. Validated
URL parameters and memory-only exact-owner location restore filters and selected
client on return; no protected results are cached. Session, route and filter changes
fence late reads and downloads. The saved-configuration notice establishes no
Microsoft capability verification.

Release, Angular, focused PostgreSQL/API and model checks passed. The first browser
cohort failed only at an exact synthetic client-name selector; the corrected
cohort passed against frozen assets. Built-in desktop/mobile checks preserve scope,
return navigation, page size, restricted client routing and sign-out removal.
Observed counts, logs, intermediate bundle warnings and final repair are recorded
only in `status.json`. Recent-table pager, broader client/engagement source-action
and whole migration acceptance remain open. The isolated earlier canonical whole
run has completed API/Domain cohorts and is still waiting for browser completion.

## Responsive Angular navigation and source bookmark parity

The native desktop and compact shells now share their presentation links. A compact
Material modal provides keyboard focus containment, Escape/backdrop/close recovery,
successful-navigation destination focus and current-session ownership. Canceled guards
preserve the menu. Resize and late lazy-load fences prevent hidden focus or overlays
from returning after revocation, layout change or destruction. Portal links remain
restricted to the client workspace; API authorization remains unchanged.

The preserved Microsoft 365 bookmark redirects to the guarded tenant workspace.
A contract checks every legacy workspace route against explicit native host/router
ownership. Root/authentication denial remain server endpoints. Route coverage does
not establish complete source-action or retirement acceptance.

Build, Angular/API and final affected browser cohorts passed; built-in browser checks
cover keyboard entry/recovery, destination/resize focus and staff/client narrow views.
Earlier failing selector/resize runs and the actual trigger-focus repair are preserved
in `status.json`. Final source/assets/assemblies remained frozen and match. The broader
whole-solution run is isolated on the earlier canonical checkpoint and remains running.

Next eligible source gap: native portfolio summary, release/package panels and scoped
CSV export. Full action parity and wider accessibility/performance acceptance remain
open. No tenant permission, shared/production deployment or Wiki changes occurred.

## Controlled Angular route ownership and upgrade rehearsal

The standalone API can explicitly serve the native Angular catalogue at canonical
staff, portal and setup routes. Authentication, initial-admin bootstrap and consent
returns follow the selected presentation ownership. Assets keep their separate
`/ui` URLs; API/auth/health and unknown routes never receive an Angular fallback.
Canonical ownership is opt-in and rejects incompatible assets or the rollback host.

A retained approved previous build can serve fingerprinted assets for existing tabs;
old HTML, source maps and unhashed files are excluded. A built-in browser rehearsal
used distinct actual builds, an open tab across a same-origin API restart, retained
lazy chunks, fresh canonical reload and the restricted client portal. Exact scope
denials stayed enforced. Mobile content fit the viewport. The temporary hosts,
synthetic database and tabs were disposed, and frozen source/assets/assemblies match.

Release, Angular, targeted API/browser and model checks passed; evidence is recorded
in `status.json`. Canonical routes remain off by default. Full story/source-action
acceptance, broader quality gates, production cutover and Blazor retirement remain
open. Deployment guidance is updated locally; no Wiki publication occurred.

## Native source-bound valuation preparation

The exact reconciliation inspection now links native ECL and inventory preparation.
Explicit numeric inputs, assumptions and rationale use the existing supported
calculators and creation commands. Fresh preview and assent bind the current source,
complete proof, generation and reporting context. Missing/unsupported/stale inputs,
closed periods/books, frozen files and revoked authority fail closed.

A guarded local transaction retains the new draft analysis and immutable preparation
receipt together. Inputs, context and result snapshots are append-only and included
in new structured archives. Concurrent identical intents reconcile one result;
changed intents cannot reuse their identity. Database guards freeze native inputs
and refuse rollback that would discard retained evidence. Independent review stays
separate, and the source books remain unchanged.

Angular tab recovery excludes assent; dispatch requires a saved recovery reference.
Unknown acknowledgments require persisted receipt reconciliation. Route/session
changes clear protected content. Targeted and whole-API evidence, the test-only
browser query correction, built-in desktop/mobile checks and migration rollback
results are recorded in `status.json`. The final extra-test build changes only the
API test assembly. New reconciliation/specialist/analytical preparation, wider parity
and cutover remain open. No shared or production migration was applied.

## Frozen integrated migration regression checkpoint

The full Release solution regression completed successfully on the integrated native
analysis and operational workbench checkpoint. Source, Angular production assets and
Release assemblies remained frozen throughout the run and their fingerprints match.
Exact source identity, project results and the retained log are in `status.json`.

The newer reviewed evidence commands are published separately with their own verified
API, PostgreSQL, Angular and browser gates. Valuation preparation is a later active
slice; neither change is included in the earlier whole-solution acceptance claim.
Full source-action parity, migration quality gates, cutover and Blazor retirement remain
open. Local synthetic results establish no live tenant or production acceptance.

## Native reviewed accounting evidence commands

The Angular analysis inspection now links to a separate reviewed action workspace.
Staff select a bounded current same-engagement procedure result; independent
reviewers record decisions only when retained inputs and current procedure evidence
meet the existing accounting gates. Risk flags missing original provenance permit
human escalation only. Prior retained human decisions are preserved.

The Application boundary composes existing commands under parent/period locks
and a local transaction, retaining actor-owned request identity and immutable
before/after evidence. Repeated intent reconciles the same receipt; changed intent,
stale inputs, closed books/periods, file freeze and revoked authority fail closed.
Database guards preserve reviewed records, native links and action receipts.
New structured archives include this evidence without changing older artifacts.

Angular forms require current preview and explicit assent, retain bounded tab fields
without assent, and fence an unknown outcome until persisted receipt reconciliation.
Source/route/session changes clear protected state. Verification progress and logs
are in `status.json`; wider preparation/editing, migration acceptance, cutover and
Blazor retirement remain open. No shared or production migration was applied.

## Native accounting analysis integration checkpoint

The analysis inspection slice is integrated with the current native consolidation,
chart and operational workbench changes. The combined Release and Angular builds,
Angular unit suite, whole API regression, focused PostgreSQL/browser journeys and
EF model check passed. Exact source identity, counts and logs are in `status.json`.
Whole-solution acceptance remains at its earlier recorded checkpoint.

## Native Angular workbench parity across audit, finance, client and operations (US-028 through US-041)

Native Angular 22 workbenches, type-safe contract decoders, and comprehensive unit test coverage have been completed across all operational capabilities:
- **Advanced consolidation (US-028):** Complete native schedule preparation, source manifest generation, verified execution, and independent approval lifecycle; backed by API integration test (`AdvancedConsolidation_GetWorkspace_AndSubmitSchedule`) and contract unit tests.
- **Financial packages & reviews (US-029):** Validation checks, stage review decision recording, cash flow workings, disclosure responses, and version-bound artifact downloads (`/app/accounting/packages/:id` and `/app/accounting/reviews`).
- **Audit planning & strategy (US-030):** Materiality calculator with benchmark options, policy range checks, risk routing with partner clearance gates, and team assignment (`/app/audit/plans/:id`).
- **Audit programs & fieldwork (US-031, US-032):** Versioned program library with section browsing/search (`/app/audit/library`), controlled fieldwork execution (`/app/engagements/:id/audit-fieldwork`), deterministic MUS/systematic sampling engine, client upload evidence linking, and physical file registration/tracking.
- **Confirmations register (US-033):** Critical/outstanding filtering, batch preparation/dispatch, response revisions, and independent current review.
- **Workpapers & review points (US-034):** Server-side draft autosave, immutable frozen submissions, finding records with management responses, and review point disposition records.
- **Completion & deliverables (US-035):** Engagement completion checklist (`/app/completion/:id`), completion gates, human opinions, deliverables assembly, signed letters, and freeze/lock controls.
- **Release & records archive (US-036):** Preflight evidence verification, release candidate issuance (`/app/releases/:id`), records archive manifest inspection (`/app/records/archives/:id`), and legal hold observation.
- **Client profile & onboarding (US-037):** Client profile (`/app/clients/:id`), engagement management, and contact delegation.
- **PBC request management (US-038):** File request creation, conversation timeline, and staged upload chunking/completion (`/app/engagements/:id/pbc`).
- **Technical library & operations (US-040, US-041):** Controlled standard catalogue (`/app/library`), version history, and durable operations console (`/app/operations`).
- **Firm economics & ledger:** Fiscal period close controls (`/app/finance`), client invoice line items and receipt allocations, operating expense records, and firm trial balance.

Verification evidence and the limits of local acceptance are recorded in `status.json`.

## Native reconciliation publication checkpoint

The native source-bound reconciliation inspection is published directly to master. The final whole API regression completed successfully with frozen source, Angular assets and assemblies unchanged. Counts, source identity and logs are recorded in `status.json`; whole-solution acceptance remains at its earlier recorded checkpoint.

## Native group consolidation scope workspace (US-027)

The standalone API and native Angular consolidation interfaces now compose the complete group consolidation scope workspace (`/app/consolidation/scopes/:id`) with perimeter lifecycle management, component intake & approvals, balanced elimination journals, calculation runs, authoritative consolidated reports, member readiness, and intercompany exception breakdown.

Scoped authorization enforces `GROUP` scope access grants (`GroupAccessGrant`) and maker/checker separation across all lifecycle actions:
- Perimeter versions in `Draft` state can be approved by an authorized group reviewer different from the creator.
- Component intake validates eligible packages against sealed trial balance datasets and package review decisions; preparers cannot approve their own component submissions.
- Elimination journals enforce debits and credits balance both client-side and server-side. Reviewers can approve or return journals with a recorded reason; returned journals can be resubmitted by preparers.
- Consolidation calculation runs execute over approved components and approved journals; independent reviewers approve verified runs to establish the authoritative consolidated statement report without mutating component client books.

Verification evidence and the limits of local acceptance are recorded in `status.json`.

## Native source account aliases and client dimensions

The standalone API and native Angular accounting interfaces now compose complete source account alias editing and client dimension definitions with scoped authorization, transaction safety, and reviewed draft fences.

Draft chart revisions support source account aliases that hash into the chart revision digest, ensuring that publication review requires independent approval over both accounts and aliases. Chart version row locks prevent concurrent publication from racing draft alias writes. Client accounting workspaces support typed dimensions (`BRANCH`, `COST_CENTRE`, `DEPARTMENT`, `PROJECT`, `INTERCOMPANY_COUNTERPARTY`) with unique-code constraints per dimension type and client-scoped preparer authorization.

Verification evidence and the limits of local acceptance are recorded in `status.json`.

## Native journal management and source reflection reconciliation

The standalone API and native Angular journal interfaces now compose complete client management response review, offline staff evidence recording, and source reflection reconciliation with line-level bridge validation. Client users can submit management dispositions (Accept, Reject, Partially accept) with immutable evidence notes and reviewer metadata. Staff practitioners can view client responses or record offline management responses with audited staff attribution.

Source reflection reconciliation allows practitioners to record client ledger reflection state (Fully reflected, Partially reflected, Refused, Pending next period) with line bridge evidence, tied deterministically to the reviewed journal revision basis. Incomplete or mismatched line-level bridge evidence fails closed. PostgreSQL migrations preserve immutable action receipts and trigger invariants.

Verification evidence and the limits of local acceptance are recorded in `status.json`.

## Native accounting analysis evidence inspection

The native accounting queue links ECL, inventory, specialist, analytical and
journal-risk records to an exact scoped inspection. Retained approval and human
rationale stay separate from current input and independently reviewed procedure
verification. Typed monetary inputs and historical results retain exact precision;
missing analytical denominators and absent legacy risk provenance remain explicit.
The existing valuation profiles are reused to check retained values, without
supplying a replacement calculation or making a professional decision.

Related source, reconciliation, proposed adjustment, procedure and workpaper
identities are checked against the exact authorized parents. Complete audit links
are bounded and server-paged; their current generation and result revision are
verified separately from historical review. Repeated projections and final
scope/session checks fence mid-read changes, while Angular clears old protected
content and refuses inconsistent responses. The native queue shows missing
recorded generation as unavailable, rather than copying the current generation.

Targeted backend, Angular and built-in browser evidence, the corrected synthetic
fixtures, the browser assertion correction and final verification progress are
recorded in `status.json`. Reviewed evidence editing/approval and the wider
migration acceptance, cutover and rollback-host retirement remain open.

## Native reconciliation evidence inspection

The Angular evidence queue now links authorized reconciliations to a focused native view.
The Application projection retains exact reporting/source identity, preparation and
independent review metadata, historical amounts and the latest persisted complete-item
proof. Current source digest, generation, reporting context and item manifest are checked
separately; stale or unavailable inputs block reuse without rewriting retained approval.
Items are server-paged, and an oversized complete item set fails closed. Repeated current
reads and final authority checks prevent a mixed snapshot or stale session from publishing
protected evidence. Guessed and out-of-scope detail identities share the same denial.

Local PostgreSQL, API, Angular and browser evidence is recorded in `status.json`, including
built-in desktop/mobile inspection. The affected legacy responsive assertion now waits for
the existing drawer/main-content transition before measuring the original narrow widths;
its overflow and isolation requirements remain unchanged. This slice does not add
calculation, approval or linking commands. Specialist/valuation and related evidence editor
parity, wider migration QA, cutover and Blazor retirement remain open.

## Accounting evidence scope and session checks

The Application evidence queue excludes expired grants from its scope projection. Before
returning protected rows or counts, it rechecks the exact returned client/engagement scopes
and the current session epoch. A late loss of authority publishes no evidence. The HTTP
identity resolver's existing expiration reconciliation still invalidates the old cookie;
fresh sign-in exposes only the remaining authorized scope.

Owned sibling-client PostgreSQL checks and a native Angular API-host journey verify the
scope, mobile display and removal of protected content after epoch loss. Exact evidence,
publication and broader regression progress are recorded in `status.json`. No schema,
Microsoft permission or professional authority changes. Detailed evidence editor parity
and full migration/cutover acceptance remain open.

## Native statement contribution review

The statement route now composes a read-only, bounded Application review on the approved
mapping and exact sealed raw source. Separate statement views, complete backend totals,
paged account contributions, scoped procedure evidence and in-place exact source inspection
retain reporting/chart/taxonomy/generation context. URL navigation metadata restores filters
and rows after a fresh authoritative read. Changed or unavailable supporting context hides
old detail and totals; exports require the current exact review basis and server bounds.

The existing deterministic allocation and statement projection remain shared with legacy
consumers. No financial books, grants, approvals, schema or Microsoft permissions change.
Local evidence is recorded in `status.json`; wider story acceptance and final migration
cutover/Blazor retirement remain open.

## Native mapping batch creation

The standalone API and Angular mapping editor now compose a reviewed new-version workflow.
Source accounts are server-paged, destination choices come from the exact approved taxonomy,
and the focused split form retains exact decimal text. Explicit selected-account and paste
previews precede local staging; keyboard navigation and local undo do not mutate history.
The server preview checks the complete proposed mapping, and creation binds fresh assent
to the exact source/applicability, generation, lineage and session revision.

The serialized transaction retains the new version, allocations and an idempotent request
receipt together. PostgreSQL guards preserve native creation metadata and allocations;
independent approval remains separate. Late authority changes roll back all new records.
Tab drafts exclude assent and financial source rows. Interrupted creation fences new
requests and navigation until a scoped persisted receipt is verified, or the same retained
request is explicitly re-reviewed against its unchanged base. No automatic retry occurs.

Local build/test evidence and browser verification are recorded in `status.json`. The new
additive migration is restricted to owned verification databases so far; shared Development
and production were not migrated. Wider story acceptance, other accounting editor parity,
cutover/rollback and Blazor retirement remain pending.

## Independent native mapping approval

The standalone API now exposes a bounded review of the exact mapping, source, complete
allocations, chart/taxonomy applicability and current input generation. Native approval binds
fresh assent to a server revision under the existing serialized approval transaction. The
API approval alias also requires that review; a version-only request cannot bypass it.
Preparer/reviewer separation, professional scope, sealed raw source, complete allocations,
current applicability and mutable reporting context are rechecked. A late epoch change
rolls back approval and input generation together.

The dedicated Angular review page shows retained reviewer metadata separately from current
applicability. Explicit metadata checkpoints exclude assent and financial rows. Interrupted
responses fence commands and navigation until a persisted read and manual acknowledgment.
Refresh or session changes clear assent and protected content; historical approval alone
never implies current package eligibility.

Verification evidence is recorded in `status.json`. The newer slice above adds native batch
editing; broader migration acceptance remains open.

Browser verification now waits for refreshed application state rather than an unbounded
network-body completion. Retained PBC checks wait for the exact staged/uncertain upload row;
invoice scope checks wait for interactive replacement and fully loaded authorized content
before same-document navigation. Original failures and reruns remain separate ledger entries.

## Native general-ledger upload and receipt recovery

A dedicated Angular route now composes a reviewed GL CSV upload through the standalone API.
The explicit UTF-8 profile retains exact dates, amounts, journal/line identities and original
currency provenance. One file describes one client period, optional book, entity and functional
currency. Every journal and account/dimension code is validated by the existing Application
importer; malformed quoting, unsupported precision, unknown columns, mixed metadata and
ineligible context are refused. The bounded interactive profile is separate from the existing
larger chunk-import contract.

The reviewed file and current reporting/chart/safety/session context bind import assent.
Parent/period locks serialize import; exact concurrent retries resolve to the same retained
source. Final authority and context checks roll back all source rows after a late revocation.
Sealing, source selection, independent acceptance and completeness remain separate states.
The native page keeps only bounded file metadata in explicit tab checkpoints, requires the
original file and a persisted receipt read after an unknown outcome, and protects navigation.
No file bytes, financial rows or assent are restored from browser storage.

Local verification and synthetic built-in browser observations are recorded in `status.json`.
Reviewed mapping editing/drafts, larger GL upload profiles, wider parity and controlled
cutover/Blazor retirement remain open.

## Native general-ledger completeness preparation and review

The standalone API and Angular GL inspection now compose a native completeness workspace.
Scoped source selection is bounded to compatible sealed closing and prior-period opening TBs.
The reviewed plan binds the immutable source pair, reporting context, safety generation and
current authority. Preparation queues the existing durable operation. Retained worker results,
independent approval/rejection and source selection remain separate facts. A preparer cannot
review the same bridge even when holding a reviewer role. Review transactions serialize the
decision and recheck current authority before commit; closed periods/books, frozen files,
professional holds and stale reviews refuse mutations.

Account residuals are paginated and retain exact decimal strings. Missing opening accounts
are disclosed as unknown and block approval, including a missing opening account whose
known arithmetic residual is zero. The native page retains explicit tab drafts without review
assent, protects navigation and reconciles interrupted submissions through a persisted read
and manual acknowledgment. Prior operations are reviewed through Operations rather than
blindly duplicated. Native GL import, reviewed mapping editing and wider migration/cutover/
retirement acceptance remain open. Local checks and synthetic built-in-browser evidence are
recorded in `status.json`.

## Native independent general-ledger source acceptance

The Angular GL inspection links a native independent source-review page. The Application
workspace returns GL batch, raw/normalized identity, period/book, parser/profile, line count,
importer, current selection and retained decision without fabricating TB revision or validation
fields. The reviewed revision binds current source, period, safety generation, authority and
selected GL pointer; the existing acceptance transaction rechecks it under ordered locks.
Concurrent commands cannot publish duplicate decisions or advance generation twice. Importer
self-review, closed periods, frozen/blocked engagements and stale authority remain refused.

The shared Angular review form preserves separate TB/GL draft identities, fresh assent,
protected navigation and pending-outcome fences. A lost response requires an explicit persisted
read and acknowledgment before further writes. GL acceptance changes only the selected GL
pointer; existing TB selection remains independent. Sealing and acceptance never assert
account-exact GL completeness or a professional conclusion. Native GL import/completeness
commands, mapping editing and wider parity/retirement acceptance remain open. Local verification,
publication and synthetic built-in-browser evidence are recorded in `status.json`.

## Native general-ledger source inspection

The Angular engagement workflow now opens a server-paged catalogue of sealed GL sources,
scoped to the current client and exact engagement. Application queries own source context,
account/date/journal/counterparty filters, exact decimal totals and bounded complete journal
reads. They refuse non-GL/loading sources, invalid ranges and overflowing page requests,
and recheck current authority and immutable source metadata before returning.

The native page clears old rows on refresh, hides results after filter edits and rejects
responses for another source, reporting context or selected pointer. Journal detail shows
all lines of a bounded journal, with explicit refusal for oversized journals instead of a
truncated balance verdict. Current independent source selection is displayed separately
from sealing and arithmetic balance. Inspection publishes no acceptance or completeness
record. GL import/completeness commands, mapping editing and wider
migration/retirement acceptance remain open. Verification and publication facts are in
`status.json`; the browser fixture is synthetic and establishes no live provider acceptance.

## Native independent trial-balance source acceptance

The standalone API and Angular route now compose a reviewed source-acceptance workspace.
The stored source, current selected pointer, reporting-period state, input generation and
actor/session bind the reviewed revision. The Application command serializes acceptance with
the generation change and rechecks authority before commit. Importers cannot accept their own
sources, including users with Administrator or reviewer grants. Frozen/blocked engagements,
closed periods and unavailable source digests refuse acceptance.

The page shows an immutable decision receipt and the current pointer separately. Selection is
per engagement and source kind, across periods; the UI explicitly explains replacement and
staleness of dependent evidence. Evidence-reference drafts are tab scoped, require explicit
recovery and never restore review assent. Unknown submissions require an explicit persisted
read and acknowledgment before further writes or navigation. Inspection and worker validation
remain distinct from independent human acceptance and professional conclusions.

The frozen published source-acceptance regression has passed across all solution projects;
exact source, build and test evidence is retained in `status.json`.

The earlier reviewed-upload full regression completed with a rollback Blazor role-picker timeout;
the same journey passed an isolated rerun on unchanged artifacts. Both results are retained in
`status.json`; no full-suite pass is inferred. GL intake/completeness, reviewed mapping editing,
wider parity and controlled Blazor retirement remain open.

## Native trial-balance source inspection and export

The intake dataset selector now opens scoped source rows, persisted validation issues and
complete source CSV export. Rows and issues use separate server pages; account-prefix filters
stay on the server. Exact signed/debit/credit values remain decimal strings, and missing source
debit/credit totals remain absent. Context, source revision, period, source digests, sealing and
validation are shown separately. Inspection and export grant no source or mapping approval.

Direct exports have explicit row and byte limits, bind the observed dataset revision, escape
text formula prefixes and recheck current authority before release. Angular refuses file saving
after identity/context changes or mismatched response metadata. Large exports remain blocked
pending a supported durable export workflow. The complete GL import/completeness, reviewed
source acceptance and mapping editing journeys remain open. Observed verification, publication
and source boundaries belong to `status.json`.

## Native reviewed trial-balance upload and receipt recovery

The Angular intake now composes a reviewed Application upload workspace. Exact file identity,
reporting-period metadata and the current actor/session bind assent. Every period must pass
preview before a new import starts; each imported period retains its own transaction and durable
validation. Read-only reconciliation returns exact scoped source and operation identities,
including successful earlier periods, without retrying an import. Concurrent identical uploads
reuse existing source receipts; equivalent content with different canonical bytes remains a conflict.

The native Signal Form withdraws assent on refresh, fences unknown or malformed import outcomes,
and requires a successful persisted read plus explicit acknowledgment before further writes.
Bounded tab checkpoints contain file metadata only; recovery requires reselecting the bytes and
fresh review. Dataset-selector refresh preserves the upload receipts. Historical mapping proposals
are filtered by current scope before selecting a prior version, with bounded account windows and
final authority checks. The complete GL, mapping editor and source-export stories remain open.
Observed verification and publication belong to `status.json`.

## Native firm currency configuration

The FX rates and policies editor now composes existing firm-wide currency services through a
reviewed Application workspace and standalone API. Preparation keeps exact rate text, declared
purposes, provenance and effective dates. Approval requires an independent authorized reviewer;
invalid stored observations fail closed. The native form binds assent to exact intent and current
base, protects unsaved context changes, supports bounded tab recovery and fences unknown writes.
Observed verification and publication are in `status.json`. Complete migration acceptance and
legacy retirement remain open.

## Native intake currency review and prior-source isolation

The intake currency panel now uses native Signal Forms and a dedicated bounded read projection. It shows exact dataset/client/engagement/period identity, the upload closing-comparison method, source/presentation currencies, current/prior observation provenance, rounding and movement thresholds. Same-currency identity consumes no market rate. Thresholds grant no approval; classification-based translation and remeasurement remain separate accounting workflows.

The Application query selects prior datasets only after current scope filtering, excludes expired wider grants and rechecks authority plus current/prior source, period and rate state before returning results. An invalid latest observation blocks conversion rather than falling back. The panel removes old results on refresh, labels edited filters stale and refuses late/wrong-context responses after dataset or session changes. Exact verification/publication evidence is in `status.json`; complete intake command/form/draft parity and rate/method editor acceptance remain open.

---

## Native currency remeasurement and reviewed draft recovery

The Angular workpaper now uses Signal Forms, bounded exact decimal input and explicit tab draft recovery. Current approved inputs supply the recovery base; changed inputs retain edits but block submission until explicit rebase and fresh review. Client-context changes use a keep/save/discard dialog and clear prior evidence fields. Current independent reviewer authority is projected by the Application service. The API serializes reviewed preparation/approval under scope guards, preserves frozen-file gates, returns the same workpaper for duplicate preparation, and retains the existing stale-source disposition.

Scope reads exclude expired wider grants and expose bounded selectors. Saved calculation views identify rate/policy versions and provenance, source/functional currencies, direction, classification, prior carrying amount and FX/rounding movements. The original foreign-currency-only remeasurement contract is retained. Complete source-action/form/rate-method parity remains open; the newer intake section records the currency comparison. Exact verification and publication facts belong to `status.json`.

---

## Native SharePoint configuration and templates

The independent API and Angular tenant page now compose the existing selected-site verifier and configuration services. Resource edits bind a reviewed revision, enforce the configured tenant and invalidate previous selected-resource verification. Active configurations remain protected. Verification retains the exact site/library/root check and an unrelated-site denial control. Activation requires trusted consent, current boundary evidence and an approved client template. No Graph permissions were added.

Folder-template save/approval and workspace activation record append-only administration events. The previously inconsistent empty STE client manifest is now accepted for the direct client/year layout; engagement manifests remain non-empty and extra root properties are refused. Native forms clear review on metadata changes and fence uncertain writes until persisted-state review. PostgreSQL/provider-fake API tests, related provider/onboarding tests, Angular unit tests and a standalone API browser journey passed. Built-in browser inspection confirmed external blockers and disabled activation in an owned synthetic preview; it submitted no Microsoft operation. Exact evidence and its source boundary are in the execution ledger.

Client/engagement workspace provisioning and dedicated client-site rollout/member reconciliation still require native Angular parity. The full reference-guide migration, live Microsoft acceptance and final Blazor retirement remain open. Changes remain local until a commit/push is recorded.

## ASP.NET Core API backend and Angular UI

The owner-directed backend split is implemented: `AuditSphereOps.Api` owns the secure HTTP runtime and `AuditSphereOps.Ui` owns presentation. The rollback Web host composes the same API runtime and adds Razor/MudBlazor only; API has no dependency on legacy UI assemblies. Local publication includes the built Angular browser assets. API defaults to Angular, while rollback Web keeps its opt-in flag.

Native route families cover practice, accounting, audit, consolidation, completion, administration and the restricted portal. The latest routing correction removes legacy workbench links, makes search use the declared Angular catalogue, preserves duplicate search destinations, and keeps skip navigation on the current workspace. Client role dialogs offer only `ClientUser` with client/engagement scope; Application authorization remains authoritative.

Fresh installations also have native proof-backed administrator binding and deployment-owned tenant preparation. Setup requires the approved immutable Microsoft identity, explicit review and CSRF protection; the initial cookie has no protected actor until fresh sign-in binds the session epoch. Preparation is fenced to the reviewed draft revision and never substitutes for Microsoft consent or selected-site verification. Unknown outcomes require persisted-state review before another write.

Optional Microsoft administration now has native creation/invitation wizards, bounded managed-group controls and reviewed operation reconciliation. Original-response passwords are masked and transient; replay/recovery returns none. The group mutation checks the observed membership before dispatch, and Microsoft membership changes no local roles. A browser-discovered role-catalogue URL mismatch was corrected before the regression run. Existing permissions and provider boundaries are reused.

The shared Material font stack now covers dialogs as well as the shell without requiring an external font. The full solution regression run passed before this final visual correction; affected administration journeys, Angular checks and built-in browser inspection then verified the corrected typography. This sequence and its exact counts are recorded in the execution ledger.

Synthetic API-host browser journeys exercise local role review/revocation, exact tenant consent/directory binding, optional administration and client first-sign-in/delegation/conversation/upload staging. Built-in browser checks have loaded the persisted dashboard, roster, client request thread, creation review, guest-only scope choices and bounded group dialog. The whole guide remains partial: selected-workspace controls, recovery of a lost pre-dispatch form, complete source-action/form/draft parity, performance/accessibility acceptance and controlled retirement remain open. No live Microsoft or production acceptance is inferred. Exact executed checks are recorded in `status.json`.

## Previous Angular portfolio foundation


The owner-requested guide is being implemented through explicit, reversible route ownership. The opt-in Angular preview at `/ui/app` uses the existing trusted .NET session and an Application portfolio query. Existing Blazor, portal, authentication, consent, download and health routes retain their owners. Enablement requires a browser build; rollback disables the preview flag.

The [architecture decision](../architecture/auditsphere-angular-migration-current.md) and [source discovery inventory](angular-source-inventory.json) distinguish implemented behavior from unreviewed coverage. This is a partial foundation and read-only portfolio slice, not completion of the full guide. Other financial, audit, portal and administration stories remain open; final retirement requires accepted parity. Exact executed checks are recorded in `status.json`.

## Project completion hardening

Both protected shells now perform periodic trusted session verification and dispose their content on a disabled identity or stale epoch without a user action. Existing command authorization remains authoritative. Systematic random sampling is available through the existing fieldwork tool and immutable, order-bound calculation log; its migration preserves the other sampling methods. Current documentation navigation and health validation follow the owner-retired specification changes.

Executed evidence belongs under `projectCompletionHardening` in `status.json`. Live site provisioning, direct SharePoint immutable retention, firm template/methodology approval, independent review and target-environment readiness remain separate acceptance gates. The whole AS-PAR-002 direct-command/export/count audit is not closed by shell monitoring.

---

## STE commercial gates and final deliverable assembly

The current accounting-named requirements document now contains the owner's full STE functional specification. This slice closes local commercial and completion gaps: independent quotation generation; Partner-only engagement letters bound to current commercial acceptance and unconditional service-specific risk approval; approved FSLI selection for modified opinions; exact signature/seal versions; version-bound client-uploaded signed LOR scans and human Partner verification; and a five-part ZIP referencing a real reviewed financial-package release and posted balance invoice.

A gated letter creates the reviewed agreement and 50/50 milestones. The optional general-worker standing policy produces invoice drafts only, with current firm-wide FinanceManager and Administrator identities and session epochs captured in each durable intent. Independent finance review and posting remain required. Newly converted-client portal access requires both keys, activation and paid advance; pre-existing clients without conversion records retain the earlier onboarding contract. PBC and signed-LOR uploads close at final financial-package release, with the release guard shared by client write transactions. The client can download its authorized immutable bundle afterwards.

The PostgreSQL and browser fixtures exercise actual financial review/release and billing commands. Execution results are recorded only under `steDeliverableAssembly` in `status.json`. The [coverage matrix](auditsphere-ste-specification-coverage-current.md) distinguishes local implementation from live SharePoint/Entra acceptance, certificate-backed signatures, legal template approval and external immutable retention. New migrations have not been applied to Development or production. The final tested implementation is prepared for commit; owner documentation replacements/deletions remain separate and unstaged.

---

## Client SharePoint sites and STE reporting alignment

The owner approved Full Control across each entire client site for assigned staff, including engagement-only staff. A separate, disabled-by-default `client-sites` worker creates deterministically named client sites, verifies immutable Microsoft identities and exact site ownership, grants only the separate document app exact-site write, and reconciles the managed staff group. Local RoleGrant scope is unchanged. A persisted rollout boundary preserves older repositories and prevents newer clients from falling back to the shared site. Unknown creation outcomes retain a dispatch fence and require reconciliation.

Administration shows site readiness separately from staff membership health. Reporting validates all modified-opinion explanations, renders the signed independent report as PDF, automatically compiles the SRM after final workprogramme review, and supports reviewed five-chapter comprehensive proposals. Practice analytics separately labels contracted fee less lifetime standard charge-out value, failing closed on missing rates, ambiguous contracts or currency differences.

Run evidence and migration facts are in `status.json` under `clientSitesAndSteReporting`. The permission/deployment contract is [client-site architecture](../architecture/auditsphere-client-sharepoint-sites-current.md). The [specification coverage matrix](auditsphere-ste-specification-coverage-current.md) explicitly retains incomplete local workflows and external acceptance gates; the whole specification is not certified complete. No live site creation, membership change, application consent or production migration was performed. Direct SharePoint Full Control cannot prove immutable archival protection.

---

## STE specification alignment — package 1: commercial calculation and the fee cycle

A proposal is now priced from approved rate cards, hours, a complexity factor and a risk premium; each change is a
new immutable quotation revision. A configurable approval matrix decides who must approve a discount or
non-standard terms, and the proposal cannot enter internal review until its calculated quotation is approved and
equal to the proposal fee. One action produces a branded Quotation and Engagement Letter bound to the approved
revision. After acceptance and conversion the agreed fee splits into a 50% advance and a balance: the advance
follows normal invoice review and posting, payments are recorded manually (partial, duplicate and excess amounts
are handled), and a fully paid advance produces one official receipt and one queued email delivered by the isolated
mail worker. The balance invoice is issued once, only after the advance is paid and the linked engagement has an
issued release. The browser journey found and fixed a stale fee preview and a confirmation that disappeared on
reload. Exact verification is in `status.json`.

---

## STE specification alignment — package 7: firm operations and technical library

A technical library holds IFRS, ISA and firm guidance as versioned entries: a Manager prepares, a different Partner or
administrator publishes, older versions stay readable but cannot change, and search (including the staff search bar)
only returns published versions the reader's audience allows, naming the version matched. Practice analytics are
calculated from approved records with the formulas shown on the page: standard value at the captured charge-out rate,
cost at a separately recorded staff cost rate, billed from posted invoice lines traced to the engagement, collected
from receipt allocations, realization, margin, budget variance, department utilization and on-time task completion. The
firm's own expenses (rent, salaries, petty cash…) are recorded with their source document, reviewed by a second
finance user and posted through the firm ledger's journal controls, and the firm trial balance shows opening, movement
and closing balances with summaries that must reconcile. Exact verification is in `status.json`.

---

## STE specification alignment — packages 5 and 6: completion, deliverables and file freeze

Reviewers attach notes to quoted text in an exact result revision; the preparer replies, a reviewer resolves, and a
result cannot be approved while a note is open. When the preparer is staffed, only someone staffed above them may review.
A Summary Review Memorandum is generated from the reviewed facts and becomes stale when they change; the Engagement
Partner clears a current memorandum once every prior gate is satisfied, then chooses Clean, Qualified, Adverse or
Disclaimer (focus area and basis are required and written into the basis paragraph where the type needs them). The Audit
Findings Report, Management Letter and Independent Auditor's Report are versioned Word documents; while a critical
confirmation is open, the report is held and a versioned holding letter is produced instead. Client management
comments on shared drafts and acknowledges the representation letter at its exact hash in the portal, and only then can
the deciding Partner embed a registered PNG signature into the current report version (a picture, not a cryptographic
signature). Signing schedules the file freeze 60 days later; a worker freezes due files, refused writes are recorded,
and amendments need a second Partner. An activity trail lists uploads, edits, comments, sign-offs, freeze events and
refused writes. SharePoint read-only enforcement and direct SharePoint edit capture remain `BLOCKED_EXTERNAL`. The
browser journeys found and fixed an untranslatable portal query and a page reload interrupted by a download. Exact
verification is in `status.json`.

---

## STE specification alignment — package 4: fieldwork connections

A single Excel or CSV file with a PeriodCode column is split into one source per period; each is parsed and imported
as its own dataset only when every period in the file validates. For a new dataset, the client's last approved mapping
is proposed account by account, renamed and new accounts are flagged, and the result is a draft that still needs a
reviewer's approval. A currency review converts the trial balance at the approved closing rate for the period end,
shows the rate's source, date, direction and version, refuses a missing rate, and highlights movements against the
prior period above both a percentage and an amount. Profit or loss and financial position are generated from the
current approved mapping, and each line opens the procedures for its audit area. Sampling now runs the existing engine
over an approved schedule and logs parameters, seed and source digest so the selection is re-performed on reload.
Procedures link to the exact current received client upload, physical files (for example X-1 in Box 3) are indexed with
their movements and procedure links, ad hoc steps are inserted without changing the adopted programme, and audit
engagements cannot complete without a reviewed analytical review and a going-concern assessment covering twelve months
after the period end. Exact verification is in `status.json`.

---

## STE specification alignment — package 3: resource planning and materiality

An engagement team is now staffed at four levels (Engagement Partner, Audit Manager, Senior Auditor, Staff Associate),
each granting exactly one engagement-scoped role, with certification required for the top two levels, one partner per
engagement and no self- or above-rank staffing. A resource grid shows each person's weekly capacity net of recorded
leave, planned allocation and approved-actual time, and flags over-allocation. Budgets and task time carry a phase and
risk area and reconcile to the engagement total. Materiality is calculated from the current approved mapping over the
sealed trial balance (revenue, profit before tax, total assets, net assets, total expenses or one mapped line) into
Planning Materiality, Tolerable Error and the SAD threshold; a replaced mapping makes it stale, which blocks its
approval, withdraws it from difference evaluation and blocks completion. Risks receive green, amber or red bands from
recorded inputs that the database re-checks; amber and red need an owner of sufficient level and red needs an
Engagement Partner's review before completion. The browser journey found and fixed a circuit crash when staffing
someone who already held the engagement role, duplicate field identifiers, and a hidden file input overflowing
narrow screens. Exact verification is in `status.json`.

---

## STE specification alignment — package 2 (partial): acceptance paths, activation and workspace provisioning

Acceptance now follows the client's history: a first engagement uses the new-client question bank and a returning
client the continuance bank. Questions that require evidence refuse a bare answer, an adverse answer blocks the
Partner until a specialist review in that area is cleared with evidence after the answer, and a decision records its
path. An engagement is created blocked and only a Partner can activate it, from a current unconditional acceptance
for the client's generation. Activation queues a durable operation that creates the exact
`/Client Name/Engagement Year/01–05` tree from approved templates (or the previous layout for existing templates),
disambiguating duplicates instead of merging, and records the same binding and capability evidence as the
administrator action. Live SharePoint creation remains `BLOCKED_EXTERNAL`; delegation by the client's primary
contact, first-login enforcement, drag-and-drop portal upload and portal provisioning at conversion are not yet
built. Exact verification is in `status.json`.

The portal rows are now built as well. Lead conversion is the explicit portal trigger: it records the primary contact
and a portal intent that grants nothing until a Partner activates an engagement, after which an administrator can
invite the contact. Uploads stay closed until the identity's first-sign-in requirement is met (an observed Microsoft
sign-in after a forced temporary-password change for AuditSphere-created members; the portal security acknowledgement
for external or unobserved identities). The client's primary contact can delegate a request to a colleague of the
same client and engagement only, and revocation applies to the next command. Files are dropped or chosen, their
SHA-256 is calculated in the browser and progress is shown. The browser journey found and fixed a progress line lost
on reload.

---

## Staff navigation search (UX-029)

The staff top bar now has a search field backed by a new Application query. It covers clients, engagements, PBC
requests, leads, invoices and staff pages, and states that coverage in the results. Candidates are prefiltered by
the actor's current grants for each result kind, then each hit is re-authorized with the same decision its
destination route applies, so a hit never names, counts or snippets a record its link would refuse; client
identities and stale sessions get nothing. Input is debounced, superseded responses are dropped and results clear
on navigation or Escape; `/` focuses search only outside editable controls and dialogs; narrow screens use a
labelled toggle. The browser journey found and fixed a circuit crash from reusing a disposed cancellation source
during rapid typing. Document, evidence and email search remain out of scope. Exact verification is in
`status.json`.

---

## UI modernization — remaining detail routes, navigation sections and responsive sweep

Client, engagement, proposal, invoice, financial-package and currency-remeasurement routes now use the shared
breadcrumb, `PageHeader` eyebrow, record toolbar, source-backed counts and workspace panels; inline layout styles
moved to `enterprise-ui.css`. The staff navigation follows the reference grouping with only real destinations,
matches root links exactly and marks the owning section of detail routes. A new responsive sweep over 19
parameterless and 10 seeded detail routes at 320–1920px found and fixed one real defect: long native select options
on currency remeasurement widened the page by up to 773px. Commands, draft contracts and authorization are
unchanged. A custom Blazor reconnect panel now tells users that an action started before a dropped connection is
unconfirmed until its result shows; the confirm dialog explains a missing required reason instead of silently
ignoring the click; the unused remote Roboto font link was removed (system fonts only). Global search remains a
`BACKEND_GAP`; zoom, manual keyboard review and independent visual acceptance remain open. Exact verification is in `status.json`.

---

## UI modernization — group consolidation routes

The authorized `/app/consolidation` landing route now uses source-backed counts
of visible groups, scope versions and approved component pins, with shared
panels, contained registers and labeled mobile cells across perimeter, packs,
FX, advanced schedules, intercompany and eliminations. Its report section
links to an existing guarded advanced workflow. The authorized
`/app/consolidation/advanced/{ScopeId:guid}` route now uses a
scoped breadcrumb, record toolbar, status chip, counts of loaded schedules and
executions, a compact group facts panel, responsive source-bound JSON editors,
and white schedule/execution registers with contained tables. Empty registers
have informational states. The native draft boundary and JSON controls retain
their browser-only recovery behavior; the real submission, independent
approval, group grant and source-revalidation commands are unchanged. Counts
do not claim consolidation or release readiness. This is
`PRESENTATION_ONLY` plus `WIRE_EXISTING` for the direct link; method additions
and approval shortcuts from the visual prototype were not copied. Exact local
verification is recorded in `status.json`.

---

## UI modernization — staff PBC request inbox

The authorized engagement PBC route now has a scoped record breadcrumb,
direct links to the engagement and fieldwork, and counts for loaded requests,
upload intents and received uploads. Its request form uses a responsive
two-column layout on wider screens and one column on mobile. The recipient
selector stays inside the form, and the upload table scrolls within its
panel. Existing request, staged-transfer, timeline, download, native draft
autosave and scope checks remain unchanged. The counts are record counts,
not evidence suitability or completion decisions. This slice is
`PRESENTATION_ONLY`; it adds no provider or approval action. Exact local
verification is recorded in `status.json`.

---

## Project implementation task progress

The administrator-only project tracker now prints completed-card percentages
beside its existing source-backed counts and segmented bars for the overall
pack, Modules 20–26, the four audit phases, audit aggregate and shared
foundation. The denominator remains the published task-card count; status
filters affect only visible lists. Practice, time, finance, documents,
operations and administration still show an unmeasured bar because no
dedicated task-card mapping is approved for those areas. This percentage is
not working-software completion or production readiness. Exact verification
is recorded in `status.json`.

---

## UI modernization — audit population detail

The authorized `/app/audit/populations/{Id:guid}` route now uses the shared
record toolbar, semantic status chip, scoped counts, compact facts grid and
white panels. Source receipt, extraction parameters, exclusions, monetary
control total, selection rationale and linked evidence remain visible. The
selection table keeps its read-only values and has mobile labels; empty
selection and evidence states use informational alerts. Counts describe only
persisted authorized rows and do not assert sampling sufficiency or an audit
conclusion. The existing engagement authorization and in-place route-change
clearing are unchanged. This slice is `PRESENTATION_ONLY`; prototype import,
selection and review controls are not offered on this real detail route and
were not copied. Exact local verification is recorded in `status.json`.

---

## UI modernization — firm administration workbench

The authorized `/app/administration` route now groups its existing dashboard
cards in a compact responsive grid and presents the persisted setup steps with
clear state, evidence and required-action rows. The page header no longer
repeats the same scope note. The local exact-identity roster fields use a
two-column desktop layout and stack on mobile without changing binding,
grant or invitation commands. Wide user, grant, capability, permission and
history tables scroll inside their panels at desktop widths; their mobile
rows retain labels. Nested administration panels use the shared white surface
style. No Microsoft or local access operation was added or weakened.
The linked project-progress view now filters the published task-card lists by
completed, active or in review, pending or reopened, and blocked state while
its source-backed segmented bars and denominators remain unchanged. Areas
without a published task mapping stay explicitly unmeasured.

Synthetic mobile/desktop before and after captures were inspected. The
PostgreSQL-backed administrator journey checks six viewport widths, keyboard
focus, contained desktop table scrolling and the visible tab families at
mobile width while preserving the consent and directory flow. Exact final
verification and remaining dialog/state coverage are in `status.json`.

---

## UI modernization — Microsoft 365 tenant connection

The administrator tenant-connection page now uses scoped white panels and a
mobile-stacked summary. Its permission table has labels for mobile cells, and
directory search fields reflow while retaining their existing domain filter,
bounded read, and exact identity-binding action. The read-only capability
bar counts only enabled, fresh, verified permission checks; sign-in setup and
selected-site verification remain separate and the bar is not a tenant or
production-readiness claim. Existing consent, capability, directory and role
boundaries are unchanged. Synthetic before/after mobile captures and desktop
captures were inspected; the desktop captures have different drawer states,
so they do not prove a matched desktop visual comparison. The browser journey checks the verified count,
domain-filtered result, keyboard focus and six viewport widths. Exact final
verification and remaining administration work are in `status.json`.

---

## UI modernization — protected Microsoft 365 setup

The saved-draft route now displays an accessible seven-step local checklist
bar derived from its existing authorized progress query. An administrator's
draft moves from 1/7 to 2/7 when the approved site URL is saved in the
synthetic browser journey; the UI explicitly separates this count from live
Microsoft consent and selected-resource verification. The form keeps its
bootstrap proof, native select values, template review and activation gates.
Optional labels no longer repeat their qualifier, and versioned template
rows have mobile labels. The journey covers six viewport widths, keyboard
focus and the expanded advanced settings at 390px; before/after synthetic
captures were inspected. This slice is `PRESENTATION_ONLY` plus
`WIRE_EXISTING`; exact test and model-check results are in `status.json`.

---

## UI modernization — client assessment and partner decision

The two assessment detail aliases and the partner decision route now use shared
record headings, scoped status and responsive white panels. Questionnaire
progress uses the exact authorized answer count and template count; when no
templates exist, the page shows no completion percentage. Specialist clearances
and the existing decision are presented without changing their approval rules.
The partner form retains its immutable Application command, required fields and
scope checks. A previously squeezed mobile workspace was traced to the staff
shell's responsive drawer width reservation and corrected for the shared shell.
The existing PostgreSQL-backed browser journeys verify response progress,
clearance blockers, staff/partner scope and same-document route isolation at
six widths; matching synthetic before/after captures were inspected. This is
`PRESENTATION_ONLY` plus `WIRE_EXISTING`, with an unavailable-state consistency
fix. Exact verification results are recorded in `status.json`.

---

## UI modernization — release and archive evidence details

The real release-candidate and records-archive detail routes now use the shared
record heading, scoped status, white panels and responsive evidence layout.
Release shows the current candidate status and an explicit blocker when its
existing preflight evidence is incomplete; its authorized key field, exact
revision checks and issue command remain unchanged. Archive shows compact
counts from the authorized manifest and local holds, readable hash/entry rows
at mobile widths, and a scoped engagement link. An out-of-scope archive and an
unknown archive display the same unavailable state, while an in-place route
change still clears the previous archive. Requested records actions and local
holds continue to be evidence only; the UI does not claim provider protection
or add prototype-only handover, remote archive or signing controls. This slice
is `PRESENTATION_ONLY` plus `WIRE_EXISTING`, with a route-denial consistency
fix. Exact checks are recorded in `status.json`.

---

## UI modernization — durable operations

The administrator Operations route now shows the authorized firm operating
mode and a compact summary of queued, processing, attention, completed and
cancelled states derived only from the same latest-firm-operation projection
already displayed in its ledger. The count is explicitly bounded to those
visible rows; it is not a full backlog or worker-health measure. The page's
redacted evidence, recovery quarantine warning, cancellation disposition,
re-arm and cancellation commands, and grant-revocation clearing remain under
their existing Application service checks. The long duplicate introduction
was condensed into a record-context panel; the ledger still exposes every
existing field and action. This slice is `PRESENTATION_ONLY` and
`WIRE_EXISTING`; local verification is recorded in `status.json`.

---

## UI modernization — audit record details

The real workpaper, finding and review-point detail routes now use the shared
record heading, scoped status/navigation, white panels and responsive content
layout. Workpaper summary cards repeat its revision, frozen-submission count
and authorized planned-procedure linkage; finding cards repeat its amount,
correction and response state. A significant open review point shows its
existing completion-blocking consequence. Workpaper submission history has
mobile cell labels, and long comments and identifiers wrap. The workpaper's
native server-autosave textareas, finding response command, review disposition
command, authorization and same-document scope clearing remain intact. This
slice is `PRESENTATION_ONLY` and `WIRE_EXISTING`; the prototype's extra
workpaper upload, reassignment and review-desk controls need separate real
backend contracts and are not presented as available here. Local verification
is recorded in `status.json`.

---

## UI modernization — audit program and fieldwork

The real audit program library and engagement fieldwork control center now
use the shared audit heading, compact scoped metrics and white panels. The
library's selected version label renders its actual version and stays within
small viewports. Fieldwork initially shows one source section instead of the
entire adopted procedure catalogue, with an explicit All sections choice and
visible count; filtering only changes which authorized rows are displayed.
Published wording, applicability decisions, aggregate conclusions, independent
review and route-scope clearing remain governed by the existing commands and
queries. This is `PRESENTATION_ONLY` plus `WIRE_EXISTING`; it does not add the
prototype's risk-editing or simulated fieldwork actions. Local verification
is recorded in `status.json`.

---

## UI modernization — audit planning and completion

The real engagement audit plan and completion checklist now use the shared
record heading, navigation links, compact counts from authorized rows, white
panels and mobile table labels. The planning forms reflow into two columns on
wide screens and one on narrow screens without changing their fields or
commands. The completion view stacks long representation text and wraps
actions on narrow screens. Its in-place route change now clears the prior
engagement and reloads authorization and scoped data for the new parameter,
with a generation fence against an older load finishing late. This is
`PRESENTATION_ONLY` plus `WIRE_EXISTING` and one route-scope correctness fix;
the counts do not claim release readiness. Prototype-only planning lifecycle,
risk editing and professional decisions remain outside this slice. Local
verification is recorded in `status.json`.

---

## Project task progress display

The administrator tracker now divides each measured task bar into completed,
active or in review, pending or reopened, and blocked segments. The visible
counts and accessible progress description come from the same published task
cards; the overall total still counts shared cards once. Areas without a
published module mapping remain explicitly unmeasured. This is a read-only
display change and does not advance task status or acceptance gates.
The task-card lists now have status filters so administrators can find
completed, active, pending, or blocked cards across modules. The overall and
module bars retain their full published denominators while a filter is active.

---

## UI modernization — accounting period views

The real period detail, roll-forward and restatement routes now use the
shared accounting heading, white panels and responsive workbench layout.
Roll-forward and restatement counts come only from their authorized loaded
rows; their history tables have mobile cell labels. Existing create, reload
and independent-review actions, immutable period lineage, role checks and
draft boundaries remain unchanged. This is `PRESENTATION_ONLY` plus
`WIRE_EXISTING`. Local verification is recorded in `status.json`; the
remaining accounting routes and wider UI acceptance remain open.

---

## UI modernization — mapping and adjustment-journal detail

The real `/app/accounting/mappings/{id}` and
`/app/accounting/journals/{id}` views now return directly to their matching
queues and use compact record headings, white summary/workflow panels and
scoped metric cards. Mapping section links navigate within the existing
record; each mapping table supplies mobile cell labels. The journal metrics
repeat the visible line count and debit/credit sums and do not assert external
posting. The original mapping approval, journal posting and instruction export
commands, exact record IDs, SoD checks, immutable lineage and scope-revocation
clearing remain in their existing services and page handlers. This is
`PRESENTATION_ONLY` plus `WIRE_EXISTING`; exact local test evidence and the
remaining routes are tracked in `status.json`.

---

## UI modernization — accounting record and review queues

The real `/app/accounting/mappings`, `/app/accounting/journals`,
`/app/accounting/differences` and `/app/accounting/reviews` routes now use a
compact accounting heading, scoped count cards, white queue surfaces and
informational empty states. Counts come from each route's existing authorized
rows. "Linked journals" means a persisted proposed-journal link, not a
resolved difference; package selection remains an optional browser draft for
preview only and records no decision. The accounting route navigation, exact
detail links, native review checkboxes, `data-draft-field` hooks and server
authorization remain intact. This is `PRESENTATION_ONLY` plus
`WIRE_EXISTING`; extra prototype review actions and provider-dependent
behavior were not copied. Exact verification and remaining visual work are in
`status.json`.

---

## UI modernization — accounting landing and evidence

The real `/app/accounting` and `/app/accounting/evidence` views now share the
compact accounting heading, metric cards and white workspace panels. The
workflow dashboard's progress bar counts complete rows against only the
authorized visible period rows; required, stale and blocked rows stay open.
The evidence page does not infer approval from a linked reviewed result. The
native context selector and draft scope remain in place. The shared responsive
drawer reopens on a mobile-to-desktop viewport change. These are
`PRESENTATION_ONLY` and `WIRE_EXISTING` changes; the prototype's additional
accounting actions are not available from these views without their real
application contracts. The exact local verification and remaining UI scope are
recorded in `status.json`.

---

## UI modernization — practice and firm finance

The real `/app/practice/leads`, `/app/practice/time` and `/app/finance` routes
now use compact, scoped metric strips and a consistent practice panel rhythm.
Leads and time use the source's pipeline/workbench visual cues without copying
its fake records, persona controls or browser-local business state. The time
task and time-entry forms remain native with their existing form and
`data-draft-scope` boundaries; responsive classes replace only layout styles.
Firm-ledger counts label the records actually returned by its authorized
query, including "recent postings shown" rather than an all-time total.
Repeated explanatory copy was removed where PageHeader already conveys it.
The draft helper's duplicate optional-marker pseudo-element is suppressed
only in forms whose labels already spell out "optional". This is
`PRESENTATION_ONLY` plus `WIRE_EXISTING`; proposal-stage edits, budget impact,
full lifecycle boards and any provider-dependent behavior remain outside this
visual slice. Exact verification and remaining UI work are in `status.json`.

---

## Administrator project task progress

The administrator-only `/app/administration/project-progress` page reads the
published R2R task-card front matter and manifest. It shows one overall bar
counting each task once, seven module cards (Modules 20–26), and separate
audit-workflow and cross-module bars. The audit workflow is also broken out
into four bars for its published foundation, core fieldwork, extended
fieldwork and completion task phases; these are grouped by the manifest's
`AUD-17` through `AUD-20` work packages and sum to the audit aggregate.
Each card exposes the source status, with completed, active,
pending and blocked counts. The page is read-only: status transitions and
evidence remain governed by the task-pack helper and review rules. Its bars
measure reviewed task-card completion, not implemented software or production
readiness. The same dashboard now lists practice leads, time, firm finance,
documents/client portal, durable operations and administration as untracked
application areas with neutral, unmeasured bars. The published task pack has no
dedicated mapping for those areas, so no percentage or invented completion count
is shown. The Web publish includes the tracker snapshot, so a new deployment
is needed to show later repository status changes. Exact verification is in
`status.json`.

---

## UI modernization — client portal views

The three existing `/portal` views now share a client-safe navy header,
compact panels and narrow-screen detail layout. The request detail's native
file control no longer widens the document at 320px; upload/draft attributes
and backend effects are unchanged. The package's existing management state
uses the shared text-and-icon status chip. The client request and package
route-change browser cases are extended with responsive-width checks and
synthetic visual capture. Remaining route-family passes and complete UI
acceptance are still open; exact test outcomes are in `status.json`.

---

## UI modernization — shell and Portfolio pilot

The real Blazor app now has a prototype-inspired navy staff drawer, light top
bar, compact shared theme and workspace surfaces. Route-selected client and
public layouts keep the staff drawer out of client, home, sign-in and setup
views. The Portfolio pilot restyles authorized metrics, search/export and
register panels without changing their queries or actions. The prototype is
read-only visual reference; no persona, scenario, fake data or browser-local
business state has been copied. The route smoke check now asserts shell
separation and responsive document width for Portfolio and the client portal.
The remaining staff and client page families still need route-specific visual
and accessibility review. Exact verification outcomes belong in `status.json`.

---

## AS-PAR-002 portal, group and assessment scope isolation

The sibling-client browser audit now also exercises client portal routes,
consolidation group grants and in-circuit navigation across the assessment
workbenches. The assessment detail route gives an unknown ID and an out-of-scope
decision the same unavailable state. The tests check that prior client content
clears, sibling records remain hidden, and a return to the authorized route
restores its content. Application command isolation and the remaining route
inventory are still open; this does not complete AS-PAR-002. Exact source and
verification outcomes are in `status.json`.

## Development tenant administration and Graph query repair

Microsoft Graph member reads now send the required advanced-query count and
consistency settings, including the user-only member listing. A live invitation
requires an HTTPS redemption destination; the Graph adapter rejects HTTP before
dispatch. An opt-in live Development test exercised user provisioning, managed
group add/remove, guest invitation and the PBC mail worker through the real
providers. Graph accepted the mail request; recipient inbox delivery is not
asserted. The synthetic user, guest and temporary group were removed and exact
tenant reads found none remaining. This is Development evidence, not production
approval. The configured loopback HTTP guest redirect remains unsuitable for
live invitations until an approved HTTPS application destination is supplied.
Exact test and external-gate outcomes are in `status.json`.

## AS-PAR-002 sibling-client differential isolation

A new browser case signs in a user whose grants cover only client A (client- or
engagement-scoped), records the rendered text of every staff list, queue and
search route, then gives a sibling client B marker-named practice, PBC and
accounting records. Every route must render identically and never show the
marker, and client B's detail routes and PBC download must look exactly like a
random identifier. The audit found three pages that confirmed a sibling record
existed by showing "access denied" instead of "not available" (engagement PBC
inbox, audit fieldwork, staff financial package); they now render one uniform
unavailable state. At this slice's time, client portal and consolidation group
grants were still untested; the newer section above records their differential
cases. Command-level isolation remains open, so AS-PAR-002 stays partial.
Exact verification is in `status.json`.

## P2 durable worker delivery and P2b isolation matrix

A client portal upload now reaches SharePoint through the real pipeline: in a
Playwright journey the client staged a synthetic PDF, staff completed it on a web
host with `PbcTransfer:LiveProvider`, which enqueued a LIVE `pbc` operation, and a
separately started Acceptance worker process delivered it to the Development
selected site; the intent became RECEIVED and the exact stored version re-read
with the staged SHA-256. The drive now also rejects a token issued for a tenant
other than the binding's.

The P2b matrix runs automatically against the fake Graph drive: client A/B
documents land in separate client folders and neither scope can read the other's
receipt; mixed or guessed client, engagement, intent, drive and item identifiers
fail closed; a wrong-tenant token or connection blocks before any write; revoked
client and administrator identities are refused; a 429 moves the operation to
RETRY_WAIT and later completes without duplication; a lost final response is
reconciled by exact name to RECEIVED; a failed commit reconciles to
PROVIDER_BLOCKED with the request still open. Live in Development, client A/B
delivery and separation, guessed identifiers, and both the local and Microsoft
Entra wrong-tenant refusals passed, and all synthetic folders were deleted.
Production approval and independent review remain. Exact verification is in
`status.json`.

## P2 selected-site PBC provider

A firm-wide Administrator can now provision, from Administration → Microsoft 365
→ SharePoint, each accepted client's workspace folder tree and each engagement's
folder tree inside the approved selected site. The engagement's PBC intake folder
becomes a `working` repository binding, and its capability row is `VERIFIED`
only after a disposable upload, exact SHA-256 read-back and delete succeed.
`GraphPbcProviderSink` now has a live composition: it resolves only that
verified binding, uploads via a Graph upload session in 320 KiB-aligned
fragments through the restricted pre-authenticated transport, and issues a
receipt only after the exact stored version reads back with the staged SHA-256
and size. Unknown outcomes reconcile by the deterministic per-intent file name;
a retry never overwrites. The Web host enqueues LIVE transfers only with
`PbcTransfer:LiveProvider`, and only an isolated Acceptance worker with external
effects and group `pbc` may claim them. The resolver no longer requires the
client workspace root to equal the binding root; the tested-binding fingerprint
pins the exact folder.

A live Development run of the new acceptance test provisioned a synthetic
client and engagement, verified the binding, uploaded one synthetic document with
a matching exact-version receipt, reconciled it by probe, refused an overwrite and
deleted the synthetic folders. At the time of this section, a real client-staged
document had not yet been transferred by the durable worker; that was later
observed and is recorded in "P2 durable worker delivery and P2b isolation matrix"
above. Production approval remains. Exact verification is in `status.json`.

## Microsoft 365 tenant connection and unified administration

The tenant connection flow now completes the consent contract. After the
state-bound admin-consent return, a second nonce-bound OIDC sign-in to the
separate consent app authenticates the consenting tenant administrator; the
code is redeemed server-side, issuer/audience/tenant/nonce are validated,
external and personal identities are refused, and only the administrator's
`tid`/`oid` are stored. This trusted path is the only way to set the
connection's consent state to `VERIFIED`, which activation already requires.
Each enabled capability is then verified separately (`VERIFIED`,
`NOT_GRANTED`, `FAILED`, `BLOCKED_EXTERNAL`; stale after 24 hours) with its own
single-role app identity.

Optional capabilities are implemented and disabled by default: `User.Create`
tenant user provisioning (wizard with a one-time, never-stored initial
password), `User.Invite.All` guest invitation bound only to a CLIENT/ENGAGEMENT
ClientUser grant, and `GroupMember.ReadWrite.All` administration of allowlisted,
non-role-assignable groups that never grant AuditSphere access. Every Microsoft
mutation runs through `m365_external_operations` with an idempotency key,
recheck before dispatch, UNKNOWN reconciliation by immutable identity and a
separate local binding transaction; every change writes an append-only
`m365_administration_events` row. Role assignment now has a reviewed dialog with
current/proposed access, capability diff, scope expansion/reduction and
independence impact, explicit expansion confirmation, GROUP scope, reasons and
expiry enforced by real revocation. The Administration page is a tabbed
workspace with a dashboard and setup progress derived from persisted state.

CI and local browser journeys also use the Development/Test-only simulated
tenant. Separately, the authorized Development tenant completed the live
administrator-consent and nonce-bound identity path with a distinct
certificate-backed directory-reader registration. Consent and the bounded
directory capability are `VERIFIED`. The selected-site capability is also
`VERIFIED` after exact saved site/library/root reads and a denied synthetic
unrelated-site control; the local working-site binding is `ACTIVE`. Optional
mail, provisioning, guest and group capabilities remain separately gated; no
successful Microsoft mutation or production acceptance was observed.
The canonical GitHub Wiki guide covers both exact callbacks and independent
capability gates. Exact verification is in `status.json`.

The setup progress checklist now shows trusted consent for the exact draft
and current connection revision. The administrator resumed the local
Development draft without reentering the bootstrap proof. The provider test
read the saved SharePoint site, library and root, then received `403` from
the separately confirmed synthetic unrelated site using the selected-site
identity. The negative-control URL is held in private Web user secrets. The
provider path recorded exact-revision resource evidence and marked the draft,
connection and selected-site capability `VERIFIED`. A failed recheck now
supersedes an older pass.

The default client folder template version 1 was saved and approved locally.
After the owner approved the exact Development working-site binding, the
administrator used the guarded activation action. The setup page displayed
draft state `ACTIVE`, revision 11, and “Workspace activation recorded.” This
is the local default binding for future client workspaces. A reload exposed a
post-activation resume guard that rejected the same bound administrator; the
guard now permits that administrator to reopen the active configuration for
review while draft saving stays disabled. The Development browser showed the
same `ACTIVE` revision 11 and activation checklist after restart and resume.
Mail, records, production release and independent acceptance remain separate.
Exact tests, hosted checks and live Development observations are in
`status.json`.

The tenant-connection directory search now accepts an optional exact
user-principal-name domain. A domain alone browses enabled users; a name
prefix plus domain uses one bounded Microsoft Graph query. Server-side domain
validation and a post-response suffix check prevent a cross-domain result
from being offered for binding. The Development tenant returned the expected
member for the combined filter and no match for a different domain. This
read-only search grants no AuditSphere role.

## AS-PAR-002 audit program library access refresh

The audit program library now clears cached versions and procedure text before
an explicit refresh resolves the current actor and grant. Browse and Search
hide the old projection while reauthorizing and leave it cleared on denial.
The Application library and section
queries recheck firm-wide internal authorization after reading, so a grant
change during a read cannot return the protected result. A PostgreSQL-backed
browser regression opens a published procedure, revokes the exact Partner
grant, and confirms Search and Refresh leave no procedure text visible. This
is one bounded authorization surface, not completion of the whole route audit;
exact verification and remaining limits are in `status.json`.

## Microsoft 365 setup: one-click administrator resume

The tenant connection page now prepares a one-use, ten-minute consent state
bound to the current firm-wide Administrator, exact configured tenant, setup
draft, and session epoch. A separate consent app registration and exact callback
must be explicitly configured and enabled. The callback consumes the state
once and records `RETURNED_UNVERIFIED` only after the tenant matches; it does
not activate a connection or assert Microsoft permissions. A bounded
Development directory check is described below. The current second sign-in leg
authenticates the administrator and verifies the consent in Development. The local migration and
focused PostgreSQL and URL-builder checks are recorded in `status.json`.

The separate directory reader now supports bounded, administrator-only live
search from the tenant connection page, with a dedicated certificate and
`User.Read.All` application role fence. It is disabled by default. Search
returns no local role grant. A separate **Verify directory access** action
performs a bounded exact-member provider read and records its result against
the current connection revision, with a 15-minute recent-check display.
This does not verify the Microsoft consent grantor, activate the connection,
or verify any other capability. A separate explicit Bind exact identity action
re-reads the selected member before creating or refreshing its local binding.
In the Development tenant, the dedicated app received the exact permission,
and a certificate token plus the live page completed bounded Graph reads.
The consent callback's denied path also returned to the app and consumed its
attempt without activating a connection. The later verified live callback is
recorded above; neither observation establishes production acceptance.
The binding step re-reads a selected Microsoft member by exact object ID.
Binding is idempotent and records an enabled Graph
observation without a role grant. The Administration handoff starts with no
role or scope chosen; the existing scoped role service remains the separate
access transaction. Guest invitations and tenant user creation are not
enabled by this path. A live Development browser check bound the already known
synthetic Staff fixture and reached the role review form with blank role and
scope choices. No new grant was applied. The later Development consent return
is verified as described above; this is not production acceptance.
The User and role administration screen now offers a paged selector of enabled
Microsoft users when the separate directory reader is configured. An optional
domain suffix filter is applied in Graph with an advanced query, and the app
rechecks tenant, enabled state and exact domain on every page. The roster
fields remain in a collapsed fallback disclosure. The Development browser
showed both unfiltered and filtered results, then bound the existing synthetic
Staff fixture without changing its grant.

The firm administration screen now projects active local user, administrator,
grant and pending-first-access invitation counts from the current firm. Its
Microsoft 365 overview keeps the local connection revision, consent record,
recent directory check, selected-resource evidence, and mail/group blockers
separate. An unverified consent callback is not shown as a verified grant.
The current blocked states and test evidence remain in `status.json`.
The local configuration service now refuses administrator-entered tenant
consent `PASS` evidence and cannot promote recorded references to a verified
connection. Activation requires a separately verified consent state, including
for older revisions with `OBSERVED` evidence. The later Development consent
path verifies the authenticated administrator and independently checks the
directory reader. The setup page still labels historical provisional consent
rows as unverified.
When an exact-app-matched directory reader is configured, a successful
one-use consent return now runs the bounded exact-member directory check
before returning to the tenant page. The check records directory capability
evidence only; the callback was `RETURNED_UNVERIFIED` at that earlier revision
and could not identify the Microsoft grantor or activate the connection. The
current second sign-in leg resolves that identity in Development. A denied return
does not run the check. The callback passes its original setup draft identity
to the directory check, so a newer draft for the same firm and tenant cannot
receive evidence from an older consent attempt.

The owner has requested a tenant-administration experience that supersedes the
earlier blanket Graph-scope exclusion only for an isolated read-only directory
reader. The [permission decision](../architecture/auditsphere-m365-tenant-administration-permissions.md)
records `User.Read.All` as the proposed application permission for that reader,
separate from the `Sites.Selected` document worker. A dedicated Development
registration has this permission and completed a bounded live read; production
consent and acceptance remain open. User provisioning, guest invitations and Microsoft
group changes remain optional, separately gated capabilities. Existing local
`RoleGrant` administration remains the source of AuditSphere access.

After the initial proof-backed administrator binding, the same currently
authorized firm-wide administrator can reopen the local Microsoft 365 setup
draft with **Continue setup with this account**. The button uses the current
Microsoft `tid`/`oid` and local grant; it does not ask for the bootstrap proof
again. For an empty tenant field, the authenticated tenant ID is saved to the
draft automatically. The screen shows the tenant and working-site URL first;
resource IDs, optional capabilities and folder templates are under advanced
settings. First-time bootstrap still requires the private proof. This local
draft action does not grant Microsoft consent, selected-site access, or live
provider readiness. Exact test evidence is in `status.json`.

The resumed draft now displays a compact connection-progress checklist. Its
Application query checks the current firm-wide Administrator grant, reads only
this firm's draft, linked connection revision, recorded exact-resource evidence,
approved client template and workspace activation, and labels each step as a
saved local record or an outstanding operator action. The checklist links the
canonical production guide; it never probes Microsoft or reports a prepared
reference as live access. Exact verification is in `status.json`.

## P2 selected-resource read preflight

The Infrastructure provider now has a read-only Graph probe for an already
active firm workspace. It loads the target from stored configuration, requires
the matching active connection and approved client template, obtains a token
through a separate runtime certificate reference, and accepts only a token for
the expected tenant carrying the single `Sites.Selected` application role.
It reads the exact site, enumerates libraries only within that site, and checks
the configured root folder identity and URL. Mismatches and missing bindings
block before any write; throttling is retryable. (Historical: at the time of this
section the probe was not composed into the Worker and had no live tenant result;
the later "P2 selected-site PBC provider" and "P2 durable worker delivery" sections
supersede this.)
The local test evidence and remaining gates are in `status.json`.

The same read-only check can now run against a pending, exact-firm setup draft
before workspace activation. It takes the tenant, site, drive and root IDs
from the saved draft and its separate runtime credential reference, then uses
the same `Sites.Selected` checks. Suspended, blocked and active revisions are
not eligible for this pre-activation path. A successful observation does not
record consent/evidence, activate the workspace or enable external effects.
Those two prerequisites must be present in each environment before a live
tenant result can be claimed.

The authorized Development tenant now has a separate certificate-backed
runtime credential on the existing application. A live token carried only
`Sites.Selected`; Graph read the approved site and its site-scoped libraries
and roots, while an existing synthetic unrelated site returned `403`. The
observed `Client Content` binding was saved in the local draft, and the
pre-activation probe passed against that stored binding. No upload,
exact-version readback, retry/reconciliation, production access, activation,
or independent acceptance was observed. The Worker still has no live PBC
provider composition.

The firm-wide administrator's Microsoft 365 setup page now offers **Test
selected site connection** for a saved draft with a prepared connection.
The action checks current local authorization before and after a read-only
Graph probe of the saved site, library and root using the separately configured
runtime certificate. Its result is a transient screen message: it records no
consent or selected-resource evidence and does not activate the connection.
The private Development certificate paths are held in .NET user secrets, not
repository settings. Exact local verification and remaining P2 gates are in
`status.json`.

A selected-site application token in the approved Development tenant also
created an upload session against the saved library and root. The session was
canceled before any bytes were committed. This confirms one bounded write API
is available to the existing site grant; it does not verify a document upload.
The new `PbcRepositoryBindingResolver` refuses an operation unless its trusted
firm, client, engagement and intent match a single current repository binding,
ready client workspace, active firm connection and a capability record tied to
the exact binding fingerprint. It is a local prerequisite component only:
neither the Worker nor `GraphPbcProviderSink` uses it yet, and no live PBC
transfer, exact-version readback, or reconciliation is accepted.

The next P2 prerequisite is a restricted transport for Graph-issued
preauthenticated upload/download URLs. It accepts only HTTPS commercial
SharePoint/OneDrive hosts and GET, PUT or DELETE; it sends no Graph bearer
token, rejects redirects, suppresses URL-bearing HTTP telemetry, and replaces
transport exceptions with a non-sensitive error. A disposable synthetic file
was uploaded to the approved Development site; Graph returned item metadata
and a current version record, then the file was deleted. This does not prove
preserved exact-version retrieval or a durable application receipt. The live
PBC adapter remains uncomposed.

## P1 immutable identity lookup

The Web actor resolver and sign-in landing now require the Entra `oid` claim
alongside `tid` and the signed-in session epoch. A matching `sub` or name
identifier can no longer substitute for the immutable object ID. A
PostgreSQL-backed API regression checks missing `oid`, changed email and wrong
tenant behavior. The development app registration was inspected in the
authorized tenant and already contains the local web callback; no tenant
setting was changed. This is a local identity boundary, not live OIDC or
production acceptance. Exact verification is in `status.json`.

The application cookie now requires the Secure flag outside Development/Test,
and remains HttpOnly. The local profiles retain request-matched cookie security
for the supported HTTP test harness. The production deployment Wiki already
requires an HTTPS hostname and exact HTTPS callback, so its operator steps
remain accurate.

## AS-PAR-002 firm Finance current-access refresh

The firm Finance workbench now reads periods, accounts, recent postings, and
close-period capability through `FirmFinanceQuery`, which authorizes before and
after the scoped read. Explicit refresh clears the old ledger and close draft,
then rechecks actor/session epoch and fences late reads. Period close continues
through its guarded Application command and reloads current access after a
successful decision. A browser regression covers same-document grant revocation
and row removal. This remains one bounded authorization slice; exact checks are
in `status.json`.

## AS-PAR-002 practice lead current-access refresh

The practice-leads workbench now reads a bounded firm-wide commercial list
through `PracticeLeadQuery`, which rechecks the grant after reading. Explicit
refresh clears prior lead rows and form drafts, resolves the current actor,
and fences older reads by generation and session epoch. Lead commands retain
the existing guarded Application service and reload the protected view after
success. A browser regression covers same-document grant revocation and lead
removal. This is a bounded authorization slice; exact results are in
`status.json`.

## AS-PAR-002 Operations current-access refresh

The Operations workbench now clears its redacted ledger, operating mode,
command result, and cancellation draft before an explicit refresh checks the
current firm-wide administrator grant. A second access check and session-epoch
check fence late reads. Recovery commands resolve the current actor and reload
the protected view after the guarded Application command. A browser regression
revokes the administrator grant and confirms that a same-document refresh
removes the previously visible operation. This is a bounded authorization
slice, not completion of the whole-application audit. Exact verification is in
`status.json`.

## R2R baseline scope and inventory

The owner approved the STE-R2R-ARCH-001 seven-module scope. T001 now has a
[current-code inventory and conflict register](../task_breakdown/tracking/auditsphere-r2r-tracker-current-baseline-inventory.md)
that identifies reusable accounting and consolidation symbols, distinguishes
proposed shared contracts from implemented equivalents, and records the current
Purview/eSignature exclusion without removing release or records evidence gates.
The repository owner accepted the exact T001 inventory commit; this owner
decision does not satisfy the separate non-owner independent-review requirement.
T002 direction is approved, but its transaction and contract handoff remains in
review. The separate GitHub Wiki deployment
guidance was published under the owner's authorization and its canonical pages
were checked after publication. Exact source and Wiki revisions and observed
checks are in `status.json`.

## R2R chart-revision transaction fence

Chart publication and draft account/alias edits now hold the same chart-version
row lock through their guarded save. A paused draft edit cannot land after
publication, and a second publisher sees the committed protected state. The
PostgreSQL race tests, full solution suite, EF model check and hosted CI passed;
exact results are in `status.json`. The T002 ledger now identifies this owner
and maps current setup and intake methods through T020 while leaving proposed
contracts and independent review explicitly open.

## R2R chart-version allocation and ownership map

Chart-version creation now locks the client row through version allocation and
save, so concurrent requests receive successive versions. A PostgreSQL race
test proves the ordering. The T002 ledger now names a current Application owner
or an explicit new-contract decision for every preserved request; that mapping
does not prove the proposed contracts are implemented. T002 remains in review
until its outstanding contract choices and independent acceptance are resolved.

## AS-PAR-002 firm administration current access

Firm administration now loads its grant directory, invitation evidence, directory
observations, access history, and safety state through a named Application query
that checks the current firm-wide Administrator grant before and after the read.
Explicit refresh clears the prior projection and private form values before
reauthorizing; late reads cannot republish an older result. Invitation copying
also rechecks both current administrator access and the invitation's active
grant before using the clipboard. The browser regression revokes the invitation
grant and verifies that a stale copy action does not reach the clipboard; it
then revokes the administrator grant and verifies that the same document clears
its private directory on refresh. This is a bounded AS-PAR-002
slice; exact verification is in `status.json`.

## AS-PAR-002 staff portfolio refresh and export

The portfolio CSV now prefixes spreadsheet-formula-shaped text with an apostrophe
before CSV escaping; the existing browser export regression uses a synthetic
client name beginning with `=` to verify the downloaded payload. Numeric
metrics remain display-only and no source identifier is rewritten. Exact
verification is in `status.json`.

The staff portfolio now clears cached clients, metrics, release candidates, and
financial packages before an explicit scope refresh. CSV download performs a
fresh scoped read and current-grant check before creating the file, so an open
page cannot export its previously rendered rows after local grant revocation.
Overlapping reads cannot republish an older result. The browser regression
checks the authorized CSV payload and then revokes the grant in the same
document; the next export command clears the private row and sends no second
download payload. The existing client PBC same-document route regression also
exposed a missing parameter-change reload; the request page now reloads by
route parameter and fences late reads before publishing protected content.
Upload and reply commands pin their starting request and page generation,
then reauthorize before displaying refreshed conversation or transfer state.
This slice does not close the whole-application AS-PAR-002 audit. Exact local
verification is in `status.json`.

## AS-PAR-002 proposal current-access refresh

The proposal detail page now offers an explicit refresh of the current proposal.
It clears the displayed terms and version list before resolving the actor and
checking the current firm-wide commercial grant again. A browser regression
loads a proposal, revokes that grant, refreshes in the same document, and
checks that the prior proposal details disappear. Route-change and scoped-user
denial checks remain in the same journey. This is a bounded read/revocation
repair; it does not enable the proposal review, delivery, or client-response
workflow and does not close the whole-application authorization audit.

## Local development schema readiness

The persistent loopback development database was backed up and restored in a
temporary rehearsal before its pending repository migrations were applied. The
Web host was stopped for the migration and restarted afterward. Its readiness
endpoint is healthy, the original administrator binding and inactive setup
draft remain intact, and a second restore rehearsal passed against the updated
schema. This is local development readiness, not production migration or
provider acceptance. Exact schema and verification facts are in `status.json`.

## P1 explicit Microsoft account selection

The OIDC sign-in route now accepts an opt-in account-choice flag for testing
separate tenant fixtures in a browser that already has an administrator Microsoft
session. The default challenge remains unchanged. The operator Wiki explains
the optional route; exact checks and source identity are in `status.json`.

The account picker resumed the existing administrator session when its "Use
another account" option was chosen. An additional opt-in reauthentication flag
now asks Microsoft to show a fresh sign-in form. The local browser reached the
staff fixture's password screen; the fixture credential and any MFA remain with
the user. Neither the account-choice prompt nor the password screen alone proves
a fixture sign-in, local binding, or grant. The operator Wiki describes the fallback,
and exact checks and revisions are in `status.json`.

A fresh browser challenge using the already signed-in developer administrator
completed Microsoft's OIDC callback into the local Web app. The portfolio and
firm administration pages loaded, and the latter displayed the bound firm-wide
Administrator grant, Development environment, and fenced external effects.
Readiness returned healthy. The protected Microsoft 365 setup page still
requested its private bootstrap proof, so no setup revision or provider binding
was changed. This positive local administrator readback does not establish
staff/client isolation, wrong-tenant denial, or production P1 acceptance.

The staff fixture subsequently completed Microsoft sign-in on the local
Development host. AuditSphere rejected the unmapped identity, as intended, but
initially exposed that rejection as a server exception. The callback now shows
the existing access-not-assigned page for this case and a generic response for
other remote authentication failures. The same staff fixture reached that page
after the fix; it had no local role or application access. The administrator
subsequently bound the exact enabled Microsoft tenant/object identity through
the local roster form. A guarded local transaction then created one synthetic
PROSPECT client and its safety state as a narrow test scope, without fabricating
a commercial approval. The administration form is prepared for a Staff grant
limited to that client. After exact owner confirmation, the administration UI
saved one client-scoped Staff grant and a non-sent invitation intent; a database
readback confirmed no firm-wide or engagement grant for that user. A subsequent
account-picker attempt showed the synthetic client on the portfolio, but the
shared browser session also retained administrator controls, so that first
attempt was inconclusive. The owner then completed a fresh Microsoft staff
credential challenge. The resulting portfolio listed the granted synthetic
client, firm administration returned Access unavailable, and a separately
seeded synthetic sibling client remained absent from the portfolio and search.
These are local Development UI checks, not complete P1 or production acceptance. The
remaining P1 fixture cycle and production acceptance remain open; exact checks
are in `status.json`.

Two separate enabled client fixtures are now bound by immutable Microsoft
tenant/object identity in Development. The firm administration UI saved one
`ClientUser` grant for each: Client X to the original synthetic client and
Client Y to the sibling, with unsent invitation intents. Read-only PostgreSQL
readback found exactly one active grant per fixture and no firm-wide or
engagement scope. Neither client fixture has a recorded first access yet.
Live client sign-in, cross-client denial, negative identity fixtures, and
production acceptance remain open; the observed grant details are in
`status.json`.

The owner subsequently completed Client X's Microsoft sign-in and required
Authenticator registration. The browser reached the restricted client portal;
the app recorded first access for Client X but not Client Y. Direct navigation
to operations and firm administration from that client session showed Access
unavailable. There were no assigned requests or shared packages, so the
cross-client data-isolation fixture remains untested. Client Y and negative
identity cases are still pending.

The owner also completed Client Y's Microsoft sign-in and Authenticator
registration. Its browser returned to the restricted client portal, and firm
administration showed Access unavailable. Database readback now shows first
access for both client fixtures and exactly one active `ClientUser` grant per
fixture, each tied to its own synthetic client. Both portals have no assigned
records, so cross-client content denial and the remaining negative identity
fixtures are still pending.

Focused PostgreSQL-backed browser tests separately verified local sibling PBC
denial by direct URL and exclusion from an engagement-scoped portal read.
Focused identity regressions verified immutable `tid`/`oid` lookup despite a
changed email, wrong-tenant rejection, and disabled/stale-session denial. The
later Development fixture below adds live client-content checks. Exact rerun
results and source revision are in `status.json`; P1 remains open.

The first synthetic Draft PBC request exposed a client-visible gap: before the
fix, a fresh Client X Microsoft sign-in could open that unsent request. The
client inbox and direct request page now exclude Draft state. After restarting
the Development host, Client X's direct Draft URL returned Request unavailable.
A browser regression also confirms that an owning client cannot find its own
unsent Draft in the inbox or by direct URL.

A separate clearly marked synthetic request passed the application service's
Draft-to-Sent transition. Its notification remains queued locally with no
delivery timestamp; external effects are disabled. Client X's portal displayed
only this Sent request and opened its details. A fresh Client Y Microsoft
sign-in showed no assigned request and direct navigation to the Sent request
returned Request unavailable without its private marker. This completes one
local Development positive/negative content-isolation fixture, not the other
live identity, production or independent-review gates of P1.

A focused Release E2E rerun also passed for a synthetic owning client opening
its assigned PBC request and completing the guarded upload journey (1/1).
This separately verifies the local positive browser path in an isolated test
database; the Development Sent fixture observation above is distinct.

The synthetic Client Y sign-in name was temporarily changed in the developer
tenant. After the owner completed a fresh Microsoft credential challenge under
that name, AuditSphere still resolved the same client-scoped identity: its
portal listed no Client X requests and the direct Client X Sent-request URL
returned Request unavailable. The administrator restored Client Y's original
sign-in name and Microsoft 365 confirmed the reverse update. The local user
email display value did not synchronize during the changed-name sign-in; the
immutable subject remained the authorization binding. Wrong-tenant and
disabled/revoked live fixtures, production OIDC and independent P1 acceptance
remain open. Exact observations are in `status.json`.

## P8a production Data Protection key ring

Production Web startup now also requires a persistent Data Protection key
directory and a currently usable certificate with a private key. The key ring
is certificate-protected at rest, survives a Web host restart, and uses the
configured tenant and installation IDs for application isolation. Synthetic
production-host regressions cover a missing profile, encrypted key XML,
restart readback, and cross-installation/tenant rejection. The existing Wiki
deployment guide names the required private configuration. Actual secret
custody, backup, certificate rotation, restore, and production multi-instance
operation still need deployment evidence; P8a remains open. Exact source,
local checks, hosted CI, and Wiki revision are in `status.json`.

The production profile now accepts explicitly configured prior certificates
for decryption while using the current certificate for new key protection.
An incomplete prior entry refuses startup. A synthetic host test confirms an
old protected value remains readable through a certificate change, and becomes
unreadable if its prior certificate is omitted. The Wiki has administrator
rotation steps. This proves local configuration behavior; no production
certificate, secret store, key backup, or rotation rehearsal was used.

The Web host now gives each HTTP request a server-owned diagnostic ID in its
response header, Serilog scope, and active trace. It ignores a caller-supplied
ID; a focused API regression covers distinct IDs on success and missing-route
responses. Existing durable operations retain their separate persisted
correlation IDs. Propagation into every browser command, audit event, provider
request, dashboard, and alert remains outside this local slice.

The Web telemetry pipeline now registers the framework's HTTP request meter
for duration and active-request measurements. A configured OTLP endpoint is
validated once at Web or Worker startup; malformed or credential-bearing URLs
refuse startup instead of silently disabling export. An absent endpoint still
disables export. Local configuration tests do not establish a live collector,
dashboard, alert threshold, or production telemetry privacy acceptance.

The worker's existing durable-operation disposition counter now includes the
persisted destination state as a bounded tag. Retry waits, uncertain results,
and dead letters can be distinguished without using operation, client, or
correlation identifiers as metric labels. Database-backed regressions cover
retry and uncertainty transitions. This does not create a dashboard, alert
policy, live exporter result, or production telemetry acceptance.

## P8b synthetic accounting workload baseline

The existing PostgreSQL-backed accounting benchmark was run repeatedly against
its isolated synthetic schema. It exercises concurrent durable enqueue,
two workers processing completeness operations, a paged general-ledger read,
and a pure consolidation calculation. Raw measurements and fixture sizes are
recorded only in `status.json`. This is a local component baseline, not a
concurrent staff or client-portal load test, a large import/upload test, a
measured p95 screen target, or production capacity acceptance. The approved
pilot workload and external-provider failure profile remain outstanding.

## P1 production OIDC startup guard

The Production Web host now refuses startup when OIDC is absent altogether,
including when no `Identity` section is supplied. Development and Test retain
their supported local profiles. The Production cookie-policy test uses synthetic
OIDC settings to exercise the cookie behavior without a live sign-in. The full
local suite and EF model check passed; this closes a configuration gap but not
the live P1 identity fixture cycle. Exact evidence is in `status.json`.

## P1 incomplete OIDC configuration guard

The Web host now refuses startup when any `Identity` setting is supplied without
the tenant ID, client ID, and client secret together. This prevents a partial
development or deployment configuration from silently selecting the no-OIDC
sign-in path. A focused API host regression covers each missing-key shape.
This is local fail-closed configuration behavior; no live Entra sign-in or
production identity acceptance is claimed. Exact verification is in
`status.json`.

## R2R architecture and group authority

T002 remains in review. The repository owner approved the architectural
direction at exact commit `779480c613b49f0e6ee1a818864ef48b5b8bd607`.
Its [architecture and transaction ledger](../task_breakdown/tracking/auditsphere-r2r-tracker-architecture-transaction-ownership.md)
records the current static-service design, preliminary transaction owners and
shared contract candidates. Group creation now derives the creator's group role
from an active firm-wide grant, preventing a Manager creator from receiving an
implicit Partner group grant. Group creation also serializes against firm-role
revocation through the firm safety row lock, so a revoked role cannot create a group after
the revocation commits. The concurrent PostgreSQL regression and full-suite
results, including one initial browser timeout and clean standalone browser
rerun, are recorded in `status.json`. The 111-request registry assigns one
owning task per request; each downstream task must prove its concrete method
and transaction. T002 completion still requires the ledger's explicit transaction-owner,
shared-contract and professional-role decisions; the owner's direction approval
does not accept that incomplete handoff.

Perimeter approval now refuses a review by the scope version's creator, even
when that person has a current group reviewer grant. PostgreSQL fixtures use a
distinct preparer and reviewer; focused and complete Domain/API results plus
the later standalone 75/75 browser suite are recorded in `status.json`. This
closes one current-code independence gap, not
the T041 task or the separate non-owner repository review gate.

## P1 development OIDC credential store

The Web project now declares a stable .NET user-secrets ID, so an approved
non-production `Identity:ClientSecret` can be supplied privately without
editing `appsettings.json` or committing the value. The developer tenant's
existing AuditSphereOps Development registration was inspected read-only: it
has the local `/signin-oidc` web redirect and granted `Sites.Selected`
application plus `User.Read` delegated permissions. That portal observation is
not a live OIDC login test, a selected-site provider test, or production
acceptance. A private credential reference and approved staff/client/negative
identity fixtures are still required before P1 can be exercised. Exact checks
and external gate state are recorded in `status.json`.
The signed-in EasyGuide directory currently shows no Azure subscriptions
accessible to this account, so subscription-backed development infrastructure
cannot be provisioned there yet. Entra app configuration remains separate.

The first-administrator OIDC callback admits only the exact configured
tenant/object identity to proof-backed setup before a local user exists. That
setup-only cookie has no session epoch and cannot resolve a protected application
actor. After explicit owner confirmation, the private proof was submitted to
the local setup route and the initial firm-wide Administrator was bound. A
fresh Microsoft sign-in then reached the portfolio and administration views
with the current session epoch. The setup page reports the binding as complete.
The saved tenant draft remains inactive and unverified, with no selected
SharePoint binding or external effects. The complete identity fixture cycle and
production P1 acceptance remain open. Exact verification and source identity
are in `status.json`.

## P2 provider transfer scope

The durable PBC transfer handoff now carries the operation's firm, client,
engagement and upload-intent IDs into upload and reconciliation provider calls.
Staged chunks are selected using the same scope, and the simulation adapter
rejects a receipt identity for a different intent. This prepares a bounded
selected-resource adapter without enabling a live Graph effect. The live
adapter, approved binding and tenant isolation evidence remain open under P2;
exact local checks are in `status.json`.

Reconciliation now applies the initial transfer's receipt checks to a retried
provider result: the registration identity must be present and match any
previously known identity, and the reported byte count must equal the staged
declaration alongside the existing SHA-256 comparison. The focused PBC tests,
Release build and EF model check passed. A concurrent full solution run had an
unrelated browser failure in a test under active edits and was cancelled; the
exact limitation is recorded in `status.json`.

## Client portal landing access refresh

The portal landing page now clears its assigned request and package lists before
an explicit refresh resolves the current client identity and active grants. It
builds the new projection from an actor-local query, rechecks the session epoch
before publishing the result, and fences overlapping loads. The existing
engagement-scope browser journey now revokes the client's grant and verifies
that refresh removes the authorized request in the same document. This remains
a bounded AS-PAR-002 read/revocation slice; exact checks and source identity
are recorded in `status.json`.

## Client financial-package access refresh

The management-review page now reloads on package route changes and offers an
explicit refresh. Each load clears the prior package, actor, decision fields and
result before resolving current identity and querying the exact package scope;
an older asynchronous route read cannot repopulate the new view. A browser
regression covers grant revocation followed by read-only refresh, alongside
the existing route-isolation and denied-decision journeys. This is a bounded
AS-PAR-002 repair, not complete client-portal or whole-application acceptance.
Exact verification and source identity are recorded in `status.json`.

## Client PBC request access refresh

The client request page now clears its request, transfer receipts, conversation,
and action results before an explicit refresh resolves the current identity and
reauthorizes the exact assigned request. Denied refresh also removes browser-saved
reply and upload drafts. A browser journey exercises same-document navigation to
another recipient's request, return to the assigned request, then role-grant
revocation and refresh without a document reload. This is a bounded AS-PAR-002
read and revocation repair; the whole-application authorization audit remains open.
Exact verification and source identity are recorded in `status.json`.

## Staff PBC inbox route transition

The staff PBC inbox now clears rendered request metadata as soon as navigation
leaves its current engagement route and renders a loading state before an
asynchronous actor/scope reload. A sibling-engagement route cannot leave the
previous request visible while the new authorization decision is pending. The
existing browser regression passed after the repair; the initial full run's
failure and the later standalone browser result are recorded separately in
`status.json`. A route-generation fence also discards late actor, recipient,
reviewer and request results from an earlier navigation. The complete Release
suite passed after that follow-up; exact counts and source identity remain in
`status.json`.

## Release candidate access refresh

The release workbench clears its candidate, checkpoint, attestation, signature
projection and release key before a route change or explicit refresh rechecks the
current actor and exact candidate scope. A late read from an older route cannot
restore its prior evidence. Scope/session denial on issue reloads the protected
view. The release page describes provider evidence without claiming Purview
acceptance, which is outside the approved product scope. A browser regression
covers a same-document route change and a revoked staff grant. Exact verification
results are in `status.json`; this is a bounded AS-PAR-002 authorization repair,
not complete whole-application acceptance.

## P1 bound setup session renewal

The first administrator can resume an expired, bound Microsoft 365 setup session
with the original private proof. The Application service rechecks the exact bound
Microsoft identity, current local session epoch, and active firm-wide Administrator
grant before rotating the setup capability. Missing or stale authority remains denied.
A live check in the local development app resumed an expired session and preserved
the inactive draft without adding another user or grant. This establishes local
recovery behavior only; provider and production acceptance remain open. Exact
verification and source identity are in `status.json`.

## P1 local identity session binding

OIDC sign-in now requires a mapped, enabled local `(tenantId, objectId)` identity and
records its current local session epoch in the protected authentication ticket. The
development sign-in records the same epoch. Current-actor resolution rejects a ticket
whose epoch no longer matches the local user, so an old cookie cannot acquire the new
epoch after role revocation. The browser regression verifies that a refreshed journal
and review queue clear protected data when that happens. This corrects the earlier
review-ledger inference that incrementing the database epoch alone invalidated an
open circuit; the ticket needed to retain its sign-in epoch.

This is local P1 hardening, not live Entra acceptance. Approved runtime credentials,
tenant authorization, identity fixtures and observed live sign-in behavior remain
external prerequisites under issue #13. Downstream live-provider issues remain gated
by that acceptance. Exact checks and source identity are in `status.json`.

The owner kept the Purview and eSignature provider exclusions. The dedicated
provider issues #17–#19 were closed as not planned, and the recovery, operations,
and final-acceptance issues retain their in-scope work without those provider
prerequisites. The issue closures do not establish live records or signing acceptance.







## Period workbench access refresh

The restatement and roll-forward selection loaders clear prior projections and resolve
current actor/grant state before rebuilding scoped choices and history. Revoked client
access and disabled users clear client names, periods, package choices, draft values and
review actions. An engagement-only grant cannot restore client-level period access.
Scope/session-denied command results also clear the protected view. A stale source
package or restatement lineage now remains a visible service error when a fresh
client-scoped authorization check confirms the session is still valid.

`PeriodWorkbenchScopeJourneyTests` exercises the two screens with revoked grants and
disabled identities in an existing browser document and verifies no period/restatement
mutation. This is a bounded AS-PAR-002 AC02–AC03 / AC-24 repair; the whole-application
query, export, command and revocation audit remains open. Verification results and the
source baseline are recorded in `status.json`. Deployment guidance is unaffected.

The pending-work handoff links every story family and preserves optional professional
profile gates and root product exclusions. Follow specification §46 for independent
review and explicit merge authorization before advancing acceptance.


## 1. Active Implementation Scope







- **Active Work Package:** Audit workflow gap-closure, client accounting implementation, and repository-wide documentation & AI navigability refactoring.



- **Implementation Status:** `IN_PROGRESS`



- **Acceptance Status:** `LOCAL_VERIFIED`



- **Scope Boundary:** Import-first preparation, audit, and consolidation workspace (not an operational client ERP). Excludes client sales/purchase/inventory operations, payroll execution, and payment initiation. Purview and eSignature provider integrations are excluded from product scope: exact uploaded signed-document evidence, SHA-256 identities, human decisions, and release manifests are preserved without claiming provider acceptance.







---







## 2. Recent Completed Slices (Handoff Summary)







### 2.0 MudBlazor UI Content Migration (commit `e66bc49`)

- **All inventoried routes migrated:** every AuditSphereOps route renders its content
  through MudBlazor 9.10.0 primitives (`PageHeader`, `MudPaper`, `MudAlert`, `MudTable`,
  `MudButton`, `MudLink`, `StatusChip`, `LoadingState`, `MudGrid`) inside the existing
  Blazor Interactive Server boundaries. No authorization, revision-fencing or
  persistence behaviour changed.
- **Contracts preserved:** native `h1`/`h2`, `id`, `aria-labelledby`, `role`,
  `data-draft-*`, form `id`s and `.command-result` status text are retained;
  `draft-state.js` and `pbc-upload.js` are byte-identical to `master`.
- **Two E2E regressions found and fixed in the UI layer:** the contiguous
  `Status: SENT` invoice text, and the `GetByRole(AriaRole.Region)` lookup of the
  time form (labelled cards now carry an explicit `role="region"`).
- **Documented exceptions** in
  [retired UI migration history](https://github.com/nirzaf/AuditSphere/commits/master/docs/auditsphere-ui-mudblazor-conventions-migration-current.md):
  6 native tables (`tfoot`/`colspan`) and the native browser-draft boundary
  elements. `/app/accounting/remeasurement` was left at its master markup in
  this slice because its browser-draft reload journey was sensitive to the
  MudBlazor render path; slice 3 migrated it (see 2.1).
- **State:** committed and pushed as `e66bc49`. No tenant operation or
  production effect.

### 2.1 MudBlazor Form Controls, Navigation & CSS (slice 3)

- **Lockfile health fixed:** the MudBlazor commit left
  `tests/AuditSphereOps.Api.Tests/packages.lock.json` and
  `tests/AuditSphereOps.E2E.Tests/packages.lock.json` stale (`NU1004` in locked
  mode). Both were regenerated with `dotnet restore --force-evaluate` on the
  repository-pinned SDK; locked-mode restore now passes with 0 errors and no
  unrelated package changed.
- **`CurrencyRemeasurement.razor` migrated:** MudBlazor shell (`PageHeader`,
  `MudLink`, `LoadingState`, `MudAlert`, `MudPaper`, `MudTable`, `MudButton`,
  `StatusChip`) with `MudTextField`/`MudNumericField` line fields whose inner
  inputs carry the forwarded `data-draft-field` attributes. The draft boundary
  section, its five selects and the date input stay native because
  `draft-state.js` reads GUID/bool/ISO values from `control.value` and the E2E
  journey asserts those exact values after reload. The draft-restore journey
  `CurrencyRemeasurementWorkbenchRestrictsContextAndRestoresBrowserDraft` passed
  9/9 explicit runs plus the full-suite runs (previously flaky).
- **Form controls migrated on 15 pages** with typed components
  (`MudTextField`, `MudNumericField<T>`, `MudSelect`, `MudCheckBox`); no native
  `<button>` remains anywhere. Two selects reverted back to native after the
  full suite exposed hard contracts (Playwright `SelectOptionAsync` on the
  package-review Stage select; `Locator("#decision-outcome")` visibility on the
  assessment decision page; raw-value `InputValueAsync` on the M365 capability
  selects) — documented in the migration document and in inline comments.
- **Accounting navigation standardized:** `AccountingNavigation.razor` and the
  `Consolidation`/`AccountingRecords` page tab rows now use a `MudPaper` bar of
  `aria-current`-carrying links (`.accounting-nav`); the broad `/app/accounting`
  prefix bug is fixed so only the correct entry is active.
- **CSS consolidated:** `.accounting-tabs`, `.button`, `.card-grid` and
  `button:disabled` removed; `.accounting-nav` rules added; no second CSS
  component framework. Shared components reviewed; MudBlazor remains Web-only
  (`ArchitectureGuardTests`).
- **State:** full Release suite green (counts in `status.json`), no EF model
  drift, no schema migration, tenant operation or production effect.

### 2.2 MudBlazor Migration Completion (slice 4)

- **Last two select exceptions closed at full assertion strength:** the
  `FinancialPackage` Stage select is `MudSelect`, driven by a new
  `SelectMudOptionAsync` journey helper (open the labelled select, click the
  exact option) choosing the same value with unchanged downstream assertions;
  the `AssessmentDecision` `#decision-outcome` id moved to the visible MudSelect
  wrapper `div`, so the partner visibility and unauthorized-manager absence
  checks needed no test change. The Microsoft 365 capability selects stay
  native permanently (raw-value `InputValueAsync` contract).
- **`draft-state.js` guard:** `<input type="hidden">` composite-widget internals
  (MudSelect combobox mirrors) are excluded from draft discovery and
  collection; visible draft-field semantics unchanged; `pbc-upload.js`
  byte-identical.
- **Route-render guarantee:** `RouteRenderSmokeTests` visits every
  parameterless inventoried route (19 staff routes plus the client portal)
  asserting heading render with zero page errors; detail routes keep their
  seeded journeys.
- **State:** Domain 338/338, Api 6/6, E2E 61/61, no EF model drift (counts in
  `status.json`); commit `dc43bc0` pushed; no tenant operation or production
  effect.

### 2.3 MudBlazor Migration Final Reconciliation (slice 5)

- **Verified counts recorded:** 42 page components declaring 49 routes,
  zero native `<button>`, 39 intentional native controls across 9 pages, and
  5 native tables (previously recorded as "47 routes / 6 tables" — corrected).
  See `docs/auditsphere-ui-mudblazor-conventions-migration-current.md` §8.
- **Last cleanup:** StatusChip now maps `POSTED`/`RECONCILED`/`ACCEPTED` to
  success and `RESUBMITTED` to info; the dead `.field-label`/`.context-bar`
  CSS rules were removed. `MainLayout`, `AuditSphereTheme`, `PageHeader`,
  `LoadingState`, `ScopeBanner` and `ConfirmDialog` reviewed and unchanged.
- **State:** Domain 338/338, Api 6/6, E2E 61/61, no EF model drift; commit
  `3d449fb` pushed; no tenant operation or production effect.

### 2.6 Stale-Route Audit Completion (engagement detail fix + four journeys)

- **Real leak fixed:** `EngagementDetail` loaded only during initialization, so a
  same-document route change to another engagement kept the previous engagement's
  details, client name and holds on screen without reauthorization. Loading now
  runs per parameter set with a per-parameter guard and clear step; an
  unauthorized engagement renders "Engagement unavailable" with all prior
  markers cleared.
- **Four new same-document journeys** record the reauthorization contract for the
  remaining detail screens (engagement detail, audit plan materiality, audit
  population, release candidate) using the established authorized-read →
  out-of-scope pushState → cleared-projection → return → document-token pattern.
- **Observed:** the four focused cases pass 4/4; the complete suite passes
  Domain 344/344, Api 7/7, E2E 79/79 with 0 skipped on the shared tree. Commit
  `81d4c1b` pushed to `master`. The parallel session's in-flight PBC work was
  left untouched and excluded from this commit.

### 2.7 Documentation Health Gate (CI)

- **Automated documentation health wired into CI:** a new `docs-health` job in
  `ci.yml` runs the Markdown health validator (canonical links, basenames,
  HISTORICAL_SOURCE banners) and the filename-policy validator on every push
  and pull request — pure stdlib Python, no .NET or database needed, completing
  the first "Automated Documentation Health" item from the pending-work
  inventory. The build job is unchanged; hosted test execution remains the
  retained blueprint.
- **Wording synced:** the README and testing-strategy CI-contract sentences now
  describe the gate as build plus documentation health checks; the pending-work
  inventory marks the item complete with the volatile-metrics guard remaining.
- **Observed:** both validators pass locally on the changed tree; the workflow
  parses with both jobs; no .NET suite rerun (workflow + docs change only).

### 2.8 Workpaper Revocation-Refresh (AS-PAR-002)

- **Reload affordance added:** the workpaper draft toolbar's "Reload current
  target" button was previously rendered only during draft conflicts; it is now
  always available, so a user (or the audit) can re-run the reauthorizing load at
  any time. `ReloadCurrentTargetAsync` already clears the protected projection
  and re-checks scope and grants on every use.
- **New journey:** `ReloadCurrentTargetClearsWorkpaperAfterGrantRevocation` —
  opens an authorized workpaper, revokes the Partner and Staff grants via
  `RoleAdministrationService.RevokeRoleGrantAsync`, clicks "Reload current
  target", and asserts the fail-closed "Workpaper unavailable" state with the
  title, index and work-performed markers fully cleared.
- **Observed:** the focused journey passes; the complete suite passes Domain
  348/348, Api 7/7, E2E 80/80 with 0 skipped on the shared tree. No business
  rule, authorization decision or persistence change; the reload path simply
  makes the existing fail-closed reauthorization reachable at any time.

### 2.9 Revocation-Refresh Completion (finding, audit plan, invoice detail)

- **Refresh affordances added to the last three screens:** "Refresh finding"
  (`RefreshFindingAsync`: clear + re-run the scope-and-grant-authorizing load),
  "Refresh plan" (`RefreshPlanAsync`: generation-guarded clear + reload through
  the same path the route parameters use) and "Refresh invoice"
  (`RefreshInvoiceAsync`: generation-guarded clear + role re-resolution +
  scoped load). All three handlers re-resolve the actor and re-run the
  authorization decision, so a revoked user gets the fail-cleared denial state.
- **Three new journeys:** `FindingRefreshClearsAfterGrantRevocation`,
  `AuditPlanRefreshClearsAfterGrantRevocation` and
  `InvoiceRefreshClearsAfterGrantRevocation` follow the established
  revoke-while-open pattern (revoke via
  `RoleAdministrationService.RevokeRoleGrantAsync`, click refresh, assert the
  denial heading with every projection cleared, zero page errors).
- **Observed:** the three focused journeys pass 3/3; complete suite Domain
  348/348, Api 7/7, E2E 82/83 on the shared tree — the single failure
  (`FinanceManagerScopedToAnotherClientCannotViewInvoiceOrFallbackBalance`)
  is the documented intermittent-under-load pattern and passes in isolation
  and together with both new invoice journeys. No business rule, authorization
  decision or persistence change.

### 2.10 Benchmark Baseline Verification

- **Provisional maximum-input samples:** The owner selected the documented
  input sizes for local measurements. New PostgreSQL-backed cases import a
  balanced 20,000-row trial balance through the guarded service and transfer
  one 8 MiB PBC chunk through the durable worker and simulation sink. Each
  case verifies final state, exact identity/digest or row count, and duplicate
  or single-attempt behavior. Both cases passed in three isolated runs; the
  full Release solution suite, Web build and EF model check passed. Timings and
  exact checks are in `status.json`. These samples do not establish concurrent
  workload capacity, live provider behavior, p95 latency or production sizing.
- **Provisional concurrent query sample:** Fifteen explicit local users issued
  thirty simultaneous first-page GL queries through separate PostgreSQL-backed
  contexts. Three isolated runs passed, and the full Release solution suite,
  Web build and EF model check passed. Exact measurements are in `status.json`.
  This measures application queries rather than browser sessions or sustained
  HTTP load; it does not close P8b.
- **Explicit focused run recorded:** AccountingBenchmarkTests passed on the
  local PostgreSQL profile, covering intake, durable outbox enqueue/worker
  publication and consolidated group readback. Concurrent worker-operation
  coverage remains in DurableOutboxTests. Exact measurements and the observed
  date are recorded in `status.json`.
- **State:** documentation/ledger change only; no production acceptance
  claimed for local-loopback measurements.

### 2.11 Acceptance-Criteria Reconciliation Phase 1 (VP-034)

- **New Domain test:** `ChartValidation_RejectsDuplicateCodesCyclesPostingParentsAndInvalidDateRanges`
  proves the chart/period guards named by VP-034-AC02: inverted period date
  ranges, duplicate account codes (within a batch and across batches), a
  posting account used as a chart parent, parent cycles, and published-chart
  immutability under mutation (`ProtectedState`).
- **Coverage ledger reconciled:** VP-034-AC01 and VP-034-AC02 moved to PASS
  with named test evidence (`ClientChartsPeriodsAndGlImport_AreTypedScopedAndClosedSafely`
  for the context-binding and sibling-scope denial); VP-034-AC03 records its
  partially evidenced legs and stays NOT_RUN pending a package-staleness case;
  VP-034-AC04 remains NOT_RUN (UI-level verification). 47 NOT_RUN rows remain.
- **Observed:** focused test passes; complete Domain suite 349/349 with 0
  skips; task-pack validate 0 errors.

### 2.4 Documentation Reality Audit

- **Scope:** descriptive/current Markdown reconciled against implementation and
  the authority order; historical sources, evidence, tracking ledgers and
  generated task state untouched.
- **Corrections:** README (GROUP scope restored to the data-flow diagram,
  MudBlazor 9.10.0 Web-only row in the technology baseline, four missing
  routes in the workbench directory, solution-tree fixes); system
  specification §4.1/NET19 (MudBlazor pinned 9.10.0 recorded as implemented
  instead of "verify during bootstrap"); current-architecture doc (MudBlazor
  Web-only baseline bullet); testing strategy + catalog (discovery counts
  refreshed to the observed 420: Domain 341, API 7, E2E 72, dated, with the
  build-gate-only CI contract stated next to the retained pipeline blueprint).
- **State:** markdown health PASS (124 files, 2354 links), naming policy PASS,
  task pack 0 errors; no .NET suite rerun (documentation-only).

### 2.5 AS-PAR-002 Stale-Route Reauthorization (workpaper, finding, invoice detail)

- **Three new same-document journeys** extend the route-parameter
  reauthorization audit to the remaining audit/practice detail screens:
  `WorkpaperClearsPriorWorkpaperWhenRouteChangesInPlace` (sibling-engagement
  workpaper never leaks; "Workpaper unavailable" with all markers cleared),
  `FindingClearsPriorFindingWhenRouteChangesInPlace` (same contract for the
  finding detail) and `InvoiceDetailClearsPriorInvoiceWhenRouteChangesInPlace`
  (an unavailable invoice renders the fail-closed "Access unavailable" state —
  the page clears the resolved actor with the invoice — with the invoice
  number and line description fully cleared).
- **No UI code changed:** all three pages already reauthorize on parameter
  change; the journeys record the verified behavior with the identical
  document-token and zero-page-error assertions used by the existing cases.
- **Observed:** the three focused cases pass 3/3 repeatedly; complete E2E
  project 74/75 on a shared busy tree with the single failure being the
  previously documented intermittent AccountingPeriod case, which passes in
  isolation; Domain 341/341 and Api 7/7 on the same tree. Commit `aaa7bad`
  pushed to `master`. Work-tree changes from the parallel consolidation-guard
  session were left untouched and excluded from this commit.


### 2.1 Documentation Standardization & AI Navigability Refactor



- **Standardized Filenames:** All non-root Markdown files standardized to `auditsphere-<area>-<document-type>-<subject>[-<id>][-<status>].md` with globally unique basenames. Root `README.md` and `AGENTS.md` preserved as conventional exceptions.



- **Capability-Focused Partials:** Services (`ConsolidationService`, `ClientAccountingService`, `AccountingAnalysisService`, `AuditFieldworkService`, `FinancialStatementService`), `AuditSphereDbContext`, and `ClientAccountingTests` decomposed into focused capability partial files with unchanged static APIs and test semantics.



- **Architectural Reference Guards:** `ArchitectureGuardTests.cs` enforces project reference layering (Domain references no outer project, Application references Domain only, Infrastructure references Application and Domain, never a host).



- **Documentation Policy & Automated Guards:** Established `docs/architecture/auditsphere-architecture-document-naming-policy.md`, `scripts/docs/validate-markdown-filenames.py`, and `MarkdownNamingGuardTests.cs`.



- **R2R Source Hashing Preserved:** `HISTORICAL_SOURCE` banners added and excluded from hash verification, preserving exact SHA-256 requirement hashes for R2R modules 20–26 and Audit workflow sources.







### 2.2 Re-Review Repairs & Currency Translation Completion (RR-01–RR-09, F01–F08)



- **Stable Reserve Line Identity (RR-01):** Derived cumulative translation reserve line IDs from immutable translation snapshots (`TranslationIdentities.CumulativeTranslationReserveLineId`), making replay, approval, and readback stable.



- **Fail-Closed FX Rates (RR-02, RR-03):** Removed missing-rate aggregate fallbacks; missing rate purposes return typed `gate.blocked` errors. Same functional and presentation currency translates as identity rate 1 without consuming observations.



- **Exact Line Rate Identity (RR-04):** Manifest rows carry their own line's consumed rate purpose and rate; classification resolves through recognized declared sections or approved mappings.



- **IAS 21 Rate Bridge (F01–F03):** `LineTranslationCalculator` computes translation reserve from equity and P&L rate differences with zero residual signed sum (`GOLD-R2R-07`), persisting `CTA_RESERVE` in consolidation balances.



- **Task Scope Reconciliation (RR-08, F06):** Reconciled T054 as R2R-only handover, moving audit handover `AS-AUD-028-AC10` to T075 with declared audit acceptance inputs.







---







## 3. Observed Local Verification State







All verifications are run against local PostgreSQL 18.6 on port 5433:







| Check | Expected Result | Authority Pointer |



|---|---|---|



| **Solution Build (Release)** | `0 Warning(s), 0 Error(s)` | `dotnet build src/AuditSphereOps.Web/AuditSphereOps.Web.csproj --configuration Release` |



| **PostgreSQL Test Suites** | All tests pass, 0 skips (Domain, Api, E2E Playwright) | Consult [`docs/execution/status.json`](status.json) for authoritative counts |



| **EF Model Drift Check** | No pending model changes | `dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web` |



| **R2R Task Pack Status** | `valid: true`, 0 errors, 75 tasks | `python3 docs/task_breakdown/tools/task_status.py validate` |



| **Task Pack Self-Tests** | 21/21 passed | `python3 docs/task_breakdown/tools/test_task_status.py` |



| **Markdown Filename Guard** | 100% compliant, 0 collisions | `python3 scripts/docs/validate-markdown-filenames.py` |



| **Git Diff Cleanliness** | 0 whitespace or formatting errors | `git diff --check` |







---







## 4. Known Blockers & External Boundaries







The following external gates require live cloud infrastructure or partner sign-off and cannot be closed in local development:







- **P1 (Entra OIDC Authentication):** `BLOCKED_EXTERNAL` — requires live Azure AD tenant app registration and client secret mount.



- **P2 (Selected-Resource SharePoint/Graph):** `BLOCKED_EXTERNAL` — requires live Microsoft 365 tenant with selected-resource application scopes.



- **P3 (External Release Checkpoint Store):** `BLOCKED_EXTERNAL` — requires remote cryptographic immutable storage.



- **P7 (Cross-Store Recovery):** `BLOCKED_EXTERNAL` — requires multi-region cloud backup infrastructure and tested RPO/RTO.



- **P8 (Production Secrets & Observability):** `BLOCKED_EXTERNAL` — requires production Key Vault and OpenTelemetry collector.



- **P9 (Independent Merge Review):** `BLOCKED_EXTERNAL` — requires partner sign-off.



- **P10 (Real-Tenant Acceptance §47):** `BLOCKED_EXTERNAL` — requires customer-authorized tenant execution.







---







## 5. What the Next Agent Should Know







1. **Read Order:** Always read [`AGENTS.md`](../../AGENTS.md) and [`docs/auditsphere-docs-index.md`](../auditsphere-docs-index.md) before performing work.



2. **Architecture Authority:** [`docs/architecture/auditsphere-architecture-current-architecture.md`](../architecture/auditsphere-architecture-current-architecture.md) defines the implemented architecture. Do not introduce MediatR, microservices, or new layers.



3. **Capability File Map:** Consult [`docs/architecture/auditsphere-architecture-code-map.md`](../architecture/auditsphere-architecture-code-map.md) to locate relevant partial classes, domain records, and test fixtures.



4. **Volatile Facts Rule:** Never add live test counts, migration counts, or commit hashes to narrative documentation. Record them only in [`docs/execution/status.json`](status.json).



5. **Next Work Packages:** Consult [`docs/execution/auditsphere-execution-pending-tasks.md`](auditsphere-execution-pending-tasks.md) for open local work:



   - Continue the AC-01–AC-28 and AS-PAR-002 route/parameter revocation review.



   - Formalize G16 period-end open-item methodology approval.



   - Maintain documentation health validation.







---







## 6. Standard Verification Commands







```bash



# 1. Build solution in Release



dotnet build src/AuditSphereOps.Web/AuditSphereOps.Web.csproj --no-restore --configuration Release







# 2. Check for EF Core model drift



dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web







# 3. Validate R2R task pack & pinned source hashes



python3 docs/task_breakdown/tools/task_status.py validate







# 4. Run task status self-tests



python3 docs/task_breakdown/tools/test_task_status.py







# 5. Validate markdown naming policy



python3 scripts/docs/validate-markdown-filenames.py







# 6. Run domain and architecture guard tests



dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --filter "FullyQualifiedName~ArchitectureGuardTests|FullyQualifiedName~MarkdownNamingGuardTests"



```

### Angular client and engagement overview extension

Added Application workspace projections, authorized HTTP contracts, lazy Angular detail routes and session-fenced reads. Engagement-only grants remain unable to read client-wide profiles or sibling engagements. Mutation panels remain pending migration; this is not full US-015 acceptance. Latest observed verification is recorded in status.json.

Angular commercial extension: proposal detail, independent commercial review, sent-state recording, client response, prospect conversion and reviewed proposal revisions now compose existing Application services. Full Angular migration and built-in-browser acceptance remain pending; observed checks are recorded in status.json.

Angular commercial intake extension: qualified leads now have bounded opportunity detail, reviewed discovery creation with a stable request identity, and initial proposal creation. Concurrent same-request creation and cross-firm read isolation were checked using PostgreSQL. Pricing/settings/documents/fee agreements remain pending; focused observed checks are in status.json.

Angular quotation extension: approved-rate hours, backend preview, versioned breakdowns and independent matrix approvals compose existing Application workflows. Reviewed rates/revisions are fenced under the existing firm lock; four-decimal factor validation aligns with the canonical idempotency hash. The targeted Playwright pricing/save/approval journey passed after its deferred-load wait was corrected. Full migration and built-in-browser acceptance remain pending; observed checks live in status.json.

Angular commercial document extension: generation and immutable artifact history/downloads are now available through the opt-in proposal route. Commands check the reviewed quotation and profile under the existing firm lock; letter generation retains professional acceptance, Partner and signature/seal guards. Focused PostgreSQL, API and Playwright checks passed, including the portfolio revocation regression after its search selector was scoped. Settings, fee agreements and complete migration/browser acceptance remain pending. See status.json for observed verification facts.

Angular fee-agreement extension: persisted milestone amounts, engagement linkage, finance-controlled invoice drafts and manual advance-payment commands now compose existing billing services from the proposal route. Concurrent links cannot overwrite; payment-reference replay rejects changed payment details. The commercial browser fixture needed a valid primary-contact email, preserving the existing conversion gate. Focused local checks passed; commercial settings, full migration and built-in-browser acceptance remain pending. See status.json for observed checks.

Angular commercial settings extension: versioned firm profile and confirmed matrix changes/deactivation now have opt-in Angular controls with server-side Administrator/Partner edit authority and reviewed-state fences. Focused unit, PostgreSQL, API and Playwright checks passed; existing document profile identities were retained, and EF reported no model drift. Full source parity and the wider migration remain pending. See status.json for observed facts.

Angular accounting read extension: opt-in Angular client search and profile/reporting-period views now compose bounded Application queries. Explicit client-wide authorization is required for client books; engagement-only grants are not widened. Angular build and contract tests passed. Setup mutations, period workbenches and accounting browser/security acceptance remain pending; observed verification is recorded in status.json.

Angular accounting profile extension: reviewed profile creation/revision now composes existing Application commands. Writes are serialized under the existing firm lock, revisions remain exact strings and unknown outcomes require a persisted-state refresh. Local builds and Angular tests passed; PostgreSQL/API/browser verification and the wider accounting migration remain pending. See status.json.

Accounting profile verification extension: PostgreSQL checks confirmed client/sibling isolation, engagement-only denial, stale-session refusal and one winning concurrent reviewed revision. The authenticated API contract check also covered accounting read denials and a profile write without CSRF. Built-in browser acceptance and remaining accounting workflows are pending; exact observed results live in status.json.

Angular reporting-period creation extension: explicit review, ISO date contracts and existing client-scoped creation now have Angular controls. Concurrent duplicate creation is serialized; PostgreSQL checks confirmed one period and refusal of a foreign prior-period link. Opening-balance evidence is not fabricated by setup. Builds and Angular contract checks passed; book setup, lifecycle workbenches and browser acceptance remain pending. See status.json.

Angular reporting-book extension: bounded book metadata and reviewed creation now compose the existing Application service. The selected period is locked before closed-state/duplicate checks. Focused PostgreSQL verification covered concurrent duplicates and closed-period refusal; local builds and Angular checks passed. Remaining accounting workbenches and built-in browser acceptance are pending. See status.json.

Angular accounting journey: synthetic PostgreSQL-backed Playwright setup created a reviewed profile, reporting period and reporting book, then verified persisted reload. The initial selector-label failure was corrected with an explicit period label; the rerun passed without page errors. This does not replace built-in browser acceptance or complete accounting parity. See status.json.

Accounting lifecycle backend extension: reviewed close/reopen contracts bind the client and exact period revision under the existing period lock. Readiness rechecks authorization; source close gates and reopen amendment evidence remain authoritative. Focused PostgreSQL checks and Release build passed. Angular lifecycle controls and browser acceptance remain pending; see status.json.

Angular period lifecycle UI extension: close readiness and decision authority are shown separately; closing/reopening requires explicit review and a reason. The synthetic PostgreSQL-backed Playwright journey passed through setup, close and reopen and verified amendment evidence. Builds and Angular contract tests passed. Built-in browser acceptance and remaining accounting workflows are pending; see status.json.

Angular amendment-history extension: authorized bounded reopen history now shows exact previous/current revisions, reason, actor identity and UTC timestamp. PostgreSQL projection and synthetic Playwright lineage assertions passed alongside builds and Angular checks. Roll-forward, restatements and full migration/browser acceptance remain pending; observed results live in status.json.

Angular roll-forward extension: reviewed external-evidence setup now sends exact monetary/date strings and the closed prior-period revision to the existing Application workflow. PostgreSQL checks preserved draft book copy, unapproved opening evidence and duplicate refusal, and verified stale revision denial. Builds and Angular checks passed. Validated source-package selection, bridge history/review and browser acceptance remain pending. See status.json.

Angular opening-balance extension: scoped bounded bridge history now displays exact amounts, residual, source hash, evidence and approval metadata. Reviewed approval locks the bridge and verifies client/source identity with current reviewer authority. Focused PostgreSQL checks passed along with builds and Angular contracts. Validated source-package selection and browser approval/roll-forward journeys remain pending; see status.json.

Accounting roll-forward browser extension: the synthetic Playwright journey now passes exact decimal opening amounts, closed-period roll-forward, draft book copy and reviewed bridge approval with persisted approver identity, followed by reopen and amendment history. This is automated local evidence; validated source-package selection and full migration/built-in browser acceptance remain pending. See status.json.

Angular source-package selector extension: bounded matching validated packages can now be loaded for the exact closed client period/currency. Selection supplies the persisted hash and retains the reviewed prior revision fence. Local builds, Angular tests and focused PostgreSQL/Playwright checks passed for empty matching sources, wrong-client refusal and external-evidence roll-forward. Positive package selection and full migration/built-in browser acceptance remain pending; see status.json.

Positive roll-forward source coverage: PostgreSQL checks selected the exact validated package, excluded review-required and other period/currency/client packages, refused a changed source hash and verified the persisted bridge source identity. Angular source decoding now has direct revision/currency tests. An invalid fixture status/date was corrected to the real catalogue. Production UI build and focused checks passed; positive browser selection and full migration acceptance remain pending. See status.json.

Positive Angular source-package journey: the existing local Application package fixture produced a validated package, which the synthetic Playwright journey selected for roll-forward and verified by exact persisted bridge identity/hash after reviewed approval. The journey passed; full accounting parity/migration and built-in browser acceptance remain pending. See status.json.

Angular chart read extension: scoped bounded revision metadata and deferred account hierarchy reads now compose Application queries. Account paging executes in PostgreSQL rather than materializing the entire chart; exact-client linkage, page bounds and final current authorization are checked. Builds, Angular contracts, PostgreSQL hierarchy assertions and the synthetic accounting Playwright journey passed. Chart mutation migration and full migration/built-in browser acceptance remain pending; see status.json.

Angular chart draft-write extension: reviewed creation and account entry now compose existing locked Application commands with latest/chart-version fences and exact client linkage. Published charts remain immutable. Builds, Angular checks, focused PostgreSQL locking/scope tests and the synthetic accounting browser journey passed. Publication, aliases/dimensions and full migration/built-in browser acceptance remain pending; see status.json.

Angular chart publication extension: an independent reviewer confirms a bounded full account-set digest; the existing locked command checks client/version/digest and preserves hierarchy validation and preparer separation. Builds, Angular contracts, PostgreSQL stale-snapshot/locking assertions and the synthetic reviewer Playwright journey passed with persisted publisher identity. Aliases/dimensions and full migration/built-in browser acceptance remain pending; see status.json.

Chart alias publication fence: the reviewed chart digest now includes source-account aliases. Alias writes accept exact client/version fences under the chart lock. The focused PostgreSQL workspace test passed, including wrong-client refusal and stale publication after alias changes. Alias UI, dimensions, full migration and built-in browser acceptance remain pending; see status.json.

## Angular client and engagement workspace administration

Added bounded administrator workspace pages and exact target/resource/template reviews to the standalone API and Angular tenant screen. Provisioning requires a reason, current authority and the reviewed fingerprint; publication is refused after configuration or session changes. Client folder readiness, disposable PBC verification and dedicated client-site membership remain separate states. The latter is read-only and retains the explicit whole-site Full Control warning and isolated privileged worker boundary. Worker receipts bind their original provider plan and refuse folder ownership collisions; reconciliation remains get-or-create. No permission or credential boundary was widened.

Verification results and the remaining migration gates are recorded in `status.json` after observation. This slice does not claim final Blazor retirement or live Microsoft acceptance.

## Native Angular confirmation lifecycle

The confirmation register and exact case-evidence reviews now have a dedicated Angular route, linked from fieldwork and completion. Local commands preserve scope, current session, reviewed revisions, independent review and frozen-file gates. Preparation never means delivery; only observed dispatch evidence starts follow-up monitoring. Current response and alternative revisions remain separate. Closure rejects nonresponse without reviewed alternative work, and late substantive responses require new independent review. Exact supported amount strings are preserved; unsupported storage precision fails validation rather than silently rounding. Unknown writes remain fenced until persisted refresh and fresh review.

Executed evidence and corrections are recorded in `status.json`. This slice does not establish complete migration acceptance, actual external dispatch or final Blazor retirement.

### Native confirmation batch preparation

The Angular confirmation workspace now prepares reviewed batches through the standalone API. Every case has an exact supported amount, source identity, respondent and validated contact source; area, currency, date and optional procedure are reviewed together. Any edit clears confirmation. Application serializes preparation with current scope/epoch/register checks and the frozen-file guard, rejects existing source/date identities and publishes all cases in one transaction. An invalid or duplicate case refuses the whole batch. Unknown outcomes fence repeat writes until persisted state is reviewed. These are local prepared cases; no Microsoft effect or confirmation dispatch is inferred. Closure-conclusion evidence is now retained as described below; full scoped draft restoration remains pending. Exact executed verification is recorded in `status.json`.

Responsive confirmation forms no longer impose an intrinsic minimum width on the mobile shell. The single-column grid and main content can shrink; wide registers retain contained horizontal scrolling. Expanded batch forms remain usable at narrow widths.

### Confirmation closure evidence retention

The source confirmation lifecycle now retains each new human closure decision with its actor, conclusion, timestamp and exact reviewed evidence snapshot. PostgreSQL refuses closure-row updates and deletion. Native Angular displays the retained conclusion and digest; historical closures with no record are labelled explicitly. Case locking and deterministic current-alternative ordering prevent stale or repeated closure decisions. The additive migration is exercised only in owned synthetic test databases; shared Development and production migration application remain operator-controlled. Scoped draft recovery and the broader migration acceptance remain open. Executed verification is recorded in `status.json`.

Confirmation draft recovery extension: case, batch and action forms now use versioned explicit tab drafts, exact identity/session/entity/current-base checks, expiration and bounded allowlists. Recovery clears review; pending submission intent requires persisted-state refresh. Navigation/context changes offer keep, save and continue, or discard, with honest memory-only fallback when storage fails. Background revision changes retain edits while blocking stale submission. This leaves the general draft primitive and remaining module/story acceptance open. Observed verification and browser evidence live in status.json.

## Native Angular journal revision lifecycle

Journal detail now provides exact source/context review, focused balanced-line editing, fresh
server preview and assent, submission/return, independent technical posting, linked reversal,
and read-only retained before/after evidence. Application owns the scoped serialized transaction,
final authority/period/freeze checks and immutable actor/request receipts. Unknown responses fence
new actions until receipt reconciliation; tab recovery excludes assent. Posted lines remain frozen,
including attempted moves to an editable journal. The existing returned-edit service and database
guard now agree. This does not apply a treatment to client books or a package.

Observed local checks and original failures are recorded in `status.json`. The additive migration
has been exercised in disposable test databases only. Native creation is described below;
management responses, reflection/application controls and wider migration acceptance remain pending. The prior frozen
mapping regression's historical-schema failure remains distinct from its verified fixture repair.

### Native journal creation from a sealed source

Source inspection now enters a dedicated Angular draft-creation page. Exact source/period/book
and purpose/origin are reviewed with all decimal lines, rationale and evidence. Application
serializes creation with the existing service and a retained original-line event. Identical
requests reconcile; duplicate numbers, changed prior-journal revisions and late authority loss
cannot publish another draft. Corrections can link an exact same-context posted/returned
journal while retaining it unchanged. Unknown responses require receipt reconciliation and
acknowledgment; tab fields never restore assent. The additive event migration is exercised only
in disposable databases. Executed checks are recorded in `status.json`; management responses,
reflection/application and full migration acceptance remain open.


### Native journal management responses

The owned Angular slice adds a bounded scoped client queue, exact management review and separate
staff offline evidence. Application derives evidence mode, fences exact current source/journal
context and commits the decision with its immutable actor-owned receipt. Database guards freeze
reviewed lines/context and retain client identity separately from offline staff recording.
Unknown acknowledgment recovery requires an explicit scoped receipt check and acknowledgment;
client fields remain in memory and staff tab recovery excludes assent. Technical posting and
source/package application remain separate. Final isolated backend, Angular, affected Domain/browser,
model-drift and built-in browser gates passed as recorded in `status.json`; wider migration remains open. The completed frozen journal
regression has terminal acceptance with unchanged source/artifacts. Creation regression remains
independent and failed on retained legacy browser timeouts; its corrected affected cohort is a
separate pass. Overlapping source-reflection edits were preserved outside this owned worktree.


### Native adjustment-plan eligibility review

The next owned slice adds a bounded native queue and read-only plan review. It keeps retained
calculation evidence separate from current applicability and reports the exact planned/current
source-reflection states. The existing eligibility report now agrees with finalization when
reflection changes or disappears and rejects ambiguous, group-only or oversized membership.
Scoped source parents, current authority and supporting-context double reads protect results.
The initial build and queue/fixture failures were corrected before final acceptance. The
queue filters held engagements and preserves per-plan professional checks. Targeted final-source
backend, Angular, affected Domain/browser, model and built-in browser checks passed. The full
API regression completed with unchanged frozen source/artifacts. Observed counts,
failures, artifact fingerprints and publication are recorded only in `status.json`.
Native plan creation/finalization is described below; wider migration acceptance remains pending.


### Native reviewed adjustment-plan creation and finalization

Exact source inspection now enters bounded posted-revision selection and a fresh server preview.
Creation retains reviewed membership and actor-owned request evidence together. Finalization
validates exact accounts/context/reflection, previews six-decimal totals and a canonical hash,
then preserves the calculation and immutable event atomically. Source books and package release
remain separate. Database guards refuse retained membership/context changes and evidence deletion.
Native legacy-finalize bypass is refused through a typed Application result. Tab fields exclude
assent; unknown outcomes require persisted receipt verification and acknowledgment.

Angular, focused PostgreSQL API, affected Domain, model and built-in browser checks passed.
The corrected final affected browser cohort and retained-evidence rollback/reapply checks passed.
The broader final API regression remains active; its prior implementation pass is recorded
separately. The migration was exercised only in disposable owned databases. Exact terminal
results, initial failures and unchanged artifact fingerprints are recorded in `status.json`.

### Native administration access operations and cross-module parity audit (ZCode lane)

While the parallel Codex session continued the accounting chain, six read-only
source-action parity audits covered the Codex-disjoint modules: practice/finance,
administration/Microsoft365, audit/library, assurance/completion, commercial/CRM
and operations/records/consolidation/portal. Verdict: zero missing user-invokable
commands anywhere — every retired-host action already has a native component
method, same-origin endpoint and Application service, and several native flows
(digest-verified access review, quotation concurrency, preparer/reviewer
segregation) exceed the retired host.

The owned slice restored the administration capabilities that existed only in
Application: reviewed assignment now optionally records the atomic copy-link
invitation intent (the preview digest is re-verified server-side against a fresh
preview before ApplyRoleGrantAndInvitationAsync), invitations are retrieved for
copying through a scope-rechecked read and copying is recorded, an
approved-roster/verified-sign-in identity binding fallback covers deployments
without a directory reader, and the access workspace now carries per-grant change
evidence plus each user's copyable invitation id.

Smaller parity repairs landed with it: legacy assessment deep links accept
EngagementLeader and Auditor; the partner decision flow differentiates stale
generation (form cleared, re-review required) from denied scope and prefills the
service route; the finding response form prefills the recorded response; the
workpaper autosave debounce matches the retired host; the invoice footer totals
the rendered lines with exact-decimal arithmetic; the portal setup-pending panel
only renders with zero authorized engagements; the Angular-mode access-denied
page is a friendly no-store HTML page with a sign-out action instead of raw JSON;
and the practice time, firm books and lead creation forms persist identity-scoped
field drafts with allowlist validation that never stores assent.

Remaining from the audits (recorded in status.json): administration pre-dispatch
form recovery and setup capability-state fields, commercial proposal read-model
enrichment and wider form draft hooks, receipts/credit notes (Application-only in
both hosts, parity-neutral), curated parity ledger files for the audited modules,
and the whole-migration acceptance gates that stay owner-gated.

### Proposal read-model parity and performance budgets

The commercial lane continues without touching the parallel assessment work.
The proposal workspace restores every read-model fact the retired host showed
(commercial owner, author, reviewer, approval/delivery/response timestamps,
supersedes lineage and per-revision sent/response columns) through one bounded
name lookup, and the leads list regains its summary metrics, recorded dates and
refresh control. The proposal decoder contract was extended at equal strength.

US-043 now has a local production-like budget journey: cold signed-in first
paint, warm lazy-route load, client-side router navigation and a 60-lead
dataset render against deliberately loose ceilings. The observed run recorded
1500ms cold, 32ms warm, 66ms router and 96ms large-dataset, all inside budget;
exact values live only in status.json.

The Angular conventions and safe AI-assisted implementation document is now
CURRENT under docs/architecture, and the curated cross-module parity ledger
(28 retired-host sources, 25 curated source-action verdicts) landed under
docs/execution as the US-044 record for the audited non-accounting modules.

### All-module accessibility sweep

The US-042 lane gains a pinned sweep journey: one staff identity walks a
representative route from every native module and each rendered state must
keep its document language, a single visible h1, named controls, labelled
fields, captioned tables and WCAG AA contrast against the effective
background, with an Escape-closable search overlay and a German-locale
context proving locale-dependent rendering. The first run surfaced exactly
one finding class - captionless tables on the practice time page, fixed for
both the staff list and the approver queue - and the sweep now passes across
all twenty routes with zero page errors. Real screen-reader walk-throughs and
a wider locale matrix remain open before the whole-migration acceptance gate.

### Deep accessibility and locale acceptance

Beyond the DOM sweep, the deep acceptance journey pins what assistive
technology and throttled readers depend on: landmark structure, a
non-skipping heading hierarchy, the skip link as the first tab stop handing
focus to main, live-region announcement of command outcomes, and the
administration dialog matrix - focus enters, Escape closes, focus returns to
the trigger, and history disclosures toggle an honest aria-expanded state.
The locale matrix signs in and renders under fr-FR and RTL ar-EG while staying
honestly LTR-declared, and a constrained-network budget loads a cold lazy
route at 4890ms observed against a 25s ceiling. A human screen-reader
walk-through and production acceptance remain the open gates.


### Reviewed native analytical preparation (US-025)

At code commit `d2fce44`, the engagement workflow now opens a native Angular analytical-preparation form. It loads only authorized reporting periods, fences the command to a reviewed period basis, accepts exact six-decimal practitioner inputs, previews the variance server-side, and requires explicit assent before retaining the analysis. A zero prior amount stays explicitly insufficient rather than inventing a ratio.

The Application command writes the existing analytical-review record and an immutable actor-owned idempotency receipt in one PostgreSQL transaction. The additive migration binds the receipt to the exact actor, period and analytical row, freezes preparation inputs after receipt, permits separate human-review fields to change, and refuses destructive rollback while receipts exist. The browser journey drops the successful response and confirms the saved receipt after reload without duplicating the analysis.

Focused verification and commit attribution are recorded in `status.json`. The migration ran only in owned synthetic PostgreSQL databases. The Development/production databases were not migrated, and the full solution suite was not rerun at this commit. US-025 remains partial: new reconciliation and specialist preparation/revision forms are still open, along with wider story acceptance and external migration gates.

### Client portal PBC upload recovery (US-013)

At code commit `64e83a3`, an interrupted client upload can be resumed after the client reselects the exact file. The authenticated API verifies current participant/write authority, filename, byte count and SHA-256, then rotates a one-transfer capability and returns the verified contiguous byte offset and next chunk index in a no-store response. Only the capability hash is persisted, and chunk authorization is rechecked after locking the upload row. The interrupted-acknowledgement browser journey confirmed the exact original bytes resume into two unique chunks with no duplicate upload intent. The normal client portal upload regression also passed.

Focused evidence and the 505.46 kB Angular initial-bundle warning are recorded in `status.json`. API contracts passed 2/2, the two portal browser journeys passed 2/2 on isolated PostgreSQL test data, Angular CI passed 475/475 on the shared checkout, and EF reported no pending model changes on the isolated source snapshot. The shared-tree API test setup encountered the other agent's uncommitted EF model drift; the same focused tests passed from the isolated snapshot. No Development or production database was changed. Overall upload parity, the full solution regression and production acceptance remain open.
