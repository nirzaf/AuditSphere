# AuditSphereOps Agent Instructions

Read this file first, every session. It is short on purpose: it points to the authority documents instead of repeating them. Full contracts live in `docs/architecture/auditsphere-architecture-current-architecture.md` and `docs/auditsphere-accounting-module-requirements-current.md`.

## 1. What this system is

An audit, accounting and assurance operations platform for the STE firm (Qatar, QAR, ISA/IFRS): one ASP.NET Core modular monolith plus a separate Angular presentation project.

| Project | Owns |
| --- | --- |
| `src/AuditSphereOps.Domain/` | Pure business models and invariants by capability (`Practice/`, `Accounting/`, `Audit/`, `Reviews/`, `Completion/`, `Documents/`, `Records/`, `Security/`, …). |
| `src/AuditSphereOps.Application/` | Static capability services, named queries, pure calculators, durable-operation contracts. |
| `src/AuditSphereOps.Infrastructure/` | EF Core 10 + Npgsql 10, `AuditSphereDbContext.<Module>.cs` partials, migrations, provider adapters. |
| `src/AuditSphereOps.Api/` | API host, cookie/Entra authentication, `/api/ui/*` endpoints (`Ui/UiEndpoints.<Area>.cs`), Angular route serving. Composes Application; never references Web or MudBlazor. |
| `src/AuditSphereOps.Ui/` | Angular 22 + Material/CDK. Staff workbench `/ui/app`, client portal `/ui/portal`. Same-origin API only; never Microsoft Graph, never browser bearer tokens. |
| `src/AuditSphereOps.Web/` | Legacy Blazor rollback/reference host. Keep it until the canonical migration readiness gate passes and the owner separately accepts retirement. |
| `src/AuditSphereOps.Worker/` | BackgroundService host for durable operations. |

### Three strict financial boundaries

1. **Firm's own books** (`Practice/`, `FirmLedger`): firm CRM, billing, time, firm ledger.
2. **Client accounting workspace** (`Accounting/`): client TB/GL, client charts, approved mappings, adjustments, entity packages.
3. **Group consolidation workspace**: component packs, perimeter, FX translation, eliminations. **Never** mutates component client books.

**Scope boundary.** Import-first accounting preparation, audit and consolidation. For a specifically authorized client accounting service, native client double-entry bookkeeping, client sales/purchase invoices and credit notes, GL/TB and statements are allowed; VAT/tax and ancillary modules are optional, independently gated, and never block the core path. Not authorized: inventory, procurement operations, payroll execution, payment initiation, external tax filing, or changes to the firm-ledger and consolidation boundaries. Existing clients stay on external-source mode unless explicitly migrated through an approved cutover. Client isolation, balanced postings, review and immutable history are mandatory in every mode.

## 2. Toolchain and local environment

- .NET SDK pinned in `global.json` (roll-forward disabled); EF tool pinned in `.config/dotnet-tools.json`; Node version in `src/AuditSphereOps.Ui/.nvmrc`; Angular 22 with Material/CDK.
- PostgreSQL 18.6, Docker-free, port `5433`. macOS: `pg_ctl -D /opt/homebrew/var/postgresql@18 -o "-p 5433" start` or `scripts/db/restore-drill.sh`. Windows: `scripts\db\status.ps1`, `start.ps1`, `stop.ps1`, `init-cluster.ps1`. Overrides: `AUDITSPHERE_TEST_CONNECTION`, `PGSQL_HOME`.
- Local development runs with `ExternalEffects.Enabled=false` and `AllowSimulationAdapters=true`.
- Production external gates (Entra OIDC, selected-resource SharePoint/Graph, any approved live release checkpoint) need live infrastructure. Record them as `BLOCKED_EXTERNAL`; never fake a pass. Purview and eSignature provider integrations are out of product scope: preserve exact uploaded signed-document evidence, SHA-256 identities, human decisions and release manifests without claiming provider acceptance.

## 3. Verification commands (run from the repository root)

```bash
dotnet tool restore
dotnet restore AuditSphereOps.slnx --locked-mode
dotnet build AuditSphereOps.slnx --no-restore --configuration Release --maxcpucount:1
dotnet test AuditSphereOps.slnx --no-build --configuration Release --maxcpucount:1
dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure \
  --startup-project src/AuditSphereOps.Api --no-build --configuration Release
npm --prefix src/AuditSphereOps.Ui ci && npm --prefix src/AuditSphereOps.Ui run build
npm --prefix src/AuditSphereOps.Ui run test:ci
bash scripts/contracts/verify-openapi.sh
python3 scripts/docs/validate-markdown-documentation.py
python3 scripts/docs/validate-markdown-filenames.py
python3 scripts/docs/validate-narrative-metrics.py
python3 scripts/ui/inventory.py --check
```

**Automated test suites are present.** They were removed on 2026-10-08 (commit `47ca0b05`) and restored by STE-NXT-001; ADR-0008 is `SUPERSEDED`. The Angular specs run in CI. The .NET suites run locally against the PostgreSQL test database `auditsphere_tests` on port 5433, never the development database. Report a test result only from the commands above and say which ran. Follow `docs/testing/auditsphere-testing-strategy-definition-of-done-current.md` for the rest of the verification rules.

## 4. Core invariants

- **Scope-checked authorization.** Every command, query and queue enforces explicit `RoleGrant` scope (`FIRM_WIDE`, `CLIENT`, `ENGAGEMENT`, `GROUP`). An engagement-only grant never widens to sibling engagements or clients. Enforce it in Application, never only in UI.
- **Immutability and lineage.** Sealed datasets, approved mappings, applied journals, issued packages and review decisions are append-only. Changes create new revisions or amendments; history never gains an update path.
- **Optimistic fencing.** Writes that edit a revisioned aggregate take an `ExpectedRevision`/`ExpectedVersion` and refuse stale input.
- **Pure calculators.** Financial and consolidation engines contain no EF Core, network, clock or non-deterministic IDs.
- **Durable operations.** Long-running work (GL completeness, package calculation, rendering) uses the local durable-operation infrastructure with revision fencing, idempotent retries and explicit cancellation dispositions.
- **Fail closed.** Missing exchange rates, unsupported valuation methods, unapproved perimeters or stale inputs block approval/release. Never default to zero or an arbitrary value.
- **No autonomous audit opinions.** The platform computes, evaluates indicators and enforces gates; human practitioners make professional conclusions and sign-offs.
- **Capability-focused files.** Large services and `AuditSphereDbContext` are partial classes (`<Service>.<Capability>.cs`, `AuditSphereDbContext.<Module>.cs`). Put new operations in the matching capability file; keep public APIs stable; use business-semantic file names.
- **Presentation composes Application.** API handlers and legacy Razor components call named Application commands/queries; no new business-state mutation directly through `DbContext`. Angular uses the API contract and renders server-computed `can*` flags. When substantially modifying a page, move complex reads into a named Application query if that reduces page responsibility; do not bulk-refactor unaffected pages.
- **Gradual Blazor retirement.** Follow discover → inventory → map → compare → close gaps → verify → cut over → observe → remove (`docs/architecture/auditsphere-angular-migration-current.md`). Route existence, story completion or a green partial suite is not parity proof. If the Web project is missing while the gate is `NOT_READY`, restore it before claiming retirement.

## 5. Never

- Introduce Frappe, ERPNext, a Python backend, a React frontend or a second ERP.
- Add Kubernetes, microservices-per-module, message brokers (Kafka/RabbitMQ) or autonomous application-level AI decision-makers.
- Add MediatR, Wolverine, MassTransit, AutoMapper, generic repositories/UnitOfWork, event sourcing or a `Features/` rewrite.
- Request tenant-wide Microsoft Graph scopes outside the separately credentialed, consented capabilities in `docs/architecture/auditsphere-m365-tenant-administration-permissions.md`. Each capability (`User.Read.All` reader, optional `User.Create`, `User.Invite.All`, `GroupMember.ReadWrite.All`, `Mail.Send`) uses its own app identity with exactly one role, off by default, usable only after verified consent. Documents stay on `Sites.Selected` and exact site grants. The isolated client-sites worker's owner-approved `Sites.FullControl.All` exception is documented in `docs/architecture/auditsphere-client-sharepoint-sites-current.md`; its certificate is never mounted in API, Web or the document worker. Never implement Entra administrator-role assignment through AuditSphere roles.
- Make autonomous professional conclusions or bypass required human review gates.
- Add an npm dependency without owner approval (`docs/architecture/auditsphere-angular-conventions-current.md`). NuGet versions are pinned centrally in `Directory.Packages.props` with locked restore; change them deliberately, never as a side effect.
- Rewrite git history destructively, or commit credentials or tenant secrets.
- Convert `BLOCKED_EXTERNAL` to `LOCAL_VERIFIED`/`APPROVED` from local runs, simulations, screenshots or declarations.

## 6. How to work a task

1. Read the story or task card you were given, then only the files it names. Find code with `docs/architecture/auditsphere-architecture-code-map.md` before searching the repository.
2. Decisions and their reasons: `docs/architecture/adr/auditsphere-architecture-index-adr-register-current.md`. Do not reverse an ADR inside a feature task.
3. Deliver the smallest coherent vertical slice: Domain → Application (command/query, scope check, guarded transaction) → migration if needed → API endpoint → OpenAPI regeneration → Angular screen.
4. Run section 3. Report what you ran, what passed, what you could not run, and every assumption.
5. Record observed facts only in `docs/execution/status.json` and `docs/execution/auditsphere-execution-current-slice.md`.

**Context budget.** `docs/execution/status.json` and `docs/execution/auditsphere-execution-current-slice.md` are very large. Never read either whole. Query `status.json` with `jq` (`jq '.active.packageId, .remainingLocalWork[].item, .externalGates' docs/execution/status.json`) and read only the newest section of the current-slice file.

## 7. Authority pointers

- Documentation map, authority order and reading order: `docs/auditsphere-docs-index.md`. Agent context slots: `docs/architecture/auditsphere-architecture-index-agent-context-current.md`.
- Implementation architecture: `docs/architecture/auditsphere-architecture-current-architecture.md`. Angular conventions: `docs/architecture/auditsphere-angular-conventions-current.md`. HTTP boundary and OpenAPI: `docs/architecture/auditsphere-architecture-http-boundary-and-contract-current.md`.
- Functional requirements (STE v2.1): `docs/auditsphere-accounting-module-requirements-current.md`. Product brief and non-goals: `docs/auditsphere-requirements-specification-product-brief-current.md`. Glossary: `docs/auditsphere-requirements-catalog-domain-glossary-current.md`.
- Preserved requirement and blueprint sources under `docs/task_breakdown/source/` are `HISTORICAL_SOURCE`: never implementation authority.
- Backlog: `docs/execution/auditsphere-execution-pending-tasks.md` and `docs/execution/auditsphere-execution-user-stories-ste-v21-remaining-proposed.md`. Volatile facts (verified SHA, counts, blockers): `docs/execution/status.json` only.

## 8. GitHub Wiki deployment guidance

**One guide, updated only when necessary.** Operator deployment guidance lives in the repository Wiki (`https://github.com/nirzaf/AuditSphere/wiki`). Read its index and relevant pages first; update the canonical page in place; create a page only for a missing topic and link it from the index. No per-release copies; no duplication across Wiki, repository docs and `AGENTS.md`. Check documentation impact when prerequisites, configuration keys/defaults, deployment commands, migrations, setup screens, permissions, verification, backup/recovery or upgrade procedures change; edit only sections that became inaccurate; no cosmetic, timestamp-only or changelog edits. Verify against the current checkout (architecture doc, `src/AuditSphereOps.Api/appsettings.json`, Angular administration components, API and rollback-host startup, worker, deployment scripts). Present the Web host only as an approved migration/reference/rollback path, never a production canary. `docs/auditsphere-m365-onboarding-user-stories.md` describes proposed requirements: check implementation before presenting a step as available. Link to the source revision; do not copy specifications or configuration wholesale.

**Make deployment easy to follow.** Plain language, short numbered steps: prerequisites, supported OS/hosting profile, required access, local simulation versus production. Each step gives action, where to run it, expected result and what to do on failure; mark optional and administrator-only steps. Shortest supported path: toolchain and PostgreSQL → safe configuration → restore/build → approved migration → web/worker startup → health and sign-in checks → Microsoft 365 setup and capability verification; link to troubleshooting, upgrades, credential rotation, backup/restore and rollback. Only existing, verified commands with explicit working directory, shell and target environment; label untested steps honestly. Keep local trust authentication, simulation adapters and development defaults out of production instructions. Never recommend bypassing a startup guard or toggling external effects to claim readiness. A saved setup draft is not Microsoft consent, verified SharePoint access or production acceptance.

**Never publish sensitive information.** Treat Wiki text, history, attachments, screenshots, links and examples as potentially public. Never include passwords, client secrets, tokens, bootstrap proofs or hashes, private keys, credentialed connection strings, signed/preauthenticated URLs, production configuration exports, client/financial data or personal information. Replace tenant/app/user/site IDs, private hostnames and resource locations with placeholders such as `<TENANT_ID>` and `<SECRET_FROM_APPROVED_STORE>`; explain that operators supply values through .NET user secrets (development) or an approved secret store/role-specific mount. Before publication inspect the full changed content for sensitive material and validate links and commands; secret scanning is an extra check, not a guarantee. If an exposure is found, stop and notify the owner privately without repeating the value; exposed credentials need revocation/rotation through the approved incident process.

**Publishing and evidence.** Wiki changes are separate from the repository; editing local docs does not publish. Publish only within explicit owner authorization through the existing approved access method, with a minimal diff and a concise reason; never change Wiki visibility, permissions or repository protections to gain access. If access is unavailable, report the topic and blocker without claiming an update. After an authorized update, verify the rendered page and navigation and report the page URL, material change and checks performed. Record a tested version/date only with new evidence, and distinguish local verification from live deployment acceptance.
