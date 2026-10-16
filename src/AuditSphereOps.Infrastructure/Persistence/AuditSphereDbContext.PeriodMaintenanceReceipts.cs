using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigurePeriodMaintenanceReceipts(ModelBuilder b)
  {
    var restatement = b.Entity<ClientPeriodRestatement>();
    restatement.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id }).HasName("ak_client_period_restatement_scope_id");
    var e = b.Entity<PeriodMaintenanceReceipt>();
    e.ToTable("period_maintenance_receipts", t => t.HasCheckConstraint("ck_period_maintenance_receipt_values",
      "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
      "request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND " +
      "length(input_json) BETWEEN 1 AND 30000 AND length(before_json) BETWEEN 1 AND 1000000 AND length(result_json) BETWEEN 1 AND 1000000 AND " +
      "((action = 'ROLL_FORWARD' AND result_period_id IS NOT NULL AND restatement_id IS NULL) OR " +
      "(action IN ('CREATE_RESTATEMENT','APPROVE_RESTATEMENT') AND result_period_id IS NULL AND restatement_id IS NOT NULL))"));
    e.Property(x => x.Action).HasMaxLength(30);
    e.Property(x => x.RequestHash).HasMaxLength(64);
    e.Property(x => x.ReviewBasis).HasMaxLength(64);
    e.Property(x => x.InputJson).HasMaxLength(30000);
    e.Property(x => x.BeforeJson).HasColumnType("text");
    e.Property(x => x.ResultJson).HasColumnType("text");
    e.HasIndex(x => new { x.FirmId, x.ActorId, x.RequestId }).IsUnique().HasDatabaseName("ux_period_maintenance_actor_request");
    e.HasIndex(x => new { x.FirmId, x.ClientId, x.ResultPeriodId }).IsUnique().HasDatabaseName("ux_period_maintenance_result_period");
    e.HasIndex(x => new { x.FirmId, x.ClientId, x.RestatementId, x.Action }).IsUnique().HasDatabaseName("ux_period_maintenance_restatement_action");
    e.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId }).HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId }).HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<ClientReportingPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId }).HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<ClientReportingPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ResultPeriodId }).HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<ClientPeriodRestatement>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.RestatementId }).HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
