using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  public DbSet<ClientSharePointSite> ClientSharePointSites => Set<ClientSharePointSite>();
  public DbSet<ClientSiteWorkerHeartbeat> ClientSiteWorkerHeartbeats => Set<ClientSiteWorkerHeartbeat>();

  private static void ConfigureClientSharePointSites(ModelBuilder b)
  {
    var site = b.Entity<ClientSharePointSite>();
    site.HasAlternateKey(x => new { x.FirmId, x.Id });
    site.HasIndex(x => new { x.FirmId, x.ClientId }).IsUnique();
    site.HasIndex(x => new { x.TenantId, x.RequestedUrl }).IsUnique();
    site.Property(x => x.TenantId).HasMaxLength(200);
    site.Property(x => x.RequestedUrl).HasMaxLength(500);
    site.Property(x => x.Title).HasMaxLength(200);
    site.Property(x => x.OwnershipMarker).HasMaxLength(200);
    site.Property(x => x.State).HasMaxLength(30);
    site.Property(x => x.MembershipState).HasMaxLength(30);
    site.Property(x => x.SiteId).HasMaxLength(2000);
    site.Property(x => x.DriveId).HasMaxLength(2000);
    site.Property(x => x.RootItemId).HasMaxLength(500);
    site.Property(x => x.StaffGroupId).HasMaxLength(100);
    site.Property(x => x.DesiredDigest).HasMaxLength(64);
    site.Property(x => x.Reason).HasMaxLength(1000);
    site.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    site.HasOne<Microsoft365ConnectionRevision>().WithMany().HasForeignKey(x => new { x.FirmId, x.ConnectionRevisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    site.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.RequestedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    site.ToTable("client_share_point_sites", t => t.HasCheckConstraint("ck_client_sharepoint_site_state", "state IN ('REQUESTED','READY') AND membership_state IN ('PENDING','VERIFIED','PARTIAL')"));

    var heartbeat = b.Entity<ClientSiteWorkerHeartbeat>();
    heartbeat.HasIndex(x => new { x.FirmId, x.WorkerGroup, x.WorkerInstanceId }).IsUnique()
      .HasDatabaseName("ux_client_site_worker_heartbeat_instance");
    heartbeat.HasIndex(x => new { x.FirmId, x.WorkerGroup, x.LastSeenAt })
      .HasDatabaseName("ix_client_site_worker_heartbeat_recent");
    heartbeat.Property(x => x.WorkerGroup).HasMaxLength(40);
    heartbeat.Property(x => x.WorkerInstanceId).HasMaxLength(64);
    heartbeat.Property(x => x.ReadinessState).HasMaxLength(40);
    heartbeat.Property(x => x.DiagnosticCode).HasMaxLength(100);
    heartbeat.ToTable("client_site_worker_heartbeats", t => t.HasCheckConstraint(
      "ck_client_site_worker_heartbeat_state",
      "worker_group = 'client-sites' AND deployment_epoch >= 1 AND readiness_state IN ('READY','DISABLED','BLOCKED_CONFIGURATION')"));
  }
}
