using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureTimeEntryChanges(ModelBuilder b)
  {
    var receipt = b.Entity<TimeEntryChangeReceipt>();
    receipt.ToTable("time_entry_change_receipts", t => t.HasCheckConstraint("ck_time_entry_change_receipt",
      "actor_epoch >= 1 AND source_revision >= 1 AND result_revision > source_revision" +
      " AND result_revision - source_revision = 1" +
      " AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid" +
      " AND request_hash ~ '^[0-9a-f]{64}$'" +
      " AND kind IN ('EDIT_UNAPPROVED','CORRECT_APPROVED')" +
      " AND octet_length(before_json) BETWEEN 1 AND 64000" +
      " AND octet_length(source_after_json) BETWEEN 1 AND 64000" +
      " AND octet_length(result_json) BETWEEN 1 AND 64000" +
      " AND ((kind = 'EDIT_UNAPPROVED' AND result_time_entry_id = time_entry_id)" +
      " OR (kind = 'CORRECT_APPROVED' AND result_time_entry_id <> time_entry_id))"));
    receipt.Property(x => x.Kind).HasMaxLength(20);
    receipt.Property(x => x.RequestHash).HasMaxLength(64);
    receipt.Property(x => x.Reason).HasMaxLength(2000);
    receipt.HasIndex(x => new { x.FirmId, x.ActorId, x.RequestId }).IsUnique();
    receipt.HasIndex(x => new { x.FirmId, x.ResultTimeEntryId, x.ResultRevision }).IsUnique();
    receipt.HasIndex(x => new { x.FirmId, x.TimeEntryId, x.SourceRevision }).IsUnique();
    receipt.HasIndex(x => new { x.FirmId, x.TimeEntryId, x.CreatedAt, x.Id });
    receipt.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    receipt.HasOne<TimeEntry>().WithMany().HasForeignKey(x => new { x.FirmId, x.TimeEntryId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    receipt.HasOne<TimeEntry>().WithMany().HasForeignKey(x => new { x.FirmId, x.ResultTimeEntryId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
