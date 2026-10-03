using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientContactCreations(ModelBuilder b)
  {
    var e = b.Entity<ClientContactCreation>();
    e.ToTable("client_contact_creations", t => t.HasCheckConstraint("ck_client_contact_creation",
      "actor_epoch >= 1 AND previous_generation >= 1 AND result_generation = previous_generation + 1" +
      " AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$'" +
      " AND length(input_json) BETWEEN 1 AND 4000 AND length(previous_primary_json) BETWEEN 1 AND 25000"));
    e.Property(x => x.RequestHash).HasMaxLength(64);
    e.Property(x => x.ReviewBasis).HasMaxLength(64);
    e.HasIndex(x => new { x.FirmId, x.ActorId, x.RequestId }).IsUnique();
    e.HasIndex(x => new { x.FirmId, x.ContactId }).IsUnique();
    e.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<ClientContact>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ContactId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
