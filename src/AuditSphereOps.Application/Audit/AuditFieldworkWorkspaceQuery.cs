using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record FieldworkProgram(string ProgramCode, string Version, string SourceHash);
public sealed record FieldworkProcedure(Guid Id, string SourceProcedureId, int? SourceSectionNumber, string? SourceSectionTitle, string Title, string ApplicabilityStatus, string Status);
public sealed record FieldworkAggregate(Guid Id, string Status, string Conclusion, bool PreparedByMe);
public sealed record FieldworkSchedule(Guid Id, string ScheduleType, int RowCount, string Currency);
public sealed record FieldworkSamplingRun(Guid Id, DateTimeOffset CreatedAt, string Method, decimal? Interval, decimal? KeyItemThreshold, int? SampleSize, int? Seed,
  int SelectedCount, int PopulationCount, decimal CoveragePercent, string SourceDigest, bool Reproduces);
public sealed record FieldworkMovement(string ToLocation, DateTimeOffset MovedAt);
public sealed record FieldworkPhysicalItem(Guid Id, string FileIndex, string BoxReference, string Description, string CurrentLocation,
  IReadOnlyList<FieldworkMovement> Movements, IReadOnlyList<string> ProcedureTitles);
public sealed record AuditFieldworkWorkspace(Guid EngagementId, int CatalogProcedureCount, string CatalogVersion, FieldworkProgram? Program,
  IReadOnlyList<FieldworkProcedure> Procedures, IReadOnlyList<AuditDifferenceSummary> Differences, FieldworkAggregate? Aggregate, bool AggregateCurrentAndReviewed,
  IReadOnlyList<FieldworkSchedule> Schedules, IReadOnlyList<string> SamplingMethods, IReadOnlyList<FieldworkSamplingRun> SamplingRuns,
  IReadOnlyList<EvidenceCandidate> EvidenceCandidates, IReadOnlyList<FieldworkPhysicalItem> PhysicalItems);
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

  private static async Task<(Domain.Engagements.Engagement? Engagement, CommandResult Auth)> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return (null, CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied."));
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, FieldworkRoles, true, true), ct);
    return (engagement, auth);
  }

  public static async Task<CommandResult<AuditFieldworkWorkspace>> GetAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var (engagement, auth) = await AuthorizeAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded) return CommandResult<AuditFieldworkWorkspace>.Fail(auth.ErrorCode!, auth.Message!);
    var adoption = await db.EngagementAuditPrograms.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement!.PracticeClientId && x.EngagementId == engagementId && x.Status == EngagementAuditProgramStatuses.Adopted)
      .OrderByDescending(x => x.AdoptedAt).FirstOrDefaultAsync(ct);
    FieldworkProgram? program = null;
    List<FieldworkProcedure> procedures = [];
    if (adoption is not null)
    {
      var version = await db.AuditProgramVersions.AsNoTracking().SingleAsync(x => x.Id == adoption.ProgramVersionId && x.FirmId == actor.FirmId, ct);
      program = new(version.ProgramCode, version.Version, version.SourceHash);
      procedures = await db.AuditProcedures.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement!.PracticeClientId && x.EngagementId == engagementId && x.EngagementProgramId == adoption.Id)
        .OrderBy(x => x.SourceSectionNumber).ThenBy(x => x.SourceProcedureId)
        .Select(x => new FieldworkProcedure(x.Id, x.SourceProcedureId, x.SourceSectionNumber, x.SourceSectionTitle, x.Title, x.ApplicabilityStatus, x.Status)).ToListAsync(ct);
    }
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
    var runs = new List<FieldworkSamplingRun>();
    foreach (var run in (await AuditFieldworkService.ListSamplingRunsAsync(db, actor, engagementId, ct)).Take(10))
      if ((await AuditFieldworkService.GetSamplingRunAsync(db, actor, run.Id, ct)).Value is { } view)
        runs.Add(new(view.Run.Id, view.Run.CreatedAt, view.Run.Method, view.Run.Interval, view.Run.KeyItemThreshold, view.Run.SampleSize, view.Run.Seed,
          view.Run.SelectedCount, view.Run.PopulationCount, view.Run.CoveragePercent, view.Run.SourceDigest, view.Reproduces));
    var candidates = await AuditFieldworkService.EvidenceCandidatesAsync(db, actor, engagementId, ct);
    var physical = (await AuditFieldworkService.PhysicalItemsAsync(db, actor, engagementId, ct)).Select(x => new FieldworkPhysicalItem(x.Id, x.FileIndex, x.BoxReference,
      x.Description, x.CurrentLocation, x.Movements.Select(m => new FieldworkMovement(m.ToLocation, m.MovedAt)).ToList(), x.Procedures.Select(p => p.Title).ToList())).ToList();
    return CommandResult<AuditFieldworkWorkspace>.Ok(new(engagementId, AuditProgramCatalog.Items.Count, DefaultProgramVersion, program, procedures, summaries,
      aggregate is null ? null : new FieldworkAggregate(aggregate.Id, aggregate.Status, aggregate.Conclusion, aggregate.CreatedByUserId == actor.UserId),
      currentAndReviewed, schedules, AuditSamplingMethods.All, runs, candidates, physical));
  }

  /// <summary>Current submitted result, its anchored review notes and linked client evidence for one procedure.</summary>
  public static async Task<CommandResult<ProcedureReviewWorkspace>> ProcedureAsync(IAuditSphereDbContext db, ActorContext actor, Guid procedureId, CancellationToken ct = default)
  {
    var procedure = await db.AuditProcedures.AsNoTracking().Where(x => x.Id == procedureId && x.FirmId == actor.FirmId).Select(x => new { x.EngagementId }).SingleOrDefaultAsync(ct);
    if (procedure is null) return CommandResult<ProcedureReviewWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var (_, auth) = await AuthorizeAsync(db, actor, procedure.EngagementId, ct);
    if (!auth.Succeeded) return CommandResult<ProcedureReviewWorkspace>.Fail(auth.ErrorCode!, auth.Message!);
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
