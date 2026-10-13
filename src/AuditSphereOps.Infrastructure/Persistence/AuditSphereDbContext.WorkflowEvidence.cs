using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureWorkflowTaskEvidence(ModelBuilder b)
  {
    var rule = b.Entity<WorkflowTaskEvidenceRule>();
    rule.HasIndex(x => new { x.FirmId, x.WorkflowTaskBindingId, x.RuleKey, x.Version }).IsUnique();
    rule.HasIndex(x => new { x.FirmId, x.WorkflowTaskBindingId, x.Kind, x.SourceId, x.Version }).IsUnique();
    rule.HasIndex(x => new { x.FirmId, x.ActorUserId, x.IdempotencyKey }).IsUnique();
    rule.HasIndex(x => new { x.FirmId, x.WorkflowTaskBindingId, x.TaskRevision }).IsUnique();
    rule.Property(x => x.DependencyType).HasMaxLength(40);
    rule.Property(x => x.Kind).HasMaxLength(40);
    rule.Property(x => x.State).HasMaxLength(20);
    rule.Property(x => x.RequestDigest).HasMaxLength(64);
    rule.Property(x => x.Reason).HasMaxLength(1000);
    rule.HasOne<WorkflowTaskBinding>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.WorkflowTaskBindingId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    rule.HasOne<PbcRequest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcRequestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    rule.HasOne<AuditProcedure>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.AuditProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    rule.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    rule.ToTable("workflow_task_evidence_rules", t =>
      t.HasCheckConstraint("ck_workflow_evidence_rule_values", """
        dependency_type = 'DOMAIN_EVIDENCE_SATISFIED' AND version >= 1 AND task_revision >= 2
        AND state IN ('REQUIRED','WITHDRAWN') AND length(trim(reason)) BETWEEN 1 AND 1000
        AND request_digest ~ '^[0-9a-f]{64}$'
        AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid
        AND rule_key <> '00000000-0000-0000-0000-000000000000'::uuid
        AND ((kind = 'CURRENT_MATERIALITY_APPROVED' AND source_id = '00000000-0000-0000-0000-000000000000'::uuid AND pbc_request_id IS NULL AND audit_procedure_id IS NULL)
          OR (kind = 'PBC_ACCEPTED_OR_CLOSED' AND pbc_request_id IS NOT NULL AND source_id = pbc_request_id AND audit_procedure_id IS NULL)
          OR (kind = 'CURRENT_PROCEDURE_REVIEWED' AND audit_procedure_id IS NOT NULL AND source_id = audit_procedure_id AND pbc_request_id IS NULL))
        """));
  }
}
