using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AuditSphereOps.Api.DesignTime;

/// <summary>
/// Keeps EF tooling from constructing and resolving the complete API host. The fallback is intentionally a non-secret,
/// local-only placeholder; database commands must receive an explicit connection through AUDITSPHERE_MIGRATION_CONNECTION.
/// </summary>
public sealed class AuditSphereDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AuditSphereDbContext>
{
  public AuditSphereDbContext CreateDbContext(string[] args)
  {
    var connectionString = Environment.GetEnvironmentVariable("AUDITSPHERE_MIGRATION_CONNECTION")
      ?? "Host=127.0.0.1;Port=5433;Database=auditsphere_design_time_only;Username=postgres";
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>()
      .UseNpgsql(connectionString)
      .Options;
    return new AuditSphereDbContext(options);
  }
}
