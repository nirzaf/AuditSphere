using System.IO.Compression;
using System.Data.Common;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AuditDeliverablesTests
{
  private sealed class ArchiveCopyTestDbContextFactory(DbContextOptions<AuditSphereDbContext> options)
    : IAuditSphereDbContextFactory
  {
    public Task<IAuditSphereDbContext> CreateAsync(CancellationToken ct = default) =>
      Task.FromResult<IAuditSphereDbContext>(new AuditSphereDbContext(options));
  }

  private sealed class ArchiveCopyRevokeOnPermissionReadInterceptor(
    string connectionString, Guid firmId, Guid userId) : DbCommandInterceptor
  {
    public bool RevocationApplied { get; private set; }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
      DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default)
    {
      if (!RevocationApplied && command.CommandText.Contains("archive_copy_permission_observations", StringComparison.OrdinalIgnoreCase))
      {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var revoke = new NpgsqlCommand("""
          UPDATE role_grants SET revoked_at = NOW()
          WHERE firm_id = @firm_id AND user_id = @user_id AND revoked_at IS NULL
            AND role IN ('Administrator', 'Partner', 'RecordsCustodian')
          """, connection);
        revoke.Parameters.AddWithValue("firm_id", firmId);
        revoke.Parameters.AddWithValue("user_id", userId);
        if (await revoke.ExecuteNonQueryAsync(cancellationToken) < 1)
          throw new InvalidOperationException("The archive readback revoke fixture found no active custodian grant.");
        RevocationApplied = true;
      }

      return result;
    }
  }

  private sealed class ArchiveCopyInterposedOperationStore(IOperationStore inner) : IOperationStore
  {
    public Func<CancellationToken, Task>? BeforeNextComplete { get; set; }
    public Func<OperationResult?, OperationResult?>? RewriteNextRemoteResult { get; set; }

    public Task<CommandResult<Guid>> EnqueueAsync(IAuditSphereDbContext db, OperationRequest request,
      IOperationHandler handler, CancellationToken ct) => inner.EnqueueAsync(db, request, handler, ct);

    public Task<DurableOperation?> ClaimAsync(WorkerOptions options, IReadOnlyList<OperationDefinition> definitions,
      string owner, bool reconciliation, CancellationToken ct) =>
      inner.ClaimAsync(options, definitions, owner, reconciliation, ct);

    public Task<bool> ValidateAsync(DurableOperation operation, WorkerOptions options, IOperationHandler handler,
      CancellationToken ct) => inner.ValidateAsync(operation, options, handler, ct);

    public Task<bool> RenewAsync(DurableOperation operation, WorkerOptions options, CancellationToken ct) =>
      inner.RenewAsync(operation, options, ct);

    public Task<bool> TransitionAsync(DurableOperation operation, WorkerOptions options, OperationState next,
      string? error, TimeSpan? delay, CancellationToken ct) =>
      inner.TransitionAsync(operation, options, next, error, delay, ct);

    public async Task<bool> CompleteAsync(DurableOperation operation, WorkerOptions options,
      IOperationHandler handler, OperationResult? remote, CancellationToken ct)
    {
      var interpose = BeforeNextComplete;
      BeforeNextComplete = null;
      if (interpose is not null) await interpose(ct);
      var rewrite = RewriteNextRemoteResult;
      RewriteNextRemoteResult = null;
      if (rewrite is not null) remote = rewrite(remote);
      return await inner.CompleteAsync(operation, options, handler, remote, ct);
    }

    public Task<int> ReapAsync(WorkerOptions options, CancellationToken ct) => inner.ReapAsync(options, ct);
  }

  private sealed class FakeArchiveCopyProvider(byte[] snapshotBytes) : IClientSharePointArchiveCopyProvider
  {
    private sealed record StoredFile(byte[] Content, string RelativePath, string ItemId, string VersionId);
    private readonly Dictionary<string, StoredFile> files = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, int> permissionReadsByTarget = [];
    public readonly List<string> IdempotencyKeys = [];
    public readonly List<string> SnapshotVersions = [];
    public bool LoseAfterNextCreate { get; set; }
    public bool CorruptNextReadback { get; set; }
    public bool StaffWrite { get; set; }
    public bool StaffWriteOnFinalRead { get; set; }
    public bool WrongSnapshotVersion { get; set; }
    public int StoredFileCount => files.Count;

    public async Task<ArchiveCopySnapshotContent> ReadExactSnapshotAsync(
      ArchiveCopySnapshotReadRequest request, CancellationToken ct)
    {
      if (request.RecheckAuthorization is not null) await request.RecheckAuthorization(ct);
      SnapshotVersions.Add(request.VersionId);
      return new(WrongSnapshotVersion ? request.VersionId + "-stale" : request.VersionId, snapshotBytes.ToArray());
    }

    public async Task<ArchiveCopyFileReadback> CreateOrReadExactFileAsync(
      ArchiveCopyFileWriteRequest request, CancellationToken ct)
    {
      if (request.RecheckAuthorization is not null) await request.RecheckAuthorization(ct);
      IdempotencyKeys.Add(request.IdempotencyKey);
      if (!files.TryGetValue(request.IdempotencyKey, out var stored))
      {
        stored = new StoredFile(request.Content.ToArray(), request.RelativePath,
          "archive-item-" + request.SourceKeySha256[..20], "version-1");
        files.Add(request.IdempotencyKey, stored);
      }
      else if (stored.RelativePath != request.RelativePath || !stored.Content.AsSpan().SequenceEqual(request.Content))
      {
        throw new OperationBlockedException("fake-provider-exact-key-conflict");
      }

      if (LoseAfterNextCreate)
      {
        LoseAfterNextCreate = false;
        throw new HttpRequestException("Synthetic response loss after create; reconciliation must read the stable key.");
      }

      var readback = stored.Content.ToArray();
      if (CorruptNextReadback)
      {
        CorruptNextReadback = false;
        readback[0] ^= 0x01;
      }
      return new(request.Target.TenantId, request.Target.SiteId, request.Target.DriveId,
        request.Target.LibraryRootItemId, stored.RelativePath, stored.ItemId, stored.VersionId,
        readback, DateTimeOffset.UtcNow);
    }

    public async Task<ArchiveCopyPermissionReadback> ReadTargetPermissionsAsync(
      ArchiveCopyPermissionReadRequest request, CancellationToken ct)
    {
      if (request.RecheckAuthorization is not null) await request.RecheckAuthorization(ct);
      var readIndex = permissionReadsByTarget.GetValueOrDefault(request.Target.TargetRevisionId);
      permissionReadsByTarget[request.Target.TargetRevisionId] = readIndex + 1;
      var role = StaffWrite || (StaffWriteOnFinalRead && readIndex > 0) ? "WRITE" : "READ";
      if (StaffWriteOnFinalRead && readIndex > 0) StaffWriteOnFinalRead = false;
      return new([new("SHAREPOINT_GROUP", request.Target.StaffGroupId, role)], true,
        request.Target.AuthorizedStaffSha256, request.Target.TenantId, request.Target.SiteId,
        request.Target.DriveId, request.Target.LibraryRootItemId, DateTimeOffset.UtcNow);
    }
  }

  [Fact]
  public async Task SignedRepresentation_IsVersionBound_Idempotent_Immutable_AndScoped()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var client = w.A("client", "ClientUser"); var partner = w.A("partner", "Partner"); var manager = w.A("manager", "Manager");
    await using var db = new AuditSphereDbContext(pg.Options);
    var letterId = (await AuditDeliverableService.GenerateReportAsync(db, manager, w.EngagementId, DeliverableKinds.RepresentationLetter)).Value!.DeliverableId!.Value;
    var letter = await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == letterId);
    var pdf = CompletionTestFixtures.SignedPdf();
    Assert.Equal(ErrorCodes.GateBlocked, (await AuditDeliverableService.UploadSignedRepresentationAsync(db, client, letterId, letter.ContentSha256, "Client Director", pdf)).ErrorCode);
    var review = (await AuditDeliverableService.ShareWithClientAsync(db, manager, letterId)).Value;
    Assert.True((await AuditDeliverableService.AcknowledgeAsync(db, client, review, letter.ContentSha256)).Succeeded);
    Assert.Equal(ErrorCodes.AuditPlanning.Invalid, (await AuditDeliverableService.UploadSignedRepresentationAsync(db, client, letterId, letter.ContentSha256, "Client Director", "%PDF-broken"u8.ToArray())).ErrorCode);
    Assert.Equal(ErrorCodes.GenerationStale, (await AuditDeliverableService.UploadSignedRepresentationAsync(db, client, letterId, new string('0', 64), "Client Director", pdf)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await AuditDeliverableService.UploadSignedRepresentationAsync(db, manager, letterId, letter.ContentSha256, "Client Director", pdf)).ErrorCode);
    var scan = await AuditDeliverableService.UploadSignedRepresentationAsync(db, client, letterId, letter.ContentSha256, "Client Director", pdf);
    Assert.True(scan.Succeeded, scan.Message);
    var realScanIds = new HashSet<Guid> { scan.Value };
    Assert.Equal(scan.Value, (await AuditDeliverableService.UploadSignedRepresentationAsync(db, client, letterId, letter.ContentSha256, "Client Director", pdf)).Value);
    Assert.Equal(ErrorCodes.IdempotencyConflict, (await AuditDeliverableService.UploadSignedRepresentationAsync(db, client, letterId, letter.ContentSha256, "Different Director", pdf)).ErrorCode);
    static byte[] HistoryPdf(int revision) => AuditDeliverableRenderer.RenderPdf(new("Synthetic management", "Management-signed representation scan",
      $"TEST-SCAN-{revision:D2}", new DateOnly(2026, 12, 31), "Auditors",
      [new("Synthetic acceptance fixture", [$"Distinct retained upload fixture {revision}; the original scan remains the verified one."])]));
    for (var revision = 1; revision <= 20; revision++)
    {
      var additional = await AuditDeliverableService.UploadSignedRepresentationAsync(db, client, letterId, letter.ContentSha256,
        "Client Director", HistoryPdf(revision));
      Assert.True(additional.Succeeded, additional.Message);
      Assert.True(realScanIds.Add(additional.Value));
    }
    var row = (await AuditDeliverableService.GetSignedRepresentationAsync(db, client, scan.Value)).Value!;
    Assert.Equal(pdf, row.Content);
    Assert.Equal(Hashing.Sha256Hex(pdf), row.ContentSha256);
    Assert.Equal(ErrorCodes.ScopeDenied, (await AuditDeliverableService.VerifySignedRepresentationAsync(db, manager, row.Id, row.ContentSha256, "Reviewed all pages and the signature.")).ErrorCode);
    Assert.Equal(ErrorCodes.GenerationStale, (await AuditDeliverableService.VerifySignedRepresentationAsync(db, partner, row.Id, new string('0',64), "Reviewed all pages and the signature.")).ErrorCode);
    var verification = await AuditDeliverableService.VerifySignedRepresentationAsync(db, partner, row.Id, row.ContentSha256, "Verified executive management authority, signature and completeness.");
    Assert.True(verification.Succeeded, verification.Message);
    Assert.Equal(verification.Value, (await AuditDeliverableService.VerifySignedRepresentationAsync(db, partner, row.Id, row.ContentSha256, "Reviewed again.")).Value);
    var clientDocuments = await ClientPortalReviewQuery.GetAsync(db, client);
    Assert.True(clientDocuments.Succeeded, clientDocuments.Message);
    var clientHistory = clientDocuments.Value!.SignedLetters;
    Assert.Equal(21, clientHistory.Count);
    Assert.Equal(realScanIds.Order(), clientHistory.Select(x => x.Id).Order());
    Assert.Equal(clientHistory.OrderByDescending(x => x.UploadedAt).ThenByDescending(x => x.Id).Select(x => x.Id), clientHistory.Select(x => x.Id));
    var originalHistory = Assert.Single(clientHistory, x => x.Id == row.Id);
    Assert.Equal(row.ContentSha256, originalHistory.ContentSha256);
    Assert.True(originalHistory.Verified);
    Assert.True(originalHistory.Current);
    Assert.Equal(20, (await AuditDeliverableService.SignedRepresentationsAsync(db, manager, w.EngagementId)).Count);
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => AuditDeliverableService.SignedRepresentationsAsync(db, client, 0, w.EngagementId));
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => AuditDeliverableService.SignedRepresentationsAsync(db, client,
      AuditDeliverableService.MaxClientPortalSignedLetters + 2, w.EngagementId));
    Assert.Null(await AuditDeliverableService.CurrentClearanceAsync(db, client, w.EngagementId));
    var siblingClient = Guid.NewGuid(); var siblingEngagement = Guid.NewGuid();
    db.PracticeClients.Add(new PracticeClient { Id = siblingClient, FirmId = w.FirmId, LegalName = "Sibling B", CreatedAt = DateTimeOffset.UtcNow });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = siblingClient, FirmId = w.FirmId });
    db.Engagements.Add(new Engagement { Id = siblingEngagement, FirmId = w.FirmId, PracticeClientId = siblingClient, CreatedAt = DateTimeOffset.UtcNow });
    var outsider = NewUser(w.FirmId, "sibling", "Client");
    db.Users.Add(outsider);
    db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = outsider.Id, Role = "ClientUser", ClientId = siblingClient, EngagementId = siblingEngagement, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.U["partner"].Id });
    await db.SaveChangesAsync();
    var stranger = new ActorContext(outsider.Id, w.FirmId, outsider.SessionEpoch, ["ClientUser"]);
    Assert.Equal(ErrorCodes.ScopeDenied, (await AuditDeliverableService.GetSignedRepresentationAsync(db, stranger, row.Id)).ErrorCode);
    Assert.Empty(await AuditDeliverableService.SignedRepresentationsAsync(db, stranger, w.EngagementId));
    Assert.Equal(ErrorCodes.ScopeDenied, (await AuditDeliverableService.UploadSignedRepresentationAsync(db, stranger, letterId, letter.ContentSha256, "Sibling Director", pdf)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await AuditDeliverableService.GetSignedRepresentationAsync(db, client with { FirmId = Guid.NewGuid() }, row.Id)).ErrorCode);
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM signed_representation_letters WHERE id = {row.Id}"));
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE representation_letter_verifications SET reason = 'changed' WHERE id = {verification.Value}"));
    // A changed fact invalidates the source and the verified scan, without overwriting either.
    db.Findings.Add(new AuditSphereOps.Domain.Audit.Finding { Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId, ActorId = w.U["manager"].Id,
      FindingType = "New finding", ImpactDescription = "Additional review required", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    Assert.False(Assert.Single(await AuditDeliverableService.SignedRepresentationsAsync(db, client,
      AuditDeliverableService.MaxClientPortalSignedLetters + 1, w.EngagementId), x => x.Id == row.Id).Current);
    Assert.Equal(ErrorCodes.GenerationStale, (await AuditDeliverableService.VerifySignedRepresentationAsync(db, partner, row.Id, row.ContentSha256, "Reviewed again after changes.")).ErrorCode);

    // Projection-boundary rows only: these 80 short payloads have scoped database keys but are not actual PDFs or signed scans.
    db.SignedRepresentationLetters.AddRange(Enumerable.Range(1, 80).Select(revision =>
    {
      var content = System.Text.Encoding.UTF8.GetBytes($"projection-only-scan-{revision}");
      return new SignedRepresentationLetter
      {
        Id = Guid.NewGuid(), FirmId = row.FirmId, ClientId = row.ClientId, EngagementId = row.EngagementId,
        DeliverableId = row.DeliverableId, DeliverableSha256 = row.DeliverableSha256,
        ContentSha256 = Hashing.Sha256Hex(content), Content = content, ManagementSignatory = "Projection only",
        UploadedByUserId = client.UserId, UploadedAt = row.UploadedAt
      };
    }));
    await db.SaveChangesAsync();
    var sentinelHistory = await AuditDeliverableService.SignedRepresentationsAsync(db, client,
      AuditDeliverableService.MaxClientPortalSignedLetters + 1, w.EngagementId);
    Assert.Equal(101, sentinelHistory.Count);
    Assert.Equal(sentinelHistory.OrderByDescending(x => x.UploadedAt).ThenByDescending(x => x.Id).Select(x => x.Id),
      sentinelHistory.Select(x => x.Id));
    var overflow = await ClientPortalReviewQuery.GetAsync(db, client);
    Assert.Equal(ErrorCodes.GateBlocked, overflow.ErrorCode);
    Assert.Null(overflow.Value);

    await db.Users.Where(x => x.Id == client.UserId).ExecuteUpdateAsync(x => x.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
    Assert.False((await AuditDeliverableService.GetSignedRepresentationAsync(db, client, row.Id)).Succeeded);
    Assert.Empty(await AuditDeliverableService.SignedRepresentationsAsync(db, client, w.EngagementId));
  }

  [Fact]
  public async Task CertificateSignedReport_IsReleasedWithCertificateSignedStatements_OrNotAtAll()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var partner = w.A("partner", "Partner"); var manager = w.A("manager", "Manager");
    await CompletionBundleFixture.ReleasedFinancialPackageAsync(pg.Options, new(w.FirmId, w.ClientId, w.EngagementId, w.U));
    await CompletionBundleFixture.SeedPostedFeeAsync(pg.Options, new(w.FirmId, w.ClientId, w.EngagementId, w.U), automateBalance: true);
    var iar = await ReportReadyForSigningAsync(pg, w);
    using var firmCertificate = ReportCertificateSigningTests.TestCertificate();
    var pfx = firmCertificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12);
    var source = new ReportCertificateSigningTests.FixedSource(
      () => System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(pfx, null), required: true);
    var thumbprint = ReportCertificateSigning.ThumbprintSha256(firmCertificate);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await AuditDeliverableService.SignIndependentReportAsync(db, partner, iar, default, source)).Succeeded);
      Assert.True((await AuditDeliverableService.GenerateReportAsync(db, manager, w.EngagementId, DeliverableKinds.ManagementLetter)).Succeeded);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // The certificate must be available again to certify the statements: without it nothing is assembled.
      Assert.Equal(ErrorCodes.GateBlocked, (await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true)).ErrorCode);
      Assert.Equal(ErrorCodes.GateBlocked, (await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true, default,
        new ReportCertificateSigningTests.FixedSource(null, required: true))).ErrorCode);
      Assert.Empty(await db.CommercialDeliverableBundles.ToListAsync());

      using var otherCertificate = ReportCertificateSigningTests.TestCertificate();
      var otherPfx = otherCertificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12);
      var mismatched = new ReportCertificateSigningTests.FixedSource(
        () => System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(otherPfx, null), required: true);
      var refusedNew = await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true, default, mismatched);
      Assert.Equal(ErrorCodes.GateBlocked, refusedNew.ErrorCode); Assert.Contains("does not match", refusedNew.Message);
      Assert.Empty(await db.CommercialDeliverableBundles.ToListAsync());
      var assembled = await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true, default, source);
      Assert.True(assembled.Succeeded, assembled.Message);
      Assert.Equal(assembled.Value, (await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true, default, source)).Value);
      // A saved receipt cannot skip credential validation on a later assembly action.
      Assert.Equal(ErrorCodes.GateBlocked, (await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true)).ErrorCode);
      var wrongCertificate = await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true, default, mismatched);
      Assert.Equal(ErrorCodes.GateBlocked, wrongCertificate.ErrorCode);
      Assert.Contains("does not match", wrongCertificate.Message);
      Assert.Single(await db.CommercialDeliverableBundles.ToListAsync());
      var row = await db.CommercialDeliverableBundles.AsNoTracking().SingleAsync(x => x.Id == assembled.Value);
      using var archive = new ZipArchive(new MemoryStream(row.Content), ZipArchiveMode.Read);
      foreach (var path in new[] { "01_Report_and_statements/Independent_auditors_report.pdf", "01_Report_and_statements/Certified_financial_statements.pdf" })
      {
        using var bytes = new MemoryStream();
        using (var stream = archive.GetEntry(path)!.Open()) await stream.CopyToAsync(bytes);
        var check = ReportSignatureVerifier.Verify(bytes.ToArray());
        Assert.Equal((ReportSignatureStates.Valid, thumbprint), (check.State, check.ThumbprintSha256));
      }
      using var manifest = System.Text.Json.JsonDocument.Parse(row.ManifestJson);
      Assert.Equal(ReportSignatureKinds.Certificate, manifest.RootElement.GetProperty("ReportSignatureKind").GetString());
      Assert.Equal(thumbprint, manifest.RootElement.GetProperty("ReportCertificateThumbprintSha256").GetString());
    }
  }

  [Fact]
  public async Task FivePartBundle_RequiresReviewedReleaseAndPostedBalance_IsIdempotent_AndClientScoped()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var partner = w.A("partner", "Partner"); var manager = w.A("manager", "Manager"); var client = w.A("client", "ClientUser");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var blocked = await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true);
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);
      Assert.Contains("financial package", blocked.Message);
      Assert.Contains("balance invoice", blocked.Message);
      Assert.Empty(await db.CommercialDeliverableBundles.ToListAsync());
    }
    var (packageId, releaseId) = await CompletionBundleFixture.ReleasedFinancialPackageAsync(pg.Options, new(w.FirmId, w.ClientId, w.EngagementId, w.U));
    await CompletionBundleFixture.SeedPostedFeeAsync(pg.Options, new(w.FirmId, w.ClientId, w.EngagementId, w.U), automateBalance: true);
    var signedId = await SignedReportAsync(pg, w);
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await AuditDeliverableService.GenerateReportAsync(db, manager, w.EngagementId, DeliverableKinds.ManagementLetter)).Succeeded);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(ErrorCodes.ScopeDenied, (await AuditDeliverableService.AssembleBundleAsync(db, manager, w.EngagementId, true)).ErrorCode);
      Assert.Equal(ErrorCodes.GateBlocked, (await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, false)).ErrorCode);
      // ADR-0016: once certificate signing is required, an image-only report cannot be released in a bundle.
      var imageOnly = await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true, default,
        new ReportCertificateSigningTests.FixedSource(null, required: true));
      Assert.Equal(ErrorCodes.GateBlocked, imageOnly.ErrorCode);
      Assert.Contains("signature image only", imageOnly.Message);
      var assembled = await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true);
      Assert.True(assembled.Succeeded, assembled.Message);
      var again = await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true);
      Assert.Equal(assembled.Value, again.Value);

      // Synthetic preapproved profile fixture only; this exercises exact bundle inclusion and
      // does not perform a live action or record a real policy approval.
      var recordsProfile = new RecordsProfile
      {
        Id = Guid.CreateVersion7(), FirmId = w.FirmId, ProfileCode = "SYNTHETIC-ARCHIVE-TEST",
        Version = 1, RecordClass = "Synthetic test record", Jurisdiction = "TEST", ServiceRoute = "TEST",
        RetentionTrigger = "TEST_FIXTURE", ProtectionMode = "TEST_FIXTURE", LabelId = "test-only",
        LegalHoldBehavior = "TEST_FIXTURE", AmendmentRoute = "TEST_FIXTURE", DispositionOwner = "Test fixture",
        BackupRequirements = "Synthetic test fixture only", CreatedByUserId = partner.UserId,
        Approved = true, ApprovedAt = DateTimeOffset.UtcNow, ApprovedByUserId = partner.UserId,
        CreatedAt = DateTimeOffset.UtcNow
      };
      var archiveId = Guid.CreateVersion7();
      db.RecordsProfiles.Add(recordsProfile);
      db.Archives.Add(new Archive
      {
        Id = archiveId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
        ProfileId = recordsProfile.ProfileCode, ProfileVersion = recordsProfile.Version,
        Status = ArchiveStates.Issued, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var included = await RecordsArchiveService.BuildManifestWithCommercialBundleAsync(
        db, partner, archiveId, assembled.Value!);
      Assert.True(included.Succeeded, included.Message);
      Assert.Equal(15, included.Value!.EntryCount); // structured export + bundle + inner manifest + ten parts + two signed parts
      var manifestEntries = await db.ArchiveManifestEntries.AsNoTracking()
        .Where(x => x.ArchiveManifestId == included.Value.ManifestId).ToListAsync();
      var bundle = await db.CommercialDeliverableBundles.AsNoTracking().SingleAsync(x => x.Id == assembled.Value);
      Assert.Equal(bundle.ContentSha256, Assert.Single(manifestEntries, x => x.EntryKind == "COMMERCIAL_BUNDLE_ASSEMBLY").ContentHash);
      Assert.Equal(bundle.ManifestSha256, Assert.Single(manifestEntries, x => x.EntryKind == "COMMERCIAL_BUNDLE_MANIFEST").ContentHash);
      var bundleParts = manifestEntries.Where(x => x.EntryKind == "COMMERCIAL_BUNDLE_PART").ToList();
      Assert.Equal(10, bundleParts.Count);
      Assert.All(bundleParts, entry => Assert.True(entry.Required));
      Assert.Single(manifestEntries, x => x.EntryKind == "SIGNED_AUDIT_REPORT" && x.SourceId == bundle.SignedReportId);
      Assert.Single(manifestEntries, x => x.EntryKind == "SIGNED_MANAGEMENT_REPRESENTATION" &&
        x.SourceId == bundle.SignedRepresentationLetterId);
      Assert.All(manifestEntries.Where(x => x.EntryKind.StartsWith("COMMERCIAL_BUNDLE", StringComparison.Ordinal) ||
        x.EntryKind.StartsWith("SIGNED_", StringComparison.Ordinal)), entry =>
      {
        Assert.Contains("ASSEMBLED_REFERENCE", entry.MetadataJson);
        Assert.Contains("\"commercialIssuanceId\":null", entry.MetadataJson);
      });

      var managementLetterId = await db.CommercialDeliverableBundles.AsNoTracking()
        .Where(x => x.FirmId == w.FirmId && x.Id == assembled.Value).Select(x => x.ManagementLetterId).SingleAsync();
      var review = await AuditDeliverableService.ShareWithClientAsync(db, manager, managementLetterId);
      Assert.True(review.Succeeded, review.Message);
      Assert.Null(await db.ClientDeliverableReviews.AsNoTracking().Where(x => x.Id == review.Value).Select(x => x.AcknowledgedAt).SingleAsync());
      var previousBundleId = assembled.Value;
      for (var revision = 1; revision <= 10; revision++)
      {
        var comment = await AuditDeliverableService.CommentAsync(db, manager, review.Value, Guid.NewGuid(), $"Synthetic staff comment {revision}");
        Assert.True(comment.Succeeded, comment.Message);
        var resolved = await AuditDeliverableService.ResolveCommentAsync(db, partner, comment.Value, $"Reviewed and resolved for snapshot {revision}.");
        Assert.True(resolved.Succeeded, resolved.Message);
        var nextBundle = await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true);
        Assert.True(nextBundle.Succeeded, nextBundle.Message);
        Assert.NotEqual(previousBundleId, nextBundle.Value);
        previousBundleId = nextBundle.Value;
      }
      var policyChanged = await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true, default,
        new ReportCertificateSigningTests.FixedSource(null, required: true));
      Assert.Equal(ErrorCodes.GateBlocked, policyChanged.ErrorCode);
      Assert.Contains("signature image only", policyChanged.Message);
      Assert.Equal(11, await db.CommercialDeliverableBundles.CountAsync());
      var row = (await AuditDeliverableService.GetBundleAsync(db, client, assembled.Value)).Value!;
      Assert.Equal(signedId, row.SignedReportId);
      Assert.Equal(releaseId, row.FinancialPackageReleaseId);
      Assert.Equal(Hashing.Sha256Hex(row.Content), row.ContentSha256);
      Assert.Equal(Hashing.Sha256Hex(row.ManifestJson), row.ManifestSha256);

      var freeze = await db.EngagementFileFreezes.AsNoTracking()
        .SingleAsync(x => x.FirmId == w.FirmId && x.EngagementId == w.EngagementId);
      Assert.Equal(signedId, freeze.ReportDeliverableId);
      Assert.Equal(FileFreezeStates.Scheduled, freeze.State);

      var lifecycle = await EngagementLifecycleQuery.GetAsync(db, partner, w.EngagementId);
      Assert.True(lifecycle.Succeeded, lifecycle.Message);
      Assert.Equal(CanonicalEngagementStages.ComplianceCountdown, lifecycle.Value!.Summary.CanonicalStage);
      Assert.Equal(10, lifecycle.Value.Summary.StageIndex);
      Assert.Equal(FileFreezeStates.Scheduled, lifecycle.Value.Summary.LocalArchiveState);
      Assert.False(lifecycle.Value.Summary.IsArchived);
      Assert.Empty(lifecycle.Value.Summary.BlockedReasons);

      using var archive = new ZipArchive(new MemoryStream(row.Content), ZipArchiveMode.Read);
      Assert.Equal(new[] {
        "01_Report_and_statements/Certified_financial_statements.pdf", "01_Report_and_statements/Independent_auditors_report.pdf",
        "01_Report_and_statements/Selected_financial_statements.docx", "01_Report_and_statements/Selected_financial_statements.pdf",
        "01_Report_and_statements/Selected_financial_statements.xlsx",
        "02_Management_letter/Management_letter.docx", "03_Representation_letter/Management_signed_letter.pdf",
        "03_Representation_letter/Representation_letter.docx", "04_Management_correspondence/Audit_trail.json",
        "05_Final_balance/Balance_fee_note.pdf", "manifest.json"
      }, archive.Entries.Select(x => x.FullName).OrderBy(x => x, StringComparer.Ordinal).ToArray());
      Assert.Equal(5, archive.Entries.Where(x => x.FullName != "manifest.json").Select(x => x.FullName.Split('/')[0]).Distinct().Count());
      Assert.Contains(archive.Entries, x => x.FullName.EndsWith("Management_signed_letter.pdf"));
      // STE J40: the management correspondence trail is one of the five parts, with its confirmations and communications.
      using (var trailStream = archive.GetEntry("04_Management_correspondence/Audit_trail.json")!.Open())
      using (var trail = await System.Text.Json.JsonDocument.ParseAsync(trailStream))
      {
        Assert.True(trail.RootElement.TryGetProperty("Confirmations", out _));
        Assert.True(trail.RootElement.TryGetProperty("Communications", out _));
      }
      using (var embeddedManifest = new StreamReader(archive.GetEntry("manifest.json")!.Open()))
        Assert.Equal(row.ManifestJson, await embeddedManifest.ReadToEndAsync());
      using var manifest = System.Text.Json.JsonDocument.Parse(row.ManifestJson);
      Assert.Equal(row.SignedReportId, manifest.RootElement.GetProperty("SignedReport").GetGuid());
      Assert.Equal(row.FinancialPackageReleaseId, manifest.RootElement.GetProperty("FinancialPackageRelease").GetGuid());
      Assert.Equal(row.FinancialPackageArtifactId, manifest.RootElement.GetProperty("FinancialPackageArtifact").GetGuid());
      Assert.NotNull(row.FinancialStatementCompositionId);
      Assert.Equal(row.FinancialStatementCompositionId, manifest.RootElement.GetProperty("SelectedStatementComposition").GetProperty("Id").GetGuid());
      Assert.Equal(row.CompositionManifestDigest, manifest.RootElement.GetProperty("SelectedStatementComposition").GetProperty("ManifestDigest").GetString());
      Assert.Equal(row.BalanceInvoiceId, manifest.RootElement.GetProperty("BalanceInvoice").GetGuid());
      foreach (var item in manifest.RootElement.GetProperty("Parts").EnumerateArray())
      {
        var entry = archive.GetEntry(item.GetProperty("Path").GetString()!)!;
        using var bytes = new MemoryStream(); using var stream = entry.Open(); await stream.CopyToAsync(bytes);
        Assert.Equal(item.GetProperty("Sha256").GetString(), Hashing.Sha256Hex(bytes.ToArray()));
        Assert.Equal(item.GetProperty("Bytes").GetInt64(), bytes.Length);
        if (entry.FullName.EndsWith(".docx", StringComparison.Ordinal))
        {
          using var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(new MemoryStream(bytes.ToArray()), false);
          var body = document.MainDocumentPart?.Document?.Body;
          Assert.NotNull(body); Assert.False(string.IsNullOrWhiteSpace(body.InnerText));
        }
      }
      using var certified = new MemoryStream();
      using (var stream = archive.GetEntry("01_Report_and_statements/Certified_financial_statements.pdf")!.Open()) await stream.CopyToAsync(certified);
      using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(certified, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
      Assert.True(pdf.PageCount > 1);
      Assert.Contains(pdf.Internals.GetAllObjects(), x => x is PdfSharp.Pdf.PdfDictionary d && d.Elements.GetName("/Subtype") == "/Image");
      Assert.Equal(11, await db.CommercialDeliverableBundles.CountAsync());
      var clientPortalReview = await ClientPortalReviewQuery.GetAsync(db, client);
      Assert.True(clientPortalReview.Succeeded, clientPortalReview.Message);
      var clientBundles = clientPortalReview.Value!.Bundles;
      Assert.Equal(11, clientBundles.Count);
      var clientBundle = Assert.Single(clientBundles, x => x.Id == row.Id);
      var contextEngagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == w.EngagementId);
      var contextClient = await db.PracticeClients.AsNoTracking().SingleAsync(x => x.Id == contextEngagement.PracticeClientId);
      Assert.Equal(new PortalDocumentContext(contextClient.LegalName, contextEngagement.ServiceRoute,
        contextEngagement.PeriodStart, contextEngagement.PeriodEnd), clientBundle.Context);
      Assert.Equal(row.ContentSha256, clientBundle.Sha256);
      Assert.Equal(row.ManifestSha256, clientBundle.ManifestSha256);
      Assert.Null(clientBundle.AcknowledgedAt);
      Assert.Equal(clientBundles.OrderByDescending(x => x.AssembledAt).ThenByDescending(x => x.Id).Select(x => x.Id),
        clientBundles.Select(x => x.Id));

      // Concurrent retries for the same client user and exact bundle return one immutable receipt.
      await using var receiptDbA = new AuditSphereDbContext(pg.Options);
      await using var receiptDbB = new AuditSphereDbContext(pg.Options);
      var acknowledgements = await Task.WhenAll(
        ClientBundleAcknowledgementCommands.AcknowledgeAsync(receiptDbA, client, row.Id, row.ContentSha256, row.ManifestSha256, true),
        ClientBundleAcknowledgementCommands.AcknowledgeAsync(receiptDbB, client, row.Id, row.ContentSha256, row.ManifestSha256, true));
      Assert.All(acknowledgements, result => Assert.True(result.Succeeded, result.Message));
      Assert.Equal(acknowledgements[0].Value, acknowledgements[1].Value);
      Assert.Single(await db.ClientBundleAcknowledgements.AsNoTracking().Where(x => x.BundleId == row.Id).ToListAsync());
      Assert.Equal(ErrorCodes.GenerationStale, (await ClientBundleAcknowledgementCommands.AcknowledgeAsync(db, client, row.Id,
        new string('0', 64), row.ManifestSha256, true)).ErrorCode);
      Assert.Equal("receipt.confirmation-required", (await ClientBundleAcknowledgementCommands.AcknowledgeAsync(db, client, row.Id,
        row.ContentSha256, row.ManifestSha256, false)).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied, (await ClientBundleAcknowledgementCommands.AcknowledgeAsync(db, partner, row.Id,
        row.ContentSha256, row.ManifestSha256, true)).ErrorCode);
      var acknowledgedView = Assert.Single(await AuditDeliverableService.ClientBundlesAsync(db, client, w.EngagementId), x => x.Id == row.Id);
      Assert.NotNull(acknowledgedView.AcknowledgedAt);
      var receipt = await db.ClientBundleAcknowledgements.AsNoTracking().SingleAsync(x => x.BundleId == row.Id);
      Assert.Equal(client.UserId, receipt.AcknowledgedByUserId);
      Assert.Equal(row.ContentSha256, receipt.ContentSha256);
      Assert.Equal(row.ManifestSha256, receipt.ManifestSha256);
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE client_bundle_acknowledgements SET content_sha256 = {new string('0', 64)} WHERE id = {receipt.Id}"));
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM client_bundle_acknowledgements WHERE id = {receipt.Id}"));
      Assert.Equal(ErrorCodes.ProtectedState, (await AuditSphereOps.Application.Documents.ClientPortalService.RequireUploadWindowAsync(db, client, w.EngagementId)).ErrorCode);
      var outsider = NewUser(w.FirmId, "outside", "Client");
      db.Users.Add(outsider); db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = outsider.Id, Role = "ClientUser", ClientId = w.ClientId, EngagementId = null,
        GrantedAt = DateTimeOffset.UtcNow, RevokedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.U["partner"].Id }); await db.SaveChangesAsync();
      Assert.Equal(ErrorCodes.ScopeDenied, (await AuditDeliverableService.GetBundleAsync(db, new(outsider.Id, w.FirmId, outsider.SessionEpoch, ["ClientUser"]), row.Id)).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied, (await ClientBundleAcknowledgementCommands.AcknowledgeAsync(db,
        new(outsider.Id, w.FirmId, outsider.SessionEpoch, ["ClientUser"]), row.Id, row.ContentSha256, row.ManifestSha256, true)).ErrorCode);
      Assert.False((await AuditDeliverableService.GetBundleAsync(db, client with { FirmId = Guid.NewGuid() }, row.Id)).Succeeded);
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM commercial_deliverable_bundles WHERE id = {row.Id}"));

      // Projection-boundary fixture only: add 90 bounded view rows reusing the real bundle's exact proof foreign keys.
      // These compact rows exercise the aggregate 101st-item gate; they do not claim to be independently assembled bundles.
      var projectionManifest = "{}";
      var projectionContent = new byte[] { 0x50 };
      db.CommercialDeliverableBundles.AddRange(Enumerable.Range(1, 90).Select(revision => new CommercialDeliverableBundle
      {
        Id = Guid.NewGuid(), FirmId = row.FirmId, ClientId = row.ClientId, EngagementId = row.EngagementId,
        SignedReportId = row.SignedReportId, FinancialPackageReleaseId = row.FinancialPackageReleaseId,
        FinancialPackageArtifactId = row.FinancialPackageArtifactId, FinancialStatementCompositionId = row.FinancialStatementCompositionId,
        CompositionManifestDigest = row.CompositionManifestDigest, ManagementLetterId = row.ManagementLetterId,
        SignedRepresentationLetterId = row.SignedRepresentationLetterId, BalanceInvoiceId = row.BalanceInvoiceId,
        SourceDigest = Hashing.Sha256Hex($"client-bundle-projection-boundary-{revision}"), ManifestJson = projectionManifest,
        ManifestSha256 = Hashing.Sha256Hex(projectionManifest), Content = projectionContent,
        ContentSha256 = Hashing.Sha256Hex(projectionContent), AssembledByUserId = row.AssembledByUserId,
        AssembledAt = row.AssembledAt.AddTicks(revision)
      }));
      await db.SaveChangesAsync();
      var overflow = await ClientPortalReviewQuery.GetAsync(db, client);
      Assert.Equal(ErrorCodes.GateBlocked, overflow.ErrorCode);
      Assert.Null(overflow.Value);
    }
  }

  [Trait("CompletionUnit", "WU-023")]
  [Fact]
  public async Task ArchiveCopyDurableHandler_ReconcilesUnknownAndPublishesOnlyExactReadbackEvidence()
  {
    const string sharedArchiveMigrationId = "20261023042015_ArchiveCopySharedSelectedResource";
    await using var pg = await PgTestSchema.CreateAsync();
    await using (var migrationDb = new AuditSphereDbContext(pg.Options))
    {
      var migrations = migrationDb.Database.GetMigrations().ToArray();
      var archiveCopyMigration = Array.FindIndex(migrations,
        x => x.EndsWith("_ArchiveCopyClientScopeAndReadback", StringComparison.Ordinal));
      var sharedResourceMigration = Array.FindIndex(migrations,
        x => x.EndsWith("_ArchiveCopySharedSelectedResource", StringComparison.Ordinal));
      Assert.Equal(sharedArchiveMigrationId, migrations[sharedResourceMigration]);
      Assert.Equal(archiveCopyMigration + 1, sharedResourceMigration);
      var migrator = migrationDb.GetService<IMigrator>();
      await migrator.MigrateAsync(migrations[archiveCopyMigration - 1]);
      await migrator.MigrateAsync(migrations[archiveCopyMigration]);
      await migrator.MigrateAsync(migrations[sharedResourceMigration]);
      Assert.Equal(0, await migrationDb.ArchiveCopyTargetRevisions.CountAsync());
      await migrator.MigrateAsync(migrations[sharedResourceMigration - 1]);
      Assert.DoesNotContain(sharedArchiveMigrationId, await migrationDb.Database.GetAppliedMigrationsAsync());
      await migrator.MigrateAsync(migrations[sharedResourceMigration]);
      Assert.Contains(sharedArchiveMigrationId, await migrationDb.Database.GetAppliedMigrationsAsync());
      await migrator.MigrateAsync();
    }

    var w = await SeedAsync(pg);
    var partner = w.A("partner", "Partner");
    var checkpointRoot = Path.Combine(Path.GetTempPath(), "auditsphere-archive-copy-checkpoint", Guid.NewGuid().ToString("N"));
    try
    {
      var bundleScope = new CompletionBundleScope(w.FirmId, w.ClientId, w.EngagementId, w.U);
      var (packageId, releaseId) = await CompletionBundleFixture.ReleasedFinancialPackageAsync(
        pg.Options, bundleScope, checkpointRoot);
      await CompletionBundleFixture.SeedPostedFeeAsync(pg.Options, bundleScope, automateBalance: true);
      _ = await SignedReportAsync(pg, w);
      await using (var db = new AuditSphereDbContext(pg.Options))
        Assert.True((await AuditDeliverableService.GenerateReportAsync(db, w.A("manager", "Manager"),
          w.EngagementId, DeliverableKinds.ManagementLetter)).Succeeded);
      Guid bundleId;
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var assembled = await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true);
        Assert.True(assembled.Succeeded, assembled.Message);
        bundleId = assembled.Value!;
      }

      var archiveId = Guid.CreateVersion7();
      var profile = new RecordsProfile
      {
        Id = Guid.CreateVersion7(), FirmId = w.FirmId, ProfileCode = "SYNTHETIC-ARCHIVE-COPY-TEST",
        Version = 1, RecordClass = "Synthetic protocol fixture", Jurisdiction = "TEST", ServiceRoute = "TEST_FIXTURE",
        RetentionTrigger = "TEST_FIXTURE", RetentionDurationDays = null, ProtectionMode = "TEST_FIXTURE",
        LabelId = "test-only", LegalHoldBehavior = "TEST_FIXTURE", AmendmentRoute = "TEST_FIXTURE",
        DispositionOwner = "Test fixture", BackupRequirements = "Synthetic fixture only",
        CreatedByUserId = partner.UserId, Approved = true, ApprovedByUserId = partner.UserId,
        ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      };
      var snapshotBytes = System.Text.Encoding.UTF8.GetBytes("synthetic source bytes bound to version 7");
      var snapshotId = Guid.CreateVersion7();
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        db.RecordsProfiles.Add(profile);
        db.Archives.Add(new Archive
        {
          Id = archiveId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
          ProfileId = profile.ProfileCode, ProfileVersion = profile.Version,
          Status = ArchiveStates.Issued, CreatedAt = DateTimeOffset.UtcNow
        });
        var bindingId = Guid.CreateVersion7();
        var referenceId = Guid.CreateVersion7();
        db.RepositoryBindings.Add(new RepositoryBinding
        {
          Id = bindingId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
          TenantId = "tenant-del", SiteId = "source-site", DriveId = "source-drive", RootFolderId = "source-root",
          Classification = "CLIENT_CONFIDENTIAL", DesiredAccess = "READ_ONLY", ObservedAccess = "READ_ONLY",
          CapabilityProfile = "SELECTED_SITE", CreatedAt = DateTimeOffset.UtcNow
        });
        db.DocumentReferences.Add(new DocumentReference
        {
          Id = referenceId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
          RepositoryBindingId = bindingId, Provider = "SharePoint", DriveId = "source-drive", ItemId = "source-item",
          Path = "evidence/source.pdf", Purpose = "Evidence", CreatedAt = DateTimeOffset.UtcNow
        });
        db.DocumentSnapshots.Add(new DocumentSnapshot
        {
          Id = snapshotId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
          DocumentReferenceId = referenceId, DriveId = "source-drive", ItemId = "source-item",
          VersionId = "source-version-7", Sha256Hex = Hashing.Sha256Hex(snapshotBytes),
          ByteCount = snapshotBytes.LongLength, CapturedBy = "synthetic-review-fixture", CapturedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var included = await RecordsArchiveService.BuildManifestWithCommercialBundleAsync(db, partner, archiveId, bundleId);
        Assert.True(included.Succeeded, included.Message);
        Assert.True((await RecordsArchiveService.ReviewManifestAsync(db, partner, archiveId)).Succeeded);
        Assert.Equal(16, included.Value!.EntryCount);
      }

      const string tenantId = "tenant-del";
      const string staffGroupId = "42";
      var clientSiteId = Guid.CreateVersion7();
      var connectionId = Guid.CreateVersion7();
      var selectedDraftId = Guid.CreateVersion7();
      var selectedVerificationId = Guid.CreateVersion7();
      var consentAttemptId = Guid.CreateVersion7();
      var selectedSiteUrl = "https://example.sharepoint.com/sites/shared-archive-copy-test";
      var selectedSiteId = $"example.sharepoint.com,{Guid.CreateVersion7():D},{Guid.CreateVersion7():D}";
      const string selectedArchiveDriveId = "shared-archive-drive";
      const string selectedArchiveRootId = "shared-library-root";
      var selectedProofObservedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var assignedUserIds = await db.EngagementStaffAssignments.AsNoTracking()
          .Where(x => x.FirmId == w.FirmId && x.ClientId == w.ClientId && x.EngagementId == w.EngagementId && x.RevokedAt == null)
          .Select(x => x.UserId).Distinct().ToListAsync();
        var assignedUsers = await db.Users.Where(x => x.FirmId == w.FirmId && assignedUserIds.Contains(x.Id)).ToListAsync();
        foreach (var user in assignedUsers)
        {
          user.Subject = Guid.CreateVersion7().ToString("D");
          user.TenantId = tenantId;
        }
        await db.SaveChangesAsync();
        var staff = await ClientSharePointSites.DesiredStaffAsync(db, w.FirmId, w.ClientId, tenantId);
        Assert.NotEmpty(staff);
        var staffDigest = ClientSharePointSites.Digest(staff);
        var now = DateTimeOffset.UtcNow;
        db.Microsoft365ConnectionRevisions.Add(new Microsoft365ConnectionRevision
        {
          Id = connectionId, FirmId = w.FirmId, Revision = 1, TenantId = tenantId,
          LoginClientIdReference = "test-only", RuntimeCredentialReference = "test-only",
          State = Microsoft365RevisionStates.Active, ConsentState = "VERIFIED",
          CreatedByUserId = partner.UserId, ApprovedByUserId = partner.UserId,
          CreatedAt = now, VerifiedAt = now, ActivatedAt = now
        });
        db.ClientSharePointSites.Add(new ClientSharePointSite
        {
          Id = clientSiteId, FirmId = w.FirmId, ClientId = w.ClientId, ConnectionRevisionId = connectionId,
          RequestedByUserId = partner.UserId, TenantId = tenantId,
          RequestedUrl = "https://example.sharepoint.com/sites/archive-copy-test", Title = "Synthetic client site",
          OwnershipMarker = $"AuditSphere test client {w.ClientId:D}", State = "READY",
          SiteId = "target-site", DriveId = "working-drive", RootItemId = "working-root", StaffGroupId = staffGroupId,
          MembershipState = "VERIFIED", MemberObjectIdsJson = JsonSerializer.Serialize(staff), DesiredDigest = staffDigest,
          Reason = "Synthetic test fixture", CreatedAt = now, VerifiedAt = now, LastMembershipSyncAt = now
        });

        var partnerUser = await db.Users.AsNoTracking().SingleAsync(x => x.FirmId == w.FirmId && x.Id == partner.UserId);
        var setupSessionId = Guid.CreateVersion7();
        db.Microsoft365SetupSessions.Add(new Microsoft365SetupSession
        {
          Id = setupSessionId, FirmId = w.FirmId, InstallationId = "archive-copy-" + Guid.CreateVersion7().ToString("N"),
          BootstrapProofHash = new string('a', 64), CapabilityHash = new string('b', 64),
          State = Microsoft365SetupStates.Active, Revision = 1, ClaimedByUserId = partner.UserId,
          ClaimedAt = selectedProofObservedAt.AddMinutes(-10), ExpiresAt = selectedProofObservedAt.AddDays(1)
        });
        db.Microsoft365SetupDrafts.Add(new Microsoft365SetupDraft
        {
          Id = selectedDraftId, FirmId = w.FirmId, SetupSessionId = setupSessionId, Revision = 1,
          State = Microsoft365RevisionStates.Active, ExpectedTenantId = tenantId,
          SiteUrl = selectedSiteUrl, SiteId = selectedSiteId, DriveId = selectedArchiveDriveId,
          RootFolderId = selectedArchiveRootId, ConnectionRevisionId = connectionId,
          CreatedAt = selectedProofObservedAt.AddMinutes(-10), UpdatedAt = selectedProofObservedAt
        });
        db.TenantConsentAttempts.Add(new TenantConsentAttempt
        {
          Id = consentAttemptId, FirmId = w.FirmId, SetupDraftId = selectedDraftId,
          InitiatedByUserId = partner.UserId, InitiatingSessionEpoch = partnerUser.SessionEpoch,
          InitiatorObjectId = Guid.CreateVersion7().ToString("D"), ExpectedTenantId = tenantId,
          ApplicationClientId = "archive-copy-test-application",
          StateHash = Hashing.Sha256Hex("archive-copy-consent-state"),
          State = TenantConsentAttemptStates.ConsentVerified,
          CreatedAt = selectedProofObservedAt.AddMinutes(-5), ExpiresAt = selectedProofObservedAt.AddHours(1),
          ReturnedAt = selectedProofObservedAt, ReturnedTenantId = tenantId,
          ConsentingTenantId = tenantId, ConsentingObjectId = Guid.CreateVersion7().ToString("D"),
          ConsentVerifiedAt = selectedProofObservedAt
        });
        db.TenantCapabilityVerifications.Add(new TenantCapabilityVerification
        {
          Id = selectedVerificationId, FirmId = w.FirmId, ConnectionRevisionId = connectionId,
          ConsentAttemptId = consentAttemptId, TenantId = tenantId, Capability = Microsoft365Capabilities.SelectedSite,
          Permission = "Sites.Selected", State = CapabilityVerificationStates.Verified,
          DiagnosticCode = "exact-resource-and-control-denial", ObservedByUserId = partner.UserId,
          ObservedAt = selectedProofObservedAt
        });
        foreach (var (kind, resourceId) in new[]
        {
          ("SITE", selectedSiteId), ("DRIVE", selectedArchiveDriveId), ("ROOT", selectedArchiveRootId)
        })
          db.IntegrationVerificationEvidences.Add(new IntegrationVerificationEvidence
          {
            Id = Guid.CreateVersion7(), FirmId = w.FirmId, SetupDraftId = selectedDraftId,
            ConnectionRevisionId = connectionId, ResourceKind = kind, ResourceId = resourceId,
            Operation = "GRAPH_BOUNDARY_READ", IdentityReference = "test-only", Result = "PASS",
            EvidenceReference = "archive-copy-selected-resource-test:" + kind,
            ObservedAt = selectedProofObservedAt
          });
        await db.SaveChangesAsync();
      }

      var factory = new ArchiveCopyTestDbContextFactory(pg.Options);
      var operationStore = new ArchiveCopyInterposedOperationStore(new PostgresOperationStore(factory));
      var provider = new FakeArchiveCopyProvider(snapshotBytes) { LoseAfterNextCreate = true };
      var checkpointStore = new LocalAppendOnlyCheckpointStore(checkpointRoot);
      var handler = new ArchiveCopyHandler(factory, checkpointStore, provider);
      var workerOptions = new WorkerOptions(w.FirmId, EnvironmentName: "Acceptance", ExternalEffectsEnabled: true,
        Group: ArchiveCopyHandler.Group, LeaseSeconds: 30, RenewalSeconds: 10);
      var dispatcher = new OperationDispatcher(operationStore,
        new DurableOperationRegistry([handler], workerOptions), workerOptions);

      async Task<ArchiveCopyTargetRevisionView> CreateTargetAsync()
      {
        await using var db = new AuditSphereDbContext(pg.Options);
        var created = await ArchiveCopyService.CreateTargetRevisionAsync(db, partner,
          new(w.ClientId, clientSiteId, "archive-library-drive", "archive-library-root"));
        Assert.True(created.Succeeded, $"{created.ErrorCode}: {created.Message}");
        return created.Value!;
      }

      async Task<ArchiveCopyQueued> EnqueueAsync(Guid targetRevisionId)
      {
        await using var db = new AuditSphereDbContext(pg.Options);
        var queued = await ArchiveCopyService.EnqueueAsync(db, operationStore, partner,
          new(archiveId, releaseId, targetRevisionId));
        Assert.True(queued.Succeeded, $"{queued.ErrorCode}: {queued.Message}");
        return queued.Value!;
      }

      async Task<(OperationState Status, int ReceiptCount, int PermissionCount)> OperationCountsAsync(Guid operationId)
      {
        await using var db = new AuditSphereDbContext(pg.Options);
        var operation = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == operationId);
        return (operation.Status, await db.ArchiveCopyFileReceipts.CountAsync(x => x.OperationId == operationId),
          await db.ArchiveCopyPermissionObservations.CountAsync(x => x.OperationId == operationId));
      }

      var target1 = await CreateTargetAsync();
      var first = await EnqueueAsync(target1.TargetRevisionId);
      Assert.Equal(first.OperationId, (await EnqueueAsync(target1.TargetRevisionId)).OperationId);
      Assert.True(await dispatcher.ProcessNextAsync());
      Assert.Equal(OperationState.RESULT_UNCERTAIN, (await OperationCountsAsync(first.OperationId)).Status);
      await using (var db = new AuditSphereDbContext(pg.Options))
        await db.DurableOperations.Where(x => x.Id == first.OperationId)
          .ExecuteUpdateAsync(update => update.SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
      Assert.True(await dispatcher.ProcessNextAsync());
      var completed = await OperationCountsAsync(first.OperationId);
      Assert.Equal(OperationState.COMPLETED, completed.Status);
      Assert.Equal(18, completed.ReceiptCount); // checkpoint + released artifact + all 16 reviewed manifest entries
      Assert.Equal(1, completed.PermissionCount);
      Assert.Equal(provider.IdempotencyKeys[0], provider.IdempotencyKeys[1]);
      Assert.DoesNotContain("attempt", provider.IdempotencyKeys[0], StringComparison.OrdinalIgnoreCase);
      Assert.Equal(18, provider.StoredFileCount);

      ArchiveCopyEvidenceReadback evidence1;
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var readback = await ArchiveCopyService.ReadEvidenceAsync(db, partner, first.OperationId);
        Assert.True(readback.Succeeded, readback.Message);
        Assert.Equal(w.FirmId, readback.Value!.FirmId);
        Assert.Equal(w.ClientId, readback.Value.ClientId);
        Assert.Equal(w.EngagementId, readback.Value.EngagementId);
        Assert.True(readback.Succeeded, readback.Message);
        evidence1 = readback.Value!;
      }
      Assert.Equal(ArchiveCopyVerificationOutcomes.Match, evidence1.Result!.ProtocolOutcome);
      Assert.All(evidence1.FileReceipts, x => Assert.Equal(ArchiveCopyVerificationOutcomes.Match, x.VerificationOutcome));
      Assert.Equal(2, evidence1.FileReceipts.Count(x => x.ManifestEntryId is null));
      Assert.Contains(evidence1.FileReceipts, x => x.ManifestEntryId is not null && x.SourceId is null);
      Assert.All(evidence1.FileReceipts, x => Assert.Equal(2, x.AttemptToken));
      List<Guid> manifestEntryIds;
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var manifestId = await db.ArchiveManifests.AsNoTracking().Where(x => x.ArchiveId == archiveId)
          .Select(x => x.Id).SingleAsync();
        manifestEntryIds = await db.ArchiveManifestEntries.AsNoTracking()
          .Where(x => x.ArchiveManifestId == manifestId).Select(x => x.Id).ToListAsync();
      }
      Assert.Equal(manifestEntryIds.Order(), evidence1.FileReceipts.Where(x => x.ManifestEntryId.HasValue)
        .Select(x => x.ManifestEntryId!.Value).Order());

      foreach (var malformedShape in new[] { "files-null", "permissions-null", "file-null", "permissions-oversized", "future-readback" })
      {
        var target = await CreateTargetAsync();
        var queued = await EnqueueAsync(target.TargetRevisionId);
        operationStore.RewriteNextRemoteResult = remote =>
        {
          Assert.NotNull(remote);
          var body = JsonNode.Parse(remote.Identity)!;
          if (malformedShape == "files-null") body[nameof(ArchiveCopyOperationResult.Files)] = null;
          else if (malformedShape == "permissions-null") body[nameof(ArchiveCopyOperationResult.Permissions)] = null;
          else if (malformedShape == "file-null") body[nameof(ArchiveCopyOperationResult.Files)]!.AsArray()[0] = null;
          else if (malformedShape == "permissions-oversized")
            body[nameof(ArchiveCopyOperationResult.Permissions)]![nameof(ArchiveCopyPermissionResult.ObservedPermissionsJson)] = new string('x', 8193);
          else
            body[nameof(ArchiveCopyOperationResult.Files)]!.AsArray()[0]![nameof(ArchiveCopyFileResult.ReadBackAt)] =
              DateTimeOffset.UtcNow.AddHours(1).ToString("O");
          var identity = body.ToJsonString();
          return new(identity, Hashing.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(identity)));
        };
        Assert.True(await dispatcher.ProcessNextAsync());
        var malformedCounts = await OperationCountsAsync(queued.OperationId);
        Assert.Equal(OperationState.PROVIDER_BLOCKED, malformedCounts.Status);
        Assert.Equal((0, 0), (malformedCounts.ReceiptCount, malformedCounts.PermissionCount));
      }

      var target2 = await CreateTargetAsync();
      provider.CorruptNextReadback = true;
      provider.StaffWriteOnFinalRead = true;
      var mismatch = await EnqueueAsync(target2.TargetRevisionId);
      Assert.True(await dispatcher.ProcessNextAsync());
      var mismatchCounts = await OperationCountsAsync(mismatch.OperationId);
      Assert.Equal(OperationState.COMPLETED, mismatchCounts.Status);
      ArchiveCopyEvidenceReadback evidence2;
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var readback = await ArchiveCopyService.ReadEvidenceAsync(db, partner, mismatch.OperationId);
        Assert.True(readback.Succeeded, readback.Message);
        evidence2 = readback.Value!;
      }
      Assert.Equal(ArchiveCopyVerificationOutcomes.Mismatch, evidence2.Result!.ProtocolOutcome);
      Assert.Contains(evidence2.FileReceipts, x => x.VerificationOutcome == ArchiveCopyVerificationOutcomes.Mismatch);
      Assert.Equal(ArchiveCopyVerificationOutcomes.Mismatch,
        Assert.Single(evidence2.PermissionObservations).ComparisonOutcome);
      provider.StaffWrite = false;

      var target3 = await CreateTargetAsync();
      provider.WrongSnapshotVersion = true;
      var wrongVersion = await EnqueueAsync(target3.TargetRevisionId);
      Assert.True(await dispatcher.ProcessNextAsync());
      provider.WrongSnapshotVersion = false;
      var versionCounts = await OperationCountsAsync(wrongVersion.OperationId);
      Assert.Equal(OperationState.PROVIDER_BLOCKED, versionCounts.Status);
      Assert.Equal((0, 0), (versionCounts.ReceiptCount, versionCounts.PermissionCount));
      Assert.Contains("source-version-7", provider.SnapshotVersions);

      var target4 = await CreateTargetAsync();
      var staleConfiguration = await EnqueueAsync(target4.TargetRevisionId);
      operationStore.BeforeNextComplete = async ct =>
      {
        await using var db = new AuditSphereDbContext(pg.Options);
        var changed = await ArchiveCopyService.CreateTargetRevisionAsync(db, partner,
          new(w.ClientId, clientSiteId, "archive-library-drive", "archive-library-root"), ct);
        Assert.True(changed.Succeeded, changed.Message);
      };
      Assert.True(await dispatcher.ProcessNextAsync());
      var staleConfigurationCounts = await OperationCountsAsync(staleConfiguration.OperationId);
      Assert.Equal(OperationState.AUTHORIZATION_BLOCKED, staleConfigurationCounts.Status);
      Assert.Equal((0, 0), (staleConfigurationCounts.ReceiptCount, staleConfigurationCounts.PermissionCount));

      var target6 = await CreateTargetAsync();
      var staleAuthority = await EnqueueAsync(target6.TargetRevisionId);
      operationStore.BeforeNextComplete = async ct =>
      {
        await using var db = new AuditSphereDbContext(pg.Options);
        var user = await db.Users.SingleAsync(x => x.FirmId == w.FirmId && x.Id == partner.UserId, ct);
        user.SessionEpoch++;
        await db.SaveChangesAsync(ct);
      };
      Assert.True(await dispatcher.ProcessNextAsync());
      var staleAuthorityCounts = await OperationCountsAsync(staleAuthority.OperationId);
      Assert.Equal(OperationState.AUTHORIZATION_BLOCKED, staleAuthorityCounts.Status);
      Assert.Equal((0, 0), (staleAuthorityCounts.ReceiptCount, staleAuthorityCounts.PermissionCount));
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var refreshedEpoch = await db.Users.AsNoTracking()
          .Where(x => x.FirmId == w.FirmId && x.Id == partner.UserId)
          .Select(x => x.SessionEpoch).SingleAsync();
        partner = partner with { SessionEpoch = refreshedEpoch };
      }

      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var migrations = db.Database.GetMigrations().ToArray();
        var foundationIndex = Array.FindIndex(migrations,
          x => x.EndsWith("_ArchiveCopyClientScopeAndReadback", StringComparison.Ordinal));
        var sharedIndex = Array.FindIndex(migrations,
          x => x.EndsWith("_ArchiveCopySharedSelectedResource", StringComparison.Ordinal));
        Assert.Equal(sharedArchiveMigrationId, migrations[sharedIndex]);
        Assert.Equal(foundationIndex + 1, sharedIndex);
        var legacyHashes = await db.ArchiveCopyTargetRevisions.AsNoTracking()
          .Where(x => x.FirmId == w.FirmId && x.ClientId == w.ClientId)
          .Select(x => new { x.Id, x.Revision, x.ConfigurationSha256, x.ExpectedPermissionsSha256 })
          .OrderBy(x => x.Revision).ToArrayAsync();
        Assert.NotEmpty(legacyHashes);

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[foundationIndex]);
        var refusedFoundationDown = await Assert.ThrowsAnyAsync<Exception>(() =>
          migrator.MigrateAsync(migrations[foundationIndex - 1]));
        Assert.Equal("55000", Assert.IsType<PostgresException>(refusedFoundationDown.GetBaseException()).SqlState);
        await migrator.MigrateAsync(migrations[sharedIndex]);
        await migrator.MigrateAsync();
        var restoredLegacyHashes = await db.ArchiveCopyTargetRevisions.AsNoTracking()
          .Where(x => x.FirmId == w.FirmId && x.ClientId == w.ClientId)
          .Select(x => new { x.Id, x.Revision, x.ConfigurationSha256, x.ExpectedPermissionsSha256 })
          .OrderBy(x => x.Revision).ToArrayAsync();
        Assert.Equal(legacyHashes, restoredLegacyHashes);

        async Task AssertAppendOnlyAsync(string sql)
        {
          await using var tx = await db.Database.BeginTransactionAsync();
          var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
          Assert.Equal("55000", error.SqlState);
          await tx.RollbackAsync();
        }

        foreach (var sql in new[]
        {
          "UPDATE archive_copy_target_revisions SET created_at = created_at",
          "DELETE FROM archive_copy_target_revisions",
          "TRUNCATE archive_copy_target_revisions CASCADE",
          "UPDATE archive_copy_file_receipts SET read_back_at = read_back_at",
          "DELETE FROM archive_copy_file_receipts",
          "TRUNCATE archive_copy_file_receipts",
          "UPDATE archive_copy_permission_observations SET observed_at = observed_at",
          "DELETE FROM archive_copy_permission_observations",
          "TRUNCATE archive_copy_permission_observations"
        }) await AssertAppendOnlyAsync(sql);

        var refused = await Assert.ThrowsAnyAsync<Exception>(() =>
          migrator.MigrateAsync(migrations[foundationIndex - 1]));
        Assert.Equal("55000", Assert.IsType<PostgresException>(refused.GetBaseException()).SqlState);
        await migrator.MigrateAsync(migrations[sharedIndex]);
        await migrator.MigrateAsync();
      }

      ArchiveCopyTargetRevisionView sharedTargetA;
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var previousRevision = await db.ArchiveCopyTargetRevisions.AsNoTracking()
          .Where(x => x.FirmId == w.FirmId && x.ClientId == w.ClientId && x.TargetCode == "SECONDARY_ARCHIVE")
          .Select(x => (long?)x.Revision).MaxAsync() ?? 0;
        var created = await ArchiveCopyService.CreateTargetRevisionAsync(db, partner,
          new(w.ClientId, null, selectedArchiveDriveId, selectedArchiveRootId, selectedDraftId, "77"));
        Assert.True(created.Succeeded, $"{created.ErrorCode}: {created.Message}");
        sharedTargetA = created.Value!;
        Assert.Equal(previousRevision + 1, sharedTargetA.Revision);
        var persisted = await db.ArchiveCopyTargetRevisions.AsNoTracking()
          .SingleAsync(x => x.FirmId == w.FirmId && x.Id == sharedTargetA.TargetRevisionId);
        Assert.Null(persisted.ClientSharePointSiteId);
        Assert.True(persisted.UsesSharedSelectedResource);
        Assert.True(persisted.HasValidDigests());
      }

      var clientBId = Guid.CreateVersion7();
      var clientBEngagementId = Guid.CreateVersion7();
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var targetA = await db.ArchiveCopyTargetRevisions.AsNoTracking()
          .SingleAsync(x => x.FirmId == w.FirmId && x.Id == sharedTargetA.TargetRevisionId);
        var staffPermissions = ArchiveCopyPermissionEncoding.Encode(ArchiveCopyPermissionEncoding.ClientStaffRead("78"));
        var clientB = new PracticeClient
        {
          Id = clientBId, FirmId = w.FirmId, LegalName = "Synthetic second archive-copy client",
          Status = "ACTIVE", CreatedAt = DateTimeOffset.UtcNow
        };
        db.PracticeClients.Add(clientB);
        db.Engagements.Add(new Engagement
        {
          Id = clientBEngagementId, FirmId = w.FirmId, PracticeClientId = clientBId,
          Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
        });
        var sharedTargetB = ArchiveCopyTargetRevision.CreateSharedSelectedResource(
          Guid.CreateVersion7(), w.FirmId, clientBId, "78", 1, connectionId, tenantId, selectedSiteId,
          "shared-archive-drive-b", "shared-library-root-b", selectedDraftId, 1, selectedVerificationId,
          selectedSiteUrl, "shared-library-root-b", targetA.SelectedResourceEvidenceSha256!,
          staffPermissions.Json, staffPermissions.Sha256, targetA.AuthorizedStaffSha256,
          partner.UserId, DateTimeOffset.UtcNow);
        Assert.True(sharedTargetB.HasValidDigests());
        db.ArchiveCopyTargetRevisions.Add(sharedTargetB);
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.ArchiveCopyTargetRevisions.AsNoTracking()
          .Where(x => x.FirmId == w.FirmId && x.ClientId == clientBId && x.TargetCode == "SECONDARY_ARCHIVE")
          .Select(x => x.Revision).SingleAsync());
      }

      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var mixed = ArchiveCopyTargetRevision.CreateSharedSelectedResource(
          Guid.CreateVersion7(), w.FirmId, w.ClientId, "77", sharedTargetA.Revision + 1, connectionId,
          tenantId, selectedSiteId, selectedArchiveDriveId, selectedArchiveRootId, selectedDraftId,
          1, selectedVerificationId, selectedSiteUrl, selectedArchiveRootId,
          Hashing.Sha256Hex("synthetic selected-resource proof for xor test"),
          ArchiveCopyPermissionEncoding.Encode(ArchiveCopyPermissionEncoding.ClientStaffRead("77")).Json,
          ArchiveCopyPermissionEncoding.Encode(ArchiveCopyPermissionEncoding.ClientStaffRead("77")).Sha256,
          Hashing.Sha256Hex("synthetic authorized staff digest"), partner.UserId, DateTimeOffset.UtcNow);
        typeof(ArchiveCopyTargetRevision).GetProperty(nameof(ArchiveCopyTargetRevision.ClientSharePointSiteId))!
          .SetValue(mixed, clientSiteId);
        Assert.False(mixed.HasValidDigests());
        db.ArchiveCopyTargetRevisions.Add(mixed);
        var rejected = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgresFailure = Assert.IsType<PostgresException>(rejected.GetBaseException());
        Assert.Equal("23514", postgresFailure.SqlState);
        Assert.Equal("ck_archive_copy_target_revision_values", postgresFailure.ConstraintName);
      }

      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var migrations = db.Database.GetMigrations().ToArray();
        var foundationIndex = Array.FindIndex(migrations,
          x => x.EndsWith("_ArchiveCopyClientScopeAndReadback", StringComparison.Ordinal));
        Assert.Equal(sharedArchiveMigrationId, migrations[foundationIndex + 1]);
        var retainedSharedProofs = await db.ArchiveCopyTargetRevisions.AsNoTracking()
          .Where(x => x.FirmId == w.FirmId && x.ClientSharePointSiteId == null)
          .OrderBy(x => x.ClientId)
          .Select(x => new
          {
            x.Id,
            x.ClientId,
            x.SelectedResourceDraftId,
            x.SelectedResourceDraftRevision,
            x.SelectedResourceVerificationId,
            x.SelectedResourceSiteUrl,
            x.SelectedResourceRootFolderId,
            x.SelectedResourceEvidenceSha256,
            x.ConfigurationSha256
          }).ToArrayAsync();
        Assert.Equal(2, retainedSharedProofs.Length);
        Assert.Contains(sharedArchiveMigrationId, await db.Database.GetAppliedMigrationsAsync());
        var refused = await Assert.ThrowsAnyAsync<Exception>(() =>
          db.GetService<IMigrator>().MigrateAsync(migrations[foundationIndex]));
        Assert.Equal("55000", Assert.IsType<PostgresException>(refused.GetBaseException()).SqlState);

        await using var verifyDb = new AuditSphereDbContext(pg.Options);
        Assert.Contains(sharedArchiveMigrationId, await verifyDb.Database.GetAppliedMigrationsAsync());
        var retainedSharedProofsAfterRefusal = await verifyDb.ArchiveCopyTargetRevisions.AsNoTracking()
          .Where(x => x.FirmId == w.FirmId && x.ClientSharePointSiteId == null)
          .OrderBy(x => x.ClientId)
          .Select(x => new
          {
            x.Id,
            x.ClientId,
            x.SelectedResourceDraftId,
            x.SelectedResourceDraftRevision,
            x.SelectedResourceVerificationId,
            x.SelectedResourceSiteUrl,
            x.SelectedResourceRootFolderId,
            x.SelectedResourceEvidenceSha256,
            x.ConfigurationSha256
          }).ToArrayAsync();
        Assert.Equal(retainedSharedProofs, retainedSharedProofsAfterRefusal);
        await verifyDb.GetService<IMigrator>().MigrateAsync();
      }

      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        Assert.Equal(18, await db.ArchiveCopyFileReceipts.CountAsync(x => x.OperationId == first.OperationId));
        Assert.Equal(1, await db.ArchiveCopyPermissionObservations.CountAsync(x => x.OperationId == first.OperationId));
        Assert.NotEmpty(await db.ArchiveCopyTargetRevisions.ToListAsync());
        Assert.Equal(ArchiveStates.AssemblyReviewed,
          await db.Archives.Where(x => x.Id == archiveId).Select(x => x.Status).SingleAsync());
      }

      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var operation = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == first.OperationId);
        operation.ClientId = clientBId;
        operation.EngagementId = clientBEngagementId;
        Assert.Throws<OperationBlockedException>(() => OperationEncoding.Matches(operation, handler));
        operation.ClientId = w.ClientId;
        operation.EngagementId = w.EngagementId;

        var originalPayloadJson = operation.PayloadJson;
        var wrongFirmPayload = JsonNode.Parse(originalPayloadJson)!.AsObject();
        wrongFirmPayload["FirmId"] = Guid.CreateVersion7().ToString("D");
        operation.PayloadJson = wrongFirmPayload.ToJsonString();
        Assert.Throws<OperationBlockedException>(() => OperationEncoding.Matches(operation, handler));
      }

      var revokeInterceptor = new ArchiveCopyRevokeOnPermissionReadInterceptor(pg.ConnectionString, w.FirmId, partner.UserId);
      var revokedReadOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
        .AddInterceptors(revokeInterceptor).Options;
      await using (var db = new AuditSphereDbContext(revokedReadOptions))
      {
        var revokedDuringRead = await ArchiveCopyService.ReadEvidenceAsync(db, partner, first.OperationId);
        Assert.True(revokeInterceptor.RevocationApplied);
        Assert.False(revokedDuringRead.Succeeded);
        Assert.Equal(ErrorCodes.ScopeDenied, revokedDuringRead.ErrorCode);
        Assert.Null(revokedDuringRead.Value);
      }
    }
    finally
    {
      if (Directory.Exists(checkpointRoot)) Directory.Delete(checkpointRoot, recursive: true);
    }
  }

}
