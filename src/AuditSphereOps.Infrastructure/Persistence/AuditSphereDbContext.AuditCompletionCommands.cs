using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureAuditCompletionCommands(ModelBuilder b)
  {
    var receipt = b.Entity<AuditCompletionVarianceCommandReceipt>();
    receipt.ToTable("audit_completion_variance_command_receipts", t => t.HasCheckConstraint(
      "ck_audit_completion_variance_command_receipt_values",
      "actor_epoch >= 0 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
      "variance_investigation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
      "request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND " +
      "action IN ('PREPARE','REVIEW') AND length(input_json) BETWEEN 1 AND 120000 AND " +
      "length(preview_json) BETWEEN 1 AND 1000000 AND length(result_json) BETWEEN 1 AND 1000000 AND " +
      "jsonb_typeof(input_json::jsonb) = 'object' AND jsonb_typeof(preview_json::jsonb) = 'object' AND " +
      "jsonb_typeof(result_json::jsonb) = 'object'"));
    receipt.Property(x => x.RequestHash).HasMaxLength(64);
    receipt.Property(x => x.ReviewBasis).HasMaxLength(64);
    receipt.Property(x => x.Action).HasMaxLength(20);
    receipt.Property(x => x.InputJson).HasMaxLength(AuditCompletionVarianceCommandReceipt.MaximumInputJsonLength);
    receipt.Property(x => x.PreviewJson).HasColumnType("text");
    receipt.Property(x => x.ResultJson).HasColumnType("text");
    receipt.HasIndex(x => new { x.FirmId, x.ActorId, x.RequestId }).IsUnique()
      .HasDatabaseName("ux_audit_completion_variance_receipt_actor_request");
    receipt.HasIndex(x => new { x.FirmId, x.EngagementId, x.CreatedAt })
      .HasDatabaseName("ix_audit_completion_variance_receipt_engagement_history");
    receipt.HasOne<Engagement>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    receipt.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    receipt.HasOne<AnalyticalReviewVarianceInvestigation>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.VarianceInvestigationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
