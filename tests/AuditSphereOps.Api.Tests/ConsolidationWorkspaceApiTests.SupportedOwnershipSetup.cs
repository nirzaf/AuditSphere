using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed partial class ConsolidationWorkspaceApiTests
{
  private sealed record SupportedOwnershipActors(
    Guid FirmId, Guid ClientId, AppUser Staff, AppUser Reviewer, AppUser Admin);

  private sealed record SupportedOwnershipFixture(
    SupportedOwnershipActors Seed, Guid GroupId, Guid ScopeId, Guid ChildClientId,
    Guid OutsideClientId, Guid FutureClientId, Guid BoundedClientId, AppUser Ungranted);

  private static async Task<SupportedOwnershipFixture> CreateSupportedOwnershipAsync(OwnedPostgresDatabase pg)
  {
    var admin = await SupportedAuditCompletionWorldBuilder.BootstrapAdminAsync(pg);
    var relationship = await SupportedAuditCompletionWorldBuilder.ProvisionFirmRoleAsync(pg, admin, "RelationshipManager");
    var partner = await SupportedAuditCompletionWorldBuilder.ProvisionFirmRoleAsync(pg, admin, "Partner");
    var manager = await SupportedAuditCompletionWorldBuilder.ProvisionFirmRoleAsync(pg, admin, "Manager");
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      var rate = await PracticeTimeService.ReviseRateCardAsync(setup, partner.Actor,
        new RateCardDraftRequest("Partner", "Audit", "QAR", 1000m));
      Assert.True(rate.Succeeded, rate.Message);
      var approved = await PracticeTimeService.ApproveRateCardAsync(setup, manager.Actor, rate.Value);
      Assert.True(approved.Succeeded, approved.Message);
      var profile = await CommercialDocumentService.SaveProfileAsync(setup, partner.Actor,
        new("Synthetic Audit Firm", "Test address", "firm@example.test", "", "#0F766E", "Synthetic test terms"));
      Assert.True(profile.Succeeded, profile.Message);
    }

    var clients = new List<Guid>();
    foreach (var suffix in new[] { "Parent", "Child", "Outside", "Future", "Bounded" })
    {
      var proposal = await SupportedAuditCompletionWorldBuilder.CreateAcceptedProposalAsync(pg,
        relationship.Actor, partner.Actor, manager.Actor, "Ownership" + suffix,
        "2026-01-01", "2026-12-31", _ => Task.CompletedTask);
      await using var conversion = new AuditSphereDbContext(pg.Options);
      var converted = await PracticeCrmService.ConvertToClientDraftAsync(conversion, relationship.Actor,
        new(proposal, "Synthetic ownership " + suffix, "Ownership " + suffix));
      Assert.True(converted.Succeeded, converted.Message);
      clients.Add(converted.Value);
    }
    Assert.Equal(5, clients.Distinct().Count());
    var parentId = clients[0];
    var childId = clients[1];
    var outsideId = clients[2];
    var futureId = clients[3];
    var boundedId = clients[4];

    await using var db = new AuditSphereDbContext(pg.Options);
    // The Manager owns the group grant. Administrator access remains oversight-only.
    var group = await ConsolidationService.CreateGroupAsync(db, manager.Actor,
      new("OWNERSHIP-GROUP", "Synthetic ownership group"));
    Assert.True(group.Succeeded, group.Message);
    Assert.Equal(1, await db.ClientGroups.Where(x => x.Id == group.Value).Select(x => x.Revision).SingleAsync());
    Assert.Equal(0, await db.ClientGroupMemberships.CountAsync(x => x.GroupId == group.Value));
    var memberships = new[]
    {
      new GroupMembershipRequest(group.Value, parentId, new(2020, 1, 1), null, "CONTROLLED", 100m, 100m, "MEMBER-EVIDENCE"),
      new GroupMembershipRequest(group.Value, childId, new(2020, 1, 1), null, "CONTROLLED", 100m, 100m, "MEMBER-EVIDENCE"),
      new GroupMembershipRequest(group.Value, futureId, new(2099, 1, 1), null, "CONTROLLED", 100m, 100m, "FUTURE-MEMBER-EVIDENCE"),
      new GroupMembershipRequest(group.Value, boundedId, new(2026, 7, 1), new(2026, 12, 31), "CONTROLLED", 100m, 100m, "BOUNDED-MEMBER-EVIDENCE")
    };
    for (var index = 0; index < memberships.Length; index++)
    {
      var added = await ConsolidationService.AddMembershipAsync(db, manager.Actor, memberships[index]);
      Assert.True(added.Succeeded, added.Message);
      Assert.Equal(index + 1, await db.ClientGroupMemberships.CountAsync(x =>
        x.GroupId == group.Value && x.Status == AccountingWorkflowStates.Approved));
      Assert.Equal(index + 2, await db.ClientGroups.Where(x => x.Id == group.Value).Select(x => x.Revision).SingleAsync());
    }

    var adminUser = await db.Users.AsNoTracking().SingleAsync(x => x.Id == admin.Actor.UserId);
    async Task<Guid> EnsureUser(string suffix)
    {
      var ensured = await RoleAdministrationService.EnsureUserAsync(db, admin.Actor,
        new(adminUser.TenantId, Guid.NewGuid().ToString("D"), $"ownership-{suffix}@auditsphere.test",
          "Synthetic ownership " + suffix, "Staff", "APPROVED_ROSTER"));
      Assert.True(ensured.Succeeded, ensured.Message);
      return ensured.Value;
    }
    var reviewerId = await EnsureUser("reader");
    var grant = await RoleAssignmentService.AssignAsync(db, admin.Actor,
      new(reviewerId, "AccountingPreparer", "GROUP", GroupId: group.Value,
        Reason: "Synthetic ownership workspace read-only coverage", ConfirmScopeExpansion: true), DateTimeOffset.UtcNow);
    Assert.True(grant.Succeeded, grant.Message);
    var ungrantedId = await EnsureUser("ungranted");

    var period = await ClientAccountingService.CreatePeriodAsync(db, manager.Actor,
      new(parentId, "FY2026", new(2026, 1, 1), new(2026, 12, 31), "ANNUAL", "QAR"));
    Assert.True(period.Succeeded, period.Message);
    var scope = await ConsolidationService.CreateScopeAsync(db, manager.Actor,
      new(group.Value, period.Value, "QAR", ConsolidationCalculator.RestrictedMethod,
        "INITIAL_CONSOLIDATION", ExpectedGroupRevision: 5));
    Assert.True(scope.Succeeded, scope.Message);
    var retainedScope = await db.ConsolidationScopeVersions.AsNoTracking().SingleAsync(x => x.Id == scope.Value);
    Assert.Equal(AccountingWorkflowStates.Draft, retainedScope.Status);
    Assert.Equal(1, retainedScope.Version);
    Assert.Equal(5, retainedScope.GroupRevision);

    // Reload identities after grants so each host starts from the current session epoch.
    var staff = await db.Users.AsNoTracking().SingleAsync(x => x.Id == manager.Actor.UserId);
    var reviewer = await db.Users.AsNoTracking().SingleAsync(x => x.Id == reviewerId);
    var administrator = await db.Users.AsNoTracking().SingleAsync(x => x.Id == admin.Actor.UserId);
    var ungranted = await db.Users.AsNoTracking().SingleAsync(x => x.Id == ungrantedId);
    return new(new(admin.FirmId, parentId, staff, reviewer, administrator), group.Value, scope.Value,
      childId, outsideId, futureId, boundedId, ungranted);
  }
}
