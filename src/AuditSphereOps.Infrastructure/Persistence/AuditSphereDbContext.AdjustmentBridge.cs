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
}
