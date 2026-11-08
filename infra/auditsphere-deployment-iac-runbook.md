# AuditSphere Infrastructure as Code

For customer Microsoft identities, consent, selected-site grants and email setup, use the [client-tenant CLI runbook](../docs/operations/auditsphere-operations-runbook-client-tenant-microsoft365-current.md) and [configuration inventory](../docs/operations/auditsphere-operations-reference-microsoft365-configuration-current.md). Current Azure templates do not wire the complete M365 capability certificates/provider settings or mandatory Production installation/Data Protection credentials. Acceptance lists external worker groups but does not supply all required enablement and credentials; template compilation does not prove those workers or the Production API start. Production live M365 worker composition remains a separate engineering/release prerequisite.

This directory is the source-controlled definition of the AuditSphere production topology.
Production must be reproducible from these files plus approved secret references; it must not
depend on an operator remembering portal steps. **Status:** every template compiles with
`az bicep build` (verified locally); no template has been deployed against a live subscription —
live deployment remains `BLOCKED_EXTERNAL` until the owner approves and provisions the Azure
plumbing listed below.

## Technology choice

Azure **Bicep** with **Azure Container Apps**: platform-native constructs (no self-managed
Kubernetes), one declarative unit per logical role, health probes, revision-based canary and
rollback, managed identities with secrets by reference, and PostgreSQL Flexible Server with
PITR. The modular monolith stays a modular monolith; multiple deployable roles do not turn it
into microservices.

## Topology

```text
Users ──> Container Apps ingress (TLS)
             │
             ├── auditsphere-prod-api          (Angular assets + ASP.NET Core API)
             │      liveness  /health/live
             │      readiness /health/ready    (database + migration-state checks)
             │
             ├── auditsphere-prod-worker-general     ┐
             ├── auditsphere-prod-worker-processing  │ durable operations,
             ├── auditsphere-prod-worker-records     ┘ one image, config-decided roles
             │
             └── migrations job                (EF bundle; the only migration executor)

PostgreSQL Flexible Server (private subnet, TLS, PITR, zone-redundant)
Key Vault (secrets by reference, purge protection, RBAC)
Log Analytics (platform logs) + Telemetry:Otlp:Endpoint (application OTLP)
Azure Files shares: ui-current / ui-previous (approved Angular asset sets)
```

The external-effect workers (`mail`, `pbc`, `client-sites`) exist only in the **Acceptance**
environment definition and receive only their own configuration; the API never receives their
credentials. `DevelopmentIdentity`, simulation adapters and external effects are hard-disabled
in deployed configuration; the application's own fail-closed startup guards remain the second
line of defense.

## Layout

```text
infra/
  modules/                      one file per platform concern
    stack.bicep                 the composed environment (network, KV, PG, env, api, workers, job)
    containerapp-api.bicep      API deployment unit with probes and asset volume mounts
    containerapp-worker.bicep   parameterized worker role (group + deployment epoch)
    migration-job.bicep         manual-trigger Container Apps Job running the EF bundle
    postgres.bicep              standalone module (the stack composes it inline)
    containerapp-environment.bicep / observability.bicep   standalone modules
  docker/migrations.Dockerfile  migration bundle image (runtime images live next to the projects)
  environments/
    acceptance/main.bicep       includes mail/pbc/client-sites worker groups
    production/main.bicep       core workers only
```

Runtime images: `src/AuditSphereOps.Api/Dockerfile` (multi-stage: Node build → locked restore →
publish → non-root runtime with the Angular asset set), `src/AuditSphereOps.Worker/Dockerfile`
(one image for every worker group; `Worker:Group` decides the role), and
`infra/docker/migrations.Dockerfile` (self-contained EF migration bundle).

## Release process (also encoded in `.github/workflows/release.yml`)

0. **Verify fast CI.** `verify-ci-passed` requires a successful fast run for the exact immutable
   release SHA. No candidate images build unless this read-only precondition passes.
1. **Overlap full-evidence verification with candidate builds.** Once fast CI passes,
   `verify-release-source` checks the retained full-regression evidence for the exact SHA while
   three independent jobs build the API, Worker and migration images for `linux/amd64` from that
   same checkout. Buildx uses an isolated GitHub Actions cache scope per image. Each job uploads a
   SHA-256 checked archive and manifest bound to repository, workflow run/attempt, source SHA,
   Dockerfile and image tag. These jobs receive no Azure OIDC token and write no ACR tags. If full
   evidence is absent or invalid, the release job remains blocked and the uncredentialed candidate
   archives are unusable and expire. Cache export is best-effort; cache misses rebuild from the
   Dockerfiles, while cache-import errors remain visible.
2. **Approve the protected environment.** The release job starts only after full evidence passes,
   all three image builds succeed and the configured reviewer approves. It downloads the three
   exact-run archives in
   parallel into separate artifact-name directories, then verifies the exact directory set,
   manifests and archive hashes before Azure login. OIDC remains limited to this protected job; it
   publishes the already-built SHA tags to ACR and records each registry digest. If an archive is
   absent, unexpected, stale, mismatched or corrupt, release stops before Azure authentication or
   registry writes.
3. **Run the migration job.** If it fails or hangs, the new revision never receives traffic.
   Migrations are forward-only; application rollback is never coupled to a database rollback.
4. **Deploy** API + worker revisions via one `az deployment group create` against
   `infra/environments/<env>/main.bicep`, passing the **deployment epoch** as an explicit
   parameter (the release pipeline assigns `github.run_number`, which is positive and strictly
   increasing — satisfying the durable-operation fencing rule with no manual ambiguity). The
   template submits the API and worker modules in the same deployment without an app-level
   dependency between them, allowing ARM to orchestrate those independent updates together; the
   migration job remains a blocking precondition and the canary remains after deployment.
5. **Canary**: `az containerapp ingress traffic update --revision-weight <new>=10`, then smoke
   checks — liveness, readiness, and a read-only Angular route. Canary validation never uses
   destructive business commands.
6. **Promote** (`--revision-weight <new>=100`) or **roll back**
   (`--revision-weight <previous>=100`). Worker rollback is redeploying the previous image tag.
7. **Angular asset transition**: the release pipeline uploads the new browser bundle to the
   `ui-current` share and moves the previous approved bundle to `ui-previous`. Existing tabs
   keep loading fingerprinted chunks from `AngularUi:PreviousBuildPath` during the transition
   window; old HTML shells are never served.

## Required owner plumbing before first live release (BLOCKED_EXTERNAL)

- Azure subscription + resource group, and an ACR accepted by the owner.
- Entra application for GitHub OIDC federation (Contributor on the resource group). No secrets
  are stored in GitHub.
- Key Vault secrets created through the approved secret process:
  `auditsphere-connection-string`, `identity-client-secret`, `postgres-admin-password`.
- The PostgreSQL server, databases and the two UI file shares are created by the stack; the
  `auditsphere` database schema itself is created by the migration bundle on first run.
- GitHub environment `production` protected with required reviewer approval.

## Verification honesty

| Property | Status |
| --- | --- |
| Templates compile (`az bicep build`) | Verified locally, all modules + both environments |
| Parallel image archive workflow | Implemented in `.github/workflows/release.yml`; requires exact-source CI and a protected-environment workflow run before its wall-time benefit is measured |
| E2E host build reuse | Candidate in `.github/workflows/release-verification.yml` reuses same-run Release API/Worker outputs through the existing artifact-root launcher; exact-source full CI and timing are still pending |
| Live deployment, canary, rollback | Not exercised; requires the owner plumbing above |
| Health probes wiring | `/health/live` and `/health/ready` are the application's existing endpoints; probe configuration compiles but is not yet observed in Azure |
