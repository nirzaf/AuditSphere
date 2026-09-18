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
  public DbSet<DocumentReference> DocumentReferences => Set<DocumentReference>();
  public DbSet<DocumentSnapshot> DocumentSnapshots => Set<DocumentSnapshot>();
  public DbSet<TrialBalanceDataset> TrialBalanceDatasets => Set<TrialBalanceDataset>();
  public DbSet<TrialBalanceRow> TrialBalanceRows => Set<TrialBalanceRow>();
  public DbSet<MappingRule> MappingRules => Set<MappingRule>();
  public DbSet<AdjustmentJournal> AdjustmentJournals => Set<AdjustmentJournal>();
  public DbSet<AdjustmentLine> AdjustmentLines => Set<AdjustmentLine>();
  public DbSet<JournalSourceReconciliation> JournalSourceReconciliations => Set<JournalSourceReconciliation>();
  public DbSet<AdjustmentPlan> AdjustmentPlans => Set<AdjustmentPlan>();
  public DbSet<AdjustmentPlanLine> AdjustmentPlanLines => Set<AdjustmentPlanLine>();
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
    ConfigureSecurity(b);
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
    b.Entity<TrialBalanceDataset>().ToTable("trial_balance_datasets", table =>
      table.HasCheckConstraint("ck_tb_validation_status",
        "validation_status IN ('Pending', 'Accepted', 'Rejected') AND (validation_status <> 'Accepted' OR (balanced AND control_total = 0))"));
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
