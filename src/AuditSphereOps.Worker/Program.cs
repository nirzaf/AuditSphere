using AuditSphereOps.Worker;
using AuditSphereOps.Application.Accounting;
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
var handler = new TrialBalanceValidationHandler();
var registry = new DurableOperationRegistry([handler], workerOptions);
builder.Services.AddSingleton(workerOptions);
builder.Services.AddSingleton(handler);
builder.Services.AddSingleton(registry);
builder.Services.AddSingleton<IAuditSphereDbContextFactory, OperationContextFactory>();
builder.Services.AddSingleton<IOperationStore, PostgresOperationStore>();
builder.Services.AddSingleton<OperationDispatcher>();
builder.Services.AddSingleton<TrialBalanceDiscovery>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
