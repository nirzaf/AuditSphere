using System.Text.Json;
using AuditSphereOps.Domain.Tests;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// RUN-005: an opt-in, loopback-only synthetic workspace for agent browser verification. It reuses
/// <see cref="OwnedHost"/> (owned PostgreSQL schema on the loopback auditsphere_tests database, external
/// effects disabled) and <see cref="SiblingClientSeed"/>, then writes a manifest of local URLs and fixture
/// identifiers without secrets. Teardown disposes only the resources this run created.
///
/// Opt in with AUDITSPHERE_SYNTHETIC_WORKSPACE_MANIFEST=&lt;repo&gt;/.local/autonomous/synthetic/manifest.json
/// (usually through scripts/agent/synthetic-workspace.py). Without it the test asserts the manifest-path
/// guard and starts nothing.
/// </summary>
[Trait("Category", "AgentTooling")]
public sealed class SyntheticWorkspaceLauncherTests(Xunit.Abstractions.ITestOutputHelper output)
{
  private const string ManifestVariable = "AUDITSPHERE_SYNTHETIC_WORKSPACE_MANIFEST";
  private const string HoldVariable = "AUDITSPHERE_SYNTHETIC_WORKSPACE_HOLD_SECONDS";
  private static readonly TimeSpan MaximumHold = TimeSpan.FromHours(4);

  [Fact]
  [Trait("CaseId", "RUN-005-SYNTHETIC-WORKSPACE")]
  public async Task SyntheticWorkspace_StartsOnlyWhenRequested_WithinTheLocalEvidenceRoot()
  {
    var repo = RepositoryRoot();
    // The guard is exercised on every run: manifests may only be written under <repo>/.local/.
    Assert.Null(ResolveManifest(repo, Path.Combine(Path.GetTempPath(), "manifest.json")));
    Assert.Null(ResolveManifest(repo, Path.Combine(repo, ".local", "..", "outside.json")));
    Assert.NotNull(ResolveManifest(repo, Path.Combine(repo, ".local", "autonomous", "synthetic", "manifest.json")));

    var requested = Environment.GetEnvironmentVariable(ManifestVariable);
    if (string.IsNullOrWhiteSpace(requested))
    {
      output.WriteLine("NOT_REQUESTED: synthetic workspace launcher is opt-in; only the path guard ran.");
      return;
    }
    var manifest = ResolveManifest(repo, requested)
      ?? throw new InvalidOperationException($"{ManifestVariable} must point inside {Path.Combine(repo, ".local")}.");
    var hold = TimeSpan.FromSeconds(Math.Clamp(
      int.TryParse(Environment.GetEnvironmentVariable(HoldVariable), out var seconds) ? seconds : 1800, 30, (int)MaximumHold.TotalSeconds));
    var stopFile = manifest + ".stop";
    File.Delete(stopFile);

    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "RUN-005-SYNTHETIC-WORKSPACE");
    var sibling = await SiblingClientSeed.SeedAsync(host.Database, host.Fixture.FirmId, "SYN-RUN005-SIBLING");
    try
    {
      var reviewerUrl = await host.StartReviewerApiAsync();
      var adminUrl = await host.StartApiForIdentityAsync(host.Fixture.Admin);
      var siblingClientUrl = await host.StartApiForIdentityAsync(sibling.Fixture.Client);
      var document = new
      {
        schemaVersion = 1,
        caseId = "RUN-005-SYNTHETIC-WORKSPACE",
        startedAtUtc = DateTimeOffset.UtcNow,
        expiresAtUtc = DateTimeOffset.UtcNow + hold,
        stopFile,
        externalEffects = "disabled (Test composition)",
        database = "loopback auditsphere_tests, owned schema dropped on teardown",
        synthetic = true,
        signIn = "Open <actor url>/auth/sign-in?returnUrl=/app; each loopback host signs in only its own synthetic identity.",
        notice = "Synthetic test identities only; never professional approval or production evidence.",
        actors = new object[]
        {
          new { role = "staff-preparer", url = host.StaffUrl, userId = host.Fixture.Staff.Id, clientId = host.Fixture.ClientId },
          new { role = "reviewer", url = reviewerUrl, userId = host.Fixture.Reviewer.Id, clientId = host.Fixture.ClientId },
          new { role = "administrator", url = adminUrl, userId = host.Fixture.Admin.Id, clientId = host.Fixture.ClientId },
          new { role = "client-user-A", url = host.ClientUrl, userId = host.Fixture.Client.Id, clientId = host.Fixture.ClientId },
          new { role = "client-user-B", url = siblingClientUrl, userId = sibling.Fixture.Client.Id, clientId = sibling.Fixture.ClientId },
        },
        fixture = new
        {
          firmId = host.Fixture.FirmId,
          clientA = new { clientId = host.Fixture.ClientId, engagementId = host.Fixture.EngagementId, pbcRequestId = host.RequestId },
          clientB = new
          {
            clientId = sibling.Fixture.ClientId, engagementId = sibling.Fixture.EngagementId, periodId = sibling.PeriodId,
            packageId = sibling.PackageId, mappingId = sibling.MappingId, journalId = sibling.JournalId,
          },
          notSeeded = new[] { "consolidation group (use the existing consolidation journey fixtures)" },
        },
      };
      Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
      var temporary = manifest + ".tmp";
      await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
      File.Move(temporary, manifest, overwrite: true);
      output.WriteLine($"READY: {manifest}");

      var deadline = DateTimeOffset.UtcNow + hold;
      while (DateTimeOffset.UtcNow < deadline && !File.Exists(stopFile))
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
    finally
    {
      File.Delete(manifest);
      File.Delete(stopFile);
      PbcSeed.DeleteDirectory(sibling.StagingRoot);
    }
  }

  internal static string? ResolveManifest(string repo, string candidate)
  {
    var root = Path.GetFullPath(Path.Combine(repo, ".local")) + Path.DirectorySeparatorChar;
    var full = Path.GetFullPath(candidate);
    return full.StartsWith(root, StringComparison.Ordinal) && full.EndsWith(".json", StringComparison.Ordinal) ? full : null;
  }

  private static string RepositoryRoot()
  {
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
      if (File.Exists(Path.Combine(dir.FullName, "AuditSphereOps.slnx"))) return dir.FullName;
    throw new InvalidOperationException("Repository root (AuditSphereOps.slnx) not found.");
  }
}
