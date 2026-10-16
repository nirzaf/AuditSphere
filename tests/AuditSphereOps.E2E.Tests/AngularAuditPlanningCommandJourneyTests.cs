using System.Text.Json;
using System.Text.RegularExpressions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAuditPlanningCommandJourneyTests
{
  public static TheoryData<string, bool> Actions => new()
  {
    { "CALCULATE_MATERIALITY", false }, { "CALCULATE_MATERIALITY", true },
    { "ROUND_MATERIALITY", false }, { "ROUND_MATERIALITY", true },
    { "APPROVE_MATERIALITY", false }, { "APPROVE_MATERIALITY", true },
    { "CREATE_RISK", false }, { "CREATE_RISK", true },
    { "ASSESS_RISK", false }, { "ASSESS_RISK", true },
    { "ASSIGN_RISK_OWNER", false }, { "ASSIGN_RISK_OWNER", true },
    { "CLEAR_RISK", false }, { "CLEAR_RISK", true },
    { "LINK_PROCEDURE_RISK", false }, { "LINK_PROCEDURE_RISK", true }
  };
  [Theory, MemberData(nameof(Actions))]
  [Trait("CaseId", "AS-ANG-AUDIT-PLANNING-RECEIPT-01")]
  public async Task EightActionsRequireExactReviewAndRecoverDroppedResponsesWithoutDuplicate(string action, bool dropResponse)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-ANG-AUDIT-PLANNING-RECEIPT-01");
    // Reuse the backend's exact synthetic planning fixture in this owned database.
    var database = new FixtureDatabase(host.DbOptions, host.ConnectionString);
    var world = await AuditPlanningReceiptSeed.SeedAsync(database);
    var f = world.Fixture;
    var (actor, fields) = await AuditPlanningReceiptSeed.FieldsAsync(database, world, action);
    var procedureId = world.ProcedureId;
    if (action == "LINK_PROCEDURE_RISK")
    {
      await using var setup = host.CreateDbContext();
      var adopted = await AuditFieldworkWorkspaceQuery.PublishAndAdoptAsync(setup,
        PbcSeed.Actor(f.Admin, "Partner"), f.EngagementId, "2026.1");
      Assert.True(adopted.Succeeded, adopted.Message);
      procedureId = await setup.AuditProcedures.Where(x => x.EngagementId == f.EngagementId &&
        x.EngagementProgramId == adopted.Value && x.SourceProcedureId == "AWP-01-01").Select(x => x.Id).SingleAsync();
    }
    var riskId = fields.AssessRisk?.RiskId ?? fields.AssignRiskOwner?.RiskId ?? fields.ClearRisk?.RiskId ?? fields.LinkProcedureRisk?.RiskId ?? Guid.Empty;
    await using var source = host.CreateDbContext();
    var user = await source.Users.AsNoTracking().SingleAsync(x => x.Id == actor.UserId);
    var digest = await source.TrialBalanceDatasets.Where(x => x.EngagementId == f.EngagementId).Select(x => x.NormalizedDatasetDigest).SingleAsync();
    var origin = await host.StartApiForIdentityAsync(user, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true", ["Application__FirmId"] = f.FirmId.ToString("D") });
    var path = $"/app/engagements/{f.EngagementId}/" + (action == "LINK_PROCEDURE_RISK" ? "audit-fieldwork" : "audit-plan");
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(); var page = await context.NewPageAsync(); var errors = new List<string>();
    var writes = 0; string? requestId = null; string? requestHash = null;
    page.PageError += (_, error) => errors.Add(error);
    page.Request += (_, request) => { if (request.Method == "POST" && request.Url.EndsWith("/audit-plan/commands/actions", StringComparison.Ordinal)) { writes++; using var json = JsonDocument.Parse(request.PostData!); requestId = json.RootElement.GetProperty("requestId").GetString(); requestHash = json.RootElement.GetProperty("expectedRequestHash").GetString(); } };
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    if (action == "CALCULATE_MATERIALITY" && !dropResponse)
      await OwnedBrowserFixtureHandoff.WaitAsync(origin, path);
    switch (action)
    {
      case "CALCULATE_MATERIALITY":
        var benchmark = page.GetByRole(AriaRole.Region, new() { Name = "Materiality calculator", Exact = true }).Locator("select[name='option']");
        await benchmark.SelectOptionAsync("REVENUE|");
        await Assertions.Expect(benchmark).ToHaveValueAsync("REVENUE|");
        await Assertions.Expect(benchmark.Locator("option:checked")).ToHaveTextAsync("Revenue (income section) — 100,000.00");
        await page.GetByLabel("Rationale for the benchmark", new() { Exact = true }).FillAsync("SYNTHETIC benchmark");
        await page.GetByRole(AriaRole.Button, new() { Name = "Preview materiality calculation", Exact = true }).ClickAsync(); break;
      case "ROUND_MATERIALITY":
        var roundIntent = fields.Round!;
        var calculator = page.GetByRole(AriaRole.Region, new() { Name = "Materiality calculator", Exact = true });
        await calculator.GetByLabel("Planning materiality", new() { Exact = true }).FillAsync(roundIntent.PlanningMateriality);
        await calculator.GetByLabel("Tolerable error", new() { Exact = true }).FillAsync(roundIntent.TolerableError);
        await calculator.GetByLabel("SAD threshold", new() { Exact = true }).FillAsync(roundIntent.SadThreshold);
        await calculator.GetByLabel("Rationale for the rounding", new() { Exact = true }).FillAsync(roundIntent.Rationale);
        await page.GetByRole(AriaRole.Button, new() { Name = "Preview practical rounding", Exact = true }).ClickAsync(); break;
      case "APPROVE_MATERIALITY":
        await page.GetByRole(AriaRole.Button, new() { Name = "Preview materiality approval", Exact = true }).First.ClickAsync(); break;
      case "CREATE_RISK":
        await page.GetByText("Record an identified risk", new() { Exact = true }).ClickAsync();
        await page.GetByLabel("Account or disclosure area", new() { Exact = true }).FillAsync("CASH"); await page.Locator("input[name='rs']").FillAsync("Existence");
        await page.GetByLabel("Risk description", new() { Exact = true }).FillAsync("SYNTHETIC risk"); await page.GetByLabel("Drivers", new() { Exact = true }).FillAsync("SYNTHETIC driver");
        await page.GetByLabel("Planned response", new() { Exact = true }).FillAsync("SYNTHETIC response");
        await page.GetByRole(AriaRole.Button, new() { Name = "Preview identified risk", Exact = true }).ClickAsync(); break;
      case "ASSESS_RISK":
        var riskCard = page.Locator("[data-risk='CASH']");
        await Assertions.Expect(riskCard).ToBeVisibleAsync();
        await riskCard.GetByLabel(new Regex(@"^Likelihood(?:\s|$)")).SelectOptionAsync(new SelectOptionValue { Label = "3 High" });
        await riskCard.GetByLabel(new Regex(@"^Magnitude(?:\s|$)")).SelectOptionAsync(new SelectOptionValue { Label = "3 High" });
        await Assertions.Expect(riskCard.GetByLabel(new Regex(@"^Likelihood(?:\s|$)")).Locator("option:checked")).ToHaveTextAsync("3 High");
        await Assertions.Expect(riskCard.GetByLabel(new Regex(@"^Magnitude(?:\s|$)")).Locator("option:checked")).ToHaveTextAsync("3 High");
        await page.GetByLabel("Band rationale for CASH", new() { Exact = true }).FillAsync("SYNTHETIC assessment");
        await page.GetByRole(AriaRole.Button, new() { Name = "Assess band for CASH", Exact = true }).ClickAsync(); break;
      case "ASSIGN_RISK_OWNER":
        await page.GetByLabel("Owner for CASH", new() { Exact = true }).SelectOptionAsync(f.Reviewer.Id.ToString("D"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Assign owner for CASH", Exact = true }).ClickAsync(); break;
      case "CLEAR_RISK":
        await page.GetByLabel("Partner review note for CASH", new() { Exact = true }).FillAsync("SYNTHETIC independent review");
        await page.GetByRole(AriaRole.Button, new() { Name = "Record Partner review for CASH", Exact = true }).ClickAsync(); break;
      default:
        var procedureRow = page.Locator($"#procedure-{procedureId:D}");
        await Assertions.Expect(procedureRow).ToContainTextAsync("AWP-01-01");
        var linkedRisk = procedureRow.Locator($"select[id='risk-link-{procedureId:D}']");
        await linkedRisk.SelectOptionAsync(riskId.ToString("D"));
        await Assertions.Expect(linkedRisk).ToHaveValueAsync(riskId.ToString("D"));
        await Assertions.Expect(linkedRisk.Locator("option:checked")).ToContainTextAsync("CASH");
        await page.Locator($"#procedure-{procedureId:D}").GetByRole(AriaRole.Button, new() { Name = "Preview risk link", Exact = true }).ClickAsync(); break;
    }
    var preview = page.GetByRole(AriaRole.Region, new() { Name = "Exact planning preview", Exact = true });
    await Assertions.Expect(preview).ToContainTextAsync(action); await Assertions.Expect(preview).ToContainTextAsync(digest!);
    Assert.Equal(0, writes);
    var confirm = preview.GetByRole(AriaRole.Button, new() { Name = "Confirm reviewed planning action", Exact = true }); await Assertions.Expect(confirm).ToBeDisabledAsync();
    await preview.GetByLabel("I reviewed this exact planning preview, source evidence and effect.", new() { Exact = true }).CheckAsync();
    const string routePattern = "**/audit-plan/commands/actions";
    if (dropResponse) await page.RouteAsync(routePattern, async route => { await route.FetchAsync(); await route.AbortAsync(); });
    await confirm.ClickAsync();
    if (dropResponse)
    {
      await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Exact planning request recovery", Exact = true })).ToBeVisibleAsync();
      await page.UnrouteAsync(routePattern); await page.ReloadAsync();
      var recovery = page.GetByRole(AriaRole.Region, new() { Name = "Exact planning request recovery", Exact = true });
      await Assertions.Expect(recovery).ToContainTextAsync(requestId!); await Assertions.Expect(recovery).ToContainTextAsync(requestHash!);
      await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Exact planning preview", Exact = true })).ToHaveCountAsync(0);
      await recovery.GetByRole(AriaRole.Button, new() { Name = "Look up exact planning receipt", Exact = true }).ClickAsync();
    }
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Verified planning receipt", Exact = true })).ToContainTextAsync(action);
    Assert.Equal(1, writes);
    await using (var verify = host.CreateDbContext())
    {
      var receipt = await verify.AuditPlanningCommandReceipts.SingleAsync(x => x.EngagementId == f.EngagementId);
      Assert.Equal(action, receipt.Action); Assert.Equal(user.Id, receipt.ActorId); Assert.Equal(Guid.Parse(requestId!), receipt.RequestId); Assert.Equal(requestHash, receipt.RequestHash);
      Assert.Equal(1, await verify.AuditPlanningCommandReceipts.CountAsync(x => x.EngagementId == f.EngagementId));
    }
    Assert.Empty(errors);
  }
  private sealed record FixtureDatabase(DbContextOptions<AuditSphereDbContext> Options, string ConnectionString) : ITestPostgresDatabase;

}
