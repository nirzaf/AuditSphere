namespace AuditSphereOps.Web.Authentication;

/// <summary>Builds a fixed-tenant Microsoft admin-consent redirect from deployment-owned values.</summary>
public static class TenantConsentRedirect
{
  public static Uri? Build(string? tenantId, string? clientId, string? callback,
    string state, bool development)
  {
    if (!Guid.TryParse(tenantId, out var tenant) || !Guid.TryParse(clientId, out var app) ||
        string.IsNullOrWhiteSpace(state) ||
        !Uri.TryCreate(callback, UriKind.Absolute, out var redirect) ||
        (!string.Equals(redirect.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
         !(development && redirect.Scheme == Uri.UriSchemeHttp &&
           redirect.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))) ||
        !string.IsNullOrEmpty(redirect.UserInfo) || !string.IsNullOrEmpty(redirect.Query) ||
        !string.IsNullOrEmpty(redirect.Fragment) ||
        redirect.AbsolutePath != "/auth/m365-consent/callback")
      return null;
    var uri = $"https://login.microsoftonline.com/{tenant:D}/v2.0/adminconsent" +
      $"?client_id={app:D}" +
      $"&scope={Uri.EscapeDataString("https://graph.microsoft.com/.default")}" +
      $"&redirect_uri={Uri.EscapeDataString(redirect.AbsoluteUri)}" +
      $"&state={Uri.EscapeDataString(state)}";
    return new Uri(uri);
  }
}
