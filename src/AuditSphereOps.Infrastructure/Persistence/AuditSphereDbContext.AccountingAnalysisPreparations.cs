using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureAccountingAnalysisPreparations(ModelBuilder b)
  {
    var e = b.Entity<AccountingAnalysisPreparation>();
    e.ToTable("accounting_analysis_preparations", t => t.HasCheckConstraint("ck_accounting_analysis_preparation",
      "actor_epoch >= 1 AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$'" +
      " AND length(input_json) BETWEEN 1 AND 20000 AND length(context_json) BETWEEN 1 AND 10000" +
      " AND length(result_json) BETWEEN 1 AND 100000 AND length(trim(reason)) BETWEEN 1 AND 4000" +
      " AND length(trim(evidence_reference)) BETWEEN 1 AND 2000"));
    e.Property(x => x.RequestHash).HasMaxLength(64);
    e.Property(x => x.ReviewBasis).HasMaxLength(64);
    e.Property(x => x.Reason).HasMaxLength(4000);
    e.Property(x => x.EvidenceReference).HasMaxLength(2000);
    e.HasIndex(x => new { x.FirmId, x.ActorId, x.RequestId }).IsUnique();
    e.HasIndex(x => new { x.FirmId, x.EngagementId, x.EvidenceId }).IsUnique();
    e.HasOne<Engagement>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<AnalyticalReview>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.EvidenceId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
