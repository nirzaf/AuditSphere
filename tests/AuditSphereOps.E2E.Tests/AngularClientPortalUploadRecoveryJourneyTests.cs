using System.Security.Cryptography;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularClientPortalUploadRecoveryJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-PORTAL-UPLOAD-RESUME-E2E")]
  public async Task LostChunkAcknowledgement_ResumesExactFileFromPersistedChunkReceipt()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-PORTAL-UPLOAD-RESUME-E2E");
    var fixture = host.Fixture;
    var primary = PbcSeed.User(fixture.FirmId, "Client");
    primary.DisplayName = "Synthetic primary contact";
    Guid requestId;
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(primary);
      db.RoleGrants.Add(PbcSeed.Grant(fixture.FirmId, primary, "ClientUser", fixture.ClientId, fixture.EngagementId));
      db.ClientContacts.Add(new ClientContact
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, PracticeClientId = fixture.ClientId,
        FullName = primary.DisplayName, Email = primary.Email, Role = "Primary contact", Primary = true
      });
      await db.SaveChangesAsync();
      var staff = PbcSeed.Actor(fixture.Staff, "Staff");
      var created = await PbcService.CreateRequestAsync(db, staff, new(fixture.EngagementId, "Bank statements", "TEST",
        "2026-01-01", "2026-12-31", "Resume upload evidence", "PDF", "Totals", primary.Id, fixture.Staff.Id,
        fixture.Reviewer.Id, "2027-01-31", "Confidential", "Complete readable statements"));
      Assert.True(created.Succeeded, created.Message);
      requestId = created.Value;
      Assert.True((await PbcService.ChangeStateAsync(db, staff, new(requestId, PbcStates.Sent, 1))).Succeeded);
    }

    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    var buildPath = Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_UI_BUILD_PATH");
    if (!string.IsNullOrWhiteSpace(buildPath)) settings["AngularUi__BuildPath"] = buildPath;
    var origin = await host.StartApiForIdentityAsync(primary, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 390, Height = 844 } });
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);

    var chunkBytes = 8 * 1024 * 1024;
    var bytes = RandomNumberGenerator.GetBytes(chunkBytes + 137);
    string? firstCapability = null;
    string? resumedCapability = null;
    var intercepted = 0;
    await page.RouteAsync("**/api/pbc/uploads/*/chunks/0", async route =>
    {
      if (route.Request.Method != "POST" || Interlocked.Increment(ref intercepted) != 1)
      {
        await route.ContinueAsync();
        return;
      }
      var headers = await route.Request.AllHeadersAsync();
      firstCapability = headers.GetValueOrDefault("x-pbc-upload-capability");
      await using var accepted = await route.FetchAsync();
      Assert.Equal(200, accepted.Status);
      await route.AbortAsync("failed");
    });
    await page.RouteAsync("**/api/pbc/uploads/*/chunks/1", async route =>
    {
      if (route.Request.Method == "POST")
      {
        var headers = await route.Request.AllHeadersAsync();
        resumedCapability = headers.GetValueOrDefault("x-pbc-upload-capability");
      }
      await route.ContinueAsync();
    });

    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fportal");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Client portal", Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I will keep my sign-in private", Exact = false }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Complete first sign-in", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("First sign-in complete. Uploads are open for your requests.", new() { Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Link, new() { Name = "Resume upload evidence", Exact = true }).ClickAsync();
    await page.GetByLabel("Choose a file (up to 250 MB)", new() { Exact = true }).SetInputFilesAsync(new FilePayload
    {
      Name = "synthetic-resume.bin", MimeType = "application/octet-stream", Buffer = bytes
    });
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Upload file", Exact = true })).ToBeEnabledAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Upload file", Exact = true }).ClickAsync();

    var receipt = page.Locator("article.receipt").Filter(new() { HasText = "synthetic-resume.bin" });
    await Assertions.Expect(receipt).ToContainTextAsync($"{chunkBytes} / {bytes.Length} bytes", new() { Timeout = 20000 });
    Assert.NotNull(firstCapability);
    Assert.Contains("outcome", await page.Locator("body").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh request", Exact = true }).ClickAsync();
    await Assertions.Expect(receipt).ToContainTextAsync($"{chunkBytes} / {bytes.Length} bytes", new() { Timeout = 10000 });
    await page.GetByLabel("Choose a file (up to 250 MB)", new() { Exact = true }).SetInputFilesAsync(new FilePayload
    {
      Name = "synthetic-resume.bin", MimeType = "application/octet-stream", Buffer = bytes
    });
    var resume = receipt.GetByRole(AriaRole.Button, new() { Name = "Resume staged upload", Exact = true });
    await Assertions.Expect(resume).ToBeEnabledAsync(new() { Timeout = 10000 });
    await resume.ClickAsync();
    await Assertions.Expect(page.GetByText(
      "File bytes staged. Your audit team must verify trusted completion and suitability before the request is received or accepted.",
      new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 20000 });
    await Assertions.Expect(receipt).ToContainTextAsync($"{bytes.Length} / {bytes.Length} bytes");
    Assert.NotNull(resumedCapability);
    Assert.NotEqual(firstCapability, resumedCapability);

    await using (var proof = host.CreateDbContext())
    {
      var intent = await proof.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.PbcRequestId == requestId);
      var chunks = await proof.PbcUploadChunks.AsNoTracking().Where(x => x.PbcUploadIntentId == intent.Id)
        .OrderBy(x => x.ChunkIndex).ToListAsync();
      Assert.Equal(PbcUploadStates.Chunking, intent.State);
      Assert.Equal(bytes.Length, intent.ReceivedByteCount);
      Assert.Equal(2, chunks.Count);
      Assert.Equal(new[] { 0, 1 }, chunks.Select(x => x.ChunkIndex));
      Assert.Equal(0, chunks[0].Offset);
      Assert.Equal(chunkBytes, chunks[0].ByteCount);
      Assert.Equal(chunkBytes, chunks[1].Offset);
      Assert.Equal(bytes.Length - chunkBytes, chunks[1].ByteCount);
      Assert.Equal(Hashing.Sha256Hex(resumedCapability!), intent.CapabilityHash);
      Assert.NotEqual(Hashing.Sha256Hex(firstCapability!), intent.CapabilityHash);
      var staged = chunks.SelectMany(x => File.ReadAllBytes(x.StagedPath!)).ToArray();
      Assert.Equal(bytes, staged);
      Assert.Single(await proof.PbcUploadIntents.Where(x => x.PbcRequestId == requestId).ToListAsync());
    }
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    Assert.Empty(errors);
  }
}
