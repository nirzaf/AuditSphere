# AuditSphere Infrastructure as Code

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

1. **Build immutable artifacts** tagged with the Git commit SHA; production runs a known
   artifact, never a rebuild. Test suites run before release in the main CI gate.
2. **Run the migration job.** If it fails or hangs, the new revision never receives traffic.
   Migrations are forward-only; application rollback is never coupled to a database rollback.
3. **Deploy** API + worker revisions via `infra/environments/<env>/main.bicep`, passing the
   **deployment epoch** as an explicit parameter (the release pipeline assigns
   `github.run_number`, which is positive and strictly increasing — satisfying the
   durable-operation fencing rule with no manual ambiguity).
4. **Canary**: `az containerapp ingress traffic update --revision-weight <new>=10`, then smoke
   checks — liveness, readiness, and a read-only Angular route. Canary validation never uses
   destructive business commands.
5. **Promote** (`--revision-weight <new>=100`) or **roll back**
   (`--revision-weight <previous>=100`). Worker rollback is redeploying the previous image tag.
6. **Angular asset transition**: the release pipeline uploads the new browser bundle to the
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
| Runtime images build | Dockerfiles written to mirror the CI build gate; not built locally (Docker daemon unavailable in the working environment) |
| Live deployment, canary, rollback | Not exercised; requires the owner plumbing above |
| Health probes wiring | `/health/live` and `/health/ready` are the application's existing endpoints; probe configuration compiles but is not yet observed in Azure |
