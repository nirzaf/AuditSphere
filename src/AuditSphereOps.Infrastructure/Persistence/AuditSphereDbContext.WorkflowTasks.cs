using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureWorkflowTasks(ModelBuilder b)
  {
    b.Entity<WorkflowTaskBinding>().HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_workflow_task_bindings_firm_id_id");
    b.Entity<WorkflowTaskBinding>().HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_workflow_task_bindings_scope_id");
    b.Entity<WorkflowTaskBinding>().HasIndex(x => new { x.FirmId, x.WorkTaskId }).IsUnique();
    b.Entity<WorkflowTaskBinding>().HasIndex(x => new { x.FirmId, x.PbcRequestId })
      .IsUnique().HasFilter("pbc_request_id IS NOT NULL")
      .HasDatabaseName("UX_workflow_task_bindings_pbc_request");
    b.Entity<WorkflowTaskBinding>().HasIndex(x => new { x.FirmId, x.EngagementId, x.SourceKey, x.Generation })
      .IsUnique().HasDatabaseName("UX_workflow_task_bindings_source_generation");
    b.Entity<WorkflowTaskBinding>().HasIndex(x => new { x.FirmId, x.EngagementId, x.Phase, x.Ordinal });
    b.Entity<WorkflowTaskBinding>().HasIndex(x => new { x.FirmId, x.AssigneeUserIdSnapshot, x.Phase });
    b.Entity<WorkflowTaskBinding>().Property(x => x.Phase).HasMaxLength(40);
    b.Entity<WorkflowTaskBinding>().Property(x => x.SourceKey).HasMaxLength(160);
    b.Entity<WorkflowTaskBinding>().Property(x => x.CreationRequestDigest).HasMaxLength(64);
    b.Entity<WorkflowTaskBinding>().Property(x => x.DueDatePolicy).HasMaxLength(32);
    b.Entity<WorkflowTaskBinding>().Property(x => x.AssignedRole).HasMaxLength(40);
    b.Entity<WorkflowTaskBinding>().Property(x => x.AssignmentSource).HasMaxLength(40);
    b.Entity<WorkflowTaskBinding>().Property(x => x.CancellationReason).HasMaxLength(1000);
    b.Entity<WorkflowTaskBinding>().Property(x => x.SupersessionReason).HasMaxLength(1000);
    b.Entity<WorkflowTaskBinding>().ToTable("workflow_task_bindings", t =>
    {
      t.HasCheckConstraint("ck_workflow_task_binding_phase",
        "phase IN ('CLIENT_ONBOARDING','AUDIT_PLANNING','AUDIT_FIELDWORK','MANAGER_REVIEW','PARTNER_SIGNOFF','REPORT_DELIVERY','ARCHIVE_CLOSE')");
      t.HasCheckConstraint("ck_workflow_task_binding_values",
        "ordinal BETWEEN 1 AND 100000 AND generation >= 1 AND revision >= 1 AND (planned_effort_minutes IS NULL OR planned_effort_minutes BETWEEN 1 AND 1000000) AND due_date_policy IN ('MANUAL','FIXED','TEMPLATE_OFFSET') AND assignment_source IN ('MANUAL','TEMPLATE','MANUAL_REASSIGNMENT','AUTOMATION') AND (assigned_role IS NULL OR assigned_role IN ('ENGAGEMENT_PARTNER','AUDIT_MANAGER','SENIOR_AUDITOR','STAFF_ASSOCIATE')) AND length(source_key) BETWEEN 1 AND 160 AND creation_request_digest ~ '^[0-9a-f]{64}$' AND ((template_revision_id IS NULL AND template_step_id IS NULL) OR (template_revision_id IS NOT NULL AND template_step_id IS NOT NULL)) AND (cancellation_reason IS NULL OR length(trim(cancellation_reason)) > 0) AND (superseded_at IS NULL OR (supersession_reason IS NOT NULL AND length(trim(supersession_reason)) > 0))");
    });
    b.Entity<WorkflowTaskBinding>().HasOne<WorkTask>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.WorkTaskId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTaskBinding>().HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTaskBinding>().HasOne<PbcRequest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcRequestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTaskBinding>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTaskBinding>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.AssigneeUserIdSnapshot })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTaskBinding>().HasOne<WorkflowTemplateStep>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.TemplateRevisionId, x.TemplateStepId })
      .HasPrincipalKey(x => new { x.FirmId, x.TemplateRevisionId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var dependency = b.Entity<WorkflowTaskDependency>();
    dependency.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_workflow_task_dependencies_firm_id_id");
    dependency.HasIndex(x => new { x.FirmId, x.EngagementId, x.PredecessorBindingId, x.SuccessorBindingId })
      .IsUnique().HasDatabaseName("UX_workflow_task_dependencies_edge");
    dependency.HasIndex(x => new { x.FirmId, x.EngagementId, x.SuccessorBindingId });
    dependency.Property(x => x.DependencyType).HasMaxLength(40);
    dependency.HasOne<WorkflowTaskBinding>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PredecessorBindingId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    dependency.HasOne<WorkflowTaskBinding>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SuccessorBindingId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    dependency.HasOne<WorkflowTemplateDependency>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.TemplateRevisionId, x.TemplateDependencyId })
      .HasPrincipalKey(x => new { x.FirmId, x.TemplateRevisionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    dependency.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    dependency.ToTable("workflow_task_dependencies", t =>
      t.HasCheckConstraint("ck_workflow_task_dependency_values",
        "predecessor_binding_id <> successor_binding_id AND dependency_type = 'TASK_COMPLETED'"));
  }
}
