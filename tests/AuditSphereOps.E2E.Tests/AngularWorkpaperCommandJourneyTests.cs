using System.Text.Json;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularWorkpaperCommandJourneyTests
{
  [Theory]
  [InlineData("SAVE_DRAFT", true, false)]
  [InlineData("SAVE_DRAFT", false, false)]
  [InlineData("DISCARD_DRAFT", true, false)]
  [InlineData("DISCARD_DRAFT", false, false)]
  [InlineData("SUBMIT", true, false)]
  [InlineData("SUBMIT", false, false)]
  [InlineData("SAVE_DRAFT", true, true)]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-WORKPAPER-ORIGINAL-RECEIPT-01")]
  public async Task LostResponseReloadRetainsExactIdentityAndRecoveryNeverBlindlyResends(string action, bool committed, bool superseded)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-WORKPAPER-ORIGINAL-RECEIPT-01");
    var f = host.Fixture;
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    Guid workpaperId;
    const string originalWork = "Original acknowledged work.";
    const string originalConclusion = "Original acknowledged conclusion.";
    const string editedWork = "Synthetic exact autosave intent.";
    const string editedConclusion = "Synthetic exact autosave conclusion.";
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Partner", f.ClientId, f.EngagementId));
      await db.SaveChangesAsync();
      var created = await AuditPlanningService.CreateWorkpaperAsync(db, actor,
        new CreateWorkpaperRequest(f.EngagementId, "WP-ORIGINAL", "Synthetic original receipt workpaper",
          "Recover the original command", "ORIGINAL-v1", null, "Inspect synthetic evidence"));
      Assert.True(created.Succeeded, created.Message); workpaperId = created.Value!.WorkpaperId;
      var saved = await AuditPlanningService.SaveWorkpaperDraftAsync(db, actor,
        new SaveWorkpaperDraftRequest(workpaperId, 0, 1, 1, 1, Guid.NewGuid(), originalWork, originalConclusion));
      Assert.True(saved.Succeeded, saved.Message);
    }
    var origin = await host.StartApiForIdentityAsync(f.Staff, new Dictionary<string, string>
    { ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    var dropped = false;
    var retryStatus = 0;
    var retryBody = "";
    var retryFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    page.PageError += (_, error) => errors.Add(error);
    page.Console += (_, message) =>
    {
      if (message.Type == "error" && !(dropped && message.Text.Contains("net::ERR_FAILED", StringComparison.Ordinal))) errors.Add(message.Text);
    };
    var path = $"/app/audit/workpapers/{workpaperId:D}";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Workpaper: Synthetic original receipt workpaper", Exact = true })).ToBeVisibleAsync();
    var writes = 0;
    Guid retainedId = Guid.Empty;
    string retainedHash = "";
    var actionUrl = $"**/api/ui/audit/workpapers/{workpaperId:D}/commands/actions";
    await page.RouteAsync(actionUrl, async route =>
    {
      using var body = JsonDocument.Parse(route.Request.PostData!);
      Assert.Equal(action, body.RootElement.GetProperty("fields").GetProperty("action").GetString());
      writes++;
      var id = body.RootElement.GetProperty("requestId").GetGuid();
      var hash = body.RootElement.GetProperty("expectedRequestHash").GetString()!;
      if (writes == 1)
      {
        retainedId = id; retainedHash = hash;
        Assert.Equal(action != "SAVE_DRAFT", body.RootElement.GetProperty("reviewed").GetBoolean());
        if (committed)
        {
          var response = await route.FetchAsync(); Assert.Equal(200, response.Status);
        }
        dropped = true; await route.AbortAsync("failed");
      }
      else
      {
        Assert.Equal(retainedId, id); Assert.Equal(retainedHash, hash);
        var response = await route.FetchAsync();
        retryStatus = response.Status;
        retryBody = await response.TextAsync();
        await route.FulfillAsync(new() { Response = response });
        retryFinished.TrySetResult();
      }
    });
    if (action == "SAVE_DRAFT")
    {
      await page.GetByLabel("Work performed", new() { Exact = true }).FillAsync(editedWork);
      await page.GetByLabel("Conclusion", new() { Exact = true }).FillAsync(editedConclusion);
    }
    else
    {
      await page.GetByRole(AriaRole.Button, new() { Name = action == "SUBMIT" ? "Submit for review" : "Discard draft", Exact = true }).ClickAsync();
      await ReviewAndConfirm(page, action, originalWork, originalConclusion);
    }
    var recovery = page.GetByRole(AriaRole.Region, new() { Name = "Workpaper request recovery", Exact = true });
    await Assertions.Expect(recovery).ToBeVisibleAsync(); Assert.Equal(1, writes);
    var checkpoint = await page.EvaluateAsync<string>("() => JSON.stringify(Object.entries(sessionStorage).filter(([key]) => key.includes('workpaper-command-request/'))) ");
    Assert.Contains(retainedId.ToString("D"), checkpoint, StringComparison.Ordinal);
    Assert.Contains(retainedHash, checkpoint, StringComparison.Ordinal);
    Assert.DoesNotContain(editedWork, checkpoint, StringComparison.Ordinal);
    Assert.DoesNotContain(originalWork, checkpoint, StringComparison.Ordinal);
    Assert.DoesNotContain("reviewed", checkpoint, StringComparison.Ordinal);
    if (superseded)
    {
      await using var later = host.CreateDbContext();
      var saved = await AuditPlanningService.SaveWorkpaperDraftAsync(later, actor,
        new SaveWorkpaperDraftRequest(workpaperId, 2, 1, 1, 1, Guid.NewGuid(),
          "Later current work must not be rolled back.", "Later current conclusion."));
      Assert.True(saved.Succeeded, saved.Message);
    }
    // beforeunload protects an unresolved command; explicitly accept this test's requested reload.
    page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
    await page.ReloadAsync();
    await Assertions.Expect(recovery).ToBeVisibleAsync();
    await Assertions.Expect(recovery.GetByText(retainedId.ToString("D"), new() { Exact = false })).ToBeVisibleAsync();
    Assert.Equal(1, writes);
    await recovery.GetByRole(AriaRole.Button, new() { Name = "Look up original workpaper receipt", Exact = true }).ClickAsync();
    if (committed)
    {
      await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Verified workpaper receipt", Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(recovery).ToHaveCountAsync(0); Assert.Equal(1, writes);
      if (superseded)
      {
        await Assertions.Expect(page.GetByLabel("Work performed", new() { Exact = true })).ToHaveValueAsync("Later current work must not be rolled back.");
        var verified = page.GetByRole(AriaRole.Region, new() { Name = "Verified workpaper receipt", Exact = true });
        await Assertions.Expect(verified).ToContainTextAsync("revision 1 → 2");
      }
    }
    else
    {
      var retry = recovery.GetByRole(AriaRole.Button, new() { Name = "Prepare same workpaper request again", Exact = true });
      await Assertions.Expect(retry).ToBeVisibleAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Save draft", Exact = true })).ToBeDisabledAsync();
      await using (var proof = host.CreateDbContext())
      {
        Assert.False(await proof.WorkpaperCommandReceipts.AsNoTracking().AnyAsync(x => x.WorkpaperId == workpaperId && x.RequestId == retainedId));
        var unchanged = await proof.WorkpaperDrafts.AsNoTracking().SingleAsync(x => x.WorkpaperId == workpaperId);
        Assert.Equal("ACTIVE", unchanged.Lifecycle); Assert.Equal(1, unchanged.DraftRevision); Assert.Equal(originalWork, unchanged.WorkPerformed);
      }
      Assert.Equal(1, writes);
      if (action == "SAVE_DRAFT")
      {
        await page.GetByLabel("Work performed", new() { Exact = true }).FillAsync(editedWork);
        await page.GetByLabel("Conclusion", new() { Exact = true }).FillAsync(editedConclusion);
      }
      await retry.ClickAsync();
      if (action != "SAVE_DRAFT") await ReviewAndConfirm(page, action, originalWork, originalConclusion);
      await retryFinished.Task.WaitAsync(TimeSpan.FromSeconds(10));
      Assert.True(retryStatus == 200, $"Retry returned {retryStatus}: {retryBody}");
      await Assertions.Expect(recovery).ToHaveCountAsync(0); Assert.Equal(2, writes);
    }
    await using (var proof = host.CreateDbContext())
    {
      var receipt = await proof.WorkpaperCommandReceipts.AsNoTracking().SingleAsync(x => x.WorkpaperId == workpaperId && x.RequestId == retainedId);
      Assert.Equal(retainedHash, receipt.RequestHash); Assert.Equal(action, receipt.Action); Assert.Equal(f.Staff.Id, receipt.ActorId);
      Assert.Equal(f.Staff.SessionEpoch, receipt.ActorEpoch);
      Assert.Equal(1, await proof.WorkpaperCommandEvents.AsNoTracking().CountAsync(x => x.WorkpaperId == workpaperId && x.RequestId == retainedId));
      var draft = await proof.WorkpaperDrafts.AsNoTracking().SingleAsync(x => x.WorkpaperId == workpaperId);
      Assert.Equal(action == "SAVE_DRAFT" ? superseded ? 3 : 2 : 1, draft.DraftRevision);
      Assert.Equal(superseded ? "Later current work must not be rolled back." : action == "SAVE_DRAFT" ? editedWork : originalWork, draft.WorkPerformed);
      Assert.Equal(superseded ? "Later current conclusion." : action == "SAVE_DRAFT" ? editedConclusion : originalConclusion, draft.Conclusion);
      var original = await WorkpaperCommandWorkspace.LookupAsync(proof, actor, workpaperId, retainedId, retainedHash);
      Assert.True(original.Succeeded, original.Message); Assert.True(original.Value!.Found);
      Assert.Equal(action == "SAVE_DRAFT" ? editedWork : originalWork, original.Value.Receipt!.CommandEvent.After.Draft!.WorkPerformed);
      Assert.Equal(action == "SUBMIT" ? "CONSUMED" : action == "DISCARD_DRAFT" ? "DISCARDED" : "ACTIVE", draft.Lifecycle);
      var submissions = await proof.WorkpaperSubmissions.AsNoTracking().Where(x => x.WorkpaperId == workpaperId).ToListAsync();
      if (action == "SUBMIT") { var submission = Assert.Single(submissions); Assert.Equal(originalWork, submission.WorkPerformed); Assert.Equal(originalConclusion, submission.Conclusion); }
      else Assert.Empty(submissions);
    }
    Assert.Empty(errors);
  }

  private static async Task ReviewAndConfirm(IPage page, string action, string work, string conclusion)
  {
    var dialog = page.GetByRole(AriaRole.Dialog).Filter(new LocatorFilterOptions
    { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Review exact workpaper action", Exact = true }) });
    await Assertions.Expect(dialog.GetByText(action + " ·", new() { Exact = false })).ToBeVisibleAsync();
    await Assertions.Expect(dialog.GetByText(work, new() { Exact = true }).Last).ToBeVisibleAsync();
    await Assertions.Expect(dialog.GetByText(conclusion, new() { Exact = true }).Last).ToBeVisibleAsync();
    var confirm = dialog.GetByRole(AriaRole.Button, new() { Name = "Confirm reviewed workpaper action", Exact = true });
    await Assertions.Expect(confirm).ToBeDisabledAsync();
    await dialog.GetByRole(AriaRole.Checkbox,
      new() { Name = "I reviewed this exact workpaper, acknowledged draft and proposed effect.", Exact = true }).CheckAsync();
    await confirm.ClickAsync();
  }
}
