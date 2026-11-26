# Customer SharePoint and Microsoft Graph setup

Follow the [client-tenant CLI procedure](auditsphere-m365-runbook-client-tenant-current.md) in order. Exact commands and configuration are maintained there and in the [environment reference](auditsphere-m365-reference-production-environment-current.md).

## Resource grant and verification

1. Review the customer tenant, exact working site/library/root and separate synthetic unrelated control site.
2. Configure the distinct document app with Graph Sites.Selected and its own certificate.
3. Apply only the approved exact-site write grant through the [customer-operated grantor procedure](auditsphere-operations-runbook-client-tenant-microsoft365-current.md#8-resolve-and-grant-the-selected-sharepoint-site). Admin consent alone is insufficient. Never grant the control site.
4. Remove the temporary grantor and its delegated consent after retaining the exact permission receipt.
5. Complete AuditSphere's nonce-bound consent, reviewed resource draft and [trusted resource verification](auditsphere-operations-runbook-client-tenant-microsoft365-current.md#9-verify-selected-resource-access). Require positive site/drive/root reads and unrelated-site HTTP 403 under the runtime identity.
6. If separately authorized, upload/read/delete one uniquely named disposable file in the approved empty synthetic root; verify cleanup.
7. Review/approve the exact folder template and activate the matching verified revision through the Application workflow.

## Dedicated sites per client

[Dedicated client-site provisioning](auditsphere-operations-runbook-client-tenant-microsoft365-current.md#10-configure-dedicated-client-site-provisioning) is enabled by default behind an explicit fixed UTC rollout boundary. One site belongs to one client; annual engagements reuse its approved folder structure. Historical clients are not automatically migrated. Missing/conflicting rollout configuration blocks provisioning and cannot silently fall back to a shared site for rollout clients.

The isolated client-sites provisioner has a separately approved Graph and SharePoint Sites.FullControl.All exception. The ordinary document identity retains Sites.Selected. Current source already uses six narrow read-only API capability mounts, excluding the parent and privileged key. Before staging the privileged key, prepare missing protected directories, recreate only the approved API and verify actual process visibility. The [credential-isolation Compose override](../../infra/hostinger-acceptance/m365-isolation.override.example.yaml) remains available for older source revisions.

Assigned audit staff have Full Control over the entire client working site under [ADR-0006](https://github.com/nirzaf/AuditSphere/blob/master/docs/architecture/adr/auditsphere-architecture-adr-0006-client-sharepoint-site-staff-full-control-approved.md), including sibling engagements, sharing and deletion. This is external SharePoint authority, not a local application role. Managed-group reconciliation is asynchronous; direct grants/sharing links require separate administrator review.

## Evidence, records and recovery

Preserve exact source bytes, SHA-256 identities, provider item/version identities, approved bindings and immutable local receipts. An eTag/cTag is not a SHA-256, human decision or business revision. Keep restricted compliance bindings independently authorized; do not assert one working app or broad staff group is appropriate for every repository purpose.

The authoritative issued release/archive store is separate from mutable working SharePoint sites. The selected read-only secondary archive needs independently verified copy hashes, exact grants, readback and staff denial. A folder name, local freeze or staff Full Control cannot prove external immutability. Purview/eSignature providers are outside product scope. Follow [ADR-0009](https://github.com/nirzaf/AuditSphere/blob/master/docs/architecture/adr/auditsphere-architecture-adr-0009-archive-authority-release-store-approved.md).

Required live evidence includes exact resource access, forbidden unrelated-site access, correct item/version byte readback, uncertain-outcome reconciliation without duplicates, staff membership addition/removal, direct-access ACL review and approved cross-store restore/recovery fencing. Record named owners, UTC times, provider request IDs, release SHA and current configuration revision without secrets or customer document content.

Worker code supports the narrow Acceptance live groups and individually allowlisted Production live groups with simulation disabled. Provider credentials and exact-resource bindings, Production target configuration, owner approval, deployment, and external acceptance remain separate gates; this documentation does not accept a production environment.
