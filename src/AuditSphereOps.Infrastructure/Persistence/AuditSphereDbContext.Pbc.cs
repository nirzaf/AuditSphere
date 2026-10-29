using Microsoft.EntityFrameworkCore;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;

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

    var acceptance = b.Entity<PbcAcceptanceReceipt>();
    acceptance.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_pbc_acceptance_receipts_firm_id_id");
    acceptance.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_pbc_acceptance_receipts_scope_id");
    acceptance.Property(x => x.FileName).HasMaxLength(255);
    acceptance.Property(x => x.ContentSha256Hex).HasMaxLength(64);
    acceptance.Property(x => x.ProviderIdentity).HasMaxLength(1000);
    acceptance.Property(x => x.ProviderReceiptDigest).HasMaxLength(64);
    acceptance.Property(x => x.SourceDigest).HasMaxLength(64);
    acceptance.HasIndex(x => new { x.FirmId, x.PbcRequestId }).IsUnique()
      .HasDatabaseName("ux_pbc_acceptance_receipt_request");
    acceptance.HasIndex(x => new { x.FirmId, x.PbcUploadIntentId });
    acceptance.ToTable("pbc_acceptance_receipts", t => t.HasCheckConstraint("ck_pbc_acceptance_receipt_values",
      "accepted_request_revision >= 1 AND source_request_revision >= 1 AND upload_intent_revision >= 1 AND byte_count > 0 AND " +
      "length(trim(file_name)) BETWEEN 1 AND 255 AND length(trim(provider_identity)) BETWEEN 1 AND 1000 AND " +
      "content_sha256_hex ~ '^[0-9a-f]{64}$' AND provider_receipt_digest ~ '^[0-9a-f]{64}$' AND " +
      "source_digest ~ '^[0-9a-f]{64}$'"));
    acceptance.HasOne<PbcRequest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcRequestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    acceptance.HasOne<PbcUploadIntent>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcUploadIntentId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    acceptance.HasOne<AuditSphereOps.Domain.Completion.DurableOperation>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.TransferOperationId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    acceptance.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReviewerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    acceptance.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.UploaderUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var registration = b.Entity<PbcDocumentRegistration>();
    registration.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_pbc_document_registrations_firm_id_id");
    registration.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_pbc_document_registrations_scope_id");
    registration.Property(x => x.ProviderIdentity).HasMaxLength(1000);
    registration.Property(x => x.BindingSha256Hex).HasMaxLength(64);
    registration.HasIndex(x => new { x.FirmId, x.PbcUploadIntentId }).IsUnique()
      .HasDatabaseName("ux_pbc_document_registration_upload");
    registration.HasIndex(x => new { x.FirmId, x.DocumentReferenceId }).IsUnique()
      .HasDatabaseName("ux_pbc_document_registration_reference");
    registration.HasIndex(x => new { x.FirmId, x.DocumentSnapshotId }).IsUnique()
      .HasDatabaseName("ux_pbc_document_registration_snapshot");
    registration.ToTable("pbc_document_registrations", t => t.HasCheckConstraint("ck_pbc_document_registration_values",
      "length(trim(provider_identity)) BETWEEN 1 AND 1000 AND binding_sha256_hex ~ '^[0-9a-f]{64}$'"));
    registration.HasOne<PbcUploadIntent>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcUploadIntentId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    registration.HasOne<DocumentReference>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.DocumentReferenceId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    registration.HasOne<DocumentSnapshot>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.DocumentSnapshotId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    registration.HasOne<AuditSphereOps.Domain.Completion.DurableOperation>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.TransferOperationId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var disposition = b.Entity<PbcStorageDisposition>();
    disposition.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_pbc_storage_dispositions_scope_id");
    disposition.Property(x => x.ReadSource).HasMaxLength(32);
    disposition.Property(x => x.LocalCopy).HasMaxLength(16);
    disposition.HasIndex(x => new { x.FirmId, x.PbcUploadIntentId }).IsUnique()
      .HasDatabaseName("ux_pbc_storage_disposition_upload");
    disposition.HasIndex(x => new { x.FirmId, x.PbcDocumentRegistrationId }).IsUnique()
      .HasDatabaseName("ux_pbc_storage_disposition_registration");
    disposition.ToTable("pbc_storage_dispositions", t => t.HasCheckConstraint("ck_pbc_storage_disposition_values",
      "read_source IN ('LOCAL_STAGED','SELECTED_SITE') AND local_copy IN ('RETAINED','RELEASED') AND revision >= 1 AND " +
      "NOT (read_source = 'LOCAL_STAGED' AND local_copy = 'RELEASED')"));
    disposition.HasOne<PbcUploadIntent>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcUploadIntentId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    disposition.HasOne<PbcDocumentRegistration>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcDocumentRegistrationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var storageEvent = b.Entity<PbcStorageEvent>();
    storageEvent.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_pbc_storage_events_scope_id");
    storageEvent.Property(x => x.Kind).HasMaxLength(32);
    storageEvent.Property(x => x.ReadSource).HasMaxLength(32);
    storageEvent.Property(x => x.LocalCopy).HasMaxLength(16);
    storageEvent.Property(x => x.Reason).HasMaxLength(500);
    storageEvent.HasIndex(x => new { x.FirmId, x.PbcStorageDispositionId, x.Revision }).IsUnique()
      .HasDatabaseName("ux_pbc_storage_event_revision");
    storageEvent.ToTable("pbc_storage_events", t => t.HasCheckConstraint("ck_pbc_storage_event_values",
      "((kind = 'REMOTE_READ_ACTIVATED' AND read_source = 'SELECTED_SITE' AND local_copy = 'RETAINED') OR " +
      "(kind = 'REMOTE_READ_REVERTED' AND read_source = 'LOCAL_STAGED' AND local_copy = 'RETAINED') OR " +
      "(kind = 'LOCAL_COPY_RELEASED' AND read_source = 'SELECTED_SITE' AND local_copy = 'RELEASED')) AND revision >= 1 AND " +
      "length(trim(reason)) BETWEEN 1 AND 500 AND (actor_user_id IS NOT NULL OR operation_id IS NOT NULL)"));
    storageEvent.HasOne<PbcStorageDisposition>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcStorageDispositionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    storageEvent.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    storageEvent.HasOne<AuditSphereOps.Domain.Completion.DurableOperation>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.OperationId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var storageReceipt = b.Entity<PbcStorageCommandReceipt>();
    storageReceipt.ToTable("pbc_storage_command_receipts", t => t.HasCheckConstraint("ck_pbc_storage_command_receipt_values",
      "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
      "request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND " +
      "((action IN ('ACTIVATE_REMOTE_READ','REVERT_TO_LOCAL_READ','RELEASE_LOCAL_COPY') AND " +
      "pbc_storage_event_id IS NOT NULL AND pbc_document_registration_id IS NULL) OR " +
      "(action = 'BACKFILL_REGISTRATION' AND pbc_document_registration_id IS NOT NULL AND pbc_storage_event_id IS NULL)) AND " +
      "length(input_json) BETWEEN 1 AND 4000 AND length(preview_json) BETWEEN 1 AND 100000 AND length(outcome_json) BETWEEN 1 AND 4000 AND " +
      "jsonb_typeof(input_json::jsonb) = 'object' AND jsonb_typeof(preview_json::jsonb) = 'object' AND " +
      "jsonb_typeof(outcome_json::jsonb) = 'object'"));
    storageReceipt.Property(x => x.Action).HasMaxLength(40);
    storageReceipt.Property(x => x.RequestHash).HasMaxLength(64);
    storageReceipt.Property(x => x.ReviewBasis).HasMaxLength(64);
    storageReceipt.Property(x => x.InputJson).HasMaxLength(PbcStorageCommandReceipt.MaximumInputLength);
    storageReceipt.Property(x => x.PreviewJson).HasColumnType("text");
    storageReceipt.Property(x => x.OutcomeJson).HasMaxLength(4000);
    storageReceipt.HasIndex(x => new { x.FirmId, x.ActorId, x.RequestId }).IsUnique()
      .HasDatabaseName("ux_pbc_storage_receipt_actor_request");
    storageReceipt.HasIndex(x => new { x.FirmId, x.PbcStorageEventId }).IsUnique()
      .HasFilter("pbc_storage_event_id IS NOT NULL").HasDatabaseName("ux_pbc_storage_receipt_event");
    storageReceipt.HasIndex(x => new { x.FirmId, x.PbcDocumentRegistrationId }).IsUnique()
      .HasFilter("pbc_document_registration_id IS NOT NULL").HasDatabaseName("ux_pbc_storage_receipt_registration");
    storageReceipt.HasOne<PbcUploadIntent>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcUploadIntentId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    storageReceipt.HasOne<PbcStorageEvent>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcStorageEventId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    storageReceipt.HasOne<PbcDocumentRegistration>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcDocumentRegistrationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    storageReceipt.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorId })
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
    intent.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcRequestId })
      .HasDatabaseName("IX_pbc_upload_intents_firm_id_client_id_engagement_id_pbc_requ~");
    intent.ToTable("pbc_upload_intents", t => t.HasCheckConstraint("ck_pbc_upload_intent_values",
      "state IN ('STARTED','CHUNKING','STAGED','RECEIVED','FAILED','EXPIRED') AND revision >= 1 AND request_revision >= 0 AND length(trim(file_name)) > 0 AND length(trim(content_type)) > 0 AND declared_byte_count > 0 AND declared_byte_count <= 262144000 AND received_byte_count >= 0 AND received_byte_count <= declared_byte_count AND declared_sha256_hex ~ '^[0-9a-f]{64}$' AND capability_hash ~ '^[0-9a-f]{64}$' AND expires_at > created_at AND ((state = 'STAGED' AND transfer_operation_id IS NOT NULL) OR state NOT IN ('STAGED')) AND ((state = 'RECEIVED' AND completed_at IS NOT NULL AND final_sha256_hex = declared_sha256_hex AND provider_registered_at IS NOT NULL AND provider_receipt_digest ~ '^[0-9a-f]{64}$' AND transfer_operation_id IS NOT NULL) OR state <> 'RECEIVED')"));
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
    communication.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_pbc_communications_scope_id");
    communication.Property(x => x.Kind).HasMaxLength(32);
    communication.Property(x => x.Body).HasMaxLength(4000);
    communication.Property(x => x.RecipientEmail).HasMaxLength(320);
    communication.Property(x => x.Subject).HasMaxLength(300);
    communication.Property(x => x.PortalUrl).HasMaxLength(2000);
    communication.Property(x => x.DeliveryState).HasMaxLength(16);
    communication.Property(x => x.ProviderCorrelationId).HasMaxLength(500);
    communication.Property(x => x.ClientRequestSha256).HasMaxLength(64);
    communication.HasIndex(x => new { x.FirmId, x.PbcRequestId, x.CreatedAt });
    communication.HasIndex(x => new { x.FirmId, x.AuthorUserId });
    communication.HasIndex(x => new { x.FirmId, x.AuthorUserId, x.ClientRequestId })
      .IsUnique().HasFilter("kind = 'CLIENT_MESSAGE' AND client_request_id IS NOT NULL")
      .HasDatabaseName("UX_pbc_communications_client_reply_request");
    communication.ToTable("pbc_communications", t => t.HasCheckConstraint("ck_pbc_communication_values",
      "kind IN ('REQUEST','STAFF_MESSAGE','CLIENT_MESSAGE','CLIENT_ACTION','EMAIL') AND length(trim(body)) > 0 AND (delivery_state IS NULL OR delivery_state IN ('QUEUED','SENT','FAILED','UNCERTAIN')) AND ((kind = 'EMAIL' AND recipient_email IS NOT NULL AND subject IS NOT NULL AND portal_url IS NOT NULL AND delivery_state IS NOT NULL) OR (kind <> 'EMAIL' AND recipient_email IS NULL AND subject IS NULL AND portal_url IS NULL AND delivery_state IS NULL)) AND ((client_request_id IS NULL AND client_request_sha256 IS NULL) OR (kind = 'CLIENT_MESSAGE' AND client_request_id IS NOT NULL AND client_request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND client_request_sha256 IS NOT NULL AND client_request_sha256 ~ '^[0-9a-f]{64}$'))"));
    communication.HasIndex(x => new { x.FirmId, x.DeliveredAt, x.PbcRequestId })
      .HasFilter("kind = 'EMAIL' AND delivery_state = 'SENT' AND delivered_at IS NOT NULL")
      .HasDatabaseName("IX_pbc_communications_sent_delivery");
    communication.HasOne<PbcRequest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcRequestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    communication.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.AuthorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var reminder = b.Entity<PbcReminderReceipt>();
    reminder.Property(x => x.ActionKey).HasMaxLength(64);
    reminder.HasIndex(x => new { x.FirmId, x.RuleRevisionId, x.PbcRequestId, x.ActionKey })
      .IsUnique().HasDatabaseName("UX_pbc_reminder_receipts_action");
    reminder.HasIndex(x => new { x.FirmId, x.EngagementId, x.CreatedAt });
    reminder.HasOne<PbcRequest>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcRequestId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    reminder.HasOne<PbcCommunication>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, CommunicationId = x.CommunicationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    reminder.ToTable("pbc_reminder_receipts", t => t.HasCheckConstraint("ck_pbc_reminder_receipt_values",
      "rule_revision_id <> '00000000-0000-0000-0000-000000000000'::uuid AND action_key ~ '^(PRE_DUE:[0-9]{4}-[0-9]{2}-[0-9]{2}|OVERDUE:[0-9]{1,6})$'"));
  }
}
