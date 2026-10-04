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
  private static void ConfigureObservability(WebApplicationBuilder builder, bool legacyPresentation)
  {
    // Serilog console bootstrap (§45.8); file/central sinks land with operations hardening.
    Log.Logger = new LoggerConfiguration()
      .ReadFrom.Configuration(builder.Configuration)
      .Enrich.FromLogContext()
      .WriteTo.Console()
      .CreateLogger();
    builder.Host.UseSerilog();

    // Telemetry is independently configurable from ExternalEffects. With no OTLP endpoint nothing
    // is exported; enabling an exporter never participates in a business transaction and therefore
    // cannot roll back durable state.
    var telemetryEndpoint = TelemetryEndpoint.Parse(builder.Configuration["Telemetry:Otlp:Endpoint"]);
    var telemetry = builder.Services.AddOpenTelemetry()
      .ConfigureResource(resource => resource.AddService("AuditSphereOps.Api"))
      .WithTracing(tracing =>
      {
        tracing.AddSource(AuditDiagnostics.ActivitySourceName)
          .AddSource("Npgsql")
          .AddAspNetCoreInstrumentation()
          .AddHttpClientInstrumentation();
        if (telemetryEndpoint is not null)
          tracing.AddOtlpExporter(options => options.Endpoint = telemetryEndpoint);
      })
      .WithMetrics(metrics =>
      {
        metrics.AddMeter(AuditDiagnostics.MeterName)
          .AddMeter("Microsoft.AspNetCore.Hosting")
          .AddMeter("Npgsql")
          .AddRuntimeInstrumentation();
        if (telemetryEndpoint is not null)
          metrics.AddOtlpExporter(options => options.Endpoint = telemetryEndpoint);
      });
  }
}
