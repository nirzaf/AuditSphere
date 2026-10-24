using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureNativeGeneralLedgerSources(ModelBuilder b)
  {
    var source = b.Entity<NativeGeneralLedgerSource>();
    source.HasKey(x => x.ImportBatchId);
    source.HasOne<SourceImportBatch>().WithOne()
      .HasForeignKey<NativeGeneralLedgerSource>(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ImportBatchId })
      .HasPrincipalKey<SourceImportBatch>(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    source.HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.TrialBalanceDatasetId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    source.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    source.HasIndex(x => new { x.FirmId, x.ActorId, x.ActorEpoch, x.RequestId })
      .IsUnique().HasDatabaseName("ux_native_gl_actor_request");
    source.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SourceIdentityHash })
      .IsUnique().HasDatabaseName("ux_native_gl_source_identity");
    source.Property(x => x.NativeTrialBalanceSourceHash).HasMaxLength(64);
    source.Property(x => x.NativeTrialBalanceManifestHash).HasMaxLength(64);
    source.Property(x => x.RequestHash).HasMaxLength(64);
    source.Property(x => x.SourceIdentityHash).HasMaxLength(64);
    source.Property(x => x.ManifestHash).HasMaxLength(64);
    source.Property(x => x.GeneratedRowsHash).HasMaxLength(64);
    source.Property(x => x.ManifestJson).HasColumnType("text");
    source.Property(x => x.GeneratedRowsJson).HasColumnType("text");
    source.ToTable("native_general_ledger_sources", t => t.HasCheckConstraint("ck_native_gl_source_values",
      "actor_epoch >= 1 AND transaction_count BETWEEN 1 AND 250000 AND line_count BETWEEN 1 AND 250000 " +
      "AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid " +
      "AND native_trial_balance_source_hash ~ '^[0-9a-f]{64}$' AND native_trial_balance_manifest_hash ~ '^[0-9a-f]{64}$' " +
      "AND request_hash ~ '^[0-9a-f]{64}$' AND source_identity_hash ~ '^[0-9a-f]{64}$' " +
      "AND manifest_hash ~ '^[0-9a-f]{64}$' AND generated_rows_hash ~ '^[0-9a-f]{64}$' " +
      "AND jsonb_typeof(manifest_json::jsonb) = 'object' " +
      "AND jsonb_typeof(generated_rows_json::jsonb) = 'array' AND jsonb_array_length(generated_rows_json::jsonb) = line_count " +
      "AND octet_length(manifest_json) BETWEEN 2 AND 16777216 AND octet_length(generated_rows_json) BETWEEN 2 AND 33554432 " +
      "AND manifest_hash = encode(sha256(convert_to(manifest_json, 'UTF8')), 'hex') " +
      "AND generated_rows_hash = encode(sha256(convert_to(generated_rows_json, 'UTF8')), 'hex')"));

    var batch = b.Entity<SourceImportBatch>();
    batch.Property(x => x.OriginKind).HasMaxLength(30).HasDefaultValue(SourceImportOrigins.External);
    batch.ToTable("source_import_batches", t => t.HasCheckConstraint("ck_source_import_origin",
      "origin_kind IN ('EXTERNAL', 'NATIVE_LEDGER') AND (origin_kind <> 'NATIVE_LEDGER' OR (" +
      "source_kind = 'GL' AND book_id IS NULL AND raw_file_sha256_hex = '' " +
      "AND profile_version = 'native-posted-gl.v1' AND parser_version = 'native-gl-export.v1' " +
      "AND expected_chunk_count = 0 AND accepted_chunk_count = 0))"));
  }
}
