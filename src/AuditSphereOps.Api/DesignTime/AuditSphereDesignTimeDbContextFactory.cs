using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AuditSphereOps.Api.DesignTime;

/// <summary>
/// Keeps EF tooling from constructing the complete API host. The dedicated migration bundle requires the
/// deployment connection environment binding. Local design-time commands can use a local-only placeholder.
/// </summary>
public sealed class AuditSphereDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AuditSphereDbContext>
{
  public AuditSphereDbContext CreateDbContext(string[] args)
  {
    var bundle = Environment.GetEnvironmentVariable("AUDITSPHERE_MIGRATION_BUNDLE") == "1";
    var value = Environment.GetEnvironmentVariable(bundle
      ? "ConnectionStrings__AuditSphere"
      : "AUDITSPHERE_MIGRATION_CONNECTION");
    if (bundle && string.IsNullOrWhiteSpace(value))
      throw new InvalidOperationException("Set ConnectionStrings__AuditSphere for the migration bundle.");
    var connectionString = value ?? "Host=127.0.0.1;Port=5433;Database=auditsphere_design_time_only;Username=postgres";
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>()
      .UseNpgsql(connectionString)
      .Options;
    return new AuditSphereDbContext(options);
  }
}
