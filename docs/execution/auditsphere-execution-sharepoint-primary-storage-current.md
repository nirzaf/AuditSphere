# SharePoint-primary working-document implementation

**Status:** CURRENT

**Purpose:** Source-backed storage inventory, dependency map and execution handoff for the owner-assigned [implementation specification](../architecture/auditsphere-sharepoint-primary-storage-implementation-spec-proposed.md).

The specification remains proposed for production activation. The [proposed ADR-0019](../architecture/adr/auditsphere-architecture-adr-0019-sharepoint-primary-working-documents-proposed.md) preserves existing automated defaults and ADR-0009 archive authority. This inventory establishes engineering boundaries; it is not a live corpus measurement, migration manifest, tenant proof or deletion authorization. Exact source and verification observations belong in `status.json`.

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

The bounded source scan excludes generated migration snapshots, which describe historical schema rather than runtime readers.

1. `src/AuditSphereOps.Domain/Documents/Pbc.cs`: retained `StagedPath` provenance on chunk rows.
2. `src/AuditSphereOps.Application/Documents/PbcService.cs`: `PbcDownload.ChunkPaths`, `PrepareDownloadAsync`, chunk registration and full staged-byte completion verification.
3. `src/AuditSphereOps.Application/Documents/PbcDocumentTransferHandler.cs`: ordered staged plan, pre-effect verification and simulation sink; uncertain transfers still require staged bytes.
4. `src/AuditSphereOps.Infrastructure/Providers/GraphSelectedSiteDrive.cs`: uploads each retained staged chunk. Keep this path for resumable transfers.
5. `src/AuditSphereOps.Api/ApiHost.DocumentEndpoints.cs`: upload chunk publication and staff-only local download streaming.
6. `src/AuditSphereOps.Infrastructure/Providers/PbcStagedScheduleContentReader.cs`: contained regular-file and exact total-byte/hash checks; the source reader must change before cleanup.
7. `src/AuditSphereOps.Application/Audit/AuditScheduleIntakeCatalog.cs` and `src/AuditSphereOps.Application/Audit/AuditScheduleIntakeSources.cs`: use the download descriptor for listing availability and accepted-source content/currentness.
8. `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Pbc.cs`: retains bounded path metadata. Historical paths and acceptance receipts are preserved after any future disposition transition.

API composition currently injects the staged schedule reader in `src/AuditSphereOps.Api/ApiHost.Providers.cs`. The API's external-effect safety setting remains unchanged. A remote read resolver must permit authorized accepted/closed sources without broadening transfer eligibility in `PbcRepositoryBindingResolver`.

## Implementation sequence and acceptance ownership

| Story | Current disposition | Next evidence |
| --- | --- | --- |
| SP-00 | Source inventory and proposed ADR recorded; runtime measurements and policy adoption remain open. | Documentation validation; operator-owned volume/custody/role decisions. |
| SP-01 | Pending. Automated site behavior remains unchanged. | Reviewed manual verification/activation, exact-scope denial and mode-race evidence; production adoption decision. |
| SP-02 | Next independent engineering slice. | Atomic transfer-to-reference/snapshot association, same-scope DB guards, replay/conflict and accepted-history preservation. |
| SP-03 | Pending SP-02. | Verified bounded exact-version spool, explicit read disposition, final authority fence and no substitute-byte release. |
| SP-04 | Pending SP-03. | Remote-only source capture/currentness, identical rows/tie-outs and no provider I/O under business write locks. |
| SP-05 | Pending SP-03/04 and custody/capacity decisions. | Shared reservations, leases, dry-run manifests and crash-safe audited cleanup; deletion disabled until authorization. |
| SP-06 | Pending SP-02. | Scoped metadata-only delta/cursor replay, reset and external-change observations. |
| SP-07 | Pending read, registration, quota and custody prerequisites. | Synthetic pilot, resumable manifest, exact remote-only use and supported rollback/rehydration. |
| SP-08 | All non-PBC rows retained above; conversion/operational handover pending. | Per-family verified disposition, operator staff-access procedure and retained/accepted archive capability. |
| SP-09 | Pending implementation and qualified deployment evidence. | Applicable SP-AC matrix on one immutable candidate, real selected-site positive/negative proof, mounted-volume and recovery rehearsal. |

## Decisions and safe defaults

- D-SP-01: owner adoption of manual-management responsibility remains unrecorded; keep automated mode/defaults.
- D-SP-02: name the administrator responsible for granting/removing direct staff access. Local application revocation never claims remote removal.
- D-SP-03: record version retention, backup/recovery and required authoritative-archive capture per family before removing the only retained copy.
- D-SP-04: retain the existing isolated archive-copy/grant-verification capability until its accepted handover. No operator assurance becomes provider `OBSERVED` automatically.
- D-SP-05: actual mounted-volume capacity, physical/global/per-firm budgets, headroom, proxy buffering and approved cleanup cohort are unmeasured/unapproved; cleanup remains disabled.
- D-SP-06: keep optional Microsoft identities until actual sign-in/onboarding/mail/archive consumers have an accepted disposition.

These are production/acceptance dependencies. Independent reversible engineering proceeds with current defaults intact, no tenant writes, no deployment and no local project test cases; verification uses the existing CI workflows.
