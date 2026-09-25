[Master index](../auditsphere-r2r-index-task-breakdown.md) · [Original module in source](../source/auditsphere-r2r-source-blueprint-modules-20-26-historical.md#module-23)

**Full module contract — required reading for its tasks.** No task may omit an applicable invariant merely because the task card is shorter. Names retain the original EXISTING / EXTEND / NEW / DECISION labels.

<!-- SOURCE-LINES: 730-839 -->
## Module 23 — Bank & Subledger Reconciliations

**Business outcome:** explain the difference between a specified ledger basis and independently sourced supporting balances, with evidence and an independent conclusion.  
**Requirement coverage:** VP-039.  
**Sequence:** M23.1 basis/sources → M23.2 typed items → M23.3 correction handoff → M23.4 proof → M23.5 review and source-change handling.

<a id="domain"></a>
### 1. Domain Modeling (`.Domain`)

**EXTEND:** current source-bound reconciliation and typed accounting/bank-schedule records in the accounting/fieldwork services. Locate exact existing entity/configuration names before implementing. The following are **logical proposed contracts**, not assertions that those names already exist:

`ReconciliationSchedule` aggregate owns a revision, scope, reconciliation kind, as-of date, account set, selected ledger basis, supporting-source references, item list, proof and review history. `ReconciliationItem` owns type, side/sign, amount/currency, dates, narrative, exact source/evidence refs and optional AJE reference. `ReconciliationProof` is immutable and records the complete input and formula version. Reuse existing independently reviewed schedule/evidence records where equivalent.

**Enums:** Kind=Bank/Receivables/Payables/OtherControl; Basis=RawTb/AcceptedGl/AdjustedSnapshot; ItemType=Timing/LedgerCorrection/SupportingSourceCorrection/Informational; Side=Ledger/Supporting; state=Draft/Submitted/Returned/Approved, plus separate Currentness.

**Sign convention:** normalize both ledger and supporting sources to the same **client-ledger perspective**, debit-positive. A bank statement's debit/credit convention must be explicitly transformed and recorded; never infer from the caption “balance”. For liability reconciliations use the same signed canonical basis and a separate display sign.

Define:

`LedgerExplained = SelectedLedgerBalance + eligible correction deltas NOT already contained in selected basis`

`SupportExplained = SelectedSupportingBalance + approved typed timing/supporting-source reconciliation items`

`Residual = LedgerExplained − SupportExplained`

For a schedule based on an adjusted snapshot, any journal in that snapshot's plan contributes **zero additional adjustment**. Merely linking the same AJE again must not change its explained ledger. Each item declares its side/effect; a journal's two balanced lines do not both affect the selected bank account.

Timing items explain when recorded activity appears in the other source; they do not generate ledger journals. Proposed ledger corrections link to M22 and count only under the approved eligibility rule. A supporting-source correction requires explicit evidence and review; it cannot be a generic “plug” field. Informational items have no numeric effect.

**Invariants:** account and source scope agree; required source coverage is complete; as-of dates and currency compatible; item dates fit the selected rule; no duplicate source-item inclusion; all values finite and within precision; required evidence available; unexplained residual exactly zero under the approved proof rule. Review cannot manufacture a waived balancing figure. Any allowed small presentation rounding is separately explained, bounded and policy-versioned.

For AR/AP subledgers, store source-row count, declared control totals, customer/vendor identity references, debit/credit treatment, aging date basis and cutoffs. Reconcile control account to the same as-of subledger, not to an unrelated current balance. Negative customer balances and unapplied credits remain visible rather than silently netted against another counterparty.

**Lifecycle:** Draft → Submitted → Returned or Approved; return reason mandatory. An approved schedule's change creates a successor revision and fresh independent review. A changed ledger source, included AJE, supporting document version or required evidence adequacy makes current proof stale. Manual carry-forward may copy still-outstanding item references into a new draft but never copies approval.

Oracle's documented separation of preparer/reviewer and rejection return informs this flow; automatic closure/notifications are deliberately not adopted. [P2](../reference/auditsphere-r2r-reference-standards-and-source-register.md#source-p2)

<a id="persistence"></a>
### 2. Persistence & Migrations (`.Infrastructure`)

Extract/extend existing reconciliation configurations and source-bound schedule relationships. Persist schedule context, revision, exact source hashes/IDs, selected basis kind, as-of/currency, creator and versioned review. Items have unique IDs/order, typed side/effect and optional journal revision FK.

Composite FKs prevent linking another client's bank statement, GL batch, subledger or adjustment. Exact document snapshot is referenced rather than a mutable filename. Ledger basis must resolve to one valid sealed source or adjusted snapshot; do not store a free-text balance as if it were a proved ledger total.

Indexes: `(FirmId,ClientId,EngagementId,PeriodId,Kind,State)` for queues; schedule/revision for history; evidence/journal refs for invalidation; selected account/as-of for prior schedule lookup. Unique effective item/source key prevents duplicate inclusion. Approved schedules/proofs/reviews are append-only; details and linked journals use Restrict.

Use FK/row constraints for date/range/amount/kind; complete residual and freshness checks run under schedule plus dependency locks. The proof persists formula-version and input manifest, including adjustment membership. Proposed migrations: `M23_ReconciliationBasisPins`, `M23_TypedItemProofs`, `M23_ReconciliationReviewHistory`, only for actual missing fields. Existing source-proof data must not be regenerated under a new sign convention without a new version.

<a id="cqrs"></a>
### 3. Application & CQRS Contracts (`.Application`)

**DTOs:**

- `ReconciliationDraftDto`: Context, Kind, AccountIdentities[], AsOfDate, LedgerBasisRef, SupportingSourceRefs[], SupportingBalance, SupportingSignConvention, Currency, ExtractionMethod(Imported/ManualTranscription), SourceLocationReferences[], PriorScheduleRef?.
- `ReconciliationItemDto`: ItemId, Type, Side, SignedEffect, Currency, TransactionDate, ExpectedClearanceDate?, Description, SourceItemRef?, EvidenceRefs[], LinkedJournalRef?, CounterpartyReference?.
- `ReconciliationEditDto`: ScheduleId, HeaderChanges, Items[]; read-only ledger balance excluded from caller-writable fields.
- `ReconciliationReviewDto`: ScheduleRef, ProofRef, Decision, ReviewerConclusion, EvidenceRefs[].

| Command / query and input | TResponse | Key validator + handler gate |
|---|---|---|
| `CreateReconciliationCommand(ReconciliationDraftDto, Meta)` | `MutationReceiptDto` | Current scoped basis, explicit sign/as-of/currency, required support reference and account coverage. |
| `SaveReconciliationDraftCommand(ReconciliationEditDto, Meta)` | `MutationReceiptDto` | Draft/returned state; valid item types/sides/dates and no duplicate item identity. |
| `LinkReconciliationCorrectionCommand(ScheduleId, ItemId, JournalRef, Meta)` | `MutationReceiptDto` | Same context/account effect, valid correction purpose; link is not approval/inclusion by itself. |
| `CalculateReconciliationProofCommand(ScheduleId, Meta)` | `ReconciliationProofDto` | Recompute basis/source/AJE treatment; no input-derived ledger total accepted blindly. |
| `SubmitReconciliationCommand(ScheduleId, ExpectedProof: Ref, Meta)` | `MutationReceiptDto` | Exact proof current, required answers/evidence and correction dispositions complete, residual zero. |
| `ReviewReconciliationCommand(ReconciliationReviewDto, Meta)` | `MutationReceiptDto` | Independent assigned reviewer; recompute proof/currentness; return/reject requires rationale. |
| `ReviseReconciliationCommand(Original: Ref, Reason, Meta)` | `MutationReceiptDto` | Preserve old schedule and review; new revision starts unapproved. |
| `CarryForwardReconciliationItemsCommand(Original: Ref, TargetContext, SelectedItemIds[], Meta)` | `MutationReceiptDto` | Explicit next-period basis; copy selected unresolved references only; no prior proof or approval. |
| `GetReconciliationRegisterQuery(Context, KindFilter, AsOfFilter, PageRequest)` | `PageDto<ReconciliationSummaryDto>` | Current and historic state, residual and responsible persons; scope applied before totals. |
| `GetReconciliationWorkspaceQuery(ScheduleId)` | `ReconciliationWorkspaceDto` | Header/items, live evidence/reflection applicability, formula, proof and review history. |
| `GetReconciliationSourceRowsQuery(ScheduleId, SourceSide, PageRequest)` | `PageDto<ReconciliationSourceRowDto>` | Exact ledger/subledger/statement rows and extraction provenance. |
| `ExportReconciliationQuery(ScheduleRef, Format)` | `ExportTicketDto` | Authorized exact revision/proof, currency, evidence/source IDs and no misleading current claim. |

`ReconciliationProofDto` includes signed source totals, included/excluded item effects, applied AJE IDs, residual, completeness, manifest and CanSubmit. Workspace DTO contains this proof plus projected header/items; summary contains kind, account, period, state/currentness and residual. SourceRow DTO preserves source identity/date/amount/currency/evidence location.

Use existing accounting-analysis/fieldwork validation; do not introduce automatic matching or bank APIs. A manually transcribed statement figure is permissible only with explicit provenance and required independent review; label it as such rather than claiming bank-system verification.

<a id="lineage"></a>
### 4. Inter-Module Lineage & Boundaries

M23 consumes immutable M21 sources and M22 eligibility/adjusted-snapshot DTOs. M22 alone creates and decides journal treatment. M23 asks M22 to create a draft through an explicit user action, then links the returned identity. No hidden journal creation occurs on “Calculate”.

M24/25 consume a reconciliation-readiness report containing required schedule IDs, manifest hashes, reviews and unresolved blockers. An irrelevant schedule is not a universal blocker; applicability must be approved in the context policy. If a required schedule becomes stale, current finalization is blocked immediately through the generation fence.

A raw-TB reconciliation and adjusted-TB reconciliation can coexist as different versions/bases; labels must make the basis clear. Never compare one module's raw figure with another's adjusted figure without a visible bridge.

<a id="blazor"></a>
### 5. Blazor UI Architecture (`.Web`)

```text
ReconciliationWorkspacePage
  ContextBanner / ScheduleRegister
  ReconciliationEditor(EditForm)
    LedgerBasisSelector / SupportingSourceSelector
    SignConventionAndAsOfPanel
    ReconciliationItemGrid → EvidencePicker / CorrectionLinkDialog
    RawExplainedResidualPanel
  ProofDetails / ReviewDialog / VersionComparison
```

Extend current accounting evidence/bank schedule surfaces. `ReconciliationState` holds schedule/proof refs, selected source and draft items. Ledger amount is server-derived/read-only. A user changes a supporting balance only in a draft with provenance. Recalculating doesn't auto-approve.

Expose exactly which correction is excluded because it already appears in the adjusted snapshot. Show unresolved source/evidence/management treatment with actionable links. A changed source puts the editor in a conflict/revision flow; no green approved residual until server review succeeds. Use field-level EditForm errors and the shared dirty/cancellation/reauthorization rules.

<a id="verification"></a>
### 6. Edge Cases, Security & Verification

| Layer | Proposed class | Required tests |
|---|---|---|
| xUnit domain | `ReconciliationProofTests` | `BankSignConventionIsExplicit`; `TimingItemDoesNotPostJournal`; `CorrectionAlreadyInSnapshotNotAddedTwice`; `UnknownReflectionBlocksProof`; `UnexplainedResidualBlocksSubmission`; `CreditsRemainVisibleByCounterparty`. |
| xUnit PostgreSQL | `ReconciliationRevisionTests` | `DifferentReviewerRequired`; `ChangedSourceOrDocumentMakesProofStale`; `ConcurrentCorrectionDecisionCannotApproveOldProof`; `ApprovedItemMutationDenied`; `CarryForwardDoesNotCopyApproval`. |
| bUnit | `ReconciliationEditorTests` | Read-only ledger source, correction-link pending state, row-date errors, visible formula/effects and reasoned return/new revision. |
| Playwright | `R2R23ReconciliationJourneys` | Bank schedule with deposit/cheque/fee bridge; accepted AJE linked once; return/rework; source replacement/reload; subledger control tie-out and denied cross-client document. |

**Exit gate:** the ledger/supporting proof is reproducible, has no unexplained residual or double-applied correction, and current independent review is required before it can satisfy a downstream gate.
