using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "MigrationParity")]
public sealed class AngularPracticeBillingLedgerParityTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-PRACTICE-BILLING-LEDGER-01")]
  public async Task AngularTimeApprovalBillingAndFirmCloseKeepTheirBoundaries()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-PRACTICE-BILLING-LEDGER-01");
    var f = host.Fixture;
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    var manager = PbcSeed.Actor(f.Staff, "FinanceManager");
    var reviewer = PbcSeed.Actor(f.Reviewer, "FinanceReviewer");
    Guid taskId, accountId, invoiceId, receiptId, periodId, timeEntryId;
    var accountIds = new Dictionary<string, Guid>();

    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Staff, "Staff", f.ClientId),
        PbcSeed.Grant(f.FirmId, f.Staff, "Manager"),
        PbcSeed.Grant(f.FirmId, f.Staff, "FinanceManager", f.ClientId),
        PbcSeed.Grant(f.FirmId, f.Staff, "FinanceManager"),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner", f.ClientId),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner"),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "FinanceReviewer"));
      await db.SaveChangesAsync();

      var task = await PracticeTimeService.CreateTaskAsync(db, PbcSeed.Actor(f.Staff, "Manager"),
        new CreateTaskRequest("Synthetic approved time", f.ClientId));
      Assert.True(task.Succeeded, task.Message);
      taskId = task.Value;
      var rate = await PracticeTimeService.ReviseRateCardAsync(db, PbcSeed.Actor(f.Staff, "Manager"),
        new RateCardDraftRequest("Staff", "Fieldwork", "QAR", 100m));
      Assert.True(rate.Succeeded, rate.Message);
      Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, PbcSeed.Actor(f.Reviewer, "Partner"),
        rate.Value)).Succeeded);

      var account = await BillingService.CreateBillingAccountAsync(db, manager,
        new CreateBillingAccountRequest(f.ClientId, "QAR"));
      Assert.True(account.Succeeded, account.Message);
      accountId = account.Value;

      var period = await LedgerService.CreateFirmPeriodAsync(db, manager,
        new CreateFirmPeriodRequest("2026-09"));
      Assert.True(period.Succeeded, period.Message);
      periodId = period.Value;
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, FunctionalCurrency = "QAR",
        ProfileKind = BillingStates.TestProfile, Approved = true, ApprovedByUserId = f.Reviewer.Id,
        ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    };
    var staffOrigin = await host.StartApiForIdentityAsync(f.Staff, settings);
    var reviewerOrigin = await host.StartApiForIdentityAsync(f.Reviewer, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var errors = new List<string>();

    // Time is entered and submitted in the native Angular work queue by staff.
    var staffPage = await browser.NewPageAsync();
    staffPage.PageError += (_, error) => errors.Add($"staff: {error}");
    await staffPage.GotoAsync(staffOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString("/app/practice/time"));
    await staffPage.GetByRole(AriaRole.Heading,
      new() { Name = "Practice time & task records", Exact = true }).WaitForAsync();
    await staffPage.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 10000 });
    if (await staffPage.Locator("select[name='task']").CountAsync() == 0)
    {
      var response = await staffPage.Context.APIRequest.GetAsync(staffOrigin + "/api/ui/practice/time");
      throw new Xunit.Sdk.XunitException(
        $"The time workspace did not expose the seeded open task. HTTP {(int)response.Status}: {await response.TextAsync()}\n" +
        await staffPage.Locator("main").InnerTextAsync());
    }
    await staffPage.Locator("select[name='task']").SelectOptionAsync(taskId.ToString("D"));
    await staffPage.Locator("input[name='date']").FillAsync("2026-09-10");
    await staffPage.Locator("input[name='minutes']").FillAsync("60");
    await staffPage.Locator("input[name='role']").FillAsync("Staff");
    await staffPage.Locator("input[name='activity']").FillAsync("Fieldwork");
    await staffPage.GetByRole(AriaRole.Button, new() { Name = "Save time draft", Exact = true }).ClickAsync();
    await Assertions.Expect(staffPage.Locator("audit-command-message"))
      .ToContainTextAsync("Time draft recorded", new() { Timeout = 30000 });
    var staffRow = staffPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "Synthetic approved time" });
    await staffRow.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync();
    await Assertions.Expect(staffPage.Locator("audit-command-message"))
      .ToContainTextAsync("submitted for approval");

    await using (var db = host.CreateDbContext())
    {
      timeEntryId = await db.TimeEntries.AsNoTracking().Where(x => x.TaskId == taskId)
        .Select(x => x.Id).SingleAsync();
      Assert.Equal(PracticeTimeStates.TimeSubmitted,
        await db.TimeEntries.Where(x => x.Id == timeEntryId).Select(x => x.Status).SingleAsync());
    }

    // A different Partner approves the entry from the Angular review queue.
    var reviewerPage = await browser.NewPageAsync();
    reviewerPage.PageError += (_, error) => errors.Add($"reviewer: {error}");
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString("/app/practice/time"));
    await reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Practice time & task records", Exact = true }).WaitForAsync();
    var reviewRow = reviewerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "Synthetic approved time" });
    await reviewRow.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.Locator("audit-command-message"))
      .ToContainTextAsync("Time entry was approved.");
    await using (var db = host.CreateDbContext())
    {
      var entry = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == timeEntryId);
      Assert.Equal(PracticeTimeStates.TimeApproved, entry.Status);
      Assert.Equal(f.Reviewer.Id, entry.ApprovedByUserId);

      var source = new InvoiceLineRequest("Approved time", 1m, 100m, "TIME", timeEntryId, 1);
      var draft = await BillingService.CreateInvoiceDraftAsync(db, manager,
        new CreateInvoiceDraftRequest(accountId, "SYN-PAR-002-TIME-INV-001", [source]));
      Assert.True(draft.Succeeded, draft.Message);
      invoiceId = draft.Value;
      var duplicate = await BillingService.CreateInvoiceDraftAsync(db, manager,
        new CreateInvoiceDraftRequest(accountId, "SYN-PAR-002-TIME-INV-002", [source]));
      Assert.False(duplicate.Succeeded);
      Assert.Equal("billing.source-duplicate", duplicate.ErrorCode);

      Assert.True((await BillingService.SubmitInvoiceAsync(db, manager, invoiceId)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, reviewer, invoiceId)).Succeeded);
      Assert.True((await BillingService.PostInvoiceAsync(db, manager, invoiceId)).Succeeded);
      Assert.True((await BillingService.SendInvoiceAsync(db, manager, invoiceId)).Succeeded);
      Assert.True((await BillingService.IssueCreditNoteAsync(db, manager,
        new IssueCreditNoteRequest(invoiceId, "SYN-PAR-002-CN-001", 20m, "Synthetic adjustment"))).Succeeded);
      receiptId = (await BillingService.RecordReceiptAsync(db, manager,
        new RecordReceiptRequest(accountId, 80m, "SYN-PAR-002-RECEIPT-001"))).Value;
      Assert.True((await BillingService.AllocateReceiptAsync(db, manager,
        new AllocateReceiptRequest(receiptId, invoiceId, 80m))).Succeeded);
      var balance = await BillingService.GetInvoiceBalanceAsync(db, manager, invoiceId);
      Assert.True(balance.Succeeded, balance.Message);
      Assert.Equal(new InvoiceBalance(invoiceId, "QAR", 100m, 20m, 80m, 0m), balance.Value);
      Assert.Empty(await db.FirmJournals.AsNoTracking().Where(x => x.SourceKind == "BILLING" &&
        x.SourceKey == invoiceId.ToString("D")).ToListAsync());

      foreach (var (code, name, type, side) in new[]
      {
        ("SYN-PAR-CASH", "Synthetic cash", LedgerStates.AccountAsset, LedgerStates.Debit),
        ("SYN-PAR-AR", "Synthetic receivable", LedgerStates.AccountAsset, LedgerStates.Debit),
        ("SYN-PAR-REV", "Synthetic service revenue", LedgerStates.AccountRevenue, LedgerStates.Credit)
      })
      {
        var created = await LedgerService.CreateFirmAccountAsync(db, manager,
          new CreateFirmAccountRequest(code, name, type, side));
        Assert.True(created.Succeeded, created.Message);
        accountIds.Add(code, created.Value);
      }

      async Task PostOnceAsync(string number, string sourceKey,
        Guid debit, Guid credit, decimal amount)
      {
        var journal = await LedgerService.CreateFirmJournalDraftAsync(db, manager,
          new CreateFirmJournalDraftRequest(periodId, number, "MANUAL", sourceKey, 1,
            "MANUAL", "QAR",
            [new(debit, "Synthetic debit", amount, 0m), new(credit, "Synthetic credit", 0m, amount)]));
        Assert.True(journal.Succeeded, journal.Message);
        Assert.True((await LedgerService.SubmitFirmJournalAsync(db, manager, journal.Value)).Succeeded);
        Assert.True((await LedgerService.ApproveFirmJournalAsync(db, reviewer, journal.Value)).Succeeded);
        Assert.True((await LedgerService.PostFirmJournalAsync(db, manager, journal.Value)).Succeeded);
        Assert.True((await LedgerService.PostFirmJournalAsync(db, manager, journal.Value)).Succeeded);
        Assert.Single(await db.FirmPostings.Where(x => x.JournalId == journal.Value).ToListAsync());
      }

      await PostOnceAsync("SYN-PAR-JRN-001", $"INVOICE:{invoiceId:D}",
        accountIds["SYN-PAR-AR"], accountIds["SYN-PAR-REV"], 100m);
      await PostOnceAsync("SYN-PAR-JRN-002", $"CREDIT:{invoiceId:D}",
        accountIds["SYN-PAR-REV"], accountIds["SYN-PAR-AR"], 20m);
      await PostOnceAsync("SYN-PAR-JRN-003", $"RECEIPT:{receiptId:D}",
        accountIds["SYN-PAR-CASH"], accountIds["SYN-PAR-AR"], 80m);
    }

    // Fiscal period close stays a reviewed action in the native firm-ledger UI.
    await reviewerPage.GotoAsync(reviewerOrigin + "/app/finance");
    await reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Firm ledger & financial operations", Exact = true }).WaitForAsync();
    var periodRow = reviewerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "2026-09" });
    await periodRow.GetByRole(AriaRole.Button, new() { Name = "Close period", Exact = true }).ClickAsync();
    await reviewerPage.GetByLabel("Close reason", new() { Exact = true }).FillAsync("Synthetic reviewed close");
    await reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Confirm close", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.Locator("audit-command-message"))
      .ToContainTextAsync("Fiscal period successfully closed.");
    await using (var db = host.CreateDbContext())
    {
      Assert.Equal(LedgerStates.PeriodClosed,
        await db.FirmPeriods.Where(x => x.Id == periodId).Select(x => x.Status).SingleAsync());
      Assert.Equal("Synthetic reviewed close", await db.PeriodCloseDecisions
        .Where(x => x.PeriodId == periodId).Select(x => x.Reason).SingleAsync());
      Assert.Equal(3, await db.FirmPostings.CountAsync(x => x.PeriodId == periodId));
    }
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-INVOICE-REVOKED-01")]
  public async Task OpenInvoiceContentClearsWhenItsGrantIsRevoked()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-INVOICE-REVOKED-01");
    var f = host.Fixture;
    var invoiceId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "FinanceManager", f.ClientId));
      var account = new BillingAccount
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
      };
      db.BillingAccounts.Add(account);
      db.Invoices.Add(new Invoice
      {
        Id = invoiceId, FirmId = f.FirmId, BillingAccountId = account.Id,
        InvoiceNumber = "SYN-PAR-002-REVOKED-001", Currency = "QAR",
        Subtotal = 750m, Tax = 0m, Total = 750m, Status = BillingStates.InvoiceSent,
        CreatedByUserId = f.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.InvoiceLines.Add(new InvoiceLine
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, InvoiceId = invoiceId,
        Description = "Synthetic revoked invoice detail", Quantity = 1m,
        UnitPrice = 750m, LineTotal = 750m
      });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(f.Staff,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString($"/app/practice/invoices/{invoiceId:D}"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "SYN-PAR-002-REVOKED-001", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("Synthetic revoked invoice detail", new() { Exact = true })).ToBeVisibleAsync();

    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.FirmId == f.FirmId && x.UserId == f.Staff.Id &&
        x.RevokedAt == null && x.Role == "FinanceManager" && x.ClientId == f.ClientId);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(f.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
      Assert.True(revoked.Succeeded, revoked.Message);
    }

    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("SYN-PAR-002-REVOKED-001", body);
    Assert.DoesNotContain("Synthetic revoked invoice detail", body);
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-009-PROPOSAL-READ-01")]
  public async Task ProposalDetailRequiresFirmWideGrantAndClearsStaleOrRevokedContent()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-009-PROPOSAL-READ-01");
    var f = host.Fixture;
    var admin = PbcSeed.Actor(f.Admin, "Administrator");
    const string leadName = "SYN-PAR-009-REAL-LEAD";
    const string latestScope = "SYN-PAR-009-REVISION-TWO-SCOPE";
    const decimal fee = 1234.56m;
    Guid proposalId, firstProposalId;
    var restricted = PbcSeed.User(f.FirmId, "Staff");

    await using (var db = host.CreateDbContext())
    {
      var lead = await PracticeCrmService.CreateLeadAsync(db, admin,
        new CreateLeadRequest(leadName, "Synthetic test", OwnerUserId: admin.UserId));
      Assert.True(lead.Succeeded, lead.Message);
      Assert.True((await PracticeCrmService.QualifyLeadAsync(db, admin, lead.Value)).Succeeded);
      var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, admin,
        new CreateOpportunityRequest(lead.Value, "SYNTHETIC-AUDIT", "SYN-PAR-009-ENTITY",
          "2026-01-01", "2026-12-31", fee, "QAR", OwnerUserId: admin.UserId));
      Assert.True(opportunity.Succeeded, opportunity.Message);
      var first = await PracticeCrmService.ReviseProposalAsync(db, admin,
        new ReviseProposalRequest(opportunity.Value, "SYN-PAR-009-PROFILE-V1", "Revision one scope",
          "Revision one exclusions", "Revision one deliverables", "Revision one dependencies",
          1000m, "QAR", "2026-01-01", "2026-12-31"));
      Assert.True(first.Succeeded, first.Message);
      firstProposalId = first.Value;
      var latest = await PracticeCrmService.ReviseProposalAsync(db, admin,
        new ReviseProposalRequest(opportunity.Value, "SYN-PAR-009-PROFILE-V2", latestScope,
          "SYN-PAR-009-EXCLUSIONS", "SYN-PAR-009-DELIVERABLES", "SYN-PAR-009-DEPENDENCIES",
          fee, "QAR", "2026-01-01", "2026-12-31", ExpectedRevision: 1));
      Assert.True(latest.Succeeded, latest.Message);
      proposalId = latest.Value;

      db.Users.Add(restricted);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Staff, "RelationshipManager"),
        PbcSeed.Grant(f.FirmId, restricted, "RelationshipManager", f.ClientId));
      await db.SaveChangesAsync();
    }

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    };
    var origin = await host.StartApiForIdentityAsync(f.Staff, settings);
    var restrictedOrigin = await host.StartApiForIdentityAsync(restricted, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add($"firm-wide: {error}");
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString($"/app/practice/proposals/{proposalId:D}"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = leadName, Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText(latestScope, new() { Exact = true })).ToBeVisibleAsync();
    var quotationPlaceholder = page.GetByText("Quotation pricing and approvals", new() { Exact = true });
    if (await quotationPlaceholder.CountAsync() > 0) await quotationPlaceholder.ScrollIntoViewIfNeededAsync();
    await Assertions.Expect(page.Locator("audit-quotation").GetByRole(AriaRole.Heading,
      new() { Name = "Calculated quotation", Exact = true })).ToBeVisibleAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.Contains("DRAFT", body);
    var feeText = await page.Locator("audit-proposal > dl > dd").Nth(5).InnerTextAsync();
    Assert.True(feeText.Replace(",", "", StringComparison.Ordinal).Contains("1234.56", StringComparison.Ordinal),
      $"Proposal fee rendered as '{feeText}'.");
    Assert.Contains("QAR", feeText);
    Assert.Contains("SYN-PAR-009-DELIVERABLES", body);
    Assert.Contains("SUPERSEDED", body);
    Assert.Contains("Proposal author", body);
    Assert.Contains(f.Admin.DisplayName, body);
    Assert.Contains("Period", body);
    Assert.Contains("2026-01-01 to 2026-12-31", body);
    Assert.Contains("Commercial workflow", body);
    Assert.Contains("Sending queues exactly one durable email", body);
    Assert.Contains("Calculated quotation", body);
    Assert.DoesNotContain("Acme Holdings W.L.L.", body);
    Assert.DoesNotContain("Senior Manager A", body);

    var token = await page.EvaluateAsync<string>("window.__proposalParityToken = crypto.randomUUID()");
    var missingProposalId = Guid.NewGuid();
    await PushRouteAsync(page, $"/app/practice/proposals/{missingProposalId:D}");
    await Assertions.Expect(page.GetByRole(AriaRole.Alert))
      .ToContainTextAsync("Proposal unavailable. Current firm-wide commercial access is required.");
    var missingBody = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(leadName, missingBody);
    Assert.DoesNotContain(latestScope, missingBody);
    Assert.DoesNotContain(proposalId.ToString("D"), missingBody);
    Assert.Equal(token, await page.EvaluateAsync<string>("window.__proposalParityToken"));

    await PushRouteAsync(page, $"/app/practice/proposals/{proposalId:D}");
    await Assertions.Expect(page.GetByText(latestScope, new() { Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.FirmId == f.FirmId && x.UserId == f.Staff.Id &&
        x.Role == "RelationshipManager" && x.ClientId == null && x.RevokedAt == null);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db, admin,
        new RevokeRoleGrantRequest(grant.Id));
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
    var revokedBody = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(leadName, revokedBody);
    Assert.DoesNotContain(latestScope, revokedBody);
    Assert.DoesNotContain(proposalId.ToString("D"), revokedBody);

    var restrictedPage = await browser.NewPageAsync();
    restrictedPage.PageError += (_, error) => errors.Add($"scoped: {error}");
    await restrictedPage.GotoAsync(restrictedOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString($"/app/practice/proposals/{proposalId:D}"));
    await Assertions.Expect(restrictedPage.GetByRole(AriaRole.Alert))
      .ToContainTextAsync("Proposal unavailable. Current firm-wide commercial access is required.");
    var restrictedBody = await restrictedPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(leadName, restrictedBody);
    Assert.DoesNotContain(latestScope, restrictedBody);
    Assert.DoesNotContain(proposalId.ToString("D"), restrictedBody);
    Assert.DoesNotContain(firstProposalId.ToString("D"), restrictedBody);
    Assert.Empty(errors);
  }

  private static Task PushRouteAsync(IPage page, string path) =>
    page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }", path);
}
