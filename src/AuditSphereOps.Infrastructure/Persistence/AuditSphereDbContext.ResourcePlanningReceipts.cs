using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;
public sealed partial class AuditSphereDbContext
{
  private static void ConfigureResourcePlanningReceipts(ModelBuilder b)
  {
    var e=b.Entity<ResourcePlanningReceipt>();
    e.ToTable("resource_planning_receipts", t=>t.HasCheckConstraint("ck_resource_planning_receipt",
      "actor_epoch >= 1 AND kind IN ('PROFILE','CERTIFICATION','AVAILABILITY','ALLOCATION')"+
      " AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid"+
      " AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$'"+
      " AND length(preview_json) BETWEEN 1 AND 100000"+
      " AND (resource_id IS NOT NULL OR kind = 'ALLOCATION')"));
    e.Property(x=>x.Kind).HasMaxLength(20);
    e.Property(x=>x.RequestHash).HasMaxLength(64);
    e.Property(x=>x.ReviewBasis).HasMaxLength(64);
    e.HasIndex(x=>new{x.FirmId,x.ActorId,x.RequestId}).IsUnique();
    e.HasOne<AppUser>().WithMany().HasForeignKey(x=>new{x.FirmId,x.ActorId}).HasPrincipalKey(x=>new{x.FirmId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<AppUser>().WithMany().HasForeignKey(x=>new{x.FirmId,x.TargetUserId}).HasPrincipalKey(x=>new{x.FirmId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  }
}
