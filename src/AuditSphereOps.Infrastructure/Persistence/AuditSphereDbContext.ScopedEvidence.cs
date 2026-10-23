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
  public DbSet<ArchiveCopyTargetRevision> ArchiveCopyTargetRevisions => Set<ArchiveCopyTargetRevision>();
  public DbSet<ArchiveCopyFileReceipt> ArchiveCopyFileReceipts => Set<ArchiveCopyFileReceipt>();
  public DbSet<ArchiveCopyPermissionObservation> ArchiveCopyPermissionObservations => Set<ArchiveCopyPermissionObservation>();

  private static void ConfigureScopedEvidence(ModelBuilder b)
  {
    var archiveCopyTarget = b.Entity<ArchiveCopyTargetRevision>();
    archiveCopyTarget.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_archive_copy_target_revisions_firm_id_id");
    archiveCopyTarget.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id })
      .HasName("AK_archive_copy_target_revisions_firm_client_id_id");
    archiveCopyTarget.Property(x => x.TargetCode).HasMaxLength(50);
    archiveCopyTarget.Property(x => x.StaffGroupId).HasMaxLength(20);
    archiveCopyTarget.Property(x => x.TenantId).HasMaxLength(200);
    archiveCopyTarget.Property(x => x.SiteId).HasMaxLength(2000);
    archiveCopyTarget.Property(x => x.DriveId).HasMaxLength(2000);
    archiveCopyTarget.Property(x => x.LibraryRootItemId).HasMaxLength(2000);
    archiveCopyTarget.Property(x => x.ExpectedPermissionsJson).HasColumnType("text");
    archiveCopyTarget.Property(x => x.ExpectedPermissionsSha256).HasMaxLength(64);
    archiveCopyTarget.Property(x => x.AuthorizedStaffSha256).HasMaxLength(64);
    archiveCopyTarget.Property(x => x.ConfigurationSha256).HasMaxLength(64);
    archiveCopyTarget.HasIndex(x => new { x.FirmId, x.ClientId, x.TargetCode, x.Revision }).IsUnique()
      .HasDatabaseName("ux_archive_copy_target_client_revision");
    archiveCopyTarget.ToTable("archive_copy_target_revisions", t => t.HasCheckConstraint("ck_archive_copy_target_revision_values",
      "target_code = 'SECONDARY_ARCHIVE' AND revision >= 1 AND length(trim(tenant_id)) > 0 AND " +
      "length(trim(site_id)) > 0 AND length(trim(drive_id)) > 0 AND length(trim(library_root_item_id)) > 0 AND " +
      "length(trim(expected_permissions_json)) > 0 AND expected_permissions_sha256 ~ '^[0-9a-f]{64}$' AND " +
      "staff_group_id ~ '^[1-9][0-9]*$' AND " +
      "authorized_staff_sha256 ~ '^[0-9a-f]{64}$' AND " +
      "configuration_sha256 ~ '^[0-9a-f]{64}$'"));
    archiveCopyTarget.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    archiveCopyTarget.HasOne<ClientSharePointSite>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.ClientSharePointSiteId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    archiveCopyTarget.HasOne<Microsoft365ConnectionRevision>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ConnectionRevisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    archiveCopyTarget.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var archiveCopyFile = b.Entity<ArchiveCopyFileReceipt>();
    archiveCopyFile.Property(x => x.ArchiveManifestSha256).HasMaxLength(64);
    archiveCopyFile.Property(x => x.TargetConfigurationSha256).HasMaxLength(64);
    archiveCopyFile.Property(x => x.SourceKind).HasMaxLength(40);
    archiveCopyFile.Property(x => x.SourceKeySha256).HasMaxLength(64);
    archiveCopyFile.Property(x => x.SourceVersion).HasMaxLength(500);
    archiveCopyFile.Property(x => x.SourceSha256).HasMaxLength(64);
    archiveCopyFile.Property(x => x.RelativePath).HasMaxLength(2000);
    archiveCopyFile.Property(x => x.DestinationTenantId).HasMaxLength(200);
    archiveCopyFile.Property(x => x.DestinationSiteId).HasMaxLength(2000);
    archiveCopyFile.Property(x => x.DestinationDriveId).HasMaxLength(2000);
    archiveCopyFile.Property(x => x.DestinationLibraryRootItemId).HasMaxLength(2000);
    archiveCopyFile.Property(x => x.DestinationItemId).HasMaxLength(500);
    archiveCopyFile.Property(x => x.DestinationVersion).HasMaxLength(500);
    archiveCopyFile.Property(x => x.ReadBackSha256).HasMaxLength(64);
    archiveCopyFile.Property(x => x.VerificationOutcome).HasMaxLength(20);
    archiveCopyFile.HasIndex(x => new { x.FirmId, x.OperationId, x.AttemptToken, x.SourceKeySha256 }).IsUnique()
      .HasDatabaseName("ux_archive_copy_file_receipt_attempt_source");
    archiveCopyFile.HasIndex(x => new { x.FirmId, x.ArchiveId, x.ArchiveManifestVersion, x.TargetRevision })
      .HasDatabaseName("ix_archive_copy_file_receipt_archive");
    ScopeToEngagement(archiveCopyFile, nameof(ArchiveCopyFileReceipt.FirmId), nameof(ArchiveCopyFileReceipt.ClientId),
      nameof(ArchiveCopyFileReceipt.EngagementId));
    archiveCopyFile.ToTable("archive_copy_file_receipts", t => t.HasCheckConstraint("ck_archive_copy_file_receipt_values",
      "attempt_token >= 1 AND archive_manifest_version >= 1 AND archive_manifest_sha256 ~ '^[0-9a-f]{64}$' AND " +
      "target_revision >= 1 AND target_configuration_sha256 ~ '^[0-9a-f]{64}$' AND " +
      "source_kind IN ('CHECKPOINT_MANIFEST','RELEASED_ARTIFACT','REVIEWED_MANIFEST_ENTRY') AND " +
      "source_key_sha256 ~ '^[0-9a-f]{64}$' AND source_sha256 ~ '^[0-9a-f]{64}$' AND source_byte_count >= 0 AND " +
      "length(trim(source_version)) > 0 AND length(trim(relative_path)) > 0 AND " +
      "length(trim(destination_tenant_id)) > 0 AND length(trim(destination_site_id)) > 0 AND " +
      "length(trim(destination_drive_id)) > 0 AND length(trim(destination_library_root_item_id)) > 0 AND " +
      "length(trim(destination_item_id)) > 0 AND length(trim(destination_version)) > 0 AND " +
      "read_back_sha256 ~ '^[0-9a-f]{64}$' AND read_back_byte_count >= 0 AND " +
      "verification_outcome IN ('MATCH','MISMATCH') AND " +
      "((source_kind = 'REVIEWED_MANIFEST_ENTRY' AND manifest_entry_id IS NOT NULL) OR " +
      " (source_kind <> 'REVIEWED_MANIFEST_ENTRY' AND manifest_entry_id IS NULL)) AND " +
      "(source_kind <> 'CHECKPOINT_MANIFEST' OR source_id = checkpoint_id)"));
    archiveCopyFile.HasOne<Archive>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    archiveCopyFile.HasOne<ArchiveManifest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveManifestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    archiveCopyFile.HasOne<ArchiveCopyTargetRevision>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, TargetRevisionId = x.TargetRevisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, Id = x.Id }).OnDelete(DeleteBehavior.Restrict);
    archiveCopyFile.HasOne<Release>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReleaseId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    archiveCopyFile.HasOne<ReleaseCheckpoint>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.CheckpointId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    archiveCopyFile.HasOne<DurableOperation>().WithMany()
      .HasForeignKey(x => x.OperationId).HasPrincipalKey(x => x.Id).OnDelete(DeleteBehavior.Restrict);

    var archiveCopyPermission = b.Entity<ArchiveCopyPermissionObservation>();
    archiveCopyPermission.Property(x => x.ArchiveManifestSha256).HasMaxLength(64);
    archiveCopyPermission.Property(x => x.TargetConfigurationSha256).HasMaxLength(64);
    archiveCopyPermission.Property(x => x.AuthorizedStaffSha256).HasMaxLength(64);
    archiveCopyPermission.Property(x => x.ObservedStaffSha256).HasMaxLength(64);
    archiveCopyPermission.Property(x => x.StaffGroupId).HasMaxLength(20);
    archiveCopyPermission.Property(x => x.ObservedTenantId).HasMaxLength(200);
    archiveCopyPermission.Property(x => x.ObservedSiteId).HasMaxLength(2000);
    archiveCopyPermission.Property(x => x.ObservedDriveId).HasMaxLength(2000);
    archiveCopyPermission.Property(x => x.ObservedLibraryRootItemId).HasMaxLength(2000);
    archiveCopyPermission.Property(x => x.ExpectedPermissionsJson).HasColumnType("text");
    archiveCopyPermission.Property(x => x.ExpectedPermissionsSha256).HasMaxLength(64);
    archiveCopyPermission.Property(x => x.ObservedPermissionsJson).HasColumnType("text");
    archiveCopyPermission.Property(x => x.ObservedPermissionsSha256).HasMaxLength(64);
    archiveCopyPermission.Property(x => x.ComparisonOutcome).HasMaxLength(20);
    archiveCopyPermission.HasIndex(x => new { x.FirmId, x.OperationId, x.AttemptToken }).IsUnique()
      .HasDatabaseName("ux_archive_copy_permission_observation_attempt");
    archiveCopyPermission.HasIndex(x => new { x.FirmId, x.TargetRevisionId, x.ObservedAt })
      .HasDatabaseName("ix_archive_copy_permission_observation_target");
    ScopeToEngagement(archiveCopyPermission, nameof(ArchiveCopyPermissionObservation.FirmId),
      nameof(ArchiveCopyPermissionObservation.ClientId), nameof(ArchiveCopyPermissionObservation.EngagementId));
    archiveCopyPermission.ToTable("archive_copy_permission_observations", t => t.HasCheckConstraint("ck_archive_copy_permission_observation_values",
      "attempt_token >= 1 AND archive_manifest_version >= 1 AND archive_manifest_sha256 ~ '^[0-9a-f]{64}$' AND " +
      "target_revision >= 1 AND target_configuration_sha256 ~ '^[0-9a-f]{64}$' AND " +
      "authorized_staff_sha256 ~ '^[0-9a-f]{64}$' AND " +
      "observed_staff_sha256 ~ '^[0-9a-f]{64}$' AND " +
      "staff_group_id ~ '^[1-9][0-9]*$' AND " +
      "length(trim(expected_permissions_json)) > 0 AND expected_permissions_sha256 ~ '^[0-9a-f]{64}$' AND " +
      "length(trim(observed_permissions_json)) > 0 AND observed_permissions_sha256 ~ '^[0-9a-f]{64}$' AND " +
      "length(trim(observed_tenant_id)) > 0 AND length(trim(observed_site_id)) > 0 AND " +
      "length(trim(observed_drive_id)) > 0 AND length(trim(observed_library_root_item_id)) > 0 AND " +
      "comparison_outcome IN ('MATCH','MISMATCH')"));
    archiveCopyPermission.HasOne<ArchiveCopyTargetRevision>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, TargetRevisionId = x.TargetRevisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, Id = x.Id }).OnDelete(DeleteBehavior.Restrict);
    archiveCopyPermission.HasOne<ArchiveManifest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveManifestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    archiveCopyPermission.HasOne<Release>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReleaseId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    archiveCopyPermission.HasOne<DurableOperation>().WithMany()
      .HasForeignKey(x => x.OperationId).HasPrincipalKey(x => x.Id).OnDelete(DeleteBehavior.Restrict);

    var assignment = b.Entity<EngagementAssignment>();
    assignment.Property(x => x.Role).HasMaxLength(50);
    assignment.HasIndex(x => new { x.FirmId, x.EngagementId, x.UserId, x.Role })
      .HasDatabaseName("ix_engagement_assignments_scope");
    ScopeToEngagement(assignment, nameof(EngagementAssignment.FirmId), nameof(EngagementAssignment.ClientId),
      nameof(EngagementAssignment.EngagementId));
    assignment.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.UserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    assignment.ToTable("engagement_assignments", t => t.HasCheckConstraint("ck_engagement_assignment_values",
      "length(trim(role)) > 0 AND allocated_hours >= 0" +
      " AND ((start_date IS NULL OR end_date IS NULL) OR start_date <= end_date)"));

    var eqr = b.Entity<EqrCase>();
    eqr.Property(x => x.Status).HasMaxLength(30);
    eqr.HasIndex(x => new { x.FirmId, x.EngagementId }).IsUnique().HasDatabaseName("ux_eqr_case_engagement");
    ScopeToEngagement(eqr, nameof(EqrCase.FirmId), nameof(EqrCase.ClientId), nameof(EqrCase.EngagementId));
    eqr.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.EqrPartnerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    eqr.ToTable("eqr_cases", t => t.HasCheckConstraint("ck_eqr_case_values",
      "status IN ('PENDING','IN_PROGRESS','CONCURRED','CHANGES_REQUESTED')" +
      " AND ((status = 'CONCURRED' AND concurrence_date IS NOT NULL AND completed_at IS NOT NULL)" +
      "   OR status <> 'CONCURRED')"));

    var representation = b.Entity<WrittenRepresentation>();
    representation.Property(x => x.Code).HasMaxLength(20);
    representation.Property(x => x.Title).HasMaxLength(300);
    representation.HasIndex(x => new { x.FirmId, x.EngagementId, x.Code }).IsUnique()
      .HasDatabaseName("ux_written_representation_code");
    ScopeToEngagement(representation, nameof(WrittenRepresentation.FirmId), nameof(WrittenRepresentation.ClientId),
      nameof(WrittenRepresentation.EngagementId));
    representation.ToTable("written_representations", t => t.HasCheckConstraint("ck_written_representation_values",
      "length(trim(code)) > 0 AND length(trim(title)) > 0 AND length(trim(narrative)) > 0" +
      " AND ((obtained AND obtained_at IS NOT NULL) OR NOT obtained)"));

    var clearance = b.Entity<SpecialistClearance>();
    clearance.Property(x => x.Area).HasMaxLength(100);
    clearance.Property(x => x.Status).HasMaxLength(30);
    clearance.HasIndex(x => new { x.FirmId, x.PracticeClientId, x.Area }).HasDatabaseName("ix_clearance_client_area");
    clearance.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PracticeClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    clearance.ToTable("specialist_clearances", t => t.HasCheckConstraint("ck_specialist_clearance_values",
      "length(trim(area)) > 0 AND length(trim(specialist_name)) > 0" +
      " AND status IN ('PENDING','CLEARED','HOLD','CONDITIONS')" +
      " AND ((status = 'CLEARED' AND cleared_at IS NOT NULL) OR status <> 'CLEARED')" +
      " AND ((status = 'CONDITIONS' AND length(trim(conditions)) > 0) OR status <> 'CONDITIONS')"));

    var receipt = b.Entity<SourceReceipt>();
    receipt.Property(x => x.SourceType).HasMaxLength(30);
    receipt.Property(x => x.ReceiptToken).HasMaxLength(200);
    receipt.Property(x => x.Sha256Digest).HasMaxLength(64);
    receipt.Property(x => x.OriginalFileName).HasMaxLength(255);
    receipt.HasIndex(x => new { x.FirmId, x.EngagementId, x.ReceiptToken }).IsUnique()
      .HasDatabaseName("ux_source_receipt_scope_token");
    ScopeToEngagement(receipt, nameof(SourceReceipt.FirmId), nameof(SourceReceipt.ClientId),
      nameof(SourceReceipt.EngagementId));
    receipt.ToTable("source_receipts", t => t.HasCheckConstraint("ck_source_receipt_values",
      "source_type IN ('PBC_UPLOAD','DIRECT_FEED','CSV_IMPORT') AND length(trim(receipt_token)) > 0" +
      " AND sha256_digest ~ '^[0-9a-f]{64}$' AND byte_count > 0 AND length(trim(original_file_name)) > 0"));
    receipt.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.AcquiredByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var evidence = b.Entity<EvidenceLink>();
    evidence.Property(x => x.Purpose).HasMaxLength(500);
    evidence.Property(x => x.Assertion).HasMaxLength(100);
    evidence.HasIndex(x => new { x.FirmId, x.EngagementId, x.SourceReceiptId }).HasDatabaseName("ix_evidence_scope_receipt");
    ScopeToEngagement(evidence, nameof(EvidenceLink.FirmId), nameof(EvidenceLink.ClientId),
      nameof(EvidenceLink.EngagementId));
    evidence.HasOne<SourceReceipt>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SourceReceiptId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    // A workpaper evidence link must match an authorized snapshot scope (§27.2): the composite key
    // makes a cross-client link unrepresentable rather than merely discouraged.
    evidence.HasOne<Workpaper>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.WorkpaperId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    evidence.ToTable("evidence_links", t => t.HasCheckConstraint("ck_evidence_link_values",
      "length(trim(purpose)) > 0 AND length(trim(assertion)) > 0 AND length(trim(relevance_reliability_assessment)) > 0"));

    var reviewPoint = b.Entity<ReviewPoint>();
    reviewPoint.Property(x => x.TargetKind).HasMaxLength(50);
    reviewPoint.Property(x => x.Comment).HasMaxLength(20000);
    reviewPoint.HasIndex(x => new { x.FirmId, x.EngagementId, x.TargetId, x.Cleared })
      .HasDatabaseName("ix_review_points_scope_cleared");
    ScopeToEngagement(reviewPoint, nameof(ReviewPoint.FirmId), nameof(ReviewPoint.ClientId),
      nameof(ReviewPoint.EngagementId));
    reviewPoint.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.RaisedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    reviewPoint.ToTable("review_points", t => t.HasCheckConstraint("ck_review_point_values",
      "length(trim(target_kind)) > 0 AND target_revision >= 1 AND length(trim(comment)) > 0"));

    var profile = b.Entity<RecordsProfile>();
    profile.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_records_profiles_firm_id_id");
    profile.Property(x => x.ProfileCode).HasMaxLength(100);
    profile.Property(x => x.RecordClass).HasMaxLength(100);
    profile.Property(x => x.Jurisdiction).HasMaxLength(100);
    profile.Property(x => x.ServiceRoute).HasMaxLength(100);
    profile.Property(x => x.RetentionTrigger).HasMaxLength(200);
    profile.Property(x => x.ProtectionMode).HasMaxLength(100);
    profile.Property(x => x.LabelId).HasMaxLength(200);
    profile.Property(x => x.LegalHoldBehavior).HasMaxLength(200);
    profile.Property(x => x.AmendmentRoute).HasMaxLength(500);
    profile.Property(x => x.DispositionOwner).HasMaxLength(200);
    profile.Property(x => x.BackupRequirements).HasMaxLength(1000);
    profile.HasIndex(x => new { x.FirmId, x.ProfileCode, x.Version }).IsUnique()
      .HasDatabaseName("ux_records_profile_code_version");
    profile.ToTable("records_profiles", t => t.HasCheckConstraint("ck_records_profile_values",
      "version >= 1 AND length(trim(profile_code)) > 0 AND length(trim(record_class)) > 0" +
      " AND length(trim(jurisdiction)) > 0 AND length(trim(service_route)) > 0" +
      " AND length(trim(retention_trigger)) > 0 AND (retention_duration_days IS NULL OR retention_duration_days > 0)" +
      " AND length(trim(protection_mode)) > 0 AND length(trim(label_id)) > 0" +
      " AND length(trim(legal_hold_behavior)) > 0 AND length(trim(amendment_route)) > 0" +
      " AND length(trim(disposition_owner)) > 0 AND length(trim(backup_requirements)) > 0" +
      " AND ((approved = false AND approved_at IS NULL AND approved_by_user_id IS NULL)" +
      " OR (approved = true AND approved_at IS NOT NULL AND approved_by_user_id IS NOT NULL))"));
    profile.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    profile.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var archive = b.Entity<Archive>();
    archive.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_archives_scope_id");
    archive.Property(x => x.ProfileId).HasMaxLength(100);
    archive.Property(x => x.ProfileVersion);
    archive.Property(x => x.ObservedProtectionState).HasMaxLength(100);
    archive.Property(x => x.Status).HasMaxLength(40);
    archive.HasIndex(x => new { x.FirmId, x.EngagementId, x.Status }).HasDatabaseName("ix_archives_scope_status");
    ScopeToEngagement(archive, nameof(Archive.FirmId), nameof(Archive.ClientId), nameof(Archive.EngagementId));
    archive.ToTable("archives", t => t.HasCheckConstraint("ck_archive_values",
      "length(trim(profile_id)) > 0 AND profile_version >= 1 AND status IN ('ISSUED','ASSEMBLY_IN_PROGRESS','MANIFEST_BUILT','ASSEMBLY_REVIEWED','RECORDS_ACTION_REQUESTED','PROTECTION_OBSERVED','ARCHIVE_VERIFIED')" +
      " AND ((status IN ('PROTECTION_OBSERVED','ARCHIVE_VERIFIED') AND length(trim(observed_protection_state)) > 0 AND observed_protection_at IS NOT NULL)" +
      " OR status IN ('ISSUED','ASSEMBLY_IN_PROGRESS','MANIFEST_BUILT','ASSEMBLY_REVIEWED','RECORDS_ACTION_REQUESTED'))"));

    var manifest = b.Entity<ArchiveManifest>();
    manifest.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_archive_manifests_firm_id_id");
    manifest.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_archive_manifests_scope_id");
    manifest.Property(x => x.Status).HasMaxLength(30);
    manifest.Property(x => x.ManifestDigest).HasMaxLength(64);
    manifest.Property(x => x.CompletenessStatus).HasMaxLength(30);
    manifest.Property(x => x.CompletenessException).HasMaxLength(2000);
    manifest.HasIndex(x => new { x.FirmId, x.ArchiveId, x.Version }).IsUnique()
      .HasDatabaseName("ux_archive_manifest_archive_version");
    ScopeToEngagement(manifest, nameof(ArchiveManifest.FirmId), nameof(ArchiveManifest.ClientId), nameof(ArchiveManifest.EngagementId));
    manifest.ToTable("archive_manifests", t =>
    {
      t.HasCheckConstraint("ck_archive_manifest_values",
        "version >= 1 AND status IN ('BUILT','REVIEWED') AND manifest_digest ~ '^[0-9a-f]{64}$'" +
        " AND entry_count >= 0 AND completeness_status IN ('COMPLETE','INCOMPLETE')" +
        " AND ((completeness_status = 'INCOMPLETE' AND length(trim(completeness_exception)) > 0)" +
        " OR (completeness_status = 'COMPLETE' AND completeness_exception IS NULL))");
      t.HasCheckConstraint("ck_archive_manifests_lineage_no_self_ref",
        "(predecessor_manifest_id IS NULL OR predecessor_manifest_id <> id) AND (superseded_by_manifest_id IS NULL OR superseded_by_manifest_id <> id)");
    });
    manifest.HasOne<Archive>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    // Self-referential predecessor link: the first manifest has no predecessor; re-archives chain back.
    manifest.Property(x => x.PredecessorManifestId).HasColumnName("predecessor_manifest_id");
    manifest.HasOne<ArchiveManifest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PredecessorManifestId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict)
      .IsRequired(false);
    // Self-referential supersession link: set on a manifest when it is superseded by a newer version.
    manifest.Property(x => x.SupersededByManifestId).HasColumnName("superseded_by_manifest_id");
    manifest.HasOne<ArchiveManifest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.SupersededByManifestId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict)
      .IsRequired(false);

    var structuredExport = b.Entity<ArchiveStructuredExport>();
    structuredExport.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_archive_structured_exports_firm_id_id");
    structuredExport.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_archive_structured_exports_scope_id");
    structuredExport.Property(x => x.Schema).HasMaxLength(100);
    structuredExport.Property(x => x.ContentHash).HasMaxLength(64);
    structuredExport.Property(x => x.PayloadJson).HasColumnType("text");
    structuredExport.HasIndex(x => new { x.FirmId, x.ArchiveManifestId, x.Version }).IsUnique()
      .HasDatabaseName("ux_archive_structured_export_manifest_version");
    ScopeToEngagement(structuredExport, nameof(ArchiveStructuredExport.FirmId),
      nameof(ArchiveStructuredExport.ClientId), nameof(ArchiveStructuredExport.EngagementId));
    structuredExport.ToTable("archive_structured_exports", t => t.HasCheckConstraint("ck_archive_structured_export_values",
      "version >= 1 AND schema = 'records-export.v1' AND length(trim(payload_json)) > 0" +
      " AND content_hash ~ '^[0-9a-f]{64}$' AND byte_count > 0"));
    structuredExport.HasOne<Archive>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    structuredExport.HasOne<ArchiveManifest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveManifestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var manifestEntry = b.Entity<ArchiveManifestEntry>();
    manifestEntry.Property(x => x.EntryKind).HasMaxLength(100);
    manifestEntry.Property(x => x.SourceKind).HasMaxLength(100);
    manifestEntry.Property(x => x.RelativeName).HasMaxLength(2000);
    manifestEntry.Property(x => x.ContentHash).HasMaxLength(64);
    manifestEntry.Property(x => x.MetadataJson).HasMaxLength(16384);
    manifestEntry.HasIndex(x => new { x.FirmId, x.ArchiveManifestId, x.Ordinal }).IsUnique()
      .HasDatabaseName("ux_archive_manifest_entry_ordinal");
    manifestEntry.ToTable("archive_manifest_entries", t => t.HasCheckConstraint("ck_archive_manifest_entry_values",
      "ordinal >= 1 AND length(trim(entry_kind)) > 0 AND length(trim(source_kind)) > 0" +
      " AND length(trim(relative_name)) > 0 AND content_hash ~ '^[0-9a-f]{64}$' AND byte_count >= 0" +
      " AND length(metadata_json) <= 16384"));
    manifestEntry.HasOne<ArchiveManifest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveManifestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var recordsAction = b.Entity<RecordsAction>();
    recordsAction.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_records_actions_firm_id_id");
    recordsAction.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_records_actions_scope_id");
    recordsAction.Property(x => x.DesiredLabel).HasMaxLength(200);
    recordsAction.Property(x => x.DesiredProtection).HasMaxLength(200);
    recordsAction.Property(x => x.ObservedLabel).HasMaxLength(200);
    recordsAction.Property(x => x.ObservedProtection).HasMaxLength(200);
    recordsAction.Property(x => x.State).HasMaxLength(30);
    recordsAction.Property(x => x.ExternalSystem).HasMaxLength(100);
    recordsAction.Property(x => x.ExternalReference).HasMaxLength(500);
    recordsAction.Property(x => x.ObservedBy).HasMaxLength(200);
    recordsAction.Property(x => x.Exception).HasMaxLength(2000);
    recordsAction.HasIndex(x => new { x.FirmId, x.ArchiveId }).IsUnique()
      .HasDatabaseName("ux_records_action_archive");
    recordsAction.ToTable("records_actions", t => t.HasCheckConstraint("ck_records_action_values",
      "length(trim(desired_label)) > 0 AND length(trim(desired_protection)) > 0" +
      " AND state IN ('REQUESTED','OBSERVED','FAILED')" +
      " AND ((state = 'OBSERVED' AND length(trim(observed_label)) > 0 AND length(trim(observed_protection)) > 0 AND observed_at IS NOT NULL AND length(trim(observed_by)) > 0)" +
      " OR (state = 'FAILED' AND length(trim(exception)) > 0) OR state = 'REQUESTED')"));
    recordsAction.HasOne<Archive>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    recordsAction.HasOne<ArchiveManifest>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveManifestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    recordsAction.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.RequestedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var recordsActionEvidence = b.Entity<RecordsActionEvidence>();
    recordsActionEvidence.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_records_action_evidence_firm_id_id");
    recordsActionEvidence.Property(x => x.EventKind).HasMaxLength(30);
    recordsActionEvidence.Property(x => x.DesiredLabel).HasMaxLength(200);
    recordsActionEvidence.Property(x => x.DesiredProtection).HasMaxLength(200);
    recordsActionEvidence.Property(x => x.ObservedLabel).HasMaxLength(200);
    recordsActionEvidence.Property(x => x.ObservedProtection).HasMaxLength(200);
    recordsActionEvidence.Property(x => x.ExternalReference).HasMaxLength(500);
    recordsActionEvidence.Property(x => x.ObservedBy).HasMaxLength(200);
    recordsActionEvidence.Property(x => x.Exception).HasMaxLength(2000);
    recordsActionEvidence.HasIndex(x => new { x.FirmId, x.RecordsActionId, x.Sequence }).IsUnique()
      .HasDatabaseName("ux_records_action_evidence_sequence");
    recordsActionEvidence.ToTable("records_action_evidence", t => t.HasCheckConstraint("ck_records_action_evidence_values",
      "sequence >= 1 AND event_kind IN ('REQUESTED','OBSERVED','FAILED')" +
      " AND length(trim(desired_label)) > 0 AND length(trim(desired_protection)) > 0" +
      " AND ((event_kind = 'OBSERVED' AND length(trim(observed_label)) > 0 AND length(trim(observed_protection)) > 0 AND length(trim(observed_by)) > 0)" +
      " OR (event_kind = 'FAILED' AND length(trim(exception)) > 0) OR event_kind = 'REQUESTED')"));
    ScopeToEngagement(recordsActionEvidence, nameof(RecordsActionEvidence.FirmId),
      nameof(RecordsActionEvidence.ClientId), nameof(RecordsActionEvidence.EngagementId));
    recordsActionEvidence.HasOne<RecordsAction>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.RecordsActionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    recordsActionEvidence.HasOne<Archive>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    recordsActionEvidence.HasOne<ArchiveManifest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveManifestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    recordsActionEvidence.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var legalHold = b.Entity<LegalHold>();
    legalHold.Property(x => x.HoldReference).HasMaxLength(200);
    legalHold.Property(x => x.State).HasMaxLength(30);
    legalHold.Property(x => x.ExternalSystem).HasMaxLength(100);
    legalHold.Property(x => x.ExternalReference).HasMaxLength(500);
    legalHold.Property(x => x.Notes).HasMaxLength(2000);
    legalHold.HasIndex(x => new { x.FirmId, x.ArchiveId, x.HoldReference }).IsUnique()
      .HasDatabaseName("ux_legal_hold_reference");
    legalHold.ToTable("legal_holds", t => t.HasCheckConstraint("ck_legal_hold_values",
      "length(trim(hold_reference)) > 0 AND state IN ('REQUESTED','APPLIED','OBSERVED','RELEASED')" +
      " AND ((state IN ('APPLIED','OBSERVED') AND applied_at IS NOT NULL) OR state IN ('REQUESTED','RELEASED'))" +
      " AND ((state = 'OBSERVED' AND observed_at IS NOT NULL) OR state <> 'OBSERVED')" +
      " AND ((state = 'RELEASED' AND released_at IS NOT NULL) OR state <> 'RELEASED')"));
    legalHold.HasOne<Archive>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ArchiveId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    legalHold.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.RequestedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var recordState = b.Entity<RecordState>();
    recordState.Property(x => x.ArtifactKind).HasMaxLength(50);
    recordState.Property(x => x.LocalState).HasMaxLength(30);
    recordState.Property(x => x.ObservedLabel).HasMaxLength(200);
    recordState.HasIndex(x => new { x.FirmId, x.EngagementId, x.ArtifactId }).IsUnique()
      .HasDatabaseName("ux_record_state_artifact");
    ScopeToEngagement(recordState, nameof(RecordState.FirmId), nameof(RecordState.ClientId),
      nameof(RecordState.EngagementId));
    recordState.ToTable("record_states", t => t.HasCheckConstraint("ck_record_state_values",
      "length(trim(artifact_kind)) > 0 AND local_state IN ('Active','Locked','Archived')"));

    var attestation = b.Entity<ProtectionAttestation>();
    attestation.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_protection_attestations_firm_id_id");
    attestation.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_protection_attestations_scope_id");
    attestation.Property(x => x.ArtifactHash).HasMaxLength(64);
    attestation.Property(x => x.Binding).HasMaxLength(200);
    attestation.Property(x => x.ProfileId).HasMaxLength(100);
    attestation.Property(x => x.ObservedState).HasMaxLength(30);
    attestation.Property(x => x.Verifier).HasMaxLength(200);
    attestation.Property(x => x.RecheckRule).HasMaxLength(200);
    attestation.HasIndex(x => new { x.FirmId, x.EngagementId, x.ArtifactHash }).HasDatabaseName("ix_protection_attestations_artifact");
    attestation.ToTable("protection_attestations", t => t.HasCheckConstraint("ck_protection_attestation_values",
      "artifact_hash ~ '^[0-9a-f]{64}$' AND profile_version >= 1 AND length(trim(binding)) > 0 AND length(trim(profile_id)) > 0 AND observed_state IN ('PROTECTED','PENDING','EXPIRED','RECHECK_REQUIRED')"));
    ScopeToEngagement(attestation, nameof(ProtectionAttestation.FirmId), nameof(ProtectionAttestation.ClientId),
      nameof(ProtectionAttestation.EngagementId));

    var acceptance = b.Entity<AcceptanceDecision>();

    acceptance.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_acceptance_decisions_firm_id_id");
    acceptance.Property(x => x.Decision).HasMaxLength(40);
    acceptance.Property(x => x.ServiceRoute).HasMaxLength(50);
    acceptance.Property(x => x.Rationale).HasMaxLength(4000);
    acceptance.Property(x => x.Conditions).HasMaxLength(4000);
    acceptance.Property(x => x.EvaluationTemplateVersion).HasMaxLength(100);
    acceptance.Property(x => x.EvaluationSnapshotDigest).HasMaxLength(64);
    acceptance.HasIndex(x => new { x.FirmId, x.PracticeClientId, x.Generation }).HasDatabaseName("ix_acceptance_client_generation");
    acceptance.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PracticeClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    acceptance.ToTable("acceptance_decisions", t =>
    {
      t.HasCheckConstraint("ck_acceptance_decision_values",
        "generation >= 1 AND length(trim(service_route)) > 0" +
        " AND decision IN ('Pending','Accepted','AcceptedWithConditions','Declined','Deferred')" +
        " AND ((decision IN ('Accepted','AcceptedWithConditions','Declined','Deferred') AND decided_at IS NOT NULL" +
        "        AND decided_by_user_id IS NOT NULL) OR decision = 'Pending')");
      t.HasCheckConstraint("ck_acceptance_decision_path",
        "path IN ('NEW_CLIENT','CONTINUANCE') AND (path = 'CONTINUANCE' OR prior_decision_id IS NULL)");
      t.HasCheckConstraint("ck_acceptance_decision_evidence",
        "((decision = 'Pending') OR (length(trim(rationale)) > 0 AND length(trim(evaluation_template_version)) > 0 AND evaluation_snapshot_digest ~ '^[0-9a-f]{64}$'))" +
        " AND ((decision = 'AcceptedWithConditions' AND length(trim(conditions)) > 0) OR decision <> 'AcceptedWithConditions')");
    });

    var evaluation = b.Entity<EvaluationResponse>();
    evaluation.Property(x => x.Bank).HasMaxLength(4);
    evaluation.Property(x => x.QuestionId).HasMaxLength(50);
    evaluation.Property(x => x.Answer).HasMaxLength(2000);
    evaluation.HasIndex(x => new { x.FirmId, x.PracticeClientId, x.Bank, x.QuestionId, x.Revision }).IsUnique()
      .HasDatabaseName("ux_evaluation_response_question");
    evaluation.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PracticeClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    evaluation.Property(x => x.EvidenceReference).HasMaxLength(500);
    evaluation.ToTable("evaluation_responses", t => t.HasCheckConstraint("ck_evaluation_response_values",
      "bank IN ('CE','RV') AND revision >= 1 AND generation >= 1 AND length(trim(question_id)) > 0 AND length(trim(answer)) > 0"));

    var mappingRule = b.Entity<MappingRule>();
    mappingRule.Property(x => x.MappingCode).HasMaxLength(50);
    mappingRule.Property(x => x.SourcePattern).HasMaxLength(200);
    mappingRule.HasIndex(x => new { x.FirmId, x.EngagementId, x.MappingCode, x.Revision }).IsUnique()
      .HasDatabaseName("ux_mapping_rule_code_revision");
    ScopeToEngagement(mappingRule, nameof(MappingRule.FirmId), nameof(MappingRule.ClientId),
      nameof(MappingRule.EngagementId));
    mappingRule.ToTable("mapping_rules", t => t.HasCheckConstraint("ck_mapping_rule_values",
      "revision >= 1 AND length(trim(mapping_code)) > 0 AND length(trim(source_pattern)) > 0"));
  }
}
