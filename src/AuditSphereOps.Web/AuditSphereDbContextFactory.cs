using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AuditSphereOps.Web;

// Design-time factory: explicit connection string for `dotnet ef migrations add` (§45.6).
// Never used at runtime; runtime resolves the connection string from configuration/secrets.
public sealed class AuditSphereDbContextFactory : IDesignTimeDbContextFactory<AuditSphereDbContext>
{
  public AuditSphereDbContext CreateDbContext(string[] args)
  {
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>()
      .UseNpgsql("Host=127.0.0.1;Port=5433;Database=auditsphere;Username=postgres")
      .Options;
    return new AuditSphereDbContext(options);
  }
}
