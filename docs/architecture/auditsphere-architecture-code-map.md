# AuditSphereOps — Current Code Map

**Status: CURRENT.** Capability → files. Consult this before changing a business capability.

Architecture authority: [`auditsphere-architecture-current-architecture.md`](auditsphere-architecture-current-architecture.md) and

[`AGENTS.md`](../../AGENTS.md). Large services and the DbContext are partial classes split by

capability; public APIs are stable across parts. Verified project state lives only in

`docs/execution/status.json`.

> **Blazor retirement note (2026-10-17):** The legacy Blazor host (`AuditSphereOps.Web`, its `Web/Components/Pages` screens and MudBlazor) was physically retired and merged to `master` as `e7ebe27c`; no `.razor` file remains in the current tree. Every `.razor` or `Web/` entry below is historical lineage preserved only at the pinned pre-removal commit `c3905e07`, not a current UI target or evidence of parity. The canonical presentation is Angular under `src/AuditSphereOps.Ui`, served by `src/AuditSphereOps.Api`.

## Current presentation ownership

- **HTTP host and contracts:** `src/AuditSphereOps.Api/` owns authenticated `/api/ui/*` endpoints, session and consent boundaries, protected file transports, health checks, and serving the Angular build. Route ownership is explicit in `src/AuditSphereOps.Api/Ui/AngularRouteOwnership.cs` and the SPA route catalogue.
- **Presentation:** `src/AuditSphereOps.Ui/src/app/` owns the Angular staff and client-portal routes and workspaces. Angular calls the same-origin API; it does not own authorization or business-state authority.
- **Historical screen references:** old Blazor screen names below are retained only to trace the pre-removal implementation at `c3905e07`. They are not current files, route owners, rollback hosts, or proof that a current Angular workspace has matching behavior. Where current API/Angular files are listed, those paths identify current implementation locations without asserting feature parity.

## Client-visible activity and consolidation read snapshots

- `Application/Documents/ClientPortalFinanceQuery.cs`, `Api/Ui/UiEndpoints.Portal.cs`: client finance applies exact client/engagement grants before its agreement page bound. Continuations use the retained creation time and agreement ID; every page owns a read snapshot and rechecks current identity/grants after disposal. A cursor never grants access. Angular `features/portal/finance.ts` owns agreement-page navigation, separately from activity paging and invoice/receipt lifecycle facts. `finance-receipts.ts` owns lazy, independent per-agreement receipt continuation; complete cash aggregates do not depend on the displayed page. Receipt row/cursor identity includes milestone kind, and download links retain the existing exact-or-legacy lineage checks.
- `Application/Documents/ClientPortalActivityQuery.cs`, `Api/Ui/UiEndpoints.Portal.cs`: read-only `/api/ui/portal/activity` merges retained client-visible facts using bounded source queries, a scope-bound cutoff cursor and current authorization/context rechecks. Receipt allocation links must match the authorized invoice's billing account.
- Angular `features/portal/activity.ts`, hosted by `home.ts`: independent activity paging, explicit recovery, source-panel navigation and session clearing. Existing document, request and finance panels remain the authoritative source views.
- `Application/Accounting/Consolidation/ConsolidationScopeWorkspaceQuery.cs`: owns the multi-panel read snapshot, refuses caller-owned transactions, rechecks current group/context and source approval readiness after disposal, and only demotes existing capabilities.
- Focused evidence is in `ClientPortalActivityQueryTests`, `ConsolidationQueryTests`, portal activity/home specs and `CompletionDeliverablesJourneyTests`; observed checks and remaining acceptance gates belong in `docs/execution/status.json`.

## Time revision and correction evidence

- `Application/Practice/PracticeTimeService.TimeChanges.cs`: locked, authorized draft/returned edits and approved correction into a new draft; actor-owned exact-request replay. Existing submit/review and billing guards remain authoritative.
- `Application/Practice/PracticeTimeChangeQuery.cs`: bounded retained history and actor-owned receipt recovery, snapshot reads with current-scope rechecks; public values exclude private cost/rate data.
- `Domain/Practice/TimeEntryChangeReceipt.cs` and `Infrastructure/Persistence/AuditSphereDbContext.TimeEntryChanges.cs`: immutable before/after snapshots, request/revision identity and same-firm constraints. Migration `TimeEntryChangeReceipts` adds publication/append-only guards without historical actor backfill.
- `Api/Ui/UiEndpoints.TimeChanges.cs`; Angular `features/practice/time-entry-change.ts`, integrated by `time.ts`: exact review, explicit unknown-outcome lookup/retry, retained history and keyboard focus.
- Focused tests live in existing `PracticeTimeTests`, `BillingTests`, `PracticeTimeStartMinuteApiTests`, `time-tracker-ui.spec.ts` and `AngularPracticeTimeScopeParityJourneyTests`. Observed verification and separate release gates remain in `docs/execution/status.json`.

## Reviewed accounting period maintenance

- `Application/Accounting/PeriodMaintenanceWorkspace.cs` and its contracts: scoped context/state, exact preview and actor-owned receipt lookup.
- `Application/Accounting/ClientAccounting/ClientAccountingService.PeriodMaintenanceReceipts.cs`: transaction-owned publication, source fencing, replay and append-only outcome snapshots; existing period/restatement service entry points remain available.
- `Domain/Accounting/PeriodMaintenanceReceipt.cs`, `Infrastructure/Persistence/AuditSphereDbContext.PeriodMaintenanceReceipts.cs`: immutable scoped request/effect evidence.
- `Api/Ui/UiEndpoints.PeriodMaintenance.cs`; Angular `features/accounting/rollforward.ts`, `restatements.ts`, shared maintenance contracts and reviewed-state orchestration.
- `Application/Accounting/AccountingPeriodRecordQuery.cs`, `AccountingPeriodEvidenceQuery.cs` and Angular `features/accounting/period.ts` / `period-evidence.ts`: exact scoped reporting-period handoffs and nine independently paginated retained-evidence categories. Client-wide authorization, repeatable-read evidence pages and final authorization fences; Int64 revisions stay strings. PBC matches exact client/date range; receipt/proof history does not imply current validity, period readiness or monthly handover acceptance.

## Native client operational bookkeeping

- Client customers/suppliers: `Application/Accounting/ClientBookkeepingCounterpartyWorkspace.cs`, `Domain/Accounting/ClientBookkeepingCounterparty.cs`, `Api/Ui/UiEndpoints.ClientCounterparties.cs` and Angular `features/accounting/counterparties.ts`. Client-scoped immutable initial profiles, combined role, optional tax/defaults, duplicate fences and retained reads after service withdrawal. `ClientBookkeepingCounterpartyWorkspace.Amendments.cs` and Angular `counterparty-history.ts` add exact-revision contact-detail proposals, independent append-only decisions, effective-profile projection and scoped paged history. Database guards protect proposal/decision history and independent, sequential approvals. Legal identity changes, merges/reclassification and frozen invoice snapshots remain separate work; CA-07 remains partial.

- Domain: `Domain/Accounting/ClientAccounting.cs`; client-owned journals, lines and review decisions.
- Application: `Application/Accounting/ClientOperationalLedgerWorkspace.cs` and `ClientOperationalGeneralLedgerWorkspace.cs`; accepted bookkeeping service gate, balanced journal preparation, independent posting and posted movement queries.
- Native activity Trial Balance and GL paging: `Application/Accounting/ClientOperationalGeneralLedgerWorkspace.cs`; the same repeatable-read snapshot covers report rows and totals, and a database-assigned, commit-serialized posting sequence fences pages across requests, including later backdated postings. Stable ordering uses posting date, journal number, journal ID and line number. Earlier posted dates and an approved native opening-balance manifest feed separate opening and period-movement columns. `Application/Accounting/ClientOperationalOpeningBalanceWorkspace.cs` and `Api/Ui/UiEndpoints.ClientOperationalLedger.cs` persist the chart-bound, balanced source manifest with independent approval; optional positive invoice-level AR/AP opening details are immutable, linked to party and control account, and must reconcile exactly to mapped opening control balances. `Application/Accounting/ClientOpenItemAllocationWorkflow.cs` includes approved opening detail in period aging, settlement allocation, and control reconciliation. Allocation targets are limited to the latest approved cutover schedule; aggregate-only openings disclose incomplete detail, and cross-period opening carry-forward remains unavailable. Angular `features/accounting/operational-opening-balances.ts` supplies the maker/reviewer journey. Adjusted/source snapshots and durable exports remain separate work.
- Saved journal discovery: `Application/Accounting/ClientOperationalLedgerWorkspace.List.cs`; client-scoped period/status paging and reopening with final role checks. Native GL/TB history reads disclose inactive service state without bypassing command mandate gates. Native Angular browsing and retained-history tests live in the existing operational journal suites.
- Submitted content: `Application/Accounting/ClientOperationalLedgerWorkspace.Snapshots.cs`; scoped historical content with exact string amounts, captured atomically by submission triggers in the snapshot migration. Existing uncaptured reviews are not reconstructed.
- Review return: `Application/Accounting/ClientOperationalLedgerWorkspace.Return.cs`; independent revision-bound return reason, append-only review history and protected returned content.
- Posting receipts: `Application/Accounting/ClientOperationalLedgerWorkspace.Receipts.cs`; client-scoped exact-intent command identities, atomic immutable outcomes, original-actor recovery and same-key replay. Angular preserves an unknown posting request until scoped recovery or retry of its original intent.
- Full manual reversals: `Application/Accounting/ClientOperationalLedgerWorkspace.Reversal.cs`; original-lock creation/replay, explicit open correction period/date, immutable reason/evidence-reference lineage and exact swapped lines enforced by deferred database guards. Independent preview/review/posting uses the normal journal workflow.
- Draft editing and rework: `Application/Accounting/ClientOperationalLedgerWorkspace.Rework.cs`; original-preparer edits to ordinary unsubmitted drafts advance the revision and invalidate earlier previews under journal/period locks. Returned rework additionally requires exact preserved submitted content and an independent return decision. Earlier submissions remain immutable; full-reversal drafts are excluded from general draft editing.
- Preview: `Application/Accounting/ClientOperationalLedgerWorkspace.Preview.cs`; read-only exact totals and digest covering current journal, period, approved chart and accepted mandate, rechecked inside submission/posting transactions.
- Client posting account roles: `Application/Accounting/ClientAccountRoleWorkspace.cs`, `Domain/Accounting/ClientAccountRoleConfiguration.cs`, `Infrastructure/Persistence/AuditSphereDbContext.ClientAccountRoles.cs`, `Api/Ui/UiEndpoints.ClientAccountRoles.cs` and Angular `features/accounting/account-roles.ts`. Scoped immutable proposals and independent decisions activate explicit effective-dated compatible roles. Overlap/cutover fences and database serialization prevent generic native journals from creating unexplained AR/AP activity. Tax/ancillary roles are optional. Invoice-linked postings require the independent sales-invoice workflow; purchases, credits, settlements and role replacement/retirement remain separate work. CA-04 remains partial.
- Client sales preparation and posting: `Application/Accounting/ClientSalesInvoiceDraftWorkspace.cs` and `ClientSalesInvoiceWorkflow*.cs`, `Domain/Accounting/ClientSalesInvoiceDraft.cs` and `ClientSalesInvoiceWorkflow.cs`, `Infrastructure/Persistence/AuditSphereDbContext.ClientSalesInvoices.cs`, migration `20261007093501_ClientSalesInvoiceWorkflow`, API `UiEndpoints.ClientSalesInvoiceDrafts.cs` and `UiEndpoints.ClientSalesInvoices.cs`, and Angular `features/accounting/sales-invoice-drafts.ts` plus `sales-invoice-workflow.ts`. Immutable same-currency untaxed drafts capture exact client seller/customer details and line inputs; submission freezes the reviewed client-ledger manifest; only an independent reviewer can post the exact AR/revenue journal and original receivable open item. Lifecycle state, recovery receipts, tax-free preparation and posted GL/TB movements are covered by PostgreSQL and API-hosted Angular tests. Tax and ancillary modules remain optional. Document issuance/delivery, verified source-byte linkage, official numbering and full requirements CA-14 and CA-15 remain open.
- Calculators: `Application/Accounting/ClientNativeAmountRules.cs` preserves the shared native numeric(19,6) storage boundary. `ClientOperationalJournalCalculator.cs` retains exact journal balance rules. `ClientInvoiceCalculator.cs` provides pure invoice/credit quantity, discount, additive rounded-net tax and explicit currency rounding calculations, including separately disclosed bounded residuals and unsupported-FX refusal. Caller-declared policy inputs are calculation data, not approval or issuance authority; approved policy configuration and posting integration remain separate work.
- API: `Api/Ui/UiEndpoints.ClientOperationalLedger.cs`, `UiEndpoints.ClientSalesInvoiceDrafts.cs` and `UiEndpoints.ClientSalesInvoices.cs`; authenticated, scope-checked commands and exact decimal string responses. The canonical OpenAPI contract includes the additive invoice workflow routes.
- Angular: `Ui/src/app/features/accounting/operational-journals.ts`, composed by the accounting workspace only in native bookkeeping mode.
- Persistence: `Infrastructure/Persistence/AuditSphereDbContext.ClientOperationalLedger.cs`; dedicated operational journal tables and integrity migration. These are separate from firm ledger entries and reporting adjustments.
- Tests: `ClientAccountingTests.OperationalLedger.cs`, `ClientAccountingTests.SalesInvoiceWorkflow.cs`, `AngularClientOperationalJournalJourneyTests.cs`, `AngularClientSalesInvoiceDraftJourneyTests.cs`, `AngularClientSalesInvoiceWorkflowJourneyTests.cs`, and Angular sales invoice workflow specs.

Posted movement lines exclude opening balances and imported GL; the official native Trial Balance combines approved opening snapshots with current-period posted movements in separate columns. The posted sales-invoice journal contributes to the same native GL/TB movement query. Partial client open-item settlement links approved posted sales/purchase credits and exact imported, balanced customer-receipt/supplier-payment evidence through independently reviewed, immutable allocations; it does not create a duplicate cash posting. Manually recorded customer receipts and supplier payments create a balanced two-line client journal only for already completed external transfers, preserve immutable external reference/evidence, require command idempotency and independent journal approval, and never initiate or verify payment. Approved opening invoice details can be allocation targets after a reversible migration extends the target constraint and deferred database validator; the validator requires the latest approved cutover, matching client party/currency and integrity-checked role linkage. AR/AP reconciliation includes remaining opening items and approved allocations; aggregate-only openings disclose incomplete aging. Allocation decisions have no separate accounting-effective date, so historical cutoffs use current approved allocation state. Prior-period opening carry-forward, audit snapshots, statement issuance/delivery and tax workings remain open. This work does not establish full bookkeeping completion.

## Accounting setup — profiles, periods, books, charts, taxonomy, capabilities

- Domain: `Domain/Accounting/ClientAccounting.cs`

- Application: `Application/Accounting/ClientAccounting/ClientAccountingService.{Profiles,Periods,Books,OpeningBalances,Restatements,Charts,Taxonomy,Capabilities,Authorization}.cs`

- Persistence: `Infrastructure/Persistence/AuditSphereDbContext.ClientAccounting.cs`, `.ClientAccountingSchedules.cs`

- Current API/Angular: `src/AuditSphereOps.Api/Ui/UiEndpoints.Accounting.cs`, `UiEndpoints.PeriodMaintenance.cs`; `src/AuditSphereOps.Ui/src/app/features/accounting/workspace.ts`, `period.ts`, `rollforward.ts`, `restatements.ts`.
- Historical Blazor screens (pinned pre-removal source only): `Web/Components/Pages/AccountingWorkspace.razor`, `AccountingPeriod.razor`, `PeriodRollforward.razor`, `PeriodRestatements.razor`.

- Tests: `tests/.../ClientAccountingTests/ClientAccountingTests.{Profiles,PeriodClose,ChartAndTaxonomy}.cs`

Critical invariants: firm-wide configuration rejects client-scoped grants; period close needs

current reviews for matching packages; reopen/restatement create immutable revision lineage.

Client bookkeeping source mode is explicit on `ClientAccountingProfile`; existing and new
profiles default to `EXTERNAL_SOURCE`. `NATIVE_BOOKKEEPING` requires reviewer access and an
unconditional client-level `BOOKKEEPING` acceptance decision. Inline enablement is blocked
when reporting periods already exist; a reviewed cutover workflow is still required for those
clients. This setup does not itself provide native journal posting.

## Trial balance intake and general ledger

- Domain: `Domain/Accounting/` (datasets, import batches, GL lines/transactions)

- Application: `Application/Accounting/TrialBalance{Calculator,ImportService,CsvImporter,XlsxImporter,DatasetQuery,ValidationHandler}.cs`, `Application/Accounting/ClientAccounting/ClientAccountingService.GeneralLedger.cs`, `GeneralLedgerCompletenessHandler.cs`, `GeneralLedgerQuery.cs`, `Application/Accounting/Analysis/AccountingAnalysisService.GeneralLedger.cs`, `Application/Accounting/Analysis/GeneralLedgerCompletenessWorkspace*.cs`

- Persistence: `AuditSphereDbContext.Accounting.cs`

- Current API/Angular: `src/AuditSphereOps.Api/Ui/UiEndpoints.GeneralLedger.cs`; `src/AuditSphereOps.Ui/src/app/features/accounting/gl-completeness.ts` and `src/AuditSphereOps.Ui/src/app/features/engagements/general-ledger.ts`.
- Historical Blazor screen (pinned pre-removal source only): `AccountingRecords.razor`.

- Tests: `ClientAccountingTests.GeneralLedger.cs`, `TrialBalanceCalculatorTests.cs`, `TrialBalanceWorkerTests.cs`, `TrialBalanceXlsxImporterTests.cs`, `SourceAcceptanceAndComparativesTests.cs`, `GeneralLedgerCompletenessApiTests.cs`, `AngularGeneralLedgerCompletenessJourneyTests.cs`

Critical invariants: imports are idempotent and sealed only when complete; GL completeness is

account-exact; source acceptance binds sealed sources and bumps generation.

## Journals, adjustments and re-measurement workpapers

- Application: `Application/Accounting/AdjustmentJournalService.cs`, `AdjustmentPlanService.cs`, `AdjustmentEligibilityQuery.cs`, `AdjustmentPlanWorkspace.cs`, `AdjustmentPlanWorkspace.Commands.cs`, `CurrencyRemeasurementService.cs`

- Persistence: `AuditSphereDbContext.AdjustmentBridge.cs`, `AuditSphereDbContext.AdjustmentPlanActions.cs`; retained native command evidence: `Domain/Accounting/AdjustmentPlanAction.cs`

- Current Angular workspaces: `src/AuditSphereOps.Ui/src/app/features/accounting/journal.ts`, `journal-management.ts`, and `remeasurement.ts`.
- Historical Blazor screens (pinned pre-removal source only): `Journals.razor`, `CurrencyRemeasurement.razor`.

- Tests: `AccountingRecordsApprovalTests.cs` (journal submit/return/resubmit, duplicate reversal), `ClientAccountingTests.Currency.cs`

## Reconciliations, valuations, specialists, journal risk and accounting evidence

- Application: `Application/Accounting/Analysis/AccountingAnalysisService.{Reconciliation,Valuations,AnalyticalReview,JournalRisk,Evidence,Authorization}.cs`; native scoped inspection: `Application/Accounting/ReconciliationWorkspaceQuery.cs`, `Application/Accounting/AccountingAnalysisReviewQuery{,.Retained}.cs`; native reviewed commands: `Application/Accounting/AccountingEvidenceWorkspace{,.Procedures}.cs`, `Application/Accounting/ValuationPreparationWorkspace.cs`; retained receipts: `Domain/Accounting/AccountingEvidenceAction.cs`, `Domain/Accounting/ValuationPreparation.cs` and their matching Infrastructure persistence configurations

- Current Angular workspaces: native queue/detail in `src/AuditSphereOps.Ui/src/app/features/accounting/evidence.ts`, `reconciliation.ts`, and `analysis-review.ts`; reviewed action/preparation forms and receipt recovery in `src/AuditSphereOps.Ui/src/app/features/accounting/evidence-actions{.ts,.html}`, `evidence-action-contracts.ts`, `valuation-preparation{.ts,.html}`, and `valuation-preparation-contracts.ts`.
- Historical Blazor screens (pinned pre-removal source only): `AccountingEvidence.razor`, `Finding.razor`.

- Tests: `ClientAccountingTests.{Reconciliations,Analysis}.cs`, `SourceAcceptanceAndComparativesTests.cs`, `ValuationPreparationApiTests.cs`, `AngularValuationPreparationJourneyTests.cs`

Critical invariants: reconciliation approval stales when the source digest changes; evidence is

blocked when the client generation changes; journal risk is deterministic, never autonomous. Reconciliation writers lock the target period before the reconciliation, refresh tracked targets and reauthorize after waits. Approval validates exact proof while locked; matching proof append preserves an approved schedule and review. Closed targets refuse writes, and caller-owned transactions remain caller-owned.

## Financial statements, mapping and packages

- Domain: `Domain/Accounting/` (mapping versions/allocations, financial packages, artifacts)

- Application: `Application/Accounting/FinancialStatements/FinancialStatementService.{Mapping,Package,Render,Authorization,CashFlowSources,CashFlowSourceReview,ContentReview,PresentationPins,Composition}.cs`, `FinancialPackageReviewService.cs` (with `.DecisionChain.cs` for latest-effective formal decisions and `.ClientComposition.cs` for client-management approval of the exact composition), `FinancialPackageBuildHandler.cs`, `FinancialPackageRenderHandler.cs`, `FinancialPackageOfficeRenderer.cs`, `PackageSealService.cs`, `PackageManifestQuery.cs`, `StatementLayoutService.cs`, `FinancialStatementCalculator.cs`

- Persistence: `AuditSphereDbContext.FinancialStatements.cs`

- Current API/Angular: `src/AuditSphereOps.Api/Ui/UiEndpoints.Packages.cs`, `UiEndpoints.Statements.cs`, `UiEndpoints.MappingDrafts.cs`; Angular mapping/review/package workspaces in `src/AuditSphereOps.Ui/src/app/features/accounting/mapping.ts`, `mapping-approval.ts`, `package.ts`, `presentation.ts`, and `src/AuditSphereOps.Ui/src/app/features/engagements/financial-package-build.ts`.
- Historical Blazor screens (pinned pre-removal source only): `Mapping.razor`, `FinancialPackage.razor`, `FinancialPackageReviews.razor`, `ClientFinancialPackage.razor`, `Release.razor`.

- Tests: `ClientAccountingTests.FinancialPackages.cs`

Critical invariants: mapping allocations are append-only; review decisions are stage-bound and

immutable; artifacts are exact-byte sealed; release candidates bind the current reviewed package.
Equity component transfers are paired, zero-sum package inputs, recorded in `FinancialPackageEquityTransfer`, included in package/presentation hashes and rendered as linked outflow/inflow rows.

Cash-flow FX assessment is persisted in `FinancialPackageFxAssessment`, alongside—not inferred from—FX effect rows. `FinancialStatementService.CashFlowSourceReview.cs` binds the assessment, evidence, assessor and exact effects into the independent review digest; `FinancialStatementPresentation.cs` requires that current review before FX completeness passes. The Angular build form leaves assessment unset until evidenced and shows the missing state as a blocker. The migration is `FinancialPackageFxAssessment`.

A signing candidate is created only when every presentation check passes, with note, disclosure and equity
content approved by an independent content review of the exact draft snapshot. Formal decisions carry an
original request identity and the latest decision the decider saw; only the latest-effective approval at each
stage, recorded after its predecessor's, counts for progression and release.

Lock order for package review, source review, content review, pin, promotion and composition: firm safety row
(when taken), client safety row, financial package row, then dependent rows and the actor's role grants (share).
Authority is rechecked after the locks are held. A command never takes the package row before the client row.

## Currency translation and foreign operations

- Domain: `Domain/Accounting/` (exchange rate sets, translation policies/results)

- Application: `Application/Accounting/CurrencyTranslationService.cs`, `CurrencyOperationCalculators.cs` (`LineTranslationCalculator`, `CurrencyTranslationCalculator`), `TranslationInputResolution.cs`

- Persistence: `AuditSphereDbContext.Consolidation.cs` (rate sets, policies, translation results)

- Historical Blazor screens (pinned pre-removal source only): `Consolidation.razor` (TranslationBridge area), `CurrencyRemeasurement.razor`.

- Tests: `tests/.../LineTranslationTests.cs`, `ClientAccountingTests/ClientAccountingTests.Currency.cs`

Critical invariants: a missing/zero/negative required rate purpose fails closed and produces no

approvable result; same currency is identity rate 1 with no rate observation; per-line rate

purposes follow the calculation method (`COMPONENT_TRANSLATION_V2`), never the reserve amount;

classification comes from declared sections, approved mappings or documented code rules — an

unclassified line is an explicit unmapped issue, never assumed ASSETS.

## Consolidation — perimeter, components, matching, journals, runs, reports

- `ConsolidationService.Components.cs` shares exact source validation between approval readiness and per-component currentness. The scope workspace carries `CurrentnessState` and `CurrentnessBlockerCode` and demotes changed evidence after its read snapshot; the temporary evidence hash stays internal. Angular `scope-contracts.ts` strictly decodes these fields and the components table distinguishes source currentness from approval. No automatic newest-upload policy or source-book mutation is implied.

- `ConsolidationService.GetComponentApprovalReadinessAsync` shares nonmutating command guards with the bounded scope workspace projection. Component rows expose `CanApprove` and a safe `ApprovalBlocker`, including selected off-page rows. Angular strictly decodes both fields, explains blocked approval and refuses local dispatch; the server command remains authoritative. Focused evidence is tracked in `status.json` under `operationalReadiness20261019`.

- Domain: `Domain/Accounting/ClientAccounting.cs` (`ClientGroup`, `ConsolidationScopeVersion`, `ConsolidationComponent`, `ConsolidationRun`, `ExchangeRateSetVersion`, `TranslationPolicyVersion`, `TranslationResult`)

- Application: `Application/Accounting/Consolidation/ConsolidationService.{Groups,Ownership,Scopes,Components,ExternalPacks,Intercompany,Journals,Advanced,Runs,Reports,Authorization}.cs`, `ConsolidationCalculator.cs`, `AdvancedConsolidationCalculator.cs`, `AdvancedConsolidationExecutionCalculator.cs`, `ConsolidationQuery.cs`

- Persistence: `AuditSphereDbContext.Consolidation.cs`

- Historical Blazor screens (pinned pre-removal source only): `Consolidation.razor`, `AdvancedConsolidationWorkflow.razor`.

- Current Angular UI: `src/AuditSphereOps.Ui/src/app/features/consolidation/scope-workspace.ts` coordinates the single aggregate workspace, scope-owned commands and child workspaces; `scope-intercompany-state.ts` owns member selection, exact response binding, match review and history; `scope-journal-state.ts` owns elimination journal editing/review intent; `scope-amounts.ts` preserves exact six-decimal bigint parsing/formatting; `ownership.ts` displays draft-scope ownership relationships and approved effective membership windows, refusing relationship intervals that exceed one approved window before dispatch. The API repeats the membership and current-revision checks. The external `scope-workspace.html` retains the workspace UI. `overview.ts` and `advanced.ts` own their separate pages.

- Current API/Application: `src/AuditSphereOps.Api/Ui/UiEndpoints.Consolidation.cs`, `ConsolidationOverviewQuery`, `AdvancedConsolidationWorkspaceQuery`, and the `ConsolidationService` partials. These file locations do not certify parity with the historical screens above.

- Tests: `ClientAccountingTests/ClientAccountingTests.{Consolidation,AdvancedConsolidation,ExternalComponents,Currency}.cs`, `ProfilesChartsAndConsolidationTests.cs`, `ConsolidationWorkspaceApiTests`, `AngularAdvancedConsolidationAuthorizationJourneyTests`, `SiblingClientIsolationJourneyTests`

Critical invariants: the group build never mutates component client books; the cumulative

translation reserve line identity is derived from the immutable translation snapshot (stable

replay/approval/readback); the group-report export/archive leg is a declared M37/M38 boundary

(`docs/task_breakdown/modules/auditsphere-r2r-module-26-consolidation-contract.md`).

## Audit planning and fieldwork

- Reviewed planning actions: `Application/Audit/AuditPlanningCommandWorkspace.cs` and `AuditPlanningCommandContracts.cs` provide scope-bound state, exact previews, atomic execution and actor-owned receipt lookup for materiality calculation/rounding/approval, risk creation/assessment/ownership/clearance, and procedure-risk linking. Existing services retain their public entry points and respect ambient transaction ownership.
- Planning receipt evidence: `Domain/Audit/AuditPlanningCommandReceipt.cs`, `AuditSphereDbContext.AuditPlanningReceipts.cs` and `AuditPlanningReviewedCommandReceipts` retain immutable original previews/results with action-specific publication guards. Actor/request barriers fence absent lookups; shared actor locks protect session epochs without blocking foreign-key checks. Procedure generation fences precede engagement locks.
- Angular reviewed planning: `features/audit/planning-command-contracts.ts`, `planning-command-state.ts` and `planning-command-review.ts`, composed by `plan.ts` and the fieldwork risk-link control. Recovery stores request identity/hash only and never restores assent. API owners are the `/audit-plan/commands` routes in `UiEndpoints.AuditPlan.cs`.

- Controlled programme publication: `Application/Audit/AuditProgramCatalog.cs` pins the source hash and canonical content digest; `AuditProgramService.cs` prepares a complete immutable draft under current firm-wide authority and publishes it only after an independent reviewer binds approval to the exact digest and reference. `AuditProgramLibraryQuery.cs`, `Api/Ui/UiEndpoints.AuditRecords.cs` and Angular `features/audit/library.ts` expose the reviewable publication flow. `AuditSphereDbContext.Audit.cs` and migration `AuditProgramPublicationEvidence` persist source/content and review provenance while preserving legacy rows. `AuditProgramCatalogSourceTests.cs`, `AuditProgramWorkflowTests.cs` and `AuditProgramCommandsApiTests.cs` cover source fidelity, maker-checker, replay, authorization and HTTP boundaries. Professional methodology acceptance remains separate.
- Controlled fieldwork definitions: `AuditProgramService.Definitions.cs` appends compare-and-swap effective-definition revisions while retaining the exact catalogue wording through `ProgramProcedureId`; `AuditProcedureResult.DefinitionRevisionId` binds submitted work to the definition actually used. Fieldwork projections expose original wording, effective wording and legacy/authored provenance. Migration `AuditProcedureDefinitionHistory` backfills unattributed legacy snapshots and blocks history/source mutation; current-evidence and review commands reject stale definition bindings.

- Domain: `Domain/Audit/`

- Application: `Application/Audit/AuditPlanningService.cs`, `Application/Audit/Fieldwork/AuditFieldworkService.{Schedules,BankReconciliations,Selections,ItemTests,Confirmations,AreaAssessments,Differences,Completion,Authorization}.cs`

- Persistence: `AuditSphereDbContext.Audit.cs`, `.Fieldwork.cs`

- Current Angular/API: `src/AuditSphereOps.Ui/src/app/features/audit/plan.ts`, `fieldwork.ts`, `fieldwork.html`, `fieldwork-contracts.ts`, `fieldwork-procedure-state.ts`, `fieldwork-sampling-state.ts`, `records.ts` and `library.ts`; `src/AuditSphereOps.Api/Ui/UiEndpoints.AuditPlan.cs`, `UiEndpoints.Fieldwork.cs` and audit record endpoints. `AuditFieldwork` retains engagement/session ownership, aggregate-difference presentation, navigation and command exclusion; extracted states own procedure/review/receipt handling and sampling parameters, previews, selected-item drafts and paging. These current paths do not assert source-action parity.
- Historical Blazor screens (pinned pre-removal source only): `AuditPlan.razor`, `AuditFieldwork.razor`, `AuditPopulation.razor` and `AuditProgramLibraryPage.razor`.
- Exact population/finding projections: `Application/Audit/AuditRecordQueries.cs`; both reauthorize after composing details. Angular record decoders bind the returned identity to the route and finding drafts clear on route/session transitions.

- Tests: `tests/.../AuditFieldwork*` and audit sections of `SourceAcceptanceAndComparativesTests.cs`

## Reviews, completion, findings and workpapers

- Application: `Application/Reviews/`, `Application/Completion/`

- Persistence: `AuditSphereDbContext.Reviews.cs`, `.Completion.cs`, `.ScopedEvidence.cs`

- Current Angular/API workspaces: `src/AuditSphereOps.Ui/src/app/features/audit/completion.ts` and `src/AuditSphereOps.Ui/src/app/features/audit/workpaper.ts`; `src/AuditSphereOps.Api/Ui/UiEndpoints.Completion.cs`, `UiEndpoints.AuditRecords.cs` and `UiEndpoints.Fieldwork.cs`.
- Historical Blazor screens (pinned pre-removal source only): `ReviewPoint.razor`, `Completion.razor`, `Workpaper.razor`.

- Tests: `tests/.../` review/completion suites, `AccountingRecordsApprovalTests.cs`

## Documents, PBC and records

- Client final-bundle history: `Application/Completion/AuditDeliverableService.Bundle.cs`, `Application/Documents/ClientPortalReviewQuery.cs` and Angular `features/portal/documents.ts`. The portal requests the remaining bounded collection allowance plus one sentinel rather than silently taking ten per engagement. Overflow fails without a partial archive. Ordering is stable within each engagement. Visible assembly and client-reported receipt times use UTC; download names include the exact bundle identity. Assembly/download does not create a receipt acknowledgement or prove provider delivery. Signed-scan client history uses the aggregate archive bound with overflow refusal; the public staff helper retains its latest-history limit. Portal-specific context projects only client legal name, recorded service and period dates. Repeatable-read evidence is followed by fresh user/session, grant-set and context fences; changed context returns a conflict without stale evidence. Signed-scan rows expose retained signatory, source version, UTC upload time and hash with an exact accessible file identity; source currentness and verification-record presence are separate states. Complete public chronology and human/live acceptance remain separate work.

- Client request detail/history: `Application/Documents/ClientPortalWorkspaceQuery.cs`, `Api/Ui/UiEndpoints.Portal.cs` and Angular `features/portal/request.ts` / `contracts.ts`. Independent read snapshots are followed by current-session, participation, request-context and capability fences. `CanManageDelegations` preserves authorized terminal-request revocation; `CanDelegate` governs new creation. Chronological conversation pages and upload-intent creation times remain distinct from provider acceptance.
- Client request action eligibility: the same query projects independent `CanUpload` and `CanReply` flags from the existing request state and current shared write window. Angular home/detail use those flags, default missing flags to false, and clear drafts on capability loss. Clarification requires a reply before replacement upload; resubmitted requests retain upload access. Legacy `CanWrite` is compatibility data only. Home reads recheck window, scope, request state and revision before publishing actions; a stale request requires refresh. Source commands retain final authority and still validate the exact file/active intent.
- Client PBC write authority: `Application/Documents/PbcService.cs` refreshes locked request rows before current participation/scope checks in replies, state changes and upload commands. Upload callers, including staff, take firm shared, client update and engagement shared guards before request/intent locks. Trusted completion takes those guards before the upload lock and enqueue, avoiding the old inverse order. Exact chunk and staged/received completion replays retain their existing semantics. Start/resume repeat the existing writability check inside the fence; a saved refusal audit is committed without upload mutations. This does not add request-owner reassignment or broaden client/provider authority.
- Retained client PBC acceptance: the same request snapshot projects the exact `PbcAcceptanceReceipt` file identity, three revisions, byte count, hash and UTC decision time. Angular `features/portal/request.ts` distinguishes this retained reviewer event from current request status and upload-intent creation. Legacy status without a receipt supplies no invented event/time; staff identities and provider internals are excluded. Receipt lineage and existing authorization remain authoritative.

- Application: `Application/Documents/PbcService.cs`, `Application/Records/RecordsArchiveService.cs`

- Persistence: `AuditSphereDbContext.Documents.cs`, `.Pbc.cs`

- Current Angular/API entry points: `src/AuditSphereOps.Ui/src/app/features/engagements/pbc.ts`, `src/AuditSphereOps.Ui/src/app/features/portal/documents.ts`, `request.ts`, `home.ts` and `src/AuditSphereOps.Ui/src/app/features/audit/records.ts`; `src/AuditSphereOps.Api/Ui/UiEndpoints.Pbc.cs` and `UiEndpoints.Portal.cs`. This mapping identifies current files only and does not assert parity.
- PBC acceptance is assigned-reviewer-only and binds one accepted request to the exact provider-verified upload revision, content hash, durable transfer result and provider receipt. `PbcInboxWorkspaceQuery.cs`, `/api/ui/pbc-requests/{id}/state` and the Angular PBC inbox expose review/accept actions and source identity; `PbcAcceptanceReceipt` is append-only, and its upload source is protected from mutation by migration `PbcAcceptanceReceipts`.
- Historical Blazor screens (pinned pre-removal source only): `PbcRequests.razor`, `ClientPbcRequest.razor`, `RecordsArchive.razor`, `ClientPortal.razor`.

- Tests: `tests/.../` document/PBC suites

Critical invariants: uploaded signed-document evidence is preserved exactly with SHA-256

identities; provider acceptance (Purview/eSignature) is never claimed.

## Practice — CRM, time, billing, firm ledger

- Recorded outbound history: `Application/Acceptance/EngagementCommunicationTimelineQuery.cs`, existing `/engagements/{id}/communications` endpoint and Angular `features/engagements/engagement-communications.ts`. Exact source-scoped counts and bounded rows share a read snapshot, followed by fresh authorization. Voucher mail follows its retained document and agreement identities. Provider-acceptance timestamps are not delivery times. The separate `.Activity.cs` feed remains management-only; neither query grants client access to internal reviews.

- Practice analytics cost: `Application/Practice/FirmOperationsServices.cs` uses the latest applicable staff-cost effective date. Conflicting values at that date leave cost, profit and margin unavailable; identical duplicate values retain the amount. Charge-out value, billing and collection remain separate measures.

- Client archive/restore presentation: `src/AuditSphereOps.Ui/src/app/features/clients/client.ts` retains the exact client/session/view/revision and keyboard-focus origin across its confirmed profile reload. Deliberate pointer/key interactions and changed ownership cancel deferred focus; no authorization or retained history is changed.


- Domain: `Domain/Practice/`; `Crm.cs` defines append-only `ClientLifecycleEvent` archive/restore history.

- Application: `Application/Practice/PracticeCrmService.cs` (CRM / proposals) and `PracticeCrmService.Relationships.cs` (client-scoped revision-fenced archive/restore), `PracticeLeadQuery.cs` (current-access lead list), `WorkspaceQuery.Client.cs` (client metadata, searchable archive state, scoped counts and bounded pages), `PortfolioQuery.cs` / `.Workspace.cs` (archive-aware portfolio search and export), `Search/GlobalSearchQuery.cs` (client-scope archive search with archived-state label), `ClientContactCreationWorkspace.cs` (reviewed contact creation and actor-owned immutable receipt recovery), `PracticeTimeService.cs` (time / budgets), `BillingService.cs` (billing), `BillingInvoiceWorkspaceQuery.cs` (invoice workspace), `InvoicePaymentTermsService.cs` (evidence-backed maker/checker due-date revisions), `ReceiptAllocationReversalService.cs` (reviewed payment unapply), `FirmReceivablesAgingQuery.cs` (firm-fee ageing and CSV export), `LedgerService.cs` and `FirmFinanceQuery.cs` (firm financial ledger)
- Commercial documents: `Application/Practice/CommercialDocumentService.cs` and `.Tender.cs` recheck generator authority under the firm lock and protect profile/list release. Client receipt downloads resolve exact milestone/agreement/proposal and engagement lineage, then current account plus scoped grant together; both-null legacy source links remain readable only through a valid chain. `features/portal/finance.ts` labels the settlement document as a receipt. Focused `CommercialWorkflowTests` and the `AngularClientPortalJourneyTests` receipt-download journey cover these boundaries.
- CRM publication and focus: core lead/proposal commands recheck current actor/session after the firm lock, before publication or replay; lead and opportunity creation also revalidate active owners. Angular `features/commercial/leads.ts` preserves qualification focus and clears stale confirmations on refresh; `opportunities.ts` restores final-history-page focus only while the initiating focus ownership stands. `PracticeCrmTests` and the focused `AngularCommercialJourneyTests` qualification journey cover these boundaries. Lead opportunities use independent scoped timestamp/ID pages; Angular preserves history/drafts and owned keyboard success/retry focus. Client profile/archive and relationship writes also use firm-first scoped authority; graph duplicate/cycle checks serialize and stale revoke retries reload retained evidence. Quotation save/submit/rule changes recheck authority under the firm lock. Other commercial/provider writers, human AT and full release require separate acceptance.

- v2.2 workflow tasks (PRs D–G, WF-04 to WF-10 partial): `Domain/Practice/WorkflowTaskBinding.cs` binds optional metadata to the existing `WorkTask`; `WorkflowTaskDependency.cs` records immutable task-completion edges from the exact pinned template revision. `EngagementWorkflowTaskService.cs` owns scoped task commands; `WorkflowTemplateService.cs` owns immutable, independently reviewed template revisions; `EngagementWorkflowPlanService.cs` previews and transactionally instantiates exact published revisions with schedule/staffing digests, recoverable receipts, and their dependencies. Its engagement-scoped `TemplatesAsync` query exposes only matching published revisions to current engagement readers; `/api/ui/engagements/{id}/task-plan/templates` does not grant access to the firm template-administration catalog. `WorkflowTaskQueueQuery.cs` builds grant-scoped My Tasks, Team, Waiting for Client, Changes required, Under review, Overdue, and Completed pages; it projects Required/Optional only from the task's exact firm, pinned template revision and step, leaving manual tasks unlabelled. Angular shows that designation in list, board and calendar views as informational text; it does not change completion or professional gates. The client-waiting view reads exact linked PBC request states. Repeated rejected results project their current comment on the latest same-scope rework task when automation is off; a newer linked task alone represents the correction. Exact next resubmissions stay in Under review; later unrepresented submissions remain discoverable in the reviewer inbox. Source bindings and professional decisions are unchanged. `ProcedureReviewInboxQuery.cs` separately lists current submitted procedure results not represented by an exact linked rejected-result/next-submission rework pair (historical task links do not hide later submissions), restricted by the actor's current reviewer grant and staffing hierarchy; opening fieldwork and making a decision still rechecks the authoritative review gates. When `WorkflowExperience:Enabled` is true, sending a PBC request creates one idempotent, exact-scope follow-up task in the same transaction; task completion stays blocked until the source PBC record is accepted or closed. `PbcReminderDiscovery.cs` is an optional, default-off worker rule that queues pre-due and recurring overdue reminders through the existing PBC mail outbox, with quiet hours, append-only idempotency receipts, exact client grant/first-sign-in checks, and upload-freeze suppression at both queue and dispatch. `PracticeTimeService.cs` also enforces pinned task prerequisites while preserving task/time-entry lineage. A feature-flagged `ChangesRequired` review can atomically create one linked fieldwork rework task, assigning the prior preparer only while that user remains eligible; otherwise it is left unassigned for manager routing. `ManagerReviewSummaryQuery.cs` exposes redacted current completion blockers, evidence counts and SRM currency without making review or opinion decisions. API routes in `Api/UiEndpoints.WorkflowTasks.cs` remain default-off through `WorkflowExperience:Enabled`, behind trusted cookie-session and antiforgery boundaries; manager-review is read-only and uses the existing fieldwork authorization. Angular `features/practice/work-queue.ts` is the queue screen at `/ui/app/work`; `features/audit/fieldwork.ts` shows the manager-review summary. Persistence is in `AuditSphereDbContext.WorkflowTasks.cs` / `WorkflowTemplates.cs` and migrations `WorkflowTaskBindings`, `WorkflowTemplateCatalog`, `WorkflowTemplateOpenRevision`, `WorkflowTaskPlanReceipts`, `WorkflowTaskDependencies`, the PBC-follow-up binding, and `PbcReminderReceipts`. Tests: `EngagementWorkflowTaskTests.cs`, `WorkflowTemplateTests.cs`, `EngagementWorkflowPlanTests.cs`, `AuditDeliverablesTests.ManagerReview.cs`, `PbcWorkflowTaskTests.cs`, and `PbcReminderTests.cs`. The versioned task-plan authoring UI is `features/practice/workflow-templates.ts` (`/ui/app/practice/workflow-templates`), with direct refresh registered in `UiEndpoints.Infrastructure.cs`. Broader internal notifications/escalation, template-declared domain prerequisites, further annual/recurrence policies and deadline automation remain open. Operational tasks never advance audit lifecycle gates.

- Assignment response recovery: `src/AuditSphereOps.Ui/src/app/features/practice/work-queue.ts` reads the selected task through the existing scoped `tasks?taskId=` filter before the workspace bound, alongside its exact activity. Current owner/session, task, engagement and revision checks still fence the retained original assignment intent.
- Exact-source discovery for template plans: `Application/Practice/WorkflowEvidenceSourceQuery.cs` supplies bounded, paged PBC/procedure references and off-page selected-source resolution within the exact engagement. Its repeatable-read snapshot is followed by fresh authorization. `Api/Ui/UiEndpoints.WorkflowTasks.cs` exposes the feature-gated read; Angular `features/practice/workflow-evidence-source-picker.ts` provides lazy search, contextual keyboard controls and stale-response rejection inside the existing plan preview. Manual identifiers remain supported. Recorded source states never satisfy or approve evidence; preview and completion retain authority.
- Domain-evidence prerequisites (WF-08): `WorkflowTaskEvidenceRule.cs` retains append-only exact-source requirement/withdrawal revisions. `WorkflowTaskEvidenceQuery.cs` evaluates current materiality, accepted/closed PBC and current independently reviewed procedures; `PracticeTimeService.WorkflowEvidence.cs` applies manager-scoped changes with revision fencing and actor-owned receipt recovery. Core task completion rechecks live evidence under engagement/client locks, including the legacy completion route. Required pending PBC evidence also drives Waiting for client. The queue exposes unmet evidence counts, and `features/practice/workflow-task-evidence.ts` provides exact review, safe lost-response recovery and session/scope clearing. Persistence is in `AuditSphereDbContext.WorkflowEvidence.cs`; migration `WorkflowEvidencePrerequisites` adds scope FKs, immutable revision guards and retained-history downgrade refusal. Tasks never approve their underlying evidence. Tests: `WorkflowEvidenceRuleTests`, `WorkflowEvidenceProcedureTests`, `WorkflowEvidenceApiTests`, and `AngularWorkflowEvidenceJourneyTests`.
- Task ordering amendments (WF-08): `WorkflowTaskDependencyRevision.cs` overlays immutable pinned template edges with append-only required/withdrawn revisions. `WorkflowTaskDependencyQuery.cs` supplies the effective graph to queue and both completion routes; `PracticeTimeService.WorkflowDependencies.cs` locks and validates the complete engagement graph, checks both task revisions, rejects cycles and recovers actor-owned receipts. `features/practice/workflow-task-dependencies.ts` reviews exact predecessor/successor identities without approving professional evidence. `AuditSphereDbContext.WorkflowDependencyRevisions.cs` and `WorkflowDependencyRevisionGuardSql` enforce exact scope, immutable history and deferred acyclic-graph checks. Editing refuses engagements over the declared graph bound; no partial graph is silently edited.
- Operational cancellation (ASPM-WF-08, distinct from legacy recurrence WF-08): `EngagementWorkflowTaskService.CancelAsync` uses the existing `PracticeTimeService` locks and current scoped management grants. An active task's reviewed revision and reason produce one immutable `TASK_CANCELLED` activity; only the same actor/reason/revision can recover a committed reply. Cancelled tasks retain time, bindings and evidence, stay visible in the Cancelled queue, and do not satisfy required predecessors. `workflow-task-cancellation.ts` provides exact review and read-only status recovery through the scoped `tasks?taskId=` query before its normal row bound. Migration `WorkflowTaskCancellation` adds the retained reason and refuses downgrade after cancellation history exists. `PracticeTimeService.WorkflowRecovery.cs` adds reviewed reopening of cancelled tasks without completed-work evidence. It retains the same task, time, evidence and binding; exact historical cancellation/reopening replays acknowledge their immutable event without overwriting later state. The shared review panel uses server-projected `canReopen`, fresh assent and exact reason/revision. Migration `WorkflowTaskReopening` extends the activity constraint and refuses downgrade after reopening evidence exists. No lifecycle or professional approval is implied; completed-work correction, suspension and missed-period recovery remain separate work.


- Persistence: `AuditSphereDbContext.Practice.cs`; migration `ClientLifecycleArchiveHistory` adds client-scoped event revisions and append-only update/delete/truncate guards, with downgrade refusal after retained events exist.

- Current API/Angular: `src/AuditSphereOps.Api/Ui/UiEndpoints.ClientRelationships.cs` exposes `PUT /api/ui/clients/{id}/archive-state` behind trusted cookie and antiforgery checks; `UiEndpoints.Portfolio.cs`, `UiEndpoints.Finance.cs`, `UiEndpoints.Time.cs` expose the surrounding workspaces. Angular `features/clients/client.ts` offers authorized reversible archive/restore, and portfolio search retains archived clients and labels their state. Other current client/lead workspaces are `src/AuditSphereOps.Ui/src/app/features/clients/client.ts` and `src/AuditSphereOps.Ui/src/app/features/commercial/leads.ts`; invoice actions/term/reversal review and firm-fee ageing/export are in `src/AuditSphereOps.Ui/src/app/features/finance/invoice.ts` and `src/AuditSphereOps.Ui/src/app/features/finance/receivables-aging.ts`.
- Historical Blazor screens (pinned pre-removal source only): `Leads.razor`, `Portfolio.razor`, `ClientDetail.razor`, `Finance.razor`, `InvoiceDetail.razor`, `PracticeTime.razor`.

- Statement presentation (FSR first slice): pure builder and checks in `Application/Accounting/FinancialStatementPresentation.cs`; renderers in `FinancialStatementPresentationRenderer.cs`; database-bound read and export in `Application/Accounting/FinancialStatements/FinancialStatementService.Presentation.cs`; endpoints in `Api/Ui/UiEndpoints.Packages.cs`; Angular `features/accounting/presentation.ts`; tests `FinancialStatementPresentationTests` and `FinancialStatementTests.StatementPresentation_*`. Equity cross-casts preserve signed control totals for current and comparative periods; opposite-magnitude schedules fail while valid deficits remain supported.
- Report certificate signing (ADR-0016): signer policy and PDF signature verifier in `Application/Completion/ReportCertificateSigning.cs`; signing paths in `AuditDeliverableRenderer.cs` and `AuditDeliverableService.cs` (`SignIndependentReportAsync`, `VerifyReportSignatureAsync`) and `AuditDeliverableService.Bundle.cs`; certificate source `Infrastructure/Providers/PemReportSigningCredentialSource.cs` (`ReportSigning:*` settings); Angular `features/audit/completion.ts`; `ReportCertificateSigningTests`, `AuditDeliverablesTests`

- End-of-service provision (ADR-0010): treatment record and Partner confirmation, accrual draft and posting gate in `Application/Practice/LedgerService.EndOfService.cs`; projection in `EndOfServiceWorkspaceQuery.cs`; `Api/UiEndpoints.EndOfService.cs`; Angular `features/finance/end-of-service.ts`; database guards in migration `FirmEndOfServiceAccruals`; `EndOfServiceAccrualTests`

- Verification: `BillingWorkspaceApiTests` checks current authorization, CSRF and exact receipt/allocation/credit balances; `FirmReceivablesAgingQueryTests` and `FirmReceivablesAgingApiTests` cover historical balances, terms, reversals, scope denial and protected export; `AngularBillingWorkspaceJourneyTests` exercises the native flow and lost-response review.

Boundary: this is the firm's own books (`Practice/FirmLedger`); it never writes client

accounting or consolidation workspaces.

- Engagement deadlines: `Application/Practice/EngagementDeadlineReminderQuery.cs` retains all unresolved active overdue tasks and the upcoming window, using oldest-first bounded results. Task links carry exact engagement/task IDs; the work queue resolves them through its authorized selected-task query without widening the selected view. Actions require matching server queue state/revision. It remains a read-only scoped projection, not a reminder rule catalogue or provider delivery mechanism. Focused evidence is in `status.json` under `operationalReadiness20261019`.

## Commercial quotation, documents and the agreed-fee cycle

- Fee-agreement create/link recheck current authority after the firm lock; linking reloads retained state before replay decisions. Tender generation binds exact reviewed inputs to `AuditSphereTenderInputSha256` in the retained DOCX and verifies bytes before reuse; unverifiable legacy artifacts remain readable but cannot establish replay. Angular commercial documents recover keyboard focus to their persistent outcome status only while request/session/interaction ownership is current. Future voucher mail describes its exact attachment rather than promising an unsupported client download. Focused evidence and remaining acceptance gates are in `status.json` under `commercialFinality20261019`.

- Domain: `Domain/Practice/Commercial.cs` — `QuotationVersion`, `CommercialApprovalRule`, `QuotationApproval`, `FirmCommercialProfile`, `CommercialDocument`, `EngagementFeeAgreement`, `FeeMilestone`, `CommercialNotification`
- Application: `Application/Practice/QuotationCalculator.cs` (pure fee model: rate × hours → complexity → risk premium → discount), `CommercialApprovalMatrix.cs` (pure rule evaluation with a documented fail-safe default), `QuotationService.cs` (versioning, submit, matrix approvals, rule administration, approved-rate options), `CommercialDocumentRenderer.cs` + `CommercialDocumentService.cs` (branded Quotation, Engagement Letter and receipt as DOCX with template/profile version and SHA-256), `FeeAgreementService.cs` (50% advance and balance milestones over the existing `BillingService` review/post/receipt path; issuance owns or reuses one transaction through the draft/source/link, refreshing current authority and locked milestone state under the firm fence; automatic publication takes the exclusive firm fence before client/target locks; new invoice numbers retain full agreement IDs while retained source allocations relink legacy drafts; new opaque receipt/voucher references retain full source IDs while issued artifacts are reused), `CommercialMailDeliveryHandler.cs` (isolated `mail` worker; receipt/voucher sends retain and recheck the selected contact/route/authority basis plus mailbox under the existing Finance/primary/prospect policy; all-null legacy snapshots require current same-mailbox authority; operation/client/artifact/milestone/payment ownership must match before DISPATCHED; changed recipients block without rewriting queued mail; preparation and DISPATCHED share the firm fence with contact/routing writers, released before provider I/O; holding letters retain Completion contact/route/hash snapshots and require current blocker/artifact scope before attaching the exact retained bytes). `PracticeCrmService.ApproveProposalAsync` gates internal review on an approved, fee-matching quotation.
- Persistence: `AuditSphereDbContext.Commercial.cs`, migration `CommercialQuotationsAndFeeAgreements` (check constraints and append-only/field-restricted triggers)
- Current API/Angular: commercial contracts in `src/AuditSphereOps.Api/Ui/UiEndpoints.Commercial.cs`, `UiEndpoints.Quotation.cs`, `UiEndpoints.FeeAgreement.cs`, `UiEndpoints.CommercialDocuments.cs` and `UiEndpoints.CommercialSettings.cs`; Angular workspaces in `src/AuditSphereOps.Ui/src/app/features/commercial/quotation.ts`, `src/AuditSphereOps.Ui/src/app/features/commercial/proposal.ts`, `fee-agreement.ts`, `documents.ts` and `settings.ts`.
- Finance fee-settlement discovery is read-only: `Application/Practice/FeeSettlementWorkspaceQuery.cs` supplies the FinanceManager/FinanceReviewer-scoped paged list and agreement detail through `GET /api/ui/finance/fee-agreements?page={page}` and `GET /api/ui/finance/fee-agreements/{id}`. Angular routes `/app/finance/fee-agreements` and `/app/finance/fee-agreements/:id` use `features/finance/fee-settlements.ts`, with the detail route composing the existing `features/commercial/fee-agreement.ts`. Draft invoice submission continues through the existing `/app/practice/invoices/:id` UI and `POST /api/ui/finance/invoices/{id}/submit` adapter into `BillingService.SubmitInvoiceAsync`; approval and posting remain existing BillingService commands. This adds no commercial acceptance or invoice mutation rule.
- Historical Blazor screens and host endpoint (pinned pre-removal source only): `Components/Commercial/QuotationWorkbench.razor`, `FeeAgreementPanel.razor`, `ProposalWorkflowPanel.razor`, `ProposalDetail.razor`, `Pages/CommercialSettings.razor` and `Web/Program.cs`. Their presence here is not evidence of parity.
- Tests: `QuotationCalculatorTests.cs`, `CommercialWorkflowTests.cs`, `tests/.../CommercialJourneyTests.cs`, `AngularFeeSettlementJourneyTests.cs` (the focused authenticated journey passes against existing fee sources; it does not establish proposal acceptance, Partner clearance, engagement activation or provider-delivery acceptance).

## Acceptance paths, activation and engagement workspace provisioning

- Assessment command publication: `AcceptanceCommandAuthority.cs` establishes firm/client/actor lock order and post-wait authority checks. `AcceptanceChecklistService.cs` and `AcceptanceDecisionService.cs` join a reviewed caller transaction without committing it; the caller must roll back on failure. `AcceptanceChecklistTests` covers rollback and actor revocation during each command lock wait. The immutable request receipt owner and typed forms remain separate migration work.

- Native assessment detail: `Application/Acceptance/AssessmentWorkspaceQuery.cs` publishes a current-authority, client-serialized profile, exact selected decision and persisted progress projection through `Api/Ui/UiEndpoints.Routes.cs`. `AssessmentRouteQuery.cs` preserves legacy decision selection. Angular `features/acceptance/checklist.ts` distinguishes historical and engagement-specific records from the editable current client evaluation, fences late replies and binds Partner assent to the reviewed intent. Tests: `AcceptanceChecklistTests`, `AssessmentWorkspaceApiTests`, `AngularAssessmentParityJourneyTests` and `assessment-workspace.spec.ts`.
- Reviewed assessment backend: `Application/Acceptance/AssessmentCommandWorkspace.cs` composes the five existing commands with exact preview, explicit assent, actor-owned request receipts and serialized recovery through `Api/Ui/UiEndpoints.Acceptance.cs`. `Domain/Acceptance/AssessmentCommandReceipt.cs`, `AuditSphereDbContext.AssessmentReceipts.cs` and the receipt migration own append-only persistence, result/publication checks and retained-evidence rollback refusal. Tests: `AcceptanceChecklistTests.Receipts.cs` and `AssessmentCommandReceiptApiTests`. Angular `assessment-command-editor.ts` owns typed Signal Forms; `assessment-command-contracts.ts` validates exact preview/receipt contracts. `checklist.ts` binds fresh assent, retains references only, fences unknown outcomes and composes authorized receipt recovery. `assessment-navigation-dialog.ts` protects unsaved notes. Tests: `assessment-command.spec.ts` and both Angular route modes in `AngularAssessmentParityJourneyTests`. Decisions can additionally target an exact client-owned engagement; preview exposes its period and the receipt retains that target. The `EngagementScopedAssessmentReceipts` migration extends only the exact-result publication guard. `AcceptanceChecklistTests.EngagementDecision.cs` and `AngularScopedAssessmentDecisionJourneyTests` cover this path. Broader assessment parity remains pending.

- Native reviewed blocked creation: `Application/Acceptance/EngagementCreationWorkspace.cs` composes `EngagementLifecycleService.Creation.cs` inside an owned transaction; `EngagementPeriodConflictQuery.cs` checks same-client/service period overlaps in preview and under the creation safety lock while retaining exact retry recovery. `Api/Ui/UiEndpoints.EngagementCreation.cs` owns exact client context/preview/confirmation/receipt routes. Angular `features/clients/engagement-create*` owns Signal Forms review, bounded tab fields and unknown-result recovery. `Domain/Engagements/EngagementCreation.cs` and `NativeEngagementCreationReview` preserve immutable actor-owned request evidence and a deferred exact blocked-publication guard. Tests: `EngagementCreationReviewTests`, `EngagementCreationReviewApiTests`, `AngularEngagementCreationJourneyTests`, `engagement-create.spec.ts`. Migration and observed acceptance boundaries live only in `status.json`. `AnnualEngagementPreparationWorkspace.cs` proposes a contiguous next annual period from a client-owned source, separates copied references from fresh work, and validates the exact source under the creation lock. Source identity is retained in the existing immutable creation receipt. An optional reviewed annual template revision is also retained without changing legacy receipt JSON when absent. A single current revision from the source plan is suggested; the new task-plan screen preselects that exact revision only while published, and never replaces an unavailable reference automatically. Annual references include active contacts valid at next-period start and approved exact-source-period mapping identities, guarded by the accounting workbench read policy; no contact authority, mapping allocations or approvals are copied. Angular annual preparation uses the same reviewed creation screen and the existing preview/execute/recovery endpoints; the suggestion GET is `/api/ui/clients/{id}/annual-engagement/{sourceEngagementId}`. Tests: `AnnualEngagementPreparationTests`, `RelationshipManagerAnnualPreparationTests`, `AngularAnnualEngagementJourneyTests`. RelationshipManager client/firm grants can create blocked shells and read client summaries; engagement-only grants cannot widen to that parent scope. Professional engagement reads and Partner acceptance/activation remain separate. The `RelationshipManagerEngagementCreationReceipts` migration updates only the exact-publication role guard. Confirmed annual receipts hand off to the new engagement’s explicitly loaded task-plan panel, where an authorized coordinator separately reviews the current template, schedule and staffing before pinning tasks. They also link to the exact engagement in the existing assessment workspace (`?engagementId=`). The scope-checked read, reviewed continuance generation, fixed-scope Partner editor and engagement-period preview fence preserve fresh answers and immutable prior decisions.
- Native reviewed activation: `Application/Acceptance/EngagementActivationWorkspace.cs` composes `EngagementLifecycleService.cs`; `Api/Ui/UiEndpoints.EngagementActivation.cs` owns exact Partner context/preview/confirmation/receipt routes; Angular `features/engagements/activation*` owns Signal Forms assent, tab request recovery and lifetime fences. `NativeEngagementActivationReview` adds nullable request metadata to existing immutable evidence, a unique request identity and a deferred publication guard. Tests: `EngagementActivationReviewTests`, `EngagementActivationReviewApiTests`, `AngularEngagementActivationJourneyTests` and `activation.spec.ts`. Observed execution and migration boundaries live only in `status.json`.

- Domain: `Domain/Acceptance/Acceptance.cs` (paths, evidence reference, generation, adverse-answer metadata), `Domain/Engagements/Engagements.cs` (`EngagementActivation`)
- Application: `Application/Acceptance/AcceptanceRules.cs` (pure gate rules), `AcceptanceChecklistService.cs` (path-derived checklist), `AcceptanceDecisionService.cs` (records path and prior decision), `EngagementLifecycleService.Creation.cs` (client-serialized blocked draft creation with current authority checks), `EngagementLifecycleService.cs` (Partner-only activation); `EngagementRiskEvidence.cs` selects the exact recurring-period risk decision consistently for readiness, lifecycle, activation and commercial letters, retaining client-level first-period compatibility; `Application/Documents/EngagementWorkspaceProvisioning.cs` (`ProvisionEngagementWorkspace.v1` handler and discovery in the isolated `pbc` live group; exact `/Client/Year/01–05` tree when the approved client template has no `engagements` node, otherwise the legacy layout; binding and capability evidence through `PbcRepositoryProvisioning.ApplyEngagementBindingAsync`); `Microsoft365ConfigurationService.SteManifest` and the Setup page "Load STE layout" action
- Persistence: migration `AcceptancePathsEvidenceAndActivation`
- Current Angular/API: assessment in `src/AuditSphereOps.Ui/src/app/features/acceptance/checklist.ts`; engagement creation and activation in `src/AuditSphereOps.Ui/src/app/features/clients/engagement-create.ts` and `src/AuditSphereOps.Ui/src/app/features/engagements/activation.ts`; current HTTP contracts are in `src/AuditSphereOps.Api/Ui/UiEndpoints.Acceptance.cs`, `UiEndpoints.EngagementCreation.cs` and `UiEndpoints.EngagementActivation.cs`.
- Historical Blazor panels (pinned pre-removal source only): `Components/Acceptance/{AcceptanceChecklistPanel,EngagementActivationPanel,EngagementCreatePanel}.razor`.
- Upload intent freshness: `Application/Documents/PbcService.cs` refreshes the locked intent in resume, chunk recording and trusted completion. Reused contexts cannot supply cached capability, revision or received-state values. `UiPortalContractTests` covers interleaved HTTP/service rotations; `PbcTransferTests` covers current received-state replay with the same retained operation/attempt. HTTP handlers continue to own a fresh context per command; no new upload protocol or provider action is introduced.
- Delegation revocation: `Application/Documents/ClientPortalService.cs` shares the firm/client mutation fence with portal writes, locks and refreshes retained delegation/request records and rechecks primary-contact authority. An already-authorized write commits before revocation returns; later uploads reauthorize after the client fence. No delegation history is deleted.
- Client portal onboarding: `Domain/Documents/ClientPortal.cs` (`ClientPortalFirstSignIn`, `PbcRequestDelegation`, `ClientPortalIntent`), `Application/Documents/ClientPortalService.cs` (identity-path-derived first-sign-in gate used by `PbcService` uploads, primary-contact delegation, participant request query, portal intent view), `PracticeCrmService.RecordPortalIntentAsync` (conversion trigger) and `EngagementLifecycleService.ActivateAsync` (ready to invite); persistence `AuditSphereDbContext.ClientPortal.cs`, migration `ClientPortalOnboardingAndDelegation`. Current Angular portal entry points include `src/AuditSphereOps.Ui/src/app/features/portal/home.ts`, `documents.ts` and `request.ts`; current API contracts are in `src/AuditSphereOps.Api/Ui/UiEndpoints.Portal.cs` and `UiEndpoints.Pbc.cs`. Historical Blazor screens and upload script (`Pages/ClientPortal.razor`, `Pages/ClientPbcRequest.razor`, `Pages/ClientDetail.razor`, `wwwroot/pbc-upload.js`) are preserved only at the pinned pre-removal commit and do not establish parity.
- Tests: `AcceptanceChecklistTests.cs`, `EngagementWorkspaceProvisioningTests.cs`, `ClientPortalOnboardingTests.cs`, `tests/.../AcceptanceJourneyTests.cs`, `tests/.../ClientPortalOnboardingJourneyTests.cs`, `tests/.../AngularPbcStaffJourneyTests.cs`

## Resource planning, staffing, materiality engine and risk routing

- Domain: `Domain/Practice/ResourcePlanning.cs` (`StaffingLevels` with explicit role mapping, `EngagementStaffAssignment`, `StaffProfile`, `StaffCertification`, `StaffAvailability`, `StaffAllocation` attributed by phase and risk area, `BudgetPhases`), phase/risk-area on `WorkTask`, `TimeEntry`, `BudgetLine`; `Domain/Audit/MaterialityAndRiskBands.cs` (`MaterialityCalculation`, `RiskBandAssessment`, `RiskPartnerClearance`, `RiskOwnerAssignment`)
- Application: `Practice/StaffingService.cs`, `Practice/ResourcePlanningService.cs` + pure `ResourceGridCalculator.cs`, `PracticeTimeService.GetBudgetBreakdownAsync`; `Audit/MaterialityCalculator.cs` (pure PM/TE/SAD and `RiskBandRules`), `Audit/MaterialityEngineService.cs` (benchmark derivation from the current approved mapping, staleness used by `ApproveMaterialityAssessmentAsync`, SAD evaluation and completion), `Audit/RiskBandService.cs` (bands, owner levels, Partner review, completion blockers)
- Time workspace authority: `Practice/PracticeTimeWorkspaceQuery.cs` derives work and review projections from active grant scopes, then rechecks the session and those scopes before returning. Preparers see only open tasks assigned to them; scoped approvers can see unrouted tasks in their exact approval scope. The UI task-creation endpoint assigns new tasks to the creating user. Scope loss during the read fails closed even when a narrower grant survives; extra grants alone do not invalidate the projection. `PracticeTimeStartMinuteApiTests` includes deterministic grant-revocation and session-change interleavings.
- Practice/client-accounting boundary: `ClientAccountingTests.GeneralLedger.cs` and `ClientAccountingTests.OperationalLedger.cs` reuse populated imported and native fixtures to verify exact client-book preservation through independently approved billable time capture and correction. The native test also retains its ledger projection and posted balances. These checks do not establish group-consolidation or firm-ledger isolation.
- Time-entry review lifecycle (RES-04): `PracticeTimeService.SubmitTimeAsync` starts a numbered review round; `ReturnTimeAsync` requires an independent Manager/Partner/Administrator and a reason; the same entry can be resubmitted without changing its approved rate snapshot; `ApproveTimeAsync` remains independent. `TimeEntryReviewEvent` is append-only in PostgreSQL and retains each submission/return/approval actor and time. Migration `TimeEntryReturnWorkflow` records legacy submission timestamps as explicitly unattributed where the previous schema did not retain a submitter, backfills only approvals with a known reviewer, and refuses downgrade after history exists. Angular `features/practice/time.ts` shows the latest return reason and permits resubmission; the review queue requires a return reason. `PracticeTimeTests.ReturnedTime_RetainsEachReviewRound` verifies separation of duties, event immutability, downgrade refusal, retained captured rates across an independently approved rate revision, later capture at the revised rate and one approved billing source; `time-tracker-ui.spec.ts` covers the return/resubmit interaction.
- Persistence: `AuditSphereDbContext.ResourcePlanning.cs`, migrations `ResourcePlanningMaterialityEngineAndRiskBands` and `StaffAllocationPhaseRiskArea` (phase/risk-aware allocation identity, legacy `UNASSIGNED` attribution, exact reviewed-receipt and downgrade guards; existing band rule check, significance trigger and append-only evidence triggers)
- Current API/Angular: `src/AuditSphereOps.Api/Ui/UiEndpoints.Resources.cs`, `UiEndpoints.StaffingChange.cs`, `UiEndpoints.BudgetPreparation.cs` and `UiEndpoints.BudgetApproval.cs`; Angular `src/AuditSphereOps.Ui/src/app/features/engagements/planning.ts` and `src/AuditSphereOps.Ui/src/app/features/practice/resources.ts`. The native resource editor and reviewed receipts are described below; the resource grid also projects the exact recorded availability entries overlapping its selected date range, without implying a separate approval state. Grid and composed-workspace reads require an unscoped planning-role grant and recheck current authority after projection; scoped roles cannot be combined with a different global role to obtain the firm-wide grid. These locations do not assert parity for every planning action.
- Historical Blazor screens (pinned pre-removal source only): `Components/Planning/{EngagementStaffingPanel,EngagementBudgetPanel,MaterialityEnginePanel,RiskRoutingPanel}.razor`, hosted by `EngagementDetail.razor` and `AuditPlan.razor`, and `Pages/ResourcePlanning.razor`.
- Angular: `features/practice/resources.ts` and `resources.html` compose the API planning workspace; `resource-editor.ts` owns typed Signal Forms, persisted-profile editing and bounded validation. Allocation lines require one of the four assignable audit phases and may carry a risk-area label; their identity is engagement/person/Sunday-week/phase/risk, while the capacity grid aggregates every line. Historical allocations retain a visible `UNASSIGNED` attribution and remain removable through a reviewed zero-minute action. `resource-navigation-dialog.ts` protects memory-only edits. `planning-command-contracts.ts` decodes exact previews and receipts. `ResourcePlanningCommandWorkspace.cs` and `ResourcePlanningService.Authority.cs` enforce reviewed publication, current authorization and replay/recovery. Global profile, certification and availability changes and every global-console action require the planning role on an unscoped grant, including retained receipt access; allocation previews retain firm-wide capacity data. The direct allocation service/API continues to enforce its exact engagement scope. Historical phase-absent allocation receipts remain recoverable without rewriting their snapshot or replaying a mutation. `ResourcePlanningReceipt.cs`, `AuditSphereDbContext.ResourcePlanningReceipts.cs` and migration `NativeResourcePlanningReceipts` retain atomic immutable evidence. Tests include `PlanningResourcesAndMaterialityTests.Receipts.cs` and `ResourcePlanningReceiptApiTests.cs`.
- Tests: `PlanningResourcesAndMaterialityTests.cs`, `tests/.../PlanningAndResourcesJourneyTests.cs`, `AngularResourcePlanningJourneyTests.cs`, `features/practice/resources.spec.ts`

## Trial-balance intake and fieldwork connections

- Application intake: `Accounting/Intake/MultiPeriodTrialBalanceService.cs` (PeriodCode split over `TrialBalanceXlsxImporter.ReadTable` or CSV, per-period import through `TrialBalanceImportService`), `MappingMemoryService.cs`, `TrialBalanceCurrencyReviewQuery.cs`, `FinancialStatementDrillDownQuery.cs`, shared `MappedTrialBalanceSource.cs` (also used by the materiality engine)
- Native engagement profile/history: `Application/Practice/WorkspaceQuery.Engagement.cs`, `Api/Ui/UiEndpoints.cs`, `Ui/src/app/features/engagements/engagement*`, and the planning coordinator/capability owners (`planning.ts`, `planning.html`, `planning-contracts.ts`, `planning-state-context.ts`, `planning-budget-state.ts`, `planning-staffing-state.ts`); metadata, complete hold totals, bounded pages and separate client navigation authority. The three accounting preparation links derive from `CanPrepareAccounting`, using the same `AccountingPreparationAuthorization` role/scope gate as their protected endpoints. `EngagementProfileWorkspaceTests`, `EngagementProfileWorkspaceApiTests`, `AngularEngagementProfileJourneyTests` and `engagement-workspace.spec.ts` cover scope, history, capability visibility, direct-route refusal and UI lifetime boundaries. Complete planning form parity remains open.

- Native intake currency review: `Ui/UiEndpoints.Intake.cs` composes `TrialBalanceCurrencyReviewQuery`; Angular `features/engagements/currency-review.ts` renders scope, provenance and exact comparison; `CurrencyReviewApiTests`, `AngularCurrencyReviewJourneyTests` and `currency-review.spec.ts` exercise scope/identity/read fences and native presentation.
- Domain: `Domain/Audit/FieldworkConnections.cs` (`AuditSamplingRun`, `ProcedureEvidenceLink`, `PhysicalEvidenceItem`/`Movement`, `ProcedurePhysicalLink`, `AdHocProcedureInsertion`/`Revision`)
- Application fieldwork: `Audit/Fieldwork/AuditFieldworkService.Connections.cs` (sampling run and re-performance, evidence picker, physical index, ad hoc steps, analytical-review and going-concern completion blockers wired into `EvaluateCompletionAsync`)
- Persistence: `AuditSphereDbContext.FieldworkConnections.cs`, migration `FieldworkConnectionsSamplingEvidenceAndPhysicalIndex`
- Current API/Angular: `src/AuditSphereOps.Api/Ui/UiEndpoints.Intake.cs`, `UiEndpoints.Statements.cs` and `UiEndpoints.Fieldwork.cs`; Angular `src/AuditSphereOps.Ui/src/app/features/engagements/tb-intake.ts`, `src/AuditSphereOps.Ui/src/app/features/engagements/statements.ts` and `src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.ts`. Current paths do not assert parity for every historical action.
- Historical Blazor screens (pinned pre-removal source only): `Pages/TrialBalanceIntake.razor`, `Pages/StatementDrillDown.razor`, `Components/Fieldwork/FieldworkToolsPanel.razor` and `AuditFieldwork.razor`.
- Tests: `FieldworkConnectionsTests.cs`, `tests/.../FieldworkConnectionsJourneyTests.cs`
- Attribute-strata sampling: `Domain/Audit/AuditSamplingEngine.cs` validates reviewer-selected account/currency/direction/month attributes and produces deterministic strata; `Application/Audit/AuditSamplingService.cs` previews and persists the exact selection with source digest, ordering policy, fields and engine version; `Audit/Fieldwork/AuditFieldworkService.Connections.cs` commits the selection and run atomically and fences replay against source changes. `Application/Audit/AuditFieldworkWorkspaceQuery.cs` publishes authorized review/execution capabilities; `Api/Ui/UiEndpoints.Fieldwork.cs` exposes preview, run, sample-set and item-review commands. Angular `features/audit/fieldwork-sampling-state.ts` owns inputs, preview fingerprinting, sample pages and item-test drafts; `fieldwork.html` presents exact preview rows/strata, explicit sample review, item-test evidence and a separate reviewer decision. Migration `AttributeStrataSamplingPreview` retains the added provenance.
- Sampling verification: `AuditSamplingEngineTests.cs`, `FieldworkConnectionsTests.cs`, `AuditSamplingSelectionIsolationTests.cs`, `AngularAttributeSamplingExecutionJourneyTests.cs` and `features/audit/fieldwork.spec.ts` cover engine validation, scoped persistence, independent review, execution, item-level evidence and second-person review. The journey does not assert statistical sufficiency; wider completion-story acceptance remains in `docs/execution/status.json`.

## Review notes, completion deliverables and file freeze

- Domain: `Domain/Completion/AuditDeliverables.cs` (`ProcedureReviewNote`/`Event`, `AuditDeliverable`, `PartnerCompletionClearance`, `AuditOpinionDecision`, `SignatureSpecimen`/`Application`, `ClientDeliverableReview`/`Comment`), `ConfirmationCriticality` and `AuditConfirmationCase.DispatchedAt`; `Domain/Records/FileFreeze.cs` (`EngagementFileFreeze`, `FileFreezeAmendment`, `FrozenAccessAttempt`, `DocumentLock`)
- Application: `Audit/ReviewNotesService.cs` (anchored notes; open-note and staffing-hierarchy checks inside `AuditProgramService.ReviewResultAsync`), `Completion/AuditDeliverableRenderer.cs` (DOCX with PNG embedding), `Completion/AuditDeliverableService.cs` (facts digest and staleness, SRM, Partner clearance, opinions, report trio, holding letter, signing, client review loop, confirmations dashboard, downloads), `Records/FileFreezeService.cs` (schedule, `FileFreezeHandler`/`FileFreezeDiscovery` on an injected clock, separately approved supplementary-record requests, terminal local freeze, write guard), `Records/EngagementActivityQuery.cs` (activity trail, document locks)
- Persistence: `AuditSphereDbContext.AuditDeliverables.cs`, `AuditSphereDbContext.FileFreeze.cs`; migrations `ReviewNotesDeliverablesOpinionsAndSignatures`, `ScheduledFileFreezeAndDocumentLocks`
- API/Angular/Worker: `Api/Ui/UiEndpoints.Completion.cs` and `.Portal.cs` own deliverable, signature, comment, freeze and portal routes; Angular completion/file-record workspaces render them; the freeze handler and discovery are registered in the Worker. The retired Web panels remain only at the pre-removal commit.
- Tests: `AuditDeliverablesTests.cs`, `tests/.../CompletionDeliverablesJourneyTests.cs`

## Technical library, practice analytics and firm books

- Domain: `Domain/Practice/FirmOperations.cs` (`TechnicalLibraryDocument`/`Version`, `StaffCostRate`, `FirmExpense`)
- Application: `Practice/FirmOperationsServices.cs` (`TechnicalLibraryService` — also feeding `GlobalSearchQuery`; `PracticeAnalyticsQuery`; `FirmExpenseService` over `LedgerService` journals and the firm trial balance; report periods, posting totals, comparatives and activity share an owned repeatable-read snapshot, committed before final current authorization; caller-owned transactions are refused without changing them)
- Persistence: `AuditSphereDbContext.FirmOperations.cs`, migration `TechnicalLibraryAnalyticsAndFirmExpenses` (published-version immutability, append-only cost rates)
- Current API/Angular: `src/AuditSphereOps.Api/Ui/UiEndpoints.PracticeInsights.cs` and `UiEndpoints.FirmBooks.cs`; Angular `src/AuditSphereOps.Ui/src/app/features/library/library.ts`, `src/AuditSphereOps.Ui/src/app/features/practice/analytics.ts` and `src/AuditSphereOps.Ui/src/app/features/finance/books.ts`. This identifies current files and routes only; it does not assert parity with the former library screens.
- Historical Blazor screens (pinned pre-removal source only): `Pages/TechnicalLibrary.razor`, `Pages/PracticeAnalytics.razor` and `Pages/FirmBooks.razor`.
- Tests: `FirmOperationsTests.cs`, `tests/.../FirmOperationsJourneyTests.cs`

## Microsoft 365 setup, security and administration

- Application: `Application/Microsoft365/Microsoft365ConfigurationService.cs`, `SelectedResourceAdministration.cs` (bounded administrator projection, reviewed draft edits and activation composition), `SelectedSiteBoundaryVerificationService.cs` (trusted exact-revision positive/negative resource evidence), `TenantConsentService.cs`, `TenantConnectionQuery.cs`, `DirectoryDiscoveryService.cs`, `DirectoryUserBindingService.cs`, `DirectoryCapabilityVerificationService.cs`, `TenantAdministrationProviders.cs` (provider interfaces + permission matrix), `TenantCapabilityService.cs`, `DirectoryProvisioningService.cs` (user creation, guest invitation, reconciliation), `ManagedGroupService.cs`, `AdministrationOverviewQuery.cs`; `Application/Security/` (`RoleAssignmentService.cs`, `RoleGrantExpiry.cs` with firm/user/grant expiry fencing and a read-only no-expiry fast path, `UserAccessWorkspaceQuery.cs`)

- Persistence: `AuditSphereDbContext.Security.cs`, `.Microsoft365.cs`; dedicated certificate and Graph directory reader, `GraphCapabilityCredential.cs`, `GraphTenantAdministrationProviders.cs`, `GraphTenantConsentVerifier.cs` and the Development/Test-only `SimulatedMicrosoftTenant.cs` in `Infrastructure/Providers/`

- Current API/Angular: `src/AuditSphereOps.Api/Ui/UiEndpoints.Microsoft365.cs`, `UiEndpoints.TenantOperations.cs`, `UiEndpoints.SelectedResources.cs`; `src/AuditSphereOps.Ui/src/app/features/admin/tenant.ts`, `src/AuditSphereOps.Ui/src/app/features/admin/sharepoint.ts`, `provisioning.ts`, `groups.ts` and `operation-recovery.ts`. The API owns consent/authentication callback composition.
- Historical Blazor screens (pinned pre-removal source only): `Microsoft365Setup.razor`, `TenantConnection.razor`, `Administration.razor` and `Components/Administration/*`.

- Tests: `tests/.../` Microsoft 365 and security suites

## Staff navigation search

- Application: `Application/Search/GlobalSearchQuery.cs` — bounded search over clients, engagements, PBC requests, leads, invoices and staff pages. Relationship Managers can discover client identities covered by their current client grants or explicit firm-wide RelationshipManager grant; an engagement-only grant does not widen to the parent client. Archived clients are labeled in the result. Candidates are prefiltered by the actor's grants for each result kind, then every hit is re-authorized with the decision its destination route applies (`AuthorizationDecision`, `BillingService.CanOpenInvoiceAsync`). Not a document, evidence or email search.
- Current API/Angular: global search route in `src/AuditSphereOps.Api/Ui/UiEndpoints.cs`; `src/AuditSphereOps.Ui/src/app/features/search/search.ts` and the current Angular shell/navigation files.
- Historical Blazor implementation (pinned pre-removal source only): `Components/Layout/GlobalSearch.razor`, `MainLayout.razor`, `wwwroot/search-shortcut.js` and `wwwroot/enterprise-ui.css`.
- Tests: `tests/AuditSphereOps.Domain.Tests/GlobalSearchQueryTests.cs`, `tests/AuditSphereOps.E2E.Tests/GlobalSearchJourneyTests.cs`

## Operations — durable work, worker host

- Application: `Application/Operations/`

- Persistence: `AuditSphereDbContext.Operations.cs`

- Current API/Angular: `src/AuditSphereOps.Api/Ui/UiEndpoints.Operations.cs` and `src/AuditSphereOps.Ui/src/app/features/admin/operations.ts`.
- Historical Blazor screen (pinned pre-removal source only): `Operations.razor`.

- Worker: `src/AuditSphereOps.Worker/` (`BackgroundService` hosts)

Critical invariants: long-running work goes through durable operations with revision fencing,

idempotent retries and explicit cancellation dispositions.

## Tests layout

- `tests/AuditSphereOps.Domain.Tests/` — PostgreSQL-backed domain and integration suites.

  `ClientAccountingTests/` holds the capability partials; `LineTranslationTests.cs`,

  `TrialBalanceCalculatorTests.cs`, `AccountingRecordsApprovalTests.cs`,

  `SourceAcceptanceAndComparativesTests.cs`, `ProfilesChartsAndConsolidationTests.cs` are

  single-topic files. `ArchitectureGuardTests.cs` enforces the project reference rules.

- `tests/AuditSphereOps.Api.Tests/` — API security and transfer tests.

- `tests/AuditSphereOps.E2E.Tests/` — Playwright journeys.

Every test provisions a disposable PostgreSQL schema; there is no InMemory provider fallback.

## Documentation and specification authority

For the complete documentation index, authority hierarchy, current requirements, proposed backlogs, operational runbooks, and evidence, consult the central documentation authority:

👉 [`docs/auditsphere-docs-index.md`](../auditsphere-docs-index.md)

## Dedicated client SharePoint sites

- Application: `Documents/ClientSharePointSites.cs` owns standing administrator authorization, the fixed UTC rollout boundary, discovery, durable operation lifecycle, immutable identity decisions and separate site/member projections. `Documents/PbcRepositoryProvisioning.cs` applies the configured/saved cutoff before any shared-site fallback; invalid or mismatched boundaries block, even before the client-sites worker persists the cutoff.
- Operator adapter: `Documents/ClientSiteOperatorIntents.cs` and `Ui/UiEndpoints.ClientSiteOperatorIntents.cs` own current administrator review, persisted-cutoff and staff fingerprints, local intent requests and bound worker-receipt read-back. Historical receipts project pending membership after authority changes. They reuse discovery; no privileged provider is composed in the API/CLI. Terminal orchestration lives under `scripts/m365/`; see its operator guide and execution ledger for integration acceptance.
- Domain: `Microsoft365/ClientSharePointSite.cs`, `Microsoft365/ClientSiteWorkerHeartbeat.cs`; EF mapping: `AuditSphereDbContext.ClientSharePointSites.cs`.
- Infrastructure: `Providers/SharePointClientSiteProvider.cs`; enabled-by-default isolated Acceptance `client-sites` worker with a read-only, dedicated credential mount. Missing rollout/configuration/consent remains visible as blocked; the `pbc` worker stays Sites.Selected.
- Current API/Angular: `src/AuditSphereOps.Api/Ui/UiEndpoints.SelectedResources.cs` and `UiEndpoints.WorkspaceProvisioning.cs`; Angular `src/AuditSphereOps.Ui/src/app/features/admin/sharepoint.ts` and `src/AuditSphereOps.Ui/src/app/features/engagements/planning.ts`.
- Historical Blazor panels (pinned pre-removal source only): `Administration/ClientSharePointSitesPanel.razor` and `Planning/EngagementStaffingPanel.razor`.
- Tests: `ClientSharePointSitesTests`, `SharePointClientSiteProviderTests`, `ClientSharePointSitesJourneyTests`.
- Permission and configuration authority: [client-site decision](auditsphere-client-sharepoint-sites-current.md).
- Requirement coverage and unclosed gaps: [STE coverage](../execution/auditsphere-ste-specification-coverage-current.md).

### STE reporting and commercial extensions

- `Practice/CommercialDocumentService.Tender.cs` assembles reviewed five-chapter proposals; current Angular commercial settings and quotation workspaces are `src/AuditSphereOps.Ui/src/app/features/commercial/settings.ts` and `src/AuditSphereOps.Ui/src/app/features/commercial/quotation.ts`. Historical Blazor references (pinned pre-removal source only): `CommercialSettings.razor` and `QuotationWorkbench.razor`.
- `Audit/AuditProgramService.cs` serializes the final review transition; `Completion/AuditDeliverableService.cs` automatically compiles the SRM and preserves separate Partner clearance/opinion/signing decisions.
- `Completion/AuditDeliverableRenderer.cs` renders the signed report PDF with a validated registered PNG specimen. The renderer also embeds the exact versioned approved firm-seal image; neither PNG image asserts certificate-backed signing.
- `Practice/ContractContributionCalculator.cs` is the pure contracted-fee-minus-standard-time-value calculator; `FirmOperationsServices.cs` and current Angular `src/AuditSphereOps.Ui/src/app/features/practice/analytics.ts` expose it separately from actual staff-cost metrics. `PracticeAnalytics.razor` is a historical screen reference at the pinned pre-removal commit only.
- Verification sources: `CommercialWorkflowTests`, `AuditDeliverablesTests`, `ContractContributionCalculatorTests` and their commercial/completion journeys. Remaining specification gaps are recorded in the execution coverage matrix.

### STE signed representations, five-part bundles and standing invoice drafts

- Domain: `Completion/DeliverableAssembly.cs`; nullable immutable commercial proof references in `Practice/Commercial.cs` and the approved FSLI identity in `Completion/AuditDeliverables.cs`.
- Application: `Completion/AuditDeliverableService.{Representations,Bundle,Seal,OpinionAreas}.cs`; `Practice/CommercialDocumentService.cs` owns dual-key letters; `Practice/AutomaticFeeInvoices.cs` owns standing-policy discovery and LOCAL draft operations; `Documents/ClientPortalService.cs` owns commercial portal readiness and release upload fences.
- EF: `AuditSphereDbContext.AuditDeliverables.cs`, `.Commercial.cs`; `SteDeliverableAssembly` adds scoped proof foreign keys, append-only triggers, exact manifest text and the engagement-letter insertion guard.
- Retired Web (historical): the Blazor completion, portal and quotation panels and their host endpoints are preserved at the pre-removal commit only. Current delivery is the API plus Angular workspaces; worker policy defaults remain in `Worker/appsettings.json`.
- Tests: `AuditDeliverablesTests.Assembly.cs`, `CompletionBundleFixture.cs`, `CommercialWorkflowTests` and `CompletionDeliverablesJourneyTests`.
- Bundle assembly rechecks the current signature policy before returning an existing bundle. Certificate-signed reports require the matching certification credential; missing/mismatched credentials refuse both new assembly and replay. Stored bundle downloads remain separate authorized reads. No signing requirement is enabled by this guard. The retired Web completion panel is historical; the API/Angular path carries the configured signing policy and credential source. Correspondence count guards are followed by limit-plus-one reads and result checks, so concurrent appends cannot cause an unbounded materialization. Integrity checks cover the stored representation template as well as the report, management letter, signed scan and statement PDF.

## Protected open-session presentation

- Retired Web (historical): `Components/Shared/SessionAccessBoundary.razor` is preserved at the pre-removal commit only. The current boundary is Angular `core/session.ts` plus the API's `TrustedActorResolver`; it rechecks identity, disabled state, expiry and session epoch and does not replace Application command/query authorization.
- Verification: `PassiveSessionRevocationJourneyTests` covers staff and client Angular routes, disabled identities and stale epochs without navigation or clicks.
- Systematic sampling: `Domain/Audit/AuditSamplingEngine.cs` adds seeded equal-row-spacing selection, including zero-value rows. `AuditFieldworkService.Connections.cs` stores order-bound source identity and a distinct engine version; the Angular fieldwork page supplies sample size/seed and the existing immutable calculation log. Migration `SystematicAuditSampling` extends the allowed-method constraint. Existing sampling engines and stored source digests retain their contracts.
- **Procedure execution and evidence:** `Application/Audit/AuditProcedureCommandWorkspace.cs` and `AuditProcedureCommandContracts.cs` own state, preview, action, receipt and recovery; `AuditProgramService.cs` enforces exact definition/result/input fencing and writes `AuditProcedureCommandReceipt`; `AuditProgramService.Evidence.cs` resolves stored link, physical and manual evidence into an immutable server snapshot; `AuditProcedureSupportMap.cs` is navigation-only. API routes: `UiEndpoints.Fieldwork.cs` `/procedures/{id}/commands/{state,preview,actions,receipts}`; direct `/results` and `/review` writes fail closed. Migrations: `20261017141659_AuditProcedureCommandReceipts`, `20261017142359_AuditProcedureCommandEvidenceDigest`. Angular: `features/audit/fieldwork.ts` procedure panel and `fieldwork.spec.ts`; tests: `AuditProgramWorkflowTests.ProcedureCommand_ExactPreviewAndReceiptRecoverOriginalOutcome`, `AuditProcedureSupportMapTests`.
- **Current-evidence progress:** `AuditFieldworkService.ProcedureEvidence.cs` is the shared batch evaluator used by `EngagementAuditProgramQuery`, `AuditAreaSummaryQuery`, `AuditFieldworkWorkspaceQuery` and completion; raw submitted/returned or historical reviewed rows are not treated as current reviewed work.

## API backend and Angular presentation migration

- Application: `Practice/PortfolioQuery{,.Scope,.Workspace}.cs` (bounded current-grant clients, summary, recent records and formula-safe scoped export under one rechecked scope snapshot).
- API host: `src/AuditSphereOps.Api/ApiHost.{Authentication,AuthenticationEndpoints,DocumentEndpoints,Persistence,Providers,Health,Observability}.cs`; `Authentication/TrustedActorResolver.cs`, consent composition and production Data Protection.
- HTTP contracts: `src/AuditSphereOps.Api/Ui/UiEndpoints.*.cs` (trusted session, CSRF-protected commands, exact financial strings and explicit SPA routes).
- Route cutover: `src/AuditSphereOps.Api/Ui/AngularRouteOwnership.cs`, `UiEndpoints.cs`, authentication/setup/consent composition and Angular `src/AuditSphereOps.Ui/src/app/core/navigation.ts` preserve deployment-owned canonical or preview bases and optional retained fingerprinted assets; `CanonicalAngularHostTests`, `AngularNavigationJourneyTests` and `AngularPortfolioJourneyTests` cover local ownership, asset fallback and protected session boundaries. This route behavior is distinct from the retired Blazor rollback host.
- Retired Web host: `Program.cs` and `Authentication/CurrentActorResolver.cs` remain in Git history at the pre-removal commit as migration evidence. The owner-authorized removal was merged to `master` as `e7ebe27c`; the host is absent from the current tree.
- Client portal queries: `Application/Documents/ClientPortalWorkspaceQuery.cs`, `ClientPortalReviewQuery.cs`, `ClientPortalReviewCommands.cs`.
- Administration: `Application/Microsoft365/TenantAdministrationWorkspaceQuery.cs` (safe saved tenant label and mail/records draft state projection, separate from verified capabilities), existing tenant/directory providers, and `Security/RoleAssignmentReviewDigest.cs` for current-access review fencing.
- Native portal/admin: `src/AuditSphereOps.Ui/src/app/features/{portal,admin}/`; standalone API contract tests and Angular browser journeys.
- Angular: `src/AuditSphereOps.Ui/src/app/core/session.ts` and `features/portfolio/portfolio.ts`.
- Responsive shell: `src/AuditSphereOps.Ui/src/app/app.{ts,html,scss}` and `core/workspace-navigation{,-dialog,-icon}.ts`, `navigation.ts` and `navigation-icons.ts`; `app-navigation.spec.ts` and `AngularShellNavigationJourneyTests.cs` cover owner/load fencing, keyboard focus, resize, revocation and client navigation isolation. `AngularRoutingContractTests.cs` checks legacy/native route disposition.
- Portfolio API/presentation: `src/AuditSphereOps.Api/Ui/UiEndpoints.Portfolio.cs`, `src/AuditSphereOps.Ui/src/app/features/portfolio/portfolio{,-contracts,.html,.scss}` and identity-owned return parameters in `src/AuditSphereOps.Ui/src/app/features/clients/client.ts`.
- Tests: `PortfolioQueryTests`, `PortfolioWorkspaceTests`, `PortfolioWorkspaceApiTests`, `UiContractTests`, `AngularPortfolioJourneyTests`, Angular component/decoder tests. `PortfolioWorkspaceSeed` supplies synthetic read-side records without professional release acceptance.
- Authority: [migration ownership and remaining scope](auditsphere-angular-migration-current.md).

### Angular workspace administration

- Application: `Documents/WorkspaceProvisioningAdministration.cs` supplies bounded administrator workspace pages, exact target/resource/template review fingerprints and reviewed calls to existing `PbcRepositoryProvisioningService`. `ClientSharePointSiteQuery` projects separate dedicated-site and membership health.
- API: `Ui/UiEndpoints.WorkspaceProvisioning.cs` exposes authenticated bounded reads and CSRF-protected client/engagement folder commands. It does not host privileged site creation or membership providers.
- Angular: `features/admin/workspaces.ts` and `workspace-contracts.ts` render native target review, reasons, idempotent folder actions, independent PBC verification, policy/boundary/worker/permission status and per-client site/member recovery details; unknown responses fence further writes.
- Worker: `EngagementWorkspaceProvisioningHandler` fingerprints the provider plan, applies the same rollout cutoff, and refuses shared-site fallback, changed plans or folder ownership collisions before binding; publication serialization stays local to PostgreSQL after provider I/O.
- Checks: `WorkspaceProvisioningApiTests`, `AngularWorkspaceAdministrationJourneyTests`, `ClientSharePointSitesTests`, `SharePointClientSiteProviderTests`, `ClientSharePointSitesJourneyTests`, `workspaces.spec.ts` and `EngagementWorkspaceProvisioningTests`. Observed results belong to the execution ledger.

### Angular confirmation administration

- Application: `Audit/ConfirmationWorkspace.cs` (bounded scoped register, current evidence review and local reviewed command transaction), `Audit/Fieldwork/AuditFieldworkService.Confirmations.cs` (confirmation lifecycle, response revisions, independent alternative review and closure).
- API: `Ui/UiEndpoints.Confirmations.cs` (trusted scoped reads and CSRF-protected reviewed commands; exact supported decimal inputs).
- Angular: `features/audit/confirmations.ts`, `confirmation-contracts.ts` (native Signal Forms, separate preparation/observed dispatch/review states, bounded filters and unknown-outcome fences).
- Verification: `ConfirmationApiTests`, `AngularConfirmationJourneyTests`, `confirmations.spec.ts`, existing fieldwork/sampling/deliverables suites.

- Native reviewed confirmation batches: `Application/Audit/ConfirmationWorkspace.Batch.cs` owns the scoped transaction and reviewed-register fences; `AuditConfirmationBatchService.cs` retains batch lifecycle preparation. `Api/Ui/UiEndpoints.Confirmations.cs` validates bounded exact inputs, and Angular `features/audit/confirmations.ts` owns the multi-case Signal Form. API, Angular unit and confirmation browser tests cover atomic publication and stale/duplicate refusal.

- Confirmation closure evidence: Domain `Audit/AuditConfirmationClosure.cs`, Application `Audit/Fieldwork/AuditFieldworkService.Confirmations.cs`, Infrastructure `AuditSphereDbContext.Fieldwork.cs` and the additive `ConfirmationClosureEvidence` migration retain immutable human conclusions and exact evidence snapshots. The native confirmation detail projection exposes scoped decision metadata; API and Angular browser tests cover retained evidence and current-alternative ordering.

- Confirmation tab drafts: Angular `core/tab-drafts.ts` owns versioned identity/epoch/base envelopes and explicit tab storage; `core/unsaved-changes.ts` owns the Material leave dialog and route guard. `features/audit/confirmation-drafts.ts` bounds allowlisted intent; `confirmations.ts` owns recovery, refreshed-revision review and command-outcome fencing. `tab-drafts.spec.ts`, `confirmations.spec.ts` and native confirmation journeys cover storage failure, expiration, stale identity/revisions, tab isolation and revoked sessions.

### Angular currency remeasurement

- Application `Accounting/CurrencyRemeasurementWorkspaceQuery.cs` owns scoped bounded input reads and approved-input revisions; `CurrencyRemeasurementWorkspace.cs` owns reviewed guarded API transactions around existing `CurrencyRemeasurementService.cs` calculations and lineage.
- API `Ui/UiEndpoints.Remeasurement.cs` validates exact decimal text and composes the Application contract.
- Angular `features/accounting/remeasurement.ts` and `remeasurement-drafts.ts` own native Signal Forms, explicit tab recovery, source/provenance presentation, independent review and unknown-outcome fencing.
- Verification: `RemeasurementApiTests`, `AngularRemeasurementJourneyTests`, `remeasurement.spec.ts`, existing currency/line-translation and legacy workbench tests. Execution evidence is recorded only in the ledger.

### Angular currency configuration

- Application: `Accounting/CurrencyConfigurationWorkspace.cs` owns bounded firm configuration reads, actor-bound reviewed revisions, serialized local commands and valid-observation approval gates; `CurrencyTranslationService.cs` retains preparation and independent approval.
- API: `Ui/UiEndpoints.CurrencyConfiguration.cs` validates explicit dates, purpose and exact rate text; `UiEndpoints.Infrastructure.cs` owns the explicit SPA route.
- Angular: `features/accounting/currency-configuration.ts` and `currency-configuration-contracts.ts` own native Signal Forms, review binding, tab recovery and unknown-outcome fencing.
- Checks: `CurrencyConfigurationApiTests`, `AngularCurrencyConfigurationJourneyTests`, and `currency-configuration.spec.ts`; observed results belong to the execution ledger.

### Reviewed native trial-balance upload

- Application: `Accounting/Intake/TrialBalanceUploadWorkspace.cs` owns exact-file reviewed intake and read-only source/validation reconciliation; `TrialBalanceImportService.cs` owns period locking and reviewed-period/final authority fencing. `MappingMemoryService.cs` bounds historical proposals and filters prior engagement authority before selection.
- API: `Ui/UiEndpoints.Intake.cs` exposes reviewed upload and receipt reads through the trusted session and antiforgery boundary.
- Angular: `features/engagements/tb-upload.ts` and `tb-upload-contracts.ts` own native assent, bounded metadata checkpoints, guarded navigation, context clearing and unknown-outcome recovery; `tb-intake.ts` preserves the receipt panel during dataset-selector refresh.
- Tests: `TrialBalanceUploadApiTests`, `AngularTrialBalanceUploadJourneyTests` and `tb-upload.spec.ts` cover negative authority/period/file cases, partial and concurrent recovery, isolated mapping history, lost responses and owned worker validation.

### Native trial-balance source inspection

- Application: `Accounting/Intake/TrialBalanceSourceWorkspace.cs` composes scoped source context, server row/issue pages and final authority fences. `TrialBalanceDatasetQuery.cs` owns deterministic paging, current-revision issue reads, bounded CSV generation and missing-value preservation.
- API: `Ui/UiEndpoints.AccountingRecords.cs` exposes source inspection and reviewed-revision CSV export through the trusted actor and antiforgery boundary.
- Angular: `features/engagements/tb-source.ts` owns the native source panel, exact-value decoding, server filters/pages and context-checked file saving; `core/api.ts` supports an optional pre-save file metadata fence.
- Tests: `TrialBalanceSourceApiTests`, `tb-source.spec.ts`, shared API download tests and `AngularTrialBalanceUploadJourneyTests` cover source isolation, malformed/stale responses, missing values, export bounds and actual scoped file download.

## Native reviewed source acceptance

- Application: `Accounting/Intake/SourceAcceptanceWorkspace.cs` and `.GeneralLedger.cs` own bounded current-source review/reconciliation and actor/session-bound reviewed revisions; `AccountingSourceAcceptanceService.cs` owns independent reviewer checks, serialized immutable decisions and atomic generation changes.
- API: `UiEndpoints.AccountingRecords.cs` maps scoped source-acceptance reads and antiforgery-protected writes; `UiEndpoints.Infrastructure.cs` declares the native deep link.
- Angular: `features/accounting/source-acceptance.ts` owns native Signal Forms, explicit evidence-reference tab recovery, navigation protection and unknown-outcome fencing; `features/engagements/tb-source.ts` and `general-ledger.ts` link from their respective inspected sources. GL metadata is separate from TB validation; draft identities cannot cross source kinds.
- Tests: `SourceAcceptanceApiTests.cs`, `AngularSourceAcceptanceJourneyTests.cs`, `AngularGeneralLedgerAcceptanceJourneyTests.cs`, `source-acceptance.spec.ts`, `general-ledger-acceptance.spec.ts` and the existing source acceptance/source-binding suites.

## Native general-ledger source inspection

- Application: `Accounting/Intake/GeneralLedgerWorkspace.cs` owns scoped source catalogue/context and selected-source composition; `GeneralLedgerQuery.cs` owns bounded server filters, deterministic pages, complete bounded journals and final authority/source fences.
- API: `Ui/UiEndpoints.GeneralLedger.cs` composes the workspace through trusted-session reads.
- Angular: `features/engagements/general-ledger.ts` and `general-ledger-contracts.ts` own native filter forms, exact values, context decoding, stale-result removal and responsive tables; engagement detail links the native workflow.
- Verification: `GeneralLedgerApiTests`, `AngularGeneralLedgerJourneyTests`, `general-ledger.spec.ts` and existing GL/source acceptance suites. `GeneralLedgerWorkspaceSeed` supplies synthetic read-side records, not worker or professional acceptance.

### Native general-ledger upload

- Application: `Accounting/Intake/GeneralLedgerCsvProfile.cs` defines the explicit bounded source profile; `GeneralLedgerUploadWorkspace.cs` binds exact-file review and scoped recovery to the existing `ClientAccountingService.GeneralLedger.cs` validator/importer.
- API: `Ui/UiEndpoints.GeneralLedger.cs` owns authenticated, antiforgery-protected preview/import/read-only reconciliation endpoints.
- Angular: `features/engagements/gl-upload.ts`, `.html` and `gl-upload-contracts.ts` own native preview, exact values, scoped metadata checkpoints, fresh assent, navigation and unknown-outcome fences.
- Checks: `GeneralLedgerCsvProfileTests`, `GeneralLedgerUploadApiTests`, `AngularGeneralLedgerUploadJourneyTests` and `gl-upload.spec.ts`; `GeneralLedgerUploadSeed` supplies owned synthetic fixtures. Observed execution results belong only to `status.json`.

### Native mapping approval review

- `src/AuditSphereOps.Application/Accounting/MappingApprovalWorkspace.cs`: bounded exact review and current applicability; composes existing serialized approval/validation.
- `src/AuditSphereOps.Api/Ui/UiEndpoints.AccountingDetail.cs`: authorized review and both guarded approval endpoints.
- `src/AuditSphereOps.Ui/src/app/features/accounting/mapping-approval.ts` and `mapping-approval.html`: fresh reviewed assent, metadata-only checkpoints and uncertain-outcome recovery.
- `tests/AuditSphereOps.Api.Tests/MappingApprovalApiTests.cs` and `tests/AuditSphereOps.E2E.Tests/AngularMappingApprovalJourneyTests.cs`: exact review, concurrency, stale/protected scope, independent approval, rollback and native recovery.

### Native mapping batch creation

- `src/AuditSphereOps.Application/Accounting/MappingDraftWorkspace.cs`: bounded editor, complete reviewed preview and receipt reconciliation; `FinancialStatementService.Mapping.cs` owns the serialized new-version transaction and final authority fence.
- `src/AuditSphereOps.Api/Ui/UiEndpoints.MappingDrafts.cs`: trusted-session editor/receipt reads and antiforgery-protected preview/creation commands.
- `src/AuditSphereOps.Ui/src/app/features/accounting/mapping-draft.ts`, `.html`, `.scss` and `mapping-draft-contracts.ts`: explicit focused split editing, selected/pasted batch previews, native keyboard controls, local undo, draft recovery and receipt fencing.
- `src/AuditSphereOps.Ui/src/app/core/tab-drafts.ts`: identity/epoch/TTL-bound convenience fields; pending-reference recovery exposes only request identity/hash after a fresh authorized base read.
- `NativeMappingCreationReceipts` migration: nullable legacy-compatible metadata, scoped parent FK, unique actor/request identity, append-only receipt and allocation guards; rollback refuses retained receipt deletion.
- `MappingDraftApiTests`, `AngularMappingDraftJourneyTests`, `mapping-draft.spec.ts` and `mapping-draft-contracts.spec.ts`: immutable history, exact fractions, idempotency, rollback, keyboard/paste, stale context and native recovery. Observed results live in `status.json`.

## Native statement contribution review

- Application: `Accounting/Intake/StatementReviewWorkspace.cs` owns scoped, revision-bound
  statement/contribution/procedure pages and complete CSV projection; `FinancialStatementDrillDownQuery.cs`
  and `MappedTrialBalanceSource.cs` share existing deterministic statement/allocation arithmetic.
- API: `Ui/UiEndpoints.Statements.cs` composes reads and protected exports.
- Angular: `features/engagements/statements.ts`, `statements.html`, `statement-contracts.ts`
  retain URL context, scoped source/evidence inspection and stale-response fences.
- Verification: `StatementReviewApiTests.cs`, `StatementReviewSeed.cs` and
  `AngularStatementReviewJourneyTests.cs` cover exact basis, scope, export and return navigation.

## Native journal revision lifecycle

- Domain: `Accounting/AdjustmentJournalAction.cs` retains actor/request identity and immutable before/after evidence.
- Application: `Accounting/AdjustmentJournalWorkspace.cs` and `.Commands.cs` own scoped reviews, previews, serialized transitions, receipt reconciliation, history and controlled export; they compose the existing `AdjustmentJournalService`.
- API: `Ui/UiEndpoints.JournalReview.cs` exposes trusted-session reads and antiforgery-protected previews/actions/exports.
- Angular: `features/accounting/journal.ts`, `.html`, `.scss` and `journal-contracts.ts` implement the exact editor, review, tab recovery, uncertain-result fencing and retained timeline.
- Persistence: `AuditSphereDbContext.AdjustmentJournalActions.cs` and `NativeAdjustmentJournalActions` enforce scoped lineage, unique requests and append-only evidence; the line trigger checks both old/new journal states.
- Verification: `JournalReviewApiTests`, `JournalReviewSeed`, `AngularJournalReviewJourneyTests` and `journal.spec.ts`. Results and migration-application boundaries live in `status.json`.

### Native journal creation

- `Accounting/AdjustmentJournalWorkspace.Creation.cs`: exact source context, preview, serialized draft/event creation, prior-revision fencing and receipt lookup; shared exact line validation remains in `.Commands.cs`.
- `Ui/UiEndpoints.JournalReview.cs`: fixed source-bound draft/preview/receipt HTTP contracts; the explicit SPA catalogue owns the source draft route.
- `features/accounting/journal-create.ts`, `.html`, `journal-creation-contracts.ts`: typed purpose/origin fields, exact line review, optional supersession, tab recovery and uncertain-result acknowledgment. `engagements/tb-source.ts` supplies the scoped entry link.
- `NativeJournalCreationEvidence`: extends existing immutable journal action evidence with an explicit no-prior-journal state; rollback preserves retained creation events.
- `JournalCreationApiTests`, `AngularJournalCreationJourneyTests` and `journal-create.spec.ts`: concurrency, original-line retention, precision, prior revision, authority, keyboard and recovery checks. Observed results live in `status.json`.

### Native journal management response

- `Accounting/JournalManagementWorkspace.cs`: fixed client/staff evidence modes, scoped bounded portal queue, exact previews, serialized decisions, final authority, idempotent receipts and unknown-result lookup.
- `Ui/UiEndpoints.JournalManagement.cs`: authenticated client/staff routes and CSRF-protected review/write contracts; explicit SPA route ownership remains in the API catalogue.
- `features/accounting/journal-management.ts`, `.html` and `journal-management-contracts.ts`: exact financial review, fresh assent, staff tab recovery, in-memory client fields and pending-result acknowledgment. `features/portal/journals.ts` supplies the client entry queue.
- `NativeJournalManagementEvidence`: exact current draft insertion, append-only decisions, reviewed-context/line freezing and the existing action constraint extension.
- `JournalManagementApiTests`, `AngularJournalManagementJourneyTests` and `journal-management.spec.ts`: evidence modes, current scope/epoch, stale context, immutable revision, concurrent receipt and browser recovery checks. Executed outcomes live in `status.json`.

### Reviewed native budget preparation

- Domain evidence: `Practice/BudgetPreparation.cs`.
- Application: `Practice/BudgetPreparationWorkspace.cs` composes `PracticeTimeService.ReviseBudgetAsync` inside the guarded publication transaction; approval remains independent.
- API: `Ui/UiEndpoints.BudgetPreparation.cs` provides preview, confirmed preparation and actor-owned request receipt lookup.
- Angular UI: `features/engagements/planning.ts` and `budget-preparation-contracts.ts` provide exact review, tab references and explicit reconciliation.
- Persistence: `AuditSphereDbContext.BudgetPreparations.cs` and `NativeBudgetPreparationReview` retain immutable exact publication evidence and rollback fences.
- Verification: `BudgetPreparationReviewTests`, `BudgetPreparationReviewApiTests`, `AngularBudgetPreparationJourneyTests` and planning component tests.

## Reviewed budget approval receipts

- Domain: `Practice/BudgetApproval.cs` retains immutable actor-owned approval evidence.
- Application: `Practice/BudgetApprovalWorkspace.cs` owns exact review, guarded approval and receipt lookup; `PracticeTimeService` retains independent approval authority.
- API: `Ui/UiEndpoints.BudgetApproval.cs` exposes authenticated preview, confirmation and receipt recovery.
- Angular UI: `features/engagements/planning.ts` and `budget-approval-contracts.ts` bind confirmation and reconcile unknown outcomes without resending writes.
- Persistence: `AuditSphereDbContext.BudgetApprovals.cs` and the native approval migration enforce append-only evidence and deferred approved-budget linkage.
- Verification: `BudgetApprovalReviewTests`, `BudgetApprovalReviewApiTests`, `AngularBudgetApprovalJourneyTests` and planning component tests.

## Reviewed staffing backend receipts

- Domain: `Practice/StaffingChange.cs` records immutable actor-owned local change evidence.
- Application: `Practice/StaffingChangeWorkspace.cs` owns preview, reviewed execution and receipt lookup; `StaffingService.cs` retains rank, certification, grant and session-epoch rules.
- API: `Ui/UiEndpoints.StaffingChange.cs` exposes CSRF-protected preview/execute and current-authorized receipt reads. Native UI adoption remains pending.
- Persistence: `AuditSphereDbContext.StaffingChanges.cs` and native staffing review migration enforce retained evidence and deferred assignment linkage.
- Tests: `StaffingChangeReviewTests.cs` covers concurrent exact replay, immutable receipts, scope, identity and stale context.

## Reviewed prospect-to-client conversion

- Domain receipt: `Practice/ClientConversion.cs`.
- Application: `Practice/ClientConversionWorkspace.cs` owns current authorization, exact previews, stale-context checks, atomic receipts and actor-owned publication-fenced lookup; `PracticeCrmService.ConvertToClientDraftAsync` joins caller transactions and preserves commercial/professional boundaries.
- Persistence: `AuditSphereDbContext.ClientConversions.cs`, migration `NativeClientConversionReview`, append-only and deferred same-firm handover guard with retained-evidence rollback refusal.
- API: `Ui/UiEndpoints.ClientConversion.cs`; Angular: `features/commercial/client-conversion.*`, native proposal link and guarded route.
- Tests: `ClientConversionReviewTests`, `ClientConversionReviewApiTests`, `AngularClientConversionJourneyTests`, shared `ClientConversionReviewSeed`; observed results belong to the execution ledger.

## Agent capability checks

`scripts/agent-check` routes to `scripts/agent/agent-check.py`; the existing catalog
`docs/testing/agent-check-capabilities.json` connects the UI, authorization and
calculator pilots to owning files, invariants, acceptance mappings and synthetic
fixture states. `scripts/agent/agent_check_evidence.py` parses bounded sanitized
failures and compares measured matching cohorts. Default preflight is read-only;
execution is opt-in, and browser execution rebuilds the current UI first. These
tools never write task completion, reset a persistent database or replace release
and external acceptance. See the [capability guide](../testing/auditsphere-testing-guide-agent-capability-checks-current.md).

## Hardened HTTP boundary and OpenAPI contract

- Boundary policies: `Api/HttpBoundary/HttpPolicies.cs` (authenticated-session policy), `Api/HttpBoundary/ApiRateClass.cs` (cost classes with endpoint markers), `Api/HttpBoundary/ApiRateLimiter.cs` (chained global limiter, per-identity partitions, configurable budgets), `Api/HttpBoundary/SecurityHeaderOptions.cs` + `SecurityHeadersMiddleware.cs` (build-derived strict script CSP and global headers), `Api/HttpBoundary/RequestBodyLimitMiddleware.cs` (Kestrel ceiling plus JSON command 413 guard), `Api/HttpBoundary/ApiError.cs` (stable boundary error body).
- Host wiring: `ApiHost.cs`, `ApiHost.HttpBoundary.cs` (rate limiting, request limits, security headers, contract registration); group-level session authorization on `/api/ui` (`Ui/UiEndpoints.cs`) and `/api/setup` (`ApiHost.Installation.cs`); export/upload rate-class markers on `UiEndpoints.{Portfolio,Statements,Packages,JournalReview,AccountingRecords,GeneralLedger}.cs` and `ApiHost.DocumentEndpoints.cs`.
- Contract: `Api/Contracts/ApiContract.cs` (native OpenAPI 3.1 document `auditsphere` with session-cookie scheme, ApiError schema, decimal-as-string rule); committed artifact `contracts/auditsphere-openapi.json`; drift script `scripts/contracts/verify-openapi.sh` wired into CI.
- Angular side: `core/api.ts` keeps command/outcome semantics (no retries, `outcome.unknown`, typed 429 refusal asserted in `core/api.spec.ts`).
- Tests: `HttpBoundarySecurityTests.cs`, `OpenApiContractTests.cs`.
- Authority: [HTTP boundary and contract architecture](auditsphere-architecture-http-boundary-and-contract-current.md); deployment topology in `infra/auditsphere-deployment-iac-runbook.md`.
