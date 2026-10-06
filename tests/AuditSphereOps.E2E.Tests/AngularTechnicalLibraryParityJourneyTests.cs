using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AngularMigrationParity")]
public sealed class AngularTechnicalLibraryParityJourneyTests
{
  [Fact]
  public async Task AngularFormsCreateAndReviseEntries_RequireIndependentPublishing_AndEnforceAudience()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-LIBRARY-PARITY");
    var firmId = host.Fixture.FirmId;
    var manager = PbcSeed.User(firmId, "Staff");
    manager.DisplayName = "Library Manager";
    var partner = PbcSeed.User(firmId, "Staff");
    partner.DisplayName = "Library Partner";
    var staff = PbcSeed.User(firmId, "Staff");
    staff.DisplayName = "Library Staff";
    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(manager, partner, staff);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(firmId, manager, "Manager"),
        PbcSeed.Grant(firmId, partner, "Partner"),
        PbcSeed.Grant(firmId, staff, "Staff"));
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var pageErrors = new List<string>();
    async Task<(IPage Page, string Origin)> SignInAsync(Domain.Security.AppUser user, string path)
    {
      var origin = await host.StartApiForIdentityAsync(user);
      var page = await (await browser.NewContextAsync()).NewPageAsync();
      page.PageError += (_, error) => pageErrors.Add(error);
      await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(path)}");
      await page.GetByRole(AriaRole.Heading, new() { Name = "Technical library", Exact = true })
        .WaitForAsync(new() { Timeout = 20000 });
      await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 });
      return (page, origin);
    }

    var code = $"MIG-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
    const string title = "Migration parity guide";
    const string bodyV1 = "Original firm guidance for the migration parity journey.";
    const string bodyV2 = "Revised firm guidance with retained migration history.";
    const string restrictedCode = "MIG-LEADERSHIP";
    const string restrictedTitle = "Leadership only migration note";
    var (managerPage, managerOrigin) = await SignInAsync(manager, "/app/library");
    await managerPage.GetByText("Add an entry (Manager, Partner or administrator)", new() { Exact = true }).ClickAsync();
    await managerPage.Locator("input[name='code']").FillAsync(code);
    await managerPage.Locator("input[name='title']").FillAsync(title);
    await managerPage.Locator("select[name='category']").SelectOptionAsync("ISA");
    await managerPage.Locator("select[name='audience']").SelectOptionAsync("ALL_STAFF");
    await managerPage.Locator("textarea[name='body']").FillAsync(bodyV1);
    await managerPage.Locator("input[name='source']").FillAsync("Internal assurance methodology v1");
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Create draft entry", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.Locator(".command-result").Last)
      .ToContainTextAsync("Entry created as a draft");

    Guid documentId;
    Guid draftV1Id;
    await using (var db = host.CreateDbContext())
    {
      var document = await db.TechnicalLibraryDocuments.AsNoTracking().SingleAsync(x => x.FirmId == firmId && x.Code == code);
      documentId = document.Id;
      var version = await db.TechnicalLibraryVersions.AsNoTracking().SingleAsync(x => x.DocumentId == documentId);
      draftV1Id = version.Id;
      Assert.Equal(bodyV1, version.Body);
      Assert.Equal("DRAFT", version.Status);
      Assert.Equal(manager.Id, version.PreparedByUserId);
      Assert.Equal(AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(bodyV1), version.ContentSha256);
    }

    // A duplicate code is rejected by the Application command and leaves the original evidence untouched.
    await managerPage.Locator("input[name='code']").FillAsync(code);
    await managerPage.Locator("input[name='title']").FillAsync("Duplicate must not replace the entry");
    await managerPage.Locator("textarea[name='body']").FillAsync("Duplicate content must not be retained.");
    await managerPage.Locator("input[name='source']").FillAsync("Duplicate source");
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Create draft entry", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.Locator(".command-result").Last)
      .ToContainTextAsync("That library code already exists");
    await using (var db = host.CreateDbContext())
    {
      Assert.Equal(1, await db.TechnicalLibraryDocuments.CountAsync(x => x.FirmId == firmId && x.Code == code));
      Assert.Equal(1, await db.TechnicalLibraryVersions.CountAsync(x => x.DocumentId == documentId));
    }

    var entryPath = $"/app/library/{documentId:D}";
    var (partnerPage, partnerOrigin) = await SignInAsync(partner, entryPath);
    await partnerPage.GetByRole(AriaRole.Button, new() { Name = "Publish v1 (second approver)", Exact = true }).ClickAsync();
    await Assertions.Expect(partnerPage.Locator(".command-result").Last).ToContainTextAsync("Version published");

    await managerPage.GotoAsync($"{managerOrigin}{entryPath}");
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Heading, new() { Name = $"{code} — {title}", Exact = true }))
      .ToBeVisibleAsync();
    await managerPage.GetByText("Prepare a new version", new() { Exact = true }).ClickAsync();
    var versionForm = managerPage.Locator("section.panel[aria-labelledby='entry-heading'] form");
    await versionForm.Locator("textarea[name='body']").FillAsync(bodyV2);
    await versionForm.Locator("input[name='source']").FillAsync("Internal assurance methodology v2");
    await versionForm.GetByRole(AriaRole.Button, new() { Name = "Save draft version", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.Locator(".command-result").Last).ToContainTextAsync("Draft version saved");

    await Assertions.Expect(managerPage.GetByRole(AriaRole.Button, new() { Name = "Publish v2 (second approver)", Exact = true }))
      .ToBeVisibleAsync();
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Publish v2 (second approver)", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Alert)).ToContainTextAsync("Another Partner or administrator must publish");

    await partnerPage.GotoAsync($"{partnerOrigin}{entryPath}");
    await partnerPage.GetByRole(AriaRole.Button, new() { Name = "Publish v2 (second approver)", Exact = true }).ClickAsync();
    await Assertions.Expect(partnerPage.Locator(".command-result").Last).ToContainTextAsync("Version published");
    await using (var db = host.CreateDbContext())
    {
      var versions = await db.TechnicalLibraryVersions.AsNoTracking().Where(x => x.DocumentId == documentId)
        .OrderBy(x => x.Version).ToListAsync();
      Assert.Equal(2, versions.Count);
      Assert.Equal((1, "SUPERSEDED", manager.Id), (versions[0].Version, versions[0].Status, versions[0].PreparedByUserId));
      Assert.Equal((2, "PUBLISHED", partner.Id, manager.Id),
        (versions[1].Version, versions[1].Status, versions[1].ApprovedByUserId, versions[1].PreparedByUserId));
      Assert.Equal(AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(bodyV2), versions[1].ContentSha256);
      Assert.Equal(draftV1Id, versions[0].Id);
    }

    // Manager can prepare leadership-only material. Staff cannot discover it in the catalogue or open it by ID.
    await managerPage.GotoAsync($"{managerOrigin}/app/library");
    await managerPage.GetByText("Add an entry (Manager, Partner or administrator)", new() { Exact = true }).ClickAsync();
    await managerPage.Locator("input[name='code']").FillAsync(restrictedCode);
    await managerPage.Locator("input[name='title']").FillAsync(restrictedTitle);
    await managerPage.Locator("select[name='category']").SelectOptionAsync("FIRM_GUIDANCE");
    await managerPage.Locator("select[name='audience']").SelectOptionAsync("PARTNERS_MANAGERS");
    await managerPage.Locator("textarea[name='body']").FillAsync("Private leadership note with no staff access.");
    await managerPage.Locator("input[name='source']").FillAsync("Leadership policy");
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Create draft entry", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.Locator(".command-result").Last).ToContainTextAsync("Entry created as a draft");

    Guid restrictedId;
    await using (var db = host.CreateDbContext())
      restrictedId = await db.TechnicalLibraryDocuments.AsNoTracking().Where(x => x.FirmId == firmId && x.Code == restrictedCode)
        .Select(x => x.Id).SingleAsync();

    var (staffPage, staffOrigin) = await SignInAsync(staff, "/app/library");
    var catalogue = staffPage.GetByRole(AriaRole.List, new() { Name = "Library catalogue", Exact = true });
    await Assertions.Expect(catalogue).ToContainTextAsync(code);
    await Assertions.Expect(catalogue).Not.ToContainTextAsync(restrictedCode);
    await staffPage.GotoAsync($"{staffOrigin}/app/library/{restrictedId:D}");
    await Assertions.Expect(staffPage.GetByText("This library entry is not available to you.", new() { Exact = true }))
      .ToBeVisibleAsync();
    var restrictedPageText = await staffPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(restrictedTitle, restrictedPageText, StringComparison.Ordinal);
    Assert.DoesNotContain("Private leadership note with no staff access.", restrictedPageText, StringComparison.Ordinal);
    Assert.Empty(pageErrors);
  }
}
