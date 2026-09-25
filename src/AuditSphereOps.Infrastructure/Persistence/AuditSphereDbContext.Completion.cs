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
  private static void ConfigureCompletion(ModelBuilder b)
  {
    var candidate = b.Entity<ReleaseCandidate>();
    candidate.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_release_candidates_firm_id_id");
    candidate.Property(x => x.TargetKind).HasMaxLength(50);
    candidate.Property(x => x.ManifestDigest).HasMaxLength(64);
    candidate.Property(x => x.Status).HasMaxLength(16);
    candidate.HasIndex(x => new
      { x.FirmId, x.TargetKind, x.TargetId, x.TargetRevision, x.ManifestDigest })
      .IsUnique().HasDatabaseName("ux_release_candidate_identity");
    candidate.ToTable("release_candidates", t => t.HasCheckConstraint("ck_release_candidate_values",
      "target_kind IN ('WORKPAPER','FINANCIAL_PACKAGE') AND revision >= 1 AND target_revision >= 1 AND input_generation >= 1 AND policy_generation >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND status IN ('READY','ISSUED')"));
    candidate.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    candidate.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    candidate.HasOne<Approval>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovalId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var checkpoint = b.Entity<ReleaseCheckpoint>();
    checkpoint.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_release_checkpoints_firm_id_id");
    checkpoint.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_release_checkpoints_scope_id");
    checkpoint.Property(x => x.AuthorizedReleaseKey).HasMaxLength(200);
    checkpoint.Property(x => x.ManifestDigest).HasMaxLength(64);
    checkpoint.Property(x => x.StoredReference).HasMaxLength(500);
    checkpoint.Property(x => x.ReadBackDigest).HasMaxLength(64);
    checkpoint.Property(x => x.VerifiedStatus).HasMaxLength(30);
    checkpoint.Property(x => x.Verifier).HasMaxLength(200);
    checkpoint.HasIndex(x => new { x.FirmId, x.ReleaseCandidateId, x.CandidateRevision, x.ManifestDigest })
      .HasDatabaseName("ix_release_checkpoint_candidate_manifest");
    checkpoint.ToTable("release_checkpoints", t => t.HasCheckConstraint("ck_release_checkpoint_values",
      "candidate_revision >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND read_back_digest ~ '^[0-9a-f]{64}$' AND length(trim(authorized_release_key)) > 0 AND length(trim(stored_reference)) > 0 AND verified_status IN ('VERIFIED','PENDING','MISMATCHED','EXPIRED')"));
    ScopeToEngagement(checkpoint, nameof(ReleaseCheckpoint.FirmId), nameof(ReleaseCheckpoint.ClientId), nameof(ReleaseCheckpoint.EngagementId));
    checkpoint.HasOne<ReleaseCandidate>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReleaseCandidateId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var lineage = b.Entity<SignatureLineage>();
    lineage.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_signature_lineages_firm_id_id");
    lineage.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_signature_lineages_scope_id");
    lineage.Property(x => x.PreSignArtifactHash).HasMaxLength(64);
    lineage.Property(x => x.SignedArtifactHash).HasMaxLength(64);
    lineage.Property(x => x.SigningMethod).HasMaxLength(100);
    lineage.Property(x => x.RequestIdentity).HasMaxLength(200);
    lineage.Property(x => x.VerificationOutcome).HasMaxLength(30);
    lineage.Property(x => x.Verifier).HasMaxLength(200);
    lineage.HasIndex(x => new { x.FirmId, x.CandidateId }).HasDatabaseName("ix_signature_lineages_candidate");
    lineage.ToTable("signature_lineages", t => t.HasCheckConstraint("ck_signature_lineage_values",
      "pre_sign_artifact_hash ~ '^[0-9a-f]{64}$' AND signed_artifact_hash ~ '^[0-9a-f]{64}$' AND length(trim(signing_method)) > 0 AND length(trim(request_identity)) > 0 AND verification_outcome IN ('VERIFIED','INVALID','REJECTED')"));
    ScopeToEngagement(lineage, nameof(SignatureLineage.FirmId), nameof(SignatureLineage.ClientId), nameof(SignatureLineage.EngagementId));
    lineage.HasOne<ReleaseCandidate>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CandidateId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var release = b.Entity<Release>();
    release.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_releases_firm_id_id");
    release.Property(x => x.ManifestDigest).HasMaxLength(64);
    release.Property(x => x.AuthorizedReleaseKey).HasMaxLength(200);
    release.HasIndex(x => new { x.FirmId, x.AuthorizedReleaseKey })
      .IsUnique().HasDatabaseName("ux_release_authorized_key");
    release.ToTable("releases", t => t.HasCheckConstraint("ck_release_values",
      "package_revision >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND length(authorized_release_key) > 0"));
    release.HasOne<ReleaseCandidate>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReleaseCandidateId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    release.HasOne<ReleaseCheckpoint>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CheckpointId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    release.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    release.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    release.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReleasedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
  }
}
