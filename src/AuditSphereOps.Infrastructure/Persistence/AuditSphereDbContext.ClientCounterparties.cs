using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientCounterparties(ModelBuilder b)
  {
    var party = b.Entity<ClientBookkeepingCounterparty>();
    party.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id });
    party.Property(x => x.LegalName).HasMaxLength(300);
    party.Property(x => x.NormalizedLegalName).HasMaxLength(300);
    party.Property(x => x.DisplayName).HasMaxLength(300);
    party.Property(x => x.Role).HasMaxLength(20);
    party.Property(x => x.Address).HasMaxLength(2000);
    party.Property(x => x.Country).HasMaxLength(2);
    party.Property(x => x.TaxIdentifier).HasMaxLength(200);
    party.Property(x => x.ContactDetails).HasMaxLength(1000);
    party.Property(x => x.PaymentTerms).HasMaxLength(500);
    party.Property(x => x.DefaultCurrency).HasMaxLength(3);
    party.Property(x => x.ExternalSystem).HasMaxLength(100);
    party.Property(x => x.ExternalReference).HasMaxLength(200);
    party.Property(x => x.NormalizedExternalIdentity).HasMaxLength(64);
    party.HasIndex(x => new { x.FirmId, x.ClientId, x.NormalizedExternalIdentity }).IsUnique();
    party.HasIndex(x => new { x.FirmId, x.ClientId, x.NormalizedLegalName, x.Country });
    party.ToTable("client_bookkeeping_counterparties", t => t.HasCheckConstraint("ck_client_bookkeeping_counterparty",
      "length(trim(legal_name))>0 AND length(trim(display_name))>0 AND length(trim(normalized_legal_name))>0" +
      " AND role IN ('CUSTOMER','SUPPLIER','BOTH') AND country ~ '^[A-Z]{2}$'" +
      " AND (default_currency='' OR default_currency ~ '^[A-Z]{3}$')" +
      " AND ((external_system='' AND external_reference='' AND normalized_external_identity IS NULL)" +
      " OR (length(trim(external_system))>0 AND length(trim(external_reference))>0 AND normalized_external_identity ~ '^[a-f0-9]{64}$'))"));
    party.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    party.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
