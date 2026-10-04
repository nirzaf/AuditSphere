using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureAccountingCreationPreparations(ModelBuilder b)
  {
    var reconciliation = b.Entity<AccountingReconciliation>();
    reconciliation.HasIndex(x => new { x.FirmId, x.EngagementId, x.PeriodId, x.Area, x.Revision })
      .IsUnique().HasDatabaseName("ux_accounting_reconciliation_revision");
    reconciliation.HasOne<AccountingReconciliation>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SupersedesReconciliationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var specialist = b.Entity<SpecialistAccountingSchedule>();
    specialist.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("ak_specialist_schedules_scope_id");
    specialist.HasIndex(x => new { x.FirmId, x.EngagementId, x.PeriodId, x.Area, x.Revision })
      .IsUnique().HasDatabaseName("ux_specialist_schedule_revision");
    specialist.HasOne<SpecialistAccountingSchedule>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SupersedesScheduleId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var reconciliationPreparation = b.Entity<AccountingReconciliationPreparation>();
    ConfigureReceipt(reconciliationPreparation, "accounting_reconciliation_preparations", "ReconciliationId",
      "RequestId", "evidence_reference");
    reconciliationPreparation.HasOne<AccountingReconciliation>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ReconciliationId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var specialistPreparation = b.Entity<SpecialistSchedulePreparation>();
    ConfigureReceipt(specialistPreparation, "specialist_schedule_preparations", "ScheduleId",
      "RequestId", null);
    specialistPreparation.HasOne<SpecialistAccountingSchedule>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ScheduleId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }

  private static void ConfigureReceipt<TEntity>(EntityTypeBuilder<TEntity> entity, string table,
    string resultProperty, string requestProperty, string? evidenceColumn)
    where TEntity : class
  {
    entity.ToTable(table, t => t.HasCheckConstraint("ck_" + table + "_values",
      "actor_epoch >= 1 AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' " +
      "AND length(input_json) BETWEEN 1 AND 30000 AND length(trim(reason)) BETWEEN 1 AND 4000" +
      (evidenceColumn is null ? string.Empty : " AND length(trim(evidence_reference)) BETWEEN 1 AND 2000")));
    entity.Property("RequestHash").HasMaxLength(64);
    entity.Property("ReviewBasis").HasMaxLength(64);
    entity.Property("Reason").HasMaxLength(4000);
    entity.Property("InputJson").HasMaxLength(30000);
    if (evidenceColumn is not null) entity.Property("EvidenceReference").HasMaxLength(2000);
    entity.HasIndex("FirmId", "ActorId", requestProperty).IsUnique()
      .HasDatabaseName("ux_" + table + "_actor_request");
    entity.HasIndex("FirmId", "EngagementId", resultProperty).IsUnique()
      .HasDatabaseName("ux_" + table + "_result");
    entity.HasOne<Engagement>().WithMany()
      .HasForeignKey("FirmId", "ClientId", "EngagementId")
      .HasPrincipalKey(nameof(Engagement.FirmId), nameof(Engagement.PracticeClientId), nameof(Engagement.Id))
      .OnDelete(DeleteBehavior.Restrict);
    entity.HasOne<AppUser>().WithMany()
      .HasForeignKey("FirmId", "ActorId")
      .HasPrincipalKey(nameof(AppUser.FirmId), nameof(AppUser.Id)).OnDelete(DeleteBehavior.Restrict);
  }
}
