using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
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
    using var keyProfile = environmentName == "Production" ? new ProductionKeyProfile() : null;
    keyProfile?.Apply(settings);
    using var factory = new ApiWebApplicationFactory(settings, environmentName);

    var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
      .Get(CookieAuthenticationDefaults.AuthenticationScheme);
    Assert.True(options.Cookie.HttpOnly);
    Assert.Equal(expectedPolicy, options.Cookie.SecurePolicy);
  }

  [Fact]
  public void ProductionDataProtection_PersistsEncryptedKeysAndIsolatesInstallations()
  {
    using var profile = new ProductionKeyProfile();
    var settings = new Dictionary<string, string?>
    {
      ["Identity:TenantId"] = Guid.NewGuid().ToString("D"),
      ["Identity:ClientId"] = Guid.NewGuid().ToString("D"),
      ["Identity:ClientSecret"] = "synthetic-test-secret",
      ["DevelopmentIdentity:Enabled"] = "false",
      ["Application:AllowSimulationAdapters"] = "false",
      ["ExternalEffects:Enabled"] = "false"
    };
    profile.Apply(settings);
    string protectedValue;
    using (var first = new ApiWebApplicationFactory(settings, "Production"))
      protectedValue = first.Services.GetRequiredService<IDataProtectionProvider>()
        .CreateProtector("fixture").Protect("test payload");

    var keyXml = string.Join('\n', Directory.GetFiles(profile.KeyDirectory, "*.xml")
      .Select(File.ReadAllText));
    Assert.Contains("encryptedSecret", keyXml, StringComparison.Ordinal);
    Assert.DoesNotContain("<masterKey", keyXml, StringComparison.Ordinal);

    using (var restarted = new ApiWebApplicationFactory(settings, "Production"))
      Assert.Equal("test payload", restarted.Services.GetRequiredService<IDataProtectionProvider>()
        .CreateProtector("fixture").Unprotect(protectedValue));

    var originalInstallation = settings["Application:InstallationId"];
    settings["Application:InstallationId"] = Guid.NewGuid().ToString("D");
    using (var anotherInstallation = new ApiWebApplicationFactory(settings, "Production"))
      Assert.Throws<CryptographicException>(() => anotherInstallation.Services
        .GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture").Unprotect(protectedValue));

    settings["Application:InstallationId"] = originalInstallation;
    settings["Identity:TenantId"] = Guid.NewGuid().ToString("D");
    using var anotherTenant = new ApiWebApplicationFactory(settings, "Production");
    Assert.Throws<CryptographicException>(() => anotherTenant.Services
      .GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture").Unprotect(protectedValue));
  }

  [Fact]
  public void ProductionDataProtection_RejectsMissingKeyConfiguration()
  {
    var settings = new Dictionary<string, string?>
    {
      ["Identity:TenantId"] = Guid.NewGuid().ToString("D"),
      ["Identity:ClientId"] = Guid.NewGuid().ToString("D"),
      ["Identity:ClientSecret"] = "synthetic-test-secret",
      ["Application:AllowSimulationAdapters"] = "false",
      ["ExternalEffects:Enabled"] = "false"
    };
    using var factory = new ApiWebApplicationFactory(settings, "Production");
    var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    Assert.Contains("Production Data Protection requires", error.ToString());
  }

  [Fact]
  public void ProductionDataProtection_RotationRetainsOnlyConfiguredPreviousCertificate()
  {
    using var profile = new ProductionKeyProfile();
    var settings = new Dictionary<string, string?>
    {
      ["Identity:TenantId"] = Guid.NewGuid().ToString("D"),
      ["Identity:ClientId"] = Guid.NewGuid().ToString("D"),
      ["Identity:ClientSecret"] = "synthetic-test-secret",
      ["Application:AllowSimulationAdapters"] = "false",
      ["ExternalEffects:Enabled"] = "false"
    };
    profile.Apply(settings);
    string protectedValue;
    using (var beforeRotation = new ApiWebApplicationFactory(settings, "Production"))
      protectedValue = beforeRotation.Services.GetRequiredService<IDataProtectionProvider>()
        .CreateProtector("rotation-fixture").Protect("retained payload");

    var priorPath = settings["DataProtection:CertificatePath"];
    var priorPassword = settings["DataProtection:CertificatePassword"];
    var replacement = profile.CreateCertificate("replacement.pfx");
    settings["DataProtection:CertificatePath"] = replacement.Path;
    settings["DataProtection:CertificatePassword"] = replacement.Password;
    settings["DataProtection:PreviousCertificates:0:Path"] = priorPath;
    settings["DataProtection:PreviousCertificates:0:Password"] = priorPassword;
    using (var rotated = new ApiWebApplicationFactory(settings, "Production"))
      Assert.Equal("retained payload", rotated.Services.GetRequiredService<IDataProtectionProvider>()
        .CreateProtector("rotation-fixture").Unprotect(protectedValue));

    settings.Remove("DataProtection:PreviousCertificates:0:Password");
    using (var incompletePriorCertificate = new ApiWebApplicationFactory(settings, "Production"))
      Assert.Contains("previous certificate is unavailable",
        Assert.ThrowsAny<Exception>(() => incompletePriorCertificate.CreateClient()).ToString());

    settings.Remove("DataProtection:PreviousCertificates:0:Path");
    using var withoutPriorCertificate = new ApiWebApplicationFactory(settings, "Production");
    Assert.Throws<CryptographicException>(() => withoutPriorCertificate.Services
      .GetRequiredService<IDataProtectionProvider>().CreateProtector("rotation-fixture")
      .Unprotect(protectedValue));
  }

  private sealed class ProductionKeyProfile : IDisposable
  {
    private readonly string root = Directory.CreateTempSubdirectory("auditsphere-dp-test-").FullName;
    public string KeyDirectory { get; }
    private (string Path, string Password) Certificate { get; }

    public ProductionKeyProfile()
    {
      KeyDirectory = Path.Combine(root, "keys");
      Directory.CreateDirectory(KeyDirectory);
      Certificate = CreateCertificate("certificate.pfx");
    }

    public (string Path, string Password) CreateCertificate(string fileName)
    {
      var path = Path.Combine(root, fileName);
      var password = Guid.NewGuid().ToString("N");
      using var rsa = RSA.Create(2048);
      var request = new CertificateRequest("CN=AuditSphereOps Test", rsa,
        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
      using var certificate = request.CreateSelfSigned(
        DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
      File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
      return (path, password);
    }

    public void Apply(Dictionary<string, string?> settings)
    {
      settings["Application:InstallationId"] = Guid.NewGuid().ToString("D");
      settings["DataProtection:KeyDirectory"] = KeyDirectory;
      settings["DataProtection:CertificatePath"] = Certificate.Path;
      settings["DataProtection:CertificatePassword"] = Certificate.Password;
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
  }
}
