using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientSalesInvoices(ModelBuilder b)
  {
    var draft = b.Entity<ClientSalesInvoiceDraft>();
    draft.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.InvoiceId, x.Id, x.Revision });
    draft.HasIndex(x => new { x.FirmId, x.ClientId, x.InvoiceId, x.Revision }).IsUnique();
    draft.HasIndex(x => new { x.FirmId, x.ClientId, x.CreatedByUserId, x.CommandId }).IsUnique();
    draft.HasIndex(x => new { x.FirmId, x.ClientId, x.DraftReference }).IsUnique().HasFilter("revision = 1");
    draft.Property(x => x.DraftReference).HasMaxLength(100);
    draft.Property(x => x.SourceReference).HasMaxLength(200);
    draft.Property(x => x.IntentHash).HasMaxLength(64);
    draft.Property(x => x.SnapshotHash).HasMaxLength(64);
    draft.Property(x => x.Currency).HasMaxLength(3);
    draft.Property(x => x.NetAmount).HasPrecision(19, 6);
    draft.Property(x => x.GrossAmount).HasPrecision(19, 6);
    // Text preserves the canonical decimal snapshot byte identity. Do not let jsonb reorder it.
    draft.Property(x => x.SnapshotJson).HasMaxLength(500000);
    draft.ToTable("client_sales_invoice_drafts", t => t.HasCheckConstraint("ck_client_sales_invoice_draft",
      "revision>0 AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid" +
      " AND invoice_id<>'00000000-0000-0000-0000-000000000000'::uuid AND length(trim(draft_reference))>0" +
      " AND intent_hash ~ '^[a-f0-9]{64}$' AND snapshot_hash ~ '^[a-f0-9]{64}$' AND currency ~ '^[A-Z]{3}$'" +
      " AND net_amount>=0 AND gross_amount=net_amount AND length(snapshot_json)>0" +
      " AND ((revision=1 AND previous_revision_id IS NULL AND previous_revision IS NULL)" +
      " OR (revision>1 AND previous_revision_id IS NOT NULL AND previous_revision=revision-1))"));
    draft.HasOne<ClientSalesInvoiceDraft>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.InvoiceId, x.PreviousRevisionId, x.PreviousRevision })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.InvoiceId, x.Id, x.Revision }).OnDelete(DeleteBehavior.Restrict);
    draft.HasOne<ClientBookkeepingCounterparty>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.CustomerId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    draft.HasOne<ClientReportingPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    draft.HasOne<ClientChartVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ChartVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    draft.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
