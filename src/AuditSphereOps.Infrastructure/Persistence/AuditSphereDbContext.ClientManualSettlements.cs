using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientManualSettlementOrigins(ModelBuilder b)
  {
    var origin = b.Entity<ClientManualSettlementOrigin>();
    origin.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id });
    origin.HasIndex(x => new { x.FirmId, x.ClientId, x.JournalId }).IsUnique();
    origin.HasIndex(x => new { x.FirmId, x.ClientId, x.SourceKind, x.Reference }).IsUnique();
    origin.HasIndex(x => new { x.FirmId, x.ClientId, x.CreatedByUserId, x.CommandId }).IsUnique();
    origin.Property(x => x.SourceKind).HasMaxLength(32);
    origin.Property(x => x.Amount).HasPrecision(19, 6);
    origin.Property(x => x.Reference).HasMaxLength(200);
    origin.Property(x => x.EvidenceReference).HasMaxLength(1000);
    origin.Property(x => x.IntentHash).HasMaxLength(64);
    origin.HasOne<ClientOperationalJournal>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.JournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    origin.HasOne<ClientBookkeepingCounterparty>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, Id = x.CounterpartyId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    origin.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, UserId = x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    origin.ToTable("client_manual_settlement_origins", t => t.HasCheckConstraint("ck_client_manual_settlement_origin",
      "source_kind IN ('SALES_RECEIPT','SUPPLIER_PAYMENT') AND amount>0 AND length(trim(reference))>0 AND " +
      "length(trim(evidence_reference))>0 AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND intent_hash ~ '^[a-f0-9]{64}$'"));
  }
}
