using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularMappingDraftJourneyTests
{
  private const string Assent = "I reviewed these exact account changes, splits, rationale and mapping basis.";

  [Fact][Trait("CaseId", "ANGULAR-MAPPING-BATCH")]
  public async Task NativeSplitDraftRecoveryAndLostCreateResponsePreserveApprovedHistory()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-MAPPING-BATCH");
    var parent = await Seed(host);
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var pw = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(pw); var page = await browser.NewPageAsync();
    var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString($"/ui/app/accounting/mappings/{parent}"));
    await page.GetByRole(AriaRole.Link, new() { Name = "Prepare new mapping version", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Edit 1000", Exact = true }).ClickAsync();
    await page.GetByLabel("Fraction 1", new() { Exact = true }).FillAsync("0.5");
    await page.GetByLabel("Rationale 1", new() { Exact = true }).FillAsync("Synthetic independently reviewed split");
    await page.GetByRole(AriaRole.Button, new() { Name = "Add destination split", Exact = true }).ClickAsync();
    await page.GetByLabel("Destination 2", new() { Exact = true }).SelectOptionAsync("REVENUE");
    await page.GetByLabel("Fraction 2", new() { Exact = true }).FillAsync("0.5");
    await page.GetByLabel("Rationale 2", new() { Exact = true }).FillAsync("Synthetic independently reviewed split");
    await page.GetByRole(AriaRole.Button, new() { Name = "Stage account change", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Save tab draft", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Link, new() { Name = "Allocations, impact and history", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Dialog)).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Keep editing", Exact = true }).ClickAsync();
    await page.ReloadAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Review complete mapping batch", Exact = true })).ToBeDisabledAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Recover saved tab draft", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Review complete mapping batch", Exact = true }).ClickAsync();
    var preview = page.GetByRole(AriaRole.Region, new() { Name = "Complete mapping batch review", Exact = true });
    await Assertions.Expect(preview).ToContainTextAsync("3 allocations");
    await page.GetByLabel(Assent, new() { Exact = true }).CheckAsync();
    var writes = 0;
    await page.RouteAsync("**/mappings/*/draft", async r => { writes++; var response = await r.FetchAsync(); Assert.Equal(200, response.Status); await r.AbortAsync("failed"); });
    await page.GetByRole(AriaRole.Button, new() { Name = "Create reviewed mapping version", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Mapping creation outcome needs verification", Exact = true })).ToBeVisibleAsync();
    await page.ReloadAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Read creation receipt", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "I reviewed the retained creation", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Mapping creation outcome needs verification", Exact = true })).ToBeHiddenAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Retained mapping creation", Exact = true })).ToBeVisibleAsync();
    Assert.Equal(1, writes); await page.SetViewportSizeAsync(390, 844);
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
    await using (var db = host.CreateDbContext())
    {
      var old = await db.MappingVersions.SingleAsync(x => x.Id == parent); Assert.Equal("APPROVED", old.Status);
      Assert.All(await db.MappingAllocations.Where(x => x.MappingVersionId == parent).ToListAsync(), x => Assert.Equal(1m, x.Fraction));
      var next = await db.MappingVersions.SingleAsync(x => x.BaseMappingVersionId == parent);
      Assert.Equal("DRAFT", next.Status); Assert.Null(next.ApprovedByUserId); Assert.Equal(3, await db.MappingAllocations.CountAsync(x => x.MappingVersionId == next.Id));
      Assert.Equal(2, await db.ClientSafetyStates.Where(x => x.Id == host.Fixture.ClientId).Select(x => x.InputGeneration).SingleAsync());
      Assert.Empty(await db.SourceAcceptanceDecisions.ToListAsync());
      await db.Users.Where(x => x.Id == host.Fixture.Staff.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    }
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
    Assert.DoesNotContain(parent.ToString(), await page.Locator("body").InnerTextAsync()); Assert.Empty(errors);
  }

  [Fact][Trait("CaseId", "ANGULAR-MAPPING-BATCH-GATES")]
  public async Task KeyboardPasteAndStaleBatchRemainExplicitAndPublishNoPartialVersion()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-MAPPING-BATCH-GATES");
    var parent = await Seed(host); var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var pw = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(pw); var page = await browser.NewPageAsync();
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString($"/ui/app/accounting/mappings/{parent}/edit"));
    var first = page.GetByRole(AriaRole.Button, new() { Name = "Edit 1000", Exact = true });
    await Assertions.Expect(first).ToBeVisibleAsync(); await first.FocusAsync(); await first.PressAsync("ArrowDown");
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Edit 3000", Exact = true })).ToBeFocusedAsync();
    await first.PressAsync("F2"); await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Edit account 1000", Exact = true })).ToBeVisibleAsync();
    await page.GetByLabel("Rationale 1", new() { Exact = true }).FillAsync("Synthetic unsaved edit"); await page.GetByLabel("Rationale 1", new() { Exact = true }).PressAsync("Escape");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Edit account 1000", Exact = true })).ToBeHiddenAsync();
    await page.GetByText("Assign selected accounts or paste exact allocations", new() { Exact = true }).ClickAsync();
    await page.GetByLabel("Paste tab-separated allocation rows", new() { Exact = true }).FillAsync("1000\tCASH\t0.1234567\tSynthetic invalid precision");
    await page.GetByRole(AriaRole.Button, new() { Name = "Preview pasted allocations", Exact = true }).ClickAsync();
    await Assertions.Expect(page.Locator("audit-command-message")).ToContainTextAsync("six decimal places");
    await page.GetByLabel("Paste tab-separated allocation rows", new() { Exact = true }).FillAsync("1000\tCASH\t1\tSynthetic explicit replacement");
    await page.GetByRole(AriaRole.Button, new() { Name = "Preview pasted allocations", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Proposed local batch", Exact = true })).ToContainTextAsync("1000");
    await page.GetByRole(AriaRole.Button, new() { Name = "Stage this exact batch", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Review complete mapping batch", Exact = true }).ClickAsync();
    await page.GetByLabel(Assent, new() { Exact = true }).CheckAsync();
    await using (var db = host.CreateDbContext()) await db.ClientSafetyStates.Where(x => x.Id == host.Fixture.ClientId).ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, x => x.InputGeneration + 1));
    await page.GetByRole(AriaRole.Button, new() { Name = "Create reviewed mapping version", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Mapping basis changed", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Local changes and draft recovery", Exact = true })).ToContainTextAsync("1000");
    await using var proof = host.CreateDbContext(); Assert.Single(await proof.MappingVersions.ToListAsync()); Assert.Equal(2, await proof.MappingAllocations.CountAsync()); Assert.Empty(await proof.SourceAcceptanceDecisions.ToListAsync());
  }

  private static async Task<Guid> Seed(OwnedBlazorHost host)
  {
    await using var db = host.CreateDbContext(); var seed = await MappingApprovalSeed.SeedAsync(db, host.Fixture);
    var actor = PbcSeed.Actor(host.Fixture.Reviewer, "AccountingReviewer"); var plan = await MappingApprovalWorkspace.GetAsync(db, actor, seed.MappingId);
    Assert.True(plan.Succeeded, plan.Message); Assert.True((await MappingApprovalWorkspace.ApproveAsync(db, actor, seed.MappingId, plan.Value!.Revision, true)).Succeeded); return seed.MappingId;
  }
}
