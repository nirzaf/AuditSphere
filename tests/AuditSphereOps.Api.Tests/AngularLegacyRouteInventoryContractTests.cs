using System.Text.Json;
using System.Text.RegularExpressions;
using AuditSphereOps.Api.Ui;

namespace AuditSphereOps.Api.Tests;

public sealed class AngularLegacyRouteInventoryContractTests
{
  private static string RepositoryRoot()
  {
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root is not null && !File.Exists(Path.Combine(root.FullName, "AuditSphereOps.slnx"))) root = root.Parent;
    return root?.FullName ?? throw new InvalidOperationException("Repository root is required for the route inventory contract.");
  }

  [Fact]
  public void EveryDiscoveredLegacyWorkspaceRouteHasExplicitNativeOwnership()
  {
    using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(),
      "docs", "execution", "angular-source-inventory.json")));
    var routes = inventory.RootElement.GetProperty("items").EnumerateArray()
      .SelectMany(item => item.GetProperty("routes").EnumerateArray())
      .Select(route => route.GetString()!)
      .Where(route => route is not "/" and not "/auth/access-not-assigned")
      .Distinct(StringComparer.Ordinal)
      .ToArray();

    static string Normalize(string route) => Regex.Replace(route, "\\{[^}]+\\}", ":id").TrimEnd('/');
    var native = UiEndpoints.SpaRoutes
      .Select(route => Normalize(route[3..]))
      .ToHashSet(StringComparer.Ordinal);

    Assert.NotEmpty(routes);
    foreach (var route in routes)
      Assert.Contains(Normalize(route), native);

    // This is route ownership only; action and behavior parity remain separate gates.
    Assert.DoesNotContain(UiEndpoints.SpaRoutes, route => route.StartsWith("/ui/auth", StringComparison.Ordinal));
  }
}
