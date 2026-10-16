using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

/// <summary>Reviewed planning publication and actor-owned exact-intent recovery. Legacy commands join this transaction.</summary>
public static class AuditPlanningCommandWorkspace
{
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static string Exact(decimal x) => x.ToString("0.000000", CultureInfo.InvariantCulture);
  private static string Long(long x) => x.ToString(CultureInfo.InvariantCulture);
  private static bool Hash(string? x) => x is { Length: 64 } && x.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static bool Text(string? x, int max) => !string.IsNullOrWhiteSpace(x) && x.Length <= max && !x.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'));
  private static bool Optional(string? x, int max) => x is null || x.Length <= max && !x.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'));
  private static bool Number(string? x, out decimal value) => decimal.TryParse(x, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
    CultureInfo.InvariantCulture, out value) && value is >= -9_999_999_999_999.999999m and <= 9_999_999_999_999.999999m && decimal.Round(value, 6) == value;
  private static decimal Decimal(string x) => decimal.Parse(x, CultureInfo.InvariantCulture);
  private static string[] Roles(string action) => action switch {
    AuditPlanningActions.RoundMateriality => MaterialityEngineService.RoundingRoles,
    AuditPlanningActions.ApproveMateriality or AuditPlanningActions.ClearRisk => ["Partner"],
    AuditPlanningActions.AssignRiskOwner => ["Partner", "Manager", "Administrator"],
    _ => AuditPlanningService.PlanningRoles
  };
  private static bool Action(string action) => action is AuditPlanningActions.CalculateMateriality or AuditPlanningActions.RoundMateriality or
    AuditPlanningActions.ApproveMateriality or AuditPlanningActions.CreateRisk or AuditPlanningActions.AssessRisk or
    AuditPlanningActions.AssignRiskOwner or AuditPlanningActions.ClearRisk or AuditPlanningActions.LinkProcedureRisk;
  internal static async Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext a, Guid engagementId,
    string action, bool publishing, CancellationToken ct)
  {
    var e = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == engagementId, ct);
    return e is null ? CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.") : await AuthorizationDecision.AuthorizeAsync(db, a,
      new(a.FirmId, e.PracticeClientId, engagementId, publishing ? Roles(action) : AuditPlanningService.PlanningRoles,
        InternalOnly: true, RequireProfessionalWork: publishing), ct);
  }
  // This lock order matches source publication and role administration. Lookup takes shared actor/client locks.
  internal static async Task<CommandResult> LockAsync(IAuditSphereDbContext db, ActorContext a, Guid engagementId,
    bool publishing, CancellationToken ct)
  {
    var e = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == engagementId, ct);
    if (e is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var firm = await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={a.FirmId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    var client = publishing
      ? await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={a.FirmId} AND id={e.PracticeClientId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct)
      : await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={a.FirmId} AND id={e.PracticeClientId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={a.FirmId} AND id={a.UserId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    if (firm is null || client is null || user is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult.Ok();
  }
  private static async Task LockRequestAsync(IAuditSphereDbContext db, ActorContext a, Guid requestId, bool publish, CancellationToken ct)
  {
    // Cross-client reuse still shares one actor/request barrier; no dependency on user-row mutation or a result match.
    var digest = Hashing.Sha256Hex(JsonSerializer.Serialize(new { Namespace = "audit-planning-request-v1", a.FirmId, a.UserId, requestId }));
    var key = unchecked((long)Convert.ToUInt64(digest[..16], 16));
    if (publish) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    else await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({key})", ct);
  }
  private static AuditPlanningCommandFields? Canonical(AuditPlanningCommandFields? f)
  {
    if (f is null || !Action(f.Action) || new object?[] { f.Calculate, f.Round, f.Approve, f.CreateRisk, f.AssessRisk, f.AssignRiskOwner, f.ClearRisk, f.LinkProcedureRisk }.Count(x => x is not null) != 1) return null;
    switch (f.Action)
    {
      case AuditPlanningActions.CalculateMateriality when f.Calculate is { } c:
        if (!Text(c.BenchmarkKind, 50) || !Optional(c.DestinationCode, 100) || !Number(c.RatePercent, out var rate) ||
          !Number(c.PerformancePercent, out var performance) || !Number(c.TrivialPercent, out var trivial) || !Text(c.Rationale, 4000) || !Optional(c.QualitativeConsiderations, 4000)) return null;
        var kind = c.BenchmarkKind.Trim().ToUpperInvariant();
        if (decimal.Round(rate, 4) != rate || decimal.Round(performance, 4) != performance || decimal.Round(trivial, 4) != trivial ||
          (kind != MaterialityBenchmarks.MappedLine && !string.IsNullOrWhiteSpace(c.DestinationCode))) return null;
        return f with { Calculate = c with { BenchmarkKind = kind, DestinationCode = string.IsNullOrWhiteSpace(c.DestinationCode) ? null : c.DestinationCode.Trim().ToUpperInvariant(),
          RatePercent = Exact(rate), PerformancePercent = Exact(performance), TrivialPercent = Exact(trivial), Rationale = c.Rationale.Trim(), QualitativeConsiderations = string.IsNullOrWhiteSpace(c.QualitativeConsiderations) ? null : c.QualitativeConsiderations.Trim() } };
      case AuditPlanningActions.RoundMateriality when f.Round is { } r:
        if (r.AssessmentId == Guid.Empty || !Number(r.PlanningMateriality, out var pm) || !Number(r.TolerableError, out var te) || !Number(r.SadThreshold, out var sad) || !Text(r.Rationale, 4000)) return null;
        return f with { Round = r with { PlanningMateriality = Exact(pm), TolerableError = Exact(te), SadThreshold = Exact(sad), Rationale = r.Rationale.Trim() } };
      case AuditPlanningActions.ApproveMateriality when f.Approve is { AssessmentId: var id } && id != Guid.Empty: return f;
      case AuditPlanningActions.CreateRisk when f.CreateRisk is { } r:
        if (!Text(r.Area, 200) || !Text(r.Assertion, 100) || !Text(r.Description, 4000) || !Text(r.Drivers, 4000) || !Text(r.Response, 4000) || !Optional(r.ControlsConsidered, 4000) ||
          r.SignificanceDecision is not ("NORMAL" or "SIGNIFICANT")) return null;
        return f with { CreateRisk = r with { Area = r.Area.Trim(), Assertion = r.Assertion.Trim(), Description = r.Description.Trim(), Drivers = r.Drivers.Trim(), Response = r.Response.Trim(), ControlsConsidered = string.IsNullOrWhiteSpace(r.ControlsConsidered) ? null : r.ControlsConsidered.Trim() } };
      case AuditPlanningActions.AssessRisk when f.AssessRisk is { } r:
        return r.RiskId != Guid.Empty && r.Likelihood is >= 1 and <= 3 && r.Magnitude is >= 1 and <= 3 && Text(r.Rationale, 4000) ? f with { AssessRisk = r with { Rationale = r.Rationale.Trim() } } : null;
      case AuditPlanningActions.AssignRiskOwner when f.AssignRiskOwner is { } r:
        return r.RiskId != Guid.Empty && r.ExpectedAssessmentId != Guid.Empty && r.OwnerUserId != Guid.Empty ? f : null;
      case AuditPlanningActions.ClearRisk when f.ClearRisk is { } r:
        return r.RiskId != Guid.Empty && r.ExpectedAssessmentId != Guid.Empty && Text(r.Note, 4000) ? f with { ClearRisk = r with { Note = r.Note.Trim() } } : null;
      case AuditPlanningActions.LinkProcedureRisk when f.LinkProcedureRisk is { } r:
        return r.ProcedureId != Guid.Empty && r.RiskId != Guid.Empty && r.ExpectedRiskId != Guid.Empty ? f : null;
      default: return null;
    }
  }
  private static string RequestHash(ActorContext a, Guid engagementId, Guid requestId, string basis, AuditPlanningCommandFields fields) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { Version = 1, a.FirmId, a.UserId, a.SessionEpoch, engagementId, requestId, basis, fields }));

  private static async Task<PlanningMaterialityDetail?> MaterialityAsync(IAuditSphereDbContext db, Guid firmId, Guid engagementId, Guid? assessmentId, CancellationToken ct)
  {
    var m = assessmentId is { } id
      ? await db.MaterialityAssessments.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.EngagementId == engagementId && x.Id == id, ct)
      : await db.MaterialityAssessments.AsNoTracking().Where(x => x.FirmId == firmId && x.EngagementId == engagementId).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    if (m is null) return null;
    var resolved = await MaterialityEngineService.ResolveAsync(db, m, ct); var c = resolved.Calculation; var r = resolved.Rounding;
    var approval = await db.MaterialityApprovals.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.MaterialityAssessmentId == m.Id, ct);
    var current = c is not null && resolved.IsCurrentHead && await MaterialityEngineService.IsAssessmentCurrentAsync(db, firmId, m.Id, ct) && MaterialityEngineService.MatchesCurrentPolicy(m, c, r);
    return new(m.Id, c?.Id, r?.Id, m.ActorId, !current ? "STALE" : approval is null ? "DRAFT" : "APPROVED", c?.BenchmarkKind ?? m.BenchmarkSource,
      c?.DestinationCode, c?.Currency ?? "", Exact(m.BenchmarkAmount), Exact((c?.RatePercent) ?? m.RateApplied * 100m),
      Exact(c?.PerformancePercent ?? 0m), Exact(c?.TrivialPercent ?? 0m), Exact(c?.PlanningMateriality ?? m.OverallMateriality),
      Exact(c?.TolerableError ?? m.PerformanceMateriality), Exact(c?.SadThreshold ?? m.ClearlyTrivialThreshold), Exact(m.OverallMateriality),
      Exact(m.PerformanceMateriality), Exact(m.ClearlyTrivialThreshold), m.Rationale, m.QualitativeConsiderations, r?.Rationale,
      r?.DecidedByUserId, c?.MappingVersionId, c is null ? null : Long(c.MappingVersionNumber), c?.DatasetId, c?.DatasetDigest,
      c?.PolicyVersion ?? MaterialityCalculator.PolicyVersion, approval?.ApprovedByUserId, approval?.ApprovedAt);
  }
  private static async Task<PlanningRiskDetail?> RiskAsync(IAuditSphereDbContext db, Guid firmId, Guid engagementId, Guid? riskId, CancellationToken ct)
  {
    var r = await db.AuditRisks.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.EngagementId == engagementId && x.Id == riskId, ct);
    if (r is null) return null;
    var band = await db.RiskBandAssessments.AsNoTracking().Where(x => x.FirmId == firmId && x.RiskId == r.Id).OrderByDescending(x => x.AssessedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    var clear = band is null ? null : await db.RiskPartnerClearances.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.RiskBandAssessmentId == band.Id, ct);
    var owner = band is null ? null : await db.RiskOwnerAssignments.AsNoTracking().Where(x => x.FirmId == firmId && x.RiskBandAssessmentId == band.Id).OrderByDescending(x => x.AssignedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    return new(r.Id, r.AccountArea, r.Assertion, r.Description, r.Drivers, r.SignificanceDecision, r.ResponseDescription, r.ControlsConsidered,
      band?.Id, band?.LikelihoodScore, band?.MagnitudeScore, band?.FraudRisk ?? false, band?.CriticalEstimate ?? false, band?.Band,
      band?.Rationale, band?.AssessedByUserId, clear?.Id, clear?.PartnerUserId, clear?.Note, owner?.Id, owner?.OwnerUserId, owner?.OwnerStaffingLevel);
  }
  public static async Task<CommandResult<AuditPlanningCommandState>> StateAsync(IAuditSphereDbContext db, ActorContext a, Guid engagementId,
    string action, Guid? assessmentId = null, Guid? riskId = null, Guid? procedureId = null, CancellationToken ct = default)
  {
    if (!Action(action)) return Fail<AuditPlanningCommandState>(ErrorCodes.AuditPlanning.Invalid, "Choose a supported planning action.");
    var auth = await AuthorizeAsync(db, a, engagementId, action, false, ct);
    if (!auth.Succeeded) return Fail<AuditPlanningCommandState>(auth.ErrorCode!, auth.Message!);
    var e = await db.Engagements.AsNoTracking().SingleAsync(x => x.FirmId == a.FirmId && x.Id == engagementId, ct);
    var materiality = await MaterialityAsync(db, a.FirmId, engagementId, assessmentId, ct);
    var risk = await RiskAsync(db, a.FirmId, engagementId, riskId, ct);
    var procedure = await db.AuditProcedures.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.EngagementId == engagementId && x.Id == procedureId, ct);
    if (assessmentId.HasValue && materiality is null || riskId.HasValue && risk is null || procedureId.HasValue && procedure is null)
      return Fail<AuditPlanningCommandState>(ErrorCodes.ScopeDenied, "Access denied.");
    var source = await MaterialityEngineService.GetSourceAsync(db, a, engagementId, ct);
    var sourceDetail = !source.Succeeded ? null : new PlanningSourceDetail(source.Value!.MappingVersionId, Long(source.Value.MappingVersion), source.Value.DatasetId,
      source.Value.DatasetDigest, source.Value.Currency, source.Value.Options.Select(x => new PlanningBenchmark(x.Kind, x.DestinationCode, x.Label, x.Amount is { } n ? Exact(n) : null, x.LineCount)).ToArray());
    var owners = await (from staff in db.EngagementStaffAssignments.AsNoTracking()
      join user in db.Users.AsNoTracking() on new { staff.FirmId, Id = staff.UserId } equals new { user.FirmId, user.Id }
      where staff.FirmId == a.FirmId && staff.EngagementId == engagementId && staff.RevokedAt == null && !user.Disabled
      orderby staff.UserId select new PlanningOwnerOption(staff.UserId, user.DisplayName, staff.StaffingLevel)).ToListAsync(ct);
    var generation = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == e.PracticeClientId, ct);
    var head = await db.MaterialityAssessments.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.EngagementId == engagementId).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
    var blockers = new List<string>();
    var write = await AuthorizeAsync(db, a, engagementId, action, true, ct);
    if (!write.Succeeded) blockers.Add(write.Message ?? "Current publishing authority is required.");
    if (action == AuditPlanningActions.CalculateMateriality && sourceDetail is null) blockers.Add("An approved mapping over a sealed balanced trial balance is required.");
    if (action is AuditPlanningActions.RoundMateriality or AuditPlanningActions.ApproveMateriality)
    {
      if (materiality is null || materiality.AssessmentId != head || materiality.State != "DRAFT" || materiality.CalculationId is null) blockers.Add("The current source-bound draft materiality is required.");
      if (action == AuditPlanningActions.ApproveMateriality && materiality?.PreparedByUserId == a.UserId) blockers.Add("Independent Partner approval is required.");
    }
    if (action is AuditPlanningActions.AssessRisk or AuditPlanningActions.AssignRiskOwner or AuditPlanningActions.ClearRisk && risk is null) blockers.Add("Select an identified risk.");
    if (action is AuditPlanningActions.AssignRiskOwner or AuditPlanningActions.ClearRisk && risk?.AssessmentId is null) blockers.Add("Assess the risk band first.");
    if (action == AuditPlanningActions.ClearRisk && (risk?.Band != RiskBands.Red || risk.AssessedByUserId == a.UserId || risk.ClearanceId is not null)) blockers.Add("An uncleared red assessment and an independent Partner are required.");
    if (action == AuditPlanningActions.LinkProcedureRisk && procedure is null) blockers.Add("Select a procedure from this engagement.");
    var proc = procedure is null ? null : new PlanningProcedureDetail(procedure.Id, procedure.RiskId, procedure.Status, Long(procedure.CurrentResultRevision));
    var json = JsonSerializer.Serialize(new { Version = 1, a.FirmId, a.UserId, a.SessionEpoch, ClientId = e.PracticeClientId, engagementId, action, e.Generation, e.ProfessionalWorkBlocked,
      InputGeneration = generation?.InputGeneration, Materiality = materiality, Head = head, Source = sourceDetail, Risk = risk, Procedure = proc, Owners = owners,
      Blockers = blockers, MaterialityPolicy = MaterialityCalculator.PolicyVersion, RiskPolicy = RiskBandRules.RuleVersion });
    auth = await AuthorizeAsync(db, a, engagementId, action, false, ct);
    return auth.Succeeded ? CommandResult<AuditPlanningCommandState>.Ok(new(engagementId, e.PracticeClientId, action, Hashing.Sha256Hex(json), blockers.Count == 0,
      blockers, sourceDetail, materiality, risk, proc, owners, json)) : Fail<AuditPlanningCommandState>(auth.ErrorCode!, auth.Message!);
  }
  private static (Guid? Assessment, Guid? Risk, Guid? Procedure) Targets(AuditPlanningCommandFields f) =>
    (f.Round?.AssessmentId ?? f.Approve?.AssessmentId, f.AssessRisk?.RiskId ?? f.AssignRiskOwner?.RiskId ?? f.ClearRisk?.RiskId ?? f.LinkProcedureRisk?.RiskId, f.LinkProcedureRisk?.ProcedureId);

  public static async Task<CommandResult<AuditPlanningCommandPreview>> PreviewAsync(IAuditSphereDbContext db, ActorContext a,
    Guid engagementId, AuditPlanningCommandRequest? request, CancellationToken ct = default)
  {
    var f = Canonical(request?.Fields);
    if (request is null || request.RequestId == Guid.Empty || !Hash(request.ReviewBasis) || f is null || JsonSerializer.Serialize(f).Length > AuditPlanningCommandReceipt.MaximumInputJsonLength)
      return Fail<AuditPlanningCommandPreview>(ErrorCodes.AuditPlanning.Invalid, "Provide a bounded exact planning intent.");
    var t = Targets(f); var read = await StateAsync(db, a, engagementId, f.Action, t.Assessment, t.Risk, t.Procedure, ct);
    if (!read.Succeeded) return Fail<AuditPlanningCommandPreview>(read.ErrorCode!, read.Message!);
    var s = read.Value!;
    if (s.ReviewBasis != request.ReviewBasis) return Fail<AuditPlanningCommandPreview>(ErrorCodes.StaleRevision, "The reviewed planning basis changed.");
    var blockers = s.Blockers.ToList(); PlanningMaterialityOutcome? outcome = null; string? band = null;
    if (f.Calculate is { } policyFields && MaterialityCalculator.Validate(policyFields.BenchmarkKind, Decimal(policyFields.RatePercent),
      Decimal(policyFields.PerformancePercent), Decimal(policyFields.TrivialPercent)) is { } policyError) blockers.Add(policyError);
    if (f.Calculate is { } calc && s.Source is { } source && blockers.Count == 0)
    {
      var benchmark = source.Benchmarks.SingleOrDefault(x => x.Kind == calc.BenchmarkKind && x.DestinationCode == calc.DestinationCode);
      if (benchmark?.Amount is null || Decimal(benchmark.Amount) <= 0) blockers.Add("Choose a positive benchmark from the current approved source.");
      else
      {
        var figures = MaterialityCalculator.Calculate(Decimal(benchmark.Amount), benchmark.LineCount, Decimal(calc.RatePercent), Decimal(calc.PerformancePercent), Decimal(calc.TrivialPercent));
        outcome = new(Exact(figures.BenchmarkAmount), Exact(figures.PlanningMateriality), Exact(figures.TolerableError), Exact(figures.SadThreshold), source.Currency);
      }
    }
    if (f.Round is { } round && s.Materiality is { } m)
    {
      var evaluation = MaterialityPracticalRounding.Evaluate(new(Decimal(m.BenchmarkAmount), 0, Decimal(m.ComputedPlanningMateriality), Decimal(m.ComputedTolerableError), Decimal(m.ComputedSadThreshold)), Decimal(round.PlanningMateriality), Decimal(round.TolerableError), Decimal(round.SadThreshold));
      if (evaluation.Adjustment is null) blockers.Add(evaluation.Error!);
      else outcome = new(m.BenchmarkAmount, round.PlanningMateriality, round.TolerableError, round.SadThreshold, m.Currency);
    }
    if (f.Approve is not null && s.Materiality is { } approval) outcome = new(approval.BenchmarkAmount, approval.PlanningMateriality, approval.TolerableError, approval.SadThreshold, approval.Currency);
    if (f.AssessRisk is { } assess && s.Risk is { } risk) band = RiskBandRules.Band(assess.Likelihood, assess.Magnitude, risk.SignificanceDecision == "SIGNIFICANT", assess.Fraud, assess.CriticalEstimate);
    if (f.AssignRiskOwner is { } assign)
    {
      if (s.Risk?.AssessmentId != assign.ExpectedAssessmentId) blockers.Add("The selected risk assessment was superseded.");
      var owner = s.Owners.SingleOrDefault(x => x.UserId == assign.OwnerUserId);
      if (owner is null || s.Risk?.Band is null || StaffingLevels.Rank(owner.StaffingLevel) < RiskBandRules.MinimumOwnerRank(s.Risk.Band)) blockers.Add("The owner must be currently staffed at the required level.");
    }
    if (f.ClearRisk is { } clear && s.Risk?.AssessmentId != clear.ExpectedAssessmentId) blockers.Add("The selected risk assessment was superseded.");
    if (f.LinkProcedureRisk is { } link && s.Procedure?.RiskId != link.ExpectedRiskId) blockers.Add("The procedure risk link changed.");
    var final = await StateAsync(db, a, engagementId, f.Action, t.Assessment, t.Risk, t.Procedure, ct);
    if (!final.Succeeded) return Fail<AuditPlanningCommandPreview>(final.ErrorCode!, final.Message!);
    if (final.Value!.ReviewBasis != s.ReviewBasis) return Fail<AuditPlanningCommandPreview>(ErrorCodes.StaleRevision, "The planning basis changed during preview.");
    var summary = f.Action switch {
      AuditPlanningActions.CalculateMateriality => $"Calculate materiality from {f.Calculate!.BenchmarkKind}: {f.Calculate.Rationale}",
      AuditPlanningActions.RoundMateriality => $"Record Manager rounding: {f.Round!.Rationale}; Partner approval stays separate.",
      AuditPlanningActions.ApproveMateriality => $"Approve exact materiality {s.Materiality!.AssessmentId}: {s.Materiality.Rationale}; PM {s.Materiality.PlanningMateriality}, TE {s.Materiality.TolerableError}, SAD {s.Materiality.SadThreshold} {s.Materiality.Currency}.",
      AuditPlanningActions.CreateRisk => $"Record {f.CreateRisk!.SignificanceDecision} risk: {f.CreateRisk.Description}",
      AuditPlanningActions.AssessRisk => $"Assess {s.Risk!.Area} as {band}: {f.AssessRisk!.Rationale}",
      AuditPlanningActions.AssignRiskOwner => $"Assign risk {f.AssignRiskOwner!.RiskId} to {f.AssignRiskOwner.OwnerUserId} for assessment {f.AssignRiskOwner.ExpectedAssessmentId}.",
      AuditPlanningActions.ClearRisk => $"Partner clearance for exact red assessment {f.ClearRisk!.ExpectedAssessmentId}: {f.ClearRisk.Note}",
      _ => $"Change procedure {f.LinkProcedureRisk!.ProcedureId} risk from {f.LinkProcedureRisk.ExpectedRiskId} to {f.LinkProcedureRisk.RiskId}."
    };
    var result = new AuditPlanningCommandPreview(request.RequestId, RequestHash(a, engagementId, request.RequestId, request.ReviewBasis, f), request.ReviewBasis,
      f, final.Value with { CanProceed = blockers.Count == 0, Blockers = blockers }, summary, outcome, band);
    return JsonSerializer.Serialize(result).Length <= AuditPlanningCommandReceipt.MaximumSnapshotJsonLength ? CommandResult<AuditPlanningCommandPreview>.Ok(result) :
      Fail<AuditPlanningCommandPreview>(ErrorCodes.AuditPlanning.Invalid, "The reviewed planning snapshot exceeds the retained evidence limit.");
  }

  private static AuditPlanningCommandReceiptView View(AuditPlanningCommandReceipt r) => new(r.Id, r.ActorId, Long(r.ActorEpoch), r.EngagementId, r.ClientId,
    r.RequestId, r.RequestHash, r.ReviewBasis, r.Action, r.CreatedAt, JsonSerializer.Deserialize<AuditPlanningCommandPreview>(r.PreviewJson)!, JsonSerializer.Deserialize<AuditPlanningCommandResult>(r.ResultJson)!);

  public static async Task<CommandResult<AuditPlanningCommandLookup>> LookupAsync(IAuditSphereDbContext db, ActorContext a,
    Guid engagementId, Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !Hash(requestHash)) return Fail<AuditPlanningCommandLookup>(ErrorCodes.AuditPlanning.Invalid, "Use the retained exact request reference.");
    var auth = await AuthorizeAsync(db, a, engagementId, "", false, ct);
    if (!auth.Succeeded) return Fail<AuditPlanningCommandLookup>(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    auth = await LockAsync(db, a, engagementId, false, ct);
    if (!auth.Succeeded) return Fail<AuditPlanningCommandLookup>(auth.ErrorCode!, auth.Message!);
    await LockRequestAsync(db, a, requestId, false, ct);
    auth = await AuthorizeAsync(db, a, engagementId, "", false, ct);
    if (!auth.Succeeded) return Fail<AuditPlanningCommandLookup>(auth.ErrorCode!, auth.Message!);
    var row = await db.AuditPlanningCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == requestId, ct);
    if (row is not null && row.ActorEpoch != a.SessionEpoch) return Fail<AuditPlanningCommandLookup>(ErrorCodes.GenerationStale, "The retained request belongs to a previous session lifetime.");
    if (row is not null && (row.EngagementId != engagementId || row.RequestHash != requestHash)) return Fail<AuditPlanningCommandLookup>(ErrorCodes.IdempotencyConflict, "This request belongs to another exact intent.");
    // Recovery needs current action authority, but never current professional execution gates.
    auth = row is null ? auth : await AuthorityOnlyAsync(db, a, engagementId, row.Action, ct);
    if (!auth.Succeeded) return Fail<AuditPlanningCommandLookup>(auth.ErrorCode!, auth.Message!);
    await tx.CommitAsync(ct); return CommandResult<AuditPlanningCommandLookup>.Ok(new(row is not null, row is null ? null : View(row)));
  }
  private static async Task<CommandResult> AuthorityOnlyAsync(IAuditSphereDbContext db, ActorContext a, Guid engagementId, string action, CancellationToken ct)
  {
    var e = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == engagementId, ct);
    return e is null ? CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.") : await AuthorizationDecision.AuthorizeAsync(db, a,
      new(a.FirmId, e.PracticeClientId, engagementId, Roles(action), InternalOnly: true), ct);
  }

  public static async Task<CommandResult<AuditPlanningCommandReceiptView>> ExecuteAsync(IAuditSphereDbContext db, ActorContext a,
    Guid engagementId, AuditPlanningCommandRequest? request, CancellationToken ct = default)
  {
    var f = Canonical(request?.Fields);
    if (request is not { Reviewed: true } || request.RequestId == Guid.Empty || !Hash(request.ReviewBasis) || !Hash(request.ExpectedRequestHash) || f is null || JsonSerializer.Serialize(f).Length > AuditPlanningCommandReceipt.MaximumInputJsonLength)
      return Fail<AuditPlanningCommandReceiptView>(ErrorCodes.AuditPlanning.Invalid, "Preview and confirm this exact planning intent.");
    var auth = await AuthorityOnlyAsync(db, a, engagementId, f.Action, ct);
    if (!auth.Succeeded) return Fail<AuditPlanningCommandReceiptView>(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    try
    {
      auth = await LockAsync(db, a, engagementId, true, ct);
      if (!auth.Succeeded) return Fail<AuditPlanningCommandReceiptView>(auth.ErrorCode!, auth.Message!);
      auth = await AuthorityOnlyAsync(db, a, engagementId, f.Action, ct);
      if (!auth.Succeeded) return Fail<AuditPlanningCommandReceiptView>(auth.ErrorCode!, auth.Message!);
      await LockRequestAsync(db, a, request.RequestId, true, ct);
      var hash = RequestHash(a, engagementId, request.RequestId, request.ReviewBasis, f);
      var prior = await db.AuditPlanningCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == request.RequestId, ct);
      if (prior is not null)
      {
        if (prior.ActorEpoch != a.SessionEpoch) return Fail<AuditPlanningCommandReceiptView>(ErrorCodes.GenerationStale, "The retained request belongs to a previous session lifetime.");
        if (prior.EngagementId != engagementId || prior.Action != f.Action || prior.RequestHash != hash || prior.RequestHash != request.ExpectedRequestHash)
          return Fail<AuditPlanningCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
        auth = await AuthorityOnlyAsync(db, a, engagementId, prior.Action, ct);
        if (!auth.Succeeded) return Fail<AuditPlanningCommandReceiptView>(auth.ErrorCode!, auth.Message!);
        await tx.CommitAsync(ct); return CommandResult<AuditPlanningCommandReceiptView>.Ok(View(prior));
      }
      if (hash != request.ExpectedRequestHash) return Fail<AuditPlanningCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The submitted intent differs from its exact preview hash.");
      var scope = await AuditPlanningService.LockedEngagementAsync(db, a, engagementId, ct);
      if (scope.Denied is not null) return Fail<AuditPlanningCommandReceiptView>(scope.Denied, scope.Message ?? "Access denied.");
      var t = Targets(f);
      if (t.Risk is { } rid) await db.AuditRisks.FromSqlInterpolated($"SELECT * FROM audit_risks WHERE firm_id={a.FirmId} AND engagement_id={engagementId} AND id={rid} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
      if (t.Procedure is { } pid) await db.AuditProcedures.FromSqlInterpolated($"SELECT * FROM audit_procedures WHERE firm_id={a.FirmId} AND engagement_id={engagementId} AND id={pid} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
      var source = await MaterialityEngineService.GetSourceAsync(db, a, engagementId, ct);
      if (source.Succeeded)
      {
        var mappingId = source.Value!.MappingVersionId; var datasetId = source.Value.DatasetId;
        await db.MappingVersions.FromSqlInterpolated($"SELECT * FROM mapping_versions WHERE firm_id={a.FirmId} AND id={mappingId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        await db.TrialBalanceDatasets.FromSqlInterpolated($"SELECT * FROM trial_balance_datasets WHERE firm_id={a.FirmId} AND id={datasetId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
      }
      if (t.Assessment is { } assessmentId)
        await db.MaterialityAssessments.FromSqlInterpolated($"SELECT * FROM materiality_assessments WHERE firm_id={a.FirmId} AND engagement_id={engagementId} AND id={assessmentId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
      if (f.AssignRiskOwner is { } assignOwner)
      {
        await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={a.FirmId} AND id={assignOwner.OwnerUserId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        await db.EngagementStaffAssignments.FromSqlInterpolated($"SELECT * FROM engagement_staff_assignments WHERE firm_id={a.FirmId} AND engagement_id={engagementId} AND user_id={assignOwner.OwnerUserId} AND revoked_at IS NULL FOR SHARE").AsNoTracking().ToListAsync(ct);
      }
      var preview = await PreviewAsync(db, a, engagementId, request with { Fields = f }, ct);
      if (!preview.Succeeded) return Fail<AuditPlanningCommandReceiptView>(preview.ErrorCode!, preview.Message!);
      if (!preview.Value!.State.CanProceed) return Fail<AuditPlanningCommandReceiptView>(ErrorCodes.GateBlocked, preview.Value.State.Blockers.First());
      Guid resultId;
      switch (f.Action)
      {
        case AuditPlanningActions.CalculateMateriality:
          var c = f.Calculate!;
          var calculated = await MaterialityEngineService.CalculateAsync(db, a, new(engagementId, c.BenchmarkKind, c.DestinationCode, Decimal(c.RatePercent), Decimal(c.PerformancePercent), Decimal(c.TrivialPercent), c.Rationale, c.QualitativeConsiderations), ct);
          if (!calculated.Succeeded) return Fail<AuditPlanningCommandReceiptView>(calculated.ErrorCode!, calculated.Message!); resultId = calculated.Value!.AssessmentId; break;
        case AuditPlanningActions.RoundMateriality:
          var r = f.Round!; var rounded = await MaterialityEngineService.ApplyPracticalRoundingAsync(db, a, new(r.AssessmentId, Decimal(r.PlanningMateriality), Decimal(r.TolerableError), Decimal(r.SadThreshold), r.Rationale), ct);
          if (!rounded.Succeeded) return Fail<AuditPlanningCommandReceiptView>(rounded.ErrorCode!, rounded.Message!); resultId = rounded.Value!.EffectiveAssessmentId; break;
        case AuditPlanningActions.ApproveMateriality:
          var approved = await AuditPlanningService.ApproveMaterialityAssessmentAsync(db, a, f.Approve!.AssessmentId, ct);
          if (!approved.Succeeded) return Fail<AuditPlanningCommandReceiptView>(approved.ErrorCode!, approved.Message!); resultId = approved.Value!.AssessmentId; break;
        case AuditPlanningActions.CreateRisk:
          var risk = f.CreateRisk!; var created = await AuditPlanningService.CreateAuditRiskAsync(db, a, new(engagementId, risk.Area, risk.Assertion, risk.Description, risk.Drivers, risk.SignificanceDecision, risk.ControlsConsidered, risk.Response), ct);
          if (!created.Succeeded) return Fail<AuditPlanningCommandReceiptView>(created.ErrorCode!, created.Message!); resultId = created.Value!.RiskId; break;
        case AuditPlanningActions.AssessRisk:
          var band = f.AssessRisk!; var assessed = await RiskBandService.AssessAsync(db, a, new(band.RiskId, band.Likelihood, band.Magnitude, band.Fraud, band.Rationale, band.CriticalEstimate), ct);
          if (!assessed.Succeeded) return Fail<AuditPlanningCommandReceiptView>(assessed.ErrorCode!, assessed.Message!); resultId = assessed.Value; break;
        case AuditPlanningActions.AssignRiskOwner:
          var owner = f.AssignRiskOwner!; var assigned = await RiskBandService.AssignOwnerAsync(db, a, owner.RiskId, owner.OwnerUserId, ct);
          if (!assigned.Succeeded) return Fail<AuditPlanningCommandReceiptView>(assigned.ErrorCode!, assigned.Message!); resultId = assigned.Value; break;
        case AuditPlanningActions.ClearRisk:
          var clear = f.ClearRisk!; var cleared = await RiskBandService.PartnerClearAsync(db, a, clear.RiskId, clear.Note, ct);
          if (!cleared.Succeeded) return Fail<AuditPlanningCommandReceiptView>(cleared.ErrorCode!, cleared.Message!); resultId = cleared.Value; break;
        default:
          var link = f.LinkProcedureRisk!; var linked = await AuditProgramService.LinkRiskAsync(db, a, link.ProcedureId, link.RiskId, ct);
          if (!linked.Succeeded) return Fail<AuditPlanningCommandReceiptView>(linked.ErrorCode!, linked.Message!); resultId = link.ProcedureId; break;
      }
      var materialityResult = f.Action is AuditPlanningActions.CalculateMateriality or AuditPlanningActions.RoundMateriality or AuditPlanningActions.ApproveMateriality
        ? await MaterialityAsync(db, a.FirmId, engagementId, resultId, ct) : null;
      var riskResult = await RiskAsync(db, a.FirmId, engagementId, f.Action == AuditPlanningActions.CreateRisk ? resultId : t.Risk, ct);
      PlanningProcedureDetail? procedureResult = null;
      if (t.Procedure is { } procedureId)
      {
        var procedure = await db.AuditProcedures.AsNoTracking().SingleAsync(x => x.FirmId == a.FirmId && x.EngagementId == engagementId && x.Id == procedureId, ct);
        procedureResult = new(procedure.Id, procedure.RiskId, procedure.Status, Long(procedure.CurrentResultRevision));
      }
      var row = new AuditPlanningCommandReceipt { Id = Guid.CreateVersion7(), FirmId = a.FirmId, ClientId = scope.ClientId, EngagementId = engagementId,
        ActorId = a.UserId, ActorEpoch = a.SessionEpoch, RequestId = request.RequestId, RequestHash = hash, ReviewBasis = request.ReviewBasis, Action = f.Action,
        ResultId = resultId, InputJson = JsonSerializer.Serialize(f), PreviewJson = JsonSerializer.Serialize(preview.Value),
        ResultJson = JsonSerializer.Serialize(new AuditPlanningCommandResult(resultId, materialityResult, riskResult, procedureResult, preview.Value.Summary)), CreatedAt = DateTimeOffset.UtcNow };
      db.AuditPlanningCommandReceipts.Add(row); await db.SaveChangesAsync(ct);
      auth = await AuthorizeAsync(db, a, engagementId, f.Action, true, ct);
      if (!auth.Succeeded) return Fail<AuditPlanningCommandReceiptView>(auth.ErrorCode!, auth.Message!);
      await tx.CommitAsync(ct); return CommandResult<AuditPlanningCommandReceiptView>.Ok(View(row));
    }
    catch (Exception ex) when (ex.GetBaseException() is DbException cause && cause.GetType().GetProperty("SqlState")?.GetValue(cause) is "40001" or "40P01" or "23505")
    {
      await tx.RollbackAsync(ct);
      return Fail<AuditPlanningCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Concurrent planning changed the outcome. Look up the retained exact request, then retry with a fresh context.");
    }
  }
}
