# Production Readiness Checklist

Use this checklist in order. Mark an item complete only with observed evidence for the exact release, deployment epoch, identity, resource binding, and configuration revision. Local tests and screenshots of configuration are not live provider acceptance.

## 1. Source and deployment

- [ ] Reviewed the current `AGENTS.md`, `docs/requirements/auditsphere-accounting-module-requirements-current.md`, `docs/architecture/auditsphere-architecture-current-architecture.md`, execution ledger, and release diff.
- [ ] Confirmed the exact commit SHA, build artifact, environment, hostname, and deployment epoch.
- [ ] Confirmed no credentials, tokens, bootstrap proof, connection strings, or tenant secrets are in Git, Wiki, logs, or support exports.
- [ ] Confirmed the production secret/configuration references are injected outside the repository.
- [ ] Confirmed `DevelopmentIdentity__Enabled` is absent/false and OIDC is configured for the approved tenant.
- [ ] Confirmed `Application__AllowSimulationAdapters=false` and worker `AllowSimulationAdapters=false`.

## 2. PostgreSQL and migrations

- [ ] PostgreSQL backup and restore owner are named.
- [ ] Connection uses the approved production database and TLS/credential policy.
- [ ] Review migration SQL before application; do not allow startup to silently apply destructive DDL.
- [ ] Apply migrations with the controlled deployment command:

  ```bash
  dotnet ef database update \
    --project src/AuditSphereOps.Infrastructure \
    --startup-project src/AuditSphereOps.Api
  ```

  Use the primary Api host as the startup project (the legacy `AuditSphereOps.Web` rollback host also works).

- [ ] Run `dotnet ef migrations has-pending-model-changes` against the intended source/configuration.
- [ ] Verify schema version, migration history, database ownership, indexes, and backup coverage.
- [ ] Perform a restore rehearsal and confirm the restored deployment remains fenced from outward effects until a new deployment/recovery epoch is approved.

## 3. Health probes and application startup

- [ ] `GET /health/live` returns healthy for process liveness only.
- [ ] `GET /health/ready` returns healthy only when PostgreSQL and the migration readiness check pass.
- [ ] A database outage makes readiness fail without claiming liveness failure.
- [ ] Startup fails closed for invalid OIDC, simulation-in-production, missing worker firm/epoch, invalid release safety, or unsupported external composition.
- [ ] HTTPS redirect, HSTS, antiforgery, authentication, authorization, and error handling are enabled for the deployed environment.
- [ ] Public base URL and callback URL match the actual HTTPS hostname; no local URL is exposed in production links.

## 4. Entra identity and roles

- [ ] Exact OIDC redirect URI and logout behavior are tested.
- [ ] Correct-tenant identity succeeds and wrong-tenant identity is rejected.
- [ ] Local identity is bound by `(tid, oid)` rather than email/UPN.
- [ ] One-time bootstrap proof is hashed, short-lived, single-use, and not in browser storage or logs.
- [ ] Existing staff are explicitly assigned local roles/scopes; Entra membership alone grants nothing.
- [ ] Role revocation denies the next protected operation and queued work rechecks access.
- [ ] Last firm administrator protection and maker/checker self-elevation tests pass.

## 5. SharePoint/Graph

- [ ] Working, restricted, and records repositories have approved owners and separate bindings where required.
- [ ] Stable tenant/site/drive/root IDs are recorded; browser paths are not ownership keys.
- [ ] Runtime app identity has only the approved selected-resource grants.
- [ ] Positive upload/read/version/reconciliation tests pass with the exact runtime identity.
- [ ] Negative site, client, engagement, guessed-ID, and wrong-tenant tests fail closed.
- [ ] Retry after timeout reconciles the same remote item and never creates a renamed duplicate.
- [ ] Direct SharePoint access is disabled or independently ACL-tested before exposing links.

## 6. Records and signed-document evidence

- [ ] The records owner and approved archive/release custody policy are recorded.
- [ ] Exact uploaded signed-document bytes, SHA-256 identities, human decisions, and release/archive manifests are preserved.
- [ ] The exact approved archive item is read back from the selected records repository; local state alone is not external custody proof.
- [ ] Delete, rename, disposition, and legal-hold behavior is tested where required by the approved repository policy.
- [ ] Records-worker/admin authority is separate from routine document access.

## 7. Outbox and worker

- [ ] Worker uses the intended `Worker__FirmId`, positive `Worker__DeploymentEpoch`, group, lease, renewal, and retry settings.
- [ ] Web process records durable intents but does not execute provider effects.
- [ ] Worker composition is the approved environment; current code permits live effects only in the isolated Acceptance live-mail composition.
- [ ] `429`, `403`, credential failure, timeout, uncertain result, and recovery-fence behavior are observed and safe.
- [ ] Mail provider is one approved Graph/SMTP/Resend path with secret references, sender authorization, and delivery semantics documented.
- [ ] Outbox backlog, failed operations, leases, retries, and reconciliation cases are monitored.

## 8. Backup, recovery, and gates

- [ ] Database backup is encrypted, tested, and custodially separate from the application host.
- [ ] SharePoint and checkpoint recovery owners and RPO/RTO targets are approved and measured.
- [ ] Restored database cannot reactivate external effects using stale flags or epoch values.
- [ ] Signed-document evidence, records custody, external checkpoint, release, and independent-review gates remain separate.
- [ ] Production activation has named owner, approval reference, exact release SHA, evidence references, and rollback/recovery plan.

## 9. Final decision

Do not mark production ready when any required item is merely configured, simulated, inferred from a screenshot, or pending an external administrator. Record `BLOCKED_EXTERNAL` with the exact owner/action when a live tenant, compliance, recovery, or independent-review prerequisite is absent.
