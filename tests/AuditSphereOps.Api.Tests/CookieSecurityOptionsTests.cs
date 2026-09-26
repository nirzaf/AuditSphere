using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AuditSphereOps.Api.Tests;

public sealed class CookieSecurityOptionsTests
{
  [Theory]
  [InlineData("Test", CookieSecurePolicy.SameAsRequest)]
  [InlineData("Production", CookieSecurePolicy.Always)]
  public void AuthenticationCookie_RequiresHttpsOutsideLocalProfiles(
    string environmentName, CookieSecurePolicy expectedPolicy)
  {
    var settings = new Dictionary<string, string?>
    {
      ["DevelopmentIdentity:Enabled"] = "false",
      ["Application:AllowSimulationAdapters"] = "false",
      ["ExternalEffects:Enabled"] = "false"
    };
    if (environmentName == "Production")
    {
      settings["Identity:TenantId"] = Guid.NewGuid().ToString("D");
      settings["Identity:ClientId"] = Guid.NewGuid().ToString("D");
      settings["Identity:ClientSecret"] = "synthetic-test-secret";
    }
    using var factory = new ApiWebApplicationFactory(settings, environmentName);

    var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
      .Get(CookieAuthenticationDefaults.AuthenticationScheme);
    Assert.True(options.Cookie.HttpOnly);
    Assert.Equal(expectedPolicy, options.Cookie.SecurePolicy);
  }
}
