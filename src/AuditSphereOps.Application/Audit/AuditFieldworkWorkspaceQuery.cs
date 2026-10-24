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
  string Title, string ApplicabilityStatus, string Status, Guid? RiskId, Guid? WorkpaperId = null,
  long CurrentResultRevision = 0, string? OriginalWording = null, string? EffectiveWording = null,
  int? DefinitionRevision = null, string DefinitionProvenance = "UNVERIFIED_LEGACY",
  int ApplicabilityRevision = 0, Guid? ApplicabilityDecisionId = null, Guid? PendingApplicabilityProposalId = null,
  string? ApplicabilityRationale = null, IReadOnlyList<FieldworkApplicabilityHistoryItem>? ApplicabilityHistory = null,
  bool CanProposeNotApplicable = false, bool CanApproveNotApplicable = false, bool CanReturnNotApplicable = false,
  string EvidenceState = "MISSING_RESULT", IReadOnlyList<ProcedureSupportLink>? SupportLinks = null);
public sealed record FieldworkApplicabilityHistoryItem(Guid Id, int Revision, string Action, string? Rationale,
  Guid? ProposalDecisionId, Guid ActorUserId, DateTimeOffset OccurredAt, int DefinitionRevision, long InputGeneration);
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
public sealed record FieldworkResultView(Guid Id, long Revision, string WorkPerformed, string? Conclusion,
  string? SubmittedEvidenceJson = null, string? EvidenceDigest = null, string? EvidenceReferencesJson = null);
public sealed record PhysicalEvidenceLinkView(Guid LinkId, Guid ItemId, string FileIndex, string BoxReference,
  string Description, string CurrentLocation, DateTimeOffset LinkedAt);
public sealed record ProcedureReviewWorkspace(Guid ProcedureId, FieldworkResultView? CurrentResult, IReadOnlyList<ReviewNoteView> Notes,
  IReadOnlyList<ProcedureEvidenceView> Evidence, IReadOnlyList<PhysicalEvidenceLinkView> PhysicalEvidence);

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
  private static readonly string[] PartnerApplicabilityRoles = ["Partner"];

  private sealed record WorkspaceAuthorization(Domain.Engagements.Engagement? Engagement, CommandResult Auth,
    bool CanManageFieldwork, bool CanApproveApplicability, bool CanReviewSelections, bool CanViewReviewNotes,
    bool CanAddOrResolveReviewNotes, bool CanRespondToReviewNotes);

  private static async Task<WorkspaceAuthorization> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return new(null, CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied."), false, false, false, false, false, false);

    async Task<CommandResult> AuthorizeRolesAsync(string[] roles) => await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, roles, true, true), ct);

    var access = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id,
        FieldworkRoles.Concat(ReviewParticipantRoles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), true, true), ct);
    if (!access.Succeeded) return new(engagement, access, false, false, false, false, false, false);

    var canManage = (await AuthorizeRolesAsync(FieldworkRoles)).Succeeded;
    var canApproveApplicability = (await AuthorizeRolesAsync(PartnerApplicabilityRoles)).Succeeded;
    var canReviewSelections = (await AuthorizeRolesAsync(SelectionReviewRoles)).Succeeded;
    var canViewNotes = (await AuthorizeRolesAsync(ReviewParticipantRoles)).Succeeded;
    var canAddOrResolveNotes = (await AuthorizeRolesAsync(ReviewRoles)).Succeeded;
    var canRespondToNotes = canViewNotes;
    return new(engagement, CommandResult.Ok(), canManage, canApproveApplicability,
      canReviewSelections, canViewNotes, canAddOrResolveNotes, canRespondToNotes);
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
        .ToListAsync(ct);
      procedures = await BuildProcedureViewsAsync(db, actor.FirmId, engagement!.PracticeClientId, engagementId, rows, workpaperMap,
        authorization.CanManageFieldwork, authorization.CanApproveApplicability, ct);
    }
    else if (authorization.CanViewReviewNotes)
    {
      // A reviewer-only projection exposes only procedures with submitted results to review.
      var rows = await db.AuditProcedures.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId && x.EngagementId == engagementId &&
          db.AuditProcedureResults.Any(r => r.FirmId == actor.FirmId && r.ClientId == engagement.PracticeClientId && r.EngagementId == engagementId && r.AuditProcedureId == x.Id))
        .OrderBy(x => x.SourceSectionNumber).ThenBy(x => x.SourceProcedureId)
        .ToListAsync(ct);
      procedures = await BuildProcedureViewsAsync(db, actor.FirmId, engagement.PracticeClientId, engagementId, rows, workpaperMap,
        authorization.CanManageFieldwork, authorization.CanApproveApplicability, ct);
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
    var typedScheduleIds = await db.AuditScheduleSourceCaptures.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == engagement!.PracticeClientId && x.EngagementId == engagementId)
      .Select(x => x.ScheduleId).ToListAsync(ct);
    var typedScheduleSet = typedScheduleIds.ToHashSet();
    foreach (var schedule in schedules.Where(x => typedScheduleSet.Contains(x.Id)).ToArray())
    {
      var current = await AuditScheduleSourceWorkspace.CheckCurrentScheduleSourceAsync(
        db, actor, engagement!.PracticeClientId, engagementId, schedule.Id, ct);
      if (!current.Succeeded) schedules.Remove(schedule);
    }
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

  private static async Task<List<FieldworkProcedure>> BuildProcedureViewsAsync(IAuditSphereDbContext db,
    Guid firmId, Guid clientId, Guid engagementId, IReadOnlyCollection<AuditProcedure> rows,
    IReadOnlyDictionary<Guid, Guid> workpaperMap, bool canPropose, bool canApprove, CancellationToken ct)
  {
    if (rows.Count == 0) return [];
    var ids = rows.Select(x => x.Id).ToArray();
    var definitions = await db.AuditProcedureDefinitionRevisions.AsNoTracking().Where(definition =>
      definition.FirmId == firmId && definition.ClientId == clientId && definition.EngagementId == engagementId &&
      ids.Contains(definition.AuditProcedureId) && db.AuditProcedures.Any(procedure =>
        procedure.FirmId == firmId && procedure.ClientId == clientId && procedure.EngagementId == engagementId &&
        procedure.Id == definition.AuditProcedureId && procedure.CurrentDefinitionRevision == definition.Revision))
      .ToDictionaryAsync(x => x.AuditProcedureId, ct);
    var sourceIds = rows.Where(x => x.ProgramProcedureId.HasValue).Select(x => x.ProgramProcedureId!.Value).Distinct().ToArray();
    var source = sourceIds.Length == 0 ? [] : await db.AuditProgramProcedures.AsNoTracking()
      .Where(x => x.FirmId == firmId && sourceIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var currentDecisions = await AuditProgramService.CurrentApplicabilityDecisionsAsync(db, firmId, clientId, engagementId, rows, ct);
    var currentList = currentDecisions.Values.ToArray();
    var proposals = await AuditProgramService.ProposalDecisionsAsync(db, firmId, clientId, engagementId, currentList, ct);
    var history = await db.AuditProcedureApplicabilityDecisions.FromSqlInterpolated($"""
      SELECT ranked.*
      FROM (
        SELECT d.*, ROW_NUMBER() OVER (PARTITION BY d.audit_procedure_id ORDER BY d.revision DESC) AS history_rank
        FROM audit_procedure_applicability_decisions AS d
        WHERE d.firm_id = {firmId} AND d.client_id = {clientId} AND d.engagement_id = {engagementId}
          AND d.audit_procedure_id = ANY ({ids})
      ) AS ranked
      WHERE ranked.history_rank <= 20
      """).AsNoTracking()
      .ToListAsync(ct);
    var historyByProcedure = history.GroupBy(x => x.AuditProcedureId).ToDictionary(x => x.Key,
      x => (IReadOnlyList<FieldworkApplicabilityHistoryItem>)x.OrderByDescending(y => y.Revision).Take(20).OrderBy(y => y.Revision).Select(y =>
        new FieldworkApplicabilityHistoryItem(y.Id, y.Revision, y.Action, y.Rationale,
          y.ProposalDecisionId, y.ActorUserId, y.OccurredAt, y.DefinitionRevision, y.InputGeneration)).ToList());
    var inputGeneration = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == firmId && x.Id == clientId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct) ?? 0;
    var evidence = await AuditFieldworkService.EvaluateProcedureEvidenceBatchForAuthorizedScopeAsync(db, firmId, clientId,
      engagementId, rows, ct);
    return rows.Select(row =>
    {
      var definition = definitions.GetValueOrDefault(row.Id);
      var currentDecision = currentDecisions.GetValueOrDefault(row.Id);
      var proposal = currentDecision?.ProposalDecisionId is { } proposalId ? proposals.GetValueOrDefault(proposalId) : currentDecision;
      var applicabilityStatus = AuditProgramService.EffectiveApplicabilityStatus(row, currentDecision, proposal, inputGeneration);
      var pendingProposalId = applicabilityStatus == AuditApplicabilityStatuses.NotApplicablePendingReview &&
        AuditProgramService.IsCurrentPendingNotApplicable(row, currentDecision, inputGeneration) ? currentDecision?.Id : null;
      var original = row.ProgramProcedureId is { } sourceId ? source.GetValueOrDefault(sourceId)?.SourceWording : null;
      var provenance = definition is null ? "UNVERIFIED_LEGACY" : definition.IsLegacySnapshot ? "LEGACY_EFFECTIVE_SNAPSHOT" : "AUTHORED_REVISION";
      var workpaperId = workpaperMap.TryGetValue(row.Id, out var wpId) ? (Guid?)wpId : null;
      return new FieldworkProcedure(row.Id, row.SourceProcedureId, row.SourceSectionNumber, row.SourceSectionTitle,
        definition?.EffectiveTitle ?? row.Title, applicabilityStatus, row.Status, row.RiskId,
        workpaperId, row.CurrentResultRevision, original,
        definition?.EffectiveWording ?? row.SourceWording, definition?.Revision, provenance,
        row.CurrentApplicabilityRevision, currentDecision?.Id, pendingProposalId, row.ApplicabilityRationale,
        historyByProcedure.GetValueOrDefault(row.Id, []), canPropose && pendingProposalId is null,
        canApprove && pendingProposalId is not null, canApprove && pendingProposalId is not null,
        evidence.GetValueOrDefault(row.Id)?.State ?? "MISSING_RESULT",
        AuditProcedureSupportMap.For(row.SourceProcedureId, row.SourceSectionNumber, engagementId, workpaperId));
    }).ToList();
  }

  /// <summary>Current submitted result, its anchored review notes and linked client evidence for one procedure.</summary>
  public static async Task<CommandResult<ProcedureReviewWorkspace>> ProcedureAsync(IAuditSphereDbContext db, ActorContext actor, Guid procedureId, CancellationToken ct = default)
  {
    var procedure = await db.AuditProcedures.AsNoTracking().Where(x => x.Id == procedureId && x.FirmId == actor.FirmId).Select(x => new { x.EngagementId }).SingleOrDefaultAsync(ct);
    if (procedure is null) return CommandResult<ProcedureReviewWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var authorization = await AuthorizeAsync(db, actor, procedure.EngagementId, ct);
    if (!authorization.Auth.Succeeded || !authorization.CanViewReviewNotes)
      return CommandResult<ProcedureReviewWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var resultRow = await db.AuditProcedureResults.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.AuditProcedureId == procedureId)
      .OrderByDescending(x => x.Revision).Select(x => new { x.Id, x.Revision, x.WorkPerformed, x.Conclusion, x.StructuredResultJson, x.EvidenceReferencesJson }).FirstOrDefaultAsync(ct);
    var result = resultRow is null ? null : new FieldworkResultView(resultRow.Id, resultRow.Revision, resultRow.WorkPerformed,
      resultRow.Conclusion, AuditProgramService.SubmittedEvidence(resultRow.StructuredResultJson),
      AuditProgramService.SubmittedEvidenceDigest(resultRow.StructuredResultJson), resultRow.EvidenceReferencesJson);
    var physical = await (from link in db.ProcedurePhysicalLinks.AsNoTracking()
      join item in db.PhysicalEvidenceItems.AsNoTracking()
        on new { link.FirmId, link.ClientId, link.EngagementId, Id = link.PhysicalEvidenceItemId }
        equals new { item.FirmId, item.ClientId, item.EngagementId, item.Id }
      where link.FirmId == actor.FirmId && link.ProcedureId == procedureId
      orderby link.LinkedAt
      select new PhysicalEvidenceLinkView(link.Id, item.Id, item.FileIndex, item.BoxReference, item.Description,
        item.CurrentLocation, link.LinkedAt)).ToListAsync(ct);
    return CommandResult<ProcedureReviewWorkspace>.Ok(new(procedureId, result, await ReviewNotesService.ListAsync(db, actor, procedureId, ct),
      await AuditFieldworkService.ProcedureEvidenceAsync(db, actor, procedureId, ct), physical));
  }

  public static Task<CommandResult<EngagementAuditProgramResult>> AdoptPublishedAsync(
    IAuditSphereDbContext db, ActorContext actor, AdoptAuditProgramRequest request, CancellationToken ct = default) =>
    AuditProgramService.AdoptAsync(db, actor, request, ct);
}
