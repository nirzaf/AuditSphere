using System.IO.Compression;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AuditDeliverablesTests
{
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
    Assert.Equal(scan.Value, (await AuditDeliverableService.UploadSignedRepresentationAsync(db, client, letterId, letter.ContentSha256, "Client Director", pdf)).Value);
    Assert.Equal(ErrorCodes.IdempotencyConflict, (await AuditDeliverableService.UploadSignedRepresentationAsync(db, client, letterId, letter.ContentSha256, "Different Director", pdf)).ErrorCode);
    var row = (await AuditDeliverableService.GetSignedRepresentationAsync(db, client, scan.Value)).Value!;
    Assert.Equal(pdf, row.Content);
    Assert.Equal(Hashing.Sha256Hex(pdf), row.ContentSha256);
    Assert.Equal(ErrorCodes.ScopeDenied, (await AuditDeliverableService.VerifySignedRepresentationAsync(db, manager, row.Id, row.ContentSha256, "Reviewed all pages and the signature.")).ErrorCode);
    Assert.Equal(ErrorCodes.GenerationStale, (await AuditDeliverableService.VerifySignedRepresentationAsync(db, partner, row.Id, new string('0',64), "Reviewed all pages and the signature.")).ErrorCode);
    var verification = await AuditDeliverableService.VerifySignedRepresentationAsync(db, partner, row.Id, row.ContentSha256, "Verified executive management authority, signature and completeness.");
    Assert.True(verification.Succeeded, verification.Message);
    Assert.Equal(verification.Value, (await AuditDeliverableService.VerifySignedRepresentationAsync(db, partner, row.Id, row.ContentSha256, "Reviewed again.")).Value);
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
    Assert.False((await AuditDeliverableService.SignedRepresentationsAsync(db, client, w.EngagementId)).Single().Current);
    Assert.Equal(ErrorCodes.GenerationStale, (await AuditDeliverableService.VerifySignedRepresentationAsync(db, partner, row.Id, row.ContentSha256, "Reviewed again after changes.")).ErrorCode);
    await db.Users.Where(x => x.Id == client.UserId).ExecuteUpdateAsync(x => x.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
    Assert.False((await AuditDeliverableService.GetSignedRepresentationAsync(db, client, row.Id)).Succeeded);
    Assert.Empty(await AuditDeliverableService.SignedRepresentationsAsync(db, client, w.EngagementId));
  }

  [Fact]
  public async Task CertificateSignedReport_IsReleasedWithCertificateSignedStatements_OrNotAtAll()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var iar = await ReportReadyForSigningAsync(pg, w);
    var partner = w.A("partner", "Partner"); var manager = w.A("manager", "Manager");
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
    await CompletionBundleFixture.ReleasedFinancialPackageAsync(pg.Options, new(w.FirmId, w.ClientId, w.EngagementId, w.U));
    await CompletionBundleFixture.SeedPostedFeeAsync(pg.Options, new(w.FirmId, w.ClientId, w.EngagementId, w.U), automateBalance: true);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // The certificate must be available again to certify the statements: without it nothing is assembled.
      Assert.Equal(ErrorCodes.GateBlocked, (await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true)).ErrorCode);
      Assert.Equal(ErrorCodes.GateBlocked, (await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true, default,
        new ReportCertificateSigningTests.FixedSource(null, required: true))).ErrorCode);
      Assert.Empty(await db.CommercialDeliverableBundles.ToListAsync());

      var assembled = await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true, default, source);
      Assert.True(assembled.Succeeded, assembled.Message);
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
    var signedId = await SignedReportAsync(pg, w);
    var partner = w.A("partner", "Partner"); var manager = w.A("manager", "Manager"); var client = w.A("client", "ClientUser");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await AuditDeliverableService.GenerateReportAsync(db, manager, w.EngagementId, DeliverableKinds.ManagementLetter)).Succeeded);
      var blocked = await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true);
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);
      Assert.Contains("financial package", blocked.Message);
      Assert.Contains("balance invoice", blocked.Message);
      Assert.Empty(await db.CommercialDeliverableBundles.ToListAsync());
    }
    var (packageId, releaseId) = await CompletionBundleFixture.ReleasedFinancialPackageAsync(pg.Options, new(w.FirmId, w.ClientId, w.EngagementId, w.U));
    await CompletionBundleFixture.SeedPostedFeeAsync(pg.Options, new(w.FirmId, w.ClientId, w.EngagementId, w.U), automateBalance: true);
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
      var row = (await AuditDeliverableService.GetBundleAsync(db, client, assembled.Value)).Value!;
      Assert.Equal(signedId, row.SignedReportId);
      Assert.Equal(releaseId, row.FinancialPackageReleaseId);
      Assert.Equal(Hashing.Sha256Hex(row.Content), row.ContentSha256);
      Assert.Equal(Hashing.Sha256Hex(row.ManifestJson), row.ManifestSha256);
      using var archive = new ZipArchive(new MemoryStream(row.Content), ZipArchiveMode.Read);
      Assert.Equal(8, archive.Entries.Count);
      Assert.Equal(5, archive.Entries.Where(x => x.FullName != "manifest.json").Select(x => x.FullName.Split('/')[0]).Distinct().Count());
      Assert.Contains(archive.Entries, x => x.FullName.EndsWith("Management_signed_letter.pdf"));
      // STE J40: the management correspondence trail is one of the five parts, with its confirmations and communications.
      using (var trailStream = archive.GetEntry("04_Management_correspondence/Audit_trail.json")!.Open())
      using (var trail = await System.Text.Json.JsonDocument.ParseAsync(trailStream))
      {
        Assert.True(trail.RootElement.TryGetProperty("Confirmations", out _));
        Assert.True(trail.RootElement.TryGetProperty("Communications", out _));
      }
      using var manifest = System.Text.Json.JsonDocument.Parse(row.ManifestJson);
      foreach (var item in manifest.RootElement.GetProperty("Parts").EnumerateArray())
      {
        var entry = archive.GetEntry(item.GetProperty("Path").GetString()!)!;
        using var bytes = new MemoryStream(); using var stream = entry.Open(); await stream.CopyToAsync(bytes);
        Assert.Equal(item.GetProperty("Sha256").GetString(), Hashing.Sha256Hex(bytes.ToArray()));
      }
      using var certified = new MemoryStream();
      using (var stream = archive.GetEntry("01_Report_and_statements/Certified_financial_statements.pdf")!.Open()) await stream.CopyToAsync(certified);
      using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(certified, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
      Assert.True(pdf.PageCount > 1);
      Assert.Contains(pdf.Internals.GetAllObjects(), x => x is PdfSharp.Pdf.PdfDictionary d && d.Elements.GetName("/Subtype") == "/Image");
      Assert.Single(await db.CommercialDeliverableBundles.ToListAsync());
      Assert.Single(await AuditDeliverableService.ClientBundlesAsync(db, client, w.EngagementId));
      Assert.Equal(ErrorCodes.ProtectedState, (await AuditSphereOps.Application.Documents.ClientPortalService.RequireUploadWindowAsync(db, client, w.EngagementId)).ErrorCode);
      var outsider = NewUser(w.FirmId, "outside", "Client");
      db.Users.Add(outsider); db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = outsider.Id, Role = "ClientUser", ClientId = w.ClientId, EngagementId = null,
        GrantedAt = DateTimeOffset.UtcNow, RevokedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.U["partner"].Id }); await db.SaveChangesAsync();
      Assert.Equal(ErrorCodes.ScopeDenied, (await AuditDeliverableService.GetBundleAsync(db, new(outsider.Id, w.FirmId, outsider.SessionEpoch, ["ClientUser"]), row.Id)).ErrorCode);
      Assert.False((await AuditDeliverableService.GetBundleAsync(db, client with { FirmId = Guid.NewGuid() }, row.Id)).Succeeded);
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM commercial_deliverable_bundles WHERE id = {row.Id}"));
    }
  }

}
