using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

// Module 25 read-side manifest: content inputs (statement lines, plan, mapping, template)
// and ordered artifact identities with byte hashes. Read-only, scope-checked, and
// reports the currentness state so stale manifests are never mistaken for approved ones.
public sealed record PackageManifestArtifactDto(
  Guid ArtifactId, string ArtifactVersion, string ArtifactSha256Hex,
  string FrameworkVersion, string TemplateVersion, long ByteLength, DateTimeOffset CreatedAt);

public sealed record PackageManifestDto(
  Guid PackageId, long Revision, long Generation, string PackageHash, string Status,
  Guid? ComparativePackageId, string? ComparativeBasis,
  string? SupplementaryHash, string? EquityHash,
  string CalculationEngineVersion, string TaxonomyVersion, string TemplateVersion,
  DateTimeOffset CreatedAt,
  IReadOnlyList<PackageManifestArtifactDto> Artifacts,
  int ValidationPassedCount, int ValidationFailedCount);

public static class PackageManifestQuery
{
  private static readonly string[] ReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<PackageManifestDto>> GetManifestAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid packageId,
    CancellationToken ct = default)
  {
    if (packageId == Guid.Empty)
      return CommandResult<PackageManifestDto>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "A package id is required.");
    var package = await db.FinancialPackages.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == packageId && x.FirmId == actor.FirmId, ct);
    if (package is null)
      return CommandResult<PackageManifestDto>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(package.FirmId, package.ClientId, package.EngagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<PackageManifestDto>.Fail(auth.ErrorCode!, auth.Message!);

    var artifacts = await db.FinancialPackageArtifacts.AsNoTracking()
      .Where(x => x.FirmId == package.FirmId && x.FinancialPackageId == package.Id &&
        x.PackageRevision == package.Revision && x.PackageGeneration == package.Generation &&
        x.PackageHash == package.CalculationHash)
      .OrderBy(x => x.ArtifactVersion).ThenBy(x => x.CreatedAt)
      .Select(x => new PackageManifestArtifactDto(
        x.Id, x.ArtifactVersion, x.ArtifactSha256Hex,
        x.FrameworkVersion, x.TemplateVersion, x.ArtifactBytes.Length, x.CreatedAt))
      .ToListAsync(ct);

    var validations = await db.FinancialPackageValidations.AsNoTracking()
      .Where(x => x.FinancialPackageId == package.Id)
      .ToListAsync(ct);

    return CommandResult<PackageManifestDto>.Ok(new PackageManifestDto(
      package.Id, package.Revision, package.Generation, package.CalculationHash, package.Status,
      package.ComparativePackageId, package.ComparativeBasis,
      package.SupplementaryHash, package.EquityHash,
      package.CalculationEngineVersion, package.TaxonomyVersion, package.TemplateVersion,
      package.CreatedAt, artifacts,
      validations.Count(x => x.Passed), validations.Count(x => !x.Passed)));
  }
}
