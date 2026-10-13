using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureWorkflowDependencyRevisions(ModelBuilder b)
  {
    var row = b.Entity<WorkflowTaskDependencyRevision>();
    row.HasIndex(x => new { x.FirmId, x.SuccessorBindingId, x.PredecessorBindingId, x.Version }).IsUnique();
    row.HasIndex(x => new { x.FirmId, x.SuccessorBindingId, x.TaskRevision }).IsUnique();
    row.HasIndex(x => new { x.FirmId, x.ActorUserId, x.IdempotencyKey }).IsUnique();
    row.Property(x => x.Reason).HasMaxLength(1000);
    row.Property(x => x.RequestDigest).HasMaxLength(64);
    row.HasOne<WorkflowTaskBinding>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PredecessorBindingId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    row.HasOne<WorkflowTaskBinding>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SuccessorBindingId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    row.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    row.ToTable("workflow_task_dependency_revisions", t => t.HasCheckConstraint("ck_workflow_dependency_revision_values",
      "predecessor_binding_id <> successor_binding_id AND version >= 1 AND task_revision >= 2 AND predecessor_revision >= 1" +
      " AND length(trim(reason)) BETWEEN 1 AND 1000 AND request_digest ~ '^[0-9a-f]{64}$'" +
      " AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid"));
  }
}
