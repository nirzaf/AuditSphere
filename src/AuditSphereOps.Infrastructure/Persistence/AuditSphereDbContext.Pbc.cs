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
  private static void ConfigurePbc(ModelBuilder b)
  {
    var request = b.Entity<PbcRequest>();
    request.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_pbc_requests_firm_id_id");
    request.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_pbc_requests_scope_id");
    request.Property(x => x.Objective).HasMaxLength(2000);
    request.Property(x => x.EntityScope).HasMaxLength(500);
    request.Property(x => x.PeriodStart).HasMaxLength(10);
    request.Property(x => x.PeriodEnd).HasMaxLength(10);
    request.Property(x => x.Area).HasMaxLength(200);
    request.Property(x => x.RequestedFormat).HasMaxLength(200);
    request.Property(x => x.ControlTotals).HasMaxLength(2000);
    request.Property(x => x.DueDate).HasMaxLength(10);
    request.Property(x => x.Confidentiality).HasMaxLength(100);
    request.Property(x => x.AcceptanceCriteria).HasMaxLength(4000);
    request.Property(x => x.State).HasMaxLength(32);
    request.Property(x => x.ClarificationReason).HasMaxLength(2000);
    request.HasIndex(x => new { x.FirmId, x.EngagementId, x.State, x.DueDate });
    request.ToTable("pbc_requests", t => t.HasCheckConstraint("ck_pbc_request_values",
      "state IN ('DRAFT','SENT','ACKNOWLEDGED','PARTIALLY_RECEIVED','RECEIVED','UNDER_REVIEW','ACCEPTED','CLOSED','CLARIFICATION_REQUIRED','RESUBMITTED') AND revision >= 1 AND length(trim(objective)) > 0 AND length(trim(entity_scope)) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND length(trim(area)) > 0 AND length(trim(requested_format)) > 0 AND length(trim(due_date)) = 10 AND length(trim(confidentiality)) > 0 AND length(trim(acceptance_criteria)) > 0 AND ((state = 'ACCEPTED' AND accepted_at IS NOT NULL AND accepted_by_user_id IS NOT NULL) OR state <> 'ACCEPTED')"));
    request.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    request.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    request.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientOwnerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    request.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.FirmOwnerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    request.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReviewerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    request.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    request.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.AcceptedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var intent = b.Entity<PbcUploadIntent>();
    intent.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_pbc_upload_intents_firm_id_id");
    intent.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_pbc_upload_intents_scope_id");
    intent.Property(x => x.FileName).HasMaxLength(255);
    intent.Property(x => x.ContentType).HasMaxLength(200);
    intent.Property(x => x.DeclaredSha256Hex).HasMaxLength(64);
    intent.Property(x => x.CapabilityHash).HasMaxLength(64);
    intent.Property(x => x.State).HasMaxLength(16);
    intent.Property(x => x.FinalSha256Hex).HasMaxLength(64);
    intent.Property(x => x.FailureReason).HasMaxLength(1000);
    intent.Property(x => x.ProviderReceiptDigest).HasMaxLength(64);
    intent.HasIndex(x => new { x.FirmId, x.PbcRequestId, x.CreatedAt });
    intent.ToTable("pbc_upload_intents", t => t.HasCheckConstraint("ck_pbc_upload_intent_values",
      "state IN ('STARTED','CHUNKING','STAGED','RECEIVED','FAILED','EXPIRED') AND revision >= 1 AND length(trim(file_name)) > 0 AND length(trim(content_type)) > 0 AND declared_byte_count > 0 AND declared_byte_count <= 262144000 AND received_byte_count >= 0 AND received_byte_count <= declared_byte_count AND declared_sha256_hex ~ '^[0-9a-f]{64}$' AND capability_hash ~ '^[0-9a-f]{64}$' AND expires_at > created_at AND ((state = 'STAGED' AND transfer_operation_id IS NOT NULL) OR state NOT IN ('STAGED')) AND ((state = 'RECEIVED' AND completed_at IS NOT NULL AND final_sha256_hex = declared_sha256_hex AND provider_registered_at IS NOT NULL AND provider_receipt_digest ~ '^[0-9a-f]{64}$' AND transfer_operation_id IS NOT NULL) OR state <> 'RECEIVED')"));
    intent.HasOne<AuditSphereOps.Domain.Completion.DurableOperation>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.TransferOperationId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    intent.HasOne<PbcRequest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcRequestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    intent.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.UploaderUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var chunk = b.Entity<PbcUploadChunk>();
    chunk.Property(x => x.Sha256Hex).HasMaxLength(64);
    chunk.Property(x => x.StagedPath).HasMaxLength(2000);
    chunk.HasIndex(x => new { x.FirmId, x.PbcUploadIntentId, x.ChunkIndex })
      .IsUnique().HasDatabaseName("ux_pbc_upload_chunk_identity");
    chunk.ToTable("pbc_upload_chunks", t => t.HasCheckConstraint("ck_pbc_upload_chunk_values",
      "chunk_index >= 0 AND \"offset\" >= 0 AND byte_count > 0 AND byte_count <= 8388608 AND sha256_hex ~ '^[0-9a-f]{64}$'"));
    chunk.HasOne<PbcUploadIntent>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcUploadIntentId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var communication = b.Entity<PbcCommunication>();
    communication.Property(x => x.Kind).HasMaxLength(32);
    communication.Property(x => x.Body).HasMaxLength(4000);
    communication.Property(x => x.RecipientEmail).HasMaxLength(320);
    communication.Property(x => x.Subject).HasMaxLength(300);
    communication.Property(x => x.PortalUrl).HasMaxLength(2000);
    communication.Property(x => x.DeliveryState).HasMaxLength(16);
    communication.Property(x => x.ProviderCorrelationId).HasMaxLength(500);
    communication.HasIndex(x => new { x.FirmId, x.PbcRequestId, x.CreatedAt });
    communication.ToTable("pbc_communications", t => t.HasCheckConstraint("ck_pbc_communication_values",
      "kind IN ('REQUEST','STAFF_MESSAGE','CLIENT_MESSAGE','EMAIL') AND length(trim(body)) > 0 AND (delivery_state IS NULL OR delivery_state IN ('QUEUED','SENT','FAILED','UNCERTAIN')) AND ((kind = 'EMAIL' AND recipient_email IS NOT NULL AND subject IS NOT NULL AND portal_url IS NOT NULL AND delivery_state IS NOT NULL) OR (kind <> 'EMAIL' AND recipient_email IS NULL AND subject IS NULL AND portal_url IS NULL AND delivery_state IS NULL))"));
    communication.HasOne<PbcRequest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcRequestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    communication.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.AuthorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
