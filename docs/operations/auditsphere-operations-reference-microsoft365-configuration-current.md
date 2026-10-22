# Microsoft 365 deployment configuration reference

**Status: CURRENT source/configuration reference.** Use with the [client-tenant CLI runbook](auditsphere-operations-runbook-client-tenant-microsoft365-current.md). Values are examples or key names, not live tenant settings. The [Wiki operator guide](https://github.com/nirzaf/AuditSphere/wiki/Production-Entra-ID-Configuration) is the publication entry point.

## AI-agent reading packet

| Question | Owning source |
| --- | --- |
| Approved permissions and separate identities | [Permission matrix](../architecture/auditsphere-m365-tenant-administration-permissions.md) |
| Site provisioning, rollout and staff Full Control | [Client-site contract](../architecture/auditsphere-client-sharepoint-sites-current.md) · [ADR-0006](../architecture/adr/auditsphere-architecture-adr-0006-client-sharepoint-site-staff-full-control-approved.md) |
| Application/worker boundaries and remaining acceptance | [Architecture](../architecture/auditsphere-architecture-current-architecture.md) · [execution ledger](../execution/status.json) |
| OIDC settings and immutable tenant/object binding | [API authentication](../../src/AuditSphereOps.Api/ApiHost.Authentication.cs) |
| Initial administrator proof and CSRF flow | [Installation API](../../src/AuditSphereOps.Api/ApiHost.Installation.cs) · [installation service](../../src/AuditSphereOps.Application/Microsoft365/Microsoft365InstallationService.cs) |
| Capability settings, identity separation, freshness | [API composition](../../src/AuditSphereOps.Api/Authentication/TenantAdministrationComposition.cs) |
| Certificate and token single-role checks | [Graph credential source](../../src/AuditSphereOps.Infrastructure/Providers/GraphCapabilityCredential.cs) |
| Trusted site/library/root and control-denial evidence | [Resource verification](../../src/AuditSphereOps.Application/Microsoft365/SelectedSiteBoundaryVerificationService.cs) · [Graph probe](../../src/AuditSphereOps.Infrastructure/Providers/GraphSelectedResourceProbe.cs) |
| Site identity and uncertain-create reconciliation | [Site provider](../../src/AuditSphereOps.Infrastructure/Providers/SharePointClientSiteProvider.cs) · [Application contracts](../../src/AuditSphereOps.Application/Documents/ClientSharePointSites.cs) |
| Worker groups, environment and provider selection | [Worker startup](../../src/AuditSphereOps.Worker/Program.cs) |
| Mail transport and attachment differences | [Graph mail](../../src/AuditSphereOps.Infrastructure/Providers/GraphPbcMailSender.cs) · [SMTP/Resend](../../src/AuditSphereOps.Infrastructure/Providers/AlternativePbcMailSenders.cs) |
| Concrete Acceptance wiring | [Compose](../../infra/hostinger-acceptance/compose.yaml) · [complete variable template](../../infra/hostinger-acceptance/.env.example) · [credential-isolation example](../../infra/hostinger-acceptance/m365-isolation.override.example.yaml) |

Inspect these sources before changing configuration. Do not infer production support from an environment variable, a historical live observation or an implemented provider class.

## Key notation and identities

The .NET key `DirectoryReader:ClientId` becomes environment key `DirectoryReader__ClientId`. Hostinger Compose exposes a different operator variable, `DIRECTORY_READER_CLIENT_ID`, then maps it into the container environment. The variable must exist in the final Compose model to affect the process.

`ClientId` means an Entra application **client ID**. Entra application-object ID and enterprise-application/service-principal object ID are distinct. `CustodianObjectId` and `INITIAL_ADMIN_OBJECT_ID` mean Microsoft user object IDs. `ClientSites:AdministratorUserId` means an existing **local AuditSphere user ID**.

## Base Compose input inventory

These are all inputs interpolated by the checked-in base Hostinger Compose. A private `.env` derived from the example supplies them. Optional empty/default values do not verify a capability.

| Operator variable | Container/application key or purpose | Value/behavior |
| --- | --- | --- |
| `RELEASE_TAG` | API, worker and migration image tag | Approved full source SHA |
| `DEPLOYMENT_EPOCH` | `Worker:DeploymentEpoch` | Positive rollout fence; coordinate with release procedure |
| `POSTGRES_PASSWORD` | Database password / connection string | Secret; no terminal/log output |
| `FIRM_ID` | `Application:FirmId`, `Setup:FirmId`, `Worker:FirmId` | Firm GUID shared by intended installation processes |
| `INSTALLATION_ID` | `Application:InstallationId`, `Setup:InstallationId` | Unique installation identifier; Production Data Protection requires a non-empty GUID |
| `BOOTSTRAP_PROOF_HASH` | `Setup:BootstrapProofHash` | SHA-256 hex of privately supplied random one-use proof |
| `IDENTITY_TENANT_ID` | `Identity:TenantId`, setup-approved tenant, Graph/SelectedSite/ClientSites tenant | Fixed firm's Entra tenant GUID |
| `IDENTITY_CLIENT_ID` | `Identity:ClientId` | Separate login client ID |
| `IDENTITY_CLIENT_SECRET` | `Identity:ClientSecret` | Login confidential secret; current OIDC code does not use a certificate instead |
| `INITIAL_ADMIN_OBJECT_ID` | `Setup:InitialAdministratorObjectId` | Exact approved member user object ID |
| `TENANT_CONSENT_ENABLED` | `TenantConsent:Enabled` | Defaults false |
| `DIRECTORY_READER_ENABLED` | `DirectoryReader:Enabled` | Defaults false |
| `DIRECTORY_READER_CLIENT_ID` | `DirectoryReader:ClientId`, `TenantConsent:ClientId` | Same separate reader/consent identity; used by client-sites reader too |
| `SELECTED_SITE_CLIENT_ID` | `SelectedSite:ClientId` | Separate document app, exactly Graph `Sites.Selected` |
| `SELECTED_SITE_CREDENTIAL_REFERENCE` | `SelectedSite:CredentialReference` | Must exactly match the saved connection revision; base default is `hostinger-acceptance-selected-site-certificate` |
| `SELECTED_SITE_NEGATIVE_CONTROL_URL` | `SelectedSite:NegativeControlSiteUrl` | Valid unrelated same-tenant site that denies this runtime app |
| `PBC_TRANSFER_LIVE_PROVIDER` | API `PbcTransfer:LiveProvider` | Defaults false; isolated PBC worker activation remains separate |
| `TENANT_ADMINISTRATION_PROVISIONING_ENABLED` | `TenantAdministration:Provisioning:Enabled` | Defaults false |
| `TENANT_ADMINISTRATION_PROVISIONING_CLIENT_ID` | `TenantAdministration:Provisioning:ClientId` | Exactly Graph `User.Create` |
| `TENANT_ADMINISTRATION_GUEST_INVITATION_ENABLED` | `TenantAdministration:GuestInvitation:Enabled` | Defaults false |
| `TENANT_ADMINISTRATION_GUEST_INVITATION_CLIENT_ID` | `TenantAdministration:GuestInvitation:ClientId` | Exactly Graph `User.Invite.All` |
| `TENANT_ADMINISTRATION_GUEST_REDIRECT_URL` | `TenantAdministration:GuestRedirectUrl` | Set the customer HTTPS landing URL; base default points at the acceptance hostname |
| `TENANT_ADMINISTRATION_GROUP_MEMBERSHIP_ENABLED` | `TenantAdministration:GroupMembership:Enabled` | Defaults false |
| `TENANT_ADMINISTRATION_GROUP_MEMBERSHIP_CLIENT_ID` | `TenantAdministration:GroupMembership:ClientId` | Exactly Graph `GroupMember.ReadWrite.All` |
| `OUTBOUND_MAIL_ENABLED` | API `TenantAdministration:OutboundMail:Enabled` | Defaults false; does not control whether a live mail worker dispatches |
| `OUTBOUND_MAIL_CLIENT_ID` | API `TenantAdministration:OutboundMail:ClientId`, worker `GraphMail:ClientId` | Separate mail identity; read runbook's AAP/RBAC compatibility boundary |
| `OUTBOUND_MAIL_SENDER_MAILBOX` | API sender / `GraphMail:SenderMailbox` | Exact approved functioning Exchange Online mailbox |
| `CLIENT_SITES_ENABLED` | `ClientSites:Enabled` in API/PBC/provisioner | Defaults true; false stops new automatic intents, not saved rollout protection |
| `CLIENT_SITES_CLIENTS_CREATED_AFTER` | `ClientSites:ClientsCreatedAfter` in API/PBC/provisioner | Explicit fixed UTC cutoff, no default; absent means blocked rollout |
| `CLIENT_SITES_CLIENT_ID` | `ClientSites:ClientId` | Separate provisioner with approved Graph and SharePoint role exception |
| `CLIENT_SITES_SITE_HOST` | `ClientSites:SiteHost` | Tenant root hostname, e.g. `customer.sharepoint.com`; no scheme/path |
| `CLIENT_SITES_CUSTODIAN_OBJECT_ID` | `ClientSites:CustodianObjectId` | Enabled same-tenant Microsoft member object ID |
| `CLIENT_SITES_ADMINISTRATOR_USER_ID` | `ClientSites:AdministratorUserId` | Current local firm-wide AuditSphere Administrator |

## Required non-variable settings and release overrides

These keys are fixed or separately composed; adding an arbitrary similarly named `.env` entry will not override them.

| Setting | Current source behavior / deployment action |
| --- | --- |
| `Application:PublicBaseUrl` | Fixed acceptance hostname in base Compose; change through reviewed customer composition |
| `Application:TimeZone` | Base Compose uses `Asia/Qatar`; choose approved business timezone |
| `Identity:CallbackPath` | `/signin-oidc`; Entra Web URI must match the resulting public URL |
| `TenantConsent:RedirectUri` | Exact public `/auth/m365-consent/callback` URI; fixed acceptance URL in base Compose |
| `TenantConsent:IdentityRedirectUri` | Exact public `/auth/m365-consent/identity-callback` URI; same reader app |
| `TenantAdministration:VerificationMaxAgeHours` | API defaults 24, bounded 1–168; change only with reviewed policy |
| `DevelopmentIdentity:Enabled`, `Application:AllowSimulationAdapters`, worker `AllowSimulationAdapters` | False in deployed environments |
| API `ExternalEffects:Enabled` | False; true refuses API startup. Per-capability API mutations have separate enablement/verification |
| Worker `ExternalEffects:Enabled`, `Worker:Group`, `Worker:ApprovedLiveGroups` | Live `mail`, `pbc` and `client-sites` run in their own isolated worker. Acceptance allows them as before. Production composes one only when that group is listed in the comma-separated `Worker:ApprovedLiveGroups` and simulation adapters are off; otherwise startup fails with named blockers. Core groups (`general`, `processing`, `records`) never carry external effects. Listing a group does not prove tenant readiness; provider credential checks still apply. |
| `FeatureActivation:LiveAuditRelease` | Keep false until its own production/provider/professional exits pass |
| Proxy trusted addresses / TLS / cookies | Update actual forwarded-header trust and network together; preserve HTTPS and same-origin auth |
| `Storage:PbcStagingRoot`, `Storage:ReleaseCheckpointRoot` | Persistent approved mounts, not transient container paths |
| Site URL / site ID / drive ID / root ID / access profile / template | Stored through reviewed Application setup/resource workflow, not simple environment substitutions |
| Managed-group IDs | Reviewed local allowlist through Users & Access/group workflows, not a tenant-wide wildcard |
| `ReportSigning:CertificatePath`, `PrivateKeyPath`, `Required` | Independent firm PDF-signing credential/legal acceptance; not an Entra service certificate or part of Graph consent |

## Credential mount inventory

Each capability directory contains `certificate.pem` and `private-key.pem`. The fixed container prefix is `/run/auditsphere/m365/`; the concrete Acceptance host prefix is `/opt/auditsphere/storage/m365-credentials/`. Adapt the host prefix in a reviewed customer composition.

| Suffix | API option prefix | Other legitimate consumers |
| --- | --- | --- |
| `directory-reader` | `DirectoryReader:*` | Client-sites worker's separate directory reader; consent redemption uses this app/certificate |
| `selected-site` | `SelectedSite:*` | Isolated PBC/document worker |
| `provisioning` | `TenantAdministration:Provisioning:*` | API configured capability only |
| `guest-invitation` | `TenantAdministration:GuestInvitation:*` | API configured capability only |
| `group-membership` | `TenantAdministration:GroupMembership:*` | API configured capability only |
| `outbound-mail` | `TenantAdministration:OutboundMail:*` | Isolated mail worker uses `GraphMail:*` |
| `client-sites` | No API credential prefix | Isolated client-sites worker only: `ClientSites:CertificatePath`, `PrivateKeyPath` |

**Credential isolation:** current base Compose mounts six individual read-only API capability directories and excludes their parent and the privileged client-sites key. `create_host_path: false` requires existing host directories; prepare protected empty directories for disabled capabilities. Existing containers retain earlier mounts until reviewed recreation and process-level inspection. Never stage the privileged key while an old API still sees the parent. The [isolation override example](../../infra/hostinger-acceptance/m365-isolation.override.example.yaml) supplies the same layout for older base revisions, using Compose `!override` (2.24.4+) to preserve the three data mounts. Updating a file does not change a running deployment. [Docker merge semantics](https://docs.docker.com/reference/compose-file/merge/#replace-value).

All mounts must be read-only and readable by the release image's actual process UID/GID. Check the deployed model and actual container visibility. Do not display key contents. Mail/PBC workers must not see sibling capability credentials, and no API/mail/PBC process may see the client-sites provisioner key.

## Production session encryption, telemetry and release settings

These are additional deployment prerequisites, not Entra/Graph permissions. The current Hostinger Acceptance template does not populate all of them. [Production Data Protection](../../src/AuditSphereOps.Api/Authentication/ProductionDataProtection.cs) refuses incomplete Production configuration.

| Key | Required handling |
| --- | --- |
| `ConnectionStrings:AuditSphere` | Intended database, TLS and separately held database credential; migration executor separate from app startup |
| `Application:InstallationId` | Stable non-empty GUID in Production; changing it changes the session-key namespace |
| `DataProtection:KeyDirectory` | Existing absolute persistent key-ring directory readable/writable by approved API instances; retain it across image upgrades |
| `DataProtection:CertificatePath` | Existing absolute path to a dedicated password-protected PKCS#12 file with private key and current validity |
| `DataProtection:CertificatePassword` | Private secret-store value; required by current loader, not a path/reference string passed as the password |
| `DataProtection:PreviousCertificates:<n>:Path`, `Password` | Complete old PKCS#12/password pairs for decrypting still-required existing keys during rotation/recovery |
| `Telemetry:Otlp:Endpoint` | Optional absolute HTTP(S) collector endpoint with no embedded credentials, query or fragment; configure approved telemetry custody separately |
| `FeatureActivation:LiveAccountingRelease`, `LiveAuditRelease`, `LiveFirmLedgerPosting` | Default false; enable only under their own accepted release/professional gates |
| `ReleaseSafety:RequireSignatureLineage`, `RequireProtectionAttestation` | Default false; existing evidence requirements/approved release policy govern activation |
| `ReleaseSafety:RequireExternalCheckpointBeforeDelivery` | Default true; retain the external-checkpoint gate |
| `ExternalEffects:DeploymentEpoch` / `Worker:DeploymentEpoch` | Follow the actual host's composition and approved recovery fence; the worker requires a positive epoch |

For a dedicated Data Protection credential, convert approved certificate/key files to PKCS#12 with `openssl pkcs12 -export -out '<protected-pfx-path>' -inkey '<dedicated-DP-private-key>' -in '<dedicated-DP-certificate>'`; supply its export password only at the private interactive prompt/secret-store channel. Do not reuse a Graph service key or PDF signing certificate. Mount the PFX read-only and retain the separate persistent key ring. The code intentionally retains its existing `AuditSphereOps.Web:Production:<tenant>:<installation>` key namespace after UI retirement; renaming that compatibility string can invalidate sessions and is not a deployment cleanup.

On rotation, configure the new current PFX and retain old decryption credentials in `PreviousCertificates`. Recreate the affected approved API instances and verify old sessions and newly protected data in a rehearsal. Remove old keys/certificates only when their retention/recovery policy permits it. A working OIDC callback does not prove key-ring backup or recovery.

Records profiles/retention and repository identities are persisted Application configuration and evidence. Do not invent `Records__ProfileId` or a Purview label setting and assume it has a startup consumer. The authoritative archive/release store, independently verified secondary SharePoint archive, report-signing/legal policy and professional decisions remain separate requirements.

## Email provider and reminder options

These settings exist in Worker code but are not all base-Compose variables. Select one provider through a reviewed override/secret store. SMTP/Resend do not produce Graph capability verification and currently omit attachments; they cannot replace Graph attachment workflows.

| Key | Expected value |
| --- | --- |
| `Mail:Provider` | `GRAPH`, `SMTP` or `RESEND`; base mail profile selects `GRAPH` |
| `GraphMail:TenantId`, `ClientId`, `SenderMailbox`, `CertificatePath`, `PrivateKeyPath` | Complete separate Graph sender identity and credential |
| `SmtpMail:Host`, `Port`, `UseSsl` | Approved relay host/port; STARTTLS true for secure relay; not implicit TLS 465 |
| `SmtpMail:SenderAddress`, `SenderName` | Approved relay sender and optional display name |
| `SmtpMail:Username`, `Password` | Both set or both absent for explicitly approved relay auth; no XOAUTH2 support |
| `ResendMail:ApiKey`, `SenderAddress` | Private provider sending key and verified sender |
| `PbcReminders:Enabled` | False until reminder policy is approved; enabling can discover/queue new sends |
| `PbcReminders:DaysBeforeDue` | Worker default 3 |
| `PbcReminders:OverdueIntervalDays` | Worker default 3 |
| `PbcReminders:TimeZoneId` | Worker default `Asia/Qatar`; use approved tenant business timezone |
| `PbcReminders:QuietHoursStart`, `QuietHoursEnd` | Worker defaults `21:00`, `07:00` |
| `PbcReminders:BatchSize` | Worker default 50; preserve validated bounds |
| `PbcReminders:PublicBaseUrl` | Exact public HTTPS customer URL when live reminders are enabled |

Reminder policy is checked at delivery time by the isolated `Worker:Group=mail` process. A quiet-hours deferral is known to have no provider effect: it does not consume an execution attempt, keeps the current attempt token, and records the deferral in operation audit history. A provider-accepted send is not requeued solely because quiet hours begin afterward. These options are read at worker startup, so configuration changes require restarting the isolated mail worker. A disabled reminder policy blocks queued delivery and requires authorized recovery.

Exchange API-side permission verification, sender-only restriction, Graph transport acceptance, Exchange delivery, and recipient action are separate facts. No IMAP/POP inbox synchronization, mailbox-read permission or provider delivery webhook is implied by these configuration keys.

## Current Azure infrastructure limits

[Production](../../infra/environments/production/main.bicep) selects core worker groups only. [Acceptance](../../infra/environments/acceptance/main.bicep) lists external groups, but the [stack](../../infra/modules/stack.bicep) does not pass complete provider enablement/credentials to the [worker module](../../infra/modules/containerapp-worker.bicep). The API Bicep supplies login credentials but does not wire the complete M365 capability certificate settings/mounts. As currently composed, Azure mail/PBC workers lack their live prerequisites and fail their environment/composition guards; client-sites reports blocked configuration. Template compilation does not prove those services start or connect to Microsoft.

The current Azure API template also does not supply the required Production installation GUID and Data Protection key-ring/certificate settings listed above. A production deployment must implement that wiring and prove startup/session recovery before being accepted; compiling the Bicep or configuring Entra alone does not resolve it.

Treat full Azure M365 wiring and Production live worker support as engineering/release prerequisites. Do not copy a Hostinger Acceptance profile into a production tenant and claim the infrastructure gap is resolved.

## Verification and agent completion rules

- Template/Compose parsing and CLI syntax checks establish documentation consistency only.
- [The tenant helper](../../scripts/verify-tenant.sh) currently reports BLOCKED even when prerequisites are present; it is not a live acceptance runner.
- Use real configured tenant/capability/resource/denial observations and approved controlled business workflows before reporting VERIFIED.
- A new customer gets fresh verification and professional/operator acceptance. The historical Hostinger/easyguide observations authorize no customer action and prove no customer configuration.
- Update the execution record with exact inspected source, checks actually run and unexecuted customer actions. Never advance task, professional or external status merely because this guide is complete.
