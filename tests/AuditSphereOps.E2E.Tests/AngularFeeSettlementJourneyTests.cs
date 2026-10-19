using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularFeeSettlementJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-FINANCE-FEE-SETTLEMENT")]
  public async Task ScopedFinancePreparesReviewsAndSettlesAdvanceWithoutCommercialAccess()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-FINANCE-FEE-SETTLEMENT");
    var f = host.Fixture;
    var manager = PbcSeed.User(f.FirmId, "Staff");
    var reviewer = PbcSeed.User(f.FirmId, "Staff");
    var siblingClient = Guid.NewGuid();
    var agreementId = Guid.Empty;
    var proposalId = Guid.Empty;
    var siblingAgreementId = Guid.Empty;
    var postedOrphanAgreementId = Guid.Empty;
    var postedOrphanMilestoneId = Guid.Empty;
    Guid retainedPostedInvoiceId;
    var advanceMilestoneId = Guid.Empty;
    Guid retainedDraftInvoiceId;
    var now = DateTimeOffset.UtcNow;
    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(manager, reviewer);
      db.RoleGrants.AddRange(PbcSeed.Grant(f.FirmId, manager, "FinanceManager", clientId: f.ClientId),
        PbcSeed.Grant(f.FirmId, reviewer, "FinanceReviewer", clientId: f.ClientId));
      db.PracticeClients.Add(new PracticeClient
      {
        Id = siblingClient, FirmId = f.FirmId, LegalName = "SYNTHETIC UNAUTHORIZED FEE CLIENT", CreatedAt = now
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = siblingClient, FirmId = f.FirmId });
      db.ClientContacts.Add(new ClientContact
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId, FullName = "Synthetic finance contact",
        Email = "synthetic-finance@example.test", Role = "CFO", Primary = true, IsActive = true
      });
      db.FirmCommercialProfiles.Add(new FirmCommercialProfile
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, Version = 1, LegalName = "Synthetic fee test firm",
        Address = "Synthetic address", ContactEmail = "synthetic-firm@example.test", ContactPhone = "",
        AccentColorHex = "#0F766E", ClosingText = "Synthetic receipt fixture", CreatedByUserId = f.Admin.Id, CreatedAt = now
      });
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, FunctionalCurrency = "QAR", ProfileKind = BillingStates.TestProfile,
        Approved = true, ApprovedByUserId = f.Admin.Id, ApprovedAt = now, CreatedAt = now
      });
      // Existing-agreement source fixtures only. These are not evidence that commercial acceptance,
      // Partner risk clearance, professional work or a real transfer has occurred.
      for (var index = 0; index < 22; index++)
      {
        var clientId = index == 21 ? siblingClient : f.ClientId;
        var lead = Guid.NewGuid(); var opportunity = Guid.NewGuid(); var proposal = Guid.NewGuid();
        var quotation = Guid.NewGuid(); var agreement = Guid.NewGuid();
        db.Leads.Add(new Lead { Id = lead, FirmId = f.FirmId, Name = "PRIVATE-COMMERCIAL-METADATA", Source = "TEST", CreatedAt = now });
        db.Opportunities.Add(new Opportunity
        {
          Id = opportunity, FirmId = f.FirmId, LeadId = lead, PracticeClientId = clientId,
          ServiceRoute = "FinancialStatementAudit", EntityScope = "PRIVATE-COMMERCIAL-METADATA",
          PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", ExpectedFee = 20000m, Currency = "QAR", CreatedAt = now
        });
        db.Proposals.Add(new Proposal
        {
          Id = proposal, FirmId = f.FirmId, OpportunityId = opportunity, PracticeClientId = clientId,
          ServiceProfileId = "synthetic", Scope = "PRIVATE-COMMERCIAL-METADATA", Deliverables = "Synthetic fixture",
          PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Fee = 20000m, Currency = "QAR", CreatedAt = now
        });
        db.QuotationVersions.Add(new QuotationVersion
        {
          Id = quotation, FirmId = f.FirmId, ProposalId = proposal, Currency = "QAR", BaseAmount = 20000m,
          Fee = 20000m, InputHash = new string('a', 64), CreatedByUserId = f.Admin.Id, CreatedAt = now
        });
        db.EngagementFeeAgreements.Add(new EngagementFeeAgreement
        {
          Id = agreement, FirmId = f.FirmId, PracticeClientId = clientId, ProposalId = proposal, QuotationVersionId = quotation,
          Currency = "QAR", AgreedFee = 20000m, AdvancePercent = 50m, CreatedByUserId = f.Admin.Id, CreatedAt = now.AddSeconds(index)
        });
        foreach (var kind in new[] { FeeMilestoneKinds.Advance, FeeMilestoneKinds.Balance })
        {
          var milestoneId = Guid.NewGuid();
          db.FeeMilestones.Add(new FeeMilestone
          {
            Id = milestoneId, FirmId = f.FirmId, AgreementId = agreement, Kind = kind,
            Amount = 10000m, State = FeeMilestoneStates.Planned, CreatedAt = now
          });
          if (index == 20 && kind == FeeMilestoneKinds.Advance) advanceMilestoneId = milestoneId;
          if (index == 19 && kind == FeeMilestoneKinds.Advance) postedOrphanMilestoneId = milestoneId;
        }
        if (index == 20) { agreementId = agreement; proposalId = proposal; }
        if (index == 21) siblingAgreementId = agreement;
        if (index == 19) postedOrphanAgreementId = agreement;
      }
      await db.SaveChangesAsync();
      // Simulate the legacy interruption after the existing draft/source was saved but before
      // the milestone link. Do not erase a committed link or rewrite issued evidence.
      var financeActor = PbcSeed.Actor(manager, "FinanceManager");
      var account = await BillingService.CreateBillingAccountAsync(db, financeActor,
        new CreateBillingAccountRequest(f.ClientId, "QAR"));
      Assert.True(account.Succeeded, account.Message);
      var draft = await BillingService.CreateInvoiceDraftAsync(db, financeActor,
        new CreateInvoiceDraftRequest(account.Value, "SYN-FEE-INTERRUPTED-DRAFT",
          [new InvoiceLineRequest("Synthetic advance source", 1m, 10000m,
            FeeAgreementService.MilestoneSourceKind, advanceMilestoneId, 1)]));
      Assert.True(draft.Succeeded, draft.Message);
      retainedDraftInvoiceId = draft.Value;
      var postedSource = await BillingService.CreateInvoiceDraftAsync(db, financeActor,
        new CreateInvoiceDraftRequest(account.Value, "SYN-FEE-POSTED-ORPHAN",
          [new InvoiceLineRequest("Synthetic posted source", 1m, 10000m,
            FeeAgreementService.MilestoneSourceKind, postedOrphanMilestoneId, 1)]));
      Assert.True(postedSource.Succeeded, postedSource.Message);
      retainedPostedInvoiceId = postedSource.Value;
      Assert.True((await BillingService.SubmitInvoiceAsync(db, financeActor, retainedPostedInvoiceId)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, PbcSeed.Actor(reviewer, "FinanceReviewer"), retainedPostedInvoiceId)).Succeeded);
      Assert.True((await BillingService.PostInvoiceAsync(db, financeActor, retainedPostedInvoiceId)).Succeeded);
    }

    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true" };
    var origin = await host.StartApiForIdentityAsync(manager, settings);
    var reviewerOrigin = await host.StartApiForIdentityAsync(reviewer, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    await using var reviewerContext = await browser.NewContextAsync();
    var page = await context.NewPageAsync(); var reviewerPage = await reviewerContext.NewPageAsync();
    var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e); reviewerPage.PageError += (_, e) => errors.Add(e);
    const string listPath = "/app/finance/fee-agreements";
    var navigation = await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(listPath));
    Assert.True(navigation?.Status == 200, $"Finance navigation returned {navigation?.Status} at {page.Url}.");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Fee settlements", Exact = true }).WaitForAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Rowheader, new() { Name = "PBC TEST CLIENT", Exact = true }).First).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "SYNTHETIC UNAUTHORIZED FEE CLIENT", Exact = true })).ToHaveCountAsync(0);
    await using (var list = await context.APIRequest.GetAsync(origin + "/api/ui/finance/fee-agreements?page=0"))
    {
      Assert.Equal(200, list.Status);
      using var json = JsonDocument.Parse(await list.TextAsync());
      Assert.Equal(20, json.RootElement.GetProperty("rows").GetArrayLength());
      Assert.True(json.RootElement.GetProperty("hasMore").GetBoolean());
      Assert.All(json.RootElement.GetProperty("rows").EnumerateArray(), row => Assert.Equal(f.ClientId, row.GetProperty("clientId").GetGuid()));
    }
    await using (var second = await context.APIRequest.GetAsync(origin + "/api/ui/finance/fee-agreements?page=1"))
    {
      Assert.Equal(200, second.Status);
      using var json = JsonDocument.Parse(await second.TextAsync());
      Assert.Single(json.RootElement.GetProperty("rows").EnumerateArray());
      Assert.False(json.RootElement.GetProperty("hasMore").GetBoolean());
    }
    await page.GetByRole(AriaRole.Link, new() { Name = "Next page", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Rowheader, new() { Name = "PBC TEST CLIENT", Exact = true })).ToHaveCountAsync(1);
    await page.GetByRole(AriaRole.Link, new() { Name = "Previous page", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Rowheader, new() { Name = "PBC TEST CLIENT", Exact = true })).ToHaveCountAsync(20);
    foreach (var denied in new[] { $"/api/ui/proposals/{proposalId}", $"/api/ui/proposals/{proposalId}/fee-agreement",
      $"/api/ui/finance/fee-agreements/{siblingAgreementId}" })
    {
      await using var response = await context.APIRequest.GetAsync(origin + denied);
      Assert.Equal(403, response.Status);
      Assert.DoesNotContain("PRIVATE-COMMERCIAL-METADATA", await response.TextAsync());
    }
    var siblingMutation = await page.EvaluateAsync<string>("""
      async agreement => {
        await fetch('/api/ui/session');
        const cookie = document.cookie.split(';').map(value => value.trim()).find(value => value.startsWith('XSRF-TOKEN='));
        const token = cookie ? decodeURIComponent(cookie.slice('XSRF-TOKEN='.length)) : '';
        const response = await fetch(`/api/ui/fee-agreements/${agreement}/advance-payment`, {
          method: 'POST', headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
          body: JSON.stringify({ amount: '1', reference: 'SYN-DENIED-SIBLING', reviewed: true })
        });
        return JSON.stringify({ status: response.status, body: await response.json() });
      }
      """, siblingAgreementId.ToString("D"));
    using (var denied = JsonDocument.Parse(siblingMutation))
    {
      Assert.Equal(400, denied.RootElement.GetProperty("status").GetInt32());
      Assert.Equal(AuditSphereOps.Domain.Shared.ErrorCodes.ScopeDenied, denied.RootElement.GetProperty("body").GetProperty("code").GetString());
    }
    await using (var db = host.CreateDbContext())
      Assert.False(await db.BillingAccounts.AnyAsync(x => x.FirmId == f.FirmId && x.PracticeClientId == siblingClient));
    var path = listPath + "/" + agreementId;
    await page.GetByRole(AriaRole.Link, new() { Name = $"Open fee settlement for PBC TEST CLIENT ({agreementId})", Exact = true }).ClickAsync();
    var fees = page.Locator("audit-fee-agreement");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Client fee settlement", Level = 1, Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(fees.GetByRole(AriaRole.Table, new() { Name = "Persisted fee milestones", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(fees).ToContainTextAsync("PBC TEST CLIENT");
    await Assertions.Expect(fees).ToContainTextAsync(agreementId.ToString("D"));
    Assert.DoesNotContain("PRIVATE-COMMERCIAL-METADATA", await fees.InnerTextAsync());
    await Assertions.Expect(fees.GetByRole(AriaRole.Button, new() { Name = "Create fee agreement", Exact = true })).ToHaveCountAsync(0);
    var assent = fees.GetByLabel("I reviewed this fee, milestone and selected action.", new() { Exact = true });
    await assent.CheckAsync();
    await fees.GetByRole(AriaRole.Button, new() { Name = "Prepare or recover advance invoice", Exact = true }).ClickAsync();
    await Assertions.Expect(fees.GetByRole(AriaRole.Link, new() { Name = "SYN-FEE-INTERRUPTED-DRAFT", Exact = true })).ToBeVisibleAsync();
    Guid invoiceId;
    await using (var db = host.CreateDbContext())
      invoiceId = (await db.FeeMilestones.AsNoTracking().SingleAsync(x => x.AgreementId == agreementId && x.Kind == FeeMilestoneKinds.Advance)).InvoiceId!.Value;
    Assert.Equal(retainedDraftInvoiceId, invoiceId);
    await using (var db = host.CreateDbContext()) Assert.Equal(2, await db.Invoices.CountAsync(x => x.FirmId == f.FirmId));
    var invoicePath = $"/app/practice/invoices/{invoiceId}";
    await page.GotoAsync(origin + invoicePath);
    await page.GetByRole(AriaRole.Button, new() { Name = "Submit invoice for review", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Approval requires a separate authorized FinanceReviewer.", new() { Exact = true })).ToBeVisibleAsync();
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(invoicePath));
    await reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Approve invoice", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.GetByText("Posting requires an authorized FinanceManager and an approved firm finance profile in the invoice currency.", new() { Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh invoice", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Post invoice (freeze & emit ledger event)", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Mark sent to client", Exact = true })).ToBeVisibleAsync();
    // The same recovery action remains usable after an independent invoice workflow has posted
    // an otherwise exact retained source; it must preserve its ID and posted history.
    await page.GotoAsync(origin + listPath + "/" + postedOrphanAgreementId);
    await fees.GetByRole(AriaRole.Table, new() { Name = "Persisted fee milestones", Exact = true }).WaitForAsync();
    await assent.CheckAsync();
    await fees.GetByRole(AriaRole.Button, new() { Name = "Prepare or recover advance invoice", Exact = true }).ClickAsync();
    await fees.GetByRole(AriaRole.Heading, new() { Name = "Record manual advance payment", Exact = true }).WaitForAsync();
    await using (var db = host.CreateDbContext())
    {
      Assert.Equal(retainedPostedInvoiceId, await db.FeeMilestones.Where(x => x.Id == postedOrphanMilestoneId).Select(x => x.InvoiceId).SingleAsync());
      Assert.Equal(BillingStates.InvoicePosted, await db.Invoices.Where(x => x.Id == retainedPostedInvoiceId).Select(x => x.Status).SingleAsync());
      Assert.Equal(2, await db.Invoices.CountAsync(x => x.FirmId == f.FirmId));
    }
    await page.GotoAsync(origin + path);
    await fees.GetByRole(AriaRole.Heading, new() { Name = "Record manual advance payment", Exact = true }).WaitForAsync();
    async Task PayAsync(string amount, string reference)
    {
      await fees.GetByLabel("Amount received", new() { Exact = true }).FillAsync(amount);
      await fees.GetByLabel("Bank or transfer reference", new() { Exact = true }).FillAsync(reference);
      await assent.CheckAsync();
      await fees.GetByRole(AriaRole.Button, new() { Name = "Record advance payment", Exact = true }).ClickAsync();
    }
    await PayAsync("4000", "SYN-FINANCE-ADVANCE-PARTIAL");
    await Assertions.Expect(fees.GetByText("Partial payment recorded. Review remaining outstanding amount.", new() { Exact = false })).ToBeVisibleAsync();
    await Assertions.Expect(assent).Not.ToBeCheckedAsync();
    await using (var db = host.CreateDbContext())
    {
      Assert.Equal(4000m, await BillingService.GetNetAppliedToInvoiceAsync(db, f.FirmId, invoiceId));
      Assert.False(await FeeAgreementService.AdvanceCurrentlyPaidAsync(db, f.FirmId, agreementId));
    }
    await page.SetViewportSizeAsync(390, 844);
    Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"),
      "The narrow fee workspace must contain its table overflow without widening the document.");
    var milestoneScroll = fees.GetByRole(AriaRole.Region, new() { Name = "Fee milestone details", Exact = true });
    await milestoneScroll.FocusAsync();
    await Assertions.Expect(milestoneScroll).ToBeFocusedAsync();
    await milestoneScroll.PressAsync("ArrowRight");
    var scrolledByKeyboard = false;
    for (var attempt = 0; attempt < 40; attempt++)
    {
      scrolledByKeyboard = await milestoneScroll.EvaluateAsync<bool>("element => element.scrollLeft > 0");
      if (scrolledByKeyboard) break;
      await Task.Delay(25);
    }
    Assert.True(scrolledByKeyboard, "The focused milestone region must scroll horizontally through native keyboard input.");
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, path, "finance-fee-partial");
    await page.SetViewportSizeAsync(1280, 900);
    await PayAsync("6000", "SYN-FINANCE-ADVANCE-FINAL");
    await Assertions.Expect(fees.GetByText("Advance paid", new() { Exact = false }).Last).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      Assert.Equal(10000m, await BillingService.GetNetAppliedToInvoiceAsync(db, f.FirmId, invoiceId));
      Assert.True(await FeeAgreementService.AdvanceCurrentlyPaidAsync(db, f.FirmId, agreementId));
      Assert.Equal(2, await db.ReceiptAllocations.CountAsync(x => x.FirmId == f.FirmId && x.InvoiceId == invoiceId));
      await db.RoleGrants.Where(x => x.FirmId == f.FirmId && x.UserId == manager.Id && x.Role == "FinanceManager")
        .ExecuteUpdateAsync(x => x.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
    }
    await fees.GetByRole(AriaRole.Button, new() { Name = "Refresh fee agreement", Exact = true }).ClickAsync();
    await Assertions.Expect(fees.GetByRole(AriaRole.Table, new() { Name = "Persisted fee milestones", Exact = true })).ToHaveCountAsync(0);
    await using (var revoked = await context.APIRequest.GetAsync(origin + "/api/ui/finance/fee-agreements")) Assert.Equal(403, revoked.Status);
    Assert.Empty(errors);
  }
}
