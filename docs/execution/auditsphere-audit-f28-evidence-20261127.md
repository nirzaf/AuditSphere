# F28 / PRA-007 / PRA-008 client maintenance evidence (2026-11-27)

## Disposition

**IMPLEMENTED — source slice only.** This implements reviewed client profile and existing contact maintenance through the native CRM writer. It does not implement directory provisioning, portal grants, legal-entity merge, terms execution, client offboarding, or a professional approval policy. No production database or provider was used.

## Source and behavior

- Audit baseline: `62059aa2e622bcb6245a211380021f5cf315cd97` (2026-11-26).
- Starting checkout and `origin/master`: `a34fd274fc4bcd496e51ca1cd64389ea7a21b7d9` (F07 statement-layout setup).
- Implemented source commit, pushed to `master`: `8f1ba239d8a189c54d58a4c54a4429a73abcec36`.
- Pre-change condition: the CRM exposed writers but no reviewed native profile/contact maintenance command, actor-owned exact-result receipt, or UI recovery path for these edits.
- Added `ClientMaintenanceWorkspace` with separate PROFILE and CONTACT state/preview/execute/lookup operations. It binds normalized field diffs, exact client/contact scope, generation and primary-contact set to a review basis; rechecks authority around the firm → actor → client-safety locks; joins or owns the existing transaction; and commits the writer change with one immutable actor/session-epoch receipt. Exact retries recover the committed result; changed intent conflicts. A requested primary contact replacement updates only the reviewed same-client primary set. Invalid/stale/revoked requests have no partial profile/contact/primary/receipt effects.
- Input validation retains existing required/email/date rules and applies only mapped storage limits: tax registration and entity type 64 characters, phone 32, title 100 and signatory authority 64. Unbounded text columns receive no invented per-field maximum; HTTP and retained-snapshot size bounds remain in force.
- `PracticeCrmService.UpdateClientProfileAsync` now joins an ambient workspace transaction. The new maintenance API uses existing authenticated `ReadAsync` / `CommandAsync` boundaries and exact receipt lookup. Angular adds reviewed profile/contact panels and retained-intent recovery; it clears pending drafts and assent on identity change and does not create access grants or treat signatory/scope metadata as verification.
- The new receipt migration enforces scoped restrictive keys, uniqueness and append-only UPDATE/DELETE/TRUNCATE guards. Downgrade refuses when receipts exist. No migration was applied outside the isolated test database.

## Changed files

- Application: `src/AuditSphereOps.Application/Practice/ClientMaintenanceWorkspace.cs`, `src/AuditSphereOps.Application/Practice/PracticeCrmService.Relationships.cs`, `src/AuditSphereOps.Application/Operations/Contracts.cs`.
- Domain / persistence: `src/AuditSphereOps.Domain/Practice/ClientMaintenanceReceipt.cs`, `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.ClientMaintenance.cs`, `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.cs`, migration `20261126211715_ClientMaintenanceReceipts` and its designer, and `AuditSphereDbContextModelSnapshot.cs`.
- API / UI / contract: `src/AuditSphereOps.Api/Ui/UiEndpoints.ClientMaintenance.cs`, `src/AuditSphereOps.Api/Ui/UiEndpoints.cs`, `src/AuditSphereOps.Ui/src/app/features/clients/client-relationships.ts`, `.html`, `.spec.ts`, and generated `contracts/auditsphere-openapi.json`.
- Focused tests: `tests/AuditSphereOps.Domain.Tests/ClientContactCreationTests.cs`, `tests/AuditSphereOps.Api.Tests/ClientContactCreationApiTests.cs`.
- Current architecture owners: `docs/architecture/auditsphere-architecture-code-map.md`, `docs/architecture/auditsphere-architecture-contract-data-model-current.md`.

## Verification

All commands below used source from the working tree based on the starting SHA above, except the separately identified F07 CI run.

- Domain / isolated PostgreSQL: `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~ClientContactCreationTests'` — **8 passed**. Covers exact concurrent retry, changed-intent conflict, stale profile generation, invalid contact input, authority loss rollback, receipt scope/epoch, direct SQL append-only refusal, and downgrade refusal with retained receipts.
- API / isolated PostgreSQL: `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~ClientMaintenanceRoutesRequireReviewedExactDiffAndPersistScopedProfileAndContact'` — **1 passed**. Covers the authenticated route, CSRF, review/execute, exact replay, changed retry, receipt lookup, primary replacement and foreign-contact refusal.
- Angular: `npm --prefix src/AuditSphereOps.Ui run test:ci -- --include 'src/app/features/clients/client-relationships.spec.ts'` — **11 passed**. Covers both panels, identity reset, empty/error views and unknown-outcome exact lookup without blind resend.
- Angular production build: `npm --prefix src/AuditSphereOps.Ui run build` — **passed**. Existing CSS budget warnings remain in `commercial/settings.scss` and `app.scss`; the changed client feature introduced no build error.
- EF model drift: `dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Api --configuration Release` — **passed; no pending model changes**.
- OpenAPI generation and contract gate: `dotnet run --project tools/AuditSphereOps.OpenApiEmitter --configuration Release --no-restore -- contracts/auditsphere-openapi.json` and `bash scripts/contracts/verify-openapi.sh` — **passed**.
- Prior F07 exact-source fast CI: run `45895636132` passed for SHA `a34fd274fc4bcd496e51ca1cd64389ea7a21b7d9`. This is not F28 evidence.
- Interactive browser workflow: **NOT_RUN**. Exact-source fast CI run `45901158827` on `8f1ba239d8a189c54d58a4c54a4429a73abcec36` had an Angular job failure after a failed-job rerun: 199/201 test files and 2,155/2,158 tests passed; the three failures were five-second timeouts in `audit/plan.spec.ts` and `practice/reminder-rules.spec.ts`. The .NET Release job was still running at this observation. This is a failed Angular CI job, not a full-suite pass; the focused checks establish only their respective boundaries.

## Remaining gates

The migration is source/model verified and exercised in the isolated PostgreSQL test cohort; it has not been applied to production. Exact-source fast CI is not green because its Angular job timed out in `plan.spec.ts` and `reminder-rules.spec.ts`. PRA-010 legal-entity merge and PRA-157 final offboarding remain separate `BLOCKED_DECISION` leaves. No release, deployment, merge, production mutation, tenant permission change, or professional sign-off was performed.
