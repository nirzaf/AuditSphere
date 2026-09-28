using System.Text.Json;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class TenantAdministrationTests
{
  // ---- Tenant consent identity leg (§3, §18: CSRF, replay, wrong tenant, forged identity) ----

  [Fact]
  public async Task ConsentIdentityLeg_BindsExactTenantAdministrator_AndRecordsCapabilitiesIndependently()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await f.ConnectAndVerifyAsync();
    await using var db = f.Db();
    var attempt = await db.TenantConsentAttempts.SingleAsync(x => x.State == TenantConsentAttemptStates.ConsentVerified);
    Assert.Equal(f.TenantId, attempt.ConsentingTenantId);
    Assert.Equal(f.Admin.Subject, attempt.ConsentingObjectId);
    Assert.Equal(64, attempt.NonceHash!.Length);
    var connection = await db.Microsoft365ConnectionRevisions.SingleAsync();
    Assert.Equal("VERIFIED", connection.ConsentState);
    Assert.NotEqual(Microsoft365RevisionStates.Active, connection.State);
    Assert.Contains(await db.IntegrationVerificationEvidences.ToListAsync(), x => x.ResourceKind == "TENANT" &&
      x.Operation == "CONSENT" && x.IdentityReference == f.Admin.Subject);
    var statuses = await TenantCapabilityService.StatusesAsync(db, f.FirmId, f.Options, DateTimeOffset.UtcNow);
    foreach (var capability in new[] { Microsoft365Capabilities.DirectoryRead, Microsoft365Capabilities.TenantUserProvisioning,
               Microsoft365Capabilities.GuestInvitation, Microsoft365Capabilities.GroupMembership, Microsoft365Capabilities.SelectedSite })
      Assert.Equal(CapabilityVerificationStates.Verified, statuses.Single(x => x.Capability == capability).State);
    Assert.Equal(TenantCapabilityService.Disabled, statuses.Single(x => x.Capability == Microsoft365Capabilities.OutboundMail).State);
    Assert.Equal(5, await db.TenantCapabilityVerifications.CountAsync());
  }

  [Fact]
  public async Task ConsentIdentityLeg_RejectsReplayWrongTenantWrongNonceExternalIdentityAndExpiry()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    var now = DateTimeOffset.UtcNow;
    await using var db = f.Db();

    async Task<TenantIdentityChallenge> ChallengeAsync()
    {
      var begin = await TenantConsentService.BeginAsync(db, f.AdminActor, f.DraftId, f.TenantId, Guid.NewGuid().ToString("D"), now);
      var returned = await TenantConsentService.CompleteCallbackAsync(db, f.AdminActor, begin.Value!.State, f.TenantId, true, false, now);
      return (await TenantConsentService.BeginIdentityVerificationAsync(db, f.AdminActor, returned.Value!.AttemptId, now)).Value!;
    }

    // Wrong tenant in the redeemed identity.
    var wrongTenant = await ChallengeAsync();
    var fake = new FixedIdentityVerifier(new(Guid.NewGuid().ToString("D"), f.Admin.Subject, wrongTenant.Nonce, false));
    Assert.False((await TenantConsentService.CompleteIdentityVerificationAsync(db, f.AdminActor, fake, wrongTenant.State, "code", false, now)).Succeeded);
    // Nonce mismatch (token issued for a different request).
    var wrongNonce = await ChallengeAsync();
    fake = new FixedIdentityVerifier(new(f.TenantId, f.Admin.Subject, "not-the-nonce", false));
    Assert.False((await TenantConsentService.CompleteIdentityVerificationAsync(db, f.AdminActor, fake, wrongNonce.State, "code", false, now)).Succeeded);
    // External (guest/personal) identity.
    var external = await ChallengeAsync();
    fake = new FixedIdentityVerifier(new(f.TenantId, Guid.NewGuid().ToString("D"), external.Nonce, true));
    Assert.False((await TenantConsentService.CompleteIdentityVerificationAsync(db, f.AdminActor, fake, external.State, "code", false, now)).Succeeded);
    // Forged state.
    Assert.False((await TenantConsentService.CompleteIdentityVerificationAsync(db, f.AdminActor, f.Microsoft, "forged-state", "code", false, now)).Succeeded);
    // Expired identity leg: never redeemed.
    var expired = await ChallengeAsync();
    var counting = new FixedIdentityVerifier(new(f.TenantId, f.Admin.Subject, expired.Nonce, false));
    Assert.False((await TenantConsentService.CompleteIdentityVerificationAsync(db, f.AdminActor, counting, expired.State, "code", false, now.AddMinutes(11))).Succeeded);
    Assert.Equal(0, counting.Redemptions);
    // Valid, then replay of the same state is refused without a second redemption.
    var valid = await ChallengeAsync();
    counting = new FixedIdentityVerifier(new(f.TenantId, f.Admin.Subject, valid.Nonce, false));
    Assert.True((await TenantConsentService.CompleteIdentityVerificationAsync(db, f.AdminActor, counting, valid.State, "code", false, now)).Succeeded);
    Assert.False((await TenantConsentService.CompleteIdentityVerificationAsync(db, f.AdminActor, counting, valid.State, "code", false, now)).Succeeded);
    Assert.Equal(1, counting.Redemptions);
    // A different administrator cannot complete another administrator's flow (session binding).
    var bound = await ChallengeAsync();
    Assert.False((await TenantConsentService.CompleteIdentityVerificationAsync(db, f.SecondAdminActor,
      new FixedIdentityVerifier(new(f.TenantId, f.SecondAdmin.Subject, bound.Nonce, false)), bound.State, "code", false, now)).Succeeded);

    db.ChangeTracker.Clear();
    Assert.Equal(1, await db.TenantConsentAttempts.CountAsync(x => x.State == TenantConsentAttemptStates.ConsentVerified));
  }

  [Fact]
  public async Task CapabilityVerification_NotGrantedAndStaleStatesBlockMutations()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    var now = DateTimeOffset.UtcNow;
    await using var db = f.Db();
    // Without consent verification nothing is usable, even if a provider says a role is present.
    await TenantCapabilityService.VerifyAsync(db, f.AdminActor, f.Microsoft, f.Options, now);
    var gate = await TenantCapabilityService.RequireVerifiedAsync(db, f.FirmId, f.Options, Microsoft365Capabilities.TenantUserProvisioning, now);
    Assert.False(gate.Succeeded);
    Assert.Equal(CapabilityVerificationStates.BlockedExternal,
      (await db.TenantCapabilityVerifications.OrderByDescending(x => x.ObservedAt)
        .FirstAsync(x => x.Capability == Microsoft365Capabilities.TenantUserProvisioning)).State);

    await f.ConnectAndVerifyAsync();
    Assert.True((await TenantCapabilityService.RequireVerifiedAsync(db, f.FirmId, f.Options, Microsoft365Capabilities.TenantUserProvisioning, now)).Succeeded);
    // Stale after the maximum age.
    Assert.False((await TenantCapabilityService.RequireVerifiedAsync(db, f.FirmId, f.Options,
      Microsoft365Capabilities.TenantUserProvisioning, now.AddHours(25))).Succeeded);
    // Disabled in deployment configuration.
    Assert.False((await TenantCapabilityService.RequireVerifiedAsync(db, f.FirmId, f.Options with { ProvisioningEnabled = false },
      Microsoft365Capabilities.TenantUserProvisioning, now)).Succeeded);

    var notGranted = new Infrastructure.Providers.SimulatedMicrosoftTenant(f.TenantId, [], [], [Microsoft365Capabilities.GuestInvitation]);
    await TenantCapabilityService.VerifyAsync(db, f.AdminActor, notGranted, f.Options, now.AddMinutes(1));
    var statuses = await TenantCapabilityService.StatusesAsync(db, f.FirmId, f.Options, now.AddMinutes(1));
    Assert.Equal(CapabilityVerificationStates.NotGranted, statuses.Single(x => x.Capability == Microsoft365Capabilities.GuestInvitation).State);
    Assert.Equal(CapabilityVerificationStates.Verified, statuses.Single(x => x.Capability == Microsoft365Capabilities.TenantUserProvisioning).State);
  }

  [Fact]
  public async Task CapabilityRows_AreAppendOnlyInPostgres()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await f.ConnectAndVerifyAsync();
    await using var db = f.Db();
    var grant = await db.RoleGrants.FirstAsync(x => x.UserId == f.SecondAdmin.Id);
    Assert.True((await RoleAdministrationService.RevokeRoleGrantAsync(db, f.AdminActor, new(grant.Id, null, "Evidence row"))).Succeeded);
    Assert.True(await db.RoleGrantChangeEvidences.AnyAsync());
    await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("UPDATE m365_tenant_capability_verifications SET state = 'VERIFIED'"));
    await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM m365_administration_events"));
    await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM role_grant_change_evidence"));
    await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("UPDATE role_grant_change_evidence SET reason = 'rewritten'"));
  }

  // ---- Tenant user provisioning (§7, §16) ----

  [Fact]
  public async Task CreateUser_IsIdempotent_BindsImmutableIdentity_AndNeverPersistsPassword()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await f.ConnectAndVerifyAsync();
    var key = TenantAdministrationFixture.Key();
    var request = new CreateTenantUserRequest(key, "New Staff", "new.staff@example.test", "new.staff", true,
      "Staff", "CLIENT", f.ClientId, null, "Joined the audit team");
    await using var db = f.Db();
    var first = await DirectoryProvisioningService.CreateTenantUserAsync(db, f.AdminActor, f.Microsoft, f.Options, request, DateTimeOffset.UtcNow);
    Assert.True(first.Succeeded, first.Message);
    Assert.Equal(ExternalOperationStates.Bound, first.Value!.State);
    var password = first.Value.TemporaryPassword;
    Assert.False(string.IsNullOrWhiteSpace(password));
    Assert.True(password!.Length >= 16);

    var retry = await DirectoryProvisioningService.CreateTenantUserAsync(db, f.AdminActor, f.Microsoft, f.Options, request, DateTimeOffset.UtcNow);
    Assert.True(retry.Succeeded);
    Assert.Equal(first.Value.BoundUserId, retry.Value!.BoundUserId);
    Assert.Null(retry.Value.TemporaryPassword);

    db.ChangeTracker.Clear();
    var user = await db.Users.SingleAsync(x => x.Email == "new.staff@example.test");
    Assert.Equal(f.TenantId, user.TenantId);
    Assert.True(Guid.TryParse(user.Subject, out _));
    Assert.Equal(f.Admin.Id, user.CreatedByUserId);
    var grant = await db.RoleGrants.SingleAsync(x => x.UserId == user.Id);
    Assert.Equal(("Staff", (Guid?)f.ClientId, (Guid?)null), (grant.Role, grant.ClientId, grant.EngagementId));
    Assert.Single(await db.Microsoft365ExternalOperations.Where(x => x.Kind == ExternalOperationKinds.CreateTenantUser).ToListAsync());
    Assert.Contains(await db.RoleGrantChangeEvidences.ToListAsync(), x => x.RoleGrantId == grant.Id && x.Source == "GRAPH_PROVISIONED");
    Assert.Contains(await db.Microsoft365AdministrationEvents.ToListAsync(), x => x.Operation == "TENANT_USER_BOUND" && x.TargetUserId == user.Id);

    // Same key with a different request is refused.
    var conflict = await DirectoryProvisioningService.CreateTenantUserAsync(db, f.AdminActor, f.Microsoft, f.Options,
      request with { DisplayName = "Someone Else" }, DateTimeOffset.UtcNow);
    Assert.Equal(ErrorCodes.IdempotencyConflict, conflict.ErrorCode);

    await AssertNoSecretAsync(db, password);
  }

  [Fact]
  public async Task CreateUser_DuplicateUpnOrLocalEmailIsRefusedBeforeDispatch()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await f.ConnectAndVerifyAsync();
    await using var db = f.Db();
    var existingInMicrosoft = await DirectoryProvisioningService.CreateTenantUserAsync(db, f.AdminActor, f.Microsoft, f.Options,
      new(TenantAdministrationFixture.Key(), "Dup", "member@example.test", "member", true, "Staff", "FIRM_WIDE", null, null, "Duplicate test"),
      DateTimeOffset.UtcNow);
    Assert.Equal("m365.user.duplicate", existingInMicrosoft.ErrorCode);
    var localEmail = await DirectoryProvisioningService.CreateTenantUserAsync(db, f.AdminActor, f.Microsoft, f.Options,
      new(TenantAdministrationFixture.Key(), "Dup", "admin2@example.test", "admin2", true, "Staff", "FIRM_WIDE", null, null, "Duplicate test"),
      DateTimeOffset.UtcNow);
    Assert.Equal("m365.duplicate", localEmail.ErrorCode);
    Assert.Equal(0, await db.Microsoft365ExternalOperations.CountAsync(x => x.State == ExternalOperationStates.Bound));
  }

  [Fact]
  public async Task CreateUser_UnknownOutcomeIsReconciledByIdentity_NotBlindlyRetried()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await f.ConnectAndVerifyAsync();
    var request = new CreateTenantUserRequest(TenantAdministrationFixture.Key(), "Timeout User", "unknown.user@example.test",
      "unknown.user", true, "Staff", "ENGAGEMENT", f.ClientId, f.EngagementId, "Timeout recovery test");
    await using var db = f.Db();
    var first = await DirectoryProvisioningService.CreateTenantUserAsync(db, f.AdminActor, f.Microsoft, f.Options, request, DateTimeOffset.UtcNow);
    Assert.True(first.Succeeded);
    Assert.Equal(ExternalOperationStates.Unknown, first.Value!.State);
    Assert.Equal(0, await db.Users.CountAsync(x => x.Email == "unknown.user@example.test"));

    // Re-submitting the same key does not dispatch again; it reconciles.
    var resumed = await DirectoryProvisioningService.CreateTenantUserAsync(db, f.AdminActor, f.Microsoft, f.Options, request, DateTimeOffset.UtcNow);
    Assert.True(resumed.Succeeded, resumed.Message);
    Assert.Equal(ExternalOperationStates.Bound, resumed.Value!.State);
    Assert.Null(resumed.Value.TemporaryPassword);
    db.ChangeTracker.Clear();
    var operation = await db.Microsoft365ExternalOperations.SingleAsync();
    Assert.Equal(1, operation.AttemptCount);
    Assert.Equal("found-created-after-dispatch", operation.ReconciliationResult);
    var bound = await db.Users.SingleAsync(x => x.Email == "unknown.user@example.test");
    Assert.Equal(operation.ResultObjectId, bound.Subject);
    Assert.Single(await db.RoleGrants.Where(x => x.UserId == bound.Id && x.EngagementId == f.EngagementId).ToListAsync());
  }

  [Fact]
  public async Task CreateUser_FailedOutcomeGrantsNothing_AndDisabledCapabilityBlocks()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await f.ConnectAndVerifyAsync();
    await using var db = f.Db();
    var rejected = await DirectoryProvisioningService.CreateTenantUserAsync(db, f.AdminActor, f.Microsoft, f.Options,
      new(TenantAdministrationFixture.Key(), "Rejected", "reject.user@example.test", "reject.user", true, "Staff", "FIRM_WIDE", null, null, "Rejected test"),
      DateTimeOffset.UtcNow);
    Assert.Equal(ExternalOperationStates.Failed, rejected.Value!.State);
    Assert.Equal(0, await db.Users.CountAsync(x => x.Email == "reject.user@example.test"));
    var disabled = await DirectoryProvisioningService.CreateTenantUserAsync(db, f.AdminActor, f.Microsoft, f.Options with { ProvisioningEnabled = false },
      new(TenantAdministrationFixture.Key(), "Off", "off.user@example.test", "off.user", true, "Staff", "FIRM_WIDE", null, null, "Disabled test"),
      DateTimeOffset.UtcNow);
    Assert.Equal(ErrorCodes.GateBlocked, disabled.ErrorCode);
    var disabledAccount = await DirectoryProvisioningService.CreateTenantUserAsync(db, f.AdminActor, f.Microsoft, f.Options,
      new(TenantAdministrationFixture.Key(), "Disabled", "disabled.new@example.test", "disabled.new", false, "Staff", "FIRM_WIDE", null, null, "Disabled account"),
      DateTimeOffset.UtcNow);
    Assert.Equal("m365.user.disabled-access", disabledAccount.ErrorCode);
    Assert.Null(await f.Microsoft.FindByUserPrincipalNameAsync(f.TenantId, "disabled.new@example.test", default));
    // ClientUser cannot be provisioned as a workforce member, and non-admins are refused.
    var clientRole = await DirectoryProvisioningService.CreateTenantUserAsync(db, f.AdminActor, f.Microsoft, f.Options,
      new(TenantAdministrationFixture.Key(), "Client", "client.user@example.test", "client.user", true, "ClientUser", "CLIENT", f.ClientId, null, "Client test"),
      DateTimeOffset.UtcNow);
    Assert.False(clientRole.Succeeded);
  }

  // ---- Guest invitation (§9) ----

  [Fact]
  public async Task GuestInvitation_BindsClientUserWithClientScopeOnly()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await f.ConnectAndVerifyAsync();
    await using var db = f.Db();
    var invite = await DirectoryProvisioningService.InviteGuestAsync(db, f.AdminActor, f.Microsoft, f.Options,
      new(TenantAdministrationFixture.Key(), "cfo@client.test", f.ClientId, null, "Client finance contact"), DateTimeOffset.UtcNow);
    Assert.True(invite.Succeeded, invite.Message);
    Assert.Equal(ExternalOperationStates.Bound, invite.Value!.State);
    db.ChangeTracker.Clear();
    var guest = await db.Users.SingleAsync(x => x.Email == "cfo@client.test");
    Assert.Equal("Client", guest.UserKind);
    var grant = await db.RoleGrants.SingleAsync(x => x.UserId == guest.Id);
    Assert.Equal(("ClientUser", (Guid?)f.ClientId), (grant.Role, grant.ClientId));
    var guestActor = new Application.Abstractions.ActorContext(guest.Id, f.FirmId, guest.SessionEpoch, ["ClientUser"]);
    Assert.True((await AuthorizationDecision.AuthorizeAsync(db, guestActor, new(f.FirmId, f.ClientId))).Succeeded);
    Assert.False((await AuthorizationDecision.AuthorizeAsync(db, guestActor, new(f.FirmId, f.SiblingClientId))).Succeeded);
    Assert.False((await AuthorizationDecision.AuthorizeAsync(db, guestActor, new(f.FirmId, RequireFirmWide: true))).Succeeded);

    // A guest that already exists must use the Existing Guest path, not a second invitation.
    var again = await DirectoryProvisioningService.InviteGuestAsync(db, f.AdminActor, f.Microsoft, f.Options,
      new(TenantAdministrationFixture.Key(), "guest@client.test", f.ClientId, null, "Second invitation"), DateTimeOffset.UtcNow);
    Assert.Equal("m365.guest.exists", again.ErrorCode);

    // Firm-wide client access is impossible even through the reviewed role dialog.
    var widen = await RoleAssignmentService.AssignAsync(db, f.AdminActor,
      new(guest.Id, "ClientUser", "FIRM_WIDE", Reason: "Attempt to widen", ConfirmScopeExpansion: true), DateTimeOffset.UtcNow);
    Assert.False(widen.Succeeded);
  }

  [Fact]
  public async Task ExistingGuestAndMemberBinding_UseImmutableIdentity_AndRefuseDisabledAccounts()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await using var db = f.Db();
    var member = await DirectoryUserBindingService.BindMemberAsync(db, f.AdminActor, f.Microsoft, f.TenantId, Guid.Parse(f.MemberObjectId));
    Assert.True(member.Succeeded, member.Message);
    var guest = await DirectoryUserBindingService.BindExistingGuestAsync(db, f.AdminActor, f.Microsoft, f.TenantId, Guid.Parse(f.GuestObjectId));
    Assert.True(guest.Succeeded, guest.Message);
    Assert.Equal("guest@client.test", (await db.Users.SingleAsync(x => x.Id == guest.Value)).Email);
    Assert.Equal("Client", (await db.Users.SingleAsync(x => x.Id == guest.Value)).UserKind);
    // A guest cannot be bound as a member and vice versa.
    Assert.False((await DirectoryUserBindingService.BindMemberAsync(db, f.AdminActor, f.Microsoft, f.TenantId, Guid.Parse(f.GuestObjectId))).Succeeded);
    Assert.False((await DirectoryUserBindingService.BindExistingGuestAsync(db, f.AdminActor, f.Microsoft, f.TenantId, Guid.Parse(f.MemberObjectId))).Succeeded);
    // Disabled Microsoft identity.
    Assert.False((await DirectoryUserBindingService.BindMemberAsync(db, f.AdminActor, f.Microsoft, f.TenantId, Guid.Parse(f.DisabledMemberObjectId))).Succeeded);
    Assert.Equal(0, await db.Users.CountAsync(x => x.Subject == f.DisabledMemberObjectId));
    // Binding grants nothing.
    Assert.Equal(0, await db.RoleGrants.CountAsync(x => x.UserId == member.Value || x.UserId == guest.Value));
  }

  // ---- Microsoft group administration (§11) ----

  [Fact]
  public async Task Groups_AllowlistOnly_RefuseRoleAssignableGroups_AndNeverGrantAuditSphereAccess()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await f.ConnectAndVerifyAsync();
    await using var db = f.Db();
    var privileged = await ManagedGroupService.ApproveGroupAsync(db, f.AdminActor, f.Microsoft, f.Options,
      f.PrivilegedGroupObjectId, "Admins", "Should be refused", DateTimeOffset.UtcNow);
    Assert.Equal("m365.group.privileged", privileged.ErrorCode);
    var approved = await ManagedGroupService.ApproveGroupAsync(db, f.AdminActor, f.Microsoft, f.Options,
      f.GroupObjectId, "Audit team distribution", "Team collaboration", DateTimeOffset.UtcNow);
    Assert.True(approved.Succeeded, approved.Message);

    var member = await DirectoryUserBindingService.BindMemberAsync(db, f.AdminActor, f.Microsoft, f.TenantId, Guid.Parse(f.MemberObjectId));
    var grantsBefore = await db.RoleGrants.CountAsync();
    var add = await ManagedGroupService.ChangeMembershipAsync(db, f.AdminActor, f.Microsoft, f.Options,
      new(TenantAdministrationFixture.Key(), approved.Value, member.Value, true, "Joined engagement team"), DateTimeOffset.UtcNow);
    Assert.True(add.Succeeded, add.Message);
    Assert.Equal(ExternalOperationStates.Accepted, add.Value!.State);
    Assert.False(add.Value.ExistingMembership);
    Assert.True(await f.Microsoft.IsMemberAsync(f.TenantId, f.GroupObjectId, f.MemberObjectId, default));
    var duplicate = await ManagedGroupService.ChangeMembershipAsync(db, f.AdminActor, f.Microsoft, f.Options,
      new(TenantAdministrationFixture.Key(), approved.Value, member.Value, true, "Duplicate add"), DateTimeOffset.UtcNow);
    Assert.Equal("NO_CHANGE", duplicate.Value!.State);
    var remove = await ManagedGroupService.ChangeMembershipAsync(db, f.AdminActor, f.Microsoft, f.Options,
      new(TenantAdministrationFixture.Key(), approved.Value, member.Value, false, "Left engagement team"), DateTimeOffset.UtcNow);
    Assert.Equal(ExternalOperationStates.Accepted, remove.Value!.State);
    Assert.False(await f.Microsoft.IsMemberAsync(f.TenantId, f.GroupObjectId, f.MemberObjectId, default));
    Assert.Equal(grantsBefore, await db.RoleGrants.CountAsync());
    var events = await db.Microsoft365AdministrationEvents.Where(x => x.Operation.Contains("GROUP_MEMBER")).ToListAsync();
    Assert.Contains(events, x => x.Operation == "ADD_GROUP_MEMBER_REQUESTED" && x.Reason == "Joined engagement team");
    Assert.Contains(events, x => x.Operation == "ADD_GROUP_MEMBER_NO_CHANGE");
    Assert.Contains(events, x => x.Operation == "REMOVE_GROUP_MEMBER_OUTCOME" && x.NewState == ExternalOperationStates.Accepted);

    // A group that is not allowlisted cannot be targeted.
    var unknownGroup = await ManagedGroupService.ChangeMembershipAsync(db, f.AdminActor, f.Microsoft, f.Options,
      new(TenantAdministrationFixture.Key(), Guid.NewGuid(), member.Value, true, "Unlisted group"), DateTimeOffset.UtcNow);
    Assert.Equal(ErrorCodes.ScopeDenied, unknownGroup.ErrorCode);
  }

  // ---- Cross-firm isolation and nondisclosure (§18) ----

  [Fact]
  public async Task OtherFirmsAndNonAdministratorsSeeNothing()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await f.ConnectAndVerifyAsync();
    await using var db = f.Db();
    var created = await DirectoryProvisioningService.CreateTenantUserAsync(db, f.AdminActor, f.Microsoft, f.Options,
      new(TenantAdministrationFixture.Key(), "Firm A User", "firma.user@example.test", "firma.user", true, "Staff", "FIRM_WIDE", null, null, "Cross-firm test"),
      DateTimeOffset.UtcNow);
    Assert.True(created.Succeeded);

    var otherFirm = Guid.NewGuid();
    var outsider = TenantAdministrationFixture.User(otherFirm, f.TenantId, "outsider@example.test", "Other Firm Admin");
    db.Users.Add(outsider);
    db.RoleGrants.Add(new Domain.Security.RoleGrant { Id = Guid.CreateVersion7(), FirmId = otherFirm, UserId = outsider.Id,
      Role = "Administrator", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = outsider.Id });
    await db.SaveChangesAsync();
    var outsiderActor = new Application.Abstractions.ActorContext(outsider.Id, otherFirm, outsider.SessionEpoch, ["Administrator"]);
    var workspace = await UserAccessWorkspaceQuery.GetAsync(db, outsiderActor, DateTimeOffset.UtcNow);
    Assert.True(workspace.Succeeded);
    Assert.DoesNotContain(workspace.Value!.Users, x => x.MicrosoftIdentity == "firma.user@example.test");
    Assert.Empty(workspace.Value.Operations);
    var resume = await DirectoryProvisioningService.ResumeAsync(db, outsiderActor, created.Value!.OperationId,
      new(f.Microsoft, f.Microsoft, f.Microsoft), f.Options, DateTimeOffset.UtcNow);
    Assert.Equal(ErrorCodes.ScopeDenied, resume.ErrorCode);
    // Cross-firm actor using firm A's id is denied.
    var spoofed = outsiderActor with { FirmId = f.FirmId };
    Assert.False((await AdministrationOverviewQuery.GetAsync(db, spoofed, f.Options, null, DateTimeOffset.UtcNow)).Succeeded);

    // A staff member of firm A without Administrator sees no counts.
    var staffId = created.Value.BoundUserId!.Value;
    var staff = await db.Users.AsNoTracking().SingleAsync(x => x.Id == staffId);
    var staffActor = new Application.Abstractions.ActorContext(staff.Id, f.FirmId, staff.SessionEpoch, ["Staff"]);
    Assert.Equal(ErrorCodes.ScopeDenied, (await AdministrationOverviewQuery.GetAsync(db, staffActor, f.Options, null, DateTimeOffset.UtcNow)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await UserAccessWorkspaceQuery.GetAsync(db, staffActor, DateTimeOffset.UtcNow)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await DirectoryDiscoveryService.SearchAsync(db, staffActor, f.Microsoft, f.TenantId, "Di", null)).ErrorCode);
  }

  [Fact]
  public async Task Overview_ProgressIsDerivedFromPersistedVerification()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await using var db = f.Db();
    var before = (await AdministrationOverviewQuery.GetAsync(db, f.AdminActor, f.Options, null, DateTimeOffset.UtcNow)).Value!;
    Assert.Equal(ProgressStates.Pending, before.Progress.Single(x => x.Key == "consent").State);
    Assert.Equal(ProgressStates.Done, before.Progress.Single(x => x.Key == "administrator").State);
    Assert.NotNull(before.Progress.Single(x => x.Key == "consent").RequiredAuthority);
    Assert.Contains(before.SecurityWarnings, x => x.Contains("consent", StringComparison.OrdinalIgnoreCase));
    await f.ConnectAndVerifyAsync();
    var after = (await AdministrationOverviewQuery.GetAsync(db, f.AdminActor, f.Options, null, DateTimeOffset.UtcNow)).Value!;
    Assert.Equal(ProgressStates.Done, after.Progress.Single(x => x.Key == "consent").State);
    Assert.Equal(ProgressStates.Done, after.Progress.Single(x => x.Key == "directory").State);
    Assert.Equal(ProgressStates.Pending, after.Progress.Single(x => x.Key == "assignment").State);
    Assert.Equal("VERIFIED", after.Cards.Single(x => x.Key == "tenant").State);
    Assert.Equal(7, after.Progress.Count);
    Assert.Equal(Microsoft365PermissionMatrix.Rows.Count, after.PermissionMatrix.Count);
  }

  [Fact]
  public void PermissionMatrix_DocumentsEveryCapability_AndUsesNoProhibitedPermission()
  {
    Assert.Equal(Microsoft365Capabilities.All.OrderBy(x => x), Microsoft365PermissionMatrix.Rows.Select(x => x.Capability).OrderBy(x => x));
    foreach (var row in Microsoft365PermissionMatrix.Rows)
    {
      Assert.False(string.IsNullOrWhiteSpace(row.GraphEndpoint));
      Assert.False(string.IsNullOrWhiteSpace(row.WhyRequired));
      Assert.False(string.IsNullOrWhiteSpace(row.AdminConsent));
      Assert.DoesNotContain(Microsoft365PermissionMatrix.Prohibited, p => row.Permission.Contains(p, StringComparison.OrdinalIgnoreCase));
    }
    Assert.Equal("User.Create", Microsoft365PermissionMatrix.For(Microsoft365Capabilities.TenantUserProvisioning).Permission);
    Assert.Equal("Sites.Selected", Microsoft365PermissionMatrix.For(Microsoft365Capabilities.SelectedSite).Permission);
    Assert.DoesNotContain(Microsoft365PermissionMatrix.Rows, x => x.Capability == Microsoft365Capabilities.PrivilegedRoleAdministration);
  }

  internal static async Task AssertNoSecretAsync(AuditSphereDbContext db, string secret)
  {
    db.ChangeTracker.Clear();
    var dump = JsonSerializer.Serialize(new object[]
    {
      await db.Microsoft365ExternalOperations.ToListAsync(), await db.Microsoft365AdministrationEvents.ToListAsync(),
      await db.DirectoryUserObservations.ToListAsync(), await db.Users.ToListAsync(), await db.RoleGrants.ToListAsync(),
      await db.RoleGrantChangeEvidences.ToListAsync(), await db.UserAccessInvitations.ToListAsync(),
      await db.TenantCapabilityVerifications.ToListAsync(), await db.TenantConsentAttempts.ToListAsync(),
      await db.IntegrationVerificationEvidences.ToListAsync()
    });
    Assert.DoesNotContain(secret, dump, StringComparison.Ordinal);
    Assert.DoesNotContain("eyJ", dump, StringComparison.Ordinal); // no JWT-shaped token
    Assert.DoesNotContain("refresh_token", dump, StringComparison.OrdinalIgnoreCase);
  }

  private sealed class FixedIdentityVerifier(ConsentingAdministrator identity) : IMicrosoftTenantConsentVerifier
  {
    public int Redemptions { get; private set; }
    public bool IsConfigured => true;
    public Uri BuildIdentityChallenge(string tenantId, string state, string nonce) => new("https://example.test");
    public Task<ConsentingAdministrator> RedeemIdentityAsync(string tenantId, string code, CancellationToken ct)
    {
      Redemptions++;
      return Task.FromResult(identity);
    }
    public Task<IReadOnlyList<CapabilityProbeResult>> VerifyCapabilitiesAsync(string tenantId,
      IReadOnlyList<CapabilityProbe> capabilities, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<CapabilityProbeResult>>([]);
  }
}
