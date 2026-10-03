using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureAccountingEvidenceActions(ModelBuilder b)
  {
    var e = b.Entity<AccountingEvidenceAction>();
    e.ToTable("accounting_evidence_actions", t => t.HasCheckConstraint("ck_accounting_evidence_action",
      "evidence_kind IN ('ECL','INVENTORY','SPECIALIST','ANALYTICAL','JOURNAL_RISK') AND actor_epoch >= 1" +
      " AND ((action='LINK' AND result_id IS NOT NULL AND link_id IS NOT NULL AND decision='')" +
      " OR (action='REVIEW' AND result_id IS NULL AND link_id IS NULL AND decision IN ('APPROVED','CHANGES_REQUIRED','REJECTED','ESCALATED')))" +
      " AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$'" +
      " AND length(trim(reason)) BETWEEN 1 AND 4000 AND length(trim(evidence_reference)) BETWEEN 1 AND 2000" +
      " AND length(before_json) BETWEEN 1 AND 250000 AND length(after_json) BETWEEN 1 AND 250000"));
    e.Property(x => x.EvidenceKind).HasMaxLength(20);
    e.Property(x => x.Action).HasMaxLength(20);
    e.Property(x => x.Decision).HasMaxLength(30);
    e.Property(x => x.RequestHash).HasMaxLength(64);
    e.Property(x => x.ReviewBasis).HasMaxLength(64);
    e.Property(x => x.Reason).HasMaxLength(4000);
    e.Property(x => x.EvidenceReference).HasMaxLength(2000);
    e.HasIndex(x => new { x.FirmId, x.ActorId, x.RequestId }).IsUnique();
    e.HasIndex(x => new { x.FirmId, x.EvidenceKind, x.EvidenceId, x.CreatedAt });
    e.HasOne<Engagement>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<AuditProcedureResult>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ResultId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<AccountingEvidenceAuditLink>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.LinkId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
