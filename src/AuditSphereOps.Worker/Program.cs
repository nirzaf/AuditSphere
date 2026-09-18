using AuditSphereOps.Worker;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Test"))
  throw new InvalidOperationException("This validation worker is not approved for live environments.");
var connection = builder.Configuration.GetConnectionString("AuditSphere");
if (string.IsNullOrWhiteSpace(connection))
  throw new InvalidOperationException("ConnectionStrings:AuditSphere is required.");
builder.Services.AddDbContextFactory<AuditSphereDbContext>(options => options.UseNpgsql(connection));
if (!Guid.TryParse(builder.Configuration["Worker:FirmId"], out var firmId))
  throw new InvalidOperationException("Worker:FirmId is required.");
var workerOptions = new WorkerOptions(firmId, builder.Environment.EnvironmentName,
  builder.Configuration.GetValue<bool>("AllowSimulationAdapters"),
  builder.Configuration.GetValue<bool>("ExternalEffects:Enabled"),
  DeploymentEpoch: builder.Configuration.GetValue("Worker:DeploymentEpoch", 1L));

builder.Services.AddSingleton(workerOptions);
builder.Services.AddSingleton<TrialBalanceValidationHandler>();
builder.Services.AddSingleton<IAuditSphereDbContextFactory, OperationContextFactory>();
builder.Services.AddSingleton<IOperationStore, PostgresOperationStore>();

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
  return simulationAllowed
    ? [validation, sp.GetRequiredService<PbcDocumentTransferHandler>()]
    : [validation];
}
