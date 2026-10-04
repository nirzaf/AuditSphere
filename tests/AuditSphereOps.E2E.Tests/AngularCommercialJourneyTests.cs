using System.Text.RegularExpressions;
using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularCommercialJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-COMMERCIAL-LEAD-RECOVERY-01")]
  public async Task LostLeadCreateResponse_ReloadResolvesSameRequestWithoutDuplicate()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "ANGULAR-COMMERCIAL-LEAD-RECOVERY-01");
    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    var buildPath = Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_UI_BUILD_PATH");
    if (!string.IsNullOrWhiteSpace(buildPath)) settings["AngularUi__BuildPath"] = buildPath;
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Admin, settings);

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    var requestIdentity = Guid.Empty;
    var postCount = 0;
    await page.RouteAsync("**/api/ui/leads", async route =>
    {
      if (route.Request.Method != "POST") { await route.ContinueAsync(); return; }
      using var body = JsonDocument.Parse(route.Request.PostData!);
      var seenIdentity = body.RootElement.GetProperty("requestId").GetGuid();
      if (Interlocked.Increment(ref postCount) == 1)
      {
        requestIdentity = seenIdentity;
        await using var accepted = await route.FetchAsync();
        Assert.Equal(200, accepted.Status);
        await route.AbortAsync("failed");
      }
      else
      {
        Assert.Equal(requestIdentity, seenIdentity);
        await route.ContinueAsync();
      }
    });

    var routePath = "/ui/app/practice/leads";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(routePath));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Practice leads", Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Lead name", Exact = true }).FillAsync("Synthetic lost-response lead");
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Source", Exact = true }).FillAsync("Recovery journey");
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Contact name", Exact = true }).FillAsync("Synthetic contact");
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Contact email", Exact = true }).FillAsync("recovery@example.test");
    await page.GetByRole(AriaRole.Button, new() { Name = "Record lead", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Lead outcome unconfirmed", new() { Exact = false })).ToBeVisibleAsync();
    Assert.NotEqual(Guid.Empty, requestIdentity);

    await page.ReloadAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Practice leads", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button,
      new() { Name = "Resolve saved lead request", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Textbox,
      new() { Name = "Lead name", Exact = true })).ToHaveValueAsync("Synthetic lost-response lead");
    await page.GetByRole(AriaRole.Button, new() { Name = "Resolve saved lead request", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Lead recorded.", new() { Exact = false })).ToBeVisibleAsync();
    Assert.Equal(2, postCount);

    await using (var db = host.CreateDbContext())
    {
      var lead = await db.Leads.SingleAsync(x => x.Id == requestIdentity);
      Assert.Equal("Synthetic lost-response lead", lead.Name);
      Assert.Equal("recovery@example.test", lead.PrimaryContactEmail);
      Assert.Equal(1, await db.Leads.CountAsync(x => x.Name == "Synthetic lost-response lead"));
    }
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "ANGULAR-COMMERCIAL-OPPORTUNITY-RECOVERY-01")]
  public async Task LostOpportunityCreateResponses_ReconcileCommitAndRetryUncommittedRequest()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "ANGULAR-COMMERCIAL-OPPORTUNITY-RECOVERY-01");
    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    var buildPath = Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_UI_BUILD_PATH");
    if (!string.IsNullOrWhiteSpace(buildPath)) settings["AngularUi__BuildPath"] = buildPath;
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Admin, settings);
    Guid leadId;
    await using (var db = host.CreateDbContext())
    {
      var actor = PbcSeed.Actor(host.Fixture.Admin, "Administrator");
      var lead = await PracticeCrmService.CreateLeadAsync(db, actor,
        new("Synthetic opportunity recovery lead", "Recovery journey"));
      Assert.True(lead.Succeeded, lead.Message);
      Assert.True((await PracticeCrmService.QualifyLeadAsync(db, actor, lead.Value)).Succeeded);
      leadId = lead.Value;
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    var requestIdentity = Guid.Empty;
    var retryIdentity = Guid.Empty;
    var postCount = 0;
    await page.RouteAsync($"**/api/ui/leads/{leadId}/opportunities", async route =>
    {
      if (route.Request.Method != "POST") { await route.ContinueAsync(); return; }
      using var body = JsonDocument.Parse(route.Request.PostData!);
      var seenIdentity = body.RootElement.GetProperty("requestId").GetGuid();
      var call = Interlocked.Increment(ref postCount);
      if (call == 1)
      {
        requestIdentity = seenIdentity;
        await using var accepted = await route.FetchAsync();
        Assert.Equal(200, accepted.Status);
        await route.AbortAsync("failed");
      }
      else if (call == 2)
      {
        retryIdentity = seenIdentity;
        await route.AbortAsync("failed");
      }
      else
      {
        Assert.Equal(retryIdentity, seenIdentity);
        await route.ContinueAsync();
      }
    });

    var routePath = $"/ui/app/practice/leads/{leadId:D}";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(routePath));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Opportunities", Exact = true })).ToBeVisibleAsync();
    await page.GetByLabel("Service route", new() { Exact = true }).FillAsync("AccountingOnly");
    await page.GetByLabel("Entity scope", new() { Exact = true }).FillAsync("Synthetic entity");
    await page.GetByLabel("Period start", new() { Exact = true }).FillAsync("2026-01-01");
    await page.GetByLabel("Period end", new() { Exact = true }).FillAsync("2026-12-31");
    await page.GetByLabel("Expected fee", new() { Exact = true }).FillAsync("1500.25");
    await page.GetByLabel("Currency", new() { Exact = true }).FillAsync("QAR");
    await page.GetByLabel("I reviewed these discovery terms.", new() { Exact = true }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Record opportunity", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Opportunity outcome unconfirmed", new() { Exact = false })).ToBeVisibleAsync();
    Assert.NotEqual(Guid.Empty, requestIdentity);

    await page.ReloadAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Opportunities", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("Persisted result found.", new() { Exact = false })).ToBeVisibleAsync();
    var recovery = page.GetByRole(AriaRole.Button,
      new() { Name = "Resolve saved opportunity request", Exact = true });
    await Assertions.Expect(recovery).ToHaveCountAsync(0);
    Assert.Equal(1, postCount);

    await page.GetByLabel("Service route", new() { Exact = true }).FillAsync("FinancialStatementAudit");
    await page.GetByLabel("Entity scope", new() { Exact = true }).FillAsync("Second synthetic entity");
    await page.GetByLabel("Expected fee", new() { Exact = true }).FillAsync("2000.00");
    await page.GetByLabel("I reviewed these discovery terms.", new() { Exact = true }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Record opportunity", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Opportunity outcome unconfirmed", new() { Exact = false })).ToBeVisibleAsync();
    Assert.NotEqual(Guid.Empty, retryIdentity);

    await page.ReloadAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Opportunities", Exact = true })).ToBeVisibleAsync();
    recovery = page.GetByRole(AriaRole.Button,
      new() { Name = "Resolve saved opportunity request", Exact = true });
    await Assertions.Expect(recovery).ToBeVisibleAsync();
    await Assertions.Expect(recovery).ToBeDisabledAsync();
    await Assertions.Expect(page.GetByLabel("Entity scope", new() { Exact = true })).ToHaveValueAsync("Second synthetic entity");
    await page.GetByLabel("I reviewed these discovery terms.", new() { Exact = true }).CheckAsync();
    await recovery.ClickAsync();
    await Assertions.Expect(page.GetByText("Opportunity recorded.", new() { Exact = false })).ToBeVisibleAsync();
    Assert.Equal(3, postCount);

    await using (var db = host.CreateDbContext())
    {
      var opportunity = await db.Opportunities.SingleAsync(x => x.Id == requestIdentity);
      Assert.Equal(leadId, opportunity.LeadId);
      Assert.Equal("Synthetic entity", opportunity.EntityScope);
      Assert.Equal(1500.25m, opportunity.ExpectedFee);
      var retry = await db.Opportunities.SingleAsync(x => x.Id == retryIdentity);
      Assert.Equal("Second synthetic entity", retry.EntityScope);
      Assert.Equal(2000m, retry.ExpectedFee);
      Assert.Equal(2, await db.Opportunities.CountAsync(x => x.LeadId == leadId));
    }
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "ANGULAR-COMMERCIAL-PROPOSAL-RECOVERY-01")]
  public async Task LostProposalCreateResponses_ReconcileCommitAndRetrySameRequest()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "ANGULAR-COMMERCIAL-PROPOSAL-RECOVERY-01");
    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    var buildPath = Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_UI_BUILD_PATH");
    if (!string.IsNullOrWhiteSpace(buildPath)) settings["AngularUi__BuildPath"] = buildPath;
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Admin, settings);
    Guid leadId, committedOpportunityId, retryOpportunityId;
    await using (var db = host.CreateDbContext())
    {
      var actor = PbcSeed.Actor(host.Fixture.Admin, "Administrator");
      var lead = await PracticeCrmService.CreateLeadAsync(db, actor,
        new("Synthetic proposal recovery lead", "Recovery journey"));
      Assert.True(lead.Succeeded, lead.Message);
      Assert.True((await PracticeCrmService.QualifyLeadAsync(db, actor, lead.Value)).Succeeded);
      leadId = lead.Value;
      var committed = await PracticeCrmService.CreateOpportunityAsync(db, actor,
        new(leadId, "AccountingOnly", "Committed synthetic entity", "2026-01-01", "2026-12-31", 1000m, "QAR"));
      var retry = await PracticeCrmService.CreateOpportunityAsync(db, actor,
        new(leadId, "FinancialStatementAudit", "Retry synthetic entity", "2026-01-01", "2026-12-31", 2000m, "QAR"));
      Assert.True(committed.Succeeded, committed.Message);
      Assert.True(retry.Succeeded, retry.Message);
      committedOpportunityId = committed.Value;
      retryOpportunityId = retry.Value;
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    var committedRequestId = Guid.Empty;
    var retryRequestId = Guid.Empty;
    var proposalPosts = 0;
    Guid committedTargetOpportunityId = Guid.Empty;
    Guid retryTargetOpportunityId = Guid.Empty;
    await page.RouteAsync("**/api/ui/opportunities/*/proposals", async route =>
    {
      if (route.Request.Method != "POST") { await route.ContinueAsync(); return; }
      using var body = JsonDocument.Parse(route.Request.PostData!);
      var seenIdentity = body.RootElement.GetProperty("requestId").GetGuid();
      var segments = new Uri(route.Request.Url).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
      var opportunityIndex = Array.IndexOf(segments, "opportunities");
      Assert.InRange(opportunityIndex, 0, segments.Length - 2);
      var seenOpportunityId = Guid.Parse(segments[opportunityIndex + 1]);
      var call = Interlocked.Increment(ref proposalPosts);
      if (call == 1)
      {
        committedTargetOpportunityId = seenOpportunityId;
        committedRequestId = seenIdentity;
        await using var accepted = await route.FetchAsync();
        Assert.Equal(200, accepted.Status);
        await route.AbortAsync("failed");
      }
      else if (call == 2)
      {
        retryTargetOpportunityId = seenOpportunityId;
        retryRequestId = seenIdentity;
        await route.AbortAsync("failed");
      }
      else
      {
        Assert.Equal(retryTargetOpportunityId, seenOpportunityId);
        Assert.Equal(retryRequestId, seenIdentity);
        await route.ContinueAsync();
      }
    });

    var routePath = $"/ui/app/practice/leads/{leadId:D}";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(routePath));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Opportunities", Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Prepare initial proposal", Exact = true }).First.ClickAsync();
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Service profile", Exact = true }).FillAsync("Committed recovery profile");
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Deliverables", Exact = true }).FillAsync("Committed recovery report");
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the proposal terms.", Exact = true }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Create initial draft", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Proposal outcome unconfirmed", new() { Exact = false })).ToBeVisibleAsync();
    Assert.NotEqual(Guid.Empty, committedRequestId);

    await page.ReloadAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Proposal", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("Committed recovery profile", new() { Exact = false })).ToBeVisibleAsync();
    Assert.Equal(1, proposalPosts);
    Assert.Contains(committedTargetOpportunityId, new[] { committedOpportunityId, retryOpportunityId });
    await using (var db = host.CreateDbContext())
    {
      var proposal = await db.Proposals.SingleAsync(x => x.OpportunityId == committedTargetOpportunityId);
      Assert.Equal(committedRequestId, proposal.Id);
      Assert.Equal("Committed recovery report", proposal.Deliverables);
    }

    await page.GotoAsync(origin + $"/ui/app/practice/leads/{leadId:D}");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Opportunities", Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Prepare initial proposal", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Service profile", Exact = true }).FillAsync("Retry recovery profile");
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Deliverables", Exact = true }).FillAsync("Retry recovery report");
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the proposal terms.", Exact = true }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Create initial draft", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Proposal outcome unconfirmed", new() { Exact = false })).ToBeVisibleAsync();
    Assert.NotEqual(Guid.Empty, retryRequestId);

    await page.ReloadAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button,
      new() { Name = "Resolve saved proposal request", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Textbox,
      new() { Name = "Service profile", Exact = true })).ToHaveValueAsync("Retry recovery profile");
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the proposal terms.", Exact = true }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Resolve saved proposal request", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Proposal", Exact = true })).ToBeVisibleAsync();
    Assert.Equal(3, proposalPosts);
    Assert.NotEqual(committedTargetOpportunityId, retryTargetOpportunityId);
    Assert.Contains(retryTargetOpportunityId, new[] { committedOpportunityId, retryOpportunityId });
    await using (var db = host.CreateDbContext())
    {
      var proposal = await db.Proposals.SingleAsync(x => x.OpportunityId == retryTargetOpportunityId);
      Assert.Equal(retryRequestId, proposal.Id);
      Assert.Equal("Retry recovery report", proposal.Deliverables);
      Assert.Equal(2, await db.Proposals.CountAsync(x =>
        x.OpportunityId == committedTargetOpportunityId || x.OpportunityId == retryTargetOpportunityId));
    }
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "ANGULAR-COMMERCIAL-E2E-01")]
  public async Task QuotationPreview_Save_AndApprovalUsePersistedServerState()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-COMMERCIAL-E2E-01");
    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    var buildPath = Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_UI_BUILD_PATH");
    if (!string.IsNullOrWhiteSpace(buildPath)) settings["AngularUi__BuildPath"] = buildPath;
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Admin, settings);
    Guid proposalId;
    await using (var db = host.CreateDbContext())
    {
      var actor = PbcSeed.Actor(host.Fixture.Admin, "Administrator");
      var lead = await PracticeCrmService.CreateLeadAsync(db, actor, new("Angular quotation client", "Referral", "Synthetic contact", "contact@example.test"));
      Assert.True(lead.Succeeded, lead.Message);
      Assert.True((await PracticeCrmService.QualifyLeadAsync(db, actor, lead.Value)).Succeeded);
      var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, actor,
        new(lead.Value, "FinancialStatementAudit", "Entity", "2026-01-01", "2026-12-31", 1m, "QAR"));
      Assert.True(opportunity.Succeeded, opportunity.Message);
      var proposal = await PracticeCrmService.ReviseProposalAsync(db, actor,
        new(opportunity.Value, "Standard", "Scope", "", "Report", "", 1m, "QAR", "2026-01-01", "2026-12-31", 0));
      Assert.True(proposal.Succeeded, proposal.Message);
      proposalId = proposal.Value;
      db.RateCardVersions.Add(new RateCardVersion
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, Role = "Manager", Activity = "Audit", Version = 1,
        Currency = "QAR", RatePerHour = 750m, Status = PracticeTimeStates.RateApproved,
        CreatedByUserId = host.Fixture.Admin.Id, ApprovedByUserId = host.Fixture.Reviewer.Id,
        CreatedAt = DateTimeOffset.UtcNow, ApprovedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var approvedRates = await QuotationService.ListRateOptionsAsync(db, actor, "QAR");
      Assert.True(approvedRates.Succeeded, approvedRates.Message);
      Assert.Contains(approvedRates.Value!, rate => rate.Role == "Manager" && rate.Activity == "Audit");
      Assert.True((await CommercialDocumentService.SaveProfileAsync(db, actor,
        new("Synthetic Angular Firm", "Address", "", "", "#0F766E", "Closing", "Reviewed history", "Reviewed credentials", "Reviewed methodology"))).Succeeded);
    }
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/ui/app/practice/proposals/" + proposalId));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Proposal", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Angular quotation client", Exact = true })).ToBeVisibleAsync();
    var placeholder = page.GetByText("Quotation pricing and approvals", new() { Exact = true });
    if (await placeholder.CountAsync() > 0) await placeholder.ScrollIntoViewIfNeededAsync();
    var quotation = page.Locator("audit-quotation");
    await Assertions.Expect(quotation.GetByRole(AriaRole.Heading, new() { Name = "Calculated quotation", Exact = true })).ToBeVisibleAsync();
    await quotation.GetByRole(AriaRole.Textbox, new() { Name = "Hours 1", Exact = true }).FillAsync("10");
    await quotation.GetByRole(AriaRole.Button, new() { Name = "Calculate preview", Exact = true }).ClickAsync();
    await Assertions.Expect(quotation.GetByText("Server preview:", new() { Exact = false })).ToContainTextAsync("7500");
    await quotation.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed these inputs and the server-calculated total.", Exact = true }).CheckAsync();
    await quotation.GetByRole(AriaRole.Button, new() { Name = "Save quotation version", Exact = true }).ClickAsync();
    await Assertions.Expect(page.Locator("audit-proposal > dl").GetByText(new Regex(@"^7500(?:\.0+)? QAR$"))).ToBeVisibleAsync();
    // Saving reloads the parent because its persisted fee changed; scroll the deferred child back into view.
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Proposal", Exact = true })).ToBeVisibleAsync();
    if (await placeholder.CountAsync() > 0) await placeholder.ScrollIntoViewIfNeededAsync();
    await Assertions.Expect(quotation.GetByRole(AriaRole.Button, new() { Name = "Approve quotation (no matrix approval required)", Exact = true })).ToBeVisibleAsync();
    await quotation.GetByRole(AriaRole.Button, new() { Name = "Approve quotation (no matrix approval required)", Exact = true }).ClickAsync();
    await Assertions.Expect(quotation.GetByRole(AriaRole.Heading, new() { Name = "Revision 1 · APPROVED", Exact = false })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      var version = Assert.Single(await db.QuotationVersions.Where(x => x.ProposalId == proposalId).ToListAsync());
      Assert.Equal(7500m, version.Fee);
      Assert.Equal("APPROVED", version.Status);
      Assert.Equal(7500m, (await db.Proposals.SingleAsync(p => p.Id == proposalId)).Fee);
    }
    var documentPlaceholder = page.GetByText("Commercial document generation and downloads", new() { Exact = true });
    if (await documentPlaceholder.CountAsync() > 0) await documentPlaceholder.ScrollIntoViewIfNeededAsync();
    var documents = page.Locator("audit-commercial-documents");
    await Assertions.Expect(documents.GetByRole(AriaRole.Heading, new() { Name = "Commercial documents", Exact = true })).ToBeVisibleAsync();
    await documents.GetByRole(AriaRole.Button, new() { Name = "Refresh document state", Exact = true }).ClickAsync();
    await Assertions.Expect(documents.GetByText("Record client commercial acceptance and convert the proposal to a prospect client.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(documents.GetByRole(AriaRole.Button, new() { Name = "Generate engagement letter", Exact = true })).ToBeDisabledAsync();
    await documents.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this quotation, firm profile and document action.", Exact = true }).CheckAsync();
    await documents.GetByRole(AriaRole.Button, new() { Name = "Generate brief quotation", Exact = true }).ClickAsync();
    var downloadLink = documents.GetByRole(AriaRole.Link, new() { NameRegex = new Regex(@"^Quotation-.*\.docx$") });
    await Assertions.Expect(downloadLink).ToBeVisibleAsync();
    var downloaded = await page.RunAndWaitForDownloadAsync(() => downloadLink.ClickAsync());
    Assert.EndsWith(".docx", downloaded.SuggestedFilename);
    Assert.Null(await downloaded.FailureAsync());
    await using (var db = host.CreateDbContext())
    {
      var artifact = Assert.Single(await db.CommercialDocuments.Where(d => d.ProposalId == proposalId).ToListAsync());
      Assert.Equal("QUOTATION", artifact.Kind);
      Assert.Equal(64, artifact.Sha256Hex.Length);
      Assert.NotEmpty(artifact.Bytes);
    }
    // Move the synthetic fixture through existing commercial commands with an independent reviewer.
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, UserId = host.Fixture.Reviewer.Id,
        Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = host.Fixture.Admin.Id });
      await db.SaveChangesAsync();
      var author = PbcSeed.Actor(host.Fixture.Admin, "Administrator");
      var reviewer = PbcSeed.Actor(host.Fixture.Reviewer, "Partner");
      Assert.True((await PracticeCrmService.ApproveProposalAsync(db, reviewer, proposalId)).Succeeded);
      Assert.True((await PracticeCrmService.SendProposalAsync(db, author, proposalId)).Succeeded);
      Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, author, proposalId, new("ACCEPTED"))).Succeeded);
      var converted = await PracticeCrmService.ConvertToClientDraftAsync(db, author, new(proposalId, "Angular fee client"));
      Assert.True(converted.Succeeded, converted.Message);
    }
    await page.ReloadAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Angular quotation client", Exact = true })).ToBeVisibleAsync();
    var fees = page.Locator("audit-fee-agreement");
    // The container survives the viewport-triggered replacement of its lazy placeholder.
    await page.Locator("#proposal-fee-agreement").ScrollIntoViewIfNeededAsync();
    await Assertions.Expect(fees.GetByRole(AriaRole.Heading, new() { Name = "Agreed fee and billing milestones", Exact = true })).ToBeVisibleAsync();
    await fees.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this fee, milestone and selected action.", Exact = true }).CheckAsync();
    await fees.GetByRole(AriaRole.Button, new() { Name = "Create fee agreement", Exact = true }).ClickAsync();
    await Assertions.Expect(fees.GetByRole(AriaRole.Table, new() { Name = "Persisted fee milestones", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(fees.Locator("tbody tr")).ToHaveCountAsync(2);
    await fees.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this fee, milestone and selected action.", Exact = true }).CheckAsync();
    await Assertions.Expect(fees.GetByText("A current FinanceManager or FinanceReviewer assignment for this client is required for invoice and payment commands.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(fees.GetByRole(AriaRole.Button, new() { Name = "Draft advance invoice", Exact = true })).ToBeDisabledAsync();
    await using (var db = host.CreateDbContext())
    {
      var agreement = Assert.Single(await db.EngagementFeeAgreements.Where(a => a.ProposalId == proposalId).ToListAsync());
      Assert.Equal(7500m, agreement.AgreedFee);
      Assert.Equal(50m, agreement.AdvancePercent);
      Assert.All(await db.FeeMilestones.Where(m => m.AgreementId == agreement.Id).ToListAsync(), m => Assert.Equal(3750m, m.Amount));
    }
    await page.GotoAsync(origin + "/ui/app/practice/commercial-settings");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Commercial settings", Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Legal name", Exact = true }).FillAsync("Synthetic Angular Revised Firm");
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this firm profile and confirm the new version.", Exact = true }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Save letterhead version", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Version 2.", new() { Exact = false })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Threshold %", Exact = true }).FillAsync("5");
    await page.GetByRole(AriaRole.Combobox, new() { Name = "Required approver role", Exact = true }).SelectOptionAsync("Manager");
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the effect on future quotation approvals.", Exact = true }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Save approval rule", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Review deactivation", Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Review deactivation", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Confirm rule deactivation", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Cell, new() { Name = "No configured rules; the safe default applies.", Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      Assert.Equal(2, await db.FirmCommercialProfiles.Where(p => p.FirmId == host.Fixture.FirmId).CountAsync());
      Assert.Equal(0, await db.CommercialApprovalRules.Where(r => r.FirmId == host.Fixture.FirmId && r.Active).CountAsync());
      Assert.Equal(1, (await db.CommercialDocuments.SingleAsync(d => d.ProposalId == proposalId)).ProfileVersion);
    }
    Assert.Empty(errors);
  }
  [Fact]
  [Trait("CaseId", "ANGULAR-COMMERCIAL-DRAFT-E2E-01")]
  public async Task EmbeddedDrafts_RecoverOnlyFields_AfterGuardedNavigation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-COMMERCIAL-DRAFT-E2E-01");
    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    var buildPath = Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_UI_BUILD_PATH");
    if (!string.IsNullOrWhiteSpace(buildPath)) settings["AngularUi__BuildPath"] = buildPath;
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Admin, settings);
    Guid proposalId;
    await using (var db = host.CreateDbContext())
    {
      var actor = PbcSeed.Actor(host.Fixture.Admin, "Administrator");
      var lead = await PracticeCrmService.CreateLeadAsync(db, actor, new("Angular quotation client", "Referral", "Synthetic contact", "contact@example.test"));
      Assert.True(lead.Succeeded, lead.Message);
      Assert.True((await PracticeCrmService.QualifyLeadAsync(db, actor, lead.Value)).Succeeded);
      var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, actor,
        new(lead.Value, "FinancialStatementAudit", "Entity", "2026-01-01", "2026-12-31", 1m, "QAR"));
      Assert.True(opportunity.Succeeded, opportunity.Message);
      var proposal = await PracticeCrmService.ReviseProposalAsync(db, actor,
        new(opportunity.Value, "Standard", "Scope", "", "Report", "", 1m, "QAR", "2026-01-01", "2026-12-31", 0));
      Assert.True(proposal.Succeeded, proposal.Message);
      proposalId = proposal.Value;
      db.RateCardVersions.Add(new RateCardVersion
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, Role = "Manager", Activity = "Audit", Version = 1,
        Currency = "QAR", RatePerHour = 750m, Status = PracticeTimeStates.RateApproved,
        CreatedByUserId = host.Fixture.Admin.Id, ApprovedByUserId = host.Fixture.Reviewer.Id,
        CreatedAt = DateTimeOffset.UtcNow, ApprovedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      Assert.True((await CommercialDocumentService.SaveProfileAsync(db, actor,
        new("Synthetic Angular Firm", "Address", "", "", "#0F766E", "Closing", "Reviewed history", "Reviewed credentials", "Reviewed methodology"))).Succeeded);
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    var url = origin + "/ui/app/practice/proposals/" + proposalId;
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/ui/app/practice/proposals/" + proposalId));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Proposal", Exact = true })).ToBeVisibleAsync();
    var quotationPlaceholder = page.GetByText("Quotation pricing and approvals", new() { Exact = true });
    var quotation = page.Locator("audit-quotation");
    if (await quotation.CountAsync() == 0)
    {
      await quotationPlaceholder.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
      await quotationPlaceholder.ScrollIntoViewIfNeededAsync();
    }
    await quotation.GetByRole(AriaRole.Heading, new() { Name = "Calculated quotation", Exact = true }).WaitForAsync();
    var quotationState = await quotation.InnerTextAsync();
    Assert.DoesNotContain("No approved rates", quotationState);
    Assert.DoesNotContain("Quotation unavailable", quotationState);
    await quotation.GetByRole(AriaRole.Textbox, new() { Name = "Hours 1", Exact = true }).FillAsync("12.25");
    var documentsPlaceholder = page.GetByText("Commercial document generation and downloads", new() { Exact = true });
    var documents = page.Locator("audit-commercial-documents");
    if (await documents.CountAsync() == 0)
    {
      await documentsPlaceholder.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
      await documentsPlaceholder.ScrollIntoViewIfNeededAsync();
    }
    await documents.GetByRole(AriaRole.Textbox, new() { Name = "Assigned Partner and team CVs", Exact = true }).FillAsync("Synthetic reviewed team draft");
    await documents.GetByRole(AriaRole.Textbox, new() { Name = "Deliverables timeline", Exact = true }).FillAsync("Synthetic delivery draft");
    await page.Locator("audit-proposal > a").ClickAsync();
    var dialog = page.GetByRole(AriaRole.Dialog);
    await Assertions.Expect(dialog.GetByRole(AriaRole.Heading, new() { Name = "Unsubmitted edits", Exact = true })).ToBeVisibleAsync();
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Keep editing", Exact = true }).ClickAsync();
    await Assertions.Expect(documents.GetByRole(AriaRole.Textbox, new() { Name = "Deliverables timeline", Exact = true })).ToHaveValueAsync("Synthetic delivery draft");
    await page.Locator("audit-proposal > a").ClickAsync();
    await dialog.GetByRole(AriaRole.Button, new() { Name = "Save draft and continue", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Practice leads", Exact = true })).ToBeVisibleAsync();
    await page.GotoAsync(url);
    if (await quotation.CountAsync() == 0)
    {
      await quotationPlaceholder.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
      await quotationPlaceholder.ScrollIntoViewIfNeededAsync();
    }
    await quotation.GetByRole(AriaRole.Button, new() { Name = "Recover quotation tab draft", Exact = true }).ClickAsync();
    await Assertions.Expect(quotation.GetByRole(AriaRole.Textbox, new() { Name = "Hours 1", Exact = true })).ToHaveValueAsync("12.25");
    await Assertions.Expect(quotation.GetByText("Server preview:", new() { Exact = false })).ToHaveCountAsync(0);
    await quotation.GetByRole(AriaRole.Button, new() { Name = "Calculate preview", Exact = true }).ClickAsync();
    await Assertions.Expect(quotation.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed these inputs and the server-calculated total.", Exact = true })).Not.ToBeCheckedAsync();
    if (await documents.CountAsync() == 0)
    {
      await documentsPlaceholder.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
      await documentsPlaceholder.ScrollIntoViewIfNeededAsync();
    }
    await documents.GetByRole(AriaRole.Button, new() { Name = "Recover document tab draft", Exact = true }).ClickAsync();
    await Assertions.Expect(documents.GetByRole(AriaRole.Textbox, new() { Name = "Deliverables timeline", Exact = true })).ToHaveValueAsync("Synthetic delivery draft");
    await Assertions.Expect(documents.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this quotation, firm profile and document action.", Exact = true })).Not.ToBeCheckedAsync();
    await using (var db = host.CreateDbContext())
    {
      Assert.Empty(await db.QuotationVersions.Where(x => x.ProposalId == proposalId).ToListAsync());
      Assert.Empty(await db.CommercialDocuments.Where(x => x.ProposalId == proposalId).ToListAsync());
    }
    Assert.Empty(errors);
  }

}
