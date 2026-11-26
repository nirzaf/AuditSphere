# Production Microsoft Entra ID Configuration

This page is for the Entra administrator and deployment owner. Replace placeholders only in the deployment secret/configuration system; never paste production values into the Wiki.

## Registration separation

Use separate production registrations and credentials for separate purposes:

| Identity | Purpose | Boundary |
| --- | --- | --- |
| `audit-web-login` | Interactive OIDC sign-in | Identity only; no document-worker credential |
| `audit-directory-reader` | Tenant connection identity check and bounded user discovery | Separate certificate, reviewed `User.Read.All` application role |
| `audit-doc-worker` | Working-document provider operations | Selected approved SharePoint sites only |
| `audit-records-worker` | Approved snapshots and final records | Separate records identity and grants |
| Provisioning/records administrator | Exceptional selected-resource grants and records administration | Controlled owner-operated path, not the public web process |

The names are conventions. Separation of purpose and least privilege are the requirements.

## Web registration

1. Create an organizational-tenant-only production app registration.
2. Record the tenant ID and client ID in the deployment record.
3. Add exactly `https://<production-host>/signin-oidc` as the web redirect URI when the default callback is used. If `Identity:CallbackPath` is changed, the Entra URI must match it exactly.
4. Do not use wildcard, localhost, alternate-host, or guessed trailing-slash redirect URIs in production.
5. Configure the confidential credential through the approved secret/certificate reference. The current code path reads `Identity:ClientSecret`; do not claim certificate/workload-federation support unless the deployed composition implements and tests it.
6. Keep development and staging registrations separate.

The ASP.NET Core API configures the tenant authority as `https://login.microsoftonline.com/<tenant-id>/v2.0`, uses authorization-code OIDC, clears default scopes, and requests only `openid`, `profile`, and `email`. Authentication setup and callback processing are implemented in `ApiHost.Authentication.cs` and `ApiHost.AuthenticationEndpoints.cs`; the API validates token `tid` and GUID `oid` before resolving a local actor.

Implementation: [API OIDC setup](../../src/AuditSphereOps.Api/ApiHost.Authentication.cs), [sign-in and callback endpoints](../../src/AuditSphereOps.Api/ApiHost.AuthenticationEndpoints.cs), and the API [TrustedActorResolver](../../src/AuditSphereOps.Api/Authentication/TrustedActorResolver.cs).

## One-time owner onboarding

With the production `AngularUi:CanonicalRoutes=true` setting, setup is at `/setup/microsoft365` and tenant connection is at `/app/administration/microsoft365/tenant-connection`. The `/ui`-prefixed compatibility routes are also served. The flow is:

1. Deployment owner creates a random proof out of band and configures only its hash at `Setup:BootstrapProofHash`.
2. Configure `Setup:FirmId`, `Setup:InstallationId`, `Setup:InitialAdministratorTenantId`, and `Setup:InitialAdministratorObjectId` from the approved deployment record.
3. The nominated owner signs in through Microsoft OIDC and enters the proof. Before the local user exists, only the exact configured tenant/object identity can complete this setup-only sign-in; protected application views still require a mapped user and current session epoch.
4. `Microsoft365OnboardingService.ClaimAsync` verifies the hash, expiry, installation, and single-use state. It binds the setup session to the authenticated tenant/object identity.
5. `CompleteInitialAdministratorBootstrapAsync` creates the local initial administrator binding only. It does not grant Entra, Graph, SharePoint, or Exchange administration.
6. After the local binding, the setup page starts a fresh Microsoft sign-in to bind the new session epoch. On later visits, the same current firm-wide Administrator selects **Continue setup with this account**; an empty draft tenant field is filled from the authenticated Microsoft tenant ID. Before activation, the administrator can save additional non-secret draft values and proceed with separately approved integration checks. After activation, the same bound administrator can reopen the active setup for review; **Save draft** is disabled, and changing the working site requires a new configuration revision. See the [active-session guard](../../src/AuditSphereOps.Application/Microsoft365/Microsoft365OnboardingService.cs).

The setup page does not grant permissions or create SharePoint content. An expired setup session can be resumed **without repeating the proof** only by its already-bound identity while its current local session and firm-wide Administrator grant remain valid; the server issues a new short-lived capability. A different identity, stale session, or revoked grant remains denied. First-time bootstrap still requires the private proof; never use a default proof or first-visitor ownership. The [current setup implementation](../../src/AuditSphereOps.Application/Microsoft365/Microsoft365OnboardingService.cs) enforces this distinction.

Setup-only sign-in and the post-binding redirect are implemented in [InitialAdministratorSignIn](../../src/AuditSphereOps.Api/Authentication/InitialAdministratorSignIn.cs), [API authentication endpoints](../../src/AuditSphereOps.Api/ApiHost.AuthenticationEndpoints.cs), and the [Angular setup page](../../src/AuditSphereOps.Ui/src/app/features/admin/bootstrap.ts). The renewal guard is implemented in [Microsoft365OnboardingService](../../src/AuditSphereOps.Application/Microsoft365/Microsoft365OnboardingService.cs).

## Local role assignment

Use `RoleAdministrationService`, not Entra directory roles, to assign AuditSphere permissions. The service binds users to exact `(TenantId, Subject)` values and keeps evidence for changes. It supports `FIRM_WIDE`, `CLIENT`, and `ENGAGEMENT` scopes; scope identifiers must match the selected client/engagement relationship.

The complete supported role catalogue and scope boundaries are maintained in the [current roles and privileges reference](../architecture/auditsphere-architecture-user-roles-and-privileges-current.md). An Entra user is not automatically an AuditSphere user, and a Microsoft administrator is not automatically an AuditSphere administrator.

Grant behavior includes maker/checker self-elevation prevention, session-epoch invalidation, historical revocation, and last-administrator protection. Invitations create a local `/auth/landing` destination and an outbox intent; they do not prove mail delivery.

Implementation: [`RoleAdministrationService.cs`](../../src/AuditSphereOps.Application/Security/RoleAdministrationService.cs).

## Permissions and live consent

The current permission decision approves one narrow exception: a **separate**, read-only directory-reader application may request Microsoft Graph application `User.Read.All` for bounded user discovery and exact-object checks. A separate Development registration completed a bounded live read; production remains unverified. Microsoft also displayed delegated `User.Read` for sign-in/profile on the initial consent screen, although that registration declares only the `User.Read.All` application role; the certificate-backed reader does not use a delegated Graph token. The normal approved-roster workflow remains available. Tenant user creation, guest invitation and managed-group mutation are optional, separately gated capabilities; none is enabled by a successful consent callback. See the [permission matrix](../architecture/auditsphere-m365-tenant-administration-permissions.md) before requesting any Graph role.

### Tenant connection consent session

1. **Deployment operator:** Prepare a separate Entra registration for the approved directory reader. Register two exact HTTPS web callbacks: `https://<APP_HOST>/auth/m365-consent/callback` for administrator consent and `https://<APP_HOST>/auth/m365-consent/identity-callback` for the subsequent administrator sign-in. For local Development only, the corresponding `http://localhost:<WEB_PORT>/...` callbacks are accepted. Use the same separate registration for both callbacks, never the Web sign-in registration. Request only the reviewed permission and arrange the tenant's required administrator authority for application consent.
2. **Deployment operator:** Privately set `TenantConsent:ClientId`, `TenantConsent:RedirectUri`, and `TenantConsent:IdentityRedirectUri` to that registration and its exact callbacks. The identity code is redeemed with the directory reader's separate certificate, so configure `DirectoryReader:ClientId`, `DirectoryReader:CertificatePath`, and `DirectoryReader:PrivateKeyPath` for the same registration. Set `TenantConsent:Enabled=true` only after these are reviewed. The checked-in defaults are disabled. Restart the application host (the `AuditSphereOps.Api` host) after the approved database migration.
3. **Current firm-wide AuditSphere Administrator:** Prepare the matching Microsoft 365 setup draft, then open **Administration → Microsoft 365 → Tenant connection** and select **Connect Microsoft 365 tenant**. Microsoft handles tenant-admin sign-in and permission review; never enter a Microsoft password in AuditSphere. If the button is disabled, check the enable flag, separate client ID, exact callback and prepared draft.
4. **Deployment operator:** Inspect the returned state. A one-use consent callback alone is provisional; the app then requests a nonce-bound Microsoft authorization-code sign-in with `openid profile`, validates the expected tenant, audience, issuer, immutable user object ID, expiry and nonce, and records `CONSENT_VERIFIED` only for an accepted member identity. The `profile` scope is needed for the immutable `oid` claim. A denied, expired, replayed, wrong-tenant, external or unverified identity stays blocked. Review each capability row separately: a verified directory read does not verify SharePoint, mail, provisioning, invitations or groups. The [current verifier](../../src/AuditSphereOps.Infrastructure/Providers/GraphTenantConsentVerifier.cs) and [consent service](../../src/AuditSphereOps.Application/Microsoft365/TenantConsentService.cs) implement these checks.

An administrator-entered evidence reference cannot establish Microsoft tenant consent. The [configuration service](../../src/AuditSphereOps.Application/Microsoft365/Microsoft365ConfigurationService.cs) refuses a manually recorded `TENANT` / `CONSENT` pass, and connection activation requires the trusted verified-consent state. Older `OBSERVED` revisions cannot use historical pass rows to activate. Consent verification alone does not activate the setup draft or prove effective access to a saved SharePoint site.

If Microsoft shows a session-context error, start a fresh connection from AuditSphere; do not reload or reuse an old Microsoft consent URL. If the return says identity blocked, confirm both exact callbacks, the separate certificate and `openid profile` support, then retry from the connection page. Never paste callback URLs, authorization codes, tokens or claims into a support ticket or the Wiki.

### Read-only directory search

1. **Deployment operator:** After the separate reader app is approved and consented for Graph application `User.Read.All`, install its certificate public half in that app registration and mount the matching private certificate files through the approved runtime secret path. Do not reuse the selected-site document-worker credential.
2. **Deployment operator:** Privately configure `DirectoryReader:ClientId` to the same separate app ID as `TenantConsent:ClientId`, plus `DirectoryReader:CertificatePath` and `DirectoryReader:PrivateKeyPath` as absolute private runtime paths. Set `DirectoryReader:Enabled=true` only for that reviewed registration. The checked-in default is disabled. Restart the application host (the `AuditSphereOps.Api` host); missing certificate files fail closed.
3. **Current firm-wide AuditSphere Administrator:** On **Tenant connection**, search by a display-name or user-principal-name prefix of at least two characters, optionally entering a user-principal-name domain such as `<APPROVED_DOMAIN>`. Entering only a domain lists enabled users in that domain. The domain is matched as an exact `@domain` suffix by Microsoft Graph, not by filtering a returned page in the browser. The page requests only `id`, `displayName`, `userPrincipalName`, `accountEnabled`, and `userType`, at most 25 per page. It requires an exact-tenant token whose sole application role is `User.Read.All`. A search result is read-only and does not create a local user or grant a role. See the [directory query](../../src/AuditSphereOps.Application/Microsoft365/DirectoryDiscoveryService.cs) and [Graph reader](../../src/AuditSphereOps.Infrastructure/Providers/GraphDirectoryReader.cs).
4. **Current firm-wide AuditSphere Administrator:** Choose **Verify directory access**. The app reads your exact Microsoft member object through the separate reader and records a pass or blocked result against the current connection revision. A pass appears as `VERIFIED_RECENT` for 15 minutes, then `STALE`; it does not prove who granted consent, activate the connection, or verify SharePoint or mail. If blocked, check consent, certificate mount, exact token role and tenant, then retry. The implementation is at [source revision `2ccaad7125563a55a398cdd906eebc03b2736860`](../../src/AuditSphereOps.Application/Microsoft365/DirectoryCapabilityVerificationService.cs).

### Choose an active Microsoft user in Administration

1. **Current firm-wide AuditSphere Administrator:** Open **Administration → User and role administration**. The **Active Microsoft user** dropdown loads enabled accounts from the configured tenant, 25 per page. Use **Next 25 users** to continue; the page does not import the full directory or grant access merely by listing it.
2. Enter an optional user-principal-name domain such as `<APPROVED_DOMAIN>` and choose **Load active users**. This matches the exact `@domain` suffix through a bounded Microsoft Graph query. Clear the filter and load again to browse all enabled users. If the query is blocked, check the separate reader consent, certificate and tenant.
3. Select a workforce member and choose **Bind selected Microsoft user**. The app re-reads that object by immutable ID before local binding. Review the resulting role and scope fields separately; both are blank. Guest entries need the separate client workflow. The manual approved-roster fields are in a collapsed fallback disclosure. The current picker is in the [Angular Users & Access page](../../src/AuditSphereOps.Ui/src/app/features/admin/access.ts).

### Bind an existing workforce member

1. **Current firm-wide AuditSphere Administrator:** Search for the existing member, review the exact object ID and enabled status, then choose **Bind exact identity**. AuditSphere re-reads that immutable object from the configured tenant before creating or refreshing the local staff binding. Disabled, guest, changed, or mismatched objects are refused.
2. Follow **Review role and scope**. The local role and scope are intentionally blank; choose each explicitly and review the target identity before saving. Binding alone creates no new `RoleGrant`. The existing role-administration service records grant evidence and invalidates protected sessions according to its own checks. Microsoft Entra administrator roles are never assigned by this action.
3. If the person is a client guest, use the separate approved guest workflow when it is available; this workforce-member path does not invite a guest or infer client access from a directory result. The binding code is in [DirectoryUserBindingService](../../src/AuditSphereOps.Application/Microsoft365/DirectoryUserBindingService.cs).

### Optional tenant administration

The Administration workspace can prepare user creation, client guest invitation, allowlisted group membership and outbound mail. Each is disabled by default. The deployment owner must approve each permission and use its own registration, certificate and private configuration prefix: `TenantAdministration:Provisioning` (`User.Create`), `TenantAdministration:GuestInvitation` (`User.Invite.All`), `TenantAdministration:GroupMembership` (`GroupMember.ReadWrite.All`), and `TenantAdministration:OutboundMail` (`Mail.Send`). For each prefix set `Enabled`, `ClientId`, `CertificatePath`, and `PrivateKeyPath` only after its separate registration and reviewed consent; guest invitations also require the approved HTTPS `TenantAdministration:GuestRedirectUrl`, and outbound mail requires `TenantAdministration:OutboundMail:SenderMailbox`. A directory-reader or selected-site credential cannot be reused for these capabilities. The current API defaults are in [`appsettings.json`](../../src/AuditSphereOps.Api/appsettings.json).

After restart, the firm-wide AuditSphere Administrator returns to **Tenant connection** and reviews the new capability state. `VERIFIED` is per-capability and expires after the configured verification age (24 hours by default); `NOT_GRANTED`, `FAILED`, `BLOCKED_EXTERNAL` and `DISABLED` do not enable that operation. Local role grants remain separate from Microsoft permissions. Before the separate saved-site test passes, the selected SharePoint row remains `BLOCKED_EXTERNAL` or `NOT_VERIFIED`; `Sites.Selected` in an app token alone is insufficient.

For the read-only boundary test, the deployment operator privately sets `SelectedSite:NegativeControlSiteUrl` to an approved, existing **synthetic** SharePoint site in the same host as the saved working site. The control must be a different site with no grant to the document-worker app; confirm its existence through SharePoint administration before testing. In development, store the value in .NET user secrets for the `AuditSphereOps.Api` project; in production, use the approved configuration store. Then restart the API host. A missing or invalid control blocks the test. The firm-wide AuditSphere Administrator chooses **Test selected site connection** in Microsoft 365 setup. The app reads the saved site, library and root, then requires Microsoft Graph to deny the same selected-site identity access to the configured synthetic control with HTTP `403`. An accessible control, `404`, throttling or changed draft fails closed. A pass records provider evidence for the exact draft and connection revision; a failed retry invalidates an earlier pass. After reviewing the exact saved working site and approved client folder template, the administrator can choose **Activate working site** to create the local default binding for future client workspaces. This does not enable mail, records or production release. See [SelectedSiteBoundaryVerificationService](../../src/AuditSphereOps.Application/Microsoft365/SelectedSiteBoundaryVerificationService.cs), the [selected-resource API endpoints](../../src/AuditSphereOps.Api/Ui/UiEndpoints.SelectedResources.cs), and the [Angular tenant page](../../src/AuditSphereOps.Ui/src/app/features/admin/tenant.ts).

`Sites.Selected` plus an explicit site grant is a separate document-worker concern. Consent is not proof of effective site access; run positive and negative tests with the exact worker identity.

## Required identity tests

- correct tenant and mapped object ID can sign in;
- wrong tenant, invalid callback, unmapped, disabled, revoked, or stale-session identities fail closed on protected application paths; the exact initial administrator can use setup-only sign-in before local mapping, with no application actor;
- the initial proof cannot create a second administrator or be claimed concurrently, and renewal after session expiry requires the bound administrator's current session and grant;
- removing a grant denies the next protected request and queued work rechecks authority;
- a last firm-wide administrator cannot be removed without a verified replacement;
- client A and engagement A cannot discover or download sibling data.

For a browser session already signed in to another account, open `/auth/sign-in?selectAccount=true` to request Microsoft’s account picker. If that session immediately resumes the existing account, open `/auth/sign-in?reauthenticate=true` to require a new Microsoft sign-in. These options only change the sign-in prompt; the identity still needs its exact local binding and active grants. The [API sign-in endpoint](../../src/AuditSphereOps.Api/ApiHost.AuthenticationEndpoints.cs) implements these options.

Record only redacted evidence: tenant/object IDs, issuer/audience, local binding, role/scope, release, time, and result. Do not attach tokens, cookies, claims dumps, or secret material.
