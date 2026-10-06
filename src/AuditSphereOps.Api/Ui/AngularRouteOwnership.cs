using System.Text.RegularExpressions;

namespace AuditSphereOps.Api.Ui;

/// <summary>Deployment-owned route selection. Browser state never changes the HTTP owner.</summary>
public static class AngularRouteOwnership
{
  /// <summary>Only approved fingerprinted assets are eligible for current/previous build serving or immutable caching.</summary>
  public static readonly Regex FingerprintedAsset = new(@"^[A-Za-z0-9_-]+-[A-Za-z0-9_-]{8,}\.(js|css|woff2?|svg|png|webp)$",
    RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

  public static bool Canonical(IConfiguration configuration) => configuration.GetValue<bool>("AngularUi:CanonicalRoutes");
  public static string Destination(IConfiguration configuration, string nativePath) =>
    (Canonical(configuration) ? "" : "/ui") + nativePath;

  /// <summary>
  /// Resolves the approved Angular build directory (configured path, published ui folder, or the
  /// development dist output). Shared by route serving, static caching and boundary CSP hashing.
  /// </summary>
  public static string? ResolveBuildRoot(IConfiguration configuration, IWebHostEnvironment environment)
  {
    if (!configuration.GetValue<bool>("AngularUi:Enabled"))
      return null;
    var published = Path.Combine(environment.ContentRootPath, "ui");
    return Path.GetFullPath(configuration["AngularUi:BuildPath"] ??
      (File.Exists(Path.Combine(published, "index.html")) ? published :
        Path.Combine(environment.ContentRootPath, "../AuditSphereOps.Ui/dist/auditsphere-ui/browser")));
  }
}
