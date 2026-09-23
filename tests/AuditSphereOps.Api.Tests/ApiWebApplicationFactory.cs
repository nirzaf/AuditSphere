using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AuditSphereOps.Api.Tests;

internal sealed class ApiWebApplicationFactory(
  IReadOnlyDictionary<string, string?> settings,
  string environmentName = "Test")
  : WebApplicationFactory<Program>
{
  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment(environmentName);
    foreach (var (key, value) in settings)
      if (value is not null) builder.UseSetting(key, value);
    builder.ConfigureAppConfiguration((_, configuration) =>
      configuration.AddInMemoryCollection(settings));
  }
}
