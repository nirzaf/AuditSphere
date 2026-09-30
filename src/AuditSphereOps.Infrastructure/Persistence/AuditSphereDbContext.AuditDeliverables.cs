using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

// Review notes, completion deliverables, Partner clearance, opinions, signatures, client review and confirmation criticality.
public sealed partial class AuditSphereDbContext
{
  private static void ConfigureAuditDeliverables(ModelBuilder b)
  {
    b.Entity<ProcedureReviewNote>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.ProcedureId, x.CreatedAt });
      e.HasOne<AuditProcedureResult>().WithMany().HasForeignKey(x => x.ResultId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("procedure_review_notes", t => t.HasCheckConstraint("ck_procedure_review_note_values",
        "field IN ('WORK_PERFORMED','CONCLUSION') AND length(excerpt) BETWEEN 1 AND 500 AND length(body) BETWEEN 1 AND 4000 AND start_offset >= 0"));
    });
    b.Entity<ProcedureReviewNoteEvent>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.NoteId, x.CreatedAt });
      e.HasOne<ProcedureReviewNote>().WithMany().HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("procedure_review_note_events", t => t.HasCheckConstraint("ck_procedure_review_note_event_values",
        "kind IN ('RESPONSE','RESOLVED','REOPENED') AND length(body) > 0"));
    });
    b.Entity<AuditDeliverable>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.EngagementId, x.Kind, x.Version }).IsUnique().HasFilter("signed_from_deliverable_id IS NULL");
      e.HasIndex(x => new { x.FirmId, x.SignedFromDeliverableId }).IsUnique().HasFilter("signed_from_deliverable_id IS NOT NULL");
      e.Property(x => x.InputSummaryJson).HasColumnType("jsonb");
      e.ToTable("audit_deliverables", t => t.HasCheckConstraint("ck_audit_deliverable_values",
        "kind IN ('SUMMARY_REVIEW_MEMORANDUM','AUDIT_FINDINGS_REPORT','MANAGEMENT_LETTER','INDEPENDENT_AUDITORS_REPORT','HOLDING_LETTER','REPRESENTATION_LETTER') AND version >= 1 AND length(input_digest) = 64 AND length(content_sha256) = 64 AND octet_length(content) > 0"));
    });
    b.Entity<PartnerCompletionClearance>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.EngagementId, x.ClearedAt });
      e.HasOne<AuditDeliverable>().WithMany().HasForeignKey(x => x.SummaryReviewMemorandumId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("partner_completion_clearances", t => t.HasCheckConstraint("ck_partner_completion_clearance_values",
        "length(key_risk_areas_comment) > 0 AND length(financial_statement_notes_comment) > 0"));
    });
    b.Entity<AuditOpinionDecision>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.EngagementId, x.DecidedAt });
      e.HasOne<PartnerCompletionClearance>().WithMany().HasForeignKey(x => x.PartnerClearanceId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("audit_opinion_decisions", t => t.HasCheckConstraint("ck_audit_opinion_decision_values",
        "opinion_type IN ('UNMODIFIED','QUALIFIED','ADVERSE','DISCLAIMER') AND ((opinion_type = 'UNMODIFIED' AND basis_text IS NULL) OR (opinion_type <> 'UNMODIFIED' AND length(basis_text) > 0)) AND (opinion_type <> 'QUALIFIED' OR length(focus_area) > 0)"));
    });
    b.Entity<SignatureSpecimen>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.UserId }).IsUnique().HasFilter("revoked_at IS NULL");
      e.ToTable("signature_specimens", t => t.HasCheckConstraint("ck_signature_specimen_values",
        "length(sha256) = 64 AND octet_length(png_content) BETWEEN 1 AND 524288 AND width_pixels > 0 AND height_pixels > 0"));
    });
    b.Entity<SignatureApplication>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.SourceDeliverableId }).IsUnique();
      e.HasOne<SignatureSpecimen>().WithMany().HasForeignKey(x => x.SpecimenId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("signature_applications");
    });
    b.Entity<ClientDeliverableReview>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.DeliverableId }).IsUnique();
      e.HasOne<AuditDeliverable>().WithMany().HasForeignKey(x => x.DeliverableId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("client_deliverable_reviews", t => t.HasCheckConstraint("ck_client_deliverable_review_values",
        "(acknowledged_at IS NULL) = (acknowledged_sha256 IS NULL)"));
    });
    b.Entity<ClientDeliverableComment>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.ReviewId, x.CreatedAt });
      e.HasOne<ClientDeliverableReview>().WithMany().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("client_deliverable_comments", t => t.HasCheckConstraint("ck_client_deliverable_comment_values",
        "length(body) > 0 AND ((resolved_at IS NULL) = (resolution IS NULL))"));
    });
    b.Entity<ConfirmationCriticality>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.ConfirmationCaseId, x.SetAt });
      e.HasOne<AuditConfirmationCase>().WithMany().HasForeignKey(x => x.ConfirmationCaseId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("confirmation_criticalities", t => t.HasCheckConstraint("ck_confirmation_criticality_values", "length(rationale) > 0"));
    });
  }
}
