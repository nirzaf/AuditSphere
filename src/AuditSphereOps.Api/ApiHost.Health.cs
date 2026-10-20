using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.Api;

public static partial class ApiHost
{
  private static void ConfigureHealth(WebApplicationBuilder builder, string connectionString)
  {
    // Liveness/readiness split (§45.4): self = always; ready = DB reachable (custom check, no extra package).
    builder.Services.AddHealthChecks()
      .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("web alive"))
      .AddNpgSqlCheck(connectionString ?? "Host=127.0.0.1;Port=5433;Database=auditsphere;Username=postgres")
      .AddCheck<MigrationCheck>("migrations", tags: ["ready"]);
  }
}

/// <summary>Npgsql readiness probe without an extra health-check package (§45.4).</summary>
file sealed class NpgsqlCheck(string connectionString) : IHealthCheck
{
  public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
  {
    try
    {
      await using var conn = new NpgsqlConnection(connectionString);
      await conn.OpenAsync(ct);
      await using var cmd = new NpgsqlCommand("SELECT 1", conn);
      await cmd.ExecuteScalarAsync(ct);
      return HealthCheckResult.Healthy("postgres reachable");
    }
    catch (Exception ex)
    {
      return HealthCheckResult.Unhealthy("postgres unreachable", ex);
    }
  }
}

file static class NpgsqlCheckExtensions
{
  public static Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder AddNpgSqlCheck(
    this Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder builder,
    string connectionString) =>
    builder.AddCheck("postgres", new NpgsqlCheck(connectionString), tags: ["ready"]);
}

file sealed class MigrationCheck(IDbContextFactory<AuditSphereDbContext> factory) : IHealthCheck
{
  public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
  {
    try
    {
      await using var db = await factory.CreateDbContextAsync(ct);
      var pending = await db.Database.GetPendingMigrationsAsync(ct);
      return pending.Any()
        ? HealthCheckResult.Unhealthy($"{pending.Count()} database migrations are pending")
        : HealthCheckResult.Healthy("database schema is current");
    }
    catch (Exception ex)
    {
      return HealthCheckResult.Unhealthy("database migration state unavailable", ex);
    }
  }
}
