# Microsoft 365 tenant administration permission decision

**Status: APPROVED for the core directory-reader boundary; PROPOSED for optional mutations.**
The repository owner's tenant-administration request changes the former blanket
prohibition on tenant-wide Graph scopes. This decision permits only the
read-only directory exception described below. Each optional mutation remains
disabled until its separate product/security approval, app configuration,
administrator consent, provider verification and acceptance tests are recorded.
This decision does not itself grant consent or turn on external effects.

## Security boundary

AuditSphere `RoleGrant` remains the sole source of application roles and scope.
No AuditSphere role maps to an Entra administrator role. Web sign-in, the
directory reader, selected-site document worker, and optional mutation
capabilities use separate app identities or credentials. The existing document
worker retains `Sites.Selected` with an explicit grant to the approved site;
directory consent does not widen its SharePoint reach. Tenant credentials and
tokens are kept in approved private stores, never ordinary database records.

The core directory reader may request application `User.Read.All` for bounded
`/users` discovery and exact-object status checks, including `accountEnabled`.
It is tenant-wide **read** access, so the runtime must select only needed
fields, page results, expose them only to a current firm-wide AuditSphere
Administrator, and never infer an AuditSphere grant from a directory result.
The prior approved-roster path remains available when the reader is absent.

## Permission matrix

`NOT_CONFIGURED` means no permission is configured for the named optional
capability. The directory reader has a separate Development registration and
observed live read, while production remains unverified.
For Microsoft Graph application permissions, tenant consent requires a
Privileged Role Administrator or a custom role authorized for the specific
consent; an AuditSphere Administrator alone is insufficient. The tenant's
consent policy and role assignment must be checked at connection time.

| Capability | Endpoint | Least permission | Type | Why needed | Consent / Microsoft authority | State |
| --- | --- | --- | --- | --- | --- | --- |
| Sign-in | Microsoft OIDC | `openid profile email` | Delegated sign-in | Authenticate one work/school identity and bind `tid` + `oid` | Normal OIDC policy; no Graph app role | Existing, separately verified |
| Directory reader | `GET /users`, `GET /users/{id}` | `User.Read.All` | Application, separate reader identity | Browse/search active users, filter by UPN domain, and re-read an exact enabled object before local binding | Tenant admin consent; Privileged Role Administrator or authorized custom role | Development-only app consented; bounded Graph read and persisted recent directory check observed; no production acceptance |
| Selected SharePoint | Exact approved `/sites/{id}` and `/drives/{id}` resources | `Sites.Selected` plus site-specific `write` grant | Application, document-worker identity | Read/write only the selected working site | Tenant admin consent plus separate exact-site grant | Existing Development selected-site read and disposable write checks; no PBC acceptance |
| Tenant user creation | `POST /users` | `User.Create` | Application, optional separate provisioner | Create a new workforce user only when explicitly enabled | Separate tenant admin consent and provisioning policy | PROPOSED; NOT_CONFIGURED |
| Guest invitation | `POST /invitations` | `User.Invite.All` | Application, optional separate inviter | Invite an explicitly approved external client identity | Separate tenant admin consent and tenant B2B policy | PROPOSED; NOT_CONFIGURED |
| Managed group members | `GET /groups/{id}/members`, `POST /groups/{id}/members/$ref`, `DELETE /groups/{id}/members/{id}/$ref` | `GroupMember.ReadBasic.All` for bounded member display; `GroupMember.ReadWrite.All` for user membership changes | Application, optional separate group manager | Administer only allowlisted AuditSphere-managed groups | Separate tenant admin consent; role-assignable groups excluded | PROPOSED; NOT_CONFIGURED |
| Outbound mail | Existing approved sender path | Existing mail-provider scope only | Separate existing delivery provider | Send AuditSphere notifications | Existing provider approval | No new permission |

No normal runtime may request `Directory.ReadWrite.All`,
`User.ReadWrite.All`, `RoleManagement.ReadWrite.Directory`,
`Sites.ReadWrite.All`, `Files.ReadWrite.All`, or `Sites.FullControl.All`.
Entra administrator-role assignment is outside this administration feature.
Optional user, guest and group mutations require an operation-specific
idempotency/reconciliation design before their permissions are enabled.

## Consent and evidence contract

The admin-consent callback supplies `state`, tenant and an outcome; it does
**not** authenticate the consenting person's `oid`. A tenant connection flow
must bind the initiating AuditSphere administrator and a separately
authenticated work/school tenant-admin identity to a short-lived setup
session, validate state/nonce and exact tenant, then verify each granted
capability with its own credential and endpoint. Callback flags alone are not
proof of consent or proof of the consenting actor. Record the observed
identity and capability evidence with honest provenance; do not label an
inferred actor as the verified grantor. A saved draft, token role, selected-site
grant, working endpoint and application activation are separate states.

The current consent slice binds state to the initiating AuditSphere session
and records only `RETURNED_UNVERIFIED`. It does not yet authenticate the
consenting Microsoft administrator, acquire a directory-reader credential,
or verify Graph permissions; those are required before `VERIFIED` status.
The local directory reader is separately disabled by default. When explicitly
configured with its own certificate, it enforces an exact-tenant app token
whose sole application role is `User.Read.All`, then reads up to 25 selected
fields per page. A successful search is an ephemeral provider observation,
not persisted capability evidence or an AuditSphere role grant.
The Development tenant now has a separate reader registration with one
certificate, an exact localhost consent callback and only the approved Graph
application role. A direct certificate token and the AuditSphere browser page
both completed bounded reads. A separate browser consent attempt was cancelled
and recorded `DENIED`; this did not revoke the previously observed app-role
grant. No verified successful consent callback is claimed. A subsequent
administrator-triggered exact-user Graph read in Development recorded a
`DIRECTORY` provider check against the current connection revision. Its
recent-check display expires after 15 minutes; it does not prove the grantor,
the consent callback, SharePoint, mail, or connection activation.
Selecting a directory member requires a fresh `GET /users/{id}` read before
creating or refreshing an exact local `(tenant ID, object ID)` binding. A
disabled, guest, changed, or mismatched object is refused. Binding records a
Graph observation but creates no `RoleGrant`; the administrator must separately
choose a local role and scope. The general roster endpoint cannot label
unverified input as `GRAPH`.
The User and role administration picker pages enabled users in batches of 25.
Its optional UPN domain suffix filter uses Graph's advanced query requirements
(`ConsistencyLevel: eventual`, `$count=true`) and rejects results from another
tenant or domain. A selected workforce member is re-read by exact object ID;
the picker itself never grants access. Guest entries remain visible but cannot
be bound through the workforce-member action.

Before optional capabilities ship, extend this matrix with the approved
credential owner, retention, revocation steps, exact endpoint tests, and
failure/unknown-outcome handling. No broad scope is added to the current app
registration as a shortcut for a picker or a setup badge.

Sources: [Microsoft admin consent protocol](https://learn.microsoft.com/en-us/entra/identity-platform/v2-admin-consent),
[tenant admin-consent role prerequisites](https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/grant-admin-consent),
[list users](https://learn.microsoft.com/en-us/graph/api/user-list?view=graph-rest-1.0),
[advanced directory filters](https://learn.microsoft.com/en-us/graph/aad-advanced-queries),
[create user](https://learn.microsoft.com/en-us/graph/api/user-post-users?view=graph-rest-1.0),
[create invitation](https://learn.microsoft.com/en-us/graph/api/invitation-post?view=graph-rest-1.0),
and [add group members](https://learn.microsoft.com/en-us/graph/api/group-post-members?view=graph-rest-1.0).
