using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  public DbSet<OperatorCommandEvent> OperatorCommandEvents => Set<OperatorCommandEvent>();
  public DbSet<OperatorCommandReceipt> OperatorCommandReceipts => Set<OperatorCommandReceipt>();

  private static void ConfigureOperatorCommands(ModelBuilder b)
  {
    var e = b.Entity<OperatorCommandEvent>();
    e.ToTable("operator_command_events", t => t.HasCheckConstraint("ck_operator_command_event",
      "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$'" +
      " AND action IN ('RETRY','CANCEL','LIFT_QUARANTINE','APPROVE_RECOVERY_RESTART')" +
      " AND ((action IN ('RETRY','CANCEL') AND operation_id IS NOT NULL AND operation_event_id IS NOT NULL AND recovery_session_id IS NULL)" +
      " OR (action = 'LIFT_QUARANTINE' AND operation_id IS NULL AND operation_event_id IS NULL)" +
      " OR (action = 'APPROVE_RECOVERY_RESTART' AND operation_id IS NULL AND operation_event_id IS NULL AND recovery_session_id IS NOT NULL))" +
      " AND length(before_json) BETWEEN 1 AND 150000 AND length(after_json) BETWEEN 1 AND 150000" +
      " AND jsonb_typeof(before_json::jsonb)='object' AND jsonb_typeof(after_json::jsonb)='object'"));
    e.Property(x => x.Action).HasMaxLength(40);
    e.Property(x => x.RequestHash).HasMaxLength(64);
    e.Property(x => x.BeforeJson).HasColumnType("text");
    e.Property(x => x.AfterJson).HasColumnType("text");
    e.HasAlternateKey(x => new { x.FirmId, x.ActorId, x.Id }).HasName("ak_operator_command_event_owner");
    e.HasIndex(x => new { x.FirmId, x.ActorId, x.RequestId }).IsUnique().HasDatabaseName("ux_operator_command_event_request");
    e.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_operator_command_event_actor");
    e.HasOne<DurableOperation>().WithMany().HasForeignKey(x => x.OperationId)
      .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_operator_command_event_operation");
    e.HasOne<OperationEvent>().WithMany().HasForeignKey(x => x.OperationEventId)
      .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_operator_command_event_source");
    e.HasOne<RecoverySession>().WithMany().HasForeignKey(x => new { x.FirmId, x.RecoverySessionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_operator_command_event_session");

    var r = b.Entity<OperatorCommandReceipt>();
    r.ToTable("operator_command_receipts", t => t.HasCheckConstraint("ck_operator_command_receipt",
      "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$'" +
      " AND action IN ('RETRY','CANCEL','LIFT_QUARANTINE','APPROVE_RECOVERY_RESTART')" +
      " AND length(input_json) BETWEEN 1 AND 65000 AND length(preview_json) BETWEEN 1 AND 250000" +
      " AND jsonb_typeof(input_json::jsonb)='object' AND jsonb_typeof(preview_json::jsonb)='object'"));
    r.Property(x => x.Action).HasMaxLength(40);
    r.Property(x => x.RequestHash).HasMaxLength(64);
    r.Property(x => x.InputJson).HasColumnType("text");
    r.Property(x => x.PreviewJson).HasColumnType("text");
    r.HasIndex(x => new { x.FirmId, x.ActorId, x.RequestId }).IsUnique().HasDatabaseName("ux_operator_command_receipt_request");
    r.HasIndex(x => new { x.FirmId, x.EventId }).IsUnique().HasDatabaseName("ux_operator_command_receipt_event");
    r.HasOne<OperatorCommandEvent>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorId, x.EventId })
      .HasPrincipalKey(x => new { x.FirmId, x.ActorId, x.Id }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_operator_command_receipt_event");
  }
}
