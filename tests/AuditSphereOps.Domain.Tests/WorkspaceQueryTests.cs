using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Engagements;
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
    var request = new CreateOpportunityRequest(lead.Value, "FinancialStatementAudit", "Entity",
      "2026-01-01", "2026-12-31", 123.45m, "QAR", OwnerUserId: fixture.Admin.Id, RequestId: Guid.NewGuid());
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
    Assert.Equal(123.45m, decimal.Parse(item.ExpectedFee, System.Globalization.CultureInfo.InvariantCulture));
    Assert.Equal("0", item.Revision);
    Assert.Null(item.ProposalId);
    var proposal = await PracticeCrmService.ReviseProposalAsync(setup, actor,
      new(item.Id, "Standard", "Scope", "", "Report", "", 123.45m, "QAR", "2026-01-01", "2026-12-31", 0));
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
      new(lead.Value, "FinancialStatementAudit", "Entity", "2026-01-01", "2026-12-31", 123.45m, "QAR"));
    Assert.True(opportunity.Succeeded, opportunity.Message);
    var proposal = await PracticeCrmService.ReviseProposalAsync(db, actor,
      new(opportunity.Value, "Standard", "Scope", "", "Report", "", 123.45m, "QAR", "2026-01-01", "2026-12-31", 0));
    Assert.True(proposal.Succeeded, proposal.Message);
    var loaded = await CommercialWorkspaceQuery.ProposalAsync(db, actor, proposal.Value);
    Assert.True(loaded.Succeeded, loaded.Message);
    Assert.Equal(123.45m, decimal.Parse(loaded.Value!.Fee, System.Globalization.CultureInfo.InvariantCulture));
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
