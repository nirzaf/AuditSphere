# AuditSphere Angular presentation migration

**Status: CURRENT for the implemented pilot; remaining feature migration is PROPOSED.**

The owner requested implementation of the attached Blazor-to-Angular guide. Angular replaces presentation incrementally. The .NET modular monolith remains the capability, session and database authority. The request does not authorize a NestJS rewrite, additional writers, different identity authorities or retirement of Blazor before parity acceptance.

## API and Angular project boundary

The owner subsequently requested an ASP.NET Core API backend and a separate Angular UI project. `AuditSphereOps.Api` owns the HTTP host, authenticated API contracts, consent callbacks, protected file transports, provider composition and health checks. `AuditSphereOps.Ui` owns Angular presentation. Domain, Application, Infrastructure and Worker retain their existing responsibilities.

The legacy Web host references the API host composition during coexistence; it adds only Razor/MudBlazor presentation and circuit actor resolution. API has no reference to Web, Razor components or MudBlazor. Both hosts use the same authorization and authentication implementation. API defaults to the Angular build, with explicit SPA routes. It never routes API/auth/health failures into the SPA.

Same-origin delivery remains the supported production boundary: build Angular separately, publish its browser artifacts with API, and terminate HTTPS at the approved host. The Angular development server proxies API/auth calls; no permissive cross-origin policy or browser bearer-token store is added. Existing private development user secrets remain in the approved `AuditSphereOps.Web.Development` namespace for both hosts; no secret is copied into project configuration. The independent API deployment and full UI retirement still require verification.

## Implemented ownership

- Angular workspace: `src/AuditSphereOps.Ui`, standalone strict Angular with Material/CDK, zoneless change notification and lazy portfolio loading.
- Opt-in preview owns `/ui`, `/ui/`, `/ui/app`, `/ui/app/clients/{id}`, `/ui/app/engagements/{id}`, `/ui/app/clients/{id}/assessment` and `/ui/app/practice/leads`. Existing `/app`, `/portal`, `/auth/*`, `/api/*` and `/health/*` keep their existing owners. Unknown preview routes are not a blanket SPA fallback.
- `AngularUi:Enabled` defaults to false. Enabling requires a production build. `AngularUi:BuildPath` optionally supplies its browser directory. Rollback disables the flag and retains existing Blazor links and routes.
- Web owns `/api/ui/session`, `/api/ui/portfolio` and CSRF-validated `POST /api/ui/sign-out`. They resolve trusted cookie identity and current epoch, never accept browser actor/firm/role authority, and return no-store responses.
- The Application `PortfolioQuery` owns bounded scoped client projection and engagement counts. It rechecks authorization before returning. Group membership never authorizes this query.
- The session bootstrap issues the antiforgery proof through `XSRF-TOKEN`; Angular sends `X-XSRF-TOKEN` for same-origin unsafe HTTP calls. Sign-out validates the antiforgery proof before removing the authentication cookie. Passive session checks do not renew an idle authentication ticket. No business mutation has migrated. Every future unsafe endpoint must explicitly validate antiforgery as well as Application authorization; client configuration alone is not CSRF enforcement.
- Session state stays in memory. Periodic and focus checks remove the protected Angular subtree when session verification fails. Canceled/superseded portfolio requests cannot refill invalidated state. Browser storage contains no session tokens, profile, drafts or portfolio cache.

Build, preview and test commands are in the [Angular development guide](../../src/AuditSphereOps.Ui/auditsphere-angular-development.md).

## Read contract

`GET /api/ui/portfolio?search=<client-name-or-id>&page=0&pageSize=25`

Search length at most 100, page size 1–100, nonnegative bounded page index. Rows sort by client legal name then immutable ID. Counts and totals include only authorized records. HTTP 401 means unavailable session, 403 unavailable local access, 400 invalid input. No raw EF entities are returned. No money or financial calculation is transferred in this pilot.

## Acceptance boundaries

The generated [source discovery inventory](../execution/angular-source-inventory.json) identifies routes, event expressions, service injections and literal links. It is not manually accepted coverage: nested/dynamic commands, dialogs, drafts, permissions and source tests still require review.

This pilot is a partial implementation of US-001–US-006 and the read-only portion of US-015. Portfolio metrics, release/package summaries, CSV export, complete client actions/engagement details, other modules, portal migration, full control/form compatibility, comprehensive parity and final route cutover remain open. Navigation explicitly returns to Blazor for existing workflows.

Execution evidence is recorded in [status.json](../execution/status.json). Local synthetic fixtures do not establish tenant, deployment, professional or owner acceptance. Wiki publication has not been authorized.

## Client profile migration in progress

`GET /api/ui/clients/{id}` composes Application `WorkspaceQuery`. Client-level authorization is required; an engagement-only assignment does not widen to a client profile. Up to 100 recent engagements are individually authorized before projection. Unknown and unauthorized clients share the same failure response. Angular validates the projection, cancels superseded reads and clears client content on session invalidation. Engagement navigation still uses the existing workbench while its panels are migrated. Profile contacts, creation, activation, staffing and budget parity remain open.

## Engagement overview migration in progress

`GET /api/ui/engagements/{id}` independently authorizes exact engagement scope, projects lifecycle/revision and the 100 most recent holds, and rechecks authority before returning. The Angular direct route validates identifiers and decimal-string revision metadata and cancels superseded reads. Clearance, staffing, budget and workbench commands retain existing ownership until their Angular parity is implemented.

## Authorized global search migration

`GET /api/ui/search` composes the existing `GlobalSearchQuery`; it retains its bounded navigation-only coverage and per-result authorization. Angular search uses Material autocomplete, debounced cancelable reads, a slash shortcut outside fields/dialogs, explicit migrated-route mapping and in-memory results cleared on session invalidation. The search controls load as a deferred chunk. Search text is not stored or sent to analytics. Keyboard and cross-scope browser parity acceptance remain pending.

## Client contacts

The client projection now includes bounded contacts and an independently authorized contact-management capability. `POST /api/ui/clients/{id}/contacts` validates antiforgery, resolves current server identity, checks client-wide access and delegates to `PracticeCrmService`. The browser sends the reviewed safety-generation string; the Application service checks it under the client safety-row lock before inserting. A repeated request with that generation is rejected after the first accepted insertion. Angular never retries commands automatically and labels an unconfirmed transport outcome for review. Existing Blazor callers retain their current optional-generation behavior. This is revision fencing, not a persisted command receipt; comprehensive reconciliation, drafts and full feature parity remain open.

## Engagement planning read panels

`GET /api/ui/engagements/{id}/planning` composes existing staffing and approved-budget services after exact engagement authorization. Budget decimal amounts and version numbers are invariant strings; approved-time minutes remain bounded integers. An unavailable approved budget is explicit and is not a zero budget. Unexpected authorization/service failures fail closed. Angular validates this contract and lazily loads the secondary planning panel. Staffing mutations, budget preparation/approval and full planning parity remain pending.

## Staffing commands

Angular staffing now offers bounded staff candidates after a separate scoped management check, requires review of the entire-client-site Full Control warning, and confirms named revocations. CSRF-validated assignment delegates to `StaffingService.AssignAsync`; revocation first checks firm and exact engagement ownership before delegating. Existing rank/certification/self-assignment rules, grant evidence, repeated assignment/revocation handling and session epoch invalidation remain owned by the existing service. Unconfirmed command outcomes are shown for reconciliation rather than automatically retried. Budget commands and full resource-planning parity remain open.

## Budget preparation and approval

Angular planning now prepares budget lines using whole forecast minutes and displays the exact draft currency, rates-derived costs and latest version. Revision submission requires the version string read from the server and delegates to `PracticeTimeService.ReviseBudgetAsync`, whose transaction locks and stale-version check fence repeated requests. Draft approval requires review and is bound to the exact engagement and budget identity before delegating to the existing approval service. Preparers cannot approve their own draft. All unsafe routes validate antiforgery; none retries automatically. Unknown transport outcomes require review of persisted state. Full planning browser parity and scoped draft persistence remain pending.

## Engagement creation and activation

Client-wide Partner/Manager authorization controls Angular draft creation. New shells remain blocked; creation is serialized on the client safety row so concurrent retries reuse a single service-period identity. A conflicting service profile is refused. Angular sends ISO date-only strings directly without converting through local time. Exact engagement Partner authorization controls the activation affordance; the existing service still requires current unconditional acceptance for the matching service and no open hold. CSRF is validated on both command endpoints. Acceptance checklist migration and complete browser parity remain pending.

## Acceptance checklist

Angular owns the opt-in client assessment route and its checklist projection, exposing questions, current/prior answers, readiness blockers and specialist-review status through a named Application query. Answer commands validate antiforgery and send exact generation/revision strings. The existing Application service checks both values while holding its client safety lock and continues to preserve decision immutability and evidence requirements. Answered/complete never means accepted: professional acceptance remains a human Partner decision. Specialist-review, continuance and decision controls still need Angular migration. No full US-017 acceptance is claimed.

## Acceptance command controls

Angular now offers specialist review requests/results, explicit Partner decisions and confirmed continuance initiation. All contracts validate CSRF and carry the current generation as a string. Specialist results bind exact firm/client/review identities and reviewed status; client-row locking fences stale generations and concurrent requests. Decision submission delegates to existing immutable acceptance evidence and workspace-intent rules, never autonomously accepts a completed checklist. Continuance invalidates the prior generation and requires fresh delta answers. Complete assessment-page parity, browser journeys and production acceptance remain unverified.

## Commercial leads

Angular owns the opt-in `/ui/app/practice/leads` route. A bounded Application lead query filters only the current firm after firm-wide commercial authorization and rechecks that authority before returning. Creation and confirmed qualification validate antiforgery and compose the existing CRM commands. Owner identity comes from the trusted server actor, not browser claims. Existing duplicate-lead checks and qualification gates remain intact; neither action accepts a client or grants access. Opportunity/proposal/document parity remains pending.

## Proposal detail and commercial actions

Angular owns the opt-in proposal detail route with bounded revision history and exact decimal-string fees. The named Application query requires firm-wide commercial authority, validates firm relationships and rechecks access before returning. CSRF-protected commands reuse independent proposal review, status-only sent recording, client response and prospect conversion. These actions never replace professional acceptance. Pricing, document generation, opportunity intake, full source metadata and browser parity remain pending.

## Proposal revisions

Angular now exposes a reviewed proposal-revision form. Fees cross HTTP as decimal strings; dates remain date-only strings. The CSRF-protected endpoint validates bounded fields and composes the existing CRM revision command with the exact reviewed revision. The firm lock and revision fence prevent stale or duplicate replay from superseding a newer proposal. Closed opportunities and accepted proposals remain blocked. Unknown responses require persisted-state review, not automatic retry. Opportunity intake, pricing and document-generation parity remain pending.

## Opportunity intake and initial proposals

The explicit opt-in `/ui/app/practice/leads/{id}` route exposes a named, firm-wide-authorized Application projection capped at the latest 100 opportunities, with latest proposal identities and exact fee/revision strings. Reviewed discovery creation validates CSRF and takes its owner from the trusted actor. A stable request identity becomes the opportunity identity; the existing firm lock serializes same-firm creation. Repetition with matching terms returns that identity, while changed terms or another firm's identity fail closed. This is an additive optional request parameter for existing callers, not a change to lead qualification or professional acceptance. Initial proposals use the existing revision command with reviewed revision zero and require fresh independent review. Unknown outcomes require explicit persisted-state inspection. Pricing, commercial settings/documents and fee agreements still need Angular migration.

## Quotation pricing and matrix approval

The Angular proposal route now defers a quotation workbench with approved rate choices, hour lines, exact decimal-string factors, a server-calculated preview, immutable version breakdowns and recorded approval requirements/results. Fixed HTTP contracts compose named Application projections and existing quotation commands; no EF entities or raw JSON state cross to Angular. Reads cap quotation versions at 100 and rate options at 200. Saving sends reviewed proposal/quotation revisions and exact rate-card identities; the existing firm lock rejects changed rates/revisions. Identical recalculation remains idempotent. Matrix approvals use the exact required firm-wide role and preserve preparer/approver separation. Factor precision is restricted to four decimals to match the existing canonical input hash. Unknown write outcomes hide stale state and require fresh persisted-state review before another action. Commercial document generation, settings and fee agreement parity remain pending; this is not full US-016 or migration acceptance.

## Commercial documents

The Angular proposal workspace now defers a commercial-document panel. A named Application projection selects only the latest 100 artifact metadata rows, never stored document bytes, and explains approved-price/profile prerequisites plus client acceptance, current unconditional risk clearance, Partner authority and approved signature/seal requirements for letters. Brief quotation, engagement-letter and comprehensive-proposal commands validate CSRF and explicit review, then compose existing immutable document-generation services. The reviewed quotation identity and profile revision are checked while holding the existing firm lock. Comprehensive proposals use reviewed firm chapters and actual assigned-team CV/timeline inputs. Historical artifacts retain their original template/profile/quotation/SHA-256 identities; same-version generation reuses the existing artifact. Downloads reuse the existing authorized endpoint, which now rechecks session validity before returning bytes. Visual signature/seal output is not an electronic-signature provider acceptance claim. Settings, fee agreements and full story/browser acceptance remain pending.

## Fee agreements and finance handoff

The Angular proposal route now defers a fee-agreement workspace with persisted agreed fee, server-owned advance percentage, exact-string milestones, allocation/outstanding values, invoice links and final-release state. Creation and engagement linking remain commercial commands; invoice drafting and manual advance payment require exact client-scoped FinanceManager/FinanceReviewer authority. Administration alone grants no finance authority. Bounded engagement candidates belong to the same client; the existing service now serializes engagement links under the firm guard so concurrent links cannot overwrite each other. Payment-reference replay reuses the recorded payment, and changed amount/received-time details are refused. The original billing review/posting, allocation, receipt and notification workflows remain authoritative; queued notification never means delivered. Fixed CSRF-protected endpoints compose these services. Unknown outcomes require fresh persisted-state review before another command. Commercial settings, full module parity and final browser acceptance remain pending.

## Commercial settings

Angular explicitly owns the opt-in commercial-settings route. A named Application query supplies the current profile, bounded active matrix, server-owned default threshold/role and edit authority. Firm-wide commercial users can read; only current firm-wide Administrators/Partners can edit. Profile changes bind the reviewed version and preserve historical document profile identities. Matrix writes/deactivation bind a fingerprint of the reviewed active rules and serialize under the existing firm guard; frozen quotation approval requirements remain unchanged. Forms require explicit review, and deactivation has a named confirmation describing future-rule/default effects. Fixed CSRF-protected endpoints reuse existing Application commands. Unknown outcomes require fresh persisted-state review before another action. Full commercial metadata/form/draft parity and the remaining Angular modules still require work; no complete migration acceptance is claimed.
