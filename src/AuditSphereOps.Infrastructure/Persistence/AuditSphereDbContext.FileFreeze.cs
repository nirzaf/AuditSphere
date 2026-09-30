using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Records;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

// Scheduled file freeze, amendments, refused writes and document locks.
public sealed partial class AuditSphereDbContext
{
  private static void ConfigureFileFreeze(ModelBuilder b)
  {
    b.Entity<EngagementFileFreeze>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.EngagementId }).IsUnique();
      e.HasIndex(x => new { x.FirmId, x.State, x.DueAt });
      e.HasOne<Engagement>().WithMany().HasForeignKey(x => x.EngagementId).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<AuditDeliverable>().WithMany().HasForeignKey(x => x.ReportDeliverableId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("engagement_file_freezes", t => t.HasCheckConstraint("ck_engagement_file_freeze_values",
        "state IN ('SCHEDULED','FROZEN','AMENDMENT_OPEN') AND external_read_only IN ('NOT_REQUESTED','REQUESTED','OBSERVED','BLOCKED_EXTERNAL') AND due_at = report_signed_at + interval '60 days' AND revision >= 1 AND ((state = 'SCHEDULED') = (frozen_at IS NULL))"));
    });
    b.Entity<FileFreezeAmendment>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.FreezeId, x.RequestedAt });
      e.HasOne<EngagementFileFreeze>().WithMany().HasForeignKey(x => x.FreezeId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("file_freeze_amendments", t => t.HasCheckConstraint("ck_file_freeze_amendment_values",
        "length(reason) > 0 AND (approved_by_user_id IS NULL OR approved_by_user_id <> requested_by_user_id) AND ((opened_at IS NULL) = (approved_by_user_id IS NULL)) AND (closed_at IS NULL OR opened_at IS NOT NULL)"));
    });
    b.Entity<FrozenAccessAttempt>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.EngagementId, x.AttemptedAt });
      e.ToTable("frozen_access_attempts", t => t.HasCheckConstraint("ck_frozen_access_attempt_values", "length(action) > 0"));
    });
    b.Entity<DocumentLock>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.EngagementId, x.DocumentKey }).IsUnique().HasFilter("released_at IS NULL");
      e.ToTable("document_locks", t => t.HasCheckConstraint("ck_document_lock_values",
        "length(document_key) > 0 AND ((released_at IS NULL) = (released_by_user_id IS NULL))"));
    });
  }
}
