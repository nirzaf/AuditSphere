using AuditSphereOps.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AuditSphereOps.Api.Tests;

internal sealed class StandaloneApiApplicationFactory(IReadOnlyDictionary<string, string?> settings, Action<IServiceCollection>? configureServices = null)
  : WebApplicationFactory<ApiProgram>
{
  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment("Test");
    builder.UseSetting("AngularUi:Enabled", "false");
    builder.UseSetting("AngularUi:CanonicalRoutes", "false");
    foreach (var (key, value) in settings) if (value is not null) builder.UseSetting(key, value);
    builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
      new Dictionary<string, string?>
      {
        ["AngularUi:Enabled"] = "false",
        ["AngularUi:CanonicalRoutes"] = "false"
      }).AddInMemoryCollection(settings));
    if (configureServices is not null) builder.ConfigureServices(configureServices);
  }
}
