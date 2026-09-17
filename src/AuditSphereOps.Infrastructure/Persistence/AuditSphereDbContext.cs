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
  public DbSet<BudgetVersion> BudgetVersions => Set<BudgetVersion>();
  public DbSet<BillingAccount> BillingAccounts => Set<BillingAccount>();
  public DbSet<Invoice> Invoices => Set<Invoice>();
  public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
  public DbSet<Receipt> Receipts => Set<Receipt>();
  public DbSet<Allocation> Allocations => Set<Allocation>();
  public DbSet<FirmAccount> FirmAccounts => Set<FirmAccount>();
  public DbSet<FirmPeriod> FirmPeriods => Set<FirmPeriod>();
  public DbSet<FirmJournal> FirmJournals => Set<FirmJournal>();
  public DbSet<FirmJournalLine> FirmJournalLines => Set<FirmJournalLine>();
  public DbSet<EvaluationResponse> EvaluationResponses => Set<EvaluationResponse>();
  public DbSet<AcceptanceDecision> AcceptanceDecisions => Set<AcceptanceDecision>();
  public DbSet<Engagement> Engagements => Set<Engagement>();
  public DbSet<EngagementHold> EngagementHolds => Set<EngagementHold>();
  public DbSet<DocumentReference> DocumentReferences => Set<DocumentReference>();
  public DbSet<DocumentSnapshot> DocumentSnapshots => Set<DocumentSnapshot>();
  public DbSet<TrialBalanceDataset> TrialBalanceDatasets => Set<TrialBalanceDataset>();
  public DbSet<TrialBalanceRow> TrialBalanceRows => Set<TrialBalanceRow>();
  public DbSet<MappingRule> MappingRules => Set<MappingRule>();
  public DbSet<AdjustmentJournal> AdjustmentJournals => Set<AdjustmentJournal>();
  public DbSet<AdjustmentLine> AdjustmentLines => Set<AdjustmentLine>();
  public DbSet<FinancialPackage> FinancialPackages => Set<FinancialPackage>();
  public DbSet<AuditRisk> AuditRisks => Set<AuditRisk>();
  public DbSet<AuditProcedure> AuditProcedures => Set<AuditProcedure>();
  public DbSet<Workpaper> Workpapers => Set<Workpaper>();
  public DbSet<Finding> Findings => Set<Finding>();
  public DbSet<ReviewPoint> ReviewPoints => Set<ReviewPoint>();
  public DbSet<Approval> Approvals => Set<Approval>();
  public DbSet<Release> Releases => Set<Release>();
  public DbSet<Archive> Archives => Set<Archive>();
  public DbSet<DurableOperation> DurableOperations => Set<DurableOperation>();
  public DbSet<OperationAttempt> OperationAttempts => Set<OperationAttempt>();
  public DbSet<OperationEvent> OperationEvents => Set<OperationEvent>();
  public DbSet<FirmSafetyState> FirmSafetyStates => Set<FirmSafetyState>();
  public DbSet<ClientSafetyState> ClientSafetyStates => Set<ClientSafetyState>();
  public DbSet<RecordState> RecordStates => Set<RecordState>();

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
    ConfigureAccounting(b);
    ConfigureOperations(b);
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
      t.HasCheckConstraint("ck_firm_safety", "deployment_epoch >= 1 AND operating_mode IN ('LOCAL_ONLY','RECOVERY_QUARANTINE')"));
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
    b.Entity<Invoice>().HasIndex(x => new { x.FirmId, x.InvoiceNumber }).IsUnique();
    b.Entity<FirmAccount>().HasIndex(x => new { x.FirmId, x.Code }).IsUnique();
    b.Entity<FirmJournal>().HasIndex(x => new { x.FirmId, x.SourceKind, x.SourceKey }).IsUnique();
    b.Entity<FirmPeriod>().HasIndex(x => new { x.FirmId, x.PeriodCode }).IsUnique();
  }

  private static void ConfigureAccounting(ModelBuilder b)
  {
    b.Entity<TrialBalanceDataset>().Property(x => x.ValidationStatus).HasMaxLength(16).HasDefaultValue("Pending");
    b.Entity<TrialBalanceDataset>().ToTable("trial_balance_datasets", table =>
      table.HasCheckConstraint("ck_tb_validation_status",
        "validation_status IN ('Pending', 'Accepted', 'Rejected') AND (validation_status <> 'Accepted' OR (balanced AND control_total = 0))"));
    b.Entity<TrialBalanceRow>().HasIndex(x => x.DatasetId);
    b.Entity<AdjustmentJournal>().HasIndex(x => new { x.FirmId, x.EngagementId, x.BaseDatasetId, x.JournalNumber }).IsUnique();
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
