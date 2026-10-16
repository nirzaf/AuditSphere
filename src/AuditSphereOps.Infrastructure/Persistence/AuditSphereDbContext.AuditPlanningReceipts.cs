using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureAuditPlanningReceipts(ModelBuilder b)
  {
    var e = b.Entity<AuditPlanningCommandReceipt>();
    e.ToTable("audit_planning_command_receipts", t => t.HasCheckConstraint("ck_audit_planning_command_receipt_values",
      "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
      "result_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND " +
      "action IN ('CALCULATE_MATERIALITY','ROUND_MATERIALITY','APPROVE_MATERIALITY','CREATE_RISK','ASSESS_RISK','ASSIGN_RISK_OWNER','CLEAR_RISK','LINK_PROCEDURE_RISK') AND " +
      "length(input_json) BETWEEN 1 AND 120000 AND length(preview_json) BETWEEN 1 AND 1000000 AND length(result_json) BETWEEN 1 AND 1000000 AND jsonb_typeof(input_json::jsonb) = 'object' AND jsonb_typeof(preview_json::jsonb) = 'object' AND jsonb_typeof(result_json::jsonb) = 'object' AND (input_json::jsonb->>'Action') IS NOT DISTINCT FROM action AND (result_json::jsonb->>'ResultId')::uuid IS NOT DISTINCT FROM result_id"));
    e.Property(x => x.Action).HasMaxLength(30);
    e.Property(x => x.RequestHash).HasMaxLength(64);
    e.Property(x => x.ReviewBasis).HasMaxLength(64);
    e.Property(x => x.InputJson).HasMaxLength(AuditPlanningCommandReceipt.MaximumInputJsonLength);
    e.Property(x => x.PreviewJson).HasColumnType("text");
    e.Property(x => x.ResultJson).HasColumnType("text");
    e.HasIndex(x => new { x.FirmId, x.ActorId, x.RequestId }).IsUnique().HasDatabaseName("ux_audit_planning_actor_request");
    e.HasIndex(x => new { x.FirmId, x.EngagementId, x.Action, x.ResultId }).IsUnique().HasFilter("action <> 'LINK_PROCEDURE_RISK'").HasDatabaseName("ux_audit_planning_action_result");
    e.HasIndex(x => new { x.FirmId, x.EngagementId, x.CreatedAt }).HasDatabaseName("ix_audit_planning_engagement_history");
    e.HasOne<Engagement>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId }).HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
