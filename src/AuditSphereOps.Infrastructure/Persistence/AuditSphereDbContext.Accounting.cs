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
public sealed partial class AuditSphereDbContext
{
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
    journal.Property(x => x.ReturnReason).HasMaxLength(2000);
    journal.ToTable("adjustment_journals", table => table.HasCheckConstraint("ck_adjustment_journal_values",
      "purpose IN ('CLIENT_BOOK_CORRECTION','REPORTING_ADJUSTMENT','PRESENTATION_RECLASSIFICATION','GROUP_ONLY_ELIMINATION')" +
      " AND origin IN ('AUDIT_PROPOSED','CLIENT_REQUESTED','MANAGEMENT_PROVIDED','IMPORTED')" +
      " AND status IN ('Draft','Submitted','Returned','Posted','ReflectedInSource','Void')" +
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
}
