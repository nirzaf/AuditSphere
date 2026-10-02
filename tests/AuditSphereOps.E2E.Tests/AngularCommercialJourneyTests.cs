using System.Text.RegularExpressions;
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
  [Trait("CaseId", "ANGULAR-COMMERCIAL-E2E-01")]
  public async Task QuotationPreview_Save_AndApprovalUsePersistedServerState()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-COMMERCIAL-E2E-01");
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Admin,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
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
}
