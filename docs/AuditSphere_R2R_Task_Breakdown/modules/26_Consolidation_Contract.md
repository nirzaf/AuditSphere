[Master index](../00_INDEX.md) · [Original module in source](../source/ORIGINAL_R2R_Blueprint_Modules_20-26.md#module-26)

**Full module contract — required reading for its tasks.** No task may omit an applicable invariant merely because the task card is shorter. Names retain the original EXISTING / EXTEND / NEW / DECISION labels.

<!-- SOURCE-LINES: 1081-1219 -->
## Module 26 — Consolidation, FX and Intercompany Eliminations

**Business outcome:** combine eligible component results into a separately governed group report, preserving each component's books and proving every group-only adjustment.  
**Requirement coverage:** VP-043–VP-046.  
**Sequence:** M26.1 group/perimeter → M26.2 component pins → M26.3 alignment/FX → M26.4 manual matching and journals → M26.5 calculation/review → M26.6 group package.

<a id="domain"></a>
### 1. Domain Modeling (`.Domain`)

**EXISTING/EXTEND:** `ClientGroup`, `ClientGroupMembership`, group grants, current consolidation scope/component/package/match/journal/run records, exchange-rate/policy records and calculators. Existing `ConsolidationService` already exposes group/membership, scope, internal/external component, intercompany match, journal and report request contracts. Preserve them. [R6](../reference/02_Standards_and_Source_Register.md#source-r6)[R9](../reference/02_Standards_and_Source_Register.md#source-r9)

Logical aggregate responsibilities:

| Aggregate / logical owner | Required records and responsibility |
|---|---|
| Group/perimeter revision | Group identity, parent, members, dates, ownership/economic interests, control assessment/evidence, method and independent acceptance. |
| Consolidation scope | Exact approved perimeter, reporting period/basis/presentation currency, policy/taxonomy/method, prior scope and rate-policy references. |
| Component acceptance | Entity, exact period/book/basis/package/statement/artifact refs, review eligibility, source revision/hash and compatibility bridges. |
| Exchange-rate set/policy | Currency pair/direction, rate date/type, precision, source/evidence, effective range, approval and versioned line-rate rules. |
| Group journal | Kind, participating entities/pairs, source matches, balanced group-currency lines, taxonomy, reason/evidence, independent decision. |
| Consolidation run | Exact membership/input manifest, component/translation/alignment/elimination rows, reconciliation and result hash, independent review. |

**Control is not ownership alone.** Store a qualified user's control conclusion and scope rationale. The first enabled acceptance profile remains one parent and one wholly-owned subsidiary with aligned reporting period/basis; ownership alone does not establish legal control. Do not produce results for Associates, minority interests, mid-period acquisition or hyperinflation merely by multiplying balances by a percentage. Existing advanced code and approved historical fixtures are preserved; each advanced production profile needs its own approved method/inputs/tests. [S5](../reference/02_Standards_and_Source_Register.md#source-s5)

An effective perimeter is a dated directed graph. Reject self-membership, cycles, duplicate economic inclusion, missing parent, incompatible periods or ambiguous overlap. Where ownership changes during a period, an approved advanced profile must supply cutover data; the simple full-period profile blocks instead of inventing proration. Consolidation relationship groups remain separate from CRM contact groups.

**Component compatibility:** same reporting scope/date and approved accounting policy, or an explicit reviewed alignment bridge. Pin exact reviewed internal packages. Where current external component-pack support is used, require equivalent source reconciliation, review, taxonomy compatibility and immutable digest—never treat an uploaded unreviewed spreadsheet as an accepted component.

**FX policy:** keep transaction-to-functional remeasurement separate from translating a foreign operation to the group's presentation currency. Imported functional amounts are not silently recalculated. For the supported nonhyperinflation foreign-operation profile, assets/liabilities use the applicable closing rate; income/expenses use transaction-date rates or a documented representative average; equity uses historical/movement lineage and opening accumulated reserves. Resulting translation differences require an explained OCI/equity bridge. Never convert every statement line at one closing rate. [S4](../reference/02_Standards_and_Source_Register.md#source-s4)

An average is allowed only where the approved method determines it adequately approximates transaction rates. Missing/zero/negative/wrong-direction rates block. Same-currency rate 1 is permitted because source and target are identical, not as an error fallback. Nonexchangeability, hyperinflation, acquisition accounting, NCI, disposals and nested groups require their separately supported policy; do not guess treatments.

**Eliminations:** include applicable investment/equity, intragroup receivable/payable, revenue/expense, dividends and intragroup cash-flow effects, plus supported unrealized-profit adjustments. The base golden profile must at least prove investment/equity and receivable/payable elimination. Unsupported consequential treatment is an explicit blocker, not “complete consolidation”. Group-only adjustments never post into entity books. [S5](../reference/02_Standards_and_Source_Register.md#source-s5)

Manual matching records both source sides, original currency/amounts, translated amounts, matched portion and explained residual. Currency/timing differences are not automatically offset or hidden. Every journal is balanced in group currency, scoped to the exact perimeter/input version and included once. A source match cannot be consumed twice by overlapping elimination journals.

**Formula:** `Consolidated = translated compatible components + approved policy alignment + approved group adjustments/eliminations`. Each column reconciles at account/taxonomy, entity and group level. Cumulative translation reserve is a supported calculated result with a rate/equity bridge, not a general imbalance plug.

**Lifecycle:** perimeter Draft → Submitted → Approved; components Submitted/Returned → Approved; rate sets Draft → Reviewed; journals Draft → Submitted → Reviewed/Returned; run Calculated → Validated → IndependentlyApproved. Currentness is separate. A changed input requires a new run, never silently refreshing an approved result.

<a id="persistence"></a>
### 2. Persistence & Migrations (`.Infrastructure`)

Extract/extend existing configurations for groups/memberships/scopes/components, rate sets/observations/policies, matches/journals/lines and runs. A real entity FK proves membership; a label such as “Subsidiary A” is not enough. Group period representation must be documented: reuse the existing scope period only where it unambiguously models group reporting; do not borrow an unrelated client period simply to satisfy a required Guid.

Constraints:

- Unique group code within firm; membership revision/effective dates and owner scope; no overlapping active membership of the same entity in a perimeter interval.
- Unique component entity/package purpose in one scope revision. Typed component-source discriminator requires one internal package or accepted external pack, never both/neither.
- Component immutable pin includes exact accepted package hash/revision, accounting input hash, mapping/policy/period and approval refs. A caller cannot submit edited component rows as a package snapshot.
- Rate-set version unique; currency pair/type/date key; positive rate, valid effective range and immutable approval. Preserve precision consistently across .NET/PostgreSQL.
- Group journal header/lines share firm/group/scope/currency, taxonomy and counterparty identities. Balance and matched-source consumption validated under locks and protected afterward.
- Run manifest unique for scope/method/input digest; run-output rows reference immutable component/translation/journal contributions. Prior group result never overwritten.
- Restrict every component, package, match, journal, rate and historical run reference. Group-only reviewer read access does not create a raw component-artifact grant.

Proposed migrations: `M26_PerimeterRevisionPins`, `M26_ComponentCompatibilityEvidence`, `M26_RatePolicyLineage`, `M26_EliminationConsumption`, `M26_GroupResultManifest`. Existing approved advanced-method schedules/runs stay intact; no destructive simplification to the initial baseline profile.

Cross-client locking must obey ADR-05. Do not take a group lock and then acquire clients in arbitrary order while another operation does the reverse. Pin component identities and calculation input hashes; no database transaction remains open for rendering/group user review.

<a id="cqrs"></a>
### 3. Application & CQRS Contracts (`.Application`)

**DTOs:**

- `GroupDraftDto`: Code, Name, ReportingPeriodRef, ReportingCurrency, Basis, FrameworkPolicyRef, MethodProfileId, ManagerUserId.
- `PerimeterDraftDto`: GroupId, PriorPerimeterRef?, Members[ClientId/EntityId,ParentEntityId?,EffectiveFrom/To?,OwnershipPercent,EconomicInterestPercent,ControlConclusion,Method,EvidenceRefs[]].
- `ComponentPinDto`: ScopeVersionRef, EntityId, SourceKind, InternalPackageRef? or ExternalPackRef?, AccountingInputDigest, PeriodRef, Basis, Currency, TaxonomyRef, MappingRef, CompatibilityBridgeRef?.
- `RateSetDraftDto`: GroupId, EffectiveRange, SourceDescription, Observations[FromCurrency,ToCurrency,Type,Date,Rate,EvidenceRef], PriorRateSetRef?.
- `TranslationPolicyDraftDto`: FrameworkPolicyRef, ProfileCode, LineRules[Taxonomy/AccountSelector,Closing/Transaction/Average/Historical/CarryForward,RateType,SourceRequirement], AverageRepresentativenessEvidence?, CtaTreatment, RoundingPolicy.
- `IntercompanyPairDto`: ScopeVersionRef, LeftEntityId, RightEntityId, LeftSourceLineRefs[], RightSourceLineRefs[], AccountNature, OriginalCurrency, OriginalAmounts, MatchedAmount, ResidualReason, EvidenceRefs[].
- `GroupJournalDraftDto`: ScopeVersionRef, Number, Kind, Currency, SourceMatchRefs[], Lines[LineId,EntityId?,TaxonomyNodeId,Debit,Credit,Description], EvidenceRefs[], Reason, PredecessorRef?.
- `ConsolidationRunInputDto`: ScopeVersionRef, ComponentPinRefs[], RateSetRef?, TranslationPolicyRef?, AlignmentRefs[], ApprovedJournalRefs[], ExpectedComponentSetRevision, ExpectedEliminationSetRevision, PriorGroupRunRef?.

| Command / query and input | TResponse | Validation + authoritative gate |
|---|---|---|
| `CreateConsolidationGroupCommand(GroupDraftDto, Meta)` | `MutationReceiptDto` | Explicit group-creation authority; approved method/profile; no automatic professional-role promotion. |
| `SavePerimeterDraftCommand(PerimeterDraftDto, Meta)` | `MutationReceiptDto` | Same-firm entities, valid dates/control evidence, no cycles/duplicates/unsupported ownership. |
| `ReviewConsolidationPerimeterCommand(PerimeterRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Independent allowed reviewer, supported exact scope/method and compatible periods. |
| `PinConsolidationComponentCommand(ComponentPinDto, Meta)` | `MutationReceiptDto` | Read server-owned package/approval/hash; membership and compatibility current; no caller-invented values. |
| `ReplaceComponentPinCommand(OldPinRef, ComponentPinDto, Meta)` | `MutationReceiptDto` | Explicit reviewed replacement, same intended member/purpose; new scope/run basis, history preserved. |
| `SaveExchangeRateSetCommand(RateSetDraftDto, Meta)` | `MutationReceiptDto` | Positive bounded rates, correct pairs/types/dates, no ambiguous duplicates or implied direction. |
| `ReviewExchangeRateSetCommand(RateSetRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Independent review, source evidence, complete required date/type coverage. |
| `PublishTranslationPolicyCommand(TranslationPolicyDraftDto, DecisionInputDto, Meta)` | `MutationReceiptDto` | Approved method and golden fixtures; historical/average/closing rules complete. |
| `TranslateComponentCommand(ComponentPinRef, RateSetRef, TranslationPolicyRef, Meta)` | `OperationTicketDto` | Required current rates and compatible method; deterministic translation and reserve bridge. |
| `RecordIntercompanyPairCommand(IntercompanyPairDto, Meta)` | `MutationReceiptDto` | Both source sides scoped/eligible, supported pair, matched amount bounded, residual explained or open. |
| `SaveGroupJournalCommand(GroupJournalDraftDto, Meta)` | `MutationReceiptDto` | Valid distinct lines/accounts/currency and source-consumption identity; draft totals visible. |
| `SubmitGroupJournalCommand(JournalId, Meta)` | `MutationReceiptDto` | Exact balance, evidence, current input set and no duplicate elimination. |
| `ReviewGroupJournalCommand(JournalRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Independent reviewer and live input/currentness checks; return reason mandatory. |
| `BuildConsolidationRunCommand(ConsolidationRunInputDto, Meta)` | `OperationTicketDto` | Full effective member/component/journal set, approved policy, no unsupported profile or unexplained required mismatch. |
| `ReviewConsolidationRunCommand(RunRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Recompute current complete input manifest; validate columns/equations; independent group review. |
| `PrepareGroupPackageCommand(ApprovedRunRef, PackageDefinitionDraftDto, Meta)` | `MutationReceiptDto` | Group branch of M25 contract only; no recalculation of component sources or borrowed client scope. |
| `GetConsolidationWorkspaceQuery(GroupId, ScopeVersionId?)` | `ConsolidationWorkspaceDto` | Perimeter/components/method/rates/journals/readiness and allowed actions. |
| `GetComponentReadinessQuery(ScopeVersionRef)` | `ComponentReadinessDto` | Required members, eligible exact package choices and explicit mismatches; no “first two clients”. |
| `GetTranslationBridgeQuery(TranslationRef, PageRequest)` | `PageDto<TranslationLineDto>` | Original, rate/purpose, translated value, historical/CTA/rounding lineage. |
| `GetIntercompanyExceptionsQuery(ScopeVersionRef, PageRequest)` | `PageDto<IntercompanyExceptionDto>` | Original/translated sides, matched and unmatched amounts with scope-safe references. |
| `GetConsolidationReportQuery(RunRef, PageRequest)` | `ConsolidationReportDto` | Component/alignment/elimination/group totals, input manifest and currentness; stale current report not asserted approved. |
| `GetGroupSourceDrillDownQuery(RunRef, OutputLineId, PageRequest)` | `PageDto<GroupContributionDto>` | Group-permitted contributions; raw component download requires separate component authorization. |

DTO projections retain IDs and all numeric bridge columns; `ConsolidationReportDto` includes exact run/method/perimeter/currency and equations. `ComponentReadinessDto` reports missing inputs without exposing unauthorized package content. `TranslationLineDto` includes SourceAmount, SourceCurrency, RateRef, RateType, TranslatedAmount, PresentationCurrency and calculated reserve/rounding category. GroupContributionDto identifies the approved source pin rather than returning its entire private file.

<a id="lineage"></a>
### 4. Inter-Module Lineage & Boundaries

Components arrive only from reviewed M25 packages or the existing equivalently accepted external-pack contract. M26 must not fetch live mutable TB totals to replace pinned package values. Map compatible taxonomy and policy versions through reviewed alignment, preserving original source meaning.

M26 owns group-only balances. An elimination never becomes an M22 entity journal automatically. A user who discovers an actual source error creates a separate entity AJE through M22, obtains a new reviewed component package, then explicitly repins and rebuilds group output.

Rate/perimeter/component/journal changes stale the **group** result and group approvals, not the entity's original accounting approval merely because it was translated. A component becomes stale on its own inputs according to entity policy; current group finalization cannot continue consuming that component without its required reacceptance.

Entity close and group close are distinct: do not require “all groups closed” before an entity can supply a reviewed component while simultaneously requiring “all entities closed” before a group can start. Define reviewed eligible component packages as the input gate; separate close decisions follow each owner's output policy.

<a id="blazor"></a>
### 5. Blazor UI Architecture (`.Web`)

```text
ConsolidationWorkspacePage
  GroupContextSelector / SupportedMethodBanner
  PerimeterEditor(EditForm) → OwnershipAndControlDialog
  ComponentIntakeGrid → PackagePinAndCompatibilityDialog
  RateSetEditor / TranslationPolicyReview / TranslationBridge
  IntercompanyReviewGrid → ManualMatchDialog
  GroupJournalEditor → IndependentJournalReview
  ConsolidatedColumnsGrid / ValidationPanel / RunHistory
  GroupReviewDialog → GroupPackageHandoff
```

Extend the existing Consolidation/AdvancedConsolidation routes; do not hide existing advanced history or make every profile available by a dropdown alone. `ConsolidationWorkspaceState` contains one selected group/scope/run, immutable DTOs and drafts. Changing group clears prior group/entity data first. Client-context cascade is not reused as group authority.

Clearly display reporting and source currencies, historical/average/closing rate kinds, unresolved pairs and input freshness. Manual matching has a preview and explicit confirm; no auto-match button. Elimination journal preview is group-only. Review cannot proceed on missing components or calculated artifacts still rendering.

Every editor has independent dirty tracking and exact expected revision. A component pin dialog displays eligibility from server data; users cannot paste arbitrary rows/hashes as an internal package. Source drill-down shows only allowed detail; denied deep links must not reveal hidden client names in errors.

<a id="verification"></a>
### 6. Edge Cases, Security & Verification

| Layer | Proposed class | Required tests |
|---|---|---|
| xUnit domain | `ConsolidationPerimeterTests` | `ControlConclusionNotInferredFromPercentage`; `SelfCycleAndDuplicateEntityRejected`; `UnsupportedMinorityOrMidPeriodProfileBlocked`; `CreatingGroupDoesNotGrantPartnerAuthority`. |
| xUnit numerical | `ConsolidationCurrencyAndEliminationTests` | `DifferentRateTypesProduceExplainedCta`; `ClosingRateNotAppliedToAllEquity`; `MissingRateCannotDefaultToOne`; `InvestmentEquityAndReceivablePayableEliminateOnce`; `UnmatchedDifferenceNotPlugged`; `SourceEntitiesRemainUnchanged`. |
| xUnit PostgreSQL | `ConsolidationInputManifestTests` | `ForgedComponentRowsRejected`; `NewComponentOrJournalChangesMembershipDigest`; `ChangedRateOrPerimeterStalesReview`; `ConcurrentComponentReplacementCannotApproveOldRun`; `GroupOnlyReviewerDeniedRawArtifact`; `PriorRunRetained`. |
| bUnit | `ConsolidationWorkspaceTests` | Explicit currency/rate labels, unsupported profile blocking, component mismatch messages, manual matching confirmation and dirty group switch. |
| Playwright | `R2R26GroupJourneys` | Parent/subsidiary same-currency full elimination; approved FX profile; missing/invalid rate; changed component/rebuild; independent run review; group package export; deny unrelated group and component artifact. |

**Exit gate:** component columns plus approved alignment/eliminations reconcile exactly to the reviewed group result; sources are unchanged, missing methods/rates remain blocked, and M25 can render a group package with the exact same reviewed manifest.
