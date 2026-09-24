[Master index](../00_INDEX.md) · [Original module in source](../source/ORIGINAL_R2R_Blueprint_Modules_20-26.md#module-25)

**Full module contract — required reading for its tasks.** No task may omit an applicable invariant merely because the task card is shorter. Names retain the original EXISTING / EXTEND / NEW / DECISION labels.

<!-- SOURCE-LINES: 965-1079 -->
## Module 25 — Financial Packages, Artifacts and Approval Decisions

**Business outcome:** assemble and review the exact set of files that will be handed to controlled release, with deterministic lineage and no silent regeneration.  
**Requirement coverage:** VP-042; integrates with existing reviews, completion and archive.  
**Sequence:** M25.1 composition → M25.2 render → M25.3 validate/seal → M25.4 exact-stage decisions → M25.5 release handoff.

<a id="domain"></a>
### 1. Domain Modeling (`.Domain`)

**EXISTING/EXTEND:** `FinancialPackage`, `FinancialPackageArtifact`, package lines/validations and `FinancialPackageReviewDecision`; existing render/build durable operations and renderer profiles. Do not classify the current OpenXML/PDFsharp-MigraDoc output as absent or replace it merely to implement a new facade. [R3](../reference/02_Standards_and_Source_Register.md#source-r3)[R4](../reference/02_Standards_and_Source_Register.md#source-r4)[R5](../reference/02_Standards_and_Source_Register.md#source-r5)

**NEW only where missing:** immutable `PackageCompositionRevision`, `ArtifactManifest` and `RenderAttempt` facts, or equivalent extensions to existing package records. Separate the canonical accounting result from the ordered presentation/export definition.

A package definition contains scope kind Entity/Group, exact StatementSetRef or approved GroupResultRef, ordered section IDs, selected note refs, output formats, template/renderer profile versions, branding/locale/rounding versions, revision, predecessor, preparer and reason. Require exactly one scope branch: do not use null/Guid.Empty client IDs to fake a group context.

**Lifecycle:** DraftDefinition → ReadyToRender → Rendering → RenderFailed or ArtifactsReady → Sealed → UnderReview → Reviewed/Returned → ReadyForRelease. Currentness is separate. Existing status strings remain through a compatibility adapter; “Validated” must not suddenly mean all human decisions are complete.

Sealing proves immutable content, not professional approval. Accounting/management/partner decisions occur afterward against exact artifact identity; which stages apply comes from the approved service profile. A package returned for substantive changes gets a new definition/input revision. The old sealed package and its review remain unchanged.

**Two hashes avoid a circular definition:**

1. `ContentManifestDigest` hashes immutable source/statement/layout/note/policy/composition/template inputs **before rendering**. It may be embedded in each output.
2. `ArtifactManifestDigest` hashes the completed ordered artifact identities, byte hashes, sizes, MIME types and renderer versions. This is stored in the seal/review/release records, not inserted back into those same artifact bytes afterward.

Do not claim an artifact contains its own final content hash; that creates a self-reference. File checksum must be calculated from exact persisted bytes.

**Artifact invariants:** format genuinely matches extension/MIME, nonempty correct bytes, exact content input, renderer/template/font dependencies pinned, stable technical metadata and expected pages/sheets/sections. Every required format succeeds before sealing; partially persisted files are not a sealed package. Optional outputs are explicitly optional in the definition, not dropped after failure.

Preserve the repository's reviewed formula-free and controlled-workbook profiles. A controlled profile may contain only explicitly allowlisted application-owned formulas; importing or rendering client-supplied active formulas/macros is forbidden. Never launch Office or recalculate arbitrary uploaded spreadsheets.

**Review invariants:** actual person, scope, stage, authority, exact revision/generation/content/artifact digests and evidence are recorded. No self-approval across role switching. Management acknowledgement does not create an audit opinion, external signature or release. A changed required input invalidates current readiness even if the file still downloads as a historical artifact.

**Events:** `PackageDefinitionRevised`, `PackageRenderRequested`, `PackageArtifactsValidated`, `PackageSealed`, `PackageDecisionRecorded`, `PackageReadyForRelease`, `PackageSuperseded`. “Ready” is a derived/current eligibility fact, not an automatic release instruction.

<a id="persistence"></a>
### 2. Persistence & Migrations (`.Infrastructure`)

Extend current package/artifact/review configurations. Proposed composition rows link ordered section IDs and exact note/layout versions. Artifact records retain current storage approach, exact content bytes or immutable storage reference, MIME, byte length, SHA-256, renderer/template version and content basis.

Unique constraints: package logical ID/revision; composition position and section occurrence per revision; artifact kind/renderer/content digest per package revision; operation/deduplication identity; subject/stage/decision uniqueness rules appropriate to append-only reviews. Duplicate renderer retry returns the same stored output or an explicit deterministic conflict—never replaces earlier bytes.

Database FK scope binds every artifact and decision to the exact package and its entity or group owner. Referential integrity cannot rely on a text hash alone. Index artifact manifests by package and exact digest, review queues by permitted scope/stage/state, and dependencies by statement/package refs. DeleteBehavior.Restrict for all sealed/history data.

Existing PostgreSQL-backed artifact storage is a valid starting point. Do not introduce a new blob provider solely for this plan. Where approved storage is external to the database, write immutable staged bytes first, verify them, then atomically publish their database references. A failed metadata commit leaves a nonpublished orphan eligible only for controlled cleanup; it must not masquerade as a released file.

Proposed migrations: `M25_PackageCompositionRevision`, `M25_ArtifactManifestSeal`, `M25_StageDecisionPins`. Preserve exact old render bytes and hashes. Newly unavailable historical renderer inputs do not justify regenerating a “replacement identical” output.

<a id="cqrs"></a>
### 3. Application & CQRS Contracts (`.Application`)

**DTOs:**

- `PackageInputDto`: discriminated `EntityStatementInput(Context, StatementSetRef)` or `GroupStatementInput(GroupId, ScopeVersionRef, ApprovedGroupResultRef)`; cannot contain both.
- `PackageDefinitionDraftDto`: PackageInputDto, PreviousPackageRef?, Sections[SectionId,Order,NoteRefs[]], RequiredFormats[], OptionalFormats[], TemplateProfileRefs[], Locale, BrandingRef, RoundingPolicyRef, Description.
- `PackageReviewInputDto`: PackageRef, ContentManifestDigest, ArtifactManifestDigest, Stage, Decision, EvidenceMode, EvidenceRefs[], Rationale, actual management contact where permitted offline.
- `ArtifactReadRequestDto`: ArtifactId and requested disposition inline/download; filename, storage key, MIME and authorization are server-resolved.

| Command / query and input | TResponse | Validation and handler rule |
|---|---|---|
| `CreatePackageDefinitionCommand(PackageDefinitionDraftDto, Meta)` | `MutationReceiptDto` | One scope/input kind, reviewed eligible statement/group result, unique ordered sections and approved template versions. |
| `RevisePackageDefinitionCommand(OriginalPackage: Ref, PackageDefinitionDraftDto, Meta)` | `MutationReceiptDto` | Original preserved, new revision/operation, change reason; no copied current approval. |
| `RequestPackageRenderingCommand(PackageDefinitionRef, Meta)` | `OperationTicketDto` | Complete current inputs, exact expected format manifest and durable parent operation. |
| `CancelPackageRenderCommand(OperationId, Meta)` | `MutationReceiptDto` | Existing worker cancellation discipline; do not cancel already published artifact history. |
| `ValidatePackageArtifactsCommand(PackageDefinitionRef, RenderAttemptId, Meta)` | `ValidationReportDto` | Server-read exact bytes, MIME/format, round-trip values, digests and required-file completeness. |
| `SealFinancialPackageCommand(PackageDefinitionRef, RenderAttemptId, ExpectedManifestDigest, Meta)` | `MutationReceiptDto` | Recompute all dependencies and byte manifest; all required formats valid; one atomic immutable seal. |
| `RecordFinancialPackageDecisionCommand(PackageReviewInputDto, Meta)` | `MutationReceiptDto` | Exact current sealed content, correct separate stage/person/scope, current evidence; return/reject reason. |
| `GetPackageWorkspaceQuery(PackageId)` | `PackageWorkspaceDto` | Definition/version list, readiness, operation states, artifacts and permitted stage decisions. |
| `GetPackageReviewQueueQuery(StageFilter, ScopeFilter, PageRequest)` | `PageDto<PackageReviewRowDto>` | Current actionable stages only; historical decisions shown distinctly; scope before counts. |
| `GetPackageManifestQuery(PackageRef)` | `PackageManifestDto` | Content and artifact manifests, lineage and historical/current state. |
| `GetPackageArtifactQuery(ArtifactReadRequestDto)` | `AuthorizedArtifactReadDto` | Reauthorize and verify digest before download; no client-chosen filesystem path. |
| `GetPackageReleaseReadinessQuery(PackageRef)` | `ReadinessDto` | M37-compatible exact package/artifacts/decisions with unresolved blockers; no release side effect. |

`PackageWorkspaceDto` contains definition projection, exact input refs, render attempts, validation summary, artifact metadata and review history. `PackageManifestDto` contains both hashes and ordered records. `AuthorizedArtifactReadDto` is an internal bounded stream/download descriptor with safe filename/MIME/hash, resolved only after authorization; public URLs require the approved short-lived scoped capability mechanism. No request returns raw tracked entities or an arbitrary storage path.

**Rendering execution:** capture immutable inputs in a short transaction; generate each format outside long-held DB locks using pure canonical DTOs; persist/reuse results; validate; then reauthorize/recompute manifest in the publication transaction. If a source changed while rendering, retain the artifact as a noncurrent attempt if policy permits, but fail publication with `generation.stale` and no misleading Ready state.

Use OpenXML and PDFsharp/MigraDoc already pinned. Pin render timestamps/identifiers from the package definition where included; never let `DateTime.Now`, random IDs, machine culture or changing font lookup produce unexplained file differences. Exact renderer version plus fixture determines output reproducibility. [R3](../reference/02_Standards_and_Source_Register.md#source-r3)

<a id="lineage"></a>
### 4. Inter-Module Lineage & Boundaries

M24 owns accounting-result semantics; M25 owns composition/rendering/sealing. A renderer cannot fetch new GL data or choose a different mapping. Group results arrive from M26 in the same canonical statement schema with group-owned authorization, not as unscoped component rows.

M25 hands M37 an exact `PackageReleaseBasis` consisting of package revision, content/artifact digests and stage decision refs. M37 must revalidate freshness at freeze and issue. M38 archives those exact released bytes, not a freshly generated report. This blueprint does not implement M37/38 internally.

Purview and eSignature are excluded. Existing release/protection policies must be reconciled in ADR-08 for the chosen target profile; this is not permission to disable independent approval, source integrity, recovery or idempotency safeguards. A missing permitted external delivery provider blocks delivery, not local accounting calculation.

<a id="blazor"></a>
### 5. Blazor UI Architecture (`.Web`)

```text
FinancialPackageWorkspacePage
  PackageInputBanner / RevisionSelector
  PackageCompositionEditor(EditForm)
    SectionOrderList / RequiredSectionWarnings / OutputProfileSelector
  RenderOperationPanel / FailedFormatDetails
  ArtifactPreviewAndDownloads / ContentLineagePanel
  PackageSealDialog
  StageReviewPanel → ManagementPresentation / ReviewDialog
  ReleaseReadinessHandoff
```

Extend existing FinancialPackage and package-review pages. `PackageWorkspaceState` stores selected revision, definition draft and operation IDs. Section reorder is an optimistic local draft only; persisted composition requires a successful command. Preview labels distinguish draft, sealed, reviewed, historical and stale states.

Disable unsupported output selections with a reason. A generation failure displays the actual safe failure and preserves current version; it must not emit a download or success message. After an operation completes, query the durable published result—not a guessed next version. Dirty-state checks apply to composition changes, not read-only review dialogs.

Review dialog shows the exact revision/hash/format list and requires appropriate evidence/rationale; cannot approve a “current” pointer whose content changed behind the dialog. Client management presentation contains only explicitly shared allowed content; internal audit findings/costs do not leak through metadata or filenames.

<a id="verification"></a>
### 6. Edge Cases, Security & Verification

| Layer | Proposed class | Required tests |
|---|---|---|
| xUnit domain | `PackageManifestTests` | `OrderedSectionsChangeContentDigest`; `ArtifactHashDoesNotSelfReference`; `MissingRequiredFormatBlocksSeal`; `ApprovalBindsEntireArtifactSet`; `ReturnedPackageNeedsNewRevision`. |
| xUnit renderer | `FinancialArtifactRoundTripTests` | `XlsxIsValidAndValuesMatchStatementLines`; `DocxContainsExpectedTablesAndNoExternalParts`; `PdfIsReadableAndContainsExpectedTotals`; `ControlledFormulaAllowlistOnly`; `FixedInputsProduceDeterministicArtifacts`. |
| xUnit PostgreSQL | `PackagePublicationIntegrityTests` | `SourceChangeDuringRenderCannotPublish`; `SameOperationDoesNotDuplicateArtifact`; `TamperedBytesBlockSealAndDownload`; `PartialPersistenceDoesNotAdvancePackage`; `HistoricApprovedBytesRemainUnchanged`. |
| bUnit | `PackageAssemblyComponentTests` | Persisted section order, failed-format display, queued-vs-complete distinction, exact-hash review dialog and no optimistic seal. |
| Playwright | `R2R25PackageJourneys` | Create/reorder/reload/render all three formats, download and parse them; independent review; injected format/storage failure; source race; replacement revision and preserved predecessor; group-only access boundaries. |

**Exit gate:** every reviewed/release-ready package points to a complete immutable artifact set with current exact inputs, and every download returns those same authorized bytes.
