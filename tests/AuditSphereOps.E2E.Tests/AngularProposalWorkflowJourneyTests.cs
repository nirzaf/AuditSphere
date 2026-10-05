using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularProposalWorkflowJourneyTests
{
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
        new("Synthetic proposal workflow", "Review journey", "Synthetic contact", "workflow@example.test"));
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
    await Assertions.Expect(page.GetByText("Marking sent records status only. No email is sent here. A commercial acceptance does not activate professional work.",
      new() { Exact = true })).ToBeVisibleAsync();

    await assent.CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Mark as sent", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("SENT · Revision 1", new() { Exact = true })).ToBeVisibleAsync();

    const string declineReason = "The client declined the synthetic proposal.";
    var decision = accepted ? "ACCEPTED" : "DECLINED";
    if (accepted)
    {
      await assent.CheckAsync();
      await page.GetByRole(AriaRole.Button, new() { Name = "Record client acceptance", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Link,
        new() { Name = "Review prospect-to-client conversion", Exact = true })).ToBeVisibleAsync();
    }
    else
    {
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
      Assert.NotNull(proposal.ResponseAt);
      Assert.Equal(accepted ? null : declineReason, proposal.ResponseReason);
      var opportunity = await db.Opportunities.SingleAsync(x => x.Id == proposal.OpportunityId);
      Assert.Equal(accepted ? "WON" : "LOST", opportunity.Stage);
      Assert.Null(opportunity.PracticeClientId);
    }
    Assert.Empty(errors);
  }
}
