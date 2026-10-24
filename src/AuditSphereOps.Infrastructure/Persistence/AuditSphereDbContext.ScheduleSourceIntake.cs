using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  // Source and command evidence remain separate from the mutable legacy schedule projection.
  private static void ConfigureScheduleSourceIntake(ModelBuilder b)
  {
    var capture = b.Entity<AuditScheduleSourceCapture>();
    capture.HasKey(x => x.ScheduleId);
    capture.Property(x => x.ScheduleId).ValueGeneratedNever();
    capture.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ScheduleId })
      .HasName("AK_audit_schedule_source_captures_scope_id");
    capture.HasIndex(x => new { x.FirmId, x.ActorUserId, x.ActorEpoch, x.RequestId }).IsUnique()
      .HasDatabaseName("ux_schedule_source_capture_actor_request");
    capture.Property(x => x.Basis).HasMaxLength(50);
    capture.Property(x => x.LegalEntityKey).HasMaxLength(200);
    capture.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    capture.Property(x => x.ProfileVersion).HasMaxLength(100);
    capture.Property(x => x.RequestJson).HasColumnType("text");
    capture.Property(x => x.ReceiptJson).HasColumnType("text");
    capture.Property(x => x.ManifestJson).HasColumnType("text");
    capture.Property(x => x.RequestHash).HasMaxLength(64);
    capture.Property(x => x.ReceiptHash).HasMaxLength(64);
    capture.Property(x => x.ManifestHash).HasMaxLength(64);
    capture.ToTable("audit_schedule_source_captures", t => t.HasCheckConstraint("ck_schedule_source_capture_values", """
      actor_epoch >= 1 AND period_revision >= 1 AND input_generation >= 1
      AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid
      AND length(trim(legal_entity_key)) BETWEEN 1 AND 200
      AND (basis IS NULL OR length(trim(basis)) BETWEEN 1 AND 50)
      AND currency ~ '^[A-Z]{3}$'
      AND profile_version = 'audit-client-listing-csv.v1'
      AND request_hash ~ '^[0-9a-f]{64}$' AND receipt_hash ~ '^[0-9a-f]{64}$'
      AND manifest_hash ~ '^[0-9a-f]{64}$'
      AND octet_length(request_json) BETWEEN 2 AND 65536
      AND octet_length(receipt_json) BETWEEN 2 AND 65536
      AND octet_length(manifest_json) BETWEEN 2 AND 16777216
      AND request_hash = encode(sha256(convert_to(request_json, 'UTF8')), 'hex')
      AND receipt_hash = encode(sha256(convert_to(receipt_json, 'UTF8')), 'hex')
      AND manifest_hash = encode(sha256(convert_to(manifest_json, 'UTF8')), 'hex')
      AND jsonb_typeof(request_json::jsonb) = 'object'
      AND jsonb_typeof(receipt_json::jsonb) = 'object'
      AND jsonb_typeof(manifest_json::jsonb) = 'object'
      AND COALESCE(manifest_json::jsonb->>'Version' = 'audit-schedule-source-capture.v1', false)
      """));
    capture.HasOne<AuditSchedule>().WithOne()
      .HasForeignKey<AuditScheduleSourceCapture>(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ScheduleId })
      .HasPrincipalKey<AuditSchedule>(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    capture.HasOne<PbcAcceptanceReceipt>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcAcceptanceReceiptId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    capture.HasOne<SourceAcceptanceDecision>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.AcceptedSourceDecisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    capture.HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.TrialBalanceDatasetId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    capture.HasOne<ClientReportingPeriod>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    capture.HasOne<ClientReportingBook>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.BookId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    capture.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var review = b.Entity<AuditScheduleSourceReview>();
    review.HasKey(x => x.Id);
    review.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ScheduleId }).IsUnique()
      .HasDatabaseName("ux_schedule_source_review_capture");
    review.HasIndex(x => new { x.FirmId, x.ActorUserId, x.ActorEpoch, x.RequestId }).IsUnique()
      .HasDatabaseName("ux_schedule_source_review_actor_request");
    review.Property(x => x.Action).HasMaxLength(10);
    review.Property(x => x.Reason).HasMaxLength(2000);
    review.Property(x => x.EvidenceReference).HasMaxLength(2000);
    review.Property(x => x.CaptureHash).HasMaxLength(64);
    review.Property(x => x.RequestJson).HasColumnType("text");
    review.Property(x => x.ReceiptJson).HasColumnType("text");
    review.Property(x => x.RequestHash).HasMaxLength(64);
    review.Property(x => x.ReceiptHash).HasMaxLength(64);
    review.ToTable("audit_schedule_source_reviews", t => t.HasCheckConstraint("ck_schedule_source_review_values", """
      actor_epoch >= 1 AND input_generation >= 1
      AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid
      AND action IN ('APPROVE','RETURN') AND length(trim(reason)) BETWEEN 1 AND 2000
      AND length(evidence_reference) <= 2000
      AND capture_hash ~ '^[0-9a-f]{64}$'
      AND request_hash ~ '^[0-9a-f]{64}$' AND receipt_hash ~ '^[0-9a-f]{64}$'
      AND octet_length(request_json) BETWEEN 2 AND 65536
      AND octet_length(receipt_json) BETWEEN 2 AND 65536
      AND request_hash = encode(sha256(convert_to(request_json, 'UTF8')), 'hex')
      AND receipt_hash = encode(sha256(convert_to(receipt_json, 'UTF8')), 'hex')
      AND jsonb_typeof(request_json::jsonb) = 'object'
      AND jsonb_typeof(receipt_json::jsonb) = 'object'
      """));
    review.HasOne<AuditScheduleSourceCapture>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ScheduleId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ScheduleId })
      .OnDelete(DeleteBehavior.Restrict);
    review.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
