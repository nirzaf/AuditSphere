using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureConfirmationBookedSources(ModelBuilder b)
  {
    var source = b.Entity<AuditConfirmationBookedSource>();
    source.HasKey(x => x.ConfirmationCaseId);
    source.HasOne<AuditConfirmationCase>().WithOne()
      .HasForeignKey<AuditConfirmationBookedSource>(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ConfirmationCaseId })
      .HasPrincipalKey<AuditConfirmationCase>(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    source.HasOne<AuditBankReconciliation>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.BankReconciliationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    source.HasOne<AuditSchedule>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.LedgerScheduleId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    source.HasOne<AuditSchedule>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.StatementScheduleId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    source.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    source.Property(x => x.SourceKind).HasMaxLength(40);
    source.Property(x => x.SnapshotSha256).HasMaxLength(64);
    source.Property(x => x.SnapshotJson).HasColumnType("text");
    source.ToTable("audit_confirmation_booked_sources", t => t.HasCheckConstraint("ck_confirmation_booked_source", """
      source_kind = 'BANK_RECONCILIATION'
      AND ledger_schedule_id <> statement_schedule_id
      AND snapshot_sha256 ~ '^[0-9a-f]{64}$'
      AND octet_length(snapshot_json) BETWEEN 2 AND 16777216
      AND snapshot_sha256 = encode(sha256(convert_to(snapshot_json, 'UTF8')), 'hex')
      AND jsonb_typeof(snapshot_json::jsonb) = 'object'
      AND COALESCE(snapshot_json::jsonb->>'Version' = 'bank-confirmation-booked-source.v1', false)
      AND COALESCE(snapshot_json::jsonb->'Bank' @> jsonb_build_object(
        'Id', bank_reconciliation_id, 'FirmId', firm_id, 'ClientId', client_id,
        'EngagementId', engagement_id, 'LedgerScheduleId', ledger_schedule_id,
        'StatementScheduleId', statement_schedule_id), false)
      """));
  }
}
