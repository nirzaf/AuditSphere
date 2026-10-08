using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// P2 durable-worker transfers and the P2b isolation matrix against the fake Graph drive:
/// client A/B separation, wrong tenant, guessed IDs, revoked identities, throttling and reconciliation.
/// </summary>
[Trait("Profile", "Database")]
public sealed class PbcSelectedSiteWorkerTests
{
  private static async Task<(P2PbcHarness H, FakeGraphDrive Graph)> CreateAsync(string? tokenTenant = null)
  {
    var graph = new FakeGraphDrive();
    const string tenant = "11111111-1111-1111-1111-111111111111";
    var drive = new GraphSelectedSiteDrive(new HttpClient(graph), new FakeSelectedSiteTokens(tokenTenant), new GraphPreauthenticatedTransport(graph), configured: true);
    return (await P2PbcHarness.CreateAsync(drive, tenant, "slot:selected-site", "site-a", FakeGraphDrive.DriveId, FakeGraphDrive.RootId), graph);
  }

  private static byte[] Document(string label) => System.Text.Encoding.UTF8.GetBytes($"%PDF-1.7 {label} {Guid.NewGuid():N}");

  [Fact]
  public async Task ClientStagedDocument_TransfersThroughLiveWorker_ToReceived()
  {
    var (h, graph) = await CreateAsync();
    await using var _ = h;
    await h.ProvisionAsync(h.A);
    var staged = await h.UploadAsync(h.A, h.RequestA, Document("client A statements"));

    var queued = await h.OperationAsync(staged.UploadIntentId);
    Assert.Equal((OperationMode.LIVE, OperationAuthority.LIVE_PROVIDER, "pbc"), (queued.ExecutionMode, queued.AuthorityMode, queued.ExecutionGroup));
    Assert.True(await h.Worker.ProcessNextAsync());

    await using var db = h.Db();
    var intent = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == staged.UploadIntentId);
    Assert.Equal(PbcUploadStates.Received, intent.State);
    Assert.Equal(staged.DeclaredSha256Hex, intent.ProviderReceiptDigest);
    Assert.Equal(PbcStates.Received, (await db.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == h.RequestA)).State);
    var operation = await h.OperationAsync(staged.UploadIntentId);
    Assert.Equal(OperationState.COMPLETED, operation.Status);
    Assert.StartsWith($"graph-drive-item:{FakeGraphDrive.DriveId}:", operation.ResultIdentity);
    Assert.Single(graph.Items, x => !x.Folder && x.Name.StartsWith(staged.UploadIntentId.ToString("N"), StringComparison.Ordinal));
    Assert.Equal(0, graph.BearerSentToSharePoint);
  }

  [Fact]
  public async Task GraphThrottling_RetriesLater_ThenCompletes()
  {
    var (h, graph) = await CreateAsync();
    await using var _ = h;
    await h.ProvisionAsync(h.A);
    var staged = await h.UploadAsync(h.A, h.RequestA, Document("throttled"));
    graph.ThrottleUploadSessions = 1;

    Assert.True(await h.Worker.ProcessNextAsync());
    var waiting = await h.OperationAsync(staged.UploadIntentId);
    Assert.Equal(OperationState.RETRY_WAIT, waiting.Status);
    Assert.True(waiting.NextAttemptAt > DateTimeOffset.UtcNow);
    Assert.DoesNotContain(graph.Items, x => !x.Folder && x.Name.StartsWith(staged.UploadIntentId.ToString("N"), StringComparison.Ordinal));

    await h.MakeDueAsync();
    Assert.True(await h.Worker.ProcessNextAsync());
    Assert.Equal(OperationState.COMPLETED, (await h.OperationAsync(staged.UploadIntentId)).Status);
    Assert.Equal(2, graph.UploadSessionCalls);
  }

  [Fact]
  public async Task LostResponseAfterCommit_IsUncertain_ThenReconcilesByExactNameToReceived()
  {
    var (h, graph) = await CreateAsync();
    await using var _ = h;
    await h.ProvisionAsync(h.A);
    var staged = await h.UploadAsync(h.A, h.RequestA, Document("lost response"));
    graph.LoseFinalResponseAfterCommit = true;

    Assert.True(await h.Worker.ProcessNextAsync());
    Assert.Equal(OperationState.RESULT_UNCERTAIN, (await h.OperationAsync(staged.UploadIntentId)).Status);
    graph.LoseFinalResponseAfterCommit = false;
    await h.MakeDueAsync();
    Assert.True(await h.Worker.ProcessNextAsync()); // reconciliation probe
    await using var db = h.Db();
    Assert.Equal(PbcUploadStates.Received, (await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == staged.UploadIntentId)).State);
    Assert.Equal(1, graph.UploadSessionCalls); // never re-sent
  }

  [Fact]
  public async Task FailureBeforeCommit_ReconciliationFindsNoEffect_AndBlocks()
  {
    var (h, graph) = await CreateAsync();
    await using var _ = h;
    await h.ProvisionAsync(h.A);
    var staged = await h.UploadAsync(h.A, h.RequestA, Document("failed before commit"));
    graph.FailFinalFragmentBeforeCommit = true;

    Assert.True(await h.Worker.ProcessNextAsync());
    Assert.Equal(OperationState.RESULT_UNCERTAIN, (await h.OperationAsync(staged.UploadIntentId)).Status);
    await h.MakeDueAsync();
    Assert.True(await h.Worker.ProcessNextAsync());
    Assert.Equal(OperationState.PROVIDER_BLOCKED, (await h.OperationAsync(staged.UploadIntentId)).Status);
    await using var db = h.Db();
    Assert.Equal(PbcStates.PartiallyReceived, (await db.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == h.RequestA)).State);
    Assert.Equal(PbcUploadStates.Staged, (await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == staged.UploadIntentId)).State);
  }

  [Fact]
  public async Task ClientAAndB_AreIsolated_AndGuessedIdentifiersFailClosed()
  {
    var (h, graph) = await CreateAsync();
    await using var _ = h;
    await h.ProvisionAsync(h.A);
    await h.ProvisionAsync(h.B);
    var stagedA = await h.UploadAsync(h.A, h.RequestA, Document("client A"));
    var stagedB = await h.UploadAsync(h.B, h.RequestB, Document("client B"));
    Assert.True(await h.Worker.ProcessNextAsync());
    Assert.True(await h.Worker.ProcessNextAsync());

    var folderA = await h.RemoteItemIdAsync(h.A.ClientId);
    var folderB = await h.RemoteItemIdAsync(h.B.ClientId);
    Assert.NotEqual(folderA, folderB);
    string ClientOf(string itemId)
    {
      var items = graph.Items.ToDictionary(x => x.Id);
      for (var id = itemId; items.TryGetValue(id, out var item); id = item.ParentId)
        if (id == folderA) return "A"; else if (id == folderB) return "B";
      return "none";
    }
    var fileA = graph.Items.Single(x => !x.Folder && x.Name.StartsWith(stagedA.UploadIntentId.ToString("N"), StringComparison.Ordinal));
    var fileB = graph.Items.Single(x => !x.Folder && x.Name.StartsWith(stagedB.UploadIntentId.ToString("N"), StringComparison.Ordinal));
    Assert.Equal(("A", "B"), (ClientOf(fileA.Id), ClientOf(fileB.Id)));

    var identityA = (await h.OperationAsync(stagedA.UploadIntentId)).ResultIdentity!;
    var scopeA = h.Scope(h.A, stagedA.UploadIntentId);
    var scopeB = h.Scope(h.B, stagedB.UploadIntentId);
    var resolver = new PbcRepositoryBindingResolver(h.Factory);

    // Cross-client: B's scope can never read A's stored document, and mixed scopes never resolve.
    await Assert.ThrowsAsync<OperationBlockedException>(() => h.Sink.VerifyAsync(scopeB, identityA, default));
    await Assert.ThrowsAsync<OperationBlockedException>(() => resolver.ResolveAsync(scopeA with { ClientId = h.B.ClientId }, default));
    await Assert.ThrowsAsync<OperationBlockedException>(() => resolver.ResolveAsync(scopeA with { EngagementId = h.B.EngagementId }, default));
    await Assert.ThrowsAsync<OperationBlockedException>(() => resolver.ResolveAsync(scopeB with { UploadIntentId = stagedA.UploadIntentId }, default));

    // Guessed identifiers.
    await Assert.ThrowsAsync<OperationBlockedException>(() => resolver.ResolveAsync(scopeA with { UploadIntentId = Guid.NewGuid() }, default));
    Assert.Null(await h.Sink.VerifyAsync(scopeA, $"graph-drive-item:{FakeGraphDrive.DriveId}:{Guid.NewGuid():N}:1.0", default));
    await Assert.ThrowsAsync<OperationBlockedException>(() => h.Sink.VerifyAsync(scopeA, $"graph-drive-item:b!other-drive:{fileA.Id}:1.0", default));
    await Assert.ThrowsAsync<OperationBlockedException>(() => h.Sink.VerifyAsync(scopeA, $"graph-drive-item:{FakeGraphDrive.DriveId}:{fileB.Id}:1.0", default));
    await Assert.ThrowsAsync<OperationBlockedException>(() => h.Sink.VerifyAsync(scopeA, "sim://pbc/" + stagedA.UploadIntentId, default));
    await Assert.ThrowsAsync<OperationBlockedException>(() => h.Sink.VerifyAsync(scopeA, $"graph-drive-item:{FakeGraphDrive.DriveId}:{folderA}:1.0", default));
  }

  [Fact]
  public async Task WrongTenantToken_IsRefusedBeforeAnyGraphCall()
  {
    var (h, graph) = await CreateAsync(tokenTenant: "22222222-2222-2222-2222-222222222222");
    await using var _ = h;
    await using var db = h.Db();
    var result = await PbcRepositoryProvisioningService.ProvisionClientWorkspaceAsync(db, h.Admin, h.Drive, h.A.ClientId, DateTimeOffset.UtcNow);
    Assert.Equal(ErrorCodes.GateBlocked, result.ErrorCode);
    Assert.Equal("selected-site-token-invalid", (await db.ClientWorkspaces.AsNoTracking().SingleAsync(x => x.PracticeClientId == h.A.ClientId)).LastErrorCode);
    Assert.Single(graph.Items); // only the root: nothing was created
  }

  [Fact]
  public async Task WrongTenantConnection_BlocksResolution()
  {
    var (h, _) = await CreateAsync();
    await using var _h = h;
    await h.ProvisionAsync(h.A);
    var staged = await h.UploadAsync(h.A, h.RequestA, Document("tenant switch"));
    await using (var db = h.Db())
    {
      await db.Database.ExecuteSqlRawAsync("UPDATE m365_connection_revisions SET tenant_id = '33333333-3333-3333-3333-333333333333'");
    }
    await Assert.ThrowsAsync<OperationBlockedException>(() =>
      new PbcRepositoryBindingResolver(h.Factory).ResolveAsync(h.Scope(h.A, staged.UploadIntentId), default));
    Assert.True(await h.Worker.ProcessNextAsync());
    Assert.Equal(OperationState.AUTHORIZATION_BLOCKED, (await h.OperationAsync(staged.UploadIntentId)).Status);
  }

  [Fact]
  public async Task RevokedIdentities_CannotProvisionStageOrComplete()
  {
    var (h, _) = await CreateAsync();
    await using var _h = h;
    await h.ProvisionAsync(h.A);

    // Revoked client user: cannot start an upload for the request.
    await using (var db = h.Db())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == h.A.Client.Id && x.RevokedAt == null);
      grant.RevokedAt = DateTimeOffset.UtcNow;
      (await db.Users.SingleAsync(x => x.Id == h.A.Client.Id)).SessionEpoch++;
      await db.SaveChangesAsync();
    }
    await using (var db = h.Db())
    {
      var start = await PbcService.StartUploadAsync(db, PbcSeed.Actor(h.A.Client, "ClientUser"),
        new StartPbcUploadRequest(h.RequestA, "late.csv", "text/csv", 10, new string('a', 64)));
      Assert.False(start.Succeeded);
    }

    // Revoked administrator (stale session epoch): cannot provision.
    await using (var db = h.Db())
    {
      // The fixture has a single administrator, so revoke directly (last-administrator protection is tested elsewhere).
      (await db.RoleGrants.SingleAsync(x => x.UserId == h.A.Admin.Id && x.Role == "Administrator")).RevokedAt = DateTimeOffset.UtcNow;
      (await db.Users.SingleAsync(x => x.Id == h.A.Admin.Id)).SessionEpoch++;
      await db.SaveChangesAsync();
    }
    await using (var db = h.Db())
    {
      var denied = await PbcRepositoryProvisioningService.ProvisionEngagementRepositoryAsync(db, h.Admin, h.Drive, h.B.EngagementId, DateTimeOffset.UtcNow);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    }
  }
}
