using AuditSphereOps.Worker;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Diagnostics;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

// Worker telemetry is independent from provider-effect enablement. The exporter is optional,
// bounded by the OpenTelemetry SDK, and never owns a business transaction or lease decision.
var telemetryEndpoint = builder.Configuration["Telemetry:Otlp:Endpoint"];
builder.Services.AddOpenTelemetry()
  .ConfigureResource(resource => resource.AddService("AuditSphereOps.Worker"))
  .WithTracing(tracing =>
  {
    tracing.AddSource(AuditDiagnostics.ActivitySourceName)
      .AddSource("Npgsql");
    if (Uri.TryCreate(telemetryEndpoint, UriKind.Absolute, out var endpoint))
      tracing.AddOtlpExporter(options => options.Endpoint = endpoint);
  })
  .WithMetrics(metrics =>
  {
    metrics.AddMeter(AuditDiagnostics.MeterName)
      .AddMeter("Npgsql")
      .AddRuntimeInstrumentation();
    if (Uri.TryCreate(telemetryEndpoint, UriKind.Absolute, out var endpoint))
      metrics.AddOtlpExporter(options => options.Endpoint = endpoint);
  });
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Test"))
  throw new InvalidOperationException("This validation worker is not approved for live environments.");
var connection = builder.Configuration.GetConnectionString("AuditSphere");
if (string.IsNullOrWhiteSpace(connection))
  throw new InvalidOperationException("ConnectionStrings:AuditSphere is required.");
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connection) { Name = "AuditSphere.Worker" };
var dataSource = dataSourceBuilder.Build();
builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContextFactory<AuditSphereDbContext>(options => options.UseNpgsql(dataSource));
if (!Guid.TryParse(builder.Configuration["Worker:FirmId"], out var firmId))
  throw new InvalidOperationException("Worker:FirmId is required.");
if (!long.TryParse(builder.Configuration["Worker:DeploymentEpoch"], out var deploymentEpoch) || deploymentEpoch < 1)
  throw new InvalidOperationException("Worker:DeploymentEpoch is required and must be positive.");
var workerOptions = new WorkerOptions(firmId, builder.Environment.EnvironmentName,
  builder.Configuration.GetValue<bool>("AllowSimulationAdapters"),
  builder.Configuration.GetValue<bool>("ExternalEffects:Enabled"),
  DeploymentEpoch: deploymentEpoch);
if (workerOptions.ExternalEffectsEnabled)
  throw new InvalidOperationException("ExternalEffects:Enabled requires an approved live provider composition; startup refused.");

var releaseSafety = builder.Configuration.GetSection(ReleaseSafetyOptions.SectionName).Get<ReleaseSafetyOptions>() ?? new();
releaseSafety.Validate(
  builder.Configuration.GetValue<bool>("ExternalEffects:Enabled"),
  builder.Configuration.GetValue<bool>("AllowSimulationAdapters"),
  builder.Environment.EnvironmentName,
  builder.Configuration.GetValue<bool>("FeatureActivation:LiveAuditRelease"));
builder.Services.AddSingleton(releaseSafety);

var checkpointRoot = builder.Configuration["Storage:ReleaseCheckpointRoot"]
  ?? Path.Combine(Path.GetTempPath(), "AuditSphereOps", "release-checkpoints");
builder.Services.AddSingleton<IReleaseCheckpointStore>(new LocalAppendOnlyCheckpointStore(checkpointRoot));
builder.Services.AddSingleton<ReleaseCheckpointHandler>();

builder.Services.AddSingleton(workerOptions);
builder.Services.AddSingleton<TrialBalanceValidationHandler>();
builder.Services.AddSingleton<IAuditSphereDbContextFactory, OperationContextFactory>();
builder.Services.AddSingleton<IOperationStore, PostgresOperationStore>();
builder.Services.AddSingleton<GeneralLedgerCompletenessHandler>();
builder.Services.AddSingleton<FinancialPackageBuildHandler>();

// Simulation adapters compose only in a Test environment with the explicit enablement flag.
// In Development the PBC transfer operations remain queued pending an approved provider
// boundary; the durable queue records the truthful pending state instead of executing.
var simulationAllowed = workerOptions.EnvironmentName == "Test" && workerOptions.AllowSimulationAdapters;
if (simulationAllowed)
{
  var providerRoot = builder.Configuration["Storage:PbcProviderSimulationRoot"]
    ?? throw new InvalidOperationException("Storage:PbcProviderSimulationRoot is required for simulated transfers.");
  builder.Services.AddSingleton<IPbcProviderSink>(new SimulationPbcProviderSink(providerRoot));
  builder.Services.AddSingleton<PbcDocumentTransferHandler>();
  builder.Services.AddSingleton<IPendingOperationDiscovery>(sp => new PbcTransferDiscovery(
    sp.GetRequiredService<IAuditSphereDbContextFactory>(),
    sp.GetRequiredService<IOperationStore>(),
    sp.GetRequiredService<PbcDocumentTransferHandler>(),
    workerOptions));
}

builder.Services.AddSingleton<IPendingOperationDiscovery>(sp =>
  sp.GetRequiredService<TrialBalanceDiscovery>());
builder.Services.AddSingleton(sp => new DurableOperationRegistry(
  ResolveHandlers(sp, simulationAllowed), workerOptions));
builder.Services.AddSingleton<OperationDispatcher>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

static IOperationHandler[] ResolveHandlers(IServiceProvider sp, bool simulationAllowed)
{
  var validation = sp.GetRequiredService<TrialBalanceValidationHandler>();
  var completeness = sp.GetRequiredService<GeneralLedgerCompletenessHandler>();
  var packageBuild = sp.GetRequiredService<FinancialPackageBuildHandler>();
  var checkpoint = sp.GetRequiredService<ReleaseCheckpointHandler>();
  return simulationAllowed
    ? [validation, completeness, packageBuild, checkpoint, sp.GetRequiredService<PbcDocumentTransferHandler>()]
    : [validation, completeness, packageBuild, checkpoint];
}
