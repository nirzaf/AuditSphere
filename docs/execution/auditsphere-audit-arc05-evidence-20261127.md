# ARC-05 commercial bundle selection for archive builds (2026-11-27)

## Disposition

**IMPLEMENTED — source/API/UI handoff; selected-bundle producer API journey locally verified.** The archive catalogue is authorized and bounded, and the UI retains a reviewed exact selection during build recovery. Interactive browser verification remains **NOT_RUN**. F21 terminal-release and records policy remains **BLOCKED_DECISION**; this change does not approve a records profile or enable first-archive eligibility.

## Behavior and scope

- Before this change, archive build UI always sent `commercialBundleId: null` and had no scoped way to choose a retained bundle.
- Added an authenticated metadata-only catalogue, ordered by assembly time and ID, scoped to the exact authorized archive firm/client/engagement, with 25 items per page and a maximum page of 100. The service authorizes before and after the query and rechecks the archive's client/engagement binding.
- The existing archive build route receives the selected ID. UI assent is explicit, shows the selected ID plus content and manifest SHA-256, and clears when selection changes. A bundle cannot be supplied through an arbitrary-ID field.
- Existing version-1 local intent remains compatible with `commercialBundleId: null`; non-null IDs must be GUIDs, and review intents remain null. Lost-response recovery submits the exact retained bundle ID and original request key, independent of the current selection.
- `RecordsArchiveService.BuildManifestCoreAsync` remains the only manifest build owner. Its existing scoped evidence resolver rejects stale/foreign IDs and its retained request-key comparison rejects changed selection. Existing accepted outcomes remain replayable.

## Changed files

- `src/AuditSphereOps.Application/Records/RecordsArchiveService.cs`
- `src/AuditSphereOps.Api/Ui/UiEndpoints.AuditRecords.cs`
- `src/AuditSphereOps.Ui/src/app/features/audit/archive-intent-recovery.ts`
- `src/AuditSphereOps.Ui/src/app/features/audit/records.ts`
- `src/AuditSphereOps.Ui/src/app/features/audit/records.spec.ts`
- `tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj` (links the existing supported completion and bundle test fixtures for the producer journey)
- `tests/AuditSphereOps.Api.Tests/RecordsArchiveRecoveryApiTests.cs`
- `contracts/auditsphere-openapi.json`
- `docs/architecture/auditsphere-architecture-code-map.md`

## Verification

- Isolated PostgreSQL HTTP cohort: `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~RecordsArchiveRecoveryApiTests' -p:UseSharedCompilation=false -nodeReuse:false` — **2 passed**. The new `SelectedProducedBundle_BuildsExactArchiveLineageAndRecoversAfterCommittedResponseLoss` case creates a real five-part bundle through `POST /api/ui/engagements/{id}/completion/bundle`, selects the exact ID returned by the authorized archive catalogue, and submits it through the archive build endpoint. A test-only Kestrel response filter truncates the response after the endpoint returns; PostgreSQL confirms the selected ID, request key, archive assembly/manifest hashes and all part hashes committed. Exact retry returns the captured result without another manifest or entries; changed selection conflicts; a stale ID is refused; an administrator from another firm is denied without revealing bundle availability. The existing null-selection recovery test also passes.
- Angular: `npm --prefix src/AuditSphereOps.Ui run test:ci -- --include 'src/app/features/audit/records.spec.ts'` — **22 passed**. Covers explicit selection/review, changed selection clearing assent, and exact selected-ID/request-key retention across response loss and component recreation.
- Angular production build: `npm --prefix src/AuditSphereOps.Ui run build` — **passed**; existing CSS budget warnings only.
- Existing supported producer and lineage regression: `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~FivePartBundle_RequiresReviewedReleaseAndPostedBalance_IsIdempotent_AndClientScoped'` — **1 passed**. It generates the five-part bundle through the supported command and verifies persisted archive manifest hashes and source lineage at the service boundary; it does not exercise the new selected-ID HTTP build route.
- OpenAPI: `dotnet run --project tools/AuditSphereOps.OpenApiEmitter --configuration Release --no-restore -- contracts/auditsphere-openapi.json` and `bash scripts/contracts/verify-openapi.sh` — **passed**; additive catalogue route and response only.
- `git diff --check` — **passed**. Documentation validators run before commit.
- Interactive browser: **NOT_RUN**. The API integration test does not exercise Angular selection, responsive rendering or browser recovery; no browser acceptance is claimed.

## Remaining acceptance

The supported producer-to-archive HTTP journey is locally verified against isolated PostgreSQL. Exact-SHA CI remains pending; interactive browser verification is still open. The synthetic RecordsProfile/archive fixture is test-only and does not establish or enable the blocked first-archive eligibility policy. No production data, provider, records approval, release or deployment effect occurred.
