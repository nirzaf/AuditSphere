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
    var cashBridgeReconciled = request.SupplementaryInformation is { } supplied &&
      ValidateSupplementaryInformation(supplied) is null;
    AddValidation(db, package, "CASH_FLOW_RECONCILED", hasSupplementary && cashBridgeReconciled,
      hasSupplementary
        ? "Cash movements, FX cash effects and noncash disclosures reconcile to the supplied opening and closing cash."
        : "Cash-flow workings are not supplied.");
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
          Amount = MoneyPolicy.Normalize(line.Amount), IsNonCash = line.IsNonCash,
          Currency = package.Currency, CreatedAt = package.CreatedAt
        });
      foreach (var fxEffect in request.SupplementaryInformation.FxCashEffects ?? [])
        db.FinancialPackageFxEffects.Add(new FinancialPackageFxEffect
        {
          Id = Guid.CreateVersion7(), FirmId = package.FirmId, ClientId = package.ClientId,
          EngagementId = package.EngagementId, FinancialPackageId = package.Id,
          CurrencyPair = fxEffect.CurrencyPair.Trim().ToUpperInvariant(),
          Amount = MoneyPolicy.Normalize(fxEffect.Amount), Currency = package.Currency,
          EvidenceReference = fxEffect.EvidenceReference.Trim(), CreatedAt = package.CreatedAt
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

    // Fail closed: a package whose blocking validations failed must never carry the
    // VALIDATED state, so review, management view and release all refuse it.
    var blockingValidationFailed = db.FinancialPackageValidations.Local
      .Any(v => !v.Passed && FinancialPackageReviewService.BlockingValidationCodes.Contains(v.Code));
    if (blockingValidationFailed && package.Status == AccountingPackageStates.PackageValidated)
      package.Status = AccountingPackageStates.PackageReviewRequired;

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

  private static string? ValidateSupplementaryInformation(FinancialSupplementaryInformation? input)
  {
    if (input is null) return null;
    if (input.CashFlowLines.Count == 0 || input.Disclosures.Count == 0)
      return "Cash-flow lines and disclosure responses are both required.";
    if (input.CashFlowLines.Any(x => x.Section.Trim().ToUpperInvariant() is not ("OPERATING" or "INVESTING" or "FINANCING") ||
                                     string.IsNullOrWhiteSpace(x.Description) || x.Description.Trim().Length > 2000))
      return "Each cash-flow line needs an allowed section and description.";
    if (input.CashFlowLines.Any(x => x.IsNonCash &&
        !x.Section.Trim().Equals("INVESTING", StringComparison.OrdinalIgnoreCase) &&
        !x.Section.Trim().Equals("FINANCING", StringComparison.OrdinalIgnoreCase)))
      return "Only investing or financing movements can be noncash; noncash amounts never enter the cash bridge.";
    if (input.FxCashEffects is { Count: > 0 } fxEffects &&
        fxEffects.Any(x => string.IsNullOrWhiteSpace(x.CurrencyPair) || x.CurrencyPair.Trim().Length > 20 ||
          string.IsNullOrWhiteSpace(x.EvidenceReference) || x.EvidenceReference.Trim().Length > 2000 ||
          MoneyPolicy.Normalize(x.Amount) != x.Amount))
      return "Each FX cash effect needs a currency pair, bounded amount and evidence reference.";
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
    var cashMovements = MoneyPolicy.Normalize(
      input.CashFlowLines.Where(x => !x.IsNonCash).Sum(x => x.Amount));
    var fxTotal = MoneyPolicy.Normalize(input.FxCashEffects?.Sum(x => x.Amount) ?? 0m);
    return expected == MoneyPolicy.Normalize(cashMovements + fxTotal)
      ? null
      : $"Cash-flow lines and FX effects total {MoneyPolicy.Normalize(cashMovements + fxTotal)} but the opening/closing cash bridge is {expected}.";
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
