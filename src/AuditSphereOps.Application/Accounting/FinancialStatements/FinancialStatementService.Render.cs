using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class FinancialStatementService
{
  /// <summary>
  /// Deterministically renders the complete financial-statement package artifact to canonical UTF-8 bytes
  /// and computes its SHA-256 digest.
  /// </summary>
  public static FinancialStatementPackageArtifact RenderPackageArtifact(
    FinancialPackage package,
    IReadOnlyCollection<FinancialPackageLine> lines,
    IReadOnlyCollection<FinancialPackageCashFlowLine> cashFlowLines,
    IReadOnlyCollection<FinancialPackageDisclosure> disclosures,
    IReadOnlyCollection<FinancialPackageValidation> validations,
    IReadOnlyCollection<FinancialPackageEquityLine>? equityLines = null,
    IReadOnlyCollection<FinancialPackageNoteLine>? noteLines = null)
  {
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("=== AUDITSPHEREOPS FINANCIAL STATEMENT PACKAGE ===");
    sb.AppendLine($"Package ID: {package.Id:D}");
    sb.AppendLine($"Firm ID: {package.FirmId:D}");
    sb.AppendLine($"Client ID: {package.ClientId:D}");
    sb.AppendLine($"Engagement ID: {package.EngagementId:D}");
    sb.AppendLine($"Adjusted Snapshot ID: {package.AdjustedDatasetId:D}");
    sb.AppendLine($"Mapping Version ID: {package.MappingVersionId:D}");
    sb.AppendLine($"Adjustment Plan ID: {package.AdjustmentPlanId:D}");
    sb.AppendLine($"Framework: {package.Framework}");
    sb.AppendLine($"Reporting Period: {package.PeriodStart} to {package.PeriodEnd}");
    sb.AppendLine($"Currency: {package.Currency}");
    sb.AppendLine($"Taxonomy Version: {package.TaxonomyVersion}");
    sb.AppendLine($"Template Version: {package.TemplateVersion}");
    sb.AppendLine($"Engine Version: {package.CalculationEngineVersion}");
    sb.AppendLine($"Calculation Hash: {package.CalculationHash}");
    if (package.SupplementaryHash is not null)
      sb.AppendLine($"Supplementary Hash: {package.SupplementaryHash}");
    if (package.EquityHash is not null)
      sb.AppendLine($"Equity Hash: {package.EquityHash}");
    if (package.ComparativePackageId is not null)
      sb.AppendLine($"Comparative Package: {package.ComparativePackageId:D} | {package.ComparativeBasis} | {package.ComparativeEvidenceReference}");
    sb.AppendLine($"Status: {package.Status}");
    sb.AppendLine();

    sb.AppendLine("--- STATEMENT LINES ---");
    foreach (var line in lines.OrderBy(x => x.StatementSection, StringComparer.Ordinal)
      .ThenBy(x => x.DestinationCode, StringComparer.Ordinal)
      .ThenBy(x => x.SourceAccountCode, StringComparer.Ordinal))
    {
      sb.AppendLine($"{line.StatementSection} | {line.DestinationCode} | {line.SourceAccountCode} | {line.Amount.ToString("0.000000", CultureInfo.InvariantCulture)} {line.Currency} | {line.Fraction.ToString("0.000000", CultureInfo.InvariantCulture)} | residual={line.RoundingResidual.ToString("0.000000", CultureInfo.InvariantCulture)}");
    }
    sb.AppendLine();

    sb.AppendLine("--- STATEMENT TOTALS ---");
    var totals = lines.GroupBy(x => x.StatementSection, StringComparer.Ordinal)
      .OrderBy(x => x.Key, StringComparer.Ordinal);
    foreach (var grp in totals)
    {
      var sum = MoneyPolicy.Normalize(grp.Sum(x => x.Amount));
      sb.AppendLine($"{grp.Key}: {sum.ToString("0.000000", CultureInfo.InvariantCulture)} {package.Currency}");
    }
    sb.AppendLine();

    if (cashFlowLines.Count > 0)
    {
      sb.AppendLine("--- CASH FLOW RECONCILIATION ---");
      sb.AppendLine($"Beginning Cash: {package.CashBeginning?.ToString("0.000000", CultureInfo.InvariantCulture)} {package.Currency}");
      sb.AppendLine($"Ending Cash: {package.CashEnding?.ToString("0.000000", CultureInfo.InvariantCulture)} {package.Currency}");
      foreach (var cf in cashFlowLines.OrderBy(x => x.Section, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Description, StringComparer.Ordinal))
      {
        sb.AppendLine($"{cf.Section} | {cf.Description} | {cf.Amount.ToString("0.000000", CultureInfo.InvariantCulture)} {cf.Currency}");
      }
      sb.AppendLine();
    }

    if (disclosures.Count > 0)
    {
      sb.AppendLine("--- DISCLOSURES ---");
      foreach (var disc in disclosures.OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase))
      {
        var resp = disc.NotApplicable ? $"[NOT APPLICABLE: {disc.Rationale}]" : disc.Response;
        sb.AppendLine($"{disc.Code}: {resp}");
      }
      sb.AppendLine();
    }

    if (equityLines is { Count: > 0 })
    {
      sb.AppendLine("--- STATEMENT OF CHANGES IN EQUITY ---");
      foreach (var equity in equityLines.OrderBy(x => x.LineCode, StringComparer.OrdinalIgnoreCase))
        sb.AppendLine($"{equity.LineCode} | {equity.Description} | opening={equity.OpeningAmount.ToString("0.000000", CultureInfo.InvariantCulture)} | profit/loss={equity.ProfitOrLossAmount.ToString("0.000000", CultureInfo.InvariantCulture)} | oci={equity.OciAmount.ToString("0.000000", CultureInfo.InvariantCulture)} | capital={equity.CapitalMovementAmount.ToString("0.000000", CultureInfo.InvariantCulture)} | dividends={equity.DividendsAmount.ToString("0.000000", CultureInfo.InvariantCulture)} | closing={equity.ClosingAmount.ToString("0.000000", CultureInfo.InvariantCulture)} | {equity.Currency} | evidence={equity.EvidenceReference}");
      sb.AppendLine();
    }

    if (noteLines is { Count: > 0 })
    {
      sb.AppendLine("--- STRUCTURED NOTE CROSS-CASTS ---");
      foreach (var note in noteLines.OrderBy(x => x.NoteCode, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.FaceDestinationCode, StringComparer.OrdinalIgnoreCase))
        sb.AppendLine($"{note.NoteCode} | {note.FaceDestinationCode} | {note.Amount.ToString("0.000000", CultureInfo.InvariantCulture)} {note.Currency} | evidence={note.EvidenceReference}");
      sb.AppendLine();
    }

    sb.AppendLine("--- VALIDATIONS ---");
    foreach (var val in validations.OrderBy(x => x.Code, StringComparer.Ordinal))
    {
      sb.AppendLine($"{val.Code} | {(val.Passed ? "PASS" : "REVIEW")} | {val.Detail}");
    }

    var text = sb.ToString();
    var bytes = System.Text.Encoding.UTF8.GetBytes(text);
    var sha256 = Hashing.Sha256Hex(bytes);
    return new FinancialStatementPackageArtifact(package.Id, package.CalculationHash, sha256, bytes, text);
  }

  /// <summary>
  /// Loads a package and its related projections from the database and renders the deterministic package artifact.
  /// </summary>
  public static async Task<CommandResult<FinancialStatementPackageArtifact>> RenderPackageArtifactAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid packageId,
    CancellationToken ct = default)
  {
    var package = await db.FinancialPackages.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == packageId && x.FirmId == actor.FirmId, ct);
    if (package is null)
      return CommandResult<FinancialStatementPackageArtifact>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizeAsync(db, actor, package.FirmId, package.ClientId, package.EngagementId, PreparerRoles.ToArray(), ct);
    if (!auth.Succeeded)
      return CommandResult<FinancialStatementPackageArtifact>.Fail(auth.ErrorCode!, auth.Message!);

    var lines = await db.FinancialPackageLines.AsNoTracking()
      .Where(x => x.FinancialPackageId == package.Id && x.FirmId == actor.FirmId)
      .ToListAsync(ct);
    var cashFlowLines = await db.FinancialPackageCashFlowLines.AsNoTracking()
      .Where(x => x.FinancialPackageId == package.Id && x.FirmId == actor.FirmId)
      .ToListAsync(ct);
    var disclosures = await db.FinancialPackageDisclosures.AsNoTracking()
      .Where(x => x.FinancialPackageId == package.Id && x.FirmId == actor.FirmId)
      .ToListAsync(ct);
    var typed = db as IClientAccountingDbContext;
    IReadOnlyCollection<FinancialPackageEquityLine> equityLines = typed is null ? [] : await typed.FinancialPackageEquityLines.AsNoTracking()
      .Where(x => x.FinancialPackageId == package.Id && x.FirmId == actor.FirmId)
      .ToListAsync(ct);
    IReadOnlyCollection<FinancialPackageNoteLine> noteLines = typed is null ? [] : await typed.FinancialPackageNoteLines.AsNoTracking()
      .Where(x => x.FinancialPackageId == package.Id && x.FirmId == actor.FirmId)
      .ToListAsync(ct);
    var validations = await db.FinancialPackageValidations.AsNoTracking()
      .Where(x => x.FinancialPackageId == package.Id && x.FirmId == actor.FirmId)
      .ToListAsync(ct);

    var artifact = RenderPackageArtifact(package, lines, cashFlowLines, disclosures, validations, equityLines, noteLines);
    var stored = await db.FinancialPackageArtifacts.SingleOrDefaultAsync(x =>
      x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.EngagementId == package.EngagementId &&
      x.FinancialPackageId == package.Id && x.PackageRevision == package.Revision &&
      x.PackageGeneration == package.Generation && x.PackageHash == package.CalculationHash &&
      x.ArtifactVersion == FinancialPackageArtifactVersions.Text, ct);
    if (stored is null)
    {
      stored = new FinancialPackageArtifact
      {
        Id = Guid.CreateVersion7(), FirmId = package.FirmId, ClientId = package.ClientId,
        EngagementId = package.EngagementId, FinancialPackageId = package.Id,
        PackageRevision = package.Revision, PackageGeneration = package.Generation,
        PackageHash = package.CalculationHash, ArtifactVersion = FinancialPackageArtifactVersions.Text,
        FrameworkVersion = package.Framework, TemplateVersion = package.TemplateVersion,
        ArtifactSha256Hex = artifact.ArtifactSha256Hex, ArtifactBytes = artifact.ArtifactBytes,
        CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
      };
      db.FinancialPackageArtifacts.Add(stored);
      try
      {
        await db.SaveChangesAsync(ct);
      }
      catch (DbUpdateException)
      {
        return CommandResult<FinancialStatementPackageArtifact>.Fail(ErrorCodes.IdempotencyConflict,
          "The exact package artifact was rendered concurrently; reload and retry.");
      }
    }
    else if (stored.ArtifactSha256Hex != artifact.ArtifactSha256Hex ||
             !stored.ArtifactBytes.SequenceEqual(artifact.ArtifactBytes) ||
             !string.Equals(stored.FrameworkVersion, package.Framework, StringComparison.Ordinal) ||
             !string.Equals(stored.TemplateVersion, package.TemplateVersion, StringComparison.Ordinal))
    {
      return CommandResult<FinancialStatementPackageArtifact>.Fail(ErrorCodes.StaleRevision,
        "The stored package artifact does not match the current deterministic export.");
    }

    return CommandResult<FinancialStatementPackageArtifact>.Ok(artifact with { ArtifactId = stored.Id });
  }

  public static async Task<CommandResult<FinancialPackageOfficeArtifact>> RenderPackageOfficeArtifactAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid packageId,
    string artifactVersion,
    CancellationToken ct = default)
  {
    if (artifactVersion is not (FinancialPackageArtifactVersions.Workbook or
        FinancialPackageArtifactVersions.ControlledWorkbook or FinancialPackageArtifactVersions.Word or
        FinancialPackageArtifactVersions.Pdf))
      return CommandResult<FinancialPackageOfficeArtifact>.Fail(ErrorCodes.Accounting.PackageInvalid, "Unsupported export artifact version.");

    var canonical = await RenderPackageArtifactAsync(db, actor, packageId, ct);
    if (!canonical.Succeeded || canonical.Value is null)
      return CommandResult<FinancialPackageOfficeArtifact>.Fail(canonical.ErrorCode!, canonical.Message!);

    var package = await db.FinancialPackages.AsNoTracking().SingleAsync(x =>
      x.Id == packageId && x.FirmId == actor.FirmId, ct);
    FinancialPackageOfficeArtifact artifact;
    try { artifact = FinancialPackageOfficeRenderer.Render(artifactVersion, canonical.Value.RenderedText); }
    catch (InvalidOperationException ex)
    {
      return CommandResult<FinancialPackageOfficeArtifact>.Fail(ErrorCodes.Accounting.PackageInvalid, ex.Message);
    }
    var stored = await db.FinancialPackageArtifacts.SingleOrDefaultAsync(x =>
      x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.EngagementId == package.EngagementId &&
      x.FinancialPackageId == package.Id && x.PackageRevision == package.Revision &&
      x.PackageGeneration == package.Generation && x.PackageHash == package.CalculationHash &&
      x.ArtifactVersion == artifactVersion, ct);
    if (stored is null)
    {
      stored = new FinancialPackageArtifact
      {
        Id = Guid.CreateVersion7(), FirmId = package.FirmId, ClientId = package.ClientId,
        EngagementId = package.EngagementId, FinancialPackageId = package.Id,
        PackageRevision = package.Revision, PackageGeneration = package.Generation,
        PackageHash = package.CalculationHash, ArtifactVersion = artifactVersion,
        FrameworkVersion = package.Framework, TemplateVersion = package.TemplateVersion,
        ArtifactSha256Hex = artifact.ArtifactSha256Hex, ArtifactBytes = artifact.ArtifactBytes,
        CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
      };
      db.FinancialPackageArtifacts.Add(stored);
      try
      {
        await db.SaveChangesAsync(ct);
      }
      catch (DbUpdateException)
      {
        return CommandResult<FinancialPackageOfficeArtifact>.Fail(ErrorCodes.IdempotencyConflict,
          "The exact Office artifact was rendered concurrently; reload and retry.");
      }
    }
    else if (stored.ArtifactSha256Hex != artifact.ArtifactSha256Hex ||
             !stored.ArtifactBytes.SequenceEqual(artifact.ArtifactBytes) ||
             !string.Equals(stored.FrameworkVersion, package.Framework, StringComparison.Ordinal) ||
             !string.Equals(stored.TemplateVersion, package.TemplateVersion, StringComparison.Ordinal))
    {
      return CommandResult<FinancialPackageOfficeArtifact>.Fail(ErrorCodes.StaleRevision,
        "The stored Office artifact does not match the current deterministic export.");
    }

    return CommandResult<FinancialPackageOfficeArtifact>.Ok(artifact with { ArtifactId = stored.Id });
  }

  public static async Task<CommandResult<FinancialStatementPackageArtifact>> GetStoredPackageArtifactAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid packageId,
    CancellationToken ct = default)
  {
    var package = await db.FinancialPackages.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == packageId && x.FirmId == actor.FirmId, ct);
    if (package is null)
      return CommandResult<FinancialStatementPackageArtifact>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizeAsync(db, actor, package.FirmId, package.ClientId, package.EngagementId, PackageReadRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<FinancialStatementPackageArtifact>.Fail(auth.ErrorCode!, auth.Message!);

    var stored = await db.FinancialPackageArtifacts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.EngagementId == package.EngagementId &&
      x.FinancialPackageId == package.Id && x.PackageRevision == package.Revision &&
      x.PackageGeneration == package.Generation && x.PackageHash == package.CalculationHash &&
      x.ArtifactVersion == FinancialPackageArtifactVersions.Text, ct);
    if (stored is null || stored.ArtifactBytes.Length == 0 ||
        !string.Equals(stored.ArtifactSha256Hex, Hashing.Sha256Hex(stored.ArtifactBytes), StringComparison.Ordinal))
      return CommandResult<FinancialStatementPackageArtifact>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "The current persisted package artifact is unavailable or failed its integrity check.");

    return CommandResult<FinancialStatementPackageArtifact>.Ok(new FinancialStatementPackageArtifact(
      package.Id, package.CalculationHash, stored.ArtifactSha256Hex, stored.ArtifactBytes,
      System.Text.Encoding.UTF8.GetString(stored.ArtifactBytes), stored.Id));
  }
}
