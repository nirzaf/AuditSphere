using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

/// <summary>The exact PDF of the composition a client manager is asked to approve, with its sealed fingerprint.</summary>
public sealed record ClientFinancialPackageCompositionPdf(Guid CompositionId, string FileName, string ArtifactSha256Hex, byte[] Bytes);

/// <summary>
/// Client-management approval of the exact composition (CP-19, F-02). A client manager sees and approves the sealed three-format composition
/// of the current signing candidate, never a staff screen and never an internal draft. The decision names that composition's identity and
/// digest, so accounting and Partner decisions and the release bind to what the client approved.
/// </summary>
public static partial class FinancialPackageReviewService
{
  /// <summary>
  /// The composition currently awaiting management approval: the most recently sealed composition of an active signing candidate for the
  /// exact package version whose source is current and whose stored artifacts still match their sealed fingerprints. Null when the package
  /// has no such composition, in which case only the historical package-only acknowledgement path applies.
  /// </summary>
  internal static async Task<FinancialStatementComposition?> CurrentClientCompositionAsync(
    IClientAccountingDbContext db, FinancialPackage package, CancellationToken ct)
  {
    var compositions = await db.FinancialStatementCompositions.AsNoTracking().Where(x =>
        x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.EngagementId == package.EngagementId &&
        x.FinancialPackageId == package.Id && x.PackageCalculationHash == package.CalculationHash)
      .OrderByDescending(x => x.SelectedAt).ThenByDescending(x => x.Id).Take(20).ToListAsync(ct);
    foreach (var composition in compositions)
    {
      var candidate = await db.FinancialStatementPresentationRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == composition.PresentationRevisionId && x.FirmId == package.FirmId && x.ClientId == package.ClientId &&
        x.EngagementId == package.EngagementId && x.FinancialPackageId == package.Id, ct);
      if (candidate is null || candidate.PackageCalculationHash != package.CalculationHash || candidate.PackageGeneration != package.Generation ||
          candidate.Status is not (FinancialStatementPresentationRevisionStatuses.Candidate or FinancialStatementPresentationRevisionStatuses.Approved))
        continue;
      // The newest active candidate decides. An older composition never stands in for a newer candidate that fails its checks.
      return FinancialStatementService.PresentationChecksAllPass(candidate) &&
        await FinancialStatementService.PresentationSourceIsCurrentAsync(db, candidate, ct) &&
        await CompositionArtifactsAreIntactAsync(db, package, candidate, composition, ct)
          ? composition
          : null;
    }
    return null;
  }

  /// <summary>
  /// The exact sealed PDF of the composition awaiting management approval, for a client user with an explicit ClientUser grant on the
  /// package's client and engagement. Downloading records nothing and approves nothing.
  /// </summary>
  public static async Task<CommandResult<ClientFinancialPackageCompositionPdf>> GetClientCompositionPdfAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid financialPackageId, Guid compositionId, CancellationToken ct = default)
  {
    var view = await GetClientViewAsync(db, actor, financialPackageId, ct);
    if (!view.Succeeded)
      return CommandResult<ClientFinancialPackageCompositionPdf>.Fail(view.ErrorCode!, view.Message!);
    if (view.Value!.Composition is not { } invited || invited.CompositionId != compositionId)
      return CommandResult<ClientFinancialPackageCompositionPdf>.Fail(ErrorCodes.StaleRevision,
        "This report is no longer the one awaiting your approval. Refresh the package.");
    var composition = await db.FinancialStatementCompositions.AsNoTracking().SingleAsync(x =>
      x.Id == compositionId && x.FirmId == actor.FirmId && x.FinancialPackageId == financialPackageId, ct);
    var pdf = await db.FinancialPackageArtifacts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == composition.PdfArtifactId && x.FirmId == composition.FirmId && x.ClientId == composition.ClientId &&
      x.EngagementId == composition.EngagementId && x.FinancialPackageId == financialPackageId, ct);
    if (pdf is null || pdf.ArtifactBytes.Length == 0 || Hashing.Sha256Hex(pdf.ArtifactBytes) != composition.PdfSha256Hex)
      return CommandResult<ClientFinancialPackageCompositionPdf>.Fail(ErrorCodes.GateBlocked,
        "The sealed report document is unavailable. Your firm has been asked to reissue it.");
    return CommandResult<ClientFinancialPackageCompositionPdf>.Ok(new(composition.Id,
      $"financial-statements-{financialPackageId:D}-{composition.Id:N}.pdf", composition.PdfSha256Hex, pdf.ArtifactBytes));
  }
}
