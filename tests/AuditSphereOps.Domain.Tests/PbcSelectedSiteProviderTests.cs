using System.Security.Cryptography;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>P2: selected-site provisioning, binding verification and the live PBC sink against a fake Graph drive.</summary>
[Trait("Profile", "Database")]
public sealed class PbcSelectedSiteProviderTests
{
  private sealed record Harness(PbcSeed.TransferHarness Transfer, FakeGraphDrive Graph, GraphSelectedSiteDrive Drive,
    ActorContext Admin, IAuditSphereDbContextFactory Factory, PgTestSchema Pg) : IAsyncDisposable
  {
    public PbcTransferScope Scope => new(Transfer.Fixture.FirmId, Transfer.Fixture.ClientId, Transfer.Fixture.EngagementId, Transfer.Staged.UploadIntentId);
    public AuditSphereDbContext Db() => new(Pg.Options);
    public ValueTask DisposeAsync() => Pg.DisposeAsync();
  }

  private static async Task<Harness> CreateAsync(bool approveEngagementTemplate = true, bool accept = true)
  {
    var pg = await PgTestSchema.CreateAsync();
    var transfer = await PbcSeed.CreateTransferHarnessAsync(pg);
    var fixture = transfer.Fixture;
    var now = DateTimeOffset.UtcNow;
    var connectionId = Guid.NewGuid();
    var clientTemplateId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var acceptanceId = Guid.NewGuid();
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = acceptanceId, FirmId = fixture.FirmId, PracticeClientId = fixture.ClientId, Decision = accept ? "Accepted" : "Deferred",
        ServiceRoute = "FinancialStatementAudit", Rationale = "Synthetic acceptance for P2 provider test.",
        EvaluationTemplateVersion = "synthetic-v1", EvaluationSnapshotDigest = new string('a', 64),
        DecidedByUserId = fixture.Admin.Id, DecidedAt = now
      });
      db.ClientWorkspaces.Add(new ClientWorkspace
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, PracticeClientId = fixture.ClientId, AcceptanceDecisionId = acceptanceId,
        LogicalKey = "client-workspace/" + fixture.ClientId, State = ClientWorkspaceStates.WaitingForIntegration, CreatedAt = now
      });
      db.Microsoft365ConnectionRevisions.Add(new Microsoft365ConnectionRevision
      {
        Id = connectionId, FirmId = fixture.FirmId, TenantId = Guid.Empty.ToString("D").Replace('0', '1'),
        LoginClientIdReference = "slot:login", RuntimeCredentialReference = "slot:selected-site",
        State = Microsoft365RevisionStates.Active, ConsentState = "VERIFIED", CreatedAt = now
      });
      db.FolderTemplateVersions.Add(Template(clientTemplateId, fixture.FirmId, FolderTemplatePurposes.ClientWorkspace, 1, now));
      if (approveEngagementTemplate)
        db.FolderTemplateVersions.Add(Template(Guid.NewGuid(), fixture.FirmId, FolderTemplatePurposes.EngagementWorkspace, 1, now));
      db.FirmWorkspaceConfigurations.Add(new FirmWorkspaceConfiguration
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, ConnectionRevisionId = connectionId, FolderTemplateVersionId = clientTemplateId,
        TenantId = Guid.Empty.ToString("D").Replace('0', '1'), SiteId = "site-a", DriveId = FakeGraphDrive.DriveId,
        RootFolderId = FakeGraphDrive.RootId, DisplayUrl = "https://fake.sharepoint.com/sites/audit",
        AccessProfile = Microsoft365AccessProfiles.AppMediated, CreatedAt = now
      });
      await db.SaveChangesAsync();
    }
    var graph = new FakeGraphDrive();
    var drive = new GraphSelectedSiteDrive(new HttpClient(graph), new FakeSelectedSiteTokens(), new GraphPreauthenticatedTransport(graph), configured: true);
    return new(transfer, graph, drive, PbcSeed.Actor(fixture.Admin, "Administrator"),
      new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options)), pg);
  }

  private static FolderTemplateVersion Template(Guid id, Guid firmId, string purpose, long version, DateTimeOffset now) => new()
  {
    Id = id, FirmId = firmId, Purpose = purpose, Version = version, ManifestJson = Microsoft365ConfigurationService.DefaultManifest(purpose),
    ManifestDigest = new string('b', 64), CreatedByUserId = Guid.Empty, CreatedAt = now, ApprovedAt = now
  };

  private static async Task<PbcTransferPlan> PlanAsync(Harness h)
  {
    await using var db = h.Db();
    var intent = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == h.Scope.UploadIntentId);
    var chunks = await db.PbcUploadChunks.AsNoTracking().Where(x => x.PbcUploadIntentId == intent.Id).OrderBy(x => x.ChunkIndex).ToListAsync();
    return new(intent.FirmId, intent.ClientId, intent.EngagementId, intent.Id, intent.DeclaredByteCount, intent.DeclaredSha256Hex.ToLowerInvariant(),
      chunks.Select(x => new PbcTransferChunk(x.ChunkIndex, x.Offset, x.ByteCount, x.Sha256Hex, x.StagedPath!)).ToArray());
  }

  private static async Task ProvisionAsync(Harness h)
  {
    await using var db = h.Db();
    var client = await PbcRepositoryProvisioningService.ProvisionClientWorkspaceAsync(db, h.Admin, h.Drive, h.Scope.ClientId, DateTimeOffset.UtcNow);
    Assert.True(client.Succeeded, client.Message);
    var engagement = await PbcRepositoryProvisioningService.ProvisionEngagementRepositoryAsync(db, h.Admin, h.Drive, h.Scope.EngagementId, DateTimeOffset.UtcNow);
    Assert.True(engagement.Succeeded, engagement.Message);
    Assert.Equal("VERIFIED", engagement.Value!.State);
  }

  [Fact]
  public async Task ProvisionedRepository_ResolvesAndTransfersWithExactVersionReadback()
  {
    await using var h = await CreateAsync();
    await ProvisionAsync(h);

    var clientFolder = h.Graph.Items.Single(x => x.ParentId == FakeGraphDrive.RootId);
    Assert.EndsWith($"({h.Scope.ClientId.ToString("N")[..8]})", clientFolder.Name);
    var engagements = h.Graph.Items.Single(x => x.ParentId == clientFolder.Id && x.Name == "Engagements");
    var engagementFolder = h.Graph.Items.Single(x => x.ParentId == engagements.Id);
    var pbcFolder = h.Graph.Items.Single(x => x.ParentId == engagementFolder.Id && x.Name == "03_PBC_Data_Intake");
    Assert.DoesNotContain(h.Graph.Items, x => x.Name.StartsWith(".auditsphere-probe", StringComparison.Ordinal)); // probe cleaned up

    var target = await new PbcRepositoryBindingResolver(h.Factory).ResolveAsync(h.Scope, CancellationToken.None);
    Assert.Equal(pbcFolder.Id, target.RootFolderId);

    var sink = new GraphPbcProviderSink(new PbcRepositoryBindingResolver(h.Factory), h.Drive, h.Factory);
    var plan = await PlanAsync(h);
    var receipt = await sink.UploadAsync(plan, CancellationToken.None);
    Assert.Equal(plan.FinalSha256Hex, receipt.ContentSha256Hex);
    Assert.Equal(plan.DeclaredByteCount, receipt.ByteCount);
    Assert.StartsWith($"graph-drive-item:{FakeGraphDrive.DriveId}:", receipt.Identity);
    Assert.EndsWith(":1.0", receipt.Identity);
    var stored = h.Graph.Items.Single(x => x.ParentId == pbcFolder.Id && !x.Folder);
    Assert.StartsWith(h.Scope.UploadIntentId.ToString("N") + "-", stored.Name);
    Assert.Equal(0, h.Graph.BearerSentToSharePoint);

    Assert.Equal(receipt, await sink.VerifyAsync(h.Scope, receipt.Identity, CancellationToken.None));
    Assert.Equal(receipt, await sink.ProbeAsync(h.Scope, CancellationToken.None));

    // A retried upload finds the existing file: surfaced as an unknown outcome, never an overwrite.
    await Assert.ThrowsAsync<IOException>(() => sink.UploadAsync(plan, CancellationToken.None));
    Assert.Single(h.Graph.Items, x => x.ParentId == pbcFolder.Id && !x.Folder);

    // A later modification is detected: the recorded exact version still verifies, the current does not match.
    h.Graph.Tamper(stored.Id, "tampered"u8.ToArray());
    Assert.Equal(receipt, await sink.VerifyAsync(h.Scope, receipt.Identity, CancellationToken.None));
    var probed = await sink.ProbeAsync(h.Scope, CancellationToken.None);
    Assert.NotEqual(plan.FinalSha256Hex, probed!.ContentSha256Hex);
  }

  [Fact]
  public async Task Provisioning_IsIdempotent_AndDoesNotDuplicateFolders()
  {
    await using var h = await CreateAsync();
    await ProvisionAsync(h);
    var creates = h.Graph.FolderCreates;
    await ProvisionAsync(h);
    Assert.Equal(creates, h.Graph.FolderCreates);
    await using var db = h.Db();
    Assert.Single(await db.RepositoryBindings.ToListAsync());
    Assert.Single(await db.IntegrationCapabilities.ToListAsync());
    Assert.Contains(await db.Microsoft365AdministrationEvents.ToListAsync(), x => x.Operation == "PBC_REPOSITORY_VERIFIED");
  }

  [Fact]
  public async Task FailedReadback_RecordsFailedCapability_AndResolverStaysBlocked()
  {
    await using var h = await CreateAsync();
    h.Graph.FailProbeReadback = true;
    await using var db = h.Db();
    Assert.True((await PbcRepositoryProvisioningService.ProvisionClientWorkspaceAsync(db, h.Admin, h.Drive, h.Scope.ClientId, DateTimeOffset.UtcNow)).Succeeded);
    var engagement = await PbcRepositoryProvisioningService.ProvisionEngagementRepositoryAsync(db, h.Admin, h.Drive, h.Scope.EngagementId, DateTimeOffset.UtcNow);
    Assert.Equal("FAILED", engagement.Value!.State);
    Assert.Equal("probe-readback-mismatch", engagement.Value.DiagnosticCode);
    Assert.Equal("none", (await db.RepositoryBindings.SingleAsync()).ObservedAccess);
    await Assert.ThrowsAsync<OperationBlockedException>(() =>
      new PbcRepositoryBindingResolver(h.Factory).ResolveAsync(h.Scope, CancellationToken.None));
  }

  [Fact]
  public async Task ProvisioningFailsClosedWithoutPrerequisitesOrAdministrator()
  {
    await using (var notAccepted = await CreateAsync(accept: false))
    await using (var db = notAccepted.Db())
      Assert.Equal(ErrorCodes.GateBlocked, (await PbcRepositoryProvisioningService.ProvisionClientWorkspaceAsync(db, notAccepted.Admin,
        notAccepted.Drive, notAccepted.Scope.ClientId, DateTimeOffset.UtcNow)).ErrorCode);

    await using var h = await CreateAsync(approveEngagementTemplate: false);
    await using (var db = h.Db())
    {
      var staff = PbcSeed.Actor(h.Transfer.Fixture.Staff, "Staff");
      Assert.Equal(ErrorCodes.ScopeDenied, (await PbcRepositoryProvisioningService.ProvisionClientWorkspaceAsync(db, staff, h.Drive,
        h.Scope.ClientId, DateTimeOffset.UtcNow)).ErrorCode);
      Assert.True((await PbcRepositoryProvisioningService.ProvisionClientWorkspaceAsync(db, h.Admin, h.Drive, h.Scope.ClientId, DateTimeOffset.UtcNow)).Succeeded);
      var noTemplate = await PbcRepositoryProvisioningService.ProvisionEngagementRepositoryAsync(db, h.Admin, h.Drive, h.Scope.EngagementId, DateTimeOffset.UtcNow);
      Assert.Equal(ErrorCodes.GateBlocked, noTemplate.ErrorCode);
      var unconfigured = new GraphSelectedSiteDrive(new HttpClient(h.Graph), new FakeSelectedSiteTokens(), new GraphPreauthenticatedTransport(h.Graph), configured: false);
      Assert.Equal(ErrorCodes.GateBlocked, (await PbcRepositoryProvisioningService.ProvisionEngagementRepositoryAsync(db, h.Admin, unconfigured,
        h.Scope.EngagementId, DateTimeOffset.UtcNow)).ErrorCode);
      Assert.Empty(await db.RepositoryBindings.ToListAsync());
    }
  }

  [Fact]
  public async Task LargeUpload_UsesAlignedFragments()
  {
    await using var h = await CreateAsync();
    await ProvisionAsync(h);
    var target = await new PbcRepositoryBindingResolver(h.Factory).ResolveAsync(h.Scope, CancellationToken.None);
    var directory = Path.Combine(Path.GetTempPath(), "as-p2-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
      // Odd staged chunk sizes across a 21 MiB document must be re-cut into 10 MiB (320 KiB-aligned) fragments.
      var sizes = new[] { 7 * 1024 * 1024 + 13, 9 * 1024 * 1024 + 7, 5 * 1024 * 1024 + 1 };
      var chunks = new List<PbcTransferChunk>();
      using var all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
      long offset = 0;
      for (var i = 0; i < sizes.Length; i++)
      {
        var bytes = RandomNumberGenerator.GetBytes(sizes[i]);
        var path = Path.Combine(directory, $"{i}.part");
        await File.WriteAllBytesAsync(path, bytes);
        all.AppendData(bytes);
        chunks.Add(new(i, offset, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), path));
        offset += bytes.Length;
      }
      var plan = new PbcTransferPlan(h.Scope.FirmId, h.Scope.ClientId, h.Scope.EngagementId, h.Scope.UploadIntentId, offset,
        Convert.ToHexString(all.GetHashAndReset()).ToLowerInvariant(), chunks);
      var location = new SelectedSiteLocation(target.TenantId, target.SiteId, target.DriveId, target.RuntimeCredentialReference);
      var item = await h.Drive.UploadAsync(location, target.RootFolderId, "large.bin", plan, CancellationToken.None);
      Assert.Equal(offset, item.Size);
      Assert.Equal(3, h.Graph.FragmentRanges.Count);
      var (sha, size) = await h.Drive.ContentDigestAsync(location, item.ItemId, null, offset, CancellationToken.None);
      Assert.Equal((plan.FinalSha256Hex, offset), (sha, size));
    }
    finally { Directory.Delete(directory, recursive: true); }
  }

  [Fact]
  public void StoredName_IsDeterministicAndSharePointSafe()
  {
    var id = Guid.NewGuid();
    Assert.Equal($"{id:N}-trial_balance_2026_.xlsx", GraphPbcProviderSink.StoredName(id, "../trial:balance|2026?.xlsx"));
    Assert.Equal(GraphPbcProviderSink.StoredName(id, "a.pdf"), GraphPbcProviderSink.StoredName(id, "a.pdf"));
    Assert.Equal("Acme Holdings Ltd (12345678)", PbcRepositoryProvisioningService.FolderName("Acme: Holdings* Ltd.", Guid.Parse("12345678-0000-0000-0000-000000000000")));
  }
}
