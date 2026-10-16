using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureReviewPointCommands(ModelBuilder b)
  {
    b.Entity<ReviewPoint>().Property(x => x.DispositionRevision).HasDefaultValue(1L);
    b.Entity<ReviewPoint>().ToTable("review_points", t => t.HasCheckConstraint("ck_review_point_disposition_revision", "disposition_revision >= 1"));
    b.Entity<ReviewPoint>().HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).HasName("ak_review_point_scope_id");
    var e = b.Entity<ReviewPointDispositionEvent>();
    e.ToTable("review_point_disposition_events", t => t.HasCheckConstraint("ck_review_point_event_values",
      "actor_epoch >= 1 AND before_revision >= 1 AND after_revision = before_revision + 1 AND " +
      "action IN ('CLEAR','REOPEN') AND " +
      "length(before_json) BETWEEN 1 AND 1000000 AND length(after_json) BETWEEN 1 AND 1000000 AND " +
      "jsonb_typeof(before_json::jsonb) = 'object' AND jsonb_typeof(after_json::jsonb) = 'object'"));
    e.Property(x => x.Action).HasMaxLength(40); e.Property(x => x.BeforeJson).HasColumnType("text"); e.Property(x => x.AfterJson).HasColumnType("text");
    e.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ReviewPointId, x.Id }).HasName("ak_review_point_event_scope_id");
    e.HasIndex(x => new { x.FirmId, x.ActorId }).HasDatabaseName("ix_review_point_event_actor");
    e.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ReviewPointId }).HasDatabaseName("ix_review_point_event_point");
    e.HasIndex(x => new { x.FirmId, x.ReviewPointId, x.AfterRevision }).IsUnique().HasDatabaseName("ux_review_point_event_revision");
    e.HasOne<ReviewPoint>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ReviewPointId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId }).HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    var r = b.Entity<ReviewPointCommandReceipt>();
    r.ToTable("review_point_command_receipts", t => t.HasCheckConstraint("ck_review_point_command_receipt_values",
      "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
      "request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND " +
      "action IN ('CLEAR','REOPEN') AND " +
      "length(input_json) BETWEEN 1 AND 1000 AND length(preview_json) BETWEEN 1 AND 1000000 AND " +
      "jsonb_typeof(input_json::jsonb) = 'object' AND jsonb_typeof(preview_json::jsonb) = 'object'"));
    r.Property(x => x.Action).HasMaxLength(40); r.Property(x => x.RequestHash).HasMaxLength(64); r.Property(x => x.ReviewBasis).HasMaxLength(64);
    r.Property(x => x.InputJson).HasMaxLength(ReviewPointCommandReceipt.MaximumInputLength); r.Property(x => x.PreviewJson).HasColumnType("text");
    r.HasIndex(x => new { x.FirmId, x.ActorId, x.RequestId }).IsUnique().HasDatabaseName("ux_review_point_receipt_actor_request");
    r.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ReviewPointId, x.DispositionEventId }).HasDatabaseName("ix_review_point_receipt_event_scope");
    r.HasIndex(x => new { x.FirmId, x.DispositionEventId }).IsUnique().HasDatabaseName("ux_review_point_receipt_event");
    r.HasOne<ReviewPointDispositionEvent>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ReviewPointId, x.DispositionEventId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ReviewPointId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    r.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId }).HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
