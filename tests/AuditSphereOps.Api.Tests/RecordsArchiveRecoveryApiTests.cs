using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuditSphereOps.Api.Tests;

public sealed class RecordsArchiveRecoveryApiTests
{
  [Fact]
  [Trait("CaseId", "R09-PERSISTED-INCOMPLETE-REPLAY")]
  public async Task BuildReadFaultPersistsIncompleteAndExactHttpReplayDoesNotWriteAgain()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("R09-PERSISTED-INCOMPLETE-REPLAY");
    // The supported single-engagement fixture generates the engagement letter and binds
    // portal intake to this engagement. The completion-only sibling fixture omits that letter.
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg, includeSibling: false);
    var scope = world.Primary;
    var archiveId = Guid.CreateVersion7();
    var path = $"/api/ui/records/archives/{archiveId:D}";

    async Task<(AppUser User, Guid GrantId)> ProvisionAsync(Guid clientId, Guid? engagementId, string role, string kind)
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var admin = await db.Users.AsNoTracking().SingleAsync(x => x.Id == world.Administrator.UserId);
      var subject = Guid.NewGuid();
      var created = await RoleAdministrationService.EnsureUserAsync(db, world.Administrator,
        new(admin.TenantId, subject.ToString("D"), $"r09-{subject:N}@example.test",
          "Synthetic archive recovery " + role, kind, "APPROVED_ROSTER"));
      Assert.True(created.Succeeded, created.Message);
      Guid grantId;
      var grantScope = engagementId is null ? "CLIENT" : "ENGAGEMENT";
      if (kind == "Client")
      {
        var grant = await RoleAdministrationService.ApplyRoleGrantAndInvitationAsync(db, world.Administrator,
          new(created.Value, role, grantScope, clientId, engagementId));
        Assert.True(grant.Succeeded, grant.Message);
        Assert.Equal("NOT_SENT", grant.Value!.DeliveryState);
        grantId = grant.Value.RoleGrantId;
      }
      else
      {
        var grant = await RoleAdministrationService.ApplyRoleGrantAsync(db, world.Administrator,
          new(created.Value, role, grantScope, clientId, engagementId));
        Assert.True(grant.Succeeded, grant.Message);
        grantId = grant.Value;
      }
      return (await db.Users.AsNoTracking().SingleAsync(x => x.Id == created.Value), grantId);
    }

    var (custodian, custodianGrant) = await ProvisionAsync(scope.ClientId, scope.EngagementId, "Partner", "Staff");
    var (clientUser, _) = await ProvisionAsync(scope.ClientId, scope.EngagementId, "ClientUser", "Client");
    var clientActor = new ActorContext(clientUser.Id, scope.FirmId, clientUser.SessionEpoch, ["ClientUser"]);
    PbcSeed.Fixture pbc;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var firstSignIn = await ClientPortalService.CompleteFirstSignInAsync(db, clientActor, true);
      Assert.True(firstSignIn.Succeeded, firstSignIn.Message);
      var portalAccess = await AuthorizationDecision.AuthorizeAsync(db, clientActor,
        new(scope.FirmId, scope.ClientId, scope.EngagementId, RequiredRoles: ["ClientUser"]));
      Assert.True(portalAccess.Succeeded, portalAccess.Message);
      pbc = new(scope.FirmId, scope.ClientId, scope.EngagementId,
        await db.Users.AsNoTracking().SingleAsync(x => x.Id == scope.Preparer.UserId),
        await db.Users.AsNoTracking().SingleAsync(x => x.Id == world.Reviewer.UserId),
        clientUser, await db.Users.AsNoTracking().SingleAsync(x => x.Id == world.Administrator.UserId));
    }
    var source = await SupportedArchiveSourceFixture.PrepareAsync(pg, pbc, world.Administrator, scope.Preparer,
      clientActor, "Synthetic R09 source bound to the captured PBC version."u8.ToArray());
    var otherClientId = await SupportedAuditCompletionWorldBuilder.CreateAdditionalClientAsync(pg, world);
    var (otherClientPartner, _) = await ProvisionAsync(otherClientId, null, "Partner", "Staff");

    Guid referenceId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var snapshot = await db.DocumentSnapshots.AsNoTracking().SingleAsync(x => x.Id == source.SnapshotId);
      referenceId = snapshot.DocumentReferenceId;
      Assert.True(await db.DocumentReferences.AnyAsync(x => x.Id == referenceId && x.FirmId == scope.FirmId &&
        x.ClientId == scope.ClientId && x.EngagementId == scope.EngagementId));
      await ArchiveRecoveryBrowserHarness.AddSyntheticArchiveAsync(db, scope, custodian, archiveId);
      Assert.False(await db.ArchiveManifests.AnyAsync());
    }
    var originalBlockers = await CompletionBlockersAsync();
    Assert.NotEmpty(originalBlockers);
    var initialSources = await SourceRowsAsync();

    var fault = new HideReferenceOnBuild(scope, referenceId, path + "/build");
    using var factory = Factory(pg, custodian, services =>
    {
      services.RemoveAll<IDbContextFactory<AuditSphereDbContext>>();
      services.RemoveAll<DbContextOptions<AuditSphereDbContext>>();
      services.AddDbContextFactory<AuditSphereDbContext>((provider, options) => options.UseNpgsql(pg.ConnectionString)
        .AddInterceptors(fault.ForRequests(provider.GetRequiredService<IHttpContextAccessor>())));
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignInAsync(client);
    var requestKey = Guid.CreateVersion7().ToString("D");
    var request = new { commercialBundleId = (Guid?)null, requestKey };

    fault.Armed = true;
    using var first = await PostAsync(client, csrf, path + "/build", request);
    fault.Armed = false;
    Assert.Equal(1, fault.Hits);
    Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    using var responseBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
    var built = responseBody.RootElement.GetProperty("value").Deserialize<ArchiveManifestResult>(JsonSerializerOptions.Web)!;
    Assert.Equal((archiveId, 1L, "BUILT", "INCOMPLETE"), (built.ArchiveId, built.ManifestVersion, built.Status, built.CompletenessStatus));

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var row = Assert.Single(await db.ArchiveManifests.AsNoTracking().ToListAsync());
      Assert.Equal((built.ManifestId, built.ManifestVersion, built.Digest, built.CompletenessStatus, requestKey),
        (row.Id, row.Version, row.ManifestDigest, row.CompletenessStatus, row.BuildRequestKey));
      Assert.Equal("1 snapshot reference(s) are missing.", row.CompletenessException);
      var entries = await db.ArchiveManifestEntries.AsNoTracking().OrderBy(x => x.Ordinal).ToListAsync();
      Assert.Equal(built.EntryCount, entries.Count);
      Assert.Equal(source.SnapshotId, Assert.Single(entries, x => x.EntryKind == "DOCUMENT_SNAPSHOT").SourceId);
      var export = Assert.Single(await db.ArchiveStructuredExports.AsNoTracking().ToListAsync());
      Assert.Equal(row.Id, export.ArchiveManifestId);
      Assert.Equal(export.ContentHash, Assert.Single(entries, x => x.EntryKind == "STRUCTURED_EXPORT").ContentHash);
      using var exportedPayload = JsonDocument.Parse(export.PayloadJson);
      var exportedReferences = exportedPayload.RootElement.GetProperty("documents").GetProperty("references");
      Assert.Equal(referenceId, Assert.Single(exportedReferences.EnumerateArray()).GetProperty("Id").GetGuid());
      Assert.Equal(ArchiveStates.Issued, (await db.Archives.AsNoTracking().SingleAsync()).Status);
    }
    var retained = await ArchiveRowsAsync();

    // The second host uses the normal context factory. No fault is registered on any recovery request.
    using var recoveryFactory = Factory(pg, custodian);
    using var recovery = recoveryFactory.CreateClient(new() { AllowAutoRedirect = false });
    var recoveryCsrf = await SignInAsync(recovery);
    using (var bundlePageResponse = await recovery.GetAsync(path + "/commercial-bundles?page=0"))
    {
      Assert.Equal(HttpStatusCode.OK, bundlePageResponse.StatusCode);
      var bundlePage = (await bundlePageResponse.Content.ReadFromJsonAsync<ArchiveCommercialBundlePage>())!;
      Assert.Equal((archiveId, scope.EngagementId, 0, false),
        (bundlePage.ArchiveId, bundlePage.EngagementId, bundlePage.Page, bundlePage.HasMore));
      Assert.Empty(bundlePage.Bundles);
    }
    using (var invalidBundlePage = await recovery.GetAsync(path + "/commercial-bundles?page=101"))
      await AssertRefusedAsync(invalidBundlePage, HttpStatusCode.BadRequest, "request.invalid");
    using (var read = await recovery.GetAsync(path))
    {
      Assert.Equal(HttpStatusCode.OK, read.StatusCode);
      var view = (await read.Content.ReadFromJsonAsync<ArchiveRecordView>())!;
      Assert.Equal((built.ManifestId, built.ManifestVersion, built.Digest, "INCOMPLETE"),
        (view.ManifestId!.Value, view.ManifestVersion!.Value, view.ManifestDigest, view.CompletenessStatus));
      Assert.Equal(built.EntryCount, view.Entries.Count);
      Assert.False(view.CanReview);
      Assert.False(view.CanExport);
    }
    using var replay = await PostAsync(recovery, recoveryCsrf, path + "/build", request);
    Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
    using (var conflict = await PostAsync(recovery, recoveryCsrf, path + "/build",
      new { commercialBundleId = Guid.CreateVersion7(), requestKey }))
      await AssertRefusedAsync(conflict, HttpStatusCode.Conflict, ErrorCodes.IdempotencyConflict);
    using (var staleSelection = await PostAsync(recovery, recoveryCsrf, path + "/build",
      new { commercialBundleId = Guid.CreateVersion7(), requestKey = Guid.CreateVersion7().ToString("D") }))
      await AssertRefusedAsync(staleSelection, HttpStatusCode.BadRequest, "archive-copy-commercial-bundle-unavailable");
    using (var review = await PostAsync(recovery, recoveryCsrf, path + "/review",
      new { manifestId = built.ManifestId, manifestVersion = built.ManifestVersion, manifestDigest = built.Digest,
        requestKey = Guid.CreateVersion7().ToString("D") }))
      await AssertRefusedAsync(review, HttpStatusCode.BadRequest, ErrorCodes.GateBlocked);
    using (var export = await recovery.GetAsync(path + "/export"))
    {
      await AssertRefusedAsync(export, HttpStatusCode.BadRequest, ErrorCodes.GateBlocked);
      Assert.Null(export.Content.Headers.ContentDisposition);
    }

    using var otherClientFactory = Factory(pg, otherClientPartner);
    using var denied = otherClientFactory.CreateClient(new() { AllowAutoRedirect = false });
    var deniedCsrf = await SignInAsync(denied);
    using (var foreignBundlePage = await denied.GetAsync(path + "/commercial-bundles?page=0"))
      await AssertRefusedAsync(foreignBundlePage, HttpStatusCode.Forbidden, ErrorCodes.ScopeDenied);
    using (var foreign = await PostAsync(denied, deniedCsrf, path + "/build", request))
      await AssertRefusedAsync(foreign, HttpStatusCode.Forbidden, ErrorCodes.ScopeDenied);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db, world.Administrator,
        new(custodianGrant, Reason: "Synthetic recovery session revocation."));
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    using (var stale = await PostAsync(recovery, recoveryCsrf, path + "/build", request))
      Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
    Assert.Equal(retained, await ArchiveRowsAsync());
    Assert.Equal(initialSources, await SourceRowsAsync());
    Assert.Equal(originalBlockers, await CompletionBlockersAsync());
    Assert.Equal(1, fault.Hits);

    async Task<string[]> CompletionBlockersAsync()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      Assert.Equal("Active", await db.Engagements.Where(x => x.Id == scope.EngagementId).Select(x => x.Status).SingleAsync());
      var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, world.Reviewer, scope.EngagementId);
      Assert.True(completion.Succeeded, completion.Message);
      return completion.Value!.Blockers.Order(StringComparer.Ordinal).ToArray();
    }

    async Task<string> ArchiveRowsAsync()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return JsonSerializer.Serialize(new
      {
        archive = await db.Archives.AsNoTracking().SingleAsync(),
        manifest = await db.ArchiveManifests.AsNoTracking().SingleAsync(),
        entries = await db.ArchiveManifestEntries.AsNoTracking().OrderBy(x => x.Ordinal).ToListAsync(),
        export = await db.ArchiveStructuredExports.AsNoTracking().SingleAsync(),
        rowVersions = await db.Database.SqlQueryRaw<string>("""
          SELECT 'archive:' || id || ':' || xmin::text AS "Value" FROM archives
          UNION ALL SELECT 'manifest:' || id || ':' || xmin::text FROM archive_manifests
          UNION ALL SELECT 'entry:' || id || ':' || xmin::text FROM archive_manifest_entries
          UNION ALL SELECT 'export:' || id || ':' || xmin::text FROM archive_structured_exports
          """).OrderBy(x => x).ToListAsync()
      });
    }

    async Task<string> SourceRowsAsync()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return JsonSerializer.Serialize(new
      {
        reference = await db.DocumentReferences.AsNoTracking().SingleAsync(x => x.Id == referenceId),
        snapshot = await db.DocumentSnapshots.AsNoTracking().SingleAsync(x => x.Id == source.SnapshotId)
      });
    }
  }

  [Fact]
  [Trait("CaseId", "ARC05-SELECTED-BUNDLE-HTTP-RECOVERY")]
  public async Task SelectedProducedBundle_BuildsExactArchiveLineageAndRecoversAfterCommittedResponseLoss()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ARC05-SELECTED-BUNDLE-HTTP-RECOVERY");
    var world = await AuditDeliverablesTests.SeedSupportedDeliverablesAsync(pg);
    var bundleScope = new CompletionBundleScope(world.FirmId, world.ClientId, world.EngagementId, world.U);
    await CompletionBundleFixture.ReleasedSupportedFinancialPackageAsync(pg.Options, bundleScope);
    await CompletionBundleFixture.PostSupportedBalanceFeeAsync(pg.Options, bundleScope, automateBalance: true);
    await AuditDeliverablesTests.RefreshSupportedPortalRiskAsync(pg, world);
    await AuditDeliverablesTests.ReviewSupportedBundleApplicabilityAsync(pg, world);
    await PrepareEarlierCompletionReviewsAsync(pg, world);
    await PrepareSignedReportAsync(pg, world);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var management = await AuditDeliverableService.GenerateReportAsync(db, world.A("manager", "Manager"),
        world.EngagementId, DeliverableKinds.ManagementLetter);
      Assert.True(management.Succeeded, management.Message);
    }

    var archiveId = Guid.CreateVersion7();
    var archivePath = $"/api/ui/records/archives/{archiveId:D}";
    var partner = world.U["partner"];
    var archiveScope = new SupportedAuditScope(world.FirmId, world.ClientId, world.EngagementId,
      world.A("associate", "Staff"));
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await ArchiveRecoveryBrowserHarness.AddSyntheticArchiveAsync(db, archiveScope, partner, archiveId, default);
    }

    var lostResponse = new LoseFirstArchiveBuildResponse(archiveId);
    using var factory = Factory(pg, partner,
      services => services.AddSingleton<IStartupFilter>(lostResponse));
    factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
    using var startupClient = factory.CreateClient(new() { AllowAutoRedirect = false });
    var origin = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true })
    { BaseAddress = new Uri(origin) };
    var csrf = await SignInAsync(client);
    using (var assembled = await PostAsync(client, csrf,
      $"/api/ui/engagements/{world.EngagementId:D}/completion/bundle", new { statementsReviewed = true }))
    {
      Assert.Equal(HttpStatusCode.OK, assembled.StatusCode);
      using var body = JsonDocument.Parse(await assembled.Content.ReadAsStringAsync());
      var producedBundleId = body.RootElement.GetProperty("value").GetGuid();
      await using var db = new AuditSphereDbContext(pg.Options);
      var producedBundle = await db.CommercialDeliverableBundles.AsNoTracking().SingleAsync(x => x.Id == producedBundleId);
      Assert.Equal(Hashing.Sha256Hex(producedBundle.Content), producedBundle.ContentSha256);
      Assert.Equal(Hashing.Sha256Hex(producedBundle.ManifestJson), producedBundle.ManifestSha256);
    }

    using (var pageResponse = await client.GetAsync(archivePath + "/commercial-bundles?page=0"))
    {
      Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
      var page = (await pageResponse.Content.ReadFromJsonAsync<ArchiveCommercialBundlePage>())!;
      var selected = Assert.Single(page.Bundles);
      Assert.Equal((archiveId, world.EngagementId), (page.ArchiveId, page.EngagementId));
      Assert.Equal((selected.Id, selected.ContentSha256, selected.ManifestSha256),
        await ProducedBundleIdentityAsync());

      var requestKey = Guid.CreateVersion7().ToString("D");
      var request = new { commercialBundleId = (Guid?)selected.Id, requestKey };
      lostResponse.Armed = true;
      var failure = await Record.ExceptionAsync(async () =>
      {
        using var response = await PostAsync(client, csrf, archivePath + "/build", request);
        _ = await response.Content.ReadAsByteArrayAsync();
      });
      Assert.NotNull(failure);
      Assert.True(failure is HttpRequestException or IOException or OperationCanceledException,
        $"Expected the real HTTP response to be truncated after commit, got {failure.GetType().Name}.");
      Assert.True(lostResponse.DidAbortCommittedResponse);

      ArchiveManifestResult committed;
      string entriesBeforeReplay;
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var manifest = await db.ArchiveManifests.AsNoTracking().SingleAsync(x => x.ArchiveId == archiveId);
        Assert.Equal(selected.Id, manifest.CommercialBundleId);
        Assert.Equal(requestKey, manifest.BuildRequestKey);
        committed = new(manifest.ArchiveId, manifest.Id, manifest.Version, manifest.Status,
          manifest.ManifestDigest, manifest.EntryCount, manifest.CompletenessStatus);
        var bundle = await db.CommercialDeliverableBundles.AsNoTracking().SingleAsync(x => x.Id == selected.Id);
        var entries = await db.ArchiveManifestEntries.AsNoTracking().Where(x => x.ArchiveManifestId == manifest.Id)
          .OrderBy(x => x.Ordinal).ToListAsync();
        var assembly = Assert.Single(entries, x => x.EntryKind == "COMMERCIAL_BUNDLE_ASSEMBLY");
        Assert.Equal((bundle.Id, bundle.ContentSha256, (long)bundle.Content.Length),
          (assembly.SourceId, assembly.ContentHash, assembly.ByteCount));
        var bundleManifest = Assert.Single(entries, x => x.EntryKind == "COMMERCIAL_BUNDLE_MANIFEST");
        Assert.Equal((bundle.Id, bundle.ManifestSha256, (long)System.Text.Encoding.UTF8.GetByteCount(bundle.ManifestJson)),
          (bundleManifest.SourceId, bundleManifest.ContentHash, bundleManifest.ByteCount));
        var bundleParts = entries.Where(x => x.EntryKind == "COMMERCIAL_BUNDLE_PART").ToArray();
        using var sourceManifest = JsonDocument.Parse(bundle.ManifestJson);
        var expectedParts = sourceManifest.RootElement.GetProperty("Parts").EnumerateArray()
          .Select(x => (Path: x.GetProperty("Path").GetString()!, Hash: x.GetProperty("Sha256").GetString()!,
            Bytes: x.GetProperty("Bytes").GetInt64())).OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        var actualParts = bundleParts.Select(x =>
        {
          using var metadata = JsonDocument.Parse(x.MetadataJson);
          return (Path: metadata.RootElement.GetProperty("PartPath").GetString()!, Hash: x.ContentHash, Bytes: x.ByteCount,
            x.SourceId);
        }).OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedParts.Length, actualParts.Length);
        Assert.All(actualParts, x => Assert.Equal(bundle.Id, x.SourceId));
        Assert.Equal(expectedParts, actualParts.Select(x => (x.Path, x.Hash, x.Bytes)));
        entriesBeforeReplay = JsonSerializer.Serialize(entries);
      }

      lostResponse.Armed = false;
      using (var replay = await PostAsync(client, csrf, archivePath + "/build", request))
      {
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(lostResponse.CapturedResponse, await replay.Content.ReadAsStringAsync());
      }
      using (var changed = await PostAsync(client, csrf, archivePath + "/build",
        new { commercialBundleId = (Guid?)Guid.CreateVersion7(), requestKey }))
        await AssertRefusedAsync(changed, HttpStatusCode.Conflict, ErrorCodes.IdempotencyConflict);
      using (var stale = await PostAsync(client, csrf, archivePath + "/build",
        new { commercialBundleId = (Guid?)Guid.CreateVersion7(), requestKey = Guid.CreateVersion7().ToString("D") }))
        await AssertRefusedAsync(stale, HttpStatusCode.BadRequest, "archive-copy-commercial-bundle-unavailable");

      var foreignAdministrator = await CreateForeignFirmAdministratorAsync();
      using var foreignFactory = Factory(pg, foreignAdministrator);
      using var foreignClient = foreignFactory.CreateClient(new() { AllowAutoRedirect = false });
      var foreignCsrf = await SignInAsync(foreignClient);
      using (var foreignBuild = await PostAsync(foreignClient, foreignCsrf, archivePath + "/build",
        new { commercialBundleId = (Guid?)selected.Id, requestKey = Guid.CreateVersion7().ToString("D") }))
        await AssertRefusedAsync(foreignBuild, HttpStatusCode.Forbidden, ErrorCodes.ScopeDenied);
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var manifest = await db.ArchiveManifests.AsNoTracking().SingleAsync(x => x.ArchiveId == archiveId);
        Assert.Equal((committed.ManifestId, committed.Digest), (manifest.Id, manifest.ManifestDigest));
        Assert.Equal(1, await db.ArchiveManifests.CountAsync());
        var entriesAfterReplay = JsonSerializer.Serialize(await db.ArchiveManifestEntries.AsNoTracking()
          .Where(x => x.ArchiveManifestId == manifest.Id).OrderBy(x => x.Ordinal).ToListAsync());
        Assert.Equal(entriesBeforeReplay, entriesAfterReplay);
        Assert.False(await db.ArchiveManifests.AnyAsync(x => x.ArchiveId != archiveId));
      }
    }

    async Task<(Guid Id, string ContentSha256, string ManifestSha256)> ProducedBundleIdentityAsync()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var bundle = await db.CommercialDeliverableBundles.AsNoTracking().SingleAsync();
      return (bundle.Id, bundle.ContentSha256, bundle.ManifestSha256);
    }
    async Task<AppUser> CreateForeignFirmAdministratorAsync()
    {
      var foreignInstallation = await SupportedAuditCompletionWorldBuilder.BootstrapAdminAsync(pg);
      await using var db = new AuditSphereDbContext(pg.Options);
      return await db.Users.AsNoTracking().SingleAsync(x => x.Id == foreignInstallation.Actor.UserId);
    }
  }

  private static async Task<Guid> PrepareSignedReportAsync(ITestPostgresDatabase pg, AuditDeliverablesTests.World world)
  {
    var partner = world.A("partner", "Partner");
    var manager = world.A("manager", "Manager");
    await PrepareCurrentPartnerClearanceAsync(pg, world);
    await using var db = new AuditSphereDbContext(pg.Options);
    var opinion = await AuditDeliverableService.DecideOpinionAsync(db, partner, world.EngagementId,
      "UNMODIFIED", null, null);
    Assert.True(opinion.Succeeded, opinion.Message);
    var letterResult = await AuditDeliverableService.GenerateReportAsync(db, manager, world.EngagementId,
      DeliverableKinds.RepresentationLetter);
    Assert.True(letterResult.Succeeded, letterResult.Message);
    var letterId = letterResult.Value!.DeliverableId!.Value;
    var letter = await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == letterId);
    var shared = await AuditDeliverableService.ShareWithClientAsync(db, manager, letterId);
    Assert.True(shared.Succeeded, shared.Message);
    var acknowledged = await AuditDeliverableService.AcknowledgeAsync(db, world.A("client", "ClientUser"),
      shared.Value, letter.ContentSha256);
    Assert.True(acknowledged.Succeeded, acknowledged.Message);
    var scan = await AuditDeliverableService.UploadSignedRepresentationAsync(db, world.A("client", "ClientUser"),
      letterId, letter.ContentSha256, "Authorized Management", CompletionTestFixtures.SignedPdf());
    Assert.True(scan.Succeeded, scan.Message);
    var uploaded = await db.SignedRepresentationLetters.AsNoTracking().SingleAsync(x => x.Id == scan.Value);
    var verification = await AuditDeliverableService.VerifySignedRepresentationAsync(db, partner, scan.Value,
      uploaded.ContentSha256, "Management signature and authority independently reviewed.");
    Assert.True(verification.Succeeded, verification.Message);
    var seal = await AuditDeliverableService.RegisterFirmSealAsync(db, partner, CompletionTestFixtures.Png(100, 100));
    Assert.True(seal.Succeeded, seal.Message);
    var reportResult = await AuditDeliverableService.GenerateReportAsync(db, manager, world.EngagementId,
      DeliverableKinds.IndependentAuditorsReport);
    Assert.True(reportResult.Succeeded, reportResult.Message);
    var signature = await AuditDeliverableService.RegisterSignatureAsync(db, partner, CompletionTestFixtures.Png(120, 40));
    Assert.True(signature.Succeeded, signature.Message);
    var signed = await AuditDeliverableService.SignIndependentReportAsync(db, partner,
      reportResult.Value!.DeliverableId!.Value);
    Assert.True(signed.Succeeded, signed.Message);
    return signed.Value;
  }

  private static async Task PrepareEarlierCompletionReviewsAsync(ITestPostgresDatabase pg, AuditDeliverablesTests.World world)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var goingConcern = await AuditCompletionService.RecordGoingConcernAssessmentAsync(db, world.A("senior", "Senior"),
      new(world.EngagementId, null, new DateOnly(2027, 2, 1), new DateOnly(2028, 3, 31),
        "Reviewed", false, true, "QAR", "Forecast reviewed", ["synthetic-evidence:forecast"]));
    Assert.True(goingConcern.Succeeded, goingConcern.Message);
    var goingConcernReview = await AuditCompletionService.ReviewCompletionRecordAsync(db, world.A("manager", "Manager"),
      "GOING_CONCERN", goingConcern.Value!.AssessmentId, goingConcern.Value.Revision);
    Assert.True(goingConcernReview.Succeeded, goingConcernReview.Message);
    var variance = await AuditCompletionService.RecordVarianceInvestigationAsync(db, world.A("senior", "Senior"),
      new(world.EngagementId, null, "Revenue", "FY2026", 100m, 105m, 10m, "QAR", null,
        ["synthetic-evidence:revenue-analysis"]));
    Assert.True(variance.Succeeded, variance.Message);
    var varianceReview = await AuditCompletionService.ReviewCompletionRecordAsync(db, world.A("manager", "Manager"),
      "VARIANCE", variance.Value!.InvestigationId, variance.Value.Revision);
    Assert.True(varianceReview.Succeeded, varianceReview.Message);
  }

  private static async Task PrepareCurrentPartnerClearanceAsync(ITestPostgresDatabase pg, AuditDeliverablesTests.World world)
  {
    var partner = world.A("partner", "Partner");
    var manager = world.A("manager", "Manager");
    await using var db = new AuditSphereDbContext(pg.Options);
    var result = await db.AuditProcedureResults.AsNoTracking().SingleAsync(x => x.Id == world.ResultId);
    var inputGeneration = await db.ClientSafetyStates.AsNoTracking()
      .Where(x => x.FirmId == world.FirmId && x.Id == world.ClientId)
      .Select(x => x.InputGeneration).SingleAsync();
    var resultId = world.ResultId;
    if (result.InputGeneration != inputGeneration)
    {
      Guid? mappingId = world.PlanningMappingId == Guid.Empty ? null : world.PlanningMappingId;
      var source = await MaterialityEngineService.GetSourceAsync(db, world.A("partner2", "Partner"),
        world.EngagementId, default, mappingId);
      Assert.True(source.Succeeded, $"GetSourceAsync failed: {source.ErrorCode} - {source.Message}");
      var calculation = await MaterialityEngineService.CalculateAsync(db, world.A("partner2", "Partner"),
        new(world.EngagementId, MaterialityBenchmarks.Revenue, null, 1m, 75m, 5m,
          "Revenue-driven trading entity", MappingId: mappingId,
          ExpectedDatasetId: mappingId is null ? null : source.Value!.DatasetId,
          ExpectedMappingVersion: mappingId is null ? null : source.Value!.MappingVersion,
          ExpectedDatasetDigest: mappingId is null ? null : source.Value!.DatasetDigest));
      Assert.True(calculation.Succeeded, $"CalculateAsync failed: {calculation.ErrorCode} - {calculation.Message}");
      var approval = await AuditPlanningService.ApproveMaterialityAssessmentAsync(db, partner,
        calculation.Value!.AssessmentId);
      Assert.True(approval.Succeeded, $"ApproveMaterialityAssessmentAsync failed: {approval.ErrorCode} - {approval.Message}");
      var submitted = await AuditProgramService.SubmitResultAsync(db, world.A("associate", "Staff"),
        new(world.ProcedureId, inputGeneration,
          "Selected 25 invoices around year end and agreed them to dispatch notes after the current accounting inputs were finalized.",
          "{}", ["synthetic-current-period-vouching"], "Revenue is recorded in the correct period."));
      Assert.True(submitted.Succeeded, $"SubmitResultAsync failed: {submitted.ErrorCode} - {submitted.Message}");
      resultId = submitted.Value!.AuditProcedureResultId;
    }
    var reviewed = await AuditProgramService.ReviewResultAsync(db, world.A("senior", "Senior"),
      new(resultId, "REVIEWED", null));
    Assert.True(reviewed.Succeeded, reviewed.Message);
    var memorandum = await AuditDeliverableService.GenerateSummaryReviewMemorandumAsync(db, manager,
      world.EngagementId, "Ready.");
    Assert.True(memorandum.Succeeded, memorandum.Message);
    var cleared = await AuditDeliverableService.PartnerClearAsync(db, partner, memorandum.Value,
      "Risks reviewed.", "Notes reviewed.");
    Assert.True(cleared.Succeeded, cleared.Message);
  }

  private static StandaloneApiApplicationFactory Factory(ITestPostgresDatabase pg, AppUser user,
    Action<IServiceCollection>? configure = null) => new(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = user.Subject,
      ["DevelopmentIdentity:TenantId"] = user.TenantId,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    }, configure);

  private static async Task<string> SignInAsync(HttpClient client)
  {
    using var signIn = await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie")
      .Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string csrf, string path, object body)
  {
    using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
    message.Headers.Add("X-XSRF-TOKEN", csrf);
    return await client.SendAsync(message);
  }

  private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
  {
    Assert.Equal(status, response.StatusCode);
    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    Assert.False(body.RootElement.TryGetProperty("value", out _));
  }

  private sealed class LoseFirstArchiveBuildResponse(Guid archiveId) : IStartupFilter
  {
    private int aborted;
    public bool Armed { get; set; }
    public bool DidAbortCommittedResponse { get; private set; }
    public string? CapturedResponse { get; private set; }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
      app.Use(async (http, continuation) =>
      {
        if (!Armed || !HttpMethods.IsPost(http.Request.Method) ||
            http.Request.Path != $"/api/ui/records/archives/{archiveId:D}/build" ||
            Interlocked.CompareExchange(ref aborted, 1, 0) != 0)
        {
          await continuation();
          return;
        }

        var responseStream = http.Response.Body;
        await using var buffered = new MemoryStream();
        http.Response.Body = buffered;
        try
        {
          await continuation();
          Assert.Equal((int)HttpStatusCode.OK, http.Response.StatusCode);
          var bytes = buffered.ToArray();
          Assert.True(bytes.Length > 1);
          CapturedResponse = System.Text.Encoding.UTF8.GetString(bytes);
          http.Response.Body = responseStream;
          http.Response.ContentLength = bytes.Length;
          await http.Response.StartAsync(http.RequestAborted);
          var prefixLength = Math.Max(1, bytes.Length / 2);
          await responseStream.WriteAsync(bytes.AsMemory(0, prefixLength), http.RequestAborted);
          await responseStream.FlushAsync(http.RequestAborted);
          DidAbortCommittedResponse = true;
          http.Abort();
        }
        finally
        {
          http.Response.Body = responseStream;
        }
      });
      next(app);
    };
  }

  private sealed class HideReferenceOnBuild(SupportedAuditScope scope, Guid referenceId, string buildPath)
  {
    private int hits;
    public int Hits => Volatile.Read(ref hits);
    public bool Armed { get; set; }

    public DbCommandInterceptor ForRequests(IHttpContextAccessor http) => new SourceReadInterceptor(this, http);

    private sealed class SourceReadInterceptor(HideReferenceOnBuild fault, IHttpContextAccessor http) : DbCommandInterceptor
    {
      public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
      {
        fault.HideReference(command, http.HttpContext?.Request);
        return ValueTask.FromResult(result);
      }
    }

    private void HideReference(DbCommand command, HttpRequest? request)
    {
      if (!Armed || request?.Path != buildPath || !HttpMethods.IsPost(request.Method) ||
          !command.CommandText.ReplaceLineEndings("\n").StartsWith(
            "SELECT d.id\nFROM document_references AS d\nWHERE ", StringComparison.Ordinal))
        return;

      Assert.NotNull(command.Transaction);
      Assert.Equal(new[] { scope.FirmId, scope.ClientId, scope.EngagementId }.Order(),
        command.Parameters.Cast<DbParameter>().Select(p => Assert.IsType<Guid>(p.Value)).Order());
      Assert.Equal(1, Interlocked.Increment(ref hits));
      // Hide only the matched reference from this SELECT. PostgreSQL retains the reference and its restrictive FK.
      var parameter = command.CreateParameter();
      parameter.ParameterName = "r09_hidden_reference";
      parameter.Value = referenceId;
      command.Parameters.Add(parameter);
      command.CommandText += " AND d.id <> @r09_hidden_reference";
    }
  }
}
