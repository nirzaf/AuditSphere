namespace AuditSphereOps.Api.Tests;

internal static class BrowserHarnessPaths
{
  private static readonly Lazy<string> Root = new(FindRepositoryRoot);

  public static string ApiContentRoot => Path.Combine(Root.Value, "src", "AuditSphereOps.Api");
  public static string AngularBuildPath => Path.Combine(Root.Value, "src", "AuditSphereOps.Ui", "dist", "auditsphere-ui", "browser");

  private static string FindRepositoryRoot()
  {
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
      if (File.Exists(Path.Combine(directory.FullName, "AuditSphere.slnx"))) return directory.FullName;
    throw new DirectoryNotFoundException("AuditSphere repository root was not found above the test assembly.");
  }
}
