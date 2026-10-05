# AuditSphere Angular presentation migration

**Status: CURRENT for implemented API/Angular boundaries; local owner-approved route cutover is complete; full migration acceptance remains PARTIAL.**

The owner requested implementation of the attached Blazor-to-Angular guide. Angular replaces presentation incrementally. The .NET modular monolith remains the capability, session and database authority. Angular owns canonical API routes. Commit `59387b54` physically removed the Web host before all behavior and operational gates passed; the source and solution project have now been restored from its parent snapshot as a buildable rollback/reference host. The migration removal gate remains `NOT_READY`. Production deployment, canary, assistive-technology and live Microsoft gates remain separate acceptance work.

## API and Angular project boundary

The owner subsequently requested an ASP.NET Core API backend and a separate Angular UI project. `AuditSphereOps.Api` owns the HTTP host, authenticated API contracts, consent callbacks, protected file transports, provider composition and health checks. `AuditSphereOps.Ui` owns Angular presentation. Domain, Application, Infrastructure and Worker retain their existing responsibilities.

The current source tree retains `AuditSphereOps.Web`, its Razor components and MudBlazor as a rollback/reference project. API serves Angular as the canonical presentation path and does not route API/auth/health failures into the SPA. Restored source and a successful build do not establish operational rollback acceptance.

Same-origin delivery remains the supported production boundary: build Angular separately, publish its browser artifacts with API, and terminate HTTPS at the approved host. The Angular development server proxies API/auth calls; no permissive cross-origin policy or browser bearer-token store is added. Existing private development user secrets remain in the approved `AuditSphereOps.Web.Development` namespace for local API development; no secret is copied into project configuration. Independent local API publication has been checked. Production deployment and canary acceptance remain open.

The owner approved local cutover on 2026-10-04. `AngularUi:Enabled` and
`AngularUi:CanonicalRoutes` are true in the API's checked-in base configuration.
`AuditSphereOps.Web` was restored after the premature physical deletion in
`59387b54`; it is included in the solution but not referenced by API.Tests or
E2E.Tests. The API-only E2E host conversion landed in `8ea3ef01` and its fixture
now uses API-specific method names. The current Angular, Release, PostgreSQL-backed
full-suite, and EF model checks pass locally. Source-action parity and operational
rollback acceptance remain open. Angular assets/API builds remain the active
route owner.

## Implemented ownership

### Assessment reviewed command backend

The scoped assessment API provides CSRF-protected preview and execute contracts
and an authorized no-store receipt lookup. Application services retain answer,
specialist, Partner decision and continuance authority. An exact persisted review
basis and separate explicit assent fence new publication; immutable actor-owned
receipts reconcile lost replies. The local mutation and receipt commit together.
No external operation, professional conclusion, role grant or engagement activation
is inferred. Retained evidence prevents rollback of the receipt migration.

Deployments require their usual approved database migration before these new
contracts are used. Existing assessment endpoints remain available to retained UI
builds. Native Angular preview/forms/reference recovery integration and retirement
acceptance are still pending; the backend alone does not establish UI parity.

### Existing presentation and host ownership

- Angular workspace: `src/AuditSphereOps.Ui`, standalone Angular with Material/CDK, zoneless change notification and lazy capability routes.
- Shared Material typography uses an installed system font stack for both the shell and body-mounted dialogs, with no external font dependency.
- API serves only the explicit `/ui` route catalogue shared by `UiEndpoints.SpaRoutes` and Angular `app.routes.ts`; a contract test prevents catalogue drift. Native families include practice, accounting, consolidation, audit, completion, administration and the restricted portal. Auth, consent callbacks, API, protected file transports and health keep their HTTP owners. Unknown routes are not a blanket SPA fallback.
- `AngularUi:Enabled` and `AngularUi:CanonicalRoutes` default to true in API. Enabling requires a production browser build. `AngularUi:BuildPath` optionally supplies its directory. Local publication bundles browser artifacts under `ui/`; API includes no Web/MudBlazor dependency. A backend-only publication is explicit and must disable local SPA serving or supply approved assets. The restored Web project is a separate solution rollback/reference host, not part of API composition or current API-only test hosting.
- Angular navigation stays within its declared routes. Search validates destinations against that same catalogue, handles multiple hits sharing one destination and shows an unavailable state for an unowned page. Skip navigation focuses the current main content without changing workspace context.
- API owns `/api/ui/session`, `/api/ui/portfolio` and CSRF-validated `POST /api/ui/sign-out`. Angular uses these same-origin contracts. The separate rollback Web host is not involved in these API routes. They resolve trusted cookie identity and current epoch, never accept browser actor/firm/role authority, and return no-store responses.
- Initial setup has a native `/ui/setup/microsoft365` page and cookie-authenticated `/api/setup/session` / `/api/setup/bootstrap` contracts. Only the exact deployment-approved Microsoft tenant/object identity can submit the installation proof with CSRF protection and explicit review. Application onboarding performs the local binding and evidence transaction; the API returns no raw proof/capability and requires fresh sign-in for the new session epoch. A revoked administrator cannot reopen bootstrap. Tenant preparation uses the deployment tenant and expected draft revision, preserving immutable connection history without asserting consent or selected-site verification.
- The Application `PortfolioQuery` owns bounded scoped clients, current-scope summary counts, recent release/package projections and formula-safe CSV export. Each projection uses one authorized scope snapshot and rechecks every contributing scope before delivery. Group membership never authorizes this query.
- The session bootstrap issues the antiforgery proof through `XSRF-TOKEN`; Angular sends `X-XSRF-TOKEN` for same-origin unsafe HTTP calls. Sign-out validates the antiforgery proof before removing the authentication cookie. Passive session checks do not renew an idle authentication ticket. Migrated business mutations compose existing Application services. Every unsafe endpoint must explicitly validate antiforgery as well as Application authorization; client configuration alone is not CSRF enforcement.
- Session state stays in memory. Periodic and focus checks remove the protected Angular subtree when session verification fails. Canceled/superseded portfolio requests cannot refill invalidated state. Browser storage contains no authentication tokens or portfolio cache. Convenience drafts use a firm/user/scope namespace and are removed at sign-out; complete draft lifetime and revision parity remains under review.

Build, preview and test commands are in the [Angular development guide](../../src/AuditSphereOps.Ui/auditsphere-angular-development.md).

### Native portfolio projection and export

`GET /api/ui/portfolio/workspace` composes the Application projection; the existing
client-list contract remains available. Client counts, engagement counts, holds,
pending operations, ready candidates and issued releases reflect explicit current
grants. Engagement-only authority excludes sibling engagements and client-level
operations. Record projections require the same firm and exact client/engagement
relationship. Summary counts remain independent of search.

The recent panels search only the bounded latest candidate/package windows, with
that limitation stated in the UI. Client pages support the preserved page-size
choices. The scoped CSV endpoint validates antiforgery, reprojects authorized data,
neutralizes spreadsheet formula/control prefixes and refuses a client set above
its declared bound. It creates no release approval or external side effect. Numeric
revision identities remain exact strings; reporting periods remain stored strings.

Search, page, page size and selected client use validated URL state. Memory-only
return navigation is owned by the exact firm/user/session generation; protected
results are never cached. Every return refetches current authority. Identity change,
revocation, route destruction and changed filters fence late reads/downloads. The
configuration notice reports persisted active configuration only; it does not
establish verified Microsoft capabilities. Full client/engagement source-action
parity, production rollout and assistive-technology acceptance remain open. The
Web host was physically removed before source-action parity passed and has since
been restored as a rollback/reference project; migration acceptance is still
`NOT_READY`, and production-like rollback operation has not been accepted. Curated source dispositions are in
the [portfolio action audit](../execution/angular-portfolio-parity.json).

### Reviewed local client contact creation

A focused native contact route composes `ClientContactCreationWorkspace` for current
client context, server preview, explicit reviewed creation and actor-owned receipt
lookup. It reuses `PracticeCrmService`, whose existing contact endpoint remains
available as an API contract. Current staff/client authority and the locked
safety generation fence publication; contact and receipt commit together. The
append-only receipt snapshots the committed review basis, exact normalized intent
and previous primary contacts. Contact email is never an identity binding.

The request hash binds actor/session, client, request identity and immutable contact
fields. A fresh review basis can safely retry that identical intent: an existing
receipt wins before another creation, while new publication still requires current
revision review. Database constraints and triggers preserve firm/client/actor
relationships, publication consistency, receipt immutability and rollback fences.

Editable drafts are explicit, tab-only and bounded. Recovery requires the same
identity/session/client revision and fresh assent. After an unknown outcome, only
the pending request identity can cross a changed revision for receipt lookup;
stale fields and assent never recover automatically. Navigation and new submissions
remain fenced until reconciliation or fresh review of the identical retained intent.
Role or session loss removes protected content; late callbacks cannot refill a new
route visit. This workflow creates no directory identity, invitation or local role.
The separate reviewed engagement-creation route now identifies a matching
service-period shell for inspection and blocks duplicate writes. Broader client
lifecycle and migration acceptance remain open.

### Native client master records and scoped pages

The existing client-profile API composes `WorkspaceQuery.ClientAsync`. Legal and
trading names, registration, jurisdiction, UTC onboarding time and exact safety
generation are explicit DTO fields; restricted profile content is excluded. Client
profile authority remains client-wide. Knowing an engagement ID or holding an
engagement-only grant does not authorize this master record.

Contacts and associated engagements have independent bounded server pages with
stable source sorting, preserved row-size choices and complete authorized counts.
Unpaged requests retain the original bounded window for retained older UI builds;
the current view always supplies explicit page parameters. The engagement projection
captures and rechecks its contributing scope requests;
a client commercial role does not widen an engagement assignment. Every delivered
page rechecks the client and any permitted command authority, while the API also
rechecks the trusted session. Invalid or guessed destinations disclose no metadata.

The portal notice reuses the existing Application intent view. It is an onboarding
notice, not identity binding, a grant or proof of Microsoft invitation redemption.
Validated URL pages survive reload; superseded reads, mismatched response contexts,
route/session changes and old command callbacks cannot restore previous content.
Existing contact and engagement-creation commands remain separate. Reviewed
engagement creation now includes bounded revision-bound tab drafts, exact request
recovery and existing-shell inspection. Client creation remains tied to the
reviewed proposal-conversion workflow: it requires an accepted proposal and won
opportunity, reviews canonical identity, and records pending portal intent without
granting access. Planning forms/drafts and broader engagement action parity remain
open. See the
[client action audit](../execution/angular-client-profile-parity.json).

### Native engagement metadata and hold history

`WorkspaceQuery.EngagementAsync` supplies service-profile and UTC creation metadata,
exact engagement generation, complete authorized hold totals and bounded history
pages ordered by creation time and immutable identity. Unpaged callers retain the
original bounded window for older UI builds. Client-profile navigation requires
separate client-wide authority; an engagement assignment never widens that scope.
Every advertised command/navigation authority is rechecked after the projection,
and the API rechecks the trusted session before delivery.

The native view validates exact response identity, page, counts and revision.
Active holds independently display blocked professional work even if the stored
engagement flag is open; the clearance link leads to retained gate evidence.
Same-engagement hold paging hides the view while revalidating and preserves its
team/budget editor instance. Refused or malformed responses, identity/session loss
and route destruction remove protected content. Route lifetime ownership also
fences a late activation result after a return to the same identity. Planning
command/read callbacks have matching owner and destruction checks. An unknown
activation outcome permits state inspection, not another blind submission. An
unsupported activation acknowledgment remains unconfirmed.

This implements read-side metadata/history parity. The reviewed blocked-engagement
creation flow now identifies matching service-period shells for authorized
inspection and keeps duplicate writes blocked. Planning forms/drafts, broader
engagement action parity and migration acceptance remain open. The
[engagement action audit](../execution/angular-engagement-parity.json) records
those boundaries; executed verification belongs to the execution ledger.

### Reviewed Partner engagement activation

The native activation route composes `EngagementActivationWorkspace` and the
existing `EngagementLifecycleService`. Every context read, preview, confirmation
and receipt lookup requires a current internal Partner grant covering the exact
engagement. Administrator authority alone is insufficient. The review displays
stored service/profile/period metadata, exact engagement and client revisions,
the current decision for the same service route, and every active hold. Only a
draft with current unconditional acceptance and no unreleased holds is eligible.

Confirmation binds the displayed review basis and explicit fresh assent. Under
the existing client safety lock, activation, revision increment, portal-intent
composition and request evidence commit together. Actor/session authority,
current acceptance and holds are checked again before commit. A unique request
identity returns its original actor-owned receipt on retry. The receipt retains
exact acceptance, path, actor/session, reviewed revisions and resulting revision;
existing activation evidence remains append-only.

Tab recovery stores only a bounded request ID and hash, never assent or an
executable stale review. Network failures and unsupported acknowledgments fence
navigation and new submissions until explicit receipt reconciliation. An absent
receipt permits inspection of fresh prerequisites and a new review of that same
fixed actor/session/engagement action. Role/session loss and route destruction
remove protected content and fence callbacks from an earlier route visit.

The additive `NativeEngagementActivationReview` migration leaves legacy activation
rows unchanged with null request metadata. Its deferred database guard checks
publication scope and exact accepted activation state. The existing append-only
guard protects all receipt fields; rollback refuses while reviewed activation
evidence exists. Operators apply it through the existing approved migration
workflow before using the new route. Verification uses disposable local databases;
no shared Development migration or live Microsoft effect is implied. Activation
does not grant portal or Microsoft access and does not record a professional
acceptance decision on behalf of a practitioner.

### Controlled canonical route ownership and retained assets

The owner-approved API configuration now enables `AngularUi:CanonicalRoutes` by
default. Deployments can explicitly set it to `false` to keep the `/ui` preview
prefix during rollback. When enabled, the exact native catalogue also owns `/app`,
`/portal` and `/setup` destinations with a root router base. Preview routes remain
available under `/ui` for existing tabs. API, auth, health, protected file paths
and unknown destinations never receive an HTML fallback. The restored Web project
is not referenced by API route composition; it remains a separate rollback/reference
host while route ownership stays with Angular. Disabled Angular serving or an incompatible build also
fails startup.

One production build uses `/ui/` for fingerprinted script/style asset URLs in both
route modes. Router links, search, deep-link sign-in, first-administrator setup and
consent return destinations follow the host-selected base. This uses the supported
[Angular base-href](https://angular.dev/api/common/APP_BASE_HREF) and
[deployment resource URL](https://angular.dev/cli/build) contracts without changing
server authentication, CSRF, session epochs or local scope authority.

An optional `AngularUi:PreviousBuildPath` selects a distinct retained approved build.
The current assets are served first; old fingerprinted scripts, styles and media
may resolve from that directory when an existing tab requests them. Old HTML,
source maps and unhashed files are excluded from the fallback. The deployment
operator must confirm API compatibility and retain the previous build for the
approved rollback window. No older API/database writer is started by this option.
Local mode/asset checks do not establish production rollout or final retirement.

### Responsive shell navigation and preserved bookmarks

Desktop and compact shells use one scoped navigation component. On narrow screens,
Material's modal presents a labelled, scrollable menu with keyboard focus containment,
Escape/backdrop dismissal and an explicit close action. Successful route navigation
closes the menu and focuses the destination; guard cancellation preserves the menu.
Session/identity changes clear it. Resize and late lazy-load checks prevent a hidden
trigger from retaining focus or a stale session from opening an overlay.

The old Microsoft 365 workspace bookmark redirects to the guarded native tenant
workspace. A source-route contract checks every legacy workspace route against the
explicit Angular/host catalogue; root and access-denial endpoints remain server-owned.
This proves route disposition, not full action parity or migration acceptance. Source
control discovery remains separately checked. Local browser/unit evidence belongs
in `status.json`; broader screen-reader, all-module and production acceptance remain open.

## Read contract

`GET /api/ui/portfolio?search=<client-name-or-id>&page=0&pageSize=25`

Search length at most 100, page size 1–100, nonnegative bounded page index. Rows sort by client legal name then immutable ID. Counts and totals include only authorized records. HTTP 401 means unavailable session, 403 unavailable local access, 400 invalid input. No raw EF entities are returned. Financial DTOs serialize decimals as exact invariant strings; date-only values do not pass through local-time conversion. Angular runtime decoders reject unsupported responses.

## Acceptance boundaries

The generated [source discovery inventory](../execution/angular-source-inventory.json) identifies routes, event expressions, service injections and literal links. It is not manually accepted coverage: nested/dynamic commands, dialogs, drafts, permissions and source tests still require review.

The implementation covers native route families for practice, accounting, consolidation, audit, completion, portal and administration. Route presence is not accepted source-action parity. Full form/draft/control parity, comprehensive security and behavioral coverage, measurable production-like performance, rollback readiness and retirement acceptance remain open. The migration backlog records partial story coverage. The Web host was physically deleted before the gate passed and has been restored as a buildable rollback/reference project; operational rollback acceptance remains open and migration readiness remains `NOT_READY`.

Execution evidence is recorded in [status.json](../execution/status.json). Local synthetic fixtures do not establish tenant, deployment, professional or owner acceptance. Wiki publication has not been authorized.

### Native source-bound ECL and inventory preparation

The native reconciliation inspection links separate ECL and inventory preparation
forms. `ValuationPreparationWorkspace` composes the existing supported calculators
and creation commands over the exact reconciled source, complete current item proof,
reporting context and client generation. Every active numeric input, including zero,
is explicit and bounded to the supported decimal precision. Unsupported methods,
missing inputs, stale sources, closed periods/books and frozen files block creation.
An approved historical reconciliation is not silently reset to make it editable.

A fresh preview binds all inputs, assumptions digest, rationale, evidence reference,
actor/session and current source basis. Explicit assent authorizes only that exact
intent. The guarded local transaction creates a new draft analysis and an immutable
actor-owned preparation receipt with input, context and result snapshots together.
Identical requests recover the same receipt; changed intents cannot reuse their
identity. Final scope/epoch checks roll back both records after late revocation.
Independent review remains separate from preparation, and source books are unchanged.

The additive migration preserves preparation receipts and their exact scoped target
lineage, freezes native valuation inputs, and refuses rollback that would discard
retained preparation evidence. New structured records archives include the retained
snapshots without changing older exports. Shared or production migration application
remains operator-controlled.

Angular retains bounded editable tab fields without assent and requires a saved
request reference before dispatch. Unknown or malformed acknowledgments fence new
writes and navigation until persisted receipt reconciliation and acknowledgment.
Route/session changes clear protected state. New reconciliation, specialist and
analytical preparation parity and wider migration acceptance remain open.

### Native reviewed accounting evidence actions

The separate native evidence action page composes `AccountingEvidenceWorkspace`
and the existing analysis commands. Procedure selection is bounded and server-paged,
with exact same-engagement workpaper/procedure parents, current result revision,
generation and immutable selected-result basis. Approval additionally requires
current retained input/method/replay checks and a linked independently reviewed
current procedure result. An evidence preparer cannot review the same evidence.
Missing legacy journal-risk provenance permits an explicit human escalation only;
it never permits native clearance or an inferred fraud conclusion.

Preview and explicit assent bind the actor, session, target, evidence basis, selected
result revision, decision, rationale and evidence reference. Execution serializes
with existing firm/client/engagement guards and reporting-period locks, checks
book closure and file freeze, repeats authority before commit, and composes the
existing service in the same local transaction as an immutable before/after receipt.
A serialization refusal leaves no partial publication. Identical local retries
reconcile the actor-owned retained intent; changed intents cannot reuse its identity.

The additive migration guards scoped targets and links, freezes native reviewed
records, and prevents edits/deletion of native receipts or their retained links.
Rollback refuses to discard retained native action evidence. New structured records
archives include these actions and their exact snapshots; older exports remain
unchanged. Shared/production migration application is still operator-controlled.

Angular Signal Forms retain bounded editable fields in an identity/base-bound tab
draft without assent. Dispatch requires a saved recovery reference. Unknown or
malformed acknowledgments fence new commands and navigation until the actor-owned
receipt is checked and acknowledged, or an identical pending intent is freshly
reviewed after persisted absence. Changed route/session clears protected state and
late responses cannot refill it. Preparation/editing of new analysis or reconciliation
records and broader migration acceptance remain open.

### Native analysis evidence inspection

`AccountingAnalysisReviewQuery` serves exact locally authorized ECL, inventory,
specialist, analytical and journal-risk records. Retained approval, current input
verification and a current independently reviewed procedure result remain separate
facts. The view keeps six-decimal inputs and historical results, methodology,
evidence references, human explanations and reviewer metadata. It supplies no
approval, calculation, posting or evidence-link command.

Valuation inspection checks the exact reconciliation and complete retained proof
against its accepted/sealed source digest and client generation. The existing ECL,
inventory and specialist calculations are reused to verify retained typed values;
no replacement result is shown. Analytical replay verifies the retained digest and
typed snapshot, preserving a missing denominator as unavailable. An assumptions
digest identifies declared evidence; it does not prove external assumptions.

Specialist and analytical records state their period/generation provenance without
inventing an exact source identity. Legacy journal-risk flags lack an original
source digest and generation: the native queue and detail mark that provenance
unverified even when a human disposition is retained. A risk indicator never
becomes an autonomous audit conclusion.

Complete audit links are bounded and server-paged. Related result, procedure,
workpaper, source and proposed-journal identities must share the exact authorized
parents; a current procedure review must have the current result revision and
client generation. Repeated projections bind related evidence and final scope/epoch
checks fence changes during a read. Angular rejects inconsistent or mismatched
responses and clears protected detail after route/session changes. Wider reviewed
editing and approval parity remains open.

### Native reconciliation inspection

`ReconciliationWorkspaceQuery` reads an exact locally authorized client/engagement and
reporting period/book. It uses the existing reconciliation item-manifest formula to check
the complete bounded item set and latest retained proof, while independently checking the
current accepted/sealed source, digest and client generation. Historical amounts and
review metadata stay separate from current reuse eligibility. Missing source, generation
or proof blocks reuse; it never invents a zero result. The detail is read only.

The native API and Angular route expose bounded item pages, preparation/review evidence
and exact source navigation. Runtime decoders retain decimal strings, validate current
proof eligibility and reject a mismatched target/page. Changed route/session clears old
protected detail. A repeated authoritative projection and final exact authorization fence
a mid-read input or epoch change. There is no new schema, Microsoft permission, calculation,
approval or evidence-link mutation in this slice.

### Accounting evidence read authorization

The evidence queue derives scope from current, unrevoked and unexpired local grants.
Application rechecks every returned client/engagement scope and the actor's current epoch
before publishing records or counts, including an empty result. The trusted HTTP identity
resolver independently reconciles grant expiry and invalidates the former session. This
read boundary does not change application roles or Microsoft directory permissions.

### Native reviewed adjustment-plan commands

Exact sealed source inspection links to native plan preparation. A bounded posted-journal
catalogue matches the authorized client, engagement, period, book, currency and basis. Each
selection names an immutable journal ID and revision. Unknown, partial, ambiguous and group-only
treatments are blocked. An empty selection explicitly prepares a source-only plan. The server
preview binds the complete source and current catalogue, rationale, evidence and request identity;
the browser must obtain fresh assent before dispatch.

Finalization has a separate exact-context preview. The Application workspace uses the existing
pure trial-balance calculator, validates bounded source accounts and posted journal lines, and
returns exact decimal totals and a culture-independent result hash. Source reflection determines
which revisions contribute; reflected entries are excluded without applying them twice. Missing
or changed decisions block calculation. Source books remain unchanged, and the resulting plan
does not issue a financial package or post to external books.

Creation and finalization serialize under the existing parent/safety locks and retain actor-owned
idempotency receipts in the same local transaction. Current scope, session epoch, period/book and
freeze checks are repeated before commit. Database guards retain native membership and context,
freeze finalized results, reject event changes and require finalization evidence atomically.
The legacy finalization entry point refuses a native plan unless it uses the reviewed command;
existing legacy plans retain their service contract. Native history is scoped and server-paged.

Tab fields exclude assent and calculated balances. Recovery references are required before a
command is sent. Unknown or malformed acknowledgments fence new commands and navigation until
an authorized persisted receipt is checked and explicitly acknowledged. Receipt absence permits
only the identical intent after a fresh preview. Source, route and session changes clear protected
state and fence late responses. The additive migration is exercised in disposable local test
databases; rollback refuses removal when retained native plan evidence exists. Shared Development
and production migration application remain operator-controlled.
Executed verification and remaining migration acceptance are recorded in `status.json`.

## Incremental capability notes

The following notes preserve the implemented slices and their safeguards. Earlier then-pending lists are superseded by the current [story backlog](../execution/angular-migration-backlog.json), implemented route catalogue and latest evidence in the execution ledger. They are not completion claims for the whole guide.

### Client profile migration

`GET /api/ui/clients/{id}` composes Application `WorkspaceQuery`. Client-level authorization is required; an engagement-only assignment does not widen to a client profile. Up to 100 recent engagements are individually authorized before projection. Unknown and unauthorized clients share the same failure response. Angular validates the projection, cancels superseded reads and clears client content on session invalidation. Engagement navigation still uses the existing workbench while its panels are migrated. Profile contacts, creation, activation, staffing and budget parity remain open.

### Engagement overview migration in progress

`GET /api/ui/engagements/{id}` independently authorizes exact engagement scope, projects lifecycle/revision and the 100 most recent holds, and rechecks authority before returning. The Angular direct route validates identifiers and decimal-string revision metadata and cancels superseded reads. Clearance, staffing, budget and workbench commands retain existing ownership until their Angular parity is implemented.

### Authorized global search migration

`GET /api/ui/search` composes the existing `GlobalSearchQuery`; it retains its bounded navigation-only coverage and per-result authorization. Angular search uses Material autocomplete, debounced cancelable reads, a slash shortcut outside fields/dialogs, explicit migrated-route mapping and in-memory results cleared on session invalidation. The search controls load as a deferred chunk. Search text is not stored or sent to analytics. Keyboard and cross-scope browser parity acceptance remain pending.

### Client contacts

The client projection now includes bounded contacts and an independently authorized contact-management capability. `POST /api/ui/clients/{id}/contacts` validates antiforgery, resolves current server identity, checks client-wide access and delegates to `PracticeCrmService`. The browser sends the reviewed safety-generation string; the Application service checks it under the client safety-row lock before inserting. A repeated request with that generation is rejected after the first accepted insertion. Angular never retries commands automatically and labels an unconfirmed transport outcome for review. Existing Blazor callers retain their current optional-generation behavior. This is revision fencing, not a persisted command receipt; comprehensive reconciliation, drafts and full feature parity remain open.

### Engagement planning read panels

`GET /api/ui/engagements/{id}/planning` composes existing staffing and approved-budget services after exact engagement authorization. Budget decimal amounts and version numbers are invariant strings; approved-time minutes remain bounded integers. An unavailable approved budget is explicit and is not a zero budget. Unexpected authorization/service failures fail closed. Angular validates this contract and lazily loads the secondary planning panel. Staffing mutations, budget preparation/approval and full planning parity remain pending.

### Staffing commands

Angular staffing now offers bounded staff candidates after a separate scoped management check, requires review of the entire-client-site Full Control warning, and confirms named revocations. CSRF-validated assignment delegates to `StaffingService.AssignAsync`; revocation first checks firm and exact engagement ownership before delegating. Existing rank/certification/self-assignment rules, grant evidence, repeated assignment/revocation handling and session epoch invalidation remain owned by the existing service. Unconfirmed command outcomes are shown for reconciliation rather than automatically retried. Budget commands and full resource-planning parity remain open.

### Budget preparation and approval

Angular planning now prepares budget lines using whole forecast minutes and displays the exact draft currency, rates-derived costs and latest version. Revision submission requires the version string read from the server and delegates to `PracticeTimeService.ReviseBudgetAsync`, whose transaction locks and stale-version check fence repeated requests. Draft approval requires review and is bound to the exact engagement and budget identity before delegating to the existing approval service. Preparers cannot approve their own draft. All unsafe routes validate antiforgery; none retries automatically. Unknown transport outcomes require review of persisted state. Full planning browser parity and scoped draft persistence remain pending.

### Engagement creation and activation

Client-wide Partner/Manager authorization controls the focused native creation
route. A preview binds the current client revision and canonical service, profile
and ISO date-only period; fresh explicit assent is required. New shells remain
blocked. The client safety lock serializes creation, and shell plus immutable
actor-owned request evidence publish together. A deferred guard checks exact
blocked-shell fields, actor epoch, scope and client revision at commit. Reviewed
retries retain the original receipt; a pre-existing service-period engagement
requires inspection rather than being presented as a new creation.

Native Signal Forms retain editable fields only through explicit bounded tab
drafts. Review assent is never persisted. Unknown responses fence navigation and
resubmission; receipt lookup waits behind creation publication. An absent result
allows fresh review of the identical request, while changed intent cannot reuse
its identity. Scope/session loss and route lifetime changes remove protected
content and fence late callbacks. The older creation API remains available for
rollback builds. No Microsoft, portal or professional authority is granted.

Reviewed Partner activation independently requires exact engagement authority,
current unconditional same-service acceptance and no active holds. Creation and
activation use fixed authenticated, no-store API contracts with antiforgery on
commands. Both retain immutable request evidence; retirement and broader story
acceptance remain open. Observed migration and test evidence lives in `status.json`.

### Acceptance checklist

Angular owns the opt-in client assessment route and its checklist projection, exposing questions, current/prior answers, readiness blockers and specialist-review status through a named Application query. Answer commands validate antiforgery and send exact generation/revision strings. The existing Application service checks both values while holding its client safety lock and continues to preserve decision immutability and evidence requirements. Answered/complete never means accepted: professional acceptance remains a human Partner decision. Specialist-review, continuance and decision controls still need Angular migration. No full US-017 acceptance is claimed.

### Acceptance command controls

Angular now offers specialist review requests/results, explicit Partner decisions and confirmed continuance initiation. All contracts validate CSRF and carry the current generation as a string. Specialist results bind exact firm/client/review identities and reviewed status; client-row locking fences stale generations and concurrent requests. Decision submission delegates to existing immutable acceptance evidence and workspace-intent rules, never autonomously accepts a completed checklist. Continuance invalidates the prior generation and requires fresh delta answers. Complete assessment-page parity, browser journeys and production acceptance remain unverified.

### Commercial leads

Angular owns the opt-in `/ui/app/practice/leads` route. A bounded Application lead query filters only the current firm after firm-wide commercial authorization and rechecks that authority before returning. Creation and confirmed qualification validate antiforgery and compose the existing CRM commands. Owner identity comes from the trusted server actor, not browser claims. Existing duplicate-lead checks and qualification gates remain intact; neither action accepts a client or grants access. Opportunity/proposal/document parity remains pending.

### Proposal detail and commercial actions

Angular owns the opt-in proposal detail route with bounded revision history and exact decimal-string fees. The named Application query requires firm-wide commercial authority, validates firm relationships and rechecks access before returning. CSRF-protected commands reuse independent proposal review, status-only sent recording, client response and prospect conversion. These actions never replace professional acceptance. Pricing, document generation, opportunity intake, full source metadata and browser parity remain pending.

### Proposal revisions

Angular now exposes a reviewed proposal-revision form. Fees cross HTTP as decimal strings; dates remain date-only strings. The CSRF-protected endpoint validates bounded fields and composes the existing CRM revision command with the exact reviewed revision. The firm lock and revision fence prevent stale or duplicate replay from superseding a newer proposal. Closed opportunities and accepted proposals remain blocked. Unknown responses require persisted-state review, not automatic retry. Opportunity intake, pricing and document-generation parity remain pending.

### Opportunity intake and initial proposals

The explicit opt-in `/ui/app/practice/leads/{id}` route exposes a named, firm-wide-authorized Application projection capped at the latest 100 opportunities, with latest proposal identities and exact fee/revision strings. Reviewed discovery creation validates CSRF and takes its owner from the trusted actor. A stable request identity becomes the opportunity identity; the existing firm lock serializes same-firm creation. Repetition with matching terms returns that identity, while changed terms or another firm's identity fail closed. This is an additive optional request parameter for existing callers, not a change to lead qualification or professional acceptance. Initial proposals use the existing revision command with reviewed revision zero and require fresh independent review. Unknown outcomes require explicit persisted-state inspection. Pricing, commercial settings/documents and fee agreements still need Angular migration.

### Quotation pricing and matrix approval

The Angular proposal route now defers a quotation workbench with approved rate choices, hour lines, exact decimal-string factors, a server-calculated preview, immutable version breakdowns and recorded approval requirements/results. Fixed HTTP contracts compose named Application projections and existing quotation commands; no EF entities or raw JSON state cross to Angular. Reads cap quotation versions at 100 and rate options at 200. Saving sends reviewed proposal/quotation revisions and exact rate-card identities; the existing firm lock rejects changed rates/revisions. Identical recalculation remains idempotent. Matrix approvals use the exact required firm-wide role and preserve preparer/approver separation. Factor precision is restricted to four decimals to match the existing canonical input hash. Unknown write outcomes hide stale state and require fresh persisted-state review before another action. Commercial document generation, settings and fee agreement parity remain pending; this is not full US-016 or migration acceptance.

### Commercial documents

The Angular proposal workspace now defers a commercial-document panel. A named Application projection selects only the latest 100 artifact metadata rows, never stored document bytes, and explains approved-price/profile prerequisites plus client acceptance, current unconditional risk clearance, Partner authority and approved signature/seal requirements for letters. Brief quotation, engagement-letter and comprehensive-proposal commands validate CSRF and explicit review, then compose existing immutable document-generation services. The reviewed quotation identity and profile revision are checked while holding the existing firm lock. Comprehensive proposals use reviewed firm chapters and actual assigned-team CV/timeline inputs. Historical artifacts retain their original template/profile/quotation/SHA-256 identities; same-version generation reuses the existing artifact. Downloads reuse the existing authorized endpoint, which now rechecks session validity before returning bytes. Visual signature/seal output is not an electronic-signature provider acceptance claim. Settings, fee agreements and full story/browser acceptance remain pending.

### Fee agreements and finance handoff

The Angular proposal route now defers a fee-agreement workspace with persisted agreed fee, server-owned advance percentage, exact-string milestones, allocation/outstanding values, invoice links and final-release state. Creation and engagement linking remain commercial commands; invoice drafting and manual advance payment require exact client-scoped FinanceManager/FinanceReviewer authority. Administration alone grants no finance authority. Bounded engagement candidates belong to the same client; the existing service now serializes engagement links under the firm guard so concurrent links cannot overwrite each other. Payment-reference replay reuses the recorded payment, and changed amount/received-time details are refused. The original billing review/posting, allocation, receipt and notification workflows remain authoritative; queued notification never means delivered. Fixed CSRF-protected endpoints compose these services. Unknown outcomes require fresh persisted-state review before another command. Commercial settings, full module parity and final browser acceptance remain pending.

### Invoice receipts, allocations and credit notes

The native invoice detail uses `BillingInvoiceWorkspaceQuery` for an authorized
history view. Each receipt and credit-note request returns at most 100 rows; the
UI loads older rows on demand with separate timestamp-and-ID keyset cursors. It
composes the existing `BillingService` for receipt recording, allocation to
posted invoices, and credit-note issuance; the UI does not write financial state
directly. Exact decimal strings, explicit review confirmation, server-side
role/scope checks and antiforgery protect each command. Displayed invoice credit
and allocation totals come from the complete persisted balance calculation,
independent of the page currently loaded. Credit-note issuance remains
FinanceManager-only. When a command response is lost, the form stays disabled
until the user reloads persisted state and clears the unresolved draft; the client
never retries automatically. Synthetic PostgreSQL API and Playwright journeys
cover recording, allocation, credit, stable paging across equal timestamps,
authorization, CSRF and lost-response recovery. Wider source-action parity,
production rollout and full migration acceptance remain open.

### Commercial settings

Angular explicitly owns the opt-in commercial-settings route. A named Application query supplies the current profile, bounded active matrix, server-owned default threshold/role and edit authority. Firm-wide commercial users can read; only current firm-wide Administrators/Partners can edit. Profile changes bind the reviewed version and preserve historical document profile identities. Matrix writes/deactivation bind a fingerprint of the reviewed active rules and serialize under the existing firm guard; frozen quotation approval requirements remain unchanged. Forms require explicit review, and deactivation has a named confirmation describing future-rule/default effects. Fixed CSRF-protected endpoints reuse existing Application commands. Unknown outcomes require fresh persisted-state review before another action. Full commercial metadata/form/draft parity and the remaining Angular modules still require work; no complete migration acceptance is claimed.

### Client portal and administration

Native Angular client routes compose `ClientPortalWorkspaceQuery`, `ClientPortalReviewQuery` and `ClientPortalReviewCommands`. Requests retain exact participant/delegation checks, first-sign-in evidence and release/freeze write windows. Conversation entries include chronologically ordered upload receipts. File hashing and same-origin chunk staging preserve exact bytes; after interruption the client reselects the exact same filename, byte count and SHA-256, and an authenticated CSRF-protected command reissues a one-transfer capability only after current participant and write-window checks. The server derives the resume byte offset and next chunk index from locked contiguous receipts, stores only the capability hash, and rechecks the capability after acquiring the chunk row lock. Raw capabilities remain in no-store responses and request memory only. Trusted staff completion and the durable document worker remain separate. A staged upload is never displayed as a delivered SharePoint file. Client package decisions, reviewed document acknowledgements and signed representation uploads reuse the existing human-review and exact-hash services.

The administration overview composes persisted `AdministrationOverviewQuery` results. Role dialogs require an explicit scope review and pass a deterministic digest into the existing firm-locked assignment transaction; current access, reason, replacement grant and timing are fenced. Revocation retains append-only evidence and session invalidation. No AuditSphere role becomes an Entra role.

Tenant connection starts a CSRF-protected single-use consent session through the existing `TenantConsentService`. The authenticated nonce-bound identity leg and exact-tenant checks remain server-owned. Native callback destinations respect API/rollback ownership. Per-capability verification is persisted independently; a green consent return does not prove selected-site or mailbox access. Directory discovery is bounded to one page, exact immutable objects are re-read before binding, and binding grants no role.

Native SharePoint controls compose `SelectedResourceAdministrationQuery`, `Microsoft365ConfigurationService` and `SelectedSiteBoundaryVerificationService`. The administrator projection contains bounded template history and exact resource metadata; credentials, setup capabilities and reusable callbacks are excluded. A reviewed draft edit binds its revision, stays in the configured tenant, refuses active/protected configurations and appends an invalidated selected-resource state. Historical connection/evidence records remain intact. Verification uses the existing Infrastructure probe for the saved resource and an unrelated synthetic-site denial; browser-entered PASS evidence is not accepted. Activation binds the reviewed draft revision and approved client template, and preserves the existing trusted consent, exact-resource and fresh boundary-evidence gates. These controls add no Microsoft Graph permissions.

Folder manifests retain immutable versions, bounded allowlisted nodes, concurrency review and append-only administration events. The STE client manifest permits an empty intermediate tree so the durable worker can use the direct client/year layout; engagement manifests still require folders. Local template approval never means SharePoint folders were created. Native client/engagement folder provisioning uses bounded administrator pages and an exact target/resource/template review fingerprint. The server rechecks the reviewed plan before dispatch and after provider I/O, refuses stale epochs and changed bindings, and records the actor, reason and scope. A failed disposable read-back remains FAILED despite an accepted HTTP command. Unknown responses require persisted-state refresh and fresh review; they are never automatically retried. Dedicated client-site status is read-only in Angular: site creation and whole-site staff Full Control reconciliation stay in the isolated privileged worker. The UI distinguishes site, membership, last verification, operation and required action; only verified safe SharePoint URLs become links. Separate policy configuration and full administration acceptance remain open. Direct staff collaboration continues to require human ACL review; merely saving its access-profile metadata grants no Microsoft access.

Optional workforce creation and guest invitation have native reviewed wizards backed by the existing `DirectoryProvisioningService`. Controls require independently enabled, fresh verified capabilities; the API repeats authority, capability, scope and CSRF checks. Guest roles are fixed to `ClientUser` and client/engagement scope. A created workforce password exists only in the original response and an ephemeral masked control; it is excluded from result history and cleared after a short lifetime, visibility change, session invalidation or dialog destruction. Repeated creation and operation recovery never return it again. These workflows reuse the existing permission matrix and add no Microsoft permissions.

Managed-group dialogs cover explicit allowlist approval, bounded member pages, an observed membership preview, reviewed add/remove and local allowlist retirement. Role-assignable/dynamic groups are refused. The mutation carries the observed membership state; a changed state requires a new review. Group reads and changes require the configured tenant and current firm administrator. Microsoft membership never grants local AuditSphere authority. Recovery reviews persisted original intent and reconciles unknown outcomes or finishes an accepted local binding; it never blindly redispatches creation/invitation. A pre-dispatch authorized request still uses the existing original-key submission contract; rebuilding that intent after losing the form remains a parity gap.

The shared Angular client discards late on-demand reads, command receipts and downloads after a session generation changes. A lost command response fences further page commands until persisted-state reconciliation; reloading alone is not evidence of a confirmed outcome. Feature-specific durable operation receipts remain the server authority. See the execution ledger for the actual tested scope.

Worker workspace receipts now bind the exact resource, credential reference fingerprint, approved manifests and deterministic naming inputs. Local folder ownership publication is serialized by exact tenant/drive through a transaction-scoped PostgreSQL advisory lock after provider I/O; it does not upgrade the existing durable worker firm safety read guard. A changed plan or already-owned client folder blocks publication, and existing get-or-create reconciliation derives a fresh plan. Legacy receipts lacking this plan fingerprint fail closed and need operator-reviewed reconciliation. No folder deletion or privileged site creation is added to the API.

### Confirmation workspace

The [curated confirmation action audit](../execution/angular-confirmation-parity.json) records implemented controls and remaining dispositions independently from the generated Razor discovery inventory.

The native engagement confirmation route composes `ConfirmationWorkspace` and the existing fieldwork/deliverable services. The register is paged with outstanding and critical filters. Case review pins current response and alternative revisions, identity, actor/session epoch and register generation. Reviewed commands take firm/client/engagement and case locks and participate in one local transaction. Creation refuses duplicate source/date identities; an uncertain response fences further writes until persisted-state refresh and fresh review. There is no external-send command or delivery claim.

Prepared/approved cases remain not dispatched until an observed channel reference is recorded. Late response observations append a revision and require independent current-response review. A reviewed nonresponse alone does not satisfy closure: a substantive independently reviewed response or current independently reviewed alternative work is required. Criticality requires explicit rationale. The native completion table links to the reviewed workspace instead of toggling criticality with an invented explanation. Confirmation amounts retain exact strings; inputs outside the existing database precision are refused before persistence. Frozen files refuse mutations. Atomic bulk preparation and retained closure decisions are implemented below. Full story and controlled external acceptance remain under review; scoped tab recovery is described below.

### Reviewed confirmation batch preparation

The native confirmation page supports multi-case preparation with shared area, currency, date and an optional applicable procedure. Signal Forms validate every required case, retain exact decimal strings and clear review when the intent changes. Case selection is bounded; each source record must be unique after trimming. The API validates the entire request before dispatching to Application. `ConfirmationWorkspace.CreateBatchAsync` takes the existing firm/client/engagement lock order, rechecks current authority and the reviewed register fingerprint, enforces frozen-file guards and refuses an existing source/date identity before composing the existing batch service inside one transaction. A failed row publishes no cases; concurrent requests with the same reviewed register can publish only one batch. Preparation remains local DRAFT evidence, never observed dispatch. Lost outcomes require persisted-state review before another submission. Closure-conclusion evidence is now retained as described below. Scoped confirmation tab-draft restoration is described below.

Responsive confirmation forms no longer impose an intrinsic minimum width on the mobile shell. The single-column grid and main content can shrink; wide registers retain contained horizontal scrolling. Expanded batch forms remain usable at narrow widths.

### Retained confirmation closure evidence

Closing a case now retains the human conclusion and an append-only snapshot of the exact case, current response and current alternative evidence. Closure takes the case lock, rechecks scope, refuses a second closure and requires independent review of the current evidence. Alternatives use the same timestamp-and-ID ordering as the native detail projection; an older reviewed alternative cannot clear a newer unreviewed revision. The API exposes only the retained conclusion, actor, timestamp and evidence digest, through the existing exact engagement authorization. Historical closed cases without a closure record are explicitly labelled as missing retained evidence; no backfill invents professional conclusions. The additive `ConfirmationClosureEvidence` migration adds scoped foreign keys, a unique case decision and an update/delete refusal trigger. Apply it through the approved deployment migration procedure before starting the upgraded API. Local synthetic migration tests do not establish production deployment acceptance.

### Confirmation draft storage and navigation

Confirmation case, batch and action forms use explicitly saved, versioned **session/tab storage**, with a four-hour expiration and bounded allowlisted fields. The envelope pins firm, user, session generation, exact engagement/case/action and the authorized register or case revision. It contains only unsubmitted intent; review confirmation, authentication material, files and reusable capabilities are excluded. The reviewed-command action also checkpoints its intent with a pending-submission fence before dispatch. No server draft endpoint or durable-save guarantee is implied. The existing generic browser draft primitive is unchanged and is not evidence of cross-feature draft acceptance.

Recovery is explicit after a fresh authorized read; a different identity, unsupported schema, expired envelope or changed base cannot be silently applied. Recovered values require new review. Tabs have independent session storage; edits from another tab that change persisted evidence invalidate the current base. Background refresh retains in-memory intent, clears review and blocks submission until the user explicitly chooses the refreshed revision and reviews again. Unknown submissions recover with a persisted-state refresh fence and no automatic retry.

A Material dialog protects confirmation route, filter, page, case, action and explicit refresh changes. It offers keep editing, save in this tab and continue, or discard and continue. A save failure retains the page and reports that edits remain in memory only. Browser unload uses the standard unsaved-change warning; it cannot promise recovery after tab closure. Session invalidation removes protected UI state and attempts to clear all versioned tab drafts; unavailable browser storage is never represented as successfully written or erased. Legacy unversioned drafts are not imported. Broader module draft migration, source-action audit and final migration acceptance remain open.


## Native currency remeasurement

The standalone API composes `CurrencyRemeasurementWorkspace` around existing calculation and evidence services. Preparation and independent approval require fresh, actor/session-bound reviewed input digests. The local transaction takes firm/client/engagement guards, rechecks professional authority, and preserves file-freeze and source-lineage gates. Concurrent identical preparation resolves to the same workpaper. Failed revalidation persists the existing STALE disposition. No journal or professional conclusion is inferred from a calculation.

Input reads exclude expired grants and enforce exact client/engagement ancestry. Periods, policies, approved rate versions, rate observations and GL choices have explicit interactive limits; the GL selector labels a truncated window. Draft bases include approved rate/policy observations, the profile/period and selected-source lineage. The native detail shows source and functional currencies, direction, rate date/type, version/provenance, classification, prior carrying amount, movement and rounding. Historical schedules lacking a recorded immutable GL link remain readable with an explicit inspection-only blocker; no link or approval eligibility is invented. Same-currency identity applies to the existing translation contract; foreign-currency-only remeasurement eligibility is preserved.

Angular uses native Signal Forms, exact bounded decimal text and explicit reviewed submission. Remeasurement intent uses the versioned tab-draft policy above, scoped to the exact period and engagement. Recovery never restores review assent. Context changes clear previous line fields only after the keep/save/discard choice; refresh retains edits while changed approved inputs block submission until explicit rebase and fresh review. Session invalidation withdraws forms, detail and drafts. An unconfirmed submission is saved as pending intent where tab storage is available and cannot be replayed automatically.

The standalone API, native browser journey and Angular component tests cover this workpaper vertical slice. Currency intake review and the complete source-action/rate-method editor parity remain separate open acceptance items; this section does not retire the legacy presentation.


### Native intake currency comparison

The Angular trial-balance intake composes a dedicated native Signal Forms currency panel. Its read-only contract shows the exact dataset/client/engagement/period, source and presentation currencies, declared upload closing-comparison method, rounding rule, current/prior rate purposes and dates, direction, provenance, approved set/version and observation identity. Same-currency identity consumes no market observation. Exact plain decimal thresholds and source amounts stay strings in the browser. Movement flags are review indicators; this panel grants no accounting or mapping approval and does not substitute classification-based translation or monetary/historical remeasurement.

The existing Application query filters prior candidates by current firm/client/engagement grants before selection. An engagement-only assignment cannot see a sibling engagement's prior balances or identity, while a valid client-wide/firm-wide assignment can include authorized prior work. Expired wider grants never qualify. Source sealing, exact period ancestry, bounded account aggregation and latest approved rate validity fail closed. An invalid latest direction/effective interval cannot silently use an older positive observation. Final authorization and current/prior source, period and rate rechecks refuse a changed read before publication.

The native panel limits rendered pages, labels a missing authorized prior comparison, and removes the previous result before a refreshed calculation. Changed filters mark displayed results stale; late or wrong-context responses cannot repopulate changed datasets or revoked sessions. Missing rates explain the required input correction without inventing approvable figures. Upload and mapping commands remain their existing separate workflows. Complete intake command/form/draft parity, rate/method editor parity, and production/accessibility acceptance remain open. The Web rollback/reference source is restored, but parity and operational rollback gates remain open; retirement acceptance remains `NOT_READY`.


## Native currency configuration

The firm-wide FX rates and policies page prepares rate-set drafts, exact DIRECT observations and
translation-policy drafts through `CurrencyConfigurationWorkspace` and the existing
`CurrencyTranslationService`. The standalone API owns authorization, CSRF validation, reviewed
revision checks and serialized local writes. Only existing firm-wide currency-review authorities
can read the bounded catalogue; engagement-only access does not expose firm configuration.

The editor declares closing, average and historical observation purposes. Rates are submitted as
plain exact decimal strings within the storage precision, with explicit date, pair, direction,
source and effective range. Same-currency translation remains identity treatment and never creates
a market observation. Invalid stored observations cannot be approved through this workspace.
Maker/checker identity and timestamps stay on the existing records; approved records have no edit
or delete action. New configuration uses a new code under the existing uniqueness contract.

Signal Forms bind editable intent and review assent. Submission checks the exact reviewed intent
and base revision. Identity/session/context-bound tab drafts restore bounded intent only and require
fresh review. Changed contexts protect unsaved edits. Unknown outcomes fence new commands until a
successful persisted-state refresh and explicit reconciliation; commands are never automatically
replayed. Server and browser regression checks, publication and remaining acceptance are recorded
in the execution ledger. This vertical slice does not establish complete migration or Blazor
retirement acceptance.

### Reviewed multi-period intake

`TrialBalanceUploadWorkspace` composes the existing split/parser and guarded source importer.
The native API requires exact file/revision assent and offers read-only receipt reconciliation.
An import is a sequence of per-period publications, not an atomic browser batch. Each source
receipt and existing durable validation operation remains independently observable. A current
period revision is rechecked under its publication lock; stale or closed periods fail closed.
The existing CSV parser bound is shown separately from the multipart transport bound.

Angular `tb-upload.ts` uses native Signal Forms, metadata-only tab checkpoints and navigation
protection. Recovery never stores file content or restores authorization assent. Unknown results
require current persisted receipts and explicit acknowledgment; there is no automatic write retry.
`tb-intake.ts` refreshes its authorized dataset selector without destroying the receipt panel.
`MappingMemoryService` filters prior history by active grant scope before selecting or counting;
sibling engagement history cannot widen an engagement-only assignment.

### Source inspection and direct CSV export

`TrialBalanceSourceWorkspace` composes bounded row and validation-issue queries for one sealed,
authorized source. The native intake uses independent server pages, a bounded account-prefix
filter and exact decimal strings. Stored period/source identity, revision, digests, sealing and
validation remain visible; missing source totals do not become zero. Persisted issues are scoped
to the source revision. Source inspection does not grant GL completeness, acceptance or approval.

The standalone API exposes an antiforgery-protected CSV read pinned to the observed source
revision. Application limits rows and UTF-8 bytes, escapes text formula prefixes and rechecks
source state and current authority before release. Angular verifies file identity/revision,
content type, byte bound and active context before saving. A changed session or destroyed view
cannot save a late file. Larger exports require a separately supported durable export workflow;
there is no unbounded download fallback. Verification and remaining guide acceptance live in
the execution ledger.


## Reviewed trial-balance source acceptance

The Angular route `/ui/app/accounting/sources/{id}/acceptance` composes
`SourceAcceptanceWorkspace` through the standalone API. The existing append-only decision
selects a TB source per engagement/source kind, across reporting periods. The page exposes
that boundary, the old and proposed pointers, source identity, importer, worker validation,
input generation and retained reviewer evidence. It never interprets sealing or worker validation
as a professional conclusion.

Reviewed writes bind current metadata, selection and actor/session under firm/client/engagement
and source/period locks. The existing command now enforces importer/reviewer separation for TB
and GL, rejects closed periods and missing identity digests, and rechecks professional authority
before committing the decision and generation increment together. No new Microsoft permission
or database schema is introduced.

Native Signal Forms preserve evidence-reference intent through explicit, bounded tab drafts.
Pending/stale checkpoints fence writes on reload; recovery never restores assent. A persisted
read plus acknowledgment is required after an unknown outcome, and no command is automatically
retried. Protected data is cleared on identity changes or failed reads. Native GL upload and
completeness are covered by the subsequent slices below; reviewed mapping editing and full
story/retirement acceptance remain pending.

### Native general-ledger inspection

The exact engagement route exposes a bounded sealed-GL source catalogue, server-applied
account/posting-date/journal/counterparty filters and deterministic line pages. The
Application workspace composes existing GL queries and independent selected-source reads;
API handlers resolve the trusted actor and recheck the session before response. Journal
reads return every line only within the interactive limit, refusing oversized sources
without a partial verdict. No permission, identity, financial model or durable-operation
boundary changes.

Angular retains exact decimal strings and uses native Signal Forms for filters. Context
changes cancel reads and clear source/journal selections; failed refreshes remove prior
results. Edited filters hide the obsolete population until applied. Sealing, arithmetic
balance, independent source acceptance and completeness are separate facts. This read-only inspection slice creates no acceptance/completeness evidence or professional
conclusion; the subsequent native source-review and completeness workspaces are described below.

### General ledger independent source review

The native GL inspection links `/ui/app/accounting/gl-sources/{id}/acceptance`.
Application review exposes real batch/period/book/parser/profile metadata and independent
GL decisions. Its reviewed revision is rechecked inside the existing serialized acceptance
transaction. The same native review component preserves fresh assent, explicit evidence-only
tab recovery, navigation guards and unknown-outcome reconciliation, with separate draft keys
and runtime decoders for TB and GL. Source-kind-specific selected pointers remain independent.
GL sealing or acceptance does not assert account-exact completeness; that proof and review
retain their existing downstream gates. Native GL import controls are covered by the subsequent bounded CSV slice below.

### General ledger completeness workspace

The native GL inspection links `/ui/app/accounting/gl-sources/{id}/completeness`.
The standalone API composes `GeneralLedgerCompletenessWorkspace` with the existing analysis
service, durable store and worker handler. Source lists contain only exact-scope compatible,
accepted, sealed and balanced closing/prior-period TBs. Plan and proof revisions are read
again and checked under ordered parent/reporting-period locks. Commands require antiforgery,
current professional authority and fresh reviewed intent. The existing normalized operation
payload and idempotency contract are preserved. Observations are filtered to the exact GL/TB
pair before bounded loading; prior requests require Operations review rather than a duplicate.

The worker computes the retained proof; an independent authorized reviewer records approval
or rejection transactionally. Final session/authorization checks roll back late revocation.
A missing opening account is unknown, including when an omitted zero amount would appear
arithmetically reconciled. Account pages show exact server decimal strings and nullable
opening/roll-forward residuals; no browser calculator or fabricated professional verdict exists.
Immutable source identities, proof digests, preparer, reviewer and evidence remain visible.

Drafts store only bounded source/bridge references, action and evidence text in the current
tab. Assent is never restored. Context changes and failed reads remove protected results.
Lost response checkpoints require an explicit persisted-state read and acknowledgment before
more writes or navigation. Completed calculation, extract coverage, independent approval,
source selection and professional conclusions are separate facts. The bounded native GL CSV
import is described below; wider intake/form/retirement acceptance remains open. Verification
facts live in the execution ledger.

### Native general-ledger CSV upload

`GeneralLedgerCsvProfile` defines a bounded, strict UTF-8 CSV profile with explicit required
and optional columns. It preserves ISO dates, exact supported decimal strings, immutable
journal/line identities and original-currency amounts; it does not infer values, round money
or perform currency conversion. One source must describe one exact period/book/entity and
functional currency. The interactive cap is deliberately smaller than the existing chunk
importer; other profiles and large-file controls remain open.

`GeneralLedgerUploadWorkspace` authorizes the exact engagement before parsing, resolves
client-local period/book/chart context and composes the existing GL validation/import service.
Preview is read-only. Import rechecks a reviewed exact-file/context digest under parent and
period locks, reuses an exact retained source after concurrent same-file requests, and checks
current authority/context before commit. Equivalent normalized rows with different file bytes
are a conflict. Receipt recovery neither imports nor changes local selected-source pointers.
A sealed GL source remains separate from independent acceptance and completeness.

The native upload route is linked from GL inspection. All monetary values remain strings;
only the first bounded sample is rendered although the server validates the complete file.
Explicit tab checkpoints reuse `TabDrafts` and the existing file metadata allowlist, bounded
to this profile. The checkpoint base binds the current user/firm/session/engagement/client;
file bytes and assent are excluded. A reselected file must match the pending exact hash and
size. Unknown writes cannot repeat until a persisted read and manual acknowledgement.
Storage failure prevents dispatch. Navigation and invalidation clear or fence protected state.

### Independent mapping approval

The native mapping workbench links a dedicated `/ui/app/accounting/mappings/{id}/approval`
review. `MappingApprovalWorkspace` reads the exact authorized mapping, sealed raw source,
complete bounded allocations, client chart, approved taxonomy, period/book and safety
generation. Its revision binds actor/session and actual source/applicability metadata. It
reuses the existing allocation and taxonomy validators; incomplete splits and unsupported
precision remain authoritative refusals. Oversized review populations fail closed.

`FinancialStatementService.ApproveMappingAsync` rechecks native reviewed intent under its
existing firm/client/mapping locks and checks current authority after saving. Approval and
input generation roll back together after late revocation. Both standalone API approval
endpoints require fresh review. Existing Application callers keep their compatibility
contract; no approved mapping history or financial books are rewritten.

Native Signal Forms never restore review assent. A metadata-only tab checkpoint is required
before dispatch. Lost or malformed responses fence further commands/navigation; recovery
requires a current persisted read and manual acknowledgment. Protected results disappear
on failed reads or session invalidation. The workbench labels approval as retained history
and links current applicability rather than inferring package eligibility from an approved
status. Wider story acceptance remains separate from this bounded implementation.

### Native mapping batch creation

The mapping workbench links `/ui/app/accounting/mappings/{id}/edit`. Its Application
workspace reads authorized source accounts in bounded pages and exposes only posting
destinations in the exact approved taxonomy. Split fractions remain plain decimal strings;
the server validates the complete proposed mapping. Selected-account and tab-separated
changes require an explicit preview before staging. Local undo affects unsaved edits only.
Native table buttons use arrows for row navigation and Enter/F2 for the focused editor;
normal form typing and Tab navigation retain their ordinary behavior.

Creation requires a fresh reviewed batch bound to source, chart, taxonomy, period/book,
generation, mapping lineage and actor/session. The serialized Application transaction
retains a new version, all allocations and an exact creation receipt together. Its final
authority check rolls the entire version back after a late epoch change. Concurrent exact
requests reconcile to the same receipt; another intent cannot reuse that request identity.
Receipt metadata and native allocations have PostgreSQL immutability guards. Approval
remains a separate independent decision. Legacy Application signatures stay compatible.

Explicit editable drafts stay in the same browser tab and expire; they exclude review
assent and source balances. A pending creation checkpoint contains only request identity
and hash. After a fresh authorized base read, that metadata may identify a receipt even
when the base changed; stale editable fields cannot be restored through this path. An
unknown result fences new requests and navigation. Recovery reads and acknowledges the
persisted receipt, or explicitly previews the exact retained request against an unchanged
base before fresh assent and an idempotent retry. No automatic retry or field rebase occurs.
Changed context retains current local fields and requires a new complete review.

The additive native-receipt migration is exercised only by owned local test databases.
It was not applied to the shared Development database or production. Migration rollback
refuses deletion of retained native receipts. Broader accounting form parity, high-volume
performance/accessibility acceptance and production rollout remain open. The route
cutover does not establish accepted rollback readiness; the Web rollback source is
restored but has not passed an operational rollback exercise, and the retirement gate remains `NOT_READY`.

### Native statement contribution review

The statement route now uses a named read-only Application workspace. It shares the existing
allocation calculator and financial-statement projection, then provides server-paged statement
lines, exact account contributions and relevant procedure pages. The complete statement total
remains independent of the visible filter. The review binds the exact approved mapping, raw
sealed source, chart/taxonomy applicability, reporting period/book, current input generation
and actor/session. A second read and final authority check reject changed or revoked contexts.
Oversized populations, incomplete allocations and unsupported sections fail closed.

URL state contains bounded section/filter/page/row metadata and a non-secret basis hash.
Returning from supporting procedure evidence refreshes the authoritative basis before restoring
the prior row. Changed-basis details and evidence stay hidden until explicit acknowledgment;
failed detail reads also hide old totals. The exact source opens within the same workspace,
using its own scoped source query. Procedure results and linked files retain their independent
server authorization. Missing evidence is labelled separately from zero contribution.

The complete contribution CSV is an explicit authenticated, antiforgery-protected export.
It carries the exact source/mapping basis, checks revision and bounds on the server, and
protects text against spreadsheet formulas. It does not issue a financial package, record
a professional conclusion or grant source/mapping approval. Complete migration acceptance,
production-like performance and deployment rollback readiness remain open. The local route
cutover does not establish rollback readiness; the Web rollback source is restored but has not
passed an operational rollback exercise, and the retirement gate remains `NOT_READY`.

### Native journal revision lifecycle

The existing journal detail route now composes an Application workspace for exact line edits,
submission, return, independent technical posting and a linked reversal draft. Each action needs
an explicit server preview and fresh review of the exact source, reporting period/book,
currency, purpose, rationale and evidence. Amounts are plain decimal strings with no rounding;
invalid precision, source accounts or unbalanced totals block confirmation. Review requires an
authorized practitioner other than the preparer. Group-only eliminations remain in consolidation;
client-book correction retains its separate management-evidence gate.

The local transaction serializes the journal and source, preserves current professional scope,
file-freeze and period guards, and retains immutable before/after line snapshots and an actor-owned
request receipt together with the transition. Final authority loss rolls back both. Identical
concurrent requests resolve to the same retained receipt; a changed intent cannot reuse its
identity. Database guards permit draft/returned line correction and reject changes or moves of
submitted/posted lines. The timeline shows observed native events only; legacy history without
retained evidence is labelled rather than reconstructed.

Explicit tab drafts retain bounded editable fields for the same identity, session and review
basis, excluding review assent. Unknown responses block new actions and navigation. Receipt
verification and acknowledgment recover a committed result; absence permits only an explicitly
reviewed retry of the identical request. Scope/session changes clear protected views. Exact source
inspection remains independently authorized. Controlled instructions verify current revision and
management evidence; downloading them is neither external posting nor package application.

The additive action-evidence migration and returned-line guard are exercised only in owned test
databases. Rollback refuses deletion of retained action evidence. Native creation is described
below. Native management response is described below. Reflection/application controls, broader
accounting parity and production-like acceptance remain open. The route cutover does not
establish rollback readiness; the Web rollback source is restored but has not passed an
operational rollback exercise, and the retirement gate remains `NOT_READY`. Executed verification lives in `status.json`.

### Native journal creation

Sealed source inspection links to an explicitly owned Angular draft-creation route. Its scoped
Application context binds the source revision/digest, reporting period/book, currency, basis,
entity, safety generation and actor/session. The preparer chooses an existing supported purpose
and origin and enters exact source-account lines, rationale and evidence. A complete server
preview identifies invalid lines and imbalance before fresh assent. Group-only eliminations
remain outside this workflow. Creation grants no technical approval, management consent,
external posting or financial-package application.

The serialized local transaction creates one draft and its immutable original-line event
together. Concurrent identical requests reconcile to that event; changed intent and duplicate
source/journal numbers are refused. Optional supersession names an exact posted/returned
journal and reviewed revision in the same reporting context. The prior journal is locked and
revalidated and remains unchanged. Final authority loss rolls back both the new journal and
its event. The retained event uses the explicit pre-creation state `NOT_CREATED`; the timeline shows
“Before creation”, with no invented prior journal or balances; later edits preserve the original creation snapshot.

Explicit tab fields exclude assent and source balances. Pending-reference reconciliation
blocks another creation and navigation after an unknown response. Opening a recovered draft
requires acknowledgment of its retained receipt. The creation-evidence migration preserves
append-only events and refuses rollback while such evidence exists. Owned local tests exercise
it; shared Development and production are not migrated. Management/client-response and
reflection/application controls, wider migration acceptance and retirement remain open.


### Native journal management response

The client portal exposes a bounded, currently authorized journal queue and an exact-revision
management review. The queue checks the exact client/engagement parent and excludes group-only
eliminations; those records remain outside both client and offline staff management routes.
A signed-in client can accept, reject or partially accept the reviewed draft
only after required first-sign-in completion. The separate staff route records evidenced offline
management disposition; its identity never represents client authentication. Evidence mode is
derived by Application from the current user and fixed route contract, not from browser input.

A complete source/period/currency/line review and explicit fresh assent precede recording.
Partial acceptance requires evidence identifying accepted and rejected portions and never
applies a remainder automatically. Scope, session epoch, professional work, period/book, source,
safety and file-freeze guards are rechecked in the serialized transaction. The existing local
management command and immutable action receipt commit together. Identical concurrent requests
recover the original result; changed intent is refused. Database guards bind the exact current
draft revision and protect the decision, its reviewed context and lines after recording.
A changed treatment needs a new linked journal.

Management disposition leaves technical state unchanged. Submission, independent technical
posting, source-reflection reconciliation and financial-package eligibility/application are
separate decisions. Signed-in client identity and staff offline evidence remain distinguishable
in the retained record and journal history. Unknown browser acknowledgments fence further writes
and navigation until scoped receipt reconciliation and explicit acknowledgment. Staff tab recovery
stores only bounded editable fields and request metadata, excluding assent; client response fields
remain in memory. Current session/scope loss clears protected context and fences late responses.

The additive management-evidence migration is exercised only in owned synthetic databases.
Shared Development/production migration application and full story, QA, cutover and retirement
acceptance remain open. Observed verification and initial failures live in `status.json`.

### Native adjustment-plan eligibility review

A server-paged plan queue links a dedicated native review. It reads the exact authorized
client/engagement/source parent and complete bounded plan membership, then pages presentation.
The Application eligibility report retains planned and current reflection separately. Lost,
unknown, partial or changed reflection blocks applicability; an ambiguous logical journal
revision and a group-only elimination cannot become an eligible client adjustment. Structured
canonical membership identities include layer, technical state and planned/current decisions.
Oversized populations are refused rather than silently returning partial eligibility counts.

The review includes reporting context, current blockers and separately labeled retained
calculation results. A finalized status is retained evidence, not current release authority.
The queue excludes held or professionally blocked engagements before paging and rechecks
authority even for an empty result. Each returned plan and direct review enforces exact
professional scope. Double reads fence changed supporting state; current scope/session loss
removes protected content. The screen is read-only and exposes no placeholder application command. Native plan
creation/finalization and wider journal/application story acceptance remain open. No schema,
Microsoft permissions, financial books or grants change. Executed evidence is in `status.json`.

### Reviewed native budget preparation

The scoped budget preparation API composes `BudgetPreparationWorkspace` and the
existing `PracticeTimeService`. Server preview binds the exact normalized budget
lines, expected version, approved rate identities, exact forecast values, actor
session and current engagement/client context. Confirmation revalidates that
snapshot under the existing firm/client transaction locks. Same-intent retries
return the original immutable receipt; changed intent cannot reuse the request.

The additive preparation evidence table has append-only and deferred publication
guards. Retained evidence blocks destructive schema rollback. Preparation publishes
a draft only; independent budget approval and professional acceptance remain
separate. The budget endpoint remains available as an API contract.

Angular keeps editable tab drafts separate from pending request references. The
reference is saved before dispatch; an unknown acknowledgement fences new changes.
Reload first reads current authorized planning and then offers explicit receipt
reconciliation, never an automatic command retry. Reconciliation waits behind
in-flight guarded publication. Editable recovery excludes assent, calculated money
and execution. Staffing/approval request parity and complete planning Signal Forms
remain open.

### Planning form and navigation boundaries

Native engagement planning uses Signal Forms for budget fields, staffing selection
and review confirmations. Assent is memory-only and bound to the exact current
selection, preview or draft plus session/engagement ownership. It is never restored
from editable tab drafts. Immediate command guards reject changed review context;
native checkboxes reset when context changes. Select labels have explicit association.

Navigation offers keep, budget-only tab save or explicit discard. Storage failure
keeps editing; staffing and approval choices cannot be saved as budget fields.
Same-engagement hold paging preserves the editor. Late dialog results cannot save
under a changed identity or engagement, and busy/unknown writes fence departure.
Tab departure warns without trapping sign-out. Application services retain all
business authority. Backend staffing/revocation and approval request recovery are
still incomplete; full planning parity and UI retirement acceptance remain open.

### Planning action authority

The Application planning projection reports `canPrepareBudget` through the same
Partner/Manager authorization used by reviewed budget preparation. Angular uses
it for editing, tab drafts, preview, confirmation and receipt recovery; absent
flags invalidate the projection. Staffing retains its own permission. Draft
approval is displayed separately and preserves the existing independent
Administrator/Partner/Manager service authority and self-approval refusal.
Staffing permission grants no additional budget-preparation authority.

### Reviewed budget approval recovery

The native approval API previews exact latest draft lines, currency, version and
client/engagement generations. Separate confirmation binds that preview to the
current actor/session. Application execution joins the existing independent
approval service in a guarded transaction and stores an immutable actor-owned
request receipt. Self-approval and stale scope/session/review are refused.

Angular retains only a bounded request reference in tab storage. Unknown outcomes
require explicit receipt lookup; reload never automatically retries approval.
Acknowledgement refreshes current authorized state. Protected preview and receipt
content clears on context loss. A deferred database guard verifies receipt-to-
approved-budget linkage and an append-only trigger protects retained evidence.
The approval migration is a deployment prerequisite; it has only been exercised
on disposable local test databases. Rollback refuses retained approval evidence.
Staffing/revocation recovery and wider migration acceptance remain pending.

### Staffing transaction foundation

`StaffingService` joins an existing transaction without committing it; a caller
must roll back on refusal. Direct callers retain service-owned commit/rollback.
Firm then client safety locks precede actor authority and target identity locks.
Assignment and revocation recheck current scope/rank under those locks and current
authority before publication. Independently assigned role grants survive staffing
revocation, while owned grant revocation retains evidence and session invalidation.
Client-site membership remains an explicit pending external reconciliation. This
foundation does not implement reviewed staffing receipts or automatic retries.

### Native reviewed staffing recovery

The Angular team editor now composes staffing-change preview/confirmation/receipt
endpoints. Separate Signal Form confirmation binds all access effects to the exact
current preview and identity/context. Session tab storage retains only a bounded
request reference; lost-response or reload recovery performs receipt lookup without
retrying mutations. Receipt acknowledgement refreshes current authorized staffing.
Protected editor state clears on session/scope/context loss, and unresolved writes
hide the stale team projection and fence competing actions/navigation. SharePoint
membership remains a separately reconciled external operation.

## Native prospect conversion review

Accepted proposals can now open the native reviewed prospect-to-client workflow.
It includes bounded identity fields, explicit same-context tab draft recovery,
canonical-client conflict/reuse preview and fresh confirmation. Restricted-profile
text is excluded from tab persistence. Conversion and its immutable receipt share
the existing Application transaction; unknown responses reconcile by actor-owned
request reference. Professional acceptance, engagement activation and portal
invitation remain separate authorities. This slice has focused local verification;
assessment, remaining quality gates and retirement acceptance remain pending.
