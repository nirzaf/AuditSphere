using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureAdjustmentJournalActions(ModelBuilder b)
  {
    var e = b.Entity<AdjustmentJournalAction>();
    e.ToTable("adjustment_journal_actions", t => t.HasCheckConstraint("ck_adjustment_journal_action",
      "((action='CREATE' AND old_revision=0 AND old_status='NOT_CREATED' AND new_revision=1 AND new_status='Draft')" +
      " OR (action IN ('UPDATE','SUBMIT','RETURN','POST','REVERSE') AND old_revision >= 1)" +
      " OR (action='MANAGEMENT' AND old_revision >= 1 AND old_revision=new_revision AND old_status='Draft' AND new_status='Draft')) AND actor_epoch >= 1 AND new_revision >= 1" +
      " AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$'" +
      " AND length(trim(reason)) BETWEEN 1 AND 4000 AND length(trim(evidence_reference)) BETWEEN 1 AND 2000" +
      " AND length(before_json) BETWEEN 1 AND 250000 AND length(after_json) BETWEEN 1 AND 250000"));
    e.Property(x => x.Action).HasMaxLength(20);
    e.Property(x => x.RequestHash).HasMaxLength(64);
    e.Property(x => x.ReviewBasis).HasMaxLength(64);
    e.Property(x => x.OldStatus).HasMaxLength(30);
    e.Property(x => x.NewStatus).HasMaxLength(30);
    e.Property(x => x.Reason).HasMaxLength(4000);
    e.Property(x => x.EvidenceReference).HasMaxLength(2000);
    e.HasIndex(x => new { x.FirmId, x.ActorId, x.RequestId }).IsUnique();
    e.HasIndex(x => new { x.FirmId, x.JournalId, x.CreatedAt });
    e.HasOne<AdjustmentJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.JournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<AdjustmentJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ResultJournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
