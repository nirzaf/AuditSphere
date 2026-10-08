using System.Globalization;
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
  public async Task AngularCommercialSettingsRespectEditAuthorityAndReviewedRevisions()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var readOnly = await CommercialSettingsQuery.GetAsync(db, w.Prep);
    Assert.True(readOnly.Succeeded, readOnly.Message);
    Assert.False(readOnly.Value!.CanEdit);
    var initial = await CommercialSettingsQuery.GetAsync(db, w.Partner);
    Assert.True(initial.Value!.CanEdit);
    var request = new SaveCommercialProfileRequest("Synthetic Settings Firm", "Address", "", "", "#0F766E", "Closing", ExpectedVersion: 0);
    Assert.False((await CommercialDocumentService.SaveProfileAsync(db, w.Prep, request)).Succeeded);
    var saved = await CommercialDocumentService.SaveProfileAsync(db, w.Partner, request);
    Assert.True(saved.Succeeded, saved.Message);
    Assert.Equal(saved.Value, (await CommercialDocumentService.SaveProfileAsync(db, w.Partner, request)).Value);
    Assert.False((await CommercialDocumentService.SaveProfileAsync(db, w.Partner, request with { LegalName = "Stale revised name" })).Succeeded);
    var rule = await QuotationService.SaveRuleAsync(db, w.Partner, "DISCOUNT_OVER_PERCENT", 5m, "Manager",
      expectedRulesRevision: initial.Value.RulesRevision);
    Assert.True(rule.Succeeded, rule.Message);
    var current = await CommercialSettingsQuery.GetAsync(db, w.Partner);
    Assert.NotEqual(initial.Value.RulesRevision, current.Value!.RulesRevision);
    Assert.Equal("1", current.Value.Profile!.Version);
    Assert.Single(current.Value.Rules);
    var stale = await QuotationService.SaveRuleAsync(db, w.Partner, "DISCOUNT_OVER_PERCENT", 10m, "Partner",
      expectedRulesRevision: initial.Value.RulesRevision);
    Assert.False(stale.Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
    Assert.False((await QuotationService.DeactivateRuleAsync(db, w.Prep, rule.Value, expectedRulesRevision: current.Value.RulesRevision)).Succeeded);
    Assert.True((await QuotationService.DeactivateRuleAsync(db, w.Partner, rule.Value, expectedRulesRevision: current.Value.RulesRevision)).Succeeded);
    var inactive = await CommercialSettingsQuery.GetAsync(db, w.Partner);
    Assert.Empty(inactive.Value!.Rules);
    Assert.Equal(initial.Value.RulesRevision, inactive.Value.RulesRevision);
    Assert.False((await QuotationService.SaveRuleAsync(db, w.Partner, "NON_STANDARD_TERMS", null, "Partner",
      expectedRulesRevision: current.Value.RulesRevision)).Succeeded);
  }

  [Fact]
  public async Task AngularFeeWorkspaceIsFirmScopedAndConcurrentEngagementLinksNeverOverwrite()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var foreign = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);
    var blocked = await FeeAgreementWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.True(blocked.Succeeded, blocked.Message);
    Assert.False(blocked.Value!.CanCreate);
    Assert.NotEmpty(blocked.Value.CreationBlockers);
    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId));
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).Succeeded);
    var offerSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).SentOfferSha256;
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new("ACCEPTED", null, offerSha, "A. Owner", "owner@gulf.example.test", "Signed acceptance letter"))).Succeeded);
    var client = await PracticeCrmService.ConvertToClientDraftAsync(db, w.Prep, new(proposalId, "Fee workspace client"));
    var eligible = await FeeAgreementWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.True(eligible.Value!.CanCreate);
    Assert.False(eligible.Value.CanFinance);
    var created = await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposalId);
    Assert.True(created.Succeeded, created.Message);
    var engagementA = Guid.NewGuid(); var engagementB = Guid.NewGuid();
    db.Engagements.AddRange(
      new Engagement { Id = engagementA, FirmId = w.FirmId, PracticeClientId = client.Value, ServiceRoute = "Audit", CreatedAt = DateTimeOffset.UtcNow },
      new Engagement { Id = engagementB, FirmId = w.FirmId, PracticeClientId = client.Value, ServiceRoute = "Accounting", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var ready = await FeeAgreementWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.Equal(2, ready.Value!.Engagements.Count);
    Assert.Equal(2, ready.Value.Milestones.Count);
    Assert.Equal(25000m, decimal.Parse(ready.Value.Fee, CultureInfo.InvariantCulture));
    Assert.Equal(50m, decimal.Parse(ready.Value.AdvancePercent, CultureInfo.InvariantCulture));
    Assert.False(ready.Value.ReleaseRecorded);
    Assert.False((await FeeAgreementWorkspaceQuery.GetAsync(db, foreign.Prep, proposalId)).Succeeded);
    async Task<CommandResult> LinkAsync(Guid engagement)
    {
      await using var scoped = new AuditSphereDbContext(pg.Options);
      return await FeeAgreementService.LinkEngagementAsync(scoped, w.Prep, created.Value, engagement);
    }
    var links = await Task.WhenAll(LinkAsync(engagementA), LinkAsync(engagementB));
    Assert.Single(links, r => r.Succeeded);
    Assert.Single(links, r => !r.Succeeded);
    var linked = await FeeAgreementWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.Contains(linked.Value!.EngagementId!.Value, new[] { engagementA, engagementB });
    Assert.Empty(linked.Value.Engagements);
    Assert.True((await LinkAsync(linked.Value.EngagementId.Value)).Succeeded);
  }

  [Fact]
  public async Task AngularCommercialDocumentsShowBlockersAndRetainImmutableIdentities()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var foreign = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);
    var initial = await CommercialDocumentWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.True(initial.Succeeded, initial.Message);
    Assert.NotEmpty(initial.Value!.CommonBlockers);
    Assert.NotEmpty(initial.Value.LetterBlockers);
    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId));
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    var profileRequest = new SaveCommercialProfileRequest("Synthetic Firm", "Address", "", "", "#0F766E", "Closing",
      "Reviewed history", "Reviewed credentials", "Reviewed methodology");
    Assert.True((await CommercialDocumentService.SaveProfileAsync(db, w.Partner, profileRequest)).Succeeded);
    var ready = await CommercialDocumentWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.Empty(ready.Value!.CommonBlockers);
    Assert.Empty(ready.Value.TenderBlockers);
    Assert.NotEmpty(ready.Value.LetterBlockers);
    Assert.False((await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId,
      expectedQuotationId: Guid.NewGuid(), expectedProfileVersion: 1)).Succeeded);
    Assert.Empty(await db.CommercialDocuments.Where(x => x.ProposalId == proposalId).ToListAsync());
    var brief = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId,
      expectedQuotationId: quote.Value, expectedProfileVersion: 1);
    Assert.True(brief.Succeeded, brief.Message);
    var replay = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId,
      expectedQuotationId: quote.Value, expectedProfileVersion: 1);
    Assert.Equal(brief.Value!.Id, replay.Value!.Id);
    var tender = await CommercialDocumentService.GenerateTenderAsync(db, w.Prep, proposalId, "Reviewed assigned team", "Reviewed delivery timeline", true,
      expectedQuotationId: quote.Value, expectedProfileVersion: 1);
    Assert.True(tender.Succeeded, tender.Message);
    var listed = await CommercialDocumentWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.Equal(2, listed.Value!.Documents.Count);
    var artifact = Assert.Single(listed.Value.Documents, d => d.Id == brief.Value.Id);
    Assert.Equal(brief.Value.Sha256Hex, artifact.Sha256);
    Assert.Equal("1", artifact.ProfileVersion);
    Assert.False((await CommercialDocumentWorkspaceQuery.GetAsync(db, foreign.Prep, proposalId)).Succeeded);
    Assert.False((await CommercialDocumentService.GenerateEngagementLetterAsync(db, w.Partner, proposalId,
      expectedQuotationId: quote.Value, expectedProfileVersion: 1)).Succeeded);
    Assert.True((await CommercialDocumentService.SaveProfileAsync(db, w.Partner, profileRequest with { LegalName = "Synthetic Revised Firm" })).Succeeded);
    var stale = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId,
      expectedQuotationId: quote.Value, expectedProfileVersion: 1);
    Assert.False(stale.Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
    var revised = await CommercialDocumentWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.Equal("2", revised.Value!.ProfileVersion);
    Assert.All(revised.Value.Documents, d => Assert.Equal("1", d.ProfileVersion));
  }

  [Fact]
  public async Task AngularQuotationWorkspaceUsesReviewedRatesAndRevisionFences()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var foreign = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    var workspace = await QuotationWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.True(workspace.Succeeded, workspace.Message);
    Assert.True(workspace.Value!.Editable);
    Assert.Empty(workspace.Value.Versions);
    var rate = workspace.Value.Rates.Single(r => r.Role == "Partner");
    var request = new SaveQuotationRequest(proposalId, [new("Partner", "Audit", 10m, rate.Id)],
      1m, 0m, 0m, false, null, ExpectedRevision: 0, ExpectedProposalRevision: 1);
    var preview = await QuotationWorkspaceQuery.PreviewAsync(db, w.Prep, request);
    Assert.True(preview.Succeeded, preview.Message);
    Assert.Equal(10000m, decimal.Parse(preview.Value!.Fee, System.Globalization.CultureInfo.InvariantCulture));
    Assert.Empty(await db.QuotationVersions.Where(x => x.ProposalId == proposalId).ToListAsync());
    var saved = await QuotationService.SaveAsync(db, w.Prep, request);
    Assert.True(saved.Succeeded, saved.Message);
    Assert.Equal(saved.Value, (await QuotationService.SaveAsync(db, w.Prep, request)).Value);
    Assert.False((await QuotationService.SaveAsync(db, w.Prep, request with { DiscountPercent = 1m })).Succeeded);
    Assert.False((await QuotationWorkspaceQuery.PreviewAsync(db, w.Prep, request)).Succeeded);
    Assert.False((await QuotationWorkspaceQuery.GetAsync(db, foreign.Prep, proposalId)).Succeeded);
    var current = await QuotationWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.Equal("1", Assert.Single(current.Value!.Versions).Revision);
    db.RateCardVersions.Add(new RateCardVersion
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, Version = 2, Role = "Partner", Activity = "Audit", Currency = "QAR",
      RatePerHour = 1100m, Status = PracticeTimeStates.RateApproved, CreatedByUserId = w.Partner.UserId,
      ApprovedByUserId = w.Manager.UserId, CreatedAt = DateTimeOffset.UtcNow, ApprovedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var stale = await QuotationService.SaveAsync(db, w.Prep, request with { ExpectedRevision = 1 });
    Assert.False(stale.Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
    Assert.False((await QuotationWorkspaceQuery.PreviewAsync(db, w.Prep, request with { ExpectedRevision = 1 })).Succeeded);
    Assert.Single(await db.QuotationVersions.Where(x => x.ProposalId == proposalId).ToListAsync());
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
    var offerSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).SentOfferSha256;
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new("ACCEPTED", null, offerSha, "A. Owner", "owner@gulf.example.test", "Signed acceptance letter"))).Succeeded);
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
    // STE 4.1.4: the statutory route selects its own governed template, and the stored identity is the exact template used.
    Assert.Equal((CommercialDocumentRenderer.QuotationTemplate, EngagementLetterTemplates.Statutory), (q.TemplateVersion, letter.TemplateVersion));
    // STE 4.1.5: while automatic drafting is off, the advance invoice is explicitly pending, never presented as prepared.
    var pending = (await FeeAgreementWorkspaceQuery.GetAsync(db, w.Partner, proposalId)).Value!;
    Assert.Equal(AdvanceInvoicePreparationStates.PendingAutomationDisabled, pending.AdvancePreparation.State);
    Assert.Contains("Finance must prepare the draft manually", pending.AdvancePreparation.Message);
    var automated = (await FeeAgreementWorkspaceQuery.GetAsync(db, w.Partner, proposalId, default, automaticDraftingEnabled: true)).Value!;
    Assert.Equal(AdvanceInvoicePreparationStates.AwaitingAutomation, automated.AdvancePreparation.State);
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
    var offerSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).SentOfferSha256;
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new("ACCEPTED", null, offerSha, "A. Owner", "owner@gulf.example.test", "Signed acceptance letter"))).Succeeded);
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
    Assert.Equal("fee.reference-conflict", (await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreementId, 4999m, "TT-1")).ErrorCode);
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
    var notification = await db.CommercialNotifications.AsNoTracking().SingleAsync(x => x.Kind == CommercialNotificationKinds.Receipt);
    Assert.Equal("owner@gulf.example.test", notification.Recipient);
    Assert.Equal(FeeMilestoneStates.Paid, (await db.FeeMilestones.AsNoTracking().SingleAsync(x => x.Kind == FeeMilestoneKinds.Advance)).State);

    // The isolated mail worker delivers the receipt email exactly once (the proposal dispatch email is separate).
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var sender = new RecordingMailSender();
    var handler = new CommercialMailDeliveryHandler(factory, sender);
    var options = new WorkerOptions(w.FirmId, "Acceptance", ExternalEffectsEnabled: true, Group: "mail");
    var worker = new AuditSphereOps.Worker.Worker(new OperationDispatcher(store, new DurableOperationRegistry([handler], options), options),
      [new CommercialMailDiscovery(factory, store, handler, options)], NullLogger<AuditSphereOps.Worker.Worker>.Instance);
    Assert.True(await worker.ProcessNextAsync());
    Assert.True(await worker.ProcessNextAsync());
    Assert.False(await worker.ProcessNextAsync());
    Assert.Equal(2, sender.Plans.Count);
    Assert.Contains(sender.Plans, p => p.Subject.StartsWith("Payment receipt"));
    Assert.Contains(sender.Plans, p => p.Subject.Contains("Proposal revision"));
    var deliveredReceipt = await db.CommercialNotifications.AsNoTracking().SingleAsync(x => x.Kind == CommercialNotificationKinds.Receipt);
    Assert.Equal(("SENT", true), (deliveredReceipt.DeliveryState, deliveredReceipt.DeliveredAt is not null));

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

    // Balance payment workflow (AS-COMP-08)
    // 1. Payment blocked while invoice is Draft
    Assert.Equal(ErrorCodes.GateBlocked, (await FeeAgreementService.RecordBalancePaymentAsync(db, w.FinanceManager, agreementId, 12500m, "TT-BAL-1")).ErrorCode);

    // 2. Submit, approve and post the balance invoice
    Assert.True((await BillingService.SubmitInvoiceAsync(db, w.FinanceManager, balance.Value)).Succeeded);
    Assert.True((await BillingService.ApproveInvoiceAsync(db, w.FinanceReviewer, balance.Value)).Succeeded);
    Assert.True((await BillingService.PostInvoiceAsync(db, w.FinanceManager, balance.Value)).Succeeded);

    // 3. Set up Finance contact routing
    var financeContact = new ClientContact
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, PracticeClientId = clientId,
      FullName = "Finance Department", Email = "finance@gulf.example.test", Role = "Finance", IsActive = true
    };
    db.ClientContacts.Add(financeContact);
    db.ClientContactRoutings.Add(new ClientContactRouting
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, PracticeClientId = clientId, ClientContactId = financeContact.Id,
      Purpose = CorrespondencePurposes.Finance, IsPrimaryForPurpose = true, CreatedByUserId = w.Partner.UserId, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    // 4. Partial balance payment
    var balPartial = await FeeAgreementService.RecordBalancePaymentAsync(db, w.FinanceManager, agreementId, 5000m, "TT-BAL-1");
    Assert.True(balPartial.Succeeded, balPartial.Message);
    Assert.False(balPartial.Value!.MilestonePaid);
    Assert.Equal(7500m, balPartial.Value.Outstanding);

    // 5. Duplicate balance payment with same reference
    var balRepeated = await FeeAgreementService.RecordBalancePaymentAsync(db, w.FinanceManager, agreementId, 5000m, "TT-BAL-1");
    Assert.Equal(balPartial.Value.ReceiptId, balRepeated.Value!.ReceiptId);

    // 6. Complete balance payment
    var balPaid = await FeeAgreementService.RecordBalancePaymentAsync(db, w.FinanceManager, agreementId, 7500m, "TT-BAL-2");
    Assert.True(balPaid.Succeeded, balPaid.Message);
    Assert.True(balPaid.Value!.MilestonePaid && balPaid.Value.EmailQueued);
    Assert.NotNull(balPaid.Value.ReceiptDocumentId);

    // 7. Balance receipt document text: full settlement phrasing
    var balReceiptDoc = await db.CommercialDocuments.AsNoTracking().SingleAsync(x => x.Id == balPaid.Value.ReceiptDocumentId);
    var balReceiptText = DocxText(balReceiptDoc.Bytes);
    Assert.Contains("Total outstanding balance", balReceiptText);
    Assert.Contains("0.00 QAR", balReceiptText);
    Assert.Contains("paid in full", balReceiptText);

    // 8. Finance notification routed to finance contact
    var balNotification = await db.CommercialNotifications.AsNoTracking()
      .Where(x => x.Kind == CommercialNotificationKinds.Receipt)
      .OrderByDescending(x => x.CreatedAt)
      .FirstAsync();
    Assert.Equal("finance@gulf.example.test", balNotification.Recipient);

    // 9. Client portal user document download
    var clientUser = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, Subject = "client-finance-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant-commercial", Email = "client.portal@gulf.example.test", DisplayName = "Client Portal User",
      UserKind = "Client", CreatedAt = DateTimeOffset.UtcNow
    };
    db.Users.Add(clientUser);
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = clientUser.Id, Role = "ClientUser",
      ClientId = clientId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.Partner.UserId
    });
    await db.SaveChangesAsync();

    var clientActor = new ActorContext(clientUser.Id, w.FirmId, clientUser.SessionEpoch, ["ClientUser"]);
    var docDownload = await CommercialDocumentService.GetAsync(db, clientActor, balReceiptDoc.Id);
    Assert.True(docDownload.Succeeded, docDownload.Message);
    Assert.NotNull(docDownload.Value);
    Assert.Equal(balReceiptDoc.FileName, docDownload.Value.FileName);

    // Client user without grant to this client is forbidden
    var otherClient = new PracticeClient { Id = Guid.CreateVersion7(), FirmId = w.FirmId, LegalName = "Other Client LLC", CreatedAt = DateTimeOffset.UtcNow };
    db.PracticeClients.Add(otherClient);
    var foreignClientUser = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, Subject = "foreign-client-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant-commercial", Email = "unauth@example.test", DisplayName = "Unauth",
      UserKind = "Client", CreatedAt = DateTimeOffset.UtcNow
    };
    db.Users.Add(foreignClientUser);
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = foreignClientUser.Id, Role = "ClientUser",
      ClientId = otherClient.Id, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.Partner.UserId
    });
    await db.SaveChangesAsync();
    var foreignActor = new ActorContext(foreignClientUser.Id, w.FirmId, foreignClientUser.SessionEpoch, ["ClientUser"]);
    var foreignDoc = await CommercialDocumentService.GetAsync(db, foreignActor, balReceiptDoc.Id);
    Assert.False(foreignDoc.Succeeded);

    // 10. Client Portal Finance Query
    var portalFinance = await ClientPortalFinanceQuery.GetAsync(db, clientActor);
    Assert.True(portalFinance.Succeeded, portalFinance.Message);
    Assert.NotNull(portalFinance.Value);
    Assert.Single(portalFinance.Value.Agreements);
    var agreementFinance = portalFinance.Value.Agreements[0];
    Assert.Equal(2, agreementFinance.Invoices.Count);
    Assert.Equal(4, agreementFinance.Receipts.Count); // 2 advance receipts + 2 balance receipts
    Assert.Equal("0", agreementFinance.OutstandingBalance);
  }

  [Fact]
  public async Task Dispatch_BindsArtifactToCurrentQuotation_AndDownstreamUsesAcceptedTerms()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);

    // Quotation A: approved while the proposal is a draft, priced, and its brief artifact generated.
    var quoteA = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId)); // 25,000
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quoteA.Value)).Succeeded);
    Assert.True((await CommercialDocumentService.SaveProfileAsync(db, w.Partner,
      new SaveCommercialProfileRequest("Binding Firm", "Doha", "", "", "#2B6CB0", "Closing"))).Succeeded);
    var artifactA = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId,
      expectedQuotationId: quoteA.Value, expectedProfileVersion: 1);
    Assert.True(artifactA.Succeeded, artifactA.Message);

    // A later approved quotation revision B prices the same fee with a different composition while the
    // proposal is still a draft: the reviewed artifact A is now older than the current quotation, so
    // dispatch is refused until the current brief is regenerated (STE 4.1.2 — the accepted terms are
    // the dispatched artifact's, not merely the newest approved pricing revision).
    var quoteB = await QuotationService.SaveAsync(db, w.Prep,
      Quote(proposalId, lines: [new QuotationHoursLine("Partner", "Audit", 17.5m), new QuotationHoursLine("Manager", "Audit", 10m)]));
    Assert.Equal(25000m, (await db.QuotationVersions.AsNoTracking().SingleAsync(x => x.Id == quoteB.Value)).Fee);
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quoteB.Value)).Succeeded);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, (await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).ErrorCode);
    Assert.Empty(await db.CommercialNotifications.Where(x => x.ProposalId == proposalId).ToListAsync());

    // Regenerating the current brief binds dispatch and acceptance to revision B.
    var artifactB = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId,
      expectedQuotationId: quoteB.Value, expectedProfileVersion: 1);
    Assert.True(artifactB.Succeeded, artifactB.Message);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).Succeeded);
    var notification = await db.CommercialNotifications.AsNoTracking().SingleAsync(x => x.ProposalId == proposalId);
    Assert.Equal(quoteB.Value, notification.QuotationVersionId);
    Assert.Equal(artifactB.Value!.Sha256Hex, notification.OfferSha256);
    var acceptedSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).SentOfferSha256;
    Assert.Equal("crm.stale-offer", (await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new("ACCEPTED", null, artifactA.Value!.Sha256Hex, "A. Owner", "owner@gulf.example.test"))).ErrorCode);
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new("ACCEPTED", null, acceptedSha, "A. Owner", "owner@gulf.example.test", "Signed acceptance letter"))).Succeeded);

    // Repricing after the proposal left draft is refused at the source, so an approved revision can
    // never silently diverge from the accepted artifact downstream; material changes require a new
    // proposal revision with fresh dispatch and acceptance.
    Assert.Equal(ErrorCodes.ProtectedState, (await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId))).ErrorCode);

    // A fresh proposal without repricing creates its agreement on exactly the accepted quotation.
    await using (var db2 = new AuditSphereDbContext(pg.Options))
    {
      var lead2 = await PracticeCrmService.CreateLeadAsync(db2, w.Prep,
        new CreateLeadRequest("Binding Client Lead", "Web", "B. Owner", "binding-client@example.test"));
      Assert.True(lead2.Succeeded, lead2.Message);
      Assert.True((await PracticeCrmService.QualifyLeadAsync(db2, w.Prep, lead2.Value)).Succeeded);
      var opportunity2 = await PracticeCrmService.CreateOpportunityAsync(db2, w.Prep,
        new CreateOpportunityRequest(lead2.Value, "FinancialStatementAudit", "BINDING-CLIENT", "2026-01-01", "2026-12-31", 30000m, "QAR", 60m));
      Assert.True(opportunity2.Succeeded, opportunity2.Message);
      var revised2 = await PracticeCrmService.ReviseProposalAsync(db2, w.Prep,
        new ReviseProposalRequest(opportunity2.Value, "AUDIT-2026", "Statutory audit of the 2026 financial statements",
          "Tax advisory", "Independent auditor's report and management letter", "Client supplies the trial balance",
          25000m, "QAR", "2026-01-01", "2026-12-31"));
      Assert.True(revised2.Succeeded, revised2.Message);
    }
    var proposal2 = (await db.Proposals.AsNoTracking()
      .Where(x => x.FirmId == w.FirmId && x.Id != proposalId).OrderByDescending(x => x.CreatedAt).FirstAsync()).Id;
    var quote2 = await QuotationService.SaveAsync(db, w.Prep, Quote(proposal2));
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote2.Value)).Succeeded);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposal2)).Succeeded);
    Assert.True((await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposal2,
      expectedQuotationId: quote2.Value, expectedProfileVersion: 1)).Succeeded);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposal2)).Succeeded);
    var offer2 = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposal2)).SentOfferSha256;
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposal2,
      new("ACCEPTED", null, offer2, "A. Owner", "owner@gulf.example.test", "Signed acceptance letter"))).Succeeded);
    var converted2 = await PracticeCrmService.ConvertToClientDraftAsync(db, w.Prep, new(proposal2, "Binding Client LLC"));
    Assert.True(converted2.Succeeded, converted2.Message);
    var agreement = await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposal2);
    Assert.True(agreement.Succeeded, agreement.Message);
    var stored = await db.EngagementFeeAgreements.AsNoTracking().SingleAsync(x => x.Id == agreement.Value);
    Assert.Equal(quote2.Value, stored.QuotationVersionId);
  }

  [Fact]
  public async Task FeeAgreement_RequiresAcceptanceCitingDispatchedOffer()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);

    // Standard happy path: approved quotation, brief artifact, dispatch, acceptance of the exact offer, client.
    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId,
      lines: [new QuotationHoursLine("Partner", "Audit", 17.5m), new QuotationHoursLine("Manager", "Audit", 10m)]));
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    Assert.True((await CommercialDocumentService.SaveProfileAsync(db, w.Partner,
      new SaveCommercialProfileRequest("Binding Firm", "Doha", "", "", "#2B6CB0", "Closing"))).Succeeded);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);
    Assert.True((await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId,
      expectedQuotationId: quote.Value, expectedProfileVersion: 1)).Succeeded);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).Succeeded);
    var proposal = await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId);
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new("ACCEPTED", null, proposal.SentOfferSha256, "A. Owner", "owner@gulf.example.test", "Signed acceptance letter"))).Succeeded);
    Assert.True((await PracticeCrmService.ConvertToClientDraftAsync(db, w.Prep, new(proposalId, "Binding Client LLC"))).Succeeded);

    // A recorded acceptance that does not cite the dispatched offer identity (a stale, replayed, or
    // tampered citation) can never underwrite the fee agreement: the agreement must carry the exact
    // accepted commercial terms the client responded to (STE 4.1.3, STE-REM-02).
    var recordedAcceptance = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).ResponseOfferSha256;
    await db.Proposals.Where(x => x.Id == proposalId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.ResponseOfferSha256, new string('f', 64)));
    var refused = await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposalId);
    Assert.Equal(ErrorCodes.GenerationStale, refused.ErrorCode);
    Assert.Empty(await db.EngagementFeeAgreements.Where(x => x.ProposalId == proposalId).ToListAsync());

    // Restoring the exact dispatched citation restores the normal reviewed agreement path.
    await db.Proposals.Where(x => x.Id == proposalId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.ResponseOfferSha256, recordedAcceptance));
    var agreement = await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposalId);
    Assert.True(agreement.Succeeded, agreement.Message);
    var stored = await db.EngagementFeeAgreements.AsNoTracking().SingleAsync(x => x.Id == agreement.Value);
    Assert.Equal(quote.Value, stored.QuotationVersionId);
  }

  [Fact]
  public async Task PortalFinanceAndReceiptDownloads_RespectExactClientAndEngagementScope()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    // Agreement A goes through the real acceptance → conversion → agreement path.
    var (_, _, proposalA) = await DraftProposalAsync(pg, w);
    var quoteA = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalA));
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quoteA.Value)).Succeeded);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalA)).Succeeded);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalA)).Succeeded);
    var offerA = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalA)).SentOfferSha256;
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalA,
      new("ACCEPTED", null, offerA, "A. Owner", "owner@gulf.example.test", "Signed acceptance letter"))).Succeeded);
    var converted = await PracticeCrmService.ConvertToClientDraftAsync(db, w.Prep, new(proposalA, "Scoped Group Client"));
    Assert.True(converted.Succeeded, converted.Message);
    var clientId = converted.Value;
    var agreementA = await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposalA);
    Assert.True(agreementA.Succeeded, agreementA.Message);

    // A second distinct lead supplies the proposal identity for the sibling engagement's agreement.
    var leadB = await PracticeCrmService.CreateLeadAsync(db, w.Prep, new CreateLeadRequest("Scoped Sibling Lead", "Referral", "B. Owner", "sibling@gulf.example.test"));
    Assert.True(leadB.Succeeded, leadB.Message);
    Assert.True((await PracticeCrmService.QualifyLeadAsync(db, w.Prep, leadB.Value)).Succeeded);
    var opportunityB = await PracticeCrmService.CreateOpportunityAsync(db, w.Prep, new CreateOpportunityRequest(leadB.Value, "FinancialStatementAudit",
      "SCOPED-SIBLING", "2026-01-01", "2026-12-31", 30000m, "QAR", 60m));
    Assert.True(opportunityB.Succeeded, opportunityB.Message);
    var proposalB = await PracticeCrmService.ReviseProposalAsync(db, w.Prep, new ReviseProposalRequest(opportunityB.Value, "AUDIT-2026-B",
      "Statutory audit of the sibling engagement", "Tax advisory", "Independent auditor's report and management letter",
      "Client supplies the trial balance", 1m, "QAR", "2026-01-01", "2026-12-31"));
    Assert.True(proposalB.Succeeded, proposalB.Message);

    var engagementA = Guid.CreateVersion7();
    var engagementB = Guid.CreateVersion7();
    db.Engagements.AddRange(
      new Engagement { Id = engagementA, FirmId = w.FirmId, PracticeClientId = clientId, Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow },
      new Engagement { Id = engagementB, FirmId = w.FirmId, PracticeClientId = clientId, Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    Assert.True((await FeeAgreementService.LinkEngagementAsync(db, w.Prep, agreementA.Value, engagementA)).Succeeded);

    var agreementB = new EngagementFeeAgreement
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, ProposalId = proposalB.Value, QuotationVersionId = Guid.CreateVersion7(),
      PracticeClientId = clientId, EngagementId = engagementB, Currency = "QAR", AgreedFee = 30000m,
      AdvancePercent = 50m, CreatedByUserId = w.PartnerUser.Id, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(1)
    };
    db.EngagementFeeAgreements.Add(agreementB);
    // Agreement A's advance/balance milestones already exist from the real agreement command.
    var milestoneA = await db.FeeMilestones.AsNoTracking().SingleAsync(x =>
      x.FirmId == w.FirmId && x.AgreementId == agreementA.Value && x.Kind == FeeMilestoneKinds.Advance);
    var milestoneB = new FeeMilestone { Id = Guid.CreateVersion7(), FirmId = w.FirmId, AgreementId = agreementB.Id, Kind = FeeMilestoneKinds.Advance, Amount = 15000m, CreatedAt = DateTimeOffset.UtcNow };
    db.FeeMilestones.Add(milestoneB);

    CommercialDocument Receipt(FeeMilestone milestone) => new()
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, FeeMilestoneId = milestone.Id, Kind = CommercialDocumentKinds.PaymentReceipt,
      TemplateVersion = "receipt-v1", ProfileVersion = 1, FileName = $"receipt-{milestone.Id:N}.docx",
      ContentType = CommercialDocumentRenderer.DocxContentType, Bytes = [1, 2, 3], Sha256Hex = new string('a', 64),
      CreatedByUserId = w.PartnerUser.Id, CreatedAt = DateTimeOffset.UtcNow
    };
    var receiptA = Receipt(milestoneA);
    var receiptB = Receipt(milestoneB);
    // An internal commercial artifact (comprehensive proposal) is never a client-downloadable document.
    var internalDoc = new CommercialDocument
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, FeeMilestoneId = milestoneA.Id, Kind = CommercialDocumentKinds.ComprehensiveProposal,
      QuotationVersionId = Guid.CreateVersion7(), TemplateVersion = "tender-v1", ProfileVersion = 1, FileName = "proposal.docx",
      ContentType = CommercialDocumentRenderer.DocxContentType, Bytes = [4, 5, 6], Sha256Hex = new string('b', 64),
      CreatedByUserId = w.PartnerUser.Id, CreatedAt = DateTimeOffset.UtcNow
    };
    db.CommercialDocuments.AddRange(receiptA, receiptB, internalDoc);

    var engagementScopedUser = User(w.FirmId, "engagement-scoped-client");
    engagementScopedUser.UserKind = "Client";
    var clientWideUser = User(w.FirmId, "client-wide-client");
    clientWideUser.UserKind = "Client";
    var nullClientUser = User(w.FirmId, "null-client-grant");
    nullClientUser.UserKind = "Client";
    db.Users.AddRange(engagementScopedUser, clientWideUser, nullClientUser);
    db.RoleGrants.AddRange(
      new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = engagementScopedUser.Id, Role = "ClientUser", ClientId = clientId, EngagementId = engagementA, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.PartnerUser.Id },
      new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = clientWideUser.Id, Role = "ClientUser", ClientId = clientId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.PartnerUser.Id },
      new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = nullClientUser.Id, Role = "ClientUser", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.PartnerUser.Id });
    await db.SaveChangesAsync();

    var engagementScopedActor = Actor(engagementScopedUser, "ClientUser");
    var clientWideActor = Actor(clientWideUser, "ClientUser");
    var nullClientActor = Actor(nullClientUser, "ClientUser");

    // The engagement-scoped grant sees exactly its engagement's agreement — never the sibling.
    var scopedFinance = await ClientPortalFinanceQuery.GetAsync(db, engagementScopedActor);
    Assert.True(scopedFinance.Succeeded, scopedFinance.Message);
    var scopedAgreement = Assert.Single(scopedFinance.Value!.Agreements);
    Assert.Equal(agreementA.Value, scopedAgreement.AgreementId);
    Assert.Equal(engagementA, scopedAgreement.EngagementId);
    Assert.StartsWith("25000", scopedAgreement.AgreedFee, StringComparison.Ordinal);

    // Its own engagement's official receipt downloads; the sibling engagement's does not.
    Assert.True((await CommercialDocumentService.GetAsync(db, engagementScopedActor, receiptA.Id)).Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, (await CommercialDocumentService.GetAsync(db, engagementScopedActor, receiptB.Id)).ErrorCode);
    // Internal commercial artifacts are never client-downloadable.
    Assert.Equal(ErrorCodes.ScopeDenied, (await CommercialDocumentService.GetAsync(db, engagementScopedActor, internalDoc.Id)).ErrorCode);

    // The client-wide grant covers both engagements of its client but still never internal artifacts.
    var wideFinance = await ClientPortalFinanceQuery.GetAsync(db, clientWideActor);
    Assert.True(wideFinance.Succeeded, wideFinance.Message);
    Assert.Equal(2, wideFinance.Value!.Agreements.Count);
    Assert.True((await CommercialDocumentService.GetAsync(db, clientWideActor, receiptB.Id)).Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, (await CommercialDocumentService.GetAsync(db, clientWideActor, internalDoc.Id)).ErrorCode);

    // A null-client ClientUser grant is not a firm-wide fallback.
    Assert.Equal(ErrorCodes.ScopeDenied, (await CommercialDocumentService.GetAsync(db, nullClientActor, receiptA.Id)).ErrorCode);
    Assert.Empty((await ClientPortalFinanceQuery.GetAsync(db, nullClientActor)).Value!.Agreements);
  }
}
