# AuditSphere — Simple Microsoft 365 Tenant Onboarding, Role Assignment and Invitations

**Story ID:** AS-M365-SSO-001
**Revision:** 1.0
**Prepared:** 22 September 2026
**Repository:** `nirzaf/AuditSphere`
**Source checkpoint inspected:** `master@0f9f10c63c8f5f97aca50546b6f0c36895b5a5e1`
**Suggested repository location:** `docs/requirements/AuditSphere_M365_Simple_Onboarding_User_Story.md`
**Status:** Proposed implementation story. No repository, tenant, credential, permission or deployment was changed to prepare this document.

> **The intended experience:** The tenant administrator connects Microsoft 365 once, selects existing people, assigns their AuditSphere roles and client/engagement scope, and sends an invitation. Each person opens AuditSphere with their existing Microsoft account. There is no separate AuditSphere password, account-registration form or routine second role-assignment exercise in Entra.

## Contents

1. [Recommended implementation](#1-recommended-implementation)
2. [User story and acceptance boundary](#2-user-story-and-acceptance-boundary)
3. [What exists and what must change](#3-what-exists-and-what-must-change)
4. [Scope and explicit exclusions](#4-scope-and-explicit-exclusions)
5. [Tenant administrator journey](#5-tenant-administrator-journey)
6. [Invited user journey](#6-invited-user-journey)
7. [Permissions and one-time provisioning](#7-permissions-and-one-time-provisioning)
8. [Role catalogue and assignment rules](#8-role-catalogue-and-assignment-rules)
9. [Identity, sessions and revocation](#9-identity-sessions-and-revocation)
10. [SharePoint and client workspaces](#10-sharepoint-and-client-workspaces)
11. [Invitation delivery](#11-invitation-delivery)
12. [Data and command contracts](#12-data-and-command-contracts)
13. [Integration, concurrency and failure handling](#13-integration-concurrency-and-failure-handling)
14. [Implementation sequence](#14-implementation-sequence)
15. [Acceptance scenarios](#15-acceptance-scenarios)
16. [Definition of done and tenant checklist](#16-definition-of-done-and-tenant-checklist)
17. [Short Codex prompt](#17-short-codex-prompt)
18. [Source register](#18-source-register)

---

## 1. Recommended implementation

Use **the existing ASP.NET Core/Blazor application, Microsoft Entra OIDC sign-in and AuditSphere's local role grants**, extended with a small Microsoft 365 setup wizard and a user/invitation screen. Keep SharePoint operations in the existing worker/provider boundary.

Do not create a separate identity product, synchronize passwords, replace the application stack, or build a general-purpose tenant-management platform.

### 1.1 One understandable administrator workflow

```text
ONE-TIME INSTALLATION
Deployment owner + authorized tenant administrator
  -> Run the reviewed setup helper
  -> Approve the explicitly listed Microsoft permissions
  -> Configure the selected SharePoint working site
  -> Open the protected AuditSphere setup page

NORMAL AUDITSPHERE ADMINISTRATION
Connect / verify Microsoft 365
  -> Select people from the directory
  -> Assign role + permitted client/engagement scope
  -> Review changes
  -> Save and invite

USER EXPERIENCE
Open invitation or normal application URL
  -> Microsoft sign-in / existing Microsoft browser session
  -> AuditSphere checks the user's identity and local grants
  -> Open the permitted staff or client workspace
```

The one-time helper removes repeated copying of app IDs, secret values and SharePoint IDs. It does not remove Microsoft's consent or administrator requirements. A brand-new self-hosted installation still needs an application registration, callback URL and trusted credential configuration before OIDC can work. The authorization-code flow and consent protocol are documented Microsoft capabilities, not something a web wizard can bypass. [M01, M02]

### 1.2 Meaning of “without additional logins”

**Required:** No AuditSphere-specific username/password, password reset, password synchronization or duplicate sign-up. Use the same Microsoft identity the person already uses for Microsoft 365.

**Not promised:** Zero Microsoft prompts in every browser. Account selection, MFA, Conditional Access, expired sessions and external-guest redemption can require Microsoft interaction. Do not force `prompt=login` for ordinary visits or bypass tenant policy to make a demonstration appear seamless. [M01, M03]

AuditSphere will retain an application user/profile record for permissions and historical attribution. That is not a second credential account.

### 1.3 Minimal technical topology

```text
Browser
  -> Existing ASP.NET Core OIDC/cookie authentication
  -> Existing AuditSphere web application
       -> Local users, scoped grants and professional authority
       -> Existing PostgreSQL durable operations
            -> Directory worker: read permitted directory fields
            -> Document worker: selected working SharePoint site
       -> Existing invitation/notification transport, when configured

Microsoft Entra: authenticates people
AuditSphere: authorizes application actions
SharePoint: stores documents and enforces direct Microsoft access
```

Use three small application identities for this scope: **Web Login**, **Directory Reader** and the **existing Document Worker**. Provision them through one reviewed helper; do not require the administrator to manage three separate product workflows. They are identity/credential boundaries, not three new microservices. Existing restricted-records identities remain separate and are outside this story's initial onboarding gate.

Directory browsing starts with paged, on-demand loading and a bounded refresh of assigned users. Do not make a tenant-wide delta-sync engine, SCIM, group-based role synchronization or webhooks prerequisites for this first release.

---

## 2. User story and acceptance boundary

### Primary story

**As** the tenant administrator who has been authorized to administer this AuditSphere installation,
**I want** to connect our Microsoft 365 tenant once, select existing users, assign application roles and scope, and invite them,
**so that** the team can use AuditSphere with its existing Microsoft accounts without creating separate passwords or repeatedly administering access in two systems.

### Supporting stories

**As an assigned staff member**, I open the application and see only my permitted work and actions.

**As an assigned client contact**, I use my existing approved Microsoft identity and see only the client portal and the entities explicitly assigned to me.

**As the administrator**, I can remove access, resend an invitation, change scope and diagnose a connection failure without editing database rows or exposing credentials.

**As an engagement owner**, I receive an idempotently provisioned client/engagement workspace after the existing acceptance and commencement conditions are satisfied.

### Success is demonstrated when

An authorized administrator completes the setup, assigns a preparer and independent reviewer plus a scoped client signatory, and sends invitations. Those people sign in with Microsoft, land in the correct application area and cannot access another client's records. Revoking one person's local access blocks subsequent protected actions, including an already-open Blazor circuit. An accepted client's selected SharePoint folder can be created and verified without granting the document worker access to unrelated sites.

This completes the **identity/onboarding and bounded working-document capability**. It does not accept audit reporting, signing, Purview retention, independent records custody or the full professional lifecycle on their behalf.

---

## 3. What exists and what must change

### 3.1 Repository observations, not newly claimed functionality

The following findings come from reads at the pinned checkpoint. Reinspect the current branch before coding; do not replace newer implemented work with this snapshot.

| Existing source | Observed boundary | Implementation instruction |
|---|---|---|
| `src/AuditSphereOps.Web/Program.cs` | Tenant-specific OIDC; raw `tid`/`oid` checks; identity-only scopes; configuration currently detects a client secret; live external composition explicitly refuses startup. | Extend the supported authentication/composition path. A flag change alone is not an integration. |
| `Application/Microsoft365/Microsoft365OnboardingService.cs` | Hashed bootstrap proof, expiring capability and resumable setup draft; explicitly local-only. | Reuse; bind bootstrap to the approved first administrator and add real verification. |
| `Application/Microsoft365/Microsoft365ConfigurationService.cs` | Connection revisions, resource verification records, activation and controlled folder templates. | Reuse records; do not turn a submitted `PASS` value into proof of Microsoft access. |
| `Domain/Microsoft365/Microsoft365Onboarding.cs` | Setup sessions/drafts, connection revisions, workspace configuration, folder templates and client workspace states. | Extend minimally; retain revision and activation history. |
| `Application/Security/RoleAdministrationService.cs` | Local user provisioning, scoped grants, revocation, session-epoch increments and last-administrator checks. | Reuse services while closing scope, role-catalogue and concurrent-administration gaps. |
| `AGENTS.md` | Existing .NET stack and provider safety fences; explicitly prohibits tenant-wide Graph scopes. | Obtain the narrowly scoped policy decision in §7.2 before implementing a prohibited permission path. |

These are code observations, not live tenant acceptance. [R01–R06]

### 3.2 Role catalogue discrepancy

The inspected role-administration allowlist contains exactly these eight codes:

```text
Administrator, Partner, Manager, Staff, RelationshipManager,
FinanceManager, FinanceReviewer, ClientUser
```

It is not the same as the 14 personas in the visual prototype. The service also uses a staff-only provisioning path. This story must reconcile real authorization and client/staff classification, not merely add friendly labels to the dropdown. [R05]

### 3.3 Required implementation changes

The missing delivery is the connection between these existing controls and real Microsoft behavior: installation bootstrap, directory retrieval, verified identity binding, a complete role-assignment screen, invitations, role-aware entry, revocation checks and the bounded SharePoint provider. Verify existing equivalents before introducing new classes.

---

## 4. Scope and explicit exclusions

### Included

- One operating firm and one approved workforce Entra tenant per installation.
- Existing tenant member accounts; already-created, approved B2B guest accounts can be explicitly selected for client access.
- Protected first-administrator bootstrap without a new permanent local password.
- A three-step wizard: **Connect**, **Select storage**, **Assign and invite**.
- Directory picker, local role/scope assignments, user state, invitations, sign-in, role-aware landing and revocation.
- Existing notification delivery with copy-invitation fallback.
- One selected working SharePoint site/library/root; server-resolved IDs and acceptance-gated folder provisioning.
- Safe configuration, credential rotation procedure, negative authorization tests and separately recorded live evidence.

### Excluded from this story

- Creating tenant users, changing their Microsoft passwords, purchasing licenses or granting Entra administrator roles.
- Automatically inviting people from other tenants with Microsoft Graph `/invitations`. Their approved B2B onboarding may be performed separately by the tenant administrator.
- Native accounts/passwords, email magic-link authentication, password collection, ROPC or a parallel client identity directory.
- Automatic role mapping from job titles, email domains, Microsoft groups or subscription licenses.
- Tenant-wide SharePoint discovery; users paste/select the explicitly approved site instead.
- Personal OneDrive as canonical storage; personal-drive imports or automatic OneDrive shortcuts.
- New mail providers, mailbox-reading access or tenant-wide mail permissions just to send invitations.
- Full direct-SharePoint permission automation, embedded Office editing, records-policy administration, signing or audit-opinion changes.
- Rewriting business modules or building 14 separate applications.

**Default document access is `APP_MEDIATED`.** Direct staff Office collaboration remains an explicit optional profile with independently verified Microsoft permissions (§10). This keeps basic onboarding simple without pretending local roles control direct SharePoint links.

---

## 5. Tenant administrator journey

### 5.1 Installation prerequisite: one reviewed setup helper

Provide `scripts/m365/Initialize-AuditSphereTenant.ps1` as a **proposed new implementation artifact**, not an already-existing command. It has plan/apply/verify modes and resumes by durable installation/app IDs.

Inputs: installation ID, public HTTPS application origin, tenant ID, designated initial administrator, selected SharePoint working-site URL and deployment-approved credential destinations. Resolve the designated person's immutable ID through the authorized administrator session; never grant bootstrap authority from a typed email alone.

The helper must:

1. Display the exact tenant, application origin, resources, requested permissions and changes before applying anything.
2. Create or reuse only this installation's registrations/service principals; record their actual IDs. Never match an unrelated app solely by display name.
3. Register exact HTTPS callback/logout URLs and tenant-only audience. Preserve unrelated existing configuration when reusing a registration.
4. Configure supported credentials via the deployment secret/certificate store. Keep private keys and tokens out of ordinary application forms, public logs, repository files and downloadable public reports.
5. Request only the separately approved permissions in §7. A human with sufficient Microsoft authority grants consent. Do not impersonate a successful consent click.
6. Resolve the supplied site URL and grant the document application `write` access only to the approved working site through the approved administrative path.
7. Produce a private installation manifest containing IDs, credential references and the designated initial administrator's `(tid, oid)`; never contain raw private keys or reusable administrator tokens.
8. Configure/restart the web and worker processes through the approved deployment mechanism. Do not claim that saving a setup draft changes the already-running OIDC middleware configuration automatically.
9. Issue the expiring bootstrap capability through a secure operator channel. It is setup authorization, not a substitute for Microsoft authentication.
10. Disconnect the administrator session and remove transient credentials according to policy. Report exactly what was created, reused, verified or blocked.

No administrator credentials are retained in the web runtime. App-registration, consent and site-grant operations require an authorized execution environment and explicit approval; this story does not authorize running them against a real tenant.

### 5.2 Step 1 — Connect Microsoft 365

Entry screen:

```text
Connect your Microsoft 365 organization

[Continue with Microsoft]

Use your existing work account.
No AuditSphere password will be created.
```

The first setup session requires **both** the valid installation capability and Microsoft authentication matching the preauthorized tenant/object ID. The first arbitrary visitor, the first user from the tenant, or somebody forging an admin-consent callback must never become administrator.

Under an exclusive firm/setup transaction, create or bind the initial `AppUser` and narrow `Administrator` grant once. Record provenance and consume the bootstrap privilege for administrator creation. Retain a separately bounded setup session for wizard completion; repeated callback/retry returns the same result. Existing installations require an active authorized administrator instead of bootstrap. Do not seed partner, finance or records approval authority.

Display verified organization/account information and capability status. Tenant ID input and the `tenant` query parameter from a consent response are not authentication evidence. Validate state and server-session binding, then verify actual capabilities with the expected credential and resource. [M02]

### 5.3 Step 2 — Select the working SharePoint location

The installation helper supplies the approved site; the wizard displays its name/URL and retrieves libraries within that site. Select one library and root. Offer **Create AuditSphere folder** only inside the selected authorized library; do not create an entire SharePoint tenant/site by default.

The administrator chooses names, not raw site/drive/item IDs. The server resolves, verifies and stores IDs against the same tenant and connection revision.

Show independently:

```text
Microsoft sign-in           Verified
Directory access            Verified / Approval required
Working SharePoint access    Verified / Site grant missing
Invitation email            Configured / Copy invitation available
Records and signing         Separate acceptance required
```

Do not make directory invitations depend on Purview or signing setup. Equally, do not mark working-document access ready merely because sign-in works.

### 5.4 Step 3 — Select people, assign roles and invite

```text
Search people: [name or work email]

Person          Microsoft account      Role             Scope        Invitation
Sample Person   reviewer@example.com   Senior reviewer  Client A     Not sent
Sample Manager  manager@example.com    Manager          Engagement 1 Not sent

[Add selected people]  [Review changes]  [Save and invite]
```

Required behavior:

- Load directory pages through the credential-isolated worker into an admin-only projection. Read only the fields needed for identity selection and enabled-state checks.
- Default to enabled members. Provide a deliberate guest filter for already-approved client identities. Never classify a guest as staff merely because its record exists.
- Show account type, UPN/email and enabled/unknown status to distinguish similar names. Do not claim to detect every shared/service account from `userType`; require administrator confirmation that selected entries represent the intended people.
- Selection grants no access until the administrator chooses an allowed role and explicit scope and confirms the preview.
- Require Client/Engagement scope for client roles. Default professional staff to assigned scope, not unrestricted firm-wide access.
- Explain extra approval/eligibility conditions for professional roles. Never grant `Manager` merely to make a reviewer screen work.
- Show prior grants, additions, removals, scope expansion, invitation recipients and any blocked rows before saving.
- Validate the whole submitted batch before any grant is committed. A failing row does not result in a partly authorized batch. Mail is queued after the local transaction and has its own outcome.
- Support **Save without sending** and **Copy invitation**. Failed or absent mail configuration must not create passwords or fabricate delivery.
- Refresh directory facts before first grant/activation; unverified or disabled identities cannot receive new usable access.

### 5.5 Routine administration

The permanent page is **Administration → Users & access**. It shows roles, scopes, account state, first/last application access where recorded, directory-check age and invitation status. Actions are add, change, revoke, resend and view history. No repeated app-registration work is needed for ordinary additions.

---

## 6. Invited user journey

1. Open a normal AuditSphere invitation URL or the application origin.
2. If no valid application session exists, redirect through Microsoft OIDC. Reuse the Microsoft browser session where available.
3. Validate the token using supported middleware and verify the allowed tenant, immutable object ID, local access state and required identity-status freshness.
4. Bind only to the selected `(tid, oid)`; never bind solely by email, UPN, display name or possession of the invitation.
5. Record first access against the local access assignment and invitation, where present.
6. Send staff to their permitted staff workspace and clients to `/portal`. Honor a validated local deep link only when its object is authorized.
7. Show only authorized navigation, queues, counts, documents and actions; enforce the same decisions in services and endpoints.

The invitation is a notification, **not an authentication token**. A user who already has a valid grant does not need to click an email before accessing the ordinary application URL. Forwarding an invitation gives its recipient no access.

An authenticated but unassigned person sees a minimal **Access not assigned** page with a generic administrator contact route. Do not create a staff account, expose tenant rosters, display client names or assign a default role.

Already-approved B2B guests authenticate through the configured resource-tenant route. Bind the resource tenant's object ID, not an assumed home-tenant ID. A new external guest must finish separate tenant invitation/redemption before this story can activate its approved portal grant. [M04, M12]

---

## 7. Permissions and one-time provisioning

### 7.1 Fixed-purpose permissions

| Identity | Runtime purpose | Permission profile | Credential location |
|---|---|---|---|
| AuditSphere Web Login | Sign people into this installation | OIDC `openid profile email`; no app-only Graph directory/document rights | Existing web authentication secret/certificate store |
| AuditSphere Directory Reader | Read directory candidates and assigned-user enabled state | Graph application `User.Read.All`, only after §7.2 approval | Existing worker process, separate credential from document access |
| AuditSphere Document Worker | Working-document transfer and selected-site operations | Graph application `Sites.Selected` plus explicit `write` grant to the approved working site | Document worker only |
| Authorized provisioning session | Registration, consent and site-grant administration | Minimum documented administrative permissions for that exact operation; time-bounded and separately approved | Administrator-run tooling; never public web/outbox |
| Existing mail transport | Send an access notification | Existing approved sender scope; no additional mail permission by default | Existing delivery worker/provider |

Application `User.Read.All` is the proposed permission for the `/users` reader. Basic-profile access is not sufficient for the intended `accountEnabled` selection. Selecting fewer fields reduces collected data but does not resource-scope the directory permission. Do not request user-update, password-management or directory-write permissions. [M05, M06]

`Sites.Selected` consent, an explicit site assignment and a suitable token are separate prerequisites. Document application access does not provision a person's direct SharePoint permission. [M07]

### 7.2 Mandatory policy decision — do not silently change AGENTS.md

At the inspected checkpoint, `AGENTS.md` prohibits **all tenant-wide Microsoft Graph scopes**. Therefore this proposal's directory reader conflicts with that literal rule. Selected SharePoint permissions do not authorize reading Entra users. [R01, M05]

Before implementing/enabling the automatic picker, obtain an owner-approved ADR with these narrow boundaries:

- Permit **read-only `User.Read.All` solely for the separate directory reader**, with tenant consent, field minimization, admin-only display and no access-derived role inference.
- Permit a separately approved, **administrator-run bootstrap session** to create/reuse registrations and consent/site grants with its necessary administrative permissions.
- Continue to prohibit tenant-wide document permissions in normal runtime, including `Sites.ReadWrite.All`, `Files.ReadWrite.All` and `Sites.FullControl.All`.
- Keep `Directory.ReadWrite.All`, password-reset permissions, general permission-management APIs and arbitrary Graph calls out of the application runtime.
- Record identity, scope, owner, purpose, retention, credential location and revocation procedure. Change repository policy only through the authorized review process.

Microsoft distinguishes the authority needed for Graph application consent from ordinary application administration; a tenant account called “admin” is not automatically sufficient. The setup helper must name the actual required role/permission and stop on insufficiency. [M08]

If the exception is declined, retain the existing approved-roster/verified-sign-in route as a clearly labelled manual alternative; do not add a second login system. Automatic directory selection remains **blocked**, not falsely completed. No secret widening of permissions is an acceptable fallback.

### 7.3 No tenant-wide site picker

Use the supplied exact working-site URL/ID. Graph's site-search method does not support `Sites.Selected` application permission. Do not request `Sites.Read.All` simply to make a search dropdown work. [M09]

Site-grant creation belongs in the privileged bootstrap/admin path. The documented site permission operation grants **application** access, not human site membership. It is not a replacement for user permission provisioning. Test its exact supported auth/role combination in the approved tenant. [M10]

### 7.4 Do not duplicate professional roles in Entra

The default installation uses AuditSphere's existing local grants as the authorization source. OIDC admission and professional role/scope assignment remain distinct.

Do not require the administrator to assign every professional role once in Entra and again in AuditSphere. For a new approved deployment, document whether the enterprise application allows tenant users to authenticate while AuditSphere denies all unassigned access. This is acceptable only with tested server-side deny-by-default behavior.

If tenant policy requires **User assignment required** in Entra, preserve it and display the additional Entra admission prerequisite. Do not silently disable the policy or grant runtime `AppRoleAssignment.ReadWrite.All` to hide that extra step. Any future Entra admission synchronization is separate, not a second professional-role authority.

### 7.5 Credentials

Prefer an already-supported managed/workload identity in a compatible deployment; otherwise use protected certificate credentials. Reuse an existing approved web-secret configuration only under the firm's expiry/rotation policy while adding the supported certificate path as needed. Do not handwrite token-signing or OIDC protocols. Never share one powerful runtime credential between web, directory and documents. Microsoft's credential guidance informs these choices; the exact hosting capability must be tested. [M11]

---

## 8. Role catalogue and assignment rules

The 14 target personas below come from the supplied STE requirements (§2.1). Code mappings marked **new/resolve** are design requirements, not claims that the current repository already implements them. Preserve current finance/legacy codes until an explicit migration is approved.

| Target role | Code reuse / proposed distinction | Allowed scope and landing | Critical restriction |
|---|---|---|---|
| Relationship owner | Reuse `RelationshipManager` where equivalent | Assigned prospects/clients; commercial workspace | No professional acceptance or audit opinion authority |
| Onboarding coordinator | New/resolve `OnboardingCoordinator` | Assigned onboarding cases | Collect/submit only; cannot approve own assessment |
| Compliance officer | New/resolve `ComplianceOfficer` | Assigned restricted cases | Specialist recommendation is not partner acceptance |
| Engagement partner | Reuse `Partner` | Assigned clients/engagements | Authority and independence prerequisites remain; no self-EQR |
| Engagement manager | Reuse `Manager` | Assigned clients/engagements | No independent review of materially self-prepared work |
| Preparer / associate | Reuse `Staff` if its permitted actions match | Assigned engagements; preparation queue | No self-approval or final release |
| Senior reviewer | New/resolve `SeniorReviewer` | Assigned review work | Do not alias to the broader Manager role |
| Engagement quality reviewer | New/resolve `EqrReviewer` plus eligible `EqrCase` | Explicit EQR assignment | Cannot prepare the engagement or replace partner responsibility |
| Client administrator | New/resolve `ClientAdministrator`, client identity kind | Named client/entity scope; portal | Can request contact changes, not grant staff roles or expand scope |
| Client finance contributor | New/resolve `ClientFinanceContributor`, client identity kind | Assigned PBC/client tasks; portal | Can submit information, not approve accounts or clear internal review |
| Client authorized signatory | New/resolve `ClientSignatory` plus authority evidence | Explicit management mandate; portal | No independent audit-opinion approval |
| Billing officer | New/resolve `BillingOfficer`; reconcile existing finance roles | Firm billing scope | No automatic ledger-review/financial approval privilege |
| Records administrator | New/resolve `RecordsAdministrator` | Assigned archive/records operations | Cannot edit signed content or bypass holds |
| System administrator | Reuse `Administrator` | Explicit firm administration | No automatic partner, client-signatory or EQR authority |

### Rules for implementation

- Maintain one canonical server-side role catalogue reused by validators, UI and tests. Normalize submitted codes to the stored canonical value; reject unknown codes.
- Reconcile `FinanceManager`, `FinanceReviewer` and legacy `ClientUser` explicitly. They are not evidence that billing, client administrator and signatory permissions are interchangeable.
- Fix the current staff-only provisioning assumption for client roles. A `ClientUser` string on a staff record is not an adequate client-isolation model.
- Entra `userType` is a directory fact, not an application role. Existing guests default to client classification; staff/external-reviewer exceptions require explicit approved identity classification.
- Separate role from engagement assignment and from professional decision authority. Multiple compatible roles are allowed; object-level maker/checker restrictions still apply.
- Self-elevation to a privileged role and widening one's own professional scope require another authorized approver. The first administrator bootstrap is a narrowly defined installation exception, not ongoing permission to self-award professional roles.
- Client grants cannot be firm-wide. The staff wizard uses the scope kinds the existing command actually supports; do not assume `GROUP` works merely because another module mentions group access.
- Grant validity and target identity must be checked in the commit transaction. Revocation, role replacement and invitation operations produce append-only attribution.
- Last-administrator checks must count **enabled, effective, usable** administrator identities, not merely unrevoked grant rows. Serialize concurrent administrator-removal operations on the firm's existing guard; two simultaneous removals must not leave zero usable administrators.
- A disabled Microsoft administrator must still be denied; last-admin protection cannot preserve access for a disabled identity. Use the documented operator-controlled recovery procedure.
- Homepages may reuse existing screens. Missing business functions must remain labelled unavailable; adding a role label must not imply an undelivered portal or grant a broader substitute role.

**Completion for role selection:** every enabled target persona has a real canonical policy, valid scope model, permitted landing route and positive/negative tests. Professional actions still require their own existing business gates. This story does not certify every domain feature merely because its role is assignable.

---

## 9. Identity, sessions and revocation

### 9.1 Stable identity and eligibility

Store `(FirmId, TenantId, EntraObjectId)` with the existing `AppUser` link and uniqueness rules. Email/UPN/name are editable contact attributes, not identity keys. Validate tenant/object IDs rather than accepting arbitrary strings from a browser or roster. Microsoft's `oid`/`tid` claims support this identity binding. [M04]

Directory listing creates candidate observations, not application access. Create/bind a local user only on approved selection or verified binding to a previously authorized identity. Email collisions enter a review state; do not merge two different object IDs.

Microsoft Graph can exhibit replication delay. A cached roster is evidence of its observation time, not an instantaneous disabled-user guarantee. [M05]

### 9.2 Small directory implementation

Illustrative Graph v1.0 request contracts:

```http
GET /v1.0/users?$select=id,displayName,userPrincipalName,mail,accountEnabled,userType&$top=100
GET /v1.0/users/{stored-object-id}?$select=id,displayName,userPrincipalName,mail,accountEnabled,userType
```

These are paths relative to the fixed Graph host. The user-detail endpoint is documented separately. [M15] Use approved SDK/typed HTTP clients, bounded pages and validated provider continuation links. Do not expose arbitrary `$filter`, URLs or pagination tokens from the browser to Graph. Optional indexed search may be added only with verified query semantics.

Discovery is on demand; a scheduled job refreshes assigned identities. Suggested operating defaults for owner approval are a five-minute refresh target and a 15-minute maximum successful-observation age for protected business access. These are application targets, not Microsoft guarantees. At the freshness limit, deny business operations and request/queue a refresh; do not silently extend stale access through an outage. A bounded identity refresh must not run while database locks are held.

Freshly created/changed grants need a recent successful enabled-state observation and current local state. An unavailable response is `UNKNOWN`, not `ENABLED`. A 403/outage is not evidence the person was deleted. A failed/incomplete discovery page cannot revoke everybody missing from that page.

### 9.3 Existing sessions and Blazor

Continue using the established OIDC/cookie flow; do not replace validated middleware with decoded JWTs. Enforce issuer/audience/signature/lifetime, state/nonce, PKCE where supported, exact redirects and local-only return destinations.

Recheck local disabled state, effective scope and `SessionEpoch` on protected requests **and Blazor service operations**. Cookie validation alone is insufficient for a long-lived interactive circuit. Revalidation must remove access without trusting an old role list held in a component.

On local revoke, commit the grant change, session/access-version change and audit event together. Cancel pending user-requested exports/invitations according to their operation policy. Do not cancel already authorized system delivery merely because its original requester left; retain existing release semantics.

Microsoft cannot directly revoke the session cookie issued by AuditSphere. The application must enforce its own revocation, with directory refresh and emergency local disable. A logout button alone does not solve it. [M13]

Do not reset Microsoft passwords or revoke all Microsoft sessions as a side effect of removing one AuditSphere assignment. Already-downloaded files cannot be recalled by revoking application access.

---

## 10. SharePoint and client workspaces

### 10.1 Working-site selection

Use one approved working site and the existing `FirmWorkspaceConfiguration`. Retain separate restricted/records repositories where the project requires them; this story does not combine them into one broad site for convenience.

Default root template:

```text
Selected working site / selected library / AuditSphere
  /Clients/{CLIENT_CODE}
    /Engagements/{ENGAGEMENT_CODE}
      /01_Administration
      /02_Planning
      /03_PBC
      /04_Accounting
      /05_Working_Papers
      /06_Review
      /07_Draft_Deliverables
```

Internal IDs/codes establish ownership; names are display conveniences. Restricted compliance materials and protected final records must not be moved here by the template.

A client root can be queued after the existing affirmative client-acceptance decision. Engagement folders require the existing engagement authorization conditions. Prospect creation, proposal acceptance or a role invitation alone must not activate professional work.

### 10.2 Provisioning protocol

Reuse `ClientWorkspace`, `FolderTemplateVersion`, `RepositoryBinding` and durable operations.

1. Validate current acceptance/engagement, approved connection revision, template version and scope.
2. Persist a unique workspace intent and outbox item.
3. Worker resolves the approved parent and creates/reconciles only the server-determined structure.
4. Read back actual item IDs, ownership metadata and expected access.
5. Publish the binding/`READY` state only after successful verification and current dependency checks.

Retry after remote success must reuse the same logical workspace; it must not create `Client (1)` or infer success from a similar folder name. Unknown collisions become `CONFLICT_REQUIRES_REVIEW`. A revoked grant results in a blocked state, not broadened consent.

### 10.3 Human access is different from application access

`APP_MEDIATED` users view/upload permitted files through AuditSphere. They receive no general SharePoint site membership and do not get a live Office edit link by default.

For optional `DIRECT_STAFF_COLLABORATION`, require verified human permissions and removal procedures in addition to the application's selected-site grant. Local role changes cannot be advertised as instantly removing direct Microsoft access. Do not grant SharePoint Owner/Full Control just because somebody is an AuditSphere partner or administrator.

A staff member may use a permitted SharePoint folder through OneDrive's shortcut/sync experience; files remain in SharePoint. The shortcut is convenience, not an access grant. No personal-OneDrive permission or automatic shortcut-creation API is required by this story. [M14]

### 10.4 Verification and activation

The administrator clicks **Test connection**, not **Mark verified**. Trusted execution records exact tenant, app/credential reference, resource IDs, connection revision, operation, observed result and time. Ordinary UI payloads cannot declare a provider `PASS`.

Evaluate the latest applicable result, not any old successful entry. A later failure, changed tenant/site/credential or different revision invalidates activation evidence. Require a permitted-site positive check and an authorized synthetic unrelated-site negative check before accepting the selected-resource boundary. Keep test artifacts in an explicitly disposable test area; never probe destructive operations against client evidence.

---

## 11. Invitation delivery

### Invitation content

```text
Subject: You have been invited to AuditSphere

Your organization has granted you access to AuditSphere.
Use your existing Microsoft 365 account to open the application.
No new AuditSphere password is required.

[Open AuditSphere]

Contact your organization's administrator if the expected access is missing.
```

Role/scope details may be included only at the level permitted by notification policy; do not put client financial data, tenant secrets, bootstrap proofs or sensitive engagement names in an email.

### Rules

- An invitation is an application notification for an existing identity, not a new Entra/B2B account invitation.
- Use the selected, administrator-confirmed delivery address. `mail` can be absent; UPN is not guaranteed to be a deliverable mailbox. Allow confirmed notification-address override without changing the identity binding. Otherwise use copy-link.
- Local grants and invitation intent commit atomically. Reuse the existing mail delivery infrastructure after commit; do not call mail inside the transaction.
- Track access state separately from transport: `ASSIGNED / ACTIVE / REVOKED` versus `NOT_SENT / QUEUED / PROVIDER_ACCEPTED / FAILED / UNKNOWN / COPIED`.
- Record actual first authorized access independently. Copying a link or a provider accepting a message does not prove delivery, opening or sign-in.
- Repeated Save requests return the same assignment/invitation result. Resend creates a deliberate delivery attempt and does not duplicate roles.
- A transport timeout after success may leave delivery `UNKNOWN`; reconcile where supported. Do not promise exactly-once email delivery.
- Open links contain only the approved application origin and safe navigation target. No bearer-token login, client secret, role list or account ID that grants access.
- Verify sender/recipient in an authorized sandbox before enabling production invitations.

When the application lacks an approved sender, **Copy invitation** remains usable and the screen states **Not sent by AuditSphere**. Do not make a new mail integration a dependency for Microsoft sign-in.

---

## 12. Data and command contracts

### 12.1 Reuse before adding records

| Existing concept | Required treatment |
|---|---|
| `Microsoft365SetupSession` / `Microsoft365SetupDraft` | Keep private, scoped and expiring; bind first-admin creation to an authenticated approved identity. |
| `Microsoft365ConnectionRevision` | Preserve version history; add a separate directory credential reference if no equivalent exists. Never store secret material. |
| `IntegrationVerificationEvidence` | Distinguish local/test assertions from real provider observations; bind capability, revision and latest result. |
| `FirmWorkspaceConfiguration` / `FolderTemplateVersion` | Reuse selected site/library/root and immutable template approval. |
| `AppUser` / `RoleGrant` / grant-change evidence | Keep stable identity, explicit kind/scope and session-epoch semantics; extend only for missing validated roles. |
| `ClientWorkspace` / `RepositoryBinding` | Reuse existing desired/observed state and provider IDs. |
| Existing operations and mail delivery records | Reuse retries, attempts and correlation; no second queue. |

Add only if absent:

**DirectoryUserObservation:** connection/tenant/object identity, selected display/contact fields, account enabled/type, observed time, provider result and roster operation. It is an admin-only cache, not a new credential directory.

**UserAccessInvitation:** firm/user, grant-change reference, notification recipient, safe destination, transport status, first-use timestamp, attempt/correlation references. Keep grant validity in the existing authorization model rather than duplicate it here.

**Role catalogue metadata:** canonical code, user-kind constraints, permissible scope kinds, display label and implemented policy. Prefer code/configuration already owned by the authorization layer, not a generic editable permission designer.

### 12.2 Proposed commands, mapped to existing services

| Command | Important server-side condition |
|---|---|
| CompleteInitialAdministratorBootstrap | Exact configured tenant/object + expiring proof + serialized one-time creation |
| VerifyMicrosoft365Connection | Trusted credential/resource operation; no browser-supplied success |
| RefreshDirectoryCandidates | Authorized administrator; fixed query/paging/field policy |
| PreviewUserAssignments | Resolve identities and existing grants; no mutation |
| ApplyUserAssignmentsAndInvite | Fresh identity observations + expected revisions + all-or-nothing local grants |
| ChangeUserScope / RevokeUserAccess | Current administrator authority, no self-escalation, serialized last-admin check |
| ResendUserInvitation | Current grant and recipient; deduplicated attempt, not new access |
| ProvisionAcceptedClientWorkspace | Current acceptance, connection/template and exact selected parent |

These are intent names, not instructions to duplicate existing methods or introduce another command-bus framework.

### 12.3 Example transport shape

```json
{
  "connectionRevisionId": "<verified-connection-revision-id>",
  "expectedAccessRevision": 4,
  "idempotencyKey": "<unique-admin-command-key>",
  "users": [
    {
      "directoryObjectId": "<selected-entra-object-id>",
      "roleCode": "SeniorReviewer",
      "scopeKind": "ENGAGEMENT",
      "clientId": "<authorized-client-id>",
      "engagementId": "<authorized-engagement-id>"
    }
  ],
  "notify": true
}
```

The server derives actor, firm, allowed tenant, real identity facts and permitted scope. It does not trust a supplied email, `isAdmin`, `approvedBy`, `enabled`, Graph URL or role label as evidence. The example is a schema illustration, not a request executed against the project.

### 12.4 Atomicity and idempotency

Enforce uniqueness for identity binding, effective grant identity and command receipts. Bind idempotency keys to actor/firm/action and a canonical request digest. Same key/different payload returns a conflict. Reuse existing ordered guards; compare expected revisions in the same transaction as the write. Do not perform a read/compare followed by an unguarded save.

The administrator batch must not call existing per-user methods that independently commit and then claim the entire batch is atomic. Refactor only the necessary transaction ownership or use a new small orchestration method around the existing invariants.

---

## 13. Integration, concurrency and failure handling

### Fixed capability activation, not one universal switch

The inspected web host deliberately rejects an enabled external composition. Replace that refusal only with a validated composition for the specific implemented capabilities. Do not simply remove the throw or turn `ExternalEffects.Enabled=true`. [R02]

Separate readiness for sign-in, directory read, invitation delivery and working SharePoint. Preserve the deployment epoch/recovery fence and keep signing, release checkpoints and records gates independently disabled until accepted. No large new feature-flag platform is necessary; extend the existing explicit readiness records/validators.

| Condition | Required behavior |
|---|---|
| No app registration/credential configuration | `CONFIGURATION_REQUIRED`; setup guidance; no fake tenant login |
| Wrong bootstrap identity, expired/reused proof | Deny first-admin creation without affecting existing users |
| Consent denied or insufficient Microsoft administrator role | Explain the required action; retain draft; no permission escalation |
| Directory policy exception missing | Automatic picker blocked; existing manual roster clearly distinguished |
| Selected site lacks explicit application grant | `SITE_GRANT_REQUIRED`; identity can remain ready; document work blocked |
| Graph 401 | Reacquire supported service token once; persistent failure requires operator action |
| Graph 403 | Diagnose consent/grant/endpoint policy; never auto-request broader permissions |
| Graph 429/5xx | Honor provider backoff; bounded retries through existing operations |
| Partial directory load | Show incomplete result; no false full count or mass deprovision |
| User disabled, unknown or stale | Deny new grants/activation; apply §9 freshness and local-access policy |
| Directory account enabled again | Do not automatically restore revoked local grants |
| Setup site/tenant/credential changes | New revision and verification; no silent reassignment of historical files/users |
| Mail unavailable | Grant remains accurately recorded; invite pending/failed or copy-link |
| Workspace upload succeeds but response is lost | Reconcile operation/provider item; no duplicate tree |
| Two admins edit grants or remove last admin | Transactional conflict or protected refusal; no lost update |
| Database restored behind provider state | Outward effects quarantined; reverify identities, connection and invitations before replay |
| Direct SharePoint revocation unverified | Local deny immediately; show Microsoft revocation pending; no false complete state |

Use separate credential mounts for the reader and document identity; the web UI reads authorized projections and queues typed operations. Never allow an outbox payload to specify arbitrary Microsoft URLs, HTTP methods, consent scopes or grant recipients.

---

## 14. Implementation sequence

One parent story; split into small dependency-ordered pull requests where needed. First read the current `AGENTS.md`, specification and existing implementation so equivalent work is reused.

| Slice | Deliverable | Exit evidence |
|---|---|---|
| S1 — Reconcile policy and roles | Source-to-code delta; narrow Graph policy ADR; 14-role policy mapping; bootstrap approach | Approved design decisions or explicit blocked items; no broad-scope changes hidden in code |
| S2 — Bootstrap and sign-in | Reviewed setup helper; exact first-admin binding; existing OIDC flow; credential/configuration validation | First admin cannot be hijacked; existing M365 account enters without an app password |
| S3 — Directory and assignment | Worker-backed candidate picker, assigned-user checks, client/staff classification, scoped batch grants | Correct identity, all enabled role policies, scope denial and no partial batch writes |
| S4 — Invitations and landing | Existing mail/copy-link, attempt status, first access and correct staff/client entry | Forwarded link grants nothing; wrong role cannot enter restricted routes |
| S5 — SharePoint binding | Exact site/library/root selection, real provider checks, accepted-client workspace provisioning | Selected positive/unrelated negative checks; retry-safe folders; no document access inferred from login |
| S6 — Revocation and usability | Session/circuit checks, disabled-user processing, local removal, setup diagnostics | Existing sessions lose protected access; no default full access or silent readiness |
| S7 — Authorized acceptance | PostgreSQL/HTTP/browser tests and real tenant checks with recorded evidence | Scoped identity/invitation/document profile accepted; other production gates remain separate |

### Expected code touchpoints

- `src/AuditSphereOps.Web/Program.cs` and current authentication/actor resolvers.
- Existing Microsoft 365 setup and administration screens; add/reuse a users/access page.
- `src/AuditSphereOps.Application/Microsoft365/*` and `Security/RoleAdministrationService.cs`.
- Existing domain security/Microsoft365/document records and EF configurations/migrations.
- Existing provider interfaces and worker registration. Reuse `IPbcProviderSink` rather than create a competing DMS adapter stack.
- Small directory provider/client and worker handler, only where absent.
- Existing mail-delivery abstraction, extended to an access-invitation purpose without weakening recipient checks.
- `scripts/m365/Initialize-AuditSphereTenant.ps1`, deployment configuration examples and operator instructions.
- Relevant existing tests plus minimal scenarios covering the changed behavior.

Keep existing C#/.NET, Blazor, EF Core and PostgreSQL. Use already-approved package versions; verify and pin compatible stable additions. Reuse the existing OIDC implementation unless a demonstrated capability gap requires a small supported authentication-library addition. Microsoft Graph SDK/typed HTTP and Azure.Identity belong at provider boundaries, not inside domain calculations or UI components.

No repository restructure, Frappe/ERPNext installation, new SPA, microservices, personal-OneDrive database, extra broker or bespoke malware scanner is needed.

---

## 15. Acceptance scenarios

These are **planned tests**, not executed results. Use existing scenario infrastructure where equivalent tests already exist. Parameterize the role matrix instead of creating redundant test frameworks.

| ID | Scenario | Required result |
|---|---|---|
| M365-01 | Fresh install with no trusted configuration | Shows setup-required state; no normal password registration or fabricated tenant data |
| M365-02 | Attacker is first visitor or first tenant user | Cannot claim administrator without exact approved identity and bootstrap proof |
| M365-03 | Expired, replayed or concurrent bootstrap requests | One authorized administrator binding at most; expired/reused privilege denied |
| M365-04 | Forged consent callback/state/tenant | No login/verification/activation; actual provider proof still required |
| M365-05 | Consent/policy approval missing | Picker/documents remain blocked as appropriate; no broader permission fallback |
| M365-06 | Same setup helper run twice | Same installation registrations/bindings reused; unrelated resources unchanged |
| M365-07 | Directory has more than one page | All retrieved pages accounted for; partial/failure state truthful; only minimum fields exposed |
| M365-08 | Disabled, deleted, unknown or stale directory identity selected | New usable access denied; disabled is not hidden by a locally enabled row |
| M365-09 | Same email, different object IDs; changed email on same object | No identity merge/elevation; verified same-ID contact change preserves history |
| M365-10 | Save batch has one invalid role or scope | Entire local batch rejected; no partial grants or invitation intents |
| M365-11 | Same idempotency key, same versus different body | Same result on retry; conflict for different requested grants |
| M365-12 | Each of 14 enabled target personas signs in | Correct kind, policy, landing and permitted actions; no broad role alias |
| M365-13 | Client contributor attempts signatory/staff/admin actions | Denied by server even with forged role, route or client IDs |
| M365-14 | Client A contact guesses Client B IDs | No records, metadata, counts, files or export leakage |
| M365-15 | Reviewer also holds preparer role for same object | Existing maker/checker rules still deny self-approval |
| M365-16 | Administrator tries self-elevation/professional scope expansion | Another authorized approval required; no tenant-admin-to-partner inheritance |
| M365-17 | Concurrent last-administrator removals | At least one eligible usable administrator remains, or both conflicting requests fail safely |
| M365-18 | Already signed into Microsoft, opening normal app link | OIDC can reuse Microsoft session; no AuditSphere password/duplicate signup |
| M365-19 | MFA, account selection or session expiry required | Microsoft interaction allowed; no bypass and no unconditional zero-prompt claim |
| M365-20 | Unassigned tenant member signs in | Access-not-assigned only; no default staff creation or business data |
| M365-21 | Invitation forwarded to another identity | Forwarded link grants no access; normal identity/grant rules apply |
| M365-22 | Mail unavailable, recipient missing, resend or timeout | Truthful transport states; copy-link works; no role duplication/exactly-once email claim |
| M365-23 | Local role revoked with active cookie/Blazor circuit | Next protected action denied; old component/session role state cannot bypass |
| M365-24 | Entra disable observed or identity freshness expires | Application access suspended/blocked according to explicit policy; outage not guessed as deletion |
| M365-25 | Microsoft account later reenabled | Revoked local grants do not silently reappear |
| M365-26 | Site consent exists but resource grant does not | Site verification fails; no false documents-ready status |
| M365-27 | Allowed working site versus unrelated synthetic site | Permitted operation succeeds only within approved selected-resource boundary |
| M365-28 | Administrator attempts arbitrary URL/path/root injection | Fixed provider host and stored bindings enforce destination; no SSRF/cross-tenant access |
| M365-29 | Prospect/declined client versus accepted client | No work-authorizing workspace for the former; eligible client gets one verified tree |
| M365-30 | Workspace worker crashes after remote success | Same logical workspace reconciled; duplicate folders not silently created |
| M365-31 | Old PASS followed by failure or changed connection revision | Latest exact evidence controls readiness; stale success cannot activate |
| M365-32 | APP_MEDIATED versus optional direct Office access | No automatic site membership; direct mode requires actual human permission proof |
| M365-33 | Wrong tenant or guest not yet approved/redeemed | No account binding or portal activation; clear non-sensitive guidance |
| M365-34 | Restore older DB with queued invitations/provisioning | Existing external fence prevents automatic replay until reconciliation |
| M365-35 | Setup succeeds while signing/records acceptance is absent | Users can access permitted ready features; reporting/records gates are not falsely accepted |
| M365-36 | Configuration/log/repository inspection | No raw credentials, bootstrap tokens, private keys, roster exports or sensitive provider links committed/logged |
| M365-37 | Unverified role string casing/legacy ClientUser on staff | Canonical role storage; explicit compatibility decision; no client-to-staff privilege confusion |
| M365-38 | Tenant enforces enterprise-app assignment | Requirement preserved and shown; no silent policy disable or runtime broad role-management grant |

### Test evidence

Record scenario, commit, environment, auth/provider mode, expected/actual outcome, timestamp, redacted artifact reference and reviewer. Unit/mocked provider success proves local behavior only. Real OIDC, consent, directory status, SharePoint restrictions and delivery require authorized tenant evidence.

Build/test commands must match the checkout. Normally restore the locked toolchain, build the **whole solution** in Release, then run the relevant tests and full suite in that same configuration. Database changes require a disposable approved PostgreSQL environment, migration/drift checks and applicable restore coverage. Never apply a test migration to production from this story.

---

## 16. Definition of done and tenant checklist

### Engineering completion

- Existing setup and authorization services extended rather than replaced.
- Single Microsoft login path; no app password, magic-link login or default staff access.
- Approved policy exception or explicit automatic-picker blocker recorded.
- All enabled roles have real policies, scopes and negative tests; legacy roles handled deliberately.
- Immutable identity binding, guarded grants, secure bootstrap and revocation across requests/circuits implemented.
- Directory data, SharePoint identity, notification transport and provider verification are independently truthful.
- Typed worker operations, request idempotency and recovery fencing retained.
- Configuration/certificate references and deployment restart/rotation instructions provided.
- Relevant tests, migrations, documentation and administrator walkthrough complete; no new unnecessary infrastructure.

### Tenant acceptance — authorized owner supplies

| Prerequisite | Owner/evidence |
|---|---|
| HTTPS application origin and approved environment | Deployment owner |
| Initial administrator's real tenant/object identity | Authorized installation/tenant owners |
| Required consent and narrow scope-policy approval | Security/repository owner plus authorized tenant consent administrator |
| Approved working site/library and separate test denial target | SharePoint administrator |
| Credential custody, callback registration and rotation | Deployment/security owner |
| Named enabled/disabled staff and approved client fixtures | Tenant/application administrators |
| Role/scoping and professional authority decisions | Practice owner; accounting/audit owners where applicable |
| Invitation sender or acknowledged manual sharing mode | Mail/operations owner |
| Actual positive and denial observations | QA plus independent reviewer as required |

Do not claim “production accepted” because a setup screen is green. The professional deployment's existing P1/P2 and other gates remain governed by their exact required evidence. Missing real access yields `BLOCKED_EXTERNAL`; independent safe local work may proceed only within the repository's sequencing policy.

**Usability acceptance:** After the one-time approved bootstrap, a tenant administrator can add another existing M365 person from the application without editing configuration, copying object IDs, resetting passwords or assigning professional roles again in Entra. Where enterprise-app admission is mandated by tenant policy, show that explicitly rather than concealing an unavoidable approval.

---

## 17. Short Codex prompt

```text
Implement docs/requirements/AuditSphere_M365_Simple_Onboarding_User_Story.md in nirzaf/AuditSphere. Read current AGENTS.md, SPECIFICATION.md and existing M365/auth/role/provider code first; reuse implemented behavior.

Deliver the smallest complete flow: approved one-time tenant bootstrap -> Microsoft-only SSO -> select existing M365 users -> assign real roles and explicit scope -> invite through existing mail or copy-link -> correct staff/client landing -> tested revocation. Extend existing selected-SharePoint bindings and accepted-client folder provisioning. No separate passwords, stack rewrite, role-label-only shortcuts or personal OneDrive storage.

Respect the story's policy blocker: User.Read.All directory access and privileged bootstrap require a narrowly approved exception to the current no-tenant-wide-Graph rule; never silently change policy or consent. Preserve immutable identity binding, maker/checker rules, session epochs, last-admin protection, outbox, provider verification and all release/records gates.

Work in dependency-ordered small branches/PRs. Test local behavior with PostgreSQL and browser coverage; record live Microsoft checks separately as BLOCKED_EXTERNAL until actually authorized and observed. Do not merge, deploy, grant consent or change tenant resources without the required explicit authorization. Report implemented criteria, tests, blockers and next permitted step.
```

---

## 18. Source register

### Source boundaries

**User goal:** Simple tenant administration, existing Microsoft identities, local role/scope assignment, invitations and SharePoint integration.

**Repository evidence:** Targeted source reads at the checkpoint stated above. The read establishes code/configuration behavior only; no build, local runtime, real tenant or production environment was exercised in this documentation task.

**Business-role source:** Supplied *STE Audit & Accounting Practice Platform — Project Requirements and End-to-End Client Lifecycle*, STE-PRD-001 revision 1.0, §2.1–2.2 and §21. The 14 role names are retained; proposed code mappings are not asserted as existing implementation.

**New design:** Wizard flow, constrained bootstrap contract, directory refresh defaults, assignment UX, delivery fallback and acceptance scenarios are proposed requirements. They are not vendor guarantees.

### Repository references (pinned)

- [R01 — AGENTS.md](https://github.com/nirzaf/AuditSphere/blob/0f9f10c63c8f5f97aca50546b6f0c36895b5a5e1/AGENTS.md): stack, scope and Graph safety rules.
- [R02 — Web Program.cs](https://github.com/nirzaf/AuditSphere/blob/0f9f10c63c8f5f97aca50546b6f0c36895b5a5e1/src/AuditSphereOps.Web/Program.cs): current OIDC and live-composition boundary.
- [R03 — Microsoft365OnboardingService.cs](https://github.com/nirzaf/AuditSphere/blob/0f9f10c63c8f5f97aca50546b6f0c36895b5a5e1/src/AuditSphereOps.Application/Microsoft365/Microsoft365OnboardingService.cs): bootstrap and draft service.
- [R04 — Microsoft365ConfigurationService.cs](https://github.com/nirzaf/AuditSphere/blob/0f9f10c63c8f5f97aca50546b6f0c36895b5a5e1/src/AuditSphereOps.Application/Microsoft365/Microsoft365ConfigurationService.cs): connection, evidence and template service.
- [R05 — RoleAdministrationService.cs](https://github.com/nirzaf/AuditSphere/blob/0f9f10c63c8f5f97aca50546b6f0c36895b5a5e1/src/AuditSphereOps.Application/Security/RoleAdministrationService.cs): role allowlist, staff provision path, grants and revocation.
- [R06 — Microsoft365Onboarding.cs](https://github.com/nirzaf/AuditSphere/blob/0f9f10c63c8f5f97aca50546b6f0c36895b5a5e1/src/AuditSphereOps.Domain/Microsoft365/Microsoft365Onboarding.cs): persisted control-plane records and states.

### Microsoft primary references, consulted 22 September 2026

- [M01 — OAuth 2.0 authorization-code flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow).
- [M02 — Administrator consent protocol](https://learn.microsoft.com/en-us/entra/identity-platform/v2-admin-consent).
- [M03 — Microsoft Entra single sign-on](https://www.microsoft.com/en-us/security/business/identity-access/microsoft-entra-single-sign-on).
- [M04 — ID-token claim reference](https://learn.microsoft.com/en-us/entra/identity-platform/id-token-claims-reference).
- [M05 — List users](https://learn.microsoft.com/en-us/graph/api/user-list?view=graph-rest-1.0).
- [M06 — Graph permissions reference](https://learn.microsoft.com/en-us/graph/permissions-reference).
- [M07 — Selected permissions overview](https://learn.microsoft.com/en-us/graph/permissions-selected-overview).
- [M08 — Grant tenant-wide administrator consent](https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/grant-admin-consent).
- [M09 — Search for sites and its Selected-permission limitation](https://learn.microsoft.com/en-us/graph/api/site-search?view=graph-rest-1.0).
- [M10 — Create a site application permission](https://learn.microsoft.com/en-us/graph/api/site-post-permissions?view=graph-rest-1.0).
- [M11 — App-registration security practices](https://learn.microsoft.com/en-us/entra/identity-platform/security-best-practices-for-app-registration).
- [M12 — B2B collaboration overview](https://learn.microsoft.com/en-us/entra/external-id/what-is-b2b).
- [M13 — User access revocation and application sessions](https://learn.microsoft.com/en-us/entra/identity/users/users-revoke-access).
- [M14 — SharePoint and OneDrive sync](https://learn.microsoft.com/en-us/sharepoint/sharepoint-sync).
- [M15 — Get a user](https://learn.microsoft.com/en-us/graph/api/user-get?view=graph-rest-1.0).

**Final implementation principle:** Make the administrator's experience simple by automating verified setup and reusing existing authorization—not by adding passwords, duplicating roles or hiding Microsoft permission and production-readiness requirements.
