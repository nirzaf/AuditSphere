using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Serilog;
using AuditSphereOps.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Serilog console bootstrap (§45.8); file/central sinks land with operations hardening.
Log.Logger = new LoggerConfiguration()
  .ReadFrom.Configuration(builder.Configuration)
  .Enrich.FromLogContext()
  .WriteTo.Console()
  .CreateLogger();
builder.Host.UseSerilog();

// Razor components: Interactive Server, no prerender for auth-sensitive shells (§43.4 draft).
builder.Services.AddRazorComponents()
  .AddInteractiveServerComponents();

// PostgreSQL: single AuditSphere connection string; startup validates, never auto-applies destructive DDL (§45.6).
var connectionString = builder.Configuration.GetConnectionString("AuditSphere");
builder.Services.AddDbContext<AuditSphereDbContext>(options =>
  options.UseNpgsql(connectionString ?? "Host=127.0.0.1;Port=5433;Database=auditsphere;Username=postgres"));

// Liveness/readiness split (§45.4): self = always; ready = DB reachable (custom check, no extra package).
builder.Services.AddHealthChecks()
  .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("web alive"))
  .AddNpgSqlCheck(connectionString ?? "Host=127.0.0.1;Port=5433;Database=auditsphere;Username=postgres");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
  app.UseExceptionHandler("/Error", createScopeForErrors: true);
  app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapHealthChecks("/health/live", new() { Predicate = r => r.Name == "self" });
app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });
app.MapGet("/", () => Results.Ok(new
{
  service = "AuditSphereOps.Web",
  environment = app.Environment.EnvironmentName,
  note = "Blazor shell lands incrementally per §43 route catalog; health endpoints verify topology first (NT-01/NT-18)."
}));

app.Run();

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

