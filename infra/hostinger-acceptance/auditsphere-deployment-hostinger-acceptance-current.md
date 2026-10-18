# Hostinger acceptance deployment

For a new customer tenant, start with the [CLI setup runbook](../../docs/operations/auditsphere-operations-runbook-client-tenant-microsoft365-current.md) and [configuration inventory](../../docs/operations/auditsphere-operations-reference-microsoft365-configuration-current.md). They explain Entra registrations/consent, certificates, exact SharePoint grants, email authorization/DNS, worker activation and rotation. This guide's dated observations are acceptance history, not new-customer verification. The base Compose API parent credential mount must be narrowed with the reviewed [isolation example](m365-isolation.override.example.yaml) before a privileged client-sites key is staged beneath that parent. Microsoft's current Exchange RBAC recommendation and AuditSphere's legacy mail-verifier compatibility limit are documented in the new runbook.

This is a separate, synthetic-data acceptance environment for the Hostinger VPS. It uses the
existing host Caddy instance and does not publish the API or PostgreSQL ports publicly. Its
Compose project, network, database, data-protection keys and upload staging are isolated from
the VPS's existing applications. Development identity and external effects for core workers are
off. The dedicated client-sites worker is enabled by default but remains blocked until its explicit
rollout boundary, separate credentials and Microsoft authorization are ready.

## Required owner configuration

Before startup, create an Entra confidential-client application for the acceptance tenant and
register `https://audit.steaudit.com/signin-oidc` as its web redirect URI. Keep its client secret
in a protected server-side `.env` file; never put it in this repository or chat. The first
administrator must be an existing user in the same tenant. Generate the bootstrap proof hash
using the application's approved setup procedure and place it in `.env` alongside the tenant,
application, administrator object, firm and installation IDs. Use fresh synthetic identifiers
and data only.

## Deployment outline

Run commands from `/opt/auditsphere/repo/infra/hostinger-acceptance` on the VPS. The deployment
operator must first install Git and Docker Compose if unavailable, create `/opt/auditsphere` with
owner-only access, and check out the approved source revision there. Do not run OS upgrades or
restart the VPS as part of this deployment.

The acceptance deployment has one 30 GB ext4 filesystem mounted at `/opt/auditsphere/storage`.
Its PostgreSQL, data-protection, PBC staging and release-checkpoint bind volumes all live there.
The filesystem is backed by `/opt/auditsphere/auditsphere-acceptance-storage.img`, so the data
area has a hard 30 GB capacity while retaining the VPS's existing root filesystem and Docker
data layout. The image file is preallocated to reserve the requested host capacity.

Create and mount the storage filesystem once, before starting Compose:

```bash
sudo install -d -m 0700 /opt/auditsphere/storage
sudo truncate -s 30G /opt/auditsphere/auditsphere-acceptance-storage.img
sudo chmod 0600 /opt/auditsphere/auditsphere-acceptance-storage.img
sudo mkfs.ext4 -F -m 0 /opt/auditsphere/auditsphere-acceptance-storage.img
sudo fallocate -l 30G /opt/auditsphere/auditsphere-acceptance-storage.img
sudo mount -o loop /opt/auditsphere/auditsphere-acceptance-storage.img /opt/auditsphere/storage
sudo install -d -o root -g root -m 0700 /opt/auditsphere/storage/postgres
sudo install -d -o 1654 -g 1654 -m 0700 /opt/auditsphere/storage/{data-protection,pbc-staging,release-checkpoints}
```

Persist the mount across restarts by adding this exact line to `/etc/fstab`, then verify it with
`findmnt /opt/auditsphere/storage` and `df -h /opt/auditsphere/storage`:

Run `fallocate` after `mkfs.ext4`; formatting the image can discard preallocated blocks.

```text
/opt/auditsphere/auditsphere-acceptance-storage.img /opt/auditsphere/storage ext4 loop,defaults,nofail 0 2
```

1. Create `.env` with `umask 077` and mode `0600`. Set `RELEASE_TAG` to the full source commit,
   a generated `POSTGRES_PASSWORD`, `FIRM_ID`, `INSTALLATION_ID`, `BOOTSTRAP_PROOF_HASH`,
   `IDENTITY_TENANT_ID`, `IDENTITY_CLIENT_ID`, `IDENTITY_CLIENT_SECRET`,
   `INITIAL_ADMIN_OBJECT_ID`, and a positive `DEPLOYMENT_EPOCH`. For dedicated new-client sites,
   set `CLIENT_SITES_ENABLED=true` (the default) and an explicitly approved UTC
   `CLIENT_SITES_CLIENTS_CREATED_AFTER` boundary. Do not backdate the boundary to include existing
   clients. Configure `CLIENT_SITES_CLIENT_ID`, `CLIENT_SITES_SITE_HOST`,
   `CLIENT_SITES_CUSTODIAN_OBJECT_ID`, and `CLIENT_SITES_ADMINISTRATOR_USER_ID`; place the
   dedicated provisioner certificate/key and separate directory-reader certificate/key under their
   respective protected host directories. Leave capability configuration incomplete until the
   exact Entra permissions and site policy are approved. Use synthetic values only.
2. Build the three `linux/amd64` images from the approved source commit using the GitHub Actions
   workflow `Hostinger acceptance images`. It publishes them to private GitHub Container Registry
   packages. On the VPS, authenticate Docker to `ghcr.io` with a GitHub token that has only
   `read:packages` access, then use the same full source SHA as `RELEASE_TAG`. Enter the token
   directly at the VPS terminal prompt; do not place it in `.env`, shell history, chat or the repo.
   Validate the Compose model with `docker compose config -q` and pull the API, worker and
   migration images with `docker compose --profile migration pull`.
3. Start only PostgreSQL, wait for its healthy status, then run the one-shot migration job:
   `docker compose up -d db`; `docker compose --profile migration run --rm migrate`.
   Application containers never apply schema changes at startup.
4. Start the API, three core workers and isolated client-sites worker with
   `docker compose up -d api worker-general worker-processing worker-records worker-client-sites`.
   The isolated worker starts with no shared credential mount; absent certificate files or rollout
   configuration produce a blocked heartbeat and no provisioning. The same explicit cutoff is passed
   to the API and optional `worker-pbc`, which refuse shared-site folder creation for an eligible client
   until its dedicated site is verified. Verify the worker state in the
   Administration client-sites panel. Verify the API logs and `http://127.0.0.1:18080/health/ready`
   locally on the VPS before changing DNS or Caddy.
5. Add the Caddy site block from `Caddyfile.audit.steaudit.com` to the existing host Caddyfile.
   Back up that file first, validate the complete config with `caddy validate`, then reload Caddy.
   Do not replace the existing Caddyfile or modify any existing site block.
6. Add a Cloudflare proxied `A` record `audit` → `72.62.252.165`, then verify HTTPS and the Entra
   sign-in flow in a browser. Ensure Cloudflare's origin mode is Full (strict) so the Caddy
   certificate is validated.
7. Bootstrap the initial firm administrator through the application's proof-backed setup flow.
   Confirm only synthetic acceptance records are present.

## Microsoft 365 capability setup

Microsoft 365 is configured capability by capability. OIDC sign-in, tenant consent, a Graph
application permission, a SharePoint site grant, library access, mail transport and AuditSphere
workspace activation are separate states. Never set the saved Mail or Records setup draft to
Configured as a substitute for external verification.

### Acceptance deployment observation (2026-10-10)

The deployed `Administration → Microsoft 365 → Tenant connection` page and Hostinger web terminal
were inspected read-only. Sign-in was Enabled and Verified. The tenant connection showed Consent
Required, no recorded attempt, and directory verification `NOT_VERIFIED`. The `Sites.Selected`
capability was Enabled but Not Verified; its site URL, site ID, drive/library ID and root folder ID
were blank. Directory reading, outbound mail, user provisioning, guest invitations and group
membership were Disabled. Mail and Records setup drafts were Not Configured. No client or
engagement workspace intents were present.

The current Entra registration page for the selected-site app showed tenant consent for
`Sites.Selected` and delegated `User.Read`. That consent is not an exact SharePoint site grant and
does not prove access to a library or root folder. The Directory Reader registration listed
`User.Read.All`, but the portal did not show a tenant grant for it. The Mail registration listed
`Mail.Send`, but the portal did not show a tenant grant for it. The Administration registration
showed all three application roles (`User.Create`, `User.Invite.All`,
`GroupMember.ReadWrite.All`) granted; this combined identity does not meet the current
one-role-per-app policy and must remain disabled until split. The server `.env` inspection found no
M365 capability key names, and a file-name-only search under `/opt/auditsphere` found no PEM, CRT
or key files. No secret values were displayed. Therefore the acceptance server had no usable Graph
capability credentials at this observation. No Graph capability or external effect was enabled
by this inspection.

### Portal and SharePoint follow-up (2026-10-10)

A later read-only Entra review showed tenant admin consent on the dedicated Directory Reader
`User.Read.All` application permission and the dedicated Mail `Mail.Send` application permission.
A dedicated Guest Invitation registration was created with `User.Invite.All` as its only
application role (plus delegated `User.Read` for sign-in), and Entra confirmed tenant-wide admin
consent. These portal grants do not enable the corresponding AuditSphere capabilities. The page
still showed the tenant connection as Consent Required with no recorded attempt and directory
verification `NOT_VERIFIED`. Directory reading, mail, user provisioning, guest invitations and
group membership were Disabled. The saved Mail and Records setup drafts remained Not Configured,
and no client workspace intents existed.

The tenant-connection page explicitly showed `BLOCKED_EXTERNAL: configure the separately approved
consent identity and fixed callback`; the Connect action was not available, so the nonce-bound
handshake has not begun. A read-only check in the Hostinger terminal confirmed that the deployment
`.env` exists, but no M365 configuration-key names or capability enable flags are present. A
file-count check under `/opt/auditsphere/storage/m365-credentials` found no PEM, CRT or key files.
No values were read, and no server setting or credential was changed. This identifies the immediate
blocker: the consent app ID/enablement and its certificate-backed credential are not configured on
the server. The fixed callback settings are present in the repository Compose definition; the live
handshake remains unstarted.

The group-membership registration is prepared in Entra but is not registered: Microsoft displays
its Platform Policies assent on the final Register action, which remains a user hand-off. The
existing Administration registration still carries `User.Create`, `User.Invite.All` and
`GroupMember.ReadWrite.All` together. Do not enable it or remove roles until the dedicated group
registration is registered, has only `GroupMember.ReadWrite.All`, and its tenant consent is
verified; then leave the old registration with `User.Create` only.

SharePoint Admin Center confirmed an existing synthetic development site and an empty workpapers
library; a separate same-tenant site is available as the negative control. No new site was created.
The target and library were saved as the selected-resource draft using the App mediated profile
(revision 4, state `VALIDATING`). The latest selected-site result is `BLOCKED_EXTERNAL` with
diagnostic `selected-resource-draft-edited`; AuditSphere application consent still reads `REQUIRED`.
The user authorized granting `write` only to the selected site and a single disposable file
create/read/delete probe, plus verification that access to the unrelated control site is denied.
Neither the grant nor the probe has run because the selected-site certificate is not installed and
the app is not configured on the server. The SharePoint tenant consent for `Sites.Selected` does
not itself authorize a site.

No M365 credential or capability flag was installed or enabled on the VPS during this follow-up.
The checked `.env` contained no M365 key names or enable flags, and the configured credential store
contained zero PEM, CRT or key files. No values were displayed or changed. The repository Compose
wiring remains disabled by default and has not been confirmed deployed to the VPS.

### Follow-up: Selected Site credential provisioned (2026-10-10)

With the owner's explicit authorization, a dedicated RSA certificate was created on the VPS for the
Selected Site app. The private key remains in the VPS credential store with owner/group-only read
permissions for the API identity; only the public certificate was uploaded to the matching Entra app.
The protected `.env` now contains that non-secret app ID and the separate control-site URL. The
Selected Site enablement flag remains off; no capability was enabled and no service was restarted.
Certificate contents, IDs and exact tenant resource URLs are intentionally omitted.

A read-only inspection of the current API container found no `/run/auditsphere/m365` mount and no
Selected Site app ID or certificate-path environment setting. The checked-in Compose file declares
the mount and settings, but the running container is not using that configuration. Recreating the
API container from the current Compose configuration requires separate owner approval. The service
was left untouched, the capability flag remains off, and the credential is not runtime-available.

No exact-site permission or test file has been created. Microsoft Graph's site-permission creation
API requires `Sites.FullControl.All` for the grant operation. The Selected Site app was not given
that tenant-wide permission; the grant must use a separately approved administrator mechanism.
Until that exact `write` grant exists, the disposable create/read/delete probe and denial check at the
unrelated control site cannot run. AuditSphere remains `BLOCKED_EXTERNAL` for this capability.

### Follow-up: directory mutation permission split (2026-10-11)

The owner authorized registering and consenting to three one-role identities and removing the two
extra roles from the former combined Administration registration. The Entra API-permission pages
now show:

| Registration | Application permission | Entra consent |
|---|---|---|
| `AuditSphereOps Development Administration` | `User.Create` only | Granted |
| `AuditSphereOps Acceptance Guest Invitation` | `User.Invite.All` only, plus delegated `User.Read` | Granted |
| `AuditSphereOps Acceptance Group Membership` | `GroupMember.ReadWrite.All` only, plus delegated `User.Read` | Granted |

For the former combined app, `User.Invite.All` and `GroupMember.ReadWrite.All` were removed from
configured permissions and their leftover tenant grants were revoked. The group registration was
created and tenant consent was granted. This completes Entra permission separation; it does not
configure role-specific credentials on the VPS. A fresh AuditSphere page observation still shows
the consent attempt as `None · Not attempted`, directory verification as `NOT_VERIFIED`, and
User.Create, guest invitation and group membership as Disabled. No capability was enabled and no
directory mutation was performed. Keep these capabilities disabled until their credentials and
synthetic target checks are separately configured and verified.

### Follow-up: safe API runtime state after approved recreation (2026-10-11)

The owner-approved API-only recreation has completed from the repository Compose configuration;
`/health/ready` returned HTTP 200. The database and workers were not recreated and no migration ran.
The API received the read-only credential mount, but `SELECTED_SITE_CLIENT_ID` was cleared from the
protected `.env` pending the exact-site grant, so the selected-site provider is not configured in
the running API. `ExternalEffects`, Directory Reader, user provisioning, guest invitation, group
membership, outbound mail and live PBC transfer remain false. A fresh AuditSphere page still reports
consent `REQUIRED` with no attempt, directory `NOT_VERIFIED`, selected resource `BLOCKED_EXTERNAL`,
and activation unavailable. No Graph call, site grant, file probe or workspace activation occurred.
Restore the Selected Site app ID privately only after the user grants the exact approved site; then
run the already-authorized positive target and unrelated-site denial probe. Do not add
`Sites.FullControl.All` to the runtime app.

### Follow-up: upgrade rehearsal and exact-site access check (2026-10-11)

Owner-authorized SSH access succeeded while the Hostinger browser terminal remained expired.
The published API and migration images were pulled. A private custom-format database backup was
catalog-checked, restored to a disposable database, and upgraded with the release migration bundle.
The release API passed `/health/ready` against that copy. The temporary API, database and environment
files were removed. The live database and existing containers were unchanged; applying the pending
live migrations requires approval beyond the earlier API-only recreation scope. The execution
ledger records the image, migration head and backup location.

The selected-site certificate matched the public certificate in Entra, and its token held exactly
`Sites.Selected`. A read-only check returned HTTP 403 `accessDenied` for the approved site as well
as the unrelated control. The positive site grant is therefore still missing. No file was created,
no site permission changed, and the runtime app ID remains unset. After the user grants the exact
site, repeat the target and unrelated-site checks before activating the resource or attempting the
already-authorized disposable file probe.

### Configure a capability

1. **Use a dedicated app identity.** Keep the OIDC sign-in application separate. Give each Graph
   identity exactly one application role: `User.Read.All`, `Sites.Selected`, `Mail.Send`,
   `User.Create`, `User.Invite.All` or `GroupMember.ReadWrite.All`. The three directory-mutation
   registrations are now split as recorded above; configure a matching role-specific credential
   before enabling any capability. Do not grant `Directory.ReadWrite.All`, broad SharePoint scopes
   or directory-role assignment.
2. **Create and install a certificate per Graph identity.** The API and optional M365 workers read
   only the read-only mount `/run/auditsphere/m365`; its host source is
   `/opt/auditsphere/storage/m365-credentials`. Create the host directory with owner-only
   permissions and keep each private key in its capability subdirectory. Upload only the public
   certificate to the matching Entra app. The repository and browser/chat are not credential
   stores. The compose wiring maps certificate paths for Directory Reader, Selected Site,
   provisioning, guest invitation, group membership and outbound mail. All corresponding switches
   default to `false`.
3. **Set deployment configuration privately.** Use `.env` for non-secret enablement flags,
   application IDs, sender mailbox and approved resource targets. The consent callback uses the
   Directory Reader app ID and certificate; `TENANT_CONSENT_ENABLED` controls the flow, while
   `TenantConsent__ClientId` is bound to `DIRECTORY_READER_CLIENT_ID` in Compose. Configure
   `DIRECTORY_READER_*`, `SELECTED_SITE_*`, `TENANT_ADMINISTRATION_*` and `OUTBOUND_MAIL_*` only
   after the matching registration, certificate and consent are ready. Never print or commit `.env`.
   Keep `ExternalEffects__Enabled=false` for the API and core workers.
4. **Complete the consent handshake.** Register the fixed callback
   `https://audit.steaudit.com/auth/m365-consent/callback` and identity callback
   `https://audit.steaudit.com/auth/m365-consent/identity-callback`. From the tenant-connection
   page, start the reviewed consent flow, complete the Microsoft administrator step, return to
   the app, and finish the nonce-bound identity check. Then use **Verify all enabled capabilities**
   and refresh persisted status. A portal consent receipt alone does not update AuditSphere's
   verification rows.
5. **Verify Selected SharePoint separately.** For this acceptance, the existing synthetic
   development site and its empty workpapers library are the reviewed target; a separate
   same-tenant site is the negative control. Confirm the server-side selected-site app ID,
   certificate paths and `SelectedSite:CredentialReference` match the persisted connection
   revision before probing. The AuditSphere boundary check reads only the saved site, drive and
   root, then requires HTTP 403 from the unrelated same-tenant site; it writes trusted evidence
   only when both results pass. A separate disposable file probe requires explicit authorization,
   an empty test library, read-back verification and confirmed deletion. Never grant the control
   site or activate a workspace from the boundary check alone.
6. **Verify outbound mail separately.** The owner must identify one approved sender mailbox.
   Keep the `Mail.Send` app restricted to that mailbox through the approved Exchange application
   access control, and configure the isolated Acceptance mail worker. The optional `worker-mail`
   service is excluded from default startup; it can run only through the `m365-mail` Compose
   profile. Send only a reviewed test to a controlled synthetic recipient. Do not start that
   profile until the permission, mailbox restriction, recipient and queued operation have been
   reviewed.
7. **Keep directory mutations off until individually reviewed.** Provisioning, invitations and
   group membership each need their own one-role app and consent. Use only synthetic test users,
   a designated test group and an HTTPS guest redirect. Review the exact target before enabling
   any operation; do not infer an AuditSphere role from Microsoft group membership.

The optional `worker-pbc` service is also excluded from default startup and requires the
`m365-pbc` profile. `PBC_TRANSFER_LIVE_PROVIDER` defaults to `false`; enabling it changes new
client uploads from a local queue disposition to live SharePoint delivery. Start that worker only
after the approved site and negative-control checks pass, and after reviewing the exact queued
synthetic transfer. The `client-sites` privileged worker remains a separate owner-approved rollout.

The Compose changes in the repository remain disabled by default. A deployed status changes only
after the approved revision is deployed and the relevant capability is freshly verified in the UI.
The portal grants above do not change the server configuration or verify capability access. The
live connection remains Consent Required, and the saved selected-site draft remains
`BLOCKED_EXTERNAL` until the exact site grant, certificate-backed probe and nonce-bound consent are
completed.

## Live upgrade and Directory Reader verification — 2026-10-11

This follow-up supersedes earlier observations that the live upgrade or Directory Reader setup
awaited approval. Exact image, database head, backup and persisted check timestamps are recorded
in [`status.json`](../../docs/execution/status.json).

1. On the VPS, retain a fresh private PostgreSQL custom-format backup and check its catalog.
   The approved migration bundle had already passed against a restored disposable copy.
   With the approved release tag in the protected environment, run
   `docker compose --profile migration run --rm migrate` from this guide's Compose directory.
   The bundle completed. Recreate only the API with `docker compose up -d --no-deps api`.
   Public readiness returned HTTP 200; worker container IDs were unchanged.
2. On the dedicated Directory Reader app's Web authentication configuration, save both fixed
   callbacks listed in the capability setup section. Create a dedicated RSA certificate under the
   protected VPS credential directory. Match its group to the API process, restrict private-key
   access, and verify the API can read the mount. Upload only the public certificate in Entra's
   Certificates and secrets page. Keep the private key on the VPS.
3. Set `DIRECTORY_READER_CLIENT_ID` privately to the dedicated registration and enable
   `DIRECTORY_READER_ENABLED` and `TENANT_CONSENT_ENABLED`. Validate Compose configuration
   with `docker compose config -q`, then recreate only the API. Readiness passed again.
   Keep outbound mail, directory mutations and live PBC transfer disabled.
4. On AuditSphere's tenant-connection page, review the exact tenant and permissions, select
   **Connect Microsoft 365 tenant**, complete Microsoft consent using the approved signed-in
   administrator, and finish the identity callback. Then select **Verify all enabled capabilities**.
   AuditSphere persisted verified consent, consenting identity and `bounded-read-succeeded`.
   Browser console error checks were empty. The connection/resource revision is a separate state;
   verified directory consent does not activate SharePoint or the workspace.
5. Selected Site is now configured and independently verified as described in
   [Selected-site grant and runtime verification](#selected-site-grant-and-runtime-verification--2026-10-11).
   The exact approved site is allowed and the unrelated control site is denied. The disposable file
   probe was created, read and deleted, and the library was confirmed empty afterward. Directory
   Reader freshness showed `STALE` at the latest tenant-page observation; refresh and verify it
   before relying on current directory data. Records remains `NOT_CONFIGURED`, no client workspace
   was activated, and broader M365 and production acceptance remain open.

The time page renders the FSLI attribution fields. End-of-service access is correctly restricted
by Finance/Partner grants. The commercial settings screen also renders the pricing
policy form and reports that no actual pricing policy has been configured. These observations do not replace workflow acceptance with reviewed
business records or the firm's professional decisions.

## Mail staging and Exchange sign-in prerequisite — 2026-10-11

The owner approved the same existing mailbox as sender and controlled recipient. Exchange's
mailbox inventory confirmed the mailbox exists. The dedicated Mail app already has a current
certificate matching the locally configured credential, so no additional Entra credential was
created. The credential was transferred privately into the protected outbound-mail VPS mount;
private-key access follows the API process group and restricted permissions. The protected
environment now contains the app and sender settings with `OUTBOUND_MAIL_ENABLED=false`.
Compose configuration validation passed. No service was recreated and no mail was sent.

Mailbox-scoping verification must precede enabling Mail or its isolated worker. Exchange Cloud
Shell was unavailable because there is no valid Azure subscription. Local PowerShell and the
ExchangeOnlineManagement module are installed; the supported device sign-in is awaiting the
administrator. Inspect the existing policy for the exact Mail app, check the approved sender,
and require an unrelated mailbox denial before proceeding. Microsoft documents this restriction
and its verification in [Application Access Policies](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-access-policies)
and [Test-ApplicationAccessPolicy](https://learn.microsoft.com/en-us/powershell/module/exchangepowershell/test-applicationaccesspolicy?view=exchange-ps).
A policy test alone does not establish message delivery.

A fresh Selected Site check after the owner's grant report still denied both the approved target
and unrelated control. Investigate the exact app/site grant before another probe. Do not broaden
permissions to make the positive check pass. Exact outcomes are in the execution ledger.

## Exchange authentication outcome — 2026-10-11

The Microsoft device sign-in page showed success, but the local Exchange PowerShell connection
reported `Broken` and an expired token. The preceding object-not-found policy response is not a
mailbox-isolation pass. Duplicate authentication retries were stopped. No Exchange policy or group
was changed, no mail capability was enabled, and no message was sent. Obtain a working supported
Exchange administrator connection before policy verification; retain the protected staged
credential and disabled settings. Do not use browser token extraction or expand application roles
to bypass this authentication failure.

## Verified sender restriction and synthetic delivery — 2026-10-11

This follow-up supersedes the earlier authentication blocker. Exact deployment observations,
request correlation and persisted capability checks are in the execution ledger.

1. Use a temporary official Microsoft PowerShell Linux container on the VPS when the local
   authentication library rejects the operator's Mac OS. This run used the official PowerShell
   Debian image and ExchangeOnlineManagement from PSGallery. Install/import the module, then
   run `Connect-ExchangeOnline -Device -ShowBanner:$false`; the administrator completes the
   specific device sign-in. Require an active connection before policy commands. No app
   credentials need to be mounted into this administrator container.
2. Inspect mailbox scope with
   `Test-ApplicationAccessPolicy -Identity <APPROVED_SENDER> -AppId <MAIL_APP_ID>` and the same
   command for `<UNRELATED_SYNTHETIC_MAILBOX>`. Both initially returned Granted. Create a
   mail-enabled security group containing only the approved sender, with closed join/departure
   settings. Check its membership before applying a policy; refuse an existing broader group.
3. Apply `New-ApplicationAccessPolicy -AppId <MAIL_APP_ID> -PolicyScopeGroupId <SCOPE_GROUP_EMAIL>
   -AccessRight RestrictAccess -Description <REVIEWED_DESCRIPTION>` only for the approved Mail
   identity. Check the approved sender is Granted and the unrelated fixture Denied. Then check
   Graph authorization separately: an unrelated-mailbox request with no recipients returned
   `403 ErrorAccessDenied`. An empty body instead returned a parser error and was not counted
   as an authorization pass. Never send a negative-control message or treat parsing failure as
   proof of isolation.
4. Send only the reviewed synthetic message from the approved sender to its approved controlled
   recipient. This self-addressed test returned Graph acceptance; a filtered Exchange Message
   Trace confirmed Delivered. Include the actual send time in the trace interval, accounting for
   the portal's displayed timezone. An initially stale trace end time excluded this message and
   was corrected. Do not confuse Graph acceptance with delivery.
5. With the existing matching certificate in the protected mount, set `OUTBOUND_MAIL_ENABLED=true`
   in the private environment. Validate Compose and recreate only the API. Readiness and key
   readability passed, and existing workers were unchanged. Use **Verify all enabled capabilities**
   in AuditSphere; Outbound Mail now reports verified application permission. Review/save the
   Mail local metadata and acknowledge its immutable receipt before refreshing the displayed
   configuration. Records remains unconfigured until its separate provider checks pass.
6. Disconnect Exchange and remove the temporary container and verification files. Retain the
   protected app credential and environment backups. The live mail worker was not started;
   review queued recipients and exact durable operations before enabling that separate profile.
   A direct transport test does not establish proposal/PBC workflow delivery or receipt integration.

The restrictive policy affects every use of the existing dedicated Mail identity, including its
local development use: it can send only from the approved sender. No tenant role or additional
Graph permission was granted. SharePoint access and directory mutations remain separate work.

## Separated directory capability setup — 2026-10-11

The owner specifically approved public certificate uploads and API enablement for the separated
Guest Invitation and Group Membership registrations, alongside the existing User Creation identity.
This supersedes the earlier disabled-state observations; full mutation workflows remain untested.

1. Inspect each exact registration and its existing credentials. The User Creation registration's
   certificate matched the locally configured credential and was privately staged on the VPS.
   Create separate RSA key pairs for Guest Invitation and Group Membership in their protected
   capability directories. Match private-key group readability to the API process and keep the
   keys on the VPS. Do not reuse the old combined Administration identity for the two new roles.
2. Upload only each public certificate to its corresponding Entra registration after exact owner
   confirmation. Verify the saved public thumbprint matches its VPS certificate. No new API
   permission or tenant role was requested in this credential step.
3. Save distinct app IDs in the protected environment. Acquire certificate-backed tokens privately
   and require the configured tenant and exactly one role: `User.Create`, `User.Invite.All`, or
   `GroupMember.ReadWrite.All` for the corresponding identity. Stop before enablement if any
   role is missing or an extra role appears. Never log tokens or assertions.
4. Enable the three reviewed `TENANT_ADMINISTRATION_*_ENABLED` flags, validate Compose, and
   recreate only the API. Readiness and key readability passed; existing workers were unchanged.
   In AuditSphere select **Verify all enabled capabilities**. User Creation and Guest Invitation
   verified their app permissions; Group Membership verified a bounded read. Browser console
   checks were empty. Exact timestamps and diagnostics are in the execution ledger.
5. Keep actual directory actions behind their application preview/review gates. This run created
   no user, invitation or group membership change and did not establish full mutation-journey
   acceptance. Microsoft permissions do not assign AuditSphere roles or Entra administrator roles.

The selected SharePoint target still requires exact-app grant investigation. No permission was
changed through its site-admin panel, and no further duplicate provider probe was run.

## Isolated worker credential mounts

The live mail profile mounts only the outbound-mail certificate directory through
`m365_mail_credentials`; it cannot read Directory Reader, User Creation, invitation, group or
Selected Site keys. The optional PBC profile mounts only its Selected Site certificate directory
through `m365_selected_site_credentials`. Both mounts are read-only. The API retains the parent
credential mount because it composes the individually configured capability providers.

Before starting the mail profile, inspect current-firm pending durable mail operations and queued
commercial/PBC notifications, including whether any queued recipient is outside the approved test
scope. Refuse activation when an unreviewed send is pending. Validate both optional profiles with
`docker compose --profile m365-mail --profile m365-pbc config -q`, then start only the reviewed
mail service with `docker compose --profile m365-mail up -d --no-deps worker-mail`.
Check the worker is running without restart or failure, can read its own key, and cannot see
sibling credential directories. Preserve the existing API and other worker containers. A running
idle worker proves deployment readiness, not an unexecuted business-mail workflow.

## Live isolated mail worker activation — 2026-10-11

This follow-up supersedes earlier statements that the mail worker remained stopped. Current-firm
queued commercial/PBC rows, pending durable mail operations and unreviewed recipients were checked
before activation and were empty. The exact image was pulled and only `worker-mail` started through
its optional profile. A credential-boundary review identified the broad parent mount; the canonical
Compose definition now limits the mail worker to its own certificate directory and the PBC worker
to its own Selected Site directory.

After validating both profile definitions against the private server environment, the clean VPS
checkout was fast-forwarded and only the new mail worker recreated. Dynamic checks confirmed its
own key was readable, all sibling credential paths were absent and the credential mount read-only.
The container remained running without restarts or failure log entries; successful database polling
queries were observed. API readiness passed and the existing API/other worker containers were
unchanged. The administrator Operations screen also passed a browser access/error check.
No queued business mail was executed, so this does not establish complete proposal/PBC workflow
acceptance. The PBC worker remains stopped pending SharePoint verification.

SharePoint consent for the runtime identity is recorded in Entra, but a separate exact-site grant
is still required, as described in Microsoft's
[Selected permissions overview](https://learn.microsoft.com/en-us/graph/permissions-selected-overview).
The owner-reported grant still produced approved-site denial. Request the exact grant receipt for
the reviewed app and site; do not repeat admin consent, broaden the runtime scopes, or repeatedly
probe an unchanged denial. Exact deployment and queue facts are in the execution ledger.

## Selected-site grant and runtime verification — 2026-10-11

This observation supersedes earlier statements in this guide that the selected-site app ID was
unset, the target was denied, or the grant and probe had not run. With the owner's authorization,
the temporary single-tenant public-client grantor received delegated `Sites.FullControl.All`
consent only for creating the one approved site grant. It granted `write` to the `AuditSphere
Development` site for the dedicated `AuditSphere SharePoint UAT` runtime app. The temporary
registration and its delegated consent were then deleted and the Entra enterprise-application
search confirmed it was absent. No application `Sites.FullControl.All` role was granted; the
runtime token continued to contain exactly `Sites.Selected`. The unrelated control site received
no grant.

The VPS already held the matching certificate and private key. The initial application check
exposed a credential-reference mismatch: the saved AuditSphere connection expects
`configuration:SelectedSite`, while Compose defaults to
`hostinger-acceptance-selected-site-certificate`. The protected `.env` was updated with the
selected-site app ID and `SELECTED_SITE_CREDENTIAL_REFERENCE=configuration:SelectedSite`; its
mode is `0600`. Only the API container was recreated. Its certificate and private-key files were
readable, its provider settings matched the saved connection, and `/health/ready` returned HTTP
200. The database and other worker containers were left running; no migration ran.

The built-in AuditSphere verification recorded the saved resource as `VERIFIED` at draft revision
7 with diagnostic `exact-resource-and-control-denial`. The app token's tenant and Graph audience
matched, and its only application role was `Sites.Selected`. Graph returned HTTP 200 for the exact
approved site, `Internal Workpapers` drive and saved root folder, and HTTP 403 for the unrelated
same-tenant control site. AuditSphere persisted the positive resource reads and control denial.

The separately authorized disposable-file check used the empty `Internal Workpapers` library. One
uniquely named text file uploaded with HTTP 201, its content read back byte-for-byte, and it was
deleted with HTTP 204. A follow-up item read returned HTTP 404; the SharePoint library browser
again showed an empty list. No client workspace was activated and the PBC worker remains stopped.
The current tenant page shows connection and consent `VERIFIED`; the Directory Reader freshness
label showed `STALE` at the latest page observation and needs its own refresh before relying on
current directory-verification recency. This selected-site result does not complete the wider
M365 or production acceptance gates.

SharePoint consent for the runtime identity is recorded in Entra, but the exact-site grant was
required separately, as described in Microsoft's
[Selected permissions overview](https://learn.microsoft.com/en-us/graph/permissions-selected-overview).
The site grant is now verified as described above. Do not repeat admin consent or broaden the
runtime scopes. Exact deployment and queue facts are in the execution ledger.

## Operational boundaries

- PostgreSQL is not published on a host port. The API is published only on host loopback port
  `18080`; host Caddy is the only public route.
- The network uses `172.29.91.0/24`; the API trusts forwarded headers only from its Docker
  gateway `172.29.91.1`. Change both the Compose subnet and trusted proxy together if the range
  conflicts with the VPS.
- Keep `ExternalEffects__Enabled=false` on the API and core workers, `DevelopmentIdentity__Enabled=false`,
  and `AllowSimulationAdapters=false`. The dedicated client-sites worker alone uses external effects
  and only the approved isolated certificate mounts; do not add production client data or approved
  release data to this environment.
- Set both `ASPNETCORE_ENVIRONMENT=Acceptance` and `DOTNET_ENVIRONMENT=Acceptance` for worker
  containers. The API uses the ASP.NET Core host; the Worker uses the Generic Host, which reads
  `DOTNET_ENVIRONMENT`.
- Persistent Compose volumes are `auditsphere-acceptance_postgres_data`,
  `auditsphere-acceptance_data_protection`, `auditsphere-acceptance_pbc_staging`, and
  `auditsphere-acceptance_release_checkpoints`, backed by the 30 GB filesystem under
  `/opt/auditsphere/storage`. Back up that filesystem before upgrades and retain the exact image
  tag for rollback. Keep at least 30 GB free on the root filesystem before creating the preallocated
  image file.
- This VPS has existing services. Do not alter their containers, databases, ports, Caddy routes,
  firewall rules or host packages as part of this app deployment.
