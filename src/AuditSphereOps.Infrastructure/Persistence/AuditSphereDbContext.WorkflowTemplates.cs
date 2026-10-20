using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Engagements;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureWorkflowTaskPlanReceipts(ModelBuilder b)
  {
    var receipt = b.Entity<WorkflowTaskPlanReceipt>();
    receipt.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_workflow_task_plan_receipts_firm_id_id");
    receipt.HasIndex(x => new { x.FirmId, x.EngagementId, x.TemplateRevisionId }).IsUnique()
      .HasDatabaseName("UX_workflow_task_plan_receipts_engagement_revision");
    receipt.HasIndex(x => new { x.FirmId, x.EngagementId, x.IdempotencyKey }).IsUnique()
      .HasDatabaseName("UX_workflow_task_plan_receipts_idempotency");
    receipt.Property(x => x.RequestDigest).HasMaxLength(64);
    receipt.ToTable("workflow_task_plan_receipts", t => t.HasCheckConstraint("ck_workflow_task_plan_receipt_values",
      "template_version >= 1 AND engagement_generation >= 1 AND (milestone_plan_id IS NULL) = (milestone_plan_revision IS NULL) AND (milestone_plan_id IS NULL) = (schedule_basis_date IS NULL) AND (milestone_plan_revision IS NULL OR milestone_plan_revision >= 1) AND idempotency_key <> '00000000-0000-0000-0000-000000000000' AND request_digest ~ '^[0-9a-f]{64}$' AND task_count BETWEEN 1 AND 1000"));
    receipt.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    receipt.HasOne<WorkflowTemplateRevision>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.TemplateId, x.TemplateRevisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.TemplateId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    receipt.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }

  private static void ConfigureWorkflowTemplates(ModelBuilder b)
  {
    b.Entity<WorkflowTemplate>().HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_workflow_templates_firm_id_id");
    b.Entity<WorkflowTemplate>().HasIndex(x => new { x.FirmId, x.CreatedAt });
    b.Entity<WorkflowTemplate>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTemplate>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ArchivedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTemplate>().ToTable("workflow_templates", t =>
      t.HasCheckConstraint("ck_workflow_template_archive", "(archived_at IS NULL AND archived_by_user_id IS NULL) OR (archived_at IS NOT NULL AND archived_by_user_id IS NOT NULL)"));

    b.Entity<WorkflowTemplateRevision>().HasAlternateKey(x => new { x.FirmId, x.TemplateId, x.Id })
      .HasName("AK_workflow_template_revisions_scope_id");
    b.Entity<WorkflowTemplateRevision>().HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_workflow_template_revisions_firm_id_id");
    b.Entity<WorkflowTemplateRevision>().HasIndex(x => new { x.FirmId, x.TemplateId, x.Version }).IsUnique();
    b.Entity<WorkflowTemplateRevision>().HasIndex(x => new { x.FirmId, x.TemplateId, x.Status });
    b.Entity<WorkflowTemplateRevision>().HasIndex(x => new { x.FirmId, x.TemplateId }, "UX_workflow_template_one_open_revision")
      .IsUnique().HasFilter("status IN ('DRAFT','REVIEWED')");
    b.Entity<WorkflowTemplateRevision>().HasIndex(x => new { x.FirmId, x.TemplateId }, "UX_workflow_template_one_published_revision")
      .IsUnique().HasFilter("status = 'PUBLISHED'");
    b.Entity<WorkflowTemplateRevision>().Property(x => x.Name).HasMaxLength(160);
    b.Entity<WorkflowTemplateRevision>().Property(x => x.ServiceRoute).HasMaxLength(50);
    b.Entity<WorkflowTemplateRevision>().Property(x => x.IndustryKey).HasMaxLength(80);
    b.Entity<WorkflowTemplateRevision>().Property(x => x.Status).HasMaxLength(24);
    b.Entity<WorkflowTemplateRevision>().Property(x => x.ContentDigest).HasMaxLength(64);
    b.Entity<WorkflowTemplateRevision>().HasOne<WorkflowTemplate>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.TemplateId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTemplateRevision>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId }).HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTemplateRevision>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.LastEditedByUserId }).HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTemplateRevision>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId }).HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTemplateRevision>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PublishedByUserId }).HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTemplateRevision>().ToTable("workflow_template_revisions", t =>
    {
      t.HasCheckConstraint("ck_workflow_template_revision_values",
        "version >= 1 AND revision >= 1 AND status IN ('DRAFT','REVIEWED','PUBLISHED','SUPERSEDED') AND length(trim(name)) BETWEEN 2 AND 160 AND length(trim(service_route)) BETWEEN 1 AND 50 AND content_digest ~ '^[0-9a-f]{64}$' AND ((reviewed_by_user_id IS NULL AND reviewed_at IS NULL) OR (reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL)) AND ((published_by_user_id IS NULL AND published_at IS NULL) OR (published_by_user_id IS NOT NULL AND published_at IS NOT NULL)) AND (status NOT IN ('REVIEWED','PUBLISHED','SUPERSEDED') OR (reviewed_at IS NOT NULL AND reviewed_by_user_id <> created_by_user_id AND reviewed_by_user_id <> last_edited_by_user_id)) AND (status NOT IN ('PUBLISHED','SUPERSEDED') OR published_at IS NOT NULL)");
    });

    b.Entity<WorkflowTemplateStep>().HasAlternateKey(x => new { x.FirmId, x.TemplateRevisionId, x.Id })
      .HasName("AK_workflow_template_steps_revision_id");
    b.Entity<WorkflowTemplateStep>().HasIndex(x => new { x.FirmId, x.TemplateRevisionId, x.StableKey }).IsUnique();
    b.Entity<WorkflowTemplateStep>().HasIndex(x => new { x.FirmId, x.TemplateRevisionId, x.SortOrder });
    b.Entity<WorkflowTemplateStep>().Property(x => x.StableKey).HasMaxLength(80);
    b.Entity<WorkflowTemplateStep>().Property(x => x.Phase).HasMaxLength(40);
    b.Entity<WorkflowTemplateStep>().Property(x => x.TaskGroupName).HasMaxLength(120);
    b.Entity<WorkflowTemplateStep>().Property(x => x.Title).HasMaxLength(200);
    b.Entity<WorkflowTemplateStep>().Property(x => x.Instructions).HasMaxLength(4000);
    b.Entity<WorkflowTemplateStep>().Property(x => x.SuggestedStaffingLevel).HasMaxLength(40);
    b.Entity<WorkflowTemplateStep>().Property(x => x.PbcRequestReference).HasMaxLength(120);
    b.Entity<WorkflowTemplateStep>().Property(x => x.PbcObjective).HasMaxLength(2000);
    b.Entity<WorkflowTemplateStep>().Property(x => x.PbcEntityScope).HasMaxLength(500);
    b.Entity<WorkflowTemplateStep>().Property(x => x.PbcArea).HasMaxLength(200);
    b.Entity<WorkflowTemplateStep>().Property(x => x.PbcRequestedFormat).HasMaxLength(200);
    b.Entity<WorkflowTemplateStep>().Property(x => x.PbcControlTotals).HasMaxLength(2000);
    b.Entity<WorkflowTemplateStep>().Property(x => x.PbcConfidentiality).HasMaxLength(100);
    b.Entity<WorkflowTemplateStep>().Property(x => x.PbcAcceptanceCriteria).HasMaxLength(4000);
    b.Entity<WorkflowTemplateStep>().HasOne<WorkflowTemplateRevision>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.TemplateRevisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTemplateStep>().ToTable("workflow_template_steps", t =>
    {
      t.HasCheckConstraint("ck_workflow_template_step_values",
        "stable_key ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$' AND phase IN ('CLIENT_ONBOARDING','AUDIT_PLANNING','AUDIT_FIELDWORK','MANAGER_REVIEW','PARTNER_SIGNOFF','REPORT_DELIVERY','ARCHIVE_CLOSE') AND length(trim(title)) BETWEEN 1 AND 200 AND length(instructions) <= 4000 AND (suggested_staffing_level IS NULL OR suggested_staffing_level IN ('ENGAGEMENT_PARTNER','AUDIT_MANAGER','SENIOR_AUDITOR','STAFF_ASSOCIATE')) AND (estimated_effort_minutes IS NULL OR estimated_effort_minutes BETWEEN 1 AND 1000000) AND (due_date_offset_days IS NULL OR due_date_offset_days BETWEEN -3650 AND 3650) AND (pbc_request_reference IS NULL OR length(trim(pbc_request_reference)) BETWEEN 1 AND 120) AND (task_group_name IS NULL OR length(trim(task_group_name)) BETWEEN 1 AND 120) AND ((pbc_objective IS NULL AND pbc_entity_scope IS NULL AND pbc_area IS NULL AND pbc_requested_format IS NULL AND pbc_control_totals IS NULL AND pbc_confidentiality IS NULL AND pbc_acceptance_criteria IS NULL) OR (length(trim(pbc_objective)) BETWEEN 1 AND 2000 AND length(trim(pbc_entity_scope)) BETWEEN 1 AND 500 AND length(trim(pbc_area)) BETWEEN 1 AND 200 AND length(trim(pbc_requested_format)) BETWEEN 1 AND 200 AND (pbc_control_totals IS NULL OR length(trim(pbc_control_totals)) BETWEEN 1 AND 2000) AND length(trim(pbc_confidentiality)) BETWEEN 1 AND 100 AND length(trim(pbc_acceptance_criteria)) BETWEEN 1 AND 4000)) AND sort_order BETWEEN 1 AND 100000");
    });

    b.Entity<WorkflowTemplateDependency>().HasAlternateKey(x => new { x.FirmId, x.TemplateRevisionId, x.Id })
      .HasName("AK_workflow_template_dependencies_revision_id");
    b.Entity<WorkflowTemplateDependency>().HasIndex(x => new { x.FirmId, x.TemplateRevisionId, x.PredecessorStepId, x.SuccessorStepId }).IsUnique();
    b.Entity<WorkflowTemplateDependency>().HasOne<WorkflowTemplateRevision>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.TemplateRevisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTemplateDependency>().HasOne<WorkflowTemplateStep>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.TemplateRevisionId, x.PredecessorStepId })
      .HasPrincipalKey(x => new { x.FirmId, x.TemplateRevisionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTemplateDependency>().HasOne<WorkflowTemplateStep>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.TemplateRevisionId, x.SuccessorStepId })
      .HasPrincipalKey(x => new { x.FirmId, x.TemplateRevisionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTemplateDependency>().ToTable("workflow_template_dependencies", t =>
      t.HasCheckConstraint("ck_workflow_template_dependency_values", "predecessor_step_id <> successor_step_id AND dependency_type = 'TASK_COMPLETED'"));

    b.Entity<WorkflowTemplateDecision>().HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_workflow_template_decisions_firm_id_id");
    b.Entity<WorkflowTemplateDecision>().HasIndex(x => new { x.FirmId, x.TemplateId, x.OccurredAt });
    b.Entity<WorkflowTemplateDecision>().Property(x => x.Kind).HasMaxLength(24);
    b.Entity<WorkflowTemplateDecision>().Property(x => x.ContentDigest).HasMaxLength(64);
    b.Entity<WorkflowTemplateDecision>().Property(x => x.Rationale).HasMaxLength(1000);
    b.Entity<WorkflowTemplateDecision>().HasOne<WorkflowTemplate>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.TemplateId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTemplateDecision>().HasOne<WorkflowTemplateRevision>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.TemplateId, x.TemplateRevisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.TemplateId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTemplateDecision>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<WorkflowTemplateDecision>().ToTable("workflow_template_decisions", t =>
      t.HasCheckConstraint("ck_workflow_template_decision_values", "kind IN ('DRAFT_CREATED','DRAFT_EDITED','REVIEWED','PUBLISHED','SUPERSEDED','ARCHIVED') AND content_digest ~ '^[0-9a-f]{64}$' AND length(trim(rationale)) BETWEEN 1 AND 1000"));
  }
}
