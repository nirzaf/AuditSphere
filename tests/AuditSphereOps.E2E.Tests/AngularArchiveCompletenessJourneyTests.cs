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
  [Trait("CaseId", "AS-PAR-002-ANGULAR-ARCHIVE-PAGING-01")]
  public async Task LargeManifestLoadsInBoundedOrderedPages()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-ARCHIVE-PAGING-01");
    var f = host.Fixture;
    var now = DateTimeOffset.UtcNow;
    var archiveId = Guid.NewGuid();
    var manifestId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.Archives.Add(new Archive
      {
        Id = archiveId, FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, ProfileId = "SYNTH-PAGED-ARCHIVE", ProfileVersion = 1,
        Status = ArchiveStates.AssemblyInProgress, CreatedAt = now
      });
      db.ArchiveManifests.Add(new ArchiveManifest
      {
        Id = manifestId, FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, ArchiveId = archiveId, Version = 1,
        Status = "BUILT", ManifestDigest = new string('a', 64), EntryCount = 205,
        CompletenessStatus = "COMPLETE", BuiltAt = now
      });
      db.ArchiveManifestEntries.AddRange(Enumerable.Range(1, 205).Select(ordinal => new ArchiveManifestEntry
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, ArchiveManifestId = manifestId, Ordinal = ordinal,
        EntryKind = "DOCUMENT", SourceKind = "TEST", RelativeName = $"evidence/item-{ordinal:D3}.pdf",
        ContentHash = new string('b', 64), ByteCount = ordinal
      }));
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
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString($"/app/records/archives/{archiveId:D}"));
    await Assertions.Expect(page.GetByText("Showing 100 of 205 entries.", new() { Exact = true })).ToBeVisibleAsync();
    var rows = page.Locator("[aria-labelledby='archive-contents'] tbody tr");
    await Assertions.Expect(rows).ToHaveCountAsync(100);
    await Assertions.Expect(rows.First).ToContainTextAsync("item-001.pdf");
    await Assertions.Expect(rows.Last).ToContainTextAsync("item-100.pdf");
    var loadMore = page.GetByRole(AriaRole.Button, new() { Name = "Load next 100 entries", Exact = true });
    await loadMore.ClickAsync();
    await Assertions.Expect(page.GetByText("Showing 200 of 205 entries.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(rows).ToHaveCountAsync(200);
    await Assertions.Expect(rows.Nth(100)).ToContainTextAsync("item-101.pdf");
    loadMore = page.GetByRole(AriaRole.Button, new() { Name = "Load next 100 entries", Exact = true });
    await loadMore.ClickAsync();
    await Assertions.Expect(page.GetByText("Showing 205 of 205 entries.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(rows).ToHaveCountAsync(205);
    await Assertions.Expect(rows.Last).ToContainTextAsync("item-205.pdf");
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Load next 100 entries", Exact = true })).ToHaveCountAsync(0);
    Assert.Empty(errors);
  }

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
