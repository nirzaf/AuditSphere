using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularClientPortalJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-CLIENT-PORTAL-E2E")]
  public async Task ClientPortal_FirstSignIn_Delegation_Conversation_Upload_AndRevocation()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-CLIENT-PORTAL-E2E");
    var f = host.Fixture; Guid requestId; Guid colleagueId;
    var primary = PbcSeed.User(f.FirmId, "Client"); primary.DisplayName = "Management";
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(primary); db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, primary, "ClientUser", f.ClientId, f.EngagementId));
      var colleague = PbcSeed.User(f.FirmId, "Client"); colleague.DisplayName = "Finance Colleague"; colleagueId = colleague.Id;
      db.Users.Add(colleague); db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, colleague, "ClientUser", f.ClientId, f.EngagementId));
      db.ClientContacts.Add(new ClientContact { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        FullName = "Management", Email = primary.Email, Role = "Primary contact", Primary = true });
      await db.SaveChangesAsync();
      var staff = PbcSeed.Actor(f.Staff, "Staff");
      var created = await PbcService.CreateRequestAsync(db, staff, new(f.EngagementId, "Bank statements", "TEST", "2026-01-01", "2026-12-31", "Angular bank evidence", "PDF", "Totals",
        primary.Id, f.Staff.Id, f.Reviewer.Id, "2027-01-31", "Confidential", "Complete readable statements"));
      Assert.True(created.Succeeded, created.Message); requestId = created.Value;
      Assert.True((await PbcService.ChangeStateAsync(db, staff, new(requestId, PbcStates.Sent, 1))).Succeeded);
    }
    var origin = await host.StartApiForIdentityAsync(primary, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 390, Height = 844 } });
    var page = await context.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fportal");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Client portal", Exact = true })).ToBeVisibleAsync();
    Assert.DoesNotContain("Portfolio", await page.Locator("aside").InnerTextAsync());
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I will keep my sign-in private", Exact = false }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Complete first sign-in", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("First sign-in complete. Uploads are open for your requests.")).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Link, new() { Name = "Angular bank evidence", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Delegate this request", Exact = true })).ToBeVisibleAsync();
    await page.GetByLabel("Colleague", new() { Exact = true }).SelectOptionAsync(colleagueId.ToString());
    await page.GetByRole(AriaRole.Button, new() { Name = "Delegate", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Delegation recorded.", new() { Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Revoke delegation for Finance Colleague", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Delegation revoked.", new() { Exact = true })).ToBeVisibleAsync();
    await page.GetByLabel("Reply to the audit team", new() { Exact = true }).FillAsync("The requested statements are attached.");
    await page.GetByRole(AriaRole.Button, new() { Name = "Send reply", Exact = true }).ClickAsync();
    await Assertions.Expect(page.Locator(".conversation")).ToContainTextAsync("The requested statements are attached.");
    var bytes = System.Text.Encoding.UTF8.GetBytes("Synthetic bank evidence from Angular portal.");
    await page.Locator(".drop-zone").EvaluateAsync(@"zone => {
      const transfer = new DataTransfer();
      transfer.items.add(new File([new TextEncoder().encode('Synthetic bank evidence from Angular portal.')],
        'angular-client-evidence.txt', { type: 'text/plain' }));
      zone.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: transfer }));
    }");
    await Assertions.Expect(page.GetByText($"angular-client-evidence.txt · {bytes.Length} bytes", new() { Exact = true }))
      .ToBeVisibleAsync();
    await Assertions.Expect(page.Locator(".fingerprint").First)
      .ToContainTextAsync(AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(bytes));
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Upload file", Exact = true })).ToBeEnabledAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Upload file", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("File bytes staged. Your audit team must verify trusted completion and suitability before the request is received or accepted.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator(".receipt")).ToContainTextAsync("angular-client-evidence.txt");
    await using (var db = host.CreateDbContext())
    {
      var upload = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.PbcRequestId == requestId);
      Assert.Equal(PbcUploadStates.Chunking, upload.State); Assert.Equal(bytes.Length, upload.ReceivedByteCount);
      Assert.Equal(AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(bytes), upload.DeclaredSha256Hex);
      var chunks = await db.PbcUploadChunks.AsNoTracking().Where(x => x.PbcUploadIntentId == upload.Id).ToListAsync();
      Assert.Single(chunks); Assert.Equal(bytes.Length, chunks[0].ByteCount);
      Assert.False(await db.PbcRequestDelegations.AnyAsync(x => x.PbcRequestId == requestId && x.RevokedAt == null));
    }
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    await page.GotoAsync(origin + "/ui/portal/requests/" + Guid.NewGuid());
    await Assertions.Expect(page.GetByText("This request is not available in your current access scope.", new() { Exact = true })).ToBeVisibleAsync();
    await page.GotoAsync(origin + "/ui/portal/requests/" + requestId);
    await Assertions.Expect(page.GetByText("Complete readable statements", new() { Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
      await db.Users.Where(x => x.Id == primary.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.Disabled, true).SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
    Assert.DoesNotContain("Complete readable statements", await page.Locator("body").InnerTextAsync());
    Assert.Empty(errors);
  }
}
