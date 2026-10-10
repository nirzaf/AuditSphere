using System.Text.RegularExpressions;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "STECommercialAcceptance")]
public sealed class AngularSteCommercialAcceptanceJourneyTests
{
  [Fact]
  [Trait("CaseId", "STE-GAP-001-N02-RISK-NO-CLIENT-ACCEPT")]
  public async Task PartnerRiskClearanceWithoutClientCommercialAcceptanceKeepsLetterBlocked()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "STE-GAP-001-N02-RISK-NO-CLIENT-ACCEPT");
    var f = host.Fixture;
    var partner = f.Reviewer;
    var now = DateTimeOffset.UtcNow;
    var leadId = Guid.NewGuid();
    var opportunityId = Guid.NewGuid();
    var proposalId = Guid.NewGuid();
    var quotationId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, partner, "Partner"));
      db.Leads.Add(new Lead
      {
        Id = leadId, FirmId = f.FirmId, Name = "Synthetic pending client acceptance", Source = "Referral",
        PrimaryContactName = "Synthetic signatory", PrimaryContactEmail = "pending@example.test",
        Status = CrmStates.LeadQualified, CreatedAt = now
      });
      db.Opportunities.Add(new Opportunity
      {
        Id = opportunityId, FirmId = f.FirmId, LeadId = leadId, PracticeClientId = f.ClientId,
        ServiceRoute = "FinancialStatementAudit", EntityScope = "Synthetic entity", PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31", ExpectedFee = 20_000m, Currency = "QAR",
        Stage = CrmStates.OpportunityProposal, CreatedAt = now
      });
      db.Proposals.Add(new Proposal
      {
        Id = proposalId, FirmId = f.FirmId, OpportunityId = opportunityId, PracticeClientId = f.ClientId,
        Revision = 1, Status = CrmStates.ProposalDraft, ServiceProfileId = "SYNTHETIC-STE-2026",
        Scope = "Synthetic route-specific engagement", Deliverables = "Synthetic final report",
        Exclusions = "", Dependencies = "", Fee = 20_000m, Currency = "QAR",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", PreparedByUserId = f.Admin.Id,
        CreatedAt = now
      });
      db.QuotationVersions.Add(new QuotationVersion
      {
        Id = quotationId, FirmId = f.FirmId, ProposalId = proposalId, Revision = 1, Currency = "QAR",
        LinesJson = "[]", ComplexityFactor = 1m, BaseAmount = 20_000m, Fee = 20_000m,
        InputHash = Hashing.Sha256Hex("synthetic-pending-client-acceptance-quotation"),
        Status = QuotationStates.Approved, RequiredApprovalsJson = "[]", CreatedByUserId = f.Admin.Id,
        CreatedAt = now, ApprovedAt = now
      });
      db.FirmCommercialProfiles.Add(new FirmCommercialProfile
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, Version = 1, LegalName = "Synthetic Audit Firm",
        Address = "Synthetic address", ContactEmail = "firm@example.test", ContactPhone = "",
        AccentColorHex = "#0F766E", ClosingText = "Reviewed synthetic terms",
        CreatedByUserId = f.Admin.Id, CreatedAt = now
      });
      var safety = await db.ClientSafetyStates.SingleAsync(x => x.Id == f.ClientId);
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Decision = "Accepted", ServiceRoute = "FinancialStatementAudit", Generation = safety.InputGeneration,
        Rationale = "Synthetic current Partner risk clearance; commercial acceptance is intentionally absent.",
        EvaluationTemplateVersion = "synthetic-e2e-v1",
        EvaluationSnapshotDigest = Hashing.Sha256Hex("synthetic-current-risk-clearance"),
        DecidedByUserId = partner.Id, DecidedAt = now
      });
      await db.SaveChangesAsync();
      Assert.True((await AuditDeliverableService.RegisterSignatureAsync(db, PbcSeed.Actor(partner, "Partner"),
        CompletionTestFixtures.Png(100, 60))).Succeeded);
      Assert.True((await AuditDeliverableService.RegisterFirmSealAsync(db, PbcSeed.Actor(partner, "Partner"),
        CompletionTestFixtures.Png(100, 100))).Succeeded);
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
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString($"/app/practice/proposals/{proposalId:D}"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Proposal", Exact = true })).ToBeVisibleAsync();
    await page.Locator("#proposal-fee-agreement").ScrollIntoViewIfNeededAsync();

    var documents = page.Locator("audit-commercial-documents");
    await Assertions.Expect(documents).ToContainTextAsync(
      "Record client commercial acceptance and convert the proposal to a prospect client.");
    await Assertions.Expect(documents.GetByText(
      "Complete the current acceptance checklist and record unconditional Partner risk clearance for this service.",
      new() { Exact = true })).ToHaveCountAsync(0);
    var generateLetter = documents.GetByRole(AriaRole.Button,
      new() { Name = "Generate engagement letter", Exact = true });
    await Assertions.Expect(generateLetter).ToBeDisabledAsync();
    await using (var db = host.CreateDbContext())
    {
      Assert.True(await db.AcceptanceDecisions.AnyAsync(x => x.PracticeClientId == f.ClientId &&
        x.ServiceRoute == "FinancialStatementAudit" && x.Decision == "Accepted"));
      Assert.False(await db.Proposals.AnyAsync(x => x.Id == proposalId && x.ResponseAt != null));
      Assert.False(await db.CommercialDocuments.AnyAsync(x => x.ProposalId == proposalId &&
        x.Kind == CommercialDocumentKinds.EngagementLetter));
    }
    Assert.Empty(errors);
  }

  [Theory]
  [InlineData("FinancialStatementAudit", "EL-STATUTORY-AUDIT-ISA210-v1")]
  [InlineData("InternalAudit", "EL-INTERNAL-AUDIT-v1")]
  [InlineData("AgreedUponProcedures", "EL-AUP-ISRS4400-v1")]
  [Trait("CaseId", "STE-GAP-001-LETTER-AND-ADVANCE-BROWSER")]
  public async Task PartnerGeneratesTheRouteBoundLetterAndReviewsAdvancePreparation(
    string serviceRoute, string expectedTemplate)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "STE-GAP-001-LETTER-" + serviceRoute.ToUpperInvariant());
    var f = host.Fixture;
    var partner = f.Reviewer;
    var now = DateTimeOffset.UtcNow;
    var leadId = Guid.NewGuid();
    var opportunityId = Guid.NewGuid();
    var proposalId = Guid.NewGuid();
    var quotationId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, partner, "Partner"));
      db.Leads.Add(new Lead
      {
        Id = leadId, FirmId = f.FirmId, Name = "Synthetic letter client", Source = "Referral",
        PrimaryContactName = "Synthetic signatory", PrimaryContactEmail = "signatory@example.test",
        Status = CrmStates.LeadQualified, CreatedAt = now
      });
      db.Opportunities.Add(new Opportunity
      {
        Id = opportunityId, FirmId = f.FirmId, LeadId = leadId, PracticeClientId = f.ClientId,
        ServiceRoute = serviceRoute, EntityScope = "Synthetic entity", PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31", ExpectedFee = 20_000m, Currency = "QAR", Stage = CrmStates.OpportunityWon,
        CreatedAt = now
      });
      db.Proposals.Add(new Proposal
      {
        Id = proposalId, FirmId = f.FirmId, OpportunityId = opportunityId, PracticeClientId = f.ClientId,
        Revision = 1, Status = CrmStates.ProposalAccepted, ServiceProfileId = "SYNTHETIC-STE-2026",
        Scope = "Synthetic route-specific engagement", Deliverables = "Synthetic final report",
        Exclusions = "", Dependencies = "", Fee = 20_000m, Currency = "QAR",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", ResponseAt = now,
        ResponseEvidenceReference = "synthetic-accepted-offer", RespondentName = "Synthetic signatory",
        RespondentEmail = "signatory@example.test", CreatedAt = now
      });
      db.QuotationVersions.Add(new QuotationVersion
      {
        Id = quotationId, FirmId = f.FirmId, ProposalId = proposalId, Revision = 1, Currency = "QAR",
        LinesJson = "[]", ComplexityFactor = 1m, BaseAmount = 20_000m, Fee = 20_000m,
        InputHash = Hashing.Sha256Hex("synthetic-approved-quotation:" + serviceRoute),
        Status = QuotationStates.Approved, RequiredApprovalsJson = "[]", CreatedByUserId = f.Admin.Id,
        CreatedAt = now, ApprovedAt = now
      });
      db.FirmCommercialProfiles.Add(new FirmCommercialProfile
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, Version = 1, LegalName = "Synthetic Audit Firm",
        Address = "Synthetic address", ContactEmail = "firm@example.test", ContactPhone = "",
        AccentColorHex = "#0F766E", ClosingText = "Reviewed synthetic terms",
        CreatedByUserId = f.Admin.Id, CreatedAt = now
      });
      var safety = await db.ClientSafetyStates.SingleAsync(x => x.Id == f.ClientId);
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Decision = "Accepted", ServiceRoute = serviceRoute, Generation = safety.InputGeneration,
        Rationale = "Synthetic accepted service-route fixture for a browser journey.",
        EvaluationTemplateVersion = "synthetic-e2e-v1",
        EvaluationSnapshotDigest = Hashing.Sha256Hex("synthetic-acceptance:" + serviceRoute),
        DecidedByUserId = partner.Id, DecidedAt = now
      });
      await db.SaveChangesAsync();
      Assert.True((await AuditDeliverableService.RegisterSignatureAsync(db, PbcSeed.Actor(partner, "Partner"),
        CompletionTestFixtures.Png(100, 60))).Succeeded);
      Assert.True((await AuditDeliverableService.RegisterFirmSealAsync(db, PbcSeed.Actor(partner, "Partner"),
        CompletionTestFixtures.Png(100, 100))).Succeeded);
    }

    var origin = await host.StartApiForIdentityAsync(partner, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AutomaticFeeInvoices:Enabled"] = "false"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    var route = $"/app/practice/proposals/{proposalId:D}";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Proposal", Exact = true })).ToBeVisibleAsync();
    await page.Locator("#proposal-fee-agreement").ScrollIntoViewIfNeededAsync();
    var documents = page.Locator("audit-commercial-documents");
    await Assertions.Expect(documents.GetByRole(AriaRole.Heading,
      new() { Name = "Commercial documents", Exact = true })).ToBeVisibleAsync();
    var generateLetter = documents.GetByRole(AriaRole.Button,
      new() { Name = "Generate engagement letter", Exact = true });
    await Assertions.Expect(generateLetter).ToBeDisabledAsync();
    await documents.GetByRole(AriaRole.Checkbox,
      new() { Name = "I reviewed this quotation, firm profile and document action.", Exact = true }).CheckAsync();
    await Assertions.Expect(generateLetter).ToBeEnabledAsync();
    await generateLetter.ClickAsync();
    await Assertions.Expect(documents.GetByText(
      "Immutable document available. An existing document is reused for the same quotation.", new() { Exact = true }))
      .ToBeVisibleAsync();
    var letterLink = documents.GetByRole(AriaRole.Link,
      new() { NameRegex = new Regex(@"^EngagementLetter-.*\.docx$") });
    await Assertions.Expect(letterLink).ToBeVisibleAsync();
    await Assertions.Expect(documents).ToContainTextAsync(expectedTemplate);

    var fees = page.Locator("audit-fee-agreement");
    await Assertions.Expect(fees.GetByRole(AriaRole.Heading,
      new() { Name = "Agreed fee and billing milestones", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(fees.Locator("[data-testid='advance-preparation']"))
      .ToContainTextAsync("Create the fee agreement to establish the 50% advance milestone.");
    await fees.GetByRole(AriaRole.Checkbox,
      new() { Name = "I reviewed this fee, milestone and selected action.", Exact = true }).CheckAsync();
    await fees.GetByRole(AriaRole.Button,
      new() { Name = "Create fee agreement", Exact = true }).ClickAsync();
    await Assertions.Expect(fees.Locator("[data-testid='advance-preparation']"))
      .ToContainTextAsync("automatic drafting is disabled");
    await Assertions.Expect(fees.GetByRole(AriaRole.Table,
      new() { Name = "Persisted fee milestones", Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      var letter = await db.CommercialDocuments.AsNoTracking().SingleAsync(x =>
        x.ProposalId == proposalId && x.Kind == CommercialDocumentKinds.EngagementLetter);
      Assert.Equal(expectedTemplate, letter.TemplateVersion);
      Assert.Equal(Hashing.Sha256Hex(letter.Bytes), letter.Sha256Hex);
      var agreement = await db.EngagementFeeAgreements.AsNoTracking().SingleAsync(x => x.ProposalId == proposalId);
      Assert.Equal(50m, agreement.AdvancePercent);
      Assert.Equal(2, await db.FeeMilestones.CountAsync(x => x.AgreementId == agreement.Id));
      var advanceState = await FeeAgreementWorkspaceQuery.GetAsync(db, PbcSeed.Actor(partner, "Partner"), proposalId,
        automaticDraftingEnabled: false);
      Assert.True(advanceState.Succeeded, advanceState.Message);
      Assert.Equal(AdvanceInvoicePreparationStates.PendingAutomationDisabled, advanceState.Value!.AdvancePreparation.State);
    }
    Assert.Empty(errors);
  }
}
