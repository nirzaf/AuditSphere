using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularClientPortalJourneyTests
{
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
    Assert.Empty(errors);

    await OwnedBrowserFixtureHandoff.WaitAsync(origin, "/ui/portal", "client-portal-finance");
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
    await using (var db = host.CreateDbContext())
    {
      var upload = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.PbcRequestId == requestId);
      Assert.Equal(PbcUploadStates.Chunking, upload.State); Assert.Equal(bytes.Length, upload.ReceivedByteCount);
      Assert.Equal(AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(bytes), upload.DeclaredSha256Hex);
      var chunks = await db.PbcUploadChunks.AsNoTracking().Where(x => x.PbcUploadIntentId == upload.Id).ToListAsync();
      Assert.Single(chunks); Assert.Equal(bytes.Length, chunks[0].ByteCount);
      Assert.False(await db.PbcRequestDelegations.AnyAsync(x => x.PbcRequestId == requestId && x.RevokedAt == null));
    }
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
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
