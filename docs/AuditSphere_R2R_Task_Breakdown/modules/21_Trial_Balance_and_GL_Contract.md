[Master index](../00_INDEX.md) · [Original module in source](../source/ORIGINAL_R2R_Blueprint_Modules_20-26.md#module-21)

**Full module contract — required reading for its tasks.** No task may omit an applicable invariant merely because the task card is shorter. Names retain the original EXISTING / EXTEND / NEW / DECISION labels.

<!-- SOURCE-LINES: 479-613 -->
## Module 21 — Trial Balance & GL: ingestion, mapping and completeness

**Business outcome:** a reviewer can explain each reported balance from accepted source data, journal movements and an approved mapping.  
**Requirement coverage:** VP-035, VP-036, VP-037.  
**Sequence:** M21.1 intake session → M21.2 parse/validate/stage → M21.3 atomic seal/accept → M21.4 mappings → M21.5 completeness and drill-down.

<a id="domain"></a>
### 1. Domain Modeling (`.Domain`)

**EXISTING/EXTEND:** `TrialBalanceDataset`, `TrialBalanceImportBatch`, `TrialBalanceRow`, existing source/GL import records, `MappingVersion`, `MappingAllocation`, opening/completeness records. Reuse the current import/profile/parser and normalized-digest contracts. Do not build a second TB table under a new module name. [R4](../reference/02_Standards_and_Source_Register.md#source-r4)[R7](../reference/02_Standards_and_Source_Register.md#source-r7)

**NEW if absent:** `SourceAcceptanceDecision`, `ImportColumnMapVersion`, typed `ImportValidationIssue`, independently identified source-selection history and a mapping account-universe manifest. These are needed only when current equivalents cannot carry the specified contract.

Maintain three distinct identities: **received file hash**, **normalized source digest**, **selected accepted revision**. Identical file bytes in another context are not automatically the same business source. Parsed source rows never change after sealing. A replacement is a successor with its own receipt/profile/parser metadata.

A GL line retains journal/line IDs, posting/document/service dates, account stable identity and original code, description, original currency/amount, functional amount, debit/credit representation, counterparty and dimensions. Resolve source aliases explicitly; do not merge accounts because descriptions match.

**Lifecycle:** `Receiving → Staging → Validating → Validated → Sealed`; failures become `Rejected`/`Cancelled` with an evidence record and no accepted-source pointer change. `Sealed` means content is immutable, not independently accepted. Source acceptance is a separate authorized decision; selected source changes through an explicit command. Map these logical stages to current LOADING/SEALED records and operation status rather than rewriting historical state strings.

GL completeness state is `NotEvaluated`, `Incomplete`, `Reconciled` or `Stale`, separate from parse success. Define:

`residual(account, functional currency) = opening + included GL movements − closing TB`

The account universe is the union of opening, GL and closing accounts, including zero-closing and GL-only accounts. Missing opening coverage is **unknown**, not zero. An explicit approved zero-opening declaration for a new entity is different. Missing chunks, unbalanced journal groups, duplicate line identities, unknown dimensions and period/basis/currency mismatches prevent completeness.

Distinguish pre-close TB from a source TB where revenue/expense accounts were closed into retained earnings. An approved import/inclusion profile defines which year-end journals and period endpoints reconcile; never discard closing journals merely to force a zero residual.

**Mapping lifecycle:** `Draft → Submitted → Approved`; return/amend creates a tracked draft/revision. Each nonzero effective reporting account must map to permitted taxonomy nodes. Manual splits conserve each source amount. Note references may cross-link primary values but must not double count them in primary statement totals. Existing `LAST_DESTINATION` is deterministic rounding allocation, not a discretionary plug.

AJE-only accounts require special treatment: Module 22 may introduce a valid account from the approved chart that has zero balance or no row in the raw TB. The effective mapping universe includes those eligible AJE accounts and is hashed; approving a later AJE cannot make an unmapped account disappear. Do not insert fake zero rows into the immutable received TB.

**Domain events:** `SourceRevisionSealed`, `AcceptedSourceChanged`, `GeneralLedgerBatchFinalized`, `CompletenessProofRecorded`, `MappingRevisionApproved`.

<a id="persistence"></a>
### 2. Persistence & Migrations (`.Infrastructure`)

Extract/extend configurations for current datasets/batches/rows, mapping versions/allocations and GL transactions/lines/chunks. Add acceptance/current-source/configuration-history mappings only where missing.

Constraints and indexes:

- Source batch and dataset carry context/firm/client/engagement FKs and parser/profile versions. `RawFileSha256Hex` and normalized digest are separate nonempty hash fields when sealed.
- Chunk uniqueness `(ImportBatchId, ChunkNumber)`; idempotent identity includes chunk digest. Same chunk number/different content is a conflict, not overwrite.
- GL line identity unique by `(BatchId, StableJournalId, StableLineId)` or existing equivalent. Retain legitimate repeated account codes on different lines/dimensions.
- TB aggregation is permitted only under a declared normalization policy. Duplicate ambiguous account/dimension keys reject; exact signed detail aggregation must retain source-row references and counts.
- Index source rows by dataset/account/dimension and GL lines by batch/account/posting date/journal key for paged drill-down. Do not load 500,000 entities into the Blazor circuit.
- Children cannot be added, changed or deleted after the source is sealed. Use existing database triggers/guards plus FK discipline; a UI status flag is insufficient.
- Mapping allocations unique for mapping/source-account/target/role; reject duplicate primary allocation. Scoped FKs bind chart and taxonomy. Fraction and amount conservation are transaction-validated.
- Completeness proof persists exact opening/GL/closing refs, inclusion profile, account residuals, row/journal coverage and result hash. New proof never edits an old accepted proof.

Proposed migration suffixes: `M21_SourceSelectionEvidence`, `M21_ImportChunkIdentity`, `M21_CompletenessManifest`, `M21_MappingUniversePins`. Existing source digests must remain resolvable; new digest versions use an explicit scheme/version field rather than rewriting old hashes.

Stage bounded rows in short transactions. Finalize only when expected chunk count, row count, journal boundaries and recomputed digest agree. In one final transaction seal membership and register the immutable result; in the separate acceptance transaction update the selected pointer, increment generation and append invalidation/evidence. A rejected import leaves the prior accepted source intact.

<a id="cqrs"></a>
### 3. Application & CQRS Contracts (`.Application`)

**Reuse boundary:** current `ClientAccountingService` has direct and chunked GL inputs and limits of 100,000 transactions / 500,000 lines per import, 10,000 transactions / 50,000 lines per chunk. These are current caps, not demonstrated throughput promises. Preserve or tighten them; raising them requires profiling and approval. [R7](../reference/02_Standards_and_Source_Register.md#source-r7)

**Input DTOs:**

- `ImportStartDto`: Context, SourceKind(TB/GL), FileName, DeclaredSize, MediaType, Layout(SignedNet/DebitCredit), ImportProfileRef, ParserVersion; source file bytes arrive through an authenticated bounded staging endpoint, not inside a mediator JSON request.
- `ColumnMapDto`: accountCode/name, amount or debit/credit columns, optional entity/currency/dimensions; GL adds stable journal/line keys, posting/document/service dates, source user, counterparty, reversal fields; locale/date conventions explicitly selected.
- `UploadChunkDto`: SessionId, Sequence, ServerStagedBlobRef, ClaimedChunkHash; server recomputes size/hash and scope. Claimed hash is a comparison input, not trusted identity.
- `ImportFinalizeDto`: SessionId, ExpectedChunks, ExpectedRows/Transactions/Lines, ExpectedRawHash, ColumnMapRef, ProfileRef.
- `MappingDraftDto`: Context, SourceRef, ChartRef, TaxonomyRef, EffectiveAccountUniverseHash, predecessor Ref?, Allocations[StableAccountIdentity,TargetNodeId,AllocationRole,DecimalFraction,Order,Rationale].
- `CompletenessInputDto`: Context, ClosingTbRef, GeneralLedgerBatchRefs[], OpeningSourceRef? or explicitly approved zero-opening declaration, InclusionProfileRef, CutoffDate.
- `SourceFilterDto`: account, date range, journal key, counterparty, dimension predicates, source revision; every filter is typed and scope-limited.

| Command / query and input | TResponse | Key `AbstractValidator<T>` rules plus authoritative checks |
|---|---|---|
| `BeginAccountingImportCommand(ImportStartDto, Meta)` | `ImportSessionDto` | Supported actual format/profile, safe filename, size limit, open context and current upload authority. |
| `RegisterImportChunkCommand(UploadChunkDto, Meta)` | `ImportChunkReceiptDto` | Exact session/sequence, approved size, hash match, no conflicting retry, unfinished session. |
| `SaveImportColumnMapCommand(SessionId, ColumnMapDto, Meta)` | `MutationReceiptDto` | Required headers unambiguous; no one numeric column assigned incompatible meanings; explicit dates/locale. |
| `ValidateAccountingImportCommand(ImportFinalizeDto, Meta)` | `OperationTicketDto` | Complete staged upload/profile; enqueue streaming parse/validation, no source acceptance yet. |
| `SealValidatedImportCommand(SessionId, ValidationReportRef, Meta)` | `MutationReceiptDto` | All fatal errors absent, exact input unchanged, complete counts and balanced required scopes. |
| `AcceptSourceRevisionCommand(Context, SourceRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Sealed source, current validation, independent acceptance where required, accepted-source uniqueness and generation bump. |
| `CancelAccountingImportCommand(SessionId, Meta)` | `MutationReceiptDto` | Reason, only cancellable stage; no removal of an accepted source or uncertain provider effect. |
| `SaveMappingDraftCommand(MappingDraftDto, Meta)` | `MutationReceiptDto` | Account universe/targets/context valid; draft can show unresolved rows but cannot submit as complete. |
| `SubmitMappingCommand(MappingId, Meta)` | `MutationReceiptDto` | All relevant accounts covered; split totals exactly 1; no cycles/double primary counting. |
| `ReviewMappingCommand(MappingId, DecisionInputDto, Meta)` | `MutationReceiptDto` | Authorized different reviewer; exact source/chart/taxonomy/universe unchanged; return needs rationale. |
| `BuildCompletenessProofCommand(CompletenessInputDto, Meta)` | `OperationTicketDto` | Compatible accepted sources; explicit opening coverage and cutoffs; no duplicate batches. |
| `GetImportSessionQuery(SessionId)` | `ImportSessionDto` | Session owner/scope, accepted chunk counts, profile, state and operation identity. |
| `GetImportIssuesQuery(SessionId, PageRequest)` | `PageDto<ImportIssueDto>` | Row keys/error codes safe; no arbitrary source-file path disclosure. |
| `GetTrialBalanceQuery(SourceRef, SourceFilterDto, PageRequest)` | `PageDto<TrialBalanceRowDto>` | Exact source, typed row values, visible context and matching totals snapshot. |
| `GetGeneralLedgerQuery(SourceRef, SourceFilterDto, PageRequest)` | `PageDto<GeneralLedgerLineDto>` | Exact source-line/journal provenance; paged dates and amounts. |
| `GetJournalDrillDownQuery(SourceRef, StableJournalId)` | `GeneralLedgerJournalDto` | All permitted journal lines and debit/credit totals; count and completeness warning. |
| `GetMappingWorkspaceQuery(Context, MappingId?)` | `MappingWorkspaceDto` | Current/prior mapping, effective account universe, unmapped queue and capabilities. |
| `GetCompletenessProofQuery(ProofId, PageRequest)` | `CompletenessProofDto` | Exact source manifest, coverage/counts, result and paged per-account residuals. |
| `ExportSourceRowsQuery(SourceRef, SourceFilterDto, ExportFormat)` | `ExportTicketDto` | Allowlisted CSV/XLSX format and bounded export; authorization reapplied at byte retrieval. |

`ImportSessionDto` contains ID, state, context, raw/normalized hashes when known, counts, limits, profile/parser versions and validation/operation refs. `ImportChunkReceiptDto` contains sequence, accepted digest/count and retry identity. Row DTOs project the source fields above plus exact IDs; issues contain row/journal keys and field errors. `CompletenessProofDto` carries input refs, coverage state, totals and residual rows. `ExportTicketDto` is a short-lived scoped output identity, not an unrestricted filesystem URL.

**Parser rules:** inspect ZIP/package structure for genuine XLSX, reject XLSM/macros/external workbook links/formula-dependent numeric cells, bound compressed/uncompressed size and shared strings, stop decompression bombs, preserve 1900/1904 date-system semantics. Never evaluate workbook formulas or open Office automation. CSV profile explicitly defines delimiter, quotes, encoding, numeric sign/decimal and date rules. Invalid dates, NaN-like text, oversized values and duplicate ambiguous keys produce row errors, not substituted zero.

Use existing CsvHelper/OpenXML parsers and pure normalization functions. Header-name guesses may be displayed as an unconfirmed convenience only; the saved mapping must be explicit. A cancellation yields an actual cancelled operation or conflict, never an announced success.

<a id="lineage"></a>
### 4. Inter-Module Lineage & Boundaries

M21 consumes M20 context/chart/taxonomy and existing document staging/evidence services. It owns source records and mappings, not management AJE decisions or financial statement layouts. M22 reads immutable source rows and chart identities; M24 reads the selected TB plus effective adjustments and approved mappings, never a mutable current-source query during rendering.

Accepting a new TB requires fresh source-specific reflection decisions. Preserve logical AJE identity/history across bases, but do not automatically mark old journal treatment applicable to replacement data. A new mapping tied to a changed taxonomy must invalidate reviewed statements using the old selected mapping while leaving historical packages unchanged.

For GL replacement, stale only dependent completeness/reconciliation/report requirements—not unrelated records solely sharing the same account label. Until precise dependency proofs exist, the existing generation fence remains conservative. A source with no GL can support only the approved profile that explicitly permits such reporting; UI must not claim GL completeness.

<a id="blazor"></a>
### 5. Blazor UI Architecture (`.Web`)

```text
AccountingSourceWorkspacePage
  ReportingContextBanner
  SourceRevisionRegister / SelectedSourcePanel
  TbGlImportWizard
    FileSelectionStep → ContextAndProfileStep → ColumnMapStep
    PreviewAndIssuesStep → ValidationProgressStep → AcceptanceStep
  TrialBalanceGrid / GeneralLedgerGrid / JournalDrawer
  MappingWorkspace
    UnmappedQueue / AllocationGrid / SplitDialog
    PriorRevisionComparison / IndependentReviewDialog
  CompletenessWorkspace → OpeningSourceSelector / ResidualDrillDown
```

Use `AccountingSourceState` with session/source/mapping IDs, filters, import progress and draft mappings. File input streams to the existing authenticated staging boundary with explicit maximum size; never retain the entire file or parsed GL in a scoped circuit service. Large grids use paging/virtualization backed by queries.

`EditForm` surrounds the profile/column/allocation choices, not a lifetime-long database transaction. Split rows have stable IDs and field-specific fraction errors. Closing or changing context while an upload is running requires an explicit cancel/leave decision; server staging retains honest resumability. Progress distinguishes bytes received, rows validated and source accepted.

After validation, render all fatal issues and a bounded preview. “Accept revision” displays the old/new source and invalidation impact. Read-only old revisions stay accessible with clear labels. Navigation back to another module preserves the exact selected context and source, not “most recently imported anywhere”.

<a id="verification"></a>
### 6. Edge Cases, Security & Verification

| Layer | Proposed class | Required test methods/journeys |
|---|---|---|
| xUnit domain/parser | `AccountingImportContractTests` | `SignedAndDebitCreditLayoutsNormalizeEqually`; `RepeatedAccountAcrossDimensionsRetainsLineage`; `FormulaMacroExternalLinkAndRenamedZipRejected`; `LeadingZeroCodesAnd1904DatesSurvive`; `SixDecimalAndOverflowLimitsAreExplicit`. |
| xUnit PostgreSQL | `SourceRevisionIntegrityTests` | `ConflictingChunkRetryRejected`; `MissingChunkCannotSeal`; `RejectedReplacementPreservesAcceptedPointer`; `SealedRowsRejectInsertUpdateDelete`; `SourceAcceptanceInvalidatesInSameTransaction`. |
| xUnit calculations | `CompletenessAndMappingTests` | `OpeningPlusMovementEqualsClosing`; `MissingOpeningIsIncomplete`; `GlOnlyAccountIsNotOmitted`; `PreCloseVsPostCloseProfileIsExplicit`; `SplitConservesMicroUnits`; `AjeOnlyAccountRequiresMapping`. |
| bUnit | `AccountingImportWizardTests`, `MappingEditorTests` | Required headers/errors, cancel/back/resume, stale acceptance button, invalid split row, no automatic approval, exact source context after navigation. |
| Playwright | `R2R21SourceJourneys` | Genuine CSV and XLSX intake; malformed/oversize/browser validation failures; GL chunk resume/reload; independent mapping review; opening/GL/TB drill-down and export reconciliation; wrong-tenant source denial. |

**Exit gate:** a complete accepted source/mapping/completeness chain is immutable, replayable and scoped; its exact identity can be consumed by M22–M25. Source acceptance cannot occur on a partial upload or a fabricated zero residual.
