using AuditSphereOps.Worker;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Test"))
  throw new InvalidOperationException("This validation worker is not approved for live environments.");
var connection = builder.Configuration.GetConnectionString("AuditSphere");
if (string.IsNullOrWhiteSpace(connection))
  throw new InvalidOperationException("ConnectionStrings:AuditSphere is required.");
builder.Services.AddDbContextFactory<AuditSphereDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
