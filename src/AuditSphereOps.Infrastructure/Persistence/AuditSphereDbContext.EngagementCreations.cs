using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Infrastructure.Persistence;
public sealed partial class AuditSphereDbContext
{
  private static void ConfigureEngagementCreations(ModelBuilder b)
  {
    var e=b.Entity<EngagementCreation>();
    e.ToTable("engagement_creations",t=>t.HasCheckConstraint("ck_engagement_creation",
      "actor_epoch >= 1 AND client_generation >= 1 AND engagement_generation = 1"+
      " AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid"+
      " AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(input_json) BETWEEN 1 AND 2000"));
    e.Property(x=>x.RequestHash).HasMaxLength(64);e.Property(x=>x.ReviewBasis).HasMaxLength(64);
    e.HasIndex(x=>new{x.FirmId,x.ActorId,x.RequestId}).IsUnique();
    e.HasIndex(x=>new{x.FirmId,x.EngagementId}).IsUnique();
    e.HasOne<PracticeClient>().WithMany().HasForeignKey(x=>new{x.FirmId,x.ClientId}).HasPrincipalKey(x=>new{x.FirmId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<AppUser>().WithMany().HasForeignKey(x=>new{x.FirmId,x.ActorId}).HasPrincipalKey(x=>new{x.FirmId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<Engagement>().WithMany().HasForeignKey(x=>x.EngagementId).OnDelete(DeleteBehavior.Restrict);
  }
}
