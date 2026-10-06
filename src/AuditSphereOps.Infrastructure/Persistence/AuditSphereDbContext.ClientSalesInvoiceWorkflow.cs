using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;
public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientSalesInvoiceWorkflow(ModelBuilder b)
  {
    var s=b.Entity<ClientSalesInvoiceSubmission>();
    s.HasAlternateKey(x=>new{x.FirmId,x.ClientId,x.Id});
    s.HasIndex(x=>new{x.FirmId,x.ClientId,x.CreatedByUserId,x.CommandId}).IsUnique();
    s.HasIndex(x=>new{x.FirmId,x.ClientId,x.InvoiceId,x.DraftRevision}).IsUnique();
    s.HasIndex(x=>new{x.FirmId,x.ClientId,x.JournalId,x.JournalSubmittedRevision}).IsUnique();
    s.Property(x=>x.IntentHash).HasMaxLength(64);s.Property(x=>x.ManifestHash).HasMaxLength(64);s.Property(x=>x.ManifestJson).HasMaxLength(500000);
    s.HasOne<ClientSalesInvoiceDraft>().WithMany().HasForeignKey(x=>new{x.FirmId,x.ClientId,x.InvoiceId,Id=x.DraftId,Revision=x.DraftRevision}).HasPrincipalKey(x=>new{x.FirmId,x.ClientId,x.InvoiceId,x.Id,x.Revision}).OnDelete(DeleteBehavior.Restrict);
    s.HasOne<ClientOperationalJournal>().WithMany().HasForeignKey(x=>new{x.FirmId,x.ClientId,x.JournalId}).HasPrincipalKey(x=>new{x.FirmId,x.ClientId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    s.HasOne<AppUser>().WithMany().HasForeignKey(x=>new{x.FirmId,UserId=x.CreatedByUserId}).HasPrincipalKey(x=>new{x.FirmId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    s.ToTable("client_sales_invoice_submissions",t=>t.HasCheckConstraint("ck_client_sales_submission","draft_revision>0 AND journal_submitted_revision>0 AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND intent_hash ~ '^[a-f0-9]{64}$' AND manifest_hash ~ '^[a-f0-9]{64}$' AND length(manifest_json)>0"));
    var d=b.Entity<ClientSalesInvoiceDecision>();d.HasIndex(x=>new{x.FirmId,x.ClientId,x.SubmissionId}).IsUnique();d.HasIndex(x=>new{x.FirmId,x.ClientId,x.ActorUserId,x.CommandId}).IsUnique();
    d.Property(x=>x.ReviewContextJson).HasMaxLength(500000);d.Property(x=>x.Decision).HasMaxLength(10);d.Property(x=>x.Reason).HasMaxLength(2000);d.Property(x=>x.IntentHash).HasMaxLength(64);d.Property(x=>x.PreviewDigest).HasMaxLength(64);
    d.HasOne<ClientSalesInvoiceSubmission>().WithMany().HasForeignKey(x=>new{x.FirmId,x.ClientId,x.SubmissionId}).HasPrincipalKey(x=>new{x.FirmId,x.ClientId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    d.HasOne<AppUser>().WithMany().HasForeignKey(x=>new{x.FirmId,UserId=x.ActorUserId}).HasPrincipalKey(x=>new{x.FirmId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    d.ToTable("client_sales_invoice_decisions",t=>t.HasCheckConstraint("ck_client_sales_decision","decision IN ('APPROVE','RETURN') AND length(trim(reason))>0 AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND intent_hash ~ '^[a-f0-9]{64}$' AND preview_digest ~ '^[a-f0-9]{64}$'"));
    var o=b.Entity<ClientSalesInvoiceOpenItem>();o.HasIndex(x=>new{x.FirmId,x.ClientId,x.InvoiceId}).IsUnique();o.HasIndex(x=>new{x.FirmId,x.ClientId,x.SubmissionId}).IsUnique();o.HasIndex(x=>new{x.FirmId,x.ClientId,x.JournalId}).IsUnique();
    o.Property(x=>x.OriginalAmount).HasPrecision(19,6);o.Property(x=>x.Currency).HasMaxLength(3);
    o.HasOne<ClientSalesInvoiceSubmission>().WithMany().HasForeignKey(x=>new{x.FirmId,x.ClientId,x.SubmissionId}).HasPrincipalKey(x=>new{x.FirmId,x.ClientId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    o.HasOne<ClientOperationalJournal>().WithMany().HasForeignKey(x=>new{x.FirmId,x.ClientId,x.JournalId}).HasPrincipalKey(x=>new{x.FirmId,x.ClientId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    o.HasOne<ClientBookkeepingCounterparty>().WithMany().HasForeignKey(x=>new{x.FirmId,x.ClientId,Id=x.CustomerId}).HasPrincipalKey(x=>new{x.FirmId,x.ClientId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    o.ToTable("client_sales_invoice_open_items",t=>t.HasCheckConstraint("ck_client_sales_open_item","original_amount>0 AND currency ~ '^[A-Z]{3}$'"));
  }
}
