using System.Text.Json;
using System.Text.RegularExpressions;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Acceptance;
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
    var financeManager = PbcSeed.User(f.FirmId, "Staff");
    var now = DateTimeOffset.UtcNow;
    var leadId = Guid.NewGuid();
    var opportunityId = Guid.NewGuid();
    var proposalId = Guid.NewGuid();
    var quotationId = Guid.NewGuid();
    var offerHash = Hashing.Sha256Hex("synthetic accepted and dispatched offer:" + serviceRoute);
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(financeManager);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, partner, "Partner"),
        PbcSeed.Grant(f.FirmId, financeManager, "FinanceManager", clientId: f.ClientId));
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
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", SentAt = now, SentOfferSha256 = offerHash,
        ResponseAt = now, ResponseOfferSha256 = offerHash,
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
      var actor = PbcSeed.Actor(partner, "Partner");
      await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, actor, proposalId);
      var offer = await db.CommercialDocuments.AsNoTracking().SingleAsync(x =>
        x.ProposalId == proposalId && x.Kind == CommercialDocumentKinds.Quotation);
      var proposal = await db.Proposals.SingleAsync(x => x.Id == proposalId);
      proposal.SentOfferSha256 = offer.Sha256Hex;
      proposal.ResponseOfferSha256 = offer.Sha256Hex;
      proposal.ResponseChannel = ProposalResponseChannels.Email;
      var dispatchId = Guid.NewGuid();
      db.CommercialNotifications.Add(new CommercialNotification
      {
        Id = dispatchId, FirmId = f.FirmId, Kind = CommercialNotificationKinds.Proposal,
        ProposalId = proposalId, PracticeClientId = f.ClientId, QuotationVersionId = quotationId,
        DocumentId = offer.Id, OfferSha256 = offer.Sha256Hex,
        RecipientAuthorityBasis = ProposalRecipientAuthorityBases.LegacyDispatchSnapshot,
        Recipient = "signatory@example.test", Subject = "Synthetic dispatched offer",
        Body = "Synthetic test evidence", CreatedAt = now
      });
      db.ProposalResponseRecords.Add(new ProposalResponseRecord
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ProposalId = proposalId,
        CommercialNotificationId = dispatchId, Decision = CrmStates.ProposalAccepted,
        OfferSha256 = offer.Sha256Hex, DispatchRecipient = "signatory@example.test",
        AuthorityBasis = ProposalRecipientAuthorityBases.LegacyDispatchSnapshot,
        RespondentName = proposal.RespondentName!, RespondentEmail = "signatory@example.test",
        EvidenceReference = "synthetic-accepted-offer", Channel = ProposalResponseChannels.Email,
        RecordedByUserId = partner.Id, RecordedAt = now
      });
      await db.SaveChangesAsync();
      Assert.True(await ProposalResponseEvidence.IsCurrentAcceptedAsync(db, f.FirmId, proposalId));
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
    var financeOrigin = await host.StartApiForIdentityAsync(financeManager, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true",
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

    const string financeListPath = "/app/finance/fee-agreements";
    await using var financeContext = await browser.NewContextAsync();
    var financePage = await financeContext.NewPageAsync();
    financePage.PageError += (_, error) => errors.Add(error);
    var financeNavigation = await financePage.GotoAsync(financeOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString(financeListPath));
    Assert.True(financeNavigation?.Status == 200,
      $"Client-scoped Finance navigation returned {financeNavigation?.Status} at {financePage.Url}.");
    await financePage.GetByRole(AriaRole.Heading, new() { Name = "Fee settlements", Exact = true }).WaitForAsync();
    Guid agreementId;
    await using (var db = host.CreateDbContext())
    {
      var financeGrants = await db.RoleGrants.AsNoTracking().Where(x => x.FirmId == f.FirmId &&
        x.UserId == financeManager.Id && x.RevokedAt == null).ToListAsync();
      var financeGrant = Assert.Single(financeGrants);
      Assert.Equal("FinanceManager", financeGrant.Role);
      Assert.Equal(f.ClientId, financeGrant.ClientId);
      Assert.Null(financeGrant.EngagementId);
      agreementId = await db.EngagementFeeAgreements.AsNoTracking()
        .Where(x => x.FirmId == f.FirmId && x.ProposalId == proposalId)
        .Select(x => x.Id).SingleAsync();
    }
    var agreementLinkName = $"Open fee settlement for PBC TEST CLIENT ({agreementId:D})";
    await Assertions.Expect(financePage.GetByRole(AriaRole.Link,
      new() { Name = agreementLinkName, Exact = true })).ToBeVisibleAsync();
    await using (var list = await financeContext.APIRequest.GetAsync(
      financeOrigin + "/api/ui/finance/fee-agreements?page=0"))
    {
      Assert.Equal(200, list.Status);
      using var json = JsonDocument.Parse(await list.TextAsync());
      var row = Assert.Single(json.RootElement.GetProperty("rows").EnumerateArray());
      Assert.Equal(agreementId, row.GetProperty("agreementId").GetGuid());
      Assert.Equal(f.ClientId, row.GetProperty("clientId").GetGuid());
    }
    foreach (var denied in new[]
    {
      $"/api/ui/proposals/{proposalId:D}",
      $"/api/ui/proposals/{proposalId:D}/fee-agreement"
    })
    {
      await using var response = await financeContext.APIRequest.GetAsync(financeOrigin + denied);
      Assert.Equal(403, response.Status);
    }

    await financePage.GetByRole(AriaRole.Link, new() { Name = agreementLinkName, Exact = true }).ClickAsync();
    var financeFees = financePage.Locator("audit-fee-agreement");
    await Assertions.Expect(financePage.GetByRole(AriaRole.Heading,
      new() { Name = "Client fee settlement", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(financeFees.GetByRole(AriaRole.Button,
      new() { Name = "Create fee agreement", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(financeFees.GetByRole(AriaRole.Table,
      new() { Name = "Persisted fee milestones", Exact = true })).ToBeVisibleAsync();
    await financeFees.GetByLabel("I reviewed this fee, milestone and selected action.",
      new() { Exact = true }).CheckAsync();
    await financeFees.GetByRole(AriaRole.Button,
      new() { Name = "Prepare or recover advance invoice", Exact = true }).ClickAsync();
    var invoiceLink = financeFees.Locator("a[href^='/app/practice/invoices/']").First;
    await Assertions.Expect(invoiceLink).ToBeVisibleAsync();
    var invoicePath = await invoiceLink.GetAttributeAsync("href");
    Assert.NotNull(invoicePath);
    Assert.True(Guid.TryParse(invoicePath!.Split('/').Last(), out var invoiceId),
      $"Expected the prepared advance to link to the normal invoice route, got {invoicePath}.");
    Assert.Equal($"/app/practice/invoices/{invoiceId:D}", invoicePath);
    await using (var db = host.CreateDbContext())
    {
      var advance = await db.FeeMilestones.AsNoTracking().SingleAsync(x =>
        x.FirmId == f.FirmId && x.AgreementId == agreementId && x.Kind == FeeMilestoneKinds.Advance);
      Assert.Equal(invoiceId, advance.InvoiceId);
      var invoice = await db.Invoices.AsNoTracking().SingleAsync(x => x.FirmId == f.FirmId && x.Id == invoiceId);
      Assert.Equal(BillingStates.InvoiceDraft, invoice.Status);
      Assert.Equal(10_000m, invoice.Total);
      Assert.Null(invoice.ApprovedAt);
      Assert.Null(invoice.PostedAt);
    }
    await financePage.GotoAsync(financeOrigin + invoicePath);
    await Assertions.Expect(financePage.GetByRole(AriaRole.Heading,
      new() { Name = "Practice invoice", Exact = true })).ToBeVisibleAsync();
    await financePage.GetByRole(AriaRole.Button,
      new() { Name = "Submit invoice for review", Exact = true }).ClickAsync();
    await Assertions.Expect(financePage.GetByText(
      "Approval requires a separate authorized FinanceReviewer.", new() { Exact = true })).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      var invoice = await db.Invoices.AsNoTracking().SingleAsync(x => x.FirmId == f.FirmId && x.Id == invoiceId);
      Assert.Equal(BillingStates.InvoiceReviewRequired, invoice.Status);
      Assert.Null(invoice.ApprovedAt);
      Assert.Null(invoice.PostedAt);
    }
    Assert.Empty(errors);
  }
}
