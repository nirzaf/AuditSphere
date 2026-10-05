using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// Package 1 in the real UI: a Partner prices a proposal from approved rate cards, approves the calculated quotation,
/// is blocked from generating documents until the firm letterhead exists, then downloads the branded documents. The
/// download is refused for a client identity, and the proposal moves to internal review only after the quotation gate.
/// </summary>
[Trait("Category", "AuthorizationAndScope")]
public sealed class CommercialJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-COMMERCIAL-QUOTE-01")]
  public async Task PartnerPricesApprovesAndDownloadsBrandedDocuments_AndClientsCannot()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-COMMERCIAL-QUOTE-01");
    var admin = PbcSeed.Actor(host.Fixture.Admin, "Administrator");
    var partner = PbcSeed.User(host.Fixture.FirmId, "Staff");
    Guid proposalId;
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(partner);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, partner, "Partner"));
      db.RateCardVersions.Add(new RateCardVersion
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, Version = 1, Role = "Partner", Activity = "Audit", Currency = "QAR", RatePerHour = 1000m,
        Status = PracticeTimeStates.RateApproved, CreatedByUserId = host.Fixture.Admin.Id, ApprovedByUserId = partner.Id,
        ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var lead = await PracticeCrmService.CreateLeadAsync(db, admin, new CreateLeadRequest("Journey Prospect LLC", "Referral", "J. Contact", "j@prospect.example.test"));
      Assert.True((await PracticeCrmService.QualifyLeadAsync(db, admin, lead.Value)).Succeeded);
      var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, admin,
        new CreateOpportunityRequest(lead.Value, "FinancialStatementAudit", "JOURNEY", "2026-01-01", "2026-12-31", 10000m, "QAR"));
      var proposal = await PracticeCrmService.ReviseProposalAsync(db, admin, new ReviseProposalRequest(opportunity.Value, "AUDIT-2026",
        "Statutory audit", "Tax advisory", "Independent auditor's report", "Trial balance supplied by the client", 1m, "QAR", "2026-01-01", "2026-12-31"));
      Assert.True(proposal.Succeeded, proposal.Message);
      proposalId = proposal.Value;
    }

    var origin = await host.StartApiForIdentityAsync(partner);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    page.Console += (_, message) => diagnostics.Add($"console-{message.Type}: {message.Text}");
    await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString($"/app/practice/proposals/{proposalId:D}")}");
    await page.GetByText("Quotation pricing and approvals").ScrollIntoViewIfNeededAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Calculated quotation" }).WaitForAsync(new() { Timeout = 20000 });
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // 10 partner hours at the approved 1,000/h rate, previewed by the same calculator the server uses.
    var quotation = page.Locator("audit-quotation");
    var hours = quotation.GetByRole(AriaRole.Textbox, new() { Name = "Hours 1", Exact = true });
    await hours.FillAsync("10");
    await quotation.GetByRole(AriaRole.Button, new() { Name = "Calculate preview", Exact = true }).ClickAsync();
    await Assertions.Expect(quotation.GetByRole(AriaRole.Status).First).ToContainTextAsync("10000");
    await quotation.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed these inputs and the server-calculated total.", Exact = true }).CheckAsync();
    await quotation.GetByRole(AriaRole.Button, new() { Name = "Save quotation version", Exact = true }).ClickAsync();
    await Assertions.Expect(quotation.GetByRole(AriaRole.Heading, new() { Name = "Revision 1 · DRAFT", Exact = false })).ToBeVisibleAsync();
    await quotation.GetByRole(AriaRole.Button, new() { Name = "Approve quotation (no matrix approval required)", Exact = true }).ClickAsync();
    await Assertions.Expect(quotation.GetByRole(AriaRole.Heading, new() { Name = "Revision 1 · APPROVED", Exact = false })).ToBeVisibleAsync();

    // The current Angular workspace explains and disables document generation until a profile exists.
    var deferredDocumentAnchor = page.Locator("#proposal-fee-agreement");
    await deferredDocumentAnchor.ScrollIntoViewIfNeededAsync();
    var documents = page.Locator("audit-commercial-documents");
    await Assertions.Expect(documents.GetByText("Configure the firm commercial profile before generating documents.", new() { Exact = true }).First).ToBeVisibleAsync();
    await Assertions.Expect(documents.GetByRole(AriaRole.Button, new() { Name = "Generate brief quotation", Exact = true })).ToBeDisabledAsync();

    await page.GotoAsync($"{origin}/app/practice/commercial-settings");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Commercial settings" }).First.WaitForAsync();
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 });
    await page.WaitForTimeoutAsync(500); // let the interactive circuit attach before typing
    await page.GetByLabel("Legal name").FillAsync("Journey Audit Partners");
    await page.GetByLabel("Address").FillAsync("West Bay, Doha");
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this firm profile and confirm the new version.", Exact = true }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Save letterhead version", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Settings recorded. Existing documents and quotation approvals retain their original identities.", new() { Exact = true })).ToBeVisibleAsync();

    await page.GotoAsync($"{origin}/app/practice/proposals/{proposalId:D}");
    deferredDocumentAnchor = page.Locator("#proposal-fee-agreement");
    await deferredDocumentAnchor.ScrollIntoViewIfNeededAsync();
    documents = page.Locator("audit-commercial-documents");
    await Assertions.Expect(documents.GetByRole(AriaRole.Heading, new() { Name = "Commercial documents", Exact = true })).ToBeVisibleAsync();
    await documents.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this quotation, firm profile and document action.", Exact = true }).CheckAsync();
    await documents.GetByRole(AriaRole.Button, new() { Name = "Generate brief quotation", Exact = true }).ClickAsync();
    await Assertions.Expect(documents.GetByText("Immutable document available. An existing document is reused for the same quotation.", new() { Exact = true })).ToBeVisibleAsync();
    var quotationLink = documents.GetByRole(AriaRole.Link, new() { NameRegex = new System.Text.RegularExpressions.Regex(@"^Quotation-.*\.docx$") });
    await quotationLink.WaitForAsync(new() { Timeout = 15000 });
    await Assertions.Expect(documents.GetByText("Record client commercial acceptance and convert the proposal to a prospect client.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(documents.GetByRole(AriaRole.Button, new() { Name = "Generate engagement letter", Exact = true })).ToBeDisabledAsync();
    await Assertions.Expect(documents.GetByRole(AriaRole.Link, new() { NameRegex = new System.Text.RegularExpressions.Regex(@"^EngagementLetter-.*\.docx$") })).ToHaveCountAsync(0);

    var href = await quotationLink.GetAttributeAsync("href");
    var download = await context.APIRequest.GetAsync(origin + href!, new() { MaxRedirects = 0 });
    Assert.Equal(200, download.Status);
    Assert.Contains("wordprocessingml", download.Headers["content-type"]);
    Assert.Equal("nosniff", download.Headers["x-content-type-options"]);
    Assert.Equal("no-store", download.Headers["cache-control"]);
    var bytes = await download.BodyAsync();
    Assert.Equal((byte)'P', bytes[0]);
    Assert.Equal((byte)'K', bytes[1]);

    // A client identity (and an unknown identifier) is refused with no document bytes.
    await using var clientContext = await browser.NewContextAsync();
    var clientPage = await clientContext.NewPageAsync();
    await clientPage.GotoAsync($"{host.ClientUrl}/auth/sign-in?returnUrl={Uri.EscapeDataString("/portal")}");
    await clientPage.GetByRole(AriaRole.Heading, new() { Name = "Client portal" }).First.WaitForAsync(new() { Timeout = 15000 });
    var clientDownload = await clientContext.APIRequest.GetAsync(host.ClientUrl + href, new() { MaxRedirects = 0 });
    Assert.NotEqual(200, clientDownload.Status);
    Assert.DoesNotContain("PK", (await clientDownload.TextAsync()).Take(2).Aggregate("", (a, c) => a + c));
    var unknown = await context.APIRequest.GetAsync($"{origin}/api/commercial/documents/{Guid.NewGuid():D}/download", new() { MaxRedirects = 0 });
    Assert.NotEqual(200, unknown.Status);

    // The proposal now passes the quotation gate for independent internal review, then can be marked sent.
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this revision and confirm the selected commercial action.", Exact = true }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Submit for independent internal review", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Status).Last).ToContainTextAsync("Commercial action recorded.");

    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
    Assert.DoesNotContain(diagnostics, x => x.Contains("unhandled exception on the current circuit", StringComparison.OrdinalIgnoreCase));
  }
}
