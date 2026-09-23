using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "PracticeAndFirmLedger")]
public sealed class PracticeBillingLedgerJourneyTests
{
  [Fact]
  [Trait("CaseId", "PROP-E2E-11")]
  public async Task ProspectTimeBillingAndManualFirmCloseKeepTheirBoundaries()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "PROP-E2E-11");
    var admin = PbcSeed.Actor(host.Fixture.Admin, "Administrator");
    var staff = PbcSeed.Actor(host.Fixture.Staff, "Staff");
    var manager = PbcSeed.Actor(host.Fixture.Staff, "FinanceManager");
    var partner = PbcSeed.Actor(host.Fixture.Reviewer, "Partner");
    var financeReviewer = PbcSeed.Actor(host.Fixture.Reviewer, "FinanceReviewer");
    Guid clientId;
    Guid taskId;
    Guid timeId;
    Guid accountId;
    Guid invoiceId;
    Guid receiptId;
    Guid periodId;

    await using (var db = host.CreateDbContext())
    {
      var leadId = (await PracticeCrmService.CreateLeadAsync(db, admin,
        new CreateLeadRequest("Synthetic E2E prospect", "Test", "Synthetic owner", "owner@example.invalid"))).Value;
      Assert.True((await PracticeCrmService.QualifyLeadAsync(db, admin, leadId)).Succeeded);
      var opportunityId = (await PracticeCrmService.CreateOpportunityAsync(db, admin,
        new CreateOpportunityRequest(leadId, "AccountingOnly", "SYNTHETIC-ENTITY", "2026-01-01", "2026-12-31",
          100m, "QAR", 60, NextAction: "Synthetic E2E check"))).Value;
      var proposalId = (await PracticeCrmService.ReviseProposalAsync(db, admin,
        new ReviseProposalRequest(opportunityId, "SYNTHETIC-2026", "Synthetic services", "None",
          "Synthetic scope", "Synthetic source records", 100m, "QAR", "2026-01-01", "2026-12-31"))).Value;
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Reviewer, "Partner"));
      await db.SaveChangesAsync();
      var proposalReview = await PracticeCrmService.ApproveProposalAsync(db, partner, proposalId);
      Assert.True(proposalReview.Succeeded, proposalReview.Message);
      Assert.True((await PracticeCrmService.SendProposalAsync(db, admin, proposalId)).Succeeded);
      Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, admin, proposalId,
        new ProposalResponseRequest(CrmStates.ProposalAccepted))).Succeeded);
      clientId = (await PracticeCrmService.ConvertToClientDraftAsync(db, admin,
        new ConvertToClientDraftRequest(proposalId, "Synthetic E2E prospect", Jurisdiction: "QA"))).Value;

      db.RoleGrants.AddRange(
        PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Staff", clientId),
        PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "FinanceManager", clientId),
        PbcSeed.Grant(host.Fixture.Staff.FirmId, host.Fixture.Staff, "Manager"),
        PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "FinanceManager"),
        PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Reviewer, "Manager", clientId),
        PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Reviewer, "FinanceReviewer"));
      await db.SaveChangesAsync();

      var task = await PracticeTimeService.CreateTaskAsync(db, staff,
        new CreateTaskRequest("Synthetic approved time", clientId));
      Assert.True(task.Succeeded, task.Message);
      taskId = task.Value;
      var rate = await PracticeTimeService.ReviseRateCardAsync(db, PbcSeed.Actor(host.Fixture.Staff, "Manager"),
        new RateCardDraftRequest("Staff", "Fieldwork", "QAR", 100m));
      Assert.True(rate.Succeeded, rate.Message);
      Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, partner, rate.Value)).Succeeded);

      var billing = await BillingService.CreateBillingAccountAsync(db, manager,
        new CreateBillingAccountRequest(clientId, "QAR"));
      Assert.True(billing.Succeeded, billing.Message);
      accountId = billing.Value;

      var period = await LedgerService.CreateFirmPeriodAsync(db, manager,
        new CreateFirmPeriodRequest("2026-09"));
      Assert.True(period.Succeeded, period.Message);
      periodId = period.Value;
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, FunctionalCurrency = "QAR",
        ProfileKind = BillingStates.TestProfile, Approved = true, ApprovedByUserId = host.Fixture.Reviewer.Id,
        ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var staffContext = await browser.NewContextAsync();
    var staffPage = await staffContext.NewPageAsync();
    var diagnostics = new List<string>();
    var staffConnected = WaitForCircuitConnectionAsync(staffPage, diagnostics);
    await staffPage.GotoAsync(SignInUrl(host.StaffUrl, "/app/practice/time"));
    await staffPage.GetByRole(AriaRole.Heading, new() { Name = "Practice time & task records" }).WaitForAsync();
    await staffConnected;
    await staffPage.Locator("select").Nth(1).SelectOptionAsync(taskId.ToString("D"));
    await staffPage.Locator("input[type='date']").Last.FillAsync("2026-09-10");
    await staffPage.GetByLabel("Duration (minutes)").FillAsync("60");
    await staffPage.GetByLabel("Role").FillAsync("Staff");
    await staffPage.GetByLabel("Activity").FillAsync("Fieldwork");
    await staffPage.GetByRole(AriaRole.Button, new() { Name = "Save time draft" }).ClickAsync();
    var timeRow = staffPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "Synthetic approved time" });
    await timeRow.WaitForAsync();
    await Assertions.Expect(staffPage.Locator(".command-result")).ToContainTextAsync(
      "Time draft recorded", new() { Timeout = 30000 });
    await timeRow.GetByRole(AriaRole.Button, new() { Name = "Submit" }).ClickAsync();
    await Assertions.Expect(staffPage.Locator(".command-result")).ToContainTextAsync("submitted for approval");
    await using (var db = host.CreateDbContext())
    {
      timeId = await db.TimeEntries.AsNoTracking().Where(x => x.TaskId == taskId)
        .Select(x => x.Id).SingleAsync();
      Assert.Equal(PracticeTimeStates.TimeSubmitted,
        await db.TimeEntries.Where(x => x.Id == timeId).Select(x => x.Status).SingleAsync());
    }

    var reviewerUrl = await host.StartReviewerWebAsync();
    await using var reviewerContext = await browser.NewContextAsync();
    var reviewerPage = await reviewerContext.NewPageAsync();
    var reviewerConnected = WaitForCircuitConnectionAsync(reviewerPage, diagnostics);
    await reviewerPage.GotoAsync(SignInUrl(reviewerUrl, "/app/practice/time"));
    await reviewerPage.GetByRole(AriaRole.Heading, new() { Name = "Practice time & task records" }).WaitForAsync();
    await reviewerConnected;
    await reviewerPage.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    var reviewRow = reviewerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "Synthetic approved time" });
    await reviewRow.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
    await Assertions.Expect(reviewerPage.Locator(".command-result")).ToContainTextAsync("approved");

    await using (var db = host.CreateDbContext())
    {
      Assert.Equal(host.Fixture.Reviewer.Id,
        await db.TimeEntries.Where(x => x.Id == timeId).Select(x => x.ApprovedByUserId).SingleAsync());
      var line = new InvoiceLineRequest("Approved time", 1m, 100m, "TIME", timeId, 1);
      var draft = await BillingService.CreateInvoiceDraftAsync(db, manager,
        new CreateInvoiceDraftRequest(accountId, "SYN-E2E-INV-001", [line]));
      Assert.True(draft.Succeeded, draft.Message);
      invoiceId = draft.Value;
      var duplicateSource = await BillingService.CreateInvoiceDraftAsync(db, manager,
        new CreateInvoiceDraftRequest(accountId, "SYN-E2E-INV-002", [line]));
      Assert.False(duplicateSource.Succeeded);
      Assert.Equal("billing.source-duplicate", duplicateSource.ErrorCode);
      Assert.True((await BillingService.SubmitInvoiceAsync(db, manager, invoiceId)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, financeReviewer, invoiceId)).Succeeded);
      Assert.True((await BillingService.PostInvoiceAsync(db, manager, invoiceId)).Succeeded);
      Assert.True((await BillingService.SendInvoiceAsync(db, manager, invoiceId)).Succeeded);
      Assert.True((await BillingService.IssueCreditNoteAsync(db, manager,
        new IssueCreditNoteRequest(invoiceId, "SYN-E2E-CN-001", 20m, "Synthetic adjustment"))).Succeeded);
      receiptId = (await BillingService.RecordReceiptAsync(db, manager,
        new RecordReceiptRequest(accountId, 80m, "SYN-E2E-RECEIPT-001"))).Value;
      Assert.True((await BillingService.AllocateReceiptAsync(db, manager,
        new AllocateReceiptRequest(receiptId, invoiceId, 80m))).Succeeded);
      var balance = await BillingService.GetInvoiceBalanceAsync(db, manager, invoiceId);
      Assert.True(balance.Succeeded);
      Assert.Equal(new InvoiceBalance(invoiceId, "QAR", 100m, 20m, 80m, 0m), balance.Value);
      Assert.Empty(await db.FirmJournals.AsNoTracking().Where(x => x.SourceKind == "BILLING" && x.SourceKey == invoiceId.ToString("D")).ToListAsync());
    }

    var accounts = new Dictionary<string, Guid>();
    await using (var db = host.CreateDbContext())
    {
      foreach (var (code, name, type, side) in new[]
      {
        ("SYN-CASH", "Synthetic cash", LedgerStates.AccountAsset, LedgerStates.Debit),
        ("SYN-AR", "Synthetic receivable", LedgerStates.AccountAsset, LedgerStates.Debit),
        ("SYN-REV", "Synthetic service revenue", LedgerStates.AccountRevenue, LedgerStates.Credit)
      })
      {
        var created = await LedgerService.CreateFirmAccountAsync(db, manager,
          new CreateFirmAccountRequest(code, name, type, side));
        Assert.True(created.Succeeded, created.Message);
        accounts[code] = created.Value;
      }

      async Task PostAsync(string number, string source, Guid debit, Guid credit, decimal amount)
      {
        var journal = await LedgerService.CreateFirmJournalDraftAsync(db, manager,
          new CreateFirmJournalDraftRequest(periodId, number, "MANUAL", source, 1, "SYNTHETIC_BILLING",
            "QAR", [new(debit, "Synthetic debit", amount, 0m), new(credit, "Synthetic credit", 0m, amount)]));
        Assert.True(journal.Succeeded, journal.Message);
        Assert.True((await LedgerService.SubmitFirmJournalAsync(db, manager, journal.Value)).Succeeded);
        Assert.True((await LedgerService.ApproveFirmJournalAsync(db, financeReviewer, journal.Value)).Succeeded);
        Assert.True((await LedgerService.PostFirmJournalAsync(db, manager, journal.Value)).Succeeded);
        Assert.True((await LedgerService.PostFirmJournalAsync(db, manager, journal.Value)).Succeeded);
        Assert.Single(await db.FirmPostings.Where(x => x.JournalId == journal.Value).ToListAsync());
      }

      await PostAsync("SYN-E2E-JRN-001", $"INVOICE:{invoiceId:D}", accounts["SYN-AR"], accounts["SYN-REV"], 100m);
      await PostAsync("SYN-E2E-JRN-002", $"CREDIT:{invoiceId:D}", accounts["SYN-REV"], accounts["SYN-AR"], 20m);
      await PostAsync("SYN-E2E-JRN-003", $"RECEIPT:{receiptId:D}", accounts["SYN-CASH"], accounts["SYN-AR"], 80m);
      Assert.True((await LedgerService.CloseFiscalPeriodAsync(db, financeReviewer, periodId,
        "Synthetic E2E period close")).Succeeded);
      Assert.Equal(LedgerStates.PeriodClosed,
        await db.FirmPeriods.Where(x => x.Id == periodId).Select(x => x.Status).SingleAsync());
      Assert.Equal(3, await db.FirmPostings.CountAsync(x => x.PeriodId == periodId));
    }

    await using (var db = host.CreateDbContext())
    {
      var acceptance = await db.AcceptanceDecisions.AsNoTracking().SingleAsync(x => x.PracticeClientId == clientId);
      Assert.Equal("Pending", acceptance.Decision);
      Assert.Null(acceptance.DecidedByUserId);
      Assert.False(await db.Engagements.AnyAsync(x => x.PracticeClientId == clientId));
    }
    var invoiceConnected = WaitForCircuitConnectionAsync(staffPage, diagnostics);
    await staffPage.GotoAsync(SignInUrl(host.StaffUrl, $"/app/practice/invoices/{invoiceId:D}"));
    await staffPage.GetByText("SYN-E2E-INV-001", new() { Exact = true }).WaitForAsync();
    await invoiceConnected;
    await staffPage.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    var invoiceBody = await staffPage.Locator("body").InnerTextAsync();
    Assert.Contains("Status: SENT", invoiceBody, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("100.00", invoiceBody);
    Assert.Contains("0.00", invoiceBody);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-009-PROPOSAL-READ-01")]
  public async Task ProposalDetailShowsPersistedFirmWideProposalAndDeniesScopedIdentity()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-009-PROPOSAL-READ-01");
    var admin = PbcSeed.Actor(host.Fixture.Admin, "Administrator");
    const string leadName = "SYN-PAR-009-REAL-LEAD";
    const string scope = "SYN-PAR-009-REVISION-TWO-SCOPE";
    const decimal fee = 1234.56m;
    Guid proposalId, firstProposalId;
    var restricted = PbcSeed.User(host.Fixture.FirmId, "Staff");
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
      var second = await PracticeCrmService.ReviseProposalAsync(db, admin,
        new ReviseProposalRequest(opportunity.Value, "SYN-PAR-009-PROFILE-V2", scope,
          "SYN-PAR-009-EXCLUSIONS", "SYN-PAR-009-DELIVERABLES", "SYN-PAR-009-DEPENDENCIES",
          fee, "QAR", "2026-01-01", "2026-12-31", ExpectedRevision: 1));
      Assert.True(second.Succeeded, second.Message);
      proposalId = second.Value;

      db.Users.Add(restricted);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "RelationshipManager"),
        PbcSeed.Grant(host.Fixture.FirmId, restricted, "RelationshipManager", host.Fixture.ClientId));
      await db.SaveChangesAsync();
    }

    var restrictedUrl = await host.StartWebForIdentityAsync(restricted);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/practice/proposals/{proposalId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = leadName }).WaitForAsync();
    await page.GetByText(scope, new() { Exact = true }).WaitForAsync();
    await connected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.Contains("DRAFT", body);
    Assert.Contains("1,234.56 QAR", body);
    Assert.Contains("SYN-PAR-009-DELIVERABLES", body);
    Assert.Contains("SUPERSEDED", body);
    Assert.Contains("Proposal author", body);
    Assert.Contains(host.Fixture.Admin.DisplayName, body);
    Assert.Contains("Validity", body);
    Assert.Contains("Workflow actions unavailable", body);
    Assert.DoesNotContain("Acme Holdings W.L.L.", body);
    Assert.DoesNotContain("Senior Manager A", body);

    var documentToken = await page.EvaluateAsync<string>("window.__proposalRouteTestToken = crypto.randomUUID()");
    var unavailableProposalId = Guid.NewGuid();
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/practice/proposals/{unavailableProposalId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Proposal unavailable" }).WaitForAsync();
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__proposalRouteTestToken"));
    var unavailableBody = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(leadName, unavailableBody);
    Assert.DoesNotContain(scope, unavailableBody);
    Assert.DoesNotContain(proposalId.ToString("D"), unavailableBody);

    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.FirmId == host.Fixture.FirmId &&
        x.UserId == host.Fixture.Staff.Id && x.Role == "RelationshipManager" && x.RevokedAt == null);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db, admin,
        new RevokeRoleGrantRequest(grant.Id));
      Assert.True(revoked.Succeeded, revoked.Message);
    }

    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/practice/proposals/{firstProposalId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var revokedBody = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(leadName, revokedBody);
    Assert.DoesNotContain(scope, revokedBody);
    Assert.DoesNotContain(proposalId.ToString("D"), revokedBody);

    var restrictedPage = await browser.NewPageAsync();
    var restrictedDiagnostics = new List<string>();
    var restrictedConnected = WaitForCircuitConnectionAsync(restrictedPage, restrictedDiagnostics);
    await restrictedPage.GotoAsync(SignInUrl(restrictedUrl, $"/app/practice/proposals/{proposalId:D}"));
    await restrictedPage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    await restrictedConnected;
    var restrictedBody = await restrictedPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(leadName, restrictedBody);
    Assert.DoesNotContain(scope, restrictedBody);
    Assert.DoesNotContain(proposalId.ToString("D"), restrictedBody);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
    Assert.DoesNotContain(restrictedDiagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  private static string SignInUrl(string origin, string returnUrl) =>
    $"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}";

  private static Task WaitForCircuitConnectionAsync(IPage page, List<string> diagnostics)
  {
    var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    page.Console += (_, message) =>
    {
      diagnostics.Add($"console/{message.Type}: {message.Text}");
      if (message.Text.Contains("WebSocket connected to ws://", StringComparison.Ordinal))
        connected.TrySetResult();
    };
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    return connected.Task.WaitAsync(TimeSpan.FromSeconds(15));
  }
}
