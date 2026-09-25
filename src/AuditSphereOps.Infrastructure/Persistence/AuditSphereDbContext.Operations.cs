using Microsoft.EntityFrameworkCore;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Microsoft365;

namespace AuditSphereOps.Infrastructure.Persistence;

// DbContext: one owner, snake_case, composite (firm,client,engagement) FK discipline (§§27, 42).
// Money decimal(19,6); IDs uuid v7-compatible; immutable TB rows have no update path.
public sealed partial class AuditSphereDbContext
{
  private static void ConfigureOperations(ModelBuilder b)
  {
    var op = b.Entity<DurableOperation>();
    op.Property(x => x.Status).HasConversion<string>();
    op.Property(x => x.ExecutionMode).HasConversion<string>();
    op.Property(x => x.AuthorityMode).HasConversion<string>();
    op.Property(x => x.IdempotencyKey).HasMaxLength(200);
    op.Property(x => x.RequestDigest).HasMaxLength(64);
    op.Property(x => x.CancellationDisposition).HasMaxLength(2000);
    op.HasIndex(x => new { x.FirmId, x.IdempotencyKey }).IsUnique().HasDatabaseName("ux_operation_firm_key");
    op.HasIndex(x => new { x.FirmId, x.ExecutionGroup, x.Status, x.NextAttemptAt, x.CreatedAt });
    op.ToTable("durable_operations", t =>
    {
      t.HasCheckConstraint("ck_operation_state", "status IN ('PENDING','CLAIMED','REMOTE_STARTED','VERIFYING','COMPLETED','RETRY_WAIT','AUTHORIZATION_BLOCKED','PROVIDER_BLOCKED','RESULT_UNCERTAIN','DEAD_LETTER','CANCEL_REQUESTED','CANCELLED_WITH_DISPOSITION')");
      t.HasCheckConstraint("ck_operation_counters", "attempt_token >= 0 AND attempt_count >= 0 AND expected_revision >= 1 AND schema_version >= 1 AND claimed_epoch >= 0");
      t.HasCheckConstraint("ck_operation_request", "length(idempotency_key) > 0 AND request_digest ~ '^[0-9a-f]{64}$' AND octet_length(request_bytes) > 0 AND length(payload_json) <= 16384");
      t.HasCheckConstraint("ck_operation_mode", "execution_mode IN ('LOCAL','SIMULATED','LIVE') AND authority_mode IN ('LOCAL_VALIDATION','SIMULATION','LIVE_PROVIDER')");
      t.HasCheckConstraint("ck_operation_lease", "(status IN ('CLAIMED','REMOTE_STARTED','VERIFYING','CANCEL_REQUESTED') AND lease_owner IS NOT NULL AND lease_expires_at IS NOT NULL AND attempt_token > 0) OR (status NOT IN ('CLAIMED','REMOTE_STARTED','VERIFYING','CANCEL_REQUESTED') AND lease_owner IS NULL AND lease_expires_at IS NULL)");
      t.HasCheckConstraint("ck_operation_scope", "engagement_id IS NULL OR client_id IS NOT NULL");
      t.HasCheckConstraint("ck_operation_result", "status <> 'COMPLETED' OR (completed_at IS NOT NULL AND result_identity IS NOT NULL AND length(result_identity) > 0 AND result_digest IS NOT NULL AND result_digest ~ '^[0-9a-f]{64}$')");
      t.HasCheckConstraint("ck_operation_cancellation", "status <> 'CANCELLED_WITH_DISPOSITION' OR length(trim(cancellation_disposition)) > 0");
    });
    op.HasOne<FirmSafetyState>().WithMany().HasForeignKey(x => x.FirmId).OnDelete(DeleteBehavior.Restrict);
    op.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    op.HasOne<Engagement>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    b.Entity<ClientSafetyState>().HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.Id }).HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    b.Entity<FirmSafetyState>().ToTable("firm_safety_states", t =>
      t.HasCheckConstraint("ck_firm_safety", "deployment_epoch >= 1 AND recovery_epoch >= 0 AND policy_generation >= 1 AND operating_mode IN ('LOCAL_ONLY','RECOVERY_QUARANTINE')"));
    var recovery = b.Entity<RecoverySession>();
    recovery.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_recovery_sessions_firm_id_id");
    recovery.Property(x => x.RestorePoint).HasMaxLength(500);
    recovery.Property(x => x.ReconciliationScope).HasMaxLength(2000);
    recovery.Property(x => x.Findings).HasMaxLength(10000);
    recovery.HasIndex(x => new { x.FirmId, x.CreatedAt });
    recovery.ToTable("recovery_sessions", t => t.HasCheckConstraint("ck_recovery_session_values",
      "external_epoch >= 1 AND length(trim(restore_point)) > 0 AND length(trim(reconciliation_scope)) > 0 AND length(trim(findings)) > 0 AND ((approved_restart_at IS NULL AND approved_by_user_id IS NULL) OR (approved_restart_at IS NOT NULL AND approved_by_user_id IS NOT NULL))"));
    recovery.HasOne<FirmSafetyState>().WithMany()
      .HasForeignKey(x => new { x.FirmId })
      .HasPrincipalKey(x => new { x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    recovery.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    b.Entity<ClientSafetyState>().ToTable("client_safety_states", t =>
      t.HasCheckConstraint("ck_client_generation", "input_generation >= 1"));
    b.Entity<OperationAttempt>().HasIndex(x => new { x.OperationId, x.Token }).IsUnique();
    b.Entity<OperationAttempt>().HasOne<DurableOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
    b.Entity<OperationEvent>().HasOne<DurableOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
    b.Entity<OperationEvent>().HasIndex(x => new { x.OperationId, x.OccurredAt });
  }
}
