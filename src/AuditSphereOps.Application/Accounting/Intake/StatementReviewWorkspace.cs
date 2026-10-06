using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record StatementBasis(Guid EngagementId, Guid ClientId, Guid MappingId, long MappingVersion, Guid DatasetId,
  long DatasetRevision, string DatasetDigest, string Currency, string PeriodStart, string PeriodEnd, string TaxonomyVersion,
  Guid? ChartVersionId, long Generation, Guid? ReviewerId, DateTimeOffset? ReviewedAt, string Revision);
public sealed record StatementReviewLine(
  string DestinationCode, string StatementSection, string AuditArea, decimal Amount, int AccountCount, int ProcedureCount,
  decimal? PriorAmount = null, decimal? Variance = null, string? PercentageVariance = null,
  string RiskBand = "Green", string RiskLabel = "Low (Green)", string AssignedPerformer = "Staff / Associate",
  string RequiredReviewer = "Manager", string ReviewStatus = "PLANNED", string ReviewStatusLabel = "Planned",
  string RiskExplanation = "");
public sealed record StatementSectionSummary(
  string Section, string Title, string TotalLabel, decimal CurrentTotal, decimal? PriorTotal, decimal? VarianceTotal,
  string? PercentageVarianceTotal, int LineCount, IReadOnlyList<StatementReviewLine> Lines);
public sealed record StatementReviewPage(StatementBasis Basis, string Section, string Title, string TotalLabel, decimal Total,
  bool Balances, int LineCount, int FilteredCount, int Page, int PageSize, IReadOnlyList<StatementReviewLine> Lines,
  StatementSectionSummary? ProfitOrLoss = null, StatementSectionSummary? FinancialPosition = null, string? PolicyNote = null);
public sealed record StatementContribution(string AccountCode, string AccountName, decimal Amount);
public sealed record StatementContributionPage(StatementBasis Basis, string Section, string DestinationCode, string StatementSection,
  decimal LineTotal, int AccountCount, int Page, int PageSize, IReadOnlyList<StatementContribution> Accounts,
  int ProcedureCount, int ProcedurePage, IReadOnlyList<DrillDownProcedure> Procedures);
public sealed record StatementReviewExport(byte[] Content, Guid EngagementId, Guid MappingId, string Revision, string FileName);

/// <summary>Read-only statement navigation fenced to a complete approved source and independently reviewed mapping.
/// Scope is checked before populations are read, all projections are bounded, and a second read fences late changes.</summary>
public static class StatementReviewWorkspace
{
  public const int PageSize = 25;
  public const int ExportByteLimit = 8 * 1024 * 1024;
  public const string PolicyNote = "Variance = Current Year − Prior Year. Percentage variance = (Current Year − Prior Year) / |Prior Year| × 100%. A zero prior denominator produces clearly labelled N/A. Missing prior data is shown as unavailable (—).";
  private static readonly string[] Roles = ["Partner", "Manager", "Senior", "Staff", "Auditor", "Reviewer", "Administrator", "AccountingPreparer", "AccountingReviewer"];
  private sealed record Snapshot(StatementBasis Basis, StatementDrillDownView View, MappedTrialBalanceSource.Source Source,
    IReadOnlyDictionary<string, string> Names, IReadOnlyList<AuditProcedure> Procedures, IReadOnlyDictionary<Guid, string> RiskAreas,
    IReadOnlyDictionary<(string DestinationCode, string StatementSection), decimal> PriorBalances,
    MaterialityCalculationView? Calculation, IReadOnlyList<RiskBandAssessment> Assessments, IReadOnlyList<AuditRisk> Risks);
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static bool Section(string s) => s is "profit" or "position" or "split";
  private static StatementView View(Snapshot s, string section) => section == "profit" ? s.View.ProfitOrLoss : s.View.FinancialPosition;
  private static Task<CommandResult> Authorize(IAuditSphereDbContext db, ActorContext actor, Guid client, Guid engagement, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, client, engagement, Roles, InternalOnly: true), ct);

  private static async Task<Dictionary<(string DestinationCode, string StatementSection), decimal>> LoadPriorBalancesAsync(
    IClientAccountingDbContext db, Guid firmId, Guid clientId, Guid engagementId, MappingVersion currentMapping, TrialBalanceDataset currentDataset, CancellationToken ct)
  {
    var map = new Dictionary<(string DestinationCode, string StatementSection), decimal>();

    var compPackageId = await db.FinancialPackages.AsNoTracking()
      .Where(p => p.FirmId == firmId && p.ClientId == clientId && p.EngagementId == engagementId &&
        p.Status == AccountingPackageStates.PackageValidated && p.ComparativePackageId != null)
      .Select(p => p.ComparativePackageId)
      .FirstOrDefaultAsync(ct);

    if (compPackageId is { } cpId)
    {
      var compLines = await db.FinancialPackageLines.AsNoTracking()
        .Where(x => x.FinancialPackageId == cpId && x.FirmId == firmId)
        .GroupBy(x => new { x.DestinationCode, Section = x.StatementSection.Trim().ToUpperInvariant() })
        .Select(g => new { g.Key.DestinationCode, g.Key.Section, Total = g.Sum(x => x.Amount) })
        .ToListAsync(ct);

      foreach (var l in compLines)
        map[(l.DestinationCode, l.Section)] = MoneyPolicy.Normalize(l.Total);

      if (map.Count > 0) return map;
    }

    var earlierPackage = await db.FinancialPackages.AsNoTracking()
      .Where(p => p.FirmId == firmId && p.ClientId == clientId &&
        p.Status == AccountingPackageStates.PackageValidated &&
        p.Currency == currentDataset.Currency &&
        string.Compare(p.PeriodEnd, currentMapping.PeriodStart) <= 0)
      .OrderByDescending(p => p.PeriodEnd)
      .ThenByDescending(p => p.Id)
      .FirstOrDefaultAsync(ct);

    if (earlierPackage is not null)
    {
      var earlierLines = await db.FinancialPackageLines.AsNoTracking()
        .Where(x => x.FinancialPackageId == earlierPackage.Id && x.FirmId == firmId)
        .GroupBy(x => new { x.DestinationCode, Section = x.StatementSection.Trim().ToUpperInvariant() })
        .Select(g => new { g.Key.DestinationCode, g.Key.Section, Total = g.Sum(x => x.Amount) })
        .ToListAsync(ct);

      foreach (var l in earlierLines)
        map[(l.DestinationCode, l.Section)] = MoneyPolicy.Normalize(l.Total);

      if (map.Count > 0) return map;
    }

    var earlierMapping = await db.MappingVersions.AsNoTracking()
      .Where(m => m.FirmId == firmId && m.ClientId == clientId && m.Id != currentMapping.Id &&
        m.Status == AccountingPackageStates.MappingApproved &&
        string.Compare(m.PeriodEnd, currentMapping.PeriodStart) <= 0)
      .OrderByDescending(m => m.PeriodEnd)
      .ThenByDescending(m => m.ApprovedAt)
      .FirstOrDefaultAsync(ct);

    if (earlierMapping is not null)
    {
      var earlierDataset = await db.TrialBalanceDatasets.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == earlierMapping.DatasetId && x.FirmId == firmId && x.Currency == currentDataset.Currency, ct);

      if (earlierDataset is { ImportState: TrialBalanceImportStates.Sealed, Balanced: true })
      {
        var balances = await db.TrialBalanceRows.AsNoTracking()
          .Where(x => x.DatasetId == earlierDataset.Id)
          .GroupBy(x => x.AccountCode)
          .Select(g => new { g.Key, Amount = g.Sum(x => x.Amount) })
          .ToDictionaryAsync(x => x.Key, x => x.Amount, ct);

        var allocations = await db.MappingAllocations.AsNoTracking()
          .Where(x => x.FirmId == firmId && x.MappingVersionId == earlierMapping.Id)
          .ToListAsync(ct);

        if (allocations.Count > 0 && balances.Count > 0)
        {
          var built = MappedTrialBalanceSource.Build(earlierMapping, earlierDataset, balances, allocations);
          var proj = FinancialStatementDrillDownQuery.Project(built, _ => []);
          foreach (var line in proj.ProfitOrLoss.Lines)
            map[(line.DestinationCode, line.StatementSection)] = line.Amount;
          foreach (var line in proj.FinancialPosition.Lines)
            map[(line.DestinationCode, line.StatementSection)] = line.Amount;
          return map;
        }
      }
    }

    return map;
  }

  private static async Task<CommandResult<Snapshot>> ReadAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct)
  {
    var e = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (e is null) return Fail<Snapshot>(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await Authorize(db, actor, e.PracticeClientId, engagementId, ct);
    if (!auth.Succeeded) return Fail<Snapshot>(auth.ErrorCode!, auth.Message!);
    var m = await MappedTrialBalanceSource.CurrentMappingQuery(db, actor.FirmId, engagementId).FirstOrDefaultAsync(ct);
    if (m is null || m.ClientId != e.PracticeClientId) return Fail<Snapshot>(ErrorCodes.GateBlocked, "Statements require an approved mapping for this engagement.");
    var d = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == m.DatasetId && x.ClientId == m.ClientId && x.EngagementId == engagementId, ct);
    if (d is null || d.SourceKind != "Raw" || d.ImportState != TrialBalanceImportStates.Sealed || !d.Balanced || d.ControlTotal != 0m || d.ValidationStatus != "Accepted")
      return Fail<Snapshot>(ErrorCodes.GateBlocked, "The exact mapped raw trial balance must be sealed, balanced and validated.");
    var safety = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == m.ClientId, ct);
    var firm = await db.FirmSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.FirmId, ct);
    if (safety is null || firm is null || m.Generation != safety.InputGeneration)
      return Fail<Snapshot>(ErrorCodes.StaleRevision, "Statement inputs changed after mapping approval. Review a new mapping before using current totals.");
    var rows = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == d.Id).OrderBy(x => x.AccountCode).ThenBy(x => x.Id).Take(20_001).ToListAsync(ct);
    var allocations = await db.MappingAllocations.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.MappingVersionId == m.Id && x.ClientId == m.ClientId && x.EngagementId == engagementId)
      .OrderBy(x => x.SourceAccountCode).ThenBy(x => x.DestinationCode).ThenBy(x => x.Id).Take(5_001).ToListAsync(ct);
    var procedures = await db.AuditProcedures.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == m.ClientId && x.EngagementId == engagementId).OrderBy(x => x.Id).Take(5_001).ToListAsync(ct);
    var risks = await db.AuditRisks.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == m.ClientId && x.EngagementId == engagementId).OrderBy(x => x.Id).Take(5_001).ToListAsync(ct);
    if (rows.Count > 20_000 || allocations.Count > 5_000 || procedures.Count > 5_000 || risks.Count > 5_000)
      return Fail<Snapshot>(ErrorCodes.GateBlocked, "This statement basis exceeds the interactive review limit. Use a separately approved larger review workflow.");
    var input = allocations.Select(x => new MappingAllocationInput(x.SourceAccountCode, x.DestinationCode, x.StatementSection, x.Fraction, x.Rationale, x.AuditArea, x.ResidualPolicy)).ToArray();
    var accounts = rows.GroupBy(x => x.AccountCode, StringComparer.Ordinal).Select(x => new MappingSourceAccount(x.Key, x.First().AccountName, x.Sum(y => y.Amount), x.First().Currency)).ToArray();
    var allocationError = input.Length == 0 ? "A complete mapping is required." : FinancialStatementService.ValidateReviewAllocations(accounts, input);
    if (allocationError is not null || allocations.Any(x => !FinancialStatementDrillDownQuery.SupportedSection(x.StatementSection) || x.ResidualPolicy != "LAST_DESTINATION" || MoneyPolicy.Normalize(x.Fraction) != x.Fraction) ||
      rows.Any(x => x.Currency != d.Currency) || m.ApprovedByUserId is null || m.ApprovedByUserId == m.CreatedByUserId || m.ApprovedAt is null)
      return Fail<Snapshot>(ErrorCodes.GateBlocked, "The mapping requires complete, exact allocations and an independent retained approval.");
    var taxonomy = await db.ReportingTaxonomyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Code == m.TaxonomyVersion, ct);
    var nodes = taxonomy is null ? [] : await db.ReportingTaxonomyNodes.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.TaxonomyVersionId == taxonomy.Id).OrderBy(x => x.Id).Take(5_001).ToListAsync(ct);
    if (taxonomy?.Status != AccountingWorkflowStates.Approved || nodes.Count > 5_000 || input.Any(x => !nodes.Any(n => n.IsPosting && n.Code == x.DestinationCode && n.StatementSection == x.StatementSection)))
      return Fail<Snapshot>(ErrorCodes.GateBlocked, "The exact mapping taxonomy is unavailable or no longer approved.");
    var period = d.PeriodId is { } periodId ? await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == m.ClientId && x.Id == periodId, ct) : null;
    var book = d.BookId is { } bookId ? await db.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == m.ClientId && x.PeriodId == d.PeriodId && x.Id == bookId, ct) : null;
    var chart = m.ClientChartVersionId is { } chartId ? await db.ClientChartVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == m.ClientId && x.Id == chartId, ct) : null;
    if (!DateOnly.TryParseExact(m.PeriodStart, "yyyy-MM-dd", out var start) || !DateOnly.TryParseExact(m.PeriodEnd, "yyyy-MM-dd", out var end) || start > end ||
      d.PeriodId is not null && (period is null || period.StartDate != start || period.EndDate != end) || d.BookId is not null && book is null ||
      m.ClientChartVersionId is not null && (chart is null || chart.Status != AccountingWorkflowStates.Approved || chart.EffectiveFrom > start || chart.EffectiveTo is { } chartEnd && chartEnd < end))
      return Fail<Snapshot>(ErrorCodes.GateBlocked, "The exact chart or reporting period no longer matches this mapping basis.");
    var digest = MappedTrialBalanceSource.Digest(d);
    if (!SourceAcceptanceWorkspace.ValidHash(digest)) return Fail<Snapshot>(ErrorCodes.GateBlocked, "The source digest is unavailable.");
    var source = MappedTrialBalanceSource.Build(m, d, accounts.ToDictionary(x => x.AccountCode, x => x.Amount, StringComparer.Ordinal), allocations);
    var riskAreas = risks.ToDictionary(x => x.Id, x => x.AccountArea);
    var projected = FinancialStatementDrillDownQuery.Project(source, _ => []);
    var revision = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { actor.FirmId, actor.UserId, actor.SessionEpoch, e, m, d, rows, allocations, procedures, risks, firm, safety, chart, taxonomy, nodes, period, book })));
    auth = await Authorize(db, actor, m.ClientId, engagementId, ct);
    if (!auth.Succeeded) return Fail<Snapshot>(auth.ErrorCode!, auth.Message!);

    var priorBalances = await LoadPriorBalancesAsync(db, actor.FirmId, m.ClientId, engagementId, m, d, ct);
    var calculation = await MaterialityEngineService.GetLatestAsync(db, actor.FirmId, engagementId, ct);
    var assessments = await db.RiskBandAssessments.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId).ToListAsync(ct);

    var basis = new StatementBasis(engagementId, m.ClientId, m.Id, m.Version, d.Id, d.Revision, digest, d.Currency, m.PeriodStart, m.PeriodEnd, m.TaxonomyVersion, m.ClientChartVersionId, m.Generation, m.ApprovedByUserId, m.ApprovedAt, revision);
    return CommandResult<Snapshot>.Ok(new(basis, projected, source, accounts.ToDictionary(x => x.AccountCode, x => x.AccountName, StringComparer.Ordinal), procedures, riskAreas, priorBalances, calculation, assessments, risks));
  }

  private static async Task<CommandResult<Snapshot>> StableAsync(IClientAccountingDbContext db, ActorContext actor, Guid id, string? revision, CancellationToken ct)
  {
    var first = await ReadAsync(db, actor, id, ct); if (!first.Succeeded) return first;
    var final = await ReadAsync(db, actor, id, ct); if (!final.Succeeded) return final;
    if (first.Value!.Basis.Revision != final.Value!.Basis.Revision || revision is not null && (!SourceAcceptanceWorkspace.ValidHash(revision) || revision != final.Value.Basis.Revision))
      return Fail<Snapshot>(ErrorCodes.StaleRevision, "Statement or supporting evidence changed. Refresh the approved basis before continuing.");
    return final;
  }

  private static IReadOnlyList<DrillDownProcedure> Procedures(Snapshot s, StatementLineView line)
  {
    var areas = s.Source.Lines.Where(x => x.DestinationCode == line.DestinationCode && x.StatementSection.Trim().ToUpperInvariant() == line.StatementSection)
      .Select(x => string.IsNullOrWhiteSpace(x.AuditArea) ? x.DestinationCode : x.AuditArea!).Distinct(StringComparer.Ordinal).ToArray();
    return s.Procedures.Where(p => areas.Any(a => FinancialStatementDrillDownQuery.Matches(p.SourceSectionTitle, a) ||
      p.RiskId is { } r && FinancialStatementDrillDownQuery.Matches(s.RiskAreas.GetValueOrDefault(r), a))).OrderBy(p => p.SourceProcedureId, StringComparer.Ordinal).ThenBy(p => p.Id)
      .Select(p => new DrillDownProcedure(p.Id, p.SourceProcedureId, p.Title, p.Status, p.SourceSectionTitle)).ToArray();
  }

  private static StatementReviewLine MapLine(Snapshot s, StatementLineView line)
  {
    var procs = Procedures(s, line);
    decimal? priorAmount = null;
    decimal? variance = null;
    string? percentageVariance = null;

    if (s.PriorBalances.TryGetValue((line.DestinationCode, line.StatementSection), out var prior))
    {
      priorAmount = MoneyPolicy.Normalize(prior);
      variance = MoneyPolicy.Normalize(line.Amount - prior);
      if (prior == 0m)
      {
        percentageVariance = "N/A";
      }
      else
      {
        var pct = Math.Round(((line.Amount - prior) / Math.Abs(prior)) * 100m, 2, MidpointRounding.AwayFromZero);
        percentageVariance = (pct > 0 ? "+" : "") + pct.ToString("0.00", CultureInfo.InvariantCulture) + "%";
      }
    }

    var matchingRisks = s.Risks.Where(r =>
      (!string.IsNullOrWhiteSpace(line.AuditArea) && string.Equals(r.AccountArea.Trim(), line.AuditArea.Trim(), StringComparison.OrdinalIgnoreCase)) ||
      string.Equals(r.AccountArea.Trim(), line.DestinationCode.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

    var matchingRiskIds = matchingRisks.Select(r => r.Id).ToHashSet();
    var matchingAssessments = s.Assessments.Where(a => matchingRiskIds.Contains(a.RiskId)).ToList();

    var significant = matchingRisks.Any(r => r.SignificanceDecision == SignificanceDecisions.Significant) ||
                      matchingAssessments.Any(a => a.Significant);
    var fraud = matchingAssessments.Any(a => a.FraudRisk);
    var critical = matchingRisks.Any(r => ProcedureRiskBandEvaluator.HasEstimateIndicator(r.Description) ||
                                          ProcedureRiskBandEvaluator.HasEstimateIndicator(r.Drivers));
    var highInherent = matchingAssessments.Any(a => a.LikelihoodScore == 3 && a.MagnitudeScore >= 2);

    string band;
    string performer;
    string reviewer;
    string explanation;

    if (s.Calculation is { State: MaterialityCalculationStates.Approved })
    {
      var c = s.Calculation.Calculation;
      band = FsliRiskBandRules.Band(line.Amount, c.TolerableError, c.PlanningMateriality, critical, highInherent, significant, fraud);
      performer = FsliRiskBandRules.Route(band);
      reviewer = FsliRiskBandRules.ReviewerDescription(band);
      explanation = FsliRiskBandRules.Explain(line.Amount, c.TolerableError, c.PlanningMateriality, band, critical, highInherent, significant, fraud);
    }
    else
    {
      var qualitativeBand = matchingAssessments.FirstOrDefault()?.Band ?? RiskBandRules.Band(1, 1, significant, fraud);
      band = critical || highInherent || significant || fraud ? RiskBands.Red : qualitativeBand;
      performer = FsliRiskBandRules.Route(band);
      reviewer = FsliRiskBandRules.ReviewerDescription(band);
      explanation = "Planning materiality calculation pending approval; classification based on qualitative risk assessment.";
    }

    var riskLabel = band switch
    {
      RiskBands.Red => "High (Red)",
      RiskBands.Amber => "Medium (Amber)",
      _ => "Low (Green)"
    };

    string reviewStatus;
    string reviewStatusLabel;

    if (procs.Count == 0)
    {
      reviewStatus = "NO_PROCEDURES";
      reviewStatusLabel = "No procedures linked";
    }
    else if (procs.All(p => p.Status == AuditProcedureStatuses.Reviewed))
    {
      reviewStatus = "REVIEWED";
      reviewStatusLabel = $"{procs.Count} of {procs.Count} reviewed";
    }
    else if (procs.Any(p => p.Status == AuditProcedureStatuses.ChangesRequired))
    {
      reviewStatus = "CHANGES_REQUIRED";
      reviewStatusLabel = "Changes required";
    }
    else if (procs.Any(p => p.Status is AuditProcedureStatuses.InReview or AuditProcedureStatuses.Submitted))
    {
      reviewStatus = "IN_REVIEW";
      reviewStatusLabel = $"{procs.Count(p => p.Status is AuditProcedureStatuses.Submitted or AuditProcedureStatuses.InReview)} in review";
    }
    else if (procs.Any(p => p.Status == AuditProcedureStatuses.InProgress))
    {
      reviewStatus = "IN_PROGRESS";
      reviewStatusLabel = $"{procs.Count(p => p.Status == AuditProcedureStatuses.InProgress)} in progress";
    }
    else
    {
      reviewStatus = "PLANNED";
      reviewStatusLabel = $"{procs.Count} planned";
    }

    return new StatementReviewLine(
      DestinationCode: line.DestinationCode,
      StatementSection: line.StatementSection,
      AuditArea: line.AuditArea,
      Amount: line.Amount,
      AccountCount: line.SourceAccountCount,
      ProcedureCount: procs.Count,
      PriorAmount: priorAmount,
      Variance: variance,
      PercentageVariance: percentageVariance,
      RiskBand: band,
      RiskLabel: riskLabel,
      AssignedPerformer: performer,
      RequiredReviewer: reviewer,
      ReviewStatus: reviewStatus,
      ReviewStatusLabel: reviewStatusLabel,
      RiskExplanation: explanation
    );
  }

  private static StatementSectionSummary BuildSectionSummary(Snapshot s, StatementView view, string sectionKey, string? filter)
  {
    var q = filter?.Trim() ?? "";
    var allMapped = view.Lines.Select(l => MapLine(s, l)).ToList();
    var filtered = allMapped.Where(x => q.Length == 0 ||
      x.DestinationCode.Contains(q, StringComparison.OrdinalIgnoreCase) ||
      x.StatementSection.Contains(q, StringComparison.OrdinalIgnoreCase) ||
      x.AuditArea.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

    decimal? priorTotal = null;
    decimal? varianceTotal = null;
    string? percentageVarianceTotal = null;

    var priorPresent = allMapped.Where(x => x.PriorAmount.HasValue).ToList();
    if (priorPresent.Count > 0)
    {
      var sumPrior = priorPresent.Sum(x => x.PriorAmount!.Value);
      priorTotal = MoneyPolicy.Normalize(sumPrior);
      varianceTotal = MoneyPolicy.Normalize(view.Total - sumPrior);
      if (sumPrior == 0m)
      {
        percentageVarianceTotal = "N/A";
      }
      else
      {
        var pct = Math.Round(((view.Total - sumPrior) / Math.Abs(sumPrior)) * 100m, 2, MidpointRounding.AwayFromZero);
        percentageVarianceTotal = (pct > 0 ? "+" : "") + pct.ToString("0.00", CultureInfo.InvariantCulture) + "%";
      }
    }

    return new StatementSectionSummary(
      Section: sectionKey,
      Title: view.Title,
      TotalLabel: view.TotalLabel,
      CurrentTotal: view.Total,
      PriorTotal: priorTotal,
      VarianceTotal: varianceTotal,
      PercentageVarianceTotal: percentageVarianceTotal,
      LineCount: filtered.Count,
      Lines: filtered
    );
  }

  public static async Task<CommandResult<StatementReviewPage>> GetAsync(IClientAccountingDbContext db, ActorContext actor, Guid id, string section = "profit", string? filter = null, int page = 1, CancellationToken ct = default)
  {
    if (!Section(section) || (filter?.Length ?? 0) > 80 || page < 1 || page > 1_000) return Fail<StatementReviewPage>(ErrorCodes.Accounting.MappingInvalid, "Choose a statement section, bounded filter and valid page.");
    var stable = await StableAsync(db, actor, id, null, ct); if (!stable.Succeeded) return Fail<StatementReviewPage>(stable.ErrorCode!, stable.Message!);
    var s = stable.Value!;
    var profitSummary = BuildSectionSummary(s, s.View.ProfitOrLoss, "profit", filter);
    var positionSummary = BuildSectionSummary(s, s.View.FinancialPosition, "position", filter);

    if (section == "split")
    {
      var allSplitLines = profitSummary.Lines.Concat(positionSummary.Lines).ToList();
      var pagedSplitLines = allSplitLines.Skip((page - 1) * PageSize).Take(PageSize).ToList();
      return CommandResult<StatementReviewPage>.Ok(new(
        Basis: s.Basis,
        Section: "split",
        Title: "Financial statements (comparative split dashboard)",
        TotalLabel: "Profit for the period (P&L) / Net financial position (B/S)",
        Total: s.View.ProfitOrLoss.Total,
        Balances: s.View.Balances,
        LineCount: s.View.ProfitOrLoss.Lines.Count + s.View.FinancialPosition.Lines.Count,
        FilteredCount: allSplitLines.Count,
        Page: page,
        PageSize: PageSize,
        Lines: pagedSplitLines,
        ProfitOrLoss: profitSummary,
        FinancialPosition: positionSummary,
        PolicyNote: PolicyNote
      ));
    }

    var view = View(s, section);
    var targetSummary = section == "profit" ? profitSummary : positionSummary;
    var filtered = targetSummary.Lines;
    if (page > Math.Max(1, (filtered.Count + PageSize - 1) / PageSize)) return Fail<StatementReviewPage>(ErrorCodes.Accounting.MappingInvalid, "The statement page is outside this filtered basis.");

    return CommandResult<StatementReviewPage>.Ok(new(
      Basis: s.Basis,
      Section: section,
      Title: view.Title,
      TotalLabel: view.TotalLabel,
      Total: view.Total,
      Balances: s.View.Balances,
      LineCount: view.Lines.Count,
      FilteredCount: filtered.Count,
      Page: page,
      PageSize: PageSize,
      Lines: filtered.Skip((page - 1) * PageSize).Take(PageSize).ToList(),
      ProfitOrLoss: profitSummary,
      FinancialPosition: positionSummary,
      PolicyNote: PolicyNote
    ));
  }

  public static Task<CommandResult<StatementReviewPage>> GetSplitDashboardAsync(IClientAccountingDbContext db, ActorContext actor, Guid id, string? filter = null, CancellationToken ct = default) =>
    GetAsync(db, actor, id, "split", filter, 1, ct);

  public static async Task<CommandResult<StatementContributionPage>> ContributionsAsync(IClientAccountingDbContext db, ActorContext actor, Guid id, string section, string destination, string statementSection, string revision, int page = 1, int procedurePage = 1, CancellationToken ct = default)
  {
    if (!Section(section) || destination is not { Length: > 0 and <= 100 } || statementSection is not { Length: > 0 and <= 100 } || page < 1 || procedurePage < 1 || page > 1_000 || procedurePage > 1_000)
      return Fail<StatementContributionPage>(ErrorCodes.Accounting.MappingInvalid, "Choose an exact statement line and bounded pages.");
    var stable = await StableAsync(db, actor, id, revision, ct); if (!stable.Succeeded) return Fail<StatementContributionPage>(stable.ErrorCode!, stable.Message!);
    var s = stable.Value!; var line = View(s, section).Lines.SingleOrDefault(x => x.DestinationCode == destination && x.StatementSection == statementSection);
    if (line is null) return Fail<StatementContributionPage>(ErrorCodes.ScopeDenied, "The line is unavailable in this statement basis.");
    var accounts = Contributions(s, line); var procedures = Procedures(s, line);
    if (page > Math.Max(1, (accounts.Count + PageSize - 1) / PageSize) || procedurePage > Math.Max(1, (procedures.Count + PageSize - 1) / PageSize))
      return Fail<StatementContributionPage>(ErrorCodes.Accounting.MappingInvalid, "The detail page is outside this statement line.");
    return CommandResult<StatementContributionPage>.Ok(new(s.Basis, section, destination, statementSection, line.Amount, accounts.Count, page, PageSize,
      accounts.Skip((page - 1) * PageSize).Take(PageSize).ToArray(), procedures.Count, procedurePage, procedures.Skip((procedurePage - 1) * PageSize).Take(PageSize).ToArray()));
  }

  private static IReadOnlyList<StatementContribution> Contributions(Snapshot s, StatementLineView line) => s.Source.Lines.Where(x => x.DestinationCode == line.DestinationCode && x.StatementSection.Trim().ToUpperInvariant() == line.StatementSection)
    .GroupBy(x => x.SourceAccountCode, StringComparer.Ordinal).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new StatementContribution(x.Key, s.Names[x.Key], MoneyPolicy.Normalize(x.Sum(y => y.Amount * FinancialStatementDrillDownQuery.DisplaySign(y.StatementSection))))).ToArray();

  public static async Task<CommandResult<StatementReviewExport>> ExportAsync(IClientAccountingDbContext db, ActorContext actor, Guid id, string revision, CancellationToken ct = default)
  {
    var stable = await StableAsync(db, actor, id, revision, ct); if (!stable.Succeeded) return Fail<StatementReviewExport>(stable.ErrorCode!, stable.Message!);
    var s = stable.Value!;
    static string Text(string v) => "\"" + ((v.TrimStart().StartsWith('=') || v.TrimStart().StartsWith('+') || v.TrimStart().StartsWith('-') || v.TrimStart().StartsWith('@') || v.Contains('\t') || v.Contains('\r') || v.Contains('\n')) ? "'" : "") + v.Replace("\"", "\"\"") + "\"";
    var b = new StringBuilder("engagement_id,mapping_id,mapping_version,dataset_id,dataset_digest,basis_revision,period_start,period_end,currency,statement,line,section,account_code,account_name,contribution,line_total,statement_total\r\n");
    foreach (var section in new[] { "profit", "position" })
      foreach (var line in View(s, section).Lines)
        foreach (var account in Contributions(s, line))
        {
          var fields = new[] { id.ToString("D"), s.Basis.MappingId.ToString("D"), s.Basis.MappingVersion.ToString(CultureInfo.InvariantCulture), s.Basis.DatasetId.ToString("D"), s.Basis.DatasetDigest, revision, s.Basis.PeriodStart, s.Basis.PeriodEnd, s.Basis.Currency, section, line.DestinationCode, line.StatementSection, account.AccountCode, account.AccountName };
          b.Append(string.Join(',', fields.Select(Text))).Append(',').Append(account.Amount.ToString(CultureInfo.InvariantCulture)).Append(',').Append(line.Amount.ToString(CultureInfo.InvariantCulture)).Append(',').Append(View(s, section).Total.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
          if (b.Length > ExportByteLimit) return Fail<StatementReviewExport>(ErrorCodes.GateBlocked, "This export exceeds the bounded CSV limit.");
        }
    var content = Encoding.UTF8.GetBytes(b.ToString());
    if (content.Length > ExportByteLimit) return Fail<StatementReviewExport>(ErrorCodes.GateBlocked, "This export exceeds the bounded CSV limit.");
    return CommandResult<StatementReviewExport>.Ok(new(content, id, s.Basis.MappingId, revision, $"auditsphere-statements-{id:D}-{s.Basis.MappingId:D}.csv"));
  }
}
