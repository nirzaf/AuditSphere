using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class RoleAssignmentTests
{
  private static async Task<AppUser> StaffAsync(TenantAdministrationFixture f, string email = "staff@example.test")
  {
    await using var db = f.Db();
    var user = TenantAdministrationFixture.User(f.FirmId, f.TenantId, email, "Staff Member");
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return user;
  }

  [Fact]
  public async Task Preview_ShowsDiffAndExpansion_AndSaveRequiresExplicitConfirmation()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    var staff = await StaffAsync(f);
    await using var db = f.Db();
    var request = new RoleAssignmentRequest(staff.Id, "Manager", "CLIENT", f.ClientId, Reason: "Assigned to client team");
    var preview = await RoleAssignmentService.PreviewAsync(db, f.AdminActor, request, DateTimeOffset.UtcNow);
    Assert.True(preview.Succeeded, preview.Message);
    Assert.True(preview.Value!.ScopeExpansion);
    Assert.Empty(preview.Value.CurrentAccess);
    Assert.Contains("Review engagement work", preview.Value.AddedCapabilities);
    Assert.Contains(preview.Value.IndependenceImpact, x => x.Contains("independence", StringComparison.OrdinalIgnoreCase));

    var unconfirmed = await RoleAssignmentService.AssignAsync(db, f.AdminActor, request, DateTimeOffset.UtcNow);
    Assert.Equal("roles.confirmation-required", unconfirmed.ErrorCode);
    var noReason = await RoleAssignmentService.AssignAsync(db, f.AdminActor, request with { Reason = "", ConfirmScopeExpansion = true }, DateTimeOffset.UtcNow);
    Assert.Equal("roles.reason-required", noReason.ErrorCode);
    var digest = RoleAssignmentReviewDigest.Compute(preview.Value!, request);
    var saved = await RoleAssignmentService.AssignAsync(db, f.AdminActor, request with { ConfirmScopeExpansion = true }, DateTimeOffset.UtcNow, expectedReviewDigest: digest);
    Assert.True(saved.Succeeded, saved.Message);
    var changed = await RoleAssignmentService.AssignAsync(db, f.AdminActor, request with { ConfirmScopeExpansion = true }, DateTimeOffset.UtcNow, expectedReviewDigest: digest);
    Assert.Equal(ErrorCodes.StaleRevision, changed.ErrorCode);
    db.ChangeTracker.Clear();
    var grant = await db.RoleGrants.SingleAsync(x => x.Id == saved.Value!.GrantId);
    Assert.Equal("Assigned to client team", grant.Reason);
    Assert.Contains(await db.RoleGrantChangeEvidences.ToListAsync(), x => x.RoleGrantId == grant.Id && x.Reason == "Assigned to client team");
    Assert.Contains(await db.Microsoft365AdministrationEvents.ToListAsync(), x => x.Operation == "ROLE_GRANTED" && x.TargetUserId == staff.Id);

    // Narrowing: replace client-scope with an engagement-scope grant is a reduction, not an expansion.
    var narrow = await RoleAssignmentService.PreviewAsync(db, f.AdminActor,
      new(staff.Id, "Manager", "ENGAGEMENT", f.ClientId, f.EngagementId, ReplacesGrantId: grant.Id, Reason: "Narrow scope"), DateTimeOffset.UtcNow);
    Assert.False(narrow.Value!.ScopeExpansion);
    Assert.True(narrow.Value.ScopeReduction);
  }

  [Fact]
  public async Task SelfElevationAndIndependenceHoldAreRefused()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await using var db = f.Db();
    var self = await RoleAssignmentService.AssignAsync(db, f.AdminActor,
      new(f.Admin.Id, "Partner", "FIRM_WIDE", Reason: "Self promotion", ConfirmScopeExpansion: true), DateTimeOffset.UtcNow);
    Assert.Equal("roles.blocked", self.ErrorCode);
    Assert.Contains("administrator", self.Message, StringComparison.OrdinalIgnoreCase);

    var staff = await StaffAsync(f);
    db.SpecialistClearances.Add(new Domain.Acceptance.SpecialistClearance
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId, Area = "Independence",
      SpecialistName = "Ethics partner", Status = "HOLD", CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var held = await RoleAssignmentService.AssignAsync(db, f.AdminActor,
      new(staff.Id, "Staff", "CLIENT", f.ClientId, Reason: "Independence test", ConfirmScopeExpansion: true), DateTimeOffset.UtcNow);
    Assert.Equal("roles.blocked", held.ErrorCode);
    Assert.Equal(0, await db.RoleGrants.CountAsync(x => x.UserId == staff.Id));
  }

  [Fact]
  public async Task LastAdministratorCannotBeReplacedAway()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await using var db = f.Db();
    // Leave the primary admin as the only administrator.
    var secondGrant = await db.RoleGrants.SingleAsync(x => x.UserId == f.SecondAdmin.Id);
    Assert.True((await RoleAdministrationService.RevokeRoleGrantAsync(db, f.AdminActor, new(secondGrant.Id, null, "Rotation"))).Succeeded);
    var soleGrant = await db.RoleGrants.AsNoTracking().SingleAsync(x => x.UserId == f.Admin.Id && x.RevokedAt == null);

    // Another administrator is needed to act; create one with a grant, then have it try to demote the last firm admin.
    var helper = TenantAdministrationFixture.User(f.FirmId, f.TenantId, "helper@example.test", "Helper Admin");
    db.Users.Add(helper);
    db.RoleGrants.Add(new RoleGrant { Id = Guid.CreateVersion7(), FirmId = f.FirmId, UserId = helper.Id, Role = "Administrator",
      ClientId = f.ClientId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = f.Admin.Id }); // client-scoped only
    await db.SaveChangesAsync();
    var demote = await RoleAssignmentService.PreviewAsync(db, f.AdminActor,
      new(f.Admin.Id, "Partner", "FIRM_WIDE", ReplacesGrantId: soleGrant.Id, Reason: "Demote"), DateTimeOffset.UtcNow);
    Assert.Contains(demote.Value!.BlockingReasons, x => x.Contains("replacement firm administrator", StringComparison.OrdinalIgnoreCase) ||
      x.Contains("self-change", StringComparison.OrdinalIgnoreCase));
    var revoke = await RoleAdministrationService.RevokeRoleGrantAsync(db, f.AdminActor, new(soleGrant.Id));
    Assert.Equal("roles.last-admin", revoke.ErrorCode);
  }

  [Fact]
  public async Task ClientEngagementAndGroupScopesStayIsolated()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    var clientStaff = await StaffAsync(f, "client.staff@example.test");
    var engagementStaff = await StaffAsync(f, "engagement.staff@example.test");
    var groupStaff = await StaffAsync(f, "group.staff@example.test");
    await using var db = f.Db();
    var groupId = Guid.NewGuid();
    var otherGroupId = Guid.NewGuid();
    db.ClientGroups.AddRange(
      new ClientGroup { Id = groupId, FirmId = f.FirmId, Code = "G1", Name = "Group one", CreatedByUserId = f.Admin.Id, CreatedAt = DateTimeOffset.UtcNow },
      new ClientGroup { Id = otherGroupId, FirmId = f.FirmId, Code = "G2", Name = "Group two", CreatedByUserId = f.Admin.Id, CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var now = DateTimeOffset.UtcNow;
    Assert.True((await RoleAssignmentService.AssignAsync(db, f.AdminActor, new(clientStaff.Id, "Staff", "CLIENT", f.ClientId, Reason: "Client team", ConfirmScopeExpansion: true), now)).Succeeded);
    Assert.True((await RoleAssignmentService.AssignAsync(db, f.AdminActor, new(engagementStaff.Id, "Staff", "ENGAGEMENT", f.ClientId, f.EngagementId, Reason: "Engagement team", ConfirmScopeExpansion: true), now)).Succeeded);
    var group = await RoleAssignmentService.AssignAsync(db, f.AdminActor, new(groupStaff.Id, "Manager", "GROUP", GroupId: groupId, Reason: "Group consolidation", ConfirmScopeExpansion: true), now);
    Assert.True(group.Succeeded, group.Message);
    Assert.True(group.Value!.GroupGrant);
    // Mismatched engagement/client pair is refused.
    Assert.False((await RoleAssignmentService.AssignAsync(db, f.AdminActor, new(clientStaff.Id, "Staff", "ENGAGEMENT", f.ClientId, f.SiblingEngagementId, Reason: "Mismatch", ConfirmScopeExpansion: true), now)).Succeeded);

    db.ChangeTracker.Clear();
    ActorContext Actor(AppUser user) => new(user.Id, f.FirmId, f.CurrentEpoch(user.Id), ["Staff"]);
    Assert.True((await AuthorizationDecision.AuthorizeAsync(db, Actor(clientStaff), new(f.FirmId, f.ClientId))).Succeeded);
    Assert.True((await AuthorizationDecision.AuthorizeAsync(db, Actor(clientStaff), new(f.FirmId, f.ClientId, f.EngagementId))).Succeeded);
    Assert.False((await AuthorizationDecision.AuthorizeAsync(db, Actor(clientStaff), new(f.FirmId, f.SiblingClientId))).Succeeded);
    Assert.True((await AuthorizationDecision.AuthorizeAsync(db, Actor(engagementStaff), new(f.FirmId, f.ClientId, f.EngagementId))).Succeeded);
    Assert.False((await AuthorizationDecision.AuthorizeAsync(db, Actor(engagementStaff), new(f.FirmId, f.ClientId))).Succeeded);
    Assert.False((await AuthorizationDecision.AuthorizeAsync(db, Actor(engagementStaff), new(f.FirmId, f.SiblingClientId, f.SiblingEngagementId))).Succeeded);
    Assert.True((await AuthorizationDecision.AuthorizeGroupAsync(db, Actor(groupStaff), groupId, ["Manager"])).Succeeded);
    Assert.False((await AuthorizationDecision.AuthorizeGroupAsync(db, Actor(groupStaff), otherGroupId, ["Manager"])).Succeeded);
    Assert.False((await AuthorizationDecision.AuthorizeAsync(db, Actor(groupStaff), new(f.FirmId, f.ClientId))).Succeeded);
  }

  [Fact]
  public async Task RevocationInvalidatesOpenSessionsImmediately()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    var staff = await StaffAsync(f);
    await using var db = f.Db();
    var granted = await RoleAssignmentService.AssignAsync(db, f.AdminActor,
      new(staff.Id, "Manager", "FIRM_WIDE", Reason: "Manager promotion", ConfirmScopeExpansion: true), DateTimeOffset.UtcNow);
    Assert.True(granted.Succeeded, granted.Message);
    var openSession = new ActorContext(staff.Id, f.FirmId, f.CurrentEpoch(staff.Id), ["Manager"]);
    Assert.True((await AuthorizationDecision.AuthorizeAsync(db, openSession, new(f.FirmId, RequiredRoles: ["Manager"]))).Succeeded);
    Assert.True((await RoleAdministrationService.RevokeRoleGrantAsync(db, f.AdminActor, new(granted.Value!.GrantId, null, "Left the firm"))).Succeeded);
    var stale = await AuthorizationDecision.AuthorizeAsync(db, openSession, new(f.FirmId, RequiredRoles: ["Manager"]));
    Assert.Equal(ErrorCodes.GenerationStale, stale.ErrorCode);
    Assert.Contains(await db.RoleGrantChangeEvidences.ToListAsync(), x => x.Action == "REVOKED" && x.Reason == "Left the firm");

    // Group grant revocation also invalidates sessions.
    var groupId = Guid.NewGuid();
    db.ClientGroups.Add(new ClientGroup { Id = groupId, FirmId = f.FirmId, Code = "GR", Name = "Group", CreatedByUserId = f.Admin.Id, CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var groupGrant = await RoleAssignmentService.AssignAsync(db, f.AdminActor, new(staff.Id, "Staff", "GROUP", GroupId: groupId, Reason: "Group work", ConfirmScopeExpansion: true), DateTimeOffset.UtcNow);
    var groupSession = new ActorContext(staff.Id, f.FirmId, f.CurrentEpoch(staff.Id), ["Staff"]);
    Assert.True((await AuthorizationDecision.AuthorizeGroupAsync(db, groupSession, groupId, ["Staff"])).Succeeded);
    Assert.True((await RoleAssignmentService.RevokeGroupGrantAsync(db, f.AdminActor, groupGrant.Value!.GrantId, "Group work ended", DateTimeOffset.UtcNow)).Succeeded);
    Assert.False((await AuthorizationDecision.AuthorizeGroupAsync(db, groupSession, groupId, ["Staff"])).Succeeded);
  }

  [Fact]
  public async Task StaleOrDisabledDirectoryObservationBlocksAssignment()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await using var db = f.Db();
    var bound = await DirectoryUserBindingService.BindMemberAsync(db, f.AdminActor, f.Microsoft, f.TenantId, Guid.Parse(f.MemberObjectId));
    Assert.True(bound.Succeeded);
    var request = new RoleAssignmentRequest(bound.Value, "Staff", "FIRM_WIDE", Reason: "Fresh identity", ConfirmScopeExpansion: true);
    // Age the Graph observation past the freshness window.
    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE m365_directory_user_observations SET observed_at = {DateTimeOffset.UtcNow.AddHours(-1)} WHERE object_id = {f.MemberObjectId}");
    var stale = await RoleAssignmentService.AssignAsync(db, f.AdminActor, request, DateTimeOffset.UtcNow);
    Assert.Equal(ErrorCodes.GateBlocked, stale.ErrorCode);
    // Re-verification refreshes the observation and permits the change.
    Assert.True((await DirectoryUserBindingService.BindMemberAsync(db, f.AdminActor, f.Microsoft, f.TenantId, Guid.Parse(f.MemberObjectId))).Succeeded);
    Assert.True((await RoleAssignmentService.AssignAsync(db, f.AdminActor, request, DateTimeOffset.UtcNow)).Succeeded);
    // After Microsoft disables the account, re-verification fails closed.
    f.Microsoft.SetEnabled(f.MemberObjectId, false);
    Assert.False((await DirectoryUserBindingService.BindMemberAsync(db, f.AdminActor, f.Microsoft, f.TenantId, Guid.Parse(f.MemberObjectId))).Succeeded);
  }

  [Fact]
  public async Task ExpiredGrantsNeverAuthorize_AndAreRevokedWithEvidence()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    var staff = await StaffAsync(f);
    await using var db = f.Db();
    var tooSoon = await RoleAssignmentService.AssignAsync(db, f.AdminActor,
      new(staff.Id, "Staff", "FIRM_WIDE", ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(1), Reason: "Too short", ConfirmScopeExpansion: true), DateTimeOffset.UtcNow);
    Assert.False(tooSoon.Succeeded);
    var future = await RoleAssignmentService.AssignAsync(db, f.AdminActor,
      new(staff.Id, "Staff", "FIRM_WIDE", EffectiveFrom: DateTimeOffset.UtcNow.AddDays(3), Reason: "Future start", ConfirmScopeExpansion: true), DateTimeOffset.UtcNow);
    Assert.False(future.Succeeded);
    var temporary = await RoleAssignmentService.AssignAsync(db, f.AdminActor,
      new(staff.Id, "Staff", "FIRM_WIDE", ExpiresAt: DateTimeOffset.UtcNow.AddDays(1), Reason: "Temporary cover", ConfirmScopeExpansion: true), DateTimeOffset.UtcNow);
    Assert.True(temporary.Succeeded, temporary.Message);
    // Simulate the passage of time by moving the expiry into the past.
    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE role_grants SET granted_at = {DateTimeOffset.UtcNow.AddDays(-2)}, expires_at = {DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE id = {temporary.Value!.GrantId}");
    var session = new ActorContext(staff.Id, f.FirmId, f.CurrentEpoch(staff.Id), ["Staff"]);
    Assert.False((await AuthorizationDecision.AuthorizeAsync(db, session, new(f.FirmId, RequiredRoles: ["Staff"]))).Succeeded);
    Assert.True(await RoleGrantExpiry.RevokeExpiredForUserAsync(db, f.FirmId, staff.Id, DateTimeOffset.UtcNow));
    db.ChangeTracker.Clear();
    Assert.NotNull((await db.RoleGrants.SingleAsync(x => x.Id == temporary.Value.GrantId)).RevokedAt);
    Assert.Contains(await db.RoleGrantChangeEvidences.ToListAsync(), x => x.RoleGrantId == temporary.Value.GrantId && x.Source == "EXPIRY");
    Assert.NotEqual(session.SessionEpoch, f.CurrentEpoch(staff.Id));
  }

  [Fact]
  public async Task AdministratorRoleNeverBecomesAnEntraRole()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    var staff = await StaffAsync(f);
    await using var db = f.Db();
    var preview = await RoleAssignmentService.PreviewAsync(db, f.AdminActor,
      new(staff.Id, "Administrator", "FIRM_WIDE", Reason: "New administrator"), DateTimeOffset.UtcNow);
    Assert.Contains(preview.Value!.Warnings, x => x.Contains("no Microsoft Entra administrator role", StringComparison.OrdinalIgnoreCase));
    var saved = await RoleAssignmentService.AssignAsync(db, f.AdminActor,
      new(staff.Id, "Administrator", "FIRM_WIDE", Reason: "New administrator", ConfirmScopeExpansion: true), DateTimeOffset.UtcNow);
    Assert.True(saved.Succeeded);
    // No Microsoft operation of any kind was requested.
    Assert.Equal(0, await db.Microsoft365ExternalOperations.CountAsync());
  }
}
