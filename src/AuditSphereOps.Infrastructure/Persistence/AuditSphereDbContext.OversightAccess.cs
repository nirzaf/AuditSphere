using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureAdministratorOversightAccess(ModelBuilder b)
  {
    var e = b.Entity<AdministratorOversightAccessEvent>();
    e.ToTable("administrator_oversight_access_events", t => t.HasCheckConstraint("ck_oversight_access_event",
      "session_epoch >= 1 AND length(operation) BETWEEN 1 AND 100" +
      " AND resource_kind IN ('FIRM','INVOICE','GROUP','CONSOLIDATION_SCOPE','EXTERNAL_PACK')" +
      " AND ((resource_kind = 'FIRM' AND resource_id IS NULL) OR (resource_kind <> 'FIRM' AND resource_id IS NOT NULL))" +
      " AND outcome = 'DISCLOSURE_AUTHORIZED'"));
    e.HasKey(x => x.Id);
    e.HasIndex(x => new { x.FirmId, x.RequestId }).IsUnique();
    e.HasIndex(x => new { x.FirmId, x.AuthorizedAt, x.Id });
    e.Property(x => x.Operation).HasMaxLength(100);
    e.Property(x => x.ResourceKind).HasMaxLength(30);
    e.Property(x => x.Outcome).HasMaxLength(30);
    e.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
