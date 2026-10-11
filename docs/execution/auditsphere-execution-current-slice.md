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

## Typed financial-statement presentation draft — 2026-10-11

The current local slice implements the recommended first FSR presentation milestone. A scoped read and export now build a typed SFP and profit-or-loss presentation from persisted package lines and a published layout for the same client/framework. The server applies display signs, joins current and explicitly bound comparative package lines, links persisted note cross-casts, and blocks unbalanced or incompletely mapped statements. Exports produce deterministic PDF, DOCX and XLSX from that same draft model; Excel numeric cells remain numeric. The package's existing immutable artifact generation and stored bytes are unchanged.

The output is marked `UNAPPROVED_DRAFT`, and amounts use a display policy that still needs professional/design approval. The fixture uses synthetic balances matched to selected reference totals; it is not the NAQAHA ledger or an approved golden fixture. The focused tests and browser boundary evidence are recorded in [`status.json`](status.json). The local browser rendered the Angular shell's unauthenticated access guard; no API session or signed-in package data was available for a full screen journey.

FSR-001/T003 accounting-policy approval remains blocked on qualified owners. Cash-flow and equity statement matrices, complete disclosure policy and dynamic note numbering, full composition/sealing, audited report assembly, management and Partner acceptance, and production/legal sign-off remain open. This partial presentation slice does not complete the R2R task cards or the attached backlog.

## Isolated background mail worker active — 2026-10-11

The acceptance mail queues and pending discovery rows were checked before activation; no
unreviewed recipient was pending. The isolated mail profile was started with the previously
verified sender credential. Its broad parent credential mount was identified and replaced with
an outbound-mail-only read-only mount. The disabled PBC profile now also uses only its own
Selected Site mount; it was not started.

The clean VPS checkout was fast-forwarded to the reviewed Compose change and only the new mail
worker recreated. Its own key is readable and sibling credentials are absent. Logs show successful
queue polling without failures; API readiness and existing worker preservation passed. The
administrator Operations page is accessible and has no browser errors. This verifies idle worker
deployment, not an unexecuted business-mail workflow. Exact evidence is in [`status.json`](status.json).

SharePoint's separate exact-site grant remains unresolved; its Entra consent is present and the
runtime identity still cannot access the approved site. The exact grant receipt is requested
before another provider probe. No tenant-wide scope or site permission was added.

## Separate directory capabilities configured and verified — 2026-10-11

Following exact owner approval, the existing User Creation certificate was matched to its Entra
registration and staged privately on the VPS. Guest Invitation and Group Membership received
separate restricted VPS key pairs; only their public certificates were uploaded to their exact
single-permission app registrations. All identities authenticated for the configured tenant with
exactly their named application role, and their distinct app IDs were saved in the protected
server environment.

Only the API was recreated to enable the reviewed capabilities. Readiness passed and existing
workers were unchanged. AuditSphere persisted verified permission checks for User Creation and
Guest Invitation, plus a successful bounded read for Group Membership. Directory Reader and Mail
also verified successfully; browser console error checks were empty. No user was created, guest
invited or group membership modified. This completes capability setup and read/permission
verification, not full mutation-journey acceptance. Exact observations are in [`status.json`](status.json).

SharePoint remains separately blocked by the approved-target denial. The site's inspected General panel
confirmed the reviewed site identity; no grant or permission scope was changed. Further duplicate target probes remain paused pending exact grant
investigation, and the runtime Selected Site app ID stays unset.

## Restricted outbound mail verified and delivered — 2026-10-11

The local interactive authentication alternative failed because the Exchange authentication
library rejects this Mac's OS version. A temporary official Linux PowerShell container on the VPS
provided an active supported Exchange connection. The initial access checks allowed both the
approved sender and unrelated synthetic mailbox, proving a sender restriction was needed.

A mail-enabled security scope group containing only the approved sender and an exact-app
`RestrictAccess` policy were created. Exchange then allowed the sender and denied the unrelated
fixture. A structurally valid Graph authorization probe with no recipients also denied the
unrelated mailbox. The dedicated token contained only `Mail.Send`; the existing certificate was
reused and no new application credential was added.

The approved self-addressed synthetic message was accepted by Graph and Exchange's filtered
message trace confirmed delivery. The API-only recreation enabled the mail capability; readiness
passed, existing workers were unchanged, and fresh AuditSphere capability verification passed.
The reviewed local Mail metadata was saved and its immutable receipt reconciled; Records remains
unconfigured. The live mail worker was not started, so this is transport and configuration evidence,
not durable commercial/PBC mail workflow acceptance. Temporary verification containers and files
were removed. Exact timestamps, status and correlation evidence are in [`status.json`](status.json).

The prior authentication blocker is resolved by the supported Linux path. SharePoint's exact-site
denial and the disabled mutation capabilities remain separate blockers.

## Live VPS upgrade and directory consent verified — 2026-10-11

After exact owner approval, a fresh catalog-checked database backup was retained, the approved
migrations were applied to the synthetic acceptance database, and only the API was recreated.
Public readiness passed. Existing worker containers and the local development database were
unchanged. Exact image, migration, backup and observation evidence is in [`status.json`](status.json).

The dedicated Directory Reader now has both fixed Web callbacks and its own certificate. Only
its public certificate was uploaded to Entra; the private key remains on the VPS. Directory Reader
and Tenant Consent were enabled. The reviewed Microsoft consent and identity flow completed,
and AuditSphere persisted verified consent and a fresh successful bounded directory read.
Mail and directory mutations remain disabled. Consent verification does not promote the separate
resource revision or activate a workspace.

The deployed time screen renders FSLI attribution. End-of-service access correctly refuses an
administrator without a Finance or Partner grant. The commercial settings screen also renders the pricing
policy form and reports that no actual pricing policy has been configured. These are live screen observations, not a
record-specific business acceptance run. Selected SharePoint remains blocked by the missing
exact-site grant; neither the unrelated control nor the approved target allowed access.

## VPS upgrade rehearsal and selected-site access check — 2026-10-11

The previously authorized SSH access worked despite the expired Hostinger browser terminal.
The clean VPS checkout was fast-forwarded, the published API and migration images were pulled,
and a private custom-format PostgreSQL backup was catalog-checked and restored to a disposable
database. The pending migrations applied there, and the new API returned readiness HTTP 200
against that copy. The rehearsal container, database and temporary environment files were removed.
The live database and existing services were unchanged. Its upgrade awaits the specific migration
approval because the earlier deployment scope allowed API recreation without database migration.
Exact image, migration and backup evidence is in [`status.json`](status.json).

The installed Selected Site certificate matches the public certificate on the approved Entra app.
A live token contained exactly `Sites.Selected` for the configured tenant. Read-only Graph checks
returned HTTP 403 with `accessDenied` for both the approved synthetic site and its unrelated
control. The positive grant is missing; no disposable file write was attempted and no site grant
was changed. The server-side Selected Site app ID remains unset pending the user's exact-site grant.

Automatic approval review rejected selecting the Web callback platform on the separate Directory
Reader app because the earlier callback authorization named the Development app. Exact approval
for the Directory Reader callbacks, certificate and read-only capability enablement has been
requested. Its registration and runtime remain unchanged pending that response.

## Current-tree acceptance verification — 2026-10-11

The complete .NET and Angular suites, Release build, OpenAPI drift check, EF model-drift check,
documentation validators and source inventory now have current-tree evidence. Exact commands,
counts, durations, migration head and the verified code commit are recorded in [`status.json`](status.json).
The PostgreSQL-backed tests used only the isolated `auditsphere_tests` database. The previous
four failing browser journeys were diagnosed and fixed; the pricing-policy maker/checker and
end-of-service professional-gate journeys are now included in browser coverage.

The end-of-service page correctly blocks accrual preparation until a qualified accountant names
the treatment and a Partner confirms it. No firm treatment or chart accounts were supplied, so no
real accrual was prepared or posted. Certificate signing remains implemented and locally verified;
the firm certificate and legal-sufficiency decision are still outstanding. The GitHub Wiki's
Production Environment Settings page already documents the `ReportSigning` configuration, so no
Wiki edit was needed.

The Entra portal now confirms admin consent for `User.Read.All` on the dedicated Directory Reader
registration, alongside its delegated `User.Read` sign-in permission. This is registration-level
consent only: AuditSphere's tenant-connection page still shows no nonce-bound consent attempt, and
its consent action remains unavailable until the deployment-owned consent identity and fixed
callback are configured. Directory verification remains `NOT_VERIFIED` and the Directory Reader
runtime switch remains disabled. The selected SharePoint target fields remain an unsubmitted
browser-tab draft; the user will grant the exact site. Runtime Graph capabilities remain disabled,
and the selected-resource probe and workspace activation remain blocked on that grant and credential
setup. Do not mark those external states complete from local or Entra-registration evidence.

## Hostinger Entra mutation-app permission split — 2026-10-11

The user authorized three dedicated, single-role registrations and admin consent, plus removal of
the duplicate roles from the former combined app. The Entra portal now shows `User.Create` only on
`AuditSphereOps Development Administration`, `User.Invite.All` only (plus delegated `User.Read`)
on `AuditSphereOps Acceptance Guest Invitation`, and `GroupMember.ReadWrite.All` only (plus
delegated `User.Read`) on `AuditSphereOps Acceptance Group Membership`; each application role shows
tenant consent granted. The former combined app's extra configured roles and their leftover tenant
grants were removed.

The dedicated `AuditSphere Acceptance - Directory Reader` registration shows application
`User.Read.All` and delegated `User.Read`, both with tenant consent granted in the Entra portal on
2026-10-11. The separate Mail registration shows `Mail.Send` consent granted. AuditSphere's
Directory Reader and Mail runtime switches remain disabled; no Graph capability verification or
Graph mutation was performed.

This is Entra registration evidence. The live AuditSphere tenant-connection page still reports no
consent attempt, directory `NOT_VERIFIED`, and the three mutation capabilities Disabled. No
role-specific VPS credentials were configured, no Graph mutation was run, and no capability was
enabled. Selected SharePoint remains `BLOCKED_EXTERNAL`; the exact site grant and positive/negative
resource probe are still pending. The previously owner-approved API-only Compose recreation
returned `/health/ready` HTTP 200; the database and workers were not recreated and no migration ran.
`SELECTED_SITE_CLIENT_ID` remains blank in the protected VPS environment until the exact-site grant.
See the M365 permission matrix and Hostinger deployment guide.

## FSR first slice — typed statement presentation (unapproved draft) — 2026-10-11

Working-tree slice on `78979390`, from the NAQAHA implementation stories (FSR-002, FSR-010, FSR-011 first slice). The statement of financial position and of profit or loss are presented from persisted package lines through the published statement layout, in PDF, Word and Excel, with blocking checks for balance, mapping, sign, note cross-cast and display footing. Observed: Release build, EF drift check, OpenAPI regeneration, Domain presentation and statement tests, API UI contract test, Angular presentation and package specs and the production build passed. The fixture is synthetic. Approval (FSR-001), composition, sealing, signing and release remain open; details are in `status.json`.

## ADR-0016 certificate signing of the final report — 2026-10-10

Working-tree slice on `cee546f9`. On the owner's instruction the final report PDF is certificate-signed when the firm's certificate is configured (`ReportSigning:*`), the bundle's certified statements are signed with it, and `ReportSigning:Required` fails closed. With no certificate the image-only behaviour is unchanged and labelled. Observed for this slice: Release build, EF drift check, OpenAPI check, the Domain test classes `ReportCertificateSigningTests` and `AuditDeliverablesTests`, and the `completion.spec.ts` screen specs passed, using a throwaway self-signed certificate. The full suites were not run. The firm's real certificate, its legal acceptance and the Wiki deployment note remain open; details are in `status.json` under ADR-0016.

## STE-NXT-014 end-of-service provision accrual and report certificate signature evidence — 2026-10-10

Working-tree slice on `cee546f9`. The ADR-0010 recording mechanism is implemented: a recorded and Partner-confirmed accounting treatment, manual accrual journals with an entered basis through the journal maker and checker, and a posting gate in Application and in the database. Report certificate signature evidence is added with migration `20261010203413_ReportCertificateSignatureEvidence`, PEM signing credential source, deliverable signature verification endpoint and Angular verification. Observed: Release build, EF drift check, OpenAPI regeneration and Angular build passed. Per user instruction, automated tests were not run locally. No treatment has been confirmed, so end-of-service posting stays blocked. Details are in `status.json` under STE-NXT-014.

## AS-PAR-009 commercial pricing, revocation and delivery receipts — 2026-10-10

Committed at `cee546f9`. Pricing policy maker/checker, append-only approval revocation, quotation validity, signatory-bound acceptance and durable mail receipt states are implemented with migration `20261010185946_CommercialPricingPolicyAndDeliveryReceipts`. Observed: Release build, EF drift check, OpenAPI check, the targeted commercial Domain subset and the Angular build and specs passed. The full Domain, API and E2E suites were not run on this tree. Exact counts and open items are in `status.json` under AS-PAR-009.

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

The statements above describe the earlier blocked state and are superseded by the observation below.

## Hostinger Selected Site exact-grant verification — 2026-10-11

The owner approved a write grant to the existing synthetic `AuditSphere Development` site and one
disposable create/read/delete probe. A temporary single-tenant grantor with delegated
`Sites.FullControl.All` created only that grant; its Entra registration, service principal and
delegated consent were then removed. The runtime app retained only `Sites.Selected`, and the
unrelated same-tenant control site received no grant.

The protected VPS `.env` now has the runtime app configured privately and its credential reference
aligned with the persisted AuditSphere connection (`configuration:SelectedSite`, file mode `0600`).
Only the API container was recreated. `/health/ready` returned HTTP 200; the live database and
worker containers were unchanged and no migration ran. The certificate matched the registration,
and token inspection showed only the `Sites.Selected` application role.

AuditSphere persisted the selected resource as `VERIFIED`, revision 7, diagnostic
`exact-resource-and-control-denial` (observed `2026-10-11T17:36:47.672211Z`). Graph allowed the
approved site, `Internal Workpapers` drive and configured root (HTTP 200), and denied the unrelated
control site (HTTP 403). The disposable file uploaded with HTTP 201, read back byte-for-byte,
deleted with HTTP 204 and then returned HTTP 404. The SharePoint library was empty after the probe.
No workspace was activated and the PBC worker remains stopped.

At the latest tenant-page observation, Directory Reader freshness showed `STALE`; refresh and
reverify its bounded read before relying on current directory data. Records remains
`NOT_CONFIGURED`. This evidence verifies the exact selected-site boundary and a file operation only;
the broader M365, PBC, client workflow and production acceptance gates remain open. Do not place IDs,
account names, secrets or certificate material in repository docs.

> **Archived handoff history:** superseded 2026-10-09 evidence and implementation notes are preserved in the [historical slice archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md#ste-v2-1-evidence-mapping-and-full-suite-runs--2026-10-09).
