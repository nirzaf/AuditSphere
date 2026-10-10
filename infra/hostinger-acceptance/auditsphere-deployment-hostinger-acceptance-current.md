# Hostinger acceptance deployment

This is a separate, synthetic-data acceptance environment for the Hostinger VPS. It uses the
existing host Caddy instance and does not publish the API or PostgreSQL ports publicly. Its
Compose project, network, database, data-protection keys and upload staging are isolated from
the VPS's existing applications. External provider effects and development identity are off.

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
   `INITIAL_ADMIN_OBJECT_ID`, and a positive `DEPLOYMENT_EPOCH`. Use synthetic values only.
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
4. Start the API and three core workers with `docker compose up -d api worker-general
   worker-processing worker-records`. Verify their logs and `http://127.0.0.1:18080/health/ready`
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
handshake has not begun. The VPS environment was not re-inspected after the portal changes, and the
specific missing or mismatched setting/certificate is unknown. Check the server privately against
the consent prerequisites below without displaying `.env` or credential material.

The group-membership registration is prepared in Entra but is not registered: Microsoft displays
its Platform Policies assent on the final Register action, which remains a user hand-off. The
existing Administration registration still carries `User.Create`, `User.Invite.All` and
`GroupMember.ReadWrite.All` together. Do not enable it or remove roles until the dedicated group
registration is registered, has only `GroupMember.ReadWrite.All`, and its tenant consent is
verified; then leave the old registration with `User.Create` only.

SharePoint Admin Center confirmed the existing `AuditSphere Development` site is present. Its
`Internal Workpapers` library root was read-only inspected and had zero children. The existing
`AuditSphere P0 Unrelated` site is available as the same-tenant negative control and must not be
granted to the selected-site app. No new SharePoint site was created. The existing site and library
were saved as the selected-resource draft using the App mediated profile (revision 4, state
`VALIDATING`). The latest selected-site result is `BLOCKED_EXTERNAL` with diagnostic
`selected-resource-draft-edited`; AuditSphere application consent still reads `REQUIRED`. The
selected-site app's exact site-level `write` grant has not been made, and no boundary probe or
disposable upload was run. The SharePoint tenant consent for `Sites.Selected` does not itself
authorize a site.

No M365 credential or capability flag was installed or enabled on the VPS during this follow-up.
The earlier read-only host inspection found no M365 variable names or certificate/key files; the
host was not re-inspected after the portal changes. The repository Compose wiring remains disabled
by default and has not been confirmed deployed to the VPS.

### Configure a capability

1. **Use a dedicated app identity.** Keep the OIDC sign-in application separate. Give each Graph
   identity exactly one application role: `User.Read.All`, `Sites.Selected`, `Mail.Send`,
   `User.Create`, `User.Invite.All` or `GroupMember.ReadWrite.All`. Do not enable the existing
   combined Administration registration until its permissions have been split. Do not grant
   `Directory.ReadWrite.All`, broad SharePoint scopes or directory-role assignment.
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
5. **Verify Selected SharePoint separately.** The owner must provide the exact synthetic test-site
   URL. Resolve its Site ID, library/drive ID and root folder ID from that site, save the reviewed
   resource draft, and grant the selected-site app access to that site only. Configure an unrelated
   same-tenant negative-control site. Run the selected-site boundary check and require both the
   approved target check and the unrelated-site denial. Only then may a separate workspace
   activation review proceed. The boundary check writes and reads a disposable test object; use
   only an approved synthetic site.
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

## Operational boundaries

- PostgreSQL is not published on a host port. The API is published only on host loopback port
  `18080`; host Caddy is the only public route.
- The network uses `172.29.91.0/24`; the API trusts forwarded headers only from its Docker
  gateway `172.29.91.1`. Change both the Compose subnet and trusted proxy together if the range
  conflicts with the VPS.
- Keep `ExternalEffects__Enabled=false`, `DevelopmentIdentity__Enabled=false`, and
  `AllowSimulationAdapters=false`. Do not add production client data, approved release data or
  external-effect credentials to this environment.
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
