# AuditSphere production deployment

This Wiki is the operational entry point for the self-hosted AuditSphereOps deployment: a .NET 11 / C# 15 / ASP.NET Core 11 API serving the canonical Angular UI, with EF Core 10.0.12 and Npgsql 10.0.0. The Hostinger GBS beta was read back on 2026-11-24 at source `fb0697b1f188d7d41c3ff16acc78ed245d53ebd1` on PostgreSQL 19.0; the separate retained-data environment was last read-only observed on PostgreSQL 18.6 on 2026-11-23. Current `master` source `ba6c2fb5083b5bb020b88ffe817cb2298996c5a5` has passed its exact CI/images/full-verification runs but is not recorded as deployed. See the [Hostinger acceptance record](../../infra/hostinger-acceptance/auditsphere-deployment-hostinger-acceptance-current.md) and the [PostgreSQL major-upgrade runbook](auditsphere-operations-runbook-postgresql-major-upgrade.md); changing an image tag is not a database migration.

**Hosting boundary:** use the existing self-hosted deployment. Microsoft Azure is limited to Microsoft Entra ID and the existing SharePoint Online integration. The retained Azure hosting/database templates are not an approved deployment path under this boundary; do not provision or deploy them. This statement does not assert that existing Azure resources have been inventoried or removed.

This Wiki is the primary source for deployment-guidance documentation. Update these pages first; Google Docs and other copied guides are non-authoritative convenience copies and may be stale.

## Pages

- [Development Tenant and Local Verification](auditsphere-m365-development-verification-historical.md) — non-production Entra sign-in, scoped synthetic fixtures, and local capacity evidence
- [Production Configuration Guide](auditsphere-deployment-guide-production-current.md)
- [Production Environment Settings](auditsphere-m365-reference-production-environment-current.md)
- [Production Entra ID Configuration](auditsphere-m365-runbook-client-tenant-current.md)
- [Production SharePoint Configuration](auditsphere-m365-sharepoint-runbook-current.md)
- [Production Readiness Checklist](auditsphere-deployment-readiness-checklist-current.md)
- [PostgreSQL 18-to-19 Upgrade](auditsphere-operations-runbook-postgresql-major-upgrade.md) — current Hostinger target, compatibility practices, and cutover gates

## Safety boundary

These pages explain configuration. They are not evidence that a tenant is production-ready and do not replace owner, compliance, signing, recovery, or independent-review approval.

Never put passwords, client secrets, certificates, private keys, access tokens, cookies, bootstrap proofs, or connection-string values in this Wiki, Git, issues, or logs. Keep `ExternalEffects__Enabled=false` until the approved live provider composition exists and the applicable external gates have observed evidence.

The authoritative repository contract is the [current requirements](../requirements/auditsphere-accounting-module-requirements-current.md) and the [implementation architecture](../architecture/auditsphere-architecture-current-architecture.md); current implementation and open gates are recorded in the [execution ledger](../execution/status.json). Purview and eSignature provider integrations are outside the approved product scope.

## Scope baseline

The repository owner approved the seven-module STE-R2R-ARCH-001 scope baseline, accepting the reviewed current-code inventory and the architectural direction. This accepts the scope baseline only: it does not accept the remaining R2R tasks, the transaction-owner/shared-contract/professional-role handoffs, or any live tenant or production capability. The exact reviewed commits, decisions, and open gates are volatile project state recorded in the [execution ledger](https://github.com/nirzaf/AuditSphere/blob/master/docs/execution/status.json) and the [documentation index](https://github.com/nirzaf/AuditSphere/blob/master/docs/auditsphere-docs-index.md); this page does not duplicate them.
