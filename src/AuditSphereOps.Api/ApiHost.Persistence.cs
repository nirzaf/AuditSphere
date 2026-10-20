using Microsoft.EntityFrameworkCore;
using Npgsql;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.Api;

public static partial class ApiHost
{
  private static string ConfigurePersistence(WebApplicationBuilder builder, bool legacyPresentation)
  {
    // PostgreSQL: single AuditSphere connection string; startup validates, never auto-applies destructive DDL (§45.6).
    var connectionString = builder.Configuration.GetConnectionString("AuditSphere")
      ?? "Host=127.0.0.1;Port=5433;Database=auditsphere;Username=postgres";
    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString) { Name = "AuditSphere.Api" };
    var dataSource = dataSourceBuilder.Build();
    builder.Services.AddSingleton(dataSource);
    builder.Services.AddDbContextFactory<AuditSphereDbContext>(options => options.UseNpgsql(dataSource));


    return connectionString;
  }
}
