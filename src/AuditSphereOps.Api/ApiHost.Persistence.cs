using Microsoft.EntityFrameworkCore;
using Npgsql;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.Api;

public static partial class ApiHost
{
  private static string ConfigurePersistence(WebApplicationBuilder builder)
  {
    // PostgreSQL: single AuditSphere connection string; startup validates, never auto-applies destructive DDL (§45.6).
    var configuredConnection = builder.Configuration.GetConnectionString("AuditSphere");
    if (string.IsNullOrWhiteSpace(configuredConnection) &&
        !builder.Environment.IsDevelopment() &&
        !builder.Environment.IsEnvironment("Test"))
      throw new InvalidOperationException("ConnectionStrings:AuditSphere must be configured outside Development and Test environments.");
    var connectionString = string.IsNullOrWhiteSpace(configuredConnection)
      ? "Host=127.0.0.1;Port=5433;Database=auditsphere;Username=postgres"
      : configuredConnection;
    // Container-created singleton: disposing the host must release its pool and metric registrations.
    builder.Services.AddSingleton<NpgsqlDataSource>(_ =>
      new NpgsqlDataSourceBuilder(connectionString) { Name = "AuditSphere.Api" }.Build());
    builder.Services.AddDbContextFactory<AuditSphereDbContext>((services, options) =>
      options.UseNpgsql(services.GetRequiredService<NpgsqlDataSource>()));


    return connectionString;
  }
}
