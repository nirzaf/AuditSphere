using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureStatementLayoutCommands(ModelBuilder b)
  {
    var receipt = b.Entity<StatementLayoutCommandReceipt>();
    receipt.ToTable("statement_layout_command_receipts", t => t.HasCheckConstraint(
      "ck_statement_layout_command_receipt_values",
      "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
      "action IN ('SAVE','PUBLISH') AND request_hash ~ '^[0-9a-f]{64}$' AND " +
      "(expected_layout_hash IS NULL OR expected_layout_hash ~ '^[0-9a-f]{64}$') AND " +
      "result_layout_hash ~ '^[0-9a-f]{64}$' AND result_status IN ('DRAFT','VALIDATED','PUBLISHED') AND " +
      "result_version_number >= 1 AND length(input_json) BETWEEN 1 AND 120000 AND " +
      "length(result_json) BETWEEN 1 AND 1000000 AND jsonb_typeof(input_json::jsonb) = 'object' AND " +
      "jsonb_typeof(result_json::jsonb) = 'object'"));
    receipt.Property(x => x.Action).HasMaxLength(20);
    receipt.Property(x => x.RequestHash).HasMaxLength(64);
    receipt.Property(x => x.ExpectedLayoutHash).HasMaxLength(64);
    receipt.Property(x => x.ResultLayoutHash).HasMaxLength(64);
    receipt.Property(x => x.ResultStatus).HasMaxLength(30);
    receipt.Property(x => x.InputJson).HasColumnType("text").HasMaxLength(120000);
    receipt.Property(x => x.ResultJson).HasColumnType("text");
    receipt.HasIndex(x => new { x.FirmId, x.ClientId, x.ActorId, x.RequestId }).IsUnique()
      .HasDatabaseName("ux_statement_layout_command_receipt_actor_request");
    receipt.HasIndex(x => new { x.FirmId, x.ClientId, x.CreatedAt })
      .HasDatabaseName("ix_statement_layout_command_receipt_client_history");
    receipt.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    receipt.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    receipt.HasOne<StatementLayoutVersion>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.LayoutVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
