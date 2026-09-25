using Microsoft.EntityFrameworkCore;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Microsoft365;

namespace AuditSphereOps.Infrastructure.Persistence;

// DbContext: one owner, snake_case, composite (firm,client,engagement) FK discipline (§§27, 42).
// Money decimal(19,6); IDs uuid v7-compatible; immutable TB rows have no update path.
public sealed partial class AuditSphereDbContext(DbContextOptions<AuditSphereDbContext> options) : DbContext(options),
  AuditSphereOps.Application.Operations.IAuditSphereDbContext,
  AuditSphereOps.Application.Operations.IClientAccountingDbContext,
  AuditSphereOps.Application.Operations.IAdjustmentJournalDbContext
{
  public DbSet<AppUser> Users => Set<AppUser>();

  public DbSet<RoleGrant> RoleGrants => Set<RoleGrant>();

  public DbSet<RoleGrantChangeEvidence> RoleGrantChangeEvidences => Set<RoleGrantChangeEvidence>();

  public DbSet<Microsoft365SetupSession> Microsoft365SetupSessions => Set<Microsoft365SetupSession>();

  public DbSet<Microsoft365SetupDraft> Microsoft365SetupDrafts => Set<Microsoft365SetupDraft>();

  public DbSet<Microsoft365ConnectionRevision> Microsoft365ConnectionRevisions => Set<Microsoft365ConnectionRevision>();

  public DbSet<FirmWorkspaceConfiguration> FirmWorkspaceConfigurations => Set<FirmWorkspaceConfiguration>();

  public DbSet<FolderTemplateVersion> FolderTemplateVersions => Set<FolderTemplateVersion>();

  public DbSet<IntegrationVerificationEvidence> IntegrationVerificationEvidences => Set<IntegrationVerificationEvidence>();

  public DbSet<ClientWorkspace> ClientWorkspaces => Set<ClientWorkspace>();

  public DbSet<DirectoryUserObservation> DirectoryUserObservations => Set<DirectoryUserObservation>();

  public DbSet<UserAccessInvitation> UserAccessInvitations => Set<UserAccessInvitation>();

  public DbSet<Lead> Leads => Set<Lead>();

  public DbSet<Opportunity> Opportunities => Set<Opportunity>();

  public DbSet<Proposal> Proposals => Set<Proposal>();

  public DbSet<PracticeClient> PracticeClients => Set<PracticeClient>();

  public DbSet<ClientContact> ClientContacts => Set<ClientContact>();

  public DbSet<WorkTask> WorkTasks => Set<WorkTask>();

  public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();

  public DbSet<RateCardVersion> RateCardVersions => Set<RateCardVersion>();

  public DbSet<EngagementBudget> EngagementBudgets => Set<EngagementBudget>();

  public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();

  public DbSet<BillingAccount> BillingAccounts => Set<BillingAccount>();

  public DbSet<FirmFinanceProfile> FirmFinanceProfiles => Set<FirmFinanceProfile>();

  public DbSet<Invoice> Invoices => Set<Invoice>();

  public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

  public DbSet<Receipt> Receipts => Set<Receipt>();

  public DbSet<ReceiptAllocation> ReceiptAllocations => Set<ReceiptAllocation>();

  public DbSet<CreditNote> CreditNotes => Set<CreditNote>();

  public DbSet<BillingSourceAllocation> BillingSourceAllocations => Set<BillingSourceAllocation>();

  public DbSet<FirmAccount> FirmAccounts => Set<FirmAccount>();

  public DbSet<FirmPeriod> FirmPeriods => Set<FirmPeriod>();

  public DbSet<FirmJournal> FirmJournals => Set<FirmJournal>();

  public DbSet<FirmJournalLine> FirmJournalLines => Set<FirmJournalLine>();

  public DbSet<FirmPosting> FirmPostings => Set<FirmPosting>();

  public DbSet<FirmPostingLine> FirmPostingLines => Set<FirmPostingLine>();

  public DbSet<LedgerSourceLink> LedgerSourceLinks => Set<LedgerSourceLink>();

  public DbSet<LedgerPostingReceipt> LedgerPostingReceipts => Set<LedgerPostingReceipt>();

  public DbSet<PeriodCloseDecision> PeriodCloseDecisions => Set<PeriodCloseDecision>();

  public DbSet<EvaluationResponse> EvaluationResponses => Set<EvaluationResponse>();

  public DbSet<AcceptanceDecision> AcceptanceDecisions => Set<AcceptanceDecision>();

  public DbSet<Engagement> Engagements => Set<Engagement>();

  public DbSet<EngagementHold> EngagementHolds => Set<EngagementHold>();

  public DbSet<RepositoryBinding> RepositoryBindings => Set<RepositoryBinding>();

  public DbSet<SyncCursor> SyncCursors => Set<SyncCursor>();

  public DbSet<IntegrationCapability> IntegrationCapabilities => Set<IntegrationCapability>();

  public DbSet<DocumentReference> DocumentReferences => Set<DocumentReference>();

  public DbSet<DocumentSnapshot> DocumentSnapshots => Set<DocumentSnapshot>();

  public DbSet<PbcRequest> PbcRequests => Set<PbcRequest>();

  public DbSet<PbcUploadIntent> PbcUploadIntents => Set<PbcUploadIntent>();

  public DbSet<PbcUploadChunk> PbcUploadChunks => Set<PbcUploadChunk>();

  public DbSet<PbcCommunication> PbcCommunications => Set<PbcCommunication>();

  public DbSet<TrialBalanceDataset> TrialBalanceDatasets => Set<TrialBalanceDataset>();

  public DbSet<SourceAcceptanceDecision> SourceAcceptanceDecisions => Set<SourceAcceptanceDecision>();

  public DbSet<TrialBalanceValidationIssue> TrialBalanceValidationIssues => Set<TrialBalanceValidationIssue>();

  public DbSet<StatementLayoutVersion> StatementLayoutVersions => Set<StatementLayoutVersion>();

  public DbSet<StatementLayoutLine> StatementLayoutLines => Set<StatementLayoutLine>();

  public DbSet<TrialBalanceImportBatch> TrialBalanceImportBatches => Set<TrialBalanceImportBatch>();

  public DbSet<TrialBalanceRow> TrialBalanceRows => Set<TrialBalanceRow>();

  public DbSet<MappingRule> MappingRules => Set<MappingRule>();

  public DbSet<MappingVersion> MappingVersions => Set<MappingVersion>();

  public DbSet<MappingAllocation> MappingAllocations => Set<MappingAllocation>();

  public DbSet<AdjustedTrialBalanceSnapshot> AdjustedTrialBalanceSnapshots => Set<AdjustedTrialBalanceSnapshot>();

  public DbSet<AdjustedTrialBalanceRow> AdjustedTrialBalanceRows => Set<AdjustedTrialBalanceRow>();

  public DbSet<AdjustmentJournal> AdjustmentJournals => Set<AdjustmentJournal>();

  public DbSet<AdjustmentJournalManagementDecision> AdjustmentJournalManagementDecisions => Set<AdjustmentJournalManagementDecision>();

  public DbSet<AdjustmentLine> AdjustmentLines => Set<AdjustmentLine>();

  public DbSet<JournalSourceReconciliation> JournalSourceReconciliations => Set<JournalSourceReconciliation>();

  public DbSet<AdjustmentPlan> AdjustmentPlans => Set<AdjustmentPlan>();

  public DbSet<AdjustmentPlanLine> AdjustmentPlanLines => Set<AdjustmentPlanLine>();

  public DbSet<FinancialPackage> FinancialPackages => Set<FinancialPackage>();

  public DbSet<FinancialPackageArtifact> FinancialPackageArtifacts => Set<FinancialPackageArtifact>();

  public DbSet<FinancialPackageLine> FinancialPackageLines => Set<FinancialPackageLine>();

  public DbSet<FinancialPackageValidation> FinancialPackageValidations => Set<FinancialPackageValidation>();

  public DbSet<FinancialPackageCashFlowLine> FinancialPackageCashFlowLines => Set<FinancialPackageCashFlowLine>();

  public DbSet<FinancialPackageFxEffect> FinancialPackageFxEffects => Set<FinancialPackageFxEffect>();

  public DbSet<FinancialPackageSeal> FinancialPackageSeals => Set<FinancialPackageSeal>();

  public DbSet<FinancialPackageDisclosure> FinancialPackageDisclosures => Set<FinancialPackageDisclosure>();

  public DbSet<FinancialPackageEquityLine> FinancialPackageEquityLines => Set<FinancialPackageEquityLine>();

  public DbSet<FinancialPackageNoteLine> FinancialPackageNoteLines => Set<FinancialPackageNoteLine>();

  public DbSet<FinancialPackageReviewDecision> FinancialPackageReviewDecisions => Set<FinancialPackageReviewDecision>();

  public DbSet<AccountingCapabilityProfile> AccountingCapabilityProfiles => Set<AccountingCapabilityProfile>();

  public DbSet<AccountingCapabilityAcceptance> AccountingCapabilityAcceptances => Set<AccountingCapabilityAcceptance>();

  public DbSet<ClientAccountingProfile> ClientAccountingProfiles => Set<ClientAccountingProfile>();

  public DbSet<ClientGroup> ClientGroups => Set<ClientGroup>();

  public DbSet<ClientGroupMembership> ClientGroupMemberships => Set<ClientGroupMembership>();

  public DbSet<GroupAccessGrant> GroupAccessGrants => Set<GroupAccessGrant>();

  public DbSet<ClientReportingPeriod> ClientReportingPeriods => Set<ClientReportingPeriod>();

  public DbSet<ClientPeriodAmendment> ClientPeriodAmendments => Set<ClientPeriodAmendment>();

  public DbSet<ClientReportingBook> ClientReportingBooks => Set<ClientReportingBook>();

  public DbSet<OpeningBalanceBridge> OpeningBalanceBridges => Set<OpeningBalanceBridge>();

  public DbSet<ClientPeriodRestatement> ClientPeriodRestatements => Set<ClientPeriodRestatement>();

  public DbSet<ClientChartVersion> ClientChartVersions => Set<ClientChartVersion>();

  public DbSet<AccountingBackfillQuarantine> AccountingBackfillQuarantines => Set<AccountingBackfillQuarantine>();

  public DbSet<ClientAccount> ClientAccounts => Set<ClientAccount>();

  public DbSet<SourceAccountAlias> SourceAccountAliases => Set<SourceAccountAlias>();

  public DbSet<ReportingTaxonomyVersion> ReportingTaxonomyVersions => Set<ReportingTaxonomyVersion>();

  public DbSet<ReportingTaxonomyNode> ReportingTaxonomyNodes => Set<ReportingTaxonomyNode>();

  public DbSet<ClientAccountingDimensionDefinition> ClientAccountingDimensionDefinitions => Set<ClientAccountingDimensionDefinition>();

  public DbSet<SourceImportBatch> SourceImportBatches => Set<SourceImportBatch>();

  public DbSet<GeneralLedgerImportChunk> GeneralLedgerImportChunks => Set<GeneralLedgerImportChunk>();

  public DbSet<GeneralLedgerTransaction> GeneralLedgerTransactions => Set<GeneralLedgerTransaction>();

  public DbSet<GeneralLedgerLine> GeneralLedgerLines => Set<GeneralLedgerLine>();

  public DbSet<GeneralLedgerCompletenessBridge> GeneralLedgerCompletenessBridges => Set<GeneralLedgerCompletenessBridge>();

  public DbSet<AccountingReconciliation> AccountingReconciliations => Set<AccountingReconciliation>();

  public DbSet<AccountingReconciliationProof> AccountingReconciliationProofs => Set<AccountingReconciliationProof>();

  public DbSet<AccountingReconciliationItem> AccountingReconciliationItems => Set<AccountingReconciliationItem>();

  public DbSet<EclAssessment> EclAssessments => Set<EclAssessment>();

  public DbSet<InventoryValuationAssessment> InventoryValuationAssessments => Set<InventoryValuationAssessment>();

  public DbSet<SpecialistAccountingSchedule> SpecialistAccountingSchedules => Set<SpecialistAccountingSchedule>();

  public DbSet<AnalyticalReview> AnalyticalReviews => Set<AnalyticalReview>();

  public DbSet<JournalRiskFlag> JournalRiskFlags => Set<JournalRiskFlag>();

  public DbSet<AccountingEvidenceAuditLink> AccountingEvidenceAuditLinks => Set<AccountingEvidenceAuditLink>();

  public DbSet<ConsolidationScopeVersion> ConsolidationScopeVersions => Set<ConsolidationScopeVersion>();

  public DbSet<ConsolidationComponent> ConsolidationComponents => Set<ConsolidationComponent>();

  public DbSet<ExternalComponentPack> ExternalComponentPacks => Set<ExternalComponentPack>();

  public DbSet<ExternalComponentPackLine> ExternalComponentPackLines => Set<ExternalComponentPackLine>();

  public DbSet<OwnershipInterestVersion> OwnershipInterestVersions => Set<OwnershipInterestVersion>();

  public DbSet<IntercompanyMatch> IntercompanyMatches => Set<IntercompanyMatch>();

  public DbSet<ConsolidationJournal> ConsolidationJournals => Set<ConsolidationJournal>();

  public DbSet<ConsolidationJournalLine> ConsolidationJournalLines => Set<ConsolidationJournalLine>();

  public DbSet<AdvancedConsolidationMethodSchedule> AdvancedConsolidationMethodSchedules => Set<AdvancedConsolidationMethodSchedule>();

  public DbSet<AdvancedConsolidationExecution> AdvancedConsolidationExecutions => Set<AdvancedConsolidationExecution>();

  public DbSet<ConsolidationRun> ConsolidationRuns => Set<ConsolidationRun>();

  public DbSet<ConsolidationRunLine> ConsolidationRunLines => Set<ConsolidationRunLine>();

  public DbSet<ExchangeRateSetVersion> ExchangeRateSetVersions => Set<ExchangeRateSetVersion>();

  public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

  public DbSet<TranslationPolicyVersion> TranslationPolicyVersions => Set<TranslationPolicyVersion>();

  public DbSet<TranslationResult> TranslationResults => Set<TranslationResult>();

  public DbSet<CurrencyRemeasurementSchedule> CurrencyRemeasurementSchedules => Set<CurrencyRemeasurementSchedule>();

  public DbSet<CurrencyRemeasurementItem> CurrencyRemeasurementItems => Set<CurrencyRemeasurementItem>();

  public DbSet<MaterialityAssessment> MaterialityAssessments => Set<MaterialityAssessment>();

  public DbSet<MaterialityApproval> MaterialityApprovals => Set<MaterialityApproval>();

  public DbSet<AuditRisk> AuditRisks => Set<AuditRisk>();

  public DbSet<AuditProcedure> AuditProcedures => Set<AuditProcedure>();

  public DbSet<PopulationVersion> PopulationVersions => Set<PopulationVersion>();

  public DbSet<Workpaper> Workpapers => Set<Workpaper>();

  public DbSet<WorkpaperDraft> WorkpaperDrafts => Set<WorkpaperDraft>();

  public DbSet<WorkpaperSubmission> WorkpaperSubmissions => Set<WorkpaperSubmission>();

  public DbSet<AuditProgramVersion> AuditProgramVersions => Set<AuditProgramVersion>();

  public DbSet<AuditProgramProcedure> AuditProgramProcedures => Set<AuditProgramProcedure>();

  public DbSet<EngagementAuditProgram> EngagementAuditPrograms => Set<EngagementAuditProgram>();

  public DbSet<AuditProcedureResult> AuditProcedureResults => Set<AuditProcedureResult>();

  public DbSet<AuditProcedureReview> AuditProcedureReviews => Set<AuditProcedureReview>();

  public DbSet<AuditSchedule> AuditSchedules => Set<AuditSchedule>();

  public DbSet<AuditBankReconciliation> AuditBankReconciliations => Set<AuditBankReconciliation>();

  public DbSet<AuditBankReconciliationItem> AuditBankReconciliationItems => Set<AuditBankReconciliationItem>();

  public DbSet<AuditScheduleRow> AuditScheduleRows => Set<AuditScheduleRow>();

  public DbSet<AuditSelection> AuditSelections => Set<AuditSelection>();

  public DbSet<AuditSelectionItem> AuditSelectionItems => Set<AuditSelectionItem>();

  public DbSet<AuditItemTest> AuditItemTests => Set<AuditItemTest>();

  public DbSet<AuditItemTestReview> AuditItemTestReviews => Set<AuditItemTestReview>();

  public DbSet<AuditCutOffTestRecord> AuditCutOffTestRecords => Set<AuditCutOffTestRecord>();

  public DbSet<OpeningBalanceVerification> OpeningBalanceVerifications => Set<OpeningBalanceVerification>();

  public DbSet<AnalyticalReviewVarianceInvestigation> AnalyticalReviewVarianceInvestigations => Set<AnalyticalReviewVarianceInvestigation>();

  public DbSet<GoingConcernAssessment> GoingConcernAssessments => Set<GoingConcernAssessment>();

  public DbSet<SubsequentEventReview> SubsequentEventReviews => Set<SubsequentEventReview>();

  public DbSet<AuditSubsequentMatchRecord> AuditSubsequentMatchRecords => Set<AuditSubsequentMatchRecord>();

  public DbSet<AuditConfirmationCase> AuditConfirmationCases => Set<AuditConfirmationCase>();

  public DbSet<AuditConfirmationResponse> AuditConfirmationResponses => Set<AuditConfirmationResponse>();

  public DbSet<AuditAlternativeProcedure> AuditAlternativeProcedures => Set<AuditAlternativeProcedure>();

  public DbSet<AuditAreaAssessment> AuditAreaAssessments => Set<AuditAreaAssessment>();

  public DbSet<AuditDifference> AuditDifferences => Set<AuditDifference>();

  public DbSet<Finding> Findings => Set<Finding>();

  public DbSet<ReviewPoint> ReviewPoints => Set<ReviewPoint>();

  public DbSet<Approval> Approvals => Set<Approval>();

  public DbSet<ApprovalApplicability> ApprovalApplicabilities => Set<ApprovalApplicability>();

  public DbSet<ReleaseCandidate> ReleaseCandidates => Set<ReleaseCandidate>();

  public DbSet<Release> Releases => Set<Release>();

  public DbSet<ReleaseCheckpoint> ReleaseCheckpoints => Set<ReleaseCheckpoint>();

  public DbSet<SignatureLineage> SignatureLineages => Set<SignatureLineage>();

  public DbSet<ProtectionAttestation> ProtectionAttestations => Set<ProtectionAttestation>();

  public DbSet<RecordsProfile> RecordsProfiles => Set<RecordsProfile>();

  public DbSet<ArchiveManifest> ArchiveManifests => Set<ArchiveManifest>();

  public DbSet<ArchiveManifestEntry> ArchiveManifestEntries => Set<ArchiveManifestEntry>();

  public DbSet<ArchiveStructuredExport> ArchiveStructuredExports => Set<ArchiveStructuredExport>();

  public DbSet<RecordsActionEvidence> RecordsActionEvidences => Set<RecordsActionEvidence>();

  public DbSet<RecordsAction> RecordsActions => Set<RecordsAction>();

  public DbSet<LegalHold> LegalHolds => Set<LegalHold>();

  public DbSet<Archive> Archives => Set<Archive>();

  public DbSet<DurableOperation> DurableOperations => Set<DurableOperation>();

  public DbSet<OperationAttempt> OperationAttempts => Set<OperationAttempt>();

  public DbSet<OperationEvent> OperationEvents => Set<OperationEvent>();

  public DbSet<FirmSafetyState> FirmSafetyStates => Set<FirmSafetyState>();

  public DbSet<RecoverySession> RecoverySessions => Set<RecoverySession>();

  public DbSet<ClientSafetyState> ClientSafetyStates => Set<ClientSafetyState>();

  public DbSet<RecordState> RecordStates => Set<RecordState>();

  public DbSet<EqrCase> EqrCases => Set<EqrCase>();

  public DbSet<WrittenRepresentation> WrittenRepresentations => Set<WrittenRepresentation>();

  public DbSet<EngagementAssignment> EngagementAssignments => Set<EngagementAssignment>();

  public DbSet<SpecialistClearance> SpecialistClearances => Set<SpecialistClearance>();

  public DbSet<QuestionnaireTemplate> QuestionnaireTemplates => Set<QuestionnaireTemplate>();

  public DbSet<QuestionDefinition> QuestionDefinitions => Set<QuestionDefinition>();

  public DbSet<SourceReceipt> SourceReceipts => Set<SourceReceipt>();

  public DbSet<EvidenceLink> EvidenceLinks => Set<EvidenceLink>();

  protected override void OnModelCreating(ModelBuilder b)
  {
    base.OnModelCreating(b);
    foreach (var e in b.Model.GetEntityTypes())
    {
      e.SetTableName(ToSnake(e.GetTableName()!));
      foreach (var p in e.GetProperties())
        p.SetColumnName(ToSnake(p.GetColumnName()));
    }
    ConfigureMoney(b);
    ConfigurePractice(b);
    ConfigureAudit(b);
    ConfigureFieldwork(b);
    ConfigureScopedEvidence(b);
    ConfigureAccounting(b);
    ConfigureClientAccounting(b);
    ConfigureDocuments(b);
    ConfigurePbc(b);
    ConfigureReviews(b);
    ConfigureCompletion(b);
    ConfigureOperations(b);
    ConfigureSecurity(b);
    ConfigureMicrosoft365(b);
  }

  private static void ConfigureMoney(ModelBuilder b)
  {
    foreach (var e in b.Model.GetEntityTypes())
      foreach (var p in e.GetProperties())
        if (p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?))
          p.SetColumnType("numeric(19,6)");
  }

  /// <summary>
  /// Professional records must never disappear because a parent row was deleted (§42.3), so each
  /// scoped child links to the engagement's composite (firm, client, engagement) identity with
  /// RESTRICT semantics. Property names are validated by EF model building, so a typo fails on the
  /// first test that constructs the context.
  /// </summary>
  private static void ScopeToEngagement<TEntity>(
    Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity,
    string firm,
    string client,
    string engagement) where TEntity : class =>
    entity.HasOne<Engagement>().WithMany()
      .HasForeignKey(firm, client, engagement)
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

  private static string ToSnake(string name)
  {
    var sb = new System.Text.StringBuilder();
    for (var i = 0; i < name.Length; i++)
    {
      var c = name[i];
      if (char.IsUpper(c) && i > 0) sb.Append('_');
      sb.Append(char.ToLowerInvariant(c));
    }
    return sb.ToString();
  }
}
