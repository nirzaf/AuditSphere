[Master index](../auditsphere-r2r-index-task-breakdown.md) · [Original module in source](../source/auditsphere-r2r-source-blueprint-modules-20-26-historical.md#module-22)

**Full module contract — required reading for its tasks.** No task may omit an applicable invariant merely because the task card is shorter. Names retain the original EXISTING / EXTEND / NEW / DECISION labels.

<!-- SOURCE-LINES: 615-728 -->
## Module 22 — Adjustments & Journals

**Business outcome:** every proposed adjustment has an exact accounting basis, balanced lines, independent technical review, attributable management disposition and explicit source-reflection treatment.  
**Requirement coverage:** VP-038.  
**Sequence:** M22.1 journal drafts → M22.2 technical decisions → M22.3 management/reflection decisions → M22.4 adjustment plans and immutable adjusted snapshots.

<a id="domain"></a>
### 1. Domain Modeling (`.Domain`)

**EXISTING/EXTEND:** `AdjustmentJournal`, `AdjustmentLine`, `AdjustmentJournalManagementDecision`, `JournalSourceReconciliation`, `AdjustmentPlan` and plan lines, `AdjustedTrialBalanceSnapshot`/rows. Current journal purpose/origin/supersession/reversal fields must remain. [R4](../reference/auditsphere-r2r-reference-standards-and-source-register.md#source-r4)

Journal identity is logical `(firm, engagement, journal number)` with distinct persisted base/revision identity. A source replacement never silently changes `BaseDatasetId` of an already reviewed journal. A revised journal states predecessor, new basis and reason. An authorized reversal is a new balanced journal linked to the original—not deletion or negation of a posted row in place.

Separate three dimensions of state:

| Dimension | Proposed explicit states | Meaning |
|---|---|---|
| Technical | Draft, Submitted, Returned, TechnicallyApproved, Voided | Correct form, balanced lines and reviewer conclusion for the exact revision. |
| Management disposition | Pending, Accepted, Rejected, PartiallyAccepted, NotRequiredByApprovedPolicy | Attributable decision; never inferred from technical approval. |
| Source reflection | Unassessed, NotReflected, Reflected, PartiallyReflected, Unknown | Whether this exact adjustment is already present in the selected received source. |

Current `Posted` and `ReflectedInSource` values require an explicit compatibility map; do not reinterpret them as external ERP posting or replace all historical strings blindly. New immutable decision records may supply richer state without rewriting old journals.

**Invariants:** at least two lines; valid posting accounts in the approved chart; one positive side per line; exact debit/credit equality; same reporting context/functional currency; dates within the eligible period or authorized amendment; evidence/rationale; no self-review. A client's management decision must be within the correct client/engagement authority, not an application administrator impersonating management.

Every line retains account stable identity, source code, dimension tuple, debit/credit, description and optional finding/evidence relationship. A legitimate zero-origin chart account may be used, but then Module 21's mapping universe must include it before statements are valid.

**Reporting eligibility:** technically approved, management disposition satisfying the approved purpose policy, current source-specific reflection decision, and not superseded/void. Only **NotReflected** contributes the journal delta. **Reflected** contributes zero because it is already in source. **Unknown/PartiallyReflected** blocks final treatment until resolved. Keep rejected/uncorrected amounts visible in the findings/evaluation layer.

Partially accepted management decisions do **not** authorize arbitrary selected journal lines or multiplication by a percentage. Create explicitly linked replacement journals whose accepted portion is itself balanced, record the rejected remainder and obtain fresh required review. Likewise, partial reflection requires account/line-level proof and a balanced approved residual, not double counting.

**Formula:** `Adjusted(account) = Raw(account) + sum(eligible, not-reflected adjustment contributions)` at the preserved numeric precision. The effective contribution set is hashed and each logical purpose/source revision can contribute once. The snapshot stores the plan, not merely the final totals.

**Events:** `AdjustmentSubmitted`, `AdjustmentTechnicalDecisionRecorded`, `AdjustmentManagementDecisionRecorded`, `JournalReflectionAssessed`, `AdjustmentPlanSealed`, `AdjustmentSuperseded`.

<a id="persistence"></a>
### 2. Persistence & Migrations (`.Infrastructure`)

Extend current journal/line/decision/plan/snapshot `IEntityTypeConfiguration<T>` mappings. Add separate technical-decision or source-reflection history tables only if existing review/reconciliation entities cannot represent them.

Use context/base-dataset composite FKs; line account references resolve to a valid same-client chart identity without requiring a preexisting raw TB row. Preserve string source codes for auditability. A reporting journal with `GroupOnlyElimination` purpose must be routed to the group-owned contract or explicitly rejected on an entity basis; it cannot leak into entity results.

Unique `(FirmId,EngagementId,JournalNumber,BaseDatasetId,Revision)` or the current equivalent; unique plan membership for the same journal revision/purpose; unique snapshot per immutable plan/calculator/input hash. Enforce nonnegative amounts and mutually exclusive positive debit/credit at row level. Balance is checked at submission/approval under header lock and protected against later child inserts/edits.

Technical, management and reflection decisions are append-only with actor/evidence/time and exact target revision. Index decisions by journal/revision/stage and selected base. Superseded states never physically remove prior snapshots. Foreign keys to findings/evidence use Restrict and exact scope.

Proposed migration suffixes: `M22_JournalDecisionHistory`, `M22_SourceReflectionPins`, `M22_EffectiveAdjustmentSet`. Migrate legacy `Posted` records only when real technical and management evidence supports each derived status; otherwise mark `UnverifiedLegacy` and block new finalization rather than manufacture approvals.

<a id="cqrs"></a>
### 3. Application & CQRS Contracts (`.Application`)

**Input DTOs:**

- `AdjustmentLineDto`: LineId, AccountStableIdentity, Debit, Credit, DimensionValues[], Description, EvidenceRefs[], FindingId?.
- `AdjustmentDraftDto`: Context, BaseSourceRef, JournalNumber, Purpose, Origin, AccountingDate, Currency, Reason, Lines[], EvidenceRefs[], SupersedesRef?, ReversalOfRef?.
- `JournalReflectionDto`: JournalRef, SelectedBaseRef, ReflectionState, ObservedSourceJournal/LineReferences[], EvidenceRefs[], Rationale. Actual effects are recomputed; no caller-provided adjusted balance.
- `AdjustmentPlanInputDto`: Context, BaseSourceRef, JournalRevisionRefs[], ReflectionDecisionRefs[], ExpectedAdjustmentSetRevision, CalculatorVersion.
- `ManagementJournalDecisionDto`: JournalRef, Decision, EvidenceMode(SignedIn/Offline), EvidenceRefs[], Rationale, ActualManagementContactId? for permitted offline recording.

| Command / query and input | TResponse | Key validator + handler gate |
|---|---|---|
| `CreateAdjustmentDraftCommand(AdjustmentDraftDto, Meta)` | `MutationReceiptDto` | Valid current base/context, allowed purpose, valid line structure; incomplete draft clearly labelled. |
| `UpdateAdjustmentDraftCommand(JournalId, AdjustmentDraftDto, Meta)` | `MutationReceiptDto` | Draft/returned only; exact revision; reviewed content creates a new revision instead. |
| `SubmitAdjustmentCommand(JournalId, Meta)` | `MutationReceiptDto` | All lines/accounts/evidence complete; debit=credit; no unsupported currency/period. |
| `ReviewAdjustmentCommand(JournalId, DecisionInputDto, Meta)` | `MutationReceiptDto` | Exact submitted revision, independent technical reviewer; return/void requires reason. |
| `RecordAdjustmentManagementDecisionCommand(ManagementJournalDecisionDto, Meta)` | `MutationReceiptDto` | Technical eligibility, actual client-management authority or explicitly permitted offline recorder; exact decision evidence. |
| `AssessJournalReflectionCommand(JournalReflectionDto, Meta)` | `MutationReceiptDto` | Base and journal match; source refs valid; ambiguous/partial remains blocked for final inclusion. |
| `ReviseAdjustmentCommand(OriginalJournal: Ref, AdjustmentDraftDto, Meta)` | `MutationReceiptDto` | Original retained; new base/version and reason; no inherited approval/reflection. |
| `ReverseAdjustmentCommand(OriginalJournal: Ref, NewJournalNumber, AccountingDate, EvidenceRefs[], Meta)` | `MutationReceiptDto` | Authorized open/amendment context, balanced reversal lines derived from original; no duplicate reversal; fresh review. |
| `SealAdjustmentPlanCommand(AdjustmentPlanInputDto, Meta)` | `MutationReceiptDto` | Recompute complete eligible journal/reflection set and generation; unresolved treatment blocks. |
| `BuildAdjustedTrialBalanceCommand(PlanRef, Meta)` | `OperationTicketDto` | Current sealed plan; deterministic snapshot, idempotent key and no raw-source mutation. |
| `GetAdjustmentRegisterQuery(Context, StatusFilters, PageRequest)` | `PageDto<AdjustmentSummaryDto>` | Submitted/returned/rejected/eligible separately visible; scoped totals by currency. |
| `GetAdjustmentDetailQuery(JournalId)` | `AdjustmentDetailDto` | Header/lines, immutable decisions, source reflection and prior revisions; capabilities. |
| `GetAdjustedTrialBalanceQuery(SnapshotRef, AccountFilter, PageRequest)` | `PageDto<AdjustedBalanceRowDto>` | Raw, reporting delta, adjusted, source account, journal contributions and mapping coverage. |
| `GetAdjustmentEligibilityQuery(Context)` | `AdjustmentEligibilityDto` | Exact membership revision, eligible/excluded/blocked journals and reason codes. |

Output detail DTOs expose draft fields plus persisted refs and decision histories; summaries include number, base/revision, amounts, the three state dimensions and currentness. `AdjustedBalanceRowDto` carries RawAmount, Delta, AdjustedAmount, Currency and contribution refs. `AdjustmentEligibilityDto` is a report, not an approval command.

Use existing `AdjustmentJournalService` and `AdjustmentPlanService`. Add structural `AbstractValidator<T>` checks, but keep balanced-content/independence/source-generation checks inside authoritative handlers. Map stable errors to `journal.rejected`, `generation.stale`, `revision.stale`, `scope.denied` or approved new catalogue entries; never parse free-text error messages in UI.

<a id="lineage"></a>
### 4. Inter-Module Lineage & Boundaries

M22 reads source/COA from M20–21 and existing audit findings/evidence; it owns journal treatment and adjustment plans. M23 may link a proposed correction to this module but cannot mark it accepted. M24 consumes only a sealed effective plan and an adjusted snapshot; it does not rescan all “latest posted” journals independently. This prevents calculation engines from disagreeing on eligibility.

A new management decision or reflection correction changes adjustment-set membership and invalidates affected statements/packages even when the raw TB hash stays the same. A source replacement makes source-reflection applicability stale, not history false. A previously issued package preserves its plan and bytes; use the existing amendment/reissue path outside this module.

Management acceptance never posts to an external ledger. An uploaded replacement TB that already includes the adjustment requires explicit Reflected evidence; otherwise the system must not automatically infer that matching amounts prove identity.

<a id="blazor"></a>
### 5. Blazor UI Architecture (`.Web`)

```text
AdjustmentJournalWorkspacePage
  ContextAndBaseSourceBanner / AdjustmentRegister
  JournalEditor(EditForm)
    HeaderFields / DebitCreditGrid / AccountDimensionPicker
    EvidenceAndFindingLinks / BalanceSummary
  TechnicalReviewDialog
  ManagementDecisionPanel
  ReflectionAssessmentPanel
  AdjustmentPlanPreview → RawDeltaAdjustedGrid
```

Reuse the existing Journals surface, extending rather than creating a competing journal route. `AdjustmentEditorState` stores draft IDs, exact base and line models. A row edit updates local balance preview, but no preview is considered posted/accepted. Submit/review waits for server receipt. Render distinct technical/management/reflection badges; one green “Approved” badge is insufficient.

All form rows have stable LineId keys; deletion/reorder retains errors against the correct line. Dirty route guards protect unsaved drafts. An upstream-source notification produces a stale warning and disables final submit until rebase/revision is explicitly selected. Management view hides internal professional working notes while retaining the proposed journal and permitted supporting explanation.

<a id="verification"></a>
### 6. Edge Cases, Security & Verification

| Layer | Proposed class | Required tests |
|---|---|---|
| xUnit domain | `AdjustmentEligibilityTests` | `BalancedSixDecimalLinesRequired`; `DebitAndCreditOnSameLineRejected`; `AjeOnlyPostingAccountAllowedButMustMap`; `PartialManagementDecisionDoesNotApplyUnbalancedSubset`; `ReflectedContributionIsZero`. |
| xUnit PostgreSQL | `AdjustmentPlanLineageTests` | `SamePersonDifferentRoleCannotReview`; `CrossClientManagementDecisionDenied`; `SourceReplacementRequiresFreshReflection`; `SameJournalCannotContributeTwice`; `NewAcceptedJournalChangesSetManifest`; `PostedHistoryRejectsMutation`. |
| bUnit | `AdjustmentJournalEditorTests` | Correct row validation, balancing preview, distinct statuses, returned-draft handling, stale-source alert and no optimistic approval. |
| Playwright | `R2R22AdjustmentJourneys` | Create arbitrary multi-line AJE → independent review → management acceptance → reflection → snapshot; reject/partial/revise; replace TB containing the AJE and prove no double inclusion; revoke scope mid-form. |

**Exit gate:** raw plus eligible deltas reconciles exactly; management rejection, reflection uncertainty and source replacement are visible and cannot be bypassed by direct handler invocation.
