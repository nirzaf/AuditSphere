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
  private static void ConfigureSecurity(ModelBuilder b)
  {
    b.Entity<AppUser>().HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_users_firm_id_id");
    b.Entity<AppUser>().HasIndex(x => new { x.TenantId, x.Subject }).IsUnique()
      .HasDatabaseName("ux_users_tenant_subject");
    b.Entity<AppUser>().ToTable("users", t =>
    {
      t.HasCheckConstraint("ck_users_kind", "user_kind IN ('Staff','Client')");
      t.HasCheckConstraint("ck_users_session",
        "session_epoch >= 1 AND length(subject) > 0 AND length(tenant_id) > 0 AND length(email) > 0");
    });
    b.Entity<Engagement>().ToTable("engagements", t =>
      t.HasCheckConstraint("ck_engagement_generation", "generation >= 1"));
    b.Entity<Engagement>()
      .HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PracticeClientId })
      .HasPrincipalKey(c => new { c.FirmId, c.Id })
      .OnDelete(DeleteBehavior.Restrict);
    b.Entity<RoleGrant>().HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_role_grants_firm_id_id");
    b.Entity<RoleGrant>().HasIndex(x => new { x.FirmId, x.UserId, x.Role, x.ClientId, x.EngagementId })
      .IsUnique().HasFilter("revoked_at IS NULL").HasDatabaseName("ux_active_role_grant_identity")
      .AreNullsDistinct(false);
    b.Entity<RoleGrant>().HasOne<AppUser>().WithMany()
      .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    b.Entity<RoleGrant>().HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<RoleGrant>().HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<RoleGrant>().ToTable("role_grants", t =>
      t.HasCheckConstraint("ck_role_grant_scope",
        "length(role) > 0 AND (engagement_id IS NULL OR client_id IS NOT NULL)"));
    var roleEvidence = b.Entity<RoleGrantChangeEvidence>();
    roleEvidence.Property(x => x.Action).HasMaxLength(20);
    roleEvidence.Property(x => x.PriorRole).HasMaxLength(100);
    roleEvidence.Property(x => x.NewRole).HasMaxLength(100);
    roleEvidence.Property(x => x.Source).HasMaxLength(40);
    roleEvidence.HasIndex(x => new { x.FirmId, x.TargetUserId, x.CreatedAt });
    roleEvidence.ToTable("role_grant_change_evidence", t => t.HasCheckConstraint("ck_role_grant_evidence_values",
      "action IN ('GRANTED','REVOKED','INVITATION_COPIED') AND length(trim(source)) > 0 AND length(trim(new_role)) > 0 AND length(trim(prior_role)) >= 0"));
    roleEvidence.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.TargetUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<EngagementHold>().HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<EngagementHold>().ToTable("engagement_holds", t =>
      t.HasCheckConstraint("ck_hold_release",
        "length(hold_kind) > 0 AND (NOT released OR released_at IS NOT NULL)"));
  }
}
