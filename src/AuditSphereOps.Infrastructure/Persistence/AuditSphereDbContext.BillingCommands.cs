using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureBillingCommands(ModelBuilder b)
  {
    var e = b.Entity<BillingCommandReceipt>();
    e.ToTable("billing_command_receipts", t => t.HasCheckConstraint("ck_billing_command_receipt_values",
      "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
      "action IN ('RECORD_RECEIPT','ALLOCATE_RECEIPT','ISSUE_CREDIT_NOTE') AND request_hash ~ '^[0-9a-f]{64}$' AND " +
      "result_id <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
      "length(outcome_json) BETWEEN 1 AND 2000 AND jsonb_typeof(outcome_json::jsonb) = 'object' AND " +
      "outcome_json::jsonb ->> 'Action' = action AND outcome_json::jsonb ->> 'ResultId' = result_id::text"));
    e.Property(x => x.Action).HasMaxLength(30);
    e.Property(x => x.RequestHash).HasMaxLength(64);
    e.Property(x => x.OutcomeJson).HasColumnType("text");
    e.HasIndex(x => new { x.FirmId, x.ActorId, x.RequestId }).IsUnique()
      .HasDatabaseName("ux_billing_command_receipts_actor_request");
    e.HasIndex(x => new { x.FirmId, x.ClientId, x.CreatedAt });
    e.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
