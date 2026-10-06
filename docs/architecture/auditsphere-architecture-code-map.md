# AuditSphereOps — Current Code Map







**Status: CURRENT.** Capability → files. Consult this before changing a business capability.



Architecture authority: [`auditsphere-architecture-current-architecture.md`](auditsphere-architecture-current-architecture.md) and



[`AGENTS.md`](../../AGENTS.md). Large services and the DbContext are partial classes split by



capability; public APIs are stable across parts. Verified project state lives only in



`docs/execution/status.json`.







## Native client operational bookkeeping

- Domain: `Domain/Accounting/ClientAccounting.cs`; client-owned journals, lines and review decisions.
- Application: `Application/Accounting/ClientOperationalLedgerWorkspace.cs` and `ClientOperationalGeneralLedgerWorkspace.cs`; accepted bookkeeping service gate, balanced journal preparation, independent posting and posted movement queries.
- Preview: `Application/Accounting/ClientOperationalLedgerWorkspace.Preview.cs`; read-only exact totals and digest covering current journal, period, approved chart and accepted mandate, rechecked inside submission/posting transactions.
- Calculator: `Application/Accounting/ClientOperationalJournalCalculator.cs`; bounded exact native line amounts and balanced totals shared by draft creation and submission/posting validation. Currency-specific rounding policy remains separate work.
- API: `Api/Ui/UiEndpoints.ClientOperationalLedger.cs`; authenticated, scope-checked commands and exact decimal string responses.
- Angular: `Ui/src/app/features/accounting/operational-journals.ts`, composed by the accounting workspace only in native bookkeeping mode.
- Persistence: `Infrastructure/Persistence/AuditSphereDbContext.ClientOperationalLedger.cs`; dedicated operational journal tables and integrity migration. These are separate from firm ledger entries and reporting adjustments.
- Tests: `ClientAccountingTests.OperationalLedger.cs` and `AngularClientOperationalJournalJourneyTests.cs`.

Posted movement reports exclude opening balances and imported GL. Native TB/snapshot adaptation, correction lineage and durable command receipts remain subsequent work; this surface does not establish full bookkeeping completion.

## Accounting setup — profiles, periods, books, charts, taxonomy, capabilities







- Domain: `Domain/Accounting/ClientAccounting.cs`



- Application: `Application/Accounting/ClientAccounting/ClientAccountingService.{Profiles,Periods,Books,OpeningBalances,Restatements,Charts,Taxonomy,Capabilities,Authorization}.cs`



- Persistence: `Infrastructure/Persistence/AuditSphereDbContext.ClientAccounting.cs`, `.ClientAccountingSchedules.cs`



- UI: `Web/Components/Pages/AccountingWorkspace.razor`, `AccountingPeriod.razor`, `PeriodRollforward.razor`, `PeriodRestatements.razor`



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



- API/UI: `Api/Ui/UiEndpoints.GeneralLedger.cs`, `Ui/src/app/features/accounting/gl-completeness*`, `Ui/src/app/features/engagements/general-ledger*`; legacy rollback `AccountingRecords.razor`



- Tests: `ClientAccountingTests.GeneralLedger.cs`, `TrialBalanceCalculatorTests.cs`, `TrialBalanceWorkerTests.cs`, `TrialBalanceXlsxImporterTests.cs`, `SourceAcceptanceAndComparativesTests.cs`, `GeneralLedgerCompletenessApiTests.cs`, `AngularGeneralLedgerCompletenessJourneyTests.cs`







Critical invariants: imports are idempotent and sealed only when complete; GL completeness is



account-exact; source acceptance binds sealed sources and bumps generation.







## Journals, adjustments and re-measurement workpapers







- Application: `Application/Accounting/AdjustmentJournalService.cs`, `AdjustmentPlanService.cs`, `AdjustmentEligibilityQuery.cs`, `AdjustmentPlanWorkspace.cs`, `AdjustmentPlanWorkspace.Commands.cs`, `CurrencyRemeasurementService.cs`



- Persistence: `AuditSphereDbContext.AdjustmentBridge.cs`, `AuditSphereDbContext.AdjustmentPlanActions.cs`; retained native command evidence: `Domain/Accounting/AdjustmentPlanAction.cs`



- UI: `Journals.razor`, `CurrencyRemeasurement.razor`



- Tests: `AccountingRecordsApprovalTests.cs` (journal submit/return/resubmit, duplicate reversal), `ClientAccountingTests.Currency.cs`







## Reconciliations, valuations, specialists, journal risk and accounting evidence







- Application: `Application/Accounting/Analysis/AccountingAnalysisService.{Reconciliation,Valuations,AnalyticalReview,JournalRisk,Evidence,Authorization}.cs`; native scoped inspection: `Application/Accounting/ReconciliationWorkspaceQuery.cs`, `Application/Accounting/AccountingAnalysisReviewQuery{,.Retained}.cs`; native reviewed commands: `Application/Accounting/AccountingEvidenceWorkspace{,.Procedures}.cs`, `Application/Accounting/ValuationPreparationWorkspace.cs`; retained receipts: `Domain/Accounting/AccountingEvidenceAction.cs`, `Domain/Accounting/ValuationPreparation.cs` and their matching Infrastructure persistence configurations



- UI: `AccountingEvidence.razor`, `Finding.razor`; native queue/detail: `src/AuditSphereOps.Ui/src/app/features/accounting/evidence.ts`, `src/AuditSphereOps.Ui/src/app/features/accounting/reconciliation.ts`, `src/AuditSphereOps.Ui/src/app/features/accounting/analysis-review.ts`; reviewed action/preparation forms and receipt recovery: `src/AuditSphereOps.Ui/src/app/features/accounting/evidence-actions{.ts,.html}`, `src/AuditSphereOps.Ui/src/app/features/accounting/evidence-action-contracts.ts`, `src/AuditSphereOps.Ui/src/app/features/accounting/valuation-preparation{.ts,.html}`, `src/AuditSphereOps.Ui/src/app/features/accounting/valuation-preparation-contracts.ts`



- Tests: `ClientAccountingTests.{Reconciliations,Analysis}.cs`, `SourceAcceptanceAndComparativesTests.cs`, `ValuationPreparationApiTests.cs`, `AngularValuationPreparationJourneyTests.cs`







Critical invariants: reconciliation approval stales when the source digest changes; evidence is



blocked when the client generation changes; journal risk is deterministic, never autonomous.







## Financial statements, mapping and packages







- Domain: `Domain/Accounting/` (mapping versions/allocations, financial packages, artifacts)



- Application: `Application/Accounting/FinancialStatements/FinancialStatementService.{Mapping,Package,Render,Authorization}.cs`, `FinancialPackageReviewService.cs`, `FinancialPackageBuildHandler.cs`, `FinancialPackageRenderHandler.cs`, `FinancialPackageOfficeRenderer.cs`, `PackageSealService.cs`, `PackageManifestQuery.cs`, `StatementLayoutService.cs`, `FinancialStatementCalculator.cs`



- Persistence: `AuditSphereDbContext.FinancialStatements.cs`



- UI: `Mapping.razor`, `FinancialPackage.razor`, `FinancialPackageReviews.razor`, `ClientFinancialPackage.razor`, `Release.razor`



- Tests: `ClientAccountingTests.FinancialPackages.cs`







Critical invariants: mapping allocations are append-only; review decisions are stage-bound and



immutable; artifacts are exact-byte sealed; release candidates bind the current reviewed package.







## Currency translation and foreign operations







- Domain: `Domain/Accounting/` (exchange rate sets, translation policies/results)



- Application: `Application/Accounting/CurrencyTranslationService.cs`, `CurrencyOperationCalculators.cs` (`LineTranslationCalculator`, `CurrencyTranslationCalculator`), `TranslationInputResolution.cs`



- Persistence: `AuditSphereDbContext.Consolidation.cs` (rate sets, policies, translation results)



- UI: `Consolidation.razor` (TranslationBridge area), `CurrencyRemeasurement.razor`



- Tests: `tests/.../LineTranslationTests.cs`, `ClientAccountingTests/ClientAccountingTests.Currency.cs`







Critical invariants: a missing/zero/negative required rate purpose fails closed and produces no



approvable result; same currency is identity rate 1 with no rate observation; per-line rate



purposes follow the calculation method (`COMPONENT_TRANSLATION_V2`), never the reserve amount;



classification comes from declared sections, approved mappings or documented code rules — an



unclassified line is an explicit unmapped issue, never assumed ASSETS.







## Consolidation — perimeter, components, matching, journals, runs, reports







- Domain: `Domain/Accounting/ClientAccounting.cs` (`ClientGroup`, `ConsolidationScopeVersion`, `ConsolidationComponent`, `ConsolidationRun`, `ExchangeRateSetVersion`, `TranslationPolicyVersion`, `TranslationResult`)



- Application: `Application/Accounting/Consolidation/ConsolidationService.{Groups,Ownership,Scopes,Components,ExternalPacks,Intercompany,Journals,Advanced,Runs,Reports,Authorization}.cs`, `ConsolidationCalculator.cs`, `AdvancedConsolidationCalculator.cs`, `AdvancedConsolidationExecutionCalculator.cs`, `ConsolidationQuery.cs`



- Persistence: `AuditSphereDbContext.Consolidation.cs`



- UI: `Consolidation.razor`, `AdvancedConsolidationWorkflow.razor`



- Tests: `ClientAccountingTests/ClientAccountingTests.{Consolidation,AdvancedConsolidation,ExternalComponents,Currency}.cs`, `ProfilesChartsAndConsolidationTests.cs`







Critical invariants: the group build never mutates component client books; the cumulative



translation reserve line identity is derived from the immutable translation snapshot (stable



replay/approval/readback); the group-report export/archive leg is a declared M37/M38 boundary



(`docs/task_breakdown/modules/auditsphere-r2r-module-26-consolidation-contract.md`).







## Audit planning and fieldwork







- Domain: `Domain/Audit/`



- Application: `Application/Audit/AuditPlanningService.cs`, `Application/Audit/Fieldwork/AuditFieldworkService.{Schedules,BankReconciliations,Selections,ItemTests,Confirmations,AreaAssessments,Differences,Completion,Authorization}.cs`



- Persistence: `AuditSphereDbContext.Audit.cs`, `.Fieldwork.cs`



- UI: `AuditPlan.razor`, `AuditFieldwork.razor`, `AuditPopulation.razor`, `AuditProgramLibraryPage.razor`



- Tests: `tests/.../AuditFieldwork*` and audit sections of `SourceAcceptanceAndComparativesTests.cs`







## Reviews, completion, findings and workpapers







- Application: `Application/Reviews/`, `Application/Completion/`



- Persistence: `AuditSphereDbContext.Reviews.cs`, `.Completion.cs`, `.ScopedEvidence.cs`



- UI: `ReviewPoint.razor`, `Completion.razor`, `Workpaper.razor`



- Tests: `tests/.../` review/completion suites, `AccountingRecordsApprovalTests.cs`







## Documents, PBC and records







- Application: `Application/Documents/PbcService.cs`, `Application/Records/RecordsArchiveService.cs`



- Persistence: `AuditSphereDbContext.Documents.cs`, `.Pbc.cs`



- UI: `PbcRequests.razor`, `ClientPbcRequest.razor`, `RecordsArchive.razor`, `ClientPortal.razor`



- Tests: `tests/.../` document/PBC suites







Critical invariants: uploaded signed-document evidence is preserved exactly with SHA-256



identities; provider acceptance (Purview/eSignature) is never claimed.







## Practice — CRM, time, billing, firm ledger







- Domain: `Domain/Practice/`



- Application: `Application/Practice/PracticeCrmService.cs` (CRM / proposals), `PracticeLeadQuery.cs` (current-access lead list), `WorkspaceQuery.Client.cs` (client-level master metadata, complete scoped counts and bounded contact/engagement pages), `ClientContactCreationWorkspace.cs` (reviewed contact creation and actor-owned immutable receipt recovery), `PracticeTimeService.cs` (time / budgets), `BillingService.cs` (billing), `BillingInvoiceWorkspaceQuery.cs` (invoice workspace), `InvoicePaymentTermsService.cs` (evidence-backed maker/checker due-date revisions), `ReceiptAllocationReversalService.cs` (reviewed payment unapply), `FirmReceivablesAgingQuery.cs` (firm-fee ageing and CSV export), `LedgerService.cs` and `FirmFinanceQuery.cs` (firm financial ledger)



- Persistence: `AuditSphereDbContext.Practice.cs`



- UI: legacy `Leads.razor`, `Portfolio.razor`, `ClientDetail.razor`, `Finance.razor`, `InvoiceDetail.razor`, `PracticeTime.razor`; native Angular invoice actions and term/reversal review in `AuditSphereOps.Ui/src/app/features/finance/invoice.ts`, plus the firm-fee ageing report/export in `AuditSphereOps.Ui/src/app/features/finance/receivables-aging.ts`, through `Api/UiEndpoints.Finance.cs`

- Verification: `BillingWorkspaceApiTests` checks current authorization, CSRF and exact receipt/allocation/credit balances; `FirmReceivablesAgingQueryTests` and `FirmReceivablesAgingApiTests` cover historical balances, terms, reversals, scope denial and protected export; `AngularBillingWorkspaceJourneyTests` exercises the native flow and lost-response review.







Boundary: this is the firm's own books (`Practice/FirmLedger`); it never writes client



accounting or consolidation workspaces.







## Commercial quotation, documents and the agreed-fee cycle

- Domain: `Domain/Practice/Commercial.cs` — `QuotationVersion`, `CommercialApprovalRule`, `QuotationApproval`, `FirmCommercialProfile`, `CommercialDocument`, `EngagementFeeAgreement`, `FeeMilestone`, `CommercialNotification`
- Application: `Application/Practice/QuotationCalculator.cs` (pure fee model: rate × hours → complexity → risk premium → discount), `CommercialApprovalMatrix.cs` (pure rule evaluation with a documented fail-safe default), `QuotationService.cs` (versioning, submit, matrix approvals, rule administration, approved-rate options), `CommercialDocumentRenderer.cs` + `CommercialDocumentService.cs` (branded Quotation, Engagement Letter and receipt as DOCX with template/profile version and SHA-256), `FeeAgreementService.cs` (50% advance and balance milestones over the existing `BillingService` review/post/receipt path), `CommercialMailDeliveryHandler.cs` (isolated `mail` worker delivery of the receipt email). `PracticeCrmService.ApproveProposalAsync` gates internal review on an approved, fee-matching quotation.
- Persistence: `AuditSphereDbContext.Commercial.cs`, migration `CommercialQuotationsAndFeeAgreements` (check constraints and append-only/field-restricted triggers)
- UI: `Components/Commercial/QuotationWorkbench.razor`, `FeeAgreementPanel.razor`, `ProposalWorkflowPanel.razor` (hosted by `ProposalDetail.razor`), `Pages/CommercialSettings.razor`; authorized download endpoint `/api/commercial/documents/{id}/download` in `Web/Program.cs`
- Tests: `QuotationCalculatorTests.cs`, `CommercialWorkflowTests.cs`, `tests/.../CommercialJourneyTests.cs`

## Acceptance paths, activation and engagement workspace provisioning

- Assessment command publication: `AcceptanceCommandAuthority.cs` establishes firm/client/actor lock order and post-wait authority checks. `AcceptanceChecklistService.cs` and `AcceptanceDecisionService.cs` join a reviewed caller transaction without committing it; the caller must roll back on failure. `AcceptanceChecklistTests` covers rollback and actor revocation during each command lock wait. The immutable request receipt owner and typed forms remain separate migration work.

- Native assessment detail: `Application/Acceptance/AssessmentWorkspaceQuery.cs` publishes a current-authority, client-serialized profile, exact selected decision and persisted progress projection through `Api/Ui/UiEndpoints.Routes.cs`. `AssessmentRouteQuery.cs` preserves legacy decision selection. Angular `features/acceptance/checklist.ts` distinguishes historical and engagement-specific records from the editable current client evaluation, fences late replies and binds Partner assent to the reviewed intent. Tests: `AcceptanceChecklistTests`, `AssessmentWorkspaceApiTests`, `AngularAssessmentParityJourneyTests` and `assessment-workspace.spec.ts`.
- Reviewed assessment backend: `Application/Acceptance/AssessmentCommandWorkspace.cs` composes the five existing commands with exact preview, explicit assent, actor-owned request receipts and serialized recovery through `Api/Ui/UiEndpoints.Acceptance.cs`. `Domain/Acceptance/AssessmentCommandReceipt.cs`, `AuditSphereDbContext.AssessmentReceipts.cs` and the receipt migration own append-only persistence, result/publication checks and retained-evidence rollback refusal. Tests: `AcceptanceChecklistTests.Receipts.cs` and `AssessmentCommandReceiptApiTests`. Angular `assessment-command-editor.ts` owns typed Signal Forms; `assessment-command-contracts.ts` validates exact preview/receipt contracts. `checklist.ts` binds fresh assent, retains references only, fences unknown outcomes and composes authorized receipt recovery. `assessment-navigation-dialog.ts` protects unsaved notes. Tests: `assessment-command.spec.ts` and both host modes of `AngularAssessmentParityJourneyTests`. Broader assessment parity remains pending.

- Native reviewed blocked creation: `Application/Acceptance/EngagementCreationWorkspace.cs` composes `EngagementLifecycleService.Creation.cs` inside an owned transaction; `Api/Ui/UiEndpoints.EngagementCreation.cs` owns exact client context/preview/confirmation/receipt routes. Angular `features/clients/engagement-create*` owns Signal Forms review, bounded tab fields and unknown-result recovery. `Domain/Engagements/EngagementCreation.cs` and `NativeEngagementCreationReview` preserve immutable actor-owned request evidence and a deferred exact blocked-publication guard. Tests: `EngagementCreationReviewTests`, `EngagementCreationReviewApiTests`, `AngularEngagementCreationJourneyTests`, `engagement-create.spec.ts`. Migration and observed acceptance boundaries live only in `status.json`.
- Native reviewed activation: `Application/Acceptance/EngagementActivationWorkspace.cs` composes `EngagementLifecycleService.cs`; `Api/Ui/UiEndpoints.EngagementActivation.cs` owns exact Partner context/preview/confirmation/receipt routes; Angular `features/engagements/activation*` owns Signal Forms assent, tab request recovery and lifetime fences. `NativeEngagementActivationReview` adds nullable request metadata to existing immutable evidence, a unique request identity and a deferred publication guard. Tests: `EngagementActivationReviewTests`, `EngagementActivationReviewApiTests`, `AngularEngagementActivationJourneyTests` and `activation.spec.ts`. Observed execution and migration boundaries live only in `status.json`.

- Domain: `Domain/Acceptance/Acceptance.cs` (paths, evidence reference, generation, adverse-answer metadata), `Domain/Engagements/Engagements.cs` (`EngagementActivation`)
- Application: `Application/Acceptance/AcceptanceRules.cs` (pure gate rules), `AcceptanceChecklistService.cs` (path-derived checklist), `AcceptanceDecisionService.cs` (records path and prior decision), `EngagementLifecycleService.Creation.cs` (client-serialized blocked draft creation with current authority checks), `EngagementLifecycleService.cs` (Partner-only activation); `Application/Documents/EngagementWorkspaceProvisioning.cs` (`ProvisionEngagementWorkspace.v1` handler and discovery in the isolated `pbc` live group; exact `/Client/Year/01–05` tree when the approved client template has no `engagements` node, otherwise the legacy layout; binding and capability evidence through `PbcRepositoryProvisioning.ApplyEngagementBindingAsync`); `Microsoft365ConfigurationService.SteManifest` and the Setup page "Load STE layout" action
- Persistence: migration `AcceptancePathsEvidenceAndActivation`
- UI: `Components/Acceptance/{AcceptanceChecklistPanel,EngagementActivationPanel,EngagementCreatePanel}.razor`
- Client portal onboarding: `Domain/Documents/ClientPortal.cs` (`ClientPortalFirstSignIn`, `PbcRequestDelegation`, `ClientPortalIntent`), `Application/Documents/ClientPortalService.cs` (identity-path-derived first-sign-in gate used by `PbcService` uploads, primary-contact delegation, participant request query, portal intent view), `PracticeCrmService.RecordPortalIntentAsync` (conversion trigger) and `EngagementLifecycleService.ActivateAsync` (ready to invite); persistence `AuditSphereDbContext.ClientPortal.cs`, migration `ClientPortalOnboardingAndDelegation`; UI `Pages/ClientPortal.razor` (first sign-in), `Pages/ClientPbcRequest.razor` (drop zone, in-browser SHA-256 via `wwwroot/pbc-upload.js`, progress, delegation), `Pages/ClientDetail.razor` (portal intent)
- Tests: `AcceptanceChecklistTests.cs`, `EngagementWorkspaceProvisioningTests.cs`, `ClientPortalOnboardingTests.cs`, `tests/.../AcceptanceJourneyTests.cs`, `tests/.../ClientPortalOnboardingJourneyTests.cs`, `tests/.../PbcUploadJourneyTests.cs`

## Resource planning, staffing, materiality engine and risk routing

- Domain: `Domain/Practice/ResourcePlanning.cs` (`StaffingLevels` with explicit role mapping, `EngagementStaffAssignment`, `StaffProfile`, `StaffCertification`, `StaffAvailability`, `StaffAllocation`, `BudgetPhases`), phase/risk-area on `WorkTask`, `TimeEntry`, `BudgetLine`; `Domain/Audit/MaterialityAndRiskBands.cs` (`MaterialityCalculation`, `RiskBandAssessment`, `RiskPartnerClearance`, `RiskOwnerAssignment`)
- Application: `Practice/StaffingService.cs`, `Practice/ResourcePlanningService.cs` + pure `ResourceGridCalculator.cs`, `PracticeTimeService.GetBudgetBreakdownAsync`; `Audit/MaterialityCalculator.cs` (pure PM/TE/SAD and `RiskBandRules`), `Audit/MaterialityEngineService.cs` (benchmark derivation from the current approved mapping, staleness used by `ApproveMaterialityAssessmentAsync`, SAD evaluation and completion), `Audit/RiskBandService.cs` (bands, owner levels, Partner review, completion blockers)
- Persistence: `AuditSphereDbContext.ResourcePlanning.cs`, migration `ResourcePlanningMaterialityEngineAndRiskBands` (band rule check, significance trigger, append-only evidence triggers, phase checks)
- UI: `Components/Planning/{EngagementStaffingPanel,EngagementBudgetPanel,MaterialityEnginePanel,RiskRoutingPanel}.razor` (hosted by `EngagementDetail.razor` and `AuditPlan.razor`), `Pages/ResourcePlanning.razor` (`/app/practice/resources`)
- Angular: `features/practice/resources.ts` and `resources.html` compose the API planning workspace; `resource-editor.ts` owns typed Signal Forms, persisted-profile editing and bounded validation. `resource-navigation-dialog.ts` protects memory-only edits. `planning-command-contracts.ts` decodes exact previews and receipts. `ResourcePlanningCommandWorkspace.cs` and `ResourcePlanningService.Authority.cs` enforce reviewed publication, current authorization and replay/recovery. `ResourcePlanningReceipt.cs`, `AuditSphereDbContext.ResourcePlanningReceipts.cs` and migration `NativeResourcePlanningReceipts` retain atomic immutable evidence. Tests include `PlanningResourcesAndMaterialityTests.Receipts.cs` and `ResourcePlanningReceiptApiTests.cs`.
- Tests: `PlanningResourcesAndMaterialityTests.cs`, `tests/.../PlanningAndResourcesJourneyTests.cs`, `AngularResourcePlanningJourneyTests.cs`, `features/practice/resources.spec.ts`

## Trial-balance intake and fieldwork connections

- Application intake: `Accounting/Intake/MultiPeriodTrialBalanceService.cs` (PeriodCode split over `TrialBalanceXlsxImporter.ReadTable` or CSV, per-period import through `TrialBalanceImportService`), `MappingMemoryService.cs`, `TrialBalanceCurrencyReviewQuery.cs`, `FinancialStatementDrillDownQuery.cs`, shared `MappedTrialBalanceSource.cs` (also used by the materiality engine)
- Native engagement profile/history: `Application/Practice/WorkspaceQuery.Engagement.cs`, `Api/Ui/UiEndpoints.cs`, `Ui/src/app/features/engagements/engagement*` and `planning.ts`; metadata, complete hold totals, bounded pages and separate client navigation authority. The three accounting preparation links derive from `CanPrepareAccounting`, using the same `AccountingPreparationAuthorization` role/scope gate as their protected endpoints. `EngagementProfileWorkspaceTests`, `EngagementProfileWorkspaceApiTests`, `AngularEngagementProfileJourneyTests` and `engagement-workspace.spec.ts` cover scope, history, capability visibility, direct-route refusal and UI lifetime boundaries. Complete planning form parity remains open.

- Native intake currency review: `Ui/UiEndpoints.Intake.cs` composes `TrialBalanceCurrencyReviewQuery`; Angular `features/engagements/currency-review.ts` renders scope, provenance and exact comparison; `CurrencyReviewApiTests`, `AngularCurrencyReviewJourneyTests` and `currency-review.spec.ts` exercise scope/identity/read fences and native presentation.
- Domain: `Domain/Audit/FieldworkConnections.cs` (`AuditSamplingRun`, `ProcedureEvidenceLink`, `PhysicalEvidenceItem`/`Movement`, `ProcedurePhysicalLink`, `AdHocProcedureInsertion`/`Revision`)
- Application fieldwork: `Audit/Fieldwork/AuditFieldworkService.Connections.cs` (sampling run and re-performance, evidence picker, physical index, ad hoc steps, analytical-review and going-concern completion blockers wired into `EvaluateCompletionAsync`)
- Persistence: `AuditSphereDbContext.FieldworkConnections.cs`, migration `FieldworkConnectionsSamplingEvidenceAndPhysicalIndex`
- UI: `Pages/TrialBalanceIntake.razor` (`/app/engagements/{id}/tb-intake`), `Pages/StatementDrillDown.razor` (`/app/engagements/{id}/statements`), `Components/Fieldwork/FieldworkToolsPanel.razor` (hosted by `AuditFieldwork.razor`)
- Tests: `FieldworkConnectionsTests.cs`, `tests/.../FieldworkConnectionsJourneyTests.cs`
- Attribute-strata sampling: `Domain/Audit/AuditSamplingEngine.cs` validates reviewer-selected account/currency/direction/month attributes and produces deterministic strata; `Application/Audit/AuditSamplingService.cs` previews and persists the exact selection with source digest, ordering policy, fields and engine version; `Audit/Fieldwork/AuditFieldworkService.Connections.cs` commits the selection and run atomically and fences replay against source changes. `Application/Audit/AuditFieldworkWorkspaceQuery.cs` publishes authorized review/execution capabilities; `Api/Ui/UiEndpoints.Fieldwork.cs` exposes preview, run, sample-set and item-review commands. Angular `features/audit/fieldwork.ts` renders selected attributes, exact preview rows/strata, explicit sample review, item-test evidence and a separate reviewer decision. Migration `AttributeStrataSamplingPreview` retains the added provenance.
- Sampling verification: `AuditSamplingEngineTests.cs`, `FieldworkConnectionsTests.cs`, `AuditSamplingSelectionIsolationTests.cs`, `AngularAttributeSamplingExecutionJourneyTests.cs` and `features/audit/fieldwork.spec.ts` cover engine validation, scoped persistence, independent review, execution, item-level evidence and second-person review. The journey does not assert statistical sufficiency; wider completion-story acceptance remains in `docs/execution/status.json`.

## Review notes, completion deliverables and file freeze

- Domain: `Domain/Completion/AuditDeliverables.cs` (`ProcedureReviewNote`/`Event`, `AuditDeliverable`, `PartnerCompletionClearance`, `AuditOpinionDecision`, `SignatureSpecimen`/`Application`, `ClientDeliverableReview`/`Comment`), `ConfirmationCriticality` and `AuditConfirmationCase.DispatchedAt`; `Domain/Records/FileFreeze.cs` (`EngagementFileFreeze`, `FileFreezeAmendment`, `FrozenAccessAttempt`, `DocumentLock`)
- Application: `Audit/ReviewNotesService.cs` (anchored notes; open-note and staffing-hierarchy checks inside `AuditProgramService.ReviewResultAsync`), `Completion/AuditDeliverableRenderer.cs` (DOCX with PNG embedding), `Completion/AuditDeliverableService.cs` (facts digest and staleness, SRM, Partner clearance, opinions, report trio, holding letter, signing, client review loop, confirmations dashboard, downloads), `Records/FileFreezeService.cs` (schedule, `FileFreezeHandler`/`FileFreezeDiscovery` on an injected clock, amendments, write guard), `Records/EngagementActivityQuery.cs` (activity trail, document locks)
- Persistence: `AuditSphereDbContext.AuditDeliverables.cs`, `AuditSphereDbContext.FileFreeze.cs`; migrations `ReviewNotesDeliverablesOpinionsAndSignatures`, `ScheduledFileFreezeAndDocumentLocks`
- Web/Worker: `Components/Completion/{CompletionDeliverablesPanel,FileRecordsPanel}.razor` (hosted by `Completion.razor`), review-notes tab in `FieldworkToolsPanel.razor`, portal review in `ClientPortal.razor`, `/api/deliverables/{id}/download` in `Web/Program.cs`; freeze handler and discovery registered in the general worker
- Tests: `AuditDeliverablesTests.cs`, `tests/.../CompletionDeliverablesJourneyTests.cs`

## Technical library, practice analytics and firm books

- Domain: `Domain/Practice/FirmOperations.cs` (`TechnicalLibraryDocument`/`Version`, `StaffCostRate`, `FirmExpense`)
- Application: `Practice/FirmOperationsServices.cs` (`TechnicalLibraryService` — also feeding `GlobalSearchQuery`; `PracticeAnalyticsQuery`; `FirmExpenseService` over `LedgerService` journals and the firm trial balance)
- Persistence: `AuditSphereDbContext.FirmOperations.cs`, migration `TechnicalLibraryAnalyticsAndFirmExpenses` (published-version immutability, append-only cost rates)
- UI: `Pages/TechnicalLibrary.razor` (`/app/library`, `/app/library/{id}`), `Pages/PracticeAnalytics.razor` (`/app/practice/analytics`), `Pages/FirmBooks.razor` (`/app/finance/books`)
- Tests: `FirmOperationsTests.cs`, `tests/.../FirmOperationsJourneyTests.cs`

## Microsoft 365 setup, security and administration







- Application: `Application/Microsoft365/Microsoft365ConfigurationService.cs`, `SelectedResourceAdministration.cs` (bounded administrator projection, reviewed draft edits and activation composition), `SelectedSiteBoundaryVerificationService.cs` (trusted exact-revision positive/negative resource evidence), `TenantConsentService.cs`, `TenantConnectionQuery.cs`, `DirectoryDiscoveryService.cs`, `DirectoryUserBindingService.cs`, `DirectoryCapabilityVerificationService.cs`, `TenantAdministrationProviders.cs` (provider interfaces + permission matrix), `TenantCapabilityService.cs`, `DirectoryProvisioningService.cs` (user creation, guest invitation, reconciliation), `ManagedGroupService.cs`, `AdministrationOverviewQuery.cs`; `Application/Security/` (`RoleAssignmentService.cs`, `RoleGrantExpiry.cs`, `UserAccessWorkspaceQuery.cs`)



- Persistence: `AuditSphereDbContext.Security.cs`, `.Microsoft365.cs`; dedicated certificate and Graph directory reader, `GraphCapabilityCredential.cs`, `GraphTenantAdministrationProviders.cs`, `GraphTenantConsentVerifier.cs` and the Development/Test-only `SimulatedMicrosoftTenant.cs` in `Infrastructure/Providers/`



- API/Angular: `Api/Ui/UiEndpoints.Microsoft365.cs`, `.TenantOperations.cs`, `.SelectedResources.cs`; `Ui/src/app/features/admin/tenant.ts`, `sharepoint.ts`, `provisioning.ts`, `groups.ts`, `operation-recovery.ts`; API owns consent/authentication callback composition. Legacy presentation: `Microsoft365Setup.razor`, `TenantConnection.razor`, `Administration.razor` and `Components/Administration/*` remain the parity/rollback reference until retirement acceptance.



- Tests: `tests/.../` Microsoft 365 and security suites







## Staff navigation search

- Application: `Application/Search/GlobalSearchQuery.cs` — bounded search over clients, engagements, PBC requests, leads, invoices and staff pages. Candidates are prefiltered by the actor's grants for each result kind, then every hit is re-authorized with the decision its destination route applies (`AuthorizationDecision`, `BillingService.CanOpenInvoiceAsync`). Not a document, evidence or email search.
- UI: `Components/Layout/GlobalSearch.razor` in the staff top bar (`MainLayout.razor`); `wwwroot/search-shortcut.js` (`/` shortcut outside editable controls and dialogs); styles in `wwwroot/enterprise-ui.css`.
- Tests: `tests/AuditSphereOps.Domain.Tests/GlobalSearchQueryTests.cs`, `tests/AuditSphereOps.E2E.Tests/GlobalSearchJourneyTests.cs`

## Operations — durable work, worker host







- Application: `Application/Operations/`



- Persistence: `AuditSphereDbContext.Operations.cs`



- UI: `Operations.razor`



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

- Application: `Documents/ClientSharePointSites.cs` owns standing administrator authorization, rollout cutoff, durable operation lifecycle, immutable identity decisions and separate site/member projections.
- Domain: `Microsoft365/ClientSharePointSite.cs`; EF mapping: `AuditSphereDbContext.ClientSharePointSites.cs`.
- Infrastructure: `Providers/SharePointClientSiteProvider.cs`; isolated Acceptance `client-sites` worker. The `pbc` worker stays Sites.Selected.
- Web: `Administration/ClientSharePointSitesPanel.razor`, with explicit site-wide Full Control warning in `Planning/EngagementStaffingPanel.razor`.
- Tests: `ClientSharePointSitesTests`, `SharePointClientSiteProviderTests`, `ClientSharePointSitesJourneyTests`.
- Permission and configuration authority: [client-site decision](auditsphere-client-sharepoint-sites-current.md).
- Requirement coverage and unclosed gaps: [STE coverage](../execution/auditsphere-ste-specification-coverage-current.md).

### STE reporting and commercial extensions

- `Practice/CommercialDocumentService.Tender.cs` assembles reviewed five-chapter proposals; `CommercialSettings.razor` versions firm chapters and `QuotationWorkbench.razor` captures the assigned team CVs, timeline and explicit review.
- `Audit/AuditProgramService.cs` serializes the final review transition; `Completion/AuditDeliverableService.cs` automatically compiles the SRM and preserves separate Partner clearance/opinion/signing decisions.
- `Completion/AuditDeliverableRenderer.cs` renders the signed report PDF with a validated registered PNG specimen. The renderer also embeds the exact versioned approved firm-seal image; neither PNG image asserts certificate-backed signing.
- `Practice/ContractContributionCalculator.cs` is the pure contracted-fee-minus-standard-time-value calculator; `FirmOperationsServices.cs` and `PracticeAnalytics.razor` expose it separately from actual staff-cost metrics.
- Verification sources: `CommercialWorkflowTests`, `AuditDeliverablesTests`, `ContractContributionCalculatorTests` and their commercial/completion journeys. Remaining specification gaps are recorded in the execution coverage matrix.

### STE signed representations, five-part bundles and standing invoice drafts

- Domain: `Completion/DeliverableAssembly.cs`; nullable immutable commercial proof references in `Practice/Commercial.cs` and the approved FSLI identity in `Completion/AuditDeliverables.cs`.
- Application: `Completion/AuditDeliverableService.{Representations,Bundle,Seal,OpinionAreas}.cs`; `Practice/CommercialDocumentService.cs` owns dual-key letters; `Practice/AutomaticFeeInvoices.cs` owns standing-policy discovery and LOCAL draft operations; `Documents/ClientPortalService.cs` owns commercial portal readiness and release upload fences.
- EF: `AuditSphereDbContext.AuditDeliverables.cs`, `.Commercial.cs`; `SteDeliverableAssembly` adds scoped proof foreign keys, append-only triggers, exact manifest text and the engagement-letter insertion guard.
- Web: `CompletionDeliverablesPanel.razor`, `ClientPortal.razor`, `QuotationWorkbench.razor`; scope-checked scan and ZIP endpoints in `Web/Program.cs`. Worker policy defaults are in `Worker/appsettings.json`.
- Tests: `AuditDeliverablesTests.Assembly.cs`, `CompletionBundleFixture.cs`, `CommercialWorkflowTests` and `CompletionDeliverablesJourneyTests`.

## Protected open-session presentation

- Web: `Components/Shared/SessionAccessBoundary.razor`, composed by both staff and client layouts. Reuses `CurrentActorResolver` and `TrustedActorResolver` for authenticated immutable identity, disabled state, expiry and session epoch checks. Checks every five seconds while connected; verification failure removes the protected component subtree and exposes a safe sign-in message. This does not replace Application command/query authorization.
- Verification: `PassiveSessionRevocationJourneyTests` covers both shells, disabled identities and stale epochs without navigation or clicks.
- Systematic sampling: `Domain/Audit/AuditSamplingEngine.cs` adds seeded equal-row-spacing selection, including zero-value rows. `AuditFieldworkService.Connections.cs` stores order-bound source identity and a distinct engine version; `FieldworkToolsPanel.razor` supplies sample size/seed and the existing immutable calculation log. Migration `SystematicAuditSampling` extends the allowed-method constraint. Existing sampling engines and stored source digests retain their contracts.

## API backend and Angular presentation migration

- Application: `Practice/PortfolioQuery{,.Scope,.Workspace}.cs` (bounded current-grant clients, summary, recent records and formula-safe scoped export under one rechecked scope snapshot).
- API host: `src/AuditSphereOps.Api/ApiHost.{Authentication,AuthenticationEndpoints,DocumentEndpoints,Persistence,Providers,Health,Observability}.cs`; `Authentication/TrustedActorResolver.cs`, consent composition and production Data Protection.
- HTTP contracts: `src/AuditSphereOps.Api/Ui/UiEndpoints.*.cs` (trusted session, CSRF-protected commands, exact financial strings and explicit SPA routes).
- Route cutover: `Api/Ui/AngularRouteOwnership.cs`, `UiEndpoints.cs`, authentication/setup/consent composition and Angular `core/navigation.ts` preserve deployment-owned canonical or preview bases and optional retained fingerprinted assets; `CanonicalAngularHostTests`, `AngularNavigationJourneyTests` and `AngularPortfolioJourneyTests` cover local ownership, rollback and protected session boundaries.
- Legacy Web: `Program.cs` supplies Razor/MudBlazor presentation to the shared API host; `Authentication/CurrentActorResolver.cs` supplies circuit resolution.
- Client portal queries: `Application/Documents/ClientPortalWorkspaceQuery.cs`, `ClientPortalReviewQuery.cs`, `ClientPortalReviewCommands.cs`.
- Administration: `Application/Microsoft365/TenantAdministrationWorkspaceQuery.cs` (safe saved tenant label and mail/records draft state projection, separate from verified capabilities), existing tenant/directory providers, and `Security/RoleAssignmentReviewDigest.cs` for current-access review fencing.
- Native portal/admin: `src/AuditSphereOps.Ui/src/app/features/{portal,admin}/`; standalone API contract tests and Angular browser journeys.
- Angular: `src/AuditSphereOps.Ui/src/app/core/session.ts` and `features/portfolio/portfolio.ts`.
- Responsive shell: `Ui/src/app/app.{ts,html,scss}` and `core/workspace-navigation{,-dialog}.ts`; `app-navigation.spec.ts` and `AngularShellNavigationJourneyTests.cs` cover owner/load fencing, keyboard focus, resize, revocation and client navigation isolation. `AngularRoutingContractTests.cs` checks legacy/native route disposition.
- Portfolio API/presentation: `Api/Ui/UiEndpoints.Portfolio.cs`, `Ui/src/app/features/portfolio/portfolio{,-contracts,.html,.scss}` and identity-owned return parameters in `features/clients/client.ts`.
- Tests: `PortfolioQueryTests`, `PortfolioWorkspaceTests`, `PortfolioWorkspaceApiTests`, `UiContractTests`, `AngularPortfolioJourneyTests`, Angular component/decoder tests. `PortfolioWorkspaceSeed` supplies synthetic read-side records without professional release acceptance.
- Authority: [migration ownership and remaining scope](auditsphere-angular-migration-current.md).

### Angular workspace administration

- Application: `Documents/WorkspaceProvisioningAdministration.cs` supplies bounded administrator workspace pages, exact target/resource/template review fingerprints and reviewed calls to existing `PbcRepositoryProvisioningService`. `ClientSharePointSiteQuery` projects separate dedicated-site and membership health.
- API: `Ui/UiEndpoints.WorkspaceProvisioning.cs` exposes authenticated bounded reads and CSRF-protected client/engagement folder commands. It does not host privileged site creation or membership providers.
- Angular: `features/admin/workspaces.ts` and `workspace-contracts.ts` render native target review, reasons, idempotent folder actions, independent PBC verification and read-only dedicated-site status; unknown responses fence further writes.
- Worker: `EngagementWorkspaceProvisioningHandler` fingerprints the provider plan and refuses changed plans or folder ownership collisions before binding; publication serialization stays local to PostgreSQL after provider I/O.
- Checks: `WorkspaceProvisioningApiTests`, `AngularWorkspaceAdministrationJourneyTests`, `workspaces.spec.ts` and `EngagementWorkspaceProvisioningTests`. Observed results belong to the execution ledger.

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
- UI: `features/engagements/planning.ts` and `budget-preparation-contracts.ts` provide exact review, tab references and explicit reconciliation.
- Persistence: `AuditSphereDbContext.BudgetPreparations.cs` and `NativeBudgetPreparationReview` retain immutable exact publication evidence and rollback fences.
- Verification: `BudgetPreparationReviewTests`, `BudgetPreparationReviewApiTests`, `AngularBudgetPreparationJourneyTests` and planning component tests.

## Reviewed budget approval receipts

- Domain: `Practice/BudgetApproval.cs` retains immutable actor-owned approval evidence.
- Application: `Practice/BudgetApprovalWorkspace.cs` owns exact review, guarded approval and receipt lookup; `PracticeTimeService` retains independent approval authority.
- API: `Ui/UiEndpoints.BudgetApproval.cs` exposes authenticated preview, confirmation and receipt recovery.
- UI: `features/engagements/planning.ts` and `budget-approval-contracts.ts` bind confirmation and reconcile unknown outcomes without resending writes.
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

## Hardened HTTP boundary and OpenAPI contract

- Boundary policies: `Api/HttpBoundary/HttpPolicies.cs` (authenticated-session policy), `Api/HttpBoundary/ApiRateClass.cs` (cost classes with endpoint markers), `Api/HttpBoundary/ApiRateLimiter.cs` (chained global limiter, per-identity partitions, configurable budgets), `Api/HttpBoundary/SecurityHeaderOptions.cs` + `SecurityHeadersMiddleware.cs` (build-derived strict script CSP and global headers), `Api/HttpBoundary/RequestBodyLimitMiddleware.cs` (Kestrel ceiling plus JSON command 413 guard), `Api/HttpBoundary/ApiError.cs` (stable boundary error body).
- Host wiring: `ApiHost.cs`, `ApiHost.HttpBoundary.cs` (rate limiting, request limits, security headers, contract registration); group-level session authorization on `/api/ui` (`Ui/UiEndpoints.cs`) and `/api/setup` (`ApiHost.Installation.cs`); export/upload rate-class markers on `UiEndpoints.{Portfolio,Statements,Packages,JournalReview,AccountingRecords,GeneralLedger}.cs` and `ApiHost.DocumentEndpoints.cs`.
- Contract: `Api/Contracts/ApiContract.cs` (native OpenAPI 3.1 document `auditsphere` with session-cookie scheme, ApiError schema, decimal-as-string rule); committed artifact `contracts/auditsphere-openapi.json`; drift script `scripts/contracts/verify-openapi.sh` wired into CI.
- Angular side: `core/api.ts` keeps command/outcome semantics (no retries, `outcome.unknown`, typed 429 refusal asserted in `core/api.spec.ts`).
- Tests: `HttpBoundarySecurityTests.cs`, `OpenApiContractTests.cs`.
- Authority: [HTTP boundary and contract architecture](auditsphere-architecture-http-boundary-and-contract-current.md); deployment topology in `infra/auditsphere-deployment-iac-runbook.md`.
