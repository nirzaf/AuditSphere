# Client SharePoint sites and assigned staff access

Status: owner-approved scope change; live execution requires separate consent and configuration.

The owner explicitly approved Full Control over each entire client site for every assigned staff member, including engagement-only staff. Local AuditSphere engagement/client scopes do not change. SharePoint access includes sibling engagements, sharing, deletion and permission management. This is recorded as separate external authority, not an Entra administrator role or a source of application authorization.

## Supported provider and permission decision

Use the supported SharePoint `SPSiteManager` API to create a non-group-connected team site (`STS#3`). A deterministic client-name slug plus the full immutable client ID avoids duplicate-name collisions. Sites are never adopted based on their URL alone: the site description must contain the exact firm/client ownership marker. A result-uncertain create is read/reconciled before any further creation; it is never blindly retried.

| Capability | Endpoint | Permission / resource | Type | Why required | Admin consent / authority | Configured / verified |
|---|---|---|---|---|---|---|
| CLIENT_SITE_CREATION | SharePoint `/_api/SPSiteManager/create`, `/status`, exact site `/_api/web` | `Sites.FullControl.All` / SharePoint | Application, isolated client-sites worker | Supported app-only site creation and site-local staff group/role management | Required; Privileged Role Administrator or Global Administrator consents, SharePoint Administrator approves site policy | Disabled by default; actual remote readback required |
| CLIENT_SITE_WORKER_GRANT | Graph v1.0 `/sites/{id}/permissions`, `/sites/{id}/drive` | `Sites.FullControl.All` / Microsoft Graph | Application, same isolated provisioner | Grant a different document-worker app `write` on the exact new site; site-level application grants cannot be managed using Sites.Selected alone | Required; same consent authority and SharePoint site policy | Disabled by default; read back worker grant |
| CLIENT_SITE_STAFF_IDENTITY | Graph v1.0 `/users/{oid}` | `User.Read.All` / Microsoft Graph | Application, existing separate directory reader | Verify immutable object ID, enabled state, member account and current UPN before SharePoint ensureuser | Required; existing reader consent | Exact immutable identity checked on every synchronization |
| CLIENT_SITE_DOCUMENTS | Exact Graph drive/folder endpoints | `Sites.Selected` plus exact-site `write` | Application, existing document worker | Documents only in explicitly approved client sites | Required plus per-site grant by provisioner | Existing disposable upload/readback verification remains mandatory |

The privileged certificate is mounted only in the isolated Acceptance `client-sites` worker, never the Web or `pbc` worker. It holds exactly one role per resource, and must not reuse the login, directory, tenant-administration, mail or selected-site app identities. Configuration alone is not live acceptance. Missing credentials, consent or owner policy leave the capability BLOCKED_EXTERNAL. A narrower Sites.Create.All Graph creation API is currently beta and is not a supported production alternative; Graph directory-write and Entra role-management permissions remain prohibited.

## Lifecycle and revocation

Durable requests persist the exact tenant, client, deterministic URL, owner marker, approved administrator, reason and operation ID before dispatch. Remote receipts contain IDs and request correlations only. Independent exact-site verification precedes local binding. Full Control is provided via an AuditSphere-managed SharePoint group; an enabled staff assignment with a current local grant is required. Current Graph identity verification precedes additions. Reconciliation removes stale managed memberships, preserves a separate configured site custodian, and reads back the actual membership. Directory outages prevent additions and leave a failed/uncertain outcome. Microsoft permission revocation is asynchronous and cannot be described as immediate circuit/session revocation. Reconciliation removes access through the managed group only. Because Full Control permits sharing and permission management, separately created direct grants or sharing links require Microsoft administrator review; removing a managed membership cannot certify that all external access has disappeared.

The working site is mutable by those staff. AuditSphere's local file freeze cannot prevent a SharePoint owner from editing/deleting directly. Immutable issued evidence must remain in the existing independently controlled archive/release store. External archive protection is BLOCKED_EXTERNAL until separately verified; a folder named Final Signed Archive does not prove retention or immutability.

References: [supported site creation](https://learn.microsoft.com/en-us/sharepoint/dev/apis/site-creation-rest), [admin API authentication](https://learn.microsoft.com/en-us/sharepoint/dev/sp-add-ins/sharepoint-admin-apis-authentication-and-authorization), [selected permissions management](https://learn.microsoft.com/en-us/graph/permissions-selected-overview), [beta alternative](https://learn.microsoft.com/en-us/graph/api/site-post-sites?view=graph-rest-beta).

## Deployment and rollout contract

Apply the new EF migrations using the normal approved migration procedure. Start the isolated worker with `DOTNET_ENVIRONMENT=Acceptance`, `Worker:Group=client-sites`, the approved `Worker:FirmId` / `DeploymentEpoch`, `ExternalEffects:Enabled=true`, `AllowSimulationAdapters=false`, and `ClientSites:Enabled=true`. Required private configuration:

- `ClientSites:TenantId`, `ClientId`, `SiteHost` (tenant root hostname, without scheme/path), `CertificatePath`, `PrivateKeyPath`.
- `ClientSites:CustodianObjectId`: verified enabled Microsoft member who remains the separately controlled site custodian.
- `ClientSites:AdministratorUserId`: current firm-wide AuditSphere administrator approving the standing policy.
- `ClientSites:ClientsCreatedAfter`: explicit fixed UTC rollout timestamp. The worker persists it in the firm workspace configuration before creating any new-client intent; changing it silently is refused. Existing clients before that boundary retain their repositories and need a separately reviewed migration if sites are later requested.
- `Identity:ClientId` (separation check), `SelectedSite:ClientId` (the different document worker receiving exact-site write), `DirectoryReader:ClientId`, `CertificatePath`, `PrivateKeyPath` (the existing isolated User.Read.All reader). Supply any configured tenant-administration/mail app IDs too so startup checks their separation.

Run the client-sites worker to persist the policy before accepting new clients or starting their workspace worker. After the rollout, a newer client with no ready dedicated site stays blocked; neither automatic nor manual folder provisioning may fall back to the shared site. Keep the `pbc` worker on its existing certificate and credential reference. Verify the client site's READY state, separate staff membership state, document-worker exact write grant, and existing disposable upload/readback evidence before testing a real upload. The console or an Operations row alone is not proof of Microsoft acceptance.

Unknown or blocked operations are reviewed in Administration → Operations. Re-arming never clears the persisted creation-dispatch fence. If Microsoft cannot find a site after an uncertain create, it stays blocked for human investigation; do not cancel and issue another create to bypass reconciliation. The site URL/ownership marker and immutable Microsoft site ID must be checked against the approved client before any manual remediation.
