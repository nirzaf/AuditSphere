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

namespace AuditSphereOps.Infrastructure.Persistence;

// DbContext: one owner, snake_case, composite (firm,client,engagement) FK discipline (§§27, 42).
// Money decimal(19,6); IDs uuid v7-compatible; immutable TB rows have no update path.
public sealed class AuditSphereDbContext(DbContextOptions<AuditSphereDbContext> options) : DbContext(options),
  AuditSphereOps.Application.Operations.IAuditSphereDbContext,
  AuditSphereOps.Application.Operations.IClientAccountingDbContext,
  AuditSphereOps.Application.Operations.IAdjustmentJournalDbContext
{
  public DbSet<AppUser> Users => Set<AppUser>();
  public DbSet<RoleGrant> RoleGrants => Set<RoleGrant>();
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
  public DbSet<TrialBalanceDataset> TrialBalanceDatasets => Set<TrialBalanceDataset>();
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
  public DbSet<AccountingReconciliationItem> AccountingReconciliationItems => Set<AccountingReconciliationItem>();
  public DbSet<EclAssessment> EclAssessments => Set<EclAssessment>();
  public DbSet<InventoryValuationAssessment> InventoryValuationAssessments => Set<InventoryValuationAssessment>();
  public DbSet<SpecialistAccountingSchedule> SpecialistAccountingSchedules => Set<SpecialistAccountingSchedule>();
  public DbSet<AnalyticalReview> AnalyticalReviews => Set<AnalyticalReview>();
  public DbSet<JournalRiskFlag> JournalRiskFlags => Set<JournalRiskFlag>();
  public DbSet<AccountingEvidenceAuditLink> AccountingEvidenceAuditLinks => Set<AccountingEvidenceAuditLink>();
  public DbSet<ConsolidationScopeVersion> ConsolidationScopeVersions => Set<ConsolidationScopeVersion>();
  public DbSet<ConsolidationComponent> ConsolidationComponents => Set<ConsolidationComponent>();
  public DbSet<OwnershipInterestVersion> OwnershipInterestVersions => Set<OwnershipInterestVersion>();
  public DbSet<IntercompanyMatch> IntercompanyMatches => Set<IntercompanyMatch>();
  public DbSet<ConsolidationJournal> ConsolidationJournals => Set<ConsolidationJournal>();
  public DbSet<ConsolidationJournalLine> ConsolidationJournalLines => Set<ConsolidationJournalLine>();
  public DbSet<ConsolidationRun> ConsolidationRuns => Set<ConsolidationRun>();
  public DbSet<ConsolidationRunLine> ConsolidationRunLines => Set<ConsolidationRunLine>();
  public DbSet<ExchangeRateSetVersion> ExchangeRateSetVersions => Set<ExchangeRateSetVersion>();
  public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
  public DbSet<TranslationPolicyVersion> TranslationPolicyVersions => Set<TranslationPolicyVersion>();
  public DbSet<TranslationResult> TranslationResults => Set<TranslationResult>();
  public DbSet<MaterialityAssessment> MaterialityAssessments => Set<MaterialityAssessment>();
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
  }

  private static void ConfigureDocuments(ModelBuilder b)
  {
    var binding = b.Entity<RepositoryBinding>();
    binding.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_repository_bindings_firm_id_id");
    binding.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_repository_bindings_scope_id");
    binding.Property(x => x.TenantId).HasMaxLength(2000);
    binding.Property(x => x.SiteId).HasMaxLength(2000);
    binding.Property(x => x.DriveId).HasMaxLength(2000);
    binding.Property(x => x.RootFolderId).HasMaxLength(2000);
    binding.Property(x => x.Classification).HasMaxLength(2000);
    binding.Property(x => x.DesiredAccess).HasMaxLength(2000);
    binding.Property(x => x.ObservedAccess).HasMaxLength(2000);
    binding.Property(x => x.CapabilityProfile).HasMaxLength(2000);
    binding.ToTable("repository_bindings", t => t.HasCheckConstraint("ck_repository_binding_values",
      "length(trim(tenant_id)) > 0 AND length(trim(site_id)) > 0 AND length(trim(drive_id)) > 0 AND length(trim(root_folder_id)) > 0 AND length(trim(classification)) > 0 AND length(trim(desired_access)) > 0 AND length(trim(observed_access)) > 0 AND length(trim(capability_profile)) > 0"));
    binding.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    binding.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var cursor = b.Entity<SyncCursor>();
    cursor.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_sync_cursors_firm_id_id");
    cursor.Property(x => x.Cursor).HasMaxLength(4000);
    cursor.HasIndex(x => new { x.FirmId, x.RepositoryBindingId })
      .IsUnique().HasDatabaseName("ux_sync_cursor_binding");
    cursor.ToTable("sync_cursors", t => t.HasCheckConstraint("ck_sync_cursor_values",
      "length(trim(cursor)) > 0 AND generation >= 1"));
    cursor.HasOne<RepositoryBinding>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.RepositoryBindingId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var capability = b.Entity<IntegrationCapability>();
    capability.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_integration_capabilities_firm_id_id");
    capability.Property(x => x.HealthStatus).HasMaxLength(50);
    capability.Property(x => x.TestedPermissions).HasMaxLength(4000);
    capability.HasIndex(x => new { x.FirmId, x.RepositoryBindingId })
      .IsUnique().HasDatabaseName("ux_integration_capability_binding");
    capability.ToTable("integration_capabilities", t => t.HasCheckConstraint("ck_integration_capability_values",
      "length(trim(health_status)) > 0 AND length(trim(tested_permissions)) > 0 AND ((tested_at IS NULL AND health_status IN ('UNKNOWN','BLOCKED')) OR tested_at IS NOT NULL)"));
    capability.HasOne<RepositoryBinding>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.RepositoryBindingId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var reference = b.Entity<DocumentReference>();
    reference.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_document_references_firm_id_id");
    reference.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_document_references_scope_id");
    reference.Property(x => x.Provider).HasMaxLength(50);
    reference.Property(x => x.DriveId).HasMaxLength(200);
    reference.Property(x => x.ItemId).HasMaxLength(200);
    reference.Property(x => x.Path).HasMaxLength(2000);
    reference.Property(x => x.Purpose).HasMaxLength(100);
    reference.ToTable("document_references", t => t.HasCheckConstraint("ck_document_reference_values",
      "length(trim(provider)) > 0 AND length(drive_id) > 0 AND length(item_id) > 0 AND length(path) > 0 AND length(purpose) > 0"));
    reference.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    reference.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    reference.HasOne<RepositoryBinding>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.RepositoryBindingId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var snapshot = b.Entity<DocumentSnapshot>();
    snapshot.Property(x => x.DriveId).HasMaxLength(200);
    snapshot.Property(x => x.ItemId).HasMaxLength(200);
    snapshot.Property(x => x.VersionId).HasMaxLength(200);
    snapshot.Property(x => x.Sha256Hex).HasMaxLength(64);
    snapshot.Property(x => x.CapturedBy).HasMaxLength(100);
    snapshot.HasIndex(x => new { x.FirmId, x.DocumentReferenceId, x.VersionId })
      .IsUnique().HasDatabaseName("ux_document_snapshot_firm_document_version");
    snapshot.ToTable("document_snapshots", t => t.HasCheckConstraint("ck_document_snapshot_values",
      "length(drive_id) > 0 AND length(item_id) > 0 AND length(version_id) > 0 AND sha256_hex ~ '^[0-9a-f]{64}$' AND byte_count >= 0 AND length(captured_by) > 0"));
    snapshot.HasOne<DocumentReference>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.DocumentReferenceId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
  }

  private static void ConfigurePbc(ModelBuilder b)
  {
    var request = b.Entity<PbcRequest>();
    request.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_pbc_requests_firm_id_id");
    request.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_pbc_requests_scope_id");
    request.Property(x => x.Objective).HasMaxLength(2000);
    request.Property(x => x.EntityScope).HasMaxLength(500);
    request.Property(x => x.PeriodStart).HasMaxLength(10);
    request.Property(x => x.PeriodEnd).HasMaxLength(10);
    request.Property(x => x.Area).HasMaxLength(200);
    request.Property(x => x.RequestedFormat).HasMaxLength(200);
    request.Property(x => x.ControlTotals).HasMaxLength(2000);
    request.Property(x => x.DueDate).HasMaxLength(10);
    request.Property(x => x.Confidentiality).HasMaxLength(100);
    request.Property(x => x.AcceptanceCriteria).HasMaxLength(4000);
    request.Property(x => x.State).HasMaxLength(32);
    request.Property(x => x.ClarificationReason).HasMaxLength(2000);
    request.HasIndex(x => new { x.FirmId, x.EngagementId, x.State, x.DueDate });
    request.ToTable("pbc_requests", t => t.HasCheckConstraint("ck_pbc_request_values",
      "state IN ('DRAFT','SENT','ACKNOWLEDGED','PARTIALLY_RECEIVED','RECEIVED','UNDER_REVIEW','ACCEPTED','CLOSED','CLARIFICATION_REQUIRED','RESUBMITTED') AND revision >= 1 AND length(trim(objective)) > 0 AND length(trim(entity_scope)) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND length(trim(area)) > 0 AND length(trim(requested_format)) > 0 AND length(trim(due_date)) = 10 AND length(trim(confidentiality)) > 0 AND length(trim(acceptance_criteria)) > 0 AND ((state = 'ACCEPTED' AND accepted_at IS NOT NULL AND accepted_by_user_id IS NOT NULL) OR state <> 'ACCEPTED')"));
    request.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    request.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    request.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientOwnerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    request.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.FirmOwnerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    request.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReviewerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    request.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    request.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.AcceptedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var intent = b.Entity<PbcUploadIntent>();
    intent.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_pbc_upload_intents_firm_id_id");
    intent.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_pbc_upload_intents_scope_id");
    intent.Property(x => x.FileName).HasMaxLength(255);
    intent.Property(x => x.ContentType).HasMaxLength(200);
    intent.Property(x => x.DeclaredSha256Hex).HasMaxLength(64);
    intent.Property(x => x.CapabilityHash).HasMaxLength(64);
    intent.Property(x => x.State).HasMaxLength(16);
    intent.Property(x => x.FinalSha256Hex).HasMaxLength(64);
    intent.Property(x => x.FailureReason).HasMaxLength(1000);
    intent.Property(x => x.ProviderReceiptDigest).HasMaxLength(64);
    intent.HasIndex(x => new { x.FirmId, x.PbcRequestId, x.CreatedAt });
    intent.ToTable("pbc_upload_intents", t => t.HasCheckConstraint("ck_pbc_upload_intent_values",
      "state IN ('STARTED','CHUNKING','STAGED','RECEIVED','FAILED','EXPIRED') AND revision >= 1 AND length(trim(file_name)) > 0 AND length(trim(content_type)) > 0 AND declared_byte_count > 0 AND declared_byte_count <= 262144000 AND received_byte_count >= 0 AND received_byte_count <= declared_byte_count AND declared_sha256_hex ~ '^[0-9a-f]{64}$' AND capability_hash ~ '^[0-9a-f]{64}$' AND expires_at > created_at AND ((state = 'STAGED' AND transfer_operation_id IS NOT NULL) OR state NOT IN ('STAGED')) AND ((state = 'RECEIVED' AND completed_at IS NOT NULL AND final_sha256_hex = declared_sha256_hex AND provider_registered_at IS NOT NULL AND provider_receipt_digest ~ '^[0-9a-f]{64}$' AND transfer_operation_id IS NOT NULL) OR state <> 'RECEIVED')"));
    intent.HasOne<AuditSphereOps.Domain.Completion.DurableOperation>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.TransferOperationId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    intent.HasOne<PbcRequest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcRequestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    intent.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.UploaderUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var chunk = b.Entity<PbcUploadChunk>();
    chunk.Property(x => x.Sha256Hex).HasMaxLength(64);
    chunk.Property(x => x.StagedPath).HasMaxLength(2000);
    chunk.HasIndex(x => new { x.FirmId, x.PbcUploadIntentId, x.ChunkIndex })
      .IsUnique().HasDatabaseName("ux_pbc_upload_chunk_identity");
    chunk.ToTable("pbc_upload_chunks", t => t.HasCheckConstraint("ck_pbc_upload_chunk_values",
      "chunk_index >= 0 AND \"offset\" >= 0 AND byte_count > 0 AND byte_count <= 8388608 AND sha256_hex ~ '^[0-9a-f]{64}$'"));
    chunk.HasOne<PbcUploadIntent>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcUploadIntentId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }

  private static void ConfigureReviews(ModelBuilder b)
  {
    var approval = b.Entity<Approval>();
    approval.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_approvals_firm_id_id");
    approval.Property(x => x.TargetKind).HasMaxLength(50);
    approval.Property(x => x.ManifestDigest).HasMaxLength(64);
    approval.Property(x => x.Decision).HasMaxLength(16);
    approval.HasIndex(x => new
      {
        x.FirmId, x.TargetKind, x.TargetId, x.TargetRevision, x.InputGeneration,
        x.PolicyGeneration, x.ManifestDigest, x.DecidedByUserId
      }).IsUnique().HasDatabaseName("ux_approval_identity");
    approval.ToTable("approvals", t => t.HasCheckConstraint("ck_approval_values",
      "length(target_kind) > 0 AND target_revision >= 1 AND input_generation >= 1 AND policy_generation >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND decision IN ('APPROVED','REJECTED')"));
    approval.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    approval.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    approval.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.DecidedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var applicability = b.Entity<ApprovalApplicability>();
    applicability.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_approval_applicabilities_firm_id_id");
    applicability.HasIndex(x => new { x.FirmId, x.ApprovalId })
      .IsUnique().HasDatabaseName("ux_approval_applicability_approval");
    applicability.Property(x => x.Status).HasMaxLength(16);
    applicability.Property(x => x.Reason).HasMaxLength(500);
    applicability.ToTable("approval_applicabilities", t => t.HasCheckConstraint("ck_approval_applicability_values",
      "status IN ('CURRENT','STALE','REJECTED') AND length(reason) > 0 AND current_target_revision >= 1 AND current_input_generation >= 1 AND current_policy_generation >= 1"));
    applicability.HasOne<Approval>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovalId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
  }

  private static void ConfigureCompletion(ModelBuilder b)
  {
    var candidate = b.Entity<ReleaseCandidate>();
    candidate.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_release_candidates_firm_id_id");
    candidate.Property(x => x.TargetKind).HasMaxLength(50);
    candidate.Property(x => x.ManifestDigest).HasMaxLength(64);
    candidate.Property(x => x.Status).HasMaxLength(16);
    candidate.HasIndex(x => new
      { x.FirmId, x.TargetKind, x.TargetId, x.TargetRevision, x.ManifestDigest })
      .IsUnique().HasDatabaseName("ux_release_candidate_identity");
    candidate.ToTable("release_candidates", t => t.HasCheckConstraint("ck_release_candidate_values",
      "target_kind IN ('WORKPAPER','FINANCIAL_PACKAGE') AND revision >= 1 AND target_revision >= 1 AND input_generation >= 1 AND policy_generation >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND status IN ('READY','ISSUED')"));
    candidate.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    candidate.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    candidate.HasOne<Approval>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovalId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var checkpoint = b.Entity<ReleaseCheckpoint>();
    checkpoint.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_release_checkpoints_firm_id_id");
    checkpoint.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_release_checkpoints_scope_id");
    checkpoint.Property(x => x.AuthorizedReleaseKey).HasMaxLength(200);
    checkpoint.Property(x => x.ManifestDigest).HasMaxLength(64);
    checkpoint.Property(x => x.StoredReference).HasMaxLength(500);
    checkpoint.Property(x => x.ReadBackDigest).HasMaxLength(64);
    checkpoint.Property(x => x.VerifiedStatus).HasMaxLength(30);
    checkpoint.Property(x => x.Verifier).HasMaxLength(200);
    checkpoint.HasIndex(x => new { x.FirmId, x.ReleaseCandidateId, x.CandidateRevision, x.ManifestDigest })
      .HasDatabaseName("ix_release_checkpoint_candidate_manifest");
    checkpoint.ToTable("release_checkpoints", t => t.HasCheckConstraint("ck_release_checkpoint_values",
      "candidate_revision >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND read_back_digest ~ '^[0-9a-f]{64}$' AND length(trim(authorized_release_key)) > 0 AND length(trim(stored_reference)) > 0 AND verified_status IN ('VERIFIED','PENDING','MISMATCHED','EXPIRED')"));
    ScopeToEngagement(checkpoint, nameof(ReleaseCheckpoint.FirmId), nameof(ReleaseCheckpoint.ClientId), nameof(ReleaseCheckpoint.EngagementId));
    checkpoint.HasOne<ReleaseCandidate>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReleaseCandidateId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var lineage = b.Entity<SignatureLineage>();
    lineage.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_signature_lineages_firm_id_id");
    lineage.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_signature_lineages_scope_id");
    lineage.Property(x => x.PreSignArtifactHash).HasMaxLength(64);
    lineage.Property(x => x.SignedArtifactHash).HasMaxLength(64);
    lineage.Property(x => x.SigningMethod).HasMaxLength(100);
    lineage.Property(x => x.RequestIdentity).HasMaxLength(200);
    lineage.Property(x => x.VerificationOutcome).HasMaxLength(30);
    lineage.Property(x => x.Verifier).HasMaxLength(200);
    lineage.HasIndex(x => new { x.FirmId, x.CandidateId }).HasDatabaseName("ix_signature_lineages_candidate");
    lineage.ToTable("signature_lineages", t => t.HasCheckConstraint("ck_signature_lineage_values",
      "pre_sign_artifact_hash ~ '^[0-9a-f]{64}$' AND signed_artifact_hash ~ '^[0-9a-f]{64}$' AND length(trim(signing_method)) > 0 AND length(trim(request_identity)) > 0 AND verification_outcome IN ('VERIFIED','INVALID','REJECTED')"));
    ScopeToEngagement(lineage, nameof(SignatureLineage.FirmId), nameof(SignatureLineage.ClientId), nameof(SignatureLineage.EngagementId));
    lineage.HasOne<ReleaseCandidate>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CandidateId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var release = b.Entity<Release>();
    release.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_releases_firm_id_id");
    release.Property(x => x.ManifestDigest).HasMaxLength(64);
    release.Property(x => x.AuthorizedReleaseKey).HasMaxLength(200);
    release.HasIndex(x => new { x.FirmId, x.AuthorizedReleaseKey })
      .IsUnique().HasDatabaseName("ux_release_authorized_key");
    release.ToTable("releases", t => t.HasCheckConstraint("ck_release_values",
      "package_revision >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND length(authorized_release_key) > 0"));
    release.HasOne<ReleaseCandidate>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReleaseCandidateId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    release.HasOne<ReleaseCheckpoint>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CheckpointId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    release.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    release.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    release.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReleasedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
  }


  private static void ConfigureSecurity(ModelBuilder b)
  {
    b.Entity<AppUser>().HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_users_firm_id_id");
    b.Entity<AppUser>().HasIndex(x => new { x.TenantId, x.Subject }).IsUnique()
      .HasDatabaseName("ux_users_tenant_subject");
    b.Entity<AppUser>().ToTable("users", t =>
    {
      t.HasCheckConstraint("ck_users_kind", "user_kind IN ('Staff','Client')");
      t.HasCheckConstraint("ck_users_session",
        "session_epoch >= 1 AND length(subject) > 0 AND length(tenant_id) > 0 AND length(email) > 0");
    });
    b.Entity<Engagement>().ToTable("engagements", t =>
      t.HasCheckConstraint("ck_engagement_generation", "generation >= 1"));
    b.Entity<Engagement>()
      .HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PracticeClientId })
      .HasPrincipalKey(c => new { c.FirmId, c.Id })
      .OnDelete(DeleteBehavior.Restrict);
    b.Entity<RoleGrant>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    b.Entity<RoleGrant>().HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<RoleGrant>().HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<RoleGrant>().ToTable("role_grants", t =>
      t.HasCheckConstraint("ck_role_grant_scope",
        "length(role) > 0 AND (engagement_id IS NULL OR client_id IS NOT NULL)"));
    b.Entity<EngagementHold>().HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<EngagementHold>().ToTable("engagement_holds", t =>
      t.HasCheckConstraint("ck_hold_release",
        "length(hold_kind) > 0 AND (NOT released OR released_at IS NOT NULL)"));
  }

  private static void ConfigureOperations(ModelBuilder b)
  {
    var op = b.Entity<DurableOperation>();
    op.Property(x => x.Status).HasConversion<string>();
    op.Property(x => x.ExecutionMode).HasConversion<string>();
    op.Property(x => x.AuthorityMode).HasConversion<string>();
    op.Property(x => x.IdempotencyKey).HasMaxLength(200);
    op.Property(x => x.RequestDigest).HasMaxLength(64);
    op.Property(x => x.CancellationDisposition).HasMaxLength(2000);
    op.HasIndex(x => new { x.FirmId, x.IdempotencyKey }).IsUnique().HasDatabaseName("ux_operation_firm_key");
    op.HasIndex(x => new { x.FirmId, x.ExecutionGroup, x.Status, x.NextAttemptAt, x.CreatedAt });
    op.ToTable("durable_operations", t =>
    {
      t.HasCheckConstraint("ck_operation_state", "status IN ('PENDING','CLAIMED','REMOTE_STARTED','VERIFYING','COMPLETED','RETRY_WAIT','AUTHORIZATION_BLOCKED','PROVIDER_BLOCKED','RESULT_UNCERTAIN','DEAD_LETTER','CANCEL_REQUESTED','CANCELLED_WITH_DISPOSITION')");
      t.HasCheckConstraint("ck_operation_counters", "attempt_token >= 0 AND attempt_count >= 0 AND expected_revision >= 1 AND schema_version >= 1 AND claimed_epoch >= 0");
      t.HasCheckConstraint("ck_operation_request", "length(idempotency_key) > 0 AND request_digest ~ '^[0-9a-f]{64}$' AND octet_length(request_bytes) > 0 AND length(payload_json) <= 16384");
      t.HasCheckConstraint("ck_operation_mode", "execution_mode IN ('LOCAL','SIMULATED','LIVE') AND authority_mode IN ('LOCAL_VALIDATION','SIMULATION')");
      t.HasCheckConstraint("ck_operation_lease", "(status IN ('CLAIMED','REMOTE_STARTED','VERIFYING','CANCEL_REQUESTED') AND lease_owner IS NOT NULL AND lease_expires_at IS NOT NULL AND attempt_token > 0) OR (status NOT IN ('CLAIMED','REMOTE_STARTED','VERIFYING','CANCEL_REQUESTED') AND lease_owner IS NULL AND lease_expires_at IS NULL)");
      t.HasCheckConstraint("ck_operation_scope", "engagement_id IS NULL OR client_id IS NOT NULL");
      t.HasCheckConstraint("ck_operation_result", "status <> 'COMPLETED' OR (completed_at IS NOT NULL AND result_identity IS NOT NULL AND length(result_identity) > 0 AND result_digest IS NOT NULL AND result_digest ~ '^[0-9a-f]{64}$')");
      t.HasCheckConstraint("ck_operation_cancellation", "status <> 'CANCELLED_WITH_DISPOSITION' OR length(trim(cancellation_disposition)) > 0");
    });
    op.HasOne<FirmSafetyState>().WithMany().HasForeignKey(x => x.FirmId).OnDelete(DeleteBehavior.Restrict);
    op.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    op.HasOne<Engagement>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<ClientSafetyState>().HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.Id }).HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmSafetyState>().ToTable("firm_safety_states", t =>
      t.HasCheckConstraint("ck_firm_safety", "deployment_epoch >= 1 AND recovery_epoch >= 0 AND policy_generation >= 1 AND operating_mode IN ('LOCAL_ONLY','RECOVERY_QUARANTINE')"));
    var recovery = b.Entity<RecoverySession>();
    recovery.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_recovery_sessions_firm_id_id");
    recovery.Property(x => x.RestorePoint).HasMaxLength(500);
    recovery.Property(x => x.ReconciliationScope).HasMaxLength(2000);
    recovery.Property(x => x.Findings).HasMaxLength(10000);
    recovery.HasIndex(x => new { x.FirmId, x.CreatedAt });
    recovery.ToTable("recovery_sessions", t => t.HasCheckConstraint("ck_recovery_session_values",
      "external_epoch >= 1 AND length(trim(restore_point)) > 0 AND length(trim(reconciliation_scope)) > 0 AND length(trim(findings)) > 0 AND ((approved_restart_at IS NULL AND approved_by_user_id IS NULL) OR (approved_restart_at IS NOT NULL AND approved_by_user_id IS NOT NULL))"));
    recovery.HasOne<FirmSafetyState>().WithMany()
      .HasForeignKey(x => new { x.FirmId })
      .HasPrincipalKey(x => new { x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    recovery.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    b.Entity<ClientSafetyState>().ToTable("client_safety_states", t =>
      t.HasCheckConstraint("ck_client_generation", "input_generation >= 1"));
    b.Entity<OperationAttempt>().HasIndex(x => new { x.OperationId, x.Token }).IsUnique();
    b.Entity<OperationAttempt>().HasOne<DurableOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
    b.Entity<OperationEvent>().HasOne<DurableOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
    b.Entity<OperationEvent>().HasIndex(x => new { x.OperationId, x.OccurredAt });
  }

  private static void ConfigureMoney(ModelBuilder b)
  {
    foreach (var e in b.Model.GetEntityTypes())
      foreach (var p in e.GetProperties())
        if (p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?))
          p.SetColumnType("numeric(19,6)");
  }

  private static void ConfigurePractice(ModelBuilder b)
  {
    b.Entity<PracticeClient>().HasIndex(x => new { x.FirmId, x.LegalName }).IsUnique();
    b.Entity<WorkTask>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_work_tasks_firm_id_id");
    b.Entity<TimeEntry>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_time_entries_firm_id_id");
    b.Entity<RateCardVersion>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_rate_cards_firm_id_id");
    b.Entity<EngagementBudget>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_budgets_firm_id_id");
    b.Entity<BudgetLine>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_budget_lines_firm_id_id");
    b.Entity<BillingAccount>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_billing_accounts_firm_id_id");
    b.Entity<FirmFinanceProfile>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_finance_profiles_firm_id_id");
    b.Entity<Invoice>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_invoices_firm_id_id");
    b.Entity<InvoiceLine>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_invoice_lines_firm_id_id");
    b.Entity<Receipt>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_receipts_firm_id_id");
    b.Entity<ReceiptAllocation>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_receipt_allocations_firm_id_id");
    b.Entity<CreditNote>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_credit_notes_firm_id_id");
    b.Entity<BillingSourceAllocation>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_billing_sources_firm_id_id");
    b.Entity<FirmAccount>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_firm_accounts_firm_id_id");
    b.Entity<FirmPeriod>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_firm_periods_firm_id_id");
    b.Entity<FirmJournal>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_firm_journals_firm_id_id");
    b.Entity<FirmJournalLine>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_firm_journal_lines_firm_id_id");
    b.Entity<FirmPosting>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_firm_postings_firm_id_id");
    b.Entity<FirmPostingLine>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_firm_posting_lines_firm_id_id");
    b.Entity<LedgerSourceLink>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_ledger_sources_firm_id_id");
    b.Entity<LedgerPostingReceipt>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_ledger_receipts_firm_id_id");
    b.Entity<PeriodCloseDecision>().HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_period_decisions_firm_id_id");
    b.Entity<Lead>().HasIndex(x => new { x.FirmId, x.Status, x.CreatedAt });
    b.Entity<Opportunity>().HasIndex(x => new { x.FirmId, x.LeadId, x.Stage });
    b.Entity<Proposal>().HasIndex(x => new { x.FirmId, x.OpportunityId, x.Revision }).IsUnique();
    b.Entity<WorkTask>().HasIndex(x => new { x.FirmId, x.Status, x.CreatedAt });
    b.Entity<TimeEntry>().HasIndex(x => new { x.FirmId, x.UserId, x.WorkDate, x.Status });
    b.Entity<RateCardVersion>().HasIndex(x => new { x.FirmId, x.Role, x.Activity, x.Currency, x.Version }).IsUnique();
    b.Entity<EngagementBudget>().HasIndex(x => new { x.FirmId, x.EngagementId, x.Version }).IsUnique();
    b.Entity<BudgetLine>().HasIndex(x => new { x.FirmId, x.EngagementBudgetId, x.Role, x.Activity }).IsUnique();
    b.Entity<BillingAccount>().HasIndex(x => new { x.FirmId, x.PracticeClientId }).IsUnique();
    b.Entity<FirmFinanceProfile>().HasIndex(x => x.FirmId).IsUnique();
    b.Entity<Invoice>().HasIndex(x => new { x.FirmId, x.InvoiceNumber }).IsUnique();
    b.Entity<InvoiceLine>().HasIndex(x => new { x.FirmId, x.InvoiceId });
    b.Entity<Receipt>().HasIndex(x => new { x.FirmId, x.BillingAccountId, x.ReceivedAt });
    b.Entity<ReceiptAllocation>().HasIndex(x => new { x.FirmId, x.ReceiptId, x.InvoiceId }).IsUnique();
    b.Entity<CreditNote>().HasIndex(x => new { x.FirmId, x.NoteNumber }).IsUnique();
    b.Entity<BillingSourceAllocation>().HasIndex(x => new { x.FirmId, x.SourceKind, x.SourceId, x.SourceRevision }).IsUnique();
    b.Entity<FirmAccount>().HasIndex(x => new { x.FirmId, x.Code }).IsUnique();
    b.Entity<FirmJournal>().HasIndex(x => new { x.FirmId, x.JournalNumber }).IsUnique();
    b.Entity<FirmJournal>().HasIndex(x => new { x.FirmId, x.SourceKind, x.SourceKey, x.SourceRevision, x.PostingPurpose }).IsUnique();
    b.Entity<FirmPeriod>().HasIndex(x => new { x.FirmId, x.PeriodCode }).IsUnique();
    b.Entity<FirmJournalLine>().HasIndex(x => new { x.FirmId, x.JournalId });
    b.Entity<FirmPosting>().HasIndex(x => new { x.FirmId, x.JournalId }).IsUnique();
    b.Entity<FirmPostingLine>().HasIndex(x => new { x.FirmId, x.PostingId });
    b.Entity<LedgerSourceLink>().HasIndex(x => new { x.FirmId, x.SourceKind, x.SourceKey, x.SourceRevision, x.PostingPurpose }).IsUnique();
    b.Entity<LedgerPostingReceipt>().HasIndex(x => new { x.FirmId, x.RequestKey }).IsUnique();
    b.Entity<PeriodCloseDecision>().HasIndex(x => new { x.FirmId, x.PeriodId, x.DecisionKind, x.DecidedAt });
    b.Entity<Lead>().ToTable("leads", t => t.HasCheckConstraint("ck_lead_status",
      "status IN ('NEW','QUALIFIED','UNQUALIFIED','LOST') AND length(name) > 0 AND length(source) > 0"));
    b.Entity<Opportunity>().ToTable("opportunities", t => t.HasCheckConstraint("ck_opportunity_state",
      "stage IN ('DISCOVERY','PROPOSAL','NEGOTIATION','WON','LOST') AND length(service_route) > 0 AND length(entity_scope) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND expected_fee >= 0 AND (probability IS NULL OR probability BETWEEN 0 AND 100) AND currency ~ '^[A-Z]{3}$'"));
    b.Entity<Proposal>().ToTable("proposals", t =>
    {
      t.HasCheckConstraint("ck_proposal_state",
        "status IN ('DRAFT','INTERNAL_REVIEW','SENT','ACCEPTED','DECLINED','SUPERSEDED') AND revision >= 1");
      t.HasCheckConstraint("ck_proposal_content",
        "length(service_profile_id) > 0 AND length(scope) > 0 AND length(deliverables) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND fee >= 0 AND currency ~ '^[A-Z]{3}$'");
    });
    b.Entity<PracticeClient>().ToTable("practice_clients", t => t.HasCheckConstraint("ck_practice_client_name",
      "length(legal_name) > 0"));
    b.Entity<ClientContact>().ToTable("client_contacts", t => t.HasCheckConstraint("ck_client_contact",
      "length(full_name) > 0 AND length(email) > 0 AND length(role) > 0 AND (valid_to IS NULL OR valid_from IS NULL OR valid_to >= valid_from)"));
    b.Entity<WorkTask>().ToTable("work_tasks", t => t.HasCheckConstraint("ck_work_task_state",
      "status IN ('OPEN','IN_PROGRESS','COMPLETED','CANCELLED') AND length(title) > 0 AND (engagement_id IS NULL OR client_id IS NOT NULL)"));
    b.Entity<TimeEntry>().ToTable("time_entries", t =>
    {
      t.HasCheckConstraint("ck_time_entry_state",
        "status IN ('DRAFT','SUBMITTED','APPROVED','SUPERSEDED') AND revision >= 1 AND start_minute BETWEEN 0 AND 1439 AND duration_minutes BETWEEN 1 AND 1440 AND start_minute + duration_minutes <= 1440");
      t.HasCheckConstraint("ck_time_entry_content",
        "length(role) > 0 AND length(activity) > 0 AND billable_classification IN ('BILLABLE','NON_BILLABLE','NO_CHARGE') AND narrative_visibility IN ('INTERNAL','CLIENT_VISIBLE') AND ((billable_classification = 'BILLABLE' AND rate_card_version_id IS NOT NULL AND rate_per_hour IS NOT NULL AND rate_per_hour >= 0) OR billable_classification <> 'BILLABLE')");
    });
    b.Entity<RateCardVersion>().ToTable("rate_card_versions", t => t.HasCheckConstraint("ck_rate_card_state",
      "version >= 1 AND length(role) > 0 AND length(activity) > 0 AND currency ~ '^[A-Z]{3}$' AND rate_per_hour >= 0 AND status IN ('DRAFT','APPROVED','SUPERSEDED')"));
    b.Entity<EngagementBudget>().ToTable("engagement_budgets", t => t.HasCheckConstraint("ck_budget_state",
      "version >= 1 AND currency ~ '^[A-Z]{3}$' AND status IN ('DRAFT','APPROVED','SUPERSEDED')"));
    b.Entity<BudgetLine>().ToTable("budget_lines", t => t.HasCheckConstraint("ck_budget_line_values",
      "length(role) > 0 AND length(activity) > 0 AND forecast_minutes > 0 AND rate_per_hour >= 0 AND forecast_cost >= 0"));
    b.Entity<BillingAccount>().ToTable("billing_accounts", t => t.HasCheckConstraint("ck_billing_account_currency",
      "currency ~ '^[A-Z]{3}$'"));
    b.Entity<FirmFinanceProfile>().ToTable("firm_finance_profiles", t =>
    {
      t.HasCheckConstraint("ck_finance_profile_currency", "functional_currency ~ '^[A-Z]{3}$'");
      t.HasCheckConstraint("ck_finance_profile_kind", "profile_kind IN ('TEST','PRODUCTION')");
      t.HasCheckConstraint("ck_finance_profile_approval", "(approved = false AND approved_at IS NULL AND approved_by_user_id IS NULL) OR (approved = true AND approved_at IS NOT NULL AND approved_by_user_id IS NOT NULL)");
    });
    b.Entity<Invoice>().ToTable("invoices", t =>
    {
      t.HasCheckConstraint("ck_invoice_state", "status IN ('DRAFT','REVIEW_REQUIRED','APPROVED','POSTED','SENT','CANCELLED') AND revision >= 1");
      t.HasCheckConstraint("ck_invoice_values", "length(invoice_number) > 0 AND (currency IS NULL OR currency ~ '^[A-Z]{3}$') AND subtotal >= 0 AND tax >= 0 AND total >= 0 AND total = subtotal + tax");
      t.HasCheckConstraint("ck_invoice_cancel", "(status = 'CANCELLED' AND cancelled_at IS NOT NULL AND length(cancellation_reason) > 0) OR status <> 'CANCELLED'");
    });
    b.Entity<InvoiceLine>().ToTable("invoice_lines", t => t.HasCheckConstraint("ck_invoice_line_values",
      "length(description) > 0 AND quantity > 0 AND unit_price >= 0 AND line_total >= 0 AND line_total = round(quantity * unit_price, 6) AND ((length(source_kind) = 0 AND source_id IS NULL AND source_revision IS NULL) OR (length(source_kind) > 0 AND source_id IS NOT NULL AND source_revision >= 1))"));
    b.Entity<Receipt>().ToTable("receipts", t => t.HasCheckConstraint("ck_receipt_values",
      "amount > 0 AND (currency IS NULL OR currency ~ '^[A-Z]{3}$') AND length(reference) > 0 AND status = 'RECORDED'"));
    b.Entity<ReceiptAllocation>().ToTable("allocations", t => t.HasCheckConstraint("ck_receipt_allocation_amount",
      "amount > 0"));
    b.Entity<CreditNote>().ToTable("credit_notes", t => t.HasCheckConstraint("ck_credit_note_values",
      "length(note_number) > 0 AND currency ~ '^[A-Z]{3}$' AND amount > 0 AND length(reason) > 0 AND status = 'ISSUED'"));
    b.Entity<BillingSourceAllocation>().ToTable("billing_source_allocations", t => t.HasCheckConstraint("ck_billing_source_values",
      "length(source_kind) > 0 AND source_revision >= 1 AND quantity > 0 AND amount > 0"));
    b.Entity<FirmAccount>().ToTable("firm_accounts", t => t.HasCheckConstraint("ck_firm_account_values",
      "length(code) > 0 AND length(name) > 0 AND account_type IN ('ASSET','LIABILITY','EQUITY','REVENUE','EXPENSE') AND normal_side IN ('DEBIT','CREDIT')"));
    b.Entity<FirmPeriod>().ToTable("firm_periods", t =>
    {
      t.HasCheckConstraint("ck_firm_period_values", "period_code ~ '^[0-9]{4}-(0[1-9]|1[0-2])$' AND status IN ('OPEN','CLOSED','REOPEN_REQUESTED') AND revision >= 1 AND ((status = 'OPEN' AND closed_at IS NULL) OR (status IN ('CLOSED','REOPEN_REQUESTED') AND closed_at IS NOT NULL))");
    });
    b.Entity<FirmJournal>().ToTable("firm_journals", t => t.HasCheckConstraint("ck_firm_journal_values",
      "length(journal_number) > 0 AND length(source_kind) > 0 AND length(source_key) > 0 AND source_revision >= 1 AND length(posting_purpose) > 0 AND currency ~ '^[A-Z]{3}$' AND status IN ('DRAFT','REVIEW_REQUIRED','APPROVED','POSTED')"));
    b.Entity<FirmJournalLine>().ToTable("firm_journal_lines", t => t.HasCheckConstraint("ck_firm_journal_line_values",
      "length(description) > 0 AND debit >= 0 AND credit >= 0 AND ((debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0))"));
    b.Entity<FirmPosting>().ToTable("firm_postings", t => t.HasCheckConstraint("ck_firm_posting_currency",
      "currency ~ '^[A-Z]{3}$'"));
    b.Entity<FirmPostingLine>().ToTable("firm_posting_lines", t => t.HasCheckConstraint("ck_firm_posting_line_values",
      "debit >= 0 AND credit >= 0 AND ((debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0))"));
    b.Entity<LedgerSourceLink>().ToTable("ledger_source_links", t => t.HasCheckConstraint("ck_ledger_source_values",
      "length(source_kind) > 0 AND length(source_key) > 0 AND source_revision >= 1 AND length(posting_purpose) > 0"));
    b.Entity<LedgerPostingReceipt>().ToTable("ledger_posting_receipts", t => t.HasCheckConstraint("ck_ledger_receipt_values",
      "length(request_key) > 0 AND request_digest ~ '^[0-9a-f]{64}$'"));
    b.Entity<PeriodCloseDecision>().ToTable("period_close_decisions", t => t.HasCheckConstraint("ck_period_decision_values",
      "decision_kind IN ('CLOSE','REOPEN') AND length(reason) > 0"));
    b.Entity<Lead>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.OwnerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Opportunity>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.OwnerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Proposal>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Opportunity>().HasOne<Lead>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.LeadId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Opportunity>().HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PracticeClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Proposal>().HasOne<Opportunity>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.OpportunityId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Proposal>().HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PracticeClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<ClientContact>().HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PracticeClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkTask>().HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkTask>().HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkTask>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.AssigneeUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<TimeEntry>().HasOne<WorkTask>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.TaskId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<TimeEntry>().HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<TimeEntry>().HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<TimeEntry>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.UserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<TimeEntry>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<TimeEntry>().HasOne<RateCardVersion>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.RateCardVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<TimeEntry>().HasOne<TimeEntry>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.SupersedesId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<RateCardVersion>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<RateCardVersion>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<EngagementBudget>().HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<EngagementBudget>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<EngagementBudget>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<BudgetLine>().HasOne<EngagementBudget>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.EngagementBudgetId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<BudgetLine>().HasOne<RateCardVersion>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.RateCardVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<BillingAccount>().HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PracticeClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmFinanceProfile>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Invoice>().HasOne<BillingAccount>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.BillingAccountId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Invoice>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Invoice>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<InvoiceLine>().HasOne<Invoice>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.InvoiceId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Receipt>().HasOne<BillingAccount>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.BillingAccountId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<Receipt>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.RecordedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<ReceiptAllocation>().HasOne<Receipt>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReceiptId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<ReceiptAllocation>().HasOne<Invoice>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.InvoiceId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<CreditNote>().HasOne<BillingAccount>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.BillingAccountId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<CreditNote>().HasOne<Invoice>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.InvoiceId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<CreditNote>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<BillingSourceAllocation>().HasOne<InvoiceLine>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.InvoiceLineId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmJournal>().HasOne<FirmPeriod>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmJournal>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmJournal>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmJournalLine>().HasOne<FirmJournal>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.JournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmJournalLine>().HasOne<FirmAccount>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.FirmAccountId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmPosting>().HasOne<FirmPeriod>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmPosting>().HasOne<FirmJournal>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.JournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmPosting>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PostedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmPosting>().HasOne<FirmPosting>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReversalOfPostingId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmPostingLine>().HasOne<FirmPosting>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PostingId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmPostingLine>().HasOne<FirmAccount>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.FirmAccountId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<LedgerSourceLink>().HasOne<FirmPosting>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PostingId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<LedgerPostingReceipt>().HasOne<FirmPosting>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PostingId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<PeriodCloseDecision>().HasOne<FirmPeriod>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<PeriodCloseDecision>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.DecidedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }

  private static void ConfigureAccounting(ModelBuilder b)
  {
    var importBatch = b.Entity<TrialBalanceImportBatch>();
    importBatch.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_trial_balance_import_batches_scope_id");
    importBatch.Property(x => x.RawFileSha256Hex).HasMaxLength(64);
    importBatch.Property(x => x.NormalizedDatasetDigest).HasMaxLength(64);
    importBatch.Property(x => x.ImportProfileVersion).HasMaxLength(100);
    importBatch.Property(x => x.SourceLayout).HasMaxLength(30);
    importBatch.Property(x => x.Basis).HasMaxLength(50);
    importBatch.Property(x => x.Status).HasMaxLength(16);
    importBatch.HasIndex(x => new { x.FirmId, x.EngagementId, x.RawFileSha256Hex }).IsUnique()
      .HasDatabaseName("ux_tb_import_batch_raw_hash");
    importBatch.ToTable("trial_balance_import_batches", table => table.HasCheckConstraint("ck_tb_import_batch_values",
      "raw_file_sha256_hex ~ '^[0-9a-f]{64}$' AND normalized_dataset_digest ~ '^[0-9a-f]{64}$' AND source_layout IN ('SIGNED_NET','DEBIT_CREDIT') AND entity_count >= 2 AND status IN ('LOADING','SEALED') AND (basis IS NULL OR length(trim(basis)) > 0)"));
    importBatch.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    importBatch.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    importBatch.HasOne<ClientReportingPeriod>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    importBatch.HasOne<ClientReportingBook>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.BookId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    b.Entity<TrialBalanceDataset>().Property(x => x.LegalEntityKey).HasMaxLength(200);
    b.Entity<TrialBalanceDataset>().Property(x => x.RawFileSha256Hex).HasMaxLength(64);
    b.Entity<TrialBalanceDataset>().Property(x => x.NormalizedDatasetDigest).HasMaxLength(64);
    b.Entity<TrialBalanceDataset>().Property(x => x.ImportProfileVersion).HasMaxLength(100);
    b.Entity<TrialBalanceDataset>().Property(x => x.SourceLayout).HasMaxLength(30);
    b.Entity<TrialBalanceDataset>().Property(x => x.Basis).HasMaxLength(50);
    b.Entity<TrialBalanceDataset>().Property(x => x.ValidationStatus).HasMaxLength(16).HasDefaultValue("Pending");
    b.Entity<TrialBalanceDataset>().Property(x => x.ImportState).HasMaxLength(16)
      .HasDefaultValue(TrialBalanceImportStates.Sealed).ValueGeneratedNever();
    b.Entity<TrialBalanceDataset>().ToTable("trial_balance_datasets", table =>
    {
      table.HasCheckConstraint("ck_tb_validation_status",
        "validation_status IN ('Pending', 'Accepted', 'Rejected') AND (validation_status <> 'Accepted' OR (balanced AND control_total = 0))");
      table.HasCheckConstraint("ck_tb_import_state",
        "import_state IN ('LOADING', 'SEALED')");
      table.HasCheckConstraint("ck_tb_source_layout",
        "source_layout IN ('SIGNED_NET','DEBIT_CREDIT') AND (import_profile_version IS NOT NULL AND length(trim(import_profile_version)) > 0) AND (basis IS NULL OR length(trim(basis)) > 0)");
    });
    b.Entity<TrialBalanceRow>().HasIndex(x => x.DatasetId);
    b.Entity<TrialBalanceRow>().ToTable("trial_balance_rows", table => table.HasCheckConstraint("ck_tb_row_source_amounts",
      "(source_debit IS NULL OR source_debit >= 0) AND (source_credit IS NULL OR source_credit >= 0)"));
    var journal = b.Entity<AdjustmentJournal>();
    journal.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_adjustment_journals_scope_id");
    journal.HasIndex(x => new { x.FirmId, x.EngagementId, x.BaseDatasetId, x.JournalNumber }).IsUnique();
    journal.Property(x => x.Purpose).HasMaxLength(40);
    journal.Property(x => x.Basis).HasMaxLength(50);
    journal.Property(x => x.Currency).HasMaxLength(3);
    journal.Property(x => x.Origin).HasMaxLength(40);
    journal.Property(x => x.Reason).HasMaxLength(4000);
    journal.Property(x => x.EvidenceReference).HasMaxLength(2000);
    journal.ToTable("adjustment_journals", table => table.HasCheckConstraint("ck_adjustment_journal_values",
      "purpose IN ('CLIENT_BOOK_CORRECTION','REPORTING_ADJUSTMENT','PRESENTATION_RECLASSIFICATION','GROUP_ONLY_ELIMINATION')" +
      " AND origin IN ('AUDIT_PROPOSED','CLIENT_REQUESTED','MANAGEMENT_PROVIDED','IMPORTED')" +
      " AND status IN ('Draft','Posted','ReflectedInSource','Void')" +
      " AND revision >= 1 AND length(trim(journal_number)) > 0 AND length(trim(reason)) <= 4000 AND length(trim(evidence_reference)) <= 2000" +
      " AND (period_id IS NULL OR (length(trim(basis)) > 0 AND currency ~ '^[A-Z]{3}$')) AND (book_id IS NULL OR period_id IS NOT NULL)"));
    // Duplicate-file guard: the same source bytes can never become two datasets for one
    // engagement. Partial so legacy/empty-hash fixtures stay migratable; the import
    // command always stamps a real hash.
    b.Entity<TrialBalanceDataset>().HasIndex(x => new { x.FirmId, x.EngagementId, x.RawFileSha256Hex, x.LegalEntityKey }).IsUnique()
      .HasDatabaseName("ux_dataset_firm_engagement_raw_hash_entity").HasFilter("length(raw_file_sha256_hex) > 0");
    b.Entity<JournalSourceReconciliation>().HasIndex(x => new { x.FirmId, x.EngagementId, x.BaseDatasetId, x.LogicalJournalNumber }).IsUnique()
      .HasDatabaseName("ux_reconciliation_base_journal");
    b.Entity<AdjustmentPlanLine>().HasIndex(x => new { x.PlanId, x.LogicalJournalNumber, x.Layer }).IsUnique()
      .HasDatabaseName("ux_planline_plan_journal_layer");
    b.Entity<DocumentSnapshot>().HasIndex(x => new { x.DocumentReferenceId, x.VersionId }).IsUnique();

    // Composite scope FKs (ND-03): a dataset can only reference a client and engagement
    // that exist inside the same firm. The FK principal keys below create the required
    // unique (firm, id) constraints on engagements and practice_clients.

    b.Entity<TrialBalanceDataset>()
      .HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.EngagementId })
      .HasPrincipalKey(e => new { e.FirmId, e.Id })
      .OnDelete(DeleteBehavior.Restrict);
    b.Entity<TrialBalanceDataset>()
      .HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(c => new { c.FirmId, c.Id })
      .OnDelete(DeleteBehavior.Restrict);
    b.Entity<TrialBalanceDataset>()
      .HasOne<ClientReportingPeriod>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    b.Entity<TrialBalanceDataset>()
      .HasOne<ClientReportingBook>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.BookId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    b.Entity<TrialBalanceDataset>()
      .HasOne<TrialBalanceImportBatch>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ImportBatchId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    b.Entity<TrialBalanceRow>()
      .HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => x.DatasetId)
      .OnDelete(DeleteBehavior.Restrict);
    journal
      .HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => x.BaseDatasetId)
      .OnDelete(DeleteBehavior.Restrict);
    journal
      .HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.EngagementId })
      .HasPrincipalKey(e => new { e.FirmId, e.Id })
      .OnDelete(DeleteBehavior.Restrict);
    journal.HasOne<ClientReportingBook>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.BookId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    journal.HasOne<ClientReportingPeriod>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    b.Entity<AdjustmentLine>()
      .HasOne<AdjustmentJournal>().WithMany()
      .HasForeignKey(x => x.JournalId)
      .OnDelete(DeleteBehavior.Restrict);
    ConfigureAdjustmentBridge(b);
    ConfigureMappingAndFinancialStatements(b);
  }

  // Source bridge (§17.4): reviewer-verified reflection + immutable plans. Database
  // constraints supplement reviewer evidence; they do not prove reflection is correct.
  private static void ConfigureAdjustmentBridge(ModelBuilder b)
  {
    var sourceReconciliation = b.Entity<JournalSourceReconciliation>();
    sourceReconciliation.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_journal_source_reconciliations_scope_id");
    sourceReconciliation.ToTable("journal_source_reconciliations", t =>
    {
      t.HasCheckConstraint("ck_reconciliation_state",
        "state IN ('UNKNOWN','NOT_REFLECTED','REFLECTED','PARTIALLY_REFLECTED','NOT_APPLICABLE')");
      t.HasCheckConstraint("ck_reconciliation_revision", "journal_revision >= 1");
      t.HasCheckConstraint("ck_reconciliation_evidence",
        "(state IN ('REFLECTED','PARTIALLY_REFLECTED') AND length(evidence) > 0 AND length(evidence) <= 2000) OR " +
        "(state NOT IN ('REFLECTED','PARTIALLY_REFLECTED') AND length(evidence) <= 2000)");
      t.HasCheckConstraint("ck_reconciliation_number", "length(logical_journal_number) > 0 AND length(logical_journal_number) <= 32");
    });
    sourceReconciliation
      .HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.EngagementId })
      .HasPrincipalKey(e => new { e.FirmId, e.Id })
      .OnDelete(DeleteBehavior.Restrict);
    sourceReconciliation
      .HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => x.BaseDatasetId)
      .OnDelete(DeleteBehavior.Restrict);
    var decision = b.Entity<AdjustmentJournalManagementDecision>();
    decision.Property(x => x.Decision).HasMaxLength(20);
    decision.Property(x => x.EvidenceMode).HasMaxLength(20);
    decision.Property(x => x.EvidenceReference).HasMaxLength(2000);
    decision.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.JournalId, x.JournalRevision })
      .IsUnique().HasDatabaseName("ux_adjustment_journal_management_decision_revision");
    decision.ToTable("adjustment_journal_management_decisions", t => t.HasCheckConstraint("ck_adjustment_journal_management_decision_values",
      "decision IN ('ACCEPTED','REJECTED','PARTIAL') AND evidence_mode IN ('SIGNED_IN','OFFLINE')" +
      " AND length(trim(evidence_reference)) > 0 AND length(trim(evidence_reference)) <= 2000 AND journal_revision >= 1"));
    decision.HasOne<AdjustmentJournal>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.JournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
      decision.HasOne<AppUser>().WithMany()
        .HasForeignKey(x => new { x.FirmId, x.DecidedByUserId })
        .HasPrincipalKey(x => new { x.FirmId, x.Id })
        .OnDelete(DeleteBehavior.Restrict);
    b.Entity<AdjustmentPlan>().ToTable("adjustment_plans", t =>
    {
      t.HasCheckConstraint("ck_plan_status", "status IN ('Draft','Finalized')");
      t.HasCheckConstraint("ck_plan_result",
        "status = 'Draft' OR (result_hash IS NOT NULL AND result_hash ~ '^[0-9a-f]{64}$')");
    });
    b.Entity<AdjustmentPlan>()
      .HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.EngagementId })
      .HasPrincipalKey(e => new { e.FirmId, e.Id })
      .OnDelete(DeleteBehavior.Restrict);
    b.Entity<AdjustmentPlan>()
      .HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => x.BaseDatasetId)
      .OnDelete(DeleteBehavior.Restrict);
    b.Entity<AdjustmentPlan>().HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_adjustment_plans_firm_id_id");
    b.Entity<AdjustmentPlanLine>().ToTable("adjustment_plan_lines", t =>
    {
      t.HasCheckConstraint("ck_planline_revision", "journal_revision >= 1");
      t.HasCheckConstraint("ck_planline_shape",
        "length(logical_journal_number) > 0 AND length(logical_journal_number) <= 32 AND length(layer) > 0 AND length(layer) <= 32");
      t.HasCheckConstraint("ck_planline_reflection",
        "reflection_state IN ('UNKNOWN','NOT_REFLECTED','REFLECTED','PARTIALLY_REFLECTED','NOT_APPLICABLE')");
    });
    b.Entity<AdjustmentPlanLine>()
      .HasOne<AdjustmentPlan>().WithMany()
      .HasForeignKey(x => x.PlanId)
      .OnDelete(DeleteBehavior.Restrict);
  }

  private static void ConfigureMappingAndFinancialStatements(ModelBuilder b)
  {
    var dataset = b.Entity<TrialBalanceDataset>();
    dataset.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_trial_balance_datasets_scope_id");

    var mapping = b.Entity<MappingVersion>();
    mapping.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_mapping_versions_firm_id_id");
    mapping.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_mapping_versions_scope_id");
    mapping.Property(x => x.TaxonomyVersion).HasMaxLength(100);
    mapping.Property(x => x.PeriodStart).HasMaxLength(10);
    mapping.Property(x => x.PeriodEnd).HasMaxLength(10);
    mapping.Property(x => x.Status).HasMaxLength(16);
    mapping.HasIndex(x => new { x.FirmId, x.EngagementId, x.DatasetId, x.Version })
      .IsUnique().HasDatabaseName("ux_mapping_version_identity");
    mapping.ToTable("mapping_versions", t => t.HasCheckConstraint("ck_mapping_version_values",
      "version >= 1 AND generation >= 1 AND length(taxonomy_version) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND status IN ('DRAFT','APPROVED') AND ((status = 'DRAFT' AND approved_by_user_id IS NULL AND approved_at IS NULL) OR (status = 'APPROVED' AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL))"));
    mapping.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    mapping.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    mapping.HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.DatasetId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    mapping.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    mapping.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var allocation = b.Entity<MappingAllocation>();
    allocation.Property(x => x.SourceAccountCode).HasMaxLength(100);
    allocation.Property(x => x.DestinationCode).HasMaxLength(100);
    allocation.Property(x => x.StatementSection).HasMaxLength(50);
    allocation.Property(x => x.AuditArea).HasMaxLength(100);
    allocation.Property(x => x.Rationale).HasMaxLength(2000);
    allocation.Property(x => x.ResidualPolicy).HasMaxLength(40).HasDefaultValue("LAST_DESTINATION");
    allocation.HasIndex(x => new { x.FirmId, x.MappingVersionId, x.SourceAccountCode, x.DestinationCode })
      .IsUnique().HasDatabaseName("ux_mapping_allocation_identity");
    allocation.ToTable("mapping_allocations", t => t.HasCheckConstraint("ck_mapping_allocation_values",
      "length(source_account_code) > 0 AND length(destination_code) > 0 AND length(statement_section) > 0 AND fraction > 0 AND fraction <= 1 AND length(rationale) > 0 AND residual_policy = 'LAST_DESTINATION'"));
    allocation.HasOne<MappingVersion>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.MappingVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var adjusted = b.Entity<AdjustedTrialBalanceSnapshot>();
    adjusted.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_adjusted_tb_snapshots_firm_id_id");
    adjusted.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_adjusted_tb_snapshots_scope_id");
    adjusted.Property(x => x.Currency).HasMaxLength(3);
    adjusted.Property(x => x.ResultHash).HasMaxLength(64);
    adjusted.HasIndex(x => new { x.FirmId, x.AdjustmentPlanId })
      .IsUnique().HasDatabaseName("ux_adjusted_tb_snapshot_plan");
    adjusted.ToTable("adjusted_tb_snapshots", t => t.HasCheckConstraint("ck_adjusted_tb_snapshot_values",
      "revision >= 1 AND currency ~ '^[A-Z]{3}$' AND result_hash ~ '^[0-9a-f]{64}$'"));
    adjusted.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    adjusted.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    adjusted.HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.BaseDatasetId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    adjusted.HasOne<AdjustmentPlan>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.AdjustmentPlanId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    adjusted.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var adjustedRow = b.Entity<AdjustedTrialBalanceRow>();
    adjustedRow.Property(x => x.AccountCode).HasMaxLength(100);
    adjustedRow.Property(x => x.Currency).HasMaxLength(3);
    adjustedRow.HasIndex(x => new { x.SnapshotId, x.AccountCode }).IsUnique()
      .HasDatabaseName("ux_adjusted_tb_row_account");
    adjustedRow.ToTable("adjusted_tb_rows", t => t.HasCheckConstraint("ck_adjusted_tb_row_values",
      "length(account_code) > 0 AND currency ~ '^[A-Z]{3}$'"));
    adjustedRow.HasOne<AdjustedTrialBalanceSnapshot>().WithMany()
      .HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);

    var package = b.Entity<FinancialPackage>();
    package.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_financial_packages_firm_id_id");
    package.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_financial_packages_scope_id");
    package.Property(x => x.Basis).HasMaxLength(50);
    package.Property(x => x.Framework).HasMaxLength(100);
    package.Property(x => x.PeriodStart).HasMaxLength(10);
    package.Property(x => x.PeriodEnd).HasMaxLength(10);
    package.Property(x => x.TaxonomyVersion).HasMaxLength(100);
    package.Property(x => x.TemplateVersion).HasMaxLength(100);
    package.Property(x => x.CalculationEngineVersion).HasMaxLength(100);
    package.Property(x => x.CalculationHash).HasMaxLength(64);
    package.Property(x => x.Currency).HasMaxLength(3);
    package.Property(x => x.Status).HasMaxLength(20);
    package.Property(x => x.SupplementaryHash).HasMaxLength(64);
    package.Property(x => x.EquityHash).HasMaxLength(64);
    package.Property(x => x.ComparativeBasis).HasMaxLength(100);
    package.Property(x => x.ComparativeEvidenceReference).HasMaxLength(2000);
    package.HasIndex(x => new { x.FirmId, x.AdjustmentPlanId, x.MappingVersionId, x.TemplateVersion })
      .IsUnique().HasDatabaseName("ux_financial_package_identity");
    package.ToTable("financial_packages", t => t.HasCheckConstraint("ck_financial_package_values",
      "revision >= 1 AND generation >= 1 AND length(framework) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND length(taxonomy_version) > 0 AND length(template_version) > 0 AND length(calculation_engine_version) > 0 AND calculation_hash ~ '^[0-9a-f]{64}$' AND currency ~ '^[A-Z]{3}$' AND status IN ('REVIEW_REQUIRED','VALIDATED') AND (period_id IS NULL OR length(trim(basis)) > 0) AND (book_id IS NULL OR period_id IS NOT NULL) AND ((cash_beginning IS NULL AND cash_ending IS NULL AND supplementary_hash IS NULL AND equity_hash IS NULL) OR (cash_beginning IS NOT NULL AND cash_ending IS NOT NULL AND supplementary_hash ~ '^[0-9a-f]{64}$' AND (equity_hash IS NULL OR equity_hash ~ '^[0-9a-f]{64}$')))"));
    package.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    package.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    package.HasOne<AdjustedTrialBalanceSnapshot>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.AdjustedDatasetId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    package.HasOne<MappingVersion>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.MappingVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    package.HasOne<AdjustmentPlan>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.AdjustmentPlanId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    package.HasOne<ClientReportingPeriod>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    package.HasOne<ClientReportingBook>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.BookId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    package.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ComparativePackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var packageArtifact = b.Entity<FinancialPackageArtifact>();
    packageArtifact.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_financial_package_artifacts_scope_id");
    packageArtifact.Property(x => x.PackageHash).HasMaxLength(64);
    packageArtifact.Property(x => x.ArtifactVersion).HasMaxLength(100);
    packageArtifact.Property(x => x.FrameworkVersion).HasMaxLength(100);
    packageArtifact.Property(x => x.TemplateVersion).HasMaxLength(100);
    packageArtifact.Property(x => x.ArtifactSha256Hex).HasMaxLength(64);
    packageArtifact.Property(x => x.ArtifactBytes).HasColumnType("bytea");
    packageArtifact.HasIndex(x => new { x.FirmId, x.FinancialPackageId, x.PackageRevision, x.PackageGeneration, x.ArtifactVersion })
      .IsUnique().HasDatabaseName("ux_financial_package_artifact_version");
    packageArtifact.ToTable("financial_package_artifacts", t => t.HasCheckConstraint("ck_financial_package_artifact_values",
      "package_revision >= 1 AND package_generation >= 1 AND package_hash ~ '^[0-9a-f]{64}$' AND length(trim(artifact_version)) > 0 AND length(trim(framework_version)) > 0 AND length(trim(template_version)) > 0 AND artifact_sha256_hex ~ '^[0-9a-f]{64}$' AND octet_length(artifact_bytes) > 0"));
    packageArtifact.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    packageArtifact.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var packageLine = b.Entity<FinancialPackageLine>();
    packageLine.Property(x => x.SourceAccountCode).HasMaxLength(100);
    packageLine.Property(x => x.DestinationCode).HasMaxLength(100);
    packageLine.Property(x => x.StatementSection).HasMaxLength(50);
    packageLine.Property(x => x.Currency).HasMaxLength(3);
    packageLine.Property(x => x.RoundingResidual).HasPrecision(19, 6);
    packageLine.HasIndex(x => new { x.FirmId, x.FinancialPackageId, x.SourceAccountCode, x.DestinationCode })
      .IsUnique().HasDatabaseName("ux_financial_package_line_identity");
    packageLine.ToTable("financial_package_lines", t => t.HasCheckConstraint("ck_financial_package_line_values",
      "length(source_account_code) > 0 AND length(destination_code) > 0 AND length(statement_section) > 0 AND fraction > 0 AND fraction <= 1 AND currency ~ '^[A-Z]{3}$'"));
    packageLine.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    packageLine.HasOne<AdjustedTrialBalanceSnapshot>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.AdjustedSnapshotId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var validation = b.Entity<FinancialPackageValidation>();
    validation.Property(x => x.Code).HasMaxLength(100);
    validation.Property(x => x.Detail).HasMaxLength(2000);
    validation.HasIndex(x => new { x.FirmId, x.FinancialPackageId, x.Code }).IsUnique()
      .HasDatabaseName("ux_financial_package_validation_code");
    validation.ToTable("financial_package_validations", t => t.HasCheckConstraint("ck_financial_package_validation_values",
      "length(code) > 0 AND length(detail) > 0"));
    validation.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var cashFlow = b.Entity<FinancialPackageCashFlowLine>();
    cashFlow.Property(x => x.Section).HasMaxLength(20);
    cashFlow.Property(x => x.Description).HasMaxLength(2000);
    cashFlow.Property(x => x.Currency).HasMaxLength(3);
    cashFlow.HasIndex(x => new { x.FirmId, x.FinancialPackageId, x.Section, x.Description })
      .IsUnique().HasDatabaseName("ux_financial_package_cash_flow_line_identity");
    cashFlow.ToTable("financial_package_cash_flow_lines", t => t.HasCheckConstraint("ck_financial_package_cash_flow_values",
      "section IN ('OPERATING','INVESTING','FINANCING') AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$'"));
    cashFlow.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var disclosure = b.Entity<FinancialPackageDisclosure>();
    disclosure.Property(x => x.Code).HasMaxLength(100);
    disclosure.Property(x => x.Response).HasMaxLength(4000);
    disclosure.Property(x => x.Rationale).HasMaxLength(2000);
    disclosure.HasIndex(x => new { x.FirmId, x.FinancialPackageId, x.Code })
      .IsUnique().HasDatabaseName("ux_financial_package_disclosure_code");
    disclosure.ToTable("financial_package_disclosures", t => t.HasCheckConstraint("ck_financial_package_disclosure_values",
      "length(trim(code)) > 0 AND ((not_applicable = false AND length(trim(response)) > 0) OR (not_applicable = true AND length(trim(rationale)) > 0))"));
    disclosure.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var equity = b.Entity<FinancialPackageEquityLine>();
    equity.Property(x => x.LineCode).HasMaxLength(100);
    equity.Property(x => x.Description).HasMaxLength(2000);
    equity.Property(x => x.Currency).HasMaxLength(3);
    equity.Property(x => x.EvidenceReference).HasMaxLength(2000);
    equity.HasIndex(x => new { x.FirmId, x.FinancialPackageId, x.LineCode })
      .IsUnique().HasDatabaseName("ux_financial_package_equity_line_identity");
    equity.ToTable("financial_package_equity_lines", t => t.HasCheckConstraint("ck_financial_package_equity_line_values",
      "length(trim(line_code)) > 0 AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(evidence_reference)) > 0"));
    equity.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var noteLine = b.Entity<FinancialPackageNoteLine>();
    noteLine.Property(x => x.NoteCode).HasMaxLength(100);
    noteLine.Property(x => x.FaceDestinationCode).HasMaxLength(100);
    noteLine.Property(x => x.Currency).HasMaxLength(3);
    noteLine.Property(x => x.EvidenceReference).HasMaxLength(2000);
    noteLine.HasIndex(x => new { x.FirmId, x.FinancialPackageId, x.NoteCode, x.FaceDestinationCode })
      .IsUnique().HasDatabaseName("ux_financial_package_note_line_identity");
    noteLine.ToTable("financial_package_note_lines", t => t.HasCheckConstraint("ck_financial_package_note_line_values",
      "length(trim(note_code)) > 0 AND length(trim(face_destination_code)) > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(evidence_reference)) > 0"));
    noteLine.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var packageReview = b.Entity<FinancialPackageReviewDecision>();
    packageReview.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_financial_package_review_decisions_scope_id");
    packageReview.Property(x => x.PackageHash).HasMaxLength(64);
    packageReview.Property(x => x.ArtifactVersion).HasMaxLength(100);
    packageReview.Property(x => x.ArtifactSha256Hex).HasMaxLength(64);
    packageReview.Property(x => x.Stage).HasMaxLength(40);
    packageReview.Property(x => x.Decision).HasMaxLength(30);
    packageReview.Property(x => x.EvidenceMode).HasMaxLength(20);
    packageReview.Property(x => x.EvidenceReference).HasMaxLength(2000);
    packageReview.Property(x => x.Comment).HasMaxLength(4000);
    packageReview.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId, x.Stage, x.DecidedAt })
      .HasDatabaseName("ix_financial_package_review_stage");
    packageReview.ToTable("financial_package_review_decisions", t => t.HasCheckConstraint("ck_financial_package_review_values",
      "package_revision >= 1 AND package_generation >= 1 AND package_hash ~ '^[0-9a-f]{64}$'" +
      " AND artifact_sha256_hex ~ '^[0-9a-f]{64}$' AND length(trim(artifact_version)) > 0" +
      " AND stage IN ('MANAGEMENT_APPROVAL','ACCOUNTING_REVIEW','PARTNER_APPROVAL')" +
      " AND decision IN ('APPROVED','CHANGES_REQUIRED','REJECTED')" +
      " AND evidence_mode IN ('SIGNED_IN','OFFLINE') AND length(trim(evidence_reference)) > 0" +
      " AND ((evidence_mode = 'SIGNED_IN' AND decided_by_user_id IS NOT NULL) OR evidence_mode = 'OFFLINE')"));
    ScopeToEngagement(packageReview, nameof(FinancialPackageReviewDecision.FirmId), nameof(FinancialPackageReviewDecision.ClientId), nameof(FinancialPackageReviewDecision.EngagementId));
    packageReview.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    packageReview.HasOne<FinancialPackageArtifact>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageArtifactId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    packageReview.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.DecidedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    b.Entity<QuestionnaireTemplate>(entity =>
    {
      entity.ToTable("questionnaire_templates");
      entity.HasKey(x => x.Id);
      entity.HasIndex(x => new { x.Bank, x.Version }).IsUnique();
    });

    b.Entity<QuestionDefinition>(entity =>
    {
      entity.ToTable("question_definitions");
      entity.HasKey(x => x.Id);
      entity.HasIndex(x => new { x.TemplateId, x.QuestionCode }).IsUnique();
      entity.HasOne<QuestionnaireTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
    });
  }

  private static void ConfigureClientAccounting(ModelBuilder b)
  {
    var capability = b.Entity<AccountingCapabilityProfile>();
    capability.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("ak_accounting_capability_profiles_firm_id_id");
    capability.Property(x => x.ServiceKind).HasMaxLength(40);
    capability.Property(x => x.Framework).HasMaxLength(100);
    capability.Property(x => x.Edition).HasMaxLength(100);
    capability.Property(x => x.PeriodRule).HasMaxLength(100);
    capability.Property(x => x.ReportingCurrency).HasMaxLength(3);
    capability.Property(x => x.AccountingMethod).HasMaxLength(100);
    capability.Property(x => x.ConsolidationMethod).HasMaxLength(100);
    capability.Property(x => x.ReviewHierarchy).HasMaxLength(200);
    capability.Property(x => x.TemplateFamily).HasMaxLength(100);
    capability.Property(x => x.Status).HasMaxLength(30);
    capability.Property(x => x.AcceptanceState).HasMaxLength(40);
    capability.HasIndex(x => new { x.FirmId, x.ClientId, x.GroupId, x.Revision }).IsUnique()
      .HasDatabaseName("ux_accounting_capability_profile_scope_revision");
    capability.ToTable("accounting_capability_profiles", t => t.HasCheckConstraint("ck_accounting_capability_profile_scope",
      "((client_id IS NOT NULL) <> (group_id IS NOT NULL)) AND length(trim(service_kind)) > 0 AND length(trim(framework)) > 0 AND reporting_currency ~ '^[A-Z]{3}$'"));
    capability.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    capability.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var acceptance = b.Entity<AccountingCapabilityAcceptance>();
    acceptance.Property(x => x.Stage).HasMaxLength(40);
    acceptance.Property(x => x.Status).HasMaxLength(30);
    acceptance.Property(x => x.EvidenceReference).HasMaxLength(2000);
    acceptance.HasIndex(x => new { x.FirmId, x.CapabilityProfileId, x.Stage }).IsUnique()
      .HasDatabaseName("ux_accounting_capability_acceptance_stage");
    acceptance.HasOne<AccountingCapabilityProfile>().WithMany().HasForeignKey(x => new { x.FirmId, x.CapabilityProfileId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var profile = b.Entity<ClientAccountingProfile>();
    profile.Property(x => x.Jurisdiction).HasMaxLength(100);
    profile.Property(x => x.FunctionalCurrency).HasMaxLength(3);
    profile.Property(x => x.SourceSystem).HasMaxLength(100);
    profile.Property(x => x.SourceSystemIdentifier).HasMaxLength(200);
    profile.Property(x => x.Status).HasMaxLength(30);
    profile.HasIndex(x => new { x.FirmId, x.ClientId }).IsUnique().HasDatabaseName("ux_client_accounting_profile_client");
    profile.ToTable("client_accounting_profiles", t => t.HasCheckConstraint("ck_client_accounting_profile_values",
      "length(trim(jurisdiction)) > 0 AND functional_currency ~ '^[A-Z]{3}$' AND fiscal_year_start_month BETWEEN 1 AND 12 AND fiscal_year_start_day BETWEEN 1 AND 31"));
    profile.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var group = b.Entity<ClientGroup>();
    group.Property(x => x.Code).HasMaxLength(100);
    group.Property(x => x.Name).HasMaxLength(300);
    group.Property(x => x.Status).HasMaxLength(30);
    group.HasIndex(x => new { x.FirmId, x.Code }).IsUnique().HasDatabaseName("ux_client_group_code");
    group.ToTable("client_groups", t => t.HasCheckConstraint("ck_client_group_values",
      "length(trim(code)) > 0 AND length(trim(name)) > 0"));

    var membership = b.Entity<ClientGroupMembership>();
    membership.Property(x => x.ControlMethod).HasMaxLength(100);
    membership.Property(x => x.EvidenceReference).HasMaxLength(2000);
    membership.Property(x => x.Status).HasMaxLength(30);
    membership.HasIndex(x => new { x.FirmId, x.GroupId, x.ClientId, x.EffectiveFrom }).IsUnique()
      .HasDatabaseName("ux_client_group_membership_effective");
    membership.ToTable("client_group_memberships", t => t.HasCheckConstraint("ck_client_group_membership_values",
      "effective_to IS NULL OR effective_from <= effective_to" +
      " AND ownership_percent >= 0 AND ownership_percent <= 100" +
      " AND economic_interest_percent >= 0 AND economic_interest_percent <= 100"));
    membership.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    membership.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var groupAccess = b.Entity<GroupAccessGrant>();
    groupAccess.Property(x => x.Role).HasMaxLength(50);
    groupAccess.HasIndex(x => new { x.FirmId, x.GroupId, x.UserId, x.Role }).IsUnique()
      .HasDatabaseName("ux_group_access_grant");
    groupAccess.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    groupAccess.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.UserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var period = b.Entity<ClientReportingPeriod>();
    period.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id }).HasName("ak_client_reporting_periods_scope_id");
    period.Property(x => x.PeriodCode).HasMaxLength(50);
    period.Property(x => x.Basis).HasMaxLength(50);
    period.Property(x => x.Currency).HasMaxLength(3);
    period.Property(x => x.Status).HasMaxLength(30);
    period.Property(x => x.CloseReason).HasMaxLength(2000);
    period.HasIndex(x => new { x.FirmId, x.ClientId, x.PeriodCode, x.Basis }).IsUnique()
      .HasDatabaseName("ux_client_reporting_period_identity");
    period.ToTable("client_reporting_periods", t => t.HasCheckConstraint("ck_client_reporting_period_values",
      "start_date <= end_date AND length(trim(period_code)) > 0 AND length(trim(basis)) > 0 AND currency ~ '^[A-Z]{3}$'"));
    period.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    period.HasOne<ClientReportingPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.PriorPeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var amendment = b.Entity<ClientPeriodAmendment>();
    amendment.Property(x => x.Reason).HasMaxLength(4000);
    amendment.HasIndex(x => new { x.FirmId, x.ClientId, x.PeriodId, x.AmendmentRevision }).IsUnique()
      .HasDatabaseName("ux_client_period_amendment_revision");
    amendment.ToTable("client_period_amendments", t => t.HasCheckConstraint("ck_client_period_amendment_values",
      "previous_revision >= 1 AND amendment_revision = previous_revision + 1 AND length(trim(reason)) > 0"));
    amendment.HasOne<ClientReportingPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    amendment.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var book = b.Entity<ClientReportingBook>();
    book.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id }).HasName("ak_client_reporting_books_scope_id");
    book.Property(x => x.Code).HasMaxLength(50);
    book.Property(x => x.Basis).HasMaxLength(50);
    book.Property(x => x.InclusionRule).HasMaxLength(100);
    book.Property(x => x.Currency).HasMaxLength(3);
    book.Property(x => x.Status).HasMaxLength(30);
    book.HasIndex(x => new { x.FirmId, x.ClientId, x.PeriodId, x.Code }).IsUnique()
      .HasDatabaseName("ux_client_reporting_book_identity");
    book.ToTable("client_reporting_books", t => t.HasCheckConstraint("ck_client_reporting_book_values",
      "length(trim(code)) > 0 AND length(trim(basis)) > 0 AND length(trim(inclusion_rule)) > 0 AND currency ~ '^[A-Z]{3}$'"));
    book.HasOne<ClientReportingPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var opening = b.Entity<OpeningBalanceBridge>();
    opening.Property(x => x.SourceHash).HasMaxLength(64);
    opening.Property(x => x.Status).HasMaxLength(30);
    opening.Property(x => x.EvidenceReference).HasMaxLength(2000);
    opening.HasIndex(x => new { x.FirmId, x.ClientId, x.CurrentPeriodId }).IsUnique()
      .HasDatabaseName("ux_opening_balance_bridge_period");
    opening.ToTable("opening_balance_bridges", t => t.HasCheckConstraint("ck_opening_balance_bridge_values",
      "length(trim(status)) > 0"));
    opening.HasOne<ClientReportingPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.CurrentPeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var restatement = b.Entity<ClientPeriodRestatement>();
    restatement.Property(x => x.OriginalPackageHash).HasMaxLength(64);
    restatement.Property(x => x.RevisedPackageHash).HasMaxLength(64);
    restatement.Property(x => x.RevisedBasis).HasMaxLength(100);
    restatement.Property(x => x.Reason).HasMaxLength(4000);
    restatement.Property(x => x.EvidenceReference).HasMaxLength(2000);
    restatement.Property(x => x.Status).HasMaxLength(30);
    restatement.HasIndex(x => new { x.FirmId, x.OriginalPackageId, x.RevisedPackageId }).IsUnique()
      .HasDatabaseName("ux_client_period_restatement_packages");
    restatement.ToTable("client_period_restatements", t => t.HasCheckConstraint("ck_client_period_restatement_values",
      "original_package_id <> revised_package_id AND original_package_hash ~ '^[0-9a-f]{64}$' AND revised_package_hash ~ '^[0-9a-f]{64}$' AND length(trim(revised_basis)) > 0 AND length(trim(reason)) > 0 AND length(trim(evidence_reference)) > 0 AND status IN ('SUBMITTED','APPROVED','REJECTED') AND ((status = 'SUBMITTED' AND approved_by_user_id IS NULL AND approved_at IS NULL) OR (status = 'APPROVED' AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL))"));
    restatement.HasOne<ClientReportingPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    restatement.HasOne<FinancialPackage>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.OriginalPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    restatement.HasOne<FinancialPackage>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.RevisedPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var chart = b.Entity<ClientChartVersion>();
    chart.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id }).HasName("ak_client_chart_versions_scope_id");
    chart.Property(x => x.SourceScope).HasMaxLength(100);
    chart.Property(x => x.Status).HasMaxLength(30);
    chart.HasIndex(x => new { x.FirmId, x.ClientId, x.Version }).IsUnique().HasDatabaseName("ux_client_chart_version_number");
    chart.ToTable("client_chart_versions", t => t.HasCheckConstraint("ck_client_chart_version_values",
      "effective_to IS NULL OR effective_from <= effective_to"));
    chart.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var account = b.Entity<ClientAccount>();
    account.Property(x => x.StableIdentity).HasMaxLength(100);
    account.Property(x => x.AccountCode).HasMaxLength(100);
    account.Property(x => x.AccountName).HasMaxLength(300);
    account.Property(x => x.AccountType).HasMaxLength(50);
    account.Property(x => x.NormalBalance).HasMaxLength(20);
    account.Property(x => x.Status).HasMaxLength(30);
    account.HasIndex(x => new { x.FirmId, x.ClientId, x.ChartVersionId, x.AccountCode }).IsUnique()
      .HasDatabaseName("ux_client_account_chart_code");
    account.HasIndex(x => new { x.FirmId, x.ClientId, x.StableIdentity }).IsUnique()
      .HasDatabaseName("ux_client_account_stable_identity");
    account.ToTable("client_accounts", t => t.HasCheckConstraint("ck_client_account_values",
      "length(trim(stable_identity)) > 0 AND length(trim(account_code)) > 0 AND length(trim(account_name)) > 0 AND length(trim(account_type)) > 0"));
    account.HasOne<ClientChartVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ChartVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    account.HasOne<ClientAccount>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ParentAccountId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var alias = b.Entity<SourceAccountAlias>();
    alias.Property(x => x.SourceSystem).HasMaxLength(100);
    alias.Property(x => x.AliasCode).HasMaxLength(100);
    alias.Property(x => x.AliasName).HasMaxLength(300);
    alias.HasIndex(x => new { x.FirmId, x.ClientId, x.ChartVersionId, x.SourceSystem, x.AliasCode }).IsUnique()
      .HasDatabaseName("ux_source_account_alias");
    alias.HasOne<ClientChartVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ChartVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    alias.HasOne<ClientAccount>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ClientAccountId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var taxonomy = b.Entity<ReportingTaxonomyVersion>();
    taxonomy.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("ak_reporting_taxonomy_versions_firm_id_id");
    taxonomy.Property(x => x.OverlayScope).HasMaxLength(200);
    taxonomy.Property(x => x.Code).HasMaxLength(100);
    taxonomy.Property(x => x.Framework).HasMaxLength(100);
    taxonomy.Property(x => x.Name).HasMaxLength(300);
    taxonomy.Property(x => x.Status).HasMaxLength(30);
    taxonomy.HasIndex(x => new { x.FirmId, x.Code }).IsUnique().HasDatabaseName("ux_reporting_taxonomy_code");
    taxonomy.ToTable("reporting_taxonomy_versions", t => t.HasCheckConstraint("ck_reporting_taxonomy_values",
      "length(trim(code)) > 0 AND length(trim(framework)) > 0 AND length(trim(name)) > 0 AND (overlay_scope = 'BASE' OR overlay_scope LIKE 'INDUSTRY:%' OR overlay_scope LIKE 'GROUP:%') AND (effective_to IS NULL OR effective_from <= effective_to)"));
    taxonomy.HasOne<ReportingTaxonomyVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.BaseTaxonomyVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var taxonomyNode = b.Entity<ReportingTaxonomyNode>();
    taxonomyNode.Property(x => x.Code).HasMaxLength(100);
    taxonomyNode.Property(x => x.Name).HasMaxLength(300);
    taxonomyNode.Property(x => x.StatementSection).HasMaxLength(50);
    taxonomyNode.Property(x => x.DisplaySign).HasMaxLength(20);
    taxonomyNode.Property(x => x.NormalBalance).HasMaxLength(20);
    taxonomyNode.Property(x => x.DisclosureArea).HasMaxLength(100);
    taxonomyNode.Property(x => x.Applicability).HasMaxLength(100);
    taxonomyNode.HasIndex(x => new { x.FirmId, x.TaxonomyVersionId, x.Code }).IsUnique()
      .HasDatabaseName("ux_reporting_taxonomy_node_code");
    taxonomyNode.HasOne<ReportingTaxonomyVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.TaxonomyVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    taxonomyNode.HasOne<ReportingTaxonomyNode>().WithMany().HasForeignKey(x => new { x.FirmId, x.TaxonomyVersionId, x.ParentNodeId })
      .HasPrincipalKey(x => new { x.FirmId, x.TaxonomyVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    taxonomyNode.HasAlternateKey(x => new { x.FirmId, x.TaxonomyVersionId, x.Id }).HasName("ak_reporting_taxonomy_nodes_scope_id");

    var dimension = b.Entity<ClientAccountingDimensionDefinition>();
    dimension.Property(x => x.DimensionType).HasMaxLength(40);
    dimension.Property(x => x.Code).HasMaxLength(100);
    dimension.Property(x => x.Name).HasMaxLength(300);
    dimension.Property(x => x.Status).HasMaxLength(30);
    dimension.HasIndex(x => new { x.FirmId, x.ClientId, x.DimensionType, x.Code }).IsUnique()
      .HasDatabaseName("ux_client_accounting_dimension_definition");
    dimension.ToTable("client_accounting_dimension_definitions", t => t.HasCheckConstraint("ck_client_accounting_dimension_values",
      "dimension_type IN ('BRANCH','COST_CENTRE','DEPARTMENT','PROJECT','INTERCOMPANY_COUNTERPARTY') AND length(trim(code)) > 0 AND length(trim(name)) > 0"));
    dimension.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var import = b.Entity<SourceImportBatch>();
    import.Property(x => x.SourceKind).HasMaxLength(30);
    import.Property(x => x.ProfileVersion).HasMaxLength(100);
    import.Property(x => x.ParserVersion).HasMaxLength(100);
    import.Property(x => x.RawFileSha256Hex).HasMaxLength(64);
    import.Property(x => x.NormalizedDatasetDigest).HasMaxLength(64);
    import.Property(x => x.LegalEntityKey).HasMaxLength(200);
    import.Property(x => x.Currency).HasMaxLength(3);
    import.Property(x => x.Status).HasMaxLength(20);
    import.Property(x => x.ReceiptReference).HasMaxLength(300);
    import.HasIndex(x => new { x.FirmId, x.EngagementId, x.RawFileSha256Hex }).IsUnique()
      .HasDatabaseName("ux_source_import_raw_hash").HasFilter("length(raw_file_sha256_hex) > 0");
    import.ToTable("source_import_batches", t => t.HasCheckConstraint("ck_source_import_batch_values",
      "length(trim(source_kind)) > 0 AND length(trim(profile_version)) > 0 AND length(trim(parser_version)) > 0 AND currency ~ '^[A-Z]{3}$'" +
      " AND expected_chunk_count >= 0 AND expected_transaction_count >= 0 AND expected_line_count >= 0" +
      " AND accepted_chunk_count >= 0 AND accepted_transaction_count >= 0 AND accepted_line_count >= 0" +
      " AND accepted_chunk_count <= expected_chunk_count AND accepted_transaction_count <= expected_transaction_count" +
      " AND accepted_line_count <= expected_line_count AND status IN ('LOADING','SEALED','REJECTED')"));
    ScopeToEngagement(import, nameof(SourceImportBatch.FirmId), nameof(SourceImportBatch.ClientId), nameof(SourceImportBatch.EngagementId));

    var glChunk = b.Entity<GeneralLedgerImportChunk>();
    glChunk.Property(x => x.ChunkDigest).HasMaxLength(64);
    glChunk.HasIndex(x => new { x.FirmId, x.ImportBatchId, x.ChunkNumber }).IsUnique()
      .HasDatabaseName("ux_gl_import_chunk_number");
    glChunk.HasIndex(x => new { x.FirmId, x.ImportBatchId, x.ChunkDigest }).IsUnique()
      .HasDatabaseName("ux_gl_import_chunk_digest");
    glChunk.ToTable("general_ledger_import_chunks", t => t.HasCheckConstraint("ck_gl_import_chunk_values",
      "chunk_number >= 0 AND chunk_digest ~ '^[0-9a-f]{64}$' AND transaction_count > 0 AND line_count > 0"));
    ScopeToEngagement(glChunk, nameof(GeneralLedgerImportChunk.FirmId), nameof(GeneralLedgerImportChunk.ClientId), nameof(GeneralLedgerImportChunk.EngagementId));
    glChunk.HasOne<SourceImportBatch>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ImportBatchId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var glTransaction = b.Entity<GeneralLedgerTransaction>();
    glTransaction.Property(x => x.StableJournalId).HasMaxLength(200);
    glTransaction.Property(x => x.DocumentNumber).HasMaxLength(200);
    glTransaction.Property(x => x.SourceUser).HasMaxLength(200);
    glTransaction.Property(x => x.SourceSystem).HasMaxLength(100);
    glTransaction.Property(x => x.ReversalReference).HasMaxLength(200);
    glTransaction.Property(x => x.Currency).HasMaxLength(3);
    glTransaction.HasIndex(x => new { x.FirmId, x.ImportBatchId, x.StableJournalId }).IsUnique()
      .HasDatabaseName("ux_gl_transaction_stable_journal");
    ScopeToEngagement(glTransaction, nameof(GeneralLedgerTransaction.FirmId), nameof(GeneralLedgerTransaction.ClientId), nameof(GeneralLedgerTransaction.EngagementId));
    glTransaction.HasOne<SourceImportBatch>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ImportBatchId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    glTransaction.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).HasName("ak_gl_transactions_scope_id");

    var glLine = b.Entity<GeneralLedgerLine>();
    glLine.Property(x => x.StableLineId).HasMaxLength(200);
    glLine.Property(x => x.AccountCode).HasMaxLength(100);
    glLine.Property(x => x.OriginalCurrency).HasMaxLength(3);
    glLine.Property(x => x.PartyIdentifier).HasMaxLength(200);
    glLine.Property(x => x.Branch).HasMaxLength(100);
    glLine.Property(x => x.CostCentre).HasMaxLength(100);
    glLine.Property(x => x.Department).HasMaxLength(100);
    glLine.Property(x => x.Project).HasMaxLength(100);
    glLine.Property(x => x.IntercompanyCounterparty).HasMaxLength(200);
    glLine.HasIndex(x => new { x.FirmId, x.ImportBatchId, x.StableLineId }).IsUnique()
      .HasDatabaseName("ux_gl_line_stable_line");
    glLine.ToTable("general_ledger_lines", t => t.HasCheckConstraint("ck_gl_line_values",
      "length(trim(account_code)) > 0 AND debit >= 0 AND credit >= 0 AND NOT (debit > 0 AND credit > 0) AND original_currency ~ '^[A-Z]{3}$'"));
    ScopeToEngagement(glLine, nameof(GeneralLedgerLine.FirmId), nameof(GeneralLedgerLine.ClientId), nameof(GeneralLedgerLine.EngagementId));
    glLine.HasOne<GeneralLedgerTransaction>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.TransactionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var completeness = b.Entity<GeneralLedgerCompletenessBridge>();
    completeness.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("ak_gl_completeness_bridges_scope_id");
    completeness.Property(x => x.TrialBalanceHash).HasMaxLength(64);
    completeness.Property(x => x.GeneralLedgerHash).HasMaxLength(64);
    completeness.Property(x => x.OpeningTrialBalanceHash).HasMaxLength(64);
    completeness.Property(x => x.AccountResidualDigest).HasMaxLength(64);
    completeness.Property(x => x.OpeningMovementResidualDigest).HasMaxLength(64);
    completeness.Property(x => x.Status).HasMaxLength(30);
    completeness.Property(x => x.EvidenceReference).HasMaxLength(2000);
    completeness.Property(x => x.CompletenessDisclosure).HasMaxLength(2000);
    completeness.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.TrialBalanceDatasetId, x.ImportBatchId })
      .IsUnique().HasDatabaseName("ux_gl_completeness_bridge_input");
    completeness.ToTable("general_ledger_completeness_bridges", t => t.HasCheckConstraint("ck_gl_completeness_bridge_values",
      "trial_balance_hash ~ '^[0-9a-f]{64}$' AND general_ledger_hash ~ '^[0-9a-f]{64}$' AND account_residual_digest ~ '^[0-9a-f]{64}$'" +
      " AND trial_balance_account_count > 0 AND general_ledger_account_count > 0 AND matched_account_count >= 0" +
      " AND mismatched_account_count >= 0 AND absolute_residual >= 0 AND coverage_start <= coverage_end" +
      " AND length(trim(evidence_reference)) > 0 AND status IN ('RECONCILED','UNRECONCILED','APPROVED','REJECTED')" +
      " AND ((status IN ('APPROVED','REJECTED') AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL)" +
      " OR status IN ('RECONCILED','UNRECONCILED'))"));
    ScopeToEngagement(completeness, nameof(GeneralLedgerCompletenessBridge.FirmId), nameof(GeneralLedgerCompletenessBridge.ClientId), nameof(GeneralLedgerCompletenessBridge.EngagementId));
    completeness.HasOne<ClientReportingPeriod>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    completeness.HasOne<ClientReportingBook>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.BookId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    completeness.HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.TrialBalanceDatasetId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    completeness.HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.OpeningTrialBalanceDatasetId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    completeness.HasOne<SourceImportBatch>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ImportBatchId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    completeness.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    completeness.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    ConfigureClientAccountingSchedules(b);
    ConfigureConsolidation(b);
  }

  private static void ConfigureClientAccountingSchedules(ModelBuilder b)
  {
    var reconciliation = b.Entity<AccountingReconciliation>();
    reconciliation.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).HasName("ak_accounting_reconciliations_scope_id");
    reconciliation.Property(x => x.Area).HasMaxLength(80);
    reconciliation.Property(x => x.AccountSelection).HasMaxLength(2000);
    reconciliation.Property(x => x.AgingBasis).HasMaxLength(30);
    reconciliation.Property(x => x.AgingBucketRuleVersion).HasMaxLength(60);
    reconciliation.Property(x => x.SourceHash).HasMaxLength(64);
    reconciliation.Property(x => x.Status).HasMaxLength(30);
    reconciliation.HasIndex(x => new { x.FirmId, x.EngagementId, x.PeriodId, x.Area }).HasDatabaseName("ix_accounting_reconciliation_area");
    reconciliation.ToTable("accounting_reconciliations", t => t.HasCheckConstraint("ck_accounting_reconciliation_values",
      "length(trim(area)) > 0 AND length(trim(account_selection)) > 0"));
    ScopeToEngagement(reconciliation, nameof(AccountingReconciliation.FirmId), nameof(AccountingReconciliation.ClientId), nameof(AccountingReconciliation.EngagementId));

    var reconItem = b.Entity<AccountingReconciliationItem>();
    reconItem.Property(x => x.StableItemId).HasMaxLength(200);
    reconItem.Property(x => x.Currency).HasMaxLength(3);
    reconItem.Property(x => x.DateBasis).HasMaxLength(30);
    reconItem.Property(x => x.AgingBucket).HasMaxLength(30);
    reconItem.Property(x => x.SettlementReference).HasMaxLength(2000);
    reconItem.Property(x => x.Reason).HasMaxLength(1000);
    reconItem.Property(x => x.EvidenceReference).HasMaxLength(2000);
    reconItem.Property(x => x.Disposition).HasMaxLength(100);
    reconItem.HasIndex(x => new { x.FirmId, x.ReconciliationId, x.StableItemId }).IsUnique()
      .HasDatabaseName("ux_accounting_reconciliation_item");
    ScopeToEngagement(reconItem, nameof(AccountingReconciliationItem.FirmId), nameof(AccountingReconciliationItem.ClientId), nameof(AccountingReconciliationItem.EngagementId));
    reconItem.HasOne<AccountingReconciliation>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ReconciliationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var ecl = b.Entity<EclAssessment>();
    ecl.Property(x => x.Method).HasMaxLength(100);
    ecl.Property(x => x.MethodologyVersion).HasMaxLength(100);
    ecl.Property(x => x.ReconciliationSourceHash).HasMaxLength(64);
    ecl.Property(x => x.AssumptionsHash).HasMaxLength(64);
    ecl.Property(x => x.Status).HasMaxLength(30);
    ecl.HasIndex(x => new { x.FirmId, x.ReconciliationId, x.Version }).IsUnique().HasDatabaseName("ux_ecl_assessment_version");
    ecl.ToTable("ecl_assessments", t => t.HasCheckConstraint("ck_ecl_assessment_values",
      "length(trim(method)) > 0 AND length(trim(methodology_version)) > 0 AND eligible_exposure >= 0 AND probability_of_default BETWEEN 0 AND 1 AND loss_given_default BETWEEN 0 AND 1 AND booked_amount >= 0"));
    ScopeToEngagement(ecl, nameof(EclAssessment.FirmId), nameof(EclAssessment.ClientId), nameof(EclAssessment.EngagementId));
    ecl.HasOne<AccountingReconciliation>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ReconciliationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    ecl.HasOne<AdjustmentJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProposedJournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var inventory = b.Entity<InventoryValuationAssessment>();
    inventory.Property(x => x.ReconciliationSourceHash).HasMaxLength(64);
    inventory.Property(x => x.MethodologyVersion).HasMaxLength(100);
    inventory.Property(x => x.AssumptionsHash).HasMaxLength(64);
    inventory.Property(x => x.Status).HasMaxLength(30);
    inventory.HasIndex(x => new { x.FirmId, x.ReconciliationId, x.Version }).IsUnique().HasDatabaseName("ux_inventory_valuation_version");
    inventory.ToTable("inventory_valuation_assessments", t => t.HasCheckConstraint("ck_inventory_valuation_values",
      "quantity >= 0 AND unit_cost >= 0 AND nrv_per_unit >= 0 AND obsolescence_reserve >= 0 AND book_amount >= 0"));
    ScopeToEngagement(inventory, nameof(InventoryValuationAssessment.FirmId), nameof(InventoryValuationAssessment.ClientId), nameof(InventoryValuationAssessment.EngagementId));
    inventory.HasOne<AccountingReconciliation>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ReconciliationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    inventory.HasOne<AdjustmentJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProposedJournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var specialist = b.Entity<SpecialistAccountingSchedule>();
    specialist.Property(x => x.Area).HasMaxLength(80);
    specialist.Property(x => x.MethodologyVersion).HasMaxLength(100);
    specialist.Property(x => x.DepreciationMethod).HasMaxLength(80);
    specialist.Property(x => x.PayrollContractReference).HasMaxLength(200);
    specialist.Property(x => x.PayrollBankPaymentReference).HasMaxLength(200);
    specialist.Property(x => x.LoanCovenantReference).HasMaxLength(200);
    specialist.Property(x => x.RelatedPartyDisclosureReference).HasMaxLength(200);
    specialist.Property(x => x.TaxJurisdiction).HasMaxLength(100);
    specialist.Property(x => x.TaxRuleVersion).HasMaxLength(100);
    specialist.Property(x => x.TaxReturnEvidenceReference).HasMaxLength(200);
    specialist.Property(x => x.TaxPaymentEvidenceReference).HasMaxLength(200);
    specialist.Property(x => x.TaxCorrespondenceReference).HasMaxLength(200);
    specialist.Property(x => x.ForecastOwner).HasMaxLength(200);
    specialist.Property(x => x.ForecastSensitivityReference).HasMaxLength(200);
    specialist.Property(x => x.ForecastSensitivityResult).HasMaxLength(4000);
    specialist.Property(x => x.AssumptionsHash).HasMaxLength(64);
    specialist.Property(x => x.EvidenceReference).HasMaxLength(2000);
    specialist.Property(x => x.ReviewConclusion).HasMaxLength(4000);
    specialist.Property(x => x.Status).HasMaxLength(30);
    specialist.HasIndex(x => new { x.FirmId, x.EngagementId, x.PeriodId, x.Area }).HasDatabaseName("ix_specialist_schedule_area");
    specialist.ToTable("specialist_accounting_schedules", t => t.HasCheckConstraint("ck_specialist_schedule_values",
      "length(trim(area)) > 0 AND length(trim(methodology_version)) > 0 AND (area <> 'ASSETS' OR (length(trim(depreciation_method)) > 0 AND useful_life_months > 0)) AND (payroll_gross_amount IS NULL OR payroll_gross_amount >= 0) AND (payroll_deductions_amount IS NULL OR payroll_deductions_amount >= 0) AND (payroll_net_amount IS NULL OR payroll_net_amount >= 0) AND (loan_repayment_amount IS NULL OR loan_repayment_amount >= 0) AND (tax_base_amount IS NULL OR tax_base_amount >= 0) AND (tax_rate IS NULL OR tax_rate >= 0) AND (forecast_cash_input_amount IS NULL OR forecast_cash_input_amount >= 0) AND (forecast_debt_input_amount IS NULL OR forecast_debt_input_amount >= 0)"));
    ScopeToEngagement(specialist, nameof(SpecialistAccountingSchedule.FirmId), nameof(SpecialistAccountingSchedule.ClientId), nameof(SpecialistAccountingSchedule.EngagementId));

    var analysis = b.Entity<AnalyticalReview>();
    analysis.Property(x => x.Area).HasMaxLength(80);
    analysis.Property(x => x.Measure).HasMaxLength(100);
    analysis.Property(x => x.Currency).HasMaxLength(3);
    analysis.Property(x => x.DenominatorBasis).HasMaxLength(200);
    analysis.Property(x => x.FormulaVersion).HasMaxLength(100);
    analysis.Property(x => x.MovementFlags).HasMaxLength(200);
    analysis.Property(x => x.SeasonalityExplanation).HasMaxLength(2000);
    analysis.Property(x => x.InputSnapshotJson).HasMaxLength(8000);
    analysis.Property(x => x.InputHash).HasMaxLength(64);
    analysis.Property(x => x.Explanation).HasMaxLength(4000);
    analysis.Property(x => x.ReviewConclusion).HasMaxLength(4000);
    analysis.Property(x => x.Status).HasMaxLength(30);
    analysis.HasIndex(x => new { x.FirmId, x.EngagementId, x.PeriodId, x.Area, x.Measure }).HasDatabaseName("ix_analytical_review_measure");
    analysis.ToTable("analytical_reviews", t => t.HasCheckConstraint("ck_analytical_review_values",
      "length(trim(area)) > 0 AND length(trim(measure)) > 0 AND length(trim(currency)) = 3 AND length(trim(denominator_basis)) > 0 AND length(trim(formula_version)) > 0"));
    ScopeToEngagement(analysis, nameof(AnalyticalReview.FirmId), nameof(AnalyticalReview.ClientId), nameof(AnalyticalReview.EngagementId));

    var flag = b.Entity<JournalRiskFlag>();
    flag.Property(x => x.RuleCode).HasMaxLength(100);
    flag.Property(x => x.Reason).HasMaxLength(2000);
    flag.Property(x => x.ManagementExplanation).HasMaxLength(4000);
    flag.Property(x => x.CorroborationReference).HasMaxLength(2000);
    flag.Property(x => x.Status).HasMaxLength(30);
    flag.Property(x => x.Disposition).HasMaxLength(2000);
    flag.Property(x => x.EvidenceReference).HasMaxLength(2000);
    flag.HasIndex(x => new { x.FirmId, x.TransactionId, x.RuleCode }).IsUnique().HasDatabaseName("ux_journal_risk_flag_rule");
    flag.ToTable("journal_risk_flags", t => t.HasCheckConstraint("ck_journal_risk_flag_values",
      "length(trim(rule_code)) > 0 AND length(trim(reason)) > 0 AND score >= 0 AND score <= 100"));
    ScopeToEngagement(flag, nameof(JournalRiskFlag.FirmId), nameof(JournalRiskFlag.ClientId), nameof(JournalRiskFlag.EngagementId));

    var evidenceLink = b.Entity<AccountingEvidenceAuditLink>();
    evidenceLink.Property(x => x.EvidenceKind).HasMaxLength(30);
    evidenceLink.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.EvidenceKind, x.EvidenceId, x.AuditProcedureResultId })
      .IsUnique().HasDatabaseName("ux_accounting_evidence_audit_link");
    evidenceLink.ToTable("accounting_evidence_audit_links", t => t.HasCheckConstraint("ck_accounting_evidence_audit_link_values",
      "evidence_kind IN ('ECL','INVENTORY','SPECIALIST','ANALYTICAL','JOURNAL_RISK')"));
    ScopeToEngagement(evidenceLink, nameof(AccountingEvidenceAuditLink.FirmId), nameof(AccountingEvidenceAuditLink.ClientId), nameof(AccountingEvidenceAuditLink.EngagementId));
    evidenceLink.HasOne<AuditProcedureResult>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.AuditProcedureResultId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    evidenceLink.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.LinkedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
  }

  private static void ConfigureConsolidation(ModelBuilder b)
  {
    var scope = b.Entity<ConsolidationScopeVersion>();
    scope.HasAlternateKey(x => new { x.FirmId, x.GroupId, x.Id }).HasName("ak_consolidation_scopes_group_id");
    scope.Property(x => x.GroupRevision);
    scope.Property(x => x.OpeningRunHash).HasMaxLength(64);
    scope.Property(x => x.OpeningTranslationManifestHash).HasMaxLength(64);
    scope.Property(x => x.OpeningTranslationReserve).HasPrecision(20, 6);
    scope.Property(x => x.RecurringEliminationManifest).HasMaxLength(64);
    scope.Property(x => x.ReportingCurrency).HasMaxLength(3);
    scope.Property(x => x.Method).HasMaxLength(100);
    scope.Property(x => x.Status).HasMaxLength(30);
    scope.Property(x => x.OpeningBasis).HasMaxLength(100);
    scope.Property(x => x.TranslationRateType).HasMaxLength(30);
    scope.HasIndex(x => new { x.FirmId, x.GroupId, x.PeriodId, x.Version }).IsUnique().HasDatabaseName("ux_consolidation_scope_version");
    scope.ToTable("consolidation_scope_versions", t => t.HasCheckConstraint("ck_consolidation_scope_values",
      "reporting_currency ~ '^[A-Z]{3}$' AND length(trim(method)) > 0 AND length(trim(opening_basis)) > 0"));
    scope.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    scope.HasOne<ExchangeRateSetVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.ExchangeRateSetVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    scope.HasOne<TranslationPolicyVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.TranslationPolicyVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var component = b.Entity<ConsolidationComponent>();
    component.Property(x => x.PackageHash).HasMaxLength(64);
    component.Property(x => x.PeriodBasis).HasMaxLength(100);
    component.Property(x => x.TaxonomyVersion).HasMaxLength(100);
    component.Property(x => x.MappingVersion).HasMaxLength(100);
    component.Property(x => x.Currency).HasMaxLength(3);
    component.Property(x => x.ControlMethod).HasMaxLength(100);
    component.Property(x => x.Status).HasMaxLength(30);
    component.HasIndex(x => new { x.FirmId, x.ScopeVersionId, x.ClientId }).IsUnique().HasDatabaseName("ux_consolidation_component_client");
    component.ToTable("consolidation_components", t => t.HasCheckConstraint("ck_consolidation_component_values",
      "package_hash ~ '^[0-9a-f]{64}$' AND currency ~ '^[A-Z]{3}$' AND ownership_percent >= 0 AND ownership_percent <= 100"));
    component.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    component.HasOne<ConsolidationScopeVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    component.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    component.HasOne<FinancialPackage>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var ownership = b.Entity<OwnershipInterestVersion>();
    ownership.Property(x => x.ControlAssessment).HasMaxLength(100);
    ownership.Property(x => x.Method).HasMaxLength(100);
    ownership.Property(x => x.EvidenceReference).HasMaxLength(2000);
    ownership.Property(x => x.Status).HasMaxLength(30);
    ownership.HasIndex(x => new { x.FirmId, x.ScopeVersionId, x.ParentClientId, x.ChildClientId }).IsUnique()
      .HasDatabaseName("ux_ownership_interest_pair");
    ownership.ToTable("ownership_interest_versions", t => t.HasCheckConstraint("ck_ownership_interest_values",
      "effective_to IS NULL OR effective_from <= effective_to" +
      " AND ownership_percent >= 0 AND ownership_percent <= 100 AND economic_interest_percent >= 0 AND economic_interest_percent <= 100"));
    ownership.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    ownership.HasOne<ConsolidationScopeVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    ownership.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ParentClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    ownership.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ChildClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var match = b.Entity<IntercompanyMatch>();
    match.Property(x => x.AccountNature).HasMaxLength(100);
    match.Property(x => x.MatchMode).HasMaxLength(20);
    match.Property(x => x.MatchGroupReference).HasMaxLength(200);
    match.Property(x => x.SellerTaxonomyCode).HasMaxLength(100);
    match.Property(x => x.BuyerTaxonomyCode).HasMaxLength(100);
    match.Property(x => x.PeriodCode).HasMaxLength(50);
    match.Property(x => x.Currency).HasMaxLength(3);
    match.Property(x => x.TransactionReference).HasMaxLength(200);
    match.Property(x => x.Status).HasMaxLength(30);
    match.Property(x => x.DifferenceReason).HasMaxLength(2000);
    match.Property(x => x.EvidenceReference).HasMaxLength(2000);
    match.HasIndex(x => new { x.FirmId, x.ScopeVersionId, x.SellerClientId, x.BuyerClientId, x.TransactionReference }).IsUnique()
      .HasDatabaseName("ux_intercompany_match_identity");
    match.ToTable("intercompany_matches", t => t.HasCheckConstraint("ck_intercompany_match_values",
      "length(trim(account_nature)) > 0 AND match_mode IN ('ONE_TO_ONE','GROUPED') AND (match_mode <> 'GROUPED' OR length(trim(match_group_reference)) > 0) AND length(trim(seller_taxonomy_code)) > 0 AND length(trim(buyer_taxonomy_code)) > 0 AND currency ~ '^[A-Z]{3}$'"));
    match.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    match.HasOne<ConsolidationScopeVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    match.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.SellerClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    match.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.BuyerClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var journal = b.Entity<ConsolidationJournal>();
    journal.Property(x => x.JournalNumber).HasMaxLength(100);
    journal.Property(x => x.JournalType).HasMaxLength(100);
    journal.Property(x => x.Currency).HasMaxLength(3);
    journal.Property(x => x.EvidenceReference).HasMaxLength(2000);
    journal.Property(x => x.Status).HasMaxLength(30);
    journal.HasIndex(x => new { x.FirmId, x.ScopeVersionId, x.JournalNumber }).IsUnique().HasDatabaseName("ux_consolidation_journal_number");
    journal.ToTable("consolidation_journals", t => t.HasCheckConstraint("ck_consolidation_journal_values",
      "currency ~ '^[A-Z]{3}$' AND total_debits = total_credits_abs"));
    journal.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    journal.HasOne<ConsolidationScopeVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var journalLine = b.Entity<ConsolidationJournalLine>();
    journalLine.Property(x => x.TaxonomyCode).HasMaxLength(100);
    journalLine.Property(x => x.Currency).HasMaxLength(3);
    journalLine.Property(x => x.Description).HasMaxLength(1000);
    journalLine.HasIndex(x => new { x.FirmId, x.ConsolidationJournalId, x.TaxonomyCode }).HasDatabaseName("ix_consolidation_journal_line");
    journalLine.ToTable("consolidation_journal_lines", t => t.HasCheckConstraint("ck_consolidation_journal_line_values",
      "length(trim(taxonomy_code)) > 0 AND debit >= 0 AND credit >= 0 AND NOT (debit > 0 AND credit > 0) AND currency ~ '^[A-Z]{3}$'"));
    journalLine.HasOne<ConsolidationJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.ConsolidationJournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    journalLine.HasOne<IntercompanyMatch>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.IntercompanyMatchId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    journal.HasAlternateKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).HasName("ak_consolidation_journals_scope_id");
    match.HasAlternateKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).HasName("ak_intercompany_matches_scope_id");

    var run = b.Entity<ConsolidationRun>();
    run.Property(x => x.EngineVersion).HasMaxLength(100);
    run.Property(x => x.InputManifest).HasMaxLength(20000);
    run.Property(x => x.RunHash).HasMaxLength(64);
    run.Property(x => x.ReportingCurrency).HasMaxLength(3);
    run.Property(x => x.Status).HasMaxLength(30);
    run.HasIndex(x => new { x.FirmId, x.ScopeVersionId, x.RunHash }).IsUnique().HasDatabaseName("ux_consolidation_run_hash");
    run.ToTable("consolidation_runs", t => t.HasCheckConstraint("ck_consolidation_run_values",
      "engine_version <> '' AND run_hash ~ '^[0-9a-f]{64}$' AND reporting_currency ~ '^[A-Z]{3}$'"));
    run.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    run.HasOne<ConsolidationScopeVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var runLine = b.Entity<ConsolidationRunLine>();
    runLine.Property(x => x.TaxonomyCode).HasMaxLength(100);
    runLine.Property(x => x.Currency).HasMaxLength(3);
    runLine.HasIndex(x => new { x.FirmId, x.RunId, x.TaxonomyCode, x.ComponentId, x.ConsolidationJournalId })
      .HasDatabaseName("ix_consolidation_run_line");
    runLine.ToTable("consolidation_run_lines", t => t.HasCheckConstraint("ck_consolidation_run_line_values",
      "length(trim(taxonomy_code)) > 0 AND currency ~ '^[A-Z]{3}$'"));
    runLine.HasOne<ConsolidationRun>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.RunId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    runLine.HasOne<ConsolidationComponent>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.ComponentId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    runLine.HasOne<ConsolidationJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.ConsolidationJournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    runLine.HasOne<IntercompanyMatch>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.IntercompanyMatchId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    run.HasAlternateKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).HasName("ak_consolidation_runs_scope_id");
    component.HasAlternateKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).HasName("ak_consolidation_components_scope_id");

    var rateSet = b.Entity<ExchangeRateSetVersion>();
    rateSet.Property(x => x.Code).HasMaxLength(100);
    rateSet.Property(x => x.Source).HasMaxLength(200);
    rateSet.Property(x => x.Status).HasMaxLength(30);
    rateSet.HasIndex(x => new { x.FirmId, x.Code }).IsUnique().HasDatabaseName("ux_exchange_rate_set_code");
    rateSet.ToTable("exchange_rate_set_versions", t => t.HasCheckConstraint("ck_exchange_rate_set_values",
      "length(trim(code)) > 0 AND length(trim(source)) > 0"));

    var rate = b.Entity<ExchangeRate>();
    rate.Property(x => x.FromCurrency).HasMaxLength(3);
    rate.Property(x => x.ToCurrency).HasMaxLength(3);
    rate.Property(x => x.RateType).HasMaxLength(30);
    rate.Property(x => x.Direction).HasMaxLength(30);
    rate.HasIndex(x => new { x.FirmId, x.RateSetVersionId, x.FromCurrency, x.ToCurrency, x.RateDate, x.RateType }).IsUnique()
      .HasDatabaseName("ux_exchange_rate_identity");
    rate.ToTable("exchange_rates", t => t.HasCheckConstraint("ck_exchange_rate_values",
      "from_currency ~ '^[A-Z]{3}$' AND to_currency ~ '^[A-Z]{3}$' AND rate > 0 AND from_currency <> to_currency"));
    rate.HasOne<ExchangeRateSetVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.RateSetVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var policy = b.Entity<TranslationPolicyVersion>();
    policy.Property(x => x.Code).HasMaxLength(100);
    policy.Property(x => x.FunctionalCurrency).HasMaxLength(3);
    policy.Property(x => x.PresentationCurrency).HasMaxLength(3);
    policy.Property(x => x.ClosingRateRule).HasMaxLength(100);
    policy.Property(x => x.AverageRateRule).HasMaxLength(100);
    policy.Property(x => x.HistoricalRateRule).HasMaxLength(100);
    policy.Property(x => x.Status).HasMaxLength(30);
    policy.HasIndex(x => new { x.FirmId, x.Code }).IsUnique().HasDatabaseName("ux_translation_policy_code");
    policy.ToTable("translation_policy_versions", t => t.HasCheckConstraint("ck_translation_policy_values",
      "functional_currency ~ '^[A-Z]{3}$' AND presentation_currency ~ '^[A-Z]{3}$'"));

    var translation = b.Entity<TranslationResult>();
    translation.Property(x => x.SourcePackageHash).HasMaxLength(64);
    translation.Property(x => x.RateType).HasMaxLength(30);
    translation.Property(x => x.FromCurrency).HasMaxLength(3);
    translation.Property(x => x.ToCurrency).HasMaxLength(3);
    translation.Property(x => x.Status).HasMaxLength(30);
    translation.HasIndex(x => new { x.FirmId, x.ScopeVersionId, x.ComponentId, x.RateSetVersionId, x.TranslationPolicyVersionId }).IsUnique()
      .HasDatabaseName("ux_translation_result_input");
    translation.ToTable("translation_results", t => t.HasCheckConstraint("ck_translation_result_values",
      "from_currency ~ '^[A-Z]{3}$' AND to_currency ~ '^[A-Z]{3}$'"));
    translation.HasOne<ConsolidationComponent>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.ComponentId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    translation.HasOne<ExchangeRateSetVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.RateSetVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    translation.HasOne<TranslationPolicyVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.TranslationPolicyVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }

  // Audit planning and execution evidence (§§19–23, 27.2, 42.3–42.4). Every row carries the full
  // (firm, client, engagement) triple and binds it to a real engagement through a composite foreign
  // key, so a cross-scope link is rejected by PostgreSQL rather than only by application code.
  private static void ConfigureAudit(ModelBuilder b)
  {
    var materiality = b.Entity<MaterialityAssessment>();
    materiality.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_materiality_assessments_firm_id_id");
    materiality.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_materiality_assessments_scope_id");
    materiality.Property(x => x.BenchmarkSource).HasMaxLength(200);
    materiality.Property(x => x.BenchmarkVersion).HasMaxLength(100);
    materiality.Property(x => x.Status).HasMaxLength(30);
    materiality.HasIndex(x => new { x.FirmId, x.EngagementId, x.Status, x.CreatedAt })
      .HasDatabaseName("ix_materiality_scope_status_created");
    materiality.ToTable("materiality_assessments", t => t.HasCheckConstraint("ck_materiality_values",
      "length(trim(benchmark_source)) > 0 AND length(trim(benchmark_version)) > 0 AND length(trim(rationale)) > 0" +
      " AND benchmark_amount > 0 AND rate_applied > 0 AND rate_applied <= 1 AND overall_materiality > 0" +
      " AND performance_materiality < overall_materiality AND clearly_trivial_threshold < performance_materiality" +
      " AND status IN ('DRAFT','APPROVED')"));
    ScopeToEngagement(materiality, nameof(MaterialityAssessment.FirmId),
      nameof(MaterialityAssessment.ClientId), nameof(MaterialityAssessment.EngagementId));
    materiality.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var risk = b.Entity<AuditRisk>();
    risk.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_risks_firm_id_id");
    risk.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_audit_risks_scope_id");
    risk.Property(x => x.AccountArea).HasMaxLength(200);
    risk.Property(x => x.Assertion).HasMaxLength(100);
    risk.Property(x => x.Severity).HasMaxLength(30);
    risk.Property(x => x.SignificanceDecision).HasMaxLength(30);
    risk.Property(x => x.Status).HasMaxLength(30);
    risk.HasIndex(x => new { x.FirmId, x.EngagementId, x.Status }).HasDatabaseName("ix_audit_risks_scope_status");
    risk.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.CreatedAt })
      .HasDatabaseName("ix_audit_risks_scope_created");
    risk.ToTable("audit_risks", t => t.HasCheckConstraint("ck_audit_risk_values",
      "length(trim(account_area)) > 0 AND length(trim(assertion)) > 0 AND length(trim(description)) > 0" +
      " AND length(trim(drivers)) > 0 AND length(trim(response_description)) > 0" +
      " AND significance_decision IN ('SIGNIFICANT','NORMAL') AND status IN ('IDENTIFIED','ASSESSED','RESPONDED')" +
      " AND severity = CASE WHEN significance_decision = 'SIGNIFICANT' THEN 'Significant' ELSE 'Normal' END"));
    ScopeToEngagement(risk, nameof(AuditRisk.FirmId), nameof(AuditRisk.ClientId), nameof(AuditRisk.EngagementId));
    risk.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var programVersion = b.Entity<AuditProgramVersion>();
    programVersion.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_audit_program_versions_firm_id_id");
    programVersion.Property(x => x.ProgramCode).HasMaxLength(100);
    programVersion.Property(x => x.Version).HasMaxLength(100);
    programVersion.Property(x => x.SourceHash).HasMaxLength(64).IsFixedLength();
    programVersion.Property(x => x.Status).HasMaxLength(20);
    programVersion.HasIndex(x => new { x.FirmId, x.ProgramCode, x.Version }).IsUnique()
      .HasDatabaseName("ux_audit_program_version_code_version");
    programVersion.ToTable("audit_program_versions", t => t.HasCheckConstraint("ck_audit_program_version_values",
      "length(trim(program_code)) > 0 AND length(trim(version)) > 0 AND length(source_hash) = 64" +
      " AND status IN ('DRAFT','PUBLISHED','RETIRED')" +
      " AND ((status = 'PUBLISHED' AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL) OR status <> 'PUBLISHED')"));
    programVersion.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    programVersion.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var programProcedure = b.Entity<AuditProgramProcedure>();
    programProcedure.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_audit_program_procedures_firm_id_id");
    programProcedure.HasAlternateKey(x => new { x.FirmId, x.ProgramVersionId, x.SourceProcedureId })
      .HasName("AK_audit_program_procedures_version_source");
    programProcedure.Property(x => x.SourceProcedureId).HasMaxLength(20);
    programProcedure.Property(x => x.SectionTitle).HasMaxLength(200);
    programProcedure.Property(x => x.SourceWording).HasMaxLength(2000);
    programProcedure.Property(x => x.ApplicabilityCondition).HasMaxLength(1000);
    programProcedure.Property(x => x.ExpectedEvidence).HasMaxLength(2000);
    programProcedure.ToTable("audit_program_procedures", t => t.HasCheckConstraint("ck_audit_program_procedure_values",
      "section_number BETWEEN 1 AND 20 AND ordinal >= 1 AND length(trim(source_procedure_id)) > 0" +
      " AND length(trim(section_title)) > 0 AND length(trim(source_wording)) > 0"));
    programProcedure.HasIndex(x => new { x.FirmId, x.ProgramVersionId, x.SectionNumber, x.Ordinal })
      .HasDatabaseName("ix_audit_program_procedures_version_order");
    programProcedure.HasOne<AuditProgramVersion>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ProgramVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var engagementProgram = b.Entity<EngagementAuditProgram>();
    engagementProgram.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_engagement_audit_programs_firm_id_id");
    engagementProgram.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProgramVersionId })
      .HasName("AK_engagement_audit_programs_scope_version");
    engagementProgram.Property(x => x.Status).HasMaxLength(20);
    engagementProgram.ToTable("engagement_audit_programs", t => t.HasCheckConstraint("ck_engagement_audit_program_values",
      "status IN ('ADOPTED','SUPERSEDED')"));
    ScopeToEngagement(engagementProgram, nameof(EngagementAuditProgram.FirmId),
      nameof(EngagementAuditProgram.ClientId), nameof(EngagementAuditProgram.EngagementId));
    engagementProgram.HasOne<AuditProgramVersion>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ProgramVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    engagementProgram.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.AdoptedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var population = b.Entity<PopulationVersion>();
    population.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_population_versions_firm_id_id");
    population.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_population_versions_scope_id");
    population.Property(x => x.Purpose).HasMaxLength(300);
    population.Property(x => x.Assertion).HasMaxLength(100);
    population.Property(x => x.SourceReceiptReference).HasColumnName("source_receipt_ref").HasMaxLength(200);
    population.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    population.Property(x => x.Status).HasMaxLength(30);
    population.HasIndex(x => new { x.FirmId, x.EngagementId, x.Status }).HasDatabaseName("ix_population_scope_status");
    population.ToTable("population_versions", t => t.HasCheckConstraint("ck_population_values",
      "length(trim(purpose)) > 0 AND length(trim(assertion)) > 0 AND length(trim(source_receipt_ref)) > 0" +
      " AND length(trim(extraction_parameters)) > 0 AND row_count >= 0 AND monetary_control_total >= 0" +
      " AND currency ~ '^[A-Z]{3}$' AND status IN ('PENDING_APPROVAL','APPROVED','REJECTED')"));
    ScopeToEngagement(population, nameof(PopulationVersion.FirmId), nameof(PopulationVersion.ClientId),
      nameof(PopulationVersion.EngagementId));
    population.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var workpaper = b.Entity<Workpaper>();
    workpaper.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_workpapers_firm_id_id");
    workpaper.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_workpapers_scope_id");
    workpaper.Property(x => x.Index).HasColumnName("wp_index").HasMaxLength(30);
    workpaper.Property(x => x.Title).HasMaxLength(300);
    workpaper.Property(x => x.TemplateVersion).HasMaxLength(100);
    workpaper.Property(x => x.Status).HasMaxLength(30);
    workpaper.HasIndex(x => new { x.FirmId, x.EngagementId, x.Status }).HasDatabaseName("ix_workpapers_scope_status");
    workpaper.ToTable("workpapers", t => t.HasCheckConstraint("ck_workpaper_values",
      "revision > 0 AND length(trim(wp_index)) > 0 AND length(trim(title)) > 0 AND length(trim(objective)) > 0" +
      " AND length(trim(template_version)) > 0 AND length(trim(procedure)) > 0" +
      " AND status IN ('WORKING','SUBMITTED_SNAPSHOT')" +
      " AND ((status = 'SUBMITTED_SNAPSHOT' AND submitted_at IS NOT NULL AND length(trim(conclusion)) > 0)" +
      "   OR (status = 'WORKING' AND submitted_at IS NULL))"));
    ScopeToEngagement(workpaper, nameof(Workpaper.FirmId), nameof(Workpaper.ClientId),
      nameof(Workpaper.EngagementId));
    workpaper.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    // A null procedure reference means "not procedure-driven"; an empty GUID in a scope key would
    // defeat duplicate prevention (§42.4), so the column is genuinely optional.
    workpaper.HasOne<AuditProcedure>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var submission = b.Entity<WorkpaperSubmission>();
    submission.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_workpaper_submissions_firm_id_id");
    submission.Property(x => x.Conclusion).HasMaxLength(20000);
    submission.Property(x => x.WorkPerformed).HasMaxLength(100000);
    submission.HasIndex(x => new { x.FirmId, x.WorkpaperId, x.Revision }).IsUnique()
      .HasDatabaseName("ux_workpaper_submission_revision");
    submission.ToTable("workpaper_submissions", t => t.HasCheckConstraint("ck_workpaper_submission_values",
      "revision > 0 AND length(trim(conclusion)) > 0 AND length(trim(work_performed)) > 0"));
    submission.HasOne<Workpaper>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.WorkpaperId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    submission.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var draft = b.Entity<WorkpaperDraft>();
    draft.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_workpaper_drafts_firm_id_id");
    draft.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.WorkpaperId, x.OwnerUserId })
      .HasName("AK_workpaper_drafts_scope_owner");
    draft.Property(x => x.WorkPerformed).HasMaxLength(100000);
    draft.Property(x => x.Conclusion).HasMaxLength(20000);
    draft.Property(x => x.Lifecycle).HasMaxLength(16);
    draft.HasIndex(x => new { x.FirmId, x.WorkpaperId, x.OwnerUserId })
      .IsUnique().HasDatabaseName("ux_workpaper_drafts_owner");
    draft.ToTable("workpaper_drafts", t => t.HasCheckConstraint("ck_workpaper_draft_values",
      "base_workpaper_revision > 0 AND base_input_generation > 0 AND base_policy_generation > 0" +
      " AND draft_revision > 0 AND length(work_performed) <= 100000 AND length(conclusion) <= 20000" +
      " AND lifecycle IN ('ACTIVE','CONSUMED','DISCARDED')"));
    ScopeToEngagement(draft, nameof(WorkpaperDraft.FirmId), nameof(WorkpaperDraft.ClientId),
      nameof(WorkpaperDraft.EngagementId));
    draft.HasOne<Workpaper>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.WorkpaperId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    draft.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.OwnerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var procedure = b.Entity<AuditProcedure>();
    procedure.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_procedures_firm_id_id");
    procedure.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_audit_procedures_scope_id");
    procedure.Property(x => x.SourceProcedureId).HasMaxLength(20);
    procedure.Property(x => x.SourceSectionTitle).HasMaxLength(200);
    procedure.Property(x => x.SourceWording).HasMaxLength(2000);
    procedure.Property(x => x.ApplicabilityStatus).HasMaxLength(30);
    procedure.ToTable("audit_procedures", t => t.HasCheckConstraint("ck_audit_procedure_values",
      "length(trim(title)) > 0 AND status IN ('PLANNED','IN_PROGRESS','SUBMITTED','IN_REVIEW','CHANGES_REQUIRED','REVIEWED')" +
      " AND applicability_status IN ('PENDING','APPLICABLE','NA_PENDING_REVIEW','NA_APPROVED')"));
    procedure.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SourceProcedureId })
      .IsUnique().HasDatabaseName("ux_audit_procedure_scope_source");
    ScopeToEngagement(procedure, nameof(AuditProcedure.FirmId), nameof(AuditProcedure.ClientId),
      nameof(AuditProcedure.EngagementId));
    procedure.HasOne<AuditRisk>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.RiskId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    procedure.HasOne<EngagementAuditProgram>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.EngagementProgramId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    procedure.HasOne<AuditProgramProcedure>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ProgramProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    procedure.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApplicabilityDecidedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var procedureResult = b.Entity<AuditProcedureResult>();
    procedureResult.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_audit_procedure_results_firm_id_id");
    procedureResult.Property(x => x.WorkPerformed).HasMaxLength(100000);
    procedureResult.Property(x => x.StructuredResultJson).HasMaxLength(100000);
    procedureResult.Property(x => x.EvidenceReferencesJson).HasMaxLength(20000);
    procedureResult.Property(x => x.Conclusion).HasMaxLength(20000);
    procedureResult.Property(x => x.Status).HasMaxLength(30);
    procedureResult.HasIndex(x => new { x.FirmId, x.AuditProcedureId, x.Revision }).IsUnique()
      .HasDatabaseName("ux_audit_procedure_result_revision");
    procedureResult.ToTable("audit_procedure_results", t => t.HasCheckConstraint("ck_audit_procedure_result_values",
      "revision > 0 AND input_generation > 0 AND length(trim(work_performed)) > 0" +
      " AND length(trim(structured_result_json)) > 0 AND length(trim(conclusion)) > 0" +
      " AND status IN ('SUBMITTED','CHANGES_REQUIRED','REVIEWED')" +
      " AND ((status = 'REVIEWED' AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL) OR status <> 'REVIEWED')"));
    ScopeToEngagement(procedureResult, nameof(AuditProcedureResult.FirmId),
      nameof(AuditProcedureResult.ClientId), nameof(AuditProcedureResult.EngagementId));
    procedureResult.HasOne<AuditProcedure>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.AuditProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    procedureResult.HasOne<Workpaper>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.WorkpaperId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    procedureResult.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PreparedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    procedureResult.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var procedureReview = b.Entity<AuditProcedureReview>();
    procedureReview.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_audit_procedure_reviews_firm_id_id");
    procedureReview.Property(x => x.Decision).HasMaxLength(30);
    procedureReview.Property(x => x.Comment).HasMaxLength(20000);
    procedureReview.HasIndex(x => new { x.FirmId, x.AuditProcedureResultId, x.CreatedAt })
      .HasDatabaseName("ix_audit_procedure_reviews_result_created");
    procedureReview.ToTable("audit_procedure_reviews", t => t.HasCheckConstraint("ck_audit_procedure_review_values",
      "result_revision > 0 AND decision IN ('REVIEWED','CHANGES_REQUIRED')"));
    ScopeToEngagement(procedureReview, nameof(AuditProcedureReview.FirmId),
      nameof(AuditProcedureReview.ClientId), nameof(AuditProcedureReview.EngagementId));
    procedureReview.HasOne<AuditProcedureResult>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.AuditProcedureResultId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    procedureReview.HasOne<AuditProcedure>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.AuditProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    procedureReview.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReviewerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var finding = b.Entity<Finding>();
    finding.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_findings_firm_id_id");
    finding.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_findings_scope_id");
    finding.Property(x => x.FindingType).HasMaxLength(100);
    finding.Property(x => x.Status).HasMaxLength(30);
    finding.HasIndex(x => new { x.FirmId, x.EngagementId, x.Status }).HasDatabaseName("ix_findings_scope_status");
    finding.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.CreatedAt })
      .HasDatabaseName("ix_findings_scope_created");
    finding.ToTable("findings", t => t.HasCheckConstraint("ck_finding_values",
      "length(trim(finding_type)) > 0 AND length(trim(impact_description)) > 0" +
      " AND (monetary_amount IS NULL OR monetary_amount >= 0) AND status IN ('OPEN','CORRECTED','EVALUATED')" +
      " AND ((corrected AND status <> 'OPEN') OR NOT corrected)"));
    ScopeToEngagement(finding, nameof(Finding.FirmId), nameof(Finding.ClientId), nameof(Finding.EngagementId));
    finding.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }

  private static void ConfigureFieldwork(ModelBuilder b)
  {
    var schedule = b.Entity<AuditSchedule>();
    schedule.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_schedules_firm_id_id");
    schedule.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_audit_schedules_scope_id");
    schedule.Property(x => x.ScheduleType).HasMaxLength(100);
    schedule.Property(x => x.EntityIdentifier).HasMaxLength(200);
    schedule.Property(x => x.SourceReceiptReference).HasMaxLength(200);
    schedule.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    schedule.Property(x => x.SignConvention).HasMaxLength(100);
    schedule.Property(x => x.SourceHash).HasMaxLength(64).IsFixedLength();
    schedule.Property(x => x.CompletenessDecision).HasMaxLength(2000);
    schedule.Property(x => x.Status).HasMaxLength(30);
    schedule.HasIndex(x => new { x.FirmId, x.EngagementId, x.SourceReceiptReference, x.SourceHash }).IsUnique()
      .HasDatabaseName("ux_audit_schedule_source_version");
    schedule.HasIndex(x => new { x.FirmId, x.EngagementId, x.ScheduleType, x.AsOfDate })
      .HasDatabaseName("ix_audit_schedule_scope_type_date");
    schedule.ToTable("audit_schedules", t => t.HasCheckConstraint("ck_audit_schedule_values",
      "length(trim(schedule_type)) > 0 AND length(trim(entity_identifier)) > 0" +
      " AND length(trim(source_receipt_reference)) > 0 AND length(source_hash) = 64" +
      " AND currency ~ '^[A-Z]{3}$' AND length(trim(sign_convention)) > 0" +
      " AND row_count >= 0 AND input_generation > 0 AND status IN ('PENDING_REVIEW','RECONCILED','UNRECONCILED','APPROVED','SUPERSEDED')"));
    ScopeToEngagement(schedule, nameof(AuditSchedule.FirmId), nameof(AuditSchedule.ClientId), nameof(AuditSchedule.EngagementId));
    schedule.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var bankReconciliation = b.Entity<AuditBankReconciliation>();
    bankReconciliation.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_bank_reconciliations_firm_id_id");
    bankReconciliation.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_audit_bank_reconciliations_scope_id");
    bankReconciliation.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    bankReconciliation.Property(x => x.Status).HasMaxLength(30);
    bankReconciliation.Property(x => x.Conclusion).HasMaxLength(4000);
    bankReconciliation.ToTable("audit_bank_reconciliations", t => t.HasCheckConstraint("ck_audit_bank_reconciliation_values",
      "currency ~ '^[A-Z]{3}$' AND input_generation > 0 AND status IN ('RECONCILED','UNRECONCILED','APPROVED','CHANGES_REQUIRED')"));
    ScopeToEngagement(bankReconciliation, nameof(AuditBankReconciliation.FirmId), nameof(AuditBankReconciliation.ClientId), nameof(AuditBankReconciliation.EngagementId));
    bankReconciliation.HasOne<AuditSchedule>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.LedgerScheduleId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    bankReconciliation.HasOne<AuditSchedule>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.StatementScheduleId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    bankReconciliation.HasOne<AuditProcedure>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    bankReconciliation.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    bankReconciliation.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var bankReconciliationItem = b.Entity<AuditBankReconciliationItem>();
    bankReconciliationItem.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_bank_reconciliation_items_firm_id_id");
    bankReconciliationItem.HasAlternateKey(x => new { x.FirmId, x.BankReconciliationId, x.StableItemId })
      .HasName("AK_audit_bank_reconciliation_items_reconciliation_stable");
    bankReconciliationItem.Property(x => x.StableItemId).HasMaxLength(200);
    bankReconciliationItem.Property(x => x.ItemType).HasMaxLength(30);
    bankReconciliationItem.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    bankReconciliationItem.Property(x => x.Description).HasMaxLength(1000);
    bankReconciliationItem.Property(x => x.SourceReference).HasMaxLength(500);
    bankReconciliationItem.Property(x => x.EvidenceReference).HasMaxLength(2000);
    bankReconciliationItem.ToTable("audit_bank_reconciliation_items", t => t.HasCheckConstraint("ck_audit_bank_reconciliation_item_values",
      "length(trim(stable_item_id)) > 0 AND item_type IN ('LEDGER','STATEMENT','TIMING','PROPOSED_CORRECTION')"));
    ScopeToEngagement(bankReconciliationItem, nameof(AuditBankReconciliationItem.FirmId), nameof(AuditBankReconciliationItem.ClientId), nameof(AuditBankReconciliationItem.EngagementId));
    bankReconciliationItem.HasOne<AuditBankReconciliation>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.BankReconciliationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    bankReconciliationItem.HasOne<AuditSchedule>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SourceScheduleId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    bankReconciliationItem.HasOne<AdjustmentJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProposedJournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var scheduleRow = b.Entity<AuditScheduleRow>();
    scheduleRow.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_schedule_rows_firm_id_id");
    scheduleRow.HasAlternateKey(x => new { x.FirmId, x.ScheduleId, x.StableRowId })
      .HasName("AK_audit_schedule_rows_schedule_stable");
    scheduleRow.Property(x => x.StableRowId).HasMaxLength(200);
    scheduleRow.Property(x => x.AccountCode).HasMaxLength(100);
    scheduleRow.Property(x => x.Description).HasMaxLength(1000);
    scheduleRow.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    scheduleRow.Property(x => x.OriginalValuesJson).HasMaxLength(20000);
    scheduleRow.ToTable("audit_schedule_rows", t => t.HasCheckConstraint("ck_audit_schedule_row_values",
      "source_line_number > 0 AND length(trim(stable_row_id)) > 0 AND length(trim(account_code)) > 0" +
      " AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(original_values_json)) > 0"));
    ScopeToEngagement(scheduleRow, nameof(AuditScheduleRow.FirmId), nameof(AuditScheduleRow.ClientId), nameof(AuditScheduleRow.EngagementId));
    scheduleRow.HasOne<AuditSchedule>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ScheduleId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    scheduleRow.HasIndex(x => new { x.FirmId, x.ScheduleId, x.SourceLineNumber })
      .HasDatabaseName("ix_audit_schedule_rows_order");

    var selection = b.Entity<AuditSelection>();
    selection.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_selections_firm_id_id");
    selection.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_audit_selections_scope_id");
    selection.Property(x => x.Method).HasMaxLength(100);
    selection.Property(x => x.Rationale).HasMaxLength(4000);
    selection.Property(x => x.Status).HasMaxLength(30);
    selection.ToTable("audit_selections", t => t.HasCheckConstraint("ck_audit_selection_values",
      "length(trim(method)) > 0 AND length(trim(rationale)) > 0 AND selected_count > 0" +
      " AND input_generation > 0 AND status IN ('SUBMITTED','REVIEWED','CHANGES_REQUIRED')"));
    ScopeToEngagement(selection, nameof(AuditSelection.FirmId), nameof(AuditSelection.ClientId), nameof(AuditSelection.EngagementId));
    selection.HasOne<AuditProcedure>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    selection.HasOne<AuditSchedule>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ScheduleId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    selection.HasOne<PopulationVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PopulationVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    selection.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    selection.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var selectionItem = b.Entity<AuditSelectionItem>();
    selectionItem.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_selection_items_firm_id_id");
    selectionItem.HasAlternateKey(x => new { x.FirmId, x.SelectionId, x.StableRowId })
      .HasName("AK_audit_selection_items_selection_stable");
    selectionItem.Property(x => x.StableRowId).HasMaxLength(200);
    selectionItem.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    selectionItem.Property(x => x.InclusionReason).HasMaxLength(1000);
    selectionItem.ToTable("audit_selection_items", t => t.HasCheckConstraint("ck_audit_selection_item_values",
      "length(trim(stable_row_id)) > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(inclusion_reason)) > 0"));
    ScopeToEngagement(selectionItem, nameof(AuditSelectionItem.FirmId), nameof(AuditSelectionItem.ClientId), nameof(AuditSelectionItem.EngagementId));
    selectionItem.HasOne<AuditSelection>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SelectionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    selectionItem.HasOne<AuditScheduleRow>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ScheduleRowId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var itemTest = b.Entity<AuditItemTest>();
    itemTest.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_item_tests_firm_id_id");
    itemTest.HasAlternateKey(x => new { x.FirmId, x.SelectionItemId, x.Revision })
      .HasName("AK_audit_item_tests_item_revision");
    itemTest.Property(x => x.WorkPerformed).HasMaxLength(100000);
    itemTest.Property(x => x.EvidenceReferencesJson).HasMaxLength(20000);
    itemTest.Property(x => x.Result).HasMaxLength(20);
    itemTest.Property(x => x.ContradictoryEvidence).HasMaxLength(4000);
    itemTest.Property(x => x.FollowUp).HasMaxLength(4000);
    itemTest.ToTable("audit_item_tests", t => t.HasCheckConstraint("ck_audit_item_test_values",
      "revision > 0 AND length(trim(work_performed)) > 0 AND length(trim(evidence_references_json)) > 0" +
      " AND input_generation > 0 AND result IN ('PENDING','PASS','EXCEPTION','LIMITATION')"));
    ScopeToEngagement(itemTest, nameof(AuditItemTest.FirmId), nameof(AuditItemTest.ClientId), nameof(AuditItemTest.EngagementId));
    itemTest.HasOne<AuditSelection>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SelectionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    itemTest.HasOne<AuditSelectionItem>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SelectionItemId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    itemTest.HasOne<AuditProcedure>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    itemTest.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.TestedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var itemTestReview = b.Entity<AuditItemTestReview>();
    itemTestReview.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_item_test_reviews_firm_id_id");
    itemTestReview.Property(x => x.Decision).HasMaxLength(30);
    itemTestReview.Property(x => x.Comment).HasMaxLength(20000);
    itemTestReview.ToTable("audit_item_test_reviews", t => t.HasCheckConstraint("ck_audit_item_test_review_values",
      "test_revision > 0 AND decision IN ('REVIEWED','CHANGES_REQUIRED')"));
    ScopeToEngagement(itemTestReview, nameof(AuditItemTestReview.FirmId), nameof(AuditItemTestReview.ClientId), nameof(AuditItemTestReview.EngagementId));
    itemTestReview.HasOne<AuditItemTest>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.AuditItemTestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    itemTestReview.HasOne<AuditSelectionItem>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SelectionItemId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    itemTestReview.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ReviewerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var confirmation = b.Entity<AuditConfirmationCase>();
    confirmation.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_confirmation_cases_firm_id_id");
    confirmation.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_audit_confirmation_cases_scope_id");
    confirmation.Property(x => x.AreaCode).HasMaxLength(40);
    confirmation.Property(x => x.SourceRecordId).HasMaxLength(200);
    confirmation.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    confirmation.Property(x => x.Respondent).HasMaxLength(500);
    confirmation.Property(x => x.ContactValidationSource).HasMaxLength(2000);
    confirmation.Property(x => x.Status).HasMaxLength(30);
    confirmation.Property(x => x.DispatchReference).HasMaxLength(500);
    confirmation.ToTable("audit_confirmation_cases", t => t.HasCheckConstraint("ck_audit_confirmation_case_values",
      "length(trim(area_code)) > 0 AND length(trim(source_record_id)) > 0 AND currency ~ '^[A-Z]{3}$'" +
      " AND length(trim(respondent)) > 0 AND length(trim(contact_validation_source)) > 0" +
      " AND input_generation > 0 AND status IN ('DRAFT','APPROVED','DISPATCHED','RESPONSE_RECEIVED','NO_RESPONSE','ALTERNATIVE_REQUIRED','CLOSED')"));
    ScopeToEngagement(confirmation, nameof(AuditConfirmationCase.FirmId), nameof(AuditConfirmationCase.ClientId), nameof(AuditConfirmationCase.EngagementId));
    confirmation.HasOne<AuditProcedure>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    confirmation.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var response = b.Entity<AuditConfirmationResponse>();
    response.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_confirmation_responses_firm_id_id");
    response.HasAlternateKey(x => new { x.FirmId, x.ConfirmationCaseId, x.Revision })
      .HasName("AK_audit_confirmation_responses_case_revision");
    response.Property(x => x.Origin).HasMaxLength(40);
    response.Property(x => x.Channel).HasMaxLength(40);
    response.Property(x => x.ResponseReference).HasMaxLength(500);
    response.Property(x => x.AuthenticityAssessment).HasMaxLength(4000);
    response.Property(x => x.Decision).HasMaxLength(30);
    response.ToTable("audit_confirmation_responses", t => t.HasCheckConstraint("ck_audit_confirmation_response_values",
      "revision > 0 AND length(trim(origin)) > 0 AND length(trim(channel)) > 0" +
      " AND length(trim(response_reference)) > 0 AND length(trim(authenticity_assessment)) > 0" +
      " AND decision IN ('PENDING','AGREED','DIFFERENCE','NO_RESPONSE','ALTERNATIVE_REQUIRED')"));
    ScopeToEngagement(response, nameof(AuditConfirmationResponse.FirmId), nameof(AuditConfirmationResponse.ClientId), nameof(AuditConfirmationResponse.EngagementId));
    response.HasOne<AuditConfirmationCase>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ConfirmationCaseId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    response.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    response.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var alternative = b.Entity<AuditAlternativeProcedure>();
    alternative.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_alternatives_firm_id_id");
    alternative.Property(x => x.Purpose).HasMaxLength(2000);
    alternative.Property(x => x.EvidenceReferencesJson).HasMaxLength(20000);
    alternative.Property(x => x.Conclusion).HasMaxLength(20000);
    alternative.Property(x => x.Status).HasMaxLength(20);
    alternative.ToTable("audit_alternative_procedures", t => t.HasCheckConstraint("ck_audit_alternative_values",
      "length(trim(purpose)) > 0 AND length(trim(evidence_references_json)) > 0 AND length(trim(conclusion)) > 0" +
      " AND status IN ('SUBMITTED','REVIEWED')"));
    ScopeToEngagement(alternative, nameof(AuditAlternativeProcedure.FirmId), nameof(AuditAlternativeProcedure.ClientId), nameof(AuditAlternativeProcedure.EngagementId));
    alternative.HasOne<AuditConfirmationCase>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ConfirmationCaseId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    alternative.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    alternative.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var assessment = b.Entity<AuditAreaAssessment>();
    assessment.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_area_assessments_firm_id_id");
    assessment.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_audit_area_assessments_scope_id");
    assessment.Property(x => x.AreaCode).HasMaxLength(40);
    assessment.Property(x => x.AssessmentKind).HasMaxLength(60);
    assessment.Property(x => x.MethodologyReference).HasMaxLength(500);
    assessment.Property(x => x.InputSnapshotJson).HasMaxLength(100000);
    assessment.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    assessment.Property(x => x.EvidenceReferencesJson).HasMaxLength(20000);
    assessment.Property(x => x.Conclusion).HasMaxLength(20000);
    assessment.Property(x => x.Status).HasMaxLength(30);
    assessment.ToTable("audit_area_assessments", t => t.HasCheckConstraint("ck_audit_area_assessment_values",
      "area_code IN ('CASH_BANK','RECEIVABLES','INVENTORY','REVENUE','PAYABLES','FIXED_ASSETS','EXPENSES','PAYROLL','LOANS','EQUITY','RELATED_PARTIES','TAX_STATUTORY','JOURNALS_FRAUD','ANALYTICAL_REVIEW','GOING_CONCERN','SUBSEQUENT_EVENTS','FINANCIAL_STATEMENTS','AUDIT_DIFFERENCES')" +
      " AND length(trim(assessment_kind)) > 0 AND length(trim(methodology_reference)) > 0" +
      " AND length(trim(input_snapshot_json)) > 0 AND length(trim(evidence_references_json)) > 0" +
      " AND length(trim(conclusion)) > 0 AND input_generation > 0 AND revision > 0" +
      " AND status IN ('SUBMITTED','REVIEWED','CHANGES_REQUIRED')"));
    ScopeToEngagement(assessment, nameof(AuditAreaAssessment.FirmId), nameof(AuditAreaAssessment.ClientId), nameof(AuditAreaAssessment.EngagementId));
    assessment.HasOne<AuditProcedure>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    assessment.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    assessment.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var difference = b.Entity<AuditDifference>();
    difference.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_differences_firm_id_id");
    difference.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_audit_differences_scope_id");
    difference.Property(x => x.AccountArea).HasMaxLength(200);
    difference.Property(x => x.DifferenceType).HasMaxLength(60);
    difference.Property(x => x.Description).HasMaxLength(4000);
    difference.Property(x => x.MaterialityReference).HasMaxLength(1000);
    difference.Property(x => x.QualitativeConcerns).HasMaxLength(4000);
    difference.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    difference.Property(x => x.ManagementResponse).HasMaxLength(4000);
    difference.Property(x => x.CorrectionReference).HasMaxLength(500);
    difference.Property(x => x.CorrectionState).HasMaxLength(40);
    difference.Property(x => x.JournalImpactJson).HasMaxLength(100000);
    difference.Property(x => x.JournalImpactHash).HasMaxLength(64);
    difference.Property(x => x.Evaluation).HasMaxLength(4000);
    difference.Property(x => x.Status).HasMaxLength(30);
    difference.ToTable("audit_differences", t => t.HasCheckConstraint("ck_audit_difference_values",
      "length(trim(account_area)) > 0 AND length(trim(difference_type)) > 0 AND length(trim(description)) > 0" +
      " AND currency ~ '^[A-Z]{3}$' AND amount <> 0 AND input_generation > 0" +
      " AND status IN ('OPEN','MANAGEMENT_RESPONDED','EVALUATED','CORRECTED','VERIFIED_REFLECTED')" +
      " AND (correction_state IS NULL OR correction_state IN ('PROPOSED','AGREED','REJECTED','APPLIED_IN_REPORTING','REPORTED_POSTED_EXTERNALLY','VERIFIED_REFLECTED'))" +
      " AND (NOT corrected OR correction_state IS NULL OR (correction_state = 'VERIFIED_REFLECTED' AND proposed_journal_id IS NOT NULL AND source_reflection_reconciliation_id IS NOT NULL AND verified_adjusted_snapshot_id IS NOT NULL))"));
    ScopeToEngagement(difference, nameof(AuditDifference.FirmId), nameof(AuditDifference.ClientId), nameof(AuditDifference.EngagementId));
    difference.HasOne<AuditProcedure>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    difference.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    difference.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.EvaluatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    difference.HasOne<AdjustmentJournal>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProposedJournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    difference.HasOne<JournalSourceReconciliation>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SourceReflectionReconciliationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    difference.HasOne<AdjustedTrialBalanceSnapshot>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.VerifiedAdjustedSnapshotId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
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

  private static void ConfigureScopedEvidence(ModelBuilder b)
  {
    var assignment = b.Entity<EngagementAssignment>();
    assignment.Property(x => x.Role).HasMaxLength(50);
    assignment.HasIndex(x => new { x.FirmId, x.EngagementId, x.UserId, x.Role })
      .HasDatabaseName("ix_engagement_assignments_scope");
    ScopeToEngagement(assignment, nameof(EngagementAssignment.FirmId), nameof(EngagementAssignment.ClientId),
      nameof(EngagementAssignment.EngagementId));
    assignment.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.UserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    assignment.ToTable("engagement_assignments", t => t.HasCheckConstraint("ck_engagement_assignment_values",
      "length(trim(role)) > 0 AND allocated_hours >= 0" +
      " AND ((start_date IS NULL OR end_date IS NULL) OR start_date <= end_date)"));

    var eqr = b.Entity<EqrCase>();
    eqr.Property(x => x.Status).HasMaxLength(30);
    eqr.HasIndex(x => new { x.FirmId, x.EngagementId }).IsUnique().HasDatabaseName("ux_eqr_case_engagement");
    ScopeToEngagement(eqr, nameof(EqrCase.FirmId), nameof(EqrCase.ClientId), nameof(EqrCase.EngagementId));
    eqr.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.EqrPartnerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    eqr.ToTable("eqr_cases", t => t.HasCheckConstraint("ck_eqr_case_values",
      "status IN ('PENDING','IN_PROGRESS','CONCURRED','CHANGES_REQUESTED')" +
      " AND ((status = 'CONCURRED' AND concurrence_date IS NOT NULL AND completed_at IS NOT NULL)" +
      "   OR status <> 'CONCURRED')"));

    var representation = b.Entity<WrittenRepresentation>();
    representation.Property(x => x.Code).HasMaxLength(20);
    representation.Property(x => x.Title).HasMaxLength(300);
    representation.HasIndex(x => new { x.FirmId, x.EngagementId, x.Code }).IsUnique()
      .HasDatabaseName("ux_written_representation_code");
    ScopeToEngagement(representation, nameof(WrittenRepresentation.FirmId), nameof(WrittenRepresentation.ClientId),
      nameof(WrittenRepresentation.EngagementId));
    representation.ToTable("written_representations", t => t.HasCheckConstraint("ck_written_representation_values",
      "length(trim(code)) > 0 AND length(trim(title)) > 0 AND length(trim(narrative)) > 0" +
      " AND ((obtained AND obtained_at IS NOT NULL) OR NOT obtained)"));

    var clearance = b.Entity<SpecialistClearance>();
    clearance.Property(x => x.Area).HasMaxLength(100);
    clearance.Property(x => x.Status).HasMaxLength(30);
    clearance.HasIndex(x => new { x.FirmId, x.PracticeClientId, x.Area }).HasDatabaseName("ix_clearance_client_area");
    clearance.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PracticeClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    clearance.ToTable("specialist_clearances", t => t.HasCheckConstraint("ck_specialist_clearance_values",
      "length(trim(area)) > 0 AND length(trim(specialist_name)) > 0" +
      " AND status IN ('PENDING','CLEARED','HOLD','CONDITIONS')" +
      " AND ((status = 'CLEARED' AND cleared_at IS NOT NULL) OR status <> 'CLEARED')" +
      " AND ((status = 'CONDITIONS' AND length(trim(conditions)) > 0) OR status <> 'CONDITIONS')"));

    var receipt = b.Entity<SourceReceipt>();
    receipt.Property(x => x.SourceType).HasMaxLength(30);
    receipt.Property(x => x.ReceiptToken).HasMaxLength(200);
    receipt.Property(x => x.Sha256Digest).HasMaxLength(64);
    receipt.Property(x => x.OriginalFileName).HasMaxLength(255);
    receipt.HasIndex(x => new { x.FirmId, x.EngagementId, x.ReceiptToken }).IsUnique()
      .HasDatabaseName("ux_source_receipt_scope_token");
    ScopeToEngagement(receipt, nameof(SourceReceipt.FirmId), nameof(SourceReceipt.ClientId),
      nameof(SourceReceipt.EngagementId));
    receipt.ToTable("source_receipts", t => t.HasCheckConstraint("ck_source_receipt_values",
      "source_type IN ('PBC_UPLOAD','DIRECT_FEED','CSV_IMPORT') AND length(trim(receipt_token)) > 0" +
      " AND sha256_digest ~ '^[0-9a-f]{64}$' AND byte_count > 0 AND length(trim(original_file_name)) > 0"));
    receipt.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.AcquiredByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var evidence = b.Entity<EvidenceLink>();
    evidence.Property(x => x.Purpose).HasMaxLength(500);
    evidence.Property(x => x.Assertion).HasMaxLength(100);
    evidence.HasIndex(x => new { x.FirmId, x.EngagementId, x.SourceReceiptId }).HasDatabaseName("ix_evidence_scope_receipt");
    ScopeToEngagement(evidence, nameof(EvidenceLink.FirmId), nameof(EvidenceLink.ClientId),
      nameof(EvidenceLink.EngagementId));
    evidence.HasOne<SourceReceipt>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SourceReceiptId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    // A workpaper evidence link must match an authorized snapshot scope (§27.2): the composite key
    // makes a cross-client link unrepresentable rather than merely discouraged.
    evidence.HasOne<Workpaper>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.WorkpaperId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    evidence.ToTable("evidence_links", t => t.HasCheckConstraint("ck_evidence_link_values",
      "length(trim(purpose)) > 0 AND length(trim(assertion)) > 0 AND length(trim(relevance_reliability_assessment)) > 0"));

    var reviewPoint = b.Entity<ReviewPoint>();
    reviewPoint.Property(x => x.TargetKind).HasMaxLength(50);
    reviewPoint.Property(x => x.Comment).HasMaxLength(20000);
    reviewPoint.HasIndex(x => new { x.FirmId, x.EngagementId, x.TargetId, x.Cleared })
      .HasDatabaseName("ix_review_points_scope_cleared");
    ScopeToEngagement(reviewPoint, nameof(ReviewPoint.FirmId), nameof(ReviewPoint.ClientId),
      nameof(ReviewPoint.EngagementId));
    reviewPoint.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.RaisedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    reviewPoint.ToTable("review_points", t => t.HasCheckConstraint("ck_review_point_values",
      "length(trim(target_kind)) > 0 AND target_revision >= 1 AND length(trim(comment)) > 0"));

    var profile = b.Entity<RecordsProfile>();
    profile.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_records_profiles_firm_id_id");
    profile.Property(x => x.ProfileCode).HasMaxLength(100);
    profile.Property(x => x.RecordClass).HasMaxLength(100);
    profile.Property(x => x.Jurisdiction).HasMaxLength(100);
    profile.Property(x => x.ServiceRoute).HasMaxLength(100);
    profile.Property(x => x.RetentionTrigger).HasMaxLength(200);
    profile.Property(x => x.ProtectionMode).HasMaxLength(100);
    profile.Property(x => x.LabelId).HasMaxLength(200);
    profile.Property(x => x.LegalHoldBehavior).HasMaxLength(200);
    profile.Property(x => x.AmendmentRoute).HasMaxLength(500);
    profile.Property(x => x.DispositionOwner).HasMaxLength(200);
    profile.Property(x => x.BackupRequirements).HasMaxLength(1000);
    profile.HasIndex(x => new { x.FirmId, x.ProfileCode, x.Version }).IsUnique()
      .HasDatabaseName("ux_records_profile_code_version");
    profile.ToTable("records_profiles", t => t.HasCheckConstraint("ck_records_profile_values",
      "version >= 1 AND length(trim(profile_code)) > 0 AND length(trim(record_class)) > 0" +
      " AND length(trim(jurisdiction)) > 0 AND length(trim(service_route)) > 0" +
      " AND length(trim(retention_trigger)) > 0 AND (retention_duration_days IS NULL OR retention_duration_days > 0)" +
      " AND length(trim(protection_mode)) > 0 AND length(trim(label_id)) > 0" +
      " AND length(trim(legal_hold_behavior)) > 0 AND length(trim(amendment_route)) > 0" +
      " AND length(trim(disposition_owner)) > 0 AND length(trim(backup_requirements)) > 0" +
      " AND ((approved = false AND approved_at IS NULL AND approved_by_user_id IS NULL)" +
      " OR (approved = true AND approved_at IS NOT NULL AND approved_by_user_id IS NOT NULL))"));
    profile.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    profile.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var archive = b.Entity<Archive>();
    archive.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_archives_scope_id");
    archive.Property(x => x.ProfileId).HasMaxLength(100);
    archive.Property(x => x.ProfileVersion);
    archive.Property(x => x.ObservedProtectionState).HasMaxLength(100);
    archive.Property(x => x.Status).HasMaxLength(40);
    archive.HasIndex(x => new { x.FirmId, x.EngagementId, x.Status }).HasDatabaseName("ix_archives_scope_status");
    ScopeToEngagement(archive, nameof(Archive.FirmId), nameof(Archive.ClientId), nameof(Archive.EngagementId));
    archive.ToTable("archives", t => t.HasCheckConstraint("ck_archive_values",
      "length(trim(profile_id)) > 0 AND profile_version >= 1 AND status IN ('ISSUED','ASSEMBLY_IN_PROGRESS','MANIFEST_BUILT','ASSEMBLY_REVIEWED','RECORDS_ACTION_REQUESTED','PROTECTION_OBSERVED','ARCHIVE_VERIFIED')" +
      " AND ((status IN ('PROTECTION_OBSERVED','ARCHIVE_VERIFIED') AND length(trim(observed_protection_state)) > 0 AND observed_protection_at IS NOT NULL)" +
      " OR status IN ('ISSUED','ASSEMBLY_IN_PROGRESS','MANIFEST_BUILT','ASSEMBLY_REVIEWED','RECORDS_ACTION_REQUESTED'))"));

    var manifest = b.Entity<ArchiveManifest>();
    manifest.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_archive_manifests_firm_id_id");
    manifest.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_archive_manifests_scope_id");
    manifest.Property(x => x.Status).HasMaxLength(30);
    manifest.Property(x => x.ManifestDigest).HasMaxLength(64);
    manifest.Property(x => x.CompletenessStatus).HasMaxLength(30);
    manifest.Property(x => x.CompletenessException).HasMaxLength(2000);
    manifest.HasIndex(x => new { x.FirmId, x.ArchiveId, x.Version }).IsUnique()
      .HasDatabaseName("ux_archive_manifest_archive_version");
    ScopeToEngagement(manifest, nameof(ArchiveManifest.FirmId), nameof(ArchiveManifest.ClientId), nameof(ArchiveManifest.EngagementId));
    manifest.ToTable("archive_manifests", t =>
    {
      t.HasCheckConstraint("ck_archive_manifest_values",
        "version >= 1 AND status IN ('BUILT','REVIEWED') AND manifest_digest ~ '^[0-9a-f]{64}$'" +
        " AND entry_count >= 0 AND completeness_status IN ('COMPLETE','INCOMPLETE')" +
        " AND ((completeness_status = 'INCOMPLETE' AND length(trim(completeness_exception)) > 0)" +
        " OR (completeness_status = 'COMPLETE' AND completeness_exception IS NULL))");
      t.HasCheckConstraint("ck_archive_manifests_lineage_no_self_ref",
        "(predecessor_manifest_id IS NULL OR predecessor_manifest_id <> id) AND (superseded_by_manifest_id IS NULL OR superseded_by_manifest_id <> id)");
    });
    manifest.HasOne<Archive>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    // Self-referential predecessor link: the first manifest has no predecessor; re-archives chain back.
    manifest.Property(x => x.PredecessorManifestId).HasColumnName("predecessor_manifest_id");
    manifest.HasOne<ArchiveManifest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PredecessorManifestId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict)
      .IsRequired(false);
    // Self-referential supersession link: set on a manifest when it is superseded by a newer version.
    manifest.Property(x => x.SupersededByManifestId).HasColumnName("superseded_by_manifest_id");
    manifest.HasOne<ArchiveManifest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.SupersededByManifestId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict)
      .IsRequired(false);

    var structuredExport = b.Entity<ArchiveStructuredExport>();
    structuredExport.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_archive_structured_exports_firm_id_id");
    structuredExport.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_archive_structured_exports_scope_id");
    structuredExport.Property(x => x.Schema).HasMaxLength(100);
    structuredExport.Property(x => x.ContentHash).HasMaxLength(64);
    structuredExport.Property(x => x.PayloadJson).HasColumnType("text");
    structuredExport.HasIndex(x => new { x.FirmId, x.ArchiveManifestId, x.Version }).IsUnique()
      .HasDatabaseName("ux_archive_structured_export_manifest_version");
    ScopeToEngagement(structuredExport, nameof(ArchiveStructuredExport.FirmId),
      nameof(ArchiveStructuredExport.ClientId), nameof(ArchiveStructuredExport.EngagementId));
    structuredExport.ToTable("archive_structured_exports", t => t.HasCheckConstraint("ck_archive_structured_export_values",
      "version >= 1 AND schema = 'records-export.v1' AND length(trim(payload_json)) > 0" +
      " AND content_hash ~ '^[0-9a-f]{64}$' AND byte_count > 0"));
    structuredExport.HasOne<Archive>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    structuredExport.HasOne<ArchiveManifest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveManifestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var manifestEntry = b.Entity<ArchiveManifestEntry>();
    manifestEntry.Property(x => x.EntryKind).HasMaxLength(100);
    manifestEntry.Property(x => x.SourceKind).HasMaxLength(100);
    manifestEntry.Property(x => x.RelativeName).HasMaxLength(2000);
    manifestEntry.Property(x => x.ContentHash).HasMaxLength(64);
    manifestEntry.Property(x => x.MetadataJson).HasMaxLength(16384);
    manifestEntry.HasIndex(x => new { x.FirmId, x.ArchiveManifestId, x.Ordinal }).IsUnique()
      .HasDatabaseName("ux_archive_manifest_entry_ordinal");
    manifestEntry.ToTable("archive_manifest_entries", t => t.HasCheckConstraint("ck_archive_manifest_entry_values",
      "ordinal >= 1 AND length(trim(entry_kind)) > 0 AND length(trim(source_kind)) > 0" +
      " AND length(trim(relative_name)) > 0 AND content_hash ~ '^[0-9a-f]{64}$' AND byte_count >= 0" +
      " AND length(metadata_json) <= 16384"));
    manifestEntry.HasOne<ArchiveManifest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveManifestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var recordsAction = b.Entity<RecordsAction>();
    recordsAction.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_records_actions_firm_id_id");
    recordsAction.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_records_actions_scope_id");
    recordsAction.Property(x => x.DesiredLabel).HasMaxLength(200);
    recordsAction.Property(x => x.DesiredProtection).HasMaxLength(200);
    recordsAction.Property(x => x.ObservedLabel).HasMaxLength(200);
    recordsAction.Property(x => x.ObservedProtection).HasMaxLength(200);
    recordsAction.Property(x => x.State).HasMaxLength(30);
    recordsAction.Property(x => x.ExternalSystem).HasMaxLength(100);
    recordsAction.Property(x => x.ExternalReference).HasMaxLength(500);
    recordsAction.Property(x => x.ObservedBy).HasMaxLength(200);
    recordsAction.Property(x => x.Exception).HasMaxLength(2000);
    recordsAction.HasIndex(x => new { x.FirmId, x.ArchiveId }).IsUnique()
      .HasDatabaseName("ux_records_action_archive");
    recordsAction.ToTable("records_actions", t => t.HasCheckConstraint("ck_records_action_values",
      "length(trim(desired_label)) > 0 AND length(trim(desired_protection)) > 0" +
      " AND state IN ('REQUESTED','OBSERVED','FAILED')" +
      " AND ((state = 'OBSERVED' AND length(trim(observed_label)) > 0 AND length(trim(observed_protection)) > 0 AND observed_at IS NOT NULL AND length(trim(observed_by)) > 0)" +
      " OR (state = 'FAILED' AND length(trim(exception)) > 0) OR state = 'REQUESTED')"));
    recordsAction.HasOne<Archive>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    recordsAction.HasOne<ArchiveManifest>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveManifestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    recordsAction.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.RequestedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var recordsActionEvidence = b.Entity<RecordsActionEvidence>();
    recordsActionEvidence.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_records_action_evidence_firm_id_id");
    recordsActionEvidence.Property(x => x.EventKind).HasMaxLength(30);
    recordsActionEvidence.Property(x => x.DesiredLabel).HasMaxLength(200);
    recordsActionEvidence.Property(x => x.DesiredProtection).HasMaxLength(200);
    recordsActionEvidence.Property(x => x.ObservedLabel).HasMaxLength(200);
    recordsActionEvidence.Property(x => x.ObservedProtection).HasMaxLength(200);
    recordsActionEvidence.Property(x => x.ExternalReference).HasMaxLength(500);
    recordsActionEvidence.Property(x => x.ObservedBy).HasMaxLength(200);
    recordsActionEvidence.Property(x => x.Exception).HasMaxLength(2000);
    recordsActionEvidence.HasIndex(x => new { x.FirmId, x.RecordsActionId, x.Sequence }).IsUnique()
      .HasDatabaseName("ux_records_action_evidence_sequence");
    recordsActionEvidence.ToTable("records_action_evidence", t => t.HasCheckConstraint("ck_records_action_evidence_values",
      "sequence >= 1 AND event_kind IN ('REQUESTED','OBSERVED','FAILED')" +
      " AND length(trim(desired_label)) > 0 AND length(trim(desired_protection)) > 0" +
      " AND ((event_kind = 'OBSERVED' AND length(trim(observed_label)) > 0 AND length(trim(observed_protection)) > 0 AND length(trim(observed_by)) > 0)" +
      " OR (event_kind = 'FAILED' AND length(trim(exception)) > 0) OR event_kind = 'REQUESTED')"));
    ScopeToEngagement(recordsActionEvidence, nameof(RecordsActionEvidence.FirmId),
      nameof(RecordsActionEvidence.ClientId), nameof(RecordsActionEvidence.EngagementId));
    recordsActionEvidence.HasOne<RecordsAction>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.RecordsActionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    recordsActionEvidence.HasOne<Archive>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    recordsActionEvidence.HasOne<ArchiveManifest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveManifestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    recordsActionEvidence.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var legalHold = b.Entity<LegalHold>();
    legalHold.Property(x => x.HoldReference).HasMaxLength(200);
    legalHold.Property(x => x.State).HasMaxLength(30);
    legalHold.Property(x => x.ExternalSystem).HasMaxLength(100);
    legalHold.Property(x => x.ExternalReference).HasMaxLength(500);
    legalHold.Property(x => x.Notes).HasMaxLength(2000);
    legalHold.HasIndex(x => new { x.FirmId, x.ArchiveId, x.HoldReference }).IsUnique()
      .HasDatabaseName("ux_legal_hold_reference");
    legalHold.ToTable("legal_holds", t => t.HasCheckConstraint("ck_legal_hold_values",
      "length(trim(hold_reference)) > 0 AND state IN ('REQUESTED','APPLIED','OBSERVED','RELEASED')" +
      " AND ((state IN ('APPLIED','OBSERVED') AND applied_at IS NOT NULL) OR state IN ('REQUESTED','RELEASED'))" +
      " AND ((state = 'OBSERVED' AND observed_at IS NOT NULL) OR state <> 'OBSERVED')" +
      " AND ((state = 'RELEASED' AND released_at IS NOT NULL) OR state <> 'RELEASED')"));
    legalHold.HasOne<Archive>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    legalHold.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.RequestedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var recordState = b.Entity<RecordState>();
    recordState.Property(x => x.ArtifactKind).HasMaxLength(50);
    recordState.Property(x => x.LocalState).HasMaxLength(30);
    recordState.Property(x => x.ObservedLabel).HasMaxLength(200);
    recordState.HasIndex(x => new { x.FirmId, x.EngagementId, x.ArtifactId }).IsUnique()
      .HasDatabaseName("ux_record_state_artifact");
    ScopeToEngagement(recordState, nameof(RecordState.FirmId), nameof(RecordState.ClientId),
      nameof(RecordState.EngagementId));
    recordState.ToTable("record_states", t => t.HasCheckConstraint("ck_record_state_values",
      "length(trim(artifact_kind)) > 0 AND local_state IN ('Active','Locked','Archived')"));

    var attestation = b.Entity<ProtectionAttestation>();
    attestation.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_protection_attestations_firm_id_id");
    attestation.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_protection_attestations_scope_id");
    attestation.Property(x => x.ArtifactHash).HasMaxLength(64);
    attestation.Property(x => x.Binding).HasMaxLength(200);
    attestation.Property(x => x.ProfileId).HasMaxLength(100);
    attestation.Property(x => x.ObservedState).HasMaxLength(30);
    attestation.Property(x => x.Verifier).HasMaxLength(200);
    attestation.Property(x => x.RecheckRule).HasMaxLength(200);
    attestation.HasIndex(x => new { x.FirmId, x.EngagementId, x.ArtifactHash }).HasDatabaseName("ix_protection_attestations_artifact");
    attestation.ToTable("protection_attestations", t => t.HasCheckConstraint("ck_protection_attestation_values",
      "artifact_hash ~ '^[0-9a-f]{64}$' AND profile_version >= 1 AND length(trim(binding)) > 0 AND length(trim(profile_id)) > 0 AND observed_state IN ('PROTECTED','PENDING','EXPIRED','RECHECK_REQUIRED')"));
    ScopeToEngagement(attestation, nameof(ProtectionAttestation.FirmId), nameof(ProtectionAttestation.ClientId),
      nameof(ProtectionAttestation.EngagementId));

    var acceptance = b.Entity<AcceptanceDecision>();

    acceptance.Property(x => x.Decision).HasMaxLength(40);
    acceptance.Property(x => x.ServiceRoute).HasMaxLength(50);
    acceptance.HasIndex(x => new { x.FirmId, x.PracticeClientId, x.Generation }).HasDatabaseName("ix_acceptance_client_generation");
    acceptance.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PracticeClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    acceptance.ToTable("acceptance_decisions", t => t.HasCheckConstraint("ck_acceptance_decision_values",
      "generation >= 1 AND length(trim(service_route)) > 0" +
      " AND decision IN ('Pending','Accepted','AcceptedWithConditions','Declined')" +
      " AND ((decision IN ('Accepted','AcceptedWithConditions','Declined') AND decided_at IS NOT NULL" +
      "        AND decided_by_user_id IS NOT NULL) OR decision = 'Pending')"));

    var evaluation = b.Entity<EvaluationResponse>();
    evaluation.Property(x => x.Bank).HasMaxLength(4);
    evaluation.Property(x => x.QuestionId).HasMaxLength(50);
    evaluation.Property(x => x.Answer).HasMaxLength(2000);
    evaluation.HasIndex(x => new { x.FirmId, x.PracticeClientId, x.Bank, x.QuestionId, x.Revision }).IsUnique()
      .HasDatabaseName("ux_evaluation_response_question");
    evaluation.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PracticeClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    evaluation.ToTable("evaluation_responses", t => t.HasCheckConstraint("ck_evaluation_response_values",
      "bank IN ('CE','RV') AND revision >= 1 AND length(trim(question_id)) > 0 AND length(trim(answer)) > 0"));

    var mappingRule = b.Entity<MappingRule>();
    mappingRule.Property(x => x.MappingCode).HasMaxLength(50);
    mappingRule.Property(x => x.SourcePattern).HasMaxLength(200);
    mappingRule.HasIndex(x => new { x.FirmId, x.EngagementId, x.MappingCode, x.Revision }).IsUnique()
      .HasDatabaseName("ux_mapping_rule_code_revision");
    ScopeToEngagement(mappingRule, nameof(MappingRule.FirmId), nameof(MappingRule.ClientId),
      nameof(MappingRule.EngagementId));
    mappingRule.ToTable("mapping_rules", t => t.HasCheckConstraint("ck_mapping_rule_values",
      "revision >= 1 AND length(trim(mapping_code)) > 0 AND length(trim(source_pattern)) > 0"));
  }

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
