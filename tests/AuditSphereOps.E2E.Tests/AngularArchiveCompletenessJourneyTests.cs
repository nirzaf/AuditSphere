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
    await Assertions.Expect(page.GetByText("Manifest version 1 · 205 manifest entries · 1 unreleased local hold", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(summary).ToContainTextAsync("Records action requested; this is not evidence of Purview protection.");
    var evidence = page.Locator("section[aria-labelledby='records-evidence']");
    await Assertions.Expect(evidence).ToContainTextAsync("SYNTH-PAGED-LABEL");
    await Assertions.Expect(evidence).ToContainTextAsync("SYNTH-PAGED-REFERENCE");
    await Assertions.Expect(evidence).ToContainTextAsync("Not observed");
    await Assertions.Expect(evidence).ToContainTextAsync("Unreleased local hold");
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
    var structuredPayload = JsonSerializer.Serialize(new
    {
      schema = "records-export.v2",
      audience = new
      {
        scope = "ENGAGEMENT_ONLY", firmId = f.FirmId, clientId = f.ClientId,
        engagementId = f.EngagementId, groupWideDataIncluded = false
      },
      scope = new { firmId = f.FirmId, clientId = f.ClientId, engagementId = f.EngagementId },
      documents = Array.Empty<object>()
    });
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
        RelativeName = "structured/records-export.v2.json", ContentHash = structuredHash,
        ByteCount = structuredBytes.Length, Required = true,
        MetadataJson = "{\"schema\":\"records-export.v2\",\"storage\":\"archive_structured_exports\",\"audience\":\"ENGAGEMENT_ONLY\"}"
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
        ArchiveId = archiveId, Version = 1, Status = "BUILT", ManifestDigest = manifestDigest,
        EntryCount = entries.Length, CompletenessStatus = "COMPLETE", BuiltAt = now
      });
      db.ArchiveManifestEntries.AddRange(entries);
      db.ArchiveStructuredExports.Add(new ArchiveStructuredExport
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ArchiveId = archiveId, ArchiveManifestId = manifestId, Version = 1, Schema = "records-export.v2",
        PayloadJson = structuredPayload, ContentHash = structuredHash, ByteCount = structuredBytes.Length,
        CreatedAt = now
      });
      await db.SaveChangesAsync();
      var manifest = await db.ArchiveManifests.SingleAsync(x => x.Id == manifestId);
      manifest.Status = "REVIEWED";
      manifest.ReviewedAt = now;
      manifest.ReviewedByUserId = f.Reviewer.Id;
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
    await Assertions.Expect(page.GetByText("Manifest version 1 · 2 manifest entries · 0 unreleased local holds",
      new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("The export reports Purview protection and the SharePoint secondary access-copy observation as separate evidence. It does not claim external immutability.",
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
    Assert.Equal("records-export.v2", root.GetProperty("structuredRecords").GetProperty("schema").GetString());
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
      db.LegalHolds.Add(new LegalHold
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ArchiveId = archiveId, HoldReference = "SYNTHETIC-RELEASED-ONLY", State = "RELEASED",
        RequestedByUserId = f.Staff.Id, RequestedAt = now, ReleasedAt = now
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
    var archiveRequests = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    page.Request += (_, request) =>
    {
      if (request.Method == "GET" && new Uri(request.Url).AbsolutePath.StartsWith("/api/ui/records/archives/", StringComparison.Ordinal))
        archiveRequests.Add(new Uri(request.Url).AbsolutePath);
    };
    page.Response += (_, response) =>
    {
      if (response.Status >= 400) errors.Add($"http-response: {response.Status} {response.Url}");
    };
    page.Console += (_, message) =>
    {
      if (message.Type == "error") errors.Add($"console-error: {message.Text}");
    };

    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString($"/app/records/archives/{archiveId:D}"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Records archive", Exact = true }))
      .ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText($"Manifest incomplete: {reason}", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("Manifest version 1 · 0 manifest entries · 0 unreleased local holds", new() { Exact = true }))
      .ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Cell, new() { Name = "No persisted manifest entries.", Exact = true }))
      .ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText($"{profile} v3", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText(digest, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Archive manifest", Exact = true })
      .GetByText("Not observed", new() { Exact = true })).ToBeVisibleAsync();
    var evidence = page.GetByRole(AriaRole.Region, new() { Name = "Records evidence", Exact = true });
    await Assertions.Expect(evidence.GetByText("Not observed", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(evidence.GetByText("No unreleased local hold", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(evidence.GetByText("Not requested", new() { Exact = true })).ToHaveCountAsync(2);
    var text = await page.Locator("main").InnerTextAsync();
    Assert.DoesNotContain("Archive complete", text, StringComparison.OrdinalIgnoreCase);
    Assert.NotEmpty(archiveRequests);
    Assert.All(archiveRequests, path => Assert.Equal($"/api/ui/records/archives/{archiveId:D}", path));
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "PMS-R10-NATIVE-CHROME-REVIEW-RECOVERY")]
  public async Task LostReviewOutcomeRecoversAfterReloadWithoutClearingOtherIntent()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "PMS-R10-NATIVE-CHROME-REVIEW-RECOVERY");
    var f = host.Fixture;
    var now = DateTimeOffset.UtcNow;
    var archiveId = Guid.NewGuid();
    var manifestId = Guid.NewGuid();
    var digest = Hashing.Sha256Hex(JsonSerializer.Serialize(Array.Empty<object>()));
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner", f.ClientId, f.EngagementId));
      db.Archives.Add(new Archive
      {
        Id = archiveId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ProfileId = "SYNTH-R10-RECOVERY", ProfileVersion = 1, Status = ArchiveStates.ManifestBuilt, CreatedAt = now
      });
      db.ArchiveManifests.Add(new ArchiveManifest
      {
        Id = manifestId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ArchiveId = archiveId, Version = 1, Status = "BUILT", ManifestDigest = digest,
        EntryCount = 0, CompletenessStatus = "COMPLETE", BuiltAt = now
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
    var pageErrors = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);
    var archiveUrl = $"{origin}/api/ui/records/archives/{archiveId:D}";
    var reviewUrl = archiveUrl + "/review";
    var archiveReads = 0;
    page.Request += (_, request) =>
    {
      if (request.Method == "GET" && request.Url == archiveUrl) archiveReads++;
    };

    var route = $"/app/records/archives/{archiveId:D}";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Records archive", Exact = true }))
      .ToBeVisibleAsync();
    var reviewButton = page.GetByRole(AriaRole.Button, new() { Name = "Review exact manifest", Exact = true });
    await Assertions.Expect(reviewButton).ToBeEnabledAsync();

    var firstBody = "";
    var replayBody = "";
    var firstReceipt = "";
    var replayReceipt = "";
    var postCount = 0;
    await page.RouteAsync(reviewUrl, async interception =>
    {
      if (interception.Request.Method != "POST")
      {
        await interception.ContinueAsync();
        return;
      }

      postCount++;
      if (postCount == 1)
      {
        firstBody = interception.Request.PostData ?? "";
        await using var committed = await interception.FetchAsync();
        Assert.Equal(200, committed.Status);
        firstReceipt = await committed.TextAsync();
        // The real API commits the review; only its response is lost to the browser.
        await interception.AbortAsync("failed");
        return;
      }

      replayBody = interception.Request.PostData ?? "";
      await using var replayed = await interception.FetchAsync();
      Assert.Equal(200, replayed.Status);
      replayReceipt = await replayed.TextAsync();
      await interception.FulfillAsync(new() { Response = replayed });
    });

    await reviewButton.ClickAsync();
    var unconfirmed = "An original review request is unconfirmed. Recover it before starting another action.";
    var retry = page.GetByRole(AriaRole.Button, new() { Name = "Retry original review", Exact = true });
    await Assertions.Expect(page.GetByText(unconfirmed, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(retry).ToBeEnabledAsync();
    Assert.Equal(1, postCount);
    Assert.NotEmpty(firstBody);

    var storageKey = $"auditsphere-archive-intent-v1:{f.FirmId:D}:{f.Reviewer.Id:D}:{f.EngagementId:D}:{archiveId:D}";
    var retained = await page.EvaluateAsync<string?>("key => sessionStorage.getItem(key)", storageKey);
    Assert.NotNull(retained);
    using var originalIntent = JsonDocument.Parse(retained);
    var requestKey = originalIntent.RootElement.GetProperty("requestKey").GetString();
    Assert.False(string.IsNullOrWhiteSpace(requestKey));
    using (var receipt = JsonDocument.Parse(firstReceipt))
      Assert.Equal("REVIEWED", receipt.RootElement.GetProperty("value").GetProperty("status").GetString());

    var sessionBeforeText = await page.EvaluateAsync<string>(
      "async () => await (await fetch('/api/ui/session', { cache: 'no-store' })).text()");
    using var sessionBefore = JsonDocument.Parse(sessionBeforeText);
    Assert.Equal(f.Reviewer.Id, sessionBefore.RootElement.GetProperty("userId").GetGuid());
    var generationBefore = sessionBefore.RootElement.GetProperty("generation").GetString();

    DateTimeOffset? firstReviewedAt;
    await using (var db = host.CreateDbContext())
    {
      var manifest = await db.ArchiveManifests.SingleAsync(x => x.Id == manifestId);
      Assert.Equal("REVIEWED", manifest.Status);
      Assert.Equal(requestKey, manifest.ReviewRequestKey);
      Assert.Equal(f.Reviewer.Id, manifest.ReviewedByUserId);
      firstReviewedAt = manifest.ReviewedAt;
      await db.Users.Where(x => x.Id == f.Reviewer.Id).ExecuteUpdateAsync(x =>
        x.SetProperty(user => user.SessionEpoch, user => user.SessionEpoch + 1));
    }

    await page.ReloadAsync();
    Assert.Equal(retained, await page.EvaluateAsync<string?>("key => sessionStorage.getItem(key)", storageKey));
    await page.GotoAsync(origin + "/auth/sign-out");
    Assert.Equal(retained, await page.EvaluateAsync<string?>("key => sessionStorage.getItem(key)", storageKey));
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await Assertions.Expect(page.GetByText(unconfirmed, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button,
      new() { Name = "Review exact manifest", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(page.GetByRole(AriaRole.Button,
      new() { Name = "Retry original review", Exact = true })).ToBeEnabledAsync();
    var sessionAfterText = await page.EvaluateAsync<string>(
      "async () => await (await fetch('/api/ui/session', { cache: 'no-store' })).text()");
    using var sessionAfter = JsonDocument.Parse(sessionAfterText);
    Assert.Equal(f.Reviewer.Id, sessionAfter.RootElement.GetProperty("userId").GetGuid());
    Assert.NotEqual(generationBefore, sessionAfter.RootElement.GetProperty("generation").GetString());

    var otherOrigin = await host.StartApiForIdentityAsync(f.Client, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    });
    await page.GotoAsync(otherOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    var replacementSessionText = await page.EvaluateAsync<string>(
      "async () => await (await fetch('/api/ui/session', { cache: 'no-store' })).text()");
    using (var replacementSession = JsonDocument.Parse(replacementSessionText))
      Assert.Equal(f.Client.Id, replacementSession.RootElement.GetProperty("userId").GetGuid());
    await page.GotoAsync(origin + route);
    var currentActorText = await page.EvaluateAsync<string>(
      "async () => await (await fetch('/api/ui/session', { cache: 'no-store' })).text()");
    using (var currentActor = JsonDocument.Parse(currentActorText))
      Assert.Equal(f.Client.Id, currentActor.RootElement.GetProperty("userId").GetGuid());
    await Assertions.Expect(page.GetByRole(AriaRole.Button,
      new() { Name = "Retry original review", Exact = true })).ToHaveCountAsync(0);
    Assert.Equal(retained, await page.EvaluateAsync<string?>("key => sessionStorage.getItem(key)", storageKey));

    await page.GotoAsync(origin + "/auth/sign-out");
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await Assertions.Expect(page.GetByText(unconfirmed, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button,
      new() { Name = "Retry original review", Exact = true })).ToBeEnabledAsync();

    var unrelatedArchiveId = Guid.NewGuid();
    var unrelatedKey = $"auditsphere-archive-intent-v1:{f.FirmId:D}:{f.Reviewer.Id:D}:{f.EngagementId:D}:{unrelatedArchiveId:D}";
    var unrelatedRaw = JsonSerializer.Serialize(new
    {
      version = 1, firmId = f.FirmId, actorUserId = f.Reviewer.Id, engagementId = f.EngagementId,
      archiveId = unrelatedArchiveId, action = "build", requestKey = Guid.NewGuid(),
      commercialBundleId = (Guid?)null, manifestId = (Guid?)null,
      manifestVersion = (long?)null, manifestDigest = (string?)null
    });
    await page.EvaluateAsync("x => sessionStorage.setItem(x.key, x.raw)", new { key = unrelatedKey, raw = unrelatedRaw });
    var readsBeforeRecovery = archiveReads;
    await page.GetByRole(AriaRole.Button, new() { Name = "Retry original review", Exact = true }).ClickAsync();

    await Assertions.Expect(page.GetByText(unconfirmed, new() { Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(page.GetByRole(AriaRole.Button,
      new() { Name = "Retry original review", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(page.Locator("audit-status")).ToContainTextAsync("assembly reviewed");
    Assert.True(archiveReads > readsBeforeRecovery, "The terminal receipt must refresh the archive from the API.");
    Assert.Equal(2, postCount);
    Assert.Equal(firstBody, replayBody);
    Assert.Equal(firstReceipt, replayReceipt);
    using (var replayRequest = JsonDocument.Parse(replayBody))
      Assert.Equal(requestKey, replayRequest.RootElement.GetProperty("requestKey").GetString());
    Assert.Null(await page.EvaluateAsync<string?>("key => sessionStorage.getItem(key)", storageKey));
    Assert.Equal(unrelatedRaw, await page.EvaluateAsync<string?>("key => sessionStorage.getItem(key)", unrelatedKey));

    await using (var db = host.CreateDbContext())
    {
      Assert.Equal(1, await db.ArchiveManifests.CountAsync(x => x.ArchiveId == archiveId));
      var manifest = await db.ArchiveManifests.SingleAsync(x => x.Id == manifestId);
      Assert.Equal("REVIEWED", manifest.Status);
      Assert.Equal(requestKey, manifest.ReviewRequestKey);
      Assert.Equal(f.Reviewer.Id, manifest.ReviewedByUserId);
      Assert.Equal(firstReviewedAt, manifest.ReviewedAt);
      Assert.Equal(ArchiveStates.AssemblyReviewed,
        await db.Archives.Where(x => x.Id == archiveId).Select(x => x.Status).SingleAsync());
    }
    Assert.Empty(pageErrors);
  }
}
