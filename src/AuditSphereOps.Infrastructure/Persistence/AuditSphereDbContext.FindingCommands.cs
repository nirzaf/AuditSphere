using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureFindingCommands(ModelBuilder b)
  {
    b.Entity<Finding>().Property(x => x.Revision).HasDefaultValue(1L);
    b.Entity<Finding>().ToTable("findings", t => t.HasCheckConstraint("ck_finding_revision", "revision >= 1"));
    var e = b.Entity<FindingAmendment>();
    e.ToTable("finding_amendments", t => t.HasCheckConstraint("ck_finding_amendment_values",
      "actor_epoch >= 1 AND before_revision >= 1 AND after_revision = before_revision + 1 AND " +
      "action IN ('RECORD_RESPONSE','DESIGNATE_MANAGEMENT_LETTER','UNDESIGNATE_MANAGEMENT_LETTER') AND " +
      "length(before_json) BETWEEN 1 AND 1000000 AND length(after_json) BETWEEN 1 AND 1000000 AND " +
      "jsonb_typeof(before_json::jsonb) = 'object' AND jsonb_typeof(after_json::jsonb) = 'object'"));
    e.Property(x => x.Action).HasMaxLength(40); e.Property(x => x.BeforeJson).HasColumnType("text"); e.Property(x => x.AfterJson).HasColumnType("text");
    e.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FindingId, x.Id }).HasName("ak_finding_amendment_scope_id");
    e.HasIndex(x => new { x.FirmId, x.ActorId }).HasDatabaseName("ix_finding_amendment_actor");
    e.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FindingId }).HasDatabaseName("ix_finding_amendment_finding");
    e.HasIndex(x => new { x.FirmId, x.FindingId, x.AfterRevision }).IsUnique().HasDatabaseName("ux_finding_amendment_revision");
    e.HasOne<Finding>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FindingId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId }).HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    var r = b.Entity<FindingCommandReceipt>();
    r.ToTable("finding_command_receipts", t => t.HasCheckConstraint("ck_finding_command_receipt_values",
      "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
      "request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND " +
      "action IN ('RECORD_RESPONSE','DESIGNATE_MANAGEMENT_LETTER','UNDESIGNATE_MANAGEMENT_LETTER') AND " +
      "length(input_json) BETWEEN 1 AND 150000 AND length(preview_json) BETWEEN 1 AND 1000000 AND " +
      "jsonb_typeof(input_json::jsonb) = 'object' AND jsonb_typeof(preview_json::jsonb) = 'object'"));
    r.Property(x => x.Action).HasMaxLength(40); r.Property(x => x.RequestHash).HasMaxLength(64); r.Property(x => x.ReviewBasis).HasMaxLength(64);
    r.Property(x => x.InputJson).HasMaxLength(FindingCommandReceipt.MaximumInputLength); r.Property(x => x.PreviewJson).HasColumnType("text");
    r.HasIndex(x => new { x.FirmId, x.ActorId, x.RequestId }).IsUnique().HasDatabaseName("ux_finding_receipt_actor_request");
    r.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FindingId, x.AmendmentId }).HasDatabaseName("ix_finding_receipt_event_scope");
    r.HasIndex(x => new { x.FirmId, x.AmendmentId }).IsUnique().HasDatabaseName("ux_finding_receipt_amendment");
    r.HasOne<FindingAmendment>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FindingId, x.AmendmentId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FindingId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    r.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId }).HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
