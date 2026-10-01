using System.IO.Compression;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// Commercial calculation, approval matrix, branded documents and the agreed-fee 50/50 cycle against PostgreSQL,
/// using real role identities and the real billing, release and mail-worker paths (no direct status edits).
/// </summary>
[Trait("Profile", "Database")]
public sealed class CommercialWorkflowTests
{
  private sealed record World(Guid FirmId, ActorContext Prep, ActorContext Partner, ActorContext Manager, ActorContext FinanceManager,
    ActorContext FinanceReviewer, AppUser PartnerUser);

  private static AppUser User(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = name + "-" + Guid.NewGuid().ToString("N"), TenantId = "tenant-commercial",
    Email = $"{name}-{Guid.NewGuid():N}@example.test", DisplayName = name, CreatedAt = DateTimeOffset.UtcNow
  };

  private static ActorContext Actor(AppUser user, string role) => new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private static async Task<World> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var prep = User(firmId, "prep"); var partner = User(firmId, "partner"); var manager = User(firmId, "manager");
    var fm = User(firmId, "financemanager"); var fr = User(firmId, "financereviewer");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.Users.AddRange(prep, partner, manager, fm, fr);
    foreach (var (user, role) in new[] { (prep, "RelationshipManager"), (partner, "Partner"), (manager, "Manager"), (fm, "FinanceManager"), (fr, "FinanceReviewer") })
      db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id });
    foreach (var (role, rate) in new[] { ("Partner", 1000m), ("Manager", 750m) })
      db.RateCardVersions.Add(new RateCardVersion
      {
        Id = Guid.NewGuid(), FirmId = firmId, Version = 1, Role = role, Activity = "Audit", Currency = "QAR", RatePerHour = rate,
        Status = PracticeTimeStates.RateApproved, CreatedByUserId = partner.Id, ApprovedByUserId = manager.Id,
        ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
    await db.SaveChangesAsync();
    return new(firmId, Actor(prep, "RelationshipManager"), Actor(partner, "Partner"), Actor(manager, "Manager"), Actor(fm, "FinanceManager"), Actor(fr, "FinanceReviewer"), partner);
  }

  private static readonly QuotationHoursLine[] StandardLines = [new("Partner", "Audit", 10m), new("Manager", "Audit", 20m)];

  private static SaveQuotationRequest Quote(Guid proposalId, decimal complexity = 1m, decimal risk = 0m, decimal discount = 0m,
    bool nonStandard = false, string? note = null, IReadOnlyList<QuotationHoursLine>? lines = null) =>
    new(proposalId, lines ?? StandardLines, complexity, risk, discount, nonStandard, note);

  private static async Task<(Guid LeadId, Guid OpportunityId, Guid ProposalId)> DraftProposalAsync(PgTestSchema pg, World w)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var lead = await PracticeCrmService.CreateLeadAsync(db, w.Prep, new CreateLeadRequest("Gulf Trading LLC", "Referral", "A. Owner", "owner@gulf.example.test"));
    Assert.True(lead.Succeeded, lead.Message);
    Assert.True((await PracticeCrmService.QualifyLeadAsync(db, w.Prep, lead.Value)).Succeeded);
    var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, w.Prep, new CreateOpportunityRequest(lead.Value, "FinancialStatementAudit",
      "GULF-TRADING", "2026-01-01", "2026-12-31", 30000m, "QAR", 60m));
    Assert.True(opportunity.Succeeded, opportunity.Message);
    var proposal = await PracticeCrmService.ReviseProposalAsync(db, w.Prep, new ReviseProposalRequest(opportunity.Value, "AUDIT-2026",
      "Statutory audit of the 2026 financial statements", "Tax advisory", "Independent auditor's report and management letter",
      "Client supplies the trial balance", 1m, "QAR", "2026-01-01", "2026-12-31"));
    Assert.True(proposal.Succeeded, proposal.Message);
    return (lead.Value, opportunity.Value, proposal.Value);
  }

  [Fact]
  public async Task Quotation_IsCalculatedFromApprovedRates_Versioned_AndApprovedByTheConfiguredMatrix()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);

    // A role without an approved rate card fails closed instead of pricing at zero.
    var noRate = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, lines: [new("Intern", "Audit", 10m)]));
    Assert.False(noRate.Succeeded);
    Assert.Contains("No approved rate", noRate.Message);

    // Rate cards + hours + complexity + risk premium: 25,000 × 1.2 = 30,000; + 10% = 33,000.
    var first = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, complexity: 1.2m, risk: 10m));
    Assert.True(first.Succeeded, first.Message);
    Assert.Equal(first.Value, (await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, complexity: 1.2m, risk: 10m))).Value); // idempotent
    var views = (await QuotationService.ListAsync(db, w.Prep, proposalId)).Value!;
    var v1 = Assert.Single(views);
    Assert.Equal((25000m, 33000m, 1L), (v1.Version.BaseAmount, v1.Version.Fee, v1.Version.Revision));
    Assert.All(v1.Lines, l => Assert.NotEqual(Guid.Empty, l.RateCardVersionId));
    Assert.Equal(33000m, (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).Fee);

    // No discount and standard terms need no approver: submitting approves; a proposal can then enter review.
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, first.Value)).Succeeded);
    Assert.Equal(QuotationStates.Approved, (await db.QuotationVersions.AsNoTracking().SingleAsync(x => x.Id == first.Value)).Status);

    // A 15% discount exceeds the unconfigured default (10%): a new revision needs a Partner. Prior version is preserved.
    var discounted = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, complexity: 1.2m, risk: 10m, discount: 15m));
    Assert.True(discounted.Succeeded);
    Assert.NotEqual(first.Value, discounted.Value);
    views = (await QuotationService.ListAsync(db, w.Prep, proposalId)).Value!;
    Assert.Equal([2L, 1L], views.Select(x => x.Version.Revision));
    Assert.Equal(QuotationStates.Superseded, views[1].Version.Status);
    Assert.Equal(33000m, views[1].Version.Fee); // history intact
    Assert.Equal(28050m, views[0].Version.Fee);
    var approval = Assert.Single(views[0].RequiredApprovals);
    Assert.Equal(("Partner", "DEFAULT:DISCOUNT"), (approval.Role, approval.RuleKey));

    // Proposal review is blocked until that quotation is approved.
    var early = await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId);
    Assert.Equal(ErrorCodes.GateBlocked, early.ErrorCode);
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, discounted.Value)).Succeeded);
    var wrongRole = await QuotationService.ApproveAsync(db, w.Manager, discounted.Value, "DEFAULT:DISCOUNT", "Manager tries to approve");
    Assert.Equal(ErrorCodes.ScopeDenied, wrongRole.ErrorCode);
    var partnerOwn = await QuotationService.SaveAsync(db, w.Partner, Quote(proposalId, complexity: 1.2m, risk: 10m, discount: 20m));
    Assert.True((await QuotationService.SubmitAsync(db, w.Partner, partnerOwn.Value)).Succeeded);
    var self = await QuotationService.ApproveAsync(db, w.Partner, partnerOwn.Value, "DEFAULT:DISCOUNT", "Partner approves own version");
    Assert.Equal(ErrorCodes.ProtectedState, self.ErrorCode); // preparers never approve their own version
    // Back to the 15% version by the preparer, approved by a different Partner identity.
    var second = User(w.FirmId, "partner2");
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.Users.Add(second);
      seed.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = second.Id, Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = second.Id });
      await seed.SaveChangesAsync();
    }
    var final = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, complexity: 1.2m, risk: 10m, discount: 15m));
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, final.Value)).Succeeded);
    Assert.True((await QuotationService.ApproveAsync(db, Actor(second, "Partner"), final.Value, "DEFAULT:DISCOUNT", "Discount agreed for a multi-year relationship")).Succeeded);
    Assert.Equal(QuotationStates.Approved, (await db.QuotationVersions.AsNoTracking().SingleAsync(x => x.Id == final.Value)).Status);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);

    // Changing the matrix changes who must approve: a Manager band at 5% and a Partner band at 15%.
    Assert.False((await QuotationService.SaveRuleAsync(db, w.Prep, CommercialRuleKinds.DiscountOver, 5m, "Manager")).Succeeded);
    var proposal2 = (await DraftProposalSecondAsync(pg, w)).ProposalId;
    Assert.True((await QuotationService.SaveRuleAsync(db, w.Partner, CommercialRuleKinds.DiscountOver, 5m, "Manager")).Succeeded);
    var partnerBand = await QuotationService.SaveRuleAsync(db, w.Partner, CommercialRuleKinds.DiscountOver, 15m, "Partner");
    Assert.True(partnerBand.Succeeded);
    var banded = await QuotationService.SaveAsync(db, w.Prep, Quote(proposal2, discount: 8m));
    var bandedRequired = Assert.Single((await QuotationService.ListAsync(db, w.Prep, proposal2)).Value!.Single().RequiredApprovals);
    Assert.Equal("Manager", bandedRequired.Role);
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, banded.Value)).Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, (await QuotationService.ApproveAsync(db, w.Partner, banded.Value, bandedRequired.RuleKey, "Insufficient: matrix names Manager")).ErrorCode);
    Assert.True((await QuotationService.ApproveAsync(db, w.Manager, banded.Value, bandedRequired.RuleKey, "Within the Manager band")).Succeeded);
    // Deactivating the top band and non-standard terms both route through the matrix.
    var terms = await QuotationService.SaveAsync(db, w.Prep, Quote(proposal2, discount: 8m, nonStandard: true, note: "Fees payable in three instalments"));
    Assert.Equal(2, (await QuotationService.ListAsync(db, w.Prep, proposal2)).Value![0].RequiredApprovals.Count);
    Assert.True((await QuotationService.DeactivateRuleAsync(db, w.Partner, partnerBand.Value)).Succeeded);
    Assert.NotEqual(Guid.Empty, terms.Value);

    // Database guards: an approved version's fee and hash cannot be rewritten, and history cannot be deleted.
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE quotation_versions SET fee = 1 WHERE id = {final.Value} AND firm_id = {w.FirmId}"));
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"DELETE FROM quotation_approvals WHERE firm_id = {w.FirmId}"));
  }

  private static async Task<(Guid LeadId, Guid OpportunityId, Guid ProposalId)> DraftProposalSecondAsync(PgTestSchema pg, World w)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var lead = await PracticeCrmService.CreateLeadAsync(db, w.Prep, new CreateLeadRequest("Second Client WLL", "Web", "B. Owner", "b@second.example.test"));
    Assert.True((await PracticeCrmService.QualifyLeadAsync(db, w.Prep, lead.Value)).Succeeded);
    var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, w.Prep, new CreateOpportunityRequest(lead.Value, "AccountingOnly", "SECOND", "2026-01-01", "2026-12-31", 9000m, "QAR"));
    var proposal = await PracticeCrmService.ReviseProposalAsync(db, w.Prep, new ReviseProposalRequest(opportunity.Value, "ACC-2026", "Monthly accounting", "", "Management accounts", "", 1m, "QAR", "2026-01-01", "2026-12-31"));
    return (lead.Value, opportunity.Value, proposal.Value);
  }

  private static string DocxText(byte[] bytes)
  {
    using var archive = new ZipArchive(new MemoryStream(bytes));
    using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
    return System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(reader.ReadToEnd(), "<[^>]+>", " "));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task OneClickDocuments_AreBrandedImmutableAndBoundToTheApprovedQuotation(bool revokeStandingAuthority)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);

    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, complexity: 1.2m, risk: 10m));
    var blocked = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId);
    Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode); // not approved yet
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    var noProfile = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId);
    Assert.Equal(ErrorCodes.GateBlocked, noProfile.ErrorCode);
    Assert.Contains("commercial profile", noProfile.Message);

    Assert.Equal(ErrorCodes.ScopeDenied, (await CommercialDocumentService.SaveProfileAsync(db, w.Prep,
      new("Gulf Audit Partners", "Doha", "hello@gulf.example.test", "+974 0000", "#2B6CB0", ""))).ErrorCode);
    Assert.False((await CommercialDocumentService.SaveProfileAsync(db, w.Partner, new("Gulf Audit Partners", "Doha", "", "", "blue", ""))).Succeeded);
    Assert.True((await CommercialDocumentService.SaveProfileAsync(db, w.Partner,
      new("Gulf Audit Partners", "West Bay, Doha", "hello@gulf.example.test", "+974 0000 0000", "#0F766E", "This letter is subject to our standard terms of business."))).Succeeded);

    var brief = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId);
    Assert.True(brief.Succeeded, brief.Message);
    Assert.Equal(ErrorCodes.GateBlocked, (await CommercialDocumentService.GenerateEngagementLetterAsync(db, w.Partner, proposalId)).ErrorCode);
    Assert.Empty(await db.CommercialDocuments.Where(x => x.Kind == CommercialDocumentKinds.EngagementLetter).ToListAsync());
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).Succeeded);
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId, new("ACCEPTED"))).Succeeded);
    var clientId = (await PracticeCrmService.ConvertToClientDraftAsync(db, w.Prep, new(proposalId, "Gulf Trading LLC"))).Value;
    Assert.Equal(ErrorCodes.GateBlocked, (await CommercialDocumentService.GenerateEngagementLetterAsync(db, w.Partner, proposalId)).ErrorCode);
    await QuestionnaireSeed.SeedTemplatesAndDefinitionsAsync(db);
    await db.SaveChangesAsync();
    var checklist = (await AcceptanceChecklistService.GetAsync(db, w.Manager, clientId)).Value!;
    Assert.NotEmpty(checklist.Items);
    foreach (var item in checklist.Items)
      Assert.True((await AcceptanceChecklistService.RecordAnswerAsync(db, w.Manager, clientId, item.Question.Code,
        item.Question.AdverseAnswer == "YES" ? "No" : "Yes", item.Question.RequiresEvidence ? $"DOC-{item.Question.Code}" : null)).Succeeded);
    var generation = (await db.ClientSafetyStates.AsNoTracking().SingleAsync(x => x.Id == clientId)).InputGeneration;
    var acceptance = await AcceptanceDecisionService.RecordAsync(db, w.Partner, new(clientId, null, "FinancialStatementAudit", "Accepted", "All risk and independence evidence reviewed.", null, generation));
    Assert.True(acceptance.Succeeded, acceptance.Message);
    Assert.Equal(ErrorCodes.ScopeDenied, (await CommercialDocumentService.GenerateEngagementLetterAsync(db, w.Prep, proposalId)).ErrorCode);
    Assert.True((await AuditDeliverableService.RegisterSignatureAsync(db, w.Partner, CompletionTestFixtures.Png(100, 60))).Succeeded);
    Assert.True((await AuditDeliverableService.RegisterFirmSealAsync(db, w.Partner, CompletionTestFixtures.Png(100, 100))).Succeeded);
    var generated = await CommercialDocumentService.GenerateProposalDocumentsAsync(db, w.Partner, proposalId);
    Assert.True(generated.Succeeded, generated.Message);
    var (q, letter) = (generated.Value!.Quotation, generated.Value.EngagementLetter);
    Assert.Equal((CommercialDocumentKinds.Quotation, CommercialDocumentKinds.EngagementLetter), (q.Kind, letter.Kind));
    Assert.Equal((CommercialDocumentRenderer.QuotationTemplate, CommercialDocumentRenderer.EngagementLetterTemplate), (q.TemplateVersion, letter.TemplateVersion));
    Assert.All(new[] { q, letter }, d =>
    {
      Assert.Equal(Hashing.Sha256Hex(d.Bytes), d.Sha256Hex);
      Assert.Equal(1L, d.ProfileVersion);
      Assert.Equal(quote.Value, d.QuotationVersionId);
    });
    var quoteText = DocxText(q.Bytes);
    Assert.Contains("Gulf Audit Partners", quoteText);
    Assert.Contains("West Bay, Doha", quoteText);
    Assert.Contains("33,000.00", quoteText);              // total fee
    Assert.Contains("Complexity factor × 1.2", quoteText);
    Assert.Contains("16,500.00", quoteText);              // 50% advance stated in the payment terms
    Assert.Contains("Gulf Trading LLC (attention A. Owner)", quoteText);
    var letterText = DocxText(letter.Bytes);
    Assert.Contains("Acceptance", letterText);
    Assert.Contains("standard terms of business", letterText);
    Assert.DoesNotContain("Acceptance", quoteText);

    // One click again returns the same documents, not new ones.
    var again = await CommercialDocumentService.GenerateProposalDocumentsAsync(db, w.Partner, proposalId);
    Assert.Equal((q.Id, letter.Id), (again.Value!.Quotation.Id, again.Value.EngagementLetter.Id));
    Assert.Equal(2, await db.CommercialDocuments.CountAsync(x => x.ProposalId == proposalId));

    Assert.Equal(acceptance.Value, letter.AcceptanceDecisionId);
    Assert.NotNull(letter.CommercialAcceptedAt);
    Assert.NotNull(letter.SignatureSpecimenId);
    Assert.NotNull(letter.FirmSealSpecimenId);
    // The gated letter creates the 50/50 agreement. Standing authority creates one DRAFT, never approval/posting.
    var admin = User(w.FirmId, "automation-administrator");
    db.Users.Add(admin);
    db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = admin.Id, Role = "Administrator", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = admin.Id });
    await db.SaveChangesAsync();
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var options = new WorkerOptions(w.FirmId, "Test");
    var policy = new AutomaticFeeInvoicePolicy(true, w.FinanceManager.UserId, admin.Id);
    var handler = new AutomaticFeeInvoiceHandler(policy);
    var discovery = new AutomaticFeeInvoiceDiscovery(factory, store, handler, options, policy);
    Assert.Equal(0, await new AutomaticFeeInvoiceDiscovery(factory, store, handler, options, policy with { Enabled = false }).EnqueuePendingAsync(default));
    Assert.Equal(0, await new AutomaticFeeInvoiceDiscovery(factory, store, handler, options, policy with { ApprovingAdministratorId = w.Prep.UserId }).EnqueuePendingAsync(default));
    Assert.Equal(1, await discovery.EnqueuePendingAsync(default));
    Assert.Equal(0, await discovery.EnqueuePendingAsync(default));
    if (revokeStandingAuthority)
      await db.Users.Where(x => x.Id == admin.Id).ExecuteUpdateAsync(x => x.SetProperty(v => v.SessionEpoch, v => v.SessionEpoch + 1));
    var dispatcher = new OperationDispatcher(store, new DurableOperationRegistry([handler], options), options);
    Assert.True(await dispatcher.ProcessNextAsync());
    Assert.False(await dispatcher.ProcessNextAsync());
    var operation = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.OperationKind == AutomaticFeeInvoiceHandler.Kind);
    Assert.Equal(revokeStandingAuthority ? OperationState.AUTHORIZATION_BLOCKED : OperationState.COMPLETED, operation.Status);
    if (revokeStandingAuthority) Assert.Empty(await db.Invoices.AsNoTracking().ToListAsync());
    else
    {
      var invoice = Assert.Single(await db.Invoices.AsNoTracking().ToListAsync());
      Assert.Equal(BillingStates.InvoiceDraft, invoice.Status);
      Assert.Null(invoice.PostedAt);
      Assert.Equal(16500m, invoice.Total);
      var agreement = await db.EngagementFeeAgreements.AsNoTracking().SingleAsync(x => x.ProposalId == proposalId);
      Assert.Equal(invoice.Id, (await FeeAgreementService.IssueAdvanceInvoiceAsync(db, w.FinanceManager, agreement.Id)).Value);
      Assert.Equal(ErrorCodes.ScopeDenied, (await FeeAgreementService.IssueAdvanceInvoiceAsync(db, w.Prep, agreement.Id)).ErrorCode);
      Assert.Equal(0, await discovery.EnqueuePendingAsync(default));
      Assert.Equal(1, await db.OperationEvents.CountAsync(x => x.OperationId == operation.Id && x.Kind == "fee.invoice-draft.created.v1"));
      var clientUser = User(w.FirmId, "client-portal-owner"); clientUser.UserKind = "Client";
      db.Users.Add(clientUser);
      db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = clientUser.Id, Role = "ClientUser", ClientId = clientId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = admin.Id });
      await db.SaveChangesAsync();
      var clientActor = Actor(clientUser, "ClientUser");
      Assert.Equal(ErrorCodes.GateBlocked, (await AuthorizationDecision.AuthorizeAsync(db, clientActor, new(w.FirmId, clientId, RequiredRoles: ["ClientUser"]))).ErrorCode);
      var engagement = await EngagementLifecycleService.CreateDraftAsync(db, w.Partner, new(clientId, "FinancialStatementAudit", "2026-01-01", "2026-12-31", "AUDIT-2026"));
      Assert.True(engagement.Succeeded, engagement.Message);
      Assert.True((await EngagementLifecycleService.ActivateAsync(db, w.Partner, engagement.Value)).Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, (await AuthorizationDecision.AuthorizeAsync(db, clientActor, new(w.FirmId, clientId, engagement.Value, ["ClientUser"]))).ErrorCode);
      Assert.Empty(await ClientPortalService.AuthorizedPortalGrantIdsAsync(db, clientActor));
      Assert.True((await BillingService.SubmitInvoiceAsync(db, w.FinanceManager, invoice.Id)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, w.FinanceReviewer, invoice.Id)).Succeeded);
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile { Id = Guid.NewGuid(), FirmId = w.FirmId, FunctionalCurrency = "QAR", ProfileKind = BillingStates.TestProfile, Approved = true,
        ApprovedByUserId = w.FinanceReviewer.UserId, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      Assert.True((await BillingService.PostInvoiceAsync(db, w.FinanceManager, invoice.Id)).Succeeded);
      var paid = await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreement.Id, 16500m, "PORTAL-TEST-ADVANCE");
      Assert.True(paid.Succeeded, paid.Message);
      Assert.Equal(ErrorCodes.ScopeDenied, (await FeeAgreementService.RecordAdvancePaymentAsync(db, w.Prep, agreement.Id, 16500m, "PORTAL-TEST-ADVANCE")).ErrorCode);
      Assert.Equal(ErrorCodes.GenerationStale, (await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager with { SessionEpoch = w.FinanceManager.SessionEpoch + 1 }, agreement.Id, 16500m, "PORTAL-TEST-ADVANCE")).ErrorCode);
      Assert.True((await AuthorizationDecision.AuthorizeAsync(db, clientActor, new(w.FirmId, clientId, engagement.Value, ["ClientUser"]))).Succeeded);
      Assert.Single(await ClientPortalService.AuthorizedPortalGrantIdsAsync(db, clientActor));
      Assert.Equal(ClientPortalIntentStates.ReadyToInvite, (await db.ClientPortalIntents.AsNoTracking().SingleAsync(x => x.PracticeClientId == clientId)).State);

    }
    Assert.Equal(ErrorCodes.ProtectedState, (await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, complexity: 1.3m, risk: 10m))).ErrorCode);
    await db.ClientSafetyStates.Where(x => x.Id == clientId).ExecuteUpdateAsync(x => x.SetProperty(v => v.InputGeneration, v => v.InputGeneration + 1));
    Assert.Equal(ErrorCodes.GenerationStale, (await CommercialDocumentService.GenerateEngagementLetterAsync(db, w.Partner, proposalId)).ErrorCode);
    Assert.Equal(revokeStandingAuthority ? 2 : 3, (await CommercialDocumentService.ListAsync(db, w.Prep, proposalId)).Value!.Count); // paid case also retains its immutable receipt

    // Generated documents are append-only, and a stranger from another firm cannot read them.
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE commercial_documents SET file_name = 'x.docx' WHERE id = {q.Id} AND firm_id = {w.FirmId}"));
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"DELETE FROM commercial_documents WHERE id = {q.Id} AND firm_id = {w.FirmId}"));
    var outsider = User(Guid.NewGuid(), "outsider");
    Assert.False((await CommercialDocumentService.GetAsync(db, Actor(outsider, "Partner"), q.Id)).Succeeded);
    Assert.True((await CommercialDocumentService.GetAsync(db, w.Prep, q.Id)).Succeeded);
  }

  [Fact]
  public async Task ComprehensiveTender_RequiresReviewedChapters_AndRetainsExactlyOneImmutableVersionPerPrice()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);
    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId));
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    Assert.False((await CommercialDocumentService.GenerateTenderAsync(db, w.Prep, proposalId, "Actual team CV", "Draft February", true)).Succeeded);
    Assert.True((await CommercialDocumentService.SaveProfileAsync(db, w.Partner, new("Gulf Audit Partners", "Doha", "", "", "#0F766E", "",
      "Firm history and approved CR registration", "Reviewed retail portfolio", "Reviewed ISA methodology"))).Succeeded);
    Assert.False((await CommercialDocumentService.GenerateTenderAsync(db, w.Prep, proposalId, "Actual team CV", "Draft February", false)).Succeeded);
    var result = await CommercialDocumentService.GenerateTenderAsync(db, w.Prep, proposalId, "Partner A and actual assigned team CVs", "Draft February; final March", true);
    Assert.True(result.Succeeded, result.Message);
    var document = result.Value!;
    Assert.Equal(CommercialDocumentKinds.ComprehensiveProposal, document.Kind);
    Assert.Equal(Hashing.Sha256Hex(document.Bytes), document.Sha256Hex);
    var text = DocxText(document.Bytes);
    Assert.Contains("1. Firm profile", text); Assert.Contains("2. Assigned Engagement Partner", text);
    Assert.Contains("3. Industry credentials", text); Assert.Contains("4. Audit methodology", text);
    Assert.Contains("5. Fee schedule", text); Assert.Contains("Partner A", text); Assert.Contains("final March", text);
    Assert.Equal(document.Id, (await CommercialDocumentService.GenerateTenderAsync(db, w.Prep, proposalId, "Other team", "Other dates", true)).Value!.Id);
    Assert.Single(await db.CommercialDocuments.Where(x => x.ProposalId == proposalId && x.Kind == CommercialDocumentKinds.ComprehensiveProposal).ToListAsync());
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE commercial_documents SET file_name = 'changed' WHERE id = {document.Id}"));
  }

  private sealed class RecordingMailSender : IPbcMailSender
  {
    public List<PbcMailPlan> Plans { get; } = [];
    public Task SendAsync(PbcMailPlan plan, CancellationToken ct) { Plans.Add(plan); return Task.CompletedTask; }
  }

  [Fact]
  public async Task AgreedFee_AdvanceReceiptEmailAndBalance_FollowTheFiftyFiftyCycleOnceEach()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);

    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId)); // 25,000 → 12,500 + 12,500
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, (await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposalId)).ErrorCode); // not accepted yet
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).Succeeded);
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId, new("ACCEPTED"))).Succeeded);
    var clientId = (await PracticeCrmService.ConvertToClientDraftAsync(db, w.Prep, new(proposalId, "Gulf Trading LLC"))).Value;

    var agreementId = (await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposalId)).Value;
    Assert.Equal(agreementId, (await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposalId)).Value);
    var view = (await FeeAgreementService.GetForProposalAsync(db, w.Prep, proposalId)).Value!;
    Assert.Equal((25000m, 50m), (view.Agreement.AgreedFee, view.Agreement.AdvancePercent));
    Assert.Equal([12500m, 12500m], view.Milestones.Select(x => x.Milestone.Amount));
    Assert.Equal(view.Agreement.AgreedFee, view.Milestones.Sum(x => x.Milestone.Amount));

    // The balance cannot be invoiced before the advance is paid, nor without a delivered final report.
    Assert.Equal(ErrorCodes.GateBlocked, (await FeeAgreementService.IssueBalanceInvoiceAsync(db, w.FinanceManager, agreementId)).ErrorCode);

    // Invoicing needs a finance identity; the invoice then follows the normal review and posting path.
    Assert.False((await FeeAgreementService.IssueAdvanceInvoiceAsync(db, w.Prep, agreementId)).Succeeded);
    var advanceInvoice = await FeeAgreementService.IssueAdvanceInvoiceAsync(db, w.FinanceManager, agreementId);
    Assert.True(advanceInvoice.Succeeded, advanceInvoice.Message);
    Assert.Equal(advanceInvoice.Value, (await FeeAgreementService.IssueAdvanceInvoiceAsync(db, w.FinanceManager, agreementId)).Value);
    Assert.Equal(12500m, (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == advanceInvoice.Value)).Total);

    Assert.Equal(ErrorCodes.GateBlocked, (await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreementId, 12500m, "TT-1")).ErrorCode); // draft invoice
    Assert.True((await BillingService.SubmitInvoiceAsync(db, w.FinanceManager, advanceInvoice.Value)).Succeeded);
    Assert.True((await BillingService.ApproveInvoiceAsync(db, w.FinanceReviewer, advanceInvoice.Value)).Succeeded);
    db.FirmFinanceProfiles.Add(new FirmFinanceProfile
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, FunctionalCurrency = "QAR", ProfileKind = BillingStates.TestProfile, Approved = true,
      ApprovedByUserId = w.FinanceReviewer.UserId, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    Assert.True((await BillingService.PostInvoiceAsync(db, w.FinanceManager, advanceInvoice.Value)).Succeeded);

    // Partial, duplicate and excessive payments are handled; the advance stays unpaid until fully allocated.
    var partial = await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreementId, 5000m, "TT-1");
    Assert.True(partial.Succeeded, partial.Message);
    Assert.Equal((false, 7500m, null), (partial.Value!.MilestonePaid, partial.Value.Outstanding, partial.Value.ReceiptDocumentId));
    var repeated = await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreementId, 5000m, "TT-1");
    Assert.Equal(partial.Value.ReceiptId, repeated.Value!.ReceiptId);
    Assert.Equal(1, await db.Receipts.CountAsync(x => x.FirmId == w.FirmId));
    Assert.Equal("fee.over-payment", (await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreementId, 8000m, "TT-2")).ErrorCode);
    Assert.Equal(0, await db.CommercialDocuments.CountAsync(x => x.Kind == CommercialDocumentKinds.PaymentReceipt));

    // Full payment: paid once, one official receipt document, one queued email.
    Assert.True((await CommercialDocumentService.SaveProfileAsync(db, w.Partner, new("Gulf Audit Partners", "Doha", "hello@gulf.example.test", "", "#2B6CB0", ""))).Succeeded);
    var paid = await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreementId, 7500m, "TT-2");
    Assert.True(paid.Succeeded, paid.Message);
    Assert.True(paid.Value!.MilestonePaid && paid.Value.EmailQueued);
    Assert.NotNull(paid.Value.ReceiptDocumentId);
    var again = await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreementId, 7500m, "TT-2");
    Assert.Equal(paid.Value.ReceiptDocumentId, again.Value!.ReceiptDocumentId);
    var receiptDoc = await db.CommercialDocuments.AsNoTracking().SingleAsync(x => x.Kind == CommercialDocumentKinds.PaymentReceipt);
    var receiptText = DocxText(receiptDoc.Bytes);
    Assert.Contains("12,500.00 QAR", receiptText);
    Assert.Contains("Balance due on delivery of the final report", receiptText);
    var notification = await db.CommercialNotifications.AsNoTracking().SingleAsync();
    Assert.Equal("owner@gulf.example.test", notification.Recipient);
    Assert.Equal(FeeMilestoneStates.Paid, (await db.FeeMilestones.AsNoTracking().SingleAsync(x => x.Kind == FeeMilestoneKinds.Advance)).State);

    // The isolated mail worker delivers the receipt email exactly once.
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var sender = new RecordingMailSender();
    var handler = new CommercialMailDeliveryHandler(factory, sender);
    var options = new WorkerOptions(w.FirmId, "Acceptance", ExternalEffectsEnabled: true, Group: "mail");
    var worker = new AuditSphereOps.Worker.Worker(new OperationDispatcher(store, new DurableOperationRegistry([handler], options), options),
      [new CommercialMailDiscovery(factory, store, handler, options)], NullLogger<AuditSphereOps.Worker.Worker>.Instance);
    Assert.True(await worker.ProcessNextAsync());
    Assert.False(await worker.ProcessNextAsync());
    Assert.Equal("owner@gulf.example.test", Assert.Single(sender.Plans).Recipient);
    Assert.Equal(("SENT", true), ((await db.CommercialNotifications.AsNoTracking().SingleAsync()).DeliveryState,
      (await db.CommercialNotifications.AsNoTracking().SingleAsync()).DeliveredAt is not null));

    // Balance: needs the linked engagement and an issued release (final report delivered and signed off).
    Assert.Equal(ErrorCodes.GateBlocked, (await FeeAgreementService.IssueBalanceInvoiceAsync(db, w.FinanceManager, agreementId)).ErrorCode);
    var engagementId = Guid.NewGuid();
    var reviewer = User(w.FirmId, "reviewer"); var releasePartner = User(w.FirmId, "releasepartner");
    var workpaperId = Guid.NewGuid();
    db.Engagements.Add(new Engagement { Id = engagementId, FirmId = w.FirmId, PracticeClientId = clientId, Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow });
    db.Users.AddRange(reviewer, releasePartner);
    db.RoleGrants.AddRange(
      new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = reviewer.Id, Role = "Reviewer", ClientId = clientId, EngagementId = engagementId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = reviewer.Id },
      new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = releasePartner.Id, Role = "Partner", ClientId = clientId, EngagementId = engagementId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = releasePartner.Id });
    db.Workpapers.Add(new Workpaper { Id = workpaperId, FirmId = w.FirmId, ClientId = clientId, EngagementId = engagementId, ActorId = releasePartner.Id, Index = "R-01",
      Title = "Final report", Objective = "Deliver", TemplateVersion = "RELEASE-2026-v1", Procedure = "Agree", Status = "WORKING", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    Assert.True((await FeeAgreementService.LinkEngagementAsync(db, w.Prep, agreementId, engagementId)).Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, (await FeeAgreementService.IssueBalanceInvoiceAsync(db, w.FinanceManager, agreementId)).ErrorCode); // no release yet

    var manifest = System.Text.Encoding.UTF8.GetBytes("final-report-manifest");
    var digest = Hashing.Sha256Hex(manifest);
    var checkpoints = new LocalAppendOnlyCheckpointStore(Path.Combine(Path.GetTempPath(), "AuditSphereOps-Tests", Guid.NewGuid().ToString("N")));
    var approval = await ApprovalService.CreateAsync(db, Actor(reviewer, "Reviewer"), new CreateApprovalRequest("WORKPAPER", workpaperId, 1, 1, 1, digest));
    var candidate = await ReleaseService.CreateCandidateAsync(db, Actor(releasePartner, "Partner"), new CreateReleaseCandidateRequest(approval.Value, "WORKPAPER", workpaperId, 1, 1, 1, digest));
    Assert.True((await ReleaseCheckpointService.RecordCheckpointDirectAsync(db, checkpoints, Actor(releasePartner, "Partner"),
      new RecordReleaseCheckpointRequest(candidate.Value, 1, "release-final", digest, manifest))).Succeeded);
    Assert.True((await ReleaseService.IssueAsync(db, Actor(releasePartner, "Partner"), new IssueReleaseRequest(candidate.Value, 1, digest, "release-final"))).Succeeded);

    var balance = await FeeAgreementService.IssueBalanceInvoiceAsync(db, w.FinanceManager, agreementId);
    Assert.True(balance.Succeeded, balance.Message);
    Assert.Equal(balance.Value, (await FeeAgreementService.IssueBalanceInvoiceAsync(db, w.FinanceManager, agreementId)).Value); // never twice
    Assert.NotEqual(advanceInvoice.Value, balance.Value);
    Assert.Equal(25000m, await db.Invoices.Where(x => x.FirmId == w.FirmId).SumAsync(x => x.Total)); // advance + balance = agreed fee
    Assert.Equal(2, await db.BillingSourceAllocations.CountAsync(x => x.SourceKind == FeeAgreementService.MilestoneSourceKind));
  }
}
