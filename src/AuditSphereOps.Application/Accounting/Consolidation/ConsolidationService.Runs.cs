using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ConsolidationService
{
  private sealed record ConsolidationBuild(
    ConsolidationCalculation Calculation,
    IReadOnlyList<(ConsolidationElimination Elimination, Guid? JournalId)> EliminationSources);

  public static async Task<CommandResult<Guid>> RunAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId,
    CancellationToken ct = default)
  {
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == scopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (scope.Status != AccountingWorkflowStates.Approved)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An approved perimeter is required before calculation.");
    if (AdvancedConsolidationMethods.All.Contains(scope.Method))
      return await RunAdvancedProfileAsync(db, actor, scope, ct);
    var build = await BuildCalculationAsync(db, actor.FirmId, scope, ct);
    if (!build.Succeeded)
      return CommandResult<Guid>.Fail(build.ErrorCode!, build.Message!);
    var calculation = build.Value!.Calculation;
    var eliminationSources = build.Value.EliminationSources;
    var existing = await db.ConsolidationRuns.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ScopeVersionId == scope.Id && x.RunHash == calculation.RunHash, ct);
    if (existing is not null)
      return CommandResult<Guid>.Ok(existing.Id);
    var run = new ConsolidationRun
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      EngineVersion = ConsolidationCalculator.EngineVersion, InputManifest = calculation.InputManifest,
      RunHash = calculation.RunHash, ReportingCurrency = scope.ReportingCurrency, SignedTotal = calculation.SignedTotal,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ConsolidationRuns.Add(run);
    foreach (var line in calculation.DetailLines)
    {
      Guid? intercompanyMatchId = null;
      Guid? consolidationJournalId = null;
      if (line.MatchId is { } matchId)
      {
        var source = eliminationSources.First(x => x.Elimination.MatchId == matchId);
        intercompanyMatchId = source.Elimination.IntercompanyMatchId;
        consolidationJournalId = source.JournalId;
      }
      db.ConsolidationRunLines.Add(new ConsolidationRunLine
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
        RunId = run.Id, ComponentId = line.ComponentId, SourceLineId = line.SourceLineId,
        IntercompanyMatchId = intercompanyMatchId, ConsolidationJournalId = consolidationJournalId,
        TaxonomyCode = line.TaxonomyCode, ComponentAmount = line.ComponentAmount, AlignmentAmount = 0m, EliminationAmount = line.EliminationAmount,
        ConsolidatedAmount = line.ConsolidatedAmount, Currency = line.Currency, CreatedAt = run.CreatedAt
      });
    }
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(run.Id);
  }

  public static async Task<CommandResult> ApproveRunAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid runId,
    CancellationToken ct = default)
  {
    var run = await db.ConsolidationRuns.SingleOrDefaultAsync(x => x.Id == runId && x.FirmId == actor.FirmId, ct);
    if (run is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, run.GroupId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (run.Status != AccountingWorkflowStates.Submitted || run.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Only a separate reviewer can approve a submitted balanced run.");
    if (run.SignedTotal != 0m)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The consolidation run is not balanced.");
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == run.ScopeVersionId && x.FirmId == actor.FirmId && x.GroupId == run.GroupId, ct);
    if (scope is null || scope.Status != AccountingWorkflowStates.Approved)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The consolidation perimeter changed; rebuild the group run.");
    var current = await BuildCalculationAsync(db, actor.FirmId, scope, ct);
    if (!current.Succeeded)
      return CommandResult.Fail(current.ErrorCode!, current.Message!);
    if (current.Value!.Calculation.RunHash != run.RunHash || current.Value.Calculation.InputManifest != run.InputManifest)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "A component, match or group journal changed; rebuild the group run.");
    run.Status = AccountingWorkflowStates.Approved;
    run.ApprovedByUserId = actor.UserId;
    run.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  private static async Task<CommandResult<ConsolidationBuild>> BuildCalculationAsync(
    IClientAccountingDbContext db, Guid firmId, ConsolidationScopeVersion scope, CancellationToken ct)
  {
    var currentGroupRevision = await db.ClientGroups.AsNoTracking().Where(x => x.FirmId == firmId && x.Id == scope.GroupId)
      .Select(x => (long?)x.Revision).SingleOrDefaultAsync(ct);
    if (currentGroupRevision is null || currentGroupRevision.Value != scope.GroupRevision)
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GenerationStale,
        "The group perimeter changed; rebuild the consolidation scope before calculating.");
    var components = await db.ConsolidationComponents.AsNoTracking().Where(x => x.FirmId == firmId && x.ScopeVersionId == scope.Id &&
      x.Status == AccountingWorkflowStates.Approved).OrderBy(x => x.Id).ToListAsync(ct);
    if (components.Count == 0)
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GateBlocked, "At least one approved component package is required.");
    var packageComponents = components.Where(x => x.SourceType == ConsolidationComponentSources.InternalPackage && x.PackageId.HasValue).ToList();
    var externalComponents = components.Where(x => x.SourceType == ConsolidationComponentSources.ExternalPack && x.ExternalComponentPackId.HasValue).ToList();
    if (packageComponents.Count + externalComponents.Count != components.Count)
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GenerationStale, "A component source is incomplete; resubmit the affected component.");
    var packageIds = packageComponents.Select(x => x.PackageId!.Value).ToArray();
    var packages = await db.FinancialPackages.AsNoTracking().Where(x => x.FirmId == firmId && packageIds.Contains(x.Id)).ToListAsync(ct);
    if (packages.Count != packageComponents.Count || packageComponents.Any(component =>
        packages.All(package => package.Id != component.PackageId || package.CalculationHash != component.PackageHash ||
          package.Status != AccountingPackageStates.PackageValidated || package.Currency != component.Currency)))
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GenerationStale,
        "A component package changed; rebuild the group run from current approved packages.");
    var externalPackIds = externalComponents.Select(x => x.ExternalComponentPackId!.Value).ToArray();
    var externalPacks = await db.ExternalComponentPacks.AsNoTracking().Where(x => x.FirmId == firmId && externalPackIds.Contains(x.Id)).ToListAsync(ct);
    var externalLines = await db.ExternalComponentPackLines.AsNoTracking().Where(x => x.FirmId == firmId && externalPackIds.Contains(x.ExternalComponentPackId)).ToListAsync(ct);
    if (externalPacks.Count != externalComponents.Count || externalComponents.Any(component =>
        externalPacks.All(pack => pack.Id != component.ExternalComponentPackId || pack.PackDigest != component.PackageHash ||
          pack.Status != ExternalComponentPackStates.Approved || pack.ReconciliationStatus != ExternalComponentReconciliationStates.Reconciled ||
          pack.ReportingCurrency != component.Currency || !ExternalPackLinesMatch(pack, externalLines.Where(line => line.ExternalComponentPackId == pack.Id).ToArray()))))
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GenerationStale,
        "An external component pack changed; rebuild the group run from the current approved pack.");
    if (scope.Method == ConsolidationCalculator.RestrictedMethod && components.Any(x => x.Currency != scope.ReportingCurrency))
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GateBlocked,
        "The restricted profile requires same-currency component packages.");
    var translationResults = new List<TranslationResult>();
    TranslationPolicyVersion? translationPolicy = null;
    if (scope.Method == ConsolidationCalculator.ForeignOperationMethod)
    {
      if (scope.ExchangeRateSetVersionId is null || scope.TranslationPolicyVersionId is null || scope.TranslationRateDate is null ||
          string.IsNullOrWhiteSpace(scope.TranslationRateType))
        return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GateBlocked,
          "The foreign-operation scope is missing its pinned translation inputs.");
      var rateSet = await db.ExchangeRateSetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId &&
        x.Id == scope.ExchangeRateSetVersionId && x.Status == AccountingWorkflowStates.Approved, ct);
      translationPolicy = await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId &&
        x.Id == scope.TranslationPolicyVersionId && x.Status == AccountingWorkflowStates.Approved, ct);
      if (rateSet is null || translationPolicy is null || translationPolicy.PresentationCurrency != scope.ReportingCurrency)
        return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GateBlocked,
          "The pinned translation policy and rate set are not approved for this group scope.");
      translationResults = await db.TranslationResults.AsNoTracking().Where(x => x.FirmId == firmId && x.GroupId == scope.GroupId &&
        x.ScopeVersionId == scope.Id && x.RateSetVersionId == scope.ExchangeRateSetVersionId &&
        x.TranslationPolicyVersionId == scope.TranslationPolicyVersionId && x.RateDate == scope.TranslationRateDate &&
        x.RateType == scope.TranslationRateType && x.Status == AccountingWorkflowStates.Approved &&
        x.CalculationVersion == TranslationCalculationVersions.ComponentTranslationV2).ToListAsync(ct);
    }
    var directRates = scope.ExchangeRateSetVersionId is { } rateSetId && scope.TranslationRateDate is { } rateDate
      ? await db.ExchangeRates.AsNoTracking().Where(x => x.FirmId == firmId &&
          x.RateSetVersionId == rateSetId && x.RateDate == rateDate && x.Direction == ExchangeRateDirections.Direct).ToListAsync(ct)
      : new List<ExchangeRate>();
    var packageLines = await db.FinancialPackageLines.AsNoTracking().Where(x => x.FirmId == firmId &&
      packageIds.Contains(x.FinancialPackageId)).ToListAsync(ct);
    var componentByPackage = packageComponents.ToDictionary(x => x.PackageId!.Value);
    var componentByExternalPack = externalComponents.ToDictionary(x => x.ExternalComponentPackId!.Value);
    var balances = new List<ConsolidationComponentBalance>(packageLines.Count + externalLines.Count + components.Count);
    // Statement sections of foreign-component lines are resolved once from approved evidence and
    // reused, so group calculation, translation and approval revalidation always classify one
    // line the same way.
    var sectionByLineId = new Dictionary<Guid, string>();
    foreach (var component in components.Where(x => x.Currency != scope.ReportingCurrency))
    {
      var componentPackageLines = packageLines
        .Where(l => componentByPackage.TryGetValue(l.FinancialPackageId, out var owner) && owner.Id == component.Id).ToList();
      var componentExternalLines = externalLines
        .Where(l => componentByExternalPack.TryGetValue(l.ExternalComponentPackId, out var owner) && owner.Id == component.Id).ToList();
      var codes = componentPackageLines.Select(l => l.DestinationCode)
        .Concat(componentExternalLines.Select(l => l.TaxonomyCode));
      var approvedSections = await TranslationInputResolution.LoadApprovedSectionsAsync(db, firmId, component.ClientId, codes, ct);
      var builtLines = TranslationInputResolution.BuildInputLines(componentPackageLines, componentExternalLines, approvedSections);
      if (!builtLines.Succeeded)
        return CommandResult<ConsolidationBuild>.Fail(builtLines.ErrorCode!, builtLines.Message!);
      foreach (var line in builtLines.Value!)
        sectionByLineId[Guid.Parse(line.SourceLineId)] = line.Section;
    }
    try
    {
      void AddBalance(ConsolidationComponent component, string destinationCode, decimal amount, string currency, Guid lineId, string section = "")
      {
        if (currency != component.Currency)
          throw new InvalidOperationException("A component line changed currency or no longer belongs to the selected component.");
        if (component.Currency == scope.ReportingCurrency)
        {
          balances.Add(new ConsolidationComponentBalance(component.Id, component.ClientId, destinationCode,
            amount, scope.ReportingCurrency, component.OwnershipPercent, component.ControlMethod, component.PackageHash,
            component.PeriodBasis, component.TaxonomyVersion, component.MappingVersion, lineId, component.Currency));
          return;
        }
        var translation = translationResults.SingleOrDefault(x => x.ComponentId == component.Id &&
          x.SourcePackageHash == component.PackageHash && x.FromCurrency == component.Currency &&
          x.ToCurrency == scope.ReportingCurrency && x.AppliedRate is > 0m);
        if (translation is null || translationPolicy is null || translationPolicy.FunctionalCurrency != component.Currency)
          throw new InvalidOperationException("A foreign component is missing its current approved translation result.");

        // The approved translation method decides which rate each line carries. A zero reserve or
        // a zero exchange effect never collapses per-line rate purposes into one header rate.
        var appliedRate = translation.AppliedRate!.Value;
        var appliedPurpose = string.Empty;
        if (translation.CalculationVersion == TranslationCalculationVersions.ComponentTranslationV2)
        {
          var ratesByPurpose = TranslationInputResolution.BuildRatesByPurpose(translationPolicy,
            directRates.Where(x => x.FromCurrency == component.Currency && x.ToCurrency == scope.ReportingCurrency).ToList(),
            translation.RateType, appliedRate);
          var purpose = LineTranslationCalculator.ResolvePurpose(
            new TranslationLineInput(lineId.ToString("D"), destinationCode, section, amount, currency),
            new Dictionary<string, string>(),
            LineTranslationCalculator.DefaultSectionPurposes);
          if (!ratesByPurpose.TryGetValue(purpose, out appliedRate))
            throw new InvalidOperationException(
              $"The approved rate set has no {purpose} rate for line {destinationCode}; a missing rate can never be substituted.");
          appliedPurpose = purpose;
        }

        var translated = CurrencyTranslationCalculator.Translate(amount, component.Currency, scope.ReportingCurrency,
          appliedRate);
        balances.Add(new ConsolidationComponentBalance(component.Id, component.ClientId, destinationCode,
          translated, scope.ReportingCurrency, component.OwnershipPercent, component.ControlMethod, component.PackageHash,
          component.PeriodBasis, component.TaxonomyVersion, component.MappingVersion, lineId, component.Currency,
          translation.Id, translation.RateSetVersionId, translation.TranslationPolicyVersionId, translation.RateDate,
          translation.RateType, appliedRate, appliedPurpose));
      }

      foreach (var line in packageLines)
      {
        if (!componentByPackage.TryGetValue(line.FinancialPackageId, out var component))
          throw new InvalidOperationException("A component package line no longer belongs to the selected component.");
        AddBalance(component, line.DestinationCode, line.Amount, line.Currency, line.Id,
          sectionByLineId.GetValueOrDefault(line.Id, line.StatementSection));
      }
      foreach (var line in externalLines)
      {
        if (!componentByExternalPack.TryGetValue(line.ExternalComponentPackId, out var component))
          throw new InvalidOperationException("An external component line no longer belongs to the selected component.");
        AddBalance(component, line.TaxonomyCode, line.Amount, line.Currency, line.Id,
          sectionByLineId.GetValueOrDefault(line.Id, string.Empty));
      }

      foreach (var component in components.Where(x => x.Currency != scope.ReportingCurrency))
      {
        var translation = translationResults.SingleOrDefault(x => x.ComponentId == component.Id &&
          x.SourcePackageHash == component.PackageHash && x.FromCurrency == component.Currency &&
          x.ToCurrency == scope.ReportingCurrency);
        if (translation is not null && translation.TranslationReserve != 0m)
        {
          var ctaRates = TranslationInputResolution.BuildRatesByPurpose(translationPolicy!,
            directRates.Where(x => x.FromCurrency == component.Currency && x.ToCurrency == scope.ReportingCurrency).ToList(),
            translation.RateType, translation.AppliedRate!.Value);
          if (!ctaRates.TryGetValue(TranslationRatePurposes.Closing, out var closingRate))
            throw new InvalidOperationException(
              $"The approved rate set has no closing rate to carry the cumulative translation reserve of component {component.Id:D}.");
          balances.Add(new ConsolidationComponentBalance(
            component.Id, component.ClientId, AccountingDefaults.CumulativeTranslationReserveSection,
            translation.TranslationReserve, scope.ReportingCurrency, component.OwnershipPercent, component.ControlMethod,
            component.PackageHash, component.PeriodBasis, component.TaxonomyVersion, component.MappingVersion,
            // Identity is owned by the immutable translation snapshot, so replay and currentness
            // checks reuse it instead of minting a new line on every recalculation.
            TranslationIdentities.CumulativeTranslationReserveLineId(translation.Id), component.Currency,
            translation.Id, translation.RateSetVersionId, translation.TranslationPolicyVersionId,
            translation.RateDate, translation.RateType, closingRate, TranslationRatePurposes.Closing));
        }
      }
    }
    catch (InvalidOperationException ex)
    {
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GenerationStale, ex.Message);
    }
    var pendingGrouped = await db.IntercompanyMatches.AsNoTracking().AnyAsync(x => x.FirmId == firmId &&
      x.ScopeVersionId == scope.Id && x.MatchMode == IntercompanyMatchModes.Grouped &&
      x.Status != AccountingWorkflowStates.Approved && x.Status != AccountingWorkflowStates.Rejected, ct);
    if (pendingGrouped)
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GateBlocked,
        "Every row in a grouped intercompany match must be reviewed before the group run.");
    var matches = await db.IntercompanyMatches.AsNoTracking().Where(x => x.FirmId == firmId && x.ScopeVersionId == scope.Id &&
      x.Status == AccountingWorkflowStates.Approved && !x.OutsidePerimeterReview).ToListAsync(ct);
    var approvedJournals = await db.ConsolidationJournals.AsNoTracking().Where(x => x.FirmId == firmId &&
      x.ScopeVersionId == scope.Id && x.Status == AccountingWorkflowStates.Approved).ToListAsync(ct);
    var journalIds = approvedJournals.Select(x => x.Id).ToArray();
    var journalLines = await db.ConsolidationJournalLines.AsNoTracking().Where(x => x.FirmId == firmId &&
      journalIds.Contains(x.ConsolidationJournalId)).ToListAsync(ct);
    var linkedMatches = journalLines.Where(x => x.IntercompanyMatchId.HasValue).Select(x => x.IntercompanyMatchId!.Value).ToHashSet();
    var eliminationSources = matches.Where(x => !linkedMatches.Contains(x.Id))
      .SelectMany(x => new[]
      {
        (new ConsolidationElimination(x.Id, x.SellerTaxonomyCode, Math.Sign(x.SellerAmount) * x.MatchedAmount * -1m, x.Currency, "SELLER", x.Id, x.AccountNature), (Guid?)null),
        (new ConsolidationElimination(x.Id, x.BuyerTaxonomyCode, Math.Sign(x.BuyerAmount) * x.MatchedAmount * -1m, x.Currency, "BUYER", x.Id, x.AccountNature), (Guid?)null)
      })
      .Concat(journalLines.Select(x => (new ConsolidationElimination(x.Id, x.TaxonomyCode, x.Debit - x.Credit, x.Currency, "", x.IntercompanyMatchId, ConsolidationEliminationKinds.GroupJournal), (Guid?)x.ConsolidationJournalId)))
      .ToList();
    try
    {
      var calculation = ConsolidationCalculator.Compute(scope.ReportingCurrency, scope.Method, scope.OpeningBasis, balances,
        eliminationSources.Select(x => x.Item1).ToList(), scope.GroupRevision);
      return CommandResult<ConsolidationBuild>.Ok(new ConsolidationBuild(calculation, eliminationSources));
    }
    catch (InvalidOperationException ex)
    {
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GateBlocked, ex.Message);
    }
  }
}
