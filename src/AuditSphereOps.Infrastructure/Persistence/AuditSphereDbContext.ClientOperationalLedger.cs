using Microsoft.EntityFrameworkCore;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientOperationalLedger(ModelBuilder b)
  {
    var journal = b.Entity<ClientOperationalJournal>();
    journal.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id })
      .HasName("ak_client_operational_journals_scope_id");
    journal.Property(x => x.JournalNumber).HasMaxLength(100);
    journal.Property(x => x.Description).HasMaxLength(1000);
    journal.Property(x => x.Currency).HasMaxLength(3);
    journal.Property(x => x.Status).HasMaxLength(20);
    journal.Property(x => x.PostingSequence).HasColumnName("posting_sequence").ValueGeneratedOnAddOrUpdate();
    journal.HasIndex(x => new { x.FirmId, x.ClientId, x.PeriodId, x.JournalNumber }).IsUnique()
      .HasDatabaseName("ux_client_operational_journal_number");
    journal.HasIndex(x => new { x.FirmId, x.ClientId, x.PeriodId, x.PostingSequence })
      .HasDatabaseName("ix_client_operational_journal_posting_snapshot").HasFilter("status = 'POSTED'");
    journal.ToTable("client_operational_journals", t => t.HasCheckConstraint("ck_client_operational_journal_values",
      "length(trim(journal_number)) > 0 AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$' AND revision >= 1" +
      " AND status IN ('DRAFT','SUBMITTED','RETURNED','APPROVED','POSTED')" +
      " AND ((status = 'POSTED' AND posted_by_user_id IS NOT NULL AND posted_at IS NOT NULL AND posting_sequence > 0) OR (status <> 'POSTED' AND posting_sequence IS NULL))"));
    journal.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    journal.HasOne<ClientReportingPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    journal.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var reversal = b.Entity<ClientOperationalJournalReversal>();
    reversal.Property(x => x.Reason).HasMaxLength(2000);
    reversal.Property(x => x.EvidenceReference).HasMaxLength(1000);
    reversal.Property(x => x.IntentHash).HasMaxLength(64);
    reversal.HasIndex(x => new { x.FirmId, x.ClientId, x.OriginalJournalId }).IsUnique();
    reversal.HasIndex(x => new { x.FirmId, x.ClientId, x.ReversalJournalId }).IsUnique();
    reversal.HasOne<ClientOperationalJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.OriginalJournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    reversal.HasOne<ClientOperationalJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ReversalJournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    reversal.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, UserId = x.PreparedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    reversal.ToTable("client_operational_journal_reversals", t => t.HasCheckConstraint("ck_client_operational_journal_reversal",
      "original_journal_id<>reversal_journal_id AND original_revision>=1 AND length(trim(reason))>0" +
      " AND length(trim(evidence_reference))>0 AND intent_hash ~ '^[a-f0-9]{64}$'"));

    var receipt = b.Entity<ClientOperationalPostingReceipt>();
    receipt.Property(x => x.IntentHash).HasMaxLength(64);
    receipt.Property(x => x.PreviewDigest).HasMaxLength(64);
    receipt.HasIndex(x => new { x.FirmId, x.ClientId, x.CommandId }).IsUnique();
    receipt.HasIndex(x => new { x.FirmId, x.ClientId, x.JournalId }).IsUnique();
    receipt.HasOne<ClientOperationalJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.JournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    receipt.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, UserId = x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    receipt.ToTable("client_operational_posting_receipts", t => t.HasCheckConstraint("ck_client_operational_posting_receipt",
      "command_id <> '00000000-0000-0000-0000-000000000000'::uuid AND submitted_revision >= 1 AND posted_revision=submitted_revision+1" +
      " AND intent_hash ~ '^[a-f0-9]{64}$' AND preview_digest ~ '^[a-f0-9]{64}$'"));

    var snapshot = b.Entity<ClientOperationalJournalSnapshot>();
    snapshot.Property(x => x.CaptureKind).HasMaxLength(30);
    snapshot.Property(x => x.SnapshotJson).HasColumnType("jsonb");
    snapshot.HasIndex(x => new { x.FirmId, x.ClientId, x.JournalId, x.JournalRevision }).IsUnique();
    snapshot.HasOne<ClientOperationalJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.JournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    snapshot.ToTable("client_operational_journal_snapshots", t => t.HasCheckConstraint("ck_client_operational_snapshot_revision",
      "journal_revision >= 1 AND capture_kind = 'SUBMISSION' AND jsonb_typeof(snapshot_json) = 'object'"));

    var line = b.Entity<ClientOperationalJournalLine>();
    line.Property(x => x.AccountCode).HasMaxLength(100);
    line.Property(x => x.AccountName).HasMaxLength(300);
    line.Property(x => x.Description).HasMaxLength(1000);
    line.HasIndex(x => new { x.FirmId, x.ClientId, x.JournalId, x.LineNumber }).IsUnique()
      .HasDatabaseName("ux_client_operational_journal_line_number");
    line.ToTable("client_operational_journal_lines", t => t.HasCheckConstraint("ck_client_operational_journal_line_values",
      "line_number > 0 AND length(trim(account_code)) > 0 AND length(trim(account_name)) > 0" +
      " AND debit >= 0 AND credit >= 0 AND NOT (debit > 0 AND credit > 0) AND (debit > 0 OR credit > 0)"));
    line.HasOne<ClientOperationalJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.JournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    line.HasOne<ClientAccount>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ClientAccountId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var decision = b.Entity<ClientOperationalJournalDecision>();
    decision.Property(x => x.Decision).HasMaxLength(20);
    decision.Property(x => x.Reason).HasMaxLength(2000);
    decision.HasIndex(x => new { x.FirmId, x.ClientId, x.JournalId, x.JournalRevision }).IsUnique()
      .HasDatabaseName("ux_client_operational_journal_decision_revision");
    decision.ToTable("client_operational_journal_decisions", t => t.HasCheckConstraint("ck_client_operational_journal_decision_values",
      "journal_revision >= 1 AND decision IN ('APPROVE','RETURN') AND length(trim(reason)) > 0"));
    decision.HasOne<ClientOperationalJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.JournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    decision.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, UserId = x.ActorUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
