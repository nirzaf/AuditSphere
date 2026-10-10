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
      e.HasIndex(x => new { x.FirmId, x.PreparedByUserId, x.CreateRequestId }).IsUnique()
        .HasFilter("create_request_id IS NOT NULL");
      e.HasOne<FirmAccount>().WithMany().HasForeignKey(x => x.ExpenseAccountId).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<FirmAccount>().WithMany().HasForeignKey(x => x.PaymentAccountId).OnDelete(DeleteBehavior.Restrict);
      e.Property(x => x.Amount).HasPrecision(18, 2);
      e.Property(x => x.CreateRequestHash).HasMaxLength(64);
      e.ToTable("firm_expenses", t => t.HasCheckConstraint("ck_firm_expense_values",
        "category IN ('RENT','SALARIES','PETTY_CASH','UTILITIES','OTHER') AND status IN ('DRAFT','SUBMITTED','APPROVED','POSTED','REJECTED') AND amount > 0 AND currency ~ '^[A-Z]{3}$' AND length(evidence_sha256) = 64 AND octet_length(evidence_content) BETWEEN 1 AND 5242880 AND (reviewed_by_user_id IS NULL OR reviewed_by_user_id <> prepared_by_user_id) AND ((status = 'POSTED') = (posting_id IS NOT NULL)) AND ((create_request_id IS NULL AND create_request_hash IS NULL) OR (create_request_id IS NOT NULL AND create_request_hash ~ '^[0-9a-f]{64}$'))"));
    });
    b.Entity<FirmEndOfServiceTreatment>(e =>
    {
      e.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_firm_end_of_service_treatments_firm_id_id");
      e.HasIndex(x => new { x.FirmId, x.Version }).IsUnique();
      e.Property(x => x.MeasurementTreatment).HasMaxLength(4000);
      e.Property(x => x.AccountantName).HasMaxLength(200);
      e.Property(x => x.AccountantCredential).HasMaxLength(300);
      e.Property(x => x.ConfirmationNote).HasMaxLength(1000);
      e.HasOne<FirmAccount>().WithMany().HasForeignKey(x => new { x.FirmId, x.ProvisionAccountId })
        .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<FirmAccount>().WithMany().HasForeignKey(x => new { x.FirmId, x.ExpenseAccountId })
        .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.RecordedByUserId })
        .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ConfirmedByUserId })
        .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
      // Recorder and confirmer are always different people; a confirmed version carries its full confirmation evidence.
      e.ToTable("firm_end_of_service_treatments", t => t.HasCheckConstraint("ck_firm_end_of_service_treatment_values",
        "version >= 1 AND length(measurement_treatment) >= 20 AND length(accountant_name) > 0 AND length(accountant_credential) > 0 AND " +
        "provision_account_id <> expense_account_id AND status IN ('RECORDED','CONFIRMED') AND " +
        "((status = 'CONFIRMED') = (confirmed_by_user_id IS NOT NULL AND confirmed_at IS NOT NULL AND length(confirmation_note) >= 5)) AND " +
        "(confirmed_by_user_id IS NULL OR confirmed_by_user_id <> recorded_by_user_id)"));
    });
    b.Entity<FirmEndOfServiceAccrual>(e =>
    {
      // One entered basis per accrual journal.
      e.HasIndex(x => new { x.FirmId, x.JournalId }).IsUnique();
      e.HasIndex(x => new { x.FirmId, x.PeriodId });
      e.Property(x => x.Amount).HasPrecision(18, 2);
      e.Property(x => x.Method).HasMaxLength(2000);
      e.Property(x => x.Inputs).HasMaxLength(4000);
      e.Property(x => x.Reason).HasMaxLength(1000);
      e.HasOne<FirmJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.JournalId })
        .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<FirmEndOfServiceTreatment>().WithMany().HasForeignKey(x => new { x.FirmId, x.TreatmentId })
        .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<FirmPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.PeriodId })
        .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
        .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("firm_end_of_service_accruals", t => t.HasCheckConstraint("ck_firm_end_of_service_accrual_values",
        "amount > 0 AND length(method) >= 5 AND length(inputs) >= 5 AND length(reason) >= 5"));
    });
  }
}
