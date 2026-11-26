using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientMaintenance(ModelBuilder b)
  {
    var e = b.Entity<ClientMaintenanceReceipt>();
    e.ToTable("client_maintenance_receipts", t => t.HasCheckConstraint("ck_client_maintenance_receipt",
      "operation_kind IN ('PROFILE','CONTACT') AND actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid" +
      " AND previous_generation >= 1 AND result_generation = previous_generation + 1" +
      " AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$'" +
      " AND ((operation_kind = 'PROFILE' AND contact_id IS NULL) OR (operation_kind = 'CONTACT' AND contact_id IS NOT NULL))" +
      " AND length(before_json) BETWEEN 1 AND 10000000 AND length(after_json) BETWEEN 1 AND 10000000" +
      " AND jsonb_typeof(before_json::jsonb) = 'object' AND jsonb_typeof(after_json::jsonb) = 'object'"));
    e.Property(x => x.OperationKind).HasMaxLength(20);
    e.Property(x => x.RequestHash).HasMaxLength(64);
    e.Property(x => x.ReviewBasis).HasMaxLength(64);
    e.HasIndex(x => new { x.FirmId, x.ActorId, x.RequestId }).IsUnique()
      .HasDatabaseName("ux_client_maintenance_receipt_actor_request");
    e.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<ClientContact>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ContactId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
