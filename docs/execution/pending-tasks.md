# AuditSphereOps — Pending Tasks to Complete

**Authoritative Specification:** `AuditSphereOps_NET_Codex_Implementation_Specification.md` (v5.0)  
**Status Baseline:** 30 applied migrations, 161/161 passing tests on PostgreSQL 18.6, with the local Records Archive, repository-binding/provider-fence, and recovery-quarantine slices verified on branch `codex/records-archive-slice`.

---

## 1. Executable Implementation Backlog (In Dependency Order per §46.3)

### Slice 3 — Records Profile, Archive Manifest, Retention & Legal Hold Evidence (Backlog Item 3, Part 2)
> **Goal:** Fulfill spec §§25.2–25.7 and replace static record state with real records profiles, canonical archive manifests, structured export, and typed legal holds.

- [x] **3a. Domain Models & State Machine (§25.2, §25.4, §25.6) — local enforcement implemented**
  - In `src/AuditSphereOps.Domain/Records/Records.cs`:
    - `RecordsProfile`: `Id`, `Version`, `Class`, `RetentionTrigger`, `RetentionDurationYears`, `ProtectionMode`, `LabelId`, `DispositionOwner`, `BackupRequirement` (approved versions only, never hard-coded years).
    - `ArchiveManifest`: `Id`, `FirmId`, `ClientId`, `EngagementId`, `ProfileId`, `ProfileVersion`, `ManifestDigest`, `Status`, `CreatedAt`.
    - `ArchiveManifestEntry`: `Id`, `FirmId`, `ManifestId`, `ArtifactId`, `ArtifactClass`, `Sha256Hex`, `ByteCount`, `StructuredExportRef`.
    - `RecordsAction`: `Id`, `FirmId`, `ManifestId`, `DesiredLabel`, `ObservedLabel`, `ObservedProtectionState`, `VerificationTime`, `OperatorIdentity`, `ExceptionMessage`.
    - `LegalHold`: typed extension of `engagement_holds` (`Scope`, `Authority`, `RequestedState`, `AppliedState`, `ObservedState`, `ReleasedState`, `ExternalConfirmationRef`). Holds block disposal but permit preservation (VX-10).
    - Extend `Archive` with the §25.4 state machine: `ISSUED` → `ASSEMBLY_IN_PROGRESS` → `MANIFEST_BUILT` → `ASSEMBLY_REVIEWED` → `RECORDS_ACTION_REQUESTED` → `PROTECTION_OBSERVED` → `ARCHIVE_VERIFIED`.
  - Add `DbSet` properties to `IAuditSphereDbContext` and `AuditSphereDbContext`.
  - Configure alternate keys `(FirmId, Id)` and composite scope FKs `(FirmId, ClientId, EngagementId)` with `ON DELETE RESTRICT`.
    - Add DB CHECK forbidding `ARCHIVE_VERIFIED` without a persisted protection state and observation time. ✅
    - Records-action request/observation history is now captured in versioned append-only evidence rows; manifest/versioned re-archive history remains a separate hardening item.

- [x] **3b. Structured Export Service (§25.3) — local implementation complete**
  - `RecordsArchiveService.BuildManifestAsync` in `src/AuditSphereOps.Application/Records/` now:
    - Produces canonical, digest-stable structured export of §25.3 lists from real persisted rows:
      - Assessments, responses, and decisions.
      - Trial balance, mapping, and adjustment snapshots with provenance.
      - Risks, procedures, populations, submissions, and findings.
      - Review points, approvals, and dependency edges.
      - Release events and manifest hashes.
    - Reuses canonicalization and SHA-256 discipline proven in `FinancialStatementService`.
    - Validates document-reference completeness before advancing to `MANIFEST_BUILT`; reports gaps rather than fabricating readiness. The canonical payload now persists row-level TB/mapping/adjustment, questionnaire, review-point, release and activity-event coverage in `archive_structured_exports`.

- [x] **3c. Records Archive UI (§43.1)**
  - Implement `/app/records/archives/{id}` (`RecordsArchive.razor`):
    - Renders desired vs observed state per manifest item.
    - Renders applied profile version, hold state, and records action exceptions.
    - Truthfully displays empty/pending sections where observations have not occurred. ✅

- [x] **3d. EF Core Migration & Tests — local verification complete, residual hardening open**
  - Migrations `20260919123339_RecordsArchiveWorkflow`, `20260919124558_ArchiveProtectionObservation`, `20260919134031_ArchiveStructuredExports`, and `20260919134442_RecordsActionEvidence`:
    - Preflight guards against ambiguous legacy archive rows.
    - Composite scope FKs and CHECK constraints, including the database refusal of `ARCHIVE_VERIFIED` without observation. ✅
    - `RecordsArchiveTests` cover persisted structured manifests, the ordered state sequence, requested-vs-observed protection, legal-hold observation, and the database protection check. ✅
  - Append-only records-action evidence is now persisted and PostgreSQL-protected by migration `20260919134442_RecordsActionEvidence`; manifest/export versions increment under the locked archive row. Disposition-package blocking remains open; archive/export down-migrations now refuse to discard existing evidence.

**Observed local Records Archive evidence (2026-09-19):** build passed with 0 warnings/errors; the full PostgreSQL 18.6 suite passed 161/161 with 0 skipped; local `auditsphere` has 30 migrations through `20260919134442_RecordsActionEvidence`; `scripts/db/restore-drill.sh` restored 30 migrations with that latest ID; web `/health/ready` and `/health/live` returned Healthy/200. This is local enforcement evidence, not Purview or production acceptance.

---

### Slice 4 — Provider Boundary, Repository Binding & Startup Fence (Backlog Item 1, Non-Live Half)
> **Goal:** Fulfill spec §27.2, §43.5, and §43.8. Close data-model repository binding gaps and enforce fail-closed startup fences without fabricating live Microsoft delivery.

- [x] **4a. Domain Models & Schema (§27.2) — local enforcement implemented**
  - In `src/AuditSphereOps.Domain/Documents/Documents.cs`:
    - `RepositoryBinding`: `Id`, `FirmId`, `ClientId`, `EngagementId`, `TenantId`, `SiteId`, `DriveId`, `RootFolderId`, `Classification`, `DesiredAccess`, `ObservedAccess`, `CapabilityProfile`.
    - `SyncCursor`: `Id`, `FirmId`, `BindingId`, `Cursor`, `Generation`, `LastSyncAt`.
    - `IntegrationCapability`: `Id`, `FirmId`, `BindingId`, `HealthStatus`, `TestedPermissions`, `TestedAt`.
    - Add composite FK from `DocumentReference` to `RepositoryBinding` ("Runtime IDs must belong to an authorized binding"). ✅

- [x] **4b. Signature-Only Provider Boundaries (Fail-Closed) — local boundary implemented**
  - In `src/AuditSphereOps.Infrastructure/Providers/`:
    - `GraphPbcProviderSink : IPbcProviderSink`: signature-only skeleton throwing `OperationBlockedException("live-provider-not-approved")` before any network call.
    - `GraphReleaseCheckpointStore : IReleaseCheckpointStore`: signature-only skeleton throwing `OperationBlockedException("live-provider-not-approved")`.
    - The Graph-shaped boundaries throw `live-provider-not-approved` before any network call. Local/test stores remain the only executable composition; no tenant-wide scopes (`Sites.Read.All`) were added.

- [x] **4c. Startup Fence (§43.8) — fail-closed locally**
  - Extend `WorkerOptions.Validate` and Web startup validation:
    - Production / Development with `ExternalEffects.Enabled=true` refuses simulation sinks (`SimulationPbcProviderSink`, `LocalAppendOnlyCheckpointStore`).
    - Refuses null required identity, DB connection, or deployment epoch values. ✅

- [x] **4d. Integration Tests (NT-23) — blocked runner and scope tests**
  - `TenantIntegration` test category runner emitting `BLOCKED` with provenance when configuration is absent (spec §44.4 return code 2 semantics).
  - `scripts/verify-tenant.sh --environment <name>` emits secret-free `BLOCKED` provenance and exits 2 when prerequisites are absent; provider-boundary and repository-binding FK tests pass locally. Live grant acceptance remains external.

---

### Slice 5 — Cross-Store Recovery Rehearsal & Reconciliation (Backlog Item 4, Locally Honest Subset)
> **Goal:** Fulfill spec §24.4, §45.5, and rule VX-11. Prove the cross-store reconciliation rule locally without claiming production custodial separation.

- [x] **5a. Domain Models & Recovery State — local quarantine state implemented**
  - In `src/AuditSphereOps.Domain/Completion/Completion.cs`:
    - `RecoverySession`: `Id`, `FirmId`, `RestorePoint`, `ExternalEpoch`, `ReconciliationScope`, `Findings`, `ApprovedRestartAt`, `ApprovedByUserId`.
    - Extend `FirmSafetyState` with `RecoveryEpoch` and support `OperatingMode = "RECOVERY_QUARANTINE"`. ✅

- [x] **5b. Operation Recovery Service Reconciliation — local approval fence implemented**
  - Extend `src/AuditSphereOps.Application/Operations/OperationRecoveryService.cs`:
    - On database restore, set `OperatingMode = "RECOVERY_QUARANTINE"`.
    - `BeginRecoverySessionAsync` quarantines a firm and persists the restore point, external epoch, scope and findings; `ApproveRecoveryRestartAsync` requires persisted findings before advancing the deployment epoch.
    - Existing operation-store epoch/quarantine guards refuse stale-worker publication and claims. External checkpoint-store read-back remains unrun locally because no custodially separate store is configured.

- [x] **5c. Script & Evidence Generation — local rehearsal evidence implemented**
  - Update `scripts/db/restore-drill.sh`:
    - Preserve loopback guard and drop-only-generated-artifacts behavior.
    - Compare source/restored release-checkpoint database count + digest summaries.
    - Write machine-readable drill record (timestamps, counts, digests, pass/fail) to `docs/evidence/restore-drill-latest.json` (secret-free per §46.7). The record explicitly marks external custody and production RPO/RTO as not run.
  - Update `docs/execution/restore-drill.md` stating plainly that loopback files are not custodially separate and that this rehearsal proves the reconciliation rule only.

- [x] **5d. Tests — local quarantine/epoch coverage**
  - Restored older DB + newer checkpoint → quarantine and delivery replay blocked.
  - Matching checkpoint → releasable after authorized restart.
    - Old-epoch worker completion denied by existing durable outbox tests; recovery-session approval is covered by `OperationRecoveryTests`. Full cross-store acceptance remains external.

**Observed local Slice 4/5 evidence (2026-09-19):** migrations `20260919130133_RepositoryBindingAndCapabilities`, `20260919130821_RecoverySessionQuarantine`, `20260919134031_ArchiveStructuredExports`, and `20260919134442_RecordsActionEvidence` applied; `dotnet build AuditSphereOps.slnx` passed with 0 warnings/errors; full PostgreSQL 18.6 suite passed 161/161 with 0 skipped; `scripts/verify-tenant.sh --environment staging` returned the required `BLOCKED` status with exit 2 when approved tenant prerequisites were absent; and `scripts/db/restore-drill.sh` produced `docs/evidence/restore-drill-latest.json` with 30/30 migrations and equal source/restored checkpoint summaries. This remains local evidence, not live provider, Purview, custodial checkpoint, or production RPO/RTO acceptance.

---

## 2. Blocked External & Acceptance Gates (Not Executable by Implementer Alone)

> [!WARNING]
> These gates require external resources, tenant grants, or independent professional actions. Per spec §47.5, these must be recorded as **BLOCKED**, never faked or bypassed.

- [ ] **Gate 1: Live Microsoft Entra & Selected SharePoint Resource Grants — PARTIAL**
  - *Observed in EasyGuide:* App registration `AuditSphereOps Development` (`29be1ee5-e90c-4ecc-b4f9-5bcce14774cc`) has `Sites.Selected` admin consent. Graph Explorer returned `201 Created` for a `write` grant to the selected `AuditSphere Development` site (`easyguide.sharepoint.com,4668a3d6-8c1a-462d-accc-f95e7534aae5,b3c5cb09-7b20-4270-b352-aa4ca4db3722`) and a follow-up permission read returned `200 OK` with the same application ID and `roles: ["write"]`. No tenant-wide `Sites.Read.All` grant was added.
  - *Still missing:* Production OIDC/provider credentials in an approved secret store and live provider/acceptance evidence.
  - *Owner Action:* Microsoft 365/operations owner supplies non-production-safe credentials and completes the approved live integration acceptance cycle.

- [ ] **Gate 2: Microsoft Purview Production Records Profile & Label Behavior**
  - *Observed in EasyGuide:* The new Purview portal loaded for `qts@easyguide.onmicrosoft.com`; Information Protection sensitivity labels and Data Lifecycle Management retention labels both showed no data. A label draft was inspected and discarded; no policy was created.
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
