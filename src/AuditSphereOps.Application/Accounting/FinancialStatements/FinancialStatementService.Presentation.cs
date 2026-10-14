using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record FinancialStatementPresentationExport(string FileName, string ContentType, string ArtifactSha256Hex, byte[] Bytes);

public static partial class FinancialStatementService
{
  /// <summary>
  /// The statement presentation for one financial package, evaluated through the published statement layout for its
  /// client and framework (or an explicitly named published layout of the same client and framework). Nothing is
  /// stored: the presentation is a deterministic read of persisted, sealed package lines.
  /// </summary>
  public static async Task<CommandResult<FinancialStatementPresentation>> BuildPresentationAsync(
    IClientAccountingDbContext db,
    ActorContext actor,
    Guid packageId,
    Guid? layoutVersionId = null,
    CancellationToken ct = default)
  {
    var package = await db.FinancialPackages.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == packageId && x.FirmId == actor.FirmId, ct);
    if (package is null)
      return CommandResult<FinancialStatementPresentation>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, package.FirmId, package.ClientId, package.EngagementId, PackageReadRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<FinancialStatementPresentation>.Fail(auth.ErrorCode!, auth.Message!);

    // Layout: the same client and framework only; an unknown, unpublished or foreign layout gets one indistinct answer.
    var layout = layoutVersionId is Guid explicitLayout
      ? await db.StatementLayoutVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == explicitLayout && x.FirmId == actor.FirmId, ct)
      : await db.StatementLayoutVersions.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.ClientId == package.ClientId && x.FrameworkPolicy == package.Framework &&
          x.Status == StatementLayoutStatuses.Published)
        .OrderByDescending(x => x.VersionNumber).FirstOrDefaultAsync(ct);
    if (layout is null || layout.Status != StatementLayoutStatuses.Published ||
        layout.ClientId != package.ClientId || layout.FrameworkPolicy != package.Framework)
      return CommandResult<FinancialStatementPresentation>.Fail(ErrorCodes.Accounting.MappingIncomplete,
        "No published statement layout applies to this package. Publish a layout for this client and framework before presenting statements.");
    var layoutLines = await StatementLayoutService.LoadLinesAsync(db, actor.FirmId, layout.Id, ct);

    FinancialPackage? comparative = null;
    IReadOnlyList<FinancialPackageLine>? priorLines = null;
    if (package.ComparativePackageId is Guid comparativeId)
    {
      comparative = await db.FinancialPackages.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == comparativeId && x.FirmId == actor.FirmId, ct);
      if (comparative is null || comparative.ClientId != package.ClientId ||
          comparative.Framework != package.Framework || comparative.Currency != package.Currency)
        return CommandResult<FinancialStatementPresentation>.Fail(ErrorCodes.Accounting.PackageInvalid,
          "The comparative package is not on the same client, framework and currency basis.");
      var priorAuth = await AuthorizeAsync(db, actor, comparative.FirmId, comparative.ClientId, comparative.EngagementId, PackageReadRoles, ct);
      if (!priorAuth.Succeeded)
        return CommandResult<FinancialStatementPresentation>.Fail(priorAuth.ErrorCode!, priorAuth.Message!);
      priorLines = await db.FinancialPackageLines.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == comparative.Id).ToListAsync(ct);
    }

    var currentLines = await db.FinancialPackageLines.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id).ToListAsync(ct);
    var currentCashLines = await db.FinancialPackageCashFlowLines.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id).ToListAsync(ct);
    var currentFxEffects = await db.FinancialPackageFxEffects.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id).ToListAsync(ct);
    var typedAccounting = db as IClientAccountingDbContext;
    var currentEquityLines = typedAccounting is null ? [] : await typedAccounting.FinancialPackageEquityLines.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id).ToListAsync(ct);
    var packageIds = comparative is null ? new[] { package.Id } : new[] { package.Id, comparative.Id };
    var packageValidations = await db.FinancialPackageValidations.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && packageIds.Contains(x.FinancialPackageId) && x.Code == "CASH_FLOW_RECONCILED")
      .ToListAsync(ct);
    var noteLines = await db.FinancialPackageNoteLines.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && packageIds.Contains(x.FinancialPackageId)).ToListAsync(ct);
    var disclosures = await db.FinancialPackageDisclosures.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && packageIds.Contains(x.FinancialPackageId)).ToListAsync(ct);
    IReadOnlyList<FinancialPackageCashFlowLine> priorCashLines = [];
    IReadOnlyList<FinancialPackageFxEffect> priorFxEffects = [];
    IReadOnlyList<FinancialPackageEquityLine> priorEquityLines = [];
    if (comparative is not null)
    {
      priorCashLines = await db.FinancialPackageCashFlowLines.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == comparative.Id).ToListAsync(ct);
      priorFxEffects = await db.FinancialPackageFxEffects.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == comparative.Id).ToListAsync(ct);
      priorEquityLines = typedAccounting is null ? [] : await typedAccounting.FinancialPackageEquityLines.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == comparative.Id).ToListAsync(ct);
    }
    var client = await db.PracticeClients.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == package.ClientId, ct);
    if (client is null)
      return CommandResult<FinancialStatementPresentation>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var header = new PresentationHeader(
      client.LegalName, package.Framework, package.Currency, package.PeriodEnd, comparative?.PeriodEnd,
      $"FSP-{package.Id.ToString("N")[..8].ToUpperInvariant()}-{package.PeriodEnd}");
    var manifest = new PresentationManifest(
      package.Id, package.CalculationHash, layout.Id, layout.LayoutHash,
      comparative?.Id, comparative?.CalculationHash,
      FinancialStatementPresentationPolicy.BuilderVersion, FinancialStatementPresentationPolicy.DisplayPolicy,
      await ComputePresentationSourceManifestHashAsync(db, actor.FirmId, packageIds, noteLines, disclosures, ct), package.PeriodId, package.BookId,
      package.Basis, package.Framework, package.TaxonomyVersion, package.TemplateVersion,
      package.CalculationEngineVersion, package.Currency, package.PeriodStart, package.PeriodEnd,
      package.ComparativeBasis, package.ComparativeEvidenceReference);

    var cashAllocations = await db.FinancialPackageCashFlowAllocations.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && packageIds.Contains(x.FinancialPackageId)).ToListAsync(ct);
    var currentSupplementary = ToPresentationSchedules(package, currentCashLines, currentFxEffects, currentEquityLines,
      packageValidations.Where(x => x.FinancialPackageId == package.Id).ToArray(),
      cashAllocations.Where(x => x.FinancialPackageId == package.Id).ToArray());
    var priorSupplementary = comparative is null
      ? null
      : ToPresentationSchedules(comparative, priorCashLines, priorFxEffects, priorEquityLines,
        packageValidations.Where(x => x.FinancialPackageId == comparative.Id).ToArray(),
        cashAllocations.Where(x => x.FinancialPackageId == comparative.Id).ToArray());
    var built = FinancialStatementPresentationBuilder.Build(
      layoutLines,
      currentLines.Select(x => new PresentationBalance(x.DestinationCode, x.StatementSection, x.Amount)).ToList(),
      priorLines?.Select(x => new PresentationBalance(x.DestinationCode, x.StatementSection, x.Amount)).ToList(),
      noteLines.Select(x => new PresentationNoteLine(x.NoteCode, x.FaceDestinationCode, x.Amount, x.FinancialPackageId,
        x.Currency, x.EvidenceReference)).ToList(),
      header,
      manifest,
      currentSupplementary: currentSupplementary,
      priorSupplementary: priorSupplementary,
      disclosures: disclosures.Select(x => new PresentationDisclosureSource(
        x.Code, x.Response, x.NotApplicable, x.Rationale, x.FinancialPackageId)).ToList());
    if (!built.Succeeded || built.Value is null) return built;
    var pinAuthorization = await AuthorizeAsync(db, actor, package.FirmId, package.ClientId, package.EngagementId,
      PreparerRoles, ct);
    return CommandResult<FinancialStatementPresentation>.Ok(built.Value with { CanPin = pinAuthorization.Succeeded });
  }

  private static PresentationSupplementarySchedules ToPresentationSchedules(
    FinancialPackage package,
    IReadOnlyList<FinancialPackageCashFlowLine> cashLines,
    IReadOnlyList<FinancialPackageFxEffect> fxEffects,
    IReadOnlyList<FinancialPackageEquityLine> equityLines,
    IReadOnlyList<FinancialPackageValidation> packageValidations,
    IReadOnlyList<FinancialPackageCashFlowAllocation> cashAllocations) => new(
      package.CashBeginning,
      package.CashEnding,
      CashMovementsWithLineage(cashLines, cashAllocations),

      fxEffects.Select(x => new PresentationFxCashEffect(x.CurrencyPair, x.Amount)).ToList(),
      equityLines.Select(x => new PresentationEquityMovement(x.LineCode, x.Description, x.OpeningAmount, x.ProfitOrLossAmount,
        x.OciAmount, x.CapitalMovementAmount, x.DividendsAmount, x.ClosingAmount)).ToList(),
      packageValidations.Select(x => new PresentationPackageValidation(x.Code, x.Passed, x.Detail)).ToList());

  private static IReadOnlyList<PresentationCashFlowMovement> CashMovementsWithLineage(
    IReadOnlyList<FinancialPackageCashFlowLine> cashLines, IReadOnlyList<FinancialPackageCashFlowAllocation> cashAllocations)
  {
    var resolved = ResolvedCashFlowMovements(cashLines.Select(x => x.Amount).ToArray(),
      cashLines.Select((line, index) => (line, index)).SelectMany(x => cashAllocations
        .Where(a => a.CashFlowLineId == x.line.Id).Select(a => (MovementIndex: x.index, a.SignedAmount))));
    return cashLines.Select((x, index) => new PresentationCashFlowMovement(x.Section, x.Description, x.Amount, x.IsNonCash,
      x.ActivityCode, resolved[index])).ToList();
  }

  // The public/DB field retains its legacy NoteSourceManifestHash name. Version 2 also seals
  // supplementary schedule provenance. A hash proves identity, never source acceptance or review.
  private static async Task<string> ComputePresentationSourceManifestHashAsync(
    IClientAccountingDbContext db, Guid firmId, Guid[] packageIds,
    IReadOnlyList<FinancialPackageNoteLine> noteLines,
    IReadOnlyList<FinancialPackageDisclosure> disclosures, CancellationToken ct)
  {
    var packages = await db.FinancialPackages.AsNoTracking()
      .Where(x => x.FirmId == firmId && packageIds.Contains(x.Id)).ToListAsync(ct);
    var cash = await db.FinancialPackageCashFlowLines.AsNoTracking()
      .Where(x => x.FirmId == firmId && packageIds.Contains(x.FinancialPackageId)).ToListAsync(ct);
    var fx = await db.FinancialPackageFxEffects.AsNoTracking()
      .Where(x => x.FirmId == firmId && packageIds.Contains(x.FinancialPackageId)).ToListAsync(ct);
    var equity = await db.FinancialPackageEquityLines.AsNoTracking()
      .Where(x => x.FirmId == firmId && packageIds.Contains(x.FinancialPackageId)).ToListAsync(ct);
    return ComputePresentationSourceManifestHash(packages, cash, fx, equity,
      ComputeNoteSourceManifestHash(packageIds, noteLines, disclosures));
  }

  internal static string ComputePresentationSourceManifestHash(
    IReadOnlyList<FinancialPackage> packages,
    IReadOnlyList<FinancialPackageCashFlowLine> cash,
    IReadOnlyList<FinancialPackageFxEffect> fx,
    IReadOnlyList<FinancialPackageEquityLine> equity, string noteHash) =>
    Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
    {
      Version = "auditsphere.statement-source-manifest.v2",
      NoteHash = noteHash,
      Packages = packages.OrderBy(x => x.Id).Select(x => new
      {
        x.Id, x.ClientId, x.EngagementId, x.PeriodId, x.BookId, x.Currency, x.PeriodStart, x.PeriodEnd,
        x.SupplementaryHash, x.CashBeginning, x.CashEnding,
        CashAccountCodes = x.CashAccountCodes.OrderBy(v => v, StringComparer.Ordinal).ToArray(),
        OpeningEvidence = x.OpeningCashEvidenceReferences.OrderBy(v => v, StringComparer.Ordinal).ToArray(),
        ClosingEvidence = x.ClosingCashEvidenceReferences.OrderBy(v => v, StringComparer.Ordinal).ToArray()
      }).ToArray(),
      Cash = cash.OrderBy(x => x.FinancialPackageId).ThenBy(x => x.Id).Select(x => new
      {
        x.Id, x.FinancialPackageId, x.ClientId, x.EngagementId, x.Currency, x.Section, x.Description,
        x.Amount, x.IsNonCash, x.MovementDate, x.ActivityCode, x.ClassificationRationale,
        Sources = x.SourceReferences.OrderBy(v => v, StringComparer.Ordinal).ToArray()
      }).ToArray(),
      Fx = fx.OrderBy(x => x.FinancialPackageId).ThenBy(x => x.Id).Select(x => new
      {
        x.Id, x.FinancialPackageId, x.ClientId, x.EngagementId, x.Currency,
        x.CurrencyPair, x.Amount, x.EvidenceReference
      }).ToArray(),
      Equity = equity.OrderBy(x => x.FinancialPackageId).ThenBy(x => x.Id).Select(x => new
      {
        x.Id, x.FinancialPackageId, x.ClientId, x.EngagementId, x.Currency, x.LineCode, x.Description,
        x.OpeningAmount, x.ProfitOrLossAmount, x.OciAmount, x.CapitalMovementAmount,
        x.DividendsAmount, x.ClosingAmount, x.EvidenceReference
      }).ToArray()
    }));

  private static string ComputeNoteSourceManifestHash(
    IReadOnlyCollection<Guid> packageIds,
    IReadOnlyList<FinancialPackageNoteLine> noteLines,
    IReadOnlyList<FinancialPackageDisclosure> disclosures)
  {
    var packages = packageIds.Distinct().OrderBy(x => x).Select(packageId => new
    {
      PackageId = packageId,
      NoteLines = noteLines.Where(x => x.FinancialPackageId == packageId).OrderBy(x => x.NoteCode, StringComparer.Ordinal)
        .ThenBy(x => x.FaceDestinationCode, StringComparer.Ordinal).ThenBy(x => x.Id)
        .Select(x => new { x.Id, x.NoteCode, x.FaceDestinationCode, x.Amount, x.Currency, x.EvidenceReference }).ToArray(),
      Disclosures = disclosures.Where(x => x.FinancialPackageId == packageId).OrderBy(x => x.Code, StringComparer.Ordinal)
        .ThenBy(x => x.Id).Select(x => new { x.Id, x.Code, x.Response, x.NotApplicable, x.Rationale }).ToArray()
    }).ToArray();
    return Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
    {
      Version = "auditsphere.statement-note-source-manifest.v1",
      Packages = packages
    }));
  }

  /// <summary>Renders the presentation as PDF, Word or Excel. Repeating the request yields identical bytes.</summary>
  public static async Task<CommandResult<FinancialStatementPresentationExport>> RenderPresentationAsync(
    IClientAccountingDbContext db,
    ActorContext actor,
    Guid packageId,
    string format,
    Guid? layoutVersionId = null,
    CancellationToken ct = default)
  {
    var normalized = (format ?? string.Empty).Trim().ToLowerInvariant();
    var contentType = FinancialStatementPresentationRenderer.ContentTypeFor(normalized);
    if (contentType is null)
      return CommandResult<FinancialStatementPresentationExport>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "Choose PDF, Word or Excel for the statement presentation.");
    var presentation = await BuildPresentationAsync(db, actor, packageId, layoutVersionId, ct);
    if (!presentation.Succeeded || presentation.Value is null)
      return CommandResult<FinancialStatementPresentationExport>.Fail(presentation.ErrorCode!, presentation.Message!);
    var bytes = FinancialStatementPresentationRenderer.Render(presentation.Value, normalized);
    return CommandResult<FinancialStatementPresentationExport>.Ok(new FinancialStatementPresentationExport(
      $"auditsphere-statement-presentation-{packageId:D}.{normalized}", contentType, Hashing.Sha256Hex(bytes), bytes));
  }
}
