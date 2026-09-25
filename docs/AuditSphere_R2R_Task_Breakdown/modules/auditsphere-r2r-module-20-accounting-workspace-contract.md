[Master index](../auditsphere-r2r-index-task-breakdown.md) · [Original module in source](../source/auditsphere-r2r-source-blueprint-modules-20-26-historical.md#module-20)

**Full module contract — required reading for its tasks.** No task may omit an applicable invariant merely because the task card is shorter. Names retain the original EXISTING / EXTEND / NEW / DECISION labels.

<!-- SOURCE-LINES: 349-477 -->
## Module 20 — Accounting: profiles, contexts, chart, periods and books

**Business outcome:** every source, journal, reconciliation and report has one defensible client/entity/period/book/currency/policy context.  
**Requirement coverage:** VP-034; supplies chart/taxonomy prerequisites for VP-037.  
**Sequence:** M20.1 profiles → M20.2 calendars/books → M20.3 chart/dimensions → M20.4 context activation. Close/amendment completion follows Modules 24–25.

<a id="domain"></a>
### 1. Domain Modeling (`.Domain`)

**EXISTING/EXTEND aggregate candidates:** `ClientAccountingProfile`, `ClientReportingPeriod`, `ClientReportingBook`, `ClientChartVersion`, `ClientAccount`, `SourceAccountAlias`, `ReportingTaxonomyVersion` and nodes. Preserve their IDs and existing persisted wire states. Encapsulate changes behind domain methods incrementally; do not just add an `AggregateRoot` base class and call existing mutable public setters safe.

**NEW where no equivalent exists:** immutable `ReportingContextRevision`, versioned `AccountingDimensionSchema`, profile-change evidence and a typed `PeriodCloseChecklist`. Reuse `ClientPeriodAmendment` and existing close controls instead of a second close history.

Core fields:

| Record | Required fields / relationships |
|---|---|
| Profile revision | Client/entity identity, jurisdiction, functional currency, fiscal calendar, source-system label, framework-policy revision, creator/reason, previous revision. |
| Period/book | Existing owner IDs, code, start/end, basis, currency, prior-period reference, state and amendment lineage. A book selects an approved inclusion rule, not executable code. |
| Chart revision | Client, stable account identity, account code/name, type, normal balance, parent stable identity, posting/active flags, publication authority/time. |
| Dimension schema | Allowed dimension types and values with stable IDs/codes, required/optional rules, active/effective dates and schema revision. |
| Reporting context | Exact profile, chart, dimension, policy, engagement, period/book and legal-entity references; functional/presentation currencies and currentness. |

**Value objects/enums proposed:** `AccountingBasisCode`, `FiscalCalendarDefinition`, `CurrencyCode`, `AccountCode`, `AccountType`, `NormalBalance`, `PeriodState`, `ConfigurationLifecycle`, `BookSelection` (explicit book or approved no-separate-book decision).

**Invariants:** chart parents belong to the exact chart revision; no cycles, duplicate normalized codes, posting parent or orphan. Preserve leading-zero codes and case-normalization policy. A non-posting account cannot receive GL or AJE lines. An archived account remains readable through historical charts. A dimension value belongs to the selected schema; descriptions do not substitute for codes.

Fiscal start dates must be real dates, including leap-year rules. Distinguish reporting periods (annual/interim) from mutually exclusive ordinary posting periods: an annual report may cover monthly periods, but two active ordinary periods in the same book/calendar cannot ambiguously accept the same posting date. Any adjustment period uses an explicit classification and inclusion policy. No arbitrary “all fiscal years are 365 days” assumption.

Functional currency is a documented professional determination, not the user's display locale. A changed functional currency needs a dated approved transition, not retroactive rewriting of old periods. Default QAR may prefill a form but must be confirmed and saved; an unknown historical currency is not automatically QAR. [S4](../reference/auditsphere-r2r-reference-standards-and-source-register.md#source-s4)

**Lifecycle:** configuration `Draft → Validated → independently Published`; edits to published content create successor revisions. Context `Draft → Active`; period `Open → Closing → Closed`, with `Closing → Open` by reasoned cancellation. A closed period can only enter an authorized amendment revision; previously sealed output remains immutable. Model new logical states through a reviewed compatibility mapping to current strings.

**Domain events proposed:** `AccountingProfileRevised`, `ChartPublished`, `DimensionSchemaPublished`, `ReportingContextActivated`, `PeriodClosingStarted`, `PeriodClosed`, `PeriodAmendmentOpened`. Events report facts; they do not automatically approve new downstream versions.

<a id="persistence"></a>
### 2. Persistence & Migrations (`.Infrastructure`)

Extract/extend `ClientAccountingProfileConfiguration`, `ClientReportingPeriodConfiguration`, `ClientReportingBookConfiguration`, `ClientChartVersionConfiguration`, `ClientAccountConfiguration`, `ReportingTaxonomyVersionConfiguration`. Add equivalent configuration classes only when absent for context/dimension/close records.

Required constraints:

- Unique client profile head per firm/client; immutable profile revisions unique by logical profile/version.
- Period code unique within firm/client/basis/calendar; book code unique within period; prior-period FK same client/basis or an explicit approved basis bridge.
- Chart code uniqueness `(ChartVersionId, NormalizedAccountCode)`; stable account identity unique within chart; composite parent FK includes chart identity.
- Context composite FKs prove profile/chart/period/book belong to the same client and correct engagement/entity. Nullable legacy book links remain explicitly unbound until resolved; do not fabricate a default book during migration.
- Index contexts by `(FirmId, ClientId, EngagementId, PeriodId, Currentness)` and close records by period/revision; published configuration cannot be updated/deleted.
- Schema/value uniqueness by owner revision; dimension history uses Restrict deletion.

Use existing locks for cross-row cycle/overlap and publication checks. A recursive domain check alone is insufficient under two concurrent parent edits; serialize the chart-revision update, then validate before commit. Add a database-enforced safeguard or equivalent publication-time locked validation with negative integration tests.

Proposed additive migration suffixes: `M20_ReportingContextPins`, `M20_DimensionRevisionConstraints`, `M20_PeriodCloseEvidence`. First run backfill analysis. Only a single exact historical match may be linked; ambiguous profile/period/chart/book mappings stay quarantined using the existing mechanism. No downgrade may erase published versions or period-amendment evidence.

<a id="cqrs"></a>
### 3. Application & CQRS Contracts (`.Application`)

**Input DTOs:**

- `ProfileDraftDto`: ClientId, Jurisdiction, FunctionalCurrency, FiscalYearStartMonth/Day, SourceSystem/Identifier, FrameworkPolicyId, optional predecessor profile Ref.
- `PeriodDraftDto`: ClientId, Code, StartDate, EndDate, Basis, Currency, PeriodKind, CalendarId, optional PriorPeriodId.
- `BookDraftDto`: ClientId, PeriodId, Code, Basis, Currency, InclusionPolicyId.
- `AccountDraftDto`: DraftLineId, StableIdentity, Code, Name, Type, NormalBalance, ParentStableIdentity?, IsPosting, IsActive.
- `ChartDraftDto`: ClientId, ChartId?, BaseChartRef?, EffectiveFrom/To?, Accounts[].
- `DimensionSchemaDraftDto`: ClientId, BaseRevisionRef?, Definitions[type,code,name,required,values[code,name,effectiveFrom,effectiveTo,active]].
- `ContextBindingDto`: ClientId, EngagementId, LegalEntityKey/verified entity identity, PeriodId, BookSelection, ProfileRef, ChartRef, DimensionSchemaRef, TaxonomyRef, PolicyRef, FunctionalCurrency, PresentationCurrency.

**Requests:** all have the MediatR interface/result convention in §4.4.

| Command / query and input | TResponse | Key validator + handler check |
|---|---|---|
| `CreateAccountingProfileCommand(ProfileDraftDto, Meta)` | `MutationReceiptDto` | Valid actual fiscal date, currency and client; reject duplicate profile; authorized creator. |
| `ReviseAccountingProfileCommand(ProfileId, ProfileDraftDto, Meta)` | `MutationReceiptDto` | Exact predecessor/revision; changed accounting basis requires reason and explicit impact preview. |
| `DefineReportingPeriodCommand(PeriodDraftDto, Meta)` | `MutationReceiptDto` | Inclusive dates, same-owner prior period, approved calendar/period kind, no ambiguous overlap. |
| `CreateReportingBookCommand(BookDraftDto, Meta)` | `MutationReceiptDto` | Period open, same basis/currency/client; unique code. |
| `SaveChartDraftCommand(ChartDraftDto, Meta)` | `MutationReceiptDto` | Full hierarchy/account validations; stale revision cannot overwrite. |
| `PublishChartCommand(ChartId, DecisionInputDto, Meta)` | `MutationReceiptDto` | Locked complete chart validation; distinct approved publisher where policy requires; immutable publication. |
| `PublishDimensionSchemaCommand(DimensionSchemaDraftDto, DecisionInputDto, Meta)` | `MutationReceiptDto` | Unique codes/effective values; used definitions preserved; impact acknowledged. |
| `BindReportingContextCommand(ContextBindingDto, Meta)` | `MutationReceiptDto` | All pins approved, compatible, current and in scope; no unbound/ambiguous legacy context. |
| `StartPeriodCloseCommand(Context, Meta)` | `MutationReceiptDto` | Freeze new source acceptance while evaluating latest close basis; no outstanding modifying operation. |
| `CancelPeriodCloseCommand(Context, Meta)` | `MutationReceiptDto` | Closing only, mandatory reason; changes close attempt rather than discarding evidence. |
| `CloseReportingPeriodCommand(Context, ExpectedCloseManifest: Ref, DecisionInputDto, Meta)` | `MutationReceiptDto` | Current approved package, resolved required reconciliations/differences and eligible policy; recompute manifest atomically. |
| `OpenPeriodAmendmentCommand(PeriodId, OriginalPackage: Ref, DecisionInputDto, Meta)` | `MutationReceiptDto` | Closed period only; new revision, reason, independent authority; original output retained. |
| `GetAccountingWorkspaceQuery(ClientId, PageRequest)` | `AccountingWorkspaceDto` | Authorized profile summary, available periods/books/charts and capabilities; no sibling data. |
| `GetChartRevisionQuery(ChartId, PageRequest)` | `PageDto<AccountViewDto>` | Exact chart, hierarchy counts and account state; not all client charts by default. |
| `GetReportingContextQuery(Context)` | `ReportingContextDto` | Full exact bindings/currentness/capabilities, not mutable EF entities. |
| `GetPeriodCloseReadinessQuery(Context)` | `ReadinessDto` | Named blockers, owner, exact target and safe link; quantities derived from same manifest. |

`AccountingWorkspaceDto` contains scoped profile summary, paged period/book/chart headers and allowed actions. `AccountViewDto` projects AccountDraftDto plus persisted ID/revision and applicability. `ReportingContextDto` projects ContextBindingDto plus context ID, state/generations. `ReadinessDto` contains manifest, `CanAdvance`, and blockers `(code,ownerModule,targetId,reason)`.

Before writing handlers, map each request to current `ClientAccountingService` methods and extend missing behavior. `CreateProfileAsync`, `CreatePeriodAsync`, `RollForwardPeriodAsync` already exist; their presence is not permission to omit the new locked invariants. [R7](../reference/auditsphere-r2r-reference-standards-and-source-register.md#source-r7)

<a id="lineage"></a>
### 4. Inter-Module Lineage & Boundaries

M20 owns configuration, not imported balances or approved statements. M21 resolves an approved context and stores exact context pins on a new source; M22–M25 reuse those pins. A changed published chart creates a new context revision rather than moving old datasets to a different chart.

Expose `GetContextForOperation`/`GetApprovedChart` read contracts through queries. No module silently creates clients, fiscal years or missing accounts. M25 returns a package-ready manifest for close; M20 records the close decision, not a release. An accounting-only close need not invent EQR or an audit release, but must satisfy its approved service profile.

Publish invalidation before downstream use can commit. A changed account **name only** may affect output artifacts but not numeric balances; a posting/classification/dimension change affects source/mapping applicability. Initially use the conservative generation fence; narrow this distinction only after tested policy rules exist.

<a id="blazor"></a>
### 5. Blazor UI Architecture (`.Web`)

Extend the existing AccountingWorkspace/AccountingPeriod routes. Proposed composition:

```text
AccountingWorkspacePage
  ClientAccountingProfilePanel
  ReportingContextSelector (period/book/basis/currency)
  ChartRevisionEditor
    AccountHierarchyGrid → AccountEditDialog
    AliasEditor / ChartComparisonPanel
  DimensionSchemaEditor
  ContextImpactPreview
  PeriodClosePanel → CloseReviewDialog / AmendmentDialog
```

`AccountingWorkspaceState` retains context IDs, filters and acknowledged draft revisions. Each editor has its own EditContext so selecting a period cannot submit a dirty chart accidentally. Context changes prompt save/discard/cancel; no automatic context substitution. `EventCallback<ContextSelected>` reloads every dependent panel using the new selection token.

Show published charts read-only with “Create revision”, show unknown legacy setup explicitly, and display blocked-close links. Only authorize permitted actions; administrator access to settings does not automatically confer management acceptance. Failed close leaves the previous period state and visible blockers; long validation shows a real operation status.

<a id="verification"></a>
### 6. Edge Cases, Security & Verification

Proposed test classes and required cases:

| Layer | Class | Concrete tests |
|---|---|---|
| xUnit domain | `ReportingContextInvariantTests` | `RejectsCrossClientBook`; `PreservesLeadingZeroAccountCodes`; `RejectsPostingParentAndCycles`; `FiscalCalendarHandlesLeapAnd53WeekYears`; `UnknownCurrencyIsNotDefaultedDuringMigration`. |
| xUnit PostgreSQL | `ReportingContextPersistenceTests` | `ConcurrentChartPublishHasOneWinner`; `PriorChartStillResolvesIssuedPackage`; `AmbiguousLegacyLinkIsQuarantined`; `CloseRacingSourceAcceptanceCannotBothCommit`; `AmendmentPreservesClosedPackage`. |
| bUnit | `AccountingContextEditorTests` | `FieldErrorsMapToCorrectAccountRow`; `ContextSwitchPromptsForDirtyDraft`; `LatePriorContextResultIsDiscarded`; `ClosedPeriodRendersReadOnly`. |
| Playwright | `R2R20AccountingJourneys` | Create client context/chart/book; publish under distinct user; import-ready handoff; deny sibling context; change chart and observe stale downstream output; close/amend/reload under actual persisted state. |

**Exit gate:** approved context can be read by all downstream modules using the same identity and revision; none can accept an orphan, closed, cross-client or unresolved context. Existing accounting setup tests remain and pass.
