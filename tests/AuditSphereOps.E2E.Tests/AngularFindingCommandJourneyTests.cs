using System.Text.Json;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularFindingCommandJourneyTests
{
  public static TheoryData<string, string> Actions => new()
  {
    { "RECORD_RESPONSE", "success" }, { "RECORD_RESPONSE", "lost-committed" }, { "RECORD_RESPONSE", "lost-uncommitted" },
    { "DESIGNATE_MANAGEMENT_LETTER", "success" }, { "DESIGNATE_MANAGEMENT_LETTER", "lost-committed" }, { "DESIGNATE_MANAGEMENT_LETTER", "lost-uncommitted" },
    { "UNDESIGNATE_MANAGEMENT_LETTER", "success" }, { "UNDESIGNATE_MANAGEMENT_LETTER", "lost-committed" }, { "UNDESIGNATE_MANAGEMENT_LETTER", "lost-uncommitted" }
  };
  [Theory, MemberData(nameof(Actions))]
  [Trait("CaseId", "AS-ANG-FINDING-RECEIPT-01")]
  public async Task FindingAmendmentsRequireFreshAssentAndRecoverTheSameRequest(string action, string mode)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-ANG-FINDING-RECEIPT-01");
    var f = host.Fixture; Guid findingId; long initialRevision; int initialAmendments;
    await using (var setup = host.CreateDbContext())
    {
      var created = await AuditPlanningService.CreateFindingAsync(setup, PbcSeed.Actor(f.Staff, "Staff"),
        new CreateFindingRequest(f.EngagementId, "SYNTHETIC finding", "SYNTHETIC impact", false, 125m, "Prior exact response"));
      Assert.True(created.Succeeded, created.Message); findingId = created.Value!.FindingId;
      if (action == "UNDESIGNATE_MANAGEMENT_LETTER")
      {
        var designated = await AuditPlanningService.DesignateManagementLetterFindingAsync(setup, PbcSeed.Actor(f.Admin, "Administrator"),
          new RecordManagementLetterDesignationRequest(findingId, true, "Prior exact recommendation"));
        Assert.True(designated.Succeeded, designated.Message);
      }
      initialRevision = await setup.Findings.Where(x => x.Id == findingId).Select(x => x.Revision).SingleAsync();
      initialAmendments = await setup.FindingAmendments.CountAsync(x => x.FindingId == findingId);
    }
    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(); var page = await context.NewPageAsync();
    var errors = new List<string>(); var identities = new List<(string Id, string Hash)>(); var recoveryStateStatuses = new List<int>();
    page.PageError += (_, error) => errors.Add(error);
    page.Response += (_, response) =>
    {
      if (response.Request.Method == "GET" && response.Url.Contains($"/api/ui/findings/{findingId:D}/commands/state", StringComparison.Ordinal))
        recoveryStateStatuses.Add(response.Status);
    };
    var actionsUrl = $"/api/ui/findings/{findingId:D}/commands/actions";
    page.Request += (_, request) =>
    {
      if (request.Method != "POST" || !request.Url.EndsWith(actionsUrl, StringComparison.Ordinal)) return;
      using var json = JsonDocument.Parse(request.PostData!);
      identities.Add((json.RootElement.GetProperty("requestId").GetString()!, json.RootElement.GetProperty("expectedRequestHash").GetString()!));
    };
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString($"/app/findings/{findingId:D}"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Finding", Exact = true })).ToBeVisibleAsync();
    if (action == "RECORD_RESPONSE" && mode == "success")
      await OwnedBrowserFixtureHandoff.WaitAsync(origin, $"/auth/sign-in?returnUrl={Uri.EscapeDataString($"/app/findings/{findingId:D}")}", "finding-command");
    await PrepareAsync(); Assert.Empty(identities);
    var pattern = "**" + actionsUrl;
    if (mode != "success") await page.RouteAsync(pattern, async route =>
    {
      if (mode == "lost-committed") await route.FetchAsync();
      await route.AbortAsync();
    });
    await ConfirmAsync();
    if (mode != "success")
    {
      var recovery = page.GetByRole(AriaRole.Region, new() { Name = "Exact finding request recovery", Exact = true });
      await Assertions.Expect(recovery).ToBeVisibleAsync(); await page.UnrouteAsync(pattern); await page.ReloadAsync();
      await Assertions.Expect(recovery).ToContainTextAsync(identities[0].Id); await Assertions.Expect(recovery).ToContainTextAsync(identities[0].Hash);
      await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Exact finding preview", Exact = true })).ToHaveCountAsync(0);
      recoveryStateStatuses.Clear();
      (int Status, string Body) lookup;
      try
      {
        lookup = await PlaywrightResponseCapture.RunAsync(page,
          request => request.Method == "GET" && request.Url.Contains($"/api/ui/findings/{findingId:D}/commands/receipts/{identities[0].Id}?requestHash={identities[0].Hash}", StringComparison.Ordinal),
          () => recovery.GetByRole(AriaRole.Button, new() { Name = "Look up exact finding receipt", Exact = true }).ClickAsync());
      }
      catch (TimeoutException exception)
      {
        throw new TimeoutException($"{exception.Message} Finding command-state HTTP statuses: {string.Join(", ", recoveryStateStatuses)}", exception);
      }
      var (lookupStatus, lookupBody) = lookup;
      Assert.True(lookupStatus == 200, $"Exact synthetic finding receipt lookup returned HTTP {lookupStatus}: {lookupBody}");
      using var lookupDocument = JsonDocument.Parse(lookupBody);
      var found = lookupDocument.RootElement.GetProperty("found").GetBoolean();
      Assert.Equal(mode == "lost-committed", found);
      if (found)
      {
        var receipt = lookupDocument.RootElement.GetProperty("receipt");
        Assert.Equal(findingId.ToString("D"), receipt.GetProperty("findingId").GetString());
        Assert.Equal(action, receipt.GetProperty("action").GetString());
        Assert.Equal(identities[0].Id, receipt.GetProperty("requestId").GetString());
        Assert.Equal(identities[0].Hash, receipt.GetProperty("requestHash").GetString());
      }
      Assert.Single(identities);
      if (mode == "lost-uncommitted")
      {
        await Assertions.Expect(recovery).ToContainTextAsync("No receipt was found at lookup time.");
        await Assertions.Expect(recovery).ToContainTextAsync(identities[0].Id);
        await PrepareAsync(); Assert.Single(identities); await ConfirmAsync();
      }
    }
    var verified = page.GetByRole(AriaRole.Region, new() { Name = "Verified finding receipt", Exact = true });
    await Assertions.Expect(verified).ToContainTextAsync(action); await Assertions.Expect(verified).ToContainTextAsync(findingId.ToString("D"));
    Assert.Equal(mode == "lost-uncommitted" ? 2 : 1, identities.Count);
    Assert.All(identities, identity => Assert.Equal(identities[0], identity));
    await using (var db = host.CreateDbContext())
    {
      var receipt = await db.FindingCommandReceipts.SingleAsync(x => x.FindingId == findingId);
      Assert.Equal(f.Admin.Id, receipt.ActorId); Assert.Equal(f.Admin.SessionEpoch, receipt.ActorEpoch); Assert.Equal(action, receipt.Action);
      Assert.Equal(Guid.Parse(identities[0].Id), receipt.RequestId); Assert.Equal(identities[0].Hash, receipt.RequestHash);
      var finding = await db.Findings.AsNoTracking().SingleAsync(x => x.Id == findingId);
      Assert.Equal(initialRevision + 1, finding.Revision);
      Assert.Equal(initialAmendments + 1, await db.FindingAmendments.CountAsync(x => x.FindingId == findingId));
      if (action == "RECORD_RESPONSE") { Assert.Equal("Exact synthetic response", finding.ManagementResponse); Assert.True(finding.Corrected); }
      else if (action == "DESIGNATE_MANAGEMENT_LETTER") { Assert.Equal("Exact synthetic recommendation", finding.LetterRecommendation); Assert.Equal(f.Admin.Id, finding.LetterDesignatedByUserId); Assert.NotNull(finding.LetterDesignatedAt); }
      else { Assert.Null(finding.LetterRecommendation); Assert.Null(finding.LetterDesignatedAt); Assert.Null(finding.LetterDesignatedByUserId); }
    }
    Assert.Empty(errors);
    async Task PrepareAsync()
    {
      if (action == "RECORD_RESPONSE")
      {
        await page.GetByLabel("Management response", new() { Exact = true }).FillAsync("Exact synthetic response");
        await page.GetByLabel("Management corrected the matter within the period under audit (optional)", new() { Exact = true }).CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Preview management response", Exact = true }).ClickAsync();
      }
      else
      {
        var designate = page.GetByLabel("Designate this matter for the client-facing management letter", new() { Exact = true });
        if (action == "DESIGNATE_MANAGEMENT_LETTER") { await designate.CheckAsync(); await page.GetByLabel("Recommendation rendered in the letter", new() { Exact = true }).FillAsync("Exact synthetic recommendation"); }
        else await designate.UncheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Preview letter designation", Exact = true }).ClickAsync();
      }
      var preview = page.GetByRole(AriaRole.Region, new() { Name = "Exact finding preview", Exact = true });
      await Assertions.Expect(preview).ToContainTextAsync(action); await Assertions.Expect(preview).ToContainTextAsync("SYNTHETIC impact");
      await Assertions.Expect(preview.GetByRole(AriaRole.Table, new() { Name = "Finding before and proposed after", Exact = true })).ToContainTextAsync(action == "RECORD_RESPONSE" ? "Exact synthetic response" : action == "DESIGNATE_MANAGEMENT_LETTER" ? "Exact synthetic recommendation" : "Prior exact recommendation");
      await Assertions.Expect(preview.GetByRole(AriaRole.Button, new() { Name = "Confirm reviewed finding action", Exact = true })).ToBeDisabledAsync();
    }
    async Task ConfirmAsync()
    {
      var preview = page.GetByRole(AriaRole.Region, new() { Name = "Exact finding preview", Exact = true });
      await preview.GetByLabel("I reviewed this exact finding amendment and its before and after effects.", new() { Exact = true }).CheckAsync();
      await preview.GetByRole(AriaRole.Button, new() { Name = "Confirm reviewed finding action", Exact = true }).ClickAsync();
    }
  }
  [Fact]
  [Trait("CaseId", "AS-ANG-FINDING-ASSENT-02")]
  public async Task StaffResponseEditsResetAssentAndDoNotGrantDesignationAuthority()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-ANG-FINDING-ASSENT-02");
    Guid findingId;
    await using (var db = host.CreateDbContext())
    {
      var created = await AuditPlanningService.CreateFindingAsync(db, PbcSeed.Actor(host.Fixture.Staff, "Staff"),
        new CreateFindingRequest(host.Fixture.EngagementId, "SYNTHETIC finding", "SYNTHETIC impact", Corrected: false, MonetaryAmount: null, ManagementResponse: "Prior response"));
      Assert.True(created.Succeeded, created.Message); findingId = created.Value!.FindingId;
    }
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(); var page = await context.NewPageAsync(); var writes = 0;
    page.Request += (_, request) => { if (request.Method == "POST" && request.Url.EndsWith("/commands/actions", StringComparison.Ordinal)) writes++; };
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString($"/app/findings/{findingId:D}"));
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Preview letter designation", Exact = true })).ToHaveCountAsync(0);
    var response = page.GetByLabel("Management response", new() { Exact = true });
    await response.FillAsync("Exact A");
    await page.GetByRole(AriaRole.Button, new() { Name = "Preview management response", Exact = true }).ClickAsync();
    var preview = page.GetByRole(AriaRole.Region, new() { Name = "Exact finding preview", Exact = true });
    await preview.GetByLabel("I reviewed this exact finding amendment and its before and after effects.", new() { Exact = true }).CheckAsync();
    await response.FillAsync("Changed B"); await response.FillAsync("Exact A");
    await Assertions.Expect(preview).ToHaveCountAsync(0);
    await page.GetByRole(AriaRole.Button, new() { Name = "Preview management response", Exact = true }).ClickAsync();
    await Assertions.Expect(preview.GetByRole(AriaRole.Button, new() { Name = "Confirm reviewed finding action", Exact = true })).ToBeDisabledAsync();
    await Assertions.Expect(preview.GetByLabel("I reviewed this exact finding amendment and its before and after effects.", new() { Exact = true })).Not.ToBeCheckedAsync();
    var longResponse = new string('x', 8001);
    await Assertions.Expect(response).ToHaveAttributeAsync("maxlength", "20000");
    await response.FillAsync(longResponse); await Assertions.Expect(response).ToHaveValueAsync(longResponse);
    await page.GetByRole(AriaRole.Button, new() { Name = "Preview management response", Exact = true }).ClickAsync();
    await Assertions.Expect(preview).ToContainTextAsync(longResponse);
    await Assertions.Expect(preview.GetByRole(AriaRole.Button, new() { Name = "Confirm reviewed finding action", Exact = true })).ToBeDisabledAsync();
    Assert.Equal(0, writes);
    await using var verify = host.CreateDbContext(); Assert.False(await verify.FindingCommandReceipts.AnyAsync(x => x.FindingId == findingId));
  }

}
