using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

/// <summary>
/// CP-18 composition. Selecting a candidate statement presentation seals its formats as one set: PDF, Word and Excel are rendered from the
/// candidate's immutable snapshot, and one composition records each artifact's identity and digest with a digest over the ordered manifest.
/// No format is optional. If any format cannot be rendered, no composition is recorded.
/// </summary>
public static partial class FinancialStatementService
{
  private static readonly string[] RequiredCompositionFormats =
    [FinancialStatementPresentationRenderer.PdfFormat, FinancialStatementPresentationRenderer.WordFormat, FinancialStatementPresentationRenderer.ExcelFormat];

  public static async Task<CommandResult<FinancialStatementComposition>> SelectCompositionAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid packageId, Guid candidateRevisionId, CancellationToken ct = default)
  {
    if (packageId == Guid.Empty || candidateRevisionId == Guid.Empty)
      return CommandResult<FinancialStatementComposition>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "Select the package and the candidate presentation revision to compose.");
    var candidate = await db.FinancialStatementPresentationRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == candidateRevisionId && x.FirmId == actor.FirmId && x.FinancialPackageId == packageId, ct);
    if (candidate is null)
      return CommandResult<FinancialStatementComposition>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (candidate.Status != FinancialStatementPresentationRevisionStatuses.Candidate)
      return CommandResult<FinancialStatementComposition>.Fail(ErrorCodes.GateBlocked,
        "Only a candidate presentation revision can be selected as a composition.");
    var auth = await AuthorizeAsync(db, actor, candidate.FirmId, candidate.ClientId, candidate.EngagementId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<FinancialStatementComposition>.Fail(auth.ErrorCode!, auth.Message!);

    // A composition is one record per candidate. Repeating the selection returns the stored composition.
    var recorded = await db.FinancialStatementCompositions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.PresentationRevisionId == candidate.Id, ct);
    if (recorded is not null)
      return CommandResult<FinancialStatementComposition>.Ok(recorded);

    // The package must still be the one the candidate was promoted from.
    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == packageId && x.FirmId == actor.FirmId, ct);
    if (package is null || package.CalculationHash != candidate.PackageCalculationHash || package.Generation != candidate.PackageGeneration)
      return CommandResult<FinancialStatementComposition>.Fail(ErrorCodes.StaleRevision,
        "The package changed after this candidate was promoted, so it cannot be composed.");

    // Every required format is rendered from the candidate's snapshot. A failed format stops the composition, so no partial set is sealed.
    var artifacts = new Dictionary<string, FinancialStatementPresentationPinnedArtifact>(StringComparer.Ordinal);
    foreach (var format in RequiredCompositionFormats)
    {
      var rendered = await RenderPinnedPresentationArtifactAsync(db, actor, candidate.Id, format, ct);
      if (!rendered.Succeeded)
        return CommandResult<FinancialStatementComposition>.Fail(rendered.ErrorCode!,
          $"The {format} format could not be rendered, so no composition was sealed. {rendered.Message}");
      artifacts[format] = rendered.Value!;
    }

    var manifest = JsonSerializer.SerializeToUtf8Bytes(new
    {
      Version = "statement-composition.v1",
      PresentationRevisionId = candidate.Id,
      PackageCalculationHash = candidate.PackageCalculationHash,
      Formats = RequiredCompositionFormats.Select(format => new
      {
        Format = format,
        ArtifactId = artifacts[format].ArtifactId,
        ArtifactSha256Hex = artifacts[format].ArtifactSha256Hex,
      }).ToArray(),
    });
    var composition = new FinancialStatementComposition
    {
      Id = Guid.CreateVersion7(), FirmId = candidate.FirmId, ClientId = candidate.ClientId, EngagementId = candidate.EngagementId,
      FinancialPackageId = candidate.FinancialPackageId, PresentationRevisionId = candidate.Id,
      PackageCalculationHash = candidate.PackageCalculationHash, ManifestDigest = Hashing.Sha256Hex(manifest),
      PdfArtifactId = artifacts[FinancialStatementPresentationRenderer.PdfFormat].ArtifactId,
      PdfSha256Hex = artifacts[FinancialStatementPresentationRenderer.PdfFormat].ArtifactSha256Hex,
      WordArtifactId = artifacts[FinancialStatementPresentationRenderer.WordFormat].ArtifactId,
      WordSha256Hex = artifacts[FinancialStatementPresentationRenderer.WordFormat].ArtifactSha256Hex,
      ExcelArtifactId = artifacts[FinancialStatementPresentationRenderer.ExcelFormat].ArtifactId,
      ExcelSha256Hex = artifacts[FinancialStatementPresentationRenderer.ExcelFormat].ArtifactSha256Hex,
      SelectedByUserId = actor.UserId, SelectedAt = DateTimeOffset.UtcNow,
    };
    db.FinancialStatementCompositions.Add(composition);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ux_financial_statement_compositions_revision", StringComparison.Ordinal) == true)
    {
      // A concurrent selection of the same candidate stored its composition first. Return that one.
      var concurrent = await db.FinancialStatementCompositions.AsNoTracking().SingleAsync(x =>
        x.FirmId == actor.FirmId && x.PresentationRevisionId == candidate.Id, ct);
      return CommandResult<FinancialStatementComposition>.Ok(concurrent);
    }
    return CommandResult<FinancialStatementComposition>.Ok(composition);
  }
}
