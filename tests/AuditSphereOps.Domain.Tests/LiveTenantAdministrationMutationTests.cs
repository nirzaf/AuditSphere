using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// Separately controlled live acceptance of optional tenant-administration mutations. It creates real directory
/// objects, so it runs only when AUDITSPHERE_LIVE_M365_MUTATIONS=1 and every live input is supplied; otherwise
/// it reports BLOCKED_EXTERNAL. Outbound mail remains blocked until this harness is bound to an owner-approved
/// retained Acceptance installation with its own current connection, consent, and independently signed evidence.
/// Inputs: the AUDITSPHERE_LIVE_M365_TENANT_ID and READER/PROVISIONING/INVITATION/GROUPS/MAIL CLIENT_ID, CERT and KEY triples used by
/// <see cref="LiveMicrosoftTenantAcceptanceTests"/>, plus CONSENT_CLIENT_ID, CONSENT_REDIRECT_URI, RECIPIENT (an operator-approved
/// external address), GROUP_ID (a non-role-assignable security group) and UPN_DOMAIN. The operator removes the
/// synthetic user and guest it reports (the administration identity deliberately cannot delete users).
/// </summary>
[Trait("Category", "LiveMicrosoft")]
public sealed class LiveTenantAdministrationMutationTests(ITestOutputHelper output)
{
  private static string? Env(string name) => Environment.GetEnvironmentVariable("AUDITSPHERE_LIVE_M365_" + name);

  private static GraphCapabilityCredentialOptions? Credential(string tenant, string prefix, string role) =>
    Guid.TryParse(Env(prefix + "_CLIENT_ID"), out _) && File.Exists(Env(prefix + "_CERT")) && File.Exists(Env(prefix + "_KEY"))
      ? new(true, tenant, Env(prefix + "_CLIENT_ID")!, Env(prefix + "_CERT")!, Env(prefix + "_KEY")!, role)
      : null;

  [Fact]
  [Trait("CaseId", "M365-ADMIN-LIVE-MUTATIONS-01")]
  public async Task ProvisioningGroupMembershipGuestInvitationAndMail_OrBlockedExternal()
  {
    var tenant = Env("TENANT_ID");
    var reader = Guid.TryParse(tenant, out _) ? Credential(tenant!, "READER", "User.Read.All") : null;
    var create = reader is null ? null : Credential(tenant!, "PROVISIONING", "User.Create");
    var invite = reader is null ? null : Credential(tenant!, "INVITATION", "User.Invite.All");
    var groups = reader is null ? null : Credential(tenant!, "GROUPS", "GroupMember.ReadWrite.All");
    var mail = reader is null ? null : Credential(tenant!, "MAIL", "Mail.Send");
    if (Env("MUTATIONS") != "1")
    {
      output.WriteLine("BLOCKED_EXTERNAL: tenant user provisioning was not opted in with AUDITSPHERE_LIVE_M365_MUTATIONS=1.");
      output.WriteLine("BLOCKED_EXTERNAL: group membership mutation was not opted in with AUDITSPHERE_LIVE_M365_MUTATIONS=1.");
      output.WriteLine("BLOCKED_EXTERNAL: guest invitation was not opted in with AUDITSPHERE_LIVE_M365_MUTATIONS=1.");
      output.WriteLine("BLOCKED_EXTERNAL: outbound mail requires a separate retained Acceptance target and signed current evidence.");
      return;
    }

    var mailBlocker = LiveMailPrerequisiteBlocker(tenant, mail, Env("RECIPIENT"));
    if (mailBlocker is not null) output.WriteLine($"BLOCKED_EXTERNAL: outbound mail: {mailBlocker}");
    var directoryBlockers = new List<string>();
    if (reader is null) directoryBlockers.Add("Directory read credential is missing.");
    if (create is null) directoryBlockers.Add("Tenant user provisioning credential is missing.");
    if (invite is null || string.IsNullOrWhiteSpace(Env("RECIPIENT"))) directoryBlockers.Add("Guest invitation credential or approved recipient is missing.");
    if (groups is null || !Guid.TryParse(Env("GROUP_ID"), out _)) directoryBlockers.Add("Group membership credential or approved group identity is missing.");
    if (string.IsNullOrWhiteSpace(Env("UPN_DOMAIN"))) directoryBlockers.Add("Provisioning UPN domain is missing.");
    foreach (var blocker in directoryBlockers) output.WriteLine($"BLOCKED_EXTERNAL: directory mutation preflight: {blocker}");
    if (directoryBlockers.Count != 0 || mailBlocker is not null)
    {
      output.WriteLine("BLOCKED_EXTERNAL: the combined live mutation run stopped before any directory or mail provider call.");
      return;
    }

    if (reader is null || create is null || invite is null || groups is null)
      return;

    var groupId = Guid.Parse(Env("GROUP_ID")!);

    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
    var readerTokens = new GraphCapabilityTokenSource(http, reader);
    var provisioner = new GraphDirectoryUserProvisioner(http, new GraphCapabilityTokenSource(http, create), readerTokens);
    var invitations = new GraphGuestInvitationProvider(http, new GraphCapabilityTokenSource(http, invite!), readerTokens);
    var membership = new GraphGroupMembershipProvider(http, new GraphCapabilityTokenSource(http, groups!));
    var verifier = new GraphTenantConsentVerifier(http, new TenantConsentVerifierOptions(true, tenant!, Env("CONSENT_CLIENT_ID") ?? string.Empty,
        Env("CONSENT_REDIRECT_URI") ?? string.Empty, reader.CertificatePath, reader.PrivateKeyPath),
      new Dictionary<string, GraphCapabilityTokenSource>
      {
        [Microsoft365Capabilities.DirectoryRead] = readerTokens,
        [Microsoft365Capabilities.TenantUserProvisioning] = new(http, create),
        [Microsoft365Capabilities.GuestInvitation] = new(http, invite!),
        [Microsoft365Capabilities.GroupMembership] = new(http, groups!),
      });

    await using var f = await TenantAdministrationFixture.CreateAsync(tenant);
    // Consent identity leg on the local simulator; capability verification below uses live Microsoft tokens.
    await f.ConnectAndVerifyAsync();
    // Use a reachable HTTPS redemption destination for a real invitation, rather than a local-only callback.
    var options = f.Options with { OutboundMailEnabled = false, GuestRedirectUrl = "https://myapps.microsoft.com/" };
    var now = DateTimeOffset.UtcNow;
    await using (var db = f.Db())
    {
      var verified = await TenantCapabilityService.VerifyAsync(db, f.AdminActor, verifier, options, now);
      Assert.True(verified.Succeeded, verified.Message);
      foreach (var status in verified.Value!) output.WriteLine($"live capability {status.Capability}: {status.State}");
      foreach (var capability in new[] { Microsoft365Capabilities.DirectoryRead, Microsoft365Capabilities.TenantUserProvisioning,
                 Microsoft365Capabilities.GuestInvitation, Microsoft365Capabilities.GroupMembership })
        Assert.Equal(CapabilityVerificationStates.Verified, verified.Value!.Single(x => x.Capability == capability).State);
      Assert.DoesNotContain(verified.Value!, x => x.Capability == Microsoft365Capabilities.OutboundMail &&
        x.State == CapabilityVerificationStates.Verified);
    }

    var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
    var upn = $"auditsphere-synthetic-{stamp}@{Env("UPN_DOMAIN")}";
    ExternalOperationResult created;
    await using (var db = f.Db())
    {
      var key = TenantAdministrationFixture.Key();
      var request = new CreateTenantUserRequest(key, $"AuditSphere Synthetic {stamp}", upn, $"auditsphere-synthetic-{stamp}", true,
        "Staff", "CLIENT", f.ClientId, null, "Live acceptance of optional tenant user provisioning.");
      var result = await DirectoryProvisioningService.CreateTenantUserAsync(db, f.AdminActor, provisioner, options, request, now);
      Assert.True(result.Succeeded, result.Message);
      created = result.Value!;
      Assert.Equal(ExternalOperationStates.Bound, created.State);
      Assert.NotNull(created.BoundUserId);
      Assert.False(string.IsNullOrEmpty(created.TemporaryPassword));
      // Idempotent retry of the same request returns the same operation and never creates a second user.
      var retry = await DirectoryProvisioningService.CreateTenantUserAsync(db, f.AdminActor, provisioner, options, request, now);
      Assert.True(retry.Succeeded, retry.Message);
      Assert.Equal((created.OperationId, created.BoundUserId), (retry.Value!.OperationId, retry.Value.BoundUserId));
      Assert.Null(retry.Value.TemporaryPassword);
      await TenantAdministrationTests.AssertNoSecretAsync(db, created.TemporaryPassword!);
    }
    // Directory reads are eventually consistent after a write; wait boundedly for the new user to be readable.
    var directoryUser = await EventuallyAsync(() => provisioner.FindByUserPrincipalNameAsync(tenant!, upn, default), x => x is not null);
    Assert.NotNull(directoryUser);
    output.WriteLine($"created and bound synthetic user; cleanup-required user {directoryUser!.ObjectId}");

    var groupRemovalAccepted = false;
    await using (var db = f.Db())
    {
      var approved = await ManagedGroupService.ApproveGroupAsync(db, f.AdminActor, membership, options, groupId.ToString("D"),
        "Live acceptance collaboration group", "Live acceptance of allowlisted group membership.", now);
      Assert.True(approved.Succeeded, approved.Message);
      var add = await ManagedGroupService.ChangeMembershipAsync(db, f.AdminActor, membership, options,
        new(TenantAdministrationFixture.Key(), approved.Value, created.BoundUserId!.Value, true, "Live acceptance membership add."), now);
      Assert.True(add.Succeeded, add.Message);
      output.WriteLine($"group add: {add.Value!.State}");
      Assert.True(await EventuallyAsync(() => membership.IsMemberAsync(tenant!, groupId.ToString("D"), directoryUser.ObjectId, default), x => x));
      var remove = await ManagedGroupService.ChangeMembershipAsync(db, f.AdminActor, membership, options,
        new(TenantAdministrationFixture.Key(), approved.Value, created.BoundUserId.Value, false, "Live acceptance membership removal."), now);
      Assert.True(remove.Succeeded, remove.Message);
      output.WriteLine($"group remove: {remove.Value!.State}");
      if (remove.Value.OperationId is { } removeOperationId)
      {
        var removeOperation = await db.Microsoft365ExternalOperations.AsNoTracking().SingleAsync(x => x.Id == removeOperationId);
        output.WriteLine($"group remove provider result: {removeOperation.ResultCode ?? "none"}; correlation: {removeOperation.ProviderCorrelationId ?? "none"}");
      }
      groupRemovalAccepted = remove.Value.State is ExternalOperationStates.Accepted or "NO_CHANGE";
      if (groupRemovalAccepted)
        Assert.False(await EventuallyAsync(() => membership.IsMemberAsync(tenant!, groupId.ToString("D"), directoryUser.ObjectId, default), x => !x));
    }

    await using (var db = f.Db())
    {
      var guest = await DirectoryProvisioningService.InviteGuestAsync(db, f.AdminActor, invitations, options,
        new(TenantAdministrationFixture.Key(), Env("RECIPIENT")!, f.ClientId, null, "Live acceptance of guest invitation."), now);
      Assert.True(guest.Succeeded, guest.Message);
      output.WriteLine($"guest invitation: {guest.Value!.State}");
      var guestOperation = await db.Microsoft365ExternalOperations.AsNoTracking()
        .SingleAsync(x => x.Id == guest.Value.OperationId);
      output.WriteLine($"guest provider result: {guestOperation.ResultCode ?? "none"}; correlation: {guestOperation.ProviderCorrelationId ?? "none"}");
      Assert.Equal(ExternalOperationStates.Bound, guest.Value.State);
      var guests = await EventuallyAsync(() => invitations.FindGuestsByEmailAsync(tenant!, Env("RECIPIENT")!, default), x => x.Count > 0);
      Assert.NotEmpty(guests);
      output.WriteLine($"cleanup-required guest {guests[0].ObjectId}");
      Assert.Contains(await db.Microsoft365AdministrationEvents.AsNoTracking()
        .Where(x => x.ExternalOperationId == guest.Value.OperationId).Select(x => x.NewState).ToListAsync(), x => x == ExternalOperationStates.Bound);
    }

    Assert.True(groupRemovalAccepted, "Microsoft did not confirm group removal; clean up the synthetic member and review the saved provider result.");
  }

  private static string? LiveMailPrerequisiteBlocker(string? tenant, GraphCapabilityCredentialOptions? mail, string? recipient)
  {
    if (mail is null || string.IsNullOrWhiteSpace(recipient))
      return "the MAIL credential or operator-approved recipient is missing.";

    try
    {
      var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
      var observedCommit = MailAuthorizationDeploymentConfiguration.ReadBuildRepositoryCommit(
        typeof(AuditSphereOps.Worker.Worker).Assembly, typeof(MailAuthorizationRuntimeOptions).Assembly,
        typeof(MailAuthorizationAttestationFileSource).Assembly);
      if (!Guid.TryParse(configuration["Worker:FirmId"], out var firmId) || firmId == Guid.Empty)
        return "the configured worker firm identity is unavailable.";
      var authorization = MailAuthorizationDeploymentConfiguration.Read(configuration, firmId, observedCommit);
      var graph = new GraphMailOptions(configuration["GraphMail:TenantId"] ?? string.Empty,
        configuration["GraphMail:ClientId"] ?? string.Empty, configuration["GraphMail:SenderObjectId"] ?? string.Empty,
        configuration["GraphMail:CertificatePath"] ?? string.Empty, configuration["GraphMail:PrivateKeyPath"] ?? string.Empty,
        firmId);
      graph.Validate();
      if (!Guid.TryParse(tenant, out var liveTenant) || liveTenant != authorization.TenantId ||
          graph.TenantId != authorization.TenantId.ToString("D") || graph.ClientId != authorization.RuntimeClientId.ToString("D") ||
          !Guid.TryParse(graph.SenderObjectId, out var sender) || !authorization.SenderMailboxObjectIds.Contains(sender) ||
          !Guid.TryParse(mail.TenantId, out var credentialTenant) || credentialTenant != liveTenant ||
          mail.ClientId != graph.ClientId || mail.CertificatePath != graph.CertificatePath || mail.PrivateKeyPath != graph.PrivateKeyPath ||
          authorization.PermissionModel != MailAuthorizationModel.LegacyApplicationAccessPolicy)
        return "the live credential does not match the exact deployment-bound Legacy tenant, client, sender, firm, and certificate identity.";

      var evidenceSource = (configuration["MailAuthorization:Evidence:Source"] ?? "FILE").Trim().ToUpperInvariant();
      if (evidenceSource == "FILE")
      {
        new MailAuthorizationAttestationFileSourceOptions(
          configuration["MailAuthorization:Evidence:CanonicalPayloadPath"] ?? string.Empty,
          configuration["MailAuthorization:Evidence:DetachedCmsPath"] ?? string.Empty).Validate();
      }
      else if (evidenceSource != "ENROLLED" || authorization.PermissionModel != MailAuthorizationModel.LegacyApplicationAccessPolicy)
      {
        return "the configured mail evidence source is unsupported.";
      }
    }
    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
    {
      return "deployment-owned GraphMail, MailAuthorization, build-SHA, certificate, or signed-evidence configuration is incomplete or invalid.";
    }

    return "this harness has no owner-approved retained Acceptance database/target binding; its random PBC fixture has no matching current connection, consent, or independently signed receipt, and the recipient approval is not bound to that target.";
  }

  private static async Task<T> EventuallyAsync<T>(Func<Task<T>> read, Func<T, bool> done)
  {
    var value = await read();
    for (var attempt = 0; attempt < 20 && !done(value); attempt++)
    {
      await Task.Delay(TimeSpan.FromSeconds(3));
      value = await read();
    }
    return value;
  }
}
