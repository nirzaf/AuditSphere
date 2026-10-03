using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientConversions(ModelBuilder b)
  {
    var e = b.Entity<ClientConversion>();
    e.ToTable("client_conversions", t => t.HasCheckConstraint("ck_client_conversion",
      "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(preview_json) BETWEEN 1 AND 200000"));
    e.Property(x => x.RequestHash).HasMaxLength(64);
    e.Property(x => x.ReviewBasis).HasMaxLength(64);
    e.HasIndex(x => new { x.FirmId, x.ActorId, x.RequestId }).IsUnique();
    e.HasIndex(x => x.ProposalId).IsUnique();
    e.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<Proposal>().WithMany().HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<PracticeClient>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
  }
}
