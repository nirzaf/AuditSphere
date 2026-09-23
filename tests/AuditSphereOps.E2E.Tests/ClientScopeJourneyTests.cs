using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class ClientScopeJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-FIRM-READ-01")]
  public async Task FirmLedgerAndLeadsRequireFirmWideRoleGrants()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-FIRM-READ-01");
    var financeReviewer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var clientFinanceManager = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var auditPartner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var relationshipManager = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var clientRelationshipManager = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var clientRelationshipIdentity = PbcSeed.User(host.Fixture.FirmId, "Client");
    const string periodCode = "2026-09";
    const string accountName = "SYN-PAR-002-FIRM-LEDGER-ACCOUNT";
    const string leadName = "SYN-PAR-002-FIRM-COMMERCIAL-LEAD";
    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(financeReviewer, clientFinanceManager, auditPartner, relationshipManager,
        clientRelationshipManager, clientRelationshipIdentity);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(host.Fixture.FirmId, financeReviewer, "FinanceReviewer"),
        PbcSeed.Grant(host.Fixture.FirmId, clientFinanceManager, "FinanceManager", clientId: host.Fixture.ClientId),
        PbcSeed.Grant(host.Fixture.FirmId, auditPartner, "Partner"),
        PbcSeed.Grant(host.Fixture.FirmId, relationshipManager, "RelationshipManager"),
        PbcSeed.Grant(host.Fixture.FirmId, clientRelationshipManager, "RelationshipManager", clientId: host.Fixture.ClientId),
        PbcSeed.Grant(host.Fixture.FirmId, clientRelationshipIdentity, "RelationshipManager"));
      db.FirmPeriods.Add(new FirmPeriod
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, PeriodCode = periodCode,
        Status = LedgerStates.PeriodOpen, Revision = 1
      });
      db.FirmAccounts.Add(new FirmAccount
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, Code = "SYN-PAR-002",
        Name = accountName, AccountType = LedgerStates.AccountAsset, NormalSide = LedgerStates.Debit
      });
      db.Leads.Add(new Lead
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, Name = leadName,
        Source = "Synthetic regression fixture", Status = CrmStates.LeadNew, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var financeReviewerUrl = await host.StartWebForIdentityAsync(financeReviewer);
    var clientFinanceUrl = await host.StartWebForIdentityAsync(clientFinanceManager);
    var auditPartnerUrl = await host.StartWebForIdentityAsync(auditPartner);
    var relationshipManagerUrl = await host.StartWebForIdentityAsync(relationshipManager);
    var clientRelationshipUrl = await host.StartWebForIdentityAsync(clientRelationshipManager);
    var clientIdentityUrl = await host.StartWebForIdentityAsync(clientRelationshipIdentity);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    async Task<string> ReadPageAsync(string origin, string route, string marker, bool denied)
    {
      await using var context = await browser.NewContextAsync();
      var page = await context.NewPageAsync();
      var diagnostics = new List<string>();
      var connected = WaitForCircuitConnectionAsync(page, diagnostics);
      await page.GotoAsync(SignInUrl(origin, route));
      if (denied)
        await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
      else
        await page.GetByText(marker, new() { Exact = true }).WaitForAsync();
      await connected;
      await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
      var body = await page.Locator("body").InnerTextAsync();
      Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
      return body;
    }

    var authorizedFinance = await ReadPageAsync(financeReviewerUrl, "/app/finance", periodCode, denied: false);
    Assert.Contains(accountName, authorizedFinance);
    var scopedFinance = await ReadPageAsync(clientFinanceUrl, "/app/finance", "Access unavailable", denied: true);
    Assert.DoesNotContain(periodCode, scopedFinance);
    Assert.DoesNotContain(accountName, scopedFinance);
    var partnerFinance = await ReadPageAsync(auditPartnerUrl, "/app/finance", "Access unavailable", denied: true);
    Assert.DoesNotContain(periodCode, partnerFinance);
    Assert.DoesNotContain(accountName, partnerFinance);

    var authorizedLeads = await ReadPageAsync(relationshipManagerUrl, "/app/practice/leads", leadName, denied: false);
    Assert.Contains(leadName, authorizedLeads);
    var scopedLeads = await ReadPageAsync(clientRelationshipUrl, "/app/practice/leads", "Access unavailable", denied: true);
    Assert.DoesNotContain(leadName, scopedLeads);
    var clientClassified = await ReadPageAsync(clientIdentityUrl, "/app/practice/leads", "Access unavailable", denied: true);
    Assert.DoesNotContain(leadName, clientClassified);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ADV-CONSOLIDATION-READ-01")]
  public async Task AdvancedConsolidationReadRejectsClientIdentityWithErroneousGroupGrant()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ADV-CONSOLIDATION-READ-01");
    var groupId = Guid.NewGuid();
    var scopeId = Guid.NewGuid();
    const string privateGroupName = "SYN-PAR-002-PRIVATE-ADVANCED-GROUP";
    await using (var db = host.CreateDbContext())
    {
      db.ClientGroups.Add(new ClientGroup
      {
        Id = groupId, FirmId = host.Fixture.FirmId, Code = "SYN-PAR-002-PRIVATE",
        Name = privateGroupName, CreatedByUserId = host.Fixture.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ConsolidationScopeVersions.Add(new ConsolidationScopeVersion
      {
        Id = scopeId, FirmId = host.Fixture.FirmId, GroupId = groupId, PeriodId = Guid.NewGuid(),
        Method = AdvancedConsolidationMethods.AcquisitionNci, ReportingCurrency = "QAR",
        OpeningBasis = "OPENING-2026", CreatedByUserId = host.Fixture.Admin.Id
      });
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, GroupId = groupId,
        UserId = host.Fixture.Client.Id, Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow,
        GrantedByUserId = host.Fixture.Admin.Id
      });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(host.Fixture.Client);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(origin, $"/app/consolidation/advanced/{scopeId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    await connected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateGroupName, body);
    Assert.DoesNotContain(scopeId.ToString("D"), body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-FIRM-ADMIN-READ-01")]
  public async Task FirmAdministrationRejectsClientIdentityWithErroneousAdminGrant()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-FIRM-ADMIN-READ-01");
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Client, "Administrator"));
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(host.Fixture.Client);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(origin, "/app/administration"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    await connected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("Firm safety state", body);
    Assert.DoesNotContain("Firm role grants", body);
    Assert.DoesNotContain(host.Fixture.Client.Email, body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-PORTFOLIO-READ-01")]
  public async Task ClientIdentityWithErroneousFirmWideStaffGrantCannotViewPortfolio()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-PORTFOLIO-READ-01");
    const string privateClientName = "SYN-PAR-002-PRIVATE-PORTFOLIO-CLIENT";
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, LegalName = privateClientName,
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Client, "Staff"));
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(host.Fixture.Client);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(origin, "/app"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "No firm, client or engagement scope" }).WaitForAsync();
    await connected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateClientName, body);
    Assert.DoesNotContain(host.Fixture.Client.Email, body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-TIME-ENGAGEMENT-SCOPE-01")]
  public async Task EngagementScopedTimeQueueHidesSiblingTasksEntriesAndClientPeriods()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-TIME-ENGAGEMENT-SCOPE-01");
    var engagementManager = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var siblingEngagementId = Guid.NewGuid();
    var assignedTaskId = Guid.NewGuid();
    var siblingTaskId = Guid.NewGuid();
    const string periodCode = "SYN-PAR-002-TIME-CLIENT-PERIOD";
    const string assignedTaskTitle = "SYN-PAR-002-ASSIGNED-ENGAGEMENT-TASK";
    const string siblingTaskTitle = "SYN-PAR-002-SIBLING-ENGAGEMENT-TASK";
    const string assignedNarrative = "SYN-PAR-002-ASSIGNED-TIME-NARRATIVE";
    const string siblingNarrative = "SYN-PAR-002-SIBLING-TIME-NARRATIVE";
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(engagementManager);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, engagementManager, "Manager",
        clientId: host.Fixture.ClientId, engagementId: host.Fixture.EngagementId));
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = siblingEngagementId, FirmId = host.Fixture.FirmId, PracticeClientId = host.Fixture.ClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        PeriodCode = periodCode, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.WorkTasks.AddRange(
        new WorkTask
        {
          Id = assignedTaskId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
          EngagementId = host.Fixture.EngagementId, Title = assignedTaskTitle, CreatedAt = DateTimeOffset.UtcNow
        },
        new WorkTask
        {
          Id = siblingTaskId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
          EngagementId = siblingEngagementId, Title = siblingTaskTitle, CreatedAt = DateTimeOffset.UtcNow
        });
      db.TimeEntries.AddRange(
        new TimeEntry
        {
          Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
          EngagementId = host.Fixture.EngagementId, TaskId = assignedTaskId, UserId = host.Fixture.Staff.Id,
          WorkDate = new DateOnly(2026, 9, 10), StartMinute = 540, DurationMinutes = 60,
          Role = "Staff", Activity = "Fieldwork", Narrative = assignedNarrative,
          BillableClassification = PracticeTimeStates.NonBillable,
          Status = PracticeTimeStates.TimeSubmitted, SubmittedAt = DateTimeOffset.UtcNow,
          NarrativeVisibility = PracticeTimeStates.NarrativeInternal, Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
        },
        new TimeEntry
        {
          Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
          EngagementId = siblingEngagementId, TaskId = siblingTaskId, UserId = host.Fixture.Staff.Id,
          WorkDate = new DateOnly(2026, 9, 11), StartMinute = 540, DurationMinutes = 60,
          Role = "Staff", Activity = "Review", Narrative = siblingNarrative,
          BillableClassification = PracticeTimeStates.NonBillable,
          Status = PracticeTimeStates.TimeSubmitted, SubmittedAt = DateTimeOffset.UtcNow,
          NarrativeVisibility = PracticeTimeStates.NarrativeInternal, Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
        });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(engagementManager);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(origin, "/app/practice/time"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Practice time & task records" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    var body = await page.Locator("body").InnerTextAsync();
    Assert.Contains(assignedTaskTitle, body);
    Assert.Contains(assignedNarrative, body);
    Assert.DoesNotContain(siblingTaskTitle, body);
    Assert.DoesNotContain(siblingNarrative, body);
    Assert.DoesNotContain(periodCode, body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ASSESS-01")]
  public async Task AssessmentDecisionIdResolvesScopeBeforeExposingAssessmentDetails()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-ASSESS-01");
    var partner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedManager = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    var decisionId = Guid.NewGuid();
    const string privateRegistration = "SYNTHETIC-PAR-002-PRIVATE-ASSESSMENT-REGISTRATION";
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated assessment client", CreatedAt = DateTimeOffset.UtcNow
      });
      var client = await db.PracticeClients.SingleAsync(x => x.Id == host.Fixture.ClientId);
      client.RegistrationNumber = privateRegistration;
      db.Users.AddRange(partner, unrelatedManager);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, partner, "Partner", clientId: host.Fixture.ClientId));
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, unrelatedManager, "Manager", clientId: unrelatedClientId));
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = decisionId, FirmId = host.Fixture.FirmId, PracticeClientId = host.Fixture.ClientId,
        Decision = "Declined", ServiceRoute = "SyntheticAudit", Generation = 1,
        Rationale = "Synthetic scope regression decision", EvaluationTemplateVersion = "SYNTHETIC-v1",
        EvaluationSnapshotDigest = new string('a', 64), DecidedByUserId = partner.Id, DecidedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var partnerUrl = await host.StartWebForIdentityAsync(partner);
    await using var partnerContext = await browser.NewContextAsync();
    var partnerPage = await partnerContext.NewPageAsync();
    var partnerConnected = WaitForCircuitConnectionAsync(partnerPage, []);
    await partnerPage.GotoAsync(SignInUrl(partnerUrl, $"/app/assessments/{decisionId:D}"));
    await partnerPage.GetByText(privateRegistration).WaitForAsync();
    await partnerPage.GetByText("Decision recorded:").WaitForAsync();
    await partnerConnected;

    var managerUrl = await host.StartWebForIdentityAsync(unrelatedManager);
    await using var managerContext = await browser.NewContextAsync();
    var managerPage = await managerContext.NewPageAsync();
    var diagnostics = new List<string>();
    var managerConnected = WaitForCircuitConnectionAsync(managerPage, diagnostics);
    await managerPage.GotoAsync(SignInUrl(managerUrl, $"/app/assessments/{decisionId:D}"));
    await managerPage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    await managerConnected;
    var body = await managerPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateRegistration, body);
    Assert.DoesNotContain("Decision recorded:", body);
    Assert.DoesNotContain("Client workspace", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ACCT-QUEUE-01")]
  public async Task ClientScopedPartnerCannotSeeAccountingEvidenceFromAnotherClient()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-ACCT-QUEUE-01");
    var allowedPartner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedPartner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    const string privateEvidenceReference = "SYNTHETIC-PAR-002-PRIVATE-ACCOUNTING-EVIDENCE";
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated accounting client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.AddRange(allowedPartner, unrelatedPartner);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, allowedPartner, "Partner", clientId: host.Fixture.ClientId));
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, unrelatedPartner, "Partner", clientId: unrelatedClientId));
      var periodId = Guid.NewGuid();
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        PeriodCode = "SYN-2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.SpecialistAccountingSchedules.Add(new SpecialistAccountingSchedule
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, PeriodId = periodId, Area = "PAYROLL",
        MethodologyVersion = "SYNTHETIC-TEST-v1", EvidenceReference = privateEvidenceReference,
        CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var allowedUrl = await host.StartWebForIdentityAsync(allowedPartner);
    await using var allowedContext = await browser.NewContextAsync();
    var allowedPage = await allowedContext.NewPageAsync();
    var allowedConnected = WaitForCircuitConnectionAsync(allowedPage, []);
    await allowedPage.GotoAsync(SignInUrl(allowedUrl, "/app/accounting/evidence"));
    await allowedPage.GetByText(privateEvidenceReference).WaitForAsync();
    await allowedConnected;

    var unrelatedUrl = await host.StartWebForIdentityAsync(unrelatedPartner);
    await using var unrelatedContext = await browser.NewContextAsync();
    var unrelatedPage = await unrelatedContext.NewPageAsync();
    var diagnostics = new List<string>();
    var unrelatedConnected = WaitForCircuitConnectionAsync(unrelatedPage, diagnostics);
    await unrelatedPage.GotoAsync(SignInUrl(unrelatedUrl, "/app/accounting/evidence"));
    await unrelatedPage.GetByText("Evidence records", new() { Exact = true }).WaitForAsync();
    await unrelatedConnected;
    var body = await unrelatedPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateEvidenceReference, body);
    Assert.Contains("No ECL, inventory, specialist, analytical or journal-risk records are available in the selected scope.", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ACCT-WORKSPACE-01")]
  public async Task AccountingWorkspaceIgnoresOtherRoleAndMismatchedEngagementGrants()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-ACCT-WORKSPACE-01");
    var partner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    var unrelatedEngagementId = Guid.NewGuid();
    const string privateClientName = "SYNTHETIC-PAR-002-UNRELATED-ACCOUNTING-CLIENT";
    const string privatePeriodCode = "SYN-PRIVATE-ACCOUNTING-PERIOD";
    const string privateEvidenceReference = "SYNTHETIC-PAR-002-UNRELATED-ACCOUNTING-EVIDENCE";
    const string privateDifferenceArea = "SYNTHETIC-PAR-002-UNRELATED-DIFFERENCE";
    const string allowedDifferenceArea = "SYNTHETIC-PAR-002-ASSIGNED-DIFFERENCE";
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = privateClientName, CreatedAt = DateTimeOffset.UtcNow
      });
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = unrelatedEngagementId, FirmId = host.Fixture.FirmId, PracticeClientId = unrelatedClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(partner);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(host.Fixture.FirmId, partner, "Partner", clientId: host.Fixture.ClientId),
        PbcSeed.Grant(host.Fixture.FirmId, partner, "Staff", clientId: unrelatedClientId),
        PbcSeed.Grant(host.Fixture.FirmId, partner, "Partner", clientId: host.Fixture.ClientId, engagementId: unrelatedEngagementId));
      db.ClientAccountingProfiles.Add(new ClientAccountingProfile
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = unrelatedClientId,
        Jurisdiction = "SYNTHETIC", FunctionalCurrency = "QAR", SourceSystem = "TEST",
        SourceSystemIdentifier = "SYNTHETIC-UNRELATED", CreatedByUserId = host.Fixture.Staff.Id,
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = unrelatedClientId,
        PeriodCode = privatePeriodCode, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.SpecialistAccountingSchedules.Add(new SpecialistAccountingSchedule
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = unrelatedClientId,
        EngagementId = unrelatedEngagementId, Area = "PAYROLL", MethodologyVersion = "SYNTHETIC-TEST-v1",
        EvidenceReference = privateEvidenceReference, CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.AuditDifferences.Add(new AuditDifference
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = unrelatedClientId,
        EngagementId = unrelatedEngagementId, AccountArea = privateDifferenceArea, DifferenceType = "UNADJUSTED",
        Description = "Synthetic unrelated-client difference", Amount = 987m, Currency = "QAR",
        CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.AuditDifferences.Add(new AuditDifference
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, AccountArea = allowedDifferenceArea, DifferenceType = "UNADJUSTED",
        Description = "Synthetic assigned-client difference", Amount = 12m, Currency = "QAR",
        CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var baseUrl = await host.StartWebForIdentityAsync(partner);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(baseUrl, "/app/accounting"));
    await page.GetByText("1 explicitly granted legal entity").WaitForAsync();
    await connected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateClientName, body);
    Assert.DoesNotContain(privatePeriodCode, body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));

    var evidencePage = await context.NewPageAsync();
    var evidenceConnected = WaitForCircuitConnectionAsync(evidencePage, diagnostics);
    await evidencePage.GotoAsync(SignInUrl(baseUrl, "/app/accounting/evidence"));
    await evidencePage.GetByText("Evidence records", new() { Exact = true }).WaitForAsync();
    await evidenceConnected;
    Assert.DoesNotContain(privateEvidenceReference, await evidencePage.Locator("body").InnerTextAsync());
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));

    var recordsPage = await context.NewPageAsync();
    var recordsConnected = WaitForCircuitConnectionAsync(recordsPage, diagnostics);
    await recordsPage.GotoAsync(SignInUrl(baseUrl, "/app/accounting/differences"));
    await recordsPage.Locator("h1").WaitForAsync();
    Assert.Equal("Audit differences", await recordsPage.Locator("h1").InnerTextAsync());
    await recordsConnected;
    await recordsPage.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    var recordsBody = await recordsPage.Locator("body").InnerTextAsync();
    Assert.Contains(allowedDifferenceArea, recordsBody);
    Assert.DoesNotContain(privateDifferenceArea, recordsBody);
    Assert.DoesNotContain(privateClientName, recordsBody);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ACCT-PERIOD-01")]
  public async Task AccountingPeriodRequiresClientGrantAndClearsPriorPeriodOnRouteChange()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-ACCT-PERIOD-01");
    var partner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    var unrelatedEngagementId = Guid.NewGuid();
    const string assignedPeriodCode = "SYN-ASSIGNED-ACCOUNTING-PERIOD";
    const string privatePeriodCode = "SYN-PRIVATE-ACCOUNTING-PERIOD-DETAIL";
    var assignedPeriodId = Guid.NewGuid();
    var privatePeriodId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "SYNTHETIC-PAR-002-UNRELATED-PERIOD-CLIENT", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = unrelatedEngagementId, FirmId = host.Fixture.FirmId, PracticeClientId = unrelatedClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(partner);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(host.Fixture.FirmId, partner, "Partner", clientId: host.Fixture.ClientId),
        PbcSeed.Grant(host.Fixture.FirmId, partner, "Staff", clientId: unrelatedClientId),
        PbcSeed.Grant(host.Fixture.FirmId, partner, "Partner", clientId: host.Fixture.ClientId, engagementId: unrelatedEngagementId));
      db.ClientReportingPeriods.AddRange(
        new ClientReportingPeriod
        {
          Id = assignedPeriodId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
          PeriodCode = assignedPeriodCode, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
          Basis = "STATUTORY", Currency = "QAR", CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
        },
        new ClientReportingPeriod
        {
          Id = privatePeriodId, FirmId = host.Fixture.FirmId, ClientId = unrelatedClientId,
          PeriodCode = privatePeriodCode, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
          Basis = "STATUTORY", Currency = "QAR", CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
        });
      await db.SaveChangesAsync();
    }

    var baseUrl = await host.StartWebForIdentityAsync(partner);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(baseUrl, $"/app/accounting/periods/{assignedPeriodId:D}"));
    await page.Locator("h1").WaitForAsync();
    await connected;
    var assignedBody = await page.Locator("body").InnerTextAsync();
    Assert.Contains(assignedPeriodCode, assignedBody);

    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/accounting/periods/{privatePeriodId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(assignedPeriodCode, body);
    Assert.DoesNotContain(privatePeriodCode, body);
    Assert.DoesNotContain("SYNTHETIC-PAR-002-UNRELATED-PERIOD-CLIENT", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ACCT-PERIOD-MAINT-01")]
  public async Task PeriodMaintenanceQueuesDoNotWidenEngagementGrantToClientPeriodScope()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-ACCT-PERIOD-MAINT-01");
    var clientPartner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var engagementPreparer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    const string privatePeriodCode = "SYN-PAR-002-CLIENT-LEVEL-PERIOD";
    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(clientPartner, engagementPreparer);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(host.Fixture.FirmId, clientPartner, "Partner", clientId: host.Fixture.ClientId),
        PbcSeed.Grant(host.Fixture.FirmId, engagementPreparer, "AccountingPreparer",
          clientId: host.Fixture.ClientId, engagementId: host.Fixture.EngagementId));
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        PeriodCode = privatePeriodCode, StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Closed,
        CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var partnerUrl = await host.StartWebForIdentityAsync(clientPartner);
    var engagementUrl = await host.StartWebForIdentityAsync(engagementPreparer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var diagnostics = new List<string>();

    foreach (var route in new[] { "/app/accounting/rollforward", "/app/accounting/restatements" })
    {
      await using var partnerContext = await browser.NewContextAsync();
      var partnerPage = await partnerContext.NewPageAsync();
      var partnerConnected = WaitForCircuitConnectionAsync(partnerPage, diagnostics);
      await partnerPage.GotoAsync(SignInUrl(partnerUrl, route));
      await partnerPage.Locator("h1").WaitForAsync();
      await partnerConnected;
      if (route.EndsWith("rollforward", StringComparison.Ordinal))
        Assert.Contains(privatePeriodCode, await partnerPage.Locator("body").InnerTextAsync());
      else
      {
        await partnerPage.GetByRole(AriaRole.Heading, new() { Name = "Request a restatement" }).WaitForAsync();
        Assert.Contains(privatePeriodCode, await partnerPage.Locator("body").InnerTextAsync());
      }

      await using var engagementContext = await browser.NewContextAsync();
      var engagementPage = await engagementContext.NewPageAsync();
      var engagementConnected = WaitForCircuitConnectionAsync(engagementPage, diagnostics);
      await engagementPage.GotoAsync(SignInUrl(engagementUrl, route));
      await engagementPage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
      await engagementConnected;
      var body = await engagementPage.Locator("body").InnerTextAsync();
      Assert.DoesNotContain(privatePeriodCode, body);
      Assert.DoesNotContain("PBC TEST CLIENT", body);
      Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
    }
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ACCEPT-01")]
  public async Task AcceptanceDecisionRequiresPartnerGrantForExactClient()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-ACCEPT-01");
    var partner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedManager = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated acceptance client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.AddRange(partner, unrelatedManager);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, partner, "Partner", clientId: host.Fixture.ClientId));
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, unrelatedManager, "Manager", clientId: unrelatedClientId));
      await db.SaveChangesAsync();
    }

    var partnerUrl = await host.StartWebForIdentityAsync(partner);
    var managerUrl = await host.StartWebForIdentityAsync(unrelatedManager);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    await using var partnerContext = await browser.NewContextAsync();
    var partnerPage = await partnerContext.NewPageAsync();
    var partnerConnected = WaitForCircuitConnectionAsync(partnerPage, []);
    await partnerPage.GotoAsync(SignInUrl(partnerUrl, $"/app/assessments/{host.Fixture.ClientId:D}/decision"));
    await partnerPage.Locator("#decision-outcome").WaitForAsync();
    await partnerConnected;

    await using var managerContext = await browser.NewContextAsync();
    var managerPage = await managerContext.NewPageAsync();
    var diagnostics = new List<string>();
    var managerConnected = WaitForCircuitConnectionAsync(managerPage, diagnostics);
    await managerPage.GotoAsync(SignInUrl(managerUrl, $"/app/assessments/{host.Fixture.ClientId:D}/decision"));
    await managerPage.GetByRole(AriaRole.Heading, new() { Name = "Decision unavailable" }).WaitForAsync();
    await managerConnected;
    Assert.False(await managerPage.Locator("#decision-outcome").CountAsync() > 0);
    Assert.False(await managerPage.GetByRole(AriaRole.Button, new() { Name = "Record Partner Decision" }).CountAsync() > 0);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-COMP-01")]
  public async Task CompletionChecklistDeniesSiblingEngagementAndKeepsRepresentationPrivate()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-COMP-01");
    var viewer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    const string privateNarrative = "SYNTHETIC-PAR-002-PRIVATE-REPRESENTATION";
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated completion client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(viewer);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, viewer, "Manager", clientId: unrelatedClientId));
      db.WrittenRepresentations.Add(new WrittenRepresentation
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, Code = "SYN-R-01", Title = "Synthetic representation",
        Narrative = privateNarrative
      });
      await db.SaveChangesAsync();
    }

    var viewerUrl = await host.StartWebForIdentityAsync(viewer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var staffContext = await browser.NewContextAsync();
    var staffPage = await staffContext.NewPageAsync();
    var staffDiagnostics = new List<string>();
    var staffConnected = WaitForCircuitConnectionAsync(staffPage, staffDiagnostics);
    await staffPage.GotoAsync(SignInUrl(host.StaffUrl, $"/app/engagements/{host.Fixture.EngagementId:D}/completion"));
    await staffPage.GetByText(privateNarrative).WaitForAsync();
    await staffConnected;

    await using var viewerContext = await browser.NewContextAsync();
    var page = await viewerContext.NewPageAsync();
    var diagnostics = new List<string>();
    var viewerConnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(viewerUrl, $"/app/engagements/{host.Fixture.EngagementId:D}/completion"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Engagement unavailable" }).WaitForAsync();
    await viewerConnected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateNarrative, body);
    Assert.DoesNotContain("SYN-R-01", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ENG-01")]
  public async Task EngagementDetailDeniesSiblingScopeBeforeLoadingClientMetadata()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-ENG-01");
    var viewer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated engagement client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(viewer);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, viewer, "Manager", clientId: unrelatedClientId));
      await db.SaveChangesAsync();
    }

    var viewerUrl = await host.StartWebForIdentityAsync(viewer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var staffContext = await browser.NewContextAsync();
    var staffPage = await staffContext.NewPageAsync();
    var staffDiagnostics = new List<string>();
    var staffConnected = WaitForCircuitConnectionAsync(staffPage, staffDiagnostics);
    await staffPage.GotoAsync(SignInUrl(host.StaffUrl, $"/app/engagements/{host.Fixture.EngagementId:D}"));
    await staffPage.GetByText("PBC TEST CLIENT", new() { Exact = true }).WaitForAsync();
    await staffConnected;

    await using var viewerContext = await browser.NewContextAsync();
    var page = await viewerContext.NewPageAsync();
    var diagnostics = new List<string>();
    var viewerConnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(viewerUrl, $"/app/engagements/{host.Fixture.EngagementId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Engagement unavailable" }).WaitForAsync();
    await viewerConnected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("PBC TEST CLIENT", body);
    Assert.DoesNotContain("Professional Work Blocked", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-REV-01")]
  public async Task ReviewPointReadAndDispositionAreEngagementScoped()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-REV-01");
    var viewer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    var point = new ReviewPoint
    {
      Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
      EngagementId = host.Fixture.EngagementId, TargetId = Guid.NewGuid(), TargetKind = "workpaper",
      TargetRevision = 1, Comment = "SYNTHETIC-PAR-002-PRIVATE-REVIEW-COMMENT",
      RaisedByUserId = host.Fixture.Staff.Id, RaisedAt = DateTimeOffset.UtcNow
    };
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated review client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(viewer);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, viewer, "Manager", clientId: unrelatedClientId));
      db.ReviewPoints.Add(point);
      await db.SaveChangesAsync();
    }

    var viewerUrl = await host.StartWebForIdentityAsync(viewer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var staffContext = await browser.NewContextAsync();
    var staffPage = await staffContext.NewPageAsync();
    var staffDiagnostics = new List<string>();
    var staffConnected = WaitForCircuitConnectionAsync(staffPage, staffDiagnostics);
    await staffPage.GotoAsync(SignInUrl(host.StaffUrl, $"/app/reviews/{point.Id:D}"));
    await staffPage.GetByText(point.Comment).WaitForAsync();
    await staffConnected;
    staffPage.PageError += (_, error) => staffDiagnostics.Add($"page-error: {error}");
    await staffPage.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    var clearButton = staffPage.GetByRole(AriaRole.Button, new() { Name = "Clear Review Point" });
    Assert.True(await clearButton.IsEnabledAsync());
    await clearButton.ClickAsync();
    try { await staffPage.GetByText("Disposition updated successfully.").WaitForAsync(new() { Timeout = 8000 }); }
    catch (TimeoutException ex)
    {
      await using var check = host.CreateDbContext();
      var cleared = await check.ReviewPoints.AsNoTracking().Where(x => x.Id == point.Id).Select(x => x.Cleared).SingleAsync();
      throw new Xunit.Sdk.XunitException($"Review-point disposition did not complete (database cleared={cleared}).\n{await staffPage.Locator("body").InnerTextAsync()}\n{string.Join("\n", staffDiagnostics)}\n{ex.Message}");
    }
    await using (var verify = host.CreateDbContext())
      Assert.True((await verify.ReviewPoints.AsNoTracking().SingleAsync(x => x.Id == point.Id)).Cleared);

    await using var viewerContext = await browser.NewContextAsync();
    var page = await viewerContext.NewPageAsync();
    var diagnostics = new List<string>();
    var viewerConnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(viewerUrl, $"/app/reviews/{point.Id:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Review point unavailable" }).WaitForAsync();
    await viewerConnected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(point.Comment, body);
    Assert.DoesNotContain("Review Context", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-FIND-01")]
  public async Task FindingDetailsRequireCurrentClientAndEngagementScope()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-FIND-01");
    var viewer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    const string privateImpact = "SYNTHETIC-PAR-002-PRIVATE-FINDING-IMPACT";
    const string privateResponse = "SYNTHETIC-PAR-002-PRIVATE-FINDING-RESPONSE";
    Guid findingId;
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated finding client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(viewer);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, viewer, "Manager", clientId: unrelatedClientId));
      var created = await AuditPlanningService.CreateFindingAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "Staff"),
        new CreateFindingRequest(host.Fixture.EngagementId, "Synthetic scoped finding", privateImpact,
          false, 125m, null));
      Assert.True(created.Succeeded, created.Message);
      findingId = created.Value!.FindingId;
      var response = await AuditPlanningService.RecordFindingResponseAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "Staff"),
        new RecordFindingResponseRequest(findingId, privateResponse, false));
      Assert.True(response.Succeeded, response.Message);
      await db.SaveChangesAsync();
    }

    var viewerUrl = await host.StartWebForIdentityAsync(viewer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    await using var staffContext = await browser.NewContextAsync();
    var staffPage = await staffContext.NewPageAsync();
    var staffConnected = WaitForCircuitConnectionAsync(staffPage, []);
    await staffPage.GotoAsync(SignInUrl(host.StaffUrl, $"/app/findings/{findingId:D}"));
    await staffPage.GetByText(privateImpact).WaitForAsync();
    await Assertions.Expect(staffPage.Locator("blockquote.management-response")).ToContainTextAsync(privateResponse);
    await staffConnected;

    await using var viewerContext = await browser.NewContextAsync();
    var page = await viewerContext.NewPageAsync();
    var diagnostics = new List<string>();
    var viewerConnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(viewerUrl, $"/app/findings/{findingId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Finding unavailable" }).WaitForAsync();
    await viewerConnected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateImpact, body);
    Assert.DoesNotContain(privateResponse, body);
    Assert.DoesNotContain("125.00", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-POP-01")]
  public async Task AuditPopulationAndLinkedReceiptsRequireCurrentEngagementScope()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-POP-01");
    var viewer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    const string privatePurpose = "SYNTHETIC-PAR-002-PRIVATE-POPULATION-PURPOSE";
    const string privateParameters = "SYNTHETIC-PAR-002-PRIVATE-EXTRACTION-PARAMETERS";
    const string privateReceiptToken = "SYNTHETIC-PAR-002-PRIVATE-RECEIPT-TOKEN";
    const string privateFileName = "SYNTHETIC-PAR-002-PRIVATE-SOURCE.csv";
    Guid populationId;
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated population client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(viewer);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, viewer, "Manager", clientId: unrelatedClientId));
      var created = await AuditPlanningService.CreatePopulationVersionAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "Staff"),
        new CreatePopulationRequest(host.Fixture.EngagementId, privatePurpose, "Existence",
          "Synthetic source receipt", privateParameters, 3, 98765.43m, "QAR", null));
      Assert.True(created.Succeeded, created.Message);
      populationId = created.Value!.PopulationId;

      var receiptId = Guid.NewGuid();
      db.SourceReceipts.Add(new SourceReceipt
      {
        Id = receiptId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, SourceType = "CSV_IMPORT", ReceiptToken = privateReceiptToken,
        Sha256Digest = new string('a', 64), ByteCount = 3, OriginalFileName = privateFileName,
        AcquiredAt = DateTimeOffset.UtcNow, AcquiredByUserId = host.Fixture.Staff.Id
      });
      db.EvidenceLinks.Add(new EvidenceLink
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, SourceReceiptId = receiptId,
        Purpose = "SYNTHETIC-PAR-002-PRIVATE-EVIDENCE-PURPOSE", Assertion = "Existence",
        RelevanceReliabilityAssessment = "Synthetic evidence scope fixture", CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var viewerUrl = await host.StartWebForIdentityAsync(viewer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    await using var staffContext = await browser.NewContextAsync();
    var staffPage = await staffContext.NewPageAsync();
    var staffConnected = WaitForCircuitConnectionAsync(staffPage, []);
    await staffPage.GotoAsync(SignInUrl(host.StaffUrl, $"/app/audit/populations/{populationId:D}"));
    await staffPage.GetByText(privatePurpose).WaitForAsync();
    await Assertions.Expect(staffPage.GetByText(privateReceiptToken)).ToBeVisibleAsync();
    await staffConnected;

    await using var viewerContext = await browser.NewContextAsync();
    var page = await viewerContext.NewPageAsync();
    var diagnostics = new List<string>();
    var viewerConnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(viewerUrl, $"/app/audit/populations/{populationId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Population unavailable" }).WaitForAsync();
    await viewerConnected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privatePurpose, body);
    Assert.DoesNotContain(privateParameters, body);
    Assert.DoesNotContain(privateReceiptToken, body);
    Assert.DoesNotContain(privateFileName, body);
    Assert.DoesNotContain("98765.43", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-WP-01")]
  public async Task WorkpaperAndSubmissionHistoryRequireCurrentEngagementScope()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-WP-01");
    var viewer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    const string privateTitle = "SYNTHETIC-PAR-002-PRIVATE-WORKPAPER-TITLE";
    const string privateObjective = "SYNTHETIC-PAR-002-PRIVATE-WORKPAPER-OBJECTIVE";
    const string privateProcedure = "SYNTHETIC-PAR-002-PRIVATE-WORKPAPER-PROCEDURE";
    const string privateSnapshotConclusion = "SYNTHETIC-PAR-002-PRIVATE-SUBMISSION-CONCLUSION";
    Guid workpaperId;
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated workpaper client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(viewer);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, viewer, "Manager", clientId: unrelatedClientId));
      var created = await AuditPlanningService.CreateWorkpaperAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "Staff"),
        new CreateWorkpaperRequest(host.Fixture.EngagementId, "SYN-WP-01", privateTitle,
          privateObjective, "SYNTHETIC-TEMPLATE-v1", null, privateProcedure));
      Assert.True(created.Succeeded, created.Message);
      workpaperId = created.Value!.WorkpaperId;
      db.WorkpaperSubmissions.Add(new WorkpaperSubmission
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, WorkpaperId = workpaperId,
        ActorId = host.Fixture.Staff.Id, Revision = 2, WorkPerformed = "Synthetic frozen snapshot",
        Conclusion = privateSnapshotConclusion, SubmittedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var viewerUrl = await host.StartWebForIdentityAsync(viewer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    await using var staffContext = await browser.NewContextAsync();
    var staffPage = await staffContext.NewPageAsync();
    var staffConnected = WaitForCircuitConnectionAsync(staffPage, []);
    await staffPage.GotoAsync(SignInUrl(host.StaffUrl, $"/app/audit/workpapers/{workpaperId:D}"));
    await staffPage.GetByText(privateObjective).WaitForAsync();
    await Assertions.Expect(staffPage.GetByText(privateProcedure)).ToBeVisibleAsync();
    await Assertions.Expect(staffPage.GetByText(privateSnapshotConclusion)).ToBeVisibleAsync();
    await staffConnected;
    await using (var db = host.CreateDbContext())
    {
      var changed = await db.RoleGrants.Where(x => x.FirmId == host.Fixture.FirmId &&
          x.UserId == host.Fixture.Staff.Id && x.RevokedAt == null)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
      Assert.Equal(1, changed);
    }
    await staffPage.Locator("#wp-work").FillAsync("SYNTHETIC-PAR-002-WORK-AFTER-REVOCATION");
    await staffPage.GetByRole(AriaRole.Heading, new() { Name = "Workpaper unavailable" }).WaitForAsync();
    var revokedBody = await staffPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateTitle, revokedBody);
    Assert.DoesNotContain(privateObjective, revokedBody);
    Assert.DoesNotContain(privateSnapshotConclusion, revokedBody);
    Assert.DoesNotContain("SYNTHETIC-PAR-002-WORK-AFTER-REVOCATION", revokedBody);
    await using (var verify = host.CreateDbContext())
      Assert.False(await verify.WorkpaperDrafts.AnyAsync(x => x.WorkpaperId == workpaperId));

    await using var viewerContext = await browser.NewContextAsync();
    var page = await viewerContext.NewPageAsync();
    var diagnostics = new List<string>();
    var viewerConnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(viewerUrl, $"/app/audit/workpapers/{workpaperId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Workpaper unavailable" }).WaitForAsync();
    await viewerConnected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateTitle, body);
    Assert.DoesNotContain(privateObjective, body);
    Assert.DoesNotContain(privateProcedure, body);
    Assert.DoesNotContain(privateSnapshotConclusion, body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-CLIENT-01")]
  public async Task ClientProfileRequiresCoveringClientGrant()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-CLIENT-01");
    var unrelatedManager = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    const string privateContact = "SYNTHETIC-PAR-002-PRIVATE-CONTACT";
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = host.Fixture.FirmId,
        LegalName = "Synthetic unrelated profile client", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(unrelatedManager);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, unrelatedManager, "Manager", clientId: unrelatedClientId));
      db.ClientContacts.Add(new ClientContact
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, PracticeClientId = host.Fixture.ClientId,
        FullName = privateContact, Email = "private-contact@example.test", Role = "Synthetic CFO",
        ValidFrom = DateTimeOffset.UtcNow, Primary = true
      });
      await db.SaveChangesAsync();
    }

    var adminUrl = await host.StartWebForIdentityAsync(host.Fixture.Admin);
    var unrelatedUrl = await host.StartWebForIdentityAsync(unrelatedManager);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    await using var adminContext = await browser.NewContextAsync();
    var adminPage = await adminContext.NewPageAsync();
    var adminConnected = WaitForCircuitConnectionAsync(adminPage, []);
    await adminPage.GotoAsync(SignInUrl(adminUrl, $"/app/clients/{host.Fixture.ClientId:D}"));
    await adminPage.GetByText(privateContact, new() { Exact = true }).WaitForAsync();
    await adminConnected;

    foreach (var origin in new[] { host.StaffUrl, unrelatedUrl })
    {
      await using var context = await browser.NewContextAsync();
      var page = await context.NewPageAsync();
      var diagnostics = new List<string>();
      var connected = WaitForCircuitConnectionAsync(page, diagnostics);
      await page.GotoAsync(SignInUrl(origin, $"/app/clients/{host.Fixture.ClientId:D}"));
      await page.GetByRole(AriaRole.Heading, new() { Name = "Client unavailable" }).WaitForAsync();
      await connected;
      var body = await page.Locator("body").InnerTextAsync();
      Assert.DoesNotContain(privateContact, body);
      Assert.DoesNotContain("private-contact@example.test", body);
      Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
    }
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-CLIENT-PORTAL-READ-01")]
  public async Task ClientPortalHidesPbcRequestsOutsideAssignedEngagement()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-CLIENT-PORTAL-READ-01");
    const string siblingMarker = "SYN-PAR-002-PRIVATE-SIBLING-REQUEST";
    await using (var db = host.CreateDbContext())
    {
      var now = DateTimeOffset.UtcNow;
      var siblingEngagementId = Guid.NewGuid();
      db.Engagements.Add(new Engagement
      {
        Id = siblingEngagementId, FirmId = host.Fixture.FirmId, PracticeClientId = host.Fixture.ClientId,
        ServiceRoute = "SYNTHETIC-SIBLING-SERVICE", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = now
      });
      db.PbcRequests.Add(new PbcRequest
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = siblingEngagementId, Objective = siblingMarker, EntityScope = "Synthetic sibling",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Area = siblingMarker,
        RequestedFormat = "PDF", ClientOwnerUserId = host.Fixture.Client.Id,
        FirmOwnerUserId = host.Fixture.Staff.Id, ReviewerUserId = host.Fixture.Reviewer.Id,
        DueDate = "2026-10-01", Confidentiality = "Confidential", AcceptanceCriteria = siblingMarker,
        State = PbcStates.Sent, CreatedAt = now, CreatedByUserId = host.Fixture.Staff.Id, UpdatedAt = now
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.ClientUrl, "/portal"));
    await page.GetByText("Cash", new() { Exact = true }).WaitForAsync();
    await connected;

    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(siblingMarker, body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-PBC-01")]
  public async Task RevokedClientGrantClearsOpenRequestAfterNextCommand()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-PBC-01");
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);

    await page.GotoAsync(SignInUrl(host.ClientUrl, $"/portal/requests/{host.RequestId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "PBC request" }).WaitForAsync();
    await connected;
    Assert.Contains("Bank statements", await page.Locator("body").InnerTextAsync());
    await page.GetByLabel("Reply to the assigned auditor or accountant").FillAsync("Synthetic reply after revocation");

    await using (var db = host.CreateDbContext())
    {
      var revokedAt = DateTimeOffset.UtcNow;
      var changed = await db.RoleGrants.Where(x => x.FirmId == host.Fixture.FirmId &&
          x.UserId == host.Fixture.Client.Id && x.Role == "ClientUser" && x.RevokedAt == null)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, revokedAt));
      Assert.Equal(1, changed);
    }

    Assert.Contains("Bank statements", await page.Locator("body").InnerTextAsync());
    try
    {
      await page.GetByRole(AriaRole.Button, new() { Name = "Send reply" }).ClickAsync(new() { Timeout = 10000 });
    }
    catch (TimeoutException ex)
    {
      if (!await page.GetByRole(AriaRole.Heading, new() { Name = "Request unavailable" }).IsVisibleAsync())
        throw new Xunit.Sdk.XunitException($"Reply action did not complete.\n{await page.Locator("body").InnerTextAsync()}\n{string.Join("\n", diagnostics)}\n{ex.Message}");
    }
    await page.GetByRole(AriaRole.Heading, new() { Name = "Request unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("Bank statements", body);
    Assert.DoesNotContain("Synthetic reply after revocation", body);
    Assert.DoesNotContain("Cash", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
    await using var verify = host.CreateDbContext();
    Assert.False(await verify.PbcCommunications.AnyAsync(x => x.FirmId == host.Fixture.FirmId &&
      x.PbcRequestId == host.RequestId && x.Body == "Synthetic reply after revocation"));
  }

  [Fact]
  [Trait("CaseId", "PROP-E2E-02")]
  public async Task UnrelatedClientCannotViewSiblingPbcRequestOrItsDescription()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "PROP-E2E-02");
    var unrelatedClientUrl = await host.StartUnrelatedClientWebAsync();
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);

    await page.GotoAsync(SignInUrl(unrelatedClientUrl, $"/portal/requests/{host.RequestId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Request unavailable" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    var body = await page.Locator("body").InnerTextAsync();
    Assert.Contains("This request is not available in the current client scope.", body);
    Assert.DoesNotContain("Bank statements", body);
    Assert.DoesNotContain("Cash", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
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
    return connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
  }
}
