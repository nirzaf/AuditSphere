# AuditSphere production deployment

This Wiki is the operational entry point for deploying AuditSphereOps, a .NET 10 / ASP.NET Core API + Angular UI (with a legacy Blazor rollback host) / EF Core / PostgreSQL modular monolith with Microsoft Entra ID and selected-resource SharePoint/Microsoft Graph integration.

This Wiki is the primary source for deployment-guidance documentation. Update these pages first; Google Docs and other copied guides are non-authoritative convenience copies and may be stale.

## Pages

- [Development Tenant and Local Verification](auditsphere-m365-development-verification-historical.md) — non-production Entra sign-in, scoped synthetic fixtures, and local capacity evidence
- [Production Configuration Guide](auditsphere-deployment-guide-production-current.md)
- [Production Environment Settings](auditsphere-m365-reference-production-environment-current.md)
- [Production Entra ID Configuration](auditsphere-m365-runbook-client-tenant-current.md)
- [Production SharePoint Configuration](auditsphere-m365-sharepoint-runbook-current.md)
- [Production Readiness Checklist](auditsphere-deployment-readiness-checklist-current.md)

## Safety boundary

These pages explain configuration. They are not evidence that a tenant is production-ready and do not replace owner, compliance, signing, recovery, or independent-review approval.

Never put passwords, client secrets, certificates, private keys, access tokens, cookies, bootstrap proofs, or connection-string values in this Wiki, Git, issues, or logs. Keep `ExternalEffects__Enabled=false` until the approved live provider composition exists and the applicable external gates have observed evidence.

The authoritative repository contract is the [current requirements](https://github.com/nirzaf/AuditSphere/blob/master/docs/auditsphere-accounting-module-requirements-current.md) and the [implementation architecture](https://github.com/nirzaf/AuditSphere/blob/master/docs/architecture/auditsphere-architecture-current-architecture.md); current implementation and open gates are recorded in the [execution ledger](https://github.com/nirzaf/AuditSphere/blob/13f31321d9abc270703a3d6ac74a2ffe30b2efc3/docs/execution/status.json). Purview and eSignature provider integrations are outside the approved product scope.

## Scope baseline

The repository owner approved the seven-module STE-R2R-ARCH-001 scope baseline, accepting the reviewed current-code inventory and the architectural direction. This accepts the scope baseline only: it does not accept the remaining R2R tasks, the transaction-owner/shared-contract/professional-role handoffs, or any live tenant or production capability. The exact reviewed commits, decisions, and open gates are volatile project state recorded in the [execution ledger](https://github.com/nirzaf/AuditSphere/blob/master/docs/execution/status.json) and the [documentation index](https://github.com/nirzaf/AuditSphere/blob/master/docs/auditsphere-docs-index.md); this page does not duplicate them.
