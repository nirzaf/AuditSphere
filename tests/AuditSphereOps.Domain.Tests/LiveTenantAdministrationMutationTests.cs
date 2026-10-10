using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// Separately controlled live acceptance of the optional tenant-administration mutations and outbound mail
/// through the real Application services, external-operation lifecycle and durable mail worker. It creates
/// real directory objects and sends real email, so it runs only when AUDITSPHERE_LIVE_M365_MUTATIONS=1 and
/// every live input is supplied as a private environment variable; otherwise it reports BLOCKED_EXTERNAL.
/// Inputs: the AUDITSPHERE_LIVE_M365_TENANT_ID and READER/PROVISIONING/INVITATION/GROUPS/MAIL CLIENT_ID, CERT and KEY triples used by
/// <see cref="LiveMicrosoftTenantAcceptanceTests"/>, plus CONSENT_CLIENT_ID, CONSENT_REDIRECT_URI, MAIL_SENDER, RECIPIENT (an operator-approved
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
    if (Env("MUTATIONS") != "1" || reader is null || create is null || invite is null || groups is null || mail is null ||
        !Guid.TryParse(Env("GROUP_ID"), out var groupId) || string.IsNullOrWhiteSpace(Env("RECIPIENT")) ||
        string.IsNullOrWhiteSpace(Env("MAIL_SENDER")) || string.IsNullOrWhiteSpace(Env("UPN_DOMAIN")))
    {
      output.WriteLine("BLOCKED_EXTERNAL: live tenant-administration mutation inputs and operator opt-in are not available to this run.");
      return;
    }

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
        [Microsoft365Capabilities.OutboundMail] = new(http, mail),
      });

    await using var f = await TenantAdministrationFixture.CreateAsync(tenant);
    // Consent identity leg on the local simulator; capability verification below uses live Microsoft tokens.
    await f.ConnectAndVerifyAsync();
    // Use a reachable HTTPS redemption destination for a real invitation, rather than a local-only callback.
    var options = f.Options with { OutboundMailEnabled = true, GuestRedirectUrl = "https://myapps.microsoft.com/" };
    var now = DateTimeOffset.UtcNow;
    await using (var db = f.Db())
    {
      var verified = await TenantCapabilityService.VerifyAsync(db, f.AdminActor, verifier, options, now);
      Assert.True(verified.Succeeded, verified.Message);
      foreach (var status in verified.Value!) output.WriteLine($"live capability {status.Capability}: {status.State}");
      foreach (var capability in new[] { Microsoft365Capabilities.DirectoryRead, Microsoft365Capabilities.TenantUserProvisioning,
                 Microsoft365Capabilities.GuestInvitation, Microsoft365Capabilities.GroupMembership, Microsoft365Capabilities.OutboundMail })
        Assert.Equal(CapabilityVerificationStates.Verified, verified.Value!.Single(x => x.Capability == capability).State);
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

    // Outbound mail: a sent PBC request queues an email to the client contact; the isolated Acceptance
    // "mail" worker delivers it through Microsoft Graph with the Mail.Send identity.
    await using var pg = await PgTestSchema.CreateAsync();
    var pbc = await PbcSeed.SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      (await db.Users.SingleAsync(x => x.Id == pbc.Client.Id)).Email = Env("RECIPIENT")!;
      await db.SaveChangesAsync();
      var staff = PbcSeed.Actor(pbc.Staff, "Staff");
      var request = await PbcService.CreateRequestAsync(db, staff, new CreatePbcRequestRequest(pbc.EngagementId,
        "AuditSphere live mail acceptance — no action needed.", "SYNTHETIC ENTITY", "2026-01-01", "2026-12-31", "Cash", "PDF",
        string.Empty, pbc.Client.Id, pbc.Staff.Id, pbc.Reviewer.Id, "2027-01-31", "Internal", "Synthetic live acceptance message."));
      Assert.True(request.Succeeded, request.ErrorCode);
      Assert.True((await PbcService.SendRequestAsync(db, staff, request.Value, 1, "http://localhost:5099/")).Succeeded);
    }
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var handler = new PbcMailDeliveryHandler(factory, new GraphPbcMailSender(http,
      new GraphMailOptions(tenant!, mail.ClientId, Env("MAIL_SENDER")!, mail.CertificatePath, mail.PrivateKeyPath)));
    var workerOptions = new WorkerOptions(pbc.FirmId, "Acceptance", ExternalEffectsEnabled: true, Group: "mail");
    var worker = new AuditSphereOps.Worker.Worker(new OperationDispatcher(store, new DurableOperationRegistry([handler], workerOptions), workerOptions),
      [new PbcMailDiscovery(factory, store, handler, workerOptions)], NullLogger<AuditSphereOps.Worker.Worker>.Instance);
    Assert.True(await worker.ProcessNextAsync());
    Assert.False(await worker.ProcessNextAsync());
    await using (var verify = new AuditSphereDbContext(pg.Options))
    {
      var sent = await verify.PbcCommunications.SingleAsync(x => x.Kind == PbcCommunicationKinds.Email);
      Assert.Equal(PbcDeliveryStates.Sent, sent.DeliveryState);
      var operation = await verify.DurableOperations.SingleAsync(x => x.TargetId == sent.Id);
      Assert.Equal((OperationState.COMPLETED, OperationMode.LIVE), (operation.Status, operation.ExecutionMode));
      output.WriteLine("PBC notification accepted by Microsoft Graph sendMail (202) through the durable Acceptance mail worker; delivery to the mailbox is confirmed by the recipient, not by Graph.");
    }
    Assert.True(groupRemovalAccepted, "Microsoft did not confirm group removal; clean up the synthetic member and review the saved provider result.");
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
