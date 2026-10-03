namespace AuditSphereOps.Api.Ui;

/// <summary>Deployment-owned route selection. Browser state never changes the HTTP owner.</summary>
public static class AngularRouteOwnership
{
  public static bool Canonical(IConfiguration configuration) => configuration.GetValue<bool>("AngularUi:CanonicalRoutes");
  public static string Destination(IConfiguration configuration, string nativePath) =>
    (Canonical(configuration) ? "" : "/ui") + nativePath;
}
