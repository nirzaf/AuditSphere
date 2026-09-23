# Existing automated test catalog

## Baseline and interpretation

Historical inventory date: 2026-09-22. Build/discovery baseline: `b8e47219179ab5cd707d95dfc6e594da37429d85`. Final source/discovery reconciliation also covered `053aaec315a179082d0535ad4bda3c2706c5900d`; the intervening commit changes only evidence documentation, with no test/runtime/configuration source changes.

A fresh locked restore and Release build preceded discovery:

```bash
dotnet tool restore
dotnet restore AuditSphereOps.slnx --locked-mode
dotnet build AuditSphereOps.slnx --no-restore --configuration Release
dotnet test AuditSphereOps.slnx --no-build --no-restore --configuration Release --list-tests
```

The tables below preserve the original **246 Domain cases**, representing **235 test methods: 229 facts and 6 theories with 17 data rows**, in **37 classes across 36 test-source files**. They belong to `AuditSphereOps.Domain.Tests`, the original [Domain test project](../../tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj). Fresh solution discovery on 2026-09-23 found **297 cases** across Domain 257, [API 6](../../tests/AuditSphereOps.Api.Tests/PbcHttpTests.cs), and [Playwright E2E 34](../../tests/AuditSphereOps.E2E.Tests). The Domain suite has grown by 11 cases since the detailed baseline; the API/E2E scenarios use `CaseId` traits. These newer cases are source-identified but not yet reconciled into the original category tables below.

The 246-case detailed tables are a historical Domain baseline, not the current solution total. The current inventory includes dedicated API and Playwright projects; the [execution ledger](../execution/status.json) records the latest local discovery and run evidence separately from hosted CI and live acceptance.

| Primary category | Methods | Expanded cases |
|---|---:|---:|
| Accounting | 81 | 84 |
| Audit workflow | 42 | 44 |
| Microsoft 365 onboarding | 10 | 10 |
| Security and authorization | 14 | 14 |
| External-service boundaries and integration | 44 | 46 |
| Performance and load | 1 | 1 |
| Supporting platform and practice | 43 | 47 |
| **Total** | **235** | **246** |

Each case has exactly one primary category. Security, immutability, concurrency, and lineage assertions also occur in the other categories; do not count cross-references again. IDs are catalog identifiers, not claimed specification IDs; preserve them when adding or moving cases.

### Layer and trait legend

The `Layer / Profile` column separates actual dependencies from current xUnit `Trait("Profile", ...)` values:
- `U`: unit/in-process calculation, validation, rendering, or policy; no database/provider network.
- `I`: integration using real PostgreSQL and application/worker services. May also use local staging files or provider doubles; never implies a live provider.
- `C`: provider-boundary contract, in process, no live network.
- `F`: local filesystem adapter integration, without PostgreSQL.
- `D`: observed `Database` trait; `U`: observed `Unit` trait; `B`: observed `Benchmark` trait; `—`: no Profile trait.
- Example: `U / D` is actually database-independent but inherits a Database trait. `U / D+U` matches both trait filters.

Dedicated HTTP API and automated browser projects now exist; their current locally verified counts and scope are summarized above and the latest evidence is in the [execution ledger](../execution/status.json). There is still no live-tenant test project. `RouteCatalogTests` continues to call services, not HTTP routes. See the [automation strategy](E2E_AUTOMATION_STRATEGY.md) for project and CI details.

## 1. Accounting module tests

| ID | Exact test method / case parameters | Layer / Profile | Asserted behavior |
|---|---|---|---|
| ACC-001 | [AccountingBackfillMigrationTests.LegacyContextBackfill_BindsSingleMatchesAndQuarantinesAmbiguity][AccountingBackfillMigrationTests] | I / D | Bind only unambiguous legacy context; retain quarantine evidence for ambiguity. |
| ACC-002 | [AccountingIntegrityTests.OrphanTrialBalanceRow_IsRejectedByForeignKey][AccountingIntegrityTests] | I / D | Reject a TB row with no parent dataset. |
| ACC-003 | [AccountingIntegrityTests.Dataset_ReferencingUnknownEngagement_IsRejectedByScopeForeignKey][AccountingIntegrityTests] | I / D | Reject an unknown engagement link. |
| ACC-004 | [AccountingIntegrityTests.Dataset_ReferencingClientOfAnotherFirm_IsRejectedByScopeForeignKey][AccountingIntegrityTests] | I / D | Reject contradictory firm/client/engagement scope. |
| ACC-005 | [AccountingIntegrityTests.TrialBalanceRows_AreAppendOnly_UpdateAndDeleteRejected][AccountingIntegrityTests] | I / D | Preserve sealed rows; refuse edits, deletes, insertion and reopening. |
| ACC-006 | [AccountingIntegrityTests.AdjustmentLines_FreezeOnceJournalLeavesDraft][AccountingIntegrityTests] | I / D | Allow draft edits; freeze posted journal lines. |
| ACC-007 | [TrialBalanceCsvParserTests.AppendixD_Parses14Rows_WithD2ControlTotals][TrialBalanceCsvParserTests] | U / U | Parse the 14-account fixture with exact control totals. |
| ACC-008 | [TrialBalanceCsvParserTests.SourceIdentity_ChangesWhenRawEvidenceChanges][TrialBalanceCsvParserTests] | U / U | Distinguish changed raw evidence identity. |
| ACC-009 | [TrialBalanceCsvParserTests.DebitCreditProfile_PreservesSourceSidesAndComputesSignedBalance][TrialBalanceCsvParserTests] | U / U | Preserve debit/credit sides and signed net. |
| ACC-010 | [TrialBalanceCsvParserTests.MalformedFiles_AreRejected][TrialBalanceCsvParserTests] — `csv=CSV-1` | U / U | Reject header-only input; full argument below. |
| ACC-011 | [TrialBalanceCsvParserTests.MalformedFiles_AreRejected][TrialBalanceCsvParserTests] — `csv=""` | U / U | Reject empty input. |
| ACC-012 | [TrialBalanceCsvParserTests.MalformedFiles_AreRejected][TrialBalanceCsvParserTests] — `csv=CSV-3` | U / U | Reject the missing-column input; full argument below. |
| ACC-013 | [TrialBalanceCsvParserTests.MixedCurrency_DuplicateAccount_ExcessScale_Formula_AreRejected][TrialBalanceCsvParserTests] | U / U | Reject mixed currency, duplicate account, excess scale and formula input. |
| ACC-014 | [AdjustmentBridgeTests.Import_AppendixD_CreatesPendingDataset_DuplicateIsReusedNeverAppended][AdjustmentBridgeTests] | I / D | Import pending dataset; identical retries reuse it. |
| ACC-015 | [AdjustmentBridgeTests.Import_EquivalentNormalizedContent_IsExplainedAsDuplicate][AdjustmentBridgeTests] | I / D | Explain duplicate normalized content. |
| ACC-016 | [AdjustmentBridgeTests.Import_RejectsContextOutsidePeriodBasis][AdjustmentBridgeTests] | I / D | Reject invalid reporting period/basis context. |
| ACC-017 | [AdjustmentBridgeTests.MultiEntityBatch_SealsIndependentDatasetsWithOneSourceReceipt][AdjustmentBridgeTests] | I / D | Seal entity datasets independently with shared receipt lineage. |
| ACC-018 | [AdjustmentBridgeTests.Import_RejectsMixedLegalEntitiesBeforePromotion][AdjustmentBridgeTests] | I / D | Reject mixed-entity promotion. |
| ACC-019 | [AdjustmentBridgeTests.FullCycle_ValidatePostReflect_ProfitStays175k][AdjustmentBridgeTests] | I / D | Validate, post and reflect once; profit stays 175,000 on replacement. |
| ACC-020 | [AdjustmentBridgeTests.UnbalancedImport_ValidationRejects_OriginalRowsPreserved][AdjustmentBridgeTests] | I / D | Reject imbalance without changing source rows. |
| ACC-021 | [AdjustmentBridgeTests.ContextBoundJournal_RejectsBookFromAnotherPeriod][AdjustmentBridgeTests] | I / D | Deny a journal book from another period. |
| ACC-022 | [AdjustmentBridgeTests.StalePlan_AfterReflectionChange_IsBlocked][AdjustmentBridgeTests] | I / D | Changed reflection decisions stale the plan. |
| ACC-023 | [AdjustmentBridgeTests.CrossScope_JournalAndImport_AreDenied][AdjustmentBridgeTests] | I / D | Deny cross-scope journal/import commands. |
| ACC-024 | [AdjustmentBridgeTests.Guards_RejectUnbalancedJournal_DuplicateNumber_DuplicatePlanLine][AdjustmentBridgeTests] | I / D | Enforce balance and journal/plan uniqueness. |
| ACC-025 | [AdjustmentBridgeTests.ClientBookCorrection_RequiresManagementDecision_AndSupportsReversal][AdjustmentBridgeTests] | I / D | Require management evidence and retain reversal lineage. |
| ACC-026 | [ClientAccountingTests.ClientChartsPeriodsAndGlImport_AreTypedScopedAndClosedSafely][ClientAccountingTests] | I / D | Typed chart/period/GL setup, scope and close controls. |
| ACC-027 | [ClientAccountingTests.AnalyticalAggregate_RequiresClientOrGroupScopeAndOmitsComponentIds][ClientAccountingTests] | I / D | Aggregate only authorized scope without component identifiers. |
| ACC-028 | [ClientAccountingTests.OwnershipInterest_RejectsDuplicateAndCircularHierarchy][ClientAccountingTests] | I / D | Reject duplicate/circular ownership. |
| ACC-029 | [ClientAccountingTests.GlImport_RejectsUndefinedClientDimensionValue][ClientAccountingTests] | I / D | Reject undefined client dimension values. |
| ACC-030 | [ClientAccountingTests.AccountingSetup_DefaultsBlankCurrencyToQar][ClientAccountingTests] | I / D | Default blank setup currency to QAR. |
| ACC-031 | [ClientAccountingTests.TaxonomyNodes_AllowIncrementalParentsOnlyWithinVersion][ClientAccountingTests] | I / D | Restrict incremental taxonomy parents to the version. |
| ACC-032 | [ClientAccountingTests.ValuationEvidence_BlocksWhenClientGenerationChanges][ClientAccountingTests] | I / D | Stale client generations block valuation evidence. |
| ACC-033 | [ClientAccountingTests.EnabledValuationProfiles_MatchGoldenFixturesAndRejectUnsupportedBoundaries][ClientAccountingTests] | I / D | ECL/NRV golden and unsupported-input boundaries. |
| ACC-034 | [ClientAccountingTests.SpecialistAndAnalyticalEvidence_BlocksWhenClientGenerationChanges][ClientAccountingTests] | I / D | Stale specialist/analytical evidence cannot be approved. |
| ACC-035 | [ClientAccountingTests.ReconciliationApproval_StalesWhenSourceDigestChanges][ClientAccountingTests] | I / D | Re-read source digest at approval. |
| ACC-036 | [ClientAccountingTests.ReceivableAging_RetainsBasisBucketsCreditTreatmentAndSettlementLinks][ClientAccountingTests] | I / D | Preserve ageing basis, buckets, credit and settlement evidence. |
| ACC-037 | [ClientAccountingTests.SpecialistAreaSchedules_RetainTypedInputsAndRequireReviewEvidence][ClientAccountingTests] | I / D | Typed specialist inputs and review evidence. |
| ACC-038 | [ClientAccountingTests.AccountingEvidenceApproval_RequiresReviewedProcedureLinkAndPreservesScope][ClientAccountingTests] | I / D | Require a reviewed, scoped audit-procedure link. |
| ACC-039 | [ClientAccountingTests.StreamingGlImport_IsIdempotentAndSealsOnlyCompleteBatch][ClientAccountingTests] | I / D | Replay chunks safely and seal only complete batches. |
| ACC-040 | [ClientAccountingTests.GlCompletenessBridge_IsAccountExactAndPagedWithinScope][ClientAccountingTests] | I / D | Exact account completeness and bounded scoped reads. |
| ACC-041 | [ClientAccountingTests.ClosedPeriodRestatement_PreservesIssuedPackagesAndRequiresIndependentApproval][ClientAccountingTests] | I / D | Restatement preserves issued packages and requires another approver. |
| ACC-042 | [ClientAccountingTests.PeriodReopen_RecordsImmutableRevisionLineage][ClientAccountingTests] | I / D | Reopen through immutable revision history. |
| ACC-043 | [ClientAccountingTests.PeriodClose_RequiresCurrentReviewsForMatchingFinancialPackages][ClientAccountingTests] | I / D | Close only with applicable current package reviews. |
| ACC-044 | [ClientAccountingTests.PeriodRollForward_CopiesDraftBooksAndRequiresOpeningEvidence][ClientAccountingTests] | I / D | Copy drafts without inherited approvals; require opening evidence. |
| ACC-045 | [ClientAccountingTests.FinancialPackageReviews_AreStageBoundAndImmutable][ClientAccountingTests] | I / D | Bind immutable review decisions to package and stage. |
| ACC-046 | [ClientAccountingTests.FinancialPackageRelease_BindsCandidateToCurrentPackageReviews][ClientAccountingTests] | I / D | Release candidate depends on exact current reviews. |
| ACC-047 | [ClientAccountingTests.ClientPackageView_IsScopedAndSupportsSignedInManagementDecision][ClientAccountingTests] | I / D | Client-safe view and scoped management acknowledgement. |
| ACC-048 | [ClientAccountingTests.RestrictedConsolidation_IsDeterministicAndFailsClosed][ClientAccountingTests] | U / U | Deterministic restricted calculation; reject unsupported inputs. |
| ACC-049 | [ClientAccountingTests.CurrencyOperations_KeepRemeasurementTranslationAndDisplaySeparate][ClientAccountingTests] | U / U | Distinct currency operations and exact rate/reserve math. |
| ACC-050 | [ClientAccountingTests.AdvancedConsolidationMethods_RequireExplicitInputsAndConserveRollforwards][ClientAccountingTests] | U / U | Acquisition/NCI, ownership, nested and elimination arithmetic boundaries. |
| ACC-051 | [ClientAccountingTests.AdvancedConsolidationCandidateFixture_BalancesCurrentAndComparativeStatements][ClientAccountingTests] | U / U | Exact constructed current/comparative fixture and movements. |
| ACC-052 | [ClientAccountingTests.AdvancedMethodScheduleValidation_RequiresMethodSpecificInputs][ClientAccountingTests] | U / U | Validate five method-specific schedule shapes and nested uniqueness. |
| ACC-053 | [ClientAccountingTests.AdvancedConsolidationExecution_RequiresBalancedCurrentAndComparativeEvidence][ClientAccountingTests] | U / U | Reject imbalance and missing required method line; hash output. |
| ACC-054 | [ClientAccountingTests.AdvancedConsolidationExecution_GoldenFixturesCoverEachMethod][ClientAccountingTests] | U / U | Five fixtures checked in one fact, not five discovered cases. |
| ACC-055 | [ClientAccountingTests.AdvancedMethodSchedules_AreCanonicalScopedAndIdempotent][ClientAccountingTests] | I / D | Canonical, scoped, replay-safe schedules. |
| ACC-056 | [ClientAccountingTests.NestedAdvancedSchedule_BindsApprovedSourceRun][ClientAccountingTests] | I / D | Bind nested schedule to approved source run. |
| ACC-057 | [ClientAccountingTests.AdvancedForeignSchedule_BindsApprovedRateEvidenceAndReserve][ClientAccountingTests] | I / D | Bind approved rate and reserve evidence. |
| ACC-058 | [ClientAccountingTests.AdvancedConsolidationExecution_IsScopedVerifiedIdempotentlyAndSeparatelyApproved][ClientAccountingTests] | I / D | Scoped execution converges; separate approval rechecks evidence. |
| ACC-059 | [ClientAccountingTests.AdvancedMethodExecution_BindsMethodJournalAndSource][ClientAccountingTests] — `method="OWNERSHIP_CHANGE_V1"` | I / D | Ownership-change journal/source linkage. |
| ACC-060 | [ClientAccountingTests.AdvancedMethodExecution_BindsMethodJournalAndSource][ClientAccountingTests] — `method="ASSET_TRANSFER_ELIMINATION_V1"` | I / D | Asset-transfer journal/source linkage. |
| ACC-061 | [ClientAccountingTests.ConsolidationEliminationKinds_RequireAnEnabledAccountingNature][ClientAccountingTests] | U / U | Only enabled elimination natures accepted. |
| ACC-062 | [ClientAccountingTests.CurrencyTranslation_RequiresPositiveApprovedRate][ClientAccountingTests] | U / U | Positive-rate arithmetic and invalid-rate rejection; no live rate approval. |
| ACC-063 | [ClientAccountingTests.ForeignOperationTranslation_PreservesLineageAndSupportsMultipleLines][ClientAccountingTests] | U / U | Preserve per-line translation identity and multiple lines. |
| ACC-064 | [ClientAccountingTests.ForeignOperationGroup_RequiresPinnedApprovedTranslation][ClientAccountingTests] | I / D | Require pinned, approved translation inputs. |
| ACC-065 | [ClientAccountingTests.GroupWorkflow_UsesApprovedComponentPackagesWithoutMutatingThem][ClientAccountingTests] | I / D | Group workflow preserves approved source packages/books. |
| ACC-066 | [ClientAccountingTests.ExternalComponentPack_WorkflowRequiresReconciliationAndPreservesHistory][ClientAccountingTests] | I / D | Reconcile externally prepared packs; retain immutable history. No provider network. |
| ACC-067 | [FinancialStatementTests.OfficeArtifacts_AreDeterministicFormulaFreeAndMacroFree][FinancialStatementTests] | U / D | Deterministic literal Office exports without formulas/macros/external links. |
| ACC-068 | [FinancialStatementTests.ControlledWorkbook_PreservesOnlyItsTrustedFormula][FinancialStatementTests] | U / D | Exactly the renderer-owned COUNTA formula; input text remains literal. |
| ACC-069 | [FinancialStatementTests.PdfArtifact_IsDeterministicUnicodeAndInactive][FinancialStatementTests] | U / D | Deterministic PDF bytes, page count and inactive structure; not a visual glyph assertion. |
| ACC-070 | [FinancialStatementTests.MappingPlanAndPackage_AreScopedDeterministicAndReviewGated][FinancialStatementTests] | I / D | Scoped mapping-to-package workflow and review gates. |
| ACC-071 | [FinancialStatementTests.AdjustmentInstructions_RequireEvidenceAndCarryExactSourceLineage][FinancialStatementTests] | I / D | Evidence-bound instructions with exact source lineage. |
| ACC-072 | [FinancialStatementTests.MappingApplicability_BindsApprovedClientChartVersion][FinancialStatementTests] | I / D | Approved chart-version applicability. |
| ACC-073 | [FinancialStatementTests.ZeroAdjustmentPlan_ProducesSourceEquivalentPackage][FinancialStatementTests] | I / D | Zero-adjustment equivalence and legacy package compatibility. |
| ACC-074 | [FinancialStatementTests.PackageRounding_ConservesEachSourceBalanceAndRecordsResidual][FinancialStatementTests] | U / D+U | Conserve source balance and explicit rounding residual. |
| ACC-075 | [FinancialStatementTests.FinancialPackage_RendersDeterministicArtifact_WithVerifiableSha256][FinancialStatementTests] | I / D | Persist deterministic artifact and matching SHA-256. |
| ACC-076 | [TrialBalanceWorkerTests.AT07_UnbalancedDataset_IsPersistentlyRejected_OriginalRowsPreserved][TrialBalanceWorkerTests] | I / D | Durable rejection retains original unbalanced rows. |
| ACC-077 | [TrialBalanceWorkerTests.ConcurrentWorkers_ClaimEachPendingDatasetExactlyOnce][TrialBalanceWorkerTests] | I / D | Concurrent workers claim pending validation once. |
| ACC-078 | [TrialBalanceXlsxImporterTests.LiteralWorkbook_UsesRawBytesAndSharedNormalizedIdentity][TrialBalanceXlsxImporterTests] | U / U | Preserve raw hash, normalized identity and leading-zero account. |
| ACC-079 | [TrialBalanceXlsxImporterTests.FormulaAndMacroWorkbooksAreRejected][TrialBalanceXlsxImporterTests] | U / U | Reject formula-shaped text and macro-containing workbook. |
| ACC-080 | [TrialBalanceCalculatorTests.Fixture_Has14Accounts_AndBalancesToZero][TrialBalanceCalculatorTests] | U / — | Fourteen accounts and zero signed balance. |
| ACC-081 | [TrialBalanceCalculatorTests.Fixture_PresentationTotals_MatchAppendixD2][TrialBalanceCalculatorTests] | U / — | Exact Appendix D presentation totals. |
| ACC-082 | [TrialBalanceCalculatorTests.Aj001_AppliesOnce_Profit175k_AndReuploadDoesNotDoubleCount][TrialBalanceCalculatorTests] | U / — | AJ-001 once; reflected replacement does not double count. |
| ACC-083 | [TrialBalanceCalculatorTests.Unbalanced_Journal_IsRejected][TrialBalanceCalculatorTests] | U / — | Refuse unbalanced journal calculation. |
| ACC-084 | [TrialBalanceCalculatorTests.MoneyPolicy_RejectsMixedCurrencySum][TrialBalanceCalculatorTests] | U / — | Refuse mixed-currency sum. |

Full CSV theory values, expressed as C# escaped strings (the `\n` is a newline in the actual argument):

```csharp
// CSV-1, ACC-010
"AccountCode,AccountName,NetClosingBalance,Currency,Entity,MappingCode\n"
// CSV-3, ACC-012
"AccountCode,AccountName,NetClosingBalance,Currency,Entity\n100101,Bank,150000,QAR,DEMO"
```

The runner truncates CSV-1 and CSV-3 to the same display text. Do not deduplicate them by display name. Advanced fixtures asserting supplied balanced statement rows do not, on their own, prove every statement amount was derived from component source lines; source-to-report reconciliation remains a proposed E2E assertion.

## 2. Audit workflow tests

| ID | Exact test method / case parameters | Layer / Profile | Asserted behavior |
|---|---|---|---|
| AUD-001 | [AcceptanceDecisionTests.AcceptedDecision_IsImmutableAndCreatesOneWaitingWorkspace][AcceptanceDecisionTests] | I / D | Immutable acceptance and one waiting workspace intent. |
| AUD-002 | [AcceptanceDecisionTests.AcceptanceRequiresCurrentEvaluationAndPartnerScope][AcceptanceDecisionTests] | I / D | Current evaluation and partner scope required. |
| AUD-003 | [ApprovalTests.ApprovalBindsTargetAndGenerations_ThenBecomesStaleWithoutRewritingHistory][ApprovalTests] | I / D | Exact target/generation binding; stale status preserves history. |
| AUD-004 | [ApprovalTests.ApprovalCreationRejectsStaleRevisionOrGeneration][ApprovalTests] | I / D | Refuse already-stale approval creation. |
| AUD-005 | [AuditFieldworkWorkflowTests.FieldworkEvidence_IsScopedAndReviewable][AuditFieldworkWorkflowTests] | I / D | Signed source rows, reviewed selections, confirmation alternatives and area evidence. Display D01. |
| AUD-006 | [AuditPlanningTests.Materiality_PersistedCorrectly][AuditPlanningTests] | I / D | Persist every materiality field in resolved scope. Display D02. |
| AUD-007 | [AuditPlanningTests.Materiality_Rejects_InvertedThresholds][AuditPlanningTests] — `overall=50000, performance=60000, trivial=2500` | I / D | Performance exceeds overall; no row created. Display D03. |
| AUD-008 | [AuditPlanningTests.Materiality_Rejects_InvertedThresholds][AuditPlanningTests] — `overall=50000, performance=40000, trivial=45000` | I / D | Trivial exceeds performance; no row created. Display D03. |
| AUD-009 | [AuditPlanningTests.Materiality_Rejects_InvertedThresholds][AuditPlanningTests] — `overall=0, performance=0, trivial=0` | I / D | Reject nonpositive overall materiality. Display D03. |
| AUD-010 | [AuditPlanningTests.AuditRisk_PersistedCorrectly][AuditPlanningTests] | I / D | Prevent shifted field-to-column mapping. Display D04. |
| AUD-011 | [AuditPlanningTests.AuditRisk_NormalDecision_DerivesNormalSeverity][AuditPlanningTests] | I / D | Normal decision yields Normal severity. Display D05. |
| AUD-012 | [AuditPlanningTests.AuditRisk_Rejects_InvalidInput][AuditPlanningTests] | I / D | Reject blank assertion and unknown decision. Display D06. |
| AUD-013 | [AuditPlanningTests.Population_PersistedAndValidated][AuditPlanningTests] | I / D | Persist population/version; reject invalid counts/currency. Display D07. |
| AUD-014 | [AuditPlanningTests.Workpaper_CreateAndSubmit_RevisionConflict][AuditPlanningTests] | I / D | Frozen snapshot and stale working-revision refusal. Display D08. |
| AUD-015 | [AuditPlanningTests.WorkpaperDraft_DurableIdempotentAndConsumed][AuditPlanningTests] | I / D | Reload, retry, conflict, discard/restart and consume drafts. Display D09. |
| AUD-016 | [AuditPlanningTests.Finding_PersistedCorrectly][AuditPlanningTests] | I / D | Preserve finding type, amount, scope and corrected flag. Display D10. |
| AUD-017 | [AuditPlanningTests.Finding_ManagementResponse_Recorded][AuditPlanningTests] | I / D | Record nonblank management response. Display D11. |
| AUD-018 | [AuditPlanningTests.ClientInputGeneration_IncrementsOnChange][AuditPlanningTests] | I / D | Explicitly increment and reload generation; not a test of every source-change trigger. Display D19. |
| AUD-019 | [AuditProgramWorkflowTests.ControlledCatalog_UsesScopedAppendOnlyWorkflow][AuditProgramWorkflowTests] | I / D | Publish/adopt/execute catalog and independently review. Display D20. |
| AUD-020 | [AuditScopeIntegrityTests.CrossClientEngagementLink_Rejected][AuditScopeIntegrityTests] | I / D | EF and SQL reject cross-client workpaper links. Display D21. |
| AUD-021 | [AuditScopeIntegrityTests.EngageDeletionRestrictedByEvidence][AuditScopeIntegrityTests] | I / D | Existing workpapers restrict engagement deletion. Display D22. |
| AUD-022 | [AuditScopeIntegrityTests.FrozenPlanningEvidence_IsAppendOnly][AuditScopeIntegrityTests] | I / D | DB refuses mutation/deletion of frozen planning evidence. Display D23. |
| AUD-023 | [AuditScopeIntegrityTests.WorkingWorkpaper_RemainsEditable][AuditScopeIntegrityTests] | I / D | Working draft remains editable. Display D24. |
| AUD-024 | [AuditScopeIntegrityTests.ScopeColumns_AreMandatoryAndConstrained][AuditScopeIntegrityTests] | I / D | Scoped-table constraints, EF ownership and no model drift. Display D25. |
| AUD-025 | [AuditScopeIntegrityTests.NullScope_RejectedByDatabase][AuditScopeIntegrityTests] | I / D | Refuse null firm scope. Display D26. |
| AUD-026 | [AuditScopeIntegrityTests.SeverityMustFollowSignificance][AuditScopeIntegrityTests] | I / D | CHECK constraint enforces significance/severity consistency. Display D27. |
| AUD-027 | [AuditScopeIntegrityTests.RejectedWrite_LeavesNoPartialState][AuditScopeIntegrityTests] | I / D | Database refusal leaves no partial planning mutation. Display D28. |
| AUD-028 | [AuditScopeIntegrityTests.MissingGrant_Denied][AuditScopeIntegrityTests] | I / D | No covering grant yields nondisclosing denial. Display D29. |
| AUD-029 | [AuditScopeIntegrityTests.DisallowedRole_Denied][AuditScopeIntegrityTests] | I / D | Client role cannot perform staff planning. Display D30. |
| AUD-030 | [AuditScopeIntegrityTests.Gates_RefusePlanningWrites][AuditScopeIntegrityTests] | I / D | Blocked professional state/hold stops writes. Display D31. |
| AUD-031 | [AuditScopeIntegrityTests.PopulatedLegacyHistory_StopsMigrationBeforeModification][AuditScopeIntegrityTests] | I / D | Ambiguous historical scope stops migration. Display D32. |
| AUD-032 | [RecordsArchiveTests.ArchiveWorkflow_StoresStructuredManifest_AndRequiresObservedProtection][RecordsArchiveTests] | I / D | Structured accounting/group export, requested/observed evidence and hold lifecycle using synthetic observations. |
| AUD-033 | [RecordsArchiveTests.ArchiveWorkflow_DoesNotTreatRequestedRecordsActionAsProtection][RecordsArchiveTests] | I / D | Request alone cannot satisfy observed protection. |
| AUD-034 | [RecordsArchiveTests.ReArchive_ProducesSeparateVersionWithPredecessorLink][RecordsArchiveTests] | I / D | New archive version retains predecessor. |
| AUD-035 | [RecordsArchiveTests.VerifyArchive_BlockedByActiveLegalHold][RecordsArchiveTests] | I / D | Active hold blocks archive verification. |
| AUD-036 | [RecordsArchiveTests.BuildManifest_RequiresCurrentApprovedProfile][RecordsArchiveTests] | I / D | Current approved records profile required. |
| AUD-037 | [RecordsArchiveTests.ReArchive_DigestStableForIdenticalContent][RecordsArchiveTests] | I / D | Stable digest for identical content. |
| AUD-038 | [ReleaseEvidenceTests.NT19_SignatureLineage_PreservesLineageWhenPreSignDiffersFromSignedHash][ReleaseEvidenceTests] | I / D | Preserve distinct pre-sign/signed identities in synthetic lineage. |
| AUD-039 | [ReleaseEvidenceTests.ReleaseGate_RefusesUnsignedRelease_WhenRequireSignatureLineageIsTrue][ReleaseEvidenceTests] | I / D | Required signature lineage blocks unsigned release. |
| AUD-040 | [ReleaseEvidenceTests.ReleaseGate_RefusesExpiredProtectionAttestation_WhenRequireProtectionAttestationIsTrue][ReleaseEvidenceTests] | I / D | Expired attestation cannot satisfy protection gate. |
| AUD-041 | [ReleaseEvidenceTests.ReleaseSafetyOptions_StartupValidation_RefusesLiveActivationWithoutEvidence][ReleaseEvidenceTests] | U / D | Validate startup safety options, not a real hosted startup. |
| AUD-042 | [ReleaseEvidenceTests.LocalAppendOnlyCheckpointStore_EnforcesContentAddressedIntegrityAndDetectsConflicts][ReleaseEvidenceTests] | F / D | Content-addressed local checkpoint integrity and conflict detection. |
| AUD-043 | [ReleaseTests.ReleaseGate_RejectsMissingCheckpointAndWrongManifest_ThenIsIdempotent][ReleaseTests] | I / D | Required exact checkpoint, then idempotent release. |
| AUD-044 | [ReleaseTests.ReleaseGate_BlocksChangedInputsAndCandidateRevision][ReleaseTests] | I / D | Stale input/candidate revisions block release. |

Cross-cutting tags: scope, maker/checker, immutable evidence, stale review, migration, recovery. Purview observations and signature records here are synthetic; no actual protection or cryptographic-provider acceptance is established.

## 3. Microsoft 365 onboarding tests

| ID | Exact test method | Layer / Profile | Asserted behavior |
|---|---|---|---|
| M365-001 | [Microsoft365AccessTests.InitialBootstrap_BindsOnlyTheConfiguredMicrosoftIdentity_AndIsIdempotent][Microsoft365AccessTests] | I / D | Exact initial identity, idempotent grant, consumed capability and authenticated resume. |
| M365-002 | [Microsoft365AccessTests.ClientAssignment_CreatesScopedCopyInvitation_AndRevokeStopsCopy][Microsoft365AccessTests] | I / D | Scoped invitation/copy evidence, revoke and stale actor denial; not email delivery. |
| M365-003 | [Microsoft365AccessTests.ClientIdentity_CannotReceiveStaffRoleOrFirmWideAccess][Microsoft365AccessTests] | I / D | Reject client elevation/firm-wide grant and administrator self-elevation. |
| M365-004 | [Microsoft365OnboardingTests.SetupClaim_IsSingleInstallationScoped_AndDraftResumesWithNewCapability][Microsoft365OnboardingTests] | I / D | Installation claim, resumed draft and stale revision rejection. |
| M365-005 | [Microsoft365OnboardingTests.DraftRejectsUnsafeSiteAndUnknownOptionalState][Microsoft365OnboardingTests] | I / D | Reject unsafe site URL and unsupported optional state. |
| M365-006 | [Microsoft365OnboardingTests.FolderTemplates_AreAllowlistedVersionedAndIdempotent][Microsoft365OnboardingTests] | I / D | Canonical allowlisted templates, versions and idempotency. |
| M365-007 | [Microsoft365OnboardingTests.ConnectionActivation_RequiresExactObservedEvidenceAndApprovedTemplate][Microsoft365OnboardingTests] | I / D | Activation requires approved template and synthetic evidence bound to connection revision. |
| M365-008 | [AuditPlanningTests.TenantCapability_WithoutConfig_IsBlocked][AuditPlanningTests] | U / D | Missing flags yield BLOCKED with provenance. Display D15. |
| M365-009 | [AuditPlanningTests.TenantCapability_Partial_StillBlocked][AuditPlanningTests] | U / D | Entra-only flags do not satisfy SharePoint prerequisite. Display D16. |
| M365-010 | [AuditPlanningTests.TenantCapability_Full_IsReady][AuditPlanningTests] | U / D | Boolean capability helper returns READY, not observed tenant acceptance. Display D17. |

Cross-reference SEC-013–014 for roster administration and last-administrator protection. No existing test above performs live OIDC, directory lookup, selected-resource Graph operations, or Purview verification.

## 4. Security and authorization tests

| ID | Exact test method | Layer / Profile | Asserted behavior |
|---|---|---|---|
| SEC-001 | [AuthorizationDecisionTests.FirmAdministration_RequiresExplicitUnscopedAdministratorGrant][AuthorizationDecisionTests] | I / D | Administration needs explicit firm-wide authority. |
| SEC-002 | [AuthorizationDecisionTests.FirmWideGrant_CanReadOwnDataset_ThroughCommand][AuthorizationDecisionTests] | I / D | Authorized own-firm dataset command succeeds. |
| SEC-003 | [AuthorizationDecisionTests.CrossFirm_DatasetRead_IsDenied][AuthorizationDecisionTests] | I / D | Deny foreign-firm dataset reads. |
| SEC-004 | [AuthorizationDecisionTests.CrossClient_GrantForOtherClient_IsDenied][AuthorizationDecisionTests] | I / D | Other-client grant is insufficient. |
| SEC-005 | [AuthorizationDecisionTests.EngagementScopedGrant_CoversOnlyItsEngagement][AuthorizationDecisionTests] | I / D | Exact-engagement grant cannot widen to siblings. |
| SEC-006 | [AuthorizationDecisionTests.DisabledUser_AndStaleSession_AreDenied][AuthorizationDecisionTests] | I / D | Disabled identity and stale session epoch denied. |
| SEC-007 | [AuthorizationDecisionTests.MissingGrant_OrMissingRole_IsDenied][AuthorizationDecisionTests] | I / D | Both covering grant and required role needed. |
| SEC-008 | [AuthorizationDecisionTests.UnreleasedHold_BlocksProfessionalWork_ButNotPlainReads][AuthorizationDecisionTests] | I / D | Distinguish professional-work gate from authorized reads. |
| SEC-009 | [AuthorizationDecisionTests.ClientUser_IsDenied_InternalOnlyTargets][AuthorizationDecisionTests] | I / D | Internal records remain staff-only. |
| SEC-010 | [AuthorizationDecisionTests.GuessedDatasetId_DoesNotDiscloseExistence][AuthorizationDecisionTests] | I / D | Nondisclosing guessed-ID responses. |
| SEC-011 | [AuthorizationDecisionTests.DuplicateIdentityBinding_IsRejected][AuthorizationDecisionTests] | I / D | Unique identity binding enforced. |
| SEC-012 | [AuthorizationDecisionTests.OrphanGrant_OrphanEngagement_AndEngagementGrantWithoutClient_AreRejected][AuthorizationDecisionTests] | I / D | Reject invalid grant/engagement scope shapes. |
| SEC-013 | [RoleAdministrationTests.RosterIdentityAndRoleGrant_AreExactScopedIdempotentAndRevokeSafely][RoleAdministrationTests] | I / D | Exact roster/grant identity and safe revocation. |
| SEC-014 | [RoleAdministrationTests.ConcurrentAdministratorRevocations_CannotRemoveTheLastUsableAdministrator][RoleAdministrationTests] | I / D | Concurrent revocations retain a usable administrator. |

Additional security assertions: ACC-003–006, ACC-023, ACC-027, ACC-038, AUD-020–031, M365-001–003, INT-003, INT-030, INT-034, PLAT-025–030. These are cross-references, not extra cases. Existing service-level denial is not proof of equivalent HTTP/circuit behavior.

## 5. Integration tests with external services

All current cases in this category use local contracts, simulated effects, or fail-closed adapters. **Live external-service cases: zero in this test inventory.** “Remote success” in a test name denotes controlled fixture behavior, not Microsoft network evidence.

| ID | Exact test method / case parameters | Layer / Profile | Asserted behavior |
|---|---|---|---|
| INT-001 | [DocumentSnapshotTests.CaptureComputesExactHash_AndRejectsDuplicateVersion][DocumentSnapshotTests] | I / D | Exact captured hash and unique version. |
| INT-002 | [DocumentSnapshotTests.DatabaseRejectsInvalidHash_AndCrossScopeSnapshot][DocumentSnapshotTests] | I / D | DB enforces hash shape and snapshot scope. |
| INT-003 | [DocumentSnapshotTests.CaptureDeniesForeignFirmActor][DocumentSnapshotTests] | I / D | Foreign-firm capture denied. |
| INT-004 | [DurableOutboxTests.IdenticalConcurrentEnqueues_HaveOneOperationAndAuditEvent_ConflictsDoNotLeak][DurableOutboxTests] | I / D | One operation/event; conflicts do not disclose scope. |
| INT-005 | [DurableOutboxTests.Rollback_RemovesBusinessChangeOperationAndAuditTogether][DurableOutboxTests] | I / D | Atomic rollback across business/outbox/audit writes. |
| INT-006 | [DurableOutboxTests.IndependentWorkers_ClaimDistinctRows_AndSkipLockedRows][DurableOutboxTests] | I / D | Independent claims and SKIP LOCKED behavior. |
| INT-007 | [DurableOutboxTests.Eligibility_ExcludesFutureTerminalOtherGroupFirmAndUnsupportedKinds][DurableOutboxTests] | I / D | Claim eligibility respects state/time/group/firm/kind. |
| INT-008 | [DurableOutboxTests.ExpiredLease_RejectsOldRenewCompleteAndFailure_BeforeAndAfterReplacement][DurableOutboxTests] | I / D | Expired owner cannot renew or publish outcomes. |
| INT-009 | [DurableOutboxTests.RemoteSuccessThenLocalRollback_RecreatedWorkerReconcilesOneDurableEffect][DurableOutboxTests] | I / D | Recreated worker reconciles one simulated durable effect. |
| INT-010 | [DurableOutboxTests.UncertainResult_MissingOrWrongDigestRemainsBlocked][DurableOutboxTests] — `mismatch=false` | I / D | Missing digest leaves uncertain result blocked. |
| INT-011 | [DurableOutboxTests.UncertainResult_MissingOrWrongDigestRemainsBlocked][DurableOutboxTests] — `mismatch=true` | I / D | Mismatched digest leaves uncertain result blocked. |
| INT-012 | [DurableOutboxTests.SafeRetry_HonorsDelay_AndDeadLettersAtAttemptLimit][DurableOutboxTests] | I / D | Retry delay and bounded attempts. |
| INT-013 | [DurableOutboxTests.PermanentErrors_AreBlockedNotRetried][DurableOutboxTests] — `fault="authorization", expected=AUTHORIZATION_BLOCKED` | I / D | Authorization failure is terminally blocked. |
| INT-014 | [DurableOutboxTests.PermanentErrors_AreBlockedNotRetried][DurableOutboxTests] — `fault="configuration", expected=PROVIDER_BLOCKED` | I / D | Configuration failure is blocked, not retried. |
| INT-015 | [DurableOutboxTests.CancellationAfterEffect_PreservesUncertaintyAndReconciles][DurableOutboxTests] | I / D | Cancellation does not erase uncertain provider effect. |
| INT-016 | [DurableOutboxTests.BackgroundRenewer_ExtendsLeaseDuringPausedEffect][DurableOutboxTests] | I / D | Renew lease during controlled paused effect. |
| INT-017 | [DurableOutboxTests.ChangedRevisionAndRecoveryEpoch_BlockPublication][DurableOutboxTests] | I / D | Revision/recovery fencing blocks publication. |
| INT-018 | [DurableOutboxTests.ScopeForeignKeysChecksAndAppendOnlyEvidence_AreDatabaseEnforced][DurableOutboxTests] | I / D | SQL constraints and immutable operation evidence. |
| INT-019 | [DurableOutboxTests.InvalidTypedRequest_IsRejectedBeforeEnqueue][DurableOutboxTests] | I / D | Validate typed request before queue mutation. |
| INT-020 | [DurableOutboxTests.MissingGuardsAndMismatchedRequest_AreNotExecutable][DurableOutboxTests] | I / D | Required guards and request identity enforced. |
| INT-021 | [DurableOutboxTests.LeaseLostDuringEffect_CancelsWorkWithoutPublishingStaleSuccess][DurableOutboxTests] | I / D | Lease loss cancels local work and fences stale success. |
| INT-022 | [DurableOutboxTests.LeaseExpiresWhileRenewalWaitsForLock_CannotBeRevived][DurableOutboxTests] | I / D | Lock-delayed renewal cannot revive expired lease. |
| INT-023 | [DurableOutboxTests.InputChangesDuringEffect_PreventPublicationAfterSuccessfulProviderWrite][DurableOutboxTests] | I / D | Successful simulated write cannot publish against changed input. |
| INT-024 | [OperationRecoveryTests.QueuedOperation_CanBeCancelledWithoutPublishing][OperationRecoveryTests] | I / D | Cancel queued operation without result publication. |
| INT-025 | [OperationRecoveryTests.BlockedTransfer_RecoveredByAdministrator_CompletesOnRetry][OperationRecoveryTests] | I / D | Authorized recovery enables safe transfer retry. |
| INT-026 | [OperationRecoveryTests.CompletedOperation_IsNeverReArmed_AndListIsRedacted][OperationRecoveryTests] | I / D | No completed replay; redact support listing. |
| INT-027 | [OperationRecoveryTests.DeadLetterOperation_RetriedWithFreshAttemptBudget][OperationRecoveryTests] | I / D | Authorized retry gets fresh attempt budget. |
| INT-028 | [OperationRecoveryTests.QuarantinedFirm_BlocksRetry_UntilQuarantineLiftedByAdministrator][OperationRecoveryTests] | I / D | Quarantine gates retries. |
| INT-029 | [OperationRecoveryTests.RecoverySession_RequiresPersistedApprovalBeforeRestart][OperationRecoveryTests] | I / D | Persist approval before recovery restart. |
| INT-030 | [OperationRecoveryTests.CrossFirmOperation_IsNondisclosingDenied][OperationRecoveryTests] | I / D | Cross-firm recovery denial is nondisclosing. |
| INT-031 | [PbcTests.MailWorker_DeliversQueuedNotification_ExactlyOnce][PbcTests] | I / D | One notification via test mail sender; no actual mailbox delivery. |
| INT-032 | [PbcTests.RequestThread_QueuesEmail_StoresReplies_AndScopesDownload][PbcTests] | I / D | Scoped request/reply/download service workflow. |
| INT-033 | [PbcTests.TrustedCompletion_StagesVerifiedBytes_QueuesTransferWithoutReceiving][PbcTests] | I / D | Staged verified bytes queue transfer; reception remains pending. |
| INT-034 | [PbcTests.ClientUploader_CannotComplete_StaffBoundaryRequired][PbcTests] | I / D | Staff completion boundary enforced. |
| INT-035 | [PbcTests.TamperedStagedBytes_AreRefused_WithoutTransferOperation][PbcTests] | I / D | Tampered bytes create no transfer. |
| INT-036 | [PbcTests.MissingStagedChunk_IsRefused_WithoutTransferOperation][PbcTests] | I / D | Missing chunk creates no transfer. |
| INT-037 | [PbcTests.ExecutableContent_IsRejected_IndependentOfDeclaredType][PbcTests] | I / D | Content validation is not based only on declared MIME type. |
| INT-038 | [PbcTests.ChunkReceipts_RemainImmutable_AfterTrustedCompletion][PbcTests] | I / D | Completed chunk receipts remain immutable. |
| INT-039 | [PbcTransferTests.StagedTransfer_CompletesThroughWorker_AndMarksReceivedOnlyThen][PbcTransferTests] | I / D | Mark received only after worker effect completion. |
| INT-040 | [PbcTransferTests.ReviewerGates_AfterReceived_StayScopedAndStale][PbcTransferTests] | I / D | Received documents retain scope/freshness review gates. |
| INT-041 | [PbcTransferTests.InterruptedProviderEffect_IsUncertain_ThenReconcilesToReceived][PbcTransferTests] | I / D | Reconcile interrupted simulated effect before reception. |
| INT-042 | [PbcTransferTests.UnknownOutcomeBeforeEffect_BlocksAfterReconciliationConfirmsNoEffect][PbcTransferTests] | I / D | Proven absence after uncertainty stays explicitly blocked. |
| INT-043 | [PbcTransferTests.DeterministicProviderRejection_BlocksExplicitly_WithoutReceiving][PbcTransferTests] | I / D | Deterministic refusal never marks received. |
| INT-044 | [PbcTransferTests.SafeTransientFailure_Retries_ThenCompletes][PbcTransferTests] | I / D | Safe transient retry completes. |
| INT-045 | [PbcTransferTests.NewUpload_IsRefused_AfterProviderReception][PbcTransferTests] | I / D | Refuse replacement upload after reception. |
| INT-046 | [ProviderBoundaryTests.GraphBoundariesFailClosedBeforeAnyExternalEffect][ProviderBoundaryTests] | C / — | Graph upload/checkpoint adapters throw `live-provider-not-approved`. |

## 6. Performance and load tests

| ID | Exact test method | Layer / Profile | Asserted behavior |
|---|---|---|---|
| PERF-001 | [AccountingBenchmarkTests.RepresentativeAccountingWorkload_CompletesDurablyAndReportsMeasurements][AccountingBenchmarkTests] | I / D+B | Four clients, 2,000 transactions, 8,000 GL lines, concurrent enqueue, two workers, paged reads and 32-line group calculation; durable correctness plus reported timings. |

This single benchmark is not a load, stress, browser-circuit, production capacity, or SLO acceptance suite. No approved throughput/p95/p99 pass threshold is established by this inventory. Load/soak scenarios are proposed below and in the strategy.

## 7. Supporting platform and practice tests

Firm-owned ledger/billing is distinct from client accounting. Retaining these cases prevents a business-category filter from silently omitting part of the suite.

| ID | Exact test method / case parameters | Layer / Profile | Asserted behavior |
|---|---|---|---|
| PLAT-001 | [BillingTests.BillingWorkflow_SeparatesFinanceRoles_BalancesReceiptAndCredit_AndProtectsPostedHistory][BillingTests] | I / D | Finance role separation, receipt/credit balance and protected history. |
| PLAT-002 | [BillingTests.ConcurrentSourceAllocations_AllowOnlyOneInvoice][BillingTests] | I / D | Concurrent allocation cannot double bill. |
| PLAT-003 | [CoreEntityCatalogTests.EngagementAssignment_Persists_Correctly][CoreEntityCatalogTests] | I / — | Assignment fields persist. Display D33. |
| PLAT-004 | [CoreEntityCatalogTests.EqrCase_EnforcesUniqueEngagement_And_TracksConcurrence][CoreEntityCatalogTests] | I / — | EQR unique engagement and concurrence lifecycle. Display D34. |
| PLAT-005 | [CoreEntityCatalogTests.WrittenRepresentation_EnforcesUniqueCode_And_TracksObtained][CoreEntityCatalogTests] | I / — | Unique representation code and obtained state. Display D35. |
| PLAT-006 | [CoreEntityCatalogTests.SpecialistClearance_Persists_ConditionsAndStatus][CoreEntityCatalogTests] | I / — | Persist clearance conditions/status. Display D36. |
| PLAT-007 | [CoreEntityCatalogTests.SourceReceipt_And_EvidenceLink_PersistCorrectly][CoreEntityCatalogTests] | I / — | Source digest and evidence link persist. Display D37. |
| PLAT-008 | [CoreEntityCatalogTests.QuestionnaireSeed_Populates_CompleteBanks][CoreEntityCatalogTests] | I / — | CE-62 and RV-30 question bank seed. Display D38. |
| PLAT-009 | [ErrorCatalogTests.Unknown_codes_use_a_safe_fallback][ErrorCatalogTests] | U / — | Unknown code has safe fallback. |
| PLAT-010 | [ErrorCatalogTests.Stable_codes_have_non_sensitive_ui_messages][ErrorCatalogTests] | U / — | Stable codes expose safe UI messages. |
| PLAT-011 | [LedgerTests.LedgerWorkflow_IsBalancedIdempotentAndImmutable][LedgerTests] | I / D | Balanced, idempotent firm postings with immutable history. |
| PLAT-012 | [LedgerTests.UnbalancedPosting_IsRejectedByCommandAndDeferredDatabaseGuard][LedgerTests] | I / D | Command and deferred DB posting balance enforcement. |
| PLAT-013 | [LedgerTests.PeriodCloseAndPostingShareThePeriodGuard][LedgerTests] | I / D | Posting/close serialize on the period guard. |
| PLAT-014 | [LedgerTests.FinanceRoleAndPostingAccountScopeAreEnforced][LedgerTests] | I / D | Finance role and account scope enforced. |
| PLAT-015 | [OutboxMigrationTests.UpgradeFromPreviousSchema_PreservesAccountingAndProvisionsLocalGuards][OutboxMigrationTests] | I / D | Upgrade preserves six-decimal data and provisions local safety. |
| PLAT-016 | [OutboxMigrationTests.LegacyOperations_StopMigrationWithoutModifyingHistory][OutboxMigrationTests] | I / D | Legacy operations require explicit disposition. |
| PLAT-017 | [OutboxMigrationTests.LegacyFirmLedgerRows_StopMigrationWithoutInventingAccountType][OutboxMigrationTests] | I / D | No fabricated legacy account type. |
| PLAT-018 | [OutboxMigrationTests.LegacyDocumentRows_StopMigrationWithoutInventingScopeOrHash][OutboxMigrationTests] | I / D | No invented scope/hash during migration. |
| PLAT-019 | [OutboxMigrationTests.LegacyApprovals_StopMigrationWithoutInventingDecisionApplicability][OutboxMigrationTests] | I / D | No invented approval applicability. |
| PLAT-020 | [OutboxMigrationTests.LegacyReleases_StopMigrationWithoutInventingCandidateIdentity][OutboxMigrationTests] | I / D | No invented historical release identity. |
| PLAT-021 | [OutboxMigrationTests.LegacyFinancialPackages_StopMigrationWithoutInventingCalculationIdentity][OutboxMigrationTests] | I / D | No invented package calculation identity. |
| PLAT-022 | [OutboxMigrationTests.SimulationAndLiveExecution_AreRejectedOutsideExplicitTestComposition][OutboxMigrationTests] — `environment="Production", simulations=false, effects=false` | U / U | Refuse simulated definition in Production. |
| PLAT-023 | [OutboxMigrationTests.SimulationAndLiveExecution_AreRejectedOutsideExplicitTestComposition][OutboxMigrationTests] — `environment="Staging", simulations=false, effects=false` | U / U | Refuse simulated definition in Staging. |
| PLAT-024 | [OutboxMigrationTests.SimulationAndLiveExecution_AreRejectedOutsideExplicitTestComposition][OutboxMigrationTests] — `environment="Development", simulations=true, effects=false` | U / U | Development cannot execute simulation definition. |
| PLAT-025 | [OutboxMigrationTests.SimulationAndLiveExecution_AreRejectedOutsideExplicitTestComposition][OutboxMigrationTests] — `environment="Test", simulations=false, effects=false` | U / U | Explicit simulation permission required in Test. |
| PLAT-026 | [OutboxMigrationTests.SimulationAndLiveExecution_AreRejectedOutsideExplicitTestComposition][OutboxMigrationTests] — `environment="Test", simulations=true, effects=true` | U / U | Refuse mixed simulated/live effects. |
| PLAT-027 | [OutboxMigrationTests.LocalValidation_RequiresFirm_AndNeverEnablesLiveAdapters][OutboxMigrationTests] | U / U | Firm required; local definition cannot authorize live adapter. |
| PLAT-028 | [PracticeCrmTests.CommercialWorkflow_ConvertsIdempotently_AndLeavesAcceptancePending][PracticeCrmTests] | I / D | Conversion is idempotent and does not approve acceptance. |
| PLAT-029 | [PracticeCrmTests.ClientContactCommand_IsScopedAndSerializesPrimaryContact][PracticeCrmTests] | I / D | Scoped contact command and single primary contact. |
| PLAT-030 | [PracticeCrmTests.ProposalRevision_PreservesSentVersion_AndRejectsStaleRevision][PracticeCrmTests] | I / D | Sent proposal remains immutable; stale edit denied. |
| PLAT-031 | [PracticeCrmTests.CommercialCommands_EnforceOrder_AndCrossFirmActorIsDenied][PracticeCrmTests] | I / D | Commercial transition order and firm scope. |
| PLAT-032 | [PracticeCrmTests.CommercialInputs_RejectInvalidDatesProbability_AndForeignOwner][PracticeCrmTests] | I / D | Validate dates, probability and owner. |
| PLAT-033 | [PracticeCrmTests.Conversion_RejectsConflictingCanonicalIdentity][PracticeCrmTests] | I / D | Conflicting canonical identity cannot convert. |
| PLAT-034 | [PracticeCrmTests.Conversion_BindsAcceptanceToCurrentExistingClientGeneration][PracticeCrmTests] | I / D | Existing-client acceptance binds current generation. |
| PLAT-035 | [PracticeTimeTests.TaskDeadlineAndReportingPeriod_ArePersisted_AndCrossClientLinkIsDenied][PracticeTimeTests] | I / D | Deadline/period persistence with cross-client denial. |
| PLAT-036 | [PracticeTimeTests.TimeWorkflow_RequiresReview_UsesIntegerMinutes_AndRejectsOverlap][PracticeTimeTests] | I / D | Integer-minute time, review and no overlap. |
| PLAT-037 | [PracticeTimeTests.Correction_SupersedesApprovedFact_AndRetainsHistoricalRate][PracticeTimeTests] | I / D | Correction supersedes without overwriting historical rate. |
| PLAT-038 | [PracticeTimeTests.BudgetVersions_SnapshotApprovedRates_AndRejectSelfApproval][PracticeTimeTests] | I / D | Budget rate snapshots and maker/checker. |
| PLAT-039 | [PracticeTimeTests.TaskAssignment_AndCrossFirmScope_AreEnforced][PracticeTimeTests] | I / D | Task assignment and firm boundary. |
| PLAT-040 | [PracticeTimeTests.DatabaseRejectsInvalidTimeShape][PracticeTimeTests] | I / D | Database time-shape constraints. |
| PLAT-041 | [RouteCatalogTests.ClientContactWorkflow_IncrementsGeneration_And_EnforcesSinglePrimaryContact][RouteCatalogTests] | I / D | Service contact workflow increments generation, one primary. |
| PLAT-042 | [RouteCatalogTests.PracticeTime_DraftSubmitApprove_EnforcesSegregationOfDuties][RouteCatalogTests] | I / D | Service draft/submit/approve separation. |
| PLAT-043 | [RouteCatalogTests.FiscalPeriod_CloseWorkflow_RequiresAllJournalsPosted_And_IsIdempotent][RouteCatalogTests] | I / D | Service period close requires posted journals and is replay-safe. |
| PLAT-044 | [AuditPlanningTests.PostingBalanceGuard_Rejects_Unbalanced][AuditPlanningTests] | U / D | Pure unbalanced-posting guard. Display D12. |
| PLAT-045 | [AuditPlanningTests.PostingBalanceGuard_Accepts_Balanced][AuditPlanningTests] | U / D | Pure balanced-posting guard. Display D13. |
| PLAT-046 | [AuditPlanningTests.PostingBalanceGuard_EmptyLines_Balanced][AuditPlanningTests] | U / D | Empty list is mathematically balanced, not a valid posting workflow. Display D14. |
| PLAT-047 | [AuditPlanningTests.ApprovedTimeEntry_IsImmutableByDirectUpdate][AuditPlanningTests] | I / D | Inserts/reloads an approved entry; does not actually attempt UPDATE despite method name. Display D18. |

## Custom display-name crosswalk

These are exact source `DisplayName` strings. For D03, discovery appends the named arguments listed in AUD-007–009. Other cases without an entry use the default fully qualified method display, with theory arguments where applicable.

| Display ID | Exact display name |
|---|---|
| D01 | Fieldwork retains signed source rows, reviewed selections, confirmation alternatives and area evidence |
| D02 | NT-21.1: Materiality assessment persists every field under the resolved scope |
| D03 | NT-21.2: Inverted materiality thresholds are refused with a stable code |
| D04 | NT-21.3: Audit risk persists each column from its own input (regression: shifted mapping) |
| D05 | NT-21.3b: A normal significance decision classifies the risk as Normal |
| D06 | NT-21.4: Risk requires non-empty assertion and a valid significance decision |
| D07 | NT-21.5: Population version persisted; negative control values refused |
| D08 | NT-21.6: Workpaper created and submitted; stale revision refused, snapshot persisted |
| D09 | ASH-05: Workpaper draft is durable, idempotent and consumed by submission |
| D10 | NT-21.7: Finding persisted with its own type, impact and corrected flag |
| D11 | NT-21.8: Filing a management response updates the working finding only |
| D12 | NT-22.1: Unbalanced posting lines rejected before any DB write |
| D13 | NT-22.2: Balanced posting lines pass the guard |
| D14 | NT-22.3: Empty posting lines are balanced (trivially) |
| D15 | NT-23.1: Missing tenant configuration remains BLOCKED with provenance |
| D16 | NT-23.2: Partial tenant config (Entra only) still BLOCKED for SharePoint |
| D17 | NT-23.3: Full tenant config returns READY status |
| D18 | NT-24.1: Approved time entries are not overwritten; they persist in APPROVED status |
| D19 | NT-24.2: Client input generation increments on source change |
| D20 | Audit program publishes, adopts, executes and independently reviews the controlled catalog |
| D21 | NT-11: A workpaper pointing at another client's engagement is rejected by PostgreSQL |
| D22 | NT-11b: An engagement cannot be deleted while its workpapers exist |
| D23 | NT-12: Frozen planning evidence refuses UPDATE and DELETE at the database |
| D24 | NT-12b: Working content stays editable until it is frozen |
| D25 | IG-01: Scope columns are mandatory and every scoped table carries a composite key |
| D26 | IG-02: A NULL scope column in a risk row is rejected by the database |
| D27 | IG-03: A CHECK refuses a significance decision that disagrees with the stored severity |
| D28 | NT-14: A database-refused write leaves no partial planning state behind |
| D29 | AUTH: An actor without a covering grant is denied nondisclosingly |
| D30 | AUTH: A role that is not permitted for planning work is denied |
| D31 | AUTH: Blocked professional work and an unreleased hold both refuse planning writes |
| D32 | MIG: Planning history whose scope the old schema cannot prove stops the migration |
| D33 | CAT-01: EngagementAssignment persists with role, allocated hours and active flag |
| D34 | CAT-02: EqrCase enforces unique engagement and tracks concurrence lifecycle |
| D35 | CAT-03: WrittenRepresentation enforces unique engagement code and tracks obtained state |
| D36 | CAT-04: SpecialistClearance persists area clearance and condition gates |
| D37 | CAT-05: SourceReceipt preserves SHA-256 digest and links to EvidenceLink |
| D38 | CAT-06: QuestionnaireSeed populates full CE-62 and RV-30 question banks |

The labels NT-21 and NT-24 in this class do not match the full corresponding scenarios in specification §44.2. In particular, an approved-time-entry reload is not the full NT-24 browser cycle. The matrix below follows behavior, not title alone.

## Automation outside xUnit

The original detailed tables stop at their 246-case Domain baseline. API and E2E additions since that snapshot:

| Existing surface | What it actually does | Automation boundary |
|---|---|---|
| [Current CI](../../.github/workflows/ci.yml) | Pinned PostgreSQL 18.6, locked restore, Release build, EF drift/migration, full discovery/execution reconciliation, API/Domain/E2E tests, readiness, coverage and artifact upload | Implemented workflow; no hosted result was observed for this inventory. The E2E project runs Playwright Chromium scenarios locally and in this workflow. |
| [Restore drill](../../scripts/db/restore-drill.sh) | Dump local `auditsphere`, restore to generated database, compare migrations/checkpoint/accounting manifests and duplicate release keys | Mutating local rehearsal with owned temporary DB cleanup; requires populated source and native client tools. Not rerun for this inventory. Defaults to writing tracked evidence; CI must redirect evidence to artifacts. |
| [Browser launcher](../../scripts/e2e/accounting-browser-journeys.sh) and [seed SQL](../../scripts/e2e/accounting-browser-seed.sql) | Seed fixed `auditsphere_browser` and launch Development Web | Launcher only, not a test runner; no browser assertions, multi-role orchestration or parallel-safe cleanup. |
| [Tenant verifier](../../scripts/verify-tenant.sh) | Report missing names or unapproved live acceptance runner | Returns BLOCKED/exit 2 even when required variables are supplied. |
| [Connection baseline](../../scripts/diagnostics/connection-baseline.ps1) | Diagnostic connection measurements | Support diagnostic, not an approved load acceptance suite. |

## Requirement coverage and proposed additions

Controlling sources: [system specification §§44–47](../SPECIFICATION.md#s44), [accounting requirements](../AuditSphere_Accounting_module.md), [audit-workflow requirements](../requirements/AuditSphere_Audit_Workflow_Gap_Closure_User_Stories.md), and [M365 onboarding requirements](../AuditSphere_M365_Simple_Onboarding_User_Story.md).

`Covered` below means the narrow stated behavior has existing automation, not that the whole module is accepted. `Partial` means some layers/conditions are unproven. `Proposed` means no executable test for that scenario is established here. `Blocked` requires separate external authority/infrastructure. The specification's 120 named scenarios are requirement IDs, not 120 discovered tests; this inventory does not assert complete AT/ET/VT/NT coverage.

| Requirement behavior | Existing evidence | Status / next proof |
|---|---|---|
| Exact 14-account and AJ-001 reflection arithmetic (§44.3) | ACC-007,019,080–084 | Covered at unit/service layers; browser source-to-report journey proposed. |
| Accounting profiles, imports, valuation, reconciliation, close and groups | ACC-014–075 | Partial: substantial service/calculation tests; full UI and independent source-to-statement reconciliation still needed. |
| Native audit planning/program/fieldwork and durable drafts | AUD-005–019 | Partial: service persistence covered; disconnect/reconnect and concurrent browser editing proposed. |
| Scope constraints, immutable history, rollback (§44.2 NT-11/12/14) | ACC-002–006, AUD-020–031, INT-004–008,018 | Covered for tested paths; does not imply every table/action covered. |
| Role revoke during an existing circuit (NT-03), client switch (NT-08), pooled scope (NT-16) | SEC-003–007, M365-002 | Partial: `AS-PAR-002-JOURNAL-STALE-01` verifies reviewer-action removal and stale-post denial; `AS-PAR-002-PBC-STALE-READ-01` verifies a revoked staff grant clears an open inbox and browser draft on the next denied command. Broader route/circuit coverage remains. |
| HTTP origin and bounded PBC transfer (NT-04/07/17) | API `PROP-API-01`–`04`; Web endpoint code | Covered for unauthenticated/forged-origin denial, invalid/over-limit chunks, duplicate handling, scoped exact-byte download and safe headers; broader adversarial cases remain. |
| XLSX and exact money (NT-09/10) | ACC-007–013,078–084 | Partial: add archive bomb/external relationship, culture and resource-bound matrices. |
| Leases, uncertain effects and recovery (NT-15/18) | INT-006–029, PLAT-015–027 | Partial: restore-behind-external-state and separate custody remain external. |
| Commercial/time/billing and firm ledger (NT-21/22) | PLAT-001–047 | Covered for listed service assertions; full browser workflow proposed. |
| M365 setup, roster and revision-bound activation | M365-001–010, SEC-013–014 | Partial: local control-plane proof only. |
| Entra OIDC and selected-resource Graph (NT-23, P1/P2) | INT-046 and synthetic capability tests | Blocked: no live acceptance tests in this inventory. |
| Release/records/signature lineage (NT-19, P3–P5) | AUD-032–044 | Partial locally; independent checkpoint, signing and Purview observation blocked. |
| Circuit capacity/full browser cycle (NT-20/24) | PERF-001; historical browser evidence | Proposed reusable automation; production capacity and professional acceptance not established. |
| Production recovery, custody, independent review and full tenant acceptance (P7–P10) | Local recovery tests and historical restore ledger | Blocked externally; CI cannot manufacture approvals. |

### Scenario backlog and implementation status

| ID | Layer | Scenario / required assertion | Current status |
|---|---|---|---|
| PROP-API-01 | API | Unauthenticated and forged/missing Origin upload attempts have no transfer effect. | Implemented locally; API test. |
| PROP-API-02 | API | Invalid and over-limit chunks fail without accepted receipts. | Implemented locally; broader cancellation/hash boundaries may be added. |
| PROP-API-03 | API | Exact duplicate chunk is idempotent; changed bytes conflict; unused staging is removed. | Implemented locally; API test. |
| PROP-API-04 | API | Expired capability and sibling scope are denied; successful download returns exact bytes and safe headers. | Implemented locally; API test. |
| PROP-API-05 | API | Liveness remains available while missing DB/pending migration fails readiness; startup does not create schema. | Implemented locally; API test. |
| PROP-API-06 | API | Unsafe test identity profiles, unavailable identity and unsafe return URL fail closed. | Implemented locally; API test. |
| PROP-E2E-01 | UI/E2E | TB/GL → adjustment → reviewed package with exact expected balances and artifact lineage. | Implemented locally; scenario-specific assertions are in test source. |
| PROP-E2E-02 | UI/E2E | Client/engagement scope isolation, including unrelated records with shared account codes. | Implemented locally; see tagged client-scope journey; delayed-read/draft variants remain. |
| PROP-E2E-03 | UI/E2E | Preparer/reviewer roles, self-review and authorization boundaries. | Implemented locally; broader multi-reviewer and revision-change variants remain. |
| AS-PAR-002-JOURNAL-STALE-01 | UI/E2E | Audited reviewer-grant revocation removes the post action; a stale-session post is denied and the journal stays Draft while the retained Staff grant allows reading. | Implemented locally in [FinancialArtifactJourneyTests.cs](../../tests/AuditSphereOps.E2E.Tests/FinancialArtifactJourneyTests.cs); live Entra/session revocation acceptance remains separate. |
| AS-PAR-002-PBC-STALE-READ-01 | UI/E2E | After the exact Staff grant is revoked, the next denied staged-upload command clears the open PBC inbox/timeline and browser draft while leaving the persisted request unchanged. | Implemented locally in [ClientScopeJourneyTests.cs](../../tests/AuditSphereOps.E2E.Tests/ClientScopeJourneyTests.cs); live Entra revocation acceptance remains separate. |
| PROP-E2E-04 | UI/E2E | Audit planning/fieldwork review and durable workpaper behavior across reconnect. | Implemented locally; see tagged audit journey. |
| PROP-E2E-05 | UI/E2E | Setup draft resumes without implying Microsoft verification. | Implemented locally; invitation and live SSO/revocation acceptance remain separate. |
| PROP-E2E-06 | UI/E2E | PBC upload/worker reconciliation and exact scoped download. | Implemented locally; see tagged PBC journey. |
| PROP-E2E-07 | UI/E2E | Advanced FX/consolidation method fixtures reconcile each output to approved inputs. | Proposed; not represented by a matching tagged journey. |
| PROP-E2E-08 | UI/E2E | Download exact persisted XLSX/DOCX/PDF artifacts and inspect inactive structure. | Implemented locally; broader keyboard/visual baselines remain. |
| PROP-E2E-09 | UI/E2E | Expired/missing records protection blocks release and simulated evidence is not represented as verified. | Implemented locally; live Purview behavior remains blocked. |
| PROP-E2E-10 | UI/E2E | Reopen/restatement preserves validated package and creates a new revision. | Implemented locally; see tagged financial-period journey. |
| PROP-E2E-11 | UI/E2E | CRM, reviewed time, billing and firm-ledger close respect role and duplicate boundaries. | Implemented locally; see tagged practice journey. |
| PROP-REC-01 | Recovery | Restore behind external checkpoint quarantines and requires review; no blind replay. | Proposed; separate local restore drill evidence is not this scenario. |
| PROP-LOAD-01 | Load | Concurrent Blazor circuits report latency, memory, pool, queue and correctness metrics against approved limits. | Proposed; no approved load SLO. |
| PROP-LOAD-02 | Soak/stress | Sustained load, worker restart and DB interruption expose leaks, duplicates and unbounded queues. | Proposed; no soak suite. |
| PROP-LIVE-01 | Tenant | Authorized non-production OIDC wrong-tenant/disabled/unassigned identities. | Blocked externally; no live identity result claimed. |
| PROP-LIVE-02 | Tenant | Approved selected-resource operations and denial outside scope. | Blocked externally; no live Graph/SharePoint result claimed. |
| PROP-LIVE-03 | Tenant | Approved Purview protection/hold behavior on synthetic assets. | Blocked externally; no live Purview result claimed. |

## Refresh procedure

1. Build current source with the pinned SDK and locked packages; discover the entire solution unfiltered.
2. Inventory source facts/theories, including classes in unexpected filenames and each data row. Normalize using method identity plus full arguments, not display name alone.
3. Reconcile method count, expanded count, custom display crosswalk, class/file references, observed traits and category sums. Do not delete duplicate-looking CSV display rows.
4. Add new IDs without renumbering existing ones. Update requirement status only when assertions/evidence justify it.
5. Keep proposed cases outside the discovered-case total. Record execution evidence separately from discovery.

Validation of the historical inventory reconciled all 246 rows and 235 method identities against source attributes, including class/file mappings, theory multiplicities and full arguments, observed traits, custom display names, unique IDs and category totals. Current solution discovery on 2026-09-23 independently reconciled 257 Domain + 6 API + 34 E2E = 297 cases, including `AS-PAR-002-PBC-ROLE-READ-01` and `AS-PAR-002-PBC-STALE-READ-01`. Fresh separate project runs passed Domain 257/257, API 6/6 and E2E 34/34 with no skips; the full solution test command was not run as one invocation for this checkpoint. An earlier 296-case full solution attempt had one intermittent E2E reviewer-page failure, which passed alone and in its complete E2E rerun. Newer cases remain source-identified but their per-category catalog reconciliation is follow-up work; this does not imply live tenant or production acceptance.

## Source references

[AcceptanceDecisionTests]: ../../tests/AuditSphereOps.Domain.Tests/AcceptanceDecisionTests.cs
[AccountingBackfillMigrationTests]: ../../tests/AuditSphereOps.Domain.Tests/AccountingBackfillMigrationTests.cs
[AccountingBenchmarkTests]: ../../tests/AuditSphereOps.Domain.Tests/AccountingBenchmarkTests.cs
[AccountingIntegrityTests]: ../../tests/AuditSphereOps.Domain.Tests/AccountingIntegrityTests.cs
[AdjustmentBridgeTests]: ../../tests/AuditSphereOps.Domain.Tests/AdjustmentBridgeTests.cs
[TrialBalanceCsvParserTests]: ../../tests/AuditSphereOps.Domain.Tests/AdjustmentBridgeTests.cs
[ApprovalTests]: ../../tests/AuditSphereOps.Domain.Tests/ApprovalTests.cs
[AuditFieldworkWorkflowTests]: ../../tests/AuditSphereOps.Domain.Tests/AuditFieldworkWorkflowTests.cs
[AuditPlanningTests]: ../../tests/AuditSphereOps.Domain.Tests/AuditPlanningTests.cs
[AuditProgramWorkflowTests]: ../../tests/AuditSphereOps.Domain.Tests/AuditProgramWorkflowTests.cs
[AuditScopeIntegrityTests]: ../../tests/AuditSphereOps.Domain.Tests/AuditScopeIntegrityTests.cs
[AuthorizationDecisionTests]: ../../tests/AuditSphereOps.Domain.Tests/AuthorizationDecisionTests.cs
[BillingTests]: ../../tests/AuditSphereOps.Domain.Tests/BillingTests.cs
[ClientAccountingTests]: ../../tests/AuditSphereOps.Domain.Tests/ClientAccountingTests.cs
[CoreEntityCatalogTests]: ../../tests/AuditSphereOps.Domain.Tests/CoreEntityCatalogTests.cs
[DocumentSnapshotTests]: ../../tests/AuditSphereOps.Domain.Tests/DocumentSnapshotTests.cs
[DurableOutboxTests]: ../../tests/AuditSphereOps.Domain.Tests/DurableOutboxTests.cs
[ErrorCatalogTests]: ../../tests/AuditSphereOps.Domain.Tests/ErrorCatalogTests.cs
[FinancialStatementTests]: ../../tests/AuditSphereOps.Domain.Tests/FinancialStatementTests.cs
[LedgerTests]: ../../tests/AuditSphereOps.Domain.Tests/LedgerTests.cs
[Microsoft365AccessTests]: ../../tests/AuditSphereOps.Domain.Tests/Microsoft365AccessTests.cs
[Microsoft365OnboardingTests]: ../../tests/AuditSphereOps.Domain.Tests/Microsoft365OnboardingTests.cs
[OperationRecoveryTests]: ../../tests/AuditSphereOps.Domain.Tests/OperationRecoveryTests.cs
[OutboxMigrationTests]: ../../tests/AuditSphereOps.Domain.Tests/OutboxMigrationTests.cs
[PbcTests]: ../../tests/AuditSphereOps.Domain.Tests/PbcTests.cs
[PbcTransferTests]: ../../tests/AuditSphereOps.Domain.Tests/PbcTransferTests.cs
[PracticeCrmTests]: ../../tests/AuditSphereOps.Domain.Tests/PracticeCrmTests.cs
[PracticeTimeTests]: ../../tests/AuditSphereOps.Domain.Tests/PracticeTimeTests.cs
[ProviderBoundaryTests]: ../../tests/AuditSphereOps.Domain.Tests/ProviderBoundaryTests.cs
[RecordsArchiveTests]: ../../tests/AuditSphereOps.Domain.Tests/RecordsArchiveTests.cs
[ReleaseEvidenceTests]: ../../tests/AuditSphereOps.Domain.Tests/ReleaseEvidenceTests.cs
[ReleaseTests]: ../../tests/AuditSphereOps.Domain.Tests/ReleaseTests.cs
[RoleAdministrationTests]: ../../tests/AuditSphereOps.Domain.Tests/RoleAdministrationTests.cs
[RouteCatalogTests]: ../../tests/AuditSphereOps.Domain.Tests/RouteCatalogTests.cs
[TrialBalanceWorkerTests]: ../../tests/AuditSphereOps.Domain.Tests/TrialBalanceWorkerTests.cs
[TrialBalanceXlsxImporterTests]: ../../tests/AuditSphereOps.Domain.Tests/TrialBalanceXlsxImporterTests.cs
[TrialBalanceCalculatorTests]: ../../tests/AuditSphereOps.Domain.Tests/UnitTest1.cs
