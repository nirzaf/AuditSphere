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
  AuditSphereOps.Application.Operations.IAuditSphereDbContext
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
  public DbSet<TrialBalanceRow> TrialBalanceRows => Set<TrialBalanceRow>();
  public DbSet<MappingRule> MappingRules => Set<MappingRule>();
  public DbSet<MappingVersion> MappingVersions => Set<MappingVersion>();
  public DbSet<MappingAllocation> MappingAllocations => Set<MappingAllocation>();
  public DbSet<AdjustedTrialBalanceSnapshot> AdjustedTrialBalanceSnapshots => Set<AdjustedTrialBalanceSnapshot>();
  public DbSet<AdjustedTrialBalanceRow> AdjustedTrialBalanceRows => Set<AdjustedTrialBalanceRow>();
  public DbSet<AdjustmentJournal> AdjustmentJournals => Set<AdjustmentJournal>();
  public DbSet<AdjustmentLine> AdjustmentLines => Set<AdjustmentLine>();
  public DbSet<JournalSourceReconciliation> JournalSourceReconciliations => Set<JournalSourceReconciliation>();
  public DbSet<AdjustmentPlan> AdjustmentPlans => Set<AdjustmentPlan>();
  public DbSet<AdjustmentPlanLine> AdjustmentPlanLines => Set<AdjustmentPlanLine>();
  public DbSet<FinancialPackage> FinancialPackages => Set<FinancialPackage>();
  public DbSet<FinancialPackageLine> FinancialPackageLines => Set<FinancialPackageLine>();
  public DbSet<FinancialPackageValidation> FinancialPackageValidations => Set<FinancialPackageValidation>();
  public DbSet<FinancialPackageCashFlowLine> FinancialPackageCashFlowLines => Set<FinancialPackageCashFlowLine>();
  public DbSet<FinancialPackageDisclosure> FinancialPackageDisclosures => Set<FinancialPackageDisclosure>();
  public DbSet<MaterialityAssessment> MaterialityAssessments => Set<MaterialityAssessment>();
  public DbSet<AuditRisk> AuditRisks => Set<AuditRisk>();
  public DbSet<AuditProcedure> AuditProcedures => Set<AuditProcedure>();
  public DbSet<PopulationVersion> PopulationVersions => Set<PopulationVersion>();
  public DbSet<Workpaper> Workpapers => Set<Workpaper>();
  public DbSet<WorkpaperDraft> WorkpaperDrafts => Set<WorkpaperDraft>();
  public DbSet<WorkpaperSubmission> WorkpaperSubmissions => Set<WorkpaperSubmission>();
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
    ConfigureScopedEvidence(b);
    ConfigureAccounting(b);
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
      "target_kind = 'WORKPAPER' AND revision >= 1 AND target_revision >= 1 AND input_generation >= 1 AND policy_generation >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND status IN ('READY','ISSUED')"));
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
    b.Entity<TrialBalanceDataset>().Property(x => x.ValidationStatus).HasMaxLength(16).HasDefaultValue("Pending");
    b.Entity<TrialBalanceDataset>().Property(x => x.ImportState).HasMaxLength(16)
      .HasDefaultValue(TrialBalanceImportStates.Sealed).ValueGeneratedNever();
    b.Entity<TrialBalanceDataset>().ToTable("trial_balance_datasets", table =>
    {
      table.HasCheckConstraint("ck_tb_validation_status",
        "validation_status IN ('Pending', 'Accepted', 'Rejected') AND (validation_status <> 'Accepted' OR (balanced AND control_total = 0))");
      table.HasCheckConstraint("ck_tb_import_state",
        "import_state IN ('LOADING', 'SEALED')");
    });
    b.Entity<TrialBalanceRow>().HasIndex(x => x.DatasetId);
    b.Entity<AdjustmentJournal>().HasIndex(x => new { x.FirmId, x.EngagementId, x.BaseDatasetId, x.JournalNumber }).IsUnique();
    // Duplicate-file guard: the same source bytes can never become two datasets for one
    // engagement. Partial so legacy/empty-hash fixtures stay migratable; the import
    // command always stamps a real hash.
    b.Entity<TrialBalanceDataset>().HasIndex(x => new { x.FirmId, x.EngagementId, x.Sha256Hex }).IsUnique()
      .HasDatabaseName("ux_dataset_firm_engagement_hash").HasFilter("length(sha256_hex) > 0");
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
    b.Entity<TrialBalanceRow>()
      .HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => x.DatasetId)
      .OnDelete(DeleteBehavior.Restrict);
    b.Entity<AdjustmentJournal>()
      .HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => x.BaseDatasetId)
      .OnDelete(DeleteBehavior.Restrict);
    b.Entity<AdjustmentJournal>()
      .HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.EngagementId })
      .HasPrincipalKey(e => new { e.FirmId, e.Id })
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
    b.Entity<JournalSourceReconciliation>().ToTable("journal_source_reconciliations", t =>
    {
      t.HasCheckConstraint("ck_reconciliation_state",
        "state IN ('UNKNOWN','NOT_REFLECTED','REFLECTED','PARTIALLY_REFLECTED','NOT_APPLICABLE')");
      t.HasCheckConstraint("ck_reconciliation_revision", "journal_revision >= 1");
      t.HasCheckConstraint("ck_reconciliation_evidence",
        "(state IN ('REFLECTED','PARTIALLY_REFLECTED') AND length(evidence) > 0 AND length(evidence) <= 2000) OR " +
        "(state NOT IN ('REFLECTED','PARTIALLY_REFLECTED') AND length(evidence) <= 2000)");
      t.HasCheckConstraint("ck_reconciliation_number", "length(logical_journal_number) > 0 AND length(logical_journal_number) <= 32");
    });
    b.Entity<JournalSourceReconciliation>()
      .HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.EngagementId })
      .HasPrincipalKey(e => new { e.FirmId, e.Id })
      .OnDelete(DeleteBehavior.Restrict);
    b.Entity<JournalSourceReconciliation>()
      .HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => x.BaseDatasetId)
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
    allocation.HasIndex(x => new { x.FirmId, x.MappingVersionId, x.SourceAccountCode, x.DestinationCode })
      .IsUnique().HasDatabaseName("ux_mapping_allocation_identity");
    allocation.ToTable("mapping_allocations", t => t.HasCheckConstraint("ck_mapping_allocation_values",
      "length(source_account_code) > 0 AND length(destination_code) > 0 AND length(statement_section) > 0 AND fraction > 0 AND fraction <= 1 AND length(rationale) > 0"));
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
    package.HasIndex(x => new { x.FirmId, x.AdjustmentPlanId, x.MappingVersionId, x.TemplateVersion })
      .IsUnique().HasDatabaseName("ux_financial_package_identity");
    package.ToTable("financial_packages", t => t.HasCheckConstraint("ck_financial_package_values",
      "revision >= 1 AND generation >= 1 AND length(framework) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND length(taxonomy_version) > 0 AND length(template_version) > 0 AND length(calculation_engine_version) > 0 AND calculation_hash ~ '^[0-9a-f]{64}$' AND currency ~ '^[A-Z]{3}$' AND status IN ('REVIEW_REQUIRED','VALIDATED') AND ((cash_beginning IS NULL AND cash_ending IS NULL AND supplementary_hash IS NULL) OR (cash_beginning IS NOT NULL AND cash_ending IS NOT NULL AND supplementary_hash ~ '^[0-9a-f]{64}$'))"));
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

    var packageLine = b.Entity<FinancialPackageLine>();
    packageLine.Property(x => x.SourceAccountCode).HasMaxLength(100);
    packageLine.Property(x => x.DestinationCode).HasMaxLength(100);
    packageLine.Property(x => x.StatementSection).HasMaxLength(50);
    packageLine.Property(x => x.Currency).HasMaxLength(3);
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
    procedure.ToTable("audit_procedures", t => t.HasCheckConstraint("ck_audit_procedure_values",
      "length(trim(title)) > 0"));
    ScopeToEngagement(procedure, nameof(AuditProcedure.FirmId), nameof(AuditProcedure.ClientId),
      nameof(AuditProcedure.EngagementId));
    procedure.HasOne<AuditRisk>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.RiskId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

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
