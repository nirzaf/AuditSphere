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
- Web sign-in, the directory reader/consent app, the selected-site document worker
  and each optional mutation capability use **separate app identities**. A
  capability token is accepted only when it is for the exact tenant and carries
  **exactly one** application role — its documented permission
  (`GraphCapabilityTokenSource`). A credential with extra roles is refused, so the
  document worker can never gain directory permissions and vice versa. Startup
  refuses an enabled capability without a complete separate certificate, or one
  that reuses the sign-in or selected-site app ID.
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
| `DIRECTORY_READ` | `GET /users`, `GET /users/{id}` | `User.Read.All` | Application, directory-reader/consent app | Bounded search of existing users; exact `accountEnabled`/`userType` checks; UPN/guest reconciliation reads | Required | Privileged Role Administrator or Global Administrator | Off (`DirectoryReader:Enabled`) |
| `SELECTED_SITE` | Exact `/sites/{id}`, `/drives/{id}` | `Sites.Selected` + per-site `write` grant | Application, document-worker app | Read/write only the approved working site | Required, plus a separate exact-site grant | Privileged Role Administrator; SharePoint Administrator for the site grant | Existing (`SelectedSite:*`) |
| `OUTBOUND_MAIL` | `POST /users/{sender}/sendMail` | `Mail.Send` | Application, mail app restricted by an Exchange application access policy | Send AuditSphere notifications from one sender only; no mailbox reading | Required | Privileged Role Administrator; Exchange Administrator for the access policy | Off (`TenantAdministration:OutboundMail`) |
| `TENANT_USER_PROVISIONING` | `POST /users`; reconciliation via reader `GET /users?$filter=userPrincipalName eq` | `User.Create` | Application, separate provisioner app | Create a workforce user only when explicitly enabled | Required | Privileged Role Administrator or Global Administrator | Off (`TenantAdministration:Provisioning`) |
| `GUEST_INVITATION` | `POST /invitations`; reconciliation via reader | `User.Invite.All` | Application, separate inviter app | Invite an explicitly approved external client identity; never automatic | Required; tenant B2B policy must allow app-only invitations | Privileged Role Administrator or Global Administrator | Off (`TenantAdministration:GuestInvitation`) |
| `GROUP_MEMBERSHIP` | `GET /groups/{id}`, `GET /groups/{id}/members`, `POST /groups/{id}/members/$ref`, `DELETE /groups/{id}/members/{id}/$ref` | `GroupMember.ReadWrite.All` | Application, separate group-manager app | Add/remove users only in allowlisted AuditSphere-managed groups | Required | Privileged Role Administrator or Global Administrator | Off (`TenantAdministration:GroupMembership`) |

**Least privilege.** `User.Create` is Microsoft's least-privileged permission for
`POST /users` (not `User.ReadWrite.All`); `User.Invite.All` for `POST /invitations`;
`GroupMember.ReadWrite.All` for user membership changes (not `Group.ReadWrite.All`);
`User.Read.All` is required because `User.ReadBasic.All` does not return
`accountEnabled`/`userType`. Role-assignable groups need
`RoleManagement.ReadWrite.Directory`, so AuditSphere refuses them (and treats an
unknown `isAssignableToRole` as privileged). Dynamic-membership groups are refused.

No AuditSphere runtime may request `Directory.ReadWrite.All`, `User.ReadWrite.All`,
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

## Remaining external gates

Live consent, live capability verification and live mutations are
`BLOCKED_EXTERNAL` until an operator configures each separate app registration and
certificate and a tenant administrator consents. The
`LiveMicrosoftTenantAcceptanceTests` test reports `BLOCKED_EXTERNAL` per capability
when live credentials are absent. Local development and CI use the
`SimulatedMicrosoftTenant` adapter, which startup refuses outside Development/Test
or alongside live credentials.

Sources: [Microsoft admin consent protocol](https://learn.microsoft.com/en-us/entra/identity-platform/v2-admin-consent),
[tenant admin-consent role prerequisites](https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/grant-admin-consent),
[list users](https://learn.microsoft.com/en-us/graph/api/user-list?view=graph-rest-1.0),
[create user](https://learn.microsoft.com/en-us/graph/api/user-post-users?view=graph-rest-1.0),
[create invitation](https://learn.microsoft.com/en-us/graph/api/invitation-post?view=graph-rest-1.0),
and [add group members](https://learn.microsoft.com/en-us/graph/api/group-post-members?view=graph-rest-1.0).
