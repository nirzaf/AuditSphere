using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientSalesCreditNotes(ModelBuilder b)
  {
    var s = b.Entity<ClientSalesCreditNoteSubmission>();
    s.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id });
    s.HasIndex(x => new { x.FirmId, x.ClientId, x.CreatedByUserId, x.CommandId }).IsUnique();
    s.HasIndex(x => new { x.FirmId, x.ClientId, x.CreditNoteId }).IsUnique();
    s.HasIndex(x => new { x.FirmId, x.ClientId, x.CreditNoteReference }).IsUnique();
    s.HasIndex(x => new { x.FirmId, x.ClientId, x.JournalId, x.JournalSubmittedRevision }).IsUnique();
    s.Property(x => x.CreditNoteReference).HasMaxLength(100);
    s.Property(x => x.Currency).HasMaxLength(3);
    s.Property(x => x.Amount).HasPrecision(19, 6);
    s.Property(x => x.IntentHash).HasMaxLength(64);
    s.Property(x => x.ManifestHash).HasMaxLength(64);
    s.Property(x => x.PreviewDigest).HasMaxLength(64);
    s.Property(x => x.Reason).HasMaxLength(2000);
    s.Property(x => x.SourceBasis).HasMaxLength(2000);
    s.Property(x => x.SourceReceiptHash).HasMaxLength(64);
    s.Property(x => x.ManifestJson).HasMaxLength(500000);
    s.HasOne<ClientSalesInvoiceSubmission>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.OriginalSubmissionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    s.HasOne<ClientSalesInvoiceOpenItem>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.OriginalOpenItemId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    s.HasOne<ClientBookkeepingCounterparty>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, Id = x.CustomerId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    s.HasOne<ClientReportingPeriod>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    s.HasOne<ClientOperationalJournal>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.JournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    s.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, UserId = x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    s.ToTable("client_sales_credit_note_submissions", t => t.HasCheckConstraint("ck_client_sales_credit_submission",
      "credit_note_id<>'00000000-0000-0000-0000-000000000000'::uuid AND length(trim(credit_note_reference))>0 AND " +
      "original_invoice_id<>'00000000-0000-0000-0000-000000000000'::uuid AND amount>0 AND currency ~ '^[A-Z]{3}$' AND " +
      "journal_submitted_revision>0 AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND " +
      "intent_hash ~ '^[a-f0-9]{64}$' AND manifest_hash ~ '^[a-f0-9]{64}$' AND preview_digest ~ '^[a-f0-9]{64}$' AND " +
      "length(trim(reason))>0 AND ((source_receipt_id IS NULL AND source_receipt_hash IS NULL AND length(trim(source_basis))>0) OR " +
      "(source_receipt_id IS NOT NULL AND source_receipt_hash ~ '^[a-f0-9]{64}$')) AND length(manifest_json)>0"));

    var d = b.Entity<ClientSalesCreditNoteDecision>();
    d.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id });
    d.HasIndex(x => new { x.FirmId, x.ClientId, x.SubmissionId }).IsUnique();
    d.HasIndex(x => new { x.FirmId, x.ClientId, x.ActorUserId, x.CommandId }).IsUnique();
    d.Property(x => x.Decision).HasMaxLength(10);
    d.Property(x => x.Reason).HasMaxLength(2000);
    d.Property(x => x.IntentHash).HasMaxLength(64);
    d.Property(x => x.PreviewDigest).HasMaxLength(64);
    d.Property(x => x.ReviewContextJson).HasMaxLength(500000);
    d.HasOne<ClientSalesCreditNoteSubmission>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.SubmissionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    d.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, UserId = x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    d.ToTable("client_sales_credit_note_decisions", t => t.HasCheckConstraint("ck_client_sales_credit_decision",
      "decision IN ('APPROVE','RETURN') AND length(trim(reason))>0 AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND " +
      "intent_hash ~ '^[a-f0-9]{64}$' AND preview_digest ~ '^[a-f0-9]{64}$' AND length(review_context_json)>0"));

    var line = b.Entity<ClientSalesCreditNoteLine>();
    line.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id });
    line.HasIndex(x => new { x.FirmId, x.ClientId, x.SubmissionId, x.OriginalLineNumber }).IsUnique();
    line.Property(x => x.RevenueAccountCode).HasMaxLength(100);
    line.Property(x => x.Amount).HasPrecision(19, 6);
    line.HasOne<ClientSalesCreditNoteSubmission>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.SubmissionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    line.HasOne<ClientAccount>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.RevenueAccountId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, Id = x.Id }).OnDelete(DeleteBehavior.Restrict);
    line.ToTable("client_sales_credit_note_lines", t => t.HasCheckConstraint("ck_client_sales_credit_line",
      "original_line_number>0 AND amount>0 AND length(trim(revenue_account_code))>0"));

    var open = b.Entity<ClientSalesCreditNoteOpenItem>();
    open.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id });
    open.HasIndex(x => new { x.FirmId, x.ClientId, x.CreditNoteId }).IsUnique();
    open.HasIndex(x => new { x.FirmId, x.ClientId, x.SubmissionId }).IsUnique();
    open.HasIndex(x => new { x.FirmId, x.ClientId, x.JournalId }).IsUnique();
    open.Property(x => x.Currency).HasMaxLength(3);
    open.Property(x => x.Direction).HasMaxLength(10);
    open.Property(x => x.OriginalAmount).HasPrecision(19, 6);
    open.HasOne<ClientSalesCreditNoteSubmission>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.SubmissionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    open.HasOne<ClientOperationalJournal>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.JournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    open.HasOne<ClientBookkeepingCounterparty>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, Id = x.CustomerId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    open.ToTable("client_sales_credit_note_open_items", t => t.HasCheckConstraint("ck_client_sales_credit_open_item",
      "original_amount>0 AND direction='CREDIT' AND currency ~ '^[A-Z]{3}$'"));
  }
}
