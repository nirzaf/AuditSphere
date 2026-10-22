using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// T015 / WU-029: from the audit plan, an applicable controlled procedure creates exactly one WORKING workpaper and
/// later opens the same paper instead of creating another.
/// </summary>
[Trait("Category", "AuditFieldwork")]
[Trait("CompletionUnit", "WU-029")]
public sealed class AngularProcedureWorkpaperJourneyTests
{
  [Fact]
  public async Task ApplicableProcedureCreatesOneWorkingPaperAndThenOpensTheSamePaper()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-AWP-PROCEDURE-WORKPAPER-01");
    var f = host.Fixture;
    var preparer = PbcSeed.Actor(f.Staff, "Partner");
    var reviewer = PbcSeed.Actor(f.Reviewer, "Partner");
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.AddRange(PbcSeed.Grant(f.FirmId, f.Staff, "Partner"), PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner"));
      await db.SaveChangesAsync();
      var publication = await AuditProgramTestHelpers.PublishAsync(db, preparer, new("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(publication.Succeeded, publication.Message);
      await PlanningBasisSeed.EstablishAsync(db, f.FirmId, f.ClientId, f.EngagementId, preparer, reviewer);
    }
    var origin = await host.StartApiForIdentityAsync(f.Staff, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true", ["WorkflowExperience__Enabled"] = "true"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);

    // Adopt the exact published program and mark the cash and bank procedure applicable, as a preparer would.
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString($"/app/engagements/{f.EngagementId:D}/audit-fieldwork"));
    var adopt = page.GetByRole(AriaRole.Button, new() { Name = "Adopt exact published version 2026.1", Exact = true });
    await adopt.ClickAsync();
    await Assertions.Expect(page.GetByText("AUDIT-WORKING-PROCESS v2026.1", new() { Exact = true })).ToBeVisibleAsync();
    var section = page.Locator("select[name='section']");
    var cashBank = section.Locator("option").Filter(new() { HasText = "Cash & Bank" });
    await section.SelectOptionAsync(new SelectOptionValue { Label = await cashBank.InnerTextAsync() });
    var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = "AWP-02-01" });
    var applicableButton = row.GetByRole(AriaRole.Button, new() { Name = "Applicable", Exact = true });
    await applicableButton.ClickAsync();
    await Assertions.Expect(applicableButton).ToHaveCountAsync(0);
    await Assertions.Expect(row).ToContainTextAsync("Execute from source workflow");

    // The plan offers a WORKING paper for the applicable procedure and opens the created paper.
    var planPath = $"/app/engagements/{f.EngagementId:D}/audit-plan";
    await page.GotoAsync(origin + planPath);
    var item = page.GetByRole(AriaRole.List, new() { Name = "Authorized procedures and working papers", Exact = true })
      .GetByRole(AriaRole.Listitem).Filter(new() { HasText = "AWP-02-01" });
    await item.GetByRole(AriaRole.Button, new() { Name = "Create WORKING workpaper", Exact = true }).ClickAsync(new() { Timeout = 30000 });
    await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/app/audit/workpapers/[0-9a-f-]{36}$"));
    var created = Guid.Parse(page.Url[^36..]);

    await page.GotoAsync(origin + planPath);
    await item.GetByRole(AriaRole.Button, new() { Name = "Open workpaper", Exact = true }).ClickAsync();
    await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/app/audit/workpapers/[0-9a-f-]{36}$"));
    Assert.Equal(created, Guid.Parse(page.Url[^36..]));

    await using (var db = host.CreateDbContext())
    {
      var procedure = await db.AuditProcedures.AsNoTracking()
        .SingleAsync(x => x.EngagementId == f.EngagementId && x.SourceProcedureId == "AWP-02-01");
      var papers = await db.Workpapers.AsNoTracking().Where(x => x.EngagementId == f.EngagementId).ToListAsync();
      Assert.Equal(created, Assert.Single(papers).Id);
      Assert.Equal(procedure.Id, papers[0].ProcedureId);
    }
    Assert.Empty(errors);
  }
}
