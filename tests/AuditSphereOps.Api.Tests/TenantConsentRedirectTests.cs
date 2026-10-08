using AuditSphereOps.Api.Authentication;

namespace AuditSphereOps.Api.Tests;

public sealed class TenantConsentRedirectTests
{
  private static readonly string Tenant = Guid.NewGuid().ToString("D");
  private static readonly string Client = Guid.NewGuid().ToString("D");

  [Fact]
  public void ExactDeploymentCallbackBuildsFixedTenantMicrosoftConsentUrl()
  {
    var uri = TenantConsentRedirect.Build(Tenant, Client,
      "https://audit.example.test/auth/m365-consent/callback", "opaque-state", false);
    Assert.NotNull(uri);
    Assert.Equal("login.microsoftonline.com", uri.Host);
    Assert.Contains("/" + Tenant + "/v2.0/adminconsent", uri.AbsolutePath, StringComparison.Ordinal);
    Assert.Contains("client_id=" + Client, uri.Query, StringComparison.Ordinal);
    Assert.Contains("state=opaque-state", uri.Query, StringComparison.Ordinal);
    Assert.Contains("graph.microsoft.com%2F.default", uri.Query, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("http://audit.example.test/auth/m365-consent/callback", false)]
  [InlineData("https://evil.test/auth/m365-consent/callback?next=evil", false)]
  [InlineData("https://audit.example.test/other", false)]
  [InlineData("https://user@audit.example.test/auth/m365-consent/callback", false)]
  [InlineData("http://localhost:5099/auth/m365-consent/callback", false)]
  public void UnsafeCallbackIsRejected(string callback, bool development)
  {
    Assert.Null(TenantConsentRedirect.Build(Tenant, Client, callback, "opaque-state", development));
  }

  [Fact]
  public void DevelopmentLoopbackCallbackIsAllowedOnlyForDevelopment()
  {
    Assert.NotNull(TenantConsentRedirect.Build(Tenant, Client,
      "http://localhost:5099/auth/m365-consent/callback", "opaque-state", true));
  }
}
