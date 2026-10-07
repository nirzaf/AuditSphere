using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record AssessRiskBandRequest(Guid RiskId, int LikelihoodScore, int MagnitudeScore, bool FraudRisk, string Rationale);

public sealed record RiskRoutingRow(
  Guid RiskId, string Area, string Assertion, string SignificanceDecision, Guid? AssessmentId, Guid? AssessedByUserId, string? Band, int? Likelihood,
  int? Magnitude, bool FraudRisk, string Route, bool PartnerReviewRequired, bool PartnerCleared, string? PartnerName,
  string? OwnerName, string? OwnerLevel);

/// <summary>
/// Green/amber/red routing for identified risks. The band is computed from recorded inputs (and the database rejects
/// a band that does not follow from them); the significance input comes from the risk's own recorded decision. Red
/// requires an Engagement Partner review of that exact assessment, and a risk owner must meet the band's minimum
/// staffing level. None of this is a professional conclusion; it routes work to the people who make one.
/// </summary>
public static class RiskBandService
{
  private static readonly string[] AssignRoles = ["Partner", "Manager", "Administrator"];

  public static async Task<CommandResult<Guid>> AssessAsync(IAuditSphereDbContext db, ActorContext actor, AssessRiskBandRequest request, CancellationToken ct = default)
  {
    if (request.LikelihoodScore is < 1 or > 3 || request.MagnitudeScore is < 1 or > 3 || string.IsNullOrWhiteSpace(request.Rationale))
      return CommandResult<Guid>.Fail(ErrorCodes.AuditPlanning.Invalid, "Likelihood and magnitude (1–3) and a rationale are required.");
    var risk = await db.AuditRisks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.RiskId && x.FirmId == actor.FirmId, ct);
    if (risk is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var scope = await AuditPlanningService.LockedEngagementAsync(db, actor, risk.EngagementId, ct, risk.ClientId);
    if (scope.Denied is not null) return CommandResult<Guid>.Fail(scope.Denied, scope.Message);
    var significant = risk.SignificanceDecision == SignificanceDecisions.Significant;
    var assessment = new RiskBandAssessment
    {
      Id = Guid.CreateVersion7(), FirmId = risk.FirmId, ClientId = risk.ClientId, EngagementId = risk.EngagementId, RiskId = risk.Id,
      LikelihoodScore = request.LikelihoodScore, MagnitudeScore = request.MagnitudeScore, Significant = significant, FraudRisk = request.FraudRisk,
      Band = RiskBandRules.Band(request.LikelihoodScore, request.MagnitudeScore, significant, request.FraudRisk),
      RuleVersion = RiskBandRules.RuleVersion, Rationale = request.Rationale.Trim(), AssessedByUserId = actor.UserId, AssessedAt = DateTimeOffset.UtcNow
    };
    db.RiskBandAssessments.Add(assessment);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(assessment.Id);
  }

  public static async Task<CommandResult<Guid>> PartnerClearAsync(IAuditSphereDbContext db, ActorContext actor, Guid riskId, string note, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(note)) return CommandResult<Guid>.Fail(ErrorCodes.AuditPlanning.Invalid, "A Partner review note is required.");
    var current = await CurrentAssessmentAsync(db, actor.FirmId, riskId, ct);
    if (current is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Assess the risk band first.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, current.ClientId, current.EngagementId, ["Partner"], InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (current.Band != RiskBands.Red) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Only a red risk requires Partner clearance.");
    if (current.AssessedByUserId == actor.UserId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The Partner who assessed the band cannot also clear it.");
    var existing = await db.RiskPartnerClearances.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.RiskBandAssessmentId == current.Id, ct);
    if (existing is not null) return CommandResult<Guid>.Ok(existing.Id);
    var clearance = new RiskPartnerClearance
    {
      Id = Guid.CreateVersion7(), FirmId = current.FirmId, ClientId = current.ClientId, EngagementId = current.EngagementId, RiskId = riskId,
      RiskBandAssessmentId = current.Id, PartnerUserId = actor.UserId, Note = note.Trim(), ClearedAt = DateTimeOffset.UtcNow
    };
    db.RiskPartnerClearances.Add(clearance);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(clearance.Id);
  }

  public static async Task<CommandResult<Guid>> AssignOwnerAsync(IAuditSphereDbContext db, ActorContext actor, Guid riskId, Guid ownerUserId, CancellationToken ct = default)
  {
    var current = await CurrentAssessmentAsync(db, actor.FirmId, riskId, ct);
    if (current is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Assess the risk band first.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, current.ClientId, current.EngagementId, AssignRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var staffing = await db.EngagementStaffAssignments.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.EngagementId == current.EngagementId && x.UserId == ownerUserId && x.RevokedAt == null, ct);
    if (staffing is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The owner must be staffed on this engagement.");
    var minimum = RiskBandRules.MinimumOwnerRank(current.Band);
    if (StaffingLevels.Rank(staffing.StaffingLevel) < minimum)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        $"A {current.Band} risk needs an owner at {StaffingLevels.Label(StaffingLevels.All.First(x => StaffingLevels.Rank(x) == minimum))} level or above.");
    var assignment = new RiskOwnerAssignment
    {
      Id = Guid.CreateVersion7(), FirmId = current.FirmId, ClientId = current.ClientId, EngagementId = current.EngagementId, RiskId = riskId,
      RiskBandAssessmentId = current.Id, OwnerUserId = ownerUserId, OwnerStaffingLevel = staffing.StaffingLevel,
      AssignedByUserId = actor.UserId, AssignedAt = DateTimeOffset.UtcNow
    };
    db.RiskOwnerAssignments.Add(assignment);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(assignment.Id);
  }

  public static async Task<CommandResult<IReadOnlyList<RiskRoutingRow>>> GetRoutingAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<IReadOnlyList<RiskRoutingRow>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId,
      engagementId, AuditPlanningService.PlanningRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<IReadOnlyList<RiskRoutingRow>>.Fail(auth.ErrorCode!, auth.Message!);
    return await GetRoutingForAuthorizedScopeAsync(db, actor.FirmId, engagementId, ct);
  }

  internal static async Task<CommandResult<IReadOnlyList<RiskRoutingRow>>> GetRoutingForAuthorizedScopeAsync(
    IAuditSphereDbContext db, Guid firmId, Guid engagementId, CancellationToken ct)
  {
    var risks = await db.AuditRisks.AsNoTracking().Where(x => x.FirmId == firmId && x.EngagementId == engagementId).OrderBy(x => x.CreatedAt).ToListAsync(ct);
    var assessments = await db.RiskBandAssessments.AsNoTracking().Where(x => x.FirmId == firmId && x.EngagementId == engagementId).ToListAsync(ct);
    var clearances = await db.RiskPartnerClearances.AsNoTracking().Where(x => x.FirmId == firmId && x.EngagementId == engagementId).ToListAsync(ct);
    var owners = await db.RiskOwnerAssignments.AsNoTracking().Where(x => x.FirmId == firmId && x.EngagementId == engagementId).ToListAsync(ct);
    var userIds = clearances.Select(x => x.PartnerUserId).Concat(owners.Select(x => x.OwnerUserId)).Distinct().ToArray();
    var names = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
    var rows = risks.Select(risk =>
    {
      var current = assessments.Where(x => x.RiskId == risk.Id).OrderByDescending(x => x.AssessedAt).ThenByDescending(x => x.Id).FirstOrDefault();
      var clearance = current is null ? null : clearances.SingleOrDefault(x => x.RiskBandAssessmentId == current.Id);
      var owner = current is null ? null : owners.Where(x => x.RiskBandAssessmentId == current.Id).OrderByDescending(x => x.AssignedAt).FirstOrDefault();
      return new RiskRoutingRow(risk.Id, risk.AccountArea, risk.Assertion, risk.SignificanceDecision, current?.Id, current?.AssessedByUserId, current?.Band,
        current?.LikelihoodScore, current?.MagnitudeScore, current?.FraudRisk ?? false,
        current is null ? "Band not assessed." : RiskBandRules.Route(current.Band), current?.Band == RiskBands.Red, clearance is not null,
        clearance is null ? null : names.GetValueOrDefault(clearance.PartnerUserId), owner is null ? null : names.GetValueOrDefault(owner.OwnerUserId),
        owner is null ? null : StaffingLevels.Label(owner.OwnerStaffingLevel));
    }).ToList();
    return CommandResult<IReadOnlyList<RiskRoutingRow>>.Ok(rows);
  }

  /// <summary>Completion blockers: every red current band needs its Partner clearance and a qualifying owner.</summary>
  internal static async Task<IReadOnlyList<string>> BlockersAsync(IAuditSphereDbContext db, Guid firmId, Guid engagementId, CancellationToken ct)
  {
    var assessments = await db.RiskBandAssessments.AsNoTracking().Where(x => x.FirmId == firmId && x.EngagementId == engagementId).ToListAsync(ct);
    var current = assessments.GroupBy(x => x.RiskId).Select(g => g.OrderByDescending(x => x.AssessedAt).ThenByDescending(x => x.Id).First()).ToList();
    var cleared = await db.RiskPartnerClearances.AsNoTracking().Where(x => x.FirmId == firmId && x.EngagementId == engagementId)
      .Select(x => x.RiskBandAssessmentId).ToListAsync(ct);
    var owned = await db.RiskOwnerAssignments.AsNoTracking().Where(x => x.FirmId == firmId && x.EngagementId == engagementId)
      .Select(x => x.RiskBandAssessmentId).ToListAsync(ct);
    return current.Where(x => x.Band == RiskBands.Red && !cleared.Contains(x.Id)).Select(x => $"risk:{x.RiskId}:red-partner-review")
      .Concat(current.Where(x => x.Band != RiskBands.Green && !owned.Contains(x.Id)).Select(x => $"risk:{x.RiskId}:{x.Band.ToLowerInvariant()}-owner"))
      .ToList();
  }

  private static async Task<RiskBandAssessment?> CurrentAssessmentAsync(IAuditSphereDbContext db, Guid firmId, Guid riskId, CancellationToken ct) =>
    await db.RiskBandAssessments.AsNoTracking().Where(x => x.FirmId == firmId && x.RiskId == riskId)
      .OrderByDescending(x => x.AssessedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
}
