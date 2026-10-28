using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  // Register together with the command, query and additive SQL publication/immutability guards.
  private static void ConfigureWorkflowApplicability(ModelBuilder b)
  {
    var proposal = b.Entity<WorkflowTaskApplicabilityProposal>();
    proposal.HasAlternateKey(x => new { x.FirmId, x.Id });
    proposal.HasAlternateKey(x => new
      { x.FirmId, x.ClientId, x.EngagementId, x.WorkflowTaskBindingId, x.Id, x.ActorUserId });
    proposal.HasIndex(x => new { x.FirmId, x.WorkflowTaskBindingId, x.Version }).IsUnique();
    proposal.HasIndex(x => new { x.FirmId, x.ActorUserId, x.IdempotencyKey }).IsUnique();
    proposal.Property(x => x.ConditionDigest).HasMaxLength(64);
    proposal.Property(x => x.BasisJson).HasMaxLength(32768);
    proposal.Property(x => x.BasisHash).HasMaxLength(64);
    proposal.Property(x => x.Reason).HasMaxLength(1000);
    proposal.Property(x => x.RequestDigest).HasMaxLength(64);
    proposal.HasOne<WorkflowTaskBinding>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.WorkflowTaskBindingId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    proposal.HasOne<WorkflowTemplateStep>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.TemplateRevisionId, x.TemplateStepId })
      .HasPrincipalKey(x => new { x.FirmId, x.TemplateRevisionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    proposal.HasOne<PbcAcceptanceReceipt>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PbcAcceptanceReceiptId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    proposal.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    proposal.ToTable("workflow_task_applicability_proposals", t =>
      t.HasCheckConstraint("ck_workflow_task_applicability_proposal_values",
        "version >= 1 AND task_revision >= 1 AND binding_generation >= 1 AND engagement_generation >= 1 AND actor_epoch >= 0" +
        " AND length(trim(reason)) BETWEEN 1 AND 1000 AND length(basis_json) BETWEEN 2 AND 32768" +
        " AND jsonb_typeof(basis_json::jsonb) = 'object'" +
        " AND basis_hash = encode(sha256(convert_to(basis_json, 'UTF8')), 'hex')" +
        " AND condition_digest ~ '^[0-9a-f]{64}$' AND request_digest ~ '^[0-9a-f]{64}$'" +
        " AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid"));

    var decision = b.Entity<WorkflowTaskApplicabilityDecision>();
    decision.HasAlternateKey(x => new { x.FirmId, x.Id });
    decision.HasAlternateKey(x => new
      { x.FirmId, x.ClientId, x.EngagementId, x.WorkflowTaskBindingId, x.ProposalId, x.Id });
    decision.HasIndex(x => new { x.FirmId, x.ProposalId, x.Version }).IsUnique();
    decision.HasIndex(x => new { x.FirmId, x.ActorUserId, x.IdempotencyKey }).IsUnique();
    decision.Property(x => x.Kind).HasMaxLength(16);
    decision.Property(x => x.BasisHash).HasMaxLength(64);
    decision.Property(x => x.Reason).HasMaxLength(1000);
    decision.Property(x => x.RequestDigest).HasMaxLength(64);
    // Proposer identity comes from the real scoped proposal, not a caller-supplied name used to evade self-review.
    decision.HasOne<WorkflowTaskApplicabilityProposal>().WithMany()
      .HasForeignKey(x => new
        { x.FirmId, x.ClientId, x.EngagementId, x.WorkflowTaskBindingId, x.ProposalId, x.ProposerUserId })
      .HasPrincipalKey(x => new
        { x.FirmId, x.ClientId, x.EngagementId, x.WorkflowTaskBindingId, x.Id, x.ActorUserId })
      .HasConstraintName("fk_workflow_applicability_decision_proposal").OnDelete(DeleteBehavior.Restrict);
    decision.HasOne<WorkflowTaskApplicabilityDecision>().WithMany()
      .HasForeignKey(x => new
        { x.FirmId, x.ClientId, x.EngagementId, x.WorkflowTaskBindingId, x.ProposalId, x.PreviousDecisionId })
      .HasPrincipalKey(x => new
        { x.FirmId, x.ClientId, x.EngagementId, x.WorkflowTaskBindingId, x.ProposalId, x.Id })
      .HasConstraintName("fk_workflow_applicability_decision_previous").OnDelete(DeleteBehavior.Restrict);
    decision.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    decision.ToTable("workflow_task_applicability_decisions", t =>
      t.HasCheckConstraint("ck_workflow_task_applicability_decision_values",
        "version >= 1 AND actor_epoch >= 0 AND actor_user_id <> proposer_user_id" +
        " AND kind IN ('APPROVE','RETURN','REVOKE') AND length(trim(reason)) BETWEEN 1 AND 1000" +
        " AND basis_hash ~ '^[0-9a-f]{64}$' AND request_digest ~ '^[0-9a-f]{64}$'" +
        " AND ((version = 1 AND previous_decision_id IS NULL AND kind IN ('APPROVE','RETURN'))" +
        " OR (version > 1 AND previous_decision_id IS NOT NULL AND kind = 'REVOKE'))" +
        " AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid"));
  }
}
