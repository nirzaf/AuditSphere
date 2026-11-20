# AuditSphere operations index

**Status: CURRENT · Navigation only**

| Operator need | Procedure or reference |
|---|---|
| PostgreSQL 18-to-19 major-version upgrade | [Major-version upgrade runbook](auditsphere-operations-runbook-postgresql-major-upgrade.md) |
| Local PostgreSQL restore rehearsal | [Restore drill](auditsphere-operations-runbook-restore-drill.md) |
| Client Microsoft 365 onboarding | [Terminal-driven setup guide](auditsphere-operations-guide-terminal-driven-microsoft365-setup-current.md) · [client-tenant runbook](auditsphere-operations-runbook-client-tenant-microsoft365-current.md) · [configuration reference](auditsphere-operations-reference-microsoft365-configuration-current.md) |
| Production deployment | [Deployment index](auditsphere-deployment-index-current.md) · [production guide](auditsphere-deployment-guide-production-current.md) · [environment reference](auditsphere-m365-reference-production-environment-current.md) · [readiness checklist](auditsphere-deployment-readiness-checklist-current.md) |
| M365 wiki-derived procedures | [Client-tenant runbook](auditsphere-m365-runbook-client-tenant-current.md) · [SharePoint runbook](auditsphere-m365-sharepoint-runbook-current.md) · [historical local verification](auditsphere-m365-development-verification-historical.md) · [historical Purview record](auditsphere-m365-purview-configuration-historical.md) |
| Entra registration automation | [Registration automation](auditsphere-entra-registration-automation.md) |
| Workflow reminders and template evidence | [Reminders](auditsphere-operations-guide-workflow-reminders-current.md) · [template evidence](auditsphere-operations-guide-workflow-template-evidence-current.md) |
| Runtime signals | [Telemetry guide](auditsphere-operations-guide-telemetry.md) |
| Database connection capacity | [Capacity decision](auditsphere-operations-decision-connection-capacity.md) |

The release verification npm scan retains both direct advisories and dependency-derived findings in the [operational evidence record](../spec/completion-operational-evidence.json). Dependency findings must link to a reported advisory through the dependency chain. The npm report does not authenticate its advisory feed, so capturing findings leaves that acceptance check open.

The redacted Git history scan retains all scanner rows and records one finding per Gitleaks fingerprint when repeated rows differ only by column span. Conflicting context for a fingerprint leaves the report unverified.

These procedures describe supported technical operations. They do not authorize production changes, broad tenant permissions, external effects, or customer acceptance. Follow `AGENTS.md` and the applicable approved permission boundary; record live evidence separately from local rehearsal evidence.
