using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Infrastructure.Persistence;
public sealed partial class AuditSphereDbContext
{
  private static void ConfigureWorkpaperCommands(ModelBuilder b)
  {
    var e=b.Entity<WorkpaperCommandEvent>();
    e.ToTable("workpaper_command_events",t=>t.HasCheckConstraint("ck_workpaper_command_event","actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND action IN ('SAVE_DRAFT','DISCARD_DRAFT','SUBMIT') AND length(before_json) BETWEEN 1 AND 3000000 AND length(after_json) BETWEEN 1 AND 3000000 AND jsonb_typeof(before_json::jsonb)='object' AND jsonb_typeof(after_json::jsonb)='object'"));
    e.Property(x=>x.Action).HasMaxLength(20);e.Property(x=>x.RequestHash).HasMaxLength(64);e.Property(x=>x.BeforeJson).HasColumnType("text");e.Property(x=>x.AfterJson).HasColumnType("text");
    e.HasAlternateKey(x=>new{x.FirmId,x.ClientId,x.EngagementId,x.WorkpaperId,x.Id}).HasName("ak_workpaper_command_event_scope");
    e.HasIndex(x=>new{x.FirmId,x.ActorId,x.RequestId}).IsUnique().HasDatabaseName("ux_workpaper_command_event_request");
    e.HasIndex(x=>new{x.FirmId,x.ClientId,x.EngagementId,x.WorkpaperId}).HasDatabaseName("ix_workpaper_command_event_scope");
    e.HasOne<Workpaper>().WithMany().HasForeignKey(x=>new{x.FirmId,x.ClientId,x.EngagementId,x.WorkpaperId}).HasPrincipalKey(x=>new{x.FirmId,x.ClientId,x.EngagementId,x.Id}).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_workpaper_command_event_target");
    e.HasOne<AppUser>().WithMany().HasForeignKey(x=>new{x.FirmId,x.ActorId}).HasPrincipalKey(x=>new{x.FirmId,x.Id}).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_workpaper_command_event_actor");
    var r=b.Entity<WorkpaperCommandReceipt>();
    r.ToTable("workpaper_command_receipts",t=>t.HasCheckConstraint("ck_workpaper_command_receipt","actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND action IN ('SAVE_DRAFT','DISCARD_DRAFT','SUBMIT') AND length(input_json) BETWEEN 1 AND 800000 AND (preview_json IS NULL OR length(preview_json) BETWEEN 1 AND 4000000) AND jsonb_typeof(input_json::jsonb)='object' AND (preview_json IS NULL OR jsonb_typeof(preview_json::jsonb)='object')"));
    r.Property(x=>x.Action).HasMaxLength(20);r.Property(x=>x.RequestHash).HasMaxLength(64);r.Property(x=>x.InputJson).HasMaxLength(WorkpaperCommandReceipt.MaximumInputLength);r.Property(x=>x.PreviewJson).HasColumnType("text");
    r.HasIndex(x=>new{x.FirmId,x.ActorId,x.RequestId}).IsUnique().HasDatabaseName("ux_workpaper_command_receipt_request");
    r.HasIndex(x=>new{x.FirmId,x.EventId}).IsUnique().HasDatabaseName("ux_workpaper_command_receipt_event");
    r.HasIndex(x=>new{x.FirmId,x.ClientId,x.EngagementId,x.WorkpaperId,x.EventId}).HasDatabaseName("ix_workpaper_command_receipt_scope");
    r.HasOne<WorkpaperCommandEvent>().WithMany().HasForeignKey(x=>new{x.FirmId,x.ClientId,x.EngagementId,x.WorkpaperId,x.EventId}).HasPrincipalKey(x=>new{x.FirmId,x.ClientId,x.EngagementId,x.WorkpaperId,x.Id}).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_workpaper_command_receipt_event");
    r.HasOne<AppUser>().WithMany().HasForeignKey(x=>new{x.FirmId,x.ActorId}).HasPrincipalKey(x=>new{x.FirmId,x.Id}).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_workpaper_command_receipt_actor");
  }
}
