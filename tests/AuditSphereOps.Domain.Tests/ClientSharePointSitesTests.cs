using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class ClientSharePointSitesTests
{
  private const string Tenant = "11111111-1111-1111-1111-111111111111";
  private sealed class FakeSites : IClientSharePointSiteProvider
  {
    public readonly Dictionary<Guid, ClientSiteReceipt> Sites = [];
    public readonly List<(Guid Client, bool Create)> Calls = [];
    public bool LoseNextResponse, WrongTenant;
    public Task<ClientSiteReceipt> ReconcileAsync(ClientSiteRequest r, bool mayCreate, CancellationToken ct)
    {
      Calls.Add((r.ClientId, mayCreate));
      if (!Sites.TryGetValue(r.ClientId, out var site))
      {
        if (!mayCreate) throw new OperationBlockedException("unknown-site-needs-review");
        Sites[r.ClientId] = site = new(r.TenantId, r.Url, r.OwnershipMarker, "site:" + r.ClientId, "drive:" + r.ClientId,
          "root:" + r.ClientId, "42", r.StaffObjectIds, Guid.NewGuid().ToString("D"));
      }
      site = site with { MemberObjectIds = r.StaffObjectIds, TenantId = WrongTenant ? Guid.NewGuid().ToString("D") : r.TenantId };
      Sites[r.ClientId] = site;
      if (LoseNextResponse) { LoseNextResponse = false; throw new HttpRequestException("fake accepted then disconnected"); }
      return Task.FromResult(site);
    }
  }
  private sealed record Rig(P2PbcHarness H, FakeSites Provider, ClientSharePointSiteHandler Handler, ClientSharePointSiteDiscovery Discovery,
    OperationDispatcher Dispatcher, Guid StaffBId);

  private static async Task<Rig> CreateAsync()
  {
    var graph = new FakeGraphDrive();
    var drive = new GraphSelectedSiteDrive(new HttpClient(graph), new FakeSelectedSiteTokens(), new GraphPreauthenticatedTransport(graph), true);
    var h = await P2PbcHarness.CreateAsync(drive, Tenant, "slot:selected-site", "site-a", FakeGraphDrive.DriveId, FakeGraphDrive.RootId);
    await using var db = h.Db();
    var staffB = PbcSeed.User(h.A.FirmId, "Staff"); db.Users.Add(staffB);
    db.RoleGrants.Add(PbcSeed.Grant(h.A.FirmId, staffB, "Staff", h.B.ClientId, h.B.EngagementId));
    await db.SaveChangesAsync();
    foreach (var fixture in new[] { h.A, h.B })
    {
      var staff = await db.Users.SingleAsync(x => x.Id == (fixture == h.A ? h.A.Staff.Id : staffB.Id));
      staff.TenantId = Tenant; staff.Subject = Guid.NewGuid().ToString("D");
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == staff.Id && x.EngagementId == fixture.EngagementId && x.Role == "Staff");
      db.EngagementStaffAssignments.Add(new EngagementStaffAssignment { Id = Guid.NewGuid(), FirmId = fixture.FirmId,
        ClientId = fixture.ClientId, EngagementId = fixture.EngagementId, UserId = staff.Id, RoleGrantId = grant.Id,
        StaffingLevel = StaffingLevels.StaffAssociate, AssignedByUserId = h.A.Admin.Id, AssignedAt = DateTimeOffset.UtcNow });
    }
    await db.SaveChangesAsync();
    var provider = new FakeSites();
    var handler = new ClientSharePointSiteHandler(h.Factory, provider);
    var options = new WorkerOptions(h.A.FirmId, "Acceptance", false, true, ClientSharePointSiteHandler.Group);
    return new(h, provider, handler, new(h.Factory, h.Store, handler, options,
        new(true, Tenant, "example.sharepoint.com", h.A.Admin.Id, DateTimeOffset.UtcNow.AddDays(-1))),
      new(h.Store, new DurableOperationRegistry([handler], options), options), staffB.Id);
  }

  [Fact]
  public void StableClientName_IsSafeAndDistinct_EvenWithDuplicateNames()
  {
    var id = Guid.NewGuid();
    var slug = ClientSharePointSites.SiteSlug("A/B: Client & Co 🌎", id);
    Assert.StartsWith("a-b-client-co-", slug);
    Assert.EndsWith(id.ToString("N"), slug);
    Assert.NotEqual(slug, ClientSharePointSites.SiteSlug("A/B: Client & Co 🌎", Guid.NewGuid()));
    Assert.StartsWith("client-", ClientSharePointSites.SiteSlug("🌎", id));
  }

  [Fact]
  public async Task ProvisioningIntentCannotCrossClientOrEngagementBoundary()
  {
    var rig = await CreateAsync(); await using var _ = rig.H;
    var payload = JsonSerializer.Serialize(new { desiredDigest = new string('a', 64) });
    var crossClient = new OperationRequest(rig.H.A.FirmId, rig.H.A.ClientId, null,
      ClientSharePointSiteHandler.Kind, rig.H.B.ClientId, 1, "cross-client", payload);
    var engagementScoped = new OperationRequest(rig.H.A.FirmId, rig.H.A.ClientId, rig.H.A.EngagementId,
      ClientSharePointSiteHandler.Kind, rig.H.A.ClientId, 1, "engagement-scoped", payload);

    Assert.Throws<OperationBlockedException>(() => rig.Handler.NormalizePayload(crossClient));
    Assert.Throws<OperationBlockedException>(() => rig.Handler.NormalizePayload(engagementScoped));
    Assert.Empty(rig.Provider.Calls);
  }

  [Fact]
  public async Task EveryClientGetsAnIsolatedSite_StaffReceiveFullControlWithoutWideningLocalRoles_RepeatIsIdempotent()
  {
    var rig = await CreateAsync(); await using var _ = rig.H;
    Assert.Equal(2, await rig.Discovery.EnqueuePendingAsync(default));
    while (await rig.Dispatcher.ProcessNextAsync()) { }
    Assert.Equal(0, await rig.Discovery.EnqueuePendingAsync(default));
    Assert.Equal(2, rig.Provider.Sites.Count);
    await using var db = rig.H.Db();
    var sites = await db.ClientSharePointSites.OrderBy(x => x.ClientId).ToListAsync();
    Assert.Equal(2, sites.Count);
    Assert.All(sites, s => { Assert.Equal("READY", s.State); Assert.Equal("VERIFIED", s.MembershipState); Assert.NotNull(s.CreationDispatchedAt); });
    Assert.NotEqual(sites[0].SiteId, sites[1].SiteId);
    Assert.NotEqual(sites[0].RequestedUrl, sites[1].RequestedUrl);
    foreach (var fixture in new[] { rig.H.A, rig.H.B })
    {
      var subject = (await db.Users.SingleAsync(x => x.Id == (fixture == rig.H.A ? rig.H.A.Staff.Id : rig.StaffBId))).Subject;
      var otherSubject = (await db.Users.SingleAsync(x => x.Id == (fixture == rig.H.A ? rig.StaffBId : rig.H.A.Staff.Id))).Subject;
      var members = rig.Provider.Sites[fixture.ClientId].MemberObjectIds;
      Assert.Contains(subject, members); Assert.DoesNotContain(otherSubject, members);
      Assert.All(await db.RoleGrants.Where(x => x.UserId == fixture.Staff.Id).ToListAsync(), g => Assert.NotNull(g.EngagementId));
    }
    Assert.True(await db.Microsoft365AdministrationEvents.AnyAsync(x => x.Operation == "CLIENT_SITE_STAFF_ACCESS" && x.NewState == "FULL_CONTROL"));
    Assert.All(await db.DurableOperations.Where(x => x.OperationKind == ClientSharePointSiteHandler.Kind).ToListAsync(), o =>
      Assert.DoesNotContain("access_token", o.ResultIdentity ?? "", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public async Task NewlyCreatedEligibleClient_GetsExactlyOneAutomaticProvisioningIntent()
  {
    var rig = await CreateAsync(); await using var _ = rig.H;
    await rig.Discovery.EnqueuePendingAsync(default);
    while (await rig.Dispatcher.ProcessNextAsync()) { }
    var createdAt = DateTimeOffset.UtcNow;
    var clientId = Guid.NewGuid();
    await using (var db = rig.H.Db())
    {
      db.PracticeClients.Add(new PracticeClient { Id = clientId, FirmId = rig.H.A.FirmId,
        LegalName = "Synthetic rollout client", CreatedAt = createdAt });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = rig.H.A.FirmId, InputGeneration = 1 });
      await db.SaveChangesAsync();
    }

    Assert.Equal(1, await rig.Discovery.EnqueuePendingAsync(default));
    Assert.Equal(0, await rig.Discovery.EnqueuePendingAsync(default));
    await using var verify = rig.H.Db();
    var intent = await verify.DurableOperations.Where(x => x.FirmId == rig.H.A.FirmId && x.TargetId == clientId &&
      x.OperationKind == ClientSharePointSiteHandler.Kind).ToListAsync();
    Assert.Single(intent);
    Assert.Equal(clientId, intent[0].ClientId);
    Assert.Null(intent[0].EngagementId);
    while (await rig.Dispatcher.ProcessNextAsync()) { }
    Assert.Contains(clientId, rig.Provider.Sites.Keys);
  }

  [Fact]
  public async Task ConcurrentDiscoveryRequests_QueueOnlyOneIntentPerEligibleClient()
  {
    var rig = await CreateAsync(); await using var _ = rig.H;
    await Task.WhenAll(rig.Discovery.EnqueuePendingAsync(default), rig.Discovery.EnqueuePendingAsync(default));
    await using var db = rig.H.Db();
    var intents = await db.DurableOperations.Where(x => x.FirmId == rig.H.A.FirmId && x.OperationKind == ClientSharePointSiteHandler.Kind).ToListAsync();
    Assert.Equal(2, intents.Count);
    Assert.Equal(2, intents.Select(x => x.TargetId).Distinct().Count());
  }

  [Fact]
  public async Task AdditionalEngagementForClient_ReusesItsSingleClientSite()
  {
    var rig = await CreateAsync(); await using var _ = rig.H;
    await rig.Discovery.EnqueuePendingAsync(default);
    while (await rig.Dispatcher.ProcessNextAsync()) { }
    var clientId = rig.H.A.ClientId;
    await using (var db = rig.H.Db())
    {
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = Guid.NewGuid(), FirmId = rig.H.A.FirmId, PracticeClientId = clientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }
    Assert.Equal(0, await rig.Discovery.EnqueuePendingAsync(default));
    await using var verify = rig.H.Db();
    Assert.Single(await verify.ClientSharePointSites.Where(x => x.FirmId == rig.H.A.FirmId && x.ClientId == clientId).ToListAsync());
    Assert.All(await verify.DurableOperations.Where(x => x.FirmId == rig.H.A.FirmId && x.TargetId == clientId &&
      x.OperationKind == ClientSharePointSiteHandler.Kind).ToListAsync(), op => Assert.Null(op.EngagementId));
  }

  [Fact]
  public async Task DisabledPolicy_DoesNotEnqueueOrChangeExistingClientRepositories()
  {
    var rig = await CreateAsync(); await using var _ = rig.H;
    var disabled = new ClientSharePointSiteDiscovery(rig.H.Factory, rig.H.Store, rig.Handler,
      new WorkerOptions(rig.H.A.FirmId, "Acceptance", false, true, ClientSharePointSiteHandler.Group),
      new(false, Tenant, "example.sharepoint.com", rig.H.A.Admin.Id));
    List<(Guid Id, DateTimeOffset CreatedAt)> clientFacts;
    List<(Guid Id, long Revision, string? RemoteItemId)> workspaceFacts;
    await using (var before = rig.H.Db())
    {
      clientFacts = await before.PracticeClients.Select(x => new ValueTuple<Guid, DateTimeOffset>(x.Id, x.CreatedAt)).ToListAsync();
      workspaceFacts = await before.ClientWorkspaces.Select(x => new ValueTuple<Guid, long, string?>(x.Id, x.Revision, x.RemoteItemId)).ToListAsync();
    }
    Assert.Equal(0, await disabled.EnqueuePendingAsync(default));
    await using var db = rig.H.Db();
    Assert.Empty(await db.ClientSharePointSites.ToListAsync());
    Assert.Empty(await db.DurableOperations.Where(x => x.OperationKind == ClientSharePointSiteHandler.Kind).ToListAsync());
    Assert.Equal(clientFacts, await db.PracticeClients.Select(x => new ValueTuple<Guid, DateTimeOffset>(x.Id, x.CreatedAt)).ToListAsync());
    Assert.Equal(workspaceFacts, await db.ClientWorkspaces.Select(x => new ValueTuple<Guid, long, string?>(x.Id, x.Revision, x.RemoteItemId)).ToListAsync());
  }

  [Fact]
  public async Task EnabledPolicyWithoutExplicitBoundary_FailsClosedWithoutTreatingHistoryAsEligible()
  {
    var rig = await CreateAsync(); await using var _ = rig.H;
    var missingBoundary = new ClientSharePointSiteDiscovery(rig.H.Factory, rig.H.Store, rig.Handler,
      new WorkerOptions(rig.H.A.FirmId, "Acceptance", false, true, ClientSharePointSiteHandler.Group),
      new(true, Tenant, "example.sharepoint.com", rig.H.A.Admin.Id));
    var error = await Assert.ThrowsAsync<OperationBlockedException>(() => missingBoundary.EnqueuePendingAsync(default));
    Assert.Equal("client-site-rollout-boundary-required", error.Code);
    await using var db = rig.H.Db();
    Assert.Empty(await db.ClientSharePointSites.ToListAsync());
    Assert.Empty(await db.DurableOperations.Where(x => x.OperationKind == ClientSharePointSiteHandler.Kind).ToListAsync());
  }

  [Fact]
  public async Task AcceptedButUnknownCreate_ReconcilesWithoutAnotherCreate_EvenAfterOperatorRearm()
  {
    var rig = await CreateAsync(); await using var _ = rig.H;
    await rig.Discovery.EnqueuePendingAsync(default);
    rig.Provider.LoseNextResponse = true;
    await rig.Dispatcher.ProcessNextAsync();
    await using (var db = rig.H.Db())
    {
      var op = await db.DurableOperations.SingleAsync(x => x.OperationKind == ClientSharePointSiteHandler.Kind && x.Status == OperationState.RESULT_UNCERTAIN);
      // A reviewed operator re-arm retains the uncertain effect as reconciliation-only.
      // It may discover the accepted site, but must never issue another create.
      var actor = PbcSeed.Actor(rig.H.A.Admin, "Administrator");
      var request = await OperatorCommandReceiptSeed.ReviewAsync(db,
        new(rig.H.A, op.Id, new(OperationCommandActions.Retry, OperationId: op.Id)));
      var rearmed = await OperationCommandWorkspace.ExecuteAsync(db, actor, request);
      Assert.True(rearmed.Succeeded, rearmed.Message);
      // Simulate the scheduler reaching the persisted five-second retry time without mutating the reviewed state transition.
      await db.DurableOperations.Where(x => x.Id == op.Id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
    }
    while (await rig.Dispatcher.ProcessNextAsync()) { }
    var client = rig.Provider.Calls[0].Client;
    Assert.Equal([true, false], rig.Provider.Calls.Where(x => x.Client == client).Select(x => x.Create));
    Assert.Equal(2, rig.Provider.Sites.Count);
  }

  [Theory]
  [InlineData("disabled")]
  [InlineData("grant-revoked")]
  [InlineData("assignment-revoked")]
  [InlineData("grant-expired")]
  [InlineData("wrong-tenant")]
  public async Task RevokedDisabledExpiredOrWrongTenantStaff_AreRemovedFromManagedMembership(string change)
  {
    var rig = await CreateAsync(); await using var _ = rig.H;
    await rig.Discovery.EnqueuePendingAsync(default); while (await rig.Dispatcher.ProcessNextAsync()) { }
    await using (var db = rig.H.Db())
    {
      var user = await db.Users.SingleAsync(x => x.Id == rig.H.A.Staff.Id);
      var assignment = await db.EngagementStaffAssignments.SingleAsync(x => x.UserId == user.Id);
      var grant = await db.RoleGrants.SingleAsync(x => x.Id == assignment.RoleGrantId);
      if (change == "disabled") user.Disabled = true;
      if (change == "wrong-tenant") user.TenantId = Guid.NewGuid().ToString("D");
      if (change == "grant-revoked") grant.RevokedAt = DateTimeOffset.UtcNow;
      if (change == "grant-expired") grant.ExpiresAt = grant.GrantedAt.AddMilliseconds(1);
      if (change == "assignment-revoked") { assignment.RevokedAt = DateTimeOffset.UtcNow; assignment.RevokedByUserId = rig.H.A.Admin.Id; }
      (await db.ClientSharePointSites.SingleAsync(x => x.ClientId == rig.H.A.ClientId)).LastMembershipSyncAt = null;
      await db.SaveChangesAsync();
    }
    Assert.Equal(1, await rig.Discovery.EnqueuePendingAsync(default));
    while (await rig.Dispatcher.ProcessNextAsync()) { }
    Assert.Empty(rig.Provider.Sites[rig.H.A.ClientId].MemberObjectIds);
    Assert.Single(rig.Provider.Sites[rig.H.B.ClientId].MemberObjectIds);
  }

  [Fact]
  public async Task WrongTenantReceipt_NeverPublishesSiteOrWorkerBinding()
  {
    var rig = await CreateAsync(); await using var _ = rig.H;
    rig.Provider.WrongTenant = true;
    await rig.Discovery.EnqueuePendingAsync(default); while (await rig.Dispatcher.ProcessNextAsync()) { }
    await using var db = rig.H.Db();
    Assert.All(await db.ClientSharePointSites.ToListAsync(), s => Assert.Equal("REQUESTED", s.State));
    Assert.All(await db.DurableOperations.Where(x => x.OperationKind == ClientSharePointSiteHandler.Kind).ToListAsync(), o => Assert.Equal(OperationState.AUTHORIZATION_BLOCKED, o.Status));
    var admin = await ClientSharePointSiteQuery.GetAdministrationAsync(db, rig.H.Admin, enabled: true,
      configuredBoundary: DateTimeOffset.UtcNow.AddDays(-1), boundaryInvalid: false);
    Assert.True(admin.Succeeded);
    var blockedClient = Assert.Single(admin.Value!.Clients, x => x.ClientId == rig.H.A.ClientId);
    Assert.Equal("BLOCKED_EXTERNAL", blockedClient.State);
    Assert.NotNull(blockedClient.ErrorCode);
    Assert.Contains("retry or reconcile", blockedClient.RequiredAction, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task MissingTenantConsent_IsVisibleAndQueuesNoSiteOperations()
  {
    var rig = await CreateAsync(); await using var _ = rig.H;
    await using var db = rig.H.Db();
    var connection = await db.Microsoft365ConnectionRevisions.SingleAsync(x => x.FirmId == rig.H.A.FirmId);
    connection.ConsentState = "REQUIRED";
    await db.SaveChangesAsync();

    var admin = await ClientSharePointSiteQuery.GetAdministrationAsync(db, rig.H.Admin, enabled: true,
      configuredBoundary: DateTimeOffset.UtcNow.AddDays(-1), boundaryInvalid: false);

    Assert.True(admin.Succeeded);
    Assert.Equal("TENANT_CONSENT_REQUIRED", admin.Value!.PolicyState);
    Assert.Equal("REQUIRED", admin.Value.TenantConsentState);
    Assert.Equal("BLOCKED_TENANT_CONSENT", admin.Value.MicrosoftPermissionState);
    Assert.All(admin.Value.Clients, client => Assert.Equal("BLOCKED_EXTERNAL", client.State));
    Assert.Empty(await db.ClientSharePointSites.ToListAsync());
    Assert.Empty(await db.DurableOperations.Where(x => x.OperationKind == ClientSharePointSiteHandler.Kind).ToListAsync());
    Assert.Empty(rig.Provider.Calls);
  }

  [Fact]
  public async Task RevokedAdministratorAndClientUsers_CannotReadOrProvisionSites()
  {
    var rig = await CreateAsync(); await using var _ = rig.H;
    await using var db = rig.H.Db();
    Assert.False((await ClientSharePointSiteQuery.GetAsync(db, PbcSeed.Actor(rig.H.A.Client, "ClientUser"))).Succeeded);
    (await db.Users.SingleAsync(x => x.Id == rig.H.A.Admin.Id)).Disabled = true;
    await db.SaveChangesAsync();
    await Assert.ThrowsAsync<OperationBlockedException>(() => rig.Discovery.EnqueuePendingAsync(default));
    Assert.Empty(rig.Provider.Calls);
  }

  [Fact]
  public async Task ExplicitRollout_PreservesOlderClientsAndRefusesChangingTheBoundary()
  {
    var rig = await CreateAsync(); await using var _ = rig.H;
    var options = new WorkerOptions(rig.H.A.FirmId, "Acceptance", false, true, ClientSharePointSiteHandler.Group);
    var cutover = DateTimeOffset.UtcNow.AddDays(1);
    var discovery = new ClientSharePointSiteDiscovery(rig.H.Factory, rig.H.Store, rig.Handler, options,
      new(true, Tenant, "example.sharepoint.com", rig.H.A.Admin.Id, cutover));
    Assert.Equal(0, await discovery.EnqueuePendingAsync(default));
    await rig.H.ProvisionAsync(rig.H.A); // Before-cutover repository still works.
    await using (var before = rig.H.Db())
    {
      Assert.Empty(await before.ClientSharePointSites.ToListAsync());
      Assert.Empty(await before.DurableOperations.Where(x => x.OperationKind == ClientSharePointSiteHandler.Kind).ToListAsync());
    }
    var changed = new ClientSharePointSiteDiscovery(rig.H.Factory, rig.H.Store, rig.Handler, options,
      new(true, Tenant, "example.sharepoint.com", rig.H.A.Admin.Id, cutover.AddDays(-1)));
    await Assert.ThrowsAsync<OperationBlockedException>(() => changed.EnqueuePendingAsync(default));
    Assert.Empty(rig.Provider.Calls);
  }

  [Fact]
  public async Task PendingDedicatedSite_RefusesSharedSiteFallback_ForPbcByteReads()
  {
    var rig = await CreateAsync(); await using var _ = rig.H;
    await rig.Discovery.EnqueuePendingAsync(default);
    await using var db = rig.H.Db();
    var result = await PbcRepositoryProvisioningService.ProvisionClientWorkspaceAsync(db, rig.H.Admin, rig.H.Drive, rig.H.A.ClientId, DateTimeOffset.UtcNow);
    Assert.False(result.Succeeded);
    Assert.Contains("fallback", result.Message!);
    Assert.Equal("WAITING_FOR_INTEGRATION", (await db.ClientWorkspaces.SingleAsync(x => x.PracticeClientId == rig.H.A.ClientId)).State);
  }
}
