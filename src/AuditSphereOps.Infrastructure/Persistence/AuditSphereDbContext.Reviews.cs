using Microsoft.EntityFrameworkCore;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Microsoft365;

namespace AuditSphereOps.Infrastructure.Persistence;

// DbContext: one owner, snake_case, composite (firm,client,engagement) FK discipline (§§27, 42).
// Money decimal(19,6); IDs uuid v7-compatible; immutable TB rows have no update path.
public sealed partial class AuditSphereDbContext
{
  private static void ConfigureReviews(ModelBuilder b)
  {
    var approval = b.Entity<Approval>();
    approval.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_approvals_firm_id_id");
    approval.Property(x => x.TargetKind).HasMaxLength(50);
    approval.Property(x => x.ManifestDigest).HasMaxLength(64);
    approval.Property(x => x.Decision).HasMaxLength(16);
    approval.HasIndex(x => new
      {
        x.FirmId, x.TargetKind, x.TargetId, x.TargetRevision, x.InputGeneration,
        x.PolicyGeneration, x.ManifestDigest, x.DecidedByUserId
      }).IsUnique().HasDatabaseName("ux_approval_identity");
    approval.ToTable("approvals", t => t.HasCheckConstraint("ck_approval_values",
      "length(target_kind) > 0 AND target_revision >= 1 AND input_generation >= 1 AND policy_generation >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND decision IN ('APPROVED','REJECTED')"));
    approval.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    approval.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    approval.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.DecidedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var applicability = b.Entity<ApprovalApplicability>();
    applicability.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_approval_applicabilities_firm_id_id");
    applicability.HasIndex(x => new { x.FirmId, x.ApprovalId })
      .IsUnique().HasDatabaseName("ux_approval_applicability_approval");
    applicability.Property(x => x.Status).HasMaxLength(16);
    applicability.Property(x => x.Reason).HasMaxLength(500);
    applicability.ToTable("approval_applicabilities", t => t.HasCheckConstraint("ck_approval_applicability_values",
      "status IN ('CURRENT','STALE','REJECTED') AND length(reason) > 0 AND current_target_revision >= 1 AND current_input_generation >= 1 AND current_policy_generation >= 1"));
    applicability.HasOne<Approval>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovalId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
  }
}
