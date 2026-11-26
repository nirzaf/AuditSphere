# Production Configuration Guide

AuditSphereOps uses Microsoft Entra ID for staff identity, local database grants for application authorization, and selected-resource SharePoint Online/Graph access for approved document storage.

This Wiki is the primary source for deployment guidance. Update it when the implementation, approved topology, or operational procedure changes; do not rely on a copied Google Doc as the current runbook.

This guide is a deployment runbook. Read it with the [current requirements](../requirements/auditsphere-accounting-module-requirements-current.md), the [implementation architecture](../architecture/auditsphere-architecture-current-architecture.md), and the [execution ledger](../execution/status.json).

## Current implementation boundary

The current code implements the local control plane:

- protected, resumable setup drafts and hashed bootstrap proofs;
- Microsoft-only OIDC validation; Production startup requires a complete `Identity` profile;
- exact tenant/object identity binding and local role/scope grants;
- selected SharePoint binding records and immutable folder-template records;
- accepted-client workspace intent and truthful waiting/blocked states;
- records archive manifests, exact uploaded signed-document evidence, and append-only observations;
- durable outbox/worker fencing, release-safety validation, and health probes.

The Production application host is the ASP.NET Core `AuditSphereOps.Api`, which serves the Angular `AuditSphereOps.Ui` frontend; the former Blazor `AuditSphereOps.Web` host was removed from the project graph. The API refuses startup without complete OIDC settings (`Identity:TenantId`, `Identity:ClientId`, and `Identity:ClientSecret`) or the persistent, certificate-protected Data Protection profile in [Production Environment Settings](auditsphere-m365-reference-production-environment-current.md). Development/Test may use their explicitly configured local profiles. The current host composition intentionally refuses startup with `ExternalEffects:Enabled=true` until an approved live provider composition is supplied. The local Development/Test harness is not production acceptance. Live SharePoint effects, recovery, and independent review remain separate gates. Purview and eSignature provider integrations are outside the approved product scope.

## Deployment order

1. Prepare PostgreSQL, the deployment-owned firm/installation identifiers, HTTPS hostname, private Data Protection key directory and certificate, secret references, and the worker deployment epoch.
2. Register the Microsoft Entra web application and configure exact OIDC settings. See [Production Entra ID Configuration](auditsphere-m365-runbook-client-tenant-current.md).
3. Keep the web and worker processes separated. Give document/records workers only approved selected-resource permissions. See [Production SharePoint Configuration](auditsphere-m365-sharepoint-runbook-current.md).
4. Start with `ExternalEffects__Enabled=false`; run database migrations and local health checks.
5. Use the canonical `/setup/microsoft365` Angular route for the one-time owner bootstrap. The `/ui/setup/microsoft365` compatibility route is also served. This route records a local draft; it does not grant Entra, Graph, or SharePoint permissions.
6. The authorized Microsoft administrator performs consent and selected-resource grants outside the application. Record the exact tenant, app, site/library/root, role, owner, and date in the deployment evidence store.
7. Run positive and negative provider tests with the exact runtime identity and resource bindings. Do not convert a successful consent screen or local simulation into a production pass.
8. Run the [Production Readiness Checklist](auditsphere-deployment-readiness-checklist-current.md), obtain independent approvals, and only then activate the approved provider composition.

## One-time tenant administrator onboarding

The protected setup and administration routes are served by Angular and the API. Their current owners are the [Angular route table](../../src/AuditSphereOps.Ui/src/app/app.routes.ts), [initial administrator page](../../src/AuditSphereOps.Ui/src/app/features/admin/bootstrap.ts), [setup endpoints](../../src/AuditSphereOps.Api/Ui/UiEndpoints.TenantSetup.cs), [onboarding service](../../src/AuditSphereOps.Application/Microsoft365/Microsoft365OnboardingService.cs), and [Microsoft 365 configuration service](../../src/AuditSphereOps.Application/Microsoft365/Microsoft365ConfigurationService.cs).

1. The installation owner creates a strong out-of-band bootstrap proof and stores only its hash in `Setup:BootstrapProofHash`.
2. The owner gives the proof to the nominated administrator through an approved secure channel. Do not place it in the Wiki, an issue, a URL, or browser storage.
3. The administrator signs in with the configured Microsoft identity and opens `/ui/setup/microsoft365`.
4. The administrator submits the proof once. The server binds the initial session to the deployment and exact configured `(tid, oid)` identity; another identity or concurrent claim fails closed. After the fresh sign-in and local binding, the same current firm-wide Administrator opens `/ui/app/administration/microsoft365` and selects **Continue setup with this account** to resume the draft without repeating the proof. A different identity or revoked grant is denied.
5. The signed-in tenant ID fills an empty draft tenant field. The administrator supplies the approved working SharePoint site URL; resource IDs, optional capabilities and versioned folder templates are under **Advanced resource IDs, capabilities and folder templates**. These entries are local metadata until separately verified.
6. Saving the wizard draft records non-secret state with an optimistic revision. The separate administrator **Test selected site connection** button performs a read-only Graph check when privately configured; neither action creates folders or establishes external provider acceptance.
7. A separate authorized administrator applies local role grants through `RoleAdministrationService`; the initial bootstrap does not grant Microsoft tenant administration.

## Microsoft 365 integration

### Entra/OIDC

Use a production-only confidential web registration, exact HTTPS callback, approved tenant authority, and a secret/certificate reference from the deployment secret store. The web process validates the configured tenant, issuer, audience, state/nonce/code flow, and immutable `tid` + `oid` claims. It does not use email or display name as an identity key.

The web registration is for sign-in. Keep app-only document, records, and mail identities separate. A Microsoft administrator or consent callback is not evidence of an AuditSphere business role.

For the separate tenant-consent registration, two exact callbacks, nonce-bound administrator sign-in and per-capability checks, follow [Production Entra ID Configuration](auditsphere-m365-runbook-client-tenant-current.md#tenant-connection-consent-session). A verified Development consent session is not production acceptance or a blanket Graph grant.

### SharePoint/Graph

Select a reviewed site, document library/drive, and root folder. Persist stable tenant/site/drive/root IDs and purpose, not an arbitrary browser URL. The ordinary worker must use the approved selected-resource binding; do not add tenant-wide Graph permissions to make a missing grant work.

The setup page records a binding draft and offers an administrator-initiated, read-only test of the saved selected site, library and root when a separate runtime certificate is privately configured. A passing screen message does not record consent evidence or activate the workspace. Provider writes, direct staff ACLs, client workspace creation, uncertain-result reconciliation, and live negative tests require the approved provider composition and external acceptance.

### Records and signed-document evidence

Preserve the exact uploaded signed document, SHA-256 identity, human decision, and release/archive manifest. A local archive state or label reference is not proof that an external retention or signing provider accepted an action. The product does not require Purview or eSignature provider setup.

## Security and authorization model

Authentication and authorization are separate:

- Entra authentication establishes the tenant/object identity.
- `RoleAdministrationService` creates or revokes local application grants; it never creates Entra directory roles.
- Every command/query checks the actor's firm, client, and engagement scope before resolving business data or requesting provider bytes.
- The complete supported role catalogue and its exact authorization boundaries are maintained in the [current roles and privileges reference](../architecture/auditsphere-architecture-user-roles-and-privileges-current.md).
- Grants use an explicit `FIRM_WIDE`, `CLIENT`, or `ENGAGEMENT` scope with matching identifiers. An empty scope never means firm-wide.
- Grant changes increment session state, retain evidence, prevent self-elevation, and protect the last firm administrator.
- A client/engagement grant does not grant sibling clients, sibling engagements, shared permanent records, or direct SharePoint rights automatically.

The implementation is in [RoleAdministrationService](../../src/AuditSphereOps.Application/Security/RoleAdministrationService.cs), [AuthorizationDecision](../../src/AuditSphereOps.Application/Security/AuthorizationDecision.cs), and the API's [TrustedActorResolver](../../src/AuditSphereOps.Api/Authentication/TrustedActorResolver.cs).

## External-effects rule

`ExternalEffects:Enabled` is a deployment fence, not a UI switch. In the current web host, setting it to `true` validates a positive deployment epoch and OIDC configuration and then intentionally refuses startup because the approved live provider composition is not registered. Do not remove that guard or enable it to test a simulation.

The worker permits the narrow live groups in Acceptance. Its Production code path permits a live group only when that group is individually listed in `Worker:ApprovedLiveGroups`, external effects are enabled, and simulation adapters are disabled; provider credential and resource checks still apply. Core `general`, `processing`, and `records` groups run with external effects disabled. Production must have `Application:AllowSimulationAdapters=false` in the web host and `AllowSimulationAdapters=false` in the worker, with no demo identity. These code paths do not establish owner approval, deployed configuration, or external acceptance.

## Completion evidence

Keep three kinds of evidence separate:

1. Local build/test/migration/restore evidence.
2. Tenant-admin and provider observations for the exact revision and identity.
3. Professional, records, signing, recovery, and independent-review approvals.

Update the repository execution ledger only with observed facts. A Wiki update documents how to deploy; it does not clear an external gate.
