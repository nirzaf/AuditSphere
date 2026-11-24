using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed class WorkspaceQueryTests
{
  [Fact]
  public async Task OpportunityRequestIdentityReconcilesConcurrentCreationWithoutCrossFirmLeakage()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(fixture.Admin, "Administrator");
    await using var setup = new AuditSphereDbContext(pg.Options);
    var lead = await PracticeCrmService.CreateLeadAsync(setup, actor, new("Discovery", "Referral"));
    Assert.True((await PracticeCrmService.QualifyLeadAsync(setup, actor, lead.Value)).Succeeded);
    var request = new CreateOpportunityRequest(lead.Value, "FinancialStatementAudit",
      "Audit of the standalone financial statements of Discovery.",
      "2026-01-01", "2026-12-31", null, null, OwnerUserId: fixture.Admin.Id, RequestId: Guid.NewGuid(),
      ProspectiveLegalName: "Discovery", EntityScopeConfirmed: true);
    async Task<AuditSphereOps.Domain.Shared.CommandResult<Guid>> CreateAsync()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await PracticeCrmService.CreateOpportunityAsync(db, actor, request);
    }
    var results = await Task.WhenAll(CreateAsync(), CreateAsync());
    Assert.All(results, r => Assert.True(r.Succeeded, r.Message));
    Assert.Equal(results[0].Value, results[1].Value);
    Assert.Single(await setup.Opportunities.Where(o => o.LeadId == lead.Value).ToListAsync());
    Assert.False((await PracticeCrmService.CreateOpportunityAsync(setup, actor, request with { ExpectedFee = 124m })).Succeeded);
    var workspace = await CommercialWorkspaceQuery.LeadAsync(setup, actor, lead.Value);
    Assert.True(workspace.Succeeded, workspace.Message);
    var item = Assert.Single(workspace.Value!.Opportunities);
    Assert.Null(item.ExpectedFee);
    Assert.Equal("0", item.Revision);
    Assert.Null(item.ProposalId);
    Assert.False(item.CanPrepareCommercial);
    var manager = PbcSeed.Actor(fixture.Admin, "Manager");
    var partner = PbcSeed.Actor(fixture.Reviewer, "Partner");
    var accepted = await CommercialAcceptanceTestSupport.AcceptOpportunityAsync(setup, manager, partner,
      fixture.FirmId, item.Id);
    Assert.True(accepted.Succeeded, accepted.Message);
    var proposal = await PracticeCrmService.ReviseProposalAsync(setup, actor,
      new(item.Id, "Standard", item.EntityScope, "", "Report", "", 123.45m, "QAR", "2026-01-01", "2026-12-31", 0));
    Assert.True(proposal.Succeeded, proposal.Message);
    var updated = await CommercialWorkspaceQuery.LeadAsync(setup, actor, lead.Value);
    Assert.Equal(proposal.Value, Assert.Single(updated.Value!.Opportunities).ProposalId);
    Assert.Equal("1", Assert.Single(updated.Value.Opportunities).Revision);
    Assert.False((await CommercialWorkspaceQuery.LeadAsync(setup, PbcSeed.Actor(foreign.Admin, "Administrator"), lead.Value)).Succeeded);
    Assert.False((await CommercialWorkspaceQuery.LeadAsync(setup, PbcSeed.Actor(fixture.Staff, "Staff"), lead.Value)).Succeeded);
    Assert.False((await PracticeCrmService.CreateOpportunityAsync(setup, actor, request with { RequestId = Guid.Empty })).Succeeded);
  }

  [Fact]
  public async Task ProposalProjectionIsFirmScopedAndPreservesReviewedFee()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var actor = PbcSeed.Actor(fixture.Admin, "Administrator");
    var lead = await PracticeCrmService.CreateLeadAsync(db, actor, new("Proposal lead", "Referral"));
    Assert.True((await PracticeCrmService.QualifyLeadAsync(db, actor, lead.Value)).Succeeded);
    var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, actor,
      new(lead.Value, "FinancialStatementAudit", "Audit of the standalone financial statements of Proposal lead.",
        "2026-01-01", "2026-12-31", null, null, ProspectiveLegalName: "Proposal lead", EntityScopeConfirmed: true));
    Assert.True(opportunity.Succeeded, opportunity.Message);
    var acceptance = await CommercialAcceptanceTestSupport.AcceptOpportunityAsync(db,
      PbcSeed.Actor(fixture.Admin, "Manager"), PbcSeed.Actor(fixture.Reviewer, "Partner"), fixture.FirmId, opportunity.Value);
    Assert.True(acceptance.Succeeded, acceptance.Message);
    var proposal = await PracticeCrmService.ReviseProposalAsync(db, actor,
      new(opportunity.Value, "Standard", "Audit of the standalone financial statements of Proposal lead.", "", "Report", "", 123.45m, "QAR", "2026-01-01", "2026-12-31", 0));
    Assert.True(proposal.Succeeded, proposal.Message);
    var loaded = await CommercialWorkspaceQuery.ProposalAsync(db, actor, proposal.Value);
    Assert.True(loaded.Succeeded, loaded.Message);
    Assert.Equal(123.45m, decimal.Parse(loaded.Value!.Fee, System.Globalization.CultureInfo.InvariantCulture));
    Assert.Equal(123.45m, (await db.Opportunities.AsNoTracking().SingleAsync(x => x.Id == opportunity.Value)).ExpectedFee);
    Assert.False(loaded.Value.CanApprove);
    Assert.Single(loaded.Value.Versions);
    Assert.False((await CommercialWorkspaceQuery.ProposalAsync(db, PbcSeed.Actor(foreign.Admin, "Administrator"), proposal.Value)).Succeeded);
    Assert.False((await CommercialWorkspaceQuery.ProposalAsync(db, PbcSeed.Actor(fixture.Staff, "Staff"), proposal.Value)).Succeeded);
  }

  [Fact]
  public async Task CommercialLeadsRequireFirmScopeAndBoundSearchResults()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var admin = PbcSeed.Actor(fixture.Admin, "Administrator");
    Assert.True((await PracticeCrmService.CreateLeadAsync(db, admin, new("Alpha lead", "Referral"))).Succeeded);
    Assert.True((await PracticeCrmService.CreateLeadAsync(db, PbcSeed.Actor(foreign.Admin, "Administrator"), new("Foreign lead", "Referral"))).Succeeded);
    var result = await PracticeLeadQuery.PageAsync(db, admin, "alpha");
    Assert.Equal("Alpha lead", Assert.Single(result.Value!.Items).Name);
    Assert.Equal(1, result.Value.Total);
    Assert.False((await PracticeLeadQuery.PageAsync(db, PbcSeed.Actor(fixture.Staff, "Staff"))).Succeeded);
    Assert.False((await PracticeLeadQuery.PageAsync(db, admin, pageSize: 101)).Succeeded);
    Assert.Empty((await PracticeLeadQuery.PageAsync(db, admin, "foreign")).Value!.Items);

    // Summaries count the complete filtered cohort, never only the loaded page.
    for (var i = 0; i < 27; i++)
      Assert.True((await PracticeCrmService.CreateLeadAsync(db, admin, new($"Bulk lead {i:00}", i % 2 == 0 ? "Email" : "Phone",
        OwnerUserId: i < 3 ? fixture.Admin.Id : null))).Succeeded);
    var qualifiedId = (await db.Leads.AsNoTracking().SingleAsync(x => x.Name == "Bulk lead 00")).Id;
    Assert.True((await PracticeCrmService.QualifyLeadAsync(db, admin, qualifiedId)).Succeeded);
    var bulk = (await PracticeLeadQuery.PageAsync(db, admin, "bulk")).Value!;
    Assert.Equal((27, 25), (bulk.Total, bulk.Items.Count));
    Assert.Equal(new PracticeLeadSummary(27, 26, 1), bulk.Summary);
    Assert.Equal(14, (await PracticeLeadQuery.PageAsync(db, admin, "bulk", filter: new(Source: "Email"))).Value!.Summary!.Matching);
    var mine = (await PracticeLeadQuery.PageAsync(db, admin, "bulk", filter: new(Owner: "me"))).Value!;
    Assert.Equal(3, mine.Total);
    Assert.All(mine.Items, x => Assert.Equal((fixture.Admin.Id, PracticeLeadOwnerStates.Active), (x.OwnerUserId!.Value, x.OwnerState)));
    var unassigned = (await PracticeLeadQuery.PageAsync(db, admin, "bulk", filter: new(Owner: "unassigned"))).Value!;
    Assert.Equal(24, unassigned.Total);
    Assert.All(unassigned.Items, x => Assert.Equal((null, PracticeLeadOwnerStates.Unassigned), (x.OwnerName, x.OwnerState)));
    Assert.Equal(new PracticeLeadSummary(1, 0, 1),
      (await PracticeLeadQuery.PageAsync(db, admin, "bulk", filter: new(Status: "QUALIFIED"))).Value!.Summary);
    foreach (var invalid in new PracticeLeadFilter[] { new(Status: "WON"), new(Source: "web form"), new(Owner: "someone") })
      Assert.Equal("request.invalid", (await PracticeLeadQuery.PageAsync(db, admin, filter: invalid)).ErrorCode);
    // A narrowing filter grants nothing to a user without firm-wide commercial access.
    Assert.False((await PracticeLeadQuery.PageAsync(db, PbcSeed.Actor(fixture.Staff, "Staff"), filter: new(Owner: "me"))).Succeeded);
  }

  [Fact]
  public async Task CommercialPipelineCountsEachOpportunityOnceWithCompleteCurrencyTotals()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var admin = PbcSeed.Actor(fixture.Admin, "Administrator");
    async Task<Guid> OpenAsync(AuditSphereOps.Application.Abstractions.ActorContext actor, string leadName,
      Guid? owner = null, string route = "FinancialStatementAudit")
    {
      var lead = await db.Leads.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Name == leadName);
      var leadId = lead?.Id ?? (await PracticeCrmService.CreateLeadAsync(db, actor, new(leadName, "Referral"))).Value;
      if (lead is null) Assert.True((await PracticeCrmService.QualifyLeadAsync(db, actor, leadId)).Succeeded);
      var created = await PracticeCrmService.CreateOpportunityAsync(db, actor,
        new(leadId, route, $"{route} services for {leadName}.", "2026-01-01", "2026-12-31", null, null,
          OwnerUserId: owner, ProspectiveLegalName: leadName, EntityScopeConfirmed: true));
      Assert.True(created.Succeeded, created.Message);
      return created.Value;
    }
    var proposed = await OpenAsync(admin, "Pipeline lead", fixture.Admin.Id);
    var sameClient = await OpenAsync(admin, "Pipeline lead");
    var additionalClientOpportunities = new List<Guid>();
    for (var i = 0; i < 11; i++) additionalClientOpportunities.Add(await OpenAsync(admin, "Pipeline lead"));
    await OpenAsync(PbcSeed.Actor(foreign.Admin, "Administrator"), "Foreign pipeline lead");

    async Task<Guid> PrepareProposalAsync(Guid opportunityId, decimal fee, string currency, long expectedRevision)
    {
      var opportunity = await db.Opportunities.AsNoTracking().SingleAsync(x => x.Id == opportunityId);
      var accepted = await CommercialAcceptanceTestSupport.AcceptOpportunityAsync(db,
        PbcSeed.Actor(fixture.Admin, "Manager"), PbcSeed.Actor(fixture.Reviewer, "Partner"), fixture.FirmId, opportunityId);
      Assert.True(accepted.Succeeded, accepted.Message);
      if (opportunityId == sameClient)
        Assert.Equal(AcceptancePaths.Continuance,
          (await db.AcceptanceDecisions.AsNoTracking().SingleAsync(x => x.Id == accepted.Value)).Path);
      var proposal = await PracticeCrmService.ReviseProposalAsync(db, admin,
        new(opportunityId, "Standard", opportunity.EntityScope, "", "Report", "", fee, currency,
          opportunity.PeriodStart, opportunity.PeriodEnd, expectedRevision));
      Assert.True(proposal.Succeeded, proposal.Message);
      return proposal.Value;
    }
    await PrepareProposalAsync(proposed, 15000m, "QAR", 0);
    await PrepareProposalAsync(proposed, 15000m, "QAR", 1);
    await PrepareProposalAsync(sameClient, 2500.5m, "USD", 0);
    static decimal Amount(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    var board = (await CommercialPipelineQuery.GetAsync(db, admin)).Value!;
    Assert.Equal(CommercialPipelineQuery.Stages, board.Stages.Select(s => s.Stage));
    Assert.Equal(13, board.Stages.Sum(s => s.Count));
    Assert.Equal("Pipeline lead", Assert.Single(board.Clients!).Name);
    Assert.False(board.ClientsHaveMore);
    Assert.DoesNotContain(board.Stages.SelectMany(s => s.Items), c => c.LeadName == "Foreign pipeline lead");
    var discovery = board.Stages[0];
    Assert.Equal((11, 10, true, 11), (discovery.Count, discovery.Items.Count, discovery.HasMore, discovery.UnavailableCurrencyCount));
    Assert.Empty(discovery.Totals);
    var proposal = board.Stages[1];
    var card = Assert.Single(proposal.Items, x => x.Id == proposed);
    Assert.Equal(2, proposal.Count);
    Assert.Equal((proposed, "2", "DRAFT", 2), (card.Id, card.ProposalRevision, card.ProposalStatus, card.ProposalRevisions));
    Assert.Equal((fixture.Admin.Id, PracticeLeadOwnerStates.Active), (card.OwnerUserId!.Value, card.OwnerState));
    Assert.Equal([("QAR", 1, 15000m), ("USD", 1, 2500.5m)], proposal.Totals.Select(t => (t.Currency, t.Count, Amount(t.ExpectedFee))));
    Assert.All(board.Stages.Skip(2), stage => Assert.Equal((0, 0), (stage.Count, stage.Items.Count)));

    // A continuation returns the rest of its stage; reused after a filter change it is refused, never mixed.
    var last = discovery.Items[^1];
    var cursor = new CommercialPipelineCursor("DISCOVERY", last.CreatedAt, last.Id);
    var rest = Assert.Single((await CommercialPipelineQuery.GetAsync(db, admin, cursor: cursor)).Value!.Stages);
    Assert.Equal((11, 1, false), (rest.Count, rest.Items.Count, rest.HasMore));
    Assert.Empty(discovery.Items.Select(c => c.Id).Intersect(rest.Items.Select(c => c.Id)));
    Assert.Equal("request.invalid", (await CommercialPipelineQuery.GetAsync(db, admin, new(Currency: "USD"), cursor)).ErrorCode);

    var prospectClientId = (await db.Opportunities.AsNoTracking().Where(x => x.Id == proposed)
      .Select(x => x.AcceptanceClientId).SingleAsync())!.Value;
    var clientBoard = (await CommercialPipelineQuery.GetAsync(db, admin, new(ClientId: prospectClientId))).Value!;
    var discoveryPage = Assert.Single(clientBoard.Stages, s => s.Stage == CrmStates.OpportunityDiscovery);
    Assert.Equal((11, 10, true), (discoveryPage.Count, discoveryPage.Items.Count, discoveryPage.HasMore));
    var continuation = await CommercialPipelineQuery.GetAsync(db, admin, new(ClientId: prospectClientId),
      new(CrmStates.OpportunityDiscovery, discoveryPage.Items[^1].CreatedAt, discoveryPage.Items[^1].Id));
    Assert.True(continuation.Succeeded, continuation.Message);
    var remainingDiscovery = Assert.Single(continuation.Value!.Stages);
    Assert.Equal((11, 1, false), (remainingDiscovery.Count, remainingDiscovery.Items.Count, remainingDiscovery.HasMore));
    var clientCards = clientBoard.Stages.SelectMany(s => s.Items).Concat(remainingDiscovery.Items).ToList();
    var expectedClientOpportunities = new HashSet<Guid> { proposed, sameClient };
    expectedClientOpportunities.UnionWith(additionalClientOpportunities);
    Assert.Equal(expectedClientOpportunities, clientCards.Select(c => c.Id).ToHashSet());
    Assert.All(clientCards, c => Assert.False(string.IsNullOrEmpty(c.ClientName)));
    Assert.Equal(sameClient, Assert.Single((await CommercialPipelineQuery.GetAsync(db, admin, new(Currency: "usd"))).Value!
      .Stages.SelectMany(s => s.Items)).Id);
    Assert.Equal(1, (await CommercialPipelineQuery.GetAsync(db, admin, new(Owner: "me"))).Value!.Stages.Sum(s => s.Count));
    Assert.Equal(13, (await CommercialPipelineQuery.GetAsync(db, admin, new(ServiceRoute: "FinancialStatementAudit"))).Value!.Stages.Sum(s => s.Count));
    foreach (var invalid in new CommercialPipelineFilter[] { new(Owner: "someone"), new(Currency: "US"), new(ServiceRoute: " ") })
      Assert.Equal("request.invalid", (await CommercialPipelineQuery.GetAsync(db, admin, invalid)).ErrorCode);
    Assert.Equal("request.invalid", (await CommercialPipelineQuery.GetAsync(db, admin, cursor: cursor with { Stage = "CLOSED" })).ErrorCode);
    Assert.False((await CommercialPipelineQuery.GetAsync(db, PbcSeed.Actor(fixture.Staff, "Staff"), new(Owner: "me"))).Succeeded);
  }

  [Fact]
  public async Task ConcurrentEngagementCreationIsSingleBlockedDraft_AndActivationRequiresAcceptance()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(fixture.FirmId, fixture.Admin, "Partner"));
      await db.SaveChangesAsync();
    }
    var actor = PbcSeed.Actor(fixture.Admin, "Partner");
    var request = new AuditSphereOps.Application.Acceptance.CreateEngagementDraftRequest(fixture.ClientId,
      "FinancialStatementAudit", "2026-01-01", "2026-12-31", "Standard");
    async Task<AuditSphereOps.Domain.Shared.CommandResult<Guid>> CreateAsync()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await AuditSphereOps.Application.Acceptance.EngagementLifecycleService.CreateDraftAsync(db, actor, request);
    }
    var results = await Task.WhenAll(CreateAsync(), CreateAsync());
    Assert.All(results, result => Assert.True(result.Succeeded, result.Message));
    Assert.Equal(results[0].Value, results[1].Value);
    await using var verify = new AuditSphereDbContext(pg.Options);
    var created = Assert.Single(await verify.Engagements.Where(e => e.ServiceRoute == request.ServiceRoute && e.FirmId == fixture.FirmId).ToListAsync());
    Assert.Equal("Draft", created.Status);
    Assert.True(created.ProfessionalWorkBlocked);
    Assert.False((await AuditSphereOps.Application.Acceptance.EngagementLifecycleService.ActivateAsync(verify, actor, created.Id)).Succeeded);
    Assert.False((await AuditSphereOps.Application.Acceptance.EngagementLifecycleService.CreateDraftAsync(verify, actor, request with { ServiceProfileId = "Other" })).Succeeded);
    var client = await WorkspaceQuery.ClientAsync(verify, actor, fixture.ClientId);
    Assert.True(client.Value!.CanCreateEngagement);
    var engagement = await WorkspaceQuery.EngagementAsync(verify, actor, created.Id);
    Assert.True(engagement.Value!.CanActivate);
  }

  [Fact]
  public async Task BudgetDraftProjectionPreservesMoneyAndSelfApprovalFence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.RateCardVersions.Add(new AuditSphereOps.Domain.Practice.RateCardVersion
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, Role = "Senior", Activity = "AUDIT", Currency = "QAR",
      RatePerHour = 123.45m, Status = "APPROVED", CreatedByUserId = fixture.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var actor = PbcSeed.Actor(fixture.Admin, "Administrator");
    var request = new ReviseBudgetRequest(fixture.EngagementId, "QAR", [new("Senior", "AUDIT", 60, "PLANNING")], 0);
    var result = await PracticeTimeService.ReviseBudgetAsync(db, actor, request);
    Assert.True(result.Succeeded, result.Message);
    Assert.False((await PracticeTimeService.ReviseBudgetAsync(db, actor, request)).Succeeded);
    var planning = await EngagementPlanningQuery.GetAsync(db, actor, fixture.EngagementId);
    Assert.True(planning.Succeeded, planning.Message);
    Assert.NotNull(planning.Value!.Draft);
    Assert.True(planning.Value.CanManageStaffing);
    Assert.False(planning.Value.CanPrepareBudget);
    Assert.Equal(123.45m, decimal.Parse(Assert.Single(planning.Value.Draft.Lines).Cost, System.Globalization.CultureInfo.InvariantCulture));
    Assert.False(planning.Value.Draft.CanApprove);
    Assert.Equal("1", planning.Value.LatestBudgetVersion);
    Assert.False((await EngagementPlanningQuery.ApproveBudgetAsync(db, actor, fixture.EngagementId, result.Value)).Succeeded);
    Assert.False((await EngagementPlanningQuery.ApproveBudgetAsync(db, actor, Guid.NewGuid(), result.Value)).Succeeded);
    db.RoleGrants.Add(new AuditSphereOps.Domain.Security.RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, UserId = fixture.Admin.Id,
      Role = "Manager", ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
      GrantedByUserId = fixture.Admin.Id, GrantedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var managerPlanning = await EngagementPlanningQuery.GetAsync(db, actor, fixture.EngagementId);
    Assert.True(managerPlanning.Succeeded, managerPlanning.Message);
    Assert.True(managerPlanning.Value!.CanPrepareBudget);
  }

  [Fact]
  public async Task StaffingCommandsBindExactEngagementAndReuseExistingAssignment()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var actor = PbcSeed.Actor(fixture.Admin, "Administrator");
    var request = new AssignStaffRequest(fixture.EngagementId, fixture.Reviewer.Id, "STAFF_ASSOCIATE");
    var first = await StaffingService.AssignAsync(db, actor, request);
    Assert.True(first.Succeeded, first.Message);
    var repeated = await StaffingService.AssignAsync(db, actor, request);
    Assert.Equal(first.Value, repeated.Value);
    Assert.False((await EngagementPlanningQuery.RevokeAsync(db, actor, Guid.NewGuid(), first.Value)).Succeeded);
    Assert.True((await EngagementPlanningQuery.RevokeAsync(db, actor, fixture.EngagementId, first.Value)).Succeeded);
    Assert.True((await EngagementPlanningQuery.RevokeAsync(db, actor, fixture.EngagementId, first.Value)).Succeeded);
    var assignment = await db.EngagementStaffAssignments.SingleAsync(a => a.Id == first.Value);
    Assert.NotNull(assignment.RevokedAt);
    var reviewer = await db.Users.SingleAsync(u => u.Id == fixture.Reviewer.Id);
    Assert.True(reviewer.SessionEpoch > fixture.Reviewer.SessionEpoch);
  }

  [Fact]
  public async Task ContactCommandRevisionFencePreventsRepeatedCreation()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var actor = PbcSeed.Actor(fixture.Admin, "Administrator");
    var workspace = await WorkspaceQuery.ClientAsync(db, actor, fixture.ClientId);
    Assert.True(workspace.Succeeded, workspace.Message);
    Assert.True(workspace.Value!.CanManageContacts);
    var request = new CreateClientContactRequest(fixture.ClientId, "Contact", "contact@example.test", "CFO",
      ExpectedSafetyGeneration: long.Parse(workspace.Value.SafetyGeneration));
    var first = await PracticeCrmService.CreateClientContactAsync(db, actor, request);
    Assert.True(first.Succeeded, first.Message);
    var repeated = await PracticeCrmService.CreateClientContactAsync(db, actor, request);
    Assert.False(repeated.Succeeded);
    Assert.Equal("generation.stale", repeated.ErrorCode);
    Assert.Single(await db.ClientContacts.Where(c => c.PracticeClientId == fixture.ClientId).ToListAsync());
    var refreshed = await WorkspaceQuery.ClientAsync(db, actor, fixture.ClientId);
    Assert.Equal("Contact", Assert.Single(refreshed.Value!.Contacts).Name);
    Assert.NotEqual(workspace.Value.SafetyGeneration, refreshed.Value.SafetyGeneration);
  }

  [Fact]
  public async Task EngagementGrantCannotReadClientOrSibling_AndRevocationClearsAccess()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var sibling = new Engagement { Id = Guid.NewGuid(), FirmId = fixture.FirmId,
      PracticeClientId = fixture.ClientId, ServiceRoute = "PRIVATE SIBLING", CreatedAt = DateTimeOffset.UtcNow };
    db.Engagements.Add(sibling);
    db.EngagementHolds.Add(new EngagementHold { Id = Guid.NewGuid(), FirmId = fixture.FirmId,
      EngagementId = fixture.EngagementId, HoldKind = "Acceptance", Reason = "Review required", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var actor = PbcSeed.Actor(fixture.Staff, "Staff");
    var result = await WorkspaceQuery.EngagementAsync(db, actor, fixture.EngagementId);
    Assert.True(result.Succeeded, result.Message);
    Assert.Equal("Review required", Assert.Single(result.Value!.Holds).Reason);
    var planning = await EngagementPlanningQuery.GetAsync(db, actor, fixture.EngagementId);
    Assert.True(planning.Succeeded, planning.Message);
    Assert.False(planning.Value!.CanManageStaffing);
    Assert.Empty(planning.Value.Candidates);
    Assert.Null(planning.Value.Budget);
    Assert.Equal("UNAVAILABLE", planning.Value.BudgetState);
    Assert.False((await EngagementPlanningQuery.GetAsync(db, actor, sibling.Id)).Succeeded);
    Assert.False((await EngagementPlanningQuery.GetAsync(db, actor, foreign.EngagementId)).Succeeded);
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, fixture.ClientId)).Succeeded);
    Assert.False((await WorkspaceQuery.EngagementAsync(db, actor, sibling.Id)).Succeeded);
    Assert.False((await WorkspaceQuery.EngagementAsync(db, actor, foreign.EngagementId)).Succeeded);
    Assert.False((await WorkspaceQuery.EngagementAsync(db, PbcSeed.Actor(fixture.Client, "ClientUser"), fixture.EngagementId)).Succeeded);
    var grant = await db.RoleGrants.SingleAsync(g => g.UserId == fixture.Staff.Id);
    grant.RevokedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    Assert.False((await WorkspaceQuery.EngagementAsync(db, actor, fixture.EngagementId)).Succeeded);
  }
}
