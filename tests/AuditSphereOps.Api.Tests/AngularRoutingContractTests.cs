using System.Text.RegularExpressions;
using AuditSphereOps.Api.Ui;

namespace AuditSphereOps.Api.Tests;

public sealed class AngularRoutingContractTests
{
  private static string RepositoryRoot()
  {
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root is not null && !File.Exists(Path.Combine(root.FullName, "AuditSphereOps.slnx"))) root = root.Parent;
    return root?.FullName ?? throw new InvalidOperationException("Repository root is required for the route contract check.");
  }

  [Fact]
  public void ApiDeepLinks_ExactlyMatchTheAngularRouteCatalogue()
  {
    var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AuditSphereOps.Ui", "src", "app", "app.routes.ts"));
    var expected = Regex.Matches(source, "path:\\s*'([^']*)'").Select(match => match.Groups[1].Value)
      .Where(path => path.Length > 0).Select(path => "/ui/" + path.Replace(":id", "{id:guid}", StringComparison.Ordinal).Replace(":kind", "{kind}", StringComparison.Ordinal))
      .Append("/ui").Append("/ui/").Order(StringComparer.Ordinal).ToArray();
    Assert.Equal(expected, UiEndpoints.SpaRoutes.Order(StringComparer.Ordinal).ToArray());
    Assert.DoesNotContain(UiEndpoints.SpaRoutes, route => route.Contains('*') || route.StartsWith("/api", StringComparison.Ordinal));
  }

  [Fact]
  public void AngularNavigation_NeverPointsAtLegacyBlazorWorkbenches()
  {
    var root = Path.Combine(RepositoryRoot(), "src", "AuditSphereOps.Ui", "src", "app");
    var legacy = new Regex("""(?:href|\[href\])\s*=\s*"(?:/app|/portal|'/app|'/portal)""");
    foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
      .Where(path => path.EndsWith(".ts", StringComparison.Ordinal) || path.EndsWith(".html", StringComparison.Ordinal)))
      Assert.False(legacy.IsMatch(File.ReadAllText(path)), $"Legacy navigation link in {Path.GetRelativePath(root, path)}");
  }
}
