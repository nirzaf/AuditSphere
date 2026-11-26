# F01 consolidation match reservation evidence — 2026-11-26

**Disposition: IMPLEMENTED.** The exact consolidation reservation feature was already present on current master in `bd32b3aa103db2f020e5dd25204aae108d45a705`: approved journals reserve each linked intercompany match atomically; scoped keys/FKs prevent cross-group reuse; run calculation validates ownership and suppresses duplicate automatic elimination. The migration preflights contradictory history and backfills only a single valid approved owner.

The required PostgreSQL workflow test exposed a defect in the existing row refresh: removing a tracked journal from `DbSet.Local` marked it deleted and severed its required journal-line relationship. `ApproveConsolidationJournalAsync` now refreshes a clean tracked row's current and original values from the locked database row in place. A journal with caller-side pending changes returns `revision.stale`; an untracked row is attached normally. Lock order, authority recheck, transaction ownership, reservations, and component-book immutability remain intact.

## Verification

- Before correction, the exact PostgreSQL test failed at `TrackFreshJournal` with EF's required-relationship severed exception.
- After correction: `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~GroupWorkflow_UsesApprovedComponentPackagesWithoutMutatingThem'` — 1/1 passed.
- `dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Api --configuration Release` — no model changes since the migration snapshot.

The F01 migration is present in source but was not applied to a shared or production database in this slice. Exact-source CI will be recorded after the correction is pushed; no external or professional acceptance is claimed.
