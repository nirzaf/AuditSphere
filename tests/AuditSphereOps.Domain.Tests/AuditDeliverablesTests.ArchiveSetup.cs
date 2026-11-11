using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AuditDeliverablesTests
{
  private sealed record SupportedArchiveSource(Guid SnapshotId, Guid ClientSiteId, Guid ConnectionId,
    Guid DraftId, long DraftRevision, Guid VerificationId, string TenantId, string SiteUrl, string SiteId,
    string DriveId, string RootId);

  private static async Task<SupportedArchiveSource> PrepareSupportedArchiveSourceAsync(PgTestSchema pg, World w, byte[] bytes)
  {
    var admin = w.A("administrator", "Administrator");
    var user = w.U["administrator"];
    var factory = new ArchiveCopyTestDbContextFactory(pg.Options);
    const string siteUrl = "https://example.sharepoint.com/sites/shared-archive-copy-test";
    var siteId = $"example.sharepoint.com,{Guid.NewGuid():D},{Guid.NewGuid():D}";
    const string driveId = "shared-archive-drive", rootId = "shared-library-root";
    Guid draftId, connectionId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var session = await db.Microsoft365SetupSessions.AsNoTracking().SingleAsync(x => x.FirmId == w.FirmId);
      var claimed = await Microsoft365OnboardingService.ClaimAsync(db, w.FirmId, session.InstallationId,
        "", session.BootstrapProofHash, DateTimeOffset.UtcNow, authenticatedTenantId: user.TenantId,
        authenticatedObjectId: user.Subject, authenticatedActor: admin);
      Assert.True(claimed.Succeeded, claimed.Message);
      var prior = await db.Microsoft365SetupDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.SetupSessionId == session.Id);
      var saved = await Microsoft365OnboardingService.SaveDraftAsync(db,
        new(claimed.Value!.SessionId, claimed.Value.Capability, prior?.Revision ?? 1, user.TenantId, "Synthetic tenant",
          siteUrl, siteId, driveId, rootId, Microsoft365AccessProfiles.AppMediated, "NOT_CONFIGURED", "NOT_CONFIGURED"), DateTimeOffset.UtcNow);
      Assert.True(saved.Succeeded, saved.Message);
      draftId = saved.Value;
      var draft = await db.Microsoft365SetupDrafts.AsNoTracking().SingleAsync(x => x.Id == draftId);
      var prepared = await Microsoft365ConfigurationService.PrepareConnectionRevisionAsync(db, admin,
        new(draftId, draft.Revision, "slot:test-login", "slot:test-runtime"), DateTimeOffset.UtcNow);
      Assert.True(prepared.Succeeded, prepared.Message);
      connectionId = prepared.Value;
      var consent = await TenantConsentService.BeginAsync(db, admin, draftId, user.TenantId, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow);
      Assert.True(consent.Succeeded, consent.Message);
      var callback = await TenantConsentService.CompleteCallbackAsync(db, admin, consent.Value!.State, user.TenantId, true, false, DateTimeOffset.UtcNow);
      Assert.True(callback.Succeeded, callback.Message);
      var challenge = await TenantConsentService.BeginIdentityVerificationAsync(db, admin, callback.Value!.AttemptId, DateTimeOffset.UtcNow);
      Assert.True(challenge.Succeeded, challenge.Message);
      var verified = await TenantConsentService.CompleteIdentityVerificationAsync(db, admin,
        new ArchiveConsentVerifier(user.TenantId, user.Subject, challenge.Value!.Nonce, challenge.Value.State),
        challenge.Value.State, "synthetic-one-use-code", false, DateTimeOffset.UtcNow);
      Assert.True(verified.Succeeded, verified.Message);
    }
    long revision;
    await using (var db = new AuditSphereDbContext(pg.Options))
      revision = await db.Microsoft365SetupDrafts.Where(x => x.Id == draftId).Select(x => x.Revision).SingleAsync();
    await new SelectedSiteBoundaryVerificationService(factory,
      new ArchiveSelectedSiteProbe(w.FirmId, new(draftId, revision, connectionId, user.TenantId, siteId, driveId, rootId, siteUrl)),
      "https://example.sharepoint.com/sites/synthetic-negative-control").VerifyDraftAsync(admin, draftId, default, revision);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Guid clientTemplateId = Guid.Empty;
      foreach (var purpose in new[] { FolderTemplatePurposes.ClientWorkspace, FolderTemplatePurposes.EngagementWorkspace })
      {
        var template = await Microsoft365ConfigurationService.SaveFolderTemplateAsync(db, admin,
          new(purpose, Microsoft365ConfigurationService.DefaultManifest(purpose)), DateTimeOffset.UtcNow);
        Assert.True(template.Succeeded, template.Message);
        Assert.True((await Microsoft365ConfigurationService.ApproveFolderTemplateAsync(db, admin, template.Value!.Id, DateTimeOffset.UtcNow)).Succeeded);
        if (purpose == FolderTemplatePurposes.ClientWorkspace) clientTemplateId = template.Value.Id;
      }
      revision = await db.Microsoft365SetupDrafts.Where(x => x.Id == draftId).Select(x => x.Revision).SingleAsync();
      var activated = await Microsoft365ConfigurationService.ActivateConnectionAsync(db, admin,
        new(draftId, connectionId, clientTemplateId, revision, siteId, driveId, rootId, siteUrl, Microsoft365AccessProfiles.AppMediated), DateTimeOffset.UtcNow);
      Assert.True(activated.Succeeded, activated.Message);
    }
    var store = new PostgresOperationStore(factory);
    var siteHandler = new ClientSharePointSiteHandler(factory, new ArchiveClientSiteProvider());
    // Shipping LIVE descriptor eligibility is confined to this fake-only isolated registry.
    // No network provider, mail dispatcher, process setting or real credential is used.
    var siteOptions = new WorkerOptions(w.FirmId, "Acceptance", ExternalEffectsEnabled: true, Group: ClientSharePointSiteHandler.Group);
    DateTimeOffset cutoff;
    await using (var db = new AuditSphereDbContext(pg.Options))
      cutoff = (await db.PracticeClients.Where(x => x.Id == w.ClientId).Select(x => x.CreatedAt).SingleAsync()).AddSeconds(-1);
    var discovery = new ClientSharePointSiteDiscovery(factory, store, siteHandler, siteOptions,
      new(true, user.TenantId, "example.sharepoint.com", admin.UserId, cutoff));
    Assert.Equal(1, await discovery.EnqueuePendingAsync(default));
    var siteDispatcher = new OperationDispatcher(store, new DurableOperationRegistry([siteHandler], siteOptions), siteOptions);
    Assert.True(await siteDispatcher.ProcessNextAsync());
    Guid clientSiteId;
    string sourceDrive;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var site = await db.ClientSharePointSites.AsNoTracking().SingleAsync(x => x.ClientId == w.ClientId);
      Assert.Equal("READY", site.State);
      clientSiteId = site.Id;
      var provider = new ArchiveWorkspaceProvisioner(new(user.TenantId, "target-site", "working-drive", "slot:test-runtime"), "working-root");
      var rollout = new ClientSiteRolloutPolicy(true, cutoff);
      var workspace = await PbcRepositoryProvisioningService.ProvisionClientWorkspaceAsync(db, admin, provider, w.ClientId,
        DateTimeOffset.UtcNow, rolloutPolicy: rollout);
      Assert.True(workspace.Succeeded, workspace.Message);
      var repository = await PbcRepositoryProvisioningService.ProvisionEngagementRepositoryAsync(db, admin, provider, w.EngagementId,
        DateTimeOffset.UtcNow, rolloutPolicy: rollout);
      Assert.True(repository.Succeeded, repository.Message);
      Assert.True(provider.SuccessfulProbes > 0);
      Assert.Equal(0, provider.ProbeFilesRemaining);
      sourceDrive = (await db.RepositoryBindings.AsNoTracking().SingleAsync(x => x.Id == repository.Value!.WorkspaceOrBindingId)).DriveId;
    }
    var fixture = new PbcSeed.Fixture(w.FirmId, w.ClientId, w.EngagementId, w.U["senior"], w.U["manager"], w.U["client"], user);
    var request = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture, w.A("senior", "Senior"), w.A("client", "ClientUser"));
    var staged = await PbcSeed.StageUploadAsync(pg, fixture, w.A("client", "ClientUser"), request, bytes, "source.txt", "text/plain");
    try
    {
      var sink = new ArchiveSourceSink(new(w.FirmId, w.ClientId, w.EngagementId, staged.UploadIntentId), sourceDrive, bytes);
      var transfer = new PbcDocumentTransferHandler(factory, sink);
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var queued = await PbcService.CompleteUploadAsync(db, w.A("senior", "Senior"), new(staged.UploadIntentId, staged.DeclaredSha256Hex), store, transfer);
        Assert.True(queued.Succeeded, queued.Message);
      }
      var transferOptions = new WorkerOptions(w.FirmId, "Test", AllowSimulationAdapters: true);
      Assert.True(await new OperationDispatcher(store, new DurableOperationRegistry([transfer], transferOptions), transferOptions).ProcessNextAsync());
      await using var proof = new AuditSphereDbContext(pg.Options);
      var snapshot = await proof.DocumentSnapshots.AsNoTracking().SingleAsync(x => x.EngagementId == w.EngagementId);
      Assert.Equal(("source-item", "source-version-7", Hashing.Sha256Hex(bytes), bytes.LongLength),
        (snapshot.ItemId, snapshot.VersionId, snapshot.Sha256Hex, snapshot.ByteCount));
      var verification = await proof.TenantCapabilityVerifications.AsNoTracking().SingleAsync(x => x.ConnectionRevisionId == connectionId && x.Capability == Microsoft365Capabilities.SelectedSite &&
        x.State == CapabilityVerificationStates.Verified && x.DiagnosticCode == "exact-resource-and-control-denial");
      var finalDraft = await proof.Microsoft365SetupDrafts.AsNoTracking().SingleAsync(x => x.Id == draftId);
      return new(snapshot.Id, clientSiteId, connectionId, draftId, finalDraft.Revision, verification.Id, user.TenantId, siteUrl, siteId, driveId, rootId);
    }
    finally { if (Directory.Exists(staged.StagingRoot)) Directory.Delete(staged.StagingRoot, recursive: true); }
  }

  private sealed record SupportedSecondaryArchive(Guid DraftId, long DraftRevision, Guid VerificationId, Guid ConnectionId,
    string SiteUrl, string SiteId, string DriveId, string RootId);

  private static async Task<SupportedSecondaryArchive> PrepareSupportedSecondaryArchiveAsync(PgTestSchema pg, World w)
  {
    var admin = w.A("administrator", "Administrator");
    var user = w.U["administrator"];
    const string siteUrl = "https://example.sharepoint.com/sites/secondary-archive-copy-test";
    var siteId = $"example.sharepoint.com,{Guid.NewGuid():D},{Guid.NewGuid():D}";
    const string driveId = "separate-secondary-archive-drive", rootId = "separate-secondary-archive-root";
    const string syntheticProof = "Synthetic isolated secondary archive installation proof";
    Guid draftId, connectionId;
    string workingBefore;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      workingBefore = JsonSerializer.Serialize(await db.FirmWorkspaceConfigurations.AsNoTracking()
        .Where(x => x.FirmId == w.FirmId).OrderBy(x => x.Id).ToArrayAsync());
      var claimed = await Microsoft365OnboardingService.ClaimAsync(db, w.FirmId,
        "synthetic-secondary-archive-" + Guid.NewGuid().ToString("N"), syntheticProof,
        Hashing.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(syntheticProof)), DateTimeOffset.UtcNow);
      Assert.True(claimed.Succeeded, claimed.Message);
      var bound = await Microsoft365OnboardingService.CompleteInitialAdministratorBootstrapAsync(db,
        new(claimed.Value!.SessionId, claimed.Value.Capability, user.TenantId, user.Subject, user.Email, user.DisplayName),
        user.TenantId, user.Subject, DateTimeOffset.UtcNow);
      Assert.True(bound.Succeeded, bound.Message);
      Assert.Equal(admin.UserId, bound.Value);
      var draft = await db.Microsoft365SetupDrafts.AsNoTracking().SingleAsync(x => x.SetupSessionId == claimed.Value.SessionId);
      var saved = await Microsoft365OnboardingService.SaveDraftAsync(db,
        new(claimed.Value.SessionId, claimed.Value.Capability, draft.Revision, user.TenantId, "Synthetic archive tenant",
          siteUrl, siteId, driveId, rootId, Microsoft365AccessProfiles.AppMediated, "NOT_CONFIGURED", "NOT_CONFIGURED"), DateTimeOffset.UtcNow);
      Assert.True(saved.Succeeded, saved.Message);
      draftId = saved.Value;
      var revision = await db.Microsoft365SetupDrafts.Where(x => x.Id == draftId).Select(x => x.Revision).SingleAsync();
      var prepared = await Microsoft365ConfigurationService.PrepareConnectionRevisionAsync(db, admin,
        new(draftId, revision, "slot:test-archive-login", "slot:test-archive-runtime"), DateTimeOffset.UtcNow);
      Assert.True(prepared.Succeeded, prepared.Message);
      connectionId = prepared.Value;
      var consent = await TenantConsentService.BeginAsync(db, admin, draftId, user.TenantId, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow);
      Assert.True(consent.Succeeded, consent.Message);
      var callback = await TenantConsentService.CompleteCallbackAsync(db, admin, consent.Value!.State, user.TenantId, true, false, DateTimeOffset.UtcNow);
      Assert.True(callback.Succeeded, callback.Message);
      var challenge = await TenantConsentService.BeginIdentityVerificationAsync(db, admin, callback.Value!.AttemptId, DateTimeOffset.UtcNow);
      Assert.True(challenge.Succeeded, challenge.Message);
      var verified = await TenantConsentService.CompleteIdentityVerificationAsync(db, admin,
        new ArchiveConsentVerifier(user.TenantId, user.Subject, challenge.Value!.Nonce, challenge.Value.State),
        challenge.Value.State, "synthetic-one-use-code", false, DateTimeOffset.UtcNow);
      Assert.True(verified.Succeeded, verified.Message);
    }
    long verifiedRevision;
    await using (var db = new AuditSphereDbContext(pg.Options))
      verifiedRevision = await db.Microsoft365SetupDrafts.Where(x => x.Id == draftId).Select(x => x.Revision).SingleAsync();
    await new SelectedSiteBoundaryVerificationService(new ArchiveCopyTestDbContextFactory(pg.Options),
      new ArchiveSelectedSiteProbe(w.FirmId, new(draftId, verifiedRevision, connectionId, user.TenantId, siteId, driveId, rootId, siteUrl)),
      "https://example.sharepoint.com/sites/synthetic-negative-control").VerifyDraftAsync(admin, draftId, default, verifiedRevision);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var draft = await db.Microsoft365SetupDrafts.AsNoTracking().SingleAsync(x => x.Id == draftId);
      var proof = await db.TenantCapabilityVerifications.AsNoTracking().SingleAsync(x =>
        x.ConnectionRevisionId == connectionId && x.Capability == Microsoft365Capabilities.SelectedSite &&
        x.State == CapabilityVerificationStates.Verified && x.DiagnosticCode == "exact-resource-and-control-denial");
      var activated = await Microsoft365ConfigurationService.ActivateArchiveResourceAsync(db, admin,
        new(draftId, draft.Revision, connectionId, proof.Id, user.TenantId, siteId, driveId, rootId, siteUrl,
          Microsoft365AccessProfiles.AppMediated), DateTimeOffset.UtcNow);
      Assert.True(activated.Succeeded, $"{activated.ErrorCode}: {activated.Message}");
      Assert.Equal(connectionId, activated.Value);
      Assert.Equal(workingBefore, JsonSerializer.Serialize(await db.FirmWorkspaceConfigurations.AsNoTracking()
        .Where(x => x.FirmId == w.FirmId).OrderBy(x => x.Id).ToArrayAsync()));
      Assert.False(await db.ClientWorkspaces.AnyAsync(x => x.FirmId == w.FirmId && x.DriveId == driveId));
      Assert.False(await db.RepositoryBindings.AnyAsync(x => x.FirmId == w.FirmId && x.DriveId == driveId));
      var active = await db.Microsoft365SetupDrafts.AsNoTracking().SingleAsync(x => x.Id == draftId);
      Assert.Equal(Microsoft365RevisionStates.Active, active.State);
      Assert.Equal(draft.Revision + 1, active.Revision);
      return new(draftId, active.Revision, proof.Id, connectionId, siteUrl, siteId, driveId, rootId);
    }
  }

  private static async Task<(Guid ClientId, Guid EngagementId)> CreateSupportedArchiveSiblingAsync(PgTestSchema pg, World w, ActorContext partner)
  {
    var admin = w.A("administrator", "Administrator");
    var relationship = await SupportedAuditCompletionWorldBuilder.ProvisionFirmRoleAsync(pg, (w.FirmId, admin), "RelationshipManager");
    var manager = await SupportedAuditCompletionWorldBuilder.ProvisionFirmRoleAsync(pg, (w.FirmId, admin), "Manager");
    var proposal = await SupportedAuditCompletionWorldBuilder.CreateAcceptedProposalAsync(pg, relationship.Actor, partner,
      manager.Actor, "archive-sibling", "2027-01-01", "2027-12-31", _ => Task.CompletedTask);
    await using var db = new AuditSphereDbContext(pg.Options);
    var converted = await PracticeCrmService.ConvertToClientDraftAsync(db, relationship.Actor,
      new(proposal, "Synthetic second archive-copy client", "Archive sibling"));
    Assert.True(converted.Succeeded, converted.Message);
    var state = await EngagementCreationWorkspace.StateAsync(db, partner, converted.Value);
    Assert.True(state.Succeeded, state.Message);
    var request = new EngagementCreationRequest(Guid.NewGuid(), state.Value!.ReviewBasis, new("Audit", "Audit", "2027-01-01", "2027-12-31"));
    var preview = await EngagementCreationWorkspace.PreviewAsync(db, partner, converted.Value, request);
    Assert.True(preview.Succeeded, preview.Message);
    var created = await EngagementCreationWorkspace.ExecuteAsync(db, partner, converted.Value,
      request with { Reviewed = true, ExpectedRequestHash = preview.Value!.RequestHash });
    Assert.True(created.Succeeded, created.Message);
    var staff = await RoleAdministrationService.EnsureUserAsync(db, admin,
      new(w.U["administrator"].TenantId, Guid.NewGuid().ToString("D"), $"archive-sibling-{Guid.NewGuid():N}@example.test", "Synthetic sibling staff", "Staff", "APPROVED_ROSTER"));
    Assert.True(staff.Succeeded, staff.Message);
    var assigned = await StaffingService.AssignAsync(db, partner, new(created.Value!.EngagementId, staff.Value, StaffingLevels.StaffAssociate));
    Assert.True(assigned.Succeeded, assigned.Message);
    return (converted.Value, created.Value.EngagementId);
  }

  private sealed class ArchiveConsentVerifier(string tenant, string subject, string nonce, string state) : IMicrosoftTenantConsentVerifier
  {
    public bool IsConfigured => true;
    public Task<IReadOnlyList<CapabilityProbeResult>> VerifyCapabilitiesAsync(string tenantId,
      IReadOnlyList<CapabilityProbe> capabilities, CancellationToken ct)
      => throw new NotSupportedException("Selected-site proof must use its dedicated boundary verifier.");
    private bool redeemed;
    public Uri BuildIdentityChallenge(string tenantId, string challengeState, string challengeNonce)
    {
      Assert.Equal(tenant, tenantId); Assert.Equal(state, challengeState); Assert.Equal(nonce, challengeNonce);
      return new("https://login.example.test/synthetic");
    }
    public Task<ConsentingAdministrator> RedeemIdentityAsync(string tenantId, string code, CancellationToken ct)
    {
      ct.ThrowIfCancellationRequested();
      Assert.False(redeemed); Assert.Equal(tenant, tenantId); Assert.Equal("synthetic-one-use-code", code);
      Assert.True(Guid.TryParse(subject, out _)); Assert.False(string.IsNullOrWhiteSpace(nonce));
      redeemed = true;
      return Task.FromResult(new ConsentingAdministrator(tenant, subject, nonce, false));
    }
  }
  private sealed class ArchiveSelectedSiteProbe(Guid expectedFirmId, SelectedSiteBoundaryObservation observation) : ISelectedSiteBoundaryProbe
  {
    public Task<SelectedSiteBoundaryObservation> ProbeAsync(Guid firmId, Guid draftId, string negativeControlSiteUrl, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); Assert.Equal(expectedFirmId, firmId); Assert.Equal(observation.DraftId, draftId); Assert.Equal("https://example.sharepoint.com/sites/synthetic-negative-control", negativeControlSiteUrl); return Task.FromResult(observation); }
  }
  private sealed class ArchiveClientSiteProvider : IClientSharePointSiteProvider
  {
    public async Task<ClientSiteReceipt> ReconcileAsync(ClientSiteRequest request, bool mayCreate, CancellationToken ct)
    {
      if (request.RecheckAuthorization is not null) await request.RecheckAuthorization(ct);
      return new(request.TenantId, request.Url, request.OwnershipMarker, "target-site", "working-drive", "working-root", "42",
        request.StaffObjectIds, "synthetic-client-site", ResidualAccessReferences: []);
    }
  }
  private sealed class ArchiveWorkspaceProvisioner(SelectedSiteLocation expected, string rootId) : ISelectedSiteWorkspaceProvisioner
  {
    private readonly HashSet<string> ownedFolders = [rootId];
    private readonly Dictionary<(string Parent, string Name), RemoteFolder> folders = [];
    private readonly Dictionary<(string Folder, Guid Id), byte[]> probeFiles = [];
    public int SuccessfulProbes { get; private set; }
    public int ProbeFilesRemaining => probeFiles.Count;
    public bool IsConfigured => true;
    public Task<RemoteFolder> EnsureFolderAsync(SelectedSiteLocation location, string parentItemId, string name, CancellationToken ct)
    {
      ct.ThrowIfCancellationRequested();
      Assert.Equal(expected, location);
      Assert.Contains(parentItemId, ownedFolders);
      Assert.False(string.IsNullOrWhiteSpace(name));
      var key = (parentItemId, name);
      if (!folders.TryGetValue(key, out var folder))
      {
        folder = new("folder-" + Hashing.Sha256Hex(parentItemId + "/" + name)[..20], name);
        folders.Add(key, folder);
        Assert.True(ownedFolders.Add(folder.ItemId));
      }
      return Task.FromResult(folder);
    }
    public Task<RemoteCapabilityTest> TestReadWriteAsync(SelectedSiteLocation location, string folderItemId, CancellationToken ct)
    {
      ct.ThrowIfCancellationRequested();
      Assert.Equal(expected, location);
      Assert.Contains(folderItemId, ownedFolders);
      var key = (folderItemId, Guid.NewGuid());
      var bytes = System.Text.Encoding.UTF8.GetBytes("synthetic disposable capability probe " + key.Item2);
      probeFiles.Add(key, bytes.ToArray());
      try
      {
        var readback = probeFiles[key].ToArray();
        Assert.Equal(bytes, readback);
        Assert.Equal(Hashing.Sha256Hex(bytes), Hashing.Sha256Hex(readback));
      }
      finally { Assert.True(probeFiles.Remove(key)); }
      Assert.False(probeFiles.ContainsKey(key));
      SuccessfulProbes++;
      return Task.FromResult(new RemoteCapabilityTest(true, true, true, "synthetic-exact-readback-and-cleanup"));
    }
  }
  private sealed class ArchiveSourceSink(PbcTransferScope expectedScope, string driveId, byte[] bytes) : IPbcProviderSink
  {
    private PbcProviderReceipt? receipt;
    public async Task<PbcProviderReceipt> UploadAsync(PbcTransferPlan plan, CancellationToken ct)
    {
      ct.ThrowIfCancellationRequested();
      Assert.Equal(expectedScope, new PbcTransferScope(plan.FirmId, plan.ClientId, plan.EngagementId, plan.UploadIntentId));
      using var actual = new MemoryStream();
      foreach (var chunk in plan.Chunks.OrderBy(x => x.ChunkIndex)) await actual.WriteAsync(await File.ReadAllBytesAsync(chunk.StagedPath, ct), ct);
      Assert.Equal(bytes, actual.ToArray());
      Assert.Equal((bytes.LongLength, Hashing.Sha256Hex(bytes)), (plan.DeclaredByteCount, plan.FinalSha256Hex));
      return receipt = new(PbcSelectedSiteIdentity.Format(driveId, "source-item", "source-version-7"), Hashing.Sha256Hex(bytes), bytes.LongLength);
    }
    public Task<PbcProviderReceipt?> VerifyAsync(PbcTransferScope scope, string identity, CancellationToken ct)
    {
      ct.ThrowIfCancellationRequested();
      Assert.Equal(expectedScope, scope);
      Assert.Equal(receipt?.Identity, identity);
      return Task.FromResult(receipt);
    }
    public Task<PbcProviderReceipt?> ProbeAsync(PbcTransferScope scope, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); Assert.Equal(expectedScope, scope); return Task.FromResult(receipt); }
  }
}
