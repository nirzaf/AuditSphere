using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record CalculateMaterialityRequest(
  Guid EngagementId, string BenchmarkKind, string? DestinationCode, decimal RatePercent, decimal PerformancePercent,
  decimal TrivialPercent, string Rationale, string? QualitativeConsiderations = null);

public sealed record MaterialityBenchmarkOption(string Kind, string? DestinationCode, string Label, decimal? Amount, int LineCount);

public sealed record MaterialitySourceView(
  Guid MappingVersionId, long MappingVersion, Guid DatasetId, string DatasetDigest, string Currency,
  IReadOnlyList<MaterialityBenchmarkOption> Options);

public static class MaterialityCalculationStates
{
  public const string Draft = "DRAFT";
  public const string Approved = "APPROVED";
  /// <summary>The approved mapping or trial balance it was derived from has been replaced.</summary>
  public const string Stale = "STALE";
}

public sealed record MaterialityCalculationView(Guid AssessmentId, MaterialityCalculation Calculation, string State, string Route);

/// <summary>
/// Automatic Planning Materiality, Tolerable Error and SAD threshold from the engagement's current approved mapping
/// over its sealed, balanced trial balance. The benchmark amount is never typed; a replaced mapping or dataset makes
/// the calculation stale, which blocks its approval and withdraws it from difference evaluation.
/// </summary>
public static class MaterialityEngineService
{
  private static readonly (string Kind, string Label)[] StandardBenchmarks =
  [
    (MaterialityBenchmarks.Revenue, "Revenue (income section)"),
    (MaterialityBenchmarks.ProfitBeforeTax, "Profit before tax (income and expense, excluding tax lines)"),
    (MaterialityBenchmarks.TotalAssets, "Total assets"),
    (MaterialityBenchmarks.NetAssets, "Net assets (assets less liabilities)"),
    (MaterialityBenchmarks.TotalExpenses, "Total expenses")
  ];

  public static async Task<CommandResult<MaterialitySourceView>> GetSourceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<MaterialitySourceView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId,
      engagementId, AuditPlanningService.PlanningRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<MaterialitySourceView>.Fail(auth.ErrorCode!, auth.Message!);
    var source = await LoadSourceAsync(db, actor.FirmId, engagementId, ct);
    if (source is null)
      return CommandResult<MaterialitySourceView>.Fail(ErrorCodes.GateBlocked,
        "Materiality needs an approved mapping over a sealed, balanced trial balance for this engagement.");
    var (mapping, dataset, lines) = source.Value;
    var options = StandardBenchmarks.Select(b =>
    {
      var derived = MaterialityCalculator.DeriveBenchmark(b.Kind, null, lines);
      return new MaterialityBenchmarkOption(b.Kind, null, b.Label, derived?.Amount, derived?.LineCount ?? 0);
    }).Concat(lines.GroupBy(x => x.DestinationCode, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.Ordinal)
      .Select(g =>
      {
        var derived = MaterialityCalculator.DeriveBenchmark(MaterialityBenchmarks.MappedLine, g.Key, lines);
        return new MaterialityBenchmarkOption(MaterialityBenchmarks.MappedLine, g.Key, $"Mapped line {g.Key} ({g.First().StatementSection})",
          derived?.Amount, derived?.LineCount ?? 0);
      })).ToList();
    return CommandResult<MaterialitySourceView>.Ok(new(mapping.Id, mapping.Version, dataset.Id, DatasetDigest(dataset), dataset.Currency, options));
  }

  public static async Task<CommandResult<MaterialityCalculationView>> CalculateAsync(
    IAuditSphereDbContext db, ActorContext actor, CalculateMaterialityRequest request, CancellationToken ct = default)
  {
    var kind = (request.BenchmarkKind ?? string.Empty).Trim().ToUpperInvariant();
    var invalid = MaterialityCalculator.Validate(kind, request.RatePercent, request.PerformancePercent, request.TrivialPercent);
    if (invalid is null && string.IsNullOrWhiteSpace(request.Rationale)) invalid = "A rationale for the benchmark choice is required.";
    if (invalid is not null) return CommandResult<MaterialityCalculationView>.Fail(ErrorCodes.AuditPlanning.Invalid, invalid);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var scope = await AuditPlanningService.LockedEngagementAsync(db, actor, request.EngagementId, ct);
    if (scope.Denied is not null) return CommandResult<MaterialityCalculationView>.Fail(scope.Denied, scope.Message);
    var source = await LoadSourceAsync(db, actor.FirmId, request.EngagementId, ct);
    if (source is null)
      return CommandResult<MaterialityCalculationView>.Fail(ErrorCodes.GateBlocked,
        "Materiality needs an approved mapping over a sealed, balanced trial balance for this engagement.");
    var (mapping, dataset, lines) = source.Value;
    var derived = MaterialityCalculator.DeriveBenchmark(kind, request.DestinationCode, lines);
    if (derived is null)
      return CommandResult<MaterialityCalculationView>.Fail(ErrorCodes.GateBlocked, "The chosen benchmark has no mapped trial-balance lines.");
    if (derived.Value.Amount <= 0)
      return CommandResult<MaterialityCalculationView>.Fail(ErrorCodes.GateBlocked,
        $"The {kind} benchmark derives to {derived.Value.Amount:N2} {dataset.Currency}; choose a benchmark with a positive amount.");
    var figures = MaterialityCalculator.Calculate(derived.Value.Amount, derived.Value.LineCount, request.RatePercent, request.PerformancePercent, request.TrivialPercent);
    var digest = DatasetDigest(dataset);
    var destination = kind == MaterialityBenchmarks.MappedLine ? request.DestinationCode!.Trim().ToUpperInvariant() : null;
    var now = DateTimeOffset.UtcNow;
    var assessment = new MaterialityAssessment
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = scope.ClientId, EngagementId = request.EngagementId, ActorId = actor.UserId,
      BenchmarkSource = $"TB:{kind}{(destination is null ? string.Empty : ":" + destination)}",
      BenchmarkVersion = $"mapping {mapping.Id:D} v{mapping.Version}; dataset {digest[..16]}",
      Rationale = request.Rationale.Trim(), BenchmarkAmount = figures.BenchmarkAmount, RateApplied = request.RatePercent / 100m,
      OverallMateriality = figures.PlanningMateriality, PerformanceMateriality = figures.TolerableError,
      ClearlyTrivialThreshold = figures.SadThreshold,
      QualitativeConsiderations = string.IsNullOrWhiteSpace(request.QualitativeConsiderations) ? null : request.QualitativeConsiderations.Trim(),
      Status = MaterialityStatuses.Draft, CreatedAt = now
    };
    var calculation = new MaterialityCalculation
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = scope.ClientId, EngagementId = request.EngagementId,
      MaterialityAssessmentId = assessment.Id, MappingVersionId = mapping.Id, MappingVersionNumber = mapping.Version,
      DatasetId = dataset.Id, DatasetDigest = digest, BenchmarkKind = kind, DestinationCode = destination,
      SourceLineCount = figures.SourceLineCount, BenchmarkAmount = figures.BenchmarkAmount, Currency = dataset.Currency,
      RatePercent = request.RatePercent, PerformancePercent = request.PerformancePercent, TrivialPercent = request.TrivialPercent,
      PlanningMateriality = figures.PlanningMateriality, TolerableError = figures.TolerableError, SadThreshold = figures.SadThreshold,
      PolicyVersion = MaterialityCalculator.PolicyVersion,
      InputHash = MaterialityCalculator.InputHash(mapping.Id, digest, kind, destination, figures.BenchmarkAmount,
        request.RatePercent, request.PerformancePercent, request.TrivialPercent),
      CreatedByUserId = actor.UserId, CreatedAt = now
    };
    db.MaterialityAssessments.Add(assessment);
    db.MaterialityCalculations.Add(calculation);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<MaterialityCalculationView>.Ok(new(assessment.Id, calculation, MaterialityCalculationStates.Draft, "Awaiting independent approval."));
  }

  /// <summary>The engagement's latest calculated materiality and whether it is still bound to the current sources.</summary>
  public static async Task<MaterialityCalculationView?> GetLatestAsync(IAuditSphereDbContext db, Guid firmId, Guid engagementId, CancellationToken ct = default)
  {
    var calculation = await db.MaterialityCalculations.AsNoTracking().Where(x => x.FirmId == firmId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    if (calculation is null) return null;
    var current = await IsCurrentAsync(db, calculation, ct);
    var approved = await db.MaterialityApprovals.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.MaterialityAssessmentId == calculation.MaterialityAssessmentId, ct);
    var state = !current ? MaterialityCalculationStates.Stale : approved ? MaterialityCalculationStates.Approved : MaterialityCalculationStates.Draft;
    var route = state switch
    {
      MaterialityCalculationStates.Stale => "The approved mapping or trial balance changed. Recalculate; this calculation no longer supports difference evaluation.",
      MaterialityCalculationStates.Approved => "Approved and bound to the current mapping and trial balance.",
      _ => "Awaiting independent approval by a Manager or Partner who did not prepare it."
    };
    return new(calculation.MaterialityAssessmentId, calculation, state, route);
  }

  /// <summary>
  /// True when the assessment has no engine calculation (a manual legacy record) or its calculation still matches the
  /// engagement's current approved mapping and dataset.
  /// </summary>
  public static async Task<bool> IsAssessmentCurrentAsync(IAuditSphereDbContext db, Guid firmId, Guid assessmentId, CancellationToken ct = default)
  {
    var calculation = await db.MaterialityCalculations.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.MaterialityAssessmentId == assessmentId, ct);
    return calculation is null || await IsCurrentAsync(db, calculation, ct);
  }

  private static async Task<bool> IsCurrentAsync(IAuditSphereDbContext db, MaterialityCalculation calculation, CancellationToken ct)
  {
    var mapping = await CurrentMappingQuery(db, calculation.FirmId, calculation.EngagementId).FirstOrDefaultAsync(ct);
    if (mapping is null || mapping.Id != calculation.MappingVersionId) return false;
    var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mapping.DatasetId && x.FirmId == calculation.FirmId, ct);
    return dataset is not null && DatasetDigest(dataset) == calculation.DatasetDigest;
  }

  private static IQueryable<MappingVersion> CurrentMappingQuery(IAuditSphereDbContext db, Guid firmId, Guid engagementId) =>
    MappedTrialBalanceSource.CurrentMappingQuery(db, firmId, engagementId);

  private static string DatasetDigest(TrialBalanceDataset dataset) => MappedTrialBalanceSource.Digest(dataset);

  private static async Task<(MappingVersion Mapping, TrialBalanceDataset Dataset, List<MappedBenchmarkLine> Lines)?> LoadSourceAsync(
    IAuditSphereDbContext db, Guid firmId, Guid engagementId, CancellationToken ct) =>
    await MappedTrialBalanceSource.LoadAsync(db, firmId, engagementId, ct) is { } source
      ? (source.Mapping, source.Dataset, source.BenchmarkLines.ToList())
      : null;
}
