# F07 / AC-LAY-01 statement-layout producer evidence

Observed 2026-11-26 UTC. This record covers the source-confirmed engineering slice only. It does not close accounting policy, release or production acceptance.

## Source and disposition

- Baseline: `62059aa2e622bcb6245a211380021f5cf315cd97`.
- Pre-change `HEAD` and fetched `origin/master`: `a5f4db3d0a9cf35e4990495197c725bd6657f061`.
- Disposition: **IMPLEMENTED locally; exact-source CI pending commit/push.**
- `./scripts/agent-check --list` returned `engagement-ui`, `review-permissions` and `accounting-calculation`; no statement-layout packet matched.

## Behavior

The empty-layout setup surface now provides bounded client-scoped catalogue/detail, typed draft save, validation, independent publication and actor/epoch/scope-bound receipt recovery. Mutations recheck authorization after the existing firm and client safety locks, lock the target layout for review, and persist the mutation with an append-only receipt. Exact retries return the retained result; changed request intent conflicts. Reviewers can read and publish the exact layout, while the author cannot publish it. Existing published layouts remain immutable, and financial presentation still selects only a published layout for the exact client and framework.

The Angular route is `/ui/app/accounting/clients/:clientId/statement-layouts`. Acknowledging a successful save reopens the exact returned draft revision and updates the edit baseline, so a completed save no longer leaves a false unsaved-change warning. The journey starts with the existing package fixture and creates the layout under test through the real API/UI flow; it does not directly seed layout rows. It then selects the published layout for that package and downloads a nonempty PDF draft. This is not a reviewed, sealed, issued or released statement.

## Changed files

- API: `src/AuditSphereOps.Api/Ui/UiEndpoints.StatementLayouts.cs` (new), `UiEndpoints.cs`, `UiEndpoints.Infrastructure.cs`.
- Application: `src/AuditSphereOps.Application/Accounting/StatementLayoutWorkspace.cs` (new), `StatementLayoutService.cs`, `Operations/Contracts.cs`.
- Domain/persistence: `src/AuditSphereOps.Domain/Accounting/StatementLayoutCommandReceipt.cs` (new); `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.StatementLayoutCommands.cs` (new), `AuditSphereDbContext.cs`, `Migrations/20261126203754_StatementLayoutCommandReceipts.cs` and `.Designer.cs` (new), `Migrations/AuditSphereDbContextModelSnapshot.cs`.
- Angular: `src/AuditSphereOps.Ui/src/app/features/accounting/statement-layouts.ts` and `.spec.ts` (new), `app.routes.ts`, `features/accounting/workspace.ts`.
- Regression coverage: `tests/AuditSphereOps.Api.Tests/StatementReviewApiTests.cs`; `tests/AuditSphereOps.Domain.Tests/AuthorizationDecisionTests.cs` and `StatementLayoutGrammarTests.cs`; `tests/AuditSphereOps.E2E.Tests/AngularFinancialArtifactParityJourneyTests.cs`.
- Contract and handoff: `contracts/auditsphere-openapi.json`; `docs/architecture/auditsphere-architecture-code-map.md`; `docs/execution/auditsphere-execution-current-slice.md`; `docs/execution/status.json`; this evidence file.

Four concurrent unrelated Angular specs were already modified and were not changed or staged by this slice: `features/admin/tenant.spec.ts`, `features/audit/plan.spec.ts`, `features/practice/resources.spec.ts` and `features/practice/work-queue-availability.spec.ts`.

## Verification

All PostgreSQL-backed tests used the isolated `auditsphere_tests` database at local port 5433 (login `qts`). The provider, production and development databases were not used.

- `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~StatementLayoutServiceTests.LayoutDraft_PublishesIndependentlyAndIsImmutable|FullyQualifiedName~StatementLayoutGrammarTests' /p:UseSharedCompilation=false` — **PASS, 8/8**.
- `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~StatementReviewApiTests.StatementLayoutSetupCreatesAndPublishesThroughScopedCommands' /p:UseSharedCompilation=false` — **PASS, 1/1**.
- `npm --prefix src/AuditSphereOps.Ui run test:ci -- --include 'src/app/features/accounting/statement-layouts.spec.ts'` — **PASS, 4/4**.
- `npm --prefix src/AuditSphereOps.Ui run build` — **PASS**. Existing SCSS size-budget warnings remain in untouched `commercial/settings.scss` and `app.scss`.
- `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~AngularFinancialArtifactParityJourneyTests.FreshClientLayoutIsCreatedAndPublishedBeforePackagePresentationExport' /p:UseSharedCompilation=false` — **PASS, 1/1**. The test launched the authenticated Angular UI and exercised real draft creation, distinct-author publication, persisted receipt/state verification, package presentation and PDF download.
- `dotnet run --project tools/AuditSphereOps.OpenApiEmitter --configuration Release --no-restore -- contracts/auditsphere-openapi.json` and `bash scripts/contracts/verify-openapi.sh` — **PASS**. Structural comparison to the parent contract: five paths and eleven schemas added; no existing paths/schemas removed or changed.
- `dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Api --configuration Release` — **PASS**, no model drift. The focused PostgreSQL tests exercised the new migration against the isolated database; no production migration was applied.
- `python3 scripts/ui/inventory.py --check` — **PASS**.
- `python3 scripts/docs/validate-markdown-documentation.py`, `python3 scripts/docs/validate-markdown-filenames.py`, `python3 scripts/docs/validate-narrative-metrics.py`, and `git diff --check` — **PASS**. The documentation size validator initially reported the two existing ledgers only 331 and 535 bytes below their limits; the detailed evidence was moved here and the handoff/status entries shortened. The final validator verified 200 Markdown files, 3082 links and the ledger size budgets.
- `./scripts/agent-check --list` — **PASS**, no matching statement-layout packet.

## Remaining gates

- Exact-source fast CI is **NOT_RUN** until the scoped F07 changes are committed and pushed; record the resulting SHA and workflow run before attributing CI evidence.
- The overall remediation assignment remains **INCOMPLETE**. Other assigned ready leaves and required blocked dispositions remain.
- No merge, release, deployment, production migration, provider action, external acceptance or professional/accounting sign-off was performed or authorized by this slice.
