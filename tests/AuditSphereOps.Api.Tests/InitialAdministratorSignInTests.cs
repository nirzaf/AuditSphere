using AuditSphereOps.Api.Authentication;
using Microsoft.Extensions.Configuration;

namespace AuditSphereOps.Api.Tests;

public sealed class InitialAdministratorSignInTests
{
  private const string Tenant = "11111111-1111-4111-8111-111111111111";
  private const string Object = "22222222-2222-4222-8222-222222222222";

  [Fact]
  public void OnlyExactApprovedIdentityWithCompleteProofBackedSetupCanEnterBeforeMapping()
  {
    var settings = new Dictionary<string, string?>
    {
      ["Setup:FirmId"] = Guid.NewGuid().ToString(),
      ["Setup:InstallationId"] = "non-production-installation",
      ["Setup:BootstrapProofHash"] = new string('a', 64),
      ["Setup:InitialAdministratorTenantId"] = Tenant,
      ["Setup:InitialAdministratorObjectId"] = Object
    };
    IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    Assert.True(InitialAdministratorSignIn.AllowsUnmappedIdentity(Configuration(), Tenant, Object));
    Assert.False(InitialAdministratorSignIn.AllowsUnmappedIdentity(Configuration(), Guid.NewGuid().ToString(), Object));
    Assert.False(InitialAdministratorSignIn.AllowsUnmappedIdentity(Configuration(), Tenant, Guid.NewGuid().ToString()));

    settings["Setup:BootstrapProofHash"] = null;
    Assert.False(InitialAdministratorSignIn.AllowsUnmappedIdentity(Configuration(), Tenant, Object));
    settings["Setup:BootstrapProofHash"] = new string('a', 64);
    settings["Setup:InstallationId"] = null;
    Assert.False(InitialAdministratorSignIn.AllowsUnmappedIdentity(Configuration(), Tenant, Object));
    settings["Setup:InstallationId"] = "non-production-installation";
    settings["Setup:FirmId"] = Guid.Empty.ToString();
    Assert.False(InitialAdministratorSignIn.AllowsUnmappedIdentity(Configuration(), Tenant, Object));
  }
}
