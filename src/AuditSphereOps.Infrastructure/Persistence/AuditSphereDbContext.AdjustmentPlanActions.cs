using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureAdjustmentPlanActions(ModelBuilder b)
  {
    var e = b.Entity<AdjustmentPlanAction>();
    e.ToTable("adjustment_plan_actions", t => t.HasCheckConstraint("ck_adjustment_plan_action",
      "action IN ('CREATE','FINALIZE') AND actor_epoch >= 1" +
      " AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$'" +
      " AND length(trim(reason)) BETWEEN 1 AND 4000 AND length(trim(evidence_reference)) BETWEEN 1 AND 2000" +
      " AND length(before_json) BETWEEN 1 AND 250000 AND length(after_json) BETWEEN 1 AND 250000"));
    e.Property(x => x.Action).HasMaxLength(20);
    e.Property(x => x.RequestHash).HasMaxLength(64);
    e.Property(x => x.ReviewBasis).HasMaxLength(64);
    e.Property(x => x.Reason).HasMaxLength(4000);
    e.Property(x => x.EvidenceReference).HasMaxLength(2000);
    e.HasIndex(x => new { x.FirmId, x.ActorId, x.RequestId }).IsUnique();
    e.HasIndex(x => new { x.FirmId, x.PlanId, x.CreatedAt });
    e.HasOne<AdjustmentPlan>().WithMany().HasForeignKey(x => new { x.FirmId, x.PlanId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
