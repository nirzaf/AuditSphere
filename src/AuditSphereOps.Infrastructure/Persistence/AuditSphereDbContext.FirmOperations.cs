using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

// Technical library, staff cost rates and firm operating expenses.
public sealed partial class AuditSphereDbContext
{
  private static void ConfigureFirmOperations(ModelBuilder b)
  {
    b.Entity<TechnicalLibraryDocument>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.Code }).IsUnique();
      e.ToTable("technical_library_documents", t => t.HasCheckConstraint("ck_technical_library_document_values",
        "category IN ('IFRS','ISA','FIRM_GUIDANCE') AND audience IN ('ALL_STAFF','PARTNERS_MANAGERS') AND length(code) BETWEEN 1 AND 40 AND length(title) > 0"));
    });
    b.Entity<TechnicalLibraryVersion>(e =>
    {
      e.HasIndex(x => new { x.DocumentId, x.Version }).IsUnique();
      e.HasIndex(x => x.DocumentId).IsUnique().HasFilter("status = 'PUBLISHED'").HasDatabaseName("ix_technical_library_versions_single_published");
      e.HasIndex(x => x.DocumentId).IsUnique().HasFilter("status = 'DRAFT'").HasDatabaseName("ix_technical_library_versions_single_draft");
      e.HasOne<TechnicalLibraryDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("technical_library_versions", t => t.HasCheckConstraint("ck_technical_library_version_values",
        "version >= 1 AND status IN ('DRAFT','PUBLISHED','SUPERSEDED') AND length(content_sha256) = 64 AND length(source_reference) > 0 AND ((status = 'DRAFT') = (approved_by_user_id IS NULL)) AND (approved_by_user_id IS NULL OR approved_by_user_id <> prepared_by_user_id)"));
    });
    b.Entity<StaffCostRate>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.UserId, x.EffectiveFrom });
      e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
      e.Property(x => x.HourlyCost).HasPrecision(18, 2);
      e.ToTable("staff_cost_rates", t => t.HasCheckConstraint("ck_staff_cost_rate_values", "hourly_cost > 0 AND currency ~ '^[A-Z]{3}$'"));
    });
    b.Entity<FirmExpense>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.ExpenseDate });
      e.HasOne<FirmAccount>().WithMany().HasForeignKey(x => x.ExpenseAccountId).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<FirmAccount>().WithMany().HasForeignKey(x => x.PaymentAccountId).OnDelete(DeleteBehavior.Restrict);
      e.Property(x => x.Amount).HasPrecision(18, 2);
      e.ToTable("firm_expenses", t => t.HasCheckConstraint("ck_firm_expense_values",
        "category IN ('RENT','SALARIES','PETTY_CASH','UTILITIES','OTHER') AND status IN ('DRAFT','SUBMITTED','APPROVED','POSTED','REJECTED') AND amount > 0 AND currency ~ '^[A-Z]{3}$' AND length(evidence_sha256) = 64 AND octet_length(evidence_content) BETWEEN 1 AND 5242880 AND (reviewed_by_user_id IS NULL OR reviewed_by_user_id <> prepared_by_user_id) AND ((status = 'POSTED') = (posting_id IS NOT NULL))"));
    });
  }
}
