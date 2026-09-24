# E2E automation strategy and CI blueprint

## 1. Scope, evidence, and safety boundary

This document records the automation strategy and the current CI contract. As of 2026-09-24, the repository contains dedicated API and Playwright E2E projects and an active CI workflow. Fresh Release discovery reconciles 319 cases: Domain 259, API 6, E2E 54. The complete PostgreSQL-backed E2E project passed 54/54 with 0 skips, including the cross-engagement accounting-mapping route case. The ReviewPoint revocation browser check handles its button being removed by the expected denial response and confirms persisted state is unchanged. EF reports no pending model changes, and actionlint v1.7.7 validates the active workflow. Hosted run `35949494319` targets pushed source `eeee4a4c` with 318 cases and was still in progress when this source was prepared; it does not include the new mapping-route test. Live Microsoft tenant/production acceptance remains separate. Earlier checkpoints also recorded intermittent browser-test failures; the proposal-authorship checkpoint adds PostgreSQL-backed preparer/checker and prior-schema migration-preservation cases plus author readback. The historical [test catalog](TEST_CASE_CATALOG.md) inventories its original 246 Domain cases; newer scenarios are identified by `CaseId` traits.

The documentation inventory baseline was inspected on 2026-09-22 at `b8e47219179ab5cd707d95dfc6e594da37429d85`, with a later source/discovery check at `053aaec315a179082d0535ad4bda3c2706c5900d`. It found **246 Domain cases / 235 methods**. That historical count is not the current solution total. Earlier full-solution checkpoints had intermittent E2E reviewer-page timeouts that passed in isolation and on complete E2E reruns. At the prior 298-case checkpoint, a grouped financial-journey attempt also had one intermittent journal-revocation timeout; the isolated case and complete E2E rerun passed. The prior 299-case checkpoint added the accounting dashboard sibling-task assertion. The 300-case checkpoint added same-document journal route-transition coverage to prevent stale detail from surviving an unavailable-ID navigation. The 301-case checkpoint added accounting queue in-app navigation and caught the PostgreSQL EF translation failure for nullable journal period IDs. The 302-case checkpoint added same-document PBC inbox scope revalidation and stale request clearing on engagement-route changes. The 303-case checkpoint characterizes same-document client-portal navigation between requests assigned to different recipients. The 304-case checkpoint characterizes same-document financial-package navigation to an unavailable ID; the existing component already clears the prior package. The 305-case checkpoint replaces the proposal page's hard-coded display with firm-wide authorized persisted data and verifies stale-route clearing, scoped denial and grant-revocation denial. The 312-case checkpoint added current-grant denial coverage for ReviewPoint and stabilized actor capture in accounting-record navigation. The 313-case checkpoint adds same-document review-point route reauthorization and prior-content clearing. The 314-case checkpoint verifies same-document records-archive scope changes clear prior manifest data. The 315-case discovery checkpoint adds ClientDetail route-scope coverage. The 316-case discovery adds `AS-PAR-002-AUDIT-FIELDWORK-STALE-ROUTE-01`, proving stale adopted program content is cleared for an unassigned engagement and restored only after returning to the authorized engagement. The 317-case discovery adds a PostgreSQL-backed same-document completion-route characterization that verifies a package ID is not retained when switching to an unassigned engagement; no runtime change was required. The 318-case discovery adds same-document advanced-consolidation navigation between authorized and unauthorized group scopes; its focused case passed 1/1 and the complete E2E project passed 53/53 with 0 skips. The 319-case discovery adds a real unauthorized sibling-engagement mapping route test; its focused PostgreSQL-backed browser case passed 1/1 and the complete E2E project passed 54/54 with 0 skips. The previous 246/246 result at `dc9cb0b` remains historical [execution evidence](../execution/status.json).

Controlling requirements are the [system test contract, §44](../SPECIFICATION.md#s44), [CI/runtime contract, §45](../SPECIFICATION.md#s45), [accounting requirements](../AuditSphere_Accounting_module.md), [audit-workflow requirements](../requirements/AuditSphere_Audit_Workflow_Gap_Closure_User_Stories.md), and [M365 onboarding requirements](../AuditSphere_M365_Simple_Onboarding_User_Story.md). Requirement checkboxes and test titles alone do not establish end-to-end acceptance.

All provisioned CI-safe checks can run unattended. External authorization and policy-required professional decisions cannot be manufactured by a green pipeline. Entra OIDC, selected-resource Graph operations, Purview observed protection, signing, independent checkpoint custody, recovery RPO/RTO, and professional sign-off remain separate acceptance proofs. Missing external prerequisites are `BLOCKED_EXTERNAL`, not successful skips.

The API and Playwright projects and the active CI workflow are implemented locally. This guide distinguishes those current checks from the broader proposed nightly, load, recovery, and external-acceptance work below; a local pass is not a hosted workflow result or live tenant acceptance.

## 2. Current architecture and remaining test layers

### Source-backed runtime boundaries

| Component | Current behavior and evidence |
|---|---|
| Domain and Application | Deterministic calculations, policies, commands and durable-operation contracts; see [Domain](../../src/AuditSphereOps.Domain) and [Application](../../src/AuditSphereOps.Application). |
| Infrastructure | EF Core/Npgsql, real PostgreSQL migrations, scoped persistence, provider boundaries and local durable operations; see [Infrastructure](../../src/AuditSphereOps.Infrastructure). |
| Web | Real Blazor Interactive Server circuits, cookie/OIDC configuration, scoped commands, PBC HTTP transfer endpoints, readiness; does not execute queued provider effects. See [Web startup](../../src/AuditSphereOps.Web/Program.cs). |
| Worker | Claims durable work with firm/group/epoch fencing; calculates/renders packages and executes explicitly allowed local adapters. See [Worker startup](../../src/AuditSphereOps.Worker/Program.cs). |
| PostgreSQL | Business state, append-only evidence, outbox, leases and migration history. PostgreSQL behavior cannot be replaced with EF InMemory/SQLite for integration assertions. |
| Local storage | PBC staging, simulated provider effects and content-addressed release checkpoints; defaults are shared temporary roots, so future automated scenarios must override them. |
| External systems | General live provider composition is not approved. Web refuses `ExternalEffects:Enabled=true`; the isolated Acceptance mail worker is a narrow exception, not general SharePoint/Purview acceptance. |

Current execution includes direct calculator/service tests plus API and browser journeys against owned Kestrel processes and PostgreSQL; selected E2E scenarios also exercise a real Worker with local simulation adapters. Broader scenario-owned hosting, independent lineage coverage, and all listed user journeys remain incremental work.

### Layer responsibilities

| Layer | Current implementation | Target responsibility |
|---|---|---|
| Unit | Pure calculations/policies/renderers in the mixed [Domain.Tests project](../../tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj) | Exact money, rounding, reflection, consolidation and validation boundaries without DB/network. |
| Integration | Real PostgreSQL, migrations, provider doubles, files, outbox and recovery in that same project | Scope constraints, rollback, append-only history, races/locks, idempotency, lease/revision fencing and uncertain effects. |
| HTTP API — implemented locally | `tests/AuditSphereOps.Api.Tests`: xUnit HTTP clients against owned Kestrel, real auth middleware, streams, headers, cancellation, readiness and startup guards. |
| UI/E2E — implemented locally | `tests/AuditSphereOps.E2E.Tests`: Microsoft Playwright for .NET + xUnit; real circuits, Web, Worker, PostgreSQL and local simulation storage. |
| Authorized external acceptance — blocked | `scripts/verify-tenant.sh` reports blocked | Separate approved non-production runner, grants, provider composition, resource ownership and protected approvals. |

The solution uses three .NET/VSTest projects: the original Domain/integration suite plus dedicated HTTP API and Playwright E2E projects. The latter share PostgreSQL test ownership utilities and retain project-specific lock files; CI restores in locked mode, pins the browser/runtime inputs, runs all projects and keeps test/coverage diagnostics. Local tests still use synthetic identities and simulation adapters only.

Do not invent REST endpoints for service-only features. Current HTTP targets are `/health/live`, `/health/ready`, `/auth/sign-in`, `/auth/landing`, `/auth/sign-out`, `POST /api/pbc/uploads/{uploadId:guid}/chunks/{chunkIndex:int}`, and `GET /api/pbc/uploads/{uploadId:guid}/download`. Accounting, audit and onboarding journeys primarily use Blazor UI/services. OIDC callback behavior is configured through the authentication handler, not a generic business REST API.

### Assertion design

Use stable role/label locators and awaited Playwright assertions; introduce explicit test IDs only where accessible semantics are insufficient. Assert navigation, validation messages, forbidden actions, download bytes, durable state and source lineage. Screenshots are diagnostics, not financial proof. A health response is not proof that a circuit connected or a Worker processed an operation.

Keep independent expected values in synthetic golden fixtures. For the §44.3 14-account/AJ-001 cycle assert debit/credit controls of 1,820,000, original profit 180,000, original balance-sheet total 750,000, adjusted profit 175,000, net PPE 95,000 and assets 745,000; a source replacement already reflecting AJ-001 must remain 175,000. Reconcile every report line to accepted source, journal, mapping and approved rate/version inputs. Balanced totals or an APPROVED label alone cannot prove advanced consolidation correctness.

## 3. Data, identity, isolation, and cleanup

### Existing integration fixture — preserve its contract

[PgTestSchema](../../tests/AuditSphereOps.Domain.Tests/PgTestSchema.cs) uses `AUDITSPHERE_TEST_CONNECTION`, defaulting to:

```text
Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=postgres
```

It rejects a different host or database name. Each fixture owns a `test_<guid>` schema, applies real migrations, uses its schema in SearchPath, and limits its pool to six connections. The fixture checks PostgreSQL **major 18**, not exact 18.6; add an environment preflight checking `server_version_num = 180006` in CI/examples. Admin connections are unpooled; disposal clears the scoped pool and attempts to drop its own schema. Cleanup is currently best effort, and setup failures can leave schemas: this is not an existing comprehensive stale-resource manager.

Use a separate PostgreSQL service/instance per CI job while retaining the exact `auditsphere_tests` database name. Do not point these tests at normal `auditsphere`, change the fixture guard, or start/stop a developer's shared server from a test.

### Proposed API/browser scenario ownership

1. Allocate a run ID from commit/run/attempt/shard plus random entropy. Use safe identifiers under PostgreSQL's length limit.
2. Allocate **one database per scenario**, named `auditsphere_e2e_<owned_suffix>`. The existing schema fixture is not reused for these database names; a new scenario fixture is required.
3. Write an ownership manifest before allocating resources; append database, role-host, Worker and directory identities as they are acquired. Never infer ownership merely from a prefix.
4. Apply migrations and seed synthetic firms, unrelated clients, sibling engagements, group scopes, periods/books, exact identity bindings, grants, TB/GL evidence and approved local fixtures **before starting hosts**. Keep setup outside browser assertions.
5. Start role-specific Web processes and the matching Worker processes with scenario-only connection/storage settings. Claim only that scenario's seeded firm and appropriate operation group/deployment epoch. Ordinary accounting/PBC work uses `general`; release checkpoints require a separate `release` group Worker, as defined by [ReleaseCheckpointHandler](../../src/AuditSphereOps.Application/Completion/ReleaseCheckpointHandler.cs). Start only the groups needed by the scenario.
6. Run each stateful multi-role journey within one shard and one scenario lifetime. No parallel use of the launcher's fixed `auditsphere_browser` database or port 5029.

Give every scenario owned staging, simulated-provider, checkpoint, logs and temporary roots. Only that scenario's Web/Worker processes may share these roots. Use synthetic nonproduction email domains such as `example.invalid`; never use production backups, real client data, tenant identifiers, credentials, cookies or signed URLs as fixtures/public artifacts.

### Multi-actor authentication

The existing development identity is fixed per Web process by `DevelopmentIdentity:Subject` and `DevelopmentIdentity:TenantId`, then resolved to a real persisted user/grant. It works only in Development/Test, cannot coexist with configured OIDC, and unavailable/disabled identities fail sign-in. Use separate role-configured Web processes and separate BrowserContexts/HTTP cookie jars for preparer, reviewer, partner, administrator and client management. Never add an arbitrary-user login bypass.

Allocate unique ports/origins, contexts, cookies, local/session storage and host-specific key storage. Cookies are not isolated by port alone: do not reuse a BrowserContext between same-host role ports. Explicit per-host Data Protection application/key-directory configuration is a **proposed fixture/hosting seam**, not an existing application configuration key. Until that seam exists, do not claim cryptographic host-key isolation from port separation. No authentication storage-state cache across scenarios/runs.

Development alone does not execute simulated PBC transfers. These journeys require both hosts in `Test`, Web `Application__AllowSimulationAdapters=true`, Worker `AllowSimulationAdapters=true`, disabled external effects, and shared owned storage. A future mail-capture composition is also required for E2E mail assertions: the ordinary Test Worker does not currently register production mail delivery. Queued mail is not mailbox delivery.

### Cleanup and cancellation

The proposed orchestrator must use try/finally and cancellation handlers from the first allocation onward:
- Capture redacted diagnostics and final durable dispositions before removing resources.
- Close BrowserContexts/download handles, then stop and await only manifest-owned Web/Worker processes, checking PID plus process start identity before signaling.
- Dispose clients, EF factories/data sources and scoped pools before removing owned databases/schemas.
- Remove only manifest-owned files/databases after verifying the exact loopback target and run ownership. Never stop shared PostgreSQL, drop `auditsphere`, or clean by broad wildcard.
- Bound graceful shutdown; record forced termination and cleanup failures. A cleanup failure makes an otherwise passing run fail and preserves the manifest for remediation.
- On hard runner loss, a separately scheduled janitor may clean stale resources only with matching ownership, expired run lease and no active processes/run. Never delete another active run's resources. Define the TTL as 24 hours for disposable CI resources; preserve failure diagnostics according to retention policy.

These lifecycle controls are proposed; the current browser launcher is not suitable as a parallel CI orchestrator.

## 4. Environment configuration

Exact existing application keys are sourced from [Web settings](../../src/AuditSphereOps.Web/appsettings.json), [Web startup](../../src/AuditSphereOps.Web/Program.cs), [Worker settings](../../src/AuditSphereOps.Worker/appsettings.json), and [Worker startup](../../src/AuditSphereOps.Worker/Program.cs). Double underscores below are environment-variable hierarchy separators. Do not inherit local user secrets or launch profiles into CI.

| Setting | Development/manual | Test API/E2E | Ordinary CI | Authorized Acceptance |
|---|---|---|---|---|
| `DOTNET_ENVIRONMENT` / Web `ASPNETCORE_ENVIRONMENT` | Both `Development` for Web | Both `Test` for Web; Worker `DOTNET_ENVIRONMENT=Test` | `Test` for owned hosts | `Acceptance`; subject to approved composition |
| `AUDITSPHERE_TEST_CONNECTION` | Loopback `auditsphere_tests` | Existing suite only; scenario fixture uses separate connection | Loopback `auditsphere_tests` per job | Not a tenant target |
| `ConnectionStrings__AuditSphere` | Explicit loopback development DB | Owned `auditsphere_e2e_*` DB | Test DB for readiness; scenario DB for future UI | Approved isolated DB; secret supplied privately |
| `AUDITSPHERE_EF_CONNECTION` | Explicit migration target | Same owned scenario target during migration | Test/scenario target only | Approved migration procedure, no runtime migrations |
| Web `Application__FirmId`, `Application__InstallationId`, `Application__PublicBaseUrl` | Configured local scope/origin | Seeded firm/installation and role-host origin | Same synthetic scope, not production values | Approved values from protected configuration |
| Web `Application__AllowSimulationAdapters` | `true` for local profile | `true` | `true` for simulated hosts | `false`; does not itself enable live providers |
| Worker `AllowSimulationAdapters` | `false` unless explicitly testing; Development still cannot transfer | `true` | `true` for Test transfer worker | `false` |
| Both `ExternalEffects__Enabled` | `false` | `false` | `false` | Only separately approved composition; current Web refuses `true` |
| Worker `Worker__FirmId`, `Worker__DeploymentEpoch`, `Worker__Group` | Exact local firm, positive seeded epoch, applicable group | Seeded firm/epoch; `general` for accounting/PBC and separate `release` Worker for checkpoints | Same per scenario; never a different firm's Worker | Exact approved scope; current live-mail exception requires `mail` |
| Web `ExternalEffects__DeploymentEpoch` | Unset while disabled | Unset while disabled | Unset while disabled | Positive approved epoch is necessary, not sufficient |
| `Storage__PbcStagingRoot`, `Storage__PbcProviderSimulationRoot`, `Storage__ReleaseCheckpointRoot` | Explicit local roots preferred | Owned roots shared only within scenario | Owned roots below runner temporary directory | Approved stores; no simulated protection proof |
| Web `DevelopmentIdentity__Enabled` | Explicit opt-in | `true` only for role hosts; `false` for anonymous tests | `false` for readiness, explicit role hosts later | `false` |
| Web `DevelopmentIdentity__Subject`, `DevelopmentIdentity__TenantId` | Exact seeded local identity | Synthetic identity per role process | Generated fixture identity | Unset |
| Web `Identity__TenantId`, `Identity__ClientId`, `Identity__ClientSecret`, `Identity__CallbackPath` | Unset for local identity | Unset for simulation | No tenant credentials | Approved OIDC; default callback `/signin-oidc` |
| Web `Setup__FirmId`, `Setup__InstallationId`, `Setup__BootstrapProofHash`, `Setup__InitialAdministratorTenantId`, `Setup__InitialAdministratorObjectId` | Only for intended local setup | Synthetic scenario-only setup capability/identity | Generated privately; never publish proof/hash | Approved bootstrap custody; never CI public artifacts |
| `FeatureActivation__LiveAccountingRelease`, `FeatureActivation__LiveAuditRelease`, `FeatureActivation__LiveFirmLedgerPosting` | `false` | `false` | `false` | Separate capability/acceptance decisions; no automatic enablement |
| `ReleaseSafety__RequireSignatureLineage`, `ReleaseSafety__RequireProtectionAttestation` | Current local defaults `false` | Scenario-specific negative/positive synthetic fixtures | Do not relax a scenario's required gate | Required approved policy, not a testing bypass |
| `ReleaseSafety__RequireExternalCheckpointBeforeDelivery` | Current default `true` | Keep `true`; local fixture remains simulated evidence | Keep `true` | Real independent checkpoint proof required |
| `Telemetry__Otlp__Endpoint` | Unset or explicitly local collector | Unset or run-owned collector | No external telemetry by default | Separately authorized endpoint/custody |

The isolated Acceptance mail worker additionally consumes `Mail__Provider` and one provider's keys: `GraphMail__TenantId`, `GraphMail__ClientId`, `GraphMail__SenderMailbox`, `GraphMail__CertificatePath`, `GraphMail__PrivateKeyPath`; or `SmtpMail__Host`, `SmtpMail__Port`, `SmtpMail__UseSsl`, `SmtpMail__SenderAddress`, `SmtpMail__SenderName`, `SmtpMail__Username`, `SmtpMail__Password`; or `ResendMail__ApiKey`, `ResendMail__SenderAddress`. Do not configure these in ordinary CI. The presence of these adapters does not establish live acceptance.

New `E2E_*` variables in later examples belong to the **proposed orchestrator contract**, not existing application settings. Secret values must come from approved private stores, never literal checked-in shell commands, fixtures, job summaries or traces.

## 5. Native local execution and diagnostics

Use a repository-root shell, SDK **10.0.300**, native PostgreSQL **18.6** bound to **127.0.0.1:5433**, and the existing `auditsphere_tests` database. No Docker is required locally. See [agent environment pointers](../../AGENTS.md#2-technology-stack--local-environment), [Windows initialization](../../scripts/db/init-cluster.ps1), [start](../../scripts/db/start.ps1), [status](../../scripts/db/status.ps1), and [PostgreSQL helper](../../scripts/db/pg-common.ps1). On macOS use the existing Homebrew PostgreSQL 18 installation/cluster; check its status before starting it. Do not initialize over an existing cluster or automatically restart a shared server.

Run the native version preflight with PostgreSQL's `psql` on PATH. Authentication must already be configured locally; this example contains no password:

```bash
set -euo pipefail
version=$(psql -X -h 127.0.0.1 -p 5433 -U postgres -d auditsphere_tests -At -v ON_ERROR_STOP=1 -c 'SHOW server_version_num')
test "$version" = 180006
dotnet --version
dotnet tool restore
dotnet restore AuditSphereOps.slnx --locked-mode
dotnet build AuditSphereOps.slnx --no-restore --configuration Release
dotnet test AuditSphereOps.slnx --no-build --no-restore --configuration Release --list-tests
dotnet test AuditSphereOps.slnx --no-build --no-restore --configuration Release \
  --logger 'trx;LogFileName=full-suite.trx' --results-directory TestResults/local \
  --collect:"XPlat Code Coverage"
dotnet ef migrations has-pending-model-changes \
  --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web \
  --no-build --configuration Release
```

The version check must fail, not silently fall back to major 18. Use a unique results directory per independent run. `TestResults/local` is a single-run example; choose a new path before repeating or comparing runs. Do not interpret missing DB/tools or zero discovery as successful tests. Avoid build/discovery configuration mismatch; `--no-build` must refer to the Release binaries just built.

Targeted diagnostic filters after that build:

```bash
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-build --no-restore --configuration Release \
  --filter 'FullyQualifiedName~AuditSphereOps.Domain.Tests.AdjustmentBridgeTests'
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-build --no-restore --configuration Release \
  --filter 'FullyQualifiedName~AuditSphereOps.Domain.Tests.AccountingBenchmarkTests'
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-build --no-restore --configuration Release --filter 'Profile=Unit'
```

The last command is **not** a complete unit partition. Traits are incomplete and overlapping: some pure tests inherit Database, rounding has Database+Unit, several classes are untagged, and RouteCatalogTests exercises services. Never replace the unfiltered gate with a union assumed from Profile filters. Theory display truncation also means display names are not unique IDs.

Run EF database update only against an explicitly authorized disposable readiness/scenario DB, before host startup:

```bash
export AUDITSPHERE_EF_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=postgres'
dotnet ef database update --project src/AuditSphereOps.Infrastructure \
  --startup-project src/AuditSphereOps.Web --no-build --configuration Release
```

The [restore drill](../../scripts/db/restore-drill.sh) is a separate mutating rehearsal, not a prerequisite for reading these docs. It dumps fixed local `auditsphere` into a generated restore DB and compares migrations/checkpoint/accounting/group identities. Scheduled CI must provide an isolated populated **synthetic** `auditsphere` source plus compatible native PG18 tools, and set `AUDITSPHERE_EVIDENCE_FILE` to an artifact path. Its version query does not enforce exact 18.6, and missing tools currently return 1. It was not rerun here. Do not run it against developer data merely to validate examples; local restore is not production RPO/RTO or separate-custody proof.

## 6. CI expansion blueprint beyond the active workflow

The [active workflow](../../.github/workflows/ci.yml) pins PostgreSQL 18.6 and action revisions, restores in locked mode, builds Release, checks EF model drift, migrates its disposable database, reconciles all 319 discovered/executed tests with zero skips, collects Cobertura/TRX, probes readiness, and uploads diagnostics. The current hosted run for 318 tests was in progress before this additional test-only slice. The longer example below is a **future hardening blueprint**, not the active workflow; it adds stricter runner-owned scenario manifests and artifact controls.

A hosted-runner PostgreSQL service container does not impose Docker on local development. The official `postgres:18.6` tag was verified as active through [Docker Hub metadata](https://hub.docker.com/v2/repositories/library/postgres/tags/18.6); the example pins its observed OCI index digest. Action v4 SHAs were resolved from upstream tags; they still require repository supply-chain review before activation. Recheck availability and patches when implementing, without silently weakening the exact-version requirement.

```yaml
name: AuditSphereOps Baseline Example
on:
  pull_request:
  push:
    branches: [master]
permissions:
  contents: read
concurrency:
  group: baseline-${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true
jobs:
  full-suite:
    runs-on: ubuntu-24.04
    timeout-minutes: 30
    services:
      postgres:
        image: postgres:18.6@sha256:86c951e05bf56c93d95d397747fb8820ac76cc3bedb78f43abd83eedbe3666ae
        env:
          POSTGRES_HOST_AUTH_METHOD: trust
          POSTGRES_DB: auditsphere_tests
        ports: ['127.0.0.1:5433:5432']
        options: >-
          --health-cmd "pg_isready -U postgres -d auditsphere_tests"
          --health-interval 5s --health-timeout 5s --health-retries 20
    env:
      AUDITSPHERE_TEST_CONNECTION: Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=postgres
      AUDITSPHERE_EF_CONNECTION: Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=postgres
      ConnectionStrings__AuditSphere: Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=postgres
      DOTNET_ENVIRONMENT: Test
      ASPNETCORE_ENVIRONMENT: Test
      ExternalEffects__Enabled: 'false'
      Application__AllowSimulationAdapters: 'true'
      DevelopmentIdentity__Enabled: 'false'
      EXPECTED_CASES: '319'
    defaults:
      run:
        shell: bash
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4
        with:
          persist-credentials: false
      - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9 # v4
        with:
          dotnet-version: 10.0.300
          cache: true
          cache-dependency-path: '**/packages.lock.json'
      - name: Initialize evidence and verify PostgreSQL
        env:
          PG_CONTAINER: ${{ job.services.postgres.id }}
        run: |
          set -euo pipefail
          mkdir -p artifacts/baseline
          dotnet --info > artifacts/baseline/dotnet.txt
          git rev-parse HEAD > artifacts/baseline/commit.txt
          docker exec "$PG_CONTAINER" psql -X -U postgres -d auditsphere_tests -At \
            -v ON_ERROR_STOP=1 -c 'SHOW server_version_num' > artifacts/baseline/postgres-version.txt
          test "$(tr -d '\r\n' < artifacts/baseline/postgres-version.txt)" = 180006
      - name: Locked restore and Release build
        run: |
          set -euo pipefail
          dotnet tool restore
          dotnet restore AuditSphereOps.slnx --locked-mode
          dotnet build AuditSphereOps.slnx --no-restore --configuration Release
      - name: Model drift and disposable readiness schema
        run: |
          set -euo pipefail
          dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure \
            --startup-project src/AuditSphereOps.Web --no-build --configuration Release
          dotnet ef database update --project src/AuditSphereOps.Infrastructure \
            --startup-project src/AuditSphereOps.Web --no-build --configuration Release
      - name: Discover the entire suite
        run: |
          set -euo pipefail
          dotnet test AuditSphereOps.slnx --no-build --no-restore --configuration Release \
            --list-tests | tee artifacts/baseline/discovery.txt
          python3 - <<'PY'
          import os
          from pathlib import Path
          text = Path('artifacts/baseline/discovery.txt').read_text()
          sections = text.split('The following Tests are available:')[1:]
          cases = [line.strip() for part in sections for line in part.splitlines()
                   if line.startswith('    ')]
          count = len(cases)
          assert count > 0 and count == int(os.environ['EXPECTED_CASES']), count
          Path('artifacts/baseline/discovery-count.txt').write_text(str(count))
          PY
      - name: Unfiltered suite with coverage
        id: tests
        run: |
          dotnet test AuditSphereOps.slnx --no-build --no-restore --configuration Release \
            --logger 'trx;LogFileName=full-suite.trx' --results-directory artifacts/baseline/results \
            --collect:"XPlat Code Coverage"
      - name: Reconcile execution and reject missing or skipped cases
        if: ${{ always() && steps.tests.outcome != 'skipped' }}
        run: |
          python3 - <<'PY'
          import json
          import xml.etree.ElementTree as ET
          from pathlib import Path
          root = Path('artifacts/baseline')
          files = list((root / 'results').rglob('*.trx'))
          assert len(files) == 1, files
          def read_report(path):
              limit = 32 * 1024 * 1024
              with path.open('rb') as stream:
                  content = stream.read(limit + 1)
              assert len(content) <= limit, 'Report exceeds size limit'
              text = content.decode('utf-8-sig')
              assert '<!DOCTYPE' not in text.upper() and '<!ENTITY' not in text.upper()
              return ET.fromstring(text)
          doc = read_report(files[0])
          counters = doc.find('.//{*}Counters')
          assert counters is not None
          c = {k: int(v) for k, v in counters.attrib.items()}
          (root / 'execution-counters.json').write_text(json.dumps(c, indent=2))
          expected = int((root / 'discovery-count.txt').read_text())
          assert c['total'] == c['executed'] == c['passed'] == expected, c
          assert c.get('failed', 0) == c.get('notExecuted', 0) == 0, c
          coverage = list((root / 'results').rglob('coverage.cobertura.xml'))
          assert coverage, 'No coverage report'
          for report in coverage:
              read_report(report)
          PY
      - name: Readiness smoke on an owned Web process
        run: |
          set -euo pipefail
          dotnet run --project src/AuditSphereOps.Web --no-launch-profile --no-build \
            --no-restore --configuration Release --urls http://127.0.0.1:5099 \
            > artifacts/baseline/web.log 2>&1 &
          web_pid=$!
          cleanup() {
            kill "$web_pid" 2>/dev/null || true
            wait "$web_pid" 2>/dev/null || true
          }
          trap cleanup EXIT
          trap 'exit 130' INT
          trap 'exit 143' TERM
          for attempt in {1..60}; do
            kill -0 "$web_pid" || exit 1
            if curl --fail --silent --max-time 2 http://127.0.0.1:5099/health/ready \
              > artifacts/baseline/readiness.txt; then
              grep -q '^Healthy$' artifacts/baseline/readiness.txt && exit 0
            fi
            sleep 1
          done
          exit 1
      - name: Capture PostgreSQL diagnostics and job summary
        if: ${{ always() }}
        env:
          PG_CONTAINER: ${{ job.services.postgres.id }}
          RESULT: ${{ job.status }}
        run: |
          mkdir -p artifacts/baseline
          docker logs "$PG_CONTAINER" > artifacts/baseline/postgres.log 2>&1
          printf 'Baseline outcome: %s\nCommit: %s\nRun: %s / attempt %s\n' \
            "$RESULT" "$GITHUB_SHA" "$GITHUB_RUN_ID" "$GITHUB_RUN_ATTEMPT" >> "$GITHUB_STEP_SUMMARY"
      - name: Diagnostic artifacts
        if: ${{ always() }}
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4
        with:
          name: baseline-diagnostics-${{ github.run_id }}-${{ github.run_attempt }}
          path: |
            artifacts/baseline/*.txt
            artifacts/baseline/*.json
            artifacts/baseline/*.log
            artifacts/baseline/results/**/*.trx
          if-no-files-found: warn
          retention-days: 14
      - name: Coverage artifacts
        if: ${{ always() }}
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4
        with:
          name: baseline-coverage-${{ github.run_id }}-${{ github.run_attempt }}
          path: artifacts/baseline/results/**/coverage.cobertura.xml
          if-no-files-found: warn
          retention-days: 30
```

The trust-auth container is disposable, loopback-published, synthetic-only and has no tenant secrets. It is not a production database recipe. Never expose this profile on a shared network. For a Docker-free CI alternative, use an ephemeral native PostgreSQL 18.6 runner image labeled for that capability, provision an isolated `auditsphere_tests` DB before the job, omit `services`, and replace `docker exec/logs` with native `psql` and owned server-log collection. Prefer separate runner VMs over changing the fixture's guarded database name. Do not schedule untrusted fork code onto a persistent privileged/self-hosted machine.

`EXPECTED_CASES=319` is the current reconciled discovery count across the three projects, not a permanent cap. Legitimate additions/removals require a reviewed count/catalog update; do not reduce it just to pass CI. The textual parser counts all three solution project sections and deliberately includes duplicate display names. The TRX check rejects any nonexecuted outcome; no `continue-on-error` or `dotnet test || true` hides failure. Infrastructure failures should be classified separately in the future structured summary while still failing the required check.

## 7. Proposed API/browser workflow and orchestration contract

**Implemented with remaining orchestration limits:** the API and E2E test projects, pinned Playwright dependency/lockfile, PostgreSQL ownership fixture and process/browser journeys exist. A separate shard-manifest/orchestrator script and cryptographic per-host Data Protection key isolation are not implemented; do not claim those capabilities from the current harness.

### Orchestrator contract

| Input / operation | Required behavior |
|---|---|
| `-Mode Run` | Validate prerequisites and discovery/shards; provision each selected scenario, migrate/seed, start owned hosts/Worker, poll readiness, execute tests, capture diagnostics and clean in finally. |
| `-Mode Cleanup` | Idempotent cleanup from `-RunRoot/ownership.json`; absent manifest is a no-op, ambiguous ownership is a failure. Useful after cancellation in addition to Run's finally. |
| `-RunRoot` | Absolute owned directory under runner temp, distinct by run/attempt/browser/shard. Contains private working state and separate `publish/` sanitized artifacts. |
| `-Browser` (`chromium`, `firefox`, or `webkit`) | Actual Playwright browser; pass `Playwright.BrowserName` to .NET E2E execution. No implicit fallback to Chromium. |
| `-Shard` (`1` or `2`), `-ShardCount 2` | Explicit test-ID manifests; compare their union to discovered cases and intersections to empty before executing. Never split a stateful journey. |
| `E2E_ADMIN_CONNECTION` | Private loopback admin connection; restrict provisioning to manifest-owned `auditsphere_e2e_*` DBs. Never log credentials. |
| `E2E_RUN_ID` | Unique CI/local ownership identity, not an application configuration key. |
| Ownership manifest | Schema version, commit/run/attempt/browser/shard, scenario IDs, DB names, synthetic scope IDs, roots, ports, PID/start identities, resource state, lease/expiry and cleanup outcomes. Secret handles only, no secret values. |
| Outputs | `publish/diagnostics/summary.json`, TRX, redacted host/worker/DB logs, version/migration evidence, failed-case trace/screenshots and cleanup report; Cobertura and trend summaries under `publish/coverage/`. No authenticated storage state. |
| Exit status | 0 only when all expected cases execute/pass and cleanup succeeds; 1 assertion/infrastructure/cleanup failure; 2 blocked required prerequisites. Preserve cancellation disposition and nonzero exit. |

Initially give each proposed stateful journey one xUnit Fact and a unique `CaseId` trait. Do not use theory rows that cannot be selected independently. The proposed helper can build an OR filter from manifest CaseIds, verify discovered identities before filtering, and reconcile executed IDs/counts afterward. If theories are added later, use structured discovery with full parameter identities; method-only sharding cannot prove expanded-case coverage. Proposed manifests cover both API and E2E projects; API cases can repeat across browser jobs but have one manifest owner within each browser run.

Run one active scenario per job and two shards. For each scenario, the helper waits up to 120 seconds for a live process and healthy Web readiness, then verifies Worker registration/claim behavior through a bounded synthetic durable operation; the Worker has no invented HTTP health endpoint. Await durable status by ID with a bounded 120-second default, overridable for declared large fixtures. Polling cadence is bounded; never use fixed sleeps as proof of workflow completion. Playwright auto-waits do not replace checking persisted operation state.

Use trusted local test HTTPS with a certificate valid for the owned origins where transport/cookie behavior is under test. A loopback HTTP smoke checks readiness only; it does not validate HTTPS redirects, secure cookies or production reverse-proxy behavior. The helper must configure the real hosts/certificates and isolated key rings; no hidden production bypass.

```yaml
name: AuditSphereOps Future API And Browser Example
on:
  pull_request:
  push:
    branches: [master]
  schedule:
    - cron: '23 2 * * *'
permissions:
  contents: read
concurrency:
  group: e2e-${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true
jobs:
  api-browser:
    runs-on: ubuntu-24.04
    timeout-minutes: 45
    strategy:
      fail-fast: false
      matrix:
        browser: ${{ fromJSON(github.event_name == 'schedule' && '["chromium","firefox","webkit"]' || '["chromium"]') }}
        shard: [1, 2]
    services:
      postgres:
        image: postgres:18.6@sha256:86c951e05bf56c93d95d397747fb8820ac76cc3bedb78f43abd83eedbe3666ae
        env:
          POSTGRES_HOST_AUTH_METHOD: trust
          POSTGRES_DB: auditsphere_tests
        ports: ['127.0.0.1:5433:5432']
        options: >-
          --health-cmd "pg_isready -U postgres -d auditsphere_tests"
          --health-interval 5s --health-timeout 5s --health-retries 20
    env:
      E2E_ADMIN_CONNECTION: Host=127.0.0.1;Port=5433;Database=postgres;Username=postgres
      E2E_RUN_ID: ${{ github.run_id }}-${{ github.run_attempt }}-${{ matrix.browser }}-${{ matrix.shard }}
      E2E_RUN_ROOT: ${{ runner.temp }}/auditsphere-${{ github.run_id }}-${{ github.run_attempt }}-${{ matrix.browser }}-${{ matrix.shard }}
      E2E_BROWSER: ${{ matrix.browser }}
      E2E_SHARD: ${{ matrix.shard }}
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262
        with:
          persist-credentials: false
      - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9
        with:
          dotnet-version: 10.0.300
          cache: true
          cache-dependency-path: '**/packages.lock.json'
      - name: Verify prerequisites and exact DB version
        shell: bash
        env:
          PG_CONTAINER: ${{ job.services.postgres.id }}
        run: |
          set -euo pipefail
          test -f tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj
          test -f tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj
          test -f scripts/ci/e2e-orchestrator.ps1
          version=$(docker exec "$PG_CONTAINER" psql -X -U postgres -d postgres -At -c 'SHOW server_version_num')
          test "$version" = 180006
      - name: Restore and build proposed solution
        shell: bash
        run: |
          set -euo pipefail
          dotnet tool restore
          dotnet restore AuditSphereOps.slnx --locked-mode
          dotnet build AuditSphereOps.slnx --no-restore --configuration Release
      - name: Install version-matched browser and Linux dependencies
        shell: pwsh
        run: |
          & tests/AuditSphereOps.E2E.Tests/bin/Release/net10.0/playwright.ps1 install --with-deps $env:E2E_BROWSER
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
      - name: Provision, exercise real hosts, report, and clean
        shell: pwsh
        run: |
          & scripts/ci/e2e-orchestrator.ps1 -Mode Run -RunRoot $env:E2E_RUN_ROOT `
            -Browser $env:E2E_BROWSER -Shard $env:E2E_SHARD -ShardCount 2
          exit $LASTEXITCODE
      - name: Cancellation cleanup safety net
        if: ${{ always() }}
        shell: pwsh
        run: |
          if (Test-Path scripts/ci/e2e-orchestrator.ps1) {
            & scripts/ci/e2e-orchestrator.ps1 -Mode Cleanup -RunRoot $env:E2E_RUN_ROOT
            exit $LASTEXITCODE
          }
      - name: Sanitized diagnostics
        if: ${{ always() }}
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02
        with:
          name: e2e-${{ matrix.browser }}-${{ matrix.shard }}-${{ github.run_id }}-${{ github.run_attempt }}
          path: ${{ env.E2E_RUN_ROOT }}/publish/diagnostics/
          if-no-files-found: warn
          retention-days: 14
      - name: Coverage and trend evidence
        if: ${{ always() }}
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02
        with:
          name: e2e-coverage-${{ matrix.browser }}-${{ matrix.shard }}-${{ github.run_id }}-${{ github.run_attempt }}
          path: ${{ env.E2E_RUN_ROOT }}/publish/coverage/
          if-no-files-found: warn
          retention-days: 30
```

The future projects must be included in the solution before its build can generate Playwright's installation script. Pin a reviewed `Microsoft.Playwright.Xunit` version in central package management/lockfiles at implementation; no version has been chosen or installed in this task. The generated script installs the matching browser revisions. Official .NET usage passes browser selection as:

```bash
# Proposed project only, after scenario provisioning by the helper:
dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj \
  --no-build --no-restore --configuration Release \
  --logger 'trx;LogFileName=browser.trx' --results-directory TestResults/e2e \
  --collect:"XPlat Code Coverage" -- Playwright.BrowserName=chromium
```

The real helper also supplies the scenario CaseId filter and owned host manifest. Code coverage from the test process does **not** automatically instrument separately launched Web/Worker processes. Keep baseline service/unit Cobertura separate; collect host coverage only after an explicit supported instrumentation design. Never describe browser test-assembly coverage as complete application coverage.

## 8. Protected external-acceptance example

**Non-executable acceptance blueprint:** `.github/workflows/tenant-acceptance.yml`, the protected GitHub environment, approved ephemeral runner/provider composition, asset grants and live runner are not created. Never run tenant-secret-bearing code from fork PRs or `pull_request_target`. A manual dispatch on protected `master`, required environment approvals and exact reviewed revision are prerequisites; this lane is not an ordinary PR gate.

The existing [tenant verifier](../../scripts/verify-tenant.sh) requires `--environment` and checks `AUDITSPHERE_TENANT_ID`, `AUDITSPHERE_CLIENT_ID`, `AUDITSPHERE_SELECTED_SITE_ID`. It returns `BLOCKED`/2 **even when all are present** because the live runner is not approved. Configuration is necessary, not sufficient.

This example intentionally invokes that existing blocked preflight. It performs no live acceptance and supplies no secrets. Only a separately approved change can replace it with a real runner implementing the same outcome contract.

```yaml
name: AuditSphereOps Protected Acceptance Blueprint
on:
  workflow_dispatch:
permissions:
  contents: read
concurrency:
  group: tenant-acceptance
  cancel-in-progress: false
jobs:
  acceptance-preflight:
    if: ${{ github.ref == 'refs/heads/master' }}
    runs-on: ubuntu-24.04
    environment: tenant-acceptance
    timeout-minutes: 15
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262
        with:
          persist-credentials: false
      - name: Preserve blocked classification as a failing check
        shell: bash
        run: |
          set -euo pipefail
          mkdir -p artifacts/acceptance
          set +e
          bash scripts/verify-tenant.sh --environment Acceptance > artifacts/acceptance/preflight.json
          result=$?
          set -e
          if [ "$result" -eq 2 ]; then
            printf '{"status":"BLOCKED_EXTERNAL","exitCode":2,"executedCases":0}\n' \
              > artifacts/acceptance/summary.json
            printf 'BLOCKED_EXTERNAL: approved live acceptance prerequisites/runner absent.\n' >> "$GITHUB_STEP_SUMMARY"
            exit 2
          fi
          # This is only a preflight, so even exit 0 cannot establish acceptance.
          printf '{"status":"INFRASTRUCTURE_FAILURE","reason":"unexpected preflight result","executedCases":0}\n' \
            > artifacts/acceptance/summary.json
          exit 1
      - name: Redacted preflight summary only
        if: ${{ always() }}
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02
        with:
          name: acceptance-preflight-${{ github.run_id }}-${{ github.run_attempt }}
          path: artifacts/acceptance/*.json
          retention-days: 14
```

The approved future runner must return 0 only after all selected authorized cases execute/pass, 1 for failed assertions/infrastructure, and structured `BLOCKED_EXTERNAL` plus exit 2 for missing authorization/configuration/runner/composition. GitHub treats exit 2 as a failing check; a wrapper may retain that classification while translating to exit 1. Do not turn it into neutral/skipped success. Maintain separate local and acceptance required-check policies; changing repository protections is outside this task.

Live acceptance must prove separately:
- Entra OIDC wrong-tenant, disabled/unassigned identity denial and policy-compliant authentication. If unattended login is prohibited by MFA/conditional access, report blocked; never bypass policy or persist public authenticated browser state.
- Graph selected-resource operations on approved disposable assets, version/hash identity and denial outside selected scope. Never request tenant-wide grants to make a test pass.
- Purview requested versus actually observed protection on the exact resource/version, approved records profile and hold behavior. Never remove retention/holds to force cleanup.
- Approved signing over exact bytes, independent checkpoint/recovery custody, and professional review through their own separately authorized evidence paths.

Routine CI uses local deterministic doubles for authorization denial, throttling/retry-after, transient failure, duplicate delivery, timeout/unknown outcome, stale revision and reconciliation. The current catalog proves only the listed existing scenarios; the entire desired fault matrix is not claimed implemented. Test assets remain under retention/approval policy even after failure; cleanup must not bypass it.

## 9. Parallelism, performance, caching, and reporting

Keep [xUnit runner](../../tests/AuditSphereOps.Domain.Tests/xunit.runner.json) `maxParallelThreads: 1`. Prior concurrent migrations exhausted PostgreSQL lock resources. Increase parallelism only from measurements after isolation/completeness controls exist. First parallelize independent jobs with separate PostgreSQL instances; do not share the developer DB across shards. Preserve an unfiltered existing-suite required check until classification is normalized and machine-checked.

NuGet cache keys must include OS and lockfile content. The future browser job intentionally installs browsers each time initially; if caching is introduced, key it by OS/architecture and exact Playwright package/browser revision, and still install OS dependencies explicitly with the generated script. Never cache databases, user sessions, authentication storage, client evidence, secrets or scenario directories.

PERF-001 is a bounded development benchmark (2,000 transactions / 8,000 GL lines), not an approved production capacity test. Add separate scheduled load/soak jobs against disposable environments after thresholds are approved. Measure throughput, p50/p95/p99 latency, durable queue delay, error rate, allocations/GC, RSS, connection-pool utilization, locks/contention and duplicate/stale results. Exercise actual Blazor circuits as well as HTTP and Worker throughput; health-endpoint load does not model Interactive Server sessions. Keep load generators and failure injection out of production. Do not invent production SLOs from one developer run.

### Outcome and artifact contract — proposed

Every completed run should emit a machine-readable summary with schema version, source commit, run/attempt/shard/browser, environment class, exact commands/tool versions, DB version and migration identity, expected/discovered/executed/passed/failed/skipped counts, case identities, durations, cleanup outcome and sanitized artifact paths. Distinguish:
- `PASSED`: every expected CI-safe case ran and passed, with required evidence and cleanup.
- `ASSERTION_FAILURE`: behavior contradicted an expectation.
- `INFRASTRUCTURE_FAILURE`: build/discovery/DB/host/browser/tooling/cleanup failed.
- `BLOCKED_EXTERNAL`: required authorization/provider prerequisites absent.
- `CANCELLED`: interrupted; do not infer a successful subset is the whole run.
- `ABSENT_COVERAGE`: required layer/scenario not implemented; not a test pass.

Retain the first-attempt result. At most one separately labeled diagnostic rerun may collect more evidence; it must not erase the initial failure or make the required check green. Record flaky signatures, owners and corrective action; no silent quarantine or widening skip allowance.

Collect TRX and Cobertura from current tests; redacted Web/Worker/PostgreSQL logs, migration/version evidence and per-failure Playwright traces/screenshots from future scenarios. Keep `summary.json` with diagnostics and the coverage/trend summary with coverage. Retain ordinary diagnostics for **14 days**, coverage/trends for **30 days**, subject to repository/organization limits. Public-repository artifacts may be publicly accessible; synthetic-only does not excuse uploading cookies, capabilities or bootstrap hashes.

For live tenants, suppress browser captures by default or place them under separately approved restricted custody. Redacting text logs cannot sanitize screenshots/DOM/network payloads in traces. Sanitize before upload using an allowlisted publication directory; never upload the entire run root, key ring, database dump, environment dump or Playwright storage state. Artifact upload uses `if: always()` but cannot guarantee output after hard cancellation/runner loss; missing required artifacts remain visible failures.

Use GitHub job summaries and native workflow-failure notifications by default. Include failing catalog/scenario IDs, classification, commit/run link and sanitized diagnostic links. Optional Teams/email delivery requires a separately authorized integration, audience and secret custody; send status/links only, not tenant data or browser attachments. No notification integration or external messages are activated here.

Coverage percentages are unmeasured by this documentation task. First approve a measured baseline for relevant production assemblies, then ratchet against unexplained regression using identical scope/configuration; enforce critical invariant assertions independently of percentage. Do not introduce a fictional target or count generated migrations/tests as business logic coverage.

## 10. Adoption and completion criteria

| Stage | Implementation exit criteria | Current state |
|---|---|---|
| Documentation baseline | Reconciled catalog, source-bound examples and honest gaps | Historical baseline reconciled; newer per-category IDs remain follow-up. |
| Existing-suite CI | Locked Release build, exact PG18.6, full discovery/execution reconciliation, no skips, coverage/readiness/artifacts | Implemented in the active workflow; local full suite passed, hosted run not observed here. |
| API fixture | Owned DB/process lifecycle, auth/streaming/health assertions, cleanup under failure/cancellation | API test project and six tagged cases implemented locally; broader fault matrix remains. |
| Chromium regression | Real multi-role circuits and Worker journeys, source-to-report proof, explicit shard completeness | Playwright project and selected journeys implemented locally; full proposal/sharding/nightly matrix remains. |
| Nightly browser/recovery/load | Firefox/WebKit matrix, synthetic recovery, measured soak/capacity baselines and trend reporting | Proposed; no approved load SLO. |
| External acceptance | Approved runner, composition, selected assets/grants, secret/capture custody and required independent approvals | Blocked; green local CI never enables production. |

Refresh the catalog when test identities/theory rows/assertions change, and this guide when runtime boundaries, configuration or commands change. Check links, code fences, YAML/shell syntax and example prerequisites. Preserve historical execution records separately from documentation verification. This task does not execute cloud workflows, tenant operations, restore drills, browser automation or notifications.

## Official references

- [.NET 10 test command and runner choice](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test) and [VSTest options](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-vstest). This repository uses the default VSTest path; [global.json](../../global.json) pins SDK 10.0.300, not Microsoft.Testing.Platform.
- [Playwright .NET introduction](https://playwright.dev/dotnet/docs/intro), [CI installation](https://playwright.dev/dotnet/docs/ci), [browser revisions](https://playwright.dev/dotnet/docs/browsers), [BrowserContext isolation](https://playwright.dev/dotnet/docs/browser-contexts), and [trace diagnostics](https://playwright.dev/dotnet/docs/trace-viewer).
- [GitHub workflow artifacts and retention](https://docs.github.com/en/actions/using-workflows/storing-workflow-data-as-artifacts), [PostgreSQL service containers](https://docs.github.com/en/actions/tutorials/use-containerized-services/create-postgresql-service-containers), and [secure use of Actions](https://docs.github.com/en/actions/security-for-github-actions/security-guides/security-hardening-for-github-actions).
- [Official PostgreSQL 18.6 image metadata](https://hub.docker.com/v2/repositories/library/postgres/tags/18.6). Tag/digest availability was checked; no container/workflow was executed during documentation authoring.
