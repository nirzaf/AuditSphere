# F12 local verification evidence — 2026-11-26

F12 (`READY_CONTAINMENT`) blocks generic trial-balance/general-ledger self-comparison from creating a new `RECONCILED` record without an independent control population. Retained proofs are not rewritten. Native AR/AP and typed bank flows remain separate; AC-REC-07–10 remain `BLOCKED_DECISION` pending an approved independent-source pairing and adjustment-basis contract.

Focused checks recorded for commit `9704395d`:

- API: `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~ReconciliationReviewApiTests'` — 3/3 passed.
- Domain: `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~ClientAccountingTests.ClientChartsPeriodsAndGlImport_AreTypedScopedAndClosedSafely'` — 1/1 passed.
- Angular: `npm --prefix src/AuditSphereOps.Ui run test:ci -- --include 'src/app/features/accounting/reconciliation-create.spec.ts'` — 1/1 passed.

Exact-source fast run `45873866146` was cancelled when newer source was pushed. This evidence is local focused verification only; no migration, browser, production, or independent-control acceptance is claimed.
