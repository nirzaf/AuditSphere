# Microsoft 365 tenant administration permission decision

**Status: APPROVED boundary; optional mutation capabilities IMPLEMENTED and disabled by default.**
The repository owner's tenant-administration request changes the former blanket
prohibition on tenant-wide Graph scopes. This decision permits only the
capabilities in the matrix below, each with its own app identity, its own
administrator consent and its own verification. Every optional capability is
off until a deployment operator enables it, supplies its separate certificate
through an approved secret store, a tenant administrator consents, and
AuditSphere records a `VERIFIED` result for that exact tenant. Nothing in this
document grants consent, turns on external effects or records live acceptance.

## Security boundary

- AuditSphere `RoleGrant` (and `GroupAccessGrant` for consolidation groups) remains
  the sole source of application roles and scope. No AuditSphere role maps to a
  Microsoft Entra administrator role, and Entra directory-role assignment is not
  implemented (`PRIVILEGED_ROLE_ADMINISTRATION` is deliberately absent). Enabling it
  would require a separate Privileged Tenant Administration feature, additional
  consent, step-up authentication and separate acceptance tests.
- Microsoft group membership is never an AuditSphere authorization source.
- Web sign-in, the directory reader/consent app, the selected-site document worker,
  the outbound-mail app, and **each tenant-administration capability** are separate
  app identities. Every capability identity may hold **exactly one** Graph
  application role: `User.Read.All`, `Sites.Selected`, `Mail.Send`, `User.Create`,
  `User.Invite.All`, or `GroupMember.ReadWrite.All`, respectively. A token is
  accepted only for the exact tenant and only when it holds its single required role
  (`GraphCapabilityTokenSource`); any additional role, such as `Sites.Selected` or
  `Directory.ReadWrite.All`, is refused. No runtime identity combines SharePoint,
  mail, directory-reading, provisioning, invitation, or group-membership permissions.
  Startup refuses an enabled capability without a complete certificate or when two
  enabled capabilities reuse an app ID.
- Tenant credentials, access tokens, refresh tokens, authorization codes and
  initial passwords are never written to database records, logs or audit events.
  Consent `state` and `nonce` are stored only as SHA-256 hashes.

## Permission matrix

"Configured?" and "Verified?" are runtime values shown on
**Administration → Microsoft 365 → Security & Consent** from persisted
`m365_tenant_capability_verifications` rows. No permission below has been
consented in a production tenant by this change.

| Capability | Graph endpoint | Permission | Delegated / Application | Why required | Admin consent | Required Entra role to consent/administer | Configured? (default) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `SIGN_IN` | Microsoft identity platform OIDC | `openid profile email` | Delegated | Authenticate one work/school identity and bind immutable `tid` + `oid` | Not required for sign-in scopes | None | Existing |
| Directory consent sign-in | Microsoft admin-consent screen and identity leg | `User.Read` (plus `openid`) | Delegated; Microsoft includes `User.Read` on the initial consent screen | Sign in the tenant administrator for the nonce-bound identity leg; AuditSphere never uses a delegated Graph token for directory access | Shown on the Microsoft consent screen | None beyond the consenting administrator | With `DirectoryReader` |
| `DIRECTORY_READ` | `GET /users`, `GET /users/{id}` | `User.Read.All` | Application, directory-reader/consent app | Bounded search of existing users; exact `accountEnabled`/`userType` checks; UPN/guest reconciliation reads | Required | Privileged Role Administrator or Global Administrator | Off (`DirectoryReader:Enabled`) |
| `SELECTED_SITE` | Exact `/sites/{id}`, `/drives/{id}` | `Sites.Selected` + per-site `write` grant | Application, document-worker app | Read/write only the approved working site | Required, plus a separate exact-site grant | Privileged Role Administrator; SharePoint Administrator for the site grant | Existing (`SelectedSite:*`) |
| `OUTBOUND_MAIL` | `POST /users/{sender}/sendMail` | `Mail.Send` | Application, mail app restricted by an Exchange application access policy | Send AuditSphere notifications from one sender only; no mailbox reading | Required | Privileged Role Administrator; Exchange Administrator for the access policy | Off (`TenantAdministration:OutboundMail`) |
| `TENANT_USER_PROVISIONING` | `POST /users`; reconciliation via reader `GET /users?$filter=userPrincipalName eq` | `User.Create` | Application, dedicated provisioning app identity | Create a workforce user only when explicitly enabled | Required | Privileged Role Administrator or Global Administrator | Off (`TenantAdministration:Provisioning`) |
| `GUEST_INVITATION` | `POST /invitations`; reconciliation via reader | `User.Invite.All` | Application, dedicated guest-invitation app identity | Invite an explicitly approved external client identity; never automatic | Required; tenant B2B policy must allow app-only invitations | Privileged Role Administrator or Global Administrator | Off (`TenantAdministration:GuestInvitation`) |
| `GROUP_MEMBERSHIP` | `GET /groups/{id}`, `GET /groups/{id}/members`, `POST /groups/{id}/members/$ref`, `DELETE /groups/{id}/members/{id}/$ref` | `GroupMember.ReadWrite.All` | Application, dedicated group-membership app identity | Add/remove users only in allowlisted AuditSphere-managed groups | Required | Privileged Role Administrator or Global Administrator | Off (`TenantAdministration:GroupMembership`) |

Microsoft also displays delegated `User.Read` on the initial admin-consent
screen for the directory app; that sign-in/profile scope is documented because
the displayed request, not only the registration manifest, defines what an
administrator is asked to accept.

**Least privilege.** `User.Create` is Microsoft's least-privileged permission for
`POST /users` (not `User.ReadWrite.All`); `User.Invite.All` for `POST /invitations`;
`GroupMember.ReadWrite.All` for user membership changes (not `Group.ReadWrite.All`);
`User.Read.All` is required because `User.ReadBasic.All` does not return
`accountEnabled`/`userType`. Role-assignable groups need
`RoleManagement.ReadWrite.Directory`, so AuditSphere refuses them (and treats an
unknown `isAssignableToRole` as privileged). Dynamic-membership groups are refused.

The isolated client-sites worker has the separately owner-approved [client-site provisioning exception](auditsphere-client-sharepoint-sites-current.md), using Graph and SharePoint `Sites.FullControl.All` only for site creation, exact document-worker grants and site-local staff administration. Every other runtime retains the prohibition below.

No other AuditSphere runtime may request `Directory.ReadWrite.All`, `User.ReadWrite.All`,
`RoleManagement.ReadWrite.Directory`, `Sites.ReadWrite.All`, `Sites.FullControl.All`,
`Files.ReadWrite.All`, `Mail.Read` or `Mail.ReadWrite`
(`Microsoft365PermissionMatrix.Prohibited`, enforced by tests).

## Consent and evidence contract

1. A current firm-wide AuditSphere Administrator starts **Connect Microsoft 365
   tenant**. A one-use, ten-minute attempt binds the actor, session epoch, setup
   draft and configured tenant; only the SHA-256 of `state` is stored.
2. Microsoft's `/adminconsent` page authenticates the tenant administrator and
   collects consent; AuditSphere never collects a Microsoft password. The callback
   must match the hashed state, the same AuditSphere actor and epoch, and the exact
   tenant; otherwise the attempt is consumed as `DENIED`/`EXPIRED`.
3. Because the admin-consent callback does not identify the consenting person, a
   second nonce-bound OIDC authorization-code sign-in to the consent app follows.
   The code is redeemed server-side with the consent app's certificate; the ID
   token's issuer, audience, tenant, expiry and nonce are validated, external
   (`idp` ≠ issuer) and personal-account identities are refused, and only the
   consenting administrator's `tid`/`oid` are stored (`CONSENT_VERIFIED`).
   Replays cannot redeem twice: the identity state is consumed before redemption.
4. Each enabled capability is then verified separately with its own credential
   (exact single app role, plus a bounded read for directory and group access)
   and recorded as `VERIFIED`, `NOT_GRANTED`, `FAILED` or `BLOCKED_EXTERNAL`.
   Capabilities are not usable without a verified consenting administrator, and a
   verification older than 24 hours is `STALE` and blocks mutations.

Administrator-entered evidence references cannot authenticate a Microsoft
grantor: `RecordVerificationEvidenceAsync` refuses a manual `TENANT`/`CONSENT`
pass and never promotes references to a verified connection. Only the
nonce-bound identity leg above sets the connection's consent state to
`VERIFIED`. `ActivateConnectionAsync` requires that `VERIFIED` consent state in
addition to a `VERIFIED` connection revision, so older `OBSERVED` revisions cannot
activate through historical pass rows. Promoting the connection revision itself to
`VERIFIED` still requires a trusted selected-site verification path; recorded
site/library/root references remain observations.

## External operation lifecycle

Microsoft mutations are never combined with a database transaction as if atomic.
`m365_external_operations` records each request with a firm-unique idempotency
key and request fingerprint: `REQUESTED → AUTHORIZED → DISPATCHING →
ACCEPTED | FAILED | UNKNOWN → RECONCILED → BOUND` (or `CONFLICT_REQUIRES_REVIEW`).
Authority is rechecked immediately before dispatch. Only a definite 4xx is
`FAILED`; timeouts, 429 and 5xx are `UNKNOWN`. An `UNKNOWN` or interrupted
operation is reconciled by immutable Microsoft identity (UPN/guest lookup with a
created-after-dispatch check, or membership check) and is never blindly re-sent;
Microsoft's UPN uniqueness also prevents duplicates. The local `AppUser`,
`RoleGrant`, evidence and session-epoch change commit in a separate binding
transaction only after acceptance or reconciliation. Every step writes an
append-only `m365_administration_events` row (actor, firm, target, operation,
old/new state, role/scope change, reason, Microsoft operation and correlation ID,
result, time).

## Directory picker

The Microsoft Directory panel offers bounded prefix search and a paged
**Browse active users** list (25 per page) with an optional UPN domain suffix
filter. The filter uses Graph advanced query requirements
(`ConsistencyLevel: eventual`, `$count=true`) and rejects results from another
tenant or domain. A selected member or guest is re-read by exact object ID before
local binding; neither the picker nor binding grants AuditSphere access.

## Historical development-tenant snapshot (observed 2026-09-28; superseded)

This snapshot predates the current one-role-per-identity requirement and is kept
only as historical evidence. It recorded one administration registration with
`User.Create`, `User.Invite.All`, and `GroupMember.ReadWrite.All`, plus separate
Directory Reader and Mail registrations. That combined administration identity
does not satisfy the current boundary. Its test result does not establish the
current live configuration or acceptance state.

## Remaining external gates

Live capability status must be read from the current deployment's persisted
verification rows. Any capability whose dedicated app registration, certificate,
tenant consent, exact resource grant (where applicable), or fresh verification is
missing remains unavailable; do not infer a pass from a neighboring app's consent.
Live mutations remain separately gated by explicit operator opt-in and approved
targets. The
`LiveMicrosoftTenantAcceptanceTests` test reports `BLOCKED_EXTERNAL` per capability
when live credentials are absent. Local development and CI use the
`SimulatedMicrosoftTenant` adapter, which startup refuses outside Development/Test
or alongside live credentials.

## Hostinger acceptance tenant observation (2026-10-10)

The Entra portal showed tenant consent for `User.Read.All` on the dedicated Directory Reader app
and `Mail.Send` on the dedicated Mail app. A dedicated Guest Invitation app was registered with
`User.Invite.All` as its only application role, plus delegated `User.Read` for sign-in; the portal
confirmed tenant admin consent. These are permission-registration facts only. AuditSphere's
tenant-connection page still showed Consent Required, no recorded attempt and directory
verification `NOT_VERIFIED`. The Directory Reader and Mail capabilities were Disabled, and no
M365 capability was runtime-verified in this observation.

The tenant-connection page displayed `BLOCKED_EXTERNAL: configure the separately approved consent
identity and fixed callback` and did not offer the Connect action. The consent handshake therefore
has not started. The acceptance deployment's current private environment was not re-inspected, so
the missing or mismatched server setting/certificate is not identified; inspect it on the VPS
without printing secret values. The deployment guide documents the required consent identity,
certificate and fixed callbacks.

The separate Group Membership app is not yet registered because the final Register action carries
Microsoft Platform Policies assent and is handed off to the user. The former Administration app
still has all three mutation application roles (`User.Create`, `User.Invite.All`,
`GroupMember.ReadWrite.All`) and remains noncompliant with the one-role-per-app boundary. Keep it
disabled; after the new Group Membership app is registered and its single role and consent are
verified, remove `User.Invite.All` and `GroupMember.ReadWrite.All` from the former app, leaving
`User.Create` only.

The existing SharePoint `AuditSphere Development` site and its empty `Internal Workpapers` library
were identified as the synthetic selected-resource candidate; `AuditSphere P0 Unrelated` is the
negative-control site. The selected-resource draft was saved with the App mediated profile and is
`VALIDATING`; its latest selected-site result is `BLOCKED_EXTERNAL` (`selected-resource-draft-edited`).
The selected-site permission `Sites.Selected` was consented on its app, but no site-level grant was
observed and the boundary check has not run. No site was created. Mail also remains disabled
pending sender-mailbox restriction, an approved synthetic recipient, and server-side
certificate/configuration.

The Hostinger acceptance deployment guide records the live server-side setup state and remaining
operator steps. Portal consent must never be described as an AuditSphere `CONSENT_VERIFIED` state;
only the nonce-bound connection flow and fresh persisted capability checks establish those states.

For the live Hostinger acceptance deployment, the dated read-only status, certificate mount,
callback, capability setup and verification procedure are recorded in the
[Hostinger acceptance deployment guide](../../infra/hostinger-acceptance/auditsphere-deployment-hostinger-acceptance-current.md#microsoft-365-capability-setup).
The consent screen and this permission matrix do not substitute for a fresh persisted capability
verification, exact selected-site grant, Exchange sender restriction, or live external acceptance.

Sources: [Microsoft admin consent protocol](https://learn.microsoft.com/en-us/entra/identity-platform/v2-admin-consent),
[tenant admin-consent role prerequisites](https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/grant-admin-consent),
[Microsoft consent types and automatically included scopes](https://learn.microsoft.com/en-us/entra/identity-platform/consent-types-developer),
[list users](https://learn.microsoft.com/en-us/graph/api/user-list?view=graph-rest-1.0),
[advanced directory filters](https://learn.microsoft.com/en-us/graph/aad-advanced-queries),
[create user](https://learn.microsoft.com/en-us/graph/api/user-post-users?view=graph-rest-1.0),
[create invitation](https://learn.microsoft.com/en-us/graph/api/invitation-post?view=graph-rest-1.0),
and [add group members](https://learn.microsoft.com/en-us/graph/api/group-post-members?view=graph-rest-1.0).
