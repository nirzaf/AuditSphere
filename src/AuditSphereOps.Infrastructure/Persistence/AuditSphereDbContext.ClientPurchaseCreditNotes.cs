using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientPurchaseCreditNotes(ModelBuilder b)
  {
    var submission = b.Entity<ClientPurchaseCreditNoteSubmission>();
    submission.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id });
    submission.HasIndex(x => new { x.FirmId, x.ClientId, x.CreditNoteId }).IsUnique();
    submission.HasIndex(x => new { x.FirmId, x.ClientId, x.CreditNoteReference }).IsUnique();
    submission.HasIndex(x => new { x.FirmId, x.ClientId, x.CreatedByUserId, x.CommandId }).IsUnique();
    submission.HasIndex(x => new { x.FirmId, x.ClientId, x.JournalId, x.JournalSubmittedRevision }).IsUnique();
    submission.Property(x => x.CreditNoteReference).HasMaxLength(200);
    submission.Property(x => x.Currency).HasMaxLength(3);
    submission.Property(x => x.Amount).HasPrecision(19, 6);
    submission.Property(x => x.IntentHash).HasMaxLength(64);
    submission.Property(x => x.ManifestJson).HasMaxLength(500000);
    submission.Property(x => x.ManifestHash).HasMaxLength(64);
    submission.Property(x => x.PreviewDigest).HasMaxLength(64);
    submission.Property(x => x.Reason).HasMaxLength(2000);
    submission.Property(x => x.SourceBasis).HasMaxLength(2000);
    submission.Property(x => x.SourceReceiptHash).HasMaxLength(64);
    submission.HasOne<ClientBookkeepingCounterparty>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.SupplierId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    submission.HasOne<ClientReportingPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    submission.HasOne<ClientPurchaseInvoiceSubmission>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.OriginalSubmissionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    submission.HasOne<ClientPurchaseInvoiceOpenItem>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.OriginalOpenItemId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    submission.HasOne<ClientOperationalJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.JournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    submission.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    submission.ToTable("client_purchase_credit_note_submissions", t => t.HasCheckConstraint("ck_client_purchase_credit_note_submission",
      "journal_submitted_revision>0 AND length(trim(credit_note_reference))>0 AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND " +
      "amount>0 AND currency ~ '^[A-Z]{3}$' AND intent_hash ~ '^[a-f0-9]{64}$' AND manifest_hash ~ '^[a-f0-9]{64}$' AND " +
      "preview_digest ~ '^[a-f0-9]{64}$' AND length(trim(reason))>0 AND length(manifest_json)>0 AND " +
      "((original_invoice_id IS NULL AND original_submission_id IS NULL AND original_open_item_id IS NULL AND length(trim(source_basis))>0) OR " +
      "(original_invoice_id IS NOT NULL AND original_submission_id IS NOT NULL AND original_open_item_id IS NOT NULL)) AND " +
      "((source_receipt_id IS NULL AND source_receipt_hash IS NULL AND length(trim(source_basis))>0) OR " +
      "(source_receipt_id IS NOT NULL AND source_receipt_hash ~ '^[a-f0-9]{64}$'))"));

    var decision = b.Entity<ClientPurchaseCreditNoteDecision>();
    decision.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id });
    decision.HasIndex(x => new { x.FirmId, x.ClientId, x.SubmissionId }).IsUnique();
    decision.HasIndex(x => new { x.FirmId, x.ClientId, x.ActorUserId, x.CommandId }).IsUnique();
    decision.Property(x => x.Decision).HasMaxLength(10);
    decision.Property(x => x.IntentHash).HasMaxLength(64);
    decision.Property(x => x.Reason).HasMaxLength(2000);
    decision.Property(x => x.DuplicateResolutionReason).HasMaxLength(2000);
    decision.Property(x => x.PreviewDigest).HasMaxLength(64);
    decision.Property(x => x.ReviewContextJson).HasMaxLength(500000);
    decision.HasOne<ClientPurchaseCreditNoteSubmission>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.SubmissionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    decision.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    decision.ToTable("client_purchase_credit_note_decisions", t => t.HasCheckConstraint("ck_client_purchase_credit_note_decision",
      "decision IN ('APPROVE','RETURN') AND length(trim(reason))>0 AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND " +
      "intent_hash ~ '^[a-f0-9]{64}$' AND preview_digest ~ '^[a-f0-9]{64}$' AND length(review_context_json)>0"));

    var line = b.Entity<ClientPurchaseCreditNoteLine>();
    line.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id });
    line.HasIndex(x => new { x.FirmId, x.ClientId, x.SubmissionId, x.LineNumber }).IsUnique();
    line.Property(x => x.ExpenseAccountCode).HasMaxLength(100);
    line.Property(x => x.Amount).HasPrecision(19, 6);
    line.HasOne<ClientPurchaseCreditNoteSubmission>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.SubmissionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    line.HasOne<ClientAccount>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ExpenseAccountId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    line.ToTable("client_purchase_credit_note_lines", t => t.HasCheckConstraint("ck_client_purchase_credit_note_line",
      "line_number>0 AND (original_line_number IS NULL OR original_line_number>0) AND amount>0 AND length(trim(expense_account_code))>0"));

    var open = b.Entity<ClientPurchaseCreditNoteOpenItem>();
    open.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id });
    open.HasIndex(x => new { x.FirmId, x.ClientId, x.SubmissionId }).IsUnique();
    open.HasIndex(x => new { x.FirmId, x.ClientId, x.JournalId }).IsUnique();
    open.Property(x => x.Currency).HasMaxLength(3);
    open.Property(x => x.Direction).HasMaxLength(10);
    open.Property(x => x.OriginalAmount).HasPrecision(19, 6);
    open.HasOne<ClientPurchaseCreditNoteSubmission>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.SubmissionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    open.HasOne<ClientOperationalJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.JournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    open.HasOne<ClientBookkeepingCounterparty>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.SupplierId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    open.ToTable("client_purchase_credit_note_open_items", t => t.HasCheckConstraint("ck_client_purchase_credit_note_open_item",
      "direction='DEBIT' AND original_amount>0 AND currency ~ '^[A-Z]{3}$'"));
  }
}
