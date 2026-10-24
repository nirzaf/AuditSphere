using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientOpeningSuccessors(ModelBuilder b)
  {
    var proof = b.Entity<ClientOperationalOpeningSuccessorProof>();
    proof.HasKey(x => x.OpeningId);
    proof.HasOne<ClientOperationalOpeningBalance>().WithOne()
      .HasForeignKey<ClientOperationalOpeningSuccessorProof>(x => new { x.FirmId, x.ClientId, x.OpeningId })
      .HasPrincipalKey<ClientOperationalOpeningBalance>(x => new { x.FirmId, x.ClientId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    proof.HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SourceDatasetId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    proof.HasOne<ClientReportingPeriod>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.PriorPeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    proof.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorId }).HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    proof.HasIndex(x => new { x.FirmId, x.ActorId, x.ActorEpoch, x.RequestId })
      .IsUnique().HasDatabaseName("ux_native_opening_successor_request");
    proof.Property(x => x.SourceIdentityHash).HasMaxLength(64);
    proof.Property(x => x.ProofHash).HasMaxLength(64);
    proof.Property(x => x.RequestHash).HasMaxLength(64);
    proof.Property(x => x.ProofJson).HasColumnType("text");
    proof.ToTable("client_operational_opening_successor_proofs", t => t.HasCheckConstraint(
      "ck_native_opening_successor_proof", """
        actor_epoch >= 1 AND prior_period_revision >= 1
        AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid
        AND source_identity_hash ~ '^[0-9a-f]{64}$'
        AND proof_hash ~ '^[0-9a-f]{64}$' AND request_hash ~ '^[0-9a-f]{64}$'
        AND octet_length(proof_json) BETWEEN 2 AND 16777216
        AND proof_hash = encode(sha256(convert_to(proof_json, 'UTF8')), 'hex')
        AND jsonb_typeof(proof_json::jsonb) = 'object'
        AND jsonb_typeof(proof_json::jsonb->'Version')='string'
        AND COALESCE(proof_json::jsonb->>'Version' IN ('native-opening-successor.v1', 'native-opening-successor.v2'), false)
        AND CASE WHEN proof_json::jsonb->>'Version'='native-opening-successor.v1'
          THEN NOT (proof_json::jsonb->'Facts' ? 'CarriedPopulation')
          ELSE jsonb_typeof(proof_json::jsonb->'Facts'->'CarriedPopulation')='object' END
        AND proof_json::jsonb @> jsonb_build_object(
          'OpeningId', opening_id,
          'FirmId', firm_id, 'ClientId', client_id, 'EngagementId', engagement_id,
          'PriorPeriodId', prior_period_id, 'PriorPeriodRevision', prior_period_revision,
          'SourceDatasetId', source_dataset_id, 'SourceIdentityHash', source_identity_hash)
        AND COALESCE((proof_json::jsonb->>'OpeningManifestHash') ~ '^[0-9a-f]{64}$', false)
        AND proof_json::jsonb ? 'OpeningManifestHash'
        """));
  }
}
