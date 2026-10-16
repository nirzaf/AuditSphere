using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
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
      db.RecordsActions.Add(new RecordsAction
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ArchiveId = archiveId, ArchiveManifestId = manifestId, DesiredLabel = "SYNTH-PAGED-LABEL", DesiredProtection = "READ_ONLY",
        State = "REQUESTED", ExternalReference = "SYNTH-PAGED-REFERENCE", RequestedByUserId = f.Staff.Id, RequestedAt = now
      });
      db.LegalHolds.AddRange(new[] { "APPLIED", "RELEASED" }.Select(state => new LegalHold
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ArchiveId = archiveId, HoldReference = "SYNTHETIC-" + state, State = state, RequestedByUserId = f.Staff.Id, RequestedAt = now,
        AppliedAt = state == "APPLIED" ? now : null, ReleasedAt = state == "RELEASED" ? now : null
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
    var mutations = 0; var firstPageAttempts = 0;
    var archiveUrl = $"{origin}/api/ui/records/archives/{archiveId:D}";
    page.Request += (_, request) =>
    {
      if (request.Method == "POST" && request.Url.Contains("/api/ui/", StringComparison.Ordinal)) mutations++;
      if (request.Url == archiveUrl + "?afterOrdinal=100") firstPageAttempts++;
    };
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString($"/app/records/archives/{archiveId:D}"));
    await Assertions.Expect(page.GetByText("Showing 100 of 205 entries.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Link,
      new() { Name = "Download read-only evidence export", Exact = true })).ToHaveCountAsync(0);
    var summary = page.Locator("section[aria-labelledby='archive-summary']");
    await Assertions.Expect(summary.Locator("dt").Filter(new() { HasText = "Created" }).Locator("+ dd"))
      .ToContainTextAsync(now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture));
    await Assertions.Expect(page.GetByText("Manifest version 1 · 205 manifest entries · 1 active local holds", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(summary).ToContainTextAsync("Records action requested; this is not evidence of Purview protection.");
    var evidence = page.Locator("section[aria-labelledby='records-evidence']");
    await Assertions.Expect(evidence).ToContainTextAsync("SYNTH-PAGED-LABEL");
    await Assertions.Expect(evidence).ToContainTextAsync("SYNTH-PAGED-REFERENCE");
    await Assertions.Expect(evidence).ToContainTextAsync("Not observed");
    await Assertions.Expect(evidence).ToContainTextAsync("Observed active hold");
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, $"/app/records/archives/{archiveId:D}", "archive-detail");
    var rows = page.Locator("[aria-labelledby='archive-contents'] tbody tr");
    await Assertions.Expect(rows).ToHaveCountAsync(100);
    await Assertions.Expect(rows.First).ToContainTextAsync("item-001.pdf");
    await Assertions.Expect(rows.Last).ToContainTextAsync("item-100.pdf");
    await Assertions.Expect(rows.First.GetByRole(AriaRole.Cell)).ToHaveTextAsync(["1", "DOCUMENT", "evidence/item-001.pdf", new string('b', 64), "1"]);
    await page.RouteAsync(archiveUrl + "?afterOrdinal=100", route => route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{}" }));
    var loadMore = page.GetByRole(AriaRole.Button, new() { Name = "Load next 100 entries", Exact = true });
    await loadMore.ClickAsync();
    await Assertions.Expect(page.GetByText("Entries could not be loaded. Retry shortly.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(rows).ToHaveCountAsync(100); await Assertions.Expect(loadMore).ToBeEnabledAsync();
    Assert.Equal(1, firstPageAttempts);
    await page.UnrouteAsync(archiveUrl + "?afterOrdinal=100"); await loadMore.ClickAsync();
    await Assertions.Expect(page.GetByText("Showing 200 of 205 entries.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(rows).ToHaveCountAsync(200);
    await Assertions.Expect(rows.Nth(100)).ToContainTextAsync("item-101.pdf");
    loadMore = page.GetByRole(AriaRole.Button, new() { Name = "Load next 100 entries", Exact = true });
    await page.RouteAsync(archiveUrl + "?afterOrdinal=200", async route =>
    {
      var response = await route.FetchAsync(); var changed = JsonNode.Parse(await response.TextAsync())!;
      changed["manifestDigest"] = new string('c', 64);
      await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = changed.ToJsonString() });
    });
    await loadMore.ClickAsync();
    await Assertions.Expect(page.GetByText("The archive identity or manifest changed. Refresh the archive.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(rows).ToHaveCountAsync(200); await Assertions.Expect(loadMore).ToBeEnabledAsync();
    await page.UnrouteAsync(archiveUrl + "?afterOrdinal=200"); await loadMore.ClickAsync();
    await Assertions.Expect(page.GetByText("Showing 205 of 205 entries.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(rows).ToHaveCountAsync(205);
    await Assertions.Expect(rows.Last).ToContainTextAsync("item-205.pdf");
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Load next 100 entries", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(rows.Last.GetByRole(AriaRole.Cell)).ToHaveTextAsync(["205", "DOCUMENT", "evidence/item-205.pdf", new string('b', 64), "205"]);
    Assert.Equal(Enumerable.Range(1, 205).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)), await rows.Locator("td:first-child").AllTextContentsAsync());
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh archive", Exact = true }).ClickAsync();
    await Assertions.Expect(rows).ToHaveCountAsync(100);
    await page.RouteAsync(archiveUrl + "?afterOrdinal=100", route => route.FulfillAsync(new() { Status = 403, ContentType = "application/json", Body = "{}" }));
    await page.GetByRole(AriaRole.Button, new() { Name = "Load next 100 entries", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("The archive is not available in the current scope.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(rows).ToHaveCountAsync(0); await Assertions.Expect(summary).ToHaveCountAsync(0); await Assertions.Expect(evidence).ToHaveCountAsync(0);
    await Assertions.Expect(page.Locator("main")).Not.ToContainTextAsync("SYNTH-PAGED-REFERENCE");
    await page.UnrouteAsync(archiveUrl + "?afterOrdinal=100");
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh archive", Exact = true }).ClickAsync();
    await Assertions.Expect(rows).ToHaveCountAsync(100); await Assertions.Expect(evidence).ToContainTextAsync("SYNTH-PAGED-REFERENCE");
    await page.RouteAsync(archiveUrl, async route =>
    {
      var response = await route.FetchAsync(); var changed = JsonNode.Parse(await response.TextAsync())!;
      changed["id"] = Guid.NewGuid().ToString("D");
      await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = changed.ToJsonString() });
    });
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh archive", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Unsupported response. Refresh the page; if this persists the workspace version may be out of date.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(rows).ToHaveCountAsync(0);
    await page.UnrouteAsync(archiveUrl); await page.GetByRole(AriaRole.Button, new() { Name = "Refresh archive", Exact = true }).ClickAsync();
    await Assertions.Expect(rows).ToHaveCountAsync(100);
    Assert.Equal(0, mutations); Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "STE-GAP-007-READ-ONLY-ARCHIVE-EXPORT")]
  public async Task RecordsCustodianDownloadsReviewedManifestAndStructuredExportWithExternalBoundary()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "STE-GAP-007-READ-ONLY-ARCHIVE-EXPORT");
    var f = host.Fixture;
    var now = DateTimeOffset.UtcNow;
    var archiveId = Guid.NewGuid();
    var manifestId = Guid.NewGuid();
    const string structuredPayload = "{\"schema\":\"records-export.v1\",\"documents\":[]}";
    var structuredBytes = Encoding.UTF8.GetBytes(structuredPayload);
    var structuredHash = Hashing.Sha256Hex(structuredBytes);
    var entries = new[]
    {
      new ArchiveManifestEntry
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ArchiveManifestId = manifestId, Ordinal = 1, EntryKind = "DOCUMENT", SourceKind = "SharePoint",
        SourceId = Guid.NewGuid(), RelativeName = "evidence/signed-report.pdf", ContentHash = new string('a', 64),
        ByteCount = 1024, Required = true, MetadataJson = "{}"
      },
      new ArchiveManifestEntry
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ArchiveManifestId = manifestId, Ordinal = 2, EntryKind = "STRUCTURED_EXPORT", SourceKind = "AuditSphereOps",
        RelativeName = "structured/records-export.v1.json", ContentHash = structuredHash,
        ByteCount = structuredBytes.Length, Required = true,
        MetadataJson = "{\"schema\":\"records-export.v1\",\"storage\":\"archive_structured_exports\"}"
      }
    };
    var manifestEntriesDigest = JsonSerializer.Serialize(entries.Select(x => new
    {
      x.Ordinal, x.EntryKind, x.SourceKind, x.SourceId, x.RelativeName, x.ContentHash,
      x.ByteCount, x.Required, x.MetadataJson
    }));
    var manifestDigest = Hashing.Sha256Hex(manifestEntriesDigest);
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner"));
      db.Archives.Add(new Archive
      {
        Id = archiveId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ProfileId = "SYNTH-RECORDS", ProfileVersion = 1, Status = "ASSEMBLY_REVIEWED", CreatedAt = now
      });
      db.ArchiveManifests.Add(new ArchiveManifest
      {
        Id = manifestId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ArchiveId = archiveId, Version = 1, Status = "REVIEWED", ManifestDigest = manifestDigest,
        EntryCount = entries.Length, CompletenessStatus = "COMPLETE", BuiltAt = now,
        ReviewedAt = now, ReviewedByUserId = f.Reviewer.Id
      });
      db.ArchiveManifestEntries.AddRange(entries);
      db.ArchiveStructuredExports.Add(new ArchiveStructuredExport
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ArchiveId = archiveId, ArchiveManifestId = manifestId, Version = 1, Schema = "records-export.v1",
        PayloadJson = structuredPayload, ContentHash = structuredHash, ByteCount = structuredBytes.Length,
        CreatedAt = now
      });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(f.Reviewer, new Dictionary<string, string>
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
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString($"/app/records/archives/{archiveId:D}"));
    await Assertions.Expect(page.GetByText("Manifest version 1 · 2 manifest entries · 0 active local holds",
      new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("Provider protection remains separate; an export does not claim external immutability.",
      new() { Exact = true })).ToBeVisibleAsync();

    var exportLink = page.GetByRole(AriaRole.Link,
      new() { Name = "Download read-only evidence export", Exact = true });
    var download = await page.RunAndWaitForDownloadAsync(() => exportLink.ClickAsync());
    Assert.Contains("AuditSphere-Archive-", download.SuggestedFilename, StringComparison.Ordinal);
    using var stream = await download.CreateReadStreamAsync();
    using var memory = new MemoryStream();
    await stream.CopyToAsync(memory);
    using var exported = JsonDocument.Parse(memory.ToArray());
    var root = exported.RootElement;
    Assert.Equal("auditsphere-archive-evidence-export.v1", root.GetProperty("schema").GetString());
    Assert.Equal(manifestDigest, root.GetProperty("manifest").GetProperty("manifestDigest").GetString());
    Assert.Equal(2, root.GetProperty("manifest").GetProperty("entries").GetArrayLength());
    Assert.Equal("BLOCKED_EXTERNAL", root.GetProperty("providerProtection").GetProperty("state").GetString());
    Assert.False(root.GetProperty("providerProtection").GetProperty("externalDocumentBytesIncluded").GetBoolean());
    Assert.Equal("records-export.v1", root.GetProperty("structuredRecords").GetProperty("schema").GetString());
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
    await Assertions.Expect(page.GetByText("Manifest version 1 · 0 manifest entries · 0 active local holds", new() { Exact = true }))
      .ToBeVisibleAsync();
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
