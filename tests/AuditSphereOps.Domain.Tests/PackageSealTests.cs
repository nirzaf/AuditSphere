using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>M25 seal: every required format must exist before sealing, the two digests
/// avoid a self-reference, and the seal binds the exact artifact set.</summary>
[Trait("Profile", "Database")]
public sealed class PackageSealTests
{
  [Fact(DisplayName = "Sealing requires every format, is idempotent for the same bytes and detects tampering")]
  public async Task Seal_RequiresAllFormatsAndBindsExactBytes()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Only the text artifact exists: the package is not sealable and reports its gaps.
      var partial = await PackageSealService.GetSealStatusAsync(db, preparer, fixture.PackageId);
      Assert.True(partial.Succeeded, partial.Message);
      Assert.False(partial.Value!.CanSeal);
      Assert.False(partial.Value.IsSealed);
      Assert.Contains(FinancialPackageArtifactVersions.Workbook, partial.Value.MissingRequiredFormats);
      Assert.Contains(FinancialPackageArtifactVersions.Word, partial.Value.MissingRequiredFormats);
      Assert.Contains(FinancialPackageArtifactVersions.Pdf, partial.Value.MissingRequiredFormats);

      var blocked = await PackageSealService.SealPackageAsync(db, preparer, fixture.PackageId);
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);
      Assert.Contains("missing", blocked.Message!, StringComparison.OrdinalIgnoreCase);

      // Add the remaining required formats.
      AddArtifact(db, fixture, FinancialPackageArtifactVersions.Workbook, "xlsx");
      AddArtifact(db, fixture, FinancialPackageArtifactVersions.Word, "docx");
      AddArtifact(db, fixture, FinancialPackageArtifactVersions.Pdf, "pdf");
      await db.SaveChangesAsync();
    }

    Guid sealId;
    string artifactDigest;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var status = await PackageSealService.GetSealStatusAsync(db, preparer, fixture.PackageId);
      Assert.True(status.Value!.CanSeal);
      Assert.Empty(status.Value.MissingRequiredFormats);

      var sealedResult = await PackageSealService.SealPackageAsync(db, preparer, fixture.PackageId);
      Assert.True(sealedResult.Succeeded, sealedResult.Message);
      var seal = sealedResult.Value!;
      Assert.Equal(4, seal.ArtifactCount);
      Assert.True(seal.TotalByteLength > 0);
      Assert.Matches("^[0-9a-f]{64}$", seal.ContentManifestDigest);
      Assert.Matches("^[0-9a-f]{64}$", seal.ArtifactManifestDigest);
      // The two digests cover different things and must not coincide.
      Assert.NotEqual(seal.ContentManifestDigest, seal.ArtifactManifestDigest);
      sealId = seal.SealId;
      artifactDigest = seal.ArtifactManifestDigest;

      // Re-sealing the identical artifact set returns the existing seal, not a second row.
      var again = await PackageSealService.SealPackageAsync(db, preparer, fixture.PackageId);
      Assert.True(again.Succeeded);
      Assert.Equal(sealId, again.Value!.SealId);
      Assert.Equal(1, await db.FinancialPackageSeals.CountAsync(x => x.FinancialPackageId == fixture.PackageId));

      // The sealed record is immutable at the database level.
      var tamper = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE financial_package_seals SET artifact_count = 99 WHERE id = {sealId}"));
      Assert.Equal("55000", tamper.SqlState);

      var sealedStatus = await PackageSealService.GetSealStatusAsync(db, preparer, fixture.PackageId);
      Assert.True(sealedStatus.Value!.IsSealed);
      Assert.Equal(artifactDigest, sealedStatus.Value.ArtifactManifestDigest);
    }

    // A different artifact set for the same revision produces a different digest, so the
    // seal cannot silently cover bytes it was not created for.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var pdf = await db.FinancialPackageArtifacts.SingleAsync(x =>
        x.FinancialPackageId == fixture.PackageId && x.ArtifactVersion == FinancialPackageArtifactVersions.Pdf);
      db.FinancialPackageArtifacts.Remove(pdf);
      AddArtifact(db, fixture, FinancialPackageArtifactVersions.Pdf, "pdf-revised");
      await db.SaveChangesAsync();

      var revised = await PackageSealService.SealPackageAsync(db, preparer, fixture.PackageId);
      Assert.True(revised.Succeeded, revised.Message);
      Assert.NotEqual(artifactDigest, revised.Value!.ArtifactManifestDigest);
      Assert.Equal(2, await db.FinancialPackageSeals.CountAsync(x => x.FinancialPackageId == fixture.PackageId));
    }
  }

  private static void AddArtifact(AuditSphereDbContext db, Fixture fixture, string version, string suffix)
  {
    var bytes = System.Text.Encoding.UTF8.GetBytes($"package-artifact|{version}|{suffix}|{fixture.PackageHash}");
    db.FinancialPackageArtifacts.Add(new FinancialPackageArtifact
    {
      Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, ClientId = fixture.ClientId,
      EngagementId = fixture.EngagementId, FinancialPackageId = fixture.PackageId,
      PackageRevision = 1, PackageGeneration = 1, PackageHash = fixture.PackageHash,
      ArtifactVersion = version, FrameworkVersion = "IFRS", TemplateVersion = "seal-template",
      ArtifactSha256Hex = Hashing.Sha256Hex(bytes), ArtifactBytes = bytes,
      CreatedByUserId = fixture.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    });
  }

  private sealed record Fixture(Guid FirmId, Guid ClientId, Guid EngagementId, Guid PackageId,
    string PackageHash, AuditSphereOps.Domain.Security.AppUser Preparer);

  private static AuditSphereOps.Application.Abstractions.ActorContext Actor(
    AuditSphereOps.Domain.Security.AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var packageId = Guid.CreateVersion7();
    var packageHash = Hashing.Sha256Hex("seal-package");
    var preparer = new AuditSphereOps.Domain.Security.AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId, Subject = "seal-preparer-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant-test", Email = "seal-preparer@example.test", DisplayName = "Seal Preparer",
      UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    };
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new AuditSphereOps.Domain.Practice.PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "PACKAGE SEAL CLIENT", CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId, ProfessionalWorkBlocked = false,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new AuditSphereOps.Domain.Completion.FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new AuditSphereOps.Domain.Completion.ClientSafetyState { Id = clientId, FirmId = firmId });
    db.Users.Add(preparer);
    db.RoleGrants.Add(new AuditSphereOps.Domain.Security.RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = firmId, UserId = preparer.Id, Role = "AccountingPreparer",
      ClientId = clientId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = preparer.Id
    });
    // The package requires its full immutable input chain.
    var datasetId = Guid.CreateVersion7();
    var planId = Guid.CreateVersion7();
    var mappingId = Guid.CreateVersion7();
    var adjustedId = Guid.CreateVersion7();
    var now = DateTimeOffset.UtcNow;
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      SourceKind = "Raw", Revision = 1, LegalEntityKey = "SEAL-CLIENT", Currency = "QAR",
      RawFileSha256Hex = packageHash, NormalizedDatasetDigest = packageHash, Sha256Hex = packageHash,
      Balanced = true, ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Sealed,
      ControlTotal = 0m, ImportedAt = now, ImportedByUserId = preparer.Id
    });
    db.AdjustmentPlans.Add(new AdjustmentPlan
    {
      Id = planId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      BaseDatasetId = datasetId, Status = "Finalized", ResultHash = packageHash,
      CreatedByUserId = preparer.Id, CreatedAt = now
    });
    db.MappingVersions.Add(new MappingVersion
    {
      Id = mappingId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      DatasetId = datasetId, Version = 1, Generation = 1, TaxonomyVersion = "tax-v1",
      PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Status = AccountingPackageStates.MappingApproved,
      CreatedByUserId = preparer.Id, ApprovedByUserId = preparer.Id, ApprovedAt = now, CreatedAt = now
    });
    db.AdjustedTrialBalanceSnapshots.Add(new AdjustedTrialBalanceSnapshot
    {
      Id = adjustedId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      BaseDatasetId = datasetId, AdjustmentPlanId = planId, Currency = "QAR", ResultHash = packageHash,
      CreatedByUserId = preparer.Id, CreatedAt = now
    });
    db.FinancialPackages.Add(new FinancialPackage
    {
      Id = packageId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      AdjustedDatasetId = adjustedId, MappingVersionId = mappingId, AdjustmentPlanId = planId,
      Framework = "IFRS", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      TaxonomyVersion = "tax-v1", TemplateVersion = "seal-template", CalculationEngineVersion = "test-engine",
      CalculationHash = packageHash, Currency = "QAR", Revision = 1, Generation = 1,
      Status = AccountingPackageStates.PackageValidated, CreatedAt = now
    });
    db.FinancialPackageValidations.Add(new FinancialPackageValidation
    {
      Id = Guid.CreateVersion7(), FirmId = firmId, FinancialPackageId = packageId,
      Code = "ACCOUNTING_EQUATION", Passed = true, Detail = "Seal fixture.", CreatedAt = DateTimeOffset.UtcNow
    });
    var textBytes = System.Text.Encoding.UTF8.GetBytes($"package-artifact|{FinancialPackageArtifactVersions.Text}|{packageHash}");
    db.FinancialPackageArtifacts.Add(new FinancialPackageArtifact
    {
      Id = Guid.CreateVersion7(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      FinancialPackageId = packageId, PackageRevision = 1, PackageGeneration = 1, PackageHash = packageHash,
      ArtifactVersion = FinancialPackageArtifactVersions.Text, FrameworkVersion = "IFRS",
      TemplateVersion = "seal-template", ArtifactSha256Hex = Hashing.Sha256Hex(textBytes),
      ArtifactBytes = textBytes, CreatedByUserId = preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    return new Fixture(firmId, clientId, engagementId, packageId, packageHash, preparer);
  }
}
