# AuditSphereOps — Current State & Active Slice Handoff

**Status:** CURRENT

**Purpose:** Compact, authoritative handoff of the active implementation slice, recent verified changes, local environment state, and next actions.

**Authority:** Active execution handoff document. Volatile metrics (exact test counts, migration count, verified commit SHA, CI run IDs) belong exclusively to [`docs/execution/status.json`](status.json).

**Audience:** AI coding agents and human developers.

**Chronology:** Sections are newest first. An older section describes the state
when it was written; where a later section supersedes it, the later section is
current. The top-level `verifiedCommit` in `status.json` names the last commit on
which the full suite was run; per-slice records carry their own evidence.

> **Archive:** earlier slice sections, moved verbatim on 2026-10-08, are in [`auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md`](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md). They are history, not current authority; the ledger is [`status.json`](status.json).

## Current verification handoff — 2026-10-10

Legal/FSLI implementation and current-tree STE journeys passed local verification. See the [STE verification tracker](auditsphere-execution-tracker-ste-v21-gap-verification-current.md) and `status.json`. T003 professional sign-off, T014 dependencies and live Microsoft/provider acceptance remain open.

## Hostinger M365 portal follow-up and verified SharePoint target — 2026-10-10

The user authorized the dedicated Graph permission plan and asked to use the existing SharePoint
site intended for acceptance. The Entra browser showed the Directory Reader `User.Read.All` and
Mail `Mail.Send` application permissions granted for the tenant. A dedicated Guest Invitation app
was created with `User.Invite.All` as its only application role (plus delegated `User.Read`), and
the portal confirmed tenant admin consent. These grants are not capability verification in
AuditSphere.

The separate Group Membership registration was prepared as single-tenant, but left at the Register
screen because it states that proceeding agrees to Microsoft Platform Policies. The user must
perform that legal assent. The old Administration app still has `User.Create`, `User.Invite.All`
and `GroupMember.ReadWrite.All`; it remains disabled until the new Group Membership app has been
registered, configured with only its approved application role, and consented. Remove the two
extra roles only after that replacement is verified.

SharePoint Admin Center and read-only site/library requests confirmed an existing synthetic
development site and an empty workpapers library root. A separate same-tenant site can serve as the
negative control and must not receive an app grant. No site-level grant, test upload, or new site
creation occurred. The user authorized a write grant only to the selected site and one disposable
file create/read/delete probe, including a denied request to the control site; execution is pending
the missing server credential/configuration.

The AuditSphere browser page was refreshed after these portal changes. It showed Consent Required,
no consent attempt and directory `NOT_VERIFIED`; all mutation capabilities were Disabled. The
selected-resource form was then saved with the existing site, empty workpapers library and App
mediated profile. AuditSphere reported draft revision 4 in `VALIDATING` and a latest selected-site
result of `BLOCKED_EXTERNAL` (`selected-resource-draft-edited`); this is not a verified site grant.
Mail and Records drafts were Not Configured, and no client workspaces existed. The initial VPS check
found no M365 configuration or certificate files. A later, explicitly authorized setup created a
dedicated Selected Site certificate on the VPS, uploaded only its public certificate to the matching
Entra app, and wrote that app ID and the unrelated control-site URL to the protected deployment
`.env`. No values or certificate material are recorded here.

The tenant page also rendered `BLOCKED_EXTERNAL: configure the separately approved consent identity
and fixed callback`, with no Connect action. The nonce-bound consent handshake has not started. The
consent app ID, certificate and enablement remain absent from the deployment configuration. The
Selected Site flag remains off, and no M365 capability is enabled. A read-only inspection of the
running API container found no `/run/auditsphere/m365` mount and no Selected Site app ID or
certificate-path environment setting. The checked-in Compose file declares the expected mount and
settings, but the running container is not using that configuration. Services were left untouched as
instructed; a separately approved API-container recreation from the current Compose configuration
is required before the certificate can be used.

The exact site grant and file probe remain pending. Microsoft Graph's site-permission creation API
requires `Sites.FullControl.All`; that tenant-wide permission was not added to any app. Use an
approved administrator grant path without broadening the Selected Site app. No site permission or
file was changed. The unrelated control site received no grant.

**Still pending:** user Register assent for the group app; a separately approved API-container
recreation from the current Compose configuration, with the Selected Site flag off; the nonce-bound
AuditSphere tenant-consent handshake; the exact-site `write` grant and
positive-target/negative-control file probe; the dedicated consent app and its certificate-backed
configuration; approved Exchange sender restriction and synthetic recipient for mail; and fresh
persisted AuditSphere verification before any capability is enabled. Do not place IDs, account names,
secrets or certificate material in repository docs.

Verification in this documentation slice: current built-in-browser views of Entra, SharePoint Admin
Center, the selected-site API response and AuditSphere tenant-connection page; read-only VPS
container inspection; GitHub Actions run #1242 for commit `ab322af6` passed Angular, .NET Release
and Docs Health. No site permission, live capability probe, disposable file, mail send or service
restart was performed.

> **Archived handoff history:** superseded 2026-10-09 evidence and implementation notes are preserved in the [historical slice archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md#ste-v2-1-evidence-mapping-and-full-suite-runs--2026-10-09).

## Task board, codebase audit and docs consolidation — 2026-10-08

A static audit of the 98 tracked task items was run at commit `13407156`: 74 R2R and AUD cards (T002 to T075), 12 STE-NXT stories and 10 STE-GAP stories; T001 is COMPLETED and was not re-audited. Each claimed COMPLETE was re-read by an independent reviewer. No item is COMPLETE: 70 PARTIAL, 13 CONFLICTS_WITH_AGENTS, 6 OWNER_DECISION, 5 EXTERNAL_BLOCKED and 2 NOT_STARTED. Six STE stories first claimed COMPLETE were downgraded (STE-NXT-002, 004, 005, 006, 007, 008). The audit read code and did not run tests.

The audit found that STE-NXT-008 was closed in error: the AR Test and Audit Workprogram links on the split dashboard pass context that the analytical preparation and fieldwork screens do not read. STE-NXT-013 is opened for it, and the gap tracker row 4.3.1 is now PARTIAL.

Consolidation: the four requirement documents moved to `docs/requirements/`, and every link and validator path was updated. The agent task board, [`docs/execution/auditsphere-execution-index-task-board-current.md`](auditsphere-execution-index-task-board-current.md), is generated by `scripts/docs/build-task-board.py` from the card front matter, the STE story headings and the audit record [`auditsphere-execution-report-task-codebase-audit-current.json`](auditsphere-execution-report-task-codebase-audit-current.json). `python3 scripts/docs/build-task-board.py --check` reports whether it is current.

Verified in this slice: the three docs validators pass (188 Markdown files); `task_status.py validate` reports no errors; the board check reports current. Not run: the .NET and Angular suites.

Next action on the critical path: the owner decides the T002 variation. Card step 3 and several downstream contracts require MediatR and bUnit, which AGENTS.md section 5 bans. Until T002 is COMPLETED, the helper keeps every later R2R card NOT_STARTED.

## Native client opening-balance maker/reviewer journey — 2026-10-07

The native accounting workspace now loads and displays the immutable opening
manifest for a selected client period, lets a preparer enter balanced approved
chart account codes with source reference/SHA-256, and offers a separate
reviewer an exact-manifest approval action. The UI prevents self-approval and
stale/closed-period approval. Approved openings appear in Trial Balance opening
columns separately from current-period movements. Angular accounting tests
passed 21/21, the production UI build passed with the existing Commercial
Settings stylesheet budget warning, and the PostgreSQL-backed API-hosted
Playwright journey passed 1/1 across maker, reviewer, retained approval and the
Trial Balance (125 opening debit/credit; zero period movement).

Evidence files are not uploaded by this workflow. Supporting opening AR/AP
invoice-level detail and reconciliation, prior-period carry-forward, correction
revisions, wider role/scope and recovery matrices, full solution regression and
production acceptance remain open. VAT/tax and ancillary capabilities remain
optional. Volatile results are recorded under
`verification.clientOperationalOpeningBalances` in `status.json`.

## Native client opening-balance snapshots — 2026-10-07

A client-period opening-balance API now records a balanced, chart-bound opening
manifest with an external evidence reference and SHA-256 metadata. Creation is
fenced to the current open period revision; a separate user must approve it.
Unapproved snapshots block the native GL/TB, and approved opening balances are
reported separately from period movements. PostgreSQL checks covered stale
revision, invalid balance, duplicate opening, self-approval, duplicate approval
and immutable-content rejection. The focused Domain test passed 1/1, API Release
build passed with zero compiler warnings/errors, OpenAPI contract tests passed
5/5, and EF reports no pending model changes. The OpenAPI artifact was refreshed.

The Angular maker/reviewer workflow has since passed a focused browser journey; see the newer handoff section above. Evidence bytes are not ingested. Supporting AR/AP detail, prior-period carry-forward, broader role/scope and recovery matrices, full solution regression and production acceptance remain open. VAT/tax and ancillary capabilities remain optional and do not block core bookkeeping.

## Angular materiality stale-source handling — 2026-10-07

The materiality journey now replaces the approved mapping after independent
approval and refreshes the audit plan. The UI marks the prior calculation stale,
explains that it no longer supports difference evaluation, removes the approval
action and omits FSLI risk stratification rather than applying old thresholds to
the replacement mapping. The PostgreSQL-backed Angular Playwright journey passed
1/1. The source/action row remains partial for calculation failure/recovery and
the broader materiality role matrix; the full solution and EF model-drift checks
were not rerun.

## Angular materiality policy refusals — 2026-10-07

The audit-plan browser journey now submits a revenue rate above its configured
policy limit, TE below 50%, and SAD below 3% through the Angular form. Each
request displays the corresponding policy error, and PostgreSQL confirms that
the refused attempts created no materiality assessment or calculation. The
journey then restores valid inputs and completes the existing successful
calculation and independent Partner approval flow. The PostgreSQL-backed
Playwright journey passed 1/1. The MaterialityEnginePanel source/action row
remains partial for calculation failure/recovery and broader
role-matrix evidence; full solution and EF checks were not rerun.

## Angular risk-routing independent review — 2026-10-07

Risk-routing projections now include the current assessment author. Angular hides
the Partner-clearance action when the signed-in Partner assessed that same risk;
the Application command continues to reject direct self-clear attempts. A
PostgreSQL-backed API-host Playwright journey passed with the fresh production
Angular bundle, checking both the hidden control and HTTP 403 denial. The Angular
suite passed 572/572, the Release solution build completed with zero warnings or
errors, and the Angular production build passed with the existing Commercial
Settings stylesheet budget warning. The source/action row remains partial for
its remaining role/scope, stale/revocation, recovery, accessibility and locale
gaps. Full solution regression, current EF drift and manual assistive-technology
acceptance were not part of this slice.

## Records Archive bounded manifest paging — focused verification passed — 2026-10-07

Archive reads now request no more than 100 ordered manifest rows and return a
total count plus an ordinal cursor. The Angular detail page appends subsequent
pages on demand, and a PostgreSQL browser journey covers a 205-entry
100/100/5 sequence with ordering and duplicate checks. The focused records
contract suite passed 6/6, including a transient next-page error followed by
an explicit successful retry that preserves then appends rows. Angular
template compilation passed with
`npx ngc -p tsconfig.app.json --noEmit`. The production build passed when run
outside the restricted sandbox; its initial bundle is 349.89 kB raw / 94.09 kB
estimated transfer. The commercial settings stylesheet remains above its
component warning budget (7.53 kB vs 4 kB). The focused PostgreSQL Playwright
journey passed 2/2, including the 205-entry 100/100/5 sequence, ordered rows,
duplicate checks and empty/incomplete-manifest presentation. Its first run
exposed an ambiguous test selector because the page has two status messages;
the assertion now targets the archive summary explicitly. The separate API
journey passed 1/1, verifying exact cursors, safe rejection of a negative
cursor, and identical foreign-versus-guessed-ID denials. The built-in browser
tab could not reach localhost:5105, so that manual visual check remains open.
The Records Archive source row remains partial and the migration gate remains
`NOT_READY`.

The full Angular CI suite also passed 557/557 tests across 108 files against
the current shared working tree, including the parallel finance UI edits. The
result is attributed to master `0c4391ba` plus those uncommitted working-tree
changes, not a clean commit. A production-build retry inside the restricted
sandbox reproduced the esbuild deadlock (exit 134); the earlier outside-sandbox
build remains the latest bundle pass. After the Records retry test was pushed
as `0833f8a2`, an outside-sandbox production build passed again and the focused
PostgreSQL Playwright archive cohort passed 2/2 against the fresh bundle. The
full PostgreSQL-backed solution and EF model drift checks were not rerun.

## Shared confirmation cancellation and validation — a27c3102

The Users & Access browser journey now verifies that closing the revocation
dialog leaves the local grant active and that revocation remains disabled until
the administrator enters a valid reason and explicitly confirms the reviewed
grant. It then completes revocation, verifies immutable evidence, and checks
that an already-open protected staff session is invalidated. The Practice Leads
journey verifies that canceling lead qualification leaves the lead in `NEW`,
then reopens the confirmation and completes qualification without implying
client acceptance or access.

Focused Release API-host Playwright journeys passed **1/1** each against owned
isolated PostgreSQL databases: revocation in 47 seconds and lead qualification
in 38 seconds. The built-in browser could not reach `localhost:5099` because the
host refused the connection, so the interactive Development session was not
available for these checks. The shared confirmation source row remains
`PARTIAL`: complete role/scope/expiry/cross-firm and unknown-result coverage
across both callers, human screen-reader and wider-locale acceptance, and the
broader retirement gates remain open. The full solution suite, Angular
production build and EF drift check were not rerun for these test-only slices.
Evidence is recorded in
[`auditsphere-migration-blazor-confirm-dialog-source-review.md`](../migration/auditsphere-migration-blazor-confirm-dialog-source-review.md)
and `status.json`.

## Records Archive incomplete-manifest state — 21e85a17

Added a PostgreSQL-backed Angular browser journey for an empty manifest marked
`INCOMPLETE`. It verifies the persisted reason, zero-entry empty state,
profile/version and digest, and distinct `Not observed` / `Not requested`
records evidence; no complete-archive claim is shown. The focused Release E2E
journey passed 1/1 in 33 seconds with no page or console errors. This closes
empty/incomplete presentation evidence only; large-manifest and failure/recovery
coverage, authorization matrices, human accessibility/locale and production
gates remain open. The full solution and EF checks were not rerun. Evidence is
recorded in `status.json` and the Records Archive/Release source review.

## Workpaper autosave and discard decision race — 6d1f91ad

A browser journey exposed that a pending Workpaper autosave could fire while
the unsaved-edits dialog was open, persisting content before a discard choice.
The navigation guard now cancels the debounce before asking the user. The
Workpaper Angular unit suite passed 13/13 and the focused PostgreSQL/API-host
Playwright class passed 4/4 in 1 minute 19 seconds. A simulated 409 discard
refusal leaves the route and active revision-1 server draft unchanged; typed
edits are not persisted. Direct discard and discard-before-navigation also
pass. The built-in browser rejected the local URL under its security policy,
so no fallback navigation was attempted; authenticated behavior was verified
in the owned Playwright sessions. Production Angular build and full solution
regression were not rerun; the Workpaper row and Blazor retirement remain
partial/NOT_READY. Evidence is recorded in `status.json` and the Workpaper
source review.

## Workpaper draft discard and navigation recovery — 1d05ed4c

Added a PostgreSQL-backed Angular/API-host browser journey for the Workpaper
discard action. The journey loads an existing server draft, discards it through
the page, verifies the editor clears, and confirms the persisted draft is
`DISCARDED` with no submission created. The navigation guard also discards the
saved draft before routing and keeps newly typed edits out of persistence. The
focused Release Workpaper E2E class passed 3/3 in 1 minute 11 seconds,
including saved-response recovery. The discard journeys had no browser errors.
This closes the successful direct and navigation discard evidence gaps only;
the Workpaper source row remains partial for
the broader role/scope, failure/recovery, accessibility and production gates.
The full solution suite and EF drift check were not rerun. Evidence is recorded
in [`auditsphere-migration-blazor-workpaper-source-review.md`](../migration/auditsphere-migration-blazor-workpaper-source-review.md)
and `status.json`.

## Route layouts and accounting navigation crosswalk — 15e4bc61

Reviewed and hash-checked the legacy `AccountingNavigation`, `ClientLayout` and
`PublicLayout` against their Angular route and shell owners. Accounting
destinations now live in the shared staff navigation, client sessions see only
the portal destination, and root/setup handling remains in the explicit Angular
shell/API ownership model. Existing navigation and route-sweep journeys cover
the main destinations and role-specific shell behavior; the full legacy
label/active-link matrix, public-state parity and human assistive-technology
acceptance remain open. No source code changed in this review.

The focused PostgreSQL browser journey was not rerun: the configured local
database port did not respond, and the existing `postmaster.pid` referenced an
active process that could not be verified as PostgreSQL. I left the lock file
and database untouched. Per-artifact hashes, dispositions and the local
verification boundary are recorded in
[`status.json`](status.json) under
`verification.angularBlazorCurrentRetirementGate.layoutAndNavigationSourceReview`.
The retirement gate remains `NOT_READY`.

## Angular shared status-chip semantics — 15e4bc61

Angular's shared `StatusChip` now applies the same informational, success,
warning and error categories as the retained Blazor component, with a neutral
fallback. The visible status text remains available to assistive technology and
does not depend on color. Focused component coverage exercises every tone and
the neutral fallback. The Angular unit suite passes at this slice; the full
production optimization still aborts locally, while Angular's development
server compiles and serves the current source.

The built-in browser opened that development server and reached the Angular
unauthenticated shell. Its session request was refused because no API host was
running, so authenticated status chips were not visually verified in this
browser run. Detailed test/build/browser evidence is recorded in
[`status.json`](status.json) under
`verification.angularBlazorCurrentRetirementGate.angularStatusChipSemanticTones`.
The expanded source review keeps the retained shell/support artifacts
`PARTIAL`; supporting-file analysis remains open. Full migration
acceptance, production canary/rollback, human assistive-technology and locale
checks, live Microsoft gates and owner acceptance remain open. The Blazor host
stays present and retirement remains `NOT_READY`.

## Blazor consolidation source/action review

Reviewed the last two unanalyzed presentation artifacts: the group consolidation overview and
advanced schedule/execution workflow. The current Angular/API/Application owners are mapped in the
code map and source review. Focused PostgreSQL-backed browser journeys confirm active group-scope
isolation, nondisclosing denial, route-change clearing and responsive rendering. Both rows remain
partial: complete per-command role/error/unknown-outcome coverage, overview-field assertions,
bounded-load acceptance, report timestamp parity, assistive-technology review and production/owner
acceptance remain open. The current gate is still `NOT_READY`; the Blazor host remains retained.

## Project task progress: current-access revocation and clearer status summaries

The Administrator-only project task progress workspace now offers an explicit refresh action,
accessible progress-bar key, per-section completed percentages and active/pending/blocked totals,
and blocked explanations in audit, phase, and shared-foundation lists. Unmapped modules state
accessibly that implementation progress is not measured. Unit coverage checks malformed payloads,
stale state, refresh recovery and cancellation after session invalidation.

Verification at `ffe3a425aa65812b9d29eb5bc7b4693048bdea20`: Angular progress tests passed 4/4;
the production build passed with the existing Commercial Settings component-style budget warning
(7.53 kB against 4 kB). The PostgreSQL-backed API-host browser journey passed 4/4: Administrator
access and non-administrator denial, concurrent expiry and one-time revocation evidence, immediate
stale-page clearing after role revocation followed by fresh-sign-in denial, and a cross-firm
misbound-grant denial. Tests used synthetic identities and owned isolated databases. The built-in
browser rendered the safe unauthenticated state but had no session for manual inspection of the
protected tracker; the authenticated behavior was verified in Playwright. A full solution regression
was not rerun. Corrupt-file recovery, human assistive-technology and wider-locale acceptance remain
open. Blazor retirement remains `NOT_READY`.

## Reproducible native GL snapshot and filters (CA-23 partial)

Native GL pages carry the database-assigned posting high-water mark captured in a repeatable-read
snapshot. A commit-serialized posting trigger prevents a later visible post from landing below a
previous page's cursor. Trial Balance and movement totals use the same cursor; a later backdated
posting cannot shift later pages. Migration backfill assigns sequences to existing posted journals
without changing their captured accounting content.

The scoped UI/API accepts date range, account-code range, source kind, reference, and counterparty
identity filters. The report returns both matching detail-line count and total posted lines in the
date range. Date and account-code bounds define the Trial Balance basis; source, reference and party
filters narrow detail lines only. Account filtering preserves earlier posted activity in opening
balances. Source distinctions come from immutable journal-origin records; invoices, credits,
settlements and reversals remain native client postings rather than imported source GL or audit
adjustments. Counterparty selection currently accepts the profile's party identifier. Snapshot
drill-through now adds exact native invoice/credit/settlement origin IDs, submitted revisions,
manifest hashes, command-intent hashes, and recorded source-evidence IDs/hashes/references. The UI
keeps manifest, intent, and evidence hashes distinct and states that referenced evidence bytes are
not embedded in the journal snapshot. The posted-invoice database guard now
excludes the database-assigned posting sequence from submitted business-content equality checks;
an additive follow-up migration applies this fix to already upgraded databases.

Verification in an isolated temporary copy: focused PostgreSQL ledger/filter and pre-migration
backfill tests pass 2/2; invoice, supplier invoice, both credit-note types and manual customer/supplier
settlement workflows pass 6/6 with the updated posting guard; the Angular operational journal suite
passes 18/18; API OpenAPI tests pass 5/5 and the generated contract includes the GL filters and
source-lineage schema. The worktree pins SDK 10.0.300 while only
10.0.400 is installed, so the project file was left unchanged and an isolated copy used the
installed SDK. Angular production and development builds repeatedly exited 134 without compiler
diagnostics. The extended Playwright journey then served the prior UI bundle and could not find the
new filter control; it timed out before issuing filter requests, so browser verification remains
unverified for the updated UI. CA-23 durable full-result export and retrieval of exact source evidence
versions/bytes remain open.
This does not complete CA-23 or CA-24; full scope remains unmerged, and tax/ancillary modules remain
optional.

## Partial client open-item allocation and imported settlement evidence (CA-19)

Client-accounting now has a client-scoped open-item workspace for approved posted invoice and credit-note balances, partial allocations, on-account residuals, as-of due-date status, independent review, and exact audited unallocation. It can also link an already imported, sealed receipt/payment line when the source has one approved AR/AP control line and an independently matching cash movement. That path references the existing ledger entry and creates no second cash posting. Cross-client, cross-party, cross-currency, stale-preview, and over-allocation requests fail closed. Tax and ancillary modules remain optional.

This slice now also permits a specifically evidenced, externally completed client receipt or supplier payment to be recorded as a balanced two-line client journal. It uses the approved client AR/AP control and a non-control cash asset, has immutable external reference/evidence, command idempotency, independent journal submission/review/posting, and can appear as an allocatable open-item source. It never initiates or verifies a bank transfer. Posting decision and journal-post timestamps share one retained instant so the database can verify the exact approval. Existing invoice and credit-note posting guard branches remain in force.

Local evidence in the managed `codex/client-accounting-workflow` worktree: the customer receipt and supplier payment journeys, supplier-invoice and both credit-note regression journeys, and settlement-migration rollback check passed 6/6. Customer receipt allocation retained a single cash posting and left the expected residual; supplier payment debited AP and credited the client cash asset. The API and referenced projects built with 0 warnings/errors in the isolated `SettlementCheck` configuration; Angular production build passed with the existing Commercial Settings stylesheet budget warning; EF reported no pending model changes; the API contract serialization test passed and the generated OpenAPI contract diff is clean. The full PostgreSQL-backed solution suite and API-hosted Angular manual-settlement journey were not run.

CA-19 now includes allocation of an already-posted receipt or credit to an approved opening invoice. The additive migration extends the allocation target constraint and deferred validator; it accepts only the latest approved opening for the same client, party and currency, and retains immutable independently reviewed allocations. The application validates the opening manifest digest before preview and submission. Focused PostgreSQL evidence proves a partial receipt allocation against opening AR, the remaining opening balance, zero residual receipt, and exact AR-to-ledger reconciliation. Opening details must tie exactly to the approved AR/AP control balance; aggregate-only openings continue to disclose incomplete aging. Allocation approvals have no separate accounting-effective date, so historical cutoffs use current approved allocations. Prior-period opening carry-forward, the full solution regression, production migration and external acceptance remain outstanding. This is a local feature slice, not full-epic or production acceptance.

## Complete frozen-file write barrier with serialized freeze (STE-REM-09)

The pending-features review's frozen-write findings are closed. Every professional mutation boundary
now carries a consistent frozen-state check: new SRMs, Partner clearance, opinions, signatures,
shares, comments, resolutions, acknowledgements, signed-representation upload/verify, confirmation
criticality and the confirmation status/response/alternative/dispatch/closure commands, note events
(respond/resolve/reopen), procedure result submission and review, item tests, audit differences,
area assessments, and the existing PBC/activity/currency/confirmation-workspace guards. Each refusal
records a distinct frozen-access attempt; refusals inside a caller's transaction commit the attempt
record before returning so the evidence survives. The professional-work authorization itself is
frozen-aware: a frozen engagement reports `protected-state.denied` (with a neutral access label)
instead of the generic professional-work block, after scope and role validation, so every
professional-work command reports the frozen state consistently. The frozen-state check takes a
shared lock on the engagement row inside the caller's transaction and the worker freeze takes the
same row exclusively, so a freeze can never be overtaken by a write that checked earlier.

Verification in an isolated worktree at the pushed STE-REM-08 head plus this slice: the new
PostgreSQL test passed 1/1
(`ConcurrentFreezeSerializesWithWrites_AndFrozenFileRefusesProfessionalMutations` — the shared-lock
serialization probe, the worker freeze through the public discovery/handler path, and eight refused
boundaries with recorded attempts and unchanged business rows), the existing 60-day freeze journey
passed, and the broad regression across the deliverables, program, fieldwork, materiality,
confirmation, difference, area-assessment, review-note, planning and authorization classes passed
64/64. Release builds passed with zero warnings and errors and the OpenAPI contract drift check
reports the committed artifact current (no route changed). The full Domain and Api suites and the
browser journeys were not rerun: the change surface was the freeze service, the professional-work
authorization branch and the named command boundaries. Tests used owned isolated PostgreSQL schemas;
no Development or production state was changed. Blazor retirement remains `NOT_READY`.

## Critical-unreturned confirmations keep blocking the report and signature (STE-REM-08)

The pending-features review's confirmation finding is closed. Confirmation rows now carry the distinct
returned-and-independently-evaluated response fact (the current response revision with an
Agreed/Difference decision independently reviewed by another user); criticality, returned-response
evidence, independent evaluation and workflow closure remain separate facts. A case that currently
stands Critical closes only on that returned and evaluated response — reviewed alternative work keeps
its appropriate noncritical path — and a critical case without it keeps holding the auditor report
even after alternative-only closure or a later criticality reassessment, with the holding letter
generated for the exact blocking set. The signature rechecks the current critical set before the
staleness fence, so a report generated before a case became critical cannot be signed. Criticality
reassessments retain every actor/rationale history row. The existing signing fixture now closes its
critical confirmation through the real response record/review/closure path instead of a direct status
edit, and the committed OpenAPI artifact was regenerated for the parallel session's four additive
AS-COMP-16 fieldwork endpoints (the drift check passes with no removals).

Verification in an isolated worktree at the pushed AS-COMP-16 head plus this slice: the new focused
PostgreSQL test passed 1/1
(`CriticalUnreturnedConfirmationKeepsBlockingAfterAlternativeClosureAndCriticalityReassessment`), the
full `AuditDeliverablesTests` and `AuditConfirmationCommandIsolationTests` classes passed 11/11, the
Release builds of the Api and test projects passed with zero warnings and errors, and the OpenAPI
contract drift check reports the committed artifact current (the additive dashboard field needs no
Angular change: the decoders read only their declared keys). The full Domain and Api suites and the
browser journeys were not rerun: the change surface was the confirmation closure gate, the dashboard
fact, the report/signing predicates and the focused tests. Tests used owned isolated PostgreSQL
schemas; no Development or production state was changed. Blazor retirement remains `NOT_READY`.

## Procedural workpapers, evidence links, and safe collaboration (AS-COMP-16)

Fieldwork procedure tailoring, workpaper generation, evidence linkage, and independent review lifecycle are verified (§§4.3.2–4.3.3, AS-COMP-16):
- Procedure tailoring (`AuditProgramService.TailorProcedureAsync`): allows tailoring title, custom wording, and applicability rationale under planning authorization. Updates acquire an exclusive row lock (`FOR UPDATE`); tailoring an already reviewed or submitted procedure reopens testing to `InProgress` and increments `CurrentResultRevision`, safely invalidating stale conclusions.
- Workpaper generation (`AuditProgramService.GetOrCreateWorkpaperAsync`): initializes a dedicated `Workpaper` entity linked to the planned procedure and moves the procedure to `InProgress`. Idempotent requests safely return the existing workpaper ID.
- Evidence linking & records queries: `AuditRecordQueries.WorkpaperAsync` returns linked digital PBC evidence (with SHA-256 content digests) and physical evidence indexing (file index, box reference, location movements).
- Submission synchronization: `AuditPlanningService.SubmitWorkpaperAsync` updates linked procedure status to `Submitted` in lockstep.
- Fieldwork API endpoints: exposed `/api/ui/procedures/{id}/tailor`, `/api/ui/procedures/{id}/workpaper`, `/api/ui/procedures/{id}/results`, and `/api/ui/procedure-results/{id}/review`.
- UI updates: `workpaper.ts` decodes and renders linked digital evidence and physical storage references; `fieldwork.ts` decodes `workpaperId`.
- Verification: PostgreSQL-backed unit tests `ProcedureTailoringAndWorkpaperGeneration_EnforcesLifecycleAndInvalidation` (in `AuditProgramWorkflowTests`) and `LateAndReopenedNotesBlockClearanceUntilRespondedResolvedAndReviewed` (in `AuditDeliverablesTests`) pass alongside the full 104-suite Angular frontend test run and .NET test suite. Release build compiles cleanly with zero warnings/errors, and EF Core model has zero pending migrations.

## Engagement-scoped CRM, hierarchy and client-finance reads (STE-REM-03)

The pending-features review's commercial scope findings are closed. Correspondence-recipient
resolution now requires current internal commercial scope for the client: it reuses the
target-aware `AuthorizationDecision` (client-level coverage, `CommercialRoles`, internal-only),
returns a nondisclosing `Denied` status when scope is missing, revoked or engagement-only, and
the authorized-override path no longer bypasses that check. The resolve endpoint maps `Denied`
to a 403 `scope.denied` refusal; no contact identity is returned on refusal.

Organization-hierarchy exploration now authorizes every counterpart client before returning any
relationship metadata. Counterpart nodes outside the actor's current commercial scope are
## Late and reopened review notes block completion (STE-REM-06)

The pending-features review's finding that a regenerated Summary Review Memorandum could bypass a
late or reopened review thread is closed. The canonical completion evaluation now includes every
unresolved thread on the engagement as a `review-note:{id}:unresolved` blocker — across all result
revisions and including reopened threads — and the SRM facts use the same engagement-level
open-thread count, so regeneration, applicability changes and clearance comments never resolve a
thread. A note added after the work was reviewed, or a thread reopened afterwards, returns the
exact procedure and result to `CHANGES_REQUIRED` while retaining the earlier review decision, so
Partner clearance, signing and publication stay blocked until the required response, reviewer
resolution, resubmission and renewed review restore the current work. A frozen file refuses the
note attempt under the existing write barrier and records the refused attempt separately.

This slice landed inside the parallel session's AS-COMP-16 commit `d51c189f` (its verification
covered the combined tree); the focused STE-REM-06 evidence was verified on an isolated worktree
before that commit and re-verified at `d51c189f`: the two new PostgreSQL tests passed 2/2
(`LateAndReopenedNotesBlockClearanceUntilRespondedResolvedAndReviewed`,
`FrozenFileRefusesReviewNoteAttemptsAndRecordsTheRefusedWrite`), the full `AuditDeliverablesTests`
and `AuditReviewNoteIsolationTests` classes passed 10/10, `AuditProgramWorkflowTests` passed 4/4,
and the OpenAPI contract drift check reports the committed artifact current (no route changed).
Tests used owned isolated PostgreSQL schemas; no Development or production state was changed.
Blazor retirement remains `NOT_READY`.

omitted entirely — no name, identifier or existence disclosure — while authorized relationships
(including the holding parent seen from an authorized subsidiary) remain visible.

Client-portal finance reads now carry exact grant scope: a client-wide `ClientUser` grant covers
the client's agreements while an engagement-scoped grant only covers its own engagement's
agreement — never a sibling engagement's agreement, invoice, receipt or amount. Commercial-document
downloads for client users now require the exact client and, for engagement documents, the exact
engagement; the permissive null-client grant fallback is removed; and only explicitly client-shared
documents (official payment receipts) are downloadable — internal quotations, tenders and letters
are refused.

Verification in an isolated worktree (the shared checkout carried another session's in-flight
planning/fieldwork edits): the three new PostgreSQL-backed tests passed 3/3
(`RecipientResolution_RequiresCurrentCommercialScope_AndOverridesDoNotBypass`,
`OrganizationHierarchy_HidesCounterpartNodesOutsideCurrentCommercialScope`,
`PortalFinanceAndReceiptDownloads_RespectExactClientAndEngagementScope`), the full
`ClientRelationshipsAndRoutingTests` and `CommercialWorkflowTests` classes passed 16/16, the
routing/portal API contract classes passed 5/5, the Release builds of the Api and both test
projects passed with zero warnings and errors, and the OpenAPI contract drift check reports the
committed artifact current (no route changed). The full Domain and Api suites and the browser
journeys were not rerun: the change surface was the named Application reads, one endpoint
response mapping and the two touched test classes. Tests used owned isolated PostgreSQL schemas;
no Development or production state was changed. Blazor retirement remains `NOT_READY`.

## Comparative split financial statement dashboard (AS-COMP-15)

The interactive Financial Statements workspace now provides a comparative split dashboard (§4.3.1, AS-COMP-15):
- Upper Statement of Profit or Loss (P&L) and lower Statement of Financial Position (B/S) are rendered simultaneously in the desktop workbench (responsively stacked on narrow viewports).
- Columns for each line and section summary show Current Year balance, Prior Year comparative balance, Absolute Variance, and Percentage Variance adhering to sign convention `(Current - Prior) / |Prior| * 100%`, with zero prior denominator displayed as `N/A` and unavailable prior data displayed as `—`.
- Prior period comparative figures are resolved systematically from the validated financial package comparative reference, earlier validated financial package for the client/currency, or earlier approved mapping and sealed trial balance.
- FSLI rows display risk band badges ("Low", "Medium", "High") derived from engagement materiality assessments or FSLI defaults, along with assigned performer and required reviewer ranks.
- Procedure execution status indicators ("Reviewed", "In progress", "Planned", "Changes required", "No procedures") reflect underlying audit substantive testing progress.
- Distinct interactive triggers are provided per row: `[AR Test]` (navigating to analytical review preparation) and `[Audit Workprogram]` (navigating to fieldwork substantive procedures).
- Backend endpoint `GET /engagements/{id}/statements/split` returns both sections simultaneously.
- Verified with PostgreSQL-backed API tests (5/5 passing in `StatementReviewApiTests`), full Angular test suite (541/541 passing across 104 suites), Angular production build, Release solution compilation (0 errors, 0 warnings), and zero EF Core model drift.

## Statutory milestone planning and 60-day archive freeze (AS-COMP-10)

Statutory filing deadlines are now explicitly stored and never silently assumed or hardcoded (§4.2.2, AS-COMP-10). The engagement milestone plan entity (`EngagementMilestonePlan`) captures the explicit statutory filing cutoff, fieldwork commencement target, draft report delivery target, final signed report date, and the mandatory 60-day archive deadline under ISA 230 (§4.4.3).

A pure calculator (`StatutoryMilestoneCalculator`) evaluates chronological consistency and overridable scheduling warnings:
- Enforces strict chronology: period end < statutory filing cutoff, fieldwork commencement >= period end, draft delivery >= fieldwork commencement, final signed report >= draft delivery, final signed report <= statutory filing cutoff, and archive freeze >= final report.
- Detects compressed scheduling conditions (fieldwork window < 14 days, review window < 7 days, statutory filing buffer < 5 days).
- Distinguishes forbidden validation errors from overridable scheduling warnings: saving with scheduling warnings strictly requires a documented partner or manager override reason.
- Requires documented adjustment reasons whenever altering calculated defaults.

The application service (`StatutoryMilestoneService`) enforces scope-checked authorization (`Partner`, `Manager`, `SeniorManager`, `Administrator` can configure; planning roles can preview/read; unauthorized access is denied). Angular decodes the milestone plan, displaying the operational schedule, 60-day archive freeze, scheduling warnings, override reasons, and an interactive schedule configuration form.

Focused PostgreSQL-backed database integration and calculator tests passed (9/9). Full Angular unit tests passed (540/540 tests across 104 suites) and production build succeeded. Release solution build passed with 0 warnings and 0 errors, and EF Core reports no pending model changes.

## Reviewed client posting account roles

Client books now support immutable role proposals and independent approval/rejection. Approved intervals select compatible posting accounts from an approved client chart; no financial-statement meaning is inferred from account-number prefixes. Roles cover AR/AP, tax recoverable/payable, revenue, purchase expense/asset, retained earnings, rounding and FX. Tax and ancillary roles remain optional.

Client locking and database checks fence overlapping approvals, self-review and control activation over posted generic activity. Generic native journals cannot create unexplained AR/AP activity: preparation, preview/submission/post validation and a specific database posting trigger refuse that path. Earlier drafted journals become ineligible after an applicable control role is approved. Angular offers bounded account search, paged retained history, explicit proposal assent and independent review; unconfirmed commands require fresh history before further preparation. Focused PostgreSQL, Angular, API contract and automated browser checks passed. Exact source and observed counts live in `verification.clientAccountRoles` in `status.json`.

Two managed solution-build attempts were interrupted when a concurrent process merged the branches and removed their checkouts. The preserved test/contract commit passed the full solution build in an independent temporary verification checkout. Work continues in a new managed worktree, with user-authorized cleanup coordination requested. This role slice does not implement default/system invoice postings, AR/AP open items, approved-role replacement or end-date amendments. The full epic remains active; the final merge is pending completion. The calculator-baseline regression continues separately and must not be attributed to this newer model.

## Hostinger acceptance Microsoft 365 readiness snapshot (2026-10-10, refreshed)

The VPS checkout was fast-forwarded to master for the Compose mount; the existing API image was
retained (no new application image deployment). `docker compose up -d --no-deps --force-recreate
api` recreated only the API. Database and workers stayed up; no migration or Graph call occurred.
`/health/ready` returned HTTP 200.

To keep Selected Site inactive until the user's exact-site grant, `SELECTED_SITE_CLIENT_ID` was
cleared from the protected VPS `.env`. The certificate directory remains mounted read-only, but
the API has no app ID and reports the provider unconfigured. `ExternalEffects`, DirectoryReader,
tenant provisioning, guest invitation, group membership, outbound mail and live PBC transfer are
false.

The reloaded page shows consent `REQUIRED` with no attempt, directory `NOT_VERIFIED`, selected
resource `BLOCKED_EXTERNAL`, unavailable activation, and Mail/Records `Not Configured`. The
capability catalog's Selected SharePoint `Enabled` label is not evidence of a configured provider
or access; the runtime credential is absent. The user will grant only the exact site. Afterward,
restore the app ID privately and run the authorized positive and unrelated-site denial probe; never
add tenant-wide `Sites.FullControl.All` to the runtime app. User-reported Entra admin consent has
not produced an AuditSphere consent attempt or verified state. No site grant/probe or activation
was performed; M365 remains `BLOCKED_EXTERNAL`.
