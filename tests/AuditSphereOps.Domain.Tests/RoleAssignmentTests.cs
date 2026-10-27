using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class RoleAssignmentTests
{
  [Theory]
  [InlineData("AccountingPreparer")]
  [InlineData("AccountingReviewer")]
  public async Task AccountingRoles_AreReviewedStaffAssignmentsWithExactScopeAndSessionInvalidation(string role)
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await using var db = f.Db();
    var now = DateTimeOffset.UtcNow;
    var group = new Domain.Accounting.ClientGroup { Id = Guid.NewGuid(), FirmId = f.FirmId,
      Code = "ROLE-GROUP", Name = "Role assignment group", CreatedByUserId = f.Admin.Id, CreatedAt = now };
    db.ClientGroups.Add(group);
    await db.SaveChangesAsync();
    Assert.Contains(role, RoleAdministrationService.RoleCodes);
    Assert.NotEmpty(RoleAssignmentService.RoleCapabilities["Senior"]);
    Assert.Contains(RoleAssignmentService.RoleCapabilities[role], x => x.Contains("Partner"));
    foreach (var scope in new[] { "FIRM_WIDE", "CLIENT", "ENGAGEMENT", "GROUP" })
    {
      var staff = await StaffAsync(f, scope.ToLowerInvariant() + "@example.test");
      var request = new RoleAssignmentRequest(staff.Id, role, scope,
        ClientId: scope is "CLIENT" or "ENGAGEMENT" ? f.ClientId : null,
        EngagementId: scope == "ENGAGEMENT" ? f.EngagementId : null,
        GroupId: scope == "GROUP" ? group.Id : null, Reason: "Explicit accounting responsibility", ConfirmScopeExpansion: true);
      var review = await RoleAssignmentService.PreviewAsync(db, f.AdminActor, request, now);
      Assert.True(review.Succeeded, review.Message);
      Assert.Empty(review.Value!.BlockingReasons);
      var result = await RoleAssignmentService.AssignAsync(db, f.AdminActor, request, now,
        expectedReviewDigest: RoleAssignmentReviewDigest.Compute(review.Value, request));
      Assert.True(result.Succeeded, result.Message);
      var stale = new ActorContext(staff.Id, f.FirmId, staff.SessionEpoch, [role]);
      var actor = stale with { SessionEpoch = f.CurrentEpoch(staff.Id) };
      Assert.NotEqual(stale.SessionEpoch, actor.SessionEpoch);
      if (scope == "GROUP")
      {
        Assert.True((await AuthorizationDecision.AuthorizeGroupAsync(db, actor, group.Id, [role])).Succeeded);
        Assert.False((await AuthorizationDecision.AuthorizeGroupAsync(db, stale, group.Id, [role])).Succeeded);
        Assert.False((await AuthorizationDecision.AuthorizeGroupAsync(db, actor, Guid.NewGuid(), [role])).Succeeded);
        Assert.Equal(role, (await db.GroupAccessGrants.SingleAsync(x => x.Id == result.Value!.GrantId)).Role);
      }
      else
      {
        Assert.True((await AuthorizationDecision.AuthorizeAsync(db, actor,
          new(f.FirmId, f.ClientId, f.EngagementId, [role], InternalOnly: true))).Succeeded);
        Assert.False((await AuthorizationDecision.AuthorizeAsync(db, stale,
          new(f.FirmId, f.ClientId, f.EngagementId, [role], InternalOnly: true))).Succeeded);
        if (scope != "FIRM_WIDE")
          Assert.False((await AuthorizationDecision.AuthorizeAsync(db, actor,
            new(f.FirmId, f.SiblingClientId, f.SiblingEngagementId, [role], InternalOnly: true))).Succeeded);
        Assert.Equal(role, (await db.RoleGrants.SingleAsync(x => x.Id == result.Value!.GrantId)).Role);
      }
    }
    var client = TenantAdministrationFixture.User(f.FirmId, f.TenantId, "client@example.test", "Client", "Client");
    db.Users.Add(client);
    await db.SaveChangesAsync();
    var denied = await RoleAssignmentService.PreviewAsync(db, f.AdminActor,
      new(client.Id, role, "CLIENT", f.ClientId, Reason: "Invalid client specialist assignment"), now);
    Assert.Contains(denied.Value!.BlockingReasons, x => x.Contains("staff identity"));
    Assert.False(await db.RoleGrants.AnyAsync(x => x.UserId == client.Id));
  }

  [Fact]
  public async Task AdministratorExpiry_RequiresASeparateViableAdministratorBeyondExpiry()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await using var db = f.Db();
    var now = DateTimeOffset.UtcNow;
    var actingGrant = await db.RoleGrants.SingleAsync(x => x.UserId == f.Admin.Id);
    actingGrant.ExpiresAt = now.AddMinutes(30);
    var replacedGrant = await db.RoleGrants.SingleAsync(x => x.UserId == f.SecondAdmin.Id);
    await db.SaveChangesAsync();
    var request = new RoleAssignmentRequest(f.SecondAdmin.Id, "Administrator", "FIRM_WIDE",
      ExpiresAt: now.AddHours(2), Reason: "Time bounded access rotation", ReplacesGrantId: replacedGrant.Id);

    var blocked = await RoleAssignmentService.PreviewAsync(db, f.AdminActor, request, now);
    Assert.True(blocked.Succeeded, blocked.Message);
    Assert.Contains(blocked.Value!.BlockingReasons, x => x.Contains("beyond this expiry"));
    var refused = await RoleAssignmentService.AssignAsync(db, f.AdminActor, request, now);
    Assert.Equal("roles.blocked", refused.ErrorCode);
    Assert.Null((await db.RoleGrants.SingleAsync(x => x.Id == replacedGrant.Id)).RevokedAt);

    actingGrant.ExpiresAt = null;
    await db.SaveChangesAsync();
    var permitted = await RoleAssignmentService.PreviewAsync(db, f.AdminActor, request, now);
    Assert.True(permitted.Succeeded, permitted.Message);
    Assert.Empty(permitted.Value!.BlockingReasons);
    Assert.Contains(permitted.Value.Warnings, x => x.Contains("expired access is never extended"));
    var applied = await RoleAssignmentService.AssignAsync(db, f.AdminActor, request, now,
      expectedReviewDigest: RoleAssignmentReviewDigest.Compute(permitted.Value, request));
    Assert.True(applied.Succeeded, applied.Message);
    db.ChangeTracker.Clear();
    Assert.NotNull((await db.RoleGrants.SingleAsync(x => x.Id == replacedGrant.Id)).RevokedAt);
    Assert.Equal(request.ExpiresAt, (await db.RoleGrants.SingleAsync(x => x.Id == applied.Value!.GrantId)).ExpiresAt);
  }

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
  public async Task GroupRevocationRefusesAdministratorRevokedWhileWaitingForFirmLock()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    var target = await StaffAsync(f, "group-target@example.test");
    var groupId = Guid.NewGuid();
    var groupGrantId = Guid.NewGuid();
    var actor = f.AdminActor;
    await using (var setup = f.Db())
    {
      setup.ClientGroups.Add(new ClientGroup
      {
        Id = groupId, FirmId = f.FirmId, Code = "RACE-" + Guid.NewGuid().ToString("N")[..8],
        Name = "Revocation race", CreatedByUserId = f.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      setup.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = groupGrantId, FirmId = f.FirmId, GroupId = groupId, UserId = target.Id, Role = "Manager",
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = f.Admin.Id
      });
      await setup.SaveChangesAsync();
    }

    await using var blocker = f.Db();
    await using var blockerTransaction = await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {f.FirmId} FOR UPDATE").SingleAsync();
    var actorGrantId = await blocker.RoleGrants.Where(x => x.FirmId == f.FirmId && x.UserId == f.Admin.Id &&
        x.Role == "Administrator" && x.RevokedAt == null)
      .Select(x => x.Id).SingleAsync();

    var signal = new ObserveFirmAdministrationLock();
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(f.Pg.Options).AddInterceptors(signal).Options;
    await using var writer = new AuditSphereDbContext(options);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var attempt = RoleAssignmentService.RevokeGroupGrantAsync(
      writer, actor, groupGrantId, "Group access review", DateTimeOffset.UtcNow, timeout.Token);
    await signal.Reached.Task.WaitAsync(timeout.Token);

    await blocker.RoleGrants.Where(x => x.Id == actorGrantId)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), timeout.Token);
    await blocker.Users.Where(x => x.Id == f.Admin.Id)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1), timeout.Token);
    await blockerTransaction.CommitAsync(timeout.Token);

    var result = await attempt.WaitAsync(timeout.Token);
    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, result.ErrorCode);
    await using var verify = f.Db();
    Assert.Null((await verify.GroupAccessGrants.SingleAsync(x => x.Id == groupGrantId)).RevokedAt);
    Assert.Equal(0, await verify.Microsoft365AdministrationEvents.CountAsync(x =>
      x.Operation == "GROUP_ROLE_REVOKED" && x.TargetUserId == target.Id));
    Assert.Equal(1, await verify.Users.Where(x => x.Id == target.Id).Select(x => x.SessionEpoch).SingleAsync());
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
    Assert.False(await RoleGrantExpiry.RevokeExpiredForUserAsync(db, f.FirmId, staff.Id, DateTimeOffset.UtcNow));
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
  public async Task ExpiryWaitsForFirmFenceAndConcurrentCallsAdvanceFreshEpochOnce()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    var staff = await StaffAsync(f);
    var now = DateTimeOffset.UtcNow;
    Guid[] grantIds;
    await using (var setup = f.Db())
    {
      var first = await RoleAssignmentService.AssignAsync(setup, f.AdminActor,
        new(staff.Id, "Staff", "CLIENT", f.ClientId, ExpiresAt: now.AddDays(1),
          Reason: "First expiry fence grant", ConfirmScopeExpansion: true), now);
      var second = await RoleAssignmentService.AssignAsync(setup, f.AdminActor,
        new(staff.Id, "Manager", "ENGAGEMENT", f.ClientId, f.EngagementId, ExpiresAt: now.AddDays(1),
          Reason: "Second expiry fence grant", ConfirmScopeExpansion: true), now);
      Assert.True(first.Succeeded, first.Message);
      Assert.True(second.Succeeded, second.Message);
      grantIds = [first.Value!.GrantId, second.Value!.GrantId];
      await setup.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE role_grants
        SET granted_at = {now.AddDays(-2)}, expires_at = {now.AddMinutes(-1)}
        WHERE id IN ({grantIds[0]}, {grantIds[1]})
        """);
    }

    await using var observer = f.Db();
    var firstSignal = new ObserveFirmAdministrationLock();
    var firstOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(f.Pg.Options).AddInterceptors(firstSignal).Options;
    await using var firstWriter = new AuditSphereDbContext(firstOptions);
    var staleTrackedUser = await firstWriter.Users.SingleAsync(x => x.Id == staff.Id);
    var staleEpoch = staleTrackedUser.SessionEpoch;
    Assert.Equal(f.CurrentEpoch(staff.Id), staleEpoch);
    var secondSignal = new ObserveFirmAdministrationLock();
    var secondOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(f.Pg.Options).AddInterceptors(secondSignal).Options;
    await using var secondWriter = new AuditSphereDbContext(secondOptions);
    var secondStaleTrackedUser = await secondWriter.Users.SingleAsync(x => x.Id == staff.Id);
    Assert.Equal(staleEpoch, secondStaleTrackedUser.SessionEpoch);

    await using var blocker = f.Db();
    await using var blockerTransaction = await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {f.FirmId} FOR UPDATE").SingleAsync();
    var blockingPid = await blocker.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync();

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var firstAttempt = RoleGrantExpiry.RevokeExpiredForUserAsync(firstWriter, f.FirmId, staff.Id, now, timeout.Token);
    await firstSignal.Reached.Task.WaitAsync(timeout.Token);
    await WaitForFirmBlockingPidAsync(observer, firstSignal.BackendPid, [blockingPid], timeout.Token);
    var secondAttempt = RoleGrantExpiry.RevokeExpiredForUserAsync(secondWriter, f.FirmId, staff.Id, now, timeout.Token);
    await secondSignal.Reached.Task.WaitAsync(timeout.Token);
    // PostgreSQL can queue this waiter behind the first waiter, which is already
    // proven blocked by the still-held root fence. Either blocker proves the chain.
    await WaitForFirmBlockingPidAsync(observer, secondSignal.BackendPid,
      [blockingPid, firstSignal.BackendPid], timeout.Token);

    await blocker.Users.Where(x => x.Id == staff.Id)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1), timeout.Token);
    Assert.Equal(staleEpoch + 1, await blocker.Users.AsNoTracking().Where(x => x.Id == staff.Id)
      .Select(x => x.SessionEpoch).SingleAsync(timeout.Token));
    await blockerTransaction.CommitAsync(timeout.Token);

    var results = await Task.WhenAll(firstAttempt, secondAttempt).WaitAsync(timeout.Token);
    Assert.Equal(1, results.Count(result => result));
    Assert.Equal(1, results.Count(result => !result));

    await using var verify = f.Db();
    var evidence = await verify.RoleGrantChangeEvidences.AsNoTracking()
      .Where(x => x.TargetUserId == staff.Id && x.Source == "EXPIRY").ToListAsync(timeout.Token);
    Assert.Equal(grantIds.Length, evidence.Count);
    Assert.All(grantIds, grantId => Assert.Single(evidence, x => x.RoleGrantId == grantId));
    Assert.All(await verify.RoleGrants.AsNoTracking().Where(x => grantIds.Contains(x.Id)).ToListAsync(timeout.Token),
      grant => Assert.NotNull(grant.RevokedAt));
    Assert.Equal(staleEpoch + 2, await verify.Users.AsNoTracking().Where(x => x.Id == staff.Id)
      .Select(x => x.SessionEpoch).SingleAsync(timeout.Token));
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

  [Fact]
  public async Task ReviewedInvitationPreservesReplacementExpiryReasonAndAtomicProvenance()
  {
    await using var f=await TenantAdministrationFixture.CreateAsync();var staff=await StaffAsync(f);await using var db=f.Db();var now=new DateTimeOffset(DateTimeOffset.UtcNow.Ticks/10*10,TimeSpan.Zero);
    var old=await RoleAssignmentService.AssignAsync(db,f.AdminActor,new(staff.Id,"Staff","CLIENT",f.ClientId,Reason:"Original fixture access",ConfirmScopeExpansion:true),now);Assert.True(old.Succeeded,old.Message);
    var epoch=f.CurrentEpoch(staff.Id);var expiry=now.AddDays(2);var request=new RoleAssignmentRequest(staff.Id,"Manager","ENGAGEMENT",f.ClientId,f.EngagementId,ExpiresAt:expiry,Reason:"Reviewed replacement rationale",ReplacesGrantId:old.Value!.GrantId,ConfirmScopeExpansion:true);
    var preview=await RoleAssignmentService.PreviewAsync(db,f.AdminActor,request,now);Assert.True(preview.Succeeded,preview.Message);
    var applied=await RoleAssignmentService.AssignWithInvitationAsync(db,f.AdminActor,request,now,RoleAssignmentReviewDigest.Compute(preview.Value!,request));Assert.True(applied.Succeeded,applied.Message);
    db.ChangeTracker.Clear();var grant=await db.RoleGrants.SingleAsync(x=>x.Id==applied.Value!.RoleGrantId);Assert.Equal(expiry,grant.ExpiresAt);Assert.Equal(request.Reason,grant.Reason);Assert.Equal(f.EngagementId,grant.EngagementId);Assert.Equal(f.ClientId,grant.ClientId);
    Assert.NotNull((await db.RoleGrants.SingleAsync(x=>x.Id==old.Value.GrantId)).RevokedAt);Assert.Equal(epoch+1,f.CurrentEpoch(staff.Id));
    var invitation=await db.UserAccessInvitations.SingleAsync();Assert.Equal(grant.Id,invitation.RoleGrantId);Assert.Equal(staff.Id,invitation.UserId);
    Assert.Equal(2,await db.RoleGrantChangeEvidences.CountAsync(x=>x.TargetUserId==staff.Id && x.Reason==request.Reason));
  }
  [Fact]
  public async Task DifferentExistingExpiryRefusesWithoutEffectsButExplicitReplacementCreatesNewGrant()
  {
    await using var f=await TenantAdministrationFixture.CreateAsync();var staff=await StaffAsync(f);await using var db=f.Db();var now=new DateTimeOffset(DateTimeOffset.UtcNow.Ticks/10*10,TimeSpan.Zero);
    var original=new RoleAssignmentRequest(staff.Id,"Staff","CLIENT",f.ClientId,Reason:"Original unexpiring access",ConfirmScopeExpansion:true);
    var old=await RoleAssignmentService.AssignAsync(db,f.AdminActor,original,now);Assert.True(old.Succeeded,old.Message);var epoch=f.CurrentEpoch(staff.Id);
    var changed=original with{ExpiresAt=now.AddDays(3)};var preview=await RoleAssignmentService.PreviewAsync(db,f.AdminActor,changed,now);Assert.NotEmpty(preview.Value!.BlockingReasons);
    Assert.False((await RoleAssignmentService.AssignAsync(db,f.AdminActor,changed,now)).Succeeded);
    Assert.False((await RoleAssignmentService.AssignWithInvitationAsync(db,f.AdminActor,changed,now,RoleAssignmentReviewDigest.Compute(preview.Value!,changed))).Succeeded);
    Assert.Empty(await db.UserAccessInvitations.ToListAsync());Assert.Equal(epoch,f.CurrentEpoch(staff.Id));
    var replacement=changed with{ReplacesGrantId=old.Value!.GrantId};var reviewed=await RoleAssignmentService.PreviewAsync(db,f.AdminActor,replacement,now);
    var applied=await RoleAssignmentService.AssignWithInvitationAsync(db,f.AdminActor,replacement,now,RoleAssignmentReviewDigest.Compute(reviewed.Value!,replacement));Assert.True(applied.Succeeded,applied.Message);
    db.ChangeTracker.Clear();Assert.NotEqual(old.Value.GrantId,applied.Value!.RoleGrantId);Assert.NotNull((await db.RoleGrants.SingleAsync(x=>x.Id==old.Value.GrantId)).RevokedAt);Assert.Equal(changed.ExpiresAt,(await db.RoleGrants.SingleAsync(x=>x.Id==applied.Value.RoleGrantId)).ExpiresAt);
  }
  [Fact]
  public async Task ReplacementOntoExistingGroupGrantInvalidatesEpochAndRecordsReduction()
  {
    await using var f=await TenantAdministrationFixture.CreateAsync();var staff=await StaffAsync(f);await using var db=f.Db();var now=new DateTimeOffset(DateTimeOffset.UtcNow.Ticks/10*10,TimeSpan.Zero);var groupId=Guid.NewGuid();
    db.ClientGroups.Add(new ClientGroup{Id=groupId,FirmId=f.FirmId,Code="EXISTING",Name="Synthetic group",CreatedByUserId=f.Admin.Id,CreatedAt=now});await db.SaveChangesAsync();
    var local=await RoleAssignmentService.AssignAsync(db,f.AdminActor,new(staff.Id,"Staff","CLIENT",f.ClientId,Reason:"Original local access",ConfirmScopeExpansion:true),now);Assert.True(local.Succeeded,local.Message);
    var groupRequest=new RoleAssignmentRequest(staff.Id,"Manager","GROUP",GroupId:groupId,Reason:"Original group access",ConfirmScopeExpansion:true);
    var group=await RoleAssignmentService.AssignAsync(db,f.AdminActor,groupRequest,now);Assert.True(group.Succeeded,group.Message);var epoch=f.CurrentEpoch(staff.Id);
    var replacement=groupRequest with{ReplacesGrantId=local.Value!.GrantId,Reason="Remove redundant local access"};var preview=await RoleAssignmentService.PreviewAsync(db,f.AdminActor,replacement,now);
    var result=await RoleAssignmentService.AssignAsync(db,f.AdminActor,replacement,now,expectedReviewDigest:RoleAssignmentReviewDigest.Compute(preview.Value!,replacement));Assert.True(result.Succeeded,result.Message);Assert.True(result.Value!.Replaced);Assert.Equal(group.Value!.GrantId,result.Value.GrantId);Assert.Equal(epoch+1,f.CurrentEpoch(staff.Id));
    Assert.Single(await db.GroupAccessGrants.ToListAsync());Assert.Contains(await db.Microsoft365AdministrationEvents.ToListAsync(),x=>x.Operation=="ROLE_REPLACED" && x.Reason==replacement.Reason);
  }
  private sealed class RejectInvitation:SaveChangesInterceptor
  {
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default)
    {
      if(data.Context!.ChangeTracker.Entries<AuditSphereOps.Domain.Microsoft365.UserAccessInvitation>().Any(x=>x.State==EntityState.Added))throw new InvalidOperationException("SYNTHETIC invitation publication failure");
      return ValueTask.FromResult(result);
    }
  }
  [Fact]
  public async Task InvitationInsertFailureRollsBackReplacementGrantEvidenceAndEpoch()
  {
    await using var f=await TenantAdministrationFixture.CreateAsync();var staff=await StaffAsync(f);var now=new DateTimeOffset(DateTimeOffset.UtcNow.Ticks/10*10,TimeSpan.Zero);Guid oldId;RoleAssignmentRequest request;string digest;
    await using(var setup=f.Db()) {
      var old=await RoleAssignmentService.AssignAsync(setup,f.AdminActor,new(staff.Id,"Staff","CLIENT",f.ClientId,Reason:"Original fixture access",ConfirmScopeExpansion:true),now);Assert.True(old.Succeeded,old.Message);oldId=old.Value!.GrantId;
      request=new(staff.Id,"Manager","CLIENT",f.ClientId,Reason:"Atomic replacement failure",ReplacesGrantId:oldId,ConfirmScopeExpansion:true);var p=await RoleAssignmentService.PreviewAsync(setup,f.AdminActor,request,now);digest=RoleAssignmentReviewDigest.Compute(p.Value!,request);
    }
    var epoch=f.CurrentEpoch(staff.Id);await using(var writer=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(f.Pg.Options).AddInterceptors(new RejectInvitation()).Options))
      await Assert.ThrowsAsync<InvalidOperationException>(()=>RoleAssignmentService.AssignWithInvitationAsync(writer,f.AdminActor,request,now,digest));
    await using var check=f.Db();Assert.Null((await check.RoleGrants.SingleAsync(x=>x.Id==oldId)).RevokedAt);Assert.Single(await check.RoleGrants.Where(x=>x.UserId==staff.Id).ToListAsync());Assert.Empty(await check.UserAccessInvitations.ToListAsync());Assert.Equal(epoch,f.CurrentEpoch(staff.Id));Assert.False(await check.RoleGrantChangeEvidences.AnyAsync(x=>x.Reason==request.Reason));
  }
  [Theory,InlineData("authority"),InlineData("access"),InlineData("hold")]
  public async Task InvitationRechecksReviewedIntentAndAuthorityAfterWaitingForFirmLock(string change)
  {
    await using var f=await TenantAdministrationFixture.CreateAsync();var staff=await StaffAsync(f);var actor=f.AdminActor;var now=new DateTimeOffset(DateTimeOffset.UtcNow.Ticks/10*10,TimeSpan.Zero);
    var request=new RoleAssignmentRequest(staff.Id,"Manager","CLIENT",f.ClientId,Reason:"Locked invitation review",ConfirmScopeExpansion:true);string digest;
    await using(var setup=f.Db()){var p=await RoleAssignmentService.PreviewAsync(setup,actor,request,now);digest=RoleAssignmentReviewDigest.Compute(p.Value!,request);}
    await using var blocker=f.Db();await using var tx=await blocker.Database.BeginTransactionAsync();await blocker.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={f.FirmId} FOR UPDATE").SingleAsync();
    var signal=new ObserveFirmAdministrationLock();await using var writer=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(f.Pg.Options).AddInterceptors(signal).Options);
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));var attempt=RoleAssignmentService.AssignWithInvitationAsync(writer,actor,request,now,digest,timeout.Token);await signal.Reached.Task.WaitAsync(timeout.Token);
    if(change=="authority")await blocker.RoleGrants.Where(x=>x.UserId==actor.UserId && x.Role=="Administrator").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,now));
    else if(change=="access")blocker.RoleGrants.Add(new RoleGrant{Id=Guid.NewGuid(),FirmId=f.FirmId,UserId=staff.Id,Role="Staff",ClientId=f.ClientId,GrantedAt=now,GrantedByUserId=f.SecondAdmin.Id});
    else blocker.SpecialistClearances.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,PracticeClientId=f.ClientId,Area="Independence",SpecialistName="Synthetic reviewer",Status="HOLD",CreatedAt=now});
    await blocker.SaveChangesAsync();await tx.CommitAsync();var result=await attempt;Assert.False(result.Succeeded);Assert.Equal(change=="authority"?ErrorCodes.ScopeDenied:ErrorCodes.StaleRevision,result.ErrorCode);
    await using var check=f.Db();Assert.Empty(await check.UserAccessInvitations.ToListAsync());Assert.False(await check.RoleGrants.AnyAsync(x=>x.UserId==staff.Id && x.Role=="Manager"));
  }
  private sealed class ChangeAuthorityAfterRead(Func<DbCommand,bool> matches,Func<Task> change):DbCommandInterceptor
  {
    public int Calls;
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,CommandExecutedEventData data,DbDataReader result,CancellationToken ct=default)
    {if(matches(command) && Interlocked.CompareExchange(ref Calls,1,0)==0)await change();return result;}
  }
  [Theory,InlineData(false),InlineData(true)]
  public async Task PreviewAndWorkspaceRefuseAuthorityChangedDuringLastProjectionRead(bool workspace)
  {
    await using var f=await TenantAdministrationFixture.CreateAsync();var staff=await StaffAsync(f);var actor=f.AdminActor;
    var interceptor=new ChangeAuthorityAfterRead(c=>workspace?c.CommandText.Contains("role_grant_change_evidence",StringComparison.Ordinal) && c.CommandText.Contains("IS NOT NULL",StringComparison.Ordinal):c.CommandText.Contains("group_access_grants",StringComparison.Ordinal),async()=>{
      await using var revoke=f.Db();await revoke.RoleGrants.Where(x=>x.UserId==actor.UserId && x.Role=="Administrator").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));});
    await using var reader=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(f.Pg.Options).AddInterceptors(interceptor).Options);
    if(workspace){var result=await UserAccessWorkspaceQuery.GetAsync(reader,actor,DateTimeOffset.UtcNow);Assert.False(result.Succeeded);Assert.Null(result.Value);}
    else {var result=await RoleAssignmentService.PreviewAsync(reader,actor,new(staff.Id,"Staff","CLIENT",f.ClientId,Reason:"Final scope read"),DateTimeOffset.UtcNow);Assert.False(result.Succeeded);Assert.Null(result.Value);}
    Assert.Equal(1,interceptor.Calls);
  }

  [Fact]
  public async Task IdenticalExistingGrantReusesInvitationWithoutDuplicateGrantEvidenceOrEpochChange()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    var staff = await StaffAsync(f);
    await using var db = f.Db();
    var now = new DateTimeOffset(DateTimeOffset.UtcNow.Ticks / 10 * 10, TimeSpan.Zero);
    var request = new RoleAssignmentRequest(staff.Id, "Staff", "CLIENT", f.ClientId,
      ExpiresAt: now.AddDays(2), Reason: "Exact existing grant", ConfirmScopeExpansion: true);
    var first = await RoleAssignmentService.AssignAsync(db, f.AdminActor, request, now);
    Assert.True(first.Succeeded, first.Message);
    var epoch = f.CurrentEpoch(staff.Id);
    var preview = await RoleAssignmentService.PreviewAsync(db, f.AdminActor, request, now);
    var digest = RoleAssignmentReviewDigest.Compute(preview.Value!, request);
    var invitation = await RoleAssignmentService.AssignWithInvitationAsync(db, f.AdminActor, request, now, digest);
    Assert.True(invitation.Succeeded, invitation.Message);
    var duplicate = await RoleAssignmentService.AssignWithInvitationAsync(db, f.AdminActor, request, now, digest);
    Assert.True(duplicate.Succeeded, duplicate.Message);
    Assert.Equal(invitation.Value, duplicate.Value);
    Assert.Equal(first.Value!.GrantId, duplicate.Value!.RoleGrantId);
    Assert.Equal(epoch, f.CurrentEpoch(staff.Id));
    Assert.Single(await db.UserAccessInvitations.ToListAsync());
    Assert.Single(await db.RoleGrants.Where(x => x.UserId == staff.Id).ToListAsync());
    Assert.Single(await db.RoleGrantChangeEvidences.Where(x => x.TargetUserId == staff.Id).ToListAsync());
  }

  private sealed class ObserveFirmAdministrationLock : DbCommandInterceptor
  {
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int BackendPid { get; private set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
      DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("firm_safety_states", StringComparison.Ordinal) &&
          command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
      {
        BackendPid = ((Npgsql.NpgsqlConnection)command.Connection!).ProcessID;
        Reached.TrySetResult();
      }
      return ValueTask.FromResult(result);
    }
  }

  private static async Task WaitForFirmBlockingPidAsync(AuditSphereDbContext observer, int waitingPid, int[] blockingPids,
    CancellationToken cancellationToken)
  {
    while (true)
    {
      await observer.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()", cancellationToken);
      var blocked = await observer.Database.SqlQuery<bool>($"""
        SELECT EXISTS (
          SELECT 1 FROM pg_stat_activity AS waiting
          WHERE waiting.datname = current_database()
            AND waiting.pid = {waitingPid}
            AND waiting.wait_event_type = 'Lock'
            AND waiting.query ILIKE '%firm_safety_states%'
            AND pg_blocking_pids(waiting.pid) && {blockingPids}
        ) AS "Value"
        """).SingleAsync(cancellationToken);
      if (blocked)
      {
        var observed = await observer.Database.SqlQuery<int[]>($"SELECT pg_blocking_pids({waitingPid}) AS \"Value\"")
          .SingleAsync(cancellationToken);
        Console.WriteLine($"Expiry waiter {waitingPid}; actual blockers [{string.Join(",", observed)}]; expected chain [{string.Join(",", blockingPids)}].");
        return;
      }
      await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
    }
  }
}
