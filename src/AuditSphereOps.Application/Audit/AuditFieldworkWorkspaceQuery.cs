using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AuditSphereOps.Application.Audit;

public sealed record FieldworkProgram(string ProgramCode, string Version, string SourceHash);
public sealed record FieldworkProcedure(Guid Id, string SourceProcedureId, int? SourceSectionNumber, string? SourceSectionTitle,
  string Title, string ApplicabilityStatus, string Status, Guid? RiskId, Guid? WorkpaperId = null, long CurrentResultRevision = 0, string? SourceWording = null);
public sealed record FieldworkRiskOption(Guid Id, string Area, string SignificanceDecision, string? Band,
  string? EffectiveBand, decimal? Balance, string? Currency, decimal? TolerableError, decimal? PlanningMateriality,
  Guid? RiskAssessmentId, Guid? MaterialityAssessmentId, Guid? MaterialityCalculationId, Guid? MappingVersionId,
  long? MappingVersionNumber, Guid? DatasetId, string? DatasetDigest, string? DestinationCode, string? StatementSection,
  string? RuleVersion, string? Explanation, string? Blocker);
public sealed record FieldworkAggregate(Guid Id, string Status, string Conclusion, bool PreparedByMe);
public sealed record FieldworkSchedule(Guid Id, string ScheduleType, int RowCount, string Currency);
public sealed record FieldworkSamplingRun(Guid Id, Guid SelectionId, string SelectionStatus, bool PreparedByMe, bool CanReviewSelection,
  DateTimeOffset CreatedAt, string Method, decimal? Interval, decimal? KeyItemThreshold, int? SampleSize, int? Seed,
  int SelectedCount, int PopulationCount, decimal CoveragePercent, string SourceDigest, string SelectionDigest, string EngineVersion,
  string? PreviewDigest, string? OrderingPolicy, IReadOnlyList<string> AttributeFields, bool Reproduces);
public sealed record FieldworkMovement(string ToLocation, DateTimeOffset MovedAt);
public sealed record FieldworkPhysicalItem(Guid Id, string FileIndex, string BoxReference, string Description, string CurrentLocation,
  IReadOnlyList<FieldworkMovement> Movements, IReadOnlyList<string> ProcedureTitles);
public sealed record AuditFieldworkWorkspace(Guid EngagementId, int CatalogProcedureCount, string CatalogVersion, FieldworkProgram? Program,
  IReadOnlyList<FieldworkProcedure> Procedures, IReadOnlyList<FieldworkRiskOption> Risks,
  IReadOnlyList<AuditDifferenceSummary> Differences, FieldworkAggregate? Aggregate, bool AggregateCurrentAndReviewed,
  IReadOnlyList<FieldworkSchedule> Schedules, IReadOnlyList<string> SamplingMethods, IReadOnlyList<FieldworkSamplingRun> SamplingRuns,
  IReadOnlyList<EvidenceCandidate> EvidenceCandidates, IReadOnlyList<FieldworkPhysicalItem> PhysicalItems, bool CanManageFieldwork,
  bool CanReviewSelections, bool CanViewReviewNotes, bool CanAddOrResolveReviewNotes, bool CanRespondToReviewNotes);
public sealed record FieldworkResultView(Guid Id, long Revision, string WorkPerformed, string? Conclusion);
public sealed record ProcedureReviewWorkspace(Guid ProcedureId, FieldworkResultView? CurrentResult, IReadOnlyList<ReviewNoteView> Notes, IReadOnlyList<ProcedureEvidenceView> Evidence);

/// <summary>
/// Engagement-scoped fieldwork control-center projection (programme coverage, differences, sampling log, evidence
/// candidates and physical file index). An out-of-scope engagement is indistinguishable from a missing one.
/// </summary>
public static class AuditFieldworkWorkspaceQuery
{
  public const string DefaultProgramVersion = "2026.1";
  private static readonly string[] FieldworkRoles = ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static readonly string[] ReviewRoles = ["Reviewer", "Senior", "Manager", "Partner", "Administrator"];
  private static readonly string[] SelectionReviewRoles = ["Reviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReviewParticipantRoles = ["Reviewer", "Senior", "Staff", "Manager", "Partner", "Administrator", "Auditor"];

  private sealed record WorkspaceAuthorization(Domain.Engagements.Engagement? Engagement, CommandResult Auth,
    bool CanManageFieldwork, bool CanReviewSelections, bool CanViewReviewNotes, bool CanAddOrResolveReviewNotes, bool CanRespondToReviewNotes);

  private static async Task<WorkspaceAuthorization> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return new(null, CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied."), false, false, false, false, false);

    async Task<CommandResult> AuthorizeRolesAsync(string[] roles) => await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, roles, true, true), ct);

    var access = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id,
        FieldworkRoles.Concat(ReviewParticipantRoles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), true, true), ct);
    if (!access.Succeeded) return new(engagement, access, false, false, false, false, false);

    var canManage = (await AuthorizeRolesAsync(FieldworkRoles)).Succeeded;
    var canReviewSelections = (await AuthorizeRolesAsync(SelectionReviewRoles)).Succeeded;
    var canViewNotes = (await AuthorizeRolesAsync(ReviewParticipantRoles)).Succeeded;
    var canAddOrResolveNotes = (await AuthorizeRolesAsync(ReviewRoles)).Succeeded;
    var canRespondToNotes = canViewNotes;
    return new(engagement, CommandResult.Ok(), canManage, canReviewSelections, canViewNotes, canAddOrResolveNotes, canRespondToNotes);
  }

  public static async Task<CommandResult<AuditFieldworkWorkspace>> GetAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var authorization = await AuthorizeAsync(db, actor, engagementId, ct);
    if (!authorization.Auth.Succeeded) return CommandResult<AuditFieldworkWorkspace>.Fail(authorization.Auth.ErrorCode!, authorization.Auth.Message!);
    var engagement = authorization.Engagement!;
    var adoption = authorization.CanManageFieldwork
      ? await db.EngagementAuditPrograms.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId && x.EngagementId == engagementId && x.Status == EngagementAuditProgramStatuses.Adopted)
        .OrderByDescending(x => x.AdoptedAt).FirstOrDefaultAsync(ct)
      : null;
    FieldworkProgram? program = null;
    List<FieldworkProcedure> procedures = [];
    var workpaperMap = await db.Workpapers.AsNoTracking()
      .Where(w => w.FirmId == actor.FirmId && w.EngagementId == engagementId && w.ProcedureId != null)
      .GroupBy(w => w.ProcedureId!.Value)
      .Select(g => new { ProcedureId = g.Key, WorkpaperId = g.OrderByDescending(x => x.CreatedAt).Select(x => x.Id).FirstOrDefault() })
      .ToDictionaryAsync(x => x.ProcedureId, x => x.WorkpaperId, ct);

    if (adoption is not null)
    {
      var version = await db.AuditProgramVersions.AsNoTracking().SingleAsync(x => x.Id == adoption.ProgramVersionId && x.FirmId == actor.FirmId, ct);
      program = new(version.ProgramCode, version.Version, version.SourceHash);
      var rows = await db.AuditProcedures.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement!.PracticeClientId && x.EngagementId == engagementId && x.EngagementProgramId == adoption.Id)
        .OrderBy(x => x.SourceSectionNumber).ThenBy(x => x.SourceProcedureId)
        .Select(x => new { x.Id, x.SourceProcedureId, x.SourceSectionNumber, x.SourceSectionTitle, x.Title, x.ApplicabilityStatus, x.Status, x.RiskId, x.CurrentResultRevision, x.SourceWording })
        .ToListAsync(ct);
      procedures = rows.Select(x => new FieldworkProcedure(x.Id, x.SourceProcedureId, x.SourceSectionNumber, x.SourceSectionTitle,
        x.Title, x.ApplicabilityStatus, x.Status, x.RiskId, workpaperMap.GetValueOrDefault(x.Id), x.CurrentResultRevision, x.SourceWording)).ToList();
    }
    else if (authorization.CanViewReviewNotes)
    {
      // A reviewer-only projection exposes only procedures with submitted results to review.
      var rows = await db.AuditProcedures.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId && x.EngagementId == engagementId &&
          db.AuditProcedureResults.Any(r => r.FirmId == actor.FirmId && r.ClientId == engagement.PracticeClientId && r.EngagementId == engagementId && r.AuditProcedureId == x.Id))
        .OrderBy(x => x.SourceSectionNumber).ThenBy(x => x.SourceProcedureId)
        .Select(x => new { x.Id, x.SourceProcedureId, x.SourceSectionNumber, x.SourceSectionTitle, x.Title, x.ApplicabilityStatus, x.Status, x.RiskId, x.CurrentResultRevision, x.SourceWording })
        .ToListAsync(ct);
      procedures = rows.Select(x => new FieldworkProcedure(x.Id, x.SourceProcedureId, x.SourceSectionNumber, x.SourceSectionTitle,
        x.Title, x.ApplicabilityStatus, x.Status, x.RiskId, workpaperMap.GetValueOrDefault(x.Id), x.CurrentResultRevision, x.SourceWording)).ToList();
    }

    IReadOnlyList<FieldworkRiskOption> risks = [];
    if (authorization.CanManageFieldwork || authorization.CanViewReviewNotes)
    {
      var riskRows = await db.AuditRisks.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId && x.EngagementId == engagementId)
        .OrderBy(x => x.AccountArea).ThenBy(x => x.Id).Take(1000)
        .Select(x => new { x.Id, x.AccountArea, x.SignificanceDecision }).ToListAsync(ct);
      var riskIds = riskRows.Select(x => x.Id).ToArray();
      var assessments = riskIds.Length == 0 ? [] : await db.RiskBandAssessments.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && riskIds.Contains(x.RiskId))
        .OrderByDescending(x => x.AssessedAt).ThenByDescending(x => x.Id).ToListAsync(ct);
      var assessmentByRisk = assessments.GroupBy(x => x.RiskId).ToDictionary(x => x.Key, x => x.First());
      var evaluations = await ProcedureRiskBandEvaluator.EvaluateManyAsync(db, actor.FirmId, engagementId, riskIds, ct);
      risks = riskRows.Select(risk =>
      {
        evaluations.TryGetValue(risk.Id, out var evaluation);
        var value = evaluation?.Value;
        assessmentByRisk.TryGetValue(risk.Id, out var assessment);
        return new FieldworkRiskOption(risk.Id, risk.AccountArea, risk.SignificanceDecision,
          value?.QualitativeBand ?? assessment?.Band, value?.Band, value?.Balance, value?.Currency,
          value?.TolerableError, value?.PlanningMateriality, value?.RiskAssessmentId ?? assessment?.Id,
          value?.MaterialityAssessmentId, value?.MaterialityCalculationId, value?.MappingVersionId,
          value?.MappingVersionNumber, value?.DatasetId, value?.DatasetDigest, value?.DestinationCode,
          value?.StatementSection, value?.RuleVersion, value?.Explanation, evaluation?.Blocker);
      }).ToList();
    }

    var runs = new List<FieldworkSamplingRun>();
    foreach (var run in (await AuditFieldworkService.ListSamplingRunsAsync(db, actor, engagementId, ct)).Take(10))
      if ((await AuditFieldworkService.GetSamplingRunAsync(db, actor, run.Id, ct)).Value is { } view)
      {
        var selection = await db.AuditSelections.AsNoTracking().SingleAsync(x => x.Id == view.Run.SelectionId && x.FirmId == actor.FirmId, ct);
        runs.Add(new(view.Run.Id, selection.Id, selection.Status, selection.CreatedByUserId == actor.UserId,
          authorization.CanReviewSelections && selection.CreatedByUserId != actor.UserId && selection.Status != AuditSelectionStatuses.Reviewed,
          view.Run.CreatedAt, view.Run.Method, view.Run.Interval, view.Run.KeyItemThreshold, view.Run.SampleSize, view.Run.Seed,
          view.Run.SelectedCount, view.Run.PopulationCount, view.Run.CoveragePercent, view.Run.SourceDigest, view.Run.SelectionDigest, view.Run.EngineVersion,
          view.Run.PreviewDigest, view.Run.OrderingPolicy, ReadSamplingFields(view.Run.AttributeFields), view.Reproduces));
      }

    if (!authorization.CanManageFieldwork)
      return CommandResult<AuditFieldworkWorkspace>.Ok(new(engagementId, 0, string.Empty, null, procedures, risks, [], null, false, [], [], runs, [], [],
        false, authorization.CanReviewSelections, authorization.CanViewReviewNotes, authorization.CanAddOrResolveReviewNotes, authorization.CanRespondToReviewNotes));

    var differences = await AuditFieldworkService.GetDifferenceSummariesAsync(db, actor, engagementId, ct);
    var aggregate = await db.AuditAreaAssessments.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement!.PracticeClientId && x.EngagementId == engagementId &&
        x.AreaCode == AuditAreaCodes.AuditDifferences && x.AssessmentKind == AuditAreaAssessmentKinds.AggregateDifferences)
      .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
    var summaries = differences.Succeeded ? differences.Value! : [];
    var currentAndReviewed = false;
    if (summaries.Count > 0)
    {
      var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, actor, engagementId, ct);
      currentAndReviewed = completion.Succeeded && !completion.Value!.Blockers.Contains("difference-aggregate:missing-stale-or-unreviewed", StringComparer.Ordinal);
    }
    var schedules = await db.AuditSchedules.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.Status == AuditScheduleStatuses.Approved)
      .Select(x => new FieldworkSchedule(x.Id, x.ScheduleType, x.RowCount, x.Currency)).ToListAsync(ct);
    var candidates = await AuditFieldworkService.EvidenceCandidatesAsync(db, actor, engagementId, ct);
    var physical = (await AuditFieldworkService.PhysicalItemsAsync(db, actor, engagementId, ct)).Select(x => new FieldworkPhysicalItem(x.Id, x.FileIndex, x.BoxReference,
      x.Description, x.CurrentLocation, x.Movements.Select(m => new FieldworkMovement(m.ToLocation, m.MovedAt)).ToList(), x.Procedures.Select(p => p.Title).ToList())).ToList();
    return CommandResult<AuditFieldworkWorkspace>.Ok(new(engagementId, AuditProgramCatalog.Items.Count, DefaultProgramVersion, program, procedures, risks, summaries,
      aggregate is null ? null : new FieldworkAggregate(aggregate.Id, aggregate.Status, aggregate.Conclusion, aggregate.CreatedByUserId == actor.UserId),
      currentAndReviewed, schedules, AuditSamplingMethods.All, runs, candidates, physical, authorization.CanManageFieldwork, authorization.CanReviewSelections, authorization.CanViewReviewNotes,
      authorization.CanAddOrResolveReviewNotes, authorization.CanRespondToReviewNotes));
  }

  private static IReadOnlyList<string> ReadSamplingFields(string? json)
  {
    if (string.IsNullOrWhiteSpace(json)) return [];
    try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
    catch (JsonException) { return []; }
  }

  /// <summary>Current submitted result, its anchored review notes and linked client evidence for one procedure.</summary>
  public static async Task<CommandResult<ProcedureReviewWorkspace>> ProcedureAsync(IAuditSphereDbContext db, ActorContext actor, Guid procedureId, CancellationToken ct = default)
  {
    var procedure = await db.AuditProcedures.AsNoTracking().Where(x => x.Id == procedureId && x.FirmId == actor.FirmId).Select(x => new { x.EngagementId }).SingleOrDefaultAsync(ct);
    if (procedure is null) return CommandResult<ProcedureReviewWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var authorization = await AuthorizeAsync(db, actor, procedure.EngagementId, ct);
    if (!authorization.Auth.Succeeded || !authorization.CanViewReviewNotes)
      return CommandResult<ProcedureReviewWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var result = await db.AuditProcedureResults.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.AuditProcedureId == procedureId)
      .OrderByDescending(x => x.Revision).Select(x => new FieldworkResultView(x.Id, x.Revision, x.WorkPerformed, x.Conclusion)).FirstOrDefaultAsync(ct);
    return CommandResult<ProcedureReviewWorkspace>.Ok(new(procedureId, result, await ReviewNotesService.ListAsync(db, actor, procedureId, ct),
      await AuditFieldworkService.ProcedureEvidenceAsync(db, actor, procedureId, ct)));
  }

  public static async Task<CommandResult<Guid>> PublishAndAdoptAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, string version, CancellationToken ct = default)
  {
    var published = await AuditProgramService.PublishAsync(db, actor, new PublishAuditProgramRequest(version, AuditProgramCatalog.SourceHash), ct);
    if (!published.Succeeded) return CommandResult<Guid>.Fail(published.ErrorCode!, published.Message!);
    var adopted = await AuditProgramService.AdoptAsync(db, actor, new AdoptAuditProgramRequest(engagementId, published.Value!.ProgramVersionId), ct);
    return adopted.Succeeded ? CommandResult<Guid>.Ok(adopted.Value!.EngagementProgramId) : CommandResult<Guid>.Fail(adopted.ErrorCode!, adopted.Message!);
  }
}
