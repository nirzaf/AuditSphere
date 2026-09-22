using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record MappingAllocationInput(
  string SourceAccountCode,
  string DestinationCode,
  string StatementSection,
  decimal Fraction,
  string Rationale,
  string? AuditArea = null,
  string ResidualPolicy = "LAST_DESTINATION");

public sealed record CreateMappingVersionRequest(
  Guid DatasetId,
  string TaxonomyVersion,
  string PeriodStart,
  string PeriodEnd,
  IReadOnlyList<MappingAllocationInput> Allocations,
  Guid? ClientChartVersionId = null);

public sealed record BuildFinancialPackageRequest(
  Guid AdjustmentPlanId,
  Guid MappingVersionId,
  string Framework,
  string PeriodStart,
  string PeriodEnd,
  string TemplateVersion,
  FinancialSupplementaryInformation? SupplementaryInformation = null);

public sealed record FinancialSupplementaryInformation(
  decimal CashBeginning,
  decimal CashEnding,
  IReadOnlyList<CashFlowLineInput> CashFlowLines,
  IReadOnlyList<DisclosureInput> Disclosures,
  IReadOnlyList<EquityLineInput>? EquityLines = null,
  FinancialComparativeInput? Comparative = null,
  IReadOnlyList<NoteLineInput>? NoteLines = null);

public sealed record CashFlowLineInput(string Section, string Description, decimal Amount);

public sealed record DisclosureInput(string Code, string Response, bool NotApplicable = false, string? Rationale = null);

public sealed record EquityLineInput(
  string LineCode,
  string Description,
  decimal OpeningAmount,
  decimal ProfitOrLossAmount,
  decimal OciAmount,
  decimal CapitalMovementAmount,
  decimal DividendsAmount,
  decimal ClosingAmount,
  string EvidenceReference);

public sealed record FinancialComparativeInput(Guid PackageId, string Basis, string EvidenceReference);

public sealed record NoteLineInput(string NoteCode, string FaceDestinationCode, decimal Amount, string EvidenceReference);

public sealed record FinancialPackageBuildResult(
  Guid PackageId,
  Guid AdjustedSnapshotId,
  string Status,
  string CalculationHash,
  IReadOnlyDictionary<string, decimal> StatementTotals);

public sealed record FinancialStatementPackageArtifact(
  Guid PackageId,
  string CalculationHash,
  string ArtifactSha256Hex,
  byte[] ArtifactBytes,
  string RenderedText,
  Guid ArtifactId = default);

/// <summary>
/// Versioned mapping and deterministic financial-statement calculation over the
/// accepted TB/adjustment inputs. Rendering, disclosures and provider delivery stay
/// separate; missing supplementary information is persisted as an explicit review gate.
/// </summary>
public static class FinancialStatementService
{
  private static readonly string[] PreparerRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReviewerRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<Guid>> CreateMappingVersionAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    CreateMappingVersionRequest request,
    CancellationToken ct = default)
  {
    var invalid = ValidateMappingRequest(request);
    if (invalid is not null)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, invalid);

    var dataset = await db.TrialBalanceDatasets.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == request.DatasetId, ct);
    if (dataset is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (dataset.ValidationStatus != "Accepted" || !dataset.Balanced || dataset.ControlTotal != 0m)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Mappings require an accepted balanced dataset.");

    var auth = await AuthorizeAsync(db, actor, dataset.FirmId, dataset.ClientId, dataset.EngagementId, PreparerRoles.ToArray(), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    Guid? chartVersionId = request.ClientChartVersionId;
    if (db is IClientAccountingDbContext typed)
    {
      var approvedCharts = await typed.ClientChartVersions.AsNoTracking().Where(x =>
        x.FirmId == dataset.FirmId && x.ClientId == dataset.ClientId && x.Status == AccountingWorkflowStates.Approved).ToListAsync(ct);
      if (chartVersionId is null && approvedCharts.Count > 0)
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
          "An approved client chart version is required for mapping applicability.");
      if (chartVersionId is { } requestedChartId)
      {
        var chart = approvedCharts.SingleOrDefault(x => x.Id == requestedChartId);
        if (chart is null)
          return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
            "The mapping chart must be an approved version in the same client scope.");
        if (!DateOnly.TryParse(request.PeriodStart, CultureInfo.InvariantCulture, DateTimeStyles.None, out var periodStart) ||
            !DateOnly.TryParse(request.PeriodEnd, CultureInfo.InvariantCulture, DateTimeStyles.None, out var periodEnd) ||
            chart.EffectiveFrom > periodStart || chart.EffectiveTo is { } chartEnd && chartEnd < periodEnd)
          return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
            "The approved client chart is not effective for the mapping period.");
      }
    }
    var taxonomyError = await ValidateApprovedTaxonomyAsync(db, dataset.FirmId, request.TaxonomyVersion, request.Allocations, ct);
    if (taxonomyError is not null)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, taxonomyError);

    var rows = await db.TrialBalanceRows.AsNoTracking()
      .Where(x => x.DatasetId == dataset.Id)
      .Select(x => new SourceBalance(x.AccountCode, x.Amount))
      .ToListAsync(ct);
    var mappingError = ValidateAllocations(rows, request.Allocations);
    if (mappingError is not null)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingIncomplete, mappingError);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var client = await LockClientAsync(db, actor.FirmId, dataset.ClientId, ct);
    if (client is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");

    var version = (await db.MappingVersions
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == dataset.EngagementId)
      .Select(x => (long?)x.Version).MaxAsync(ct) ?? 0) + 1;
    var mapping = new MappingVersion
    {
      Id = Guid.CreateVersion7(), FirmId = dataset.FirmId, ClientId = dataset.ClientId,
      EngagementId = dataset.EngagementId, DatasetId = dataset.Id, ClientChartVersionId = chartVersionId, Version = version,
      Generation = client.InputGeneration, TaxonomyVersion = request.TaxonomyVersion.Trim(),
      PeriodStart = request.PeriodStart.Trim(), PeriodEnd = request.PeriodEnd.Trim(),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.MappingVersions.Add(mapping);
    foreach (var input in request.Allocations)
      db.MappingAllocations.Add(ToAllocation(mapping, input));

    try
    {
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      return CommandResult<Guid>.Ok(mapping.Id);
    }
    catch (DbUpdateException)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict,
        "The mapping version identity changed; reload the current dataset.");
    }
  }

  public static async Task<CommandResult> ApproveMappingAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid mappingVersionId,
    long expectedVersion,
    CancellationToken ct = default)
  {
    if (mappingVersionId == Guid.Empty || expectedVersion < 1)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "A mapping version and positive expected version are required.");

    var snapshot = await db.MappingVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == mappingVersionId && x.FirmId == actor.FirmId, ct);
    if (snapshot is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, snapshot.FirmId, snapshot.ClientId, snapshot.EngagementId, ReviewerRoles.ToArray(), ct);
    if (!auth.Succeeded)
      return auth;
    if (snapshot.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid,
        "The mapping preparer cannot approve the same mapping version.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var client = await LockClientAsync(db, actor.FirmId, snapshot.ClientId, ct);
    if (client is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    var mapping = await db.MappingVersions
      .FromSqlInterpolated($"SELECT * FROM mapping_versions WHERE id = {mappingVersionId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (mapping is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (mapping.Version != expectedVersion)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "The mapping version changed; reload it.");
    if (mapping.Status == AccountingPackageStates.MappingApproved)
      return CommandResult.Ok();
    if (mapping.Status != AccountingPackageStates.MappingDraft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft mapping can be approved.");

    var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == mapping.DatasetId && x.FirmId == mapping.FirmId, ct);
    if (dataset is null || dataset.ClientId != mapping.ClientId || dataset.EngagementId != mapping.EngagementId ||
        dataset.ValidationStatus != "Accepted" || !dataset.Balanced || dataset.ControlTotal != 0m)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The mapping dataset is no longer eligible.");
    if (mapping.ClientChartVersionId is { } chartVersionId && db is IClientAccountingDbContext typedCharts)
    {
      var chart = await typedCharts.ClientChartVersions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == mapping.FirmId && x.ClientId == mapping.ClientId && x.Id == chartVersionId &&
        x.Status == AccountingWorkflowStates.Approved, ct);
      if (chart is null || !DateOnly.TryParse(mapping.PeriodStart, CultureInfo.InvariantCulture, DateTimeStyles.None, out var periodStart) ||
          !DateOnly.TryParse(mapping.PeriodEnd, CultureInfo.InvariantCulture, DateTimeStyles.None, out var periodEnd) ||
          chart.EffectiveFrom > periodStart || chart.EffectiveTo is { } chartEnd && chartEnd < periodEnd)
        return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid,
          "The approved client chart applicability changed; create a new mapping version.");
    }
    var rows = await db.TrialBalanceRows.AsNoTracking()
      .Where(x => x.DatasetId == mapping.DatasetId)
      .Select(x => new SourceBalance(x.AccountCode, x.Amount)).ToListAsync(ct);
    var allocations = await db.MappingAllocations.AsNoTracking()
      .Where(x => x.FirmId == mapping.FirmId && x.MappingVersionId == mapping.Id)
      .Select(x => new MappingAllocationInput(x.SourceAccountCode, x.DestinationCode,
        x.StatementSection, x.Fraction, x.Rationale, x.AuditArea)).ToListAsync(ct);
    var mappingError = ValidateAllocations(rows, allocations);
    if (mappingError is not null)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingIncomplete, mappingError);
    var taxonomyError = await ValidateApprovedTaxonomyAsync(db, mapping.FirmId, mapping.TaxonomyVersion, allocations, ct);
    if (taxonomyError is not null)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, taxonomyError);

    client.InputGeneration++;
    mapping.Generation = client.InputGeneration;
    mapping.Status = AccountingPackageStates.MappingApproved;
    mapping.ApprovedByUserId = actor.UserId;
    mapping.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<FinancialPackageBuildResult>> BuildFinancialPackageAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    BuildFinancialPackageRequest request,
    CancellationToken ct = default)
  {
    var invalid = ValidatePackageRequest(request);
    if (invalid is not null)
      return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.Accounting.PackageInvalid, invalid);

    var mapping = await db.MappingVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.MappingVersionId && x.FirmId == actor.FirmId, ct);
    var plan = await db.AdjustmentPlans.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.AdjustmentPlanId && x.FirmId == actor.FirmId, ct);
    if (mapping is null || plan is null || mapping.FirmId != plan.FirmId ||
        mapping.ClientId != plan.ClientId || mapping.EngagementId != plan.EngagementId ||
        mapping.DatasetId != plan.BaseDatasetId)
      return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (mapping.Status != AccountingPackageStates.MappingApproved)
      return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.GateBlocked, "An approved mapping is required.");
    if (plan.Status != "Finalized" || string.IsNullOrWhiteSpace(plan.ResultHash))
      return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.GateBlocked, "A finalized adjustment plan is required.");

    var auth = await AuthorizeAsync(db, actor, mapping.FirmId, mapping.ClientId, mapping.EngagementId, PreparerRoles.ToArray(), ct);
    if (!auth.Succeeded)
      return CommandResult<FinancialPackageBuildResult>.Fail(auth.ErrorCode!, auth.Message!);

    var calculated = await CalculateAdjustedBalancesAsync(db, plan, ct);
    if (!calculated.Succeeded)
      return CommandResult<FinancialPackageBuildResult>.Fail(calculated.ErrorCode!, calculated.Message!);
    if (calculated.Value!.ResultHash != plan.ResultHash)
      return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.StaleRevision,
        "The finalized adjustment plan no longer matches its stored calculation hash.");

    var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == plan.BaseDatasetId, ct);
    if (dataset is null || dataset.Currency.Length != 3 || dataset.Currency != dataset.Currency.ToUpperInvariant())
      return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.GateBlocked, "The source dataset is unavailable.");
    var typedAccounting = db as IClientAccountingDbContext;
    var datasetBasis = dataset.Basis?.Trim().ToUpperInvariant();
    var hasReportingContext = dataset.PeriodId is not null || dataset.BookId is not null || !string.IsNullOrWhiteSpace(datasetBasis);
    if (hasReportingContext)
    {
      if (typedAccounting is null || dataset.PeriodId is null || string.IsNullOrWhiteSpace(datasetBasis))
        return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.GateBlocked,
          "The source dataset has incomplete reporting context.");
      var period = await typedAccounting.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == dataset.PeriodId && x.FirmId == mapping.FirmId && x.ClientId == mapping.ClientId, ct);
      if (period is null || !string.Equals(period.Basis, datasetBasis, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(period.Currency, dataset.Currency, StringComparison.Ordinal))
        return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.GateBlocked,
          "The source dataset reporting context is unavailable or inconsistent.");
      if (!string.Equals(request.PeriodStart.Trim(), period.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal) ||
          !string.Equals(request.PeriodEnd.Trim(), period.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal))
        return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.GateBlocked,
          "The package period must match the selected client reporting period.");
      if (dataset.BookId is { } bookId)
      {
        var book = await typedAccounting.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(x =>
          x.Id == bookId && x.FirmId == mapping.FirmId && x.ClientId == mapping.ClientId && x.PeriodId == period.Id, ct);
        if (book is null || !string.Equals(book.Basis, datasetBasis, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(book.Currency, dataset.Currency, StringComparison.Ordinal))
          return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.GateBlocked,
            "The source dataset reporting book is unavailable or inconsistent.");
      }
    }
    var allocations = await db.MappingAllocations.AsNoTracking()
      .Where(x => x.FirmId == mapping.FirmId && x.MappingVersionId == mapping.Id)
      .Select(x => new MappingAllocationInput(x.SourceAccountCode, x.DestinationCode,
        x.StatementSection, x.Fraction, x.Rationale, x.AuditArea)).ToListAsync(ct);
    var allocationError = ValidateAllocations(
      calculated.Value.Balances.Select(x => new SourceBalance(x.Key, x.Value)).ToList(), allocations);
    if (allocationError is not null)
      return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.Accounting.MappingIncomplete, allocationError);

    var supplementaryError = ValidateSupplementaryInformation(request.SupplementaryInformation);
    if (supplementaryError is not null)
      return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.Accounting.PackageSupplementaryInvalid, supplementaryError);
    if ((request.SupplementaryInformation?.EquityLines is not null || request.SupplementaryInformation?.NoteLines is not null) && typedAccounting is null)
      return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.GateBlocked,
        "Structured equity and note package outputs require the client-accounting persistence surface.");

    FinancialPackage? comparative = null;
    if (request.SupplementaryInformation?.Comparative is { } comparativeInput)
    {
      comparative = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == comparativeInput.PackageId && x.FirmId == mapping.FirmId, ct);
      if (comparative is null ||
          comparative.ClientId != mapping.ClientId || comparative.EngagementId != mapping.EngagementId ||
          comparative.Status != AccountingPackageStates.PackageValidated ||
          !string.Equals(comparative.Framework, request.Framework.Trim(), StringComparison.Ordinal) ||
          !string.Equals(comparative.Currency, dataset.Currency, StringComparison.Ordinal) ||
          string.CompareOrdinal(comparative.PeriodEnd, request.PeriodStart.Trim()) >= 0)
        return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.Accounting.PackageSupplementaryInvalid,
          "The comparative must be an approved validated package for the same scope, framework and currency, from an earlier period.");
    }

    var packageLines = FinancialStatementCalculator.BuildPackageLines(calculated.Value.Balances, allocations, dataset.Currency);
    var statementTotals = packageLines.GroupBy(x => x.StatementSection, StringComparer.Ordinal)
      .ToDictionary(x => x.Key, x => MoneyPolicy.Normalize(x.Sum(y => y.Amount)), StringComparer.Ordinal);
    var supplementaryHash = request.SupplementaryInformation is null
      ? null
      : FinancialStatementCalculator.ComputeSupplementaryHash(request.SupplementaryInformation, dataset.Currency);
    var equityHash = request.SupplementaryInformation?.EquityLines is { Count: > 0 } equityLines
      ? FinancialStatementCalculator.ComputeEquityHash(equityLines, dataset.Currency)
      : null;
    var noteTotals = (request.SupplementaryInformation?.NoteLines ?? [])
      .GroupBy(x => x.FaceDestinationCode.Trim(), StringComparer.OrdinalIgnoreCase)
      .ToDictionary(x => x.Key, x => MoneyPolicy.Normalize(x.Sum(y => y.Amount)), StringComparer.OrdinalIgnoreCase);
    if (request.SupplementaryInformation?.NoteLines is { Count: > 0 } && noteTotals.Any(x =>
        MoneyPolicy.Normalize(packageLines.Where(y => string.Equals(y.DestinationCode, x.Key, StringComparison.OrdinalIgnoreCase)).Sum(y => y.Amount)) != x.Value))
      return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.Accounting.PackageSupplementaryInvalid,
        "Structured note amounts must cross-cast to their mapped face destinations.");
    var packageHash = FinancialStatementCalculator.ComputePackageHash(request, mapping, plan,
      calculated.Value.ResultHash, packageLines, supplementaryHash, dataset.PeriodId, dataset.BookId, datasetBasis);

    var ownsTransaction = db.Database.CurrentTransaction is null;
    await using var tx = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var client = await LockClientAsync(db, actor.FirmId, mapping.ClientId, ct);
    if (client is null)
      return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");

    // Re-read mutable inputs after the lock order has been acquired. Calculation may have
    // taken time, so an approval/generation change observed here must refuse publication
    // instead of silently persisting a package from an obsolete snapshot.
    var currentMapping = await db.MappingVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == mapping.Id && x.FirmId == mapping.FirmId, ct);
    var currentPlan = await db.AdjustmentPlans.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == plan.Id && x.FirmId == plan.FirmId, ct);
    if (currentMapping is null || currentPlan is null ||
        currentMapping.Version != mapping.Version ||
        currentMapping.Generation != client.InputGeneration ||
        currentMapping.Status != AccountingPackageStates.MappingApproved ||
        currentPlan.Status != "Finalized" || currentPlan.ResultHash != plan.ResultHash)
      return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.GenerationStale,
        "Accounting inputs changed while the package was being calculated; reload and retry.");

    var existing = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.AdjustmentPlanId == plan.Id &&
      x.MappingVersionId == mapping.Id && x.TemplateVersion == request.TemplateVersion.Trim(), ct);
    if (existing is not null)
    {
      if (existing.CalculationHash != packageHash)
        return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.IdempotencyConflict,
          "The package identity is already bound to different calculation inputs.");
      if (ownsTransaction)
        await tx!.CommitAsync(ct);
      return CommandResult<FinancialPackageBuildResult>.Ok(new FinancialPackageBuildResult(
        existing.Id, existing.AdjustedDatasetId, existing.Status, existing.CalculationHash, statementTotals));
    }

    var adjusted = await db.AdjustedTrialBalanceSnapshots.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.AdjustmentPlanId == plan.Id, ct);
    if (adjusted is null)
    {
      adjusted = new AdjustedTrialBalanceSnapshot
      {
        Id = Guid.CreateVersion7(), FirmId = plan.FirmId, ClientId = plan.ClientId,
        EngagementId = plan.EngagementId, BaseDatasetId = plan.BaseDatasetId,
        AdjustmentPlanId = plan.Id, Revision = 1, Currency = dataset.Currency,
        ResultHash = calculated.Value.ResultHash, CreatedByUserId = actor.UserId,
        CreatedAt = DateTimeOffset.UtcNow
      };
      db.AdjustedTrialBalanceSnapshots.Add(adjusted);
      foreach (var balance in calculated.Value.Balances)
        db.AdjustedTrialBalanceRows.Add(new AdjustedTrialBalanceRow
        {
          Id = Guid.CreateVersion7(), SnapshotId = adjusted.Id, AccountCode = balance.Key,
          Amount = balance.Value, Currency = dataset.Currency
        });
    }
    else if (adjusted.ResultHash != calculated.Value.ResultHash)
    {
      return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.StaleRevision,
        "The adjustment snapshot does not match the finalized plan.");
    }

    var package = new FinancialPackage
    {
      Id = Guid.CreateVersion7(), FirmId = mapping.FirmId, ClientId = mapping.ClientId,
      EngagementId = mapping.EngagementId, AdjustedDatasetId = adjusted.Id,
      MappingVersionId = mapping.Id, AdjustmentPlanId = plan.Id,
      PeriodId = dataset.PeriodId, BookId = dataset.BookId, Basis = datasetBasis,
      Framework = request.Framework.Trim(), PeriodStart = request.PeriodStart.Trim(),
      PeriodEnd = request.PeriodEnd.Trim(), TaxonomyVersion = mapping.TaxonomyVersion,
      TemplateVersion = request.TemplateVersion.Trim(), CalculationEngineVersion = FinancialStatementCalculator.CalculationEngineVersion,
      CalculationHash = packageHash, Currency = dataset.Currency, Revision = 1,
      Generation = client.InputGeneration,
      Status = request.SupplementaryInformation is null
        ? AccountingPackageStates.PackageReviewRequired : AccountingPackageStates.PackageValidated,
      CashBeginning = request.SupplementaryInformation?.CashBeginning,
      CashEnding = request.SupplementaryInformation?.CashEnding,
      SupplementaryHash = supplementaryHash,
      EquityHash = equityHash,
      ComparativePackageId = comparative?.Id,
      ComparativeBasis = request.SupplementaryInformation?.Comparative?.Basis.Trim(),
      ComparativeEvidenceReference = request.SupplementaryInformation?.Comparative?.EvidenceReference.Trim(),
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.FinancialPackages.Add(package);
    foreach (var line in packageLines)
      db.FinancialPackageLines.Add(new FinancialPackageLine
      {
        Id = Guid.CreateVersion7(), FirmId = package.FirmId, ClientId = package.ClientId,
        EngagementId = package.EngagementId, FinancialPackageId = package.Id,
        SourceAccountCode = line.SourceAccountCode, DestinationCode = line.DestinationCode,
        StatementSection = line.StatementSection, Amount = line.Amount,
        RoundingResidual = line.RoundingResidual, Fraction = line.Fraction,
        Currency = package.Currency, AdjustedSnapshotId = adjusted.Id, CreatedAt = package.CreatedAt
      });
    AddValidation(db, package, "TB_BALANCED", calculated.Value.TotalSigned == 0m,
      "Adjusted signed total is zero at six-decimal precision.");
    AddValidation(db, package, "MAPPING_COMPLETE", true,
      "Every non-zero adjusted account has a reviewed allocation totaling 100%.");
    AddValidation(db, package, "ADJUSTMENTS_REPRODUCIBLE", true,
      "The adjusted snapshot hash matches the finalized adjustment plan.");
    AddValidation(db, package, "PACKAGE_BALANCED", MoneyPolicy.Normalize(packageLines.Sum(x => x.Amount)) == 0m,
      "Mapped presentation lines retain a zero signed total.");
    var sectionTotals = packageLines.GroupBy(x => x.StatementSection, StringComparer.Ordinal)
      .ToDictionary(x => x.Key, x => MoneyPolicy.Normalize(x.Sum(y => y.Amount)), StringComparer.Ordinal);
    var crossCastTotal = MoneyPolicy.Normalize(sectionTotals.Values.Sum());
    var lineTotal = MoneyPolicy.Normalize(packageLines.Sum(x => x.Amount));
    AddValidation(db, package, "STATEMENT_CROSS_CAST", crossCastTotal == lineTotal,
      $"Statement-section totals cross-cast to the mapped line total ({crossCastTotal.ToString("0.000000", CultureInfo.InvariantCulture)} {package.Currency}).");
    var equationSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
      { "ASSETS", "LIABILITIES", "EQUITY", "INCOME", "EXPENSE", "OCI", "SFP", "BALANCE_SHEET", "P&L", "P_AND_L", "PROFIT_LOSS" };
    var unclassifiedSections = sectionTotals.Keys.Where(x => !equationSections.Contains(x)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    AddValidation(db, package, "ACCOUNTING_EQUATION", lineTotal == 0m && unclassifiedSections.Length == 0,
      unclassifiedSections.Length == 0
        ? "Assets, liabilities, equity, income, expense and OCI signed totals satisfy the accounting equation."
        : $"The accounting equation has unclassified statement sections: {string.Join(", ", unclassifiedSections)}.");
    var hasSupplementary = request.SupplementaryInformation is not null;
    AddValidation(db, package, "CASH_FLOW_RECONCILED", hasSupplementary,
      hasSupplementary ? "Cash-flow lines reconcile to the supplied opening and closing cash." : "Cash-flow workings are not supplied.");
    AddValidation(db, package, "DISCLOSURES_COMPLETE", hasSupplementary,
      hasSupplementary ? "Disclosure responses are present, including rationale for not-applicable items." : "Disclosure responses are not supplied.");
    AddValidation(db, package, "SUPPLEMENTARY_INFORMATION", hasSupplementary,
      hasSupplementary ? "Cash-flow workings and disclosure responses are persisted with the package." : "Cash-flow workings, disclosures and management information require separate approved inputs.");
    if (request.SupplementaryInformation?.EquityLines is { Count: > 0 } requestedEquity)
    {
      AddValidation(db, package, "EQUITY_ROLLFORWARD", true,
        "Statement-of-changes-in-equity lines reconcile opening, movements and closing balances.");
      var incomeTotal = MoneyPolicy.Normalize(packageLines.Where(x => x.StatementSection.Equals("INCOME", StringComparison.OrdinalIgnoreCase) ||
          x.StatementSection.Equals("P&L", StringComparison.OrdinalIgnoreCase) ||
          x.StatementSection.Equals("PROFIT_LOSS", StringComparison.OrdinalIgnoreCase) ||
          x.StatementSection.Equals("P_AND_L", StringComparison.OrdinalIgnoreCase))
        .Sum(x => x.Amount));
      var ociTotal = MoneyPolicy.Normalize(packageLines.Where(x => x.StatementSection.Equals("OCI", StringComparison.OrdinalIgnoreCase))
        .Sum(x => x.Amount));
      var equityProfit = MoneyPolicy.Normalize(requestedEquity.Sum(x => x.ProfitOrLossAmount) + incomeTotal) == 0m;
      var equityOci = MoneyPolicy.Normalize(requestedEquity.Sum(x => x.OciAmount) + ociTotal) == 0m;
      AddValidation(db, package, "EQUITY_PROFIT", equityProfit && equityOci,
        equityProfit && equityOci
          ? "Equity profit/loss and OCI movements agree to the mapped income and OCI totals."
          : "Equity profit/loss or OCI movements do not agree to the mapped statement totals.");
      foreach (var line in requestedEquity)
        typedAccounting!.FinancialPackageEquityLines.Add(new FinancialPackageEquityLine
        {
          Id = Guid.CreateVersion7(), FirmId = package.FirmId, ClientId = package.ClientId,
          EngagementId = package.EngagementId, FinancialPackageId = package.Id,
          LineCode = line.LineCode.Trim().ToUpperInvariant(), Description = line.Description.Trim(),
          OpeningAmount = MoneyPolicy.Normalize(line.OpeningAmount), ProfitOrLossAmount = MoneyPolicy.Normalize(line.ProfitOrLossAmount),
          OciAmount = MoneyPolicy.Normalize(line.OciAmount), CapitalMovementAmount = MoneyPolicy.Normalize(line.CapitalMovementAmount),
          DividendsAmount = MoneyPolicy.Normalize(line.DividendsAmount), ClosingAmount = MoneyPolicy.Normalize(line.ClosingAmount),
          Currency = package.Currency, EvidenceReference = line.EvidenceReference.Trim(), CreatedAt = package.CreatedAt
        });
    }
    else
      AddValidation(db, package, "EQUITY_PROFIT", false,
        "A typed equity rollforward was not supplied for equity/profit validation.");
    if (request.SupplementaryInformation?.Comparative is { } requestedComparative)
    {
      AddValidation(db, package, "COMPARATIVE_BOUND", true,
        $"Comparative package {requestedComparative.PackageId:D} is bound by exact package identity and evidence.");
      var comparativeLineCount = await db.FinancialPackageLines.AsNoTracking()
        .CountAsync(x => x.FinancialPackageId == comparative!.Id && x.FirmId == package.FirmId, ct);
      AddValidation(db, package, "COMPARATIVE_CONSISTENCY", comparativeLineCount > 0,
        comparativeLineCount > 0
          ? "The validated comparative contains mapped statement lines for the same scope, framework and currency."
          : "The bound comparative contains no mapped statement lines.");
    }
    else
      AddValidation(db, package, "COMPARATIVE_CONSISTENCY", true,
        "No comparative was required for this package input.");
    if (request.SupplementaryInformation?.NoteLines is { Count: > 0 } requestedNotes)
    {
      AddValidation(db, package, "NOTE_TO_FACE_TOTALS", true,
        "Structured note amounts cross-cast to the mapped face destinations.");
      foreach (var line in requestedNotes)
        typedAccounting!.FinancialPackageNoteLines.Add(new FinancialPackageNoteLine
        {
          Id = Guid.CreateVersion7(), FirmId = package.FirmId, ClientId = package.ClientId,
          EngagementId = package.EngagementId, FinancialPackageId = package.Id,
          NoteCode = line.NoteCode.Trim().ToUpperInvariant(), FaceDestinationCode = line.FaceDestinationCode.Trim(),
          Amount = MoneyPolicy.Normalize(line.Amount), Currency = package.Currency,
          EvidenceReference = line.EvidenceReference.Trim(), CreatedAt = package.CreatedAt
        });
    }
    else
      AddValidation(db, package, "NOTE_TO_FACE_TOTALS", true,
        "No structured note lines were supplied; note-to-face cross-casts are not applicable.");
    if (request.SupplementaryInformation is not null)
    {
      foreach (var line in request.SupplementaryInformation.CashFlowLines)
        db.FinancialPackageCashFlowLines.Add(new FinancialPackageCashFlowLine
        {
          Id = Guid.CreateVersion7(), FirmId = package.FirmId, ClientId = package.ClientId,
          EngagementId = package.EngagementId, FinancialPackageId = package.Id,
          Section = line.Section.Trim().ToUpperInvariant(), Description = line.Description.Trim(),
          Amount = MoneyPolicy.Normalize(line.Amount), Currency = package.Currency, CreatedAt = package.CreatedAt
        });
      foreach (var disclosure in request.SupplementaryInformation.Disclosures)
        db.FinancialPackageDisclosures.Add(new FinancialPackageDisclosure
        {
          Id = Guid.CreateVersion7(), FirmId = package.FirmId, ClientId = package.ClientId,
          EngagementId = package.EngagementId, FinancialPackageId = package.Id,
          Code = disclosure.Code.Trim().ToUpperInvariant(), Response = disclosure.NotApplicable ? string.Empty : disclosure.Response.Trim(),
          NotApplicable = disclosure.NotApplicable,
          Rationale = disclosure.NotApplicable ? disclosure.Rationale!.Trim() : null, CreatedAt = package.CreatedAt
        });
    }

    try
    {
      await db.SaveChangesAsync(ct);
      if (ownsTransaction)
        await tx!.CommitAsync(ct);
      return CommandResult<FinancialPackageBuildResult>.Ok(new FinancialPackageBuildResult(
        package.Id, adjusted.Id, package.Status, package.CalculationHash, statementTotals));
    }
    catch (DbUpdateException)
    {
      return CommandResult<FinancialPackageBuildResult>.Fail(ErrorCodes.IdempotencyConflict,
        "The package identity changed; retry from the current mapping and plan.");
    }
  }

  public static async Task<CommandResult<Guid>> EnqueueFinancialPackageBuildAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    BuildFinancialPackageRequest request,
    IOperationStore operationStore,
    FinancialPackageBuildHandler handler,
    CancellationToken ct = default)
  {
    var invalid = ValidatePackageRequest(request);
    if (invalid is not null)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid, invalid);
    var mapping = await db.MappingVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.MappingVersionId && x.FirmId == actor.FirmId, ct);
    var plan = await db.AdjustmentPlans.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.AdjustmentPlanId && x.FirmId == actor.FirmId, ct);
    if (mapping is null || plan is null || mapping.ClientId != plan.ClientId ||
        mapping.EngagementId != plan.EngagementId || mapping.DatasetId != plan.BaseDatasetId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, mapping.FirmId, mapping.ClientId, mapping.EngagementId,
      PreparerRoles.ToArray(), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (mapping.Status != AccountingPackageStates.MappingApproved || plan.Status != "Finalized")
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An approved mapping and finalized adjustment plan are required.");

    var payload = FinancialPackageBuildHandler.SerializePayload(mapping.ClientId, mapping.EngagementId, request);
    var payloadDigest = Hashing.Sha256Hex(payload);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    CommandResult<Guid> enqueued;
    try
    {
      enqueued = await operationStore.EnqueueAsync(db, new OperationRequest(
        actor.FirmId, mapping.ClientId, mapping.EngagementId, FinancialPackageBuildHandler.Kind,
        mapping.Id, mapping.Version,
        $"financial-package-build:{mapping.Id:D}:{plan.Id:D}:{mapping.Version}:{payloadDigest}",
        payload, actor.UserId), handler, ct);
    }
    catch (OperationBlockedException)
    {
      enqueued = CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "The financial-package operation request was refused.");
    }
    if (!enqueued.Succeeded)
      return CommandResult<Guid>.Fail(enqueued.ErrorCode!, enqueued.Message!);
    await tx.CommitAsync(ct);
    return enqueued;
  }

  public static async Task<CommandResult<Guid>> EnqueueFinancialPackageRenderAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid packageId,
    IOperationStore operationStore,
    FinancialPackageRenderHandler handler,
    CancellationToken ct = default)
  {
    if (packageId == Guid.Empty)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid, "A financial package is required.");
    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == packageId && x.FirmId == actor.FirmId, ct);
    if (package is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, package.FirmId, package.ClientId, package.EngagementId,
      PreparerRoles.ToArray(), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var payload = FinancialPackageRenderHandler.SerializePayload(package.ClientId, package.EngagementId, package.Id);
    var payloadDigest = Hashing.Sha256Hex(payload);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    CommandResult<Guid> enqueued;
    try
    {
      enqueued = await operationStore.EnqueueAsync(db, new OperationRequest(
        actor.FirmId, package.ClientId, package.EngagementId, FinancialPackageRenderHandler.Kind,
        package.Id, package.Revision,
        $"financial-package-render:{package.Id:D}:{package.Revision}:{payloadDigest}",
        payload, actor.UserId), handler, ct);
    }
    catch (OperationBlockedException)
    {
      enqueued = CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "The financial-package render operation request was refused.");
    }
    if (!enqueued.Succeeded)
      return CommandResult<Guid>.Fail(enqueued.ErrorCode!, enqueued.Message!);
    await tx.CommitAsync(ct);
    return enqueued;
  }

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
        FinancialPackageArtifactVersions.ControlledWorkbook or FinancialPackageArtifactVersions.Word))
      return CommandResult<FinancialPackageOfficeArtifact>.Fail(ErrorCodes.Accounting.PackageInvalid, "Unsupported Office artifact version.");

    var canonical = await RenderPackageArtifactAsync(db, actor, packageId, ct);
    if (!canonical.Succeeded || canonical.Value is null)
      return CommandResult<FinancialPackageOfficeArtifact>.Fail(canonical.ErrorCode!, canonical.Message!);

    var package = await db.FinancialPackages.AsNoTracking().SingleAsync(x =>
      x.Id == packageId && x.FirmId == actor.FirmId, ct);
    var artifact = FinancialPackageOfficeRenderer.Render(artifactVersion, canonical.Value.RenderedText);
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

  private static async Task<CommandResult> AuthorizeAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid firmId, Guid clientId, Guid engagementId,
    IReadOnlyList<string> roles, CancellationToken ct) =>
    await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(firmId, clientId, engagementId, roles.ToArray(), InternalOnly: true), ct);

  private static async Task<FirmSafetyState?> LockFirmAsync(
    IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    await db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").SingleOrDefaultAsync(ct);

  private static async Task<ClientSafetyState?> LockClientAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, CancellationToken ct) =>
    await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE firm_id = {firmId} AND id = {clientId} FOR UPDATE").SingleOrDefaultAsync(ct);

  private static MappingAllocation ToAllocation(MappingVersion mapping, MappingAllocationInput input) => new()
  {
    Id = Guid.CreateVersion7(), FirmId = mapping.FirmId, ClientId = mapping.ClientId,
    EngagementId = mapping.EngagementId, MappingVersionId = mapping.Id,
    SourceAccountCode = input.SourceAccountCode.Trim(), DestinationCode = input.DestinationCode.Trim(),
    StatementSection = input.StatementSection.Trim().ToUpperInvariant(),
    AuditArea = string.IsNullOrWhiteSpace(input.AuditArea) ? null : input.AuditArea.Trim(),
    Fraction = MoneyPolicy.Normalize(input.Fraction),
    ResidualPolicy = input.ResidualPolicy.Trim().ToUpperInvariant(),
    Rationale = input.Rationale.Trim(),
    CreatedAt = mapping.CreatedAt
  };

  private static string? ValidateMappingRequest(CreateMappingVersionRequest request)
  {
    if (request.DatasetId == Guid.Empty || string.IsNullOrWhiteSpace(request.TaxonomyVersion) ||
        string.IsNullOrWhiteSpace(request.PeriodStart) || string.IsNullOrWhiteSpace(request.PeriodEnd))
      return "Dataset, taxonomy and reporting period are required.";
    if (!ValidDate(request.PeriodStart) || !ValidDate(request.PeriodEnd) || string.CompareOrdinal(request.PeriodStart, request.PeriodEnd) > 0)
      return "Reporting period must be an ordered ISO date range.";
    return request.Allocations.Count == 0 ? "At least one mapping allocation is required." : null;
  }

  private static string? ValidatePackageRequest(BuildFinancialPackageRequest request)
  {
    if (request.AdjustmentPlanId == Guid.Empty || request.MappingVersionId == Guid.Empty ||
        string.IsNullOrWhiteSpace(request.Framework) || string.IsNullOrWhiteSpace(request.TemplateVersion))
      return "Adjustment plan, mapping, framework and template version are required.";
    if (!ValidDate(request.PeriodStart) || !ValidDate(request.PeriodEnd) || string.CompareOrdinal(request.PeriodStart, request.PeriodEnd) > 0)
      return "Reporting period must be an ordered ISO date range.";
    return null;
  }

  private static async Task<string?> ValidateApprovedTaxonomyAsync(
    IAuditSphereDbContext db,
    Guid firmId,
    string taxonomyCode,
    IReadOnlyCollection<MappingAllocationInput> allocations,
    CancellationToken ct)
  {
    if (db is not IClientAccountingDbContext typed)
      return null; // Legacy command adapters without the client-accounting surface remain compatible.
    var taxonomy = await typed.ReportingTaxonomyVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.Code == taxonomyCode.Trim() && x.Status == AccountingWorkflowStates.Approved, ct);
    if (taxonomy is null)
      return "An approved reporting taxonomy version is required.";
    var destinations = allocations.Select(x => x.DestinationCode.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var approvedNodes = await typed.ReportingTaxonomyNodes.AsNoTracking().Where(x => x.FirmId == firmId &&
      x.TaxonomyVersionId == taxonomy.Id && x.IsPosting)
      .Select(x => new { x.Code, x.StatementSection }).ToListAsync(ct);
    var approvedByCode = approvedNodes.ToDictionary(x => x.Code, x => x.StatementSection, StringComparer.OrdinalIgnoreCase);
    var unknown = destinations.Where(x => !approvedByCode.ContainsKey(x)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    if (unknown.Length > 0)
      return $"Mapping destinations are not approved taxonomy nodes: {string.Join(", ", unknown)}.";
    var mismatchedSections = allocations
      .Where(x => approvedByCode.TryGetValue(x.DestinationCode.Trim(), out var section) &&
        !string.Equals(section.Trim(), x.StatementSection.Trim(), StringComparison.OrdinalIgnoreCase))
      .Select(x => $"{x.DestinationCode.Trim()} expected {approvedByCode[x.DestinationCode.Trim()]}, received {x.StatementSection.Trim()}")
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(x => x, StringComparer.Ordinal)
      .ToArray();
    return mismatchedSections.Length == 0
      ? null
      : $"Mapping statement sections do not match the approved taxonomy: {string.Join("; ", mismatchedSections)}.";
  }

  private static string? ValidateAllocations(
    IReadOnlyCollection<SourceBalance> rows,
    IReadOnlyCollection<MappingAllocationInput> allocations)
  {
    var sourceAmounts = rows.Select(row => (Account: row.AccountCode.Trim(), Amount: row.Amount)).ToList();
    var accountSet = sourceAmounts.Select(x => x.Account).ToHashSet(StringComparer.Ordinal);
    var normalized = allocations.Select(x => new MappingAllocationInput(
      x.SourceAccountCode.Trim(), x.DestinationCode.Trim(), x.StatementSection.Trim().ToUpperInvariant(),
      MoneyPolicy.Normalize(x.Fraction), x.Rationale.Trim(), string.IsNullOrWhiteSpace(x.AuditArea) ? null : x.AuditArea.Trim())).ToList();
    if (normalized.Any(x => x.SourceAccountCode.Length == 0 || x.DestinationCode.Length == 0 ||
        x.StatementSection.Length == 0 || x.Rationale.Length == 0 || x.Fraction <= 0m || x.Fraction > 1m))
      return "Each allocation needs a positive fraction, destination, statement section and rationale.";
    if (normalized.Any(x => !string.Equals(x.ResidualPolicy, "LAST_DESTINATION", StringComparison.Ordinal)))
      return "Only the deterministic LAST_DESTINATION residual policy is supported.";
    if (normalized.Any(x => !accountSet.Contains(x.SourceAccountCode)))
      return "A mapping allocation references an account outside the selected dataset.";
    if (normalized.GroupBy(x => (x.SourceAccountCode, x.DestinationCode)).Any(x => x.Count() > 1))
      return "A source account and destination may occur only once in a mapping version.";
    foreach (var source in sourceAmounts.Where(x => x.Amount != 0m).Select(x => x.Account).Distinct(StringComparer.Ordinal))
    {
      var sum = normalized.Where(x => x.SourceAccountCode == source).Sum(x => x.Fraction);
      if (MoneyPolicy.Normalize(sum) != 1m)
        return $"Non-zero account {source} must be allocated exactly 100%.";
    }
    return null;
  }

  private static string? ValidateSupplementaryInformation(FinancialSupplementaryInformation? input)
  {
    if (input is null) return null;
    if (input.CashFlowLines.Count == 0 || input.Disclosures.Count == 0)
      return "Cash-flow lines and disclosure responses are both required.";
    if (input.CashFlowLines.Any(x => x.Section.Trim().ToUpperInvariant() is not ("OPERATING" or "INVESTING" or "FINANCING") ||
                                     string.IsNullOrWhiteSpace(x.Description) || x.Description.Trim().Length > 2000))
      return "Each cash-flow line needs an allowed section and description.";
    if (input.CashFlowLines.GroupBy(x => x.Section.Trim().ToUpperInvariant() + "\u001f" + x.Description.Trim(), StringComparer.Ordinal).Any(x => x.Count() != 1))
      return "Cash-flow section and description pairs must be unique.";
    if (MoneyPolicy.Normalize(input.CashBeginning) != input.CashBeginning ||
        MoneyPolicy.Normalize(input.CashEnding) != input.CashEnding ||
        input.CashFlowLines.Any(x => MoneyPolicy.Normalize(x.Amount) != x.Amount))
      return "Cash-flow amounts exceed the six-decimal precision policy.";
    if (input.Disclosures.Any(x => string.IsNullOrWhiteSpace(x.Code) ||
      x.Code.Trim().Length > 100 || x.Response.Trim().Length > 4000 || (x.Rationale?.Trim().Length ?? 0) > 2000 ||
      (!x.NotApplicable && string.IsNullOrWhiteSpace(x.Response)) ||
      (x.NotApplicable && string.IsNullOrWhiteSpace(x.Rationale))))
      return "Each disclosure needs a response or a not-applicable rationale.";
    if (input.Disclosures.GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() != 1))
      return "Disclosure codes must be unique in a package.";
    if (input.EquityLines is { Count: > 0 } equity)
    {
      if (equity.Any(x => string.IsNullOrWhiteSpace(x.LineCode) || string.IsNullOrWhiteSpace(x.Description) ||
          string.IsNullOrWhiteSpace(x.EvidenceReference) || new[] { x.OpeningAmount, x.ProfitOrLossAmount, x.OciAmount,
            x.CapitalMovementAmount, x.DividendsAmount, x.ClosingAmount }.Any(y => MoneyPolicy.Normalize(y) != y)))
        return "Each equity line needs descriptions, evidence and six-decimal amounts.";
      if (equity.GroupBy(x => x.LineCode.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() != 1))
        return "Equity line codes must be unique in a package.";
      if (equity.Any(x => MoneyPolicy.Normalize(x.OpeningAmount + x.ProfitOrLossAmount + x.OciAmount +
          x.CapitalMovementAmount - x.DividendsAmount) != MoneyPolicy.Normalize(x.ClosingAmount)))
        return "Each equity line must reconcile opening balance, profit/loss, OCI, capital, dividends and closing balance.";
    }
    if (input.NoteLines is { Count: > 0 } notes)
    {
      if (notes.Any(x => string.IsNullOrWhiteSpace(x.NoteCode) || string.IsNullOrWhiteSpace(x.FaceDestinationCode) ||
          string.IsNullOrWhiteSpace(x.EvidenceReference) || MoneyPolicy.Normalize(x.Amount) != x.Amount))
        return "Each structured note line needs a destination, evidence and six-decimal amount.";
      if (notes.GroupBy(x => $"{x.NoteCode.Trim().ToUpperInvariant()}\u001f{x.FaceDestinationCode.Trim().ToUpperInvariant()}", StringComparer.Ordinal).Any(x => x.Count() != 1))
        return "Structured note line identities must be unique in a package.";
    }
    if (input.Comparative is { } comparative &&
        (comparative.PackageId == Guid.Empty || string.IsNullOrWhiteSpace(comparative.Basis) || string.IsNullOrWhiteSpace(comparative.EvidenceReference)))
      return "A comparative package needs an exact package identity, basis and evidence reference.";
    var expected = MoneyPolicy.Normalize(input.CashEnding - input.CashBeginning);
    var actual = MoneyPolicy.Normalize(input.CashFlowLines.Sum(x => x.Amount));
    return expected == actual ? null : $"Cash-flow lines total {actual} but the opening/closing bridge is {expected}.";
  }

  private static void AddValidation(
    IAuditSphereDbContext db, FinancialPackage package, string code, bool passed, string detail) =>
    db.FinancialPackageValidations.Add(new FinancialPackageValidation
    {
      Id = Guid.CreateVersion7(), FirmId = package.FirmId, FinancialPackageId = package.Id,
      Code = code, Passed = passed, Detail = detail, CreatedAt = package.CreatedAt
    });

  private static async Task<CommandResult<AdjustedCalculation>> CalculateAdjustedBalancesAsync(
    IAuditSphereDbContext db, AdjustmentPlan plan, CancellationToken ct)
  {
    var rows = await db.TrialBalanceRows.AsNoTracking()
      .Where(x => x.DatasetId == plan.BaseDatasetId).ToListAsync(ct);
    if (rows.Count == 0)
      return CommandResult<AdjustedCalculation>.Fail(ErrorCodes.GateBlocked, "The adjustment base has no rows.");
    var balances = rows.ToDictionary(x => x.AccountCode, x => x.Amount, StringComparer.Ordinal);
    var lines = await db.AdjustmentPlanLines.AsNoTracking().Where(x => x.PlanId == plan.Id).ToListAsync(ct);
    foreach (var line in lines.Where(x => x.ReflectionState == ReflectionStates.NotReflected))
    {
      var journal = await db.AdjustmentJournals.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == plan.FirmId && x.EngagementId == plan.EngagementId &&
        x.JournalNumber == line.LogicalJournalNumber && x.Revision == line.JournalRevision && x.Status == "Posted", ct);
      if (journal is null)
        return CommandResult<AdjustedCalculation>.Fail(ErrorCodes.GateBlocked, "A finalized plan references a missing posted journal.");
      var journalLines = await db.AdjustmentLines.AsNoTracking().Where(x => x.JournalId == journal.Id).ToListAsync(ct);
      try
      {
        balances = new Dictionary<string, decimal>(TrialBalanceCalculator.ApplyJournal(
          balances, journalLines.Select(x => (x.AccountCode, x.Debit, x.Credit))), StringComparer.Ordinal);
      }
      catch (InvalidOperationException ex)
      {
        return CommandResult<AdjustedCalculation>.Fail(ErrorCodes.GateBlocked, ex.Message);
      }
    }
    var signed = MoneyPolicy.Normalize(balances.Values.Sum());
    var canonical = string.Join('\n', balances.OrderBy(x => x.Key, StringComparer.Ordinal)
      .Select(x => $"{x.Key}|{x.Value.ToString("0.000000", CultureInfo.InvariantCulture)}"));
    return CommandResult<AdjustedCalculation>.Ok(new AdjustedCalculation(
      balances, signed, Hashing.Sha256Hex(canonical),
      balances.Values.Where(x => x >= 0m).Sum(), balances.Values.Where(x => x < 0m).Sum(x => -x)));
  }

  private static bool ValidDate(string value) =>
    DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
      DateTimeStyles.None, out _);

  private sealed record SourceBalance(string AccountCode, decimal Amount);

  private sealed record AdjustedCalculation(
    IReadOnlyDictionary<string, decimal> Balances,
    decimal TotalSigned,
    string ResultHash,
    decimal TotalDebits,
    decimal TotalCreditsAbs);
}
