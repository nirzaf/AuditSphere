using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record PlanMateriality(Guid Id, Guid PreparedByUserId, string BenchmarkSource, string BenchmarkVersion, string Rationale, decimal BenchmarkAmount,
  decimal RateApplied, decimal OverallMateriality, decimal PerformanceMateriality, decimal ClearlyTrivialThreshold, string? QualitativeConsiderations,
  string Status, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt);
public sealed record PlanRisk(Guid Id, string AccountArea, string Description, string Assertion, string Severity, string Status);
public sealed record PlanPopulation(Guid Id, string Purpose, string Assertion, int RowCount, decimal MonetaryControlTotal, string Currency, string Status);
public sealed record PlanFinding(Guid Id, string FindingType, string Status, decimal? MonetaryAmount);
public sealed record PlanWorkpaper(Guid Id, string Index, string Title, string Status, long Revision);
public sealed record PlanCalculation(Guid AssessmentId, string State, string Route, string BenchmarkKind, string? DestinationCode, decimal BenchmarkAmount, string Currency,
  int SourceLineCount, long MappingVersionNumber, decimal RatePercent, decimal PerformancePercent, decimal TrivialPercent, decimal PlanningMateriality,
  decimal TolerableError, decimal SadThreshold, string PolicyVersion, PlanRounding? Rounding = null);
/// <summary>The manager's practical rounding of the computed thresholds (STE 3.2), with server-computed deltas.</summary>
public sealed record PlanRounding(DateTimeOffset DecidedAt, Guid DecidedByUserId, string Rationale, decimal AdjustedPlanningMateriality,
  decimal AdjustedTolerableError, decimal AdjustedSadThreshold, decimal PlanningDeltaPercent, decimal TolerableDeltaPercent, decimal SadDeltaPercent);
public sealed record PlanPolicyRange(string Kind, decimal MinRatePercent, decimal MaxRatePercent);
public sealed record PlanFsliRiskRow(
  string DestinationCode, string StatementSection, string? AuditArea, decimal Balance, decimal AbsoluteBalance,
  string Currency, decimal TolerableError, decimal PlanningMateriality, string Band, bool CriticalEstimate,
  bool HighInherentRisk, bool SignificantRisk, bool FraudRisk, string PerformerRole, string ReviewerRole, string Explanation);
public sealed record AuditPlanWorkspace(Guid EngagementId, bool ProfessionalWorkBlocked, PlanMateriality? Materiality, bool CanApproveMateriality,
  IReadOnlyList<PlanRisk> Risks, IReadOnlyList<PlanPopulation> Populations, IReadOnlyList<PlanFinding> Findings, IReadOnlyList<PlanWorkpaper> Workpapers,
  MaterialitySourceView? MaterialitySource, string? MaterialitySourceMessage, PlanCalculation? LatestCalculation, IReadOnlyList<PlanPolicyRange> RateRanges,
  decimal PerformanceMin, decimal PerformanceMax, decimal TrivialMin, decimal TrivialMax, string RiskRuleVersion, IReadOnlyList<RiskRoutingRow> Routing,
  IReadOnlyList<StaffAssignmentRow> Team, bool CanAssignOwners, bool IsPartner, IReadOnlyList<PlanFsliRiskRow> FsliStratification,
  MilestonePlanView? MilestonePlan = null, bool CanApplyPracticalRounding = false);

/// <summary>Engagement-scoped audit plan projection: planning records, the materiality engine state and risk routing.</summary>
public static class AuditPlanWorkspaceQuery
{
  private static readonly string[] PlanningRoles = ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];

  public static async Task<CommandResult<AuditPlanWorkspace>> GetAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, EngagementId: engagementId, RequiredRoles: PlanningRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<AuditPlanWorkspace>.Fail(auth.ErrorCode!, auth.Message!);
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(e => e.Id == engagementId && e.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<AuditPlanWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var materiality = await db.MaterialityAssessments.AsNoTracking().Where(m => m.FirmId == actor.FirmId && m.EngagementId == engagementId)
      .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).FirstOrDefaultAsync(ct);
    var approval = materiality is null ? null : await db.MaterialityApprovals.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.MaterialityAssessmentId == materiality.Id, ct);
    var partnerApproval = materiality is not null && approval is not null &&
      await MaterialityEngineService.HasIndependentPartnerApprovalAsync(db, materiality, ct);
    var hasSourceBoundCalculation = materiality is not null &&
      (await MaterialityEngineService.ResolveAsync(db, materiality, ct)).Calculation is not null;
    var materialitySourceCurrent = hasSourceBoundCalculation && await MaterialityEngineService.IsAssessmentCurrentAsync(
      db, actor.FirmId, materiality!.Id, ct);
    var risks = await db.AuditRisks.AsNoTracking().Where(r => r.FirmId == actor.FirmId && r.EngagementId == engagementId).OrderBy(r => r.CreatedAt)
      .Select(r => new PlanRisk(r.Id, r.AccountArea, r.Description, r.Assertion, r.Severity, r.Status)).ToListAsync(ct);
    var populations = await db.PopulationVersions.AsNoTracking().Where(p => p.FirmId == actor.FirmId && p.EngagementId == engagementId).OrderByDescending(p => p.CreatedAt)
      .Select(p => new PlanPopulation(p.Id, p.Purpose, p.Assertion, p.RowCount, p.MonetaryControlTotal, p.Currency, p.Status)).ToListAsync(ct);
    var findings = await db.Findings.AsNoTracking().Where(f => f.FirmId == actor.FirmId && f.EngagementId == engagementId).OrderByDescending(f => f.CreatedAt)
      .Select(f => new PlanFinding(f.Id, f.FindingType, f.Status, f.MonetaryAmount)).ToListAsync(ct);
    var workpapers = await db.Workpapers.AsNoTracking().Where(w => w.FirmId == actor.FirmId && w.EngagementId == engagementId).OrderBy(w => w.Index)
      .Select(w => new PlanWorkpaper(w.Id, w.Index, w.Title, w.Status, w.Revision)).ToListAsync(ct);
    var source = await MaterialityEngineService.GetSourceAsync(db, actor, engagementId, ct);
    var latest = await MaterialityEngineService.GetLatestAsync(db, actor.FirmId, engagementId, ct);
    if (latest?.AssessmentId != materiality?.Id) latest = null;
    var routing = await RiskBandService.GetRoutingAsync(db, actor, engagementId, ct);
    var team = await StaffingService.ListAsync(db, actor, engagementId, ct);
    var milestonePlan = await StatutoryMilestoneService.GetMilestonesAsync(db, actor, engagementId, ct);
    var c = latest?.Calculation;

    var fsliRows = new List<PlanFsliRiskRow>();
    if (latest is { State: MaterialityCalculationStates.Draft or MaterialityCalculationStates.Approved, Calculation: { } calc } &&
        calc.PlanningMateriality > 0 && calc.TolerableError > 0)
    {
      var rawSource = await MappedTrialBalanceSource.LoadAsync(db, actor.FirmId, engagementId, ct);
      if (rawSource is not null)
      {
        var risksForEngagement = await db.AuditRisks.AsNoTracking().Where(r => r.FirmId == actor.FirmId && r.EngagementId == engagementId).ToListAsync(ct);
        var assessmentsForEngagement = await db.RiskBandAssessments.AsNoTracking().Where(a => a.FirmId == actor.FirmId && a.EngagementId == engagementId).ToListAsync(ct);

        var grouped = rawSource.Lines.GroupBy(l => (l.DestinationCode, l.StatementSection, l.AuditArea));
        foreach (var g in grouped)
        {
          var balance = g.Sum(x => x.Amount);
          var destCode = g.Key.DestinationCode;
          var section = g.Key.StatementSection;
          var area = g.Key.AuditArea;

          var matchingRisks = risksForEngagement.Where(r =>
            (!string.IsNullOrWhiteSpace(area) && string.Equals(r.AccountArea, area, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(r.AccountArea, destCode, StringComparison.OrdinalIgnoreCase)).ToList();

          var matchingRiskIds = matchingRisks.Select(r => r.Id).ToHashSet();
          var matchingAssessments = assessmentsForEngagement.Where(a => matchingRiskIds.Contains(a.RiskId)).ToList();

          var significant = matchingRisks.Any(r => r.SignificanceDecision == SignificanceDecisions.Significant) ||
                            matchingAssessments.Any(a => a.Significant);
          var fraud = matchingAssessments.Any(a => a.FraudRisk);
          var critical = matchingRisks.Any(r => ProcedureRiskBandEvaluator.HasEstimateIndicator(r.Description) ||
                                                ProcedureRiskBandEvaluator.HasEstimateIndicator(r.Drivers));
          var highInherent = matchingAssessments.Any(a => a.LikelihoodScore == 3 && a.MagnitudeScore >= 2);

          var band = FsliRiskBandRules.Band(balance, calc.TolerableError, calc.PlanningMateriality,
            critical, highInherent, significant, fraud);
          var performer = FsliRiskBandRules.Route(band);
          var reviewer = FsliRiskBandRules.ReviewerDescription(band);
          var explanation = FsliRiskBandRules.Explain(balance, calc.TolerableError, calc.PlanningMateriality,
            band, critical, highInherent, significant, fraud);

          fsliRows.Add(new PlanFsliRiskRow(destCode, section, area, balance, Math.Abs(balance), rawSource.Dataset.Currency,
            calc.TolerableError, calc.PlanningMateriality, band, critical, highInherent, significant, fraud,
            performer, reviewer, explanation));
        }
      }
    }

    // STE 3.2 practical rounding: the same engagement-scoped authorization as ApplyPracticalRoundingAsync, and only for the
    // current, source-bound, unapproved draft. Angular renders this flag; the command re-checks every gate.
    var roundingAuthorized = materiality is not null && (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId, MaterialityEngineService.RoundingRoles,
        InternalOnly: true), ct)).Succeeded;
    var canRound = roundingAuthorized && latest is not null && latest.AssessmentId == materiality!.Id &&
      latest.State == MaterialityCalculationStates.Draft && approval is null && materialitySourceCurrent;

    return CommandResult<AuditPlanWorkspace>.Ok(new(engagementId, engagement.ProfessionalWorkBlocked,
      materiality is null ? null : new PlanMateriality(materiality.Id, materiality.ActorId, materiality.BenchmarkSource, materiality.BenchmarkVersion, materiality.Rationale,
        materiality.BenchmarkAmount, materiality.RateApplied, materiality.OverallMateriality, materiality.PerformanceMateriality, materiality.ClearlyTrivialThreshold,
        materiality.QualitativeConsiderations, approval is null ? materiality.Status : partnerApproval ? MaterialityStatuses.Approved : "RECALCULATION_REQUIRED",
        partnerApproval ? approval!.ApprovedByUserId : null, partnerApproval ? approval!.ApprovedAt : null),
      materiality is not null && hasSourceBoundCalculation && materialitySourceCurrent && latest?.State == MaterialityCalculationStates.Draft && approval is null &&
        materiality.ActorId != actor.UserId && actor.Roles.Contains("Partner"),
      risks, populations, findings, workpapers, source.Succeeded ? source.Value : null, source.Succeeded ? null : source.Message,
      latest is null ? null : new PlanCalculation(latest.AssessmentId, latest.State, latest.Route, c!.BenchmarkKind, c.DestinationCode, c.BenchmarkAmount, c.Currency,
        c.SourceLineCount, c.MappingVersionNumber, c.RatePercent, c.PerformancePercent, c.TrivialPercent, c.PlanningMateriality, c.TolerableError, c.SadThreshold, c.PolicyVersion,
        latest.Rounding is { } rounding ? new PlanRounding(rounding.DecidedAt, rounding.DecidedByUserId, rounding.Rationale,
          rounding.AdjustedPlanningMateriality, rounding.AdjustedTolerableError, rounding.AdjustedSadThreshold,
          rounding.PlanningDeltaPercent, rounding.TolerableDeltaPercent, rounding.SadDeltaPercent) : null),
      MaterialityCalculator.RateRanges.Select(x => new PlanPolicyRange(x.Key, x.Value.MinRatePercent, x.Value.MaxRatePercent)).ToList(),
      MaterialityCalculator.PerformanceRange.Min, MaterialityCalculator.PerformanceRange.Max, MaterialityCalculator.TrivialRange.Min, MaterialityCalculator.TrivialRange.Max,
      RiskBandRules.RuleVersion, routing.Succeeded ? routing.Value! : [], team.Succeeded ? team.Value! : [],
      actor.Roles.Any(x => x is "Partner" or "Manager" or "Administrator"), actor.Roles.Contains("Partner"), fsliRows,
      milestonePlan.Succeeded ? milestonePlan.Value : null, canRound));
  }
}
