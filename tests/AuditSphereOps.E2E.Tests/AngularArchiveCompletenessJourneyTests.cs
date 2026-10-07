using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularArchiveCompletenessJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-ARCHIVE-INCOMPLETE-01")]
  public async Task IncompleteEmptyManifestShowsBlockerAndNoEntries()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-ARCHIVE-INCOMPLETE-01");
    var f = host.Fixture;
    var now = DateTimeOffset.UtcNow;
    var archiveId = Guid.NewGuid();
    const string profile = "SYNTH-INCOMPLETE-ARCHIVE";
    const string reason = "Required source artifact is unavailable.";
    var digest = new string('d', 64);
    await using (var db = host.CreateDbContext())
    {
      db.Archives.Add(new Archive
      {
        Id = archiveId, FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, ProfileId = profile, ProfileVersion = 3,
        Status = ArchiveStates.AssemblyInProgress, CreatedAt = now
      });
      db.ArchiveManifests.Add(new ArchiveManifest
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, ArchiveId = archiveId, Version = 1,
        Status = "BUILT", ManifestDigest = digest, EntryCount = 0,
        CompletenessStatus = "INCOMPLETE", CompletenessException = reason, BuiltAt = now
      });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(f.Staff, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    page.Console += (_, message) =>
    {
      if (message.Type == "error") errors.Add($"console-error: {message.Text}");
    };

    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString($"/app/records/archives/{archiveId:D}"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Records archive", Exact = true }))
      .ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync($"Manifest incomplete: {reason}");
    await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Manifest version 1 · 0 manifest entries · 0 active local holds");
    await Assertions.Expect(page.GetByRole(AriaRole.Cell, new() { Name = "No persisted manifest entries.", Exact = true }))
      .ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText($"{profile} v3", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText(digest, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Archive manifest", Exact = true })
      .GetByText("Not observed", new() { Exact = true })).ToBeVisibleAsync();
    var evidence = page.GetByRole(AriaRole.Region, new() { Name = "Records evidence", Exact = true });
    await Assertions.Expect(evidence.GetByText("Not observed", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(evidence.GetByText("Not requested", new() { Exact = true })).ToHaveCountAsync(2);
    var text = await page.Locator("main").InnerTextAsync();
    Assert.DoesNotContain("Archive complete", text, StringComparison.OrdinalIgnoreCase);
    Assert.Empty(errors);
  }
}
