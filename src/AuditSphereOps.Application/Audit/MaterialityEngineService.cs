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
  public const string BlockedPolicy = "BLOCKED_POLICY";
}

public sealed record MaterialityCalculationView(Guid AssessmentId, MaterialityCalculation Calculation, string State, string Route,
  MaterialityRoundingDecision? Rounding = null);

/// <summary>The calculation behind an assessment, its own rounding decision (if it is an effective assessment), and head status.</summary>
public sealed record ResolvedMateriality(MaterialityCalculation? Calculation, MaterialityRoundingDecision? Rounding, bool IsCurrentHead);

public sealed record PracticalRoundingRequest(Guid AssessmentId, decimal PlanningMateriality, decimal TolerableError, decimal SadThreshold, string Rationale);

/// <summary>The new effective draft assessment carrying the rounded thresholds, with its append-only decision.</summary>
public sealed record PracticalRoundingView(Guid EffectiveAssessmentId, MaterialityRoundingDecision Decision, MaterialityAssessment EffectiveAssessment, string State);

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
    (MaterialityBenchmarks.ProfitBeforeTax, "Profit before tax (mapped balances, excluding tax; no normalization applied)"),
    (MaterialityBenchmarks.TotalAssets, "Total assets"),
    (MaterialityBenchmarks.NetAssets, "Equity / net assets (assets less liabilities)")
  ];

  /// <summary>Practical rounding is an Audit Manager decision; Partner approval of the rounded values stays separate.</summary>
  private static readonly string[] RoundingRoles = ["Manager", "SeniorManager"];

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
    }).ToList();
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
    var rounding = await LatestRoundingAsync(db, firmId, calculation.Id, ct);
    var headAssessmentId = rounding?.EffectiveAssessmentId ?? calculation.MaterialityAssessmentId;
    var assessment = await db.MaterialityAssessments.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.Id == headAssessmentId, ct);
    var policyCurrent = assessment is not null && MatchesCurrentPolicy(assessment, calculation, rounding);
    var approved = assessment is not null && await HasIndependentPartnerApprovalAsync(db, assessment, ct);
    var state = !current ? MaterialityCalculationStates.Stale : !policyCurrent ? MaterialityCalculationStates.BlockedPolicy :
      approved ? MaterialityCalculationStates.Approved : MaterialityCalculationStates.Draft;
    var route = state switch
    {
      MaterialityCalculationStates.Stale => "The approved mapping or trial balance changed. Recalculate; this calculation no longer supports difference evaluation.",
      MaterialityCalculationStates.BlockedPolicy => "This historical calculation does not meet the current materiality policy. Recalculate before approval.",
      MaterialityCalculationStates.Approved => "Approved and bound to the current mapping and trial balance.",
      _ => "Awaiting independent Engagement Partner materiality approval."
    };
    return new(headAssessmentId, calculation, state, route, rounding);
  }

  /// <summary>
  /// Resolves the calculation an assessment belongs to. An original calculation's own assessment resolves directly; an
  /// effective (practically rounded) assessment resolves through its rounding decision. <see cref="ResolvedMateriality.IsCurrentHead"/>
  /// is true only when the assessment is the newest effective assessment of the newest calculation for its engagement, so a
  /// superseded draft can never be approved or read as current.
  /// </summary>
  public static async Task<ResolvedMateriality> ResolveAsync(IAuditSphereDbContext db, MaterialityAssessment assessment, CancellationToken ct = default)
  {
    var ownRounding = await db.MaterialityRoundingDecisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == assessment.FirmId && x.EffectiveAssessmentId == assessment.Id, ct);
    var calculation = ownRounding is null
      ? await db.MaterialityCalculations.AsNoTracking().SingleOrDefaultAsync(x =>
          x.FirmId == assessment.FirmId && x.MaterialityAssessmentId == assessment.Id, ct)
      : await db.MaterialityCalculations.AsNoTracking().SingleOrDefaultAsync(x =>
          x.FirmId == assessment.FirmId && x.Id == ownRounding.MaterialityCalculationId, ct);
    if (calculation is null) return new(null, null, false);
    var latestRounding = await LatestRoundingAsync(db, assessment.FirmId, calculation.Id, ct);
    var headAssessmentId = latestRounding?.EffectiveAssessmentId ?? calculation.MaterialityAssessmentId;
    var latestCalculationId = await db.MaterialityCalculations.AsNoTracking()
      .Where(x => x.FirmId == assessment.FirmId && x.EngagementId == assessment.EngagementId)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => x.Id).FirstOrDefaultAsync(ct);
    return new(calculation, ownRounding, assessment.Id == headAssessmentId && calculation.Id == latestCalculationId);
  }

  private static async Task<MaterialityRoundingDecision?> LatestRoundingAsync(IAuditSphereDbContext db, Guid firmId, Guid calculationId, CancellationToken ct) =>
    await db.MaterialityRoundingDecisions.AsNoTracking().Where(x => x.FirmId == firmId && x.MaterialityCalculationId == calculationId)
      .OrderByDescending(x => x.DecidedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);

  /// <summary>
  /// True when the assessment has no engine calculation (a manual legacy record) or it is the current head of a calculation
  /// whose mapping and dataset still match the engagement's current approved source.
  /// </summary>
  public static async Task<bool> IsAssessmentCurrentAsync(IAuditSphereDbContext db, Guid firmId, Guid assessmentId, CancellationToken ct = default)
  {
    var assessment = await db.MaterialityAssessments.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.Id == assessmentId, ct);
    if (assessment is null) return false;
    var resolved = await ResolveAsync(db, assessment, ct);
    if (resolved.Calculation is null) return true;
    return resolved.IsCurrentHead && await IsCurrentAsync(db, resolved.Calculation, ct);
  }

  /// <summary>
  /// Recomputes the calculation's figures from its stored inputs and checks the assessment carries exactly them. When a
  /// practical rounding decision is supplied the assessment must instead carry its adjusted figures, and those must still
  /// satisfy the ±5% and hierarchy rules against the stored calculation.
  /// </summary>
  public static bool MatchesCurrentPolicy(MaterialityAssessment assessment, MaterialityCalculation calculation, MaterialityRoundingDecision? rounding = null)
  {
    if (calculation.PolicyVersion != MaterialityCalculator.PolicyVersion ||
        MaterialityCalculator.Validate(calculation.BenchmarkKind, calculation.RatePercent,
          calculation.PerformancePercent, calculation.TrivialPercent) is not null || calculation.BenchmarkAmount <= 0)
      return false;
    var figures = MaterialityCalculator.Calculate(calculation.BenchmarkAmount, calculation.SourceLineCount,
      calculation.RatePercent, calculation.PerformancePercent, calculation.TrivialPercent);
    var expectedHash = MaterialityCalculator.InputHash(calculation.MappingVersionId, calculation.DatasetDigest,
      calculation.BenchmarkKind, calculation.DestinationCode, calculation.BenchmarkAmount,
      calculation.RatePercent, calculation.PerformancePercent, calculation.TrivialPercent);
    if (calculation.InputHash != expectedHash || figures.PlanningMateriality != calculation.PlanningMateriality ||
        figures.TolerableError != calculation.TolerableError || figures.SadThreshold != calculation.SadThreshold)
      return false;
    if (rounding is null)
      return assessment.OverallMateriality == figures.PlanningMateriality && assessment.PerformanceMateriality == figures.TolerableError &&
        assessment.ClearlyTrivialThreshold == figures.SadThreshold;
    if (rounding.EffectiveAssessmentId != assessment.Id || rounding.MaterialityCalculationId != calculation.Id ||
        rounding.PolicyVersion != MaterialityCalculator.PolicyVersion ||
        rounding.ComputedPlanningMateriality != figures.PlanningMateriality || rounding.ComputedTolerableError != figures.TolerableError ||
        rounding.ComputedSadThreshold != figures.SadThreshold)
      return false;
    var bounded = MaterialityPracticalRounding.Evaluate(figures, rounding.AdjustedPlanningMateriality, rounding.AdjustedTolerableError, rounding.AdjustedSadThreshold);
    return bounded.Error is null &&
      assessment.OverallMateriality == rounding.AdjustedPlanningMateriality && assessment.PerformanceMateriality == rounding.AdjustedTolerableError &&
      assessment.ClearlyTrivialThreshold == rounding.AdjustedSadThreshold;
  }

  /// <summary>True only for a separate Partner with a Partner grant valid for this exact scope at approval time.</summary>
  public static async Task<bool> HasIndependentPartnerApprovalAsync(
    IAuditSphereDbContext db, MaterialityAssessment assessment, CancellationToken ct = default)
  {
    var approval = await db.MaterialityApprovals.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == assessment.FirmId && x.ClientId == assessment.ClientId && x.EngagementId == assessment.EngagementId &&
      x.MaterialityAssessmentId == assessment.Id, ct);
    if (approval is null || approval.ApprovedByUserId == assessment.ActorId) return false;
    return await db.RoleGrants.AsNoTracking().AnyAsync(x => x.FirmId == assessment.FirmId &&
      x.UserId == approval.ApprovedByUserId && x.Role == "Partner" && x.GrantedAt <= approval.ApprovedAt &&
      (x.RevokedAt == null || x.RevokedAt >= approval.ApprovedAt) && (x.ExpiresAt == null || x.ExpiresAt > approval.ApprovedAt) &&
      ((x.ClientId == null && x.EngagementId == null) ||
       (x.ClientId == assessment.ClientId && x.EngagementId == null) ||
       (x.ClientId == assessment.ClientId && x.EngagementId == assessment.EngagementId)), ct);
  }

  public static async Task<bool> IsPartnerApprovedCurrentAsync(
    IAuditSphereDbContext db, MaterialityAssessment assessment, CancellationToken ct = default)
  {
    var resolved = await ResolveAsync(db, assessment, ct);
    return resolved.Calculation is not null && resolved.IsCurrentHead &&
      MatchesCurrentPolicy(assessment, resolved.Calculation, resolved.Rounding) &&
      await IsCurrentAsync(db, resolved.Calculation, ct) && await HasIndependentPartnerApprovalAsync(db, assessment, ct);
  }

  /// <summary>
  /// STE 3.2 manager-controlled practical rounding of the current draft materiality. Writes a new effective draft assessment
  /// with the rounded thresholds and an append-only decision that keeps the original computed figures. The original
  /// calculation and assessment are never changed, and the approval gate then binds Partner approval to the rounded values.
  /// </summary>
  public static async Task<CommandResult<PracticalRoundingView>> ApplyPracticalRoundingAsync(
    IAuditSphereDbContext db, ActorContext actor, PracticalRoundingRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Rationale))
      return CommandResult<PracticalRoundingView>.Fail(ErrorCodes.AuditPlanning.Invalid, "A rationale for the practical rounding is required.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var existing = await db.MaterialityAssessments.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.AssessmentId && x.FirmId == actor.FirmId, ct);
    if (existing is null) return CommandResult<PracticalRoundingView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var scope = await AuditPlanningService.LockedEngagementAsync(db, actor, existing.EngagementId, ct, existing.ClientId);
    if (scope.Denied is not null) return CommandResult<PracticalRoundingView>.Fail(scope.Denied, scope.Message);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, existing.ClientId, existing.EngagementId, RoundingRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<PracticalRoundingView>.Fail(auth.ErrorCode!, auth.Message!);

    var resolved = await ResolveAsync(db, existing, ct);
    if (resolved.Calculation is null)
      return CommandResult<PracticalRoundingView>.Fail(ErrorCodes.GateBlocked,
        "Practical rounding needs a materiality calculated from the current approved mapping and sealed trial balance.");
    if (!resolved.IsCurrentHead)
      return CommandResult<PracticalRoundingView>.Fail(ErrorCodes.GenerationStale,
        "This materiality was superseded by a later calculation or rounding. Apply rounding to the current assessment.");
    var calculation = resolved.Calculation;
    if (existing.Status != MaterialityStatuses.Draft || await db.MaterialityApprovals.AnyAsync(x =>
      x.FirmId == existing.FirmId && x.MaterialityAssessmentId == existing.Id, ct))
      return CommandResult<PracticalRoundingView>.Fail(ErrorCodes.ProtectedState,
        "Practical rounding can only be applied to a draft materiality that has not been approved.");
    if (!await IsCurrentAsync(db, calculation, ct))
      return CommandResult<PracticalRoundingView>.Fail(ErrorCodes.GenerationStale,
        "The mapping or trial balance this materiality was calculated from has been replaced; recalculate it.");
    if (!MatchesCurrentPolicy(existing, calculation, resolved.Rounding))
      return CommandResult<PracticalRoundingView>.Fail(ErrorCodes.GateBlocked,
        "The saved materiality inputs or thresholds do not reconcile to the current policy. Recalculate before rounding.");

    var computed = new MaterialityFigures(calculation.BenchmarkAmount, calculation.SourceLineCount,
      calculation.PlanningMateriality, calculation.TolerableError, calculation.SadThreshold);
    var (adjustment, error) = MaterialityPracticalRounding.Evaluate(computed, request.PlanningMateriality, request.TolerableError, request.SadThreshold);
    if (adjustment is null) return CommandResult<PracticalRoundingView>.Fail(ErrorCodes.AuditPlanning.Invalid, error!);

    var now = DateTimeOffset.UtcNow;
    var effective = new MaterialityAssessment
    {
      Id = Guid.CreateVersion7(), FirmId = existing.FirmId, ClientId = existing.ClientId, EngagementId = existing.EngagementId,
      ActorId = actor.UserId, BenchmarkSource = existing.BenchmarkSource, BenchmarkVersion = existing.BenchmarkVersion,
      Rationale = existing.Rationale, BenchmarkAmount = existing.BenchmarkAmount, RateApplied = existing.RateApplied,
      OverallMateriality = adjustment.PlanningMateriality, PerformanceMateriality = adjustment.TolerableError,
      ClearlyTrivialThreshold = adjustment.SadThreshold, QualitativeConsiderations = existing.QualitativeConsiderations,
      Status = MaterialityStatuses.Draft, CreatedAt = now
    };
    var decision = new MaterialityRoundingDecision
    {
      Id = Guid.CreateVersion7(), FirmId = existing.FirmId, ClientId = existing.ClientId, EngagementId = existing.EngagementId,
      MaterialityCalculationId = calculation.Id, SourceAssessmentId = calculation.MaterialityAssessmentId, EffectiveAssessmentId = effective.Id,
      ComputedPlanningMateriality = calculation.PlanningMateriality, ComputedTolerableError = calculation.TolerableError,
      ComputedSadThreshold = calculation.SadThreshold,
      AdjustedPlanningMateriality = adjustment.PlanningMateriality, AdjustedTolerableError = adjustment.TolerableError,
      AdjustedSadThreshold = adjustment.SadThreshold,
      PlanningDeltaPercent = adjustment.PlanningDeltaPercent, TolerableDeltaPercent = adjustment.TolerableDeltaPercent,
      SadDeltaPercent = adjustment.SadDeltaPercent,
      Rationale = request.Rationale.Trim(), PolicyVersion = MaterialityCalculator.PolicyVersion,
      DecidedByUserId = actor.UserId, DecidedAt = now
    };
    db.MaterialityAssessments.Add(effective);
    db.MaterialityRoundingDecisions.Add(decision);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<PracticalRoundingView>.Ok(new(effective.Id, decision, effective, MaterialityCalculationStates.Draft));
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
