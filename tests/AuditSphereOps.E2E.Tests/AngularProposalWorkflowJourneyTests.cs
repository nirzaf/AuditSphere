using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularProposalWorkflowJourneyTests
{
  [Fact]
  [Trait("CaseId", "STE-GAP-008-PROPOSAL-DISPATCH-STAGE3")]
  public async Task ProposalDispatchMovesEngagementLifecycleFromProposalGenerationToDualKeyPending()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "STE-GAP-008-PROPOSAL-DISPATCH-STAGE3");
    var fixture = host.Fixture;
    var partner = fixture.Reviewer;
    var now = DateTimeOffset.UtcNow;
    var leadId = Guid.NewGuid();
    var opportunityId = Guid.NewGuid();
    var proposalId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      var engagement = await db.Engagements.SingleAsync(x => x.Id == fixture.EngagementId);
      engagement.ServiceRoute = "FinancialStatementAudit";
      engagement.PeriodStart = "2026-01-01";
      engagement.PeriodEnd = "2026-12-31";
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, UserId = partner.Id,
        Role = "Partner", GrantedAt = now, GrantedByUserId = fixture.Admin.Id
      });
      db.Leads.Add(new Lead
      {
        Id = leadId, FirmId = fixture.FirmId, Name = "Synthetic lifecycle proposal", Source = "Referral",
        PrimaryContactName = "Synthetic contact", PrimaryContactEmail = "lifecycle@example.test",
        Status = CrmStates.LeadQualified, CreatedAt = now
      });
      db.Opportunities.Add(new Opportunity
      {
        Id = opportunityId, FirmId = fixture.FirmId, LeadId = leadId,
        PracticeClientId = fixture.ClientId, ServiceRoute = "FinancialStatementAudit",
        EntityScope = "Synthetic lifecycle entity", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        ExpectedFee = 1200m, Currency = "QAR", Stage = CrmStates.OpportunityNegotiation, CreatedAt = now
      });
      db.Proposals.Add(new Proposal
      {
        Id = proposalId, FirmId = fixture.FirmId, OpportunityId = opportunityId, PracticeClientId = fixture.ClientId,
        Revision = 1, Status = CrmStates.ProposalInternalReview, ServiceProfileId = "SYNTHETIC-STE-2026",
        Scope = "Synthetic scope", Exclusions = "", Deliverables = "Synthetic report", Dependencies = "",
        Fee = 1200m, Currency = "QAR", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        PreparedByUserId = fixture.Admin.Id, ApprovedByUserId = partner.Id, ApprovedAt = now, CreatedAt = now
      });
      var quotationId = Guid.NewGuid();
      db.QuotationVersions.Add(new QuotationVersion
      {
        Id = quotationId, FirmId = fixture.FirmId, ProposalId = proposalId, Revision = 1, Currency = "QAR",
        LinesJson = "[]", ComplexityFactor = 1m, BaseAmount = 1200m, Fee = 1200m,
        InputHash = Hashing.Sha256Hex("synthetic-quotation-workflow-1"),
        Status = QuotationStates.Approved, RequiredApprovalsJson = "[]", CreatedByUserId = fixture.Admin.Id,
        CreatedAt = now, ApprovedAt = now
      });
      var bytes = System.Text.Encoding.UTF8.GetBytes("Synthetic quotation artifact 1");
      db.CommercialDocuments.Add(new CommercialDocument
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, ProposalId = proposalId, QuotationVersionId = quotationId,
        Kind = CommercialDocumentKinds.Quotation, FileName = "Quotation.txt", ContentType = "text/plain",
        TemplateVersion = "v1", Bytes = bytes, Sha256Hex = Hashing.Sha256Hex(bytes),
        CreatedByUserId = fixture.Admin.Id, CreatedAt = now
      });
      var contactId = Guid.NewGuid();
      db.ClientContacts.Add(new ClientContact
      {
        Id = contactId, FirmId = fixture.FirmId, PracticeClientId = fixture.ClientId,
        FullName = "Synthetic contact", Email = "lifecycle@example.test", Role = "ClientCommercial",
        IsActive = true
      });
      db.ClientContactRoutings.Add(new ClientContactRouting
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, PracticeClientId = fixture.ClientId,
        ClientContactId = contactId, Purpose = CorrespondencePurposes.Commercial,
        IsPrimaryForPurpose = true, CreatedByUserId = fixture.Admin.Id, CreatedAt = now
      });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(partner, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);

    var engagementPath = $"/app/engagements/{fixture.EngagementId:D}";
    var lifecycleUrl = $"/api/ui/engagements/{fixture.EngagementId:D}/lifecycle";
    var (stage2Status, stage2Body) = await PlaywrightResponseCapture.RunAsync(page,
      request => request.Method == "GET" && Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) && uri.AbsolutePath == lifecycleUrl,
      async () =>
      {
        await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(engagementPath));
        await page.GetByText("Canonical Engagement Lifecycle", new() { Exact = true }).ScrollIntoViewIfNeededAsync();
      });
    Assert.Equal(200, stage2Status);
    using (var body = JsonDocument.Parse(stage2Body))
      Assert.Equal(2, body.RootElement.GetProperty("summary").GetProperty("stageIndex").GetInt32());
    var lifecycle = page.GetByRole(AriaRole.Region, new() { Name = "Engagement lifecycle progression", Exact = true });
    await Assertions.Expect(lifecycle).ToContainTextAsync("Stage 2 of 11: Proposal Generation");

    await page.GotoAsync(origin + $"/app/practice/proposals/{proposalId:D}");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Proposal", Exact = true })).ToBeVisibleAsync();
    var assent = page.GetByLabel(
      "I reviewed this revision and confirm the selected commercial action.", new() { Exact = true });
    await assent.CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Mark as sent", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("SENT · Revision 1", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText(
      "Email queued for lifecycle@example.test; awaiting the isolated mail worker.", new() { Exact = true })).ToBeVisibleAsync();

    var (stage3Status, stage3Body) = await PlaywrightResponseCapture.RunAsync(page,
      request => request.Method == "GET" && Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) && uri.AbsolutePath == lifecycleUrl,
      async () =>
      {
        await page.GotoAsync(origin + engagementPath);
        await page.GetByText("Canonical Engagement Lifecycle", new() { Exact = true }).ScrollIntoViewIfNeededAsync();
      });
    Assert.Equal(200, stage3Status);
    using (var body = JsonDocument.Parse(stage3Body))
      Assert.Equal(3, body.RootElement.GetProperty("summary").GetProperty("stageIndex").GetInt32());
    await Assertions.Expect(lifecycle).ToContainTextAsync("Stage 3 of 11: Dual-Key Acceptance Pending");
    await Assertions.Expect(lifecycle).ToContainTextAsync("Client commercial acceptance of proposal required");
    await using (var db = host.CreateDbContext())
    {
      var proposal = await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId);
      Assert.Equal(CrmStates.ProposalSent, proposal.Status);
      Assert.NotNull(proposal.SentAt);
      Assert.False(string.IsNullOrWhiteSpace(proposal.SentOfferSha256));
    }
    Assert.Empty(errors);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  [Trait("CaseId", "AS-PAR-002-ANG-COMM-PROPOSAL-WORKFLOW-01")]
  public async Task ProposalWorkflowRequiresIndependentReviewAndRecordsSendAndClientResponse(bool accepted)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-COMM-PROPOSAL-WORKFLOW-01-" + (accepted ? "ACCEPT" : "DECLINE"));
    var fixture = host.Fixture;
    Guid proposalId;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, UserId = fixture.Reviewer.Id,
        Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Admin.Id,
      });
      await db.SaveChangesAsync();

      var author = PbcSeed.Actor(fixture.Admin, "Administrator");
      var lead = await PracticeCrmService.CreateLeadAsync(db, author,
        new("Synthetic proposal workflow", "Referral", "Synthetic contact", "workflow@example.test"));
      Assert.True(lead.Succeeded, lead.Message);
      Assert.True((await PracticeCrmService.QualifyLeadAsync(db, author, lead.Value)).Succeeded);
      var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, author,
        new(lead.Value, "FinancialStatementAudit", "Synthetic entity", "2026-01-01", "2026-12-31", 1200m, "QAR"));
      Assert.True(opportunity.Succeeded, opportunity.Message);
      var proposal = await PracticeCrmService.ReviseProposalAsync(db, author,
        new(opportunity.Value, "Standard", "Synthetic scope", "", "Synthetic deliverables", "", 1200m,
          "QAR", "2026-01-01", "2026-12-31", 0));
      Assert.True(proposal.Succeeded, proposal.Message);
      proposalId = proposal.Value;
      var quotationId = Guid.NewGuid();
      var now = DateTimeOffset.UtcNow;
      db.QuotationVersions.Add(new QuotationVersion
      {
        Id = quotationId, FirmId = fixture.FirmId, ProposalId = proposalId, Revision = 1, Currency = "QAR",
        LinesJson = "[]", ComplexityFactor = 1m, BaseAmount = 1200m, Fee = 1200m,
        InputHash = Hashing.Sha256Hex("synthetic-quotation-workflow-2"),
        Status = QuotationStates.Approved, RequiredApprovalsJson = "[]", CreatedByUserId = fixture.Admin.Id,
        CreatedAt = now, ApprovedAt = now
      });
      var bytes = System.Text.Encoding.UTF8.GetBytes("Synthetic quotation artifact 2");
      db.CommercialDocuments.Add(new CommercialDocument
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, ProposalId = proposalId, QuotationVersionId = quotationId,
        Kind = CommercialDocumentKinds.Quotation, FileName = "Quotation.txt", ContentType = "text/plain",
        TemplateVersion = "v1", Bytes = bytes, Sha256Hex = Hashing.Sha256Hex(bytes),
        CreatedByUserId = fixture.Admin.Id, CreatedAt = now
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };

    var authorOrigin = await host.StartApiForIdentityAsync(fixture.Admin, settings);
    await using (var authorContext = await browser.NewContextAsync())
    {
      var authorPage = await authorContext.NewPageAsync();
      await authorPage.GotoAsync(authorOrigin + "/auth/sign-in?returnUrl=" +
        Uri.EscapeDataString($"/ui/app/practice/proposals/{proposalId:D}"));
      await Assertions.Expect(authorPage.GetByRole(AriaRole.Heading,
        new() { Name = "Proposal", Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(authorPage.GetByText(
        "An independent commercial reviewer must review your proposal.", new() { Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(authorPage.GetByRole(AriaRole.Button,
        new() { Name = "Submit for independent internal review", Exact = true })).ToHaveCountAsync(0);
    }

    var reviewerOrigin = await host.StartApiForIdentityAsync(fixture.Reviewer, settings);
    await using var reviewerContext = await browser.NewContextAsync();
    var page = await reviewerContext.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString($"/ui/app/practice/proposals/{proposalId:D}"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Proposal", Exact = true })).ToBeVisibleAsync();

    var assent = page.GetByLabel(
      "I reviewed this revision and confirm the selected commercial action.", new() { Exact = true });
    await assent.CheckAsync();
    await page.GetByRole(AriaRole.Button,
      new() { Name = "Submit for independent internal review", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("INTERNAL_REVIEW · Revision 1", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText(
      "Sending queues exactly one durable email bound to the exact dispatched offer identity; its delivery state is tracked by the mail worker below. A commercial acceptance does not activate professional work.",
      new() { Exact = true })).ToBeVisibleAsync();

    await assent.CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Mark as sent", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("SENT · Revision 1", new() { Exact = true })).ToBeVisibleAsync();
    // The dispatch evidence is visible before any response is recorded: one queued email bound to the offer.
    await Assertions.Expect(page.GetByText("Offer dispatch", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("Email queued for workflow@example.test; awaiting the isolated mail worker.",
      new() { Exact = true })).ToBeVisibleAsync();

    const string declineReason = "The client declined the synthetic proposal.";
    var decision = accepted ? "ACCEPTED" : "DECLINED";
    if (accepted)
    {
      await page.GetByLabel("Respondent name", new() { Exact = true }).FillAsync("Synthetic contact");
      await page.GetByLabel("Respondent email", new() { Exact = true }).FillAsync("workflow@example.test");
      await page.GetByLabel("Evidence reference", new() { Exact = true }).FillAsync("Signed acceptance letter received by email");
      await page.GetByLabel("Response channel", new() { Exact = true }).SelectOptionAsync("EMAIL");
      await assent.CheckAsync();
      await page.GetByRole(AriaRole.Button, new() { Name = "Record client acceptance", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Link,
        new() { Name = "Review prospect-to-client conversion", Exact = true })).ToBeVisibleAsync();
    }
    else
    {
      await page.GetByLabel("Respondent name", new() { Exact = true }).FillAsync("Synthetic contact");
      await page.GetByLabel("Respondent email", new() { Exact = true }).FillAsync("workflow@example.test");
      await page.GetByLabel("Client response reason", new() { Exact = true }).FillAsync(declineReason);
      await assent.CheckAsync();
      await page.GetByRole(AriaRole.Button, new() { Name = "Record client decline", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByText(declineReason, new() { Exact = true })).ToBeVisibleAsync();
    }
    await Assertions.Expect(page.GetByText($"{decision} · Revision 1", new() { Exact = true })).ToBeVisibleAsync();

    await using (var db = host.CreateDbContext())
    {
      var proposal = await db.Proposals.SingleAsync(x => x.Id == proposalId);
      Assert.Equal(decision, proposal.Status);
      Assert.Equal(fixture.Reviewer.Id, proposal.ApprovedByUserId);
      Assert.NotNull(proposal.ApprovedAt);
      Assert.NotNull(proposal.SentAt);
      // The response is bound to the exact dispatched offer identity with respondent evidence (STE-REM-01).
      Assert.False(string.IsNullOrWhiteSpace(proposal.SentOfferSha256));
      Assert.Equal(proposal.SentOfferSha256, proposal.ResponseOfferSha256);
      Assert.Equal("Synthetic contact", proposal.RespondentName);
      Assert.Equal(accepted ? "Signed acceptance letter received by email" : null, proposal.ResponseEvidenceReference);
      Assert.NotNull(proposal.ResponseAt);
      Assert.Equal(accepted ? null : declineReason, proposal.ResponseReason);
      var opportunity = await db.Opportunities.SingleAsync(x => x.Id == proposal.OpportunityId);
      Assert.Equal(accepted ? "WON" : "LOST", opportunity.Stage);
      Assert.Null(opportunity.PracticeClientId);
    }
    Assert.Empty(errors);
  }
}
