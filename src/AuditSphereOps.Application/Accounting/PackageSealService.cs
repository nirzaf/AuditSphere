using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

// M25 seal: an atomic, immutable record that every required artifact format exists with
// exact persisted bytes for one package revision/generation. Two digests avoid the
// self-reference trap: the content manifest covers the immutable inputs captured before
// rendering, and the artifact manifest covers the completed ordered artifact identities.
public sealed record PackageArtifactManifestEntry(
  string ArtifactVersion, long ByteLength, string Sha256Hex,
  string FrameworkVersion, string TemplateVersion, string MimeType);

public sealed record PackageSealValue(
  Guid SealId, Guid FinancialPackageId, long PackageRevision, long PackageGeneration,
  string ContentManifestDigest, string ArtifactManifestDigest,
  int ArtifactCount, long TotalByteLength);

public sealed record PackageSealStatusView(
  Guid FinancialPackageId, long PackageRevision, long PackageGeneration,
  bool IsSealed, Guid? SealId, string? ContentManifestDigest, string? ArtifactManifestDigest,
  IReadOnlyList<string> PresentFormats, IReadOnlyList<string> MissingRequiredFormats, bool CanSeal);

public static class PackageSealService
{
  private static readonly string[] PreparerRoles = ["AccountingPreparer", "Staff", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  /// <summary>Formats every sealed package must contain. The text artifact is the canonical
  /// rendered statement; the workbook, word and pdf artifacts are the client-facing outputs
  /// the completion and release gates depend on.</summary>
  public static readonly string[] RequiredFormats =
  [
    FinancialPackageArtifactVersions.Text,
    FinancialPackageArtifactVersions.Workbook,
    FinancialPackageArtifactVersions.Word,
    FinancialPackageArtifactVersions.Pdf
  ];

  /// <summary>Reports whether the exact package revision can be sealed and which required
  /// formats are missing. A partially rendered package reports its gaps rather than a
  /// misleading ready state.</summary>
  public static async Task<CommandResult<PackageSealStatusView>> GetSealStatusAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid financialPackageId,
    CancellationToken ct = default)
  {
    if (financialPackageId == Guid.Empty)
      return CommandResult<PackageSealStatusView>.Fail(ErrorCodes.Accounting.PackageInvalid, "A package id is required.");
    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == financialPackageId && x.FirmId == actor.FirmId, ct);
    if (package is null)
      return CommandResult<PackageSealStatusView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(package.FirmId, package.ClientId, package.EngagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<PackageSealStatusView>.Fail(auth.ErrorCode!, auth.Message!);

    var artifacts = await LoadCurrentArtifactsAsync(db, package, ct);
    var seal = await db.FinancialPackageSeals.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id &&
        x.PackageRevision == package.Revision && x.PackageGeneration == package.Generation)
      .OrderByDescending(x => x.SealedAt).ThenByDescending(x => x.Id)
      .FirstOrDefaultAsync(ct);
    var present = artifacts.Select(x => x.ArtifactVersion).ToList();
    var missing = RequiredFormats.Where(required => !present.Contains(required, StringComparer.Ordinal)).ToList();

    return CommandResult<PackageSealStatusView>.Ok(new PackageSealStatusView(
      package.Id, package.Revision, package.Generation,
      seal is not null, seal?.Id, seal?.ContentManifestDigest, seal?.ArtifactManifestDigest,
      present, missing, CanSeal: missing.Count == 0));
  }

  /// <summary>Atomically seals the current package revision. Every required format must
  /// exist with non-empty bytes, and a structurally invalid artifact set is refused so a
  /// partially persisted package is never sealed. Re-sealing the identical artifact set
  /// returns the existing seal rather than writing a second one.</summary>
  public static async Task<CommandResult<PackageSealValue>> SealPackageAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid financialPackageId,
    CancellationToken ct = default)
  {
    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == financialPackageId && x.FirmId == actor.FirmId, ct);
    if (package is null)
      return CommandResult<PackageSealValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(package.FirmId, package.ClientId, package.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<PackageSealValue>.Fail(auth.ErrorCode!, auth.Message!);

    var artifacts = await LoadCurrentArtifactsAsync(db, package, ct);
    var present = artifacts.Select(x => x.ArtifactVersion).ToList();
    var missing = RequiredFormats.Where(required => !present.Contains(required, StringComparer.Ordinal)).ToList();
    if (missing.Count > 0)
      return CommandResult<PackageSealValue>.Fail(ErrorCodes.GateBlocked,
        $"Every required format must be rendered before sealing; missing: {string.Join(", ", missing)}.");
    if (artifacts.Any(x => x.ArtifactBytes.Length == 0))
      return CommandResult<PackageSealValue>.Fail(ErrorCodes.GateBlocked,
        "An artifact with no persisted bytes cannot be sealed.");
    var failedBlocking = await db.FinancialPackageValidations.AsNoTracking()
      .Where(x => x.FinancialPackageId == package.Id && !x.Passed &&
        FinancialPackageReviewService.BlockingValidationCodes.Contains(x.Code))
      .Select(x => x.Code).ToListAsync(ct);
    if (failedBlocking.Count > 0)
      return CommandResult<PackageSealValue>.Fail(ErrorCodes.GateBlocked,
        $"The package has failed blocking validations: {string.Join(", ", failedBlocking.OrderBy(x => x, StringComparer.Ordinal))}.");

    var manifest = artifacts.Select(x => new PackageArtifactManifestEntry(
      x.ArtifactVersion, x.ArtifactBytes.LongLength, x.ArtifactSha256Hex,
      x.FrameworkVersion, x.TemplateVersion, MimeTypeFor(x.ArtifactVersion))).ToList();
    // The artifact digest covers ordered identities and their byte hashes only, so it is a
    // function of the persisted bytes rather than of this record.
    var artifactDigest = Hashing.Sha256Hex(string.Join('\n', manifest
      .OrderBy(x => x.ArtifactVersion, StringComparer.Ordinal)
      .Select(x => string.Join('|', x.ArtifactVersion, x.ByteLength, x.Sha256Hex, x.RendererOrTemplateVersion()))));
    // The content digest covers the immutable inputs captured before rendering.
    var contentDigest = Hashing.Sha256Hex(string.Join('|',
      package.CalculationHash, package.MappingVersionId.ToString("D"), package.AdjustmentPlanId.ToString("D"),
      package.AdjustedDatasetId.ToString("D"), package.TaxonomyVersion, package.TemplateVersion,
      package.CalculationEngineVersion, package.Currency, package.Revision, package.Generation,
      package.SupplementaryHash ?? string.Empty, package.EquityHash ?? string.Empty));

    var existing = await db.FinancialPackageSeals.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id &&
      x.PackageRevision == package.Revision && x.PackageGeneration == package.Generation &&
      x.ArtifactManifestDigest == artifactDigest, ct);
    if (existing is not null)
      return CommandResult<PackageSealValue>.Ok(ToValue(existing));

    var seal = new FinancialPackageSeal
    {
      Id = Guid.CreateVersion7(), FirmId = package.FirmId, ClientId = package.ClientId,
      EngagementId = package.EngagementId, FinancialPackageId = package.Id,
      PackageRevision = package.Revision, PackageGeneration = package.Generation,
      PackageHash = package.CalculationHash, ContentManifestDigest = contentDigest,
      ArtifactManifestDigest = artifactDigest, ArtifactCount = manifest.Count,
      TotalByteLength = manifest.Sum(x => x.ByteLength),
      ArtifactManifestJson = System.Text.Json.JsonSerializer.Serialize(manifest),
      SealedByUserId = actor.UserId, SealedAt = DateTimeOffset.UtcNow
    };
    db.FinancialPackageSeals.Add(seal);
    await db.SaveChangesAsync(ct);
    return CommandResult<PackageSealValue>.Ok(ToValue(seal));
  }

  private static async Task<List<FinancialPackageArtifact>> LoadCurrentArtifactsAsync(
    IClientAccountingDbContext db, FinancialPackage package, CancellationToken ct) =>
    await db.FinancialPackageArtifacts.AsNoTracking()
      .Where(x => x.FirmId == package.FirmId && x.ClientId == package.ClientId &&
        x.EngagementId == package.EngagementId && x.FinancialPackageId == package.Id &&
        x.PackageRevision == package.Revision && x.PackageGeneration == package.Generation &&
        x.PackageHash == package.CalculationHash &&
        x.ArtifactVersion != FinancialPackageArtifactVersions.ControlledWorkbook)
      .OrderBy(x => x.ArtifactVersion).ThenBy(x => x.Id)
      .ToListAsync(ct);

  /// <summary>Genuine MIME type per artifact format; an unknown format is refused by the
  /// seal rather than labelled generically.</summary>
  private static string MimeTypeFor(string artifactVersion) => artifactVersion switch
  {
    FinancialPackageArtifactVersions.Text => "text/plain",
    FinancialPackageArtifactVersions.Workbook => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    FinancialPackageArtifactVersions.Word => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    FinancialPackageArtifactVersions.Pdf => "application/pdf",
    _ => "application/octet-stream"
  };

  private static PackageSealValue ToValue(FinancialPackageSeal seal) => new(
    seal.Id, seal.FinancialPackageId, seal.PackageRevision, seal.PackageGeneration,
    seal.ContentManifestDigest, seal.ArtifactManifestDigest, seal.ArtifactCount, seal.TotalByteLength);
}

internal static class PackageArtifactManifestEntryExtensions
{
  internal static string RendererOrTemplateVersion(this PackageArtifactManifestEntry entry) =>
    $"{entry.FrameworkVersion}|{entry.TemplateVersion}";
}
