using AuditSphereOps.Domain.Microsoft365;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularSharePointJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-SELECTED-SHAREPOINT")]
  public async Task ExactResourceDraftAndTemplateApproval_RemainSeparateFromLiveVerification()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-SELECTED-SHAREPOINT");
    var seeded = await TenantAdministrationJourneyTests.SeedAsync(host, verified: true);
    var settings = TenantAdministrationJourneyTests.Simulation(seeded); settings["AngularUi__Enabled"] = "true";
    var origin = await host.StartApiForIdentityAsync(seeded.Admin,settings);
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_,e) => errors.Add(e);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Fadministration%2Fmicrosoft365%2Ftenant-connection");
    var panel = page.Locator("audit-sharepoint-administration");
    await Assertions.Expect(panel.GetByRole(AriaRole.Heading,new() { Name = "Selected SharePoint workspace",Exact = true })).ToBeVisibleAsync();
    await panel.GetByLabel("Selected site URL",new() { Exact = true }).FillAsync("https://synthetic.sharepoint.com/sites/working");
    await panel.GetByLabel("Site ID",new() { Exact = true }).FillAsync("site-1");
    await panel.GetByLabel("Library / drive ID",new() { Exact = true }).FillAsync("drive-1");
    await panel.GetByLabel("Root folder ID",new() { Exact = true }).FillAsync("root-1");
    var save = panel.GetByRole(AriaRole.Button,new() { Name = "Save resource draft",Exact = true }); await Assertions.Expect(save).ToBeDisabledAsync();
    await panel.GetByRole(AriaRole.Checkbox,new() { Name = "I reviewed the exact site, library, root and access profile.",Exact = true }).CheckAsync();
    await save.ClickAsync(); await Assertions.Expect(panel).ToContainTextAsync("Draft saved. Its selected-site verification is invalidated");
    await Assertions.Expect(panel.GetByRole(AriaRole.Button,new() { Name = "Verify selected-site boundary",Exact = true })).ToBeDisabledAsync();
    await Assertions.Expect(panel.GetByRole(AriaRole.Button,new() { Name = "Activate selected workspace",Exact = true })).ToBeDisabledAsync();
    await panel.GetByRole(AriaRole.Button,new() { Name = "Load STE layout",Exact = true }).ClickAsync();
    await panel.GetByRole(AriaRole.Checkbox,new() { Name = "I reviewed this local manifest and its current version.",Exact = true }).CheckAsync();
    await panel.GetByRole(AriaRole.Button,new() { Name = "Save template version",Exact = true }).ClickAsync();
    await panel.GetByRole(AriaRole.Button,new() { Name = "Review version 1 CLIENT_WORKSPACE",Exact = true }).ClickAsync();
    await Assertions.Expect(panel.GetByRole(AriaRole.Button,new() { Name = "Approve reviewed template",Exact = true })).ToBeDisabledAsync();
    await panel.GetByRole(AriaRole.Checkbox,new() { Name = "I reviewed this exact immutable manifest and digest.",Exact = true }).CheckAsync();
    await panel.GetByRole(AriaRole.Button,new() { Name = "Approve reviewed template",Exact = true }).ClickAsync();
    await Assertions.Expect(panel).ToContainTextAsync("Exact local template approved.");
    await using var db = host.CreateDbContext();
    var template = await db.FolderTemplateVersions.SingleAsync(x => x.FirmId == host.Fixture.FirmId);
    Assert.NotNull(template.ApprovedAt); Assert.Equal("{\"nodes\":[]}",template.ManifestJson);
    var draft = await db.Microsoft365SetupDrafts.SingleAsync(x => x.FirmId == host.Fixture.FirmId);
    Assert.Equal("drive-1",draft.DriveId); Assert.Equal(2,draft.Revision);
    Assert.Empty(await db.FirmWorkspaceConfigurations.ToListAsync());
    Assert.Equal("BLOCKED_EXTERNAL",(await db.TenantCapabilityVerifications.Where(x => x.Capability == Microsoft365Capabilities.SelectedSite).SingleAsync()).State);
    Assert.Equal(1,await db.Microsoft365AdministrationEvents.CountAsync(x => x.Operation == "FOLDER_TEMPLATE_APPROVED"));
    Assert.Empty(errors);
  }
}
