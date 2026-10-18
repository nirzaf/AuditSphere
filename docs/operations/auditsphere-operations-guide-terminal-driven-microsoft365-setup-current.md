# Terminal-driven Microsoft 365 setup for AuditSphere

**Status: CURRENT operator overview.** This guide explains which parts of an AuditSphere customer-tenant setup can be scripted and where a tenant administrator or browser callback remains part of the supported flow. It is an orientation and decision guide; the detailed commands and change sequence remain in the [client-tenant Microsoft 365 CLI runbook](auditsphere-operations-runbook-client-tenant-microsoft365-current.md).

## Short answer

Most Microsoft 365 configuration that AuditSphere supports can be prepared, inspected, and applied from a terminal using Azure CLI, Microsoft Graph PowerShell/API, Exchange Online PowerShell, approved SharePoint APIs, and host-side Bash/Docker Compose. DNS can be scripted when the customer's DNS provider exposes an appropriate API or CLI.

This does not make onboarding fully unattended. An authorized customer administrator must authenticate and satisfy MFA, Conditional Access, and other tenant policies. The required administrator must approve privileged application consent and resource grants. AuditSphere also uses its own authenticated setup and nonce-bound consent callbacks; a direct database update or a successful CLI permission write does not replace those flows or prove runtime access.

“Terminal-driven” therefore means the operator can automate the supported configuration while an authorized person completes Microsoft authentication, approval, and AuditSphere callback steps. It does not mean that a script can create its own authority or configure every Microsoft 365 workload and tenant policy.

## What can be scripted

| Area | Terminal-driven work | Required authority or boundary |
| --- | --- | --- |
| Entra applications | Create or inspect app registrations and service principals; set reviewed redirect URIs and authentication settings. | Tenant application-management permission; preserve existing reviewed callbacks. |
| Graph permissions | Request and inspect the exact application roles assigned to each capability identity; grant consent through supported Graph/PowerShell operations. | A suitably privileged administrator must sign in and authorize the requested permissions. Consent is tenant-wide for the app role and is a sensitive operation. |
| Certificates and host settings | Generate a separate key pair for each approved capability, upload only public certificates, configure protected host-side secret references and Compose settings. | Keep private keys in the approved secret/certificate store. Never print, commit, or place them in ordinary application settings. |
| Selected SharePoint access | Resolve exact site/library IDs, apply the separately approved site grant, and run positive and unrelated-site denial checks. | The runtime document app needs `Sites.Selected` plus an exact site grant. Creating that grant requires a separate authorized grantor with sufficient SharePoint authority; app consent alone does not grant site access. |
| Dedicated client sites | Configure the isolated `client-sites` worker and its approved identity, then let the existing provider create/reconcile sites idempotently for clients after the explicit rollout cutoff. | This is a separate privileged capability and credential boundary. Do not manually create parallel client sites or share its credentials with the API/document worker. |
| Exchange Online | Inspect mailbox scope and configure the currently supported sender restriction using Exchange Online PowerShell. | Exchange administrator authority and an approved sender/test recipient. The current application verification path must support the selected authorization model before enabling mail. |
| DNS | Publish reviewed MX, SPF, DKIM, or DMARC records through the provider's API/CLI and verify public DNS. | Provider support varies. Domain ownership and mail routing are separate checks. |
| AuditSphere deployment | Set protected environment references, deploy reviewed images, apply migrations under the release procedure, and start only approved worker profiles. | Deployment approval, backup/rehearsal requirements, and worker-effect review still apply. |

See the [approved Microsoft 365 permission matrix](../architecture/auditsphere-m365-tenant-administration-permissions.md) for the exact capability identities, roles, and default-disabled features. Microsoft documents programmatic admin consent, but the operation still requires an authorized administrator and careful review of its scope ([admin consent](https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/grant-admin-consent)). For SharePoint, `Sites.Selected` is paired with a resource-specific grant; managing a site-level permission requires strong authority over that site ([selected permissions](https://learn.microsoft.com/en-us/graph/permissions-selected-overview)).

## AuditSphere-specific constraints

### Keep capability identities separate

The application uses distinct identities and credentials for sign-in, directory reading, selected-site document access, outbound mail, optional user provisioning, guest invitations, group membership, and dedicated client-site provisioning. Optional tenant-mutation capabilities remain disabled unless separately approved, configured, and verified. Microsoft group membership does not grant AuditSphere application roles.

The dedicated `client-sites` identity is a special provisioning exception with its own privileged Graph/SharePoint authority. It must remain isolated in the existing worker and cannot be treated as the selected-site document identity. Follow [ADR-0006](../architecture/adr/auditsphere-architecture-adr-0006-client-sharepoint-site-staff-full-control-approved.md) and the [client-site contract](../architecture/auditsphere-client-sharepoint-sites-current.md). The explicit UTC rollout boundary protects existing clients; never backdate it or silently route eligible clients to a shared site.

### CLI permission state is not AuditSphere verification

After upstream permissions are configured, complete the supported AuditSphere tenant connection and nonce-bound identity callback. Then run capability verification and inspect the persisted tenant-specific result in **Administration → Microsoft 365**. Do not set a verification row directly in the database. A consent receipt, token, successful build, or HTTP readiness response is not proof that the exact runtime identity can access the approved resource.

For SharePoint, verify the approved working site and an unrelated same-tenant control site. For mail, verify the approved sender and a controlled denied sender before starting the mail worker. Keep unknown provider outcomes pending until reconciled; do not retry a potentially successful external mutation blindly.

### Exchange mail authorization is implementation-sensitive

Microsoft recommends Exchange Online Application RBAC for new application access. Exchange RBAC grants and Entra application grants are additive, so a scoped Exchange assignment does not constrain a separate tenant-wide Entra `Mail.Send` grant. AuditSphere's current capability verifier expects the Graph `Mail.Send` application role; the operator runbook documents the currently supported compatibility path and treats an RBAC-only migration as requiring an engineering change and live positive/negative verification. Do not enable an RBAC-only configuration based solely on Exchange scope tests. See the [Exchange Application RBAC guidance](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac) and [mail section of the operator runbook](auditsphere-operations-runbook-client-tenant-microsoft365-current.md#11-configure-exchange-online-mail).

## Suggested terminal-first workflow

1. **Plan:** Record the exact tenant, public host, app identities, approved site/library, unrelated control resource, sender, test recipient, DNS provider, deployment SHA, and operator authority. Inspect existing registrations and grants before proposing changes.
2. **Review:** Produce a change list grouped by capability. Obtain explicit approval for each app role, exact SharePoint site grant, Exchange scope, optional directory mutation, worker activation, and deployment change.
3. **Apply:** Use the CLI runbook to create or update only the reviewed resources. Generate credentials in the approved secret store and upload only public certificates. Do not make broad permission grants to avoid a blocked setup step.
4. **Authenticate and consent:** An authorized administrator completes device/browser sign-in, MFA, policy prompts, and required consent. For example, `az login --tenant <tenant-guid> --allow-no-subscriptions --use-device-code` begins an Azure CLI session; the person still completes the displayed authentication flow. Exchange Online PowerShell also supports device sign-in through `Connect-ExchangeOnline -Device` ([Microsoft instructions](https://learn.microsoft.com/en-us/powershell/module/exchangepowershell/connect-exchangeonline)).
5. **Verify in AuditSphere:** Complete initial administrator binding and the tenant-consent callbacks, then verify each enabled capability against the exact tenant and resource. Preserve positive and negative evidence with timestamps and sanitized request IDs.
6. **Activate in stages:** Review queued work and start only the worker profiles covered by current verification and approval. Verify health and persisted capability state after deployment. Record live provider acceptance separately from configuration success.
7. **Clean up:** Retire temporary grantors and CLI token caches when approved, disconnect administrative sessions, and retain cleanup receipts without storing tokens or private keys in the repository.

## Safe inspection examples

These examples inspect tenant state; they do not grant permissions or modify applications.

```bash
az account show --query '{tenant:tenantId,user:user.name}' --output json
az ad app list --display-name 'AuditSphere' --all \
  --query '[].{name:displayName,appId:appId,objectId:id}' --output table
```

```powershell
Connect-MgGraph -TenantId '<tenant-guid>' -Scopes 'Application.Read.All'
Get-MgContext
Get-MgApplication -All |
  Where-Object DisplayName -Like '*AuditSphere*' |
  Select-Object DisplayName, AppId, Id
Disconnect-MgGraph
```

Use the full runbook for write commands. Before a write, confirm the target tenant and object IDs, the approved change, the minimum required administrator role, and the expected before/after state. Do not copy secrets, device codes, access tokens, or private key material into tickets, chat, shell history, or output logs.

## What “complete” means

Terminal configuration can be complete while customer acceptance remains pending. Record these separately:

- **Configured:** The intended app roles, resource grants, certificates, DNS, mailbox scope, and host settings exist and match the reviewed plan.
- **Verified:** AuditSphere's live verifier confirms each enabled capability for the exact tenant/resource; positive and denial probes behave as expected.
- **Accepted:** An authorized customer operator and required reviewers accept the deployment, operational controls, recovery arrangements, and any professional or legal decisions.

Never mark live M365 acceptance from simulated tests, CLI configuration output, or documentation review alone. Keep unavailable tenant/provider actions as `BLOCKED_EXTERNAL` and incomplete owner decisions as pending.

## Related operator references

- [Detailed client-tenant Microsoft 365 CLI runbook](auditsphere-operations-runbook-client-tenant-microsoft365-current.md)
- [Microsoft 365 configuration and environment reference](auditsphere-operations-reference-microsoft365-configuration-current.md)
- [Approved M365 tenant permission matrix](../architecture/auditsphere-m365-tenant-administration-permissions.md)
- [Dedicated SharePoint client-site contract](../architecture/auditsphere-client-sharepoint-sites-current.md)
- [Microsoft Graph PowerShell installation](https://learn.microsoft.com/en-us/powershell/microsoftgraph/installation)
- [Azure CLI installation](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)
