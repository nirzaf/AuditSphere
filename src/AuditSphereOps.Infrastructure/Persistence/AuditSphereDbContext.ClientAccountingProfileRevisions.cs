// Immutable historical snapshots for the existing mutable client accounting profile head.
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientAccountingProfileRevisions(ModelBuilder b)
  {
    var profile = b.Entity<ClientAccountingProfile>();
    profile.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id })
      .HasName("ak_client_accounting_profile_scope_id");

    var revision = b.Entity<ClientAccountingProfileRevisionRecord>();
    revision.ToTable("client_accounting_profile_revisions", t =>
      t.HasCheckConstraint("ck_client_accounting_profile_revision_values",
        "revision > 0 AND length(trim(status)) > 0 AND length(trim(jurisdiction)) > 0" +
        " AND functional_currency ~ '^[A-Z]{3}$'" +
        " AND fiscal_year_start_month BETWEEN 1 AND 12 AND fiscal_year_start_day BETWEEN 1 AND 31" +
        " AND length(trim(source_system)) > 0" +
        " AND source_mode IN ('EXTERNAL_SOURCE', 'NATIVE_BOOKKEEPING')" +
        " AND (reason IS NULL OR length(trim(reason)) BETWEEN 1 AND 2000)"));
    revision.HasKey(x => x.Id);
    revision.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.ProfileId, x.Id })
      .HasName("ak_client_accounting_profile_revision_scope_id");
    revision.Property(x => x.Status).HasMaxLength(30);
    revision.Property(x => x.Jurisdiction).HasMaxLength(100);
    revision.Property(x => x.FunctionalCurrency).HasMaxLength(3);
    revision.Property(x => x.SourceSystem).HasMaxLength(100);
    revision.Property(x => x.SourceSystemIdentifier).HasMaxLength(200);
    revision.Property(x => x.SourceMode).HasMaxLength(30);
    revision.Property(x => x.Reason).HasMaxLength(2000);
    revision.HasIndex(x => new { x.FirmId, x.ClientId, x.ProfileId, x.Revision }).IsUnique()
      .HasDatabaseName("ux_client_accounting_profile_revision");
    revision.HasOne<ClientAccountingProfile>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.ProfileId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id })
      .OnDelete(DeleteBehavior.Restrict)
      .HasConstraintName("fk_client_accounting_profile_revision_profile");
    revision.HasOne<ClientAccountingProfileRevisionRecord>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.ProfileId, x.PreviousRevisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.ProfileId, x.Id })
      .OnDelete(DeleteBehavior.Restrict)
      .HasConstraintName("fk_client_accounting_profile_revision_previous");
    revision.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict)
      .HasConstraintName("fk_client_accounting_profile_revision_created_by");
  }
}
