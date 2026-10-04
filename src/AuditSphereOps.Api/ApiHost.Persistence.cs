using System.Buffers;
using System.Security.Claims;
using System.Security.Cryptography;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Diagnostics;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Api.Diagnostics;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;

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
