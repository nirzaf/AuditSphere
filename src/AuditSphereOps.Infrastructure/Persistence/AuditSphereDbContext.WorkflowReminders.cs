using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureWorkflowReminders(ModelBuilder b)
  {
    var rule = b.Entity<WorkflowReminderRule>();
    rule.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_workflow_reminder_rules_firm_id_id");
    rule.HasIndex(x => new { x.FirmId, x.CreatedAt, x.Id });
    rule.HasIndex(x => new { x.FirmId, x.LastEvaluatedAt, x.Id });
    rule.Property(x => x.SuspensionReason).HasMaxLength(1000);
    rule.Property(x => x.LastEvaluationCode).HasMaxLength(100);
    rule.Property(x => x.ScanAfterDueDate).HasMaxLength(10);
    rule.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    rule.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.SuspendedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    rule.HasOne<WorkflowReminderRuleRevision>().WithMany()
      .HasForeignKey(nameof(WorkflowReminderRule.FirmId), nameof(WorkflowReminderRule.Id),
        nameof(WorkflowReminderRule.CurrentPublishedRevisionId))
      .HasPrincipalKey(x => new { x.FirmId, x.RuleId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    rule.HasOne<WorkflowReminderRuleRevision>().WithMany()
      .HasForeignKey(nameof(WorkflowReminderRule.FirmId), nameof(WorkflowReminderRule.Id),
        nameof(WorkflowReminderRule.ScanRevisionId))
      .HasPrincipalKey(x => new { x.FirmId, x.RuleId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    rule.ToTable("workflow_reminder_rules", t =>
      t.HasCheckConstraint("ck_workflow_reminder_rule_values",
        "revision >= 1 AND ((suspended_at IS NULL AND suspended_by_user_id IS NULL AND suspension_reason IS NULL) OR (suspended_at IS NOT NULL AND suspended_by_user_id IS NOT NULL AND suspension_reason IS NOT NULL AND length(trim(suspension_reason)) BETWEEN 1 AND 1000)) AND ((scan_after_due_date IS NULL) = (scan_after_source_id IS NULL)) AND (scan_after_due_date IS NULL OR (scan_after_due_date ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' AND scan_revision_id IS NOT NULL))"));

    var revision = b.Entity<WorkflowReminderRuleRevision>();
    revision.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_workflow_reminder_rule_revisions_firm_id_id");
    revision.HasAlternateKey(x => new { x.FirmId, x.RuleId, x.Id })
      .HasName("AK_workflow_reminder_rule_revisions_scope_id");
    revision.HasAlternateKey(x => new { x.FirmId, x.CreationRequestKey })
      .HasName("AK_workflow_reminder_rule_revisions_firm_creation_request");
    revision.HasIndex(x => new { x.FirmId, x.RuleId, x.Version }).IsUnique()
      .HasDatabaseName("UX_workflow_reminder_rule_revisions_version");
    revision.HasIndex(x => new { x.FirmId, x.RuleId, x.Status });
    revision.HasIndex(x => new { x.FirmId, x.RuleId }, "UX_workflow_reminder_one_open_revision")
      .IsUnique().HasFilter("status IN ('DRAFT','REVIEWED')");
    revision.HasIndex(x => new { x.FirmId, x.RuleId }, "UX_workflow_reminder_one_published_revision")
      .IsUnique().HasFilter("status = 'PUBLISHED'");
    revision.Property(x => x.Name).HasMaxLength(160);
    revision.Property(x => x.SourceKind).HasMaxLength(24);
    revision.Property(x => x.RecipientKind).HasMaxLength(32);
    revision.Property(x => x.TimeZoneId).HasMaxLength(100);
    revision.Property(x => x.QuietHoursStart).HasMaxLength(5);
    revision.Property(x => x.QuietHoursEnd).HasMaxLength(5);
    revision.Property(x => x.Status).HasMaxLength(24);
    revision.Property(x => x.CreationRequestDigest).HasMaxLength(64);
    revision.Property(x => x.ContentDigest).HasMaxLength(64);
    revision.HasOne<WorkflowReminderRule>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.RuleId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    revision.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    revision.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.LastEditedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    revision.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    revision.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PublishedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    revision.ToTable("workflow_reminder_rule_revisions", t =>
    {
      t.HasCheckConstraint("ck_workflow_reminder_rule_revision_values",
        "version >= 1 AND revision >= 1 AND creation_request_key <> '00000000-0000-0000-0000-000000000000'::uuid AND creation_request_digest ~ '^[0-9a-f]{64}$' AND status IN ('DRAFT','REVIEWED','PUBLISHED','SUPERSEDED') AND length(trim(name)) BETWEEN 2 AND 160 AND source_kind IN ('TASK_DUE','PBC_DUE') AND recipient_kind IN ('RESPONSIBLE_STAFF','ENGAGEMENT_REVIEWER') AND offset_days BETWEEN -30 AND 365 AND repeat_every_days BETWEEN 0 AND 90 AND length(trim(time_zone_id)) BETWEEN 1 AND 100 AND quiet_hours_start ~ '^(?:[01][0-9]|2[0-3]):[0-5][0-9]$' AND quiet_hours_end ~ '^(?:[01][0-9]|2[0-3]):[0-5][0-9]$' AND content_digest ~ '^[0-9a-f]{64}$' AND ((status = 'DRAFT' AND reviewed_by_user_id IS NULL AND reviewed_at IS NULL AND published_by_user_id IS NULL AND published_at IS NULL AND published_session_epoch IS NULL) OR (status = 'REVIEWED' AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL AND reviewed_by_user_id <> created_by_user_id AND reviewed_by_user_id <> last_edited_by_user_id AND published_by_user_id IS NULL AND published_at IS NULL AND published_session_epoch IS NULL) OR (status IN ('PUBLISHED','SUPERSEDED') AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL AND reviewed_by_user_id <> created_by_user_id AND reviewed_by_user_id <> last_edited_by_user_id AND published_by_user_id IS NOT NULL AND published_at IS NOT NULL AND published_session_epoch >= 1))");
      t.HasCheckConstraint("ck_workflow_reminder_rule_revision_schedule_window_bound",
        "max_scheduled_window_count IS NULL OR max_scheduled_window_count BETWEEN 1 AND 100");
    });

    var decision = b.Entity<WorkflowReminderRuleDecision>();
    decision.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_workflow_reminder_rule_decisions_firm_id_id");
    decision.HasIndex(x => new { x.FirmId, x.RuleId, x.OccurredAt, x.Id });
    decision.Property(x => x.Kind).HasMaxLength(24);
    decision.Property(x => x.ContentDigest).HasMaxLength(64);
    decision.Property(x => x.Rationale).HasMaxLength(1000);
    decision.HasOne<WorkflowReminderRule>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.RuleId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    decision.HasOne<WorkflowReminderRuleRevision>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.RuleId, x.RuleRevisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.RuleId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    decision.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    decision.ToTable("workflow_reminder_rule_decisions", t =>
      t.HasCheckConstraint("ck_workflow_reminder_rule_decision_values",
        "kind IN ('DRAFT_CREATED','DRAFT_EDITED','REVIEWED','PUBLISHED','SUPERSEDED','SUSPENDED','RESUMED') AND content_digest ~ '^[0-9a-f]{64}$' AND length(trim(rationale)) BETWEEN 1 AND 1000"));

    b.Entity<DurableOperation>().HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_durable_operations_firm_id_id");
  }
}
