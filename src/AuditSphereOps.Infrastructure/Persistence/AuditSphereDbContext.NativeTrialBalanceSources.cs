using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureNativeTrialBalanceSources(ModelBuilder b)
  {
    var source = b.Entity<NativeTrialBalanceSource>();
    source.HasKey(x => x.DatasetId);
    source.HasOne<TrialBalanceDataset>().WithOne()
      .HasForeignKey<NativeTrialBalanceSource>(x => new { x.FirmId, x.ClientId, x.EngagementId, x.DatasetId })
      .HasPrincipalKey<TrialBalanceDataset>(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    source.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    source.HasIndex(x => new { x.FirmId, x.ActorId, x.ActorEpoch, x.RequestId })
      .IsUnique().HasDatabaseName("ux_native_tb_actor_request");
    source.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SourceIdentityHash })
      .IsUnique().HasDatabaseName("ux_native_tb_source_identity");
    source.Property(x => x.RequestHash).HasMaxLength(64);
    source.Property(x => x.SourceIdentityHash).HasMaxLength(64);
    source.Property(x => x.ManifestHash).HasMaxLength(64);
    source.Property(x => x.GeneratedRowsHash).HasMaxLength(64);
    source.Property(x => x.ManifestJson).HasColumnType("text");
    source.Property(x => x.GeneratedRowsJson).HasColumnType("text");
    source.ToTable("native_trial_balance_sources", t => t.HasCheckConstraint("ck_native_tb_source_values",
      "actor_epoch >= 1 AND row_count BETWEEN 1 AND 250000 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid " +
      "AND request_hash ~ '^[0-9a-f]{64}$' AND source_identity_hash ~ '^[0-9a-f]{64}$' " +
      "AND manifest_hash ~ '^[0-9a-f]{64}$' AND generated_rows_hash ~ '^[0-9a-f]{64}$' " +
      "AND jsonb_typeof(manifest_json::jsonb) = 'object' " +
      "AND jsonb_typeof(generated_rows_json::jsonb) = 'array' AND jsonb_array_length(generated_rows_json::jsonb) = row_count " +
      "AND octet_length(manifest_json) BETWEEN 2 AND 16777216 " +
      "AND octet_length(generated_rows_json) BETWEEN 2 AND 33554432 " +
      "AND manifest_hash = encode(sha256(convert_to(manifest_json, 'UTF8')), 'hex') " +
      "AND generated_rows_hash = encode(sha256(convert_to(generated_rows_json, 'UTF8')), 'hex')"));
    b.Entity<TrialBalanceDataset>().ToTable("trial_balance_datasets", t =>
      t.HasCheckConstraint("ck_tb_native_origin",
        "source_kind <> 'Native' OR (period_id IS NOT NULL AND book_id IS NULL AND import_batch_id IS NULL " +
        "AND raw_file_sha256_hex = '' AND import_profile_version = 'native-period-ledger.v1' " +
        "AND source_layout = 'DEBIT_CREDIT' AND revision >= 1 AND currency ~ '^[A-Z]{3}$' " +
        "AND length(trim(legal_entity_key)) > 0 AND normalized_dataset_digest ~ '^[0-9a-f]{64}$' " +
        "AND sha256_hex ~ '^[0-9a-f]{64}$')"));
  }
}
