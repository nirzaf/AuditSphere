# AuditSphereOps — Current State & Active Slice Handoff







**Status:** CURRENT



**Purpose:** Compact, authoritative handoff of the active implementation slice, recent verified changes, local environment state, and next actions.



**Authority:** Active execution handoff document. Volatile metrics (exact test counts, migration count, verified commit SHA, CI run IDs) belong exclusively to [`docs/execution/status.json`](status.json).



**Audience:** AI coding agents and human developers.







---

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
not activate a connection or assert Microsoft permissions. Directory provider
verification and live tenant consent remain pending. The local migration and
focused PostgreSQL and URL-builder checks are recorded in `status.json`.

The separate directory reader now supports bounded, administrator-only live
search from the tenant connection page, with a dedicated certificate and
`User.Read.All` application role fence. It is disabled by default. Search
returns no local role grant, and the current release does not persist a
provider-verification revision or bind the selected result to a local user.
In the Development tenant, the dedicated app received the exact permission,
and a certificate token plus the live page completed bounded Graph reads.
The consent callback's denied path also returned to the app and consumed its
attempt without activating a connection. These are Development observations,
not production acceptance or a successful callback observation.

The owner has requested a tenant-administration experience that supersedes the
earlier blanket Graph-scope exclusion only for an isolated read-only directory
reader. The [permission decision](../architecture/auditsphere-m365-tenant-administration-permissions.md)
records `User.Read.All` as the proposed application permission for that reader,
separate from the `Sites.Selected` document worker. It has not been configured
or consented in the tenant. User provisioning, guest invitations and Microsoft
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
block before any write; throttling is retryable. This probe is not yet composed
into the Worker, is not a PBC upload provider, and has no live tenant result.
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
  [`docs/auditsphere-ui-mudblazor-conventions-migration-current.md`](../auditsphere-ui-mudblazor-conventions-migration-current.md):
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
