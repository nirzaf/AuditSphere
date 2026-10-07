using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientOpenItemAllocations(ModelBuilder b)
  {
    var submission = b.Entity<ClientOpenItemAllocationSubmission>();
    submission.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id });
    submission.HasIndex(x => new { x.FirmId, x.ClientId, x.CreatedByUserId, x.CommandId }).IsUnique();
    submission.Property(x => x.SourceKind).HasMaxLength(40);
    submission.Property(x => x.Disposition).HasMaxLength(20);
    submission.Property(x => x.Currency).HasMaxLength(3);
    submission.Property(x => x.SourceAmount).HasPrecision(19, 6);
    submission.Property(x => x.SourceHash).HasMaxLength(64);
    submission.Property(x => x.Reference).HasMaxLength(200);
    submission.Property(x => x.Reason).HasMaxLength(2000);
    submission.Property(x => x.IntentHash).HasMaxLength(64);
    submission.Property(x => x.PreviewDigest).HasMaxLength(64);
    submission.Property(x => x.ManifestHash).HasMaxLength(64);
    submission.Property(x => x.ManifestJson).HasMaxLength(500000);
    submission.HasOne<ClientBookkeepingCounterparty>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, Id = x.CounterpartyId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    submission.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, UserId = x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    submission.ToTable("client_open_item_allocation_submissions", t => t.HasCheckConstraint("ck_client_open_item_allocation_submission",
      "source_kind IN ('SALES_CREDIT','PURCHASE_CREDIT','SALES_RECEIPT','SUPPLIER_PAYMENT') AND disposition IN ('ALLOCATE','UNALLOCATE') AND " +
      "source_item_id<>'00000000-0000-0000-0000-000000000000'::uuid AND counterparty_id<>'00000000-0000-0000-0000-000000000000'::uuid AND " +
      "currency ~ '^[A-Z]{3}$' AND source_amount>0 AND source_hash ~ '^[a-f0-9]{64}$' AND " +
      "command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND intent_hash ~ '^[a-f0-9]{64}$' AND " +
      "preview_digest ~ '^[a-f0-9]{64}$' AND manifest_hash ~ '^[a-f0-9]{64}$' AND length(trim(reference))>0 AND " +
      "length(trim(reason))>0 AND length(manifest_json)>0"));

    var line = b.Entity<ClientOpenItemAllocationLine>();
    line.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id });
    line.HasIndex(x => new { x.FirmId, x.ClientId, x.SubmissionId, x.LineNumber }).IsUnique();
    line.HasIndex(x => new { x.FirmId, x.ClientId, x.ReversesAllocationLineId });
    line.Property(x => x.TargetKind).HasMaxLength(40);
    line.Property(x => x.Amount).HasPrecision(19, 6);
    line.HasOne<ClientOpenItemAllocationSubmission>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.SubmissionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict)
      .HasConstraintName("fk_client_open_item_allocation_line_submission");
    line.HasOne<ClientOpenItemAllocationLine>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.ReversesAllocationLineId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict)
      .HasConstraintName("fk_client_open_item_allocation_line_reversal");
    line.ToTable("client_open_item_allocation_lines", t => t.HasCheckConstraint("ck_client_open_item_allocation_line",
      "line_number>0 AND target_kind IN ('SALES_INVOICE','PURCHASE_INVOICE') AND target_open_item_id<>'00000000-0000-0000-0000-000000000000'::uuid AND amount>0"));

    var decision = b.Entity<ClientOpenItemAllocationDecision>();
    decision.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id });
    decision.HasIndex(x => new { x.FirmId, x.ClientId, x.SubmissionId }).IsUnique();
    decision.HasIndex(x => new { x.FirmId, x.ClientId, x.ActorUserId, x.CommandId }).IsUnique();
    decision.Property(x => x.CommandId);
    decision.Property(x => x.IntentHash).HasMaxLength(64);
    decision.Property(x => x.Decision).HasMaxLength(10);
    decision.Property(x => x.Reason).HasMaxLength(2000);
    decision.Property(x => x.PreviewDigest).HasMaxLength(64);
    decision.Property(x => x.ReviewContextJson).HasMaxLength(500000);
    decision.HasOne<ClientOpenItemAllocationSubmission>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.SubmissionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    decision.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, UserId = x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    decision.ToTable("client_open_item_allocation_decisions", t => t.HasCheckConstraint("ck_client_open_item_allocation_decision",
      "decision IN ('APPROVE','RETURN') AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND " +
      "intent_hash ~ '^[a-f0-9]{64}$' AND preview_digest ~ '^[a-f0-9]{64}$' AND length(trim(reason))>0 AND length(review_context_json)>0"));
  }
}
