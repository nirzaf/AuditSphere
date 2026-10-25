# SharePoint-primary working-document implementation

**Status:** CURRENT

**Purpose:** Source-backed storage inventory, dependency map and execution handoff for the owner-assigned [functional-parity storage specification](../architecture/auditsphere-sharepoint-primary-storage-functional-parity-spec-proposed.md), which supersedes the [earlier specification](../architecture/auditsphere-sharepoint-primary-storage-implementation-spec-proposed.md).

The change is storage-only: site provisioning, rollout cutoff, staff Full Control, folder taxonomy, Microsoft identities and permissions, PBC flows, limits, routes and contracts, and ADR-0009 archive authority are unchanged. The [proposed ADR-0019](../architecture/adr/auditsphere-architecture-adr-0019-sharepoint-primary-working-documents-proposed.md) records that scope. This handoff is not a live corpus measurement, tenant proof, activation or deletion authorization. Exact source and verification observations belong in `status.json` under `targetedVerification.sharePointPrimary`.

## Storage and consumer inventory

All rows below need measured tenant/host volume before production enrollment. No live content, credentials, tenant metadata or filesystem corpus was read to create this inventory.

| Family / source owner | Current byte location and writer | Readers / retained dependencies | Disposition and remaining work |
| --- | --- | --- | --- |
| PBC upload intents/chunks (`PbcUploadIntent`, `PbcUploadChunk`) | Private staging paths written by `src/AuditSphereOps.Api/ApiHost.DocumentEndpoints.cs`; transfer worker consumes ordered chunks. Simulation sink writes a separate local provider copy. | `PbcService` completion/hash checks; `PbcDocumentTransferHandler`; `GraphSelectedSiteDrive` upload; staff download endpoint; staged schedule reader. `PbcAcceptanceReceipt` pins revisions/hash/provider evidence. | First mandatory remote-primary cohort. No cleanup until every reader is migrated and custody/capacity/cohort gates pass. Simulation copy is not SharePoint evidence. |
| Accepted PBC CSV schedule sources | PBC staging plus retained structured schedule capture in PostgreSQL. `AuditScheduleIntakeSources` resolves the accepted upload and TB; source workspace captures normalized and original-cell evidence. | `AuditScheduleIntakeCatalog`, `AuditScheduleIntakeSources`, `PbcStagedScheduleContentReader`; schedule capture/review/currentness, sampling/item tests and readiness consume their proof. | Migrate physical-byte proof to the exact verified remote snapshot. Keep captured rows, account-control witnesses, accepted TB pins and human decisions as application evidence. No Graph read inside a protected write transaction. |
| Raw TB upload / retained TB rows | `TrialBalanceUploadWorkspace` and import services consume bounded request bytes; PostgreSQL retains datasets, hashes, parsed rows and validation evidence. | TB validation/acceptance, mapping, adjustments, statements, schedule tie-outs and successor source workflows. | Raw-source custody/persistent binary retention needs an explicit disposition; do not infer that a hash retains the original file. Parsed accounting rows and generated NativeTB evidence stay in PostgreSQL. No deletion or raw-file migration yet. |
| Raw GL upload / retained GL rows | `GeneralLedgerUploadWorkspace` and intake/import services consume bounded CSV input and retain import/chunk/row evidence in PostgreSQL. | Independent GL selection/completeness, cash flow, schedules and NativeGL handoffs. | Identify raw-file custody and any resumable payload readers before conversion. Preserve journal/line identities and normalized chunk rows; do not classify business JSON as a binary mirror. |
| Workpapers, document references and snapshots | Existing `DocumentReference` points to a provider item; `DocumentSnapshot` retains version/hash/count metadata, not file bytes. Workpaper services retain authored/reviewed structured state. | `DocumentSnapshotService`, workpaper commands, approvals, linked evidence and release manifests. | Inventory each physical attachment/source link before enrollment. Reuse scoped references/snapshots, preserve version-bound reviews, and record unavailable external content rather than infer availability from metadata. |
| Unissued financial package/presentation artifacts | `FinancialPackageArtifact.ArtifactBytes` and retained presentation/snapshot records in PostgreSQL; accounting renderers own generation. | Statement composition, package/release services, commercial bundle and archive-copy guards. | Distinguish retained drafts from issued/release-bound artifacts before moving any bytes. Keep calculated rows, source snapshots and composition manifests. No disposable classification or cleanup assumed. |
| Commercial documents | `CommercialDocument.Bytes` in PostgreSQL, produced by `CommercialDocumentService`/renderer. | Commercial document download/query, quotation/engagement/invoice/payment and release correspondence paths. | Classify document-by-document with its actual issuance/acceptance lineage. Issued commercial evidence is not disposable scratch; migration requires a separate verified consumer disposition. |
| Firm expense / ledger supporting evidence | `FirmExpense.EvidenceContent` and optional firm-journal supporting bytes in PostgreSQL, written by firm operations/ledger services. | `FirmOperationsServices`, `LedgerService`, `FirmFinanceQuery` and supporting-evidence downloads. | Separate firm-owned storage scope from client working sites. No client-site mapping, custody exclusion or ledger mutation is inferred. Requires SP-08 disposition and a suitable firm-scoped target if enrolled. |
| Signed representations, signed reports, assembled deliverables and bundles | Exact retained signed scans, deliverable content and bundle bytes in PostgreSQL; release/checkpoint stores retain authoritative published evidence. | Deliverable signing/verification, independent Partner decisions, bundle assembly, release export and archive-copy verification. | Preserve ADR-0009 authority and exact uploaded signed bytes. Excluded from mutable working-site replacement and working-staging cleanup; any secondary SharePoint copy remains separately verified. |
| Signatures, firm seals, logos and Microsoft mail attestation bytes | Dedicated scoped PostgreSQL records; certificate/private-key material remains in separately configured secret custody. | Signing/rendering, commercial/portal branding, mail authorization verification. | Security/configuration assets, not PBC working files. Preserve current approval/hash/secret boundaries; no migration to client libraries or cleanup by the working-file process. |
| Temporary read/render/export scratch and checkpoint temporary writes | Process-private or explicitly configured paths; existing stores/renderers own their individual lifetimes. | Download/export consumers, renderer, checkpoint verification and recovery. | Inventory actual shared mounts and proxy buffering; add volume-wide reservations and leases. Never sweep authoritative checkpoint roots as ordinary scratch. |

## Exact PBC local-path dependency map

The bounded source scan excludes generated migration snapshots. Status after the implemented slices:

1. `src/AuditSphereOps.Domain/Documents/Pbc.cs`: retained `StagedPath` provenance on chunk rows. Unchanged and never rewritten; release is recorded separately in `PbcStorageDisposition`/`PbcStorageEvent`.
2. `src/AuditSphereOps.Application/Documents/PbcService.cs`: `PrepareDownloadAsync` returns the exact registered `PbcRemoteSource` once an upload's disposition selects SharePoint, otherwise verified staged `ChunkPaths`; there is no fallback between them. Source-currentness checks use the immutable registered snapshot without provider I/O.
3. `src/AuditSphereOps.Application/Documents/PbcDocumentTransferHandler.cs`: transfer and reconciliation still need staged bytes; cleanup refuses any upload with a non-terminal operation.
4. `src/AuditSphereOps.Infrastructure/Providers/GraphSelectedSiteDrive.cs`: uploads unchanged; `ReadExactVersionAsync` adds the version-fenced exact read used by readers.
5. `src/AuditSphereOps.Api/ApiHost.DocumentEndpoints.cs`: the download serves a verified spool for remote sources and rechecks authority after the remote read; the staged branch is unchanged.
6. `src/AuditSphereOps.Infrastructure/Providers/PbcStagedScheduleContentReader.cs`: unchanged for pre-cutover sources; `PbcScheduleSourceContentReader` dispatches remote sources to the exact registered version.
7. `src/AuditSphereOps.Application/Audit/AuditScheduleIntakeCatalog.cs` and `AuditScheduleIntakeSources.cs`: remote sources are prefetched and verified before the locked capture/review transaction; IO failures replay where the locked path reads.
8. `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Pbc.cs`: adds the registration, disposition and event tables; historical paths and acceptance receipts are preserved.

The API composes the remote reader with the Sites.Selected credential and drive it already uses for workspace provisioning; no new permission, credential or process. `PbcRepositoryBindingResolver` keeps its upload resolver unchanged and adds separate read resolvers for registered versions and backfill candidates.

## Implementation sequence and acceptance ownership

| Story | Current disposition | Next evidence |
| --- | --- | --- |
| SP-00 | Source inventory recorded above. | Measured per-family volume on the real host before production claims. |
| SP-01 | No prohibited source area was changed: site provisioning, membership, folder templates, M365 identities, PBC upload transport, routes and OpenAPI are untouched (`verify-openapi.sh` current). Existing PBC HTTP, transfer, provider and schedule-intake cohorts pass. | Exact-source CI; live site/membership parity observations under authorized tenant access. |
| SP-02 | Implemented: publication registers a scoped reference, exact-version snapshot and append-only registration in the `RECEIVED` transaction. Migration `PbcDocumentRegistrationAndStorageDisposition` installs deferred source guards and append-only triggers; Down refuses while evidence exists. Simulated receipts register nothing. | Exact-source CI for the migration. |
| SP-03 | Implemented: administrator-only per-upload activation/revert (`ActivateRemoteReadAsync`/`RevertToLocalReadAsync`) and exact-version reads behind the existing download with the same headers and staff-only audience. | Real selected-site read observation (operator routes per D-SP-05). |
| SP-04 | Implemented: remote-only accepted schedule sources yield the identical manifest, capture, independent review and currentness without local chunks; mismatched remote bytes fail closed without rewriting approvals. | Exact-source CI; pilot observation. |
| SP-05 | Implemented, disabled by default: `PlanLocalCopyCleanupAsync` dry-run and `ReleaseLocalCopyAsync` (enabled deployments only) with allowlist/link, final-decision, hold and active-operation gates, fresh verified remote read, record-before-delete and retryable deletion. | Owner cleanup authorization; synthetic pilot. |
| SP-06 | Implemented: `PlanRegistrationBackfillAsync` and `BackfillRegistrationAsync` register earlier verified transfers from the completed transfer's recorded identity after an exact-version read-back, without changing upload, request or acceptance evidence. | Run against real retained transfers once authorized. |
| SP-07 | Pending owner authorization: bounded synthetic pilot of activation, remote-only use, cleanup and rollback/rehydration. | Pilot evidence with original files retained first. |
| SP-08 | Pending: each non-PBC family in the inventory needs its own writer/reader disposition before any change. | Per-family inventory and equivalence. |
| SP-09 | Pending: exact-source CI, live selected-site positive/negative proof, workload and restore evidence. | Release-candidate evidence. |

## Decisions and safe defaults

These are the operational decisions from specification §16; none is a functionality redesign.

- D-SP-01 SharePoint working-version retention and recovery: verified hashes do not guarantee site administrators keep the version. Safe default: never remove the only required copy.
- D-SP-02 SharePoint outage parity: after cleanup, downloads and remote-only source reads depend on SharePoint availability. Safe default: keep cleanup off for any class that needs offline availability.
- D-SP-03 VPS temporary budget and recovery: streaming, import, render and remaining archive/checkpoint bytes still need storage. Safe default: measure first; do not reduce upload limits.
- D-SP-04 Pilot cleanup authorization: physical deletion is irreversible locally. Safe default: explicit dry-run review, approval, narrow cohort and independent restore/read verification.
- D-SP-05 Invocation surface: resolved by admin-only, backward-compatible API routes with no UI (`/api/ui/administration/pbc-storage/commands/{state,preview,actions,receipts}` plus `cleanup/plan` and `backfill/plan`) using the reviewed preview/assent/receipt pattern. `Storage:PbcLocalCopyCleanup:Enabled` defaults to false, so release stays refused until a reviewed deployment enables it.
- D-SP-06 Supported rollback: binaries that assume staged `ChunkPaths` are not valid rollback targets after cleanup. Safe default: keep a remote-read-capable build or prove exact rehydration before any release.

Production migration, activation, deletion, tenant changes and deployment still require explicit owner authorization; nothing here grants it.
