using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientPurchaseInvoices(ModelBuilder b)
  {
    var draft = b.Entity<ClientPurchaseInvoiceDraft>();
    draft.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.InvoiceId, x.Id, x.Revision });
    draft.HasIndex(x => new { x.FirmId, x.ClientId, x.InvoiceId, x.Revision }).IsUnique();
    draft.HasIndex(x => new { x.FirmId, x.ClientId, x.CreatedByUserId, x.CommandId }).IsUnique();
    draft.HasIndex(x => new { x.FirmId, x.ClientId, x.VoucherReference }).IsUnique().HasFilter("revision=1");
    draft.Property(x => x.IntentHash).HasMaxLength(64);
    draft.Property(x => x.SupplierInvoiceReference).HasMaxLength(200);
    draft.Property(x => x.NormalizedSupplierReference).HasMaxLength(200);
    draft.Property(x => x.VoucherReference).HasMaxLength(100);
    draft.Property(x => x.Currency).HasMaxLength(3);
    draft.Property(x => x.NetAmount).HasPrecision(19, 6);
    draft.Property(x => x.TaxAmount).HasPrecision(19, 6);
    draft.Property(x => x.GrossAmount).HasPrecision(19, 6);
    draft.Property(x => x.SnapshotJson).HasMaxLength(500000);
    draft.Property(x => x.SnapshotHash).HasMaxLength(64);
    draft.HasOne<ClientBookkeepingCounterparty>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.SupplierId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    draft.HasOne<ClientReportingPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    draft.HasOne<ClientChartVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ChartVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    draft.HasOne<ClientPurchaseInvoiceDraft>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.InvoiceId, x.PreviousRevisionId, x.PreviousRevision })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.InvoiceId, x.Id, x.Revision }).OnDelete(DeleteBehavior.Restrict);
    draft.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    draft.ToTable("client_purchase_invoice_drafts", t => t.HasCheckConstraint("ck_client_purchase_invoice_draft",
      "revision>0 AND invoice_id<>'00000000-0000-0000-0000-000000000000'::uuid AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND " +
      "length(trim(voucher_reference))>0 AND length(trim(supplier_invoice_reference))>0 AND length(trim(normalized_supplier_reference))>0 AND " +
      "receipt_date>=document_date AND accounting_date BETWEEN '0001-01-01' AND '9999-12-31' AND due_date>=document_date AND " +
      "currency ~ '^[A-Z]{3}$' AND net_amount>=0 AND tax_amount>=0 AND gross_amount=net_amount+tax_amount AND " +
      "intent_hash ~ '^[a-f0-9]{64}$' AND snapshot_hash ~ '^[a-f0-9]{64}$' AND length(snapshot_json)>0 AND " +
      "((revision=1 AND previous_revision_id IS NULL AND previous_revision IS NULL) OR (revision>1 AND previous_revision_id IS NOT NULL AND previous_revision=revision-1))"));

    var submission = b.Entity<ClientPurchaseInvoiceSubmission>();
    submission.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id });
    submission.HasIndex(x => new { x.FirmId, x.ClientId, x.CreatedByUserId, x.CommandId }).IsUnique();
    submission.HasIndex(x => new { x.FirmId, x.ClientId, x.JournalId, x.JournalSubmittedRevision }).IsUnique();
    submission.Property(x => x.SupplierInvoiceReference).HasMaxLength(200);
    submission.Property(x => x.NormalizedSupplierReference).HasMaxLength(200);
    submission.Property(x => x.IntentHash).HasMaxLength(64);
    submission.Property(x => x.ManifestHash).HasMaxLength(64);
    submission.Property(x => x.PreviewDigest).HasMaxLength(64);
    submission.Property(x => x.ManifestJson).HasMaxLength(500000);
    submission.Property(x => x.SourceBasis).HasMaxLength(2000);
    submission.Property(x => x.SourceReceiptHash).HasMaxLength(64);
    submission.HasOne<ClientPurchaseInvoiceDraft>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.InvoiceId, x.DraftId, x.DraftRevision })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.InvoiceId, x.Id, x.Revision }).OnDelete(DeleteBehavior.Restrict);
    submission.HasOne<ClientBookkeepingCounterparty>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.SupplierId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    submission.HasOne<ClientOperationalJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.JournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    submission.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    submission.ToTable("client_purchase_invoice_submissions", t => t.HasCheckConstraint("ck_client_purchase_invoice_submission",
      "journal_submitted_revision>0 AND length(trim(supplier_invoice_reference))>0 AND length(trim(normalized_supplier_reference))>0 AND " +
      "command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND intent_hash ~ '^[a-f0-9]{64}$' AND " +
      "manifest_hash ~ '^[a-f0-9]{64}$' AND preview_digest ~ '^[a-f0-9]{64}$' AND length(manifest_json)>0 AND " +
      "((source_receipt_id IS NULL AND source_receipt_hash IS NULL AND length(trim(source_basis))>0) OR " +
      "(source_receipt_id IS NOT NULL AND source_receipt_hash ~ '^[a-f0-9]{64}$'))"));

    var decision = b.Entity<ClientPurchaseInvoiceDecision>();
    decision.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id });
    decision.HasIndex(x => new { x.FirmId, x.ClientId, x.SubmissionId }).IsUnique();
    decision.HasIndex(x => new { x.FirmId, x.ClientId, x.ActorUserId, x.CommandId }).IsUnique();
    decision.Property(x => x.Decision).HasMaxLength(10);
    decision.Property(x => x.IntentHash).HasMaxLength(64);
    decision.Property(x => x.Reason).HasMaxLength(2000);
    decision.Property(x => x.DuplicateResolutionReason).HasMaxLength(2000);
    decision.Property(x => x.PreviewDigest).HasMaxLength(64);
    decision.Property(x => x.ReviewContextJson).HasMaxLength(500000);
    decision.HasOne<ClientPurchaseInvoiceSubmission>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.SubmissionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    decision.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    decision.ToTable("client_purchase_invoice_decisions", t => t.HasCheckConstraint("ck_client_purchase_invoice_decision",
      "decision IN ('APPROVE','RETURN') AND length(trim(reason))>0 AND " +
      "command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND intent_hash ~ '^[a-f0-9]{64}$' AND " +
      "preview_digest ~ '^[a-f0-9]{64}$' AND length(review_context_json)>0"));

    var open = b.Entity<ClientPurchaseInvoiceOpenItem>();
    open.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id });
    open.HasIndex(x => new { x.FirmId, x.ClientId, x.InvoiceId }).IsUnique();
    open.HasIndex(x => new { x.FirmId, x.ClientId, x.SubmissionId }).IsUnique();
    open.HasIndex(x => new { x.FirmId, x.ClientId, x.JournalId }).IsUnique();
    open.Property(x => x.Currency).HasMaxLength(3);
    open.Property(x => x.OriginalAmount).HasPrecision(19, 6);
    open.HasOne<ClientPurchaseInvoiceSubmission>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.SubmissionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    open.HasOne<ClientOperationalJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.JournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    open.HasOne<ClientBookkeepingCounterparty>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.SupplierId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    open.ToTable("client_purchase_invoice_open_items", t => t.HasCheckConstraint("ck_client_purchase_invoice_open_item",
      "original_amount>0 AND currency ~ '^[A-Z]{3}$'"));
  }
}
