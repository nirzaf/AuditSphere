[Master index](../00_INDEX.md) · [Original module in source](../source/ORIGINAL_R2R_Blueprint_Modules_20-26.md#module-24)

**Full module contract — required reading for its tasks.** No task may omit an applicable invariant merely because the task card is shorter. Names retain the original EXISTING / EXTEND / NEW / DECISION labels.

<!-- SOURCE-LINES: 841-963 -->
## Module 24 — Financial Statements, Comparatives and Disclosures

**Business outcome:** produce a complete, policy-appropriate and source-traceable statement set, not merely a formatted trial balance.  
**Requirement coverage:** VP-040 and VP-041.  
**Sequence:** M24.1 approved layout/policy → M24.2 balances/comparatives → M24.3 cash/equity/notes → M24.4 deterministic statement set → M24.5 review/restatement.

<a id="domain"></a>
### 1. Domain Modeling (`.Domain`)

**EXISTING/EXTEND:** existing financial-package lines, validations, cash-flow/disclosure/equity/note records; `ReportingTaxonomyVersion`/nodes; `ClientPeriodRestatement`; existing deterministic financial-statement/calculation services. They are not missing simply because preparation and assembly currently share a `FinancialPackage` model. [R4](../reference/02_Standards_and_Source_Register.md#source-r4)[R5](../reference/02_Standards_and_Source_Register.md#source-r5)

**NEW if necessary:** independently versioned `StatementLayoutVersion`, typed `StatementLineDefinition`, `CashFlowScheduleRevision`, `DisclosureNoteRevision`, `EquityMovementSchedule`, `StatementSetRevision` and `SubsequentEventAssessment`. Where existing records already persist these facts, extend them instead. If a StatementSet layer is introduced, it stores the one canonical calculated result and M25 references it; do not calculate a competing result in the package builder.

**Statement scope:** financial position; profit or loss and other comprehensive income in the applicable one/two-statement presentation; changes in equity; cash flows; notes; comparative information and an opening comparative financial position where the selected framework requires it. Avoid treating “BS/P&L” labels as the entire reporting obligation. [S6](../reference/02_Standards_and_Source_Register.md#source-s6)

**Policy version:** framework, edition, jurisdiction/industry applicability, effective-from/to, early-adoption decision, taxonomy/layout family, required sections/disclosures, cash-flow classification policy, rounding, reviewed owner and policy hash. A user selects an approved policy; the engine does not decide which law applies.

For IFRS 18 periods, the policy must address its defined subtotals, classification/aggregation and applicable management-defined-performance-measure disclosures. A historical IAS 1 report retains its original policy. Transition comparative re-presentation is an explicit reviewed output, not an overwrite. Initial industry support must be enumerated; an unsupported bank/insurer or specialized business profile cannot inherit a general-company classification silently. [S1](../reference/02_Standards_and_Source_Register.md#source-s1)

**Layout grammar:** allow Heading, MappedTaxonomyBalance, SumChildLines, Add/SubtractReferencedLines, approved Ratio with divide-by-zero behavior, and linked Schedule/Note display. Each numeric line has explicit source kind, sign, order, subtotal role and current/comparative applicability. No arbitrary C#, SQL, JavaScript, Excel expressions or free text interpreted as a calculation. Enforce acyclic references, valid same-layout targets, supported sections and no unintended inclusion of both parent totals and the same leaf values.

**Cash-flow contract:** explicit opening/closing cash and cash-equivalent membership; direct or indirect method selected from the approved supported profile; movement IDs, date, amount/currency, activity classification, cash/noncash flag, source reference, FX cash effect and rationale. First deliver one complete approved indirect profile; direct-method output is enabled only when its complete movement-input tests exist. No requirement to invent direct cash receipts from a closing TB.

Reconcile `Opening cash + operating + investing + financing + FX effect on cash = Closing cash`. Noncash investing/financing items are separately disclosed, not counted as cash. An indirect schedule needs supported movements/adjustments to distinguish working capital, acquisitions, FX, noncash changes and reclassifications. The edition controls the starting profit subtotal and interest/dividend classification. [S2](../reference/02_Standards_and_Source_Register.md#source-s2)

**Equity contract:** reconcile opening equity by component + current profit/loss + OCI + owner contributions − distributions + approved prior-period adjustments = closing equity. Source books that already transfer profit to retained earnings need the approved source-closing convention, so profit is not added twice. Contributions/distributions cannot silently default to none.

**Notes:** NoteCode, PolicyRequirementId, applicability(Pending/Required/NotApplicable), text/table, linked statement lines, source/evidence, owner/reviewer, revision and status. Required notes need content; NotApplicable needs a reason and appropriate review. Include bounded related-party, going-concern and subsequent-event inputs without autonomous conclusions. Record financial-statement authorization date separately from generation date. IAS 10 adjusting/nonadjusting treatment is recorded by a qualified user. [S7](../reference/02_Standards_and_Source_Register.md#source-s7)

**Comparatives/restatements:** distinguish AsIssued, Reclassified, RestatedError, PolicyTransition and ProspectiveEstimateChange. IAS 8 errors/policy changes and estimate changes have different temporal effects; the user supplies the approved treatment/evidence. Do not classify every difference as a current-year AJE. Keep original and revised package/statement references, period-by-period impact and disclosures. [S3](../reference/02_Standards_and_Source_Register.md#source-s3)

**Lifecycle:** layouts/notes/schedules Draft → Submitted → independently Approved/Returned → successor revision. Statement set Calculated → ValidationBlocked or ReadyForReview → Reviewed; separate currentness. Any changed basis invalidates current review, not historical content.

<a id="persistence"></a>
### 2. Persistence & Migrations (`.Infrastructure`)

Extract/extend the current package/line/note/cash/equity/validation mappings. Add separate layout/statement revision tables only as justified by ADR-07. Keep one canonical result-to-source mapping.

Required persistence:

- Layout unique by framework/edition/family/version; line IDs stable within immutable layout, referenced lines same layout version; acyclic formula graph checked before publication.
- Note/schedule revisions scoped to context and exact period/currency; note code and line order unique in owner revision. Foreign keys bind evidential sources and policy requirement IDs.
- StatementSetRevision pins accepted raw source, adjusted snapshot/plan, mapping/universe, reconciliation readiness, current/prior context, layout/note/cash/equity/rounding/calculator refs and input manifest hash.
- Persist line-level amounts and lineage, not merely section totals. Compare line totals to canonical account contributions; normalized source records remain unchanged.
- Comparative linkage includes exact **prior revision**, selection mode and approved restatement/reclassification evidence. Do not use an unqualified “latest prior package” lookup.
- Validation facts persist rule version, inputs and outcomes. Review references exact result hash; positive validation is not a reviewer signature.
- Restrict deletion of all referenced configuration, sources, notes, revisions and reviews. Index context/period/state and reverse dependencies to find stale consumers.

Proposed migrations: `M24_StatementLayoutVersions`, `M24_SupplementarySchedulePins`, `M24_StatementSetManifest`, `M24_ComparativeRestatementEvidence`. Existing completed statement/package records get source-preserving adapters; they are not reconstructed with today's policy to populate new mandatory fields. Unprovable historical pins remain explicitly legacy.

<a id="cqrs"></a>
### 3. Application & CQRS Contracts (`.Application`)

**DTOs:**

- `StatementLayoutDraftDto`: FrameworkPolicyRef, Name, VersionBaseRef?, StatementKinds[], Lines[LineId,Code,Label,Kind,Section,DisplaySign,Order,TaxonomyNodeIds[],ReferencedLineIds[],Operation,ScheduleKind?,ComparativeMode], RoundingPolicyRef.
- `ComparativeSelectionDto`: CurrentContext, PriorStatementOrPackageRef, Mode, RestatementCaseRef?, BasisBridgeRef?, Explanation.
- `CashFlowScheduleDto`: Context, Method, PolicyRef, OpeningCashSourceRef, ClosingCashSourceRef, CashAccountIdentities[], Movements[Id,Date,Amount,Currency,Activity,IsNonCash,SourceRefs[],Rationale], FxCashEffects[], StartingProfitLineRef for indirect.
- `EquityScheduleDto`: Context, OpeningEquitySourceRef, Components[], Movements[Id,ComponentId,Type,Amount,Date,SourceRefs[],Rationale], ProfitTransferConvention.
- `DisclosureDraftDto`: Context, NoteCode, PolicyRequirementId, Applicability, Text, TypedTable?, StatementLineLinks[], EvidenceRefs[], NotApplicableReason?.
- `StatementBuildInputDto`: Context, AdjustedSnapshotRef, MappingRef, LayoutRef, ComparativeSelectionRef?, CashScheduleRef, EquityScheduleRef, DisclosureRevisionRefs[], ExpectedNoteSetRevision, ReconciliationReadinessRef.
- `RestatementDraftDto`: Context, OriginalStatementOrPackageRef, ChangeType, AffectedPeriods[], ProposedJournalOrReclassificationRefs[], RevisedBasis, Reason, EvidenceRefs[], MethodologyDecisionRef.

| Command / query and input | TResponse | Validation and authoritative rule |
|---|---|---|
| `SaveStatementLayoutCommand(StatementLayoutDraftDto, Meta)` | `MutationReceiptDto` | Allowed grammar, unique lines/order, valid references/sections, no formula cycles or duplicated primary balances. |
| `PublishStatementLayoutCommand(LayoutId, DecisionInputDto, Meta)` | `MutationReceiptDto` | Complete policy-required sections, golden fixture validation, independent publication and immutable version. |
| `SelectComparativeBasisCommand(ComparativeSelectionDto, Meta)` | `MutationReceiptDto` | Same entity/compatible basis/currency; exact approved prior source; explicit restatement/reclassification. |
| `SaveCashFlowScheduleCommand(CashFlowScheduleDto, Meta)` | `MutationReceiptDto` | Defined cash population and support, policy-valid categories, dates/currencies and no duplicate movement IDs. |
| `SaveEquityMovementScheduleCommand(EquityScheduleDto, Meta)` | `MutationReceiptDto` | All equity components and owner movements explicit; no double profit transfer. |
| `SaveDisclosureNoteCommand(DisclosureDraftDto, Meta)` | `MutationReceiptDto` | Applicability/content/rationale, typed table bounds, safe markup and valid same-context line/evidence refs. |
| `ReviewSupplementaryScheduleCommand(ScheduleKind, ScheduleRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Allowlisted kind; exact current schedule, independent reviewer and documented reconciliation. |
| `ReviewDisclosureNoteCommand(NoteRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Required content/evidence, exact version, independent reviewer; NoApplicable is evidenced not a blank bypass. |
| `BuildStatementSetCommand(StatementBuildInputDto, Meta)` | `OperationTicketDto` | Complete approved inputs; capture membership revisions; worker uses immutable inputs and fenced publication. |
| `ReviewStatementSetCommand(StatementSetRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Every blocking validation passes, manifest remains current, appropriate independent reviewer. |
| `CreateRestatementCaseCommand(RestatementDraftDto, Meta)` | `MutationReceiptDto` | Original preserved; supported change classification and period impact; estimate change not silently retrospective. |
| `ApproveRestatementCaseCommand(CaseId, RevisedStatementSetRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Distinct reviewer, exact original/revised manifests, balanced impacts and complete required disclosure. |
| `GetStatementWorkspaceQuery(Context)` | `StatementWorkspaceDto` | Selected source, mapping/layout/supplementary readiness and eligible comparative options. |
| `GetStatementSetQuery(StatementSetRef)` | `StatementSetDto` | Typed lines, current/prior values, OCI/equity/cash/notes, validations and manifest. |
| `GetStatementLineageQuery(StatementSetRef, LineId, PageRequest)` | `PageDto<StatementContributionDto>` | Source accounts/journal contributions/mapping fractions plus schedule references; no hidden source access. |
| `GetComparativeDifferenceQuery(OriginalRef, RevisedRef)` | `ComparativeDifferenceDto` | Per-line original/revised/delta and treatment evidence; scoped same entity. |

StatementSetDto is the canonical immutable preparation input for M25, not a re-evaluatable expression tree. `StatementContributionDto` identifies SourceRef, AccountIdentity, JournalRef?, MappingAllocationId, Fraction, SignedAmount and currency. Workspace/ComparativeDifference DTOs contain exact refs and derived currentness. No client-supplied totals are treated as calculated results.

Reuse `FinancialStatementService` and the existing arithmetic engines. New layout operations require exhaustive pure tests; do not add a second SQL/JavaScript calculation inside Razor or a PDF renderer.

<a id="lineage"></a>
### 4. Inter-Module Lineage & Boundaries

M24 consumes M21 source/mapping, M22 adjusted snapshot and M23 required reconciliation status. M20 owns chart/taxonomy identity; M24 owns presentation semantics and layout publication. This prevents a bootstrap cycle where mappings require a statement result that itself needs mappings.

M24 returns a sealed **preparation snapshot** for M25. A new note or required applicability decision changes note-set membership and stales current output even when totals do not change. A prior-period mapping/restatement change must identify all current comparative consumers. The old as-issued comparative is still available; user selection cannot silently switch to a restated version.

Source-to-line, line-to-note and current-to-prior references form a directed acyclic dependency graph. Layout formatting edits affect artifacts; amount/classification edits additionally affect numeric validations. Both require a new reviewed output version according to their affected scope.

<a id="blazor"></a>
### 5. Blazor UI Architecture (`.Web`)

```text
FinancialStatementsWorkspacePage
  ContextAndPolicyBanner / CurrentPriorSelector
  StatementLayoutEditor → LineDefinitionDialog / LayoutValidationPanel
  StatementTabs (position, performance/OCI, equity, cash flows)
    StatementGrid → SourceContributionDrawer
  CashFlowEditor / EquityMovementEditor
  NotesRegister → DisclosureEditor / ApplicabilityReview
  ValidationSummary / IndependentStatementReview
  ComparativeRestatementWizard → AsIssuedVsRestatedComparison
```

`StatementWorkspaceState` stores exact selected versions and draft layout/schedule/note forms. Each form has its own EditContext; switching a tab preserves acknowledged context but cannot silently save another dirty form. Layout preview clearly distinguishes Draft/NotValidated from a reviewed statement set.

Unsupported or missing cash-flow movements display unavailable/required inputs, not illustrative numbers in a production report. Missing prior data displays unavailable rather than zero. Totals are formatted in UI but calculated only on the server. Source drill-down reauthorizes the underlying record; group-only access does not imply every client account is viewable.

Publication/review waits for server verification, and stale data disables review actions. A note-edit callback refreshes the statement readiness/manifest, not just the visible note text. Explicit dirty navigation and failed-generation messages preserve unsaved work without claiming a saved version.

<a id="verification"></a>
### 6. Edge Cases, Security & Verification

| Layer | Proposed class | Required tests |
|---|---|---|
| xUnit domain | `StatementLayoutAndPolicyTests` | `CyclicSubtotalRejected`; `ParentAndLeafDoubleCountDetected`; `DisplaySignDoesNotChangeCanonicalAmount`; `MissingRequiredNoteBlocks`; `IFRS18AndLegacyPoliciesRemainVersioned`. |
| xUnit numerical | `CashEquityComparativeTests` | `NonCashAcquisitionExcludedFromCashMovements`; `CashReconcilesWithSeparateFxEffect`; `ProfitTransferNotCountedTwice`; `MissingComparativeNotZero`; `PriorErrorDoesNotBecomeCurrentProfit`; `EstimateChangeUsesApprovedProspectiveTreatment`. |
| xUnit PostgreSQL | `StatementRevisionLineageTests` | `ChangedPriorMappingStalesCurrentComparative`; `NewRequiredNoteStalesSet`; `ReviewedSetRejectsMutation`; `RestatementRetainsAsIssuedReferences`; `ConcurrentUpstreamChangeBlocksReview`. |
| bUnit | `StatementDesignerTests`, `DisclosureEditorTests` | Grammar/row errors, required/N/A fields, dirty note switch, missing cash-flow state, exact lineage drawer and independent review controls. |
| Playwright | `R2R24StatementJourneys` | Create approved layout; compare current/prior; supported cash/equity/notes; independently review; change prior source and observe stale state; produce separate restatement preserving originals. |

**Exit gate:** all required statements/notes in the selected policy are supported, reconciled and reviewed for exact inputs. Unsupported methods or missing schedules block the relevant final output rather than being omitted silently.
