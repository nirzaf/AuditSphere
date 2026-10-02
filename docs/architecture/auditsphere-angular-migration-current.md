# AuditSphere Angular presentation migration

**Status: CURRENT for implemented API/Angular boundaries; full migration acceptance remains PARTIAL.**

The owner requested implementation of the attached Blazor-to-Angular guide. Angular replaces presentation incrementally. The .NET modular monolith remains the capability, session and database authority. The request does not authorize a NestJS rewrite, additional writers, different identity authorities or retirement of Blazor before parity acceptance.

## API and Angular project boundary

The owner subsequently requested an ASP.NET Core API backend and a separate Angular UI project. `AuditSphereOps.Api` owns the HTTP host, authenticated API contracts, consent callbacks, protected file transports, provider composition and health checks. `AuditSphereOps.Ui` owns Angular presentation. Domain, Application, Infrastructure and Worker retain their existing responsibilities.

The legacy Web host references the API host composition during coexistence; it adds only Razor/MudBlazor presentation and circuit actor resolution. API has no reference to Web, Razor components or MudBlazor. Both hosts use the same authorization and authentication implementation. API defaults to the Angular build, with explicit SPA routes. It never routes API/auth/health failures into the SPA.

Same-origin delivery remains the supported production boundary: build Angular separately, publish its browser artifacts with API, and terminate HTTPS at the approved host. The Angular development server proxies API/auth calls; no permissive cross-origin policy or browser bearer-token store is added. Existing private development user secrets remain in the approved `AuditSphereOps.Web.Development` namespace for both hosts; no secret is copied into project configuration. Independent local API publication has been checked. Production deployment and full UI retirement still require acceptance.

## Implemented ownership

- Angular workspace: `src/AuditSphereOps.Ui`, standalone Angular with Material/CDK, zoneless change notification and lazy capability routes.
- Shared Material typography uses an installed system font stack for both the shell and body-mounted dialogs, with no external font dependency.
- API serves only the explicit `/ui` route catalogue shared by `UiEndpoints.SpaRoutes` and Angular `app.routes.ts`; a contract test prevents catalogue drift. Native families include practice, accounting, consolidation, audit, completion, administration and the restricted portal. Auth, consent callbacks, API, protected file transports and health keep their HTTP owners. Unknown routes are not a blanket SPA fallback.
- `AngularUi:Enabled` defaults to true in API and false in the rollback Web host. Enabling requires a production browser build. `AngularUi:BuildPath` optionally supplies its directory. Local publication bundles browser artifacts under `ui/`; API includes no Web/MudBlazor dependency. A backend-only publication is explicit and must disable local SPA serving or supply approved assets.
- Angular navigation stays within its declared routes. Search validates destinations against that same catalogue, handles multiple hits sharing one destination and shows an unavailable state for an unowned page. No migrated control links to `/app` or `/portal` on the API host. The Web rollback host remains directly available; it is not a hidden frontend fallback. Skip navigation focuses the current main content without changing workspace context.
- API owns `/api/ui/session`, `/api/ui/portfolio` and CSRF-validated `POST /api/ui/sign-out`; the legacy Web host reuses these endpoints. They resolve trusted cookie identity and current epoch, never accept browser actor/firm/role authority, and return no-store responses.
- Initial setup has a native `/ui/setup/microsoft365` page and cookie-authenticated `/api/setup/session` / `/api/setup/bootstrap` contracts. Only the exact deployment-approved Microsoft tenant/object identity can submit the installation proof with CSRF protection and explicit review. Application onboarding performs the local binding and evidence transaction; the API returns no raw proof/capability and requires fresh sign-in for the new session epoch. A revoked administrator cannot reopen bootstrap. Tenant preparation uses the deployment tenant and expected draft revision, preserving immutable connection history without asserting consent or selected-site verification.
- The Application `PortfolioQuery` owns bounded scoped client projection and engagement counts. It rechecks authorization before returning. Group membership never authorizes this query.
- The session bootstrap issues the antiforgery proof through `XSRF-TOKEN`; Angular sends `X-XSRF-TOKEN` for same-origin unsafe HTTP calls. Sign-out validates the antiforgery proof before removing the authentication cookie. Passive session checks do not renew an idle authentication ticket. Migrated business mutations compose existing Application services. Every unsafe endpoint must explicitly validate antiforgery as well as Application authorization; client configuration alone is not CSRF enforcement.
- Session state stays in memory. Periodic and focus checks remove the protected Angular subtree when session verification fails. Canceled/superseded portfolio requests cannot refill invalidated state. Browser storage contains no authentication tokens or portfolio cache. Convenience drafts use a firm/user/scope namespace and are removed at sign-out; complete draft lifetime and revision parity remains under review.

Build, preview and test commands are in the [Angular development guide](../../src/AuditSphereOps.Ui/auditsphere-angular-development.md).

## Read contract

`GET /api/ui/portfolio?search=<client-name-or-id>&page=0&pageSize=25`

Search length at most 100, page size 1–100, nonnegative bounded page index. Rows sort by client legal name then immutable ID. Counts and totals include only authorized records. HTTP 401 means unavailable session, 403 unavailable local access, 400 invalid input. No raw EF entities are returned. Financial DTOs serialize decimals as exact invariant strings; date-only values do not pass through local-time conversion. Angular runtime decoders reject unsupported responses.

## Acceptance boundaries

The generated [source discovery inventory](../execution/angular-source-inventory.json) identifies routes, event expressions, service injections and literal links. It is not manually accepted coverage: nested/dynamic commands, dialogs, drafts, permissions and source tests still require review.

The implementation covers native route families for practice, accounting, consolidation, audit, completion, portal and administration. Route presence is not accepted source-action parity. Full form/draft/control parity, comprehensive security and behavioral coverage, measurable production-like performance, final route cutover and retirement remain open. The migration backlog records partial story coverage; the legacy host remains available for rollback.

Execution evidence is recorded in [status.json](../execution/status.json). Local synthetic fixtures do not establish tenant, deployment, professional or owner acceptance. Wiki publication has not been authorized.

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

Client-wide Partner/Manager authorization controls Angular draft creation. New shells remain blocked; creation is serialized on the client safety row so concurrent retries reuse a single service-period identity. A conflicting service profile is refused. Angular sends ISO date-only strings directly without converting through local time. Exact engagement Partner authorization controls the activation affordance; the existing service still requires current unconditional acceptance for the matching service and no open hold. CSRF is validated on both command endpoints. Acceptance checklist migration and complete browser parity remain pending.

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

### Commercial settings

Angular explicitly owns the opt-in commercial-settings route. A named Application query supplies the current profile, bounded active matrix, server-owned default threshold/role and edit authority. Firm-wide commercial users can read; only current firm-wide Administrators/Partners can edit. Profile changes bind the reviewed version and preserve historical document profile identities. Matrix writes/deactivation bind a fingerprint of the reviewed active rules and serialize under the existing firm guard; frozen quotation approval requirements remain unchanged. Forms require explicit review, and deactivation has a named confirmation describing future-rule/default effects. Fixed CSRF-protected endpoints reuse existing Application commands. Unknown outcomes require fresh persisted-state review before another action. Full commercial metadata/form/draft parity and the remaining Angular modules still require work; no complete migration acceptance is claimed.

### Client portal and administration

Native Angular client routes compose `ClientPortalWorkspaceQuery`, `ClientPortalReviewQuery` and `ClientPortalReviewCommands`. Requests retain exact participant/delegation checks, first-sign-in evidence and release/freeze write windows. Conversation entries include chronologically ordered upload receipts. File hashing and same-origin chunk staging preserve exact bytes; trusted staff completion and the durable document worker remain separate. A staged upload is never displayed as a delivered SharePoint file. Client package decisions, reviewed document acknowledgements and signed representation uploads reuse the existing human-review and exact-hash services.

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

Closing a case now retains the human conclusion and an append-only snapshot of the exact case, current response and current alternative evidence. Closure takes the case lock, rechecks scope, refuses a second closure and requires independent review of the current evidence. Alternatives use the same timestamp-and-ID ordering as the native detail projection; an older reviewed alternative cannot clear a newer unreviewed revision. The API exposes only the retained conclusion, actor, timestamp and evidence digest, through the existing exact engagement authorization. Historical closed cases without a closure record are explicitly labelled as missing retained evidence; no backfill invents professional conclusions. The additive `ConfirmationClosureEvidence` migration adds scoped foreign keys, a unique case decision and an update/delete refusal trigger. Apply it through the approved deployment migration procedure before starting the upgraded API or rollback host. Local synthetic migration tests do not establish production deployment acceptance.

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

The native panel limits rendered pages, labels a missing authorized prior comparison, and removes the previous result before a refreshed calculation. Changed filters mark displayed results stale; late or wrong-context responses cannot repopulate changed datasets or revoked sessions. Missing rates explain the required input correction without inventing approvable figures. Upload and mapping commands remain their existing separate workflows. Complete intake command/form/draft parity, rate/method editor parity and final Blazor retirement remain open.


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
retried. Protected data is cleared on identity changes or failed reads. GL upload/completeness,
reviewed mapping editing and full story/retirement acceptance remain pending.

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
retain their existing downstream gates. Native GL import controls remain open.

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
source selection and professional conclusions are separate facts. Native GL import and wider
intake/form/retirement acceptance remain open; verification facts live in the execution ledger.
