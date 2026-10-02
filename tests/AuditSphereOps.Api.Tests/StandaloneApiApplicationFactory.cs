using AuditSphereOps.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AuditSphereOps.Api.Tests;

internal sealed class StandaloneApiApplicationFactory(IReadOnlyDictionary<string, string?> settings)
  : WebApplicationFactory<ApiProgram>
{
  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment("Test");
    builder.UseSetting("AngularUi:Enabled", "false");
    foreach (var (key, value) in settings) if (value is not null) builder.UseSetting(key, value);
    builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
      new Dictionary<string, string?> { ["AngularUi:Enabled"] = "false" }).AddInMemoryCollection(settings));
  }
}
