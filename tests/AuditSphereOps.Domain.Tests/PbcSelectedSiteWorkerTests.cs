using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

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

    // Publication registered the exact verified version as a scoped reference and snapshot in the same transaction.
    var registration = await db.PbcDocumentRegistrations.AsNoTracking().SingleAsync(x => x.PbcUploadIntentId == staged.UploadIntentId);
    var snapshot = await db.DocumentSnapshots.AsNoTracking().SingleAsync(x => x.Id == registration.DocumentSnapshotId);
    var reference = await db.DocumentReferences.AsNoTracking().SingleAsync(x => x.Id == registration.DocumentReferenceId);
    var binding = await db.RepositoryBindings.AsNoTracking().SingleAsync(x => x.EngagementId == h.A.EngagementId);
    Assert.Equal((operation.Id, operation.ResultIdentity), (registration.TransferOperationId, registration.ProviderIdentity));
    Assert.Equal(operation.ResultIdentity, PbcSelectedSiteIdentity.Format(snapshot.DriveId, snapshot.ItemId, snapshot.VersionId));
    Assert.Equal((staged.DeclaredSha256Hex, intent.DeclaredByteCount, reference.Id), (snapshot.Sha256Hex, snapshot.ByteCount, snapshot.DocumentReferenceId));
    Assert.Equal((binding.Id, h.A.ClientId, h.A.EngagementId), (reference.RepositoryBindingId, reference.ClientId, reference.EngagementId));
    Assert.Equal(RepositoryBindingDigest.Compute(binding), registration.BindingSha256Hex);
    foreach (var mutation in new FormattableString[]
    {
      $"UPDATE pbc_document_registrations SET provider_identity = 'rewritten' WHERE id = {registration.Id}",
      $"UPDATE document_snapshots SET version_id = '9.0' WHERE id = {snapshot.Id}",
      $"DELETE FROM document_references WHERE id = {reference.Id}"
    })
      Assert.Equal("55000", (await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(mutation))).SqlState);

    // Acceptance pins the same upload revision; registration never needs to rewrite accepted evidence.
    var reviewer = PbcSeed.Actor(h.A.Reviewer, "Reviewer");
    var requestRevision = (await db.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == h.RequestA)).Revision;
    var underReview = await PbcService.ChangeStateAsync(db, reviewer, new PbcStateChangeRequest(h.RequestA, PbcStates.UnderReview, requestRevision));
    Assert.True(underReview.Succeeded, underReview.ErrorCode);
    var accepted = await PbcService.ChangeStateAsync(db, reviewer, new PbcStateChangeRequest(h.RequestA, PbcStates.Accepted, requestRevision + 1,
      ReviewedUploadIntentId: intent.Id, ReviewedUploadRevision: intent.Revision, ReviewedContentSha256Hex: staged.DeclaredSha256Hex));
    Assert.True(accepted.Succeeded, accepted.ErrorCode);
    var receipt = await db.PbcAcceptanceReceipts.AsNoTracking().SingleAsync(x => x.PbcRequestId == h.RequestA);
    Assert.Equal((intent.Revision, registration.ProviderIdentity), (receipt.UploadIntentRevision, receipt.ProviderIdentity));
  }

  [Fact]
  public async Task Publication_WithUnsupportedOrOtherDriveIdentity_FailsClosedWithoutRegistration()
  {
    var (h, _) = await CreateAsync();
    await using var _h = h;
    await h.ProvisionAsync(h.A);
    var staged = await h.UploadAsync(h.A, h.RequestA, Document("conflicting identity"));
    var operation = await h.OperationAsync(staged.UploadIntentId);
    foreach (var (identity, code) in new[]
    {
      ("sim://pbc/" + staged.UploadIntentId, "provider-identity-unsupported"),
      ($"graph-drive-item:b!other-drive:{Guid.NewGuid():N}:1.0", "provider-receipt-conflict")
    })
    {
      await using var db = h.Db();
      var blocked = await Assert.ThrowsAsync<OperationBlockedException>(() =>
        h.Handler.PublishAsync(db, operation, new OperationResult(identity, staged.DeclaredSha256Hex), default));
      Assert.Equal(code, blocked.Code);
    }
    await using var verify = h.Db();
    Assert.False(await verify.PbcDocumentRegistrations.AnyAsync());
    Assert.Equal(PbcUploadStates.Staged, (await verify.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == staged.UploadIntentId)).State);
  }

  [Fact]
  public async Task RegistrationConstraints_RejectReplayCrossScopeAndUnverifiedRows()
  {
    var (h, _) = await CreateAsync();
    await using var _h = h;
    await h.ProvisionAsync(h.A);
    await h.ProvisionAsync(h.B);
    var stagedB = await h.UploadAsync(h.B, h.RequestB, Document("client B registered"));
    Assert.True(await h.Worker.ProcessNextAsync());
    var stagedA = await h.UploadAsync(h.A, h.RequestA, Document("client A still staged"));
    var operationA = await h.OperationAsync(stagedA.UploadIntentId);

    await using var db = h.Db();
    var b = await db.PbcDocumentRegistrations.AsNoTracking().SingleAsync(x => x.PbcUploadIntentId == stagedB.UploadIntentId);
    var bindingA = await db.RepositoryBindings.AsNoTracking().SingleAsync(x => x.EngagementId == h.A.EngagementId);
    async Task<string> InsertAsync(Guid clientId, Guid engagementId, Guid intentId, Guid operationId, Guid referenceId, Guid snapshotId, string identity) =>
      (await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO pbc_document_registrations (id, firm_id, client_id, engagement_id, pbc_upload_intent_id, transfer_operation_id,
          document_reference_id, document_snapshot_id, provider_identity, binding_sha256_hex, registered_at)
        VALUES ({Guid.NewGuid()}, {h.A.FirmId}, {clientId}, {engagementId}, {intentId}, {operationId},
          {referenceId}, {snapshotId}, {identity}, {b.BindingSha256Hex}, {DateTimeOffset.UtcNow})
        """))).SqlState;

    // Replay of the same upload registration yields no second row.
    Assert.Equal("23505", await InsertAsync(b.ClientId, b.EngagementId, b.PbcUploadIntentId, b.TransferOperationId,
      b.DocumentReferenceId, b.DocumentSnapshotId, b.ProviderIdentity));

    var now = DateTimeOffset.UtcNow;
    var reference = new DocumentReference
    {
      Id = Guid.NewGuid(), FirmId = h.A.FirmId, ClientId = h.A.ClientId, EngagementId = h.A.EngagementId,
      RepositoryBindingId = bindingA.Id, DriveId = bindingA.DriveId, ItemId = "unverified-item", Path = "unverified.csv",
      Purpose = "Evidence", CreatedAt = now
    };
    var snapshot = new DocumentSnapshot
    {
      Id = Guid.NewGuid(), FirmId = h.A.FirmId, ClientId = h.A.ClientId, EngagementId = h.A.EngagementId,
      DocumentReferenceId = reference.Id, DriveId = bindingA.DriveId, ItemId = reference.ItemId, VersionId = "1.0",
      Sha256Hex = stagedA.DeclaredSha256Hex, ByteCount = stagedA.ByteCount, CapturedBy = "test", CapturedAt = now
    };
    db.DocumentReferences.Add(reference);
    db.DocumentSnapshots.Add(snapshot);
    await db.SaveChangesAsync();
    var identity = PbcSelectedSiteIdentity.Format(snapshot.DriveId, snapshot.ItemId, snapshot.VersionId);
    // Client B's scope cannot claim client A's upload or document rows.
    Assert.Equal("23503", await InsertAsync(h.B.ClientId, h.B.EngagementId, stagedA.UploadIntentId, operationA.Id,
      reference.Id, snapshot.Id, identity));
    // A same-scope snapshot cannot register an upload whose provider transfer has not completed.
    Assert.Equal("23514", await InsertAsync(h.A.ClientId, h.A.EngagementId, stagedA.UploadIntentId, operationA.Id,
      reference.Id, snapshot.Id, identity));
  }

  [Fact]
  public async Task RegistrationMigration_DownRefusesRetainedEvidence_AndReappliesWhenEmpty()
  {
    const string previous = "20261025050756_BudgetReceiptFirmScope";
    var (h, _) = await CreateAsync();
    await using var _h = h;
    await using (var empty = h.Db())
    {
      // Destructive down/up rehearsal is confined to this disposable schema.
      var migrator = empty.GetService<IMigrator>();
      await migrator.MigrateAsync(previous);
      await migrator.MigrateAsync();
    }

    await h.ProvisionAsync(h.A);
    var staged = await h.UploadAsync(h.A, h.RequestA, Document("retained registration"));
    Assert.True(await h.Worker.ProcessNextAsync());
    await using var db = h.Db();
    var refused = await Assert.ThrowsAnyAsync<Exception>(() => db.GetService<IMigrator>().MigrateAsync(previous));
    Assert.Equal("55000", Assert.IsType<Npgsql.PostgresException>(refused.InnerException ?? refused).SqlState);
    Assert.Equal(1, await db.PbcDocumentRegistrations.CountAsync(x => x.PbcUploadIntentId == staged.UploadIntentId));
  }

  [Fact]
  public async Task RemoteRead_ServesOnlyThePinnedVerifiedVersion_AndRevertsToTheRetainedLocalCopy()
  {
    var (h, graph) = await CreateAsync();
    await using var _h = h;
    await h.ProvisionAsync(h.A);
    await h.ProvisionAsync(h.B);
    var original = Document("remote primary");
    var staged = await h.UploadAsync(h.A, h.RequestA, original);
    Assert.True(await h.Worker.ProcessNextAsync());
    var reviewer = PbcSeed.Actor(h.A.Reviewer, "Reviewer");
    await using var db = h.Db();
    var registration = await db.PbcDocumentRegistrations.AsNoTracking().SingleAsync(x => x.PbcUploadIntentId == staged.UploadIntentId);

    // Before activation the retained staged chunks are read exactly as before.
    var local = await PbcService.PrepareDownloadAsync(db, reviewer, staged.UploadIntentId);
    Assert.True(local.Succeeded, local.ErrorCode);
    Assert.Null(local.Value!.Remote);
    Assert.NotEmpty(local.Value.ChunkPaths);

    var activate = new PbcReadDispositionRequest(staged.UploadIntentId, registration.Id, 0, "Pilot remote read");
    Assert.Equal(ErrorCodes.ScopeDenied, (await PbcService.ActivateRemoteReadAsync(db, reviewer, activate)).ErrorCode);
    var active = await PbcService.ActivateRemoteReadAsync(db, h.Admin, activate);
    Assert.True(active.Succeeded, active.ErrorCode);
    Assert.Equal((PbcReadSources.SelectedSite, PbcLocalCopyStates.Retained, 1L), (active.Value!.ReadSource, active.Value.LocalCopy, active.Value.Revision));
    var replay = await PbcService.ActivateRemoteReadAsync(db, h.Admin, activate with { ExpectedRevision = 1 });
    Assert.Equal(active.Value, replay.Value);
    Assert.Equal(ErrorCodes.GenerationStale, (await PbcService.ActivateRemoteReadAsync(db, h.Admin, activate)).ErrorCode);

    var remote = await PbcService.PrepareDownloadAsync(db, reviewer, staged.UploadIntentId);
    Assert.True(remote.Succeeded, remote.ErrorCode);
    var source = Assert.IsType<PbcRemoteSource>(remote.Value!.Remote);
    Assert.Empty(remote.Value.ChunkPaths);
    Assert.Equal((registration.Id, staged.DeclaredSha256Hex, (long)original.Length), (source.RegistrationId, source.Sha256Hex, source.ByteCount));
    // The staff-only audience is unchanged: neither this client's portal user nor another client's can read it.
    foreach (var denied in new[] { PbcSeed.Actor(h.A.Client, "ClientUser"), PbcSeed.Actor(h.B.Client, "ClientUser") })
      Assert.Equal(ErrorCodes.ScopeDenied, (await PbcService.PrepareDownloadAsync(db, denied, staged.UploadIntentId)).ErrorCode);

    var scratch = Path.Combine(Path.GetTempPath(), "AuditSphereOps.Tests", $"pbc-read-{Guid.NewGuid():N}");
    try
    {
      var reader = new SelectedSitePbcContentReader(new PbcRepositoryBindingResolver(h.Factory), h.Drive, scratch);
      async Task<byte[]> ReadAsync(PbcRemoteSource s)
      {
        var read = await reader.ReadVerifiedAsync(s, CancellationToken.None);
        Assert.True(read.Succeeded, read.ErrorCode);
        await using var content = read.Value!;
        using var copy = new MemoryStream();
        await content.Content.CopyToAsync(copy);
        return copy.ToArray();
      }
      Assert.Equal(original, await ReadAsync(source));
      // A later SharePoint edit adds a version; the pinned version is still the one served.
      graph.Tamper(source.ItemId, Document("edited in SharePoint"));
      Assert.Equal(original, await ReadAsync(source));
      // Bytes that do not match the registered hash are never released.
      var mismatch = await reader.ReadVerifiedAsync(source with { Sha256Hex = new string('0', 64) }, CancellationToken.None);
      Assert.Equal("pbc.source-integrity", mismatch.ErrorCode);
      Assert.Null(mismatch.Value);
      Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
    }
    finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }

    // Before any cleanup the upload can return to its retained local copy.
    var reverted = await PbcService.RevertToLocalReadAsync(db, h.Admin, activate with { ExpectedRevision = 1, Reason = "Pilot rollback" });
    Assert.True(reverted.Succeeded, reverted.ErrorCode);
    Assert.Equal((PbcReadSources.LocalStaged, 2L), (reverted.Value!.ReadSource, reverted.Value.Revision));
    var rolledBack = await PbcService.PrepareDownloadAsync(db, reviewer, staged.UploadIntentId);
    Assert.Equal(local.Value.ChunkPaths, rolledBack.Value!.ChunkPaths);
    Assert.Equal(2, await db.PbcStorageEvents.CountAsync());
    Assert.Equal("55000", (await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
      db.Database.ExecuteSqlRawAsync("UPDATE pbc_storage_events SET reason = 'rewritten'"))).SqlState);
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
    Assert.Equal(1, await db.PbcDocumentRegistrations.CountAsync(x => x.PbcUploadIntentId == staged.UploadIntentId));
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
