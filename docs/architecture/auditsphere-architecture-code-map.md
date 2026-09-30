# AuditSphereOps — Current Code Map







**Status: CURRENT.** Capability → files. Consult this before changing a business capability.



Architecture authority: [`auditsphere-architecture-current-architecture.md`](auditsphere-architecture-current-architecture.md) and



[`AGENTS.md`](../../AGENTS.md). Large services and the DbContext are partial classes split by



capability; public APIs are stable across parts. Verified project state lives only in



`docs/execution/status.json`.







## Accounting setup — profiles, periods, books, charts, taxonomy, capabilities







- Domain: `Domain/Accounting/ClientAccounting.cs`



- Application: `Application/Accounting/ClientAccounting/ClientAccountingService.{Profiles,Periods,Books,OpeningBalances,Restatements,Charts,Taxonomy,Capabilities,Authorization}.cs`



- Persistence: `Infrastructure/Persistence/AuditSphereDbContext.ClientAccounting.cs`, `.ClientAccountingSchedules.cs`



- UI: `Web/Components/Pages/AccountingWorkspace.razor`, `AccountingPeriod.razor`, `PeriodRollforward.razor`, `PeriodRestatements.razor`



- Tests: `tests/.../ClientAccountingTests/ClientAccountingTests.{Profiles,PeriodClose,ChartAndTaxonomy}.cs`







Critical invariants: firm-wide configuration rejects client-scoped grants; period close needs



current reviews for matching packages; reopen/restatement create immutable revision lineage.







## Trial balance intake and general ledger







- Domain: `Domain/Accounting/` (datasets, import batches, GL lines/transactions)



- Application: `Application/Accounting/TrialBalance{Calculator,ImportService,CsvImporter,XlsxImporter,DatasetQuery,ValidationHandler}.cs`, `Application/Accounting/ClientAccounting/ClientAccountingService.GeneralLedger.cs`, `GeneralLedgerCompletenessHandler.cs`, `GeneralLedgerQuery.cs`, `Application/Accounting/Analysis/AccountingAnalysisService.GeneralLedger.cs`



- Persistence: `AuditSphereDbContext.Accounting.cs`



- UI: `AccountingRecords.razor`



- Tests: `ClientAccountingTests.GeneralLedger.cs`, `TrialBalanceCalculatorTests.cs`, `TrialBalanceWorkerTests.cs`, `TrialBalanceXlsxImporterTests.cs`, `SourceAcceptanceAndComparativesTests.cs`







Critical invariants: imports are idempotent and sealed only when complete; GL completeness is



account-exact; source acceptance binds sealed sources and bumps generation.







## Journals, adjustments and re-measurement workpapers







- Application: `Application/Accounting/AdjustmentJournalService.cs`, `AdjustmentPlanService.cs`, `AdjustmentEligibilityQuery.cs`, `CurrencyRemeasurementService.cs`



- Persistence: `AuditSphereDbContext.AdjustmentBridge.cs`



- UI: `Journals.razor`, `CurrencyRemeasurement.razor`



- Tests: `AccountingRecordsApprovalTests.cs` (journal submit/return/resubmit, duplicate reversal), `ClientAccountingTests.Currency.cs`







## Reconciliations, valuations, specialists, journal risk and accounting evidence







- Application: `Application/Accounting/Analysis/AccountingAnalysisService.{Reconciliation,Valuations,AnalyticalReview,JournalRisk,Evidence,Authorization}.cs`



- UI: `AccountingEvidence.razor`, `Finding.razor`



- Tests: `ClientAccountingTests.{Reconciliations,Analysis}.cs`, `SourceAcceptanceAndComparativesTests.cs`







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



- Application: `Application/Practice/PracticeCrmService.cs` (CRM / proposals), `PracticeLeadQuery.cs` (current-access lead list), `PracticeTimeService.cs` (time / budgets), `BillingService.cs` (billing), `LedgerService.cs` and `FirmFinanceQuery.cs` (firm financial ledger)



- Persistence: `AuditSphereDbContext.Practice.cs`



- UI: `Leads.razor`, `Portfolio.razor`, `ClientDetail.razor`, `Finance.razor`, `InvoiceDetail.razor`, `PracticeTime.razor`







Boundary: this is the firm's own books (`Practice/FirmLedger`); it never writes client



accounting or consolidation workspaces.







## Commercial quotation, documents and the agreed-fee cycle

- Domain: `Domain/Practice/Commercial.cs` — `QuotationVersion`, `CommercialApprovalRule`, `QuotationApproval`, `FirmCommercialProfile`, `CommercialDocument`, `EngagementFeeAgreement`, `FeeMilestone`, `CommercialNotification`
- Application: `Application/Practice/QuotationCalculator.cs` (pure fee model: rate × hours → complexity → risk premium → discount), `CommercialApprovalMatrix.cs` (pure rule evaluation with a documented fail-safe default), `QuotationService.cs` (versioning, submit, matrix approvals, rule administration, approved-rate options), `CommercialDocumentRenderer.cs` + `CommercialDocumentService.cs` (branded Quotation, Engagement Letter and receipt as DOCX with template/profile version and SHA-256), `FeeAgreementService.cs` (50% advance and balance milestones over the existing `BillingService` review/post/receipt path), `CommercialMailDeliveryHandler.cs` (isolated `mail` worker delivery of the receipt email). `PracticeCrmService.ApproveProposalAsync` gates internal review on an approved, fee-matching quotation.
- Persistence: `AuditSphereDbContext.Commercial.cs`, migration `CommercialQuotationsAndFeeAgreements` (check constraints and append-only/field-restricted triggers)
- UI: `Components/Commercial/QuotationWorkbench.razor`, `FeeAgreementPanel.razor`, `ProposalWorkflowPanel.razor` (hosted by `ProposalDetail.razor`), `Pages/CommercialSettings.razor`; authorized download endpoint `/api/commercial/documents/{id}/download` in `Web/Program.cs`
- Tests: `QuotationCalculatorTests.cs`, `CommercialWorkflowTests.cs`, `tests/.../CommercialJourneyTests.cs`

## Acceptance paths, activation and engagement workspace provisioning

- Domain: `Domain/Acceptance/Acceptance.cs` (paths, evidence reference, generation, adverse-answer metadata), `Domain/Engagements/Engagements.cs` (`EngagementActivation`)
- Application: `Application/Acceptance/AcceptanceRules.cs` (pure gate rules), `AcceptanceChecklistService.cs` (path-derived checklist), `AcceptanceDecisionService.cs` (records path and prior decision), `EngagementLifecycleService.cs` (blocked draft creation, Partner-only activation); `Application/Documents/EngagementWorkspaceProvisioning.cs` (`ProvisionEngagementWorkspace.v1` handler and discovery in the isolated `pbc` live group; exact `/Client/Year/01–05` tree when the approved client template has no `engagements` node, otherwise the legacy layout; binding and capability evidence through `PbcRepositoryProvisioning.ApplyEngagementBindingAsync`); `Microsoft365ConfigurationService.SteManifest` and the Setup page "Load STE layout" action
- Persistence: migration `AcceptancePathsEvidenceAndActivation`
- UI: `Components/Acceptance/{AcceptanceChecklistPanel,EngagementActivationPanel,EngagementCreatePanel}.razor`
- Client portal onboarding: `Domain/Documents/ClientPortal.cs` (`ClientPortalFirstSignIn`, `PbcRequestDelegation`, `ClientPortalIntent`), `Application/Documents/ClientPortalService.cs` (identity-path-derived first-sign-in gate used by `PbcService` uploads, primary-contact delegation, participant request query, portal intent view), `PracticeCrmService.RecordPortalIntentAsync` (conversion trigger) and `EngagementLifecycleService.ActivateAsync` (ready to invite); persistence `AuditSphereDbContext.ClientPortal.cs`, migration `ClientPortalOnboardingAndDelegation`; UI `Pages/ClientPortal.razor` (first sign-in), `Pages/ClientPbcRequest.razor` (drop zone, in-browser SHA-256 via `wwwroot/pbc-upload.js`, progress, delegation), `Pages/ClientDetail.razor` (portal intent)
- Tests: `AcceptanceChecklistTests.cs`, `EngagementWorkspaceProvisioningTests.cs`, `ClientPortalOnboardingTests.cs`, `tests/.../AcceptanceJourneyTests.cs`, `tests/.../ClientPortalOnboardingJourneyTests.cs`, `tests/.../PbcUploadJourneyTests.cs`

## Resource planning, staffing, materiality engine and risk routing

- Domain: `Domain/Practice/ResourcePlanning.cs` (`StaffingLevels` with explicit role mapping, `EngagementStaffAssignment`, `StaffProfile`, `StaffCertification`, `StaffAvailability`, `StaffAllocation`, `BudgetPhases`), phase/risk-area on `WorkTask`, `TimeEntry`, `BudgetLine`; `Domain/Audit/MaterialityAndRiskBands.cs` (`MaterialityCalculation`, `RiskBandAssessment`, `RiskPartnerClearance`, `RiskOwnerAssignment`)
- Application: `Practice/StaffingService.cs`, `Practice/ResourcePlanningService.cs` + pure `ResourceGridCalculator.cs`, `PracticeTimeService.GetBudgetBreakdownAsync`; `Audit/MaterialityCalculator.cs` (pure PM/TE/SAD and `RiskBandRules`), `Audit/MaterialityEngineService.cs` (benchmark derivation from the current approved mapping, staleness used by `ApproveMaterialityAssessmentAsync`, SAD evaluation and completion), `Audit/RiskBandService.cs` (bands, owner levels, Partner review, completion blockers)
- Persistence: `AuditSphereDbContext.ResourcePlanning.cs`, migration `ResourcePlanningMaterialityEngineAndRiskBands` (band rule check, significance trigger, append-only evidence triggers, phase checks)
- UI: `Components/Planning/{EngagementStaffingPanel,EngagementBudgetPanel,MaterialityEnginePanel,RiskRoutingPanel}.razor` (hosted by `EngagementDetail.razor` and `AuditPlan.razor`), `Pages/ResourcePlanning.razor` (`/app/practice/resources`)
- Tests: `PlanningResourcesAndMaterialityTests.cs`, `tests/.../PlanningAndResourcesJourneyTests.cs`

## Microsoft 365 setup, security and administration







- Application: `Application/Microsoft365/Microsoft365ConfigurationService.cs`, `SelectedSiteBoundaryVerificationService.cs` (trusted exact-revision positive/negative resource evidence), `TenantConsentService.cs`, `TenantConnectionQuery.cs`, `DirectoryDiscoveryService.cs`, `DirectoryUserBindingService.cs`, `DirectoryCapabilityVerificationService.cs`, `TenantAdministrationProviders.cs` (provider interfaces + permission matrix), `TenantCapabilityService.cs`, `DirectoryProvisioningService.cs` (user creation, guest invitation, reconciliation), `ManagedGroupService.cs`, `AdministrationOverviewQuery.cs`; `Application/Security/` (`RoleAssignmentService.cs`, `RoleGrantExpiry.cs`, `UserAccessWorkspaceQuery.cs`)



- Persistence: `AuditSphereDbContext.Security.cs`, `.Microsoft365.cs`; dedicated certificate and Graph directory reader, `GraphCapabilityCredential.cs`, `GraphTenantAdministrationProviders.cs`, `GraphTenantConsentVerifier.cs` and the Development/Test-only `SimulatedMicrosoftTenant.cs` in `Infrastructure/Providers/`



- UI: `Microsoft365Setup.razor`, `TenantConnection.razor`, `Administration.razor` (dashboard + Users & Access / Microsoft 365 / Audit History tabs), `Components/Administration/*`, `AccessNotAssigned.razor`; consent callback in `Web/Program.cs`, identity callback and composition in `Web/Authentication/TenantAdministrationComposition.cs`



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
