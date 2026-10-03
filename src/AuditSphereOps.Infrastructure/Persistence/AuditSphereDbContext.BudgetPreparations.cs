using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Engagements;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Infrastructure.Persistence;
public sealed partial class AuditSphereDbContext
{
  private static void ConfigureBudgetPreparations(ModelBuilder b)
  {
    var e=b.Entity<BudgetPreparation>();
    e.ToTable("budget_preparations",t=>t.HasCheckConstraint("ck_budget_preparation", "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(preview_json) BETWEEN 1 AND 200000"));
    e.Property(x=>x.RequestHash).HasMaxLength(64);e.Property(x=>x.ReviewBasis).HasMaxLength(64);
    e.HasIndex(x=>new{x.FirmId,x.ActorId,x.RequestId}).IsUnique();
    e.HasIndex(x=>x.BudgetId).IsUnique();
    e.HasOne<AppUser>().WithMany().HasForeignKey(x=>new{x.FirmId,x.ActorId}).HasPrincipalKey(x=>new{x.FirmId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<Engagement>().WithMany().HasForeignKey(x=>x.EngagementId).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<EngagementBudget>().WithMany().HasForeignKey(x=>x.BudgetId).OnDelete(DeleteBehavior.Restrict);
  }
}
