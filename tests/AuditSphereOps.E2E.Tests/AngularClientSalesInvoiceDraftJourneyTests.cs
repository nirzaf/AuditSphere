using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularClientSalesInvoiceDraftJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-CLIENT-SALES-DRAFT-01")]
  public async Task ClientSalesDraftRecoversLostReplyRevisesAndKeepsLedgerUntouched()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-CLIENT-SALES-DRAFT-01");
    var f = host.Fixture; var admin = PbcSeed.Actor(f.Admin, "Administrator"); var reviewer = PbcSeed.Actor(f.Reviewer, "AccountingReviewer");
    Guid customerId, periodId; string clientName;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Reviewer, "AccountingReviewer"));
      db.AcceptanceDecisions.Add(new AcceptanceDecision { Id = Guid.CreateVersion7(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1, Rationale = "Synthetic invoice preparation service",
        EvaluationTemplateVersion = "TEST-1", EvaluationSnapshotDigest = new string('f', 64), DecidedByUserId = f.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, admin,
        new(f.ClientId, "QA", "QAR", 1, 1, "AUDITSPHERE", "NATIVE", ClientAccountingSourceModes.NativeBookkeeping))).Succeeded);
      var period = await ClientAccountingService.CreatePeriodAsync(db, admin, new(f.ClientId, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR"));
      Assert.True(period.Succeeded, period.Message); periodId = period.Value;
      var chart = await ClientAccountingService.CreateChartVersionAsync(db, admin, f.ClientId, "AUDITSPHERE", new DateOnly(2026, 1, 1));
      Assert.True(chart.Succeeded, chart.Message);
      Assert.True((await ClientAccountingService.AddAccountsAsync(db, admin, chart.Value, [new("revenue", "4000", "Client revenue", "INCOME", "CREDIT", true)])).Succeeded);
      Assert.True((await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chart.Value)).Succeeded);
      var customer = await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, admin, f.ClientId,
        new("Synthetic buyer", "Synthetic buyer", "CUSTOMER", "Original buyer address", "QA", "", "", "", "", "", ""));
      Assert.True(customer.Succeeded, customer.Message); customerId = customer.Value;
      clientName = await db.PracticeClients.Where(x => x.Id == f.ClientId).Select(x => x.LegalName).SingleAsync();
    }
    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright); await using var context = await browser.NewContextAsync(); var page = await context.NewPageAsync();
    var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Faccounting");
    await page.Locator("audit-accounting").GetByRole(AriaRole.Button, new() { Name = clientName, Exact = true }).ClickAsync();
    var drafts = page.Locator("audit-sales-invoice-drafts");
    await drafts.GetByText("Prepare or revise a sales draft", new() { Exact = true }).ClickAsync();
    await drafts.GetByRole(AriaRole.Button, new() { Name = "Load client customers", Exact = true }).ClickAsync();
    await drafts.GetByLabel("Sales draft customer", new() { Exact = true }).SelectOptionAsync(customerId.ToString());
    await drafts.GetByLabel("Sales draft period", new() { Exact = true }).SelectOptionAsync(periodId.ToString());
    await drafts.GetByLabel("Internal sales draft reference", new() { Exact = true }).FillAsync("DRAFT-UI-001");
    await drafts.GetByLabel("Client sales source reference", new() { Exact = true }).FillAsync("CLIENT-SOURCE-001");
    foreach (var label in new[] { "Sales document date", "Sales accounting date", "Sales supply date" }) await drafts.GetByLabel(label, new() { Exact = true }).FillAsync("2026-01-10");
    await drafts.GetByLabel("Sales due date", new() { Exact = true }).FillAsync("2026-02-10");
    await drafts.GetByLabel("Draft currency precision", new() { Exact = true }).SelectOptionAsync("2");
    await drafts.GetByLabel("Draft midpoint rounding", new() { Exact = true }).SelectOptionAsync("AWAY_FROM_ZERO");
    await drafts.GetByLabel("Sales income account", new() { Exact = true }).FillAsync("4000");
    await drafts.GetByLabel("Sales line description", new() { Exact = true }).FillAsync("Client consulting");
    await drafts.GetByLabel("Sales quantity", new() { Exact = true }).FillAsync("2");
    await drafts.GetByLabel("Sales unit price", new() { Exact = true }).FillAsync("125.125");
    await drafts.GetByLabel("Sales line discount", new() { Exact = true }).FillAsync("0.25");
    Guid commandId = Guid.Empty; var requests = 0;
    var routePattern = "**/api/ui/accounting/clients/*/sales-invoice-drafts";
    await page.RouteAsync(routePattern, async route => {
      if (route.Request.Method != "POST") { await route.ContinueAsync(); return; }
      requests++;
      using var body = JsonDocument.Parse(route.Request.PostData!); commandId = body.RootElement.GetProperty("commandId").GetGuid();
      Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("lines")[0].GetProperty("unitPrice").ValueKind);
      var accepted = await route.FetchAsync(); Assert.Equal(200, accepted.Status); await route.AbortAsync();
    });
    await drafts.GetByRole(AriaRole.Checkbox, new() { Name = "I checked the client, customer, dates, declared calculation policy and exact draft lines.", Exact = true }).CheckAsync();
    await drafts.GetByRole(AriaRole.Button, new() { Name = "Save client sales draft", Exact = true }).ClickAsync();
    await Assertions.Expect(drafts.GetByRole(AriaRole.Button, new() { Name = "Recover invoice draft request", Exact = true })).ToBeEnabledAsync();
    await Assertions.Expect(drafts.GetByLabel("Sales unit price", new() { Exact = true })).ToBeDisabledAsync();
    await drafts.GetByRole(AriaRole.Button, new() { Name = "Recover invoice draft request", Exact = true }).ClickAsync();
    await Assertions.Expect(drafts.GetByRole(AriaRole.Heading, new() { Name = "DRAFT-UI-001 · DRAFT · Revision 1", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(drafts.GetByText("Unposted gross: 250.00 QAR", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(drafts.GetByText("Seller: " + clientName + " · Customer: Synthetic buyer", new() { Exact = true })).ToBeVisibleAsync();
    Assert.Equal(1, requests); Assert.NotEqual(Guid.Empty, commandId);
    await page.UnrouteAsync(routePattern);
    Guid invoiceId;
    await using (var db = host.CreateDbContext())
    {
      var row = await db.ClientSalesInvoiceDrafts.SingleAsync(x => x.ClientId == f.ClientId); invoiceId = row.InvoiceId;
      Assert.False((await ClientSalesInvoiceDraftWorkspace.GetByCommandAsync(db, reviewer, f.ClientId, commandId)).Succeeded);
      var amendment = await ClientBookkeepingCounterpartyWorkspace.ProposeAmendmentAsync(db, admin, f.ClientId, customerId, new(1, "Synthetic buyer", "Reviewed buyer address", "", "", "", "Update buyer address"));
      Assert.True(amendment.Succeeded, amendment.Message);
      Assert.True((await ClientBookkeepingCounterpartyWorkspace.ReviewAmendmentAsync(db, reviewer, f.ClientId, customerId, amendment.Value, 2, "APPROVE", "Independent address check")).Succeeded);
    }
    await drafts.GetByRole(AriaRole.Button, new() { Name = "Revise saved sales draft", Exact = true }).ClickAsync();
    await Assertions.Expect(drafts.GetByLabel("Sales unit price", new() { Exact = true })).ToHaveValueAsync("125.125000");
    await drafts.GetByLabel("Sales quantity", new() { Exact = true }).FillAsync("1");
    await drafts.GetByLabel("Sales unit price", new() { Exact = true }).FillAsync("50.25");
    await drafts.GetByLabel("Sales line discount", new() { Exact = true }).FillAsync("0");
    await drafts.GetByRole(AriaRole.Checkbox, new() { Name = "I checked the client, customer, dates, declared calculation policy and exact draft lines.", Exact = true }).CheckAsync();
    var secondResponse = await page.RunAndWaitForResponseAsync(async () => await drafts.GetByRole(AriaRole.Button, new() { Name = "Save client sales draft", Exact = true }).ClickAsync(), response => response.Request.Method == "POST" && response.Url.EndsWith("/sales-invoice-drafts"));
    Assert.Equal(200, secondResponse.Status);
    using var secondBody = JsonDocument.Parse(secondResponse.Request.PostData!);
    Assert.Equal("1", secondBody.RootElement.GetProperty("lines")[0].GetProperty("quantity").GetString());
    Assert.Equal("50.25", secondBody.RootElement.GetProperty("lines")[0].GetProperty("unitPrice").GetString());
    using var secondResult = JsonDocument.Parse(await secondResponse.TextAsync());
    Assert.Equal("50.25", secondResult.RootElement.GetProperty("snapshot").GetProperty("gross").GetString());
    await Assertions.Expect(drafts.GetByRole(AriaRole.Heading, new() { Name = "DRAFT-UI-001 · DRAFT · Revision 2", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(drafts.GetByText("Unposted gross: 50.25 QAR", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(drafts.GetByText("Captured customer address: Reviewed buyer address", new() { Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      var old = await ClientSalesInvoiceDraftWorkspace.GetAsync(db, admin, f.ClientId, invoiceId, 1);
      Assert.Equal("Original buyer address", old.Value!.Snapshot.Customer.Address); Assert.Equal("250.00", old.Value.Snapshot.Gross);
      Assert.Equal(2, await db.ClientSalesInvoiceDrafts.CountAsync(x => x.ClientId == f.ClientId));
      Assert.Equal(0, await db.ClientOperationalJournals.CountAsync(x => x.ClientId == f.ClientId)); Assert.Equal(0, await db.FirmJournals.CountAsync());
      var gl = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, admin, f.ClientId, periodId);
      Assert.True(gl.Succeeded, gl.Message); Assert.Equal(0, gl.Value!.TotalEntries);
    }
    Assert.Empty(errors);
  }
}
