# AuditSphereOps — Pending Tasks to Complete

**Authoritative Specification:** `AuditSphereOps_NET_Codex_Implementation_Specification.md` (v5.0)  
**Status Baseline:** 24 applied migrations, 157/157 passing tests on PostgreSQL 18.6, Slice 1 (Audit Planning Scope Integrity) and Slice 2 (Release Checkpoint & Evidence Gates) completed.

---

## 1. Executable Implementation Backlog (In Dependency Order per §46.3)

### Slice 3 — Records Profile, Archive Manifest, Retention & Legal Hold Evidence (Backlog Item 3, Part 2)
> **Goal:** Fulfill spec §§25.2–25.7 and replace static record state with real records profiles, canonical archive manifests, structured export, and typed legal holds.

- [ ] **3a. Domain Models & State Machine (§25.2, §25.4, §25.6)**
  - In `src/AuditSphereOps.Domain/Records/Records.cs`:
    - `RecordsProfile`: `Id`, `Version`, `Class`, `RetentionTrigger`, `RetentionDurationYears`, `ProtectionMode`, `LabelId`, `DispositionOwner`, `BackupRequirement` (approved versions only, never hard-coded years).
    - `ArchiveManifest`: `Id`, `FirmId`, `ClientId`, `EngagementId`, `ProfileId`, `ProfileVersion`, `ManifestDigest`, `Status`, `CreatedAt`.
    - `ArchiveManifestEntry`: `Id`, `FirmId`, `ManifestId`, `ArtifactId`, `ArtifactClass`, `Sha256Hex`, `ByteCount`, `StructuredExportRef`.
    - `RecordsAction`: `Id`, `FirmId`, `ManifestId`, `DesiredLabel`, `ObservedLabel`, `ObservedProtectionState`, `VerificationTime`, `OperatorIdentity`, `ExceptionMessage`.
    - `LegalHold`: typed extension of `engagement_holds` (`Scope`, `Authority`, `RequestedState`, `AppliedState`, `ObservedState`, `ReleasedState`, `ExternalConfirmationRef`). Holds block disposal but permit preservation (VX-10).
    - Extend `Archive` with the §25.4 state machine: `ISSUED` → `ASSEMBLY_IN_PROGRESS` → `MANIFEST_BUILT` → `ASSEMBLY_REVIEWED` → `RECORDS_ACTION_REQUESTED` → `PROTECTION_OBSERVED` → `ARCHIVE_VERIFIED`.
  - Add `DbSet` properties to `IAuditSphereDbContext` and `AuditSphereDbContext`.
  - Configure alternate keys `(FirmId, Id)` and composite scope FKs `(FirmId, ClientId, EngagementId)` with `ON DELETE RESTRICT`.
  - Add DB CHECK forbidding `ARCHIVE_VERIFIED` with null or unverified observation (`ObservedLabel IS NULL`).
  - Add append-only triggers on manifests, manifest entries, and records actions.

- [ ] **3b. Structured Export Service (§25.3)**
  - Implement `ArchiveExportService` in `src/AuditSphereOps.Application/Records/`:
    - Produces canonical, digest-stable structured export of §25.3 lists from real persisted rows:
      - Assessments, responses, and decisions.
      - Trial balance, mapping, and adjustment snapshots with provenance.
      - Risks, procedures, populations, submissions, and findings.
      - Review points, approvals, and dependency edges.
      - Release events and manifest hashes.
    - Reuses canonicalization and SHA-256 discipline proven in `FinancialStatementService`.
    - Validates referential completeness of the manifest before advancing to `MANIFEST_BUILT`; reports gaps rather than fabricating readiness.

- [ ] **3c. Records Archive UI (§43.1)**
  - Implement `/app/records/archives/{id}` (`RecordsArchive.razor`):
    - Renders desired vs observed state per manifest item.
    - Renders applied profile version, hold state, and records action exceptions.
    - Truthfully displays empty/pending sections where observations have not occurred.

- [ ] **3d. EF Core Migration & Tests**
  - Migration `20260919..._RecordsProfileAndArchiveManifest`:
    - Preflight guards against ambiguous legacy archive rows.
    - Composite scope FKs, CHECK constraints, and append-only immutability triggers.
    - Down migration refuses evidence loss if rows exist.
  - Tests (`RecordsRetentionTests.cs`):
    - Manifest digest determinism.
    - `ARCHIVE_VERIFIED` refused with unverified item (DB CHECK).
    - Profile version preserved on re-archive.
    - Legal hold blocks disposition package while permitting preservation actions.
    - Referential completeness refusal on missing evidence.

---

### Slice 4 — Provider Boundary, Repository Binding & Startup Fence (Backlog Item 1, Non-Live Half)
> **Goal:** Fulfill spec §27.2, §43.5, and §43.8. Close data-model repository binding gaps and enforce fail-closed startup fences without fabricating live Microsoft delivery.

- [ ] **4a. Domain Models & Schema (§27.2)**
  - In `src/AuditSphereOps.Domain/Documents/Documents.cs`:
    - `RepositoryBinding`: `Id`, `FirmId`, `ClientId`, `EngagementId`, `TenantId`, `SiteId`, `DriveId`, `RootFolderId`, `Classification`, `DesiredAccess`, `ObservedAccess`, `CapabilityProfile`.
    - `SyncCursor`: `Id`, `FirmId`, `BindingId`, `Cursor`, `Generation`, `LastSyncAt`.
    - `IntegrationCapability`: `Id`, `FirmId`, `BindingId`, `HealthStatus`, `TestedPermissions`, `TestedAt`.
    - Add composite FK from `DocumentReference` to `RepositoryBinding` ("Runtime IDs must belong to an authorized binding").

- [ ] **4b. Signature-Only Provider Boundaries (Fail-Closed)**
  - In `src/AuditSphereOps.Infrastructure/Providers/`:
    - `GraphPbcProviderSink : IPbcProviderSink`: signature-only skeleton throwing `OperationBlockedException("live-provider-not-approved")` before any network call.
    - `GraphReleaseCheckpointStore : IReleaseCheckpointStore`: signature-only skeleton throwing `OperationBlockedException("live-provider-not-approved")`.
    - Registered only when `ExternalEffects.Enabled=true` AND all selected binding IDs and credentials resolve; otherwise simulation or local stores are used in dev/test. No tenant-wide scopes (`Sites.Read.All` forbidden).

- [ ] **4c. Startup Fence (§43.8)**
  - Extend `WorkerOptions.Validate` and Web startup validation:
    - Production / Development with `ExternalEffects.Enabled=true` refuses simulation sinks (`SimulationPbcProviderSink`, `LocalAppendOnlyCheckpointStore`).
    - Refuses null required identity, DB connection, or deployment epoch values.

- [ ] **4d. Integration Tests (NT-23)**
  - `TenantIntegration` test category runner emitting `BLOCKED` with provenance when configuration is absent (spec §44.4 return code 2 semantics).
  - Tests verifying repository binding scope FK refusals and startup refusals when live mode is requested without grants.

---

### Slice 5 — Cross-Store Recovery Rehearsal & Reconciliation (Backlog Item 4, Locally Honest Subset)
> **Goal:** Fulfill spec §24.4, §45.5, and rule VX-11. Prove the cross-store reconciliation rule locally without claiming production custodial separation.

- [ ] **5a. Domain Models & Recovery State**
  - In `src/AuditSphereOps.Domain/Completion/Completion.cs`:
    - `RecoverySession`: `Id`, `FirmId`, `RestorePoint`, `ExternalEpoch`, `ReconciliationScope`, `Findings`, `ApprovedRestartAt`, `ApprovedByUserId`.
    - Extend `FirmSafetyState` with `RecoveryEpoch` and support `OperatingMode = "RECOVERY_QUARANTINE"`.

- [ ] **5b. Operation Recovery Service Reconciliation**
  - Extend `src/AuditSphereOps.Application/Operations/OperationRecoveryService.cs`:
    - On database restore, set `OperatingMode = "RECOVERY_QUARANTINE"`.
    - Reconcile release events against `IReleaseCheckpointStore` read-back (count + digests).
    - Refuse auto-replay of pending effects whose checkpoint is newer than the restored database.

- [ ] **5c. Script & Evidence Generation**
  - Update `scripts/db/restore-drill.sh`:
    - Preserve loopback guard and drop-only-generated-artifacts behavior.
    - Compare restored release events to the checkpoint store (count + digest).
    - Write machine-readable drill record (timestamps, counts, digests, pass/fail) to `docs/evidence/` (secret-free per §46.7).
  - Update `docs/execution/restore-drill.md` stating plainly that loopback files are not custodially separate and that this rehearsal proves the reconciliation rule only.

- [ ] **5d. Tests (`RecoveryReconciliationTests.cs`)**
  - Restored older DB + newer checkpoint → quarantine and delivery replay blocked.
  - Matching checkpoint → releasable after authorized restart.
  - Old-epoch worker completion denied.

---

## 2. Blocked External & Acceptance Gates (Not Executable by Implementer Alone)

> [!WARNING]
> These gates require external resources, tenant grants, or independent professional actions. Per spec §47.5, these must be recorded as **BLOCKED**, never faked or bypassed.

- [ ] **Gate 1: Live Microsoft Entra & Selected SharePoint Resource Grants**
  - *Missing:* AuditSphere multi-tenant Azure App Registration, selected SharePoint site/drive grants (no tenant-wide scopes), and ClientSecret/Cert credentials in secret store.
  - *Owner Action:* Microsoft 365 Global Admin grants selected site permissions and supplies credentials.

- [ ] **Gate 2: Microsoft Purview Production Records Profile & Label Behavior**
  - *Missing:* Verified Purview records retention label applied to SharePoint libraries, profile definition, and retention event observing pipeline.
  - *Owner Action:* Compliance Officer configures Purview retention labels and supplies profile configuration.

- [ ] **Gate 3: Approved Signing Methodology & Professional Sign-Off Evidence**
  - *Missing:* Approved X.509 signing certificate/HSM, methodology approvals, and professional partner signatures.
  - *Owner Action:* Audit firm technical committee approves methodology and supplies signing credentials.

- [ ] **Gate 4: Independent Human Review & PR Merge Evidence (Checklist #14)**
  - *Missing:* Independent human peer review of merged PRs and outstanding implementation slices.
  - *Owner Action:* Designated independent reviewer conducts review and records sign-off in `docs/execution/status.json`.

- [ ] **Gate 5: §47 Real-Tenant Production Acceptance Cycle & Production RPO/RTO**
  - *Missing:* Real-tenant end-to-end acceptance run (§47) and isolated cross-datacenter backup/recovery rehearsal with named custodians.
  - *Owner Action:* Operational team executes recovery rehearsal across distinct cloud environments.
