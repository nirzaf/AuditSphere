using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularClientPortalJourneyTests
{
  [Fact]
  public async Task ClientReplyAndUploadAvailabilityFollowReviewClarificationAndResubmission()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-PBC-CAPABILITIES");
    var f = host.Fixture;
    var staged = await PbcSeed.StageUploadAsync(host.Database, f, PbcSeed.Actor(f.Client, "ClientUser"), host.RequestId,
      "Synthetic first submission"u8.ToArray());
    try
    {
      var (store, handler) = PbcSeed.Boundary(
        new SimulationPbcProviderSink(Path.Combine(staged.StagingRoot, "provider")), host.Database);
      await using (var db = host.CreateDbContext())
      {
        var complete = await PbcService.CompleteUploadAsync(db, PbcSeed.Actor(f.Staff, "Staff"),
          new(staged.UploadIntentId, staged.DeclaredSha256Hex), store, handler);
        Assert.True(complete.Succeeded, complete.Message);
      }
      var options = new WorkerOptions(f.FirmId, "Test", AllowSimulationAdapters: true);
      Assert.True(await new OperationDispatcher(store, new DurableOperationRegistry([handler], options), options).ProcessNextAsync());
      await using (var db = host.CreateDbContext())
      {
        var request = await db.PbcRequests.SingleAsync(x => x.Id == host.RequestId);
        var review = await PbcService.ChangeStateAsync(db, PbcSeed.Actor(f.Reviewer, "Reviewer"),
          new(host.RequestId, PbcStates.UnderReview, request.Revision));
        Assert.True(review.Succeeded, review.Message);
      }
      var origin = await host.StartApiForIdentityAsync(f.Client, new Dictionary<string, string> {
        ["AngularUi__Enabled"] = "true"
      });
      using var playwright = await Playwright.CreateAsync();
      await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
      await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 320, Height = 844 } });
      var page = await context.NewPageAsync();
      var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
      await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fportal");
      var replyLink = page.GetByRole(AriaRole.Link, new() { Name = "Reply to audit team for Cash", Exact = false });
      var provideLink = page.GetByRole(AriaRole.Link, new() { Name = "Provide requested document for Cash", Exact = false });
      await Assertions.Expect(replyLink).ToBeVisibleAsync();
      await Assertions.Expect(provideLink).ToHaveCountAsync(0);
      await replyLink.ClickAsync();
      var chooser = page.GetByLabel("Choose a file (up to 250 MB)", new() { Exact = true });
      await Assertions.Expect(chooser).ToBeDisabledAsync();
      var reply = page.GetByLabel("Reply to the audit team", new() { Exact = true });
      await reply.FillAsync("Please advise if further details are required.");
      await page.GetByRole(AriaRole.Button, new() { Name = "Send reply", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByText("Reply added to the request timeline.", new() { Exact = true })).ToBeVisibleAsync();
      await using (var db = host.CreateDbContext())
      {
        Assert.Equal(PbcStates.UnderReview, (await db.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == host.RequestId)).State);
        var clarification = await PbcService.RequestMoreFilesAsync(db, PbcSeed.Actor(f.Staff, "Staff"),
          new(host.RequestId, "Please supply the missing final page.", origin));
        Assert.True(clarification.Succeeded, clarification.Message);
      }
      await page.GetByRole(AriaRole.Button, new() { Name = "Refresh request", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Auditor note / rejection reason:" })
        .GetByText("Please supply the missing final page.", new() { Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(chooser).ToBeDisabledAsync();
      await Assertions.Expect(page.GetByText("Reply to the audit team before providing replacement files.", new() { Exact = true })).ToBeVisibleAsync();
      var path = $"/ui/portal/requests/{host.RequestId}";
      await OwnedBrowserFixtureHandoff.WaitAsync(origin, path, "pbc-capabilities");
      await reply.FillAsync("The replacement final page follows in this resubmission.");
      await page.GetByRole(AriaRole.Button, new() { Name = "Send reply", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByText("Reply added to the request timeline.", new() { Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(chooser).ToBeEnabledAsync();
      await using (var afterReply = host.CreateDbContext())
        Assert.Equal(PbcStates.Resubmitted, (await afterReply.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == host.RequestId)).State);
      await page.GetByRole(AriaRole.Navigation, new() { Name = "Request location", Exact = true })
        .GetByRole(AriaRole.Link, new() { Name = "Client portal", Exact = true }).ClickAsync();
      await Assertions.Expect(provideLink).ToBeVisibleAsync();
      await provideLink.ClickAsync();
      var bytes = "Synthetic replacement final page"u8.ToArray();
      await chooser.SetInputFilesAsync(new FilePayload { Name = "replacement-final-page.txt", MimeType = "text/plain", Buffer = bytes });
      var upload = page.GetByRole(AriaRole.Button, new() { Name = "Upload file", Exact = true });
      await Assertions.Expect(upload).ToBeEnabledAsync();
      await upload.ClickAsync();
      await Assertions.Expect(page.GetByText("File bytes staged. Your audit team must verify trusted completion and suitability before the request is received or accepted.", new() { Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "replacement-final-page.txt", Exact = true })).ToBeVisibleAsync();
      await using (var db = host.CreateDbContext())
      {
        var replacement = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x =>
          x.PbcRequestId == host.RequestId && x.FileName == "replacement-final-page.txt");
        Assert.NotEqual(staged.UploadIntentId, replacement.Id);
        Assert.Equal(PbcUploadStates.Chunking, replacement.State);
        Assert.Equal(bytes.Length, replacement.ReceivedByteCount);
        Assert.Equal(Hashing.Sha256Hex(bytes), replacement.DeclaredSha256Hex);
        // The reply opens resubmission; starting the replacement upload then moves it to partial receipt,
        // never provider-received or reviewer-accepted merely because browser staging completed.
        Assert.Equal(PbcStates.PartiallyReceived, (await db.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == host.RequestId)).State);
        Assert.Empty(await db.PbcAcceptanceReceipts.ToListAsync());
      }
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
      Assert.Empty(errors);
    }
    finally { PbcSeed.DeleteDirectory(staged.StagingRoot); }
  }

  [Fact]
  public async Task ClientSeesRetainedAcceptanceForOnlyTheExactReviewedUpload()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-PBC-ACCEPTANCE");
    var f = host.Fixture;
    var clientActor = PbcSeed.Actor(f.Client, "ClientUser");
    var staged = await PbcSeed.StageUploadAsync(host.Database, f, clientActor, host.RequestId,
      "Synthetic accepted-file evidence"u8.ToArray(), "reviewed-synthetic-support.txt", "text/plain");
    try
    {
      var origin = await host.StartApiForIdentityAsync(f.Client, new Dictionary<string, string> {
        ["AngularUi__Enabled"] = "true"
      });
      using var playwright = await Playwright.CreateAsync();
      await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
      await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 390, Height = 844 } });
      var page = await context.NewPageAsync();
      var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
      var path = $"/ui/portal/requests/{host.RequestId}";
      await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "PBC request", Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Recorded reviewer acceptance", Exact = true })).ToHaveCountAsync(0);

      // Real local transfer/review commands against the owned synthetic database. This proves application
      // projection, not a human professional review or live SharePoint acceptance.
      var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(host.DbOptions));
      var store = new PostgresOperationStore(factory);
      var handler = new PbcDocumentTransferHandler(factory,
        new SimulationPbcProviderSink(Path.Combine(staged.StagingRoot, "provider")));
      await using (var db = host.CreateDbContext())
      {
        var complete = await PbcService.CompleteUploadAsync(db, PbcSeed.Actor(f.Staff, "Staff"),
          new(staged.UploadIntentId, staged.DeclaredSha256Hex), store, handler);
        Assert.True(complete.Succeeded, complete.Message);
      }
      var options = new WorkerOptions(f.FirmId, "Test", AllowSimulationAdapters: true);
      Assert.True(await new OperationDispatcher(store, new DurableOperationRegistry([handler], options), options).ProcessNextAsync());
      PbcAcceptanceReceipt retained;
      await using (var db = host.CreateDbContext())
      {
        var request = await db.PbcRequests.SingleAsync(x => x.Id == host.RequestId);
        var review = await PbcService.ChangeStateAsync(db, PbcSeed.Actor(f.Reviewer, "Reviewer"),
          new(host.RequestId, PbcStates.UnderReview, request.Revision));
        Assert.True(review.Succeeded, review.Message);
        var upload = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == staged.UploadIntentId);
        var accept = await PbcService.ChangeStateAsync(db, PbcSeed.Actor(f.Reviewer, "Reviewer"),
          new(host.RequestId, PbcStates.Accepted, request.Revision, ReviewedUploadIntentId: upload.Id,
            ReviewedUploadRevision: upload.Revision, ReviewedContentSha256Hex: staged.DeclaredSha256Hex));
        Assert.True(accept.Succeeded, accept.Message);
        retained = await db.PbcAcceptanceReceipts.AsNoTracking().SingleAsync(x => x.PbcRequestId == host.RequestId);
      }
      await page.GetByRole(AriaRole.Button, new() { Name = "Refresh request", Exact = true }).ClickAsync();
      var acceptance = page.GetByRole(AriaRole.Region, new() { Name = "Recorded reviewer acceptance", Exact = true });
      await Assertions.Expect(acceptance).ToBeVisibleAsync();
      await Assertions.Expect(acceptance).ToContainTextAsync(retained.FileName);
      await Assertions.Expect(acceptance).ToContainTextAsync(retained.ContentSha256Hex);
      await Assertions.Expect(acceptance).ToContainTextAsync(retained.PbcUploadIntentId.ToString());
      Assert.Equal(retained.AcceptedAt, DateTimeOffset.Parse((await acceptance.Locator("time").GetAttributeAsync("datetime"))!));
      await Assertions.Expect(acceptance.Locator("time")).ToHaveTextAsync(retained.AcceptedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture));
      var response = await context.APIRequest.GetAsync(origin + $"/api/ui/portal/requests/{host.RequestId}");
      Assert.Equal(200, response.Status);
      var body = await response.TextAsync();
      Assert.DoesNotContain(retained.ProviderIdentity, body, StringComparison.Ordinal);
      Assert.DoesNotContain(retained.SourceDigest, body, StringComparison.Ordinal);
      Assert.DoesNotContain(retained.ReviewerUserId.ToString(), body, StringComparison.OrdinalIgnoreCase);
      Assert.DoesNotContain("providerReceiptDigest", body, StringComparison.Ordinal);
      await using (var db = host.CreateDbContext())
      {
        var request = await db.PbcRequests.SingleAsync(x => x.Id == host.RequestId);
        var close = await PbcService.ChangeStateAsync(db, PbcSeed.Actor(f.Reviewer, "Reviewer"),
          new(host.RequestId, PbcStates.Closed, request.Revision));
        Assert.True(close.Succeeded, close.Message);
      }
      await page.GetByRole(AriaRole.Button, new() { Name = "Refresh request", Exact = true }).ClickAsync();
      await Assertions.Expect(acceptance).ToBeVisibleAsync();
      await Assertions.Expect(acceptance).ToContainTextAsync(retained.FileName);
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
      Assert.Empty(errors);
      await OwnedBrowserFixtureHandoff.WaitAsync(origin, path, "pbc-acceptance");
      await using (var db = host.CreateDbContext())
        await db.Users.Where(x => x.Id == f.Client.Id).ExecuteUpdateAsync(s => s
          .SetProperty(x => x.Disabled, true).SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
      Assert.DoesNotContain(retained.FileName, await page.Locator("body").InnerTextAsync());
    }
    finally { PbcSeed.DeleteDirectory(staged.StagingRoot); }
  }

  [Fact]
  [Trait("CaseId", "ASPM-COM06-CLIENT-FINANCE")]
  public async Task ClientPortal_FinanceTablesAreScopedAndKeyboardScrollableOnMobile()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ASPM-COM06-CLIENT-FINANCE");
    var f = host.Fixture;
    var now = DateTimeOffset.UtcNow;
    var dueDate = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-10);
    var agreementId = Guid.NewGuid();
    var siblingAgreementId = Guid.NewGuid();
    var invoiceId = Guid.NewGuid();
    var receiptId = Guid.NewGuid();
    var clientUser = f.Client; // Seeded ClientUser identity with a completed first sign-in and exact engagement grant.

    // These persisted rows are projection fixtures only. They exercise the real authenticated API/browser rendering,
    // not proposal, invoice, payment-terms or receipt approval policy. No commercial-document bytes are fabricated.
    await using (var db = host.CreateDbContext())
    {
      var otherEngagementId = Guid.NewGuid();
      var primaryLeadId = Guid.NewGuid();
      var primaryOpportunityId = Guid.NewGuid();
      var primaryProposalId = Guid.NewGuid();
      var siblingLeadId = Guid.NewGuid();
      var siblingOpportunityId = Guid.NewGuid();
      var siblingProposalId = Guid.NewGuid();
      var accountId = Guid.NewGuid();

      db.Engagements.Add(new Engagement
      {
        Id = otherEngagementId, FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = now
      });
      db.Leads.AddRange(
        new Lead { Id = primaryLeadId, FirmId = f.FirmId, Name = "Synthetic portal finance fixture", Source = "Referral", CreatedAt = now },
        new Lead { Id = siblingLeadId, FirmId = f.FirmId, Name = "Synthetic sibling finance fixture", Source = "Referral", CreatedAt = now });
      db.Opportunities.AddRange(
        new Opportunity
        {
          Id = primaryOpportunityId, FirmId = f.FirmId, LeadId = primaryLeadId, ServiceRoute = "FinancialStatementAudit",
          EntityScope = "ASPM-COM06", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
          ExpectedFee = 20000m, Currency = "QAR", CreatedAt = now
        },
        new Opportunity
        {
          Id = siblingOpportunityId, FirmId = f.FirmId, LeadId = siblingLeadId, ServiceRoute = "FinancialStatementAudit",
          EntityScope = "ASPM-COM06-SIBLING", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
          ExpectedFee = 30000m, Currency = "QAR", CreatedAt = now
        });
      db.Proposals.AddRange(
        new Proposal
        {
          Id = primaryProposalId, FirmId = f.FirmId, OpportunityId = primaryOpportunityId, PracticeClientId = f.ClientId,
          ServiceProfileId = "ASPM-COM06-FIXTURE", Scope = "Synthetic finance projection fixture", Exclusions = "",
          Deliverables = "Synthetic projection only", Dependencies = "", Fee = 20000m, Currency = "QAR", PeriodStart = "2026-01-01",
          PeriodEnd = "2026-12-31", PreparedByUserId = f.Staff.Id, CreatedAt = now
        },
        new Proposal
        {
          Id = siblingProposalId, FirmId = f.FirmId, OpportunityId = siblingOpportunityId, PracticeClientId = f.ClientId,
          ServiceProfileId = "ASPM-COM06-SIBLING-FIXTURE", Scope = "Synthetic sibling scope decoy", Exclusions = "",
          Deliverables = "Synthetic sibling projection only", Dependencies = "", Fee = 30000m, Currency = "QAR", PeriodStart = "2026-01-01",
          PeriodEnd = "2026-12-31", PreparedByUserId = f.Staff.Id, CreatedAt = now
        });
      db.EngagementFeeAgreements.AddRange(
        new EngagementFeeAgreement
        {
          Id = agreementId, FirmId = f.FirmId, ProposalId = primaryProposalId, QuotationVersionId = Guid.NewGuid(),
          PracticeClientId = f.ClientId, EngagementId = f.EngagementId, Currency = "QAR", AgreedFee = 20000m,
          AdvancePercent = 50m, CreatedByUserId = f.Staff.Id, CreatedAt = now
        },
        new EngagementFeeAgreement
        {
          Id = siblingAgreementId, FirmId = f.FirmId, ProposalId = siblingProposalId, QuotationVersionId = Guid.NewGuid(),
          PracticeClientId = f.ClientId, EngagementId = otherEngagementId, Currency = "QAR", AgreedFee = 30000m,
          AdvancePercent = 50m, CreatedByUserId = f.Staff.Id, CreatedAt = now
        });

      db.BillingAccounts.Add(new BillingAccount { Id = accountId, FirmId = f.FirmId, PracticeClientId = f.ClientId, Currency = "QAR", CreatedAt = now });
      var client = await db.PracticeClients.SingleAsync(x => x.Id == f.ClientId && x.FirmId == f.FirmId);
      client.BillingAccountId = accountId;
      db.Invoices.Add(new Invoice
      {
        Id = invoiceId, FirmId = f.FirmId, BillingAccountId = accountId, InvoiceNumber = "INV-ASPM-COM06-ADV",
        Currency = "QAR", Subtotal = 10000m, Tax = 0m, Total = 10000m, Revision = 1,
        Status = BillingStates.InvoiceSent, CreatedByUserId = f.Staff.Id, ApprovedByUserId = f.Reviewer.Id,
        ApprovedAt = now.AddDays(-12), PostedAt = now.AddDays(-11), SentAt = now.AddDays(-10), CreatedAt = now.AddDays(-12)
      });
      db.FeeMilestones.Add(new FeeMilestone
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, AgreementId = agreementId, Kind = FeeMilestoneKinds.Advance,
        Amount = 10000m, State = FeeMilestoneStates.Invoiced, InvoiceId = invoiceId, CreatedAt = now.AddDays(-12)
      });
      db.InvoicePaymentTermsRevisions.Add(new InvoicePaymentTermsRevision
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, InvoiceId = invoiceId, Revision = 1, DueDate = dueDate,
        Basis = InvoicePaymentTermsKinds.ContractualDueDate, TermsDescription = "Synthetic approved due-date projection fixture",
        EvidenceReference = "fixture:ASPM-COM06-terms", Status = InvoicePaymentTermsStates.Approved,
        SubmittedByUserId = f.Staff.Id, SubmittedAt = now.AddDays(-12), ReviewedByUserId = f.Reviewer.Id,
        ReviewedAt = now.AddDays(-11), ReviewReason = "Synthetic fixture state for browser projection only."
      });
      db.Receipts.Add(new Receipt
      {
        Id = receiptId, FirmId = f.FirmId, BillingAccountId = accountId, Amount = 4000m, Currency = "QAR",
        Reference = "TT-ASPM-COM06-001", Status = BillingStates.ReceiptRecorded, RecordedByUserId = f.Staff.Id,
        ReceivedAt = now.AddDays(-1)
      });
      db.ReceiptAllocations.Add(new ReceiptAllocation
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ReceiptId = receiptId, InvoiceId = invoiceId,
        Amount = 4000m, CreatedAt = now.AddDays(-1)
      });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(clientUser,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 390, Height = 844 } });
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/ui/portal"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Client portal", Exact = true })).ToBeVisibleAsync();
    var finance = page.Locator("audit-portal-finance");
    await Assertions.Expect(finance.GetByRole(AriaRole.Heading, new() { Name = "Billing and payment receipts", Exact = true })).ToBeVisibleAsync();
    var invoiceTable = finance.GetByRole(AriaRole.Table, new() { Name = "Fee invoices", Exact = true });
    var invoiceRow = invoiceTable.Locator("tbody tr").First;
    await Assertions.Expect(invoiceRow).ToContainTextAsync("INV-ASPM-COM06-ADV");
    await Assertions.Expect(invoiceRow).ToContainTextAsync("sent");
    await Assertions.Expect(invoiceRow).ToContainTextAsync("partially paid");
    await Assertions.Expect(invoiceRow).ToContainTextAsync("overdue 1 30");
    await Assertions.Expect(invoiceRow).ToContainTextAsync("not tracked");
    await Assertions.Expect(invoiceRow).ToContainTextAsync(dueDate.ToString("yyyy-MM-dd"));
    await Assertions.Expect(finance.Locator(".agreement-card")).ToHaveCountAsync(1);
    await Assertions.Expect(finance.GetByRole(AriaRole.Heading, new() { Level = 3 }))
      .ToContainTextAsync(new System.Text.RegularExpressions.Regex(@"Agreed fee: 20000(?:\.0+)? QAR"));
    await Assertions.Expect(invoiceRow.Locator("td").Nth(5))
      .ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^10000(?:\.0+)? QAR$"));
    await Assertions.Expect(invoiceRow.Locator("td").Nth(6))
      .ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^4000(?:\.0+)? QAR$"));
    await Assertions.Expect(invoiceRow.Locator("td").Nth(8))
      .ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^6000(?:\.0+)? QAR$"));
    await Assertions.Expect(finance.GetByRole(AriaRole.Table, new() { Name = "Payment receipts", Exact = true }))
      .ToContainTextAsync("TT-ASPM-COM06-001");
    await Assertions.Expect(finance.GetByText("Retained", new() { Exact = true })).ToBeVisibleAsync();
    var financeText = await finance.InnerTextAsync();
    foreach (var privateLabel in new[] { "Staff cost", "Partner rate", "Manager rate", "Internal margin", "Internal notes" })
      Assert.False(financeText.Contains(privateLabel, StringComparison.OrdinalIgnoreCase), $"Client finance exposed '{privateLabel}'.");

    var invoiceRegion = page.GetByRole(AriaRole.Region, new() { Name = "Scrollable fee invoices", Exact = true });
    var receiptsRegion = page.GetByRole(AriaRole.Region, new() { Name = "Scrollable payment receipts", Exact = true });
    await Assertions.Expect(invoiceRegion).ToBeVisibleAsync();
    await Assertions.Expect(receiptsRegion).ToBeVisibleAsync();
    Assert.True(await invoiceRegion.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 1"), "The invoice table must scroll inside its labeled region at 390px.");
    await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
    var reachedInvoiceRegionByTab = false;
    for (var tab = 0; tab < 80 && !reachedInvoiceRegionByTab; tab++)
    {
      await page.Keyboard.PressAsync("Tab");
      reachedInvoiceRegionByTab = await invoiceRegion.EvaluateAsync<bool>("el => document.activeElement === el");
    }
    Assert.True(reachedInvoiceRegionByTab, "The invoice scroll region must be reachable through real Tab navigation.");
    await invoiceRegion.EvaluateAsync("el => { el.scrollLeft = 0; }");
    var scrollBefore = await invoiceRegion.EvaluateAsync<int>("el => el.scrollLeft");
    await page.Keyboard.PressAsync("ArrowRight");
    await Assertions.Expect(invoiceRegion).Not.ToHaveJSPropertyAsync("scrollLeft", scrollBefore);
    var scrollAfter = await invoiceRegion.EvaluateAsync<int>("el => el.scrollLeft");
    Assert.True(scrollAfter > scrollBefore, "ArrowRight should move the focused invoice region horizontally.");
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(receiptsRegion).ToBeFocusedAsync();
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"),
      "Finance tables must not cause document-level horizontal overflow at 390px.");
    // Additional projection-only agreements exercise bounded history, not commercial approval or payment policy.
    Guid oldestAgreementId;
    await using (var db = host.CreateDbContext())
    {
      var sourceAgreement = await db.EngagementFeeAgreements.AsNoTracking().SingleAsync(x => x.Id == agreementId);
      var sourceProposal = await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == sourceAgreement.ProposalId);
      var olderAgreements = Enumerable.Range(0, 50).Select(index =>
      {
        var proposal = new Proposal
        {
          Id = Guid.CreateVersion7(), FirmId = f.FirmId, OpportunityId = sourceProposal.OpportunityId,
          PracticeClientId = f.ClientId, Revision = index + 2, ServiceProfileId = "ASPM-COM06-PAGING",
          Scope = "Synthetic finance paging projection", Deliverables = "Projection fixture only",
          Fee = 70000m + index, Currency = "QAR", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
          PreparedByUserId = f.Staff.Id, CreatedAt = now.AddMinutes(-1).AddSeconds(-index)
        };
        var agreement = new EngagementFeeAgreement
        {
          Id = Guid.CreateVersion7(), FirmId = f.FirmId, ProposalId = proposal.Id, QuotationVersionId = Guid.CreateVersion7(),
          PracticeClientId = f.ClientId, EngagementId = f.EngagementId, Currency = "QAR", AgreedFee = proposal.Fee,
          AdvancePercent = 50m, CreatedByUserId = f.Staff.Id, CreatedAt = proposal.CreatedAt
        };
        return (proposal, agreement);
      }).ToArray();
      db.Proposals.AddRange(olderAgreements.Select(x => x.proposal));
      db.EngagementFeeAgreements.AddRange(olderAgreements.Select(x => x.agreement));
      await db.SaveChangesAsync();
      oldestAgreementId = olderAgreements[^1].agreement.Id;
    }
    await finance.GetByRole(AriaRole.Button, new() { Name = "Refresh finance from start", Exact = true }).ClickAsync();
    await Assertions.Expect(finance.Locator(".agreement-card")).ToHaveCountAsync(50);
    await Assertions.Expect(finance.GetByRole(AriaRole.Table, new() { Name = "Fee invoices", Exact = true }))
      .ToContainTextAsync("INV-ASPM-COM06-ADV");
    var olderAgreementsButton = finance.GetByRole(AriaRole.Button, new() { Name = "Older agreements", Exact = true });
    await olderAgreementsButton.FocusAsync();
    var (olderStatus, olderBody) = await PlaywrightResponseCapture.RunAsync(page,
      request => request.Method == "GET" && request.Url.Contains("/api/ui/portal/finance?", StringComparison.Ordinal),
      () => olderAgreementsButton.PressAsync("Space"));
    Assert.Equal(200, olderStatus);
    using (var body = System.Text.Json.JsonDocument.Parse(olderBody))
      Assert.Equal(oldestAgreementId, Assert.Single(body.RootElement.GetProperty("agreements").EnumerateArray()).GetProperty("agreementId").GetGuid());
    await Assertions.Expect(finance.Locator(".agreement-card")).ToHaveCountAsync(1);
    await Assertions.Expect(finance).ToContainTextAsync("70049");
    await Assertions.Expect(finance.GetByRole(AriaRole.Heading, new() { Name = "Billing and payment receipts", Exact = true })).ToBeFocusedAsync();
    await Assertions.Expect(finance.GetByRole(AriaRole.Button, new() { Name = "Older agreements", Exact = true })).ToBeDisabledAsync();
    await finance.GetByRole(AriaRole.Button, new() { Name = "Newer agreements", Exact = true }).PressAsync("Enter");
    await Assertions.Expect(finance.Locator(".agreement-card")).ToHaveCountAsync(50);
    await Assertions.Expect(finance.GetByRole(AriaRole.Table, new() { Name = "Fee invoices", Exact = true }))
      .ToContainTextAsync("INV-ASPM-COM06-ADV");
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    Assert.Empty(errors);

    await OwnedBrowserFixtureHandoff.WaitAsync(origin, "/ui/portal", "client-portal-finance");
  }

  [Fact]
  [Trait("CaseId", "ANGULAR-CLIENT-PORTAL-RECEIPT-SCOPE")]
  public async Task ClientPortal_ReceiptDownloadUsesDualLinkedAgreementScope()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-CLIENT-PORTAL-RECEIPT-SCOPE");
    var f = host.Fixture;
    var now = DateTimeOffset.UtcNow;
    var ownEngagementId = f.EngagementId;
    var siblingEngagementId = Guid.NewGuid();
    var billingAccountId = Guid.NewGuid();
    Guid ownDocumentId;
    Guid siblingDocumentId;
    var malformedDocumentId = Guid.NewGuid();
    byte[] expectedBytes;
    string expectedSha256;

    await using (var db = host.CreateDbContext())
    {
      // These are persisted projection fixtures only. They do not assert commercial acceptance,
      // invoice approval, a real transfer, or professional work. The client identity retains only
      // its PbcSeed grant for ownEngagementId; the sibling engagement belongs to the same client.
      var client = await db.PracticeClients.SingleAsync(x => x.Id == f.ClientId && x.FirmId == f.FirmId);
      var clientName = client.LegalName;
      client.BillingAccountId = billingAccountId;
      db.Engagements.Add(new Engagement
      {
        Id = siblingEngagementId, FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = now
      });
      db.BillingAccounts.Add(new BillingAccount
      {
        Id = billingAccountId, FirmId = f.FirmId, PracticeClientId = f.ClientId, Currency = "QAR", CreatedAt = now
      });

      var profile = new FirmCommercialProfile
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, Version = 1, LegalName = "Synthetic receipt fixture firm",
        Address = "Synthetic test address", ContactEmail = "receipt-fixture@example.test", ContactPhone = "",
        AccentColorHex = "#0F766E", ClosingText = "Synthetic fixture artifact; no real payment or approval is represented.",
        CreatedByUserId = f.Staff.Id, CreatedAt = now
      };

      (Guid ProposalId, Guid QuotationId, Guid AgreementId, Guid MilestoneId, Guid DocumentId, Guid InvoiceId, Guid EngagementId) AddPaidScenario(
        string suffix, Guid engagementId)
      {
        var leadId = Guid.NewGuid();
        var opportunityId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var quotationId = Guid.NewGuid();
        var agreementId = Guid.NewGuid();
        var milestoneId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var amount = 1000m;

        db.Leads.Add(new Lead
        {
          Id = leadId, FirmId = f.FirmId, Name = $"Synthetic receipt lead {suffix}", Source = "TEST", CreatedAt = now
        });
        db.Opportunities.Add(new Opportunity
        {
          Id = opportunityId, FirmId = f.FirmId, LeadId = leadId, PracticeClientId = f.ClientId,
          ServiceRoute = "FinancialStatementAudit", EntityScope = $"RECEIPT-SCOPE-{suffix}",
          PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", ExpectedFee = 2000m, Currency = "QAR", CreatedAt = now
        });
        db.Proposals.Add(new Proposal
        {
          Id = proposalId, FirmId = f.FirmId, OpportunityId = opportunityId, PracticeClientId = f.ClientId,
          ServiceProfileId = "SYNTHETIC-RECEIPT-SCOPE", Scope = "Synthetic portal projection fixture", Exclusions = "",
          Deliverables = "Synthetic fixture only", Dependencies = "", Fee = 2000m, Currency = "QAR",
          PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", PreparedByUserId = f.Staff.Id, CreatedAt = now
        });
        db.QuotationVersions.Add(new QuotationVersion
        {
          Id = quotationId, FirmId = f.FirmId, ProposalId = proposalId, Revision = 1, Currency = "QAR",
          BaseAmount = 2000m, Fee = 2000m, InputHash = new string('a', 64),
          Status = QuotationStates.Draft, CreatedByUserId = f.Staff.Id, CreatedAt = now
        });
        db.EngagementFeeAgreements.Add(new EngagementFeeAgreement
        {
          Id = agreementId, FirmId = f.FirmId, ProposalId = proposalId, QuotationVersionId = quotationId,
          PracticeClientId = f.ClientId, EngagementId = engagementId, Currency = "QAR", AgreedFee = 2000m,
          AdvancePercent = 50m, CreatedByUserId = f.Staff.Id, CreatedAt = now
        });
        db.Invoices.Add(new Invoice
        {
          Id = invoiceId, FirmId = f.FirmId, BillingAccountId = billingAccountId,
          InvoiceNumber = $"INV-PORTAL-RECEIPT-{suffix}", Currency = "QAR", Subtotal = amount, Tax = 0m,
          Total = amount, Revision = 1, Status = BillingStates.InvoiceSent, PostedAt = now.AddDays(-2),
          SentAt = now.AddDays(-1), CreatedByUserId = f.Staff.Id, CreatedAt = now.AddDays(-2)
        });
        db.Receipts.Add(new Receipt
        {
          Id = receiptId, FirmId = f.FirmId, BillingAccountId = billingAccountId, Amount = amount, Currency = "QAR",
          Reference = $"PORTAL-RECEIPT-{suffix}", Status = BillingStates.ReceiptRecorded,
          RecordedByUserId = f.Staff.Id, ReceivedAt = now.AddDays(-1)
        });
        db.ReceiptAllocations.Add(new ReceiptAllocation
        {
          Id = Guid.NewGuid(), FirmId = f.FirmId, ReceiptId = receiptId, InvoiceId = invoiceId,
          Amount = amount, CreatedAt = now.AddDays(-1)
        });
        db.FeeMilestones.Add(new FeeMilestone
        {
          Id = milestoneId, FirmId = f.FirmId, AgreementId = agreementId, Kind = FeeMilestoneKinds.Advance,
          Amount = amount, State = FeeMilestoneStates.Paid, InvoiceId = invoiceId, ReceiptId = receiptId,
          PaidAt = now.AddDays(-1), CreatedAt = now.AddDays(-2)
        });

        var bytes = CommercialDocumentRenderer.RenderDocx(new CommercialDocumentModel(profile,
          "Synthetic portal receipt fixture", $"SYNTHETIC-{suffix}", DateOnly.FromDateTime(now.UtcDateTime), clientName,
          [new DocumentSection("Scope fixture", ["Synthetic document used to verify client download scope."], null)], false));
        db.CommercialDocuments.Add(new CommercialDocument
        {
          Id = documentId, FirmId = f.FirmId, ProposalId = proposalId, QuotationVersionId = quotationId,
          FeeMilestoneId = milestoneId, Kind = CommercialDocumentKinds.PaymentReceipt,
          TemplateVersion = CommercialDocumentRenderer.SettlementReceiptTemplate, ProfileVersion = profile.Version,
          FileName = $"Receipt-{suffix}.docx", ContentType = CommercialDocumentRenderer.DocxContentType,
          Bytes = bytes, Sha256Hex = Hashing.Sha256Hex(bytes), CreatedByUserId = f.Staff.Id, CreatedAt = now
        });
        return (proposalId, quotationId, agreementId, milestoneId, documentId, invoiceId, engagementId);
      }

      var own = AddPaidScenario("OWN-A", ownEngagementId);
      var sibling = AddPaidScenario("SIBLING-B", siblingEngagementId);
      ownDocumentId = own.DocumentId;
      siblingDocumentId = sibling.DocumentId;

      // The receipt links to a real second milestone on the authorized agreement but carries a
      // different proposal identity. It must fail the document-to-agreement lineage check.
      var malformedMilestoneId = Guid.NewGuid();
      db.FeeMilestones.Add(new FeeMilestone
      {
        Id = malformedMilestoneId, FirmId = f.FirmId, AgreementId = own.AgreementId,
        Kind = FeeMilestoneKinds.Balance, Amount = 1000m, State = FeeMilestoneStates.Planned, CreatedAt = now
      });
      var malformedBytes = CommercialDocumentRenderer.RenderDocx(new CommercialDocumentModel(profile,
        "Synthetic mismatched receipt fixture", "SYNTHETIC-MISMATCH", DateOnly.FromDateTime(now.UtcDateTime), clientName,
        [new DocumentSection("Scope fixture", ["Synthetic mismatched lineage fixture."], null)], false));
      db.CommercialDocuments.Add(new CommercialDocument
      {
        Id = malformedDocumentId, FirmId = f.FirmId, ProposalId = sibling.ProposalId, QuotationVersionId = own.QuotationId,
        FeeMilestoneId = malformedMilestoneId, Kind = CommercialDocumentKinds.PaymentReceipt,
        TemplateVersion = CommercialDocumentRenderer.SettlementReceiptTemplate, ProfileVersion = profile.Version,
        FileName = "Receipt-mismatched-lineage.docx", ContentType = CommercialDocumentRenderer.DocxContentType,
        Bytes = malformedBytes, Sha256Hex = Hashing.Sha256Hex(malformedBytes), CreatedByUserId = f.Staff.Id, CreatedAt = now
      });

      await db.SaveChangesAsync();
      var storedOwnDocument = await db.CommercialDocuments.AsNoTracking().SingleAsync(x => x.Id == ownDocumentId);
      expectedBytes = storedOwnDocument.Bytes;
      expectedSha256 = storedOwnDocument.Sha256Hex;
      Assert.Equal(Hashing.Sha256Hex(expectedBytes), expectedSha256);
      foreach (var scenario in new[] { own, sibling })
      {
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == scenario.InvoiceId);
        var allocated = await db.ReceiptAllocations.AsNoTracking()
          .Where(x => x.FirmId == f.FirmId && x.InvoiceId == scenario.InvoiceId).SumAsync(x => x.Amount);
        Assert.Equal(invoice.Total, allocated);
      }
      foreach (var scenario in new[] { own, sibling })
      {
        var agreement = await db.EngagementFeeAgreements.AsNoTracking().SingleAsync(x => x.Id == scenario.AgreementId);
        var document = await db.CommercialDocuments.AsNoTracking().SingleAsync(x => x.Id == scenario.DocumentId);
        Assert.Equal<Guid?>(scenario.EngagementId, agreement.EngagementId);
        Assert.Equal(scenario.ProposalId, agreement.ProposalId);
        Assert.Equal<Guid?>(scenario.ProposalId, document.ProposalId);
        Assert.Equal<Guid?>(scenario.QuotationId, document.QuotationVersionId);
        Assert.Equal<Guid?>(scenario.MilestoneId, document.FeeMilestoneId);
      }
    }

    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_UI_BUILD_PATH") is { Length: > 0 } buildPath)
      settings["AngularUi__BuildPath"] = buildPath;
    var origin = await host.StartApiForIdentityAsync(f.Client, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 390, Height = 844 } });
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/ui/portal"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Client portal", Exact = true })).ToBeVisibleAsync();

    var finance = page.Locator("audit-portal-finance");
    var receiptTable = finance.GetByRole(AriaRole.Table, new() { Name = "Payment receipts", Exact = true });
    await Assertions.Expect(receiptTable).ToBeVisibleAsync();
    var receiptRow = receiptTable.Locator("tbody tr");
    await Assertions.Expect(receiptRow).ToHaveCountAsync(1);
    await Assertions.Expect(receiptRow).ToContainTextAsync("PORTAL-RECEIPT-OWN-A");
    Assert.DoesNotContain("PORTAL-RECEIPT-SIBLING-B", await finance.InnerTextAsync());
    var receiptDownload = receiptRow.GetByRole(AriaRole.Link, new() { Name = "Download receipt", Exact = true });
    await Assertions.Expect(receiptDownload).ToBeVisibleAsync();
    Assert.Equal($"/api/commercial/documents/{ownDocumentId:D}/download", await receiptDownload.GetAttributeAsync("href"));

    await receiptDownload.FocusAsync();
    await Assertions.Expect(receiptDownload).ToBeFocusedAsync();
    var download = await page.RunAndWaitForDownloadAsync(() => receiptDownload.PressAsync("Enter"));
    var downloadPath = await download.PathAsync();
    Assert.NotNull(downloadPath);
    var downloadedBytes = await File.ReadAllBytesAsync(downloadPath!);
    Assert.Equal(expectedBytes, downloadedBytes);
    Assert.Equal(expectedSha256, Hashing.Sha256Hex(downloadedBytes));

    await using (var siblingResponse = await context.APIRequest.GetAsync(
      origin + $"/api/commercial/documents/{siblingDocumentId:D}/download", new() { MaxRedirects = 0 }))
      Assert.Equal(403, siblingResponse.Status);
    await using (var malformedResponse = await context.APIRequest.GetAsync(
      origin + $"/api/commercial/documents/{malformedDocumentId:D}/download", new() { MaxRedirects = 0 }))
      Assert.Equal(403, malformedResponse.Status);

    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
      "The client portal must not overflow horizontally at 390px.");
    Assert.Empty(errors);
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, "/ui/portal", "client-receipt-download");
  }

  [Fact]
  [Trait("CaseId", "ANGULAR-CLIENT-PORTAL-E2E")]
  public async Task ClientPortal_FirstSignIn_Delegation_Conversation_Upload_AndRevocation()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-CLIENT-PORTAL-E2E");
    var f = host.Fixture; Guid requestId; Guid colleagueId;
    var primary = PbcSeed.User(f.FirmId, "Client"); primary.DisplayName = "Management";
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(primary); db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, primary, "ClientUser", f.ClientId, f.EngagementId));
      var colleague = PbcSeed.User(f.FirmId, "Client"); colleague.DisplayName = "Finance Colleague"; colleagueId = colleague.Id;
      db.Users.Add(colleague); db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, colleague, "ClientUser", f.ClientId, f.EngagementId));
      db.ClientContacts.Add(new ClientContact { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        FullName = "Management", Email = primary.Email, Role = "Primary contact", Primary = true });
      await db.SaveChangesAsync();
      var staff = PbcSeed.Actor(f.Staff, "Staff");
      var created = await PbcService.CreateRequestAsync(db, staff, new(f.EngagementId, "Bank statements", "TEST", "2026-01-01", "2026-12-31", "Angular bank evidence", "PDF", "Totals",
        primary.Id, f.Staff.Id, f.Reviewer.Id, "2027-01-31", "Confidential", "Complete readable statements"));
      Assert.True(created.Succeeded, created.Message); requestId = created.Value;
      Assert.True((await PbcService.ChangeStateAsync(db, staff, new(requestId, PbcStates.Sent, 1))).Succeeded);
    }
    var origin = await host.StartApiForIdentityAsync(primary, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 390, Height = 844 } });
    var page = await context.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fportal");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Client portal", Exact = true })).ToBeVisibleAsync();
    Assert.DoesNotContain("Portfolio", await page.Locator("aside").InnerTextAsync());
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I will keep my sign-in private", Exact = false }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Complete first sign-in", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("First sign-in complete. Uploads are open for your requests.")).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Link, new() { Name = "Angular bank evidence", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Delegate this request", Exact = true })).ToBeVisibleAsync();
    await page.GetByLabel("Colleague", new() { Exact = true }).SelectOptionAsync(colleagueId.ToString());
    await page.GetByRole(AriaRole.Button, new() { Name = "Delegate", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Delegation recorded.", new() { Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Revoke delegation for Finance Colleague", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Delegation revoked.", new() { Exact = true })).ToBeVisibleAsync();
    await page.GetByLabel("Reply to the audit team", new() { Exact = true }).FillAsync("The requested statements are attached.");
    await page.GetByRole(AriaRole.Button, new() { Name = "Send reply", Exact = true }).ClickAsync();
    await Assertions.Expect(page.Locator(".conversation")).ToContainTextAsync("The requested statements are attached.");
    var bytes = System.Text.Encoding.UTF8.GetBytes("Synthetic bank evidence from Angular portal.");
    await page.Locator(".drop-zone").EvaluateAsync(@"zone => {
      const transfer = new DataTransfer();
      transfer.items.add(new File([new TextEncoder().encode('Synthetic bank evidence from Angular portal.')],
        'angular-client-evidence.txt', { type: 'text/plain' }));
      zone.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: transfer }));
    }");
    await Assertions.Expect(page.GetByText($"angular-client-evidence.txt · {bytes.Length} bytes", new() { Exact = true }))
      .ToBeVisibleAsync();
    await Assertions.Expect(page.Locator(".fingerprint").First)
      .ToContainTextAsync(AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(bytes));
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Upload file", Exact = true })).ToBeEnabledAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Upload file", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("File bytes staged. Your audit team must verify trusted completion and suitability before the request is received or accepted.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator(".receipt")).ToContainTextAsync("angular-client-evidence.txt");
    DateTimeOffset expectedUploadAt;
    DateTimeOffset expectedReplyAt;
    await using (var db = host.CreateDbContext())
    {
      var upload = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.PbcRequestId == requestId);
      expectedUploadAt = upload.CreatedAt;
      expectedReplyAt = await db.PbcCommunications.Where(x => x.PbcRequestId == requestId &&
        x.Body == "The requested statements are attached.").Select(x => x.CreatedAt).SingleAsync();
      Assert.Equal(PbcUploadStates.Chunking, upload.State); Assert.Equal(bytes.Length, upload.ReceivedByteCount);
      Assert.Equal(AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(bytes), upload.DeclaredSha256Hex);
      var chunks = await db.PbcUploadChunks.AsNoTracking().Where(x => x.PbcUploadIntentId == upload.Id).ToListAsync();
      Assert.Single(chunks); Assert.Equal(bytes.Length, chunks[0].ByteCount);
      Assert.False(await db.PbcRequestDelegations.AnyAsync(x => x.PbcRequestId == requestId && x.RevokedAt == null));
      // Public-history projection fixtures only; these rows do not claim email or provider delivery.
      var firstMessageAt = await db.PbcCommunications.Where(x => x.PbcRequestId == requestId).MinAsync(x => x.CreatedAt);
      db.PbcCommunications.AddRange(Enumerable.Range(0, 31).Select(index => new PbcCommunication
      {
        Id = Guid.CreateVersion7(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        PbcRequestId = requestId, AuthorUserId = f.Staff.Id, Kind = PbcCommunicationKinds.StaffMessage,
        Body = $"Paged synthetic message {index:D2}", CreatedAt = firstMessageAt.AddMinutes(-40 + index)
      }));
      await db.SaveChangesAsync();
    }
    var uploadTime = page.Locator(".receipt time");
    Assert.Equal(expectedUploadAt, DateTimeOffset.Parse((await uploadTime.GetAttributeAsync("datetime"))!));
    await Assertions.Expect(uploadTime).ToHaveTextAsync(expectedUploadAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture));
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh request", Exact = true }).ClickAsync();
    await Assertions.Expect(page.Locator(".conversation li")).ToHaveCountAsync(25);
    await Assertions.Expect(page.Locator(".conversation")).ToContainTextAsync("Paged synthetic message 00");
    var nextMessages = page.GetByRole(AriaRole.Button, new() { Name = "Next messages", Exact = true });
    await nextMessages.FocusAsync();
    await page.Keyboard.PressAsync("Space");
    await Assertions.Expect(page.Locator(".conversation")).ToContainTextAsync("Paged synthetic message 25");
    var retainedReply = page.Locator(".conversation li").Filter(new() { HasText = "The requested statements are attached." });
    await Assertions.Expect(retainedReply).ToBeVisibleAsync();
    Assert.Equal(expectedReplyAt, DateTimeOffset.Parse((await retainedReply.Locator("time").GetAttributeAsync("datetime"))!));
    await Assertions.Expect(retainedReply.Locator("time")).ToHaveTextAsync(expectedReplyAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture));
    Guid closedDelegationId;
    await using (var db = host.CreateDbContext())
    {
      var delegated = await ClientPortalService.DelegateRequestAsync(db, PbcSeed.Actor(primary, "ClientUser"), requestId, colleagueId);
      Assert.True(delegated.Succeeded, delegated.Message);
      closedDelegationId = delegated.Value;
      // Terminal read projection only: this is not evidence that professional review or acceptance occurred.
      await db.PbcRequests.Where(x => x.Id == requestId).ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.State, PbcStates.Closed).SetProperty(x => x.Revision, x => x.Revision + 1));
    }
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh request", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Request delegations", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByLabel("Colleague", new() { Exact = true })).ToHaveCountAsync(0);
    var revokeClosed = page.GetByRole(AriaRole.Button, new() { Name = "Revoke delegation for Finance Colleague", Exact = true });
    await revokeClosed.FocusAsync();
    await page.Keyboard.PressAsync("Space");
    await Assertions.Expect(page.GetByText("Delegation revoked.", new() { Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
      Assert.NotNull((await db.PbcRequestDelegations.AsNoTracking().SingleAsync(x => x.Id == closedDelegationId)).RevokedAt);
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, "/ui/portal/requests/" + requestId, "pbc-request-history");
    await page.GotoAsync(origin + "/ui/portal/requests/" + Guid.NewGuid());
    await Assertions.Expect(page.GetByText("This request is not available in your current access scope.", new() { Exact = true })).ToBeVisibleAsync();
    await page.GotoAsync(origin + "/ui/portal/requests/" + requestId);
    await Assertions.Expect(page.GetByText("Complete readable statements", new() { Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
      await db.Users.Where(x => x.Id == primary.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.Disabled, true).SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
    Assert.DoesNotContain("Complete readable statements", await page.Locator("body").InnerTextAsync());
    Assert.Empty(errors);
  }
}
