using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// Separately controlled live P2 acceptance against an approved selected site. It provisions a synthetic
/// client workspace and engagement repository, uploads one synthetic document, verifies the exact stored
/// version, reconciles by probe, then deletes everything it created. Without live inputs it reports
/// BLOCKED_EXTERNAL. Inputs are private environment variables: AUDITSPHERE_LIVE_SITE_TENANT_ID,
/// _CLIENT_ID, _CREDENTIAL_REFERENCE, _CERT, _KEY, _SITE_ID, _DRIVE_ID, _ROOT_ID (prefix AUDITSPHERE_LIVE_SITE_).
/// </summary>
[Trait("Category", "LiveMicrosoft")]
[Trait("Profile", "Database")]
public sealed class LivePbcSelectedSiteAcceptanceTests(ITestOutputHelper output)
{
  private static string? Env(string name) => Environment.GetEnvironmentVariable("AUDITSPHERE_LIVE_SITE_" + name);

  [Fact]
  public async Task SelectedSitePbcTransfer_IsBlockedExternalUnlessLiveSiteIsSupplied()
  {
    var options = new SelectedSiteCertificateOptions(Env("TENANT_ID") ?? "", Env("CLIENT_ID") ?? "", Env("CREDENTIAL_REFERENCE") ?? "",
      Env("CERT") ?? "", Env("KEY") ?? "");
    var live = Guid.TryParse(options.TenantId, out _) && Guid.TryParse(options.ClientId, out _) && File.Exists(options.CertificatePath) &&
      File.Exists(options.PrivateKeyPath) && !string.IsNullOrWhiteSpace(Env("SITE_ID")) && !string.IsNullOrWhiteSpace(Env("DRIVE_ID")) &&
      !string.IsNullOrWhiteSpace(Env("ROOT_ID")) && !string.IsNullOrWhiteSpace(options.CredentialReference);
    if (!live)
    {
      output.WriteLine("BLOCKED_EXTERNAL: live selected-site credential and site identifiers are not available to this run.");
      var placeholder = new GraphPbcProviderSink();
      await Assert.ThrowsAsync<AuditSphereOps.Application.Operations.OperationBlockedException>(() => placeholder.ProbeAsync(
        new PbcTransferScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), default));
      return;
    }

    await using var pg = await PgTestSchema.CreateAsync();
    var transfer = await PbcSeed.CreateTransferHarnessAsync(pg);
    var fixture = transfer.Fixture;
    var now = DateTimeOffset.UtcNow;
    var connectionId = Guid.NewGuid();
    var templateId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var acceptanceId = Guid.NewGuid();
      (await db.PracticeClients.SingleAsync(x => x.Id == fixture.ClientId)).LegalName = "AuditSphere P2 Live Acceptance";
      db.AcceptanceDecisions.Add(new AcceptanceDecision { Id = acceptanceId, FirmId = fixture.FirmId, PracticeClientId = fixture.ClientId,
        Decision = "Accepted", ServiceRoute = "FinancialStatementAudit", Rationale = "Synthetic live P2 acceptance fixture.",
        EvaluationTemplateVersion = "synthetic-v1", EvaluationSnapshotDigest = new string('a', 64), DecidedByUserId = fixture.Admin.Id, DecidedAt = now });
      db.ClientWorkspaces.Add(new ClientWorkspace { Id = Guid.NewGuid(), FirmId = fixture.FirmId, PracticeClientId = fixture.ClientId,
        AcceptanceDecisionId = acceptanceId, LogicalKey = "client-workspace/" + fixture.ClientId, State = ClientWorkspaceStates.WaitingForIntegration, CreatedAt = now });
      db.Microsoft365ConnectionRevisions.Add(new Microsoft365ConnectionRevision { Id = connectionId, FirmId = fixture.FirmId, TenantId = options.TenantId,
        LoginClientIdReference = "slot:login", RuntimeCredentialReference = options.CredentialReference,
        State = Microsoft365RevisionStates.Active, ConsentState = "VERIFIED", CreatedAt = now });
      foreach (var (purpose, id) in new[] { (FolderTemplatePurposes.ClientWorkspace, templateId), (FolderTemplatePurposes.EngagementWorkspace, Guid.NewGuid()) })
        db.FolderTemplateVersions.Add(new FolderTemplateVersion { Id = id, FirmId = fixture.FirmId, Purpose = purpose,
          ManifestJson = Microsoft365ConfigurationService.DefaultManifest(purpose), ManifestDigest = new string('b', 64), CreatedAt = now, ApprovedAt = now });
      db.FirmWorkspaceConfigurations.Add(new FirmWorkspaceConfiguration { Id = Guid.NewGuid(), FirmId = fixture.FirmId, ConnectionRevisionId = connectionId,
        FolderTemplateVersionId = templateId, TenantId = options.TenantId, SiteId = Env("SITE_ID")!, DriveId = Env("DRIVE_ID")!, RootFolderId = Env("ROOT_ID")!,
        DisplayUrl = "https://example.sharepoint.com/", AccessProfile = Microsoft365AccessProfiles.AppMediated, CreatedAt = now });
      await db.SaveChangesAsync();
    }

    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(100) };
    using var transport = new GraphPreauthenticatedTransport();
    var drive = new GraphSelectedSiteDrive(http, new CertificateSelectedSiteTokenSource(new HttpClient(), options), transport, configured: true);
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var admin = PbcSeed.Actor(fixture.Admin, "Administrator");
    var location = new SelectedSiteLocation(options.TenantId, Env("SITE_ID")!, Env("DRIVE_ID")!, options.CredentialReference);
    string? clientFolderId = null;
    try
    {
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var client = await PbcRepositoryProvisioningService.ProvisionClientWorkspaceAsync(db, admin, drive, fixture.ClientId, DateTimeOffset.UtcNow);
        Assert.True(client.Succeeded, client.Message);
        clientFolderId = (await db.ClientWorkspaces.AsNoTracking().SingleAsync()).RemoteItemId;
        var repository = await PbcRepositoryProvisioningService.ProvisionEngagementRepositoryAsync(db, admin, drive, fixture.EngagementId, DateTimeOffset.UtcNow);
        output.WriteLine($"engagement repository: {repository.Value?.State} {repository.Value?.DiagnosticCode ?? repository.Message}");
        Assert.Equal("VERIFIED", repository.Value?.State);
      }
      var scope = new PbcTransferScope(fixture.FirmId, fixture.ClientId, fixture.EngagementId, transfer.Staged.UploadIntentId);
      var sink = new GraphPbcProviderSink(new PbcRepositoryBindingResolver(factory), drive, factory);
      await using var read = new AuditSphereDbContext(pg.Options);
      var intent = await read.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == scope.UploadIntentId);
      var chunks = await read.PbcUploadChunks.AsNoTracking().Where(x => x.PbcUploadIntentId == intent.Id).OrderBy(x => x.ChunkIndex).ToListAsync();
      var plan = new PbcTransferPlan(intent.FirmId, intent.ClientId, intent.EngagementId, intent.Id, intent.DeclaredByteCount,
        intent.DeclaredSha256Hex.ToLowerInvariant(), chunks.Select(x => new PbcTransferChunk(x.ChunkIndex, x.Offset, x.ByteCount, x.Sha256Hex, x.StagedPath!)).ToArray());
      var receipt = await sink.UploadAsync(plan, default);
      output.WriteLine($"upload: sha256 match={receipt.ContentSha256Hex == plan.FinalSha256Hex} bytes={receipt.ByteCount} version={receipt.Identity.Split(':')[^1]}");
      Assert.Equal((plan.FinalSha256Hex, plan.DeclaredByteCount), (receipt.ContentSha256Hex, receipt.ByteCount));
      Assert.Equal(receipt, await sink.VerifyAsync(scope, receipt.Identity, default));
      Assert.Equal(receipt, await sink.ProbeAsync(scope, default));
      await Assert.ThrowsAsync<IOException>(() => sink.UploadAsync(plan, default)); // no overwrite on retry
      output.WriteLine("exact-version readback, probe reconciliation and no-overwrite retry: PASS");
    }
    finally
    {
      if (clientFolderId is not null)
        output.WriteLine($"cleanup: synthetic client folder deleted={await drive.DeleteItemAsync(location, clientFolderId, default)}");
    }
  }
}
