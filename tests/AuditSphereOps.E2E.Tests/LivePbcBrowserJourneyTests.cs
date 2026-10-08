using System.Security.Cryptography;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// Separately controlled live P2 journey: a client uploads a synthetic document in the portal, staff completes
/// the staged transfer on a web host that enqueues LIVE transfers, and the isolated Acceptance "pbc" worker
/// process writes it to the approved Development selected site. Reports BLOCKED_EXTERNAL without live inputs.
/// </summary>
[Trait("Category", "LiveMicrosoft")]
public sealed class LivePbcBrowserJourneyTests(ITestOutputHelper output)
{
  private static string? Env(string name) => Environment.GetEnvironmentVariable("AUDITSPHERE_LIVE_SITE_" + name);

  [Fact]
  [Trait("CaseId", "P2-LIVE-BROWSER-01")]
  public async Task ClientPortalUpload_IsDeliveredBySelectedSiteWorker_OrBlockedExternal()
  {
    var options = new SelectedSiteCertificateOptions(Env("TENANT_ID") ?? "", Env("CLIENT_ID") ?? "", Env("CREDENTIAL_REFERENCE") ?? "",
      Env("CERT") ?? "", Env("KEY") ?? "");
    if (!(Guid.TryParse(options.TenantId, out _) && Guid.TryParse(options.ClientId, out _) && File.Exists(options.CertificatePath) &&
          File.Exists(options.PrivateKeyPath) && !string.IsNullOrWhiteSpace(Env("SITE_ID")) && !string.IsNullOrWhiteSpace(Env("DRIVE_ID")) &&
          !string.IsNullOrWhiteSpace(Env("ROOT_ID"))))
    {
      output.WriteLine("BLOCKED_EXTERNAL: live selected-site credential and site identifiers are not available to this run.");
      return;
    }
    var selectedSite = new Dictionary<string, string>
    {
      ["SelectedSite__TenantId"] = options.TenantId, ["SelectedSite__ClientId"] = options.ClientId,
      ["SelectedSite__CredentialReference"] = options.CredentialReference,
      ["SelectedSite__CertificatePath"] = options.CertificatePath, ["SelectedSite__PrivateKeyPath"] = options.PrivateKeyPath
    };

    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "P2-LIVE-BROWSER-01");
    var location = new SelectedSiteLocation(options.TenantId, Env("SITE_ID")!, Env("DRIVE_ID")!, options.CredentialReference);
    using var graphHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(100) };
    using var transport = new GraphPreauthenticatedTransport();
    var drive = new GraphSelectedSiteDrive(graphHttp, new CertificateSelectedSiteTokenSource(new HttpClient(), options), transport, configured: true);
    string? clientFolder = null;
    try
    {
      await SeedSelectedSiteAsync(host, options, location);
      await using (var db = host.CreateDbContext())
      {
        var admin = PbcSeed.Actor(host.Fixture.Admin, "Administrator");
        Assert.True((await PbcRepositoryProvisioningService.ProvisionClientWorkspaceAsync(db, admin, drive, host.Fixture.ClientId, DateTimeOffset.UtcNow)).Succeeded);
        clientFolder = (await db.ClientWorkspaces.AsNoTracking().SingleAsync()).RemoteItemId;
        var repository = await PbcRepositoryProvisioningService.ProvisionEngagementRepositoryAsync(db, admin, drive, host.Fixture.EngagementId, DateTimeOffset.UtcNow);
        Assert.Equal("VERIFIED", repository.Value?.State);
      }

      var liveStaffUrl = await host.StartApiForIdentityAsync(host.Fixture.Staff,
        new Dictionary<string, string>(selectedSite) { ["PbcTransfer__LiveProvider"] = "true" });
      await host.StartLivePbcWorkerAsync(selectedSite);

      using var playwright = await Playwright.CreateAsync();
      await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
      var clientPage = await (await browser.NewContextAsync()).NewPageAsync();
      var staffPage = await (await browser.NewContextAsync()).NewPageAsync();
      var fileBytes = System.Text.Encoding.UTF8.GetBytes($"%PDF-1.7 AuditSphere synthetic live PBC document {Guid.NewGuid():N}");
      var digest = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();

      await clientPage.GotoAsync($"{host.ClientUrl}/auth/sign-in?returnUrl={Uri.EscapeDataString($"/portal/requests/{host.RequestId:D}")}");
      await clientPage.GetByRole(AriaRole.Heading, new() { Name = "PBC request" }).WaitForAsync();
      await clientPage.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 });
      await clientPage.WaitForTimeoutAsync(300);
      var fileInputId = $"pbc-file-{host.RequestId:N}";
      await clientPage.Locator($"#{fileInputId}").SetInputFilesAsync(new FilePayload
        { Name = "AuditSphere-Live-Synthetic-PBC.pdf", MimeType = "application/pdf", Buffer = fileBytes });
      await Assertions.Expect(clientPage.Locator("#content-hash")).ToHaveValueAsync(digest, new() { Timeout = 10000 });
      await clientPage.GetByRole(AriaRole.Button, new() { Name = "Upload file" }).ClickAsync();
      await clientPage.GetByText($"Staged {fileBytes.Length} bytes in 1 chunk(s); trusted completion is still required.").WaitForAsync(new() { Timeout = 15000 });

      Guid uploadId;
      await using (var db = host.CreateDbContext())
        uploadId = (await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.PbcRequestId == host.RequestId)).Id;

      await staffPage.GotoAsync($"{liveStaffUrl}/auth/sign-in?returnUrl={Uri.EscapeDataString($"/app/engagements/{host.Fixture.EngagementId:D}/pbc")}");
      await staffPage.GetByRole(AriaRole.Heading, new() { Name = "Prepared-by-client requests" }).WaitForAsync();
      await staffPage.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 });
      await staffPage.GetByRole(AriaRole.Button, new() { Name = "Complete staged transfer" }).ClickAsync();
      await staffPage.Locator(".command-result").WaitForAsync(new() { Timeout = 15000 });
      Assert.StartsWith("Staged bytes verified;", await staffPage.Locator(".command-result").InnerTextAsync());

      await using (var db = host.CreateDbContext())
      {
        var operation = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.TargetId == uploadId);
        Assert.Equal(("LIVE", "pbc"), (operation.ExecutionMode.ToString(), operation.ExecutionGroup));
      }
      await host.WaitForReceivedAsync(uploadId);

      await using (var db = host.CreateDbContext())
      {
        var intent = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == uploadId);
        Assert.Equal((PbcUploadStates.Received, digest), (intent.State, intent.ProviderReceiptDigest));
        var operation = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.TargetId == uploadId);
        var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(host.DbOptions));
        var sink = new GraphPbcProviderSink(new PbcRepositoryBindingResolver(factory), drive, factory);
        var verified = await sink.VerifyAsync(new PbcTransferScope(host.Fixture.FirmId, host.Fixture.ClientId, host.Fixture.EngagementId, uploadId),
          operation.ResultIdentity!, default);
        Assert.Equal((digest, (long)fileBytes.Length), (verified!.ContentSha256Hex, verified.ByteCount));
        output.WriteLine($"client portal upload delivered by the live pbc worker process to SharePoint; exact version {operation.ResultIdentity!.Split(':')[^1]} re-read with matching SHA-256");
      }
    }
    finally
    {
      if (clientFolder is not null)
        output.WriteLine($"cleanup: synthetic client folder deleted={await drive.DeleteItemAsync(location, clientFolder, default)}");
    }
  }

  private static async Task SeedSelectedSiteAsync(OwnedHost host, SelectedSiteCertificateOptions options, SelectedSiteLocation location)
  {
    var now = DateTimeOffset.UtcNow;
    var connectionId = Guid.NewGuid();
    var templateId = Guid.NewGuid();
    var acceptanceId = Guid.NewGuid();
    await using var db = host.CreateDbContext();
    (await db.PracticeClients.SingleAsync(x => x.Id == host.Fixture.ClientId)).LegalName = "AuditSphere P2 Live Browser";
    db.AcceptanceDecisions.Add(new AcceptanceDecision { Id = acceptanceId, FirmId = host.Fixture.FirmId, PracticeClientId = host.Fixture.ClientId,
      Decision = "Accepted", ServiceRoute = "FinancialStatementAudit", Rationale = "Synthetic live P2 browser fixture.",
      EvaluationTemplateVersion = "synthetic-v1", EvaluationSnapshotDigest = new string('a', 64), DecidedByUserId = host.Fixture.Admin.Id, DecidedAt = now });
    db.ClientWorkspaces.Add(new ClientWorkspace { Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, PracticeClientId = host.Fixture.ClientId,
      AcceptanceDecisionId = acceptanceId, LogicalKey = "client-workspace/" + host.Fixture.ClientId, State = ClientWorkspaceStates.WaitingForIntegration, CreatedAt = now });
    db.Microsoft365ConnectionRevisions.Add(new Microsoft365ConnectionRevision { Id = connectionId, FirmId = host.Fixture.FirmId, TenantId = options.TenantId,
      LoginClientIdReference = "slot:login", RuntimeCredentialReference = options.CredentialReference,
      State = Microsoft365RevisionStates.Active, ConsentState = "VERIFIED", CreatedAt = now });
    foreach (var (purpose, id) in new[] { (FolderTemplatePurposes.ClientWorkspace, templateId), (FolderTemplatePurposes.EngagementWorkspace, Guid.NewGuid()) })
      db.FolderTemplateVersions.Add(new FolderTemplateVersion { Id = id, FirmId = host.Fixture.FirmId, Purpose = purpose,
        ManifestJson = Microsoft365ConfigurationService.DefaultManifest(purpose), ManifestDigest = new string('b', 64), CreatedAt = now, ApprovedAt = now });
    db.FirmWorkspaceConfigurations.Add(new FirmWorkspaceConfiguration { Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ConnectionRevisionId = connectionId,
      FolderTemplateVersionId = templateId, TenantId = options.TenantId, SiteId = location.SiteId, DriveId = location.DriveId, RootFolderId = Env("ROOT_ID")!,
      DisplayUrl = "https://example.sharepoint.com/", AccessProfile = Microsoft365AccessProfiles.AppMediated, CreatedAt = now });
    await db.SaveChangesAsync();
  }
}
