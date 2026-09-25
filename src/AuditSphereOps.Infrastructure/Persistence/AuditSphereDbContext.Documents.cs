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
  private static void ConfigureDocuments(ModelBuilder b)
  {
    var binding = b.Entity<RepositoryBinding>();
    binding.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_repository_bindings_firm_id_id");
    binding.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_repository_bindings_scope_id");
    binding.Property(x => x.TenantId).HasMaxLength(2000);
    binding.Property(x => x.SiteId).HasMaxLength(2000);
    binding.Property(x => x.DriveId).HasMaxLength(2000);
    binding.Property(x => x.RootFolderId).HasMaxLength(2000);
    binding.Property(x => x.Classification).HasMaxLength(2000);
    binding.Property(x => x.DesiredAccess).HasMaxLength(2000);
    binding.Property(x => x.ObservedAccess).HasMaxLength(2000);
    binding.Property(x => x.CapabilityProfile).HasMaxLength(2000);
    binding.ToTable("repository_bindings", t => t.HasCheckConstraint("ck_repository_binding_values",
      "length(trim(tenant_id)) > 0 AND length(trim(site_id)) > 0 AND length(trim(drive_id)) > 0 AND length(trim(root_folder_id)) > 0 AND length(trim(classification)) > 0 AND length(trim(desired_access)) > 0 AND length(trim(observed_access)) > 0 AND length(trim(capability_profile)) > 0"));
    binding.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    binding.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var cursor = b.Entity<SyncCursor>();
    cursor.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_sync_cursors_firm_id_id");
    cursor.Property(x => x.Cursor).HasMaxLength(4000);
    cursor.HasIndex(x => new { x.FirmId, x.RepositoryBindingId })
      .IsUnique().HasDatabaseName("ux_sync_cursor_binding");
    cursor.ToTable("sync_cursors", t => t.HasCheckConstraint("ck_sync_cursor_values",
      "length(trim(cursor)) > 0 AND generation >= 1"));
    cursor.HasOne<RepositoryBinding>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.RepositoryBindingId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var capability = b.Entity<IntegrationCapability>();
    capability.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_integration_capabilities_firm_id_id");
    capability.Property(x => x.HealthStatus).HasMaxLength(50);
    capability.Property(x => x.TestedPermissions).HasMaxLength(4000);
    capability.HasIndex(x => new { x.FirmId, x.RepositoryBindingId })
      .IsUnique().HasDatabaseName("ux_integration_capability_binding");
    capability.ToTable("integration_capabilities", t => t.HasCheckConstraint("ck_integration_capability_values",
      "length(trim(health_status)) > 0 AND length(trim(tested_permissions)) > 0 AND ((tested_at IS NULL AND health_status IN ('UNKNOWN','BLOCKED')) OR tested_at IS NOT NULL)"));
    capability.HasOne<RepositoryBinding>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.RepositoryBindingId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var reference = b.Entity<DocumentReference>();
    reference.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_document_references_firm_id_id");
    reference.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_document_references_scope_id");
    reference.Property(x => x.Provider).HasMaxLength(50);
    reference.Property(x => x.DriveId).HasMaxLength(200);
    reference.Property(x => x.ItemId).HasMaxLength(200);
    reference.Property(x => x.Path).HasMaxLength(2000);
    reference.Property(x => x.Purpose).HasMaxLength(100);
    reference.ToTable("document_references", t => t.HasCheckConstraint("ck_document_reference_values",
      "length(trim(provider)) > 0 AND length(drive_id) > 0 AND length(item_id) > 0 AND length(path) > 0 AND length(purpose) > 0"));
    reference.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    reference.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    reference.HasOne<RepositoryBinding>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.RepositoryBindingId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var snapshot = b.Entity<DocumentSnapshot>();
    snapshot.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("ak_document_snapshots_scope_id");
    snapshot.Property(x => x.DriveId).HasMaxLength(200);
    snapshot.Property(x => x.ItemId).HasMaxLength(200);
    snapshot.Property(x => x.VersionId).HasMaxLength(200);
    snapshot.Property(x => x.Sha256Hex).HasMaxLength(64);
    snapshot.Property(x => x.CapturedBy).HasMaxLength(100);
    snapshot.HasIndex(x => new { x.FirmId, x.DocumentReferenceId, x.VersionId })
      .IsUnique().HasDatabaseName("ux_document_snapshot_firm_document_version");
    snapshot.ToTable("document_snapshots", t => t.HasCheckConstraint("ck_document_snapshot_values",
      "length(drive_id) > 0 AND length(item_id) > 0 AND length(version_id) > 0 AND sha256_hex ~ '^[0-9a-f]{64}$' AND byte_count >= 0 AND length(captured_by) > 0"));
    snapshot.HasOne<DocumentReference>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.DocumentReferenceId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
  }
}
