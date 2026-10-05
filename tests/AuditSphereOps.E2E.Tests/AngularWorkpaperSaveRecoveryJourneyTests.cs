using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularWorkpaperSaveRecoveryJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-WORKPAPER-SAVE-RECOVERY-01")]
  public async Task LostDraftSaveResponseIsReconciledBeforeSubmission()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-WORKPAPER-SAVE-RECOVERY-01");
    var f = host.Fixture;
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    Guid workpaperId;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Partner", f.ClientId, f.EngagementId));
      await db.SaveChangesAsync();
      var created = await AuditPlanningService.CreateWorkpaperAsync(db, staff,
        new CreateWorkpaperRequest(f.EngagementId, "WP-RECOVERY", "Synthetic recovery workpaper",
          "Prove exact save reconciliation", "RECOVERY-v1", null, "Perform a synthetic control"));
      Assert.True(created.Succeeded, created.Message);
      workpaperId = created.Value!.WorkpaperId;
      var initial = await AuditPlanningService.SaveWorkpaperDraftAsync(db, staff,
        new SaveWorkpaperDraftRequest(workpaperId, 0, 1, 1, 1, Guid.NewGuid(),
          "Original work performed.", "Original conclusion."));
      Assert.True(initial.Succeeded, initial.Message);
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
    var lostAcknowledgementInjected = false;
    page.PageError += (_, error) => errors.Add(error);
    page.Console += (_, message) =>
    {
      if (message.Type != "error") return;
      // The routed POST is intentionally aborted after the API commits; its browser
      // network error is expected, while unrelated console errors still fail the journey.
      if (lostAcknowledgementInjected && message.Text.Contains("net::ERR_FAILED", StringComparison.Ordinal)) return;
      errors.Add($"console-error: {message.Text}");
    };
    var path = $"/app/audit/workpapers/{workpaperId:D}";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Workpaper: Synthetic recovery workpaper", Exact = true })).ToBeVisibleAsync();

    var work = "Reviewed work performed after source inspection.";
    var conclusion = "The selected evidence supports this conclusion.";
    var writeCount = 0;
    var draftUrl = $"**/api/ui/audit/workpapers/{workpaperId:D}/draft";
    await page.RouteAsync(draftUrl, async route =>
    {
      var response = await route.FetchAsync();
      Assert.Equal(200, response.Status);
      writeCount++;
      // The API commits successfully; only the browser acknowledgement is lost.
      lostAcknowledgementInjected = true;
      await route.AbortAsync("failed");
    });
    await page.GetByLabel("Work performed", new() { Exact = true }).FillAsync(work);
    await page.GetByLabel("Conclusion", new() { Exact = true }).FillAsync(conclusion);
    var recovery = page.GetByRole(AriaRole.Region,
      new() { Name = "Workpaper save recovery", Exact = true });
    await Assertions.Expect(recovery).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button,
      new() { Name = "Submit for review", Exact = true })).ToBeDisabledAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button,
      new() { Name = "Save draft", Exact = true })).ToBeDisabledAsync();

    await recovery.GetByRole(AriaRole.Button, new() { Name = "Check saved draft", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("The persisted draft confirms that save.", new() { Exact = true }))
      .ToBeVisibleAsync();
    await Assertions.Expect(recovery).ToHaveCountAsync(0);
    Assert.Equal(1, writeCount);

    await page.GetByRole(AriaRole.Button, new() { Name = "Submit for review", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("The submission was recorded.", new() { Exact = true })).ToBeVisibleAsync();
    await using (var proof = host.CreateDbContext())
    {
      var draft = await proof.WorkpaperDrafts.AsNoTracking().SingleAsync(x => x.WorkpaperId == workpaperId);
      Assert.Equal(2, draft.DraftRevision);
      Assert.Equal(work, draft.WorkPerformed);
      Assert.Equal(conclusion, draft.Conclusion);
      Assert.Equal("CONSUMED", draft.Lifecycle);
      var submission = await proof.WorkpaperSubmissions.AsNoTracking().SingleAsync(x => x.WorkpaperId == workpaperId);
      Assert.Equal(work, submission.WorkPerformed);
      Assert.Equal(conclusion, submission.Conclusion);
    }
    Assert.Empty(errors);
  }
}
