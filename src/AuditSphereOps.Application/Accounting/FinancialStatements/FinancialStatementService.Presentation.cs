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
    var noteLines = await db.FinancialPackageNoteLines.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id).ToListAsync(ct);
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
      FinancialStatementPresentationPolicy.BuilderVersion, FinancialStatementPresentationPolicy.DisplayPolicy);

    var currentSupplementary = ToPresentationSchedules(package, currentCashLines, currentFxEffects, currentEquityLines);
    var priorSupplementary = comparative is null
      ? null
      : ToPresentationSchedules(comparative, priorCashLines, priorFxEffects, priorEquityLines);
    return FinancialStatementPresentationBuilder.Build(
      layoutLines,
      currentLines.Select(x => new PresentationBalance(x.DestinationCode, x.StatementSection, x.Amount)).ToList(),
      priorLines?.Select(x => new PresentationBalance(x.DestinationCode, x.StatementSection, x.Amount)).ToList(),
      noteLines.Select(x => new PresentationNoteLine(x.NoteCode, x.FaceDestinationCode, x.Amount)).ToList(),
      header,
      manifest,
      currentSupplementary: currentSupplementary,
      priorSupplementary: priorSupplementary);
  }

  private static PresentationSupplementarySchedules ToPresentationSchedules(
    FinancialPackage package,
    IReadOnlyList<FinancialPackageCashFlowLine> cashLines,
    IReadOnlyList<FinancialPackageFxEffect> fxEffects,
    IReadOnlyList<FinancialPackageEquityLine> equityLines) => new(
      package.CashBeginning,
      package.CashEnding,
      cashLines.Select(x => new PresentationCashFlowMovement(x.Section, x.Description, x.Amount, x.IsNonCash, x.ActivityCode)).ToList(),
      fxEffects.Select(x => new PresentationFxCashEffect(x.CurrencyPair, x.Amount)).ToList(),
      equityLines.Select(x => new PresentationEquityMovement(x.LineCode, x.Description, x.OpeningAmount, x.ProfitOrLossAmount,
        x.OciAmount, x.CapitalMovementAmount, x.DividendsAmount, x.ClosingAmount)).ToList());

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
