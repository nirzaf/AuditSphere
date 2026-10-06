using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ConsolidationScopeRow
{
  public Guid Id { get; init; }
  public string GroupName { get; init; } = string.Empty;
  public int Version { get; init; }
  public string Method { get; init; } = string.Empty;
  public string Currency { get; init; } = string.Empty;
  public int OwnershipEdges { get; init; }
  public int GroupedMatches { get; init; }
  public int OutsideReviews { get; init; }
  public int OpenMatchCount { get; init; }
  public int ComponentCount { get; init; }
  public int ApprovedComponentCount { get; init; }
  public int ExternalPackCount { get; init; }
  public int ApprovedExternalPackCount { get; init; }
  public int AdvancedScheduleCount { get; init; }
  public int ApprovedAdvancedScheduleCount { get; init; }
  public int AdvancedExecutionCount { get; init; }
  public int ApprovedAdvancedExecutionCount { get; init; }
  public string AdvancedScheduleStatus { get; init; } = string.Empty;
  public string AdvancedScheduleGuidance { get; init; } = string.Empty;
  public string RateSetStatus { get; init; } = string.Empty;
  public string TranslationPolicyStatus { get; init; } = string.Empty;
  public int EliminationJournalCount { get; init; }
  public int ApprovedEliminationJournalCount { get; init; }
  public string Status { get; init; } = string.Empty;
  public string RunStatus { get; init; } = string.Empty;
  public bool IsAdvanced { get; init; }
  public ConsolidationReport? Report { get; init; }
}
public sealed record ConsolidationGroupRow(Guid Id, string Code, string Name, IReadOnlyList<ConsolidationScopeRow> Scopes);

/// <summary>
/// Group consolidation overview for explicitly granted groups only (firm-wide grants never imply group access):
/// perimeter versions, component packs, FX pins, advanced-schedule readiness, intercompany, eliminations and the latest
/// current approved report. Consolidation never mutates component client books.
/// </summary>
public static class ConsolidationOverviewQuery
{
  public static async Task<CommandResult<IReadOnlyList<ConsolidationGroupRow>>> GetAsync(IClientAccountingDbContext db, ActorContext actor, CancellationToken ct = default)
  {

    var candidateGroupIds = await db.GroupAccessGrants.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.RevokedAt == null)
      .Select(x => x.GroupId).Distinct().ToArrayAsync(ct);
    var groupIds = new List<Guid>(candidateGroupIds.Length);
    foreach (var groupId in candidateGroupIds)
    {
      var access = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, groupId,
        ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"], ct);
      if (access.Succeeded) groupIds.Add(groupId);
    }
    var groups = await db.ClientGroups.AsNoTracking().Where(x => x.FirmId == actor.FirmId && groupIds.Contains(x.Id)).OrderBy(x => x.Name).ToListAsync(ct);
    var scopes = await db.ConsolidationScopeVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && groupIds.Contains(x.GroupId)).ToListAsync(ct);
    var ownershipCounts = await db.OwnershipInterestVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && groupIds.Contains(x.GroupId) &&
      x.Status == AccountingWorkflowStates.Approved).GroupBy(x => x.ScopeVersionId).Select(x => new { ScopeId = x.Key, Count = x.Count() })
      .ToDictionaryAsync(x => x.ScopeId, x => x.Count, ct);
    var matchCounts = await db.IntercompanyMatches.AsNoTracking().Where(x => x.FirmId == actor.FirmId && groupIds.Contains(x.GroupId) &&
      x.Status != AccountingWorkflowStates.Rejected).GroupBy(x => x.ScopeVersionId).Select(x => new
      {
        ScopeId = x.Key,
        Grouped = x.Count(y => y.MatchMode == IntercompanyMatchModes.Grouped),
        Outside = x.Count(y => y.OutsidePerimeterReview),
        Open = x.Count(y => y.Status != AccountingWorkflowStates.Approved)
      }).ToDictionaryAsync(x => x.ScopeId, ct);
    var componentCounts = await db.ConsolidationComponents.AsNoTracking().Where(x => x.FirmId == actor.FirmId && groupIds.Contains(x.GroupId))
      .GroupBy(x => x.ScopeVersionId).Select(x => new { ScopeId = x.Key, Count = x.Count(), Approved = x.Count(y => y.Status == AccountingWorkflowStates.Approved) })
      .ToDictionaryAsync(x => x.ScopeId, ct);
    var packCounts = await db.ExternalComponentPacks.AsNoTracking().Where(x => x.FirmId == actor.FirmId && groupIds.Contains(x.GroupId))
      .GroupBy(x => x.ScopeVersionId).Select(x => new { ScopeId = x.Key, Count = x.Count(), Approved = x.Count(y => y.Status == ExternalComponentPackStates.Approved) })
      .ToDictionaryAsync(x => x.ScopeId, ct);
    var advancedScheduleCounts = await db.AdvancedConsolidationMethodSchedules.AsNoTracking().Where(x => x.FirmId == actor.FirmId && groupIds.Contains(x.GroupId))
      .GroupBy(x => x.ScopeVersionId).Select(x => new { ScopeId = x.Key, Count = x.Count(), Approved = x.Count(y => y.Status == AdvancedConsolidationMethodScheduleStates.Approved) })
      .ToDictionaryAsync(x => x.ScopeId, ct);
    var advancedExecutionCounts = await db.AdvancedConsolidationExecutions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && groupIds.Contains(x.GroupId))
      .GroupBy(x => x.ScopeVersionId).Select(x => new { ScopeId = x.Key, Count = x.Count(), Approved = x.Count(y => y.Status == AdvancedConsolidationExecutionStates.Approved) })
      .ToDictionaryAsync(x => x.ScopeId, ct);
    var journalCounts = await db.ConsolidationJournals.AsNoTracking().Where(x => x.FirmId == actor.FirmId && groupIds.Contains(x.GroupId))
      .GroupBy(x => x.ScopeVersionId).Select(x => new { ScopeId = x.Key, Count = x.Count(), Approved = x.Count(y => y.Status == AccountingWorkflowStates.Approved) })
      .ToDictionaryAsync(x => x.ScopeId, ct);
    var runStatuses = await db.ConsolidationRuns.AsNoTracking().Where(x => x.FirmId == actor.FirmId && groupIds.Contains(x.GroupId))
      .GroupBy(x => x.ScopeVersionId).Select(x => new { ScopeId = x.Key, Status = x.OrderByDescending(y => y.CreatedAt)
        .ThenByDescending(y => y.Id).Select(y => y.Status).First() }).ToDictionaryAsync(x => x.ScopeId, x => x.Status, ct);
    var reports = new Dictionary<Guid, ConsolidationReport>();
    foreach (var scope in scopes.Where(x => !AdvancedConsolidationMethods.All.Contains(x.Method) &&
      runStatuses.GetValueOrDefault(x.Id) == AccountingWorkflowStates.Approved))
    {
      var report = await ConsolidationService.GetLatestReportAsync(db, actor, scope.Id, ct);
      if (report.Succeeded && report.Value is not null)
        reports[scope.Id] = report.Value;
    }
    var rateSetIds = scopes.Where(x => x.ExchangeRateSetVersionId.HasValue).Select(x => x.ExchangeRateSetVersionId!.Value).Distinct().ToArray();
    var rateSets = await db.ExchangeRateSetVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && rateSetIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.Status, ct);
    var translationPolicyIds = scopes.Where(x => x.TranslationPolicyVersionId.HasValue).Select(x => x.TranslationPolicyVersionId!.Value).Distinct().ToArray();
    var translationPolicies = await db.TranslationPolicyVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && translationPolicyIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.Status, ct);
    var result = groups.Select(group => new ConsolidationGroupRow(group.Id, group.Code, group.Name, scopes.Where(x => x.GroupId == group.Id)
      .OrderByDescending(x => x.Version).Select(x =>
      {
        var matches = matchCounts.GetValueOrDefault(x.Id);
        var components = componentCounts.GetValueOrDefault(x.Id);
        var packs = packCounts.GetValueOrDefault(x.Id);
        var advancedSchedules = advancedScheduleCounts.GetValueOrDefault(x.Id);
        var advancedExecutions = advancedExecutionCounts.GetValueOrDefault(x.Id);
        var advancedRequired = AdvancedConsolidationMethods.All.Contains(x.Method);
        var advancedStatus = !advancedRequired ? "NOT_APPLICABLE" : advancedSchedules is null ? "REQUIRED" :
          advancedSchedules.Approved != advancedSchedules.Count || advancedSchedules.Count == 0 ? "REVIEW_REQUIRED" :
          advancedExecutions is null ? "EXECUTION_REQUIRED" : advancedExecutions.Approved > 0 ? "APPROVED" : "EXECUTION_REVIEW_REQUIRED";
        var journals = journalCounts.GetValueOrDefault(x.Id);
        return new ConsolidationScopeRow
        {
          Id = x.Id,
          GroupName = group.Name,
          Version = x.Version,
          Method = x.Method,
          Currency = x.ReportingCurrency,
          OwnershipEdges = ownershipCounts.GetValueOrDefault(x.Id),
          GroupedMatches = matches?.Grouped ?? 0,
          OutsideReviews = matches?.Outside ?? 0,
          OpenMatchCount = matches?.Open ?? 0,
          ComponentCount = components?.Count ?? 0,
          ApprovedComponentCount = components?.Approved ?? 0,
          ExternalPackCount = packs?.Count ?? 0,
          ApprovedExternalPackCount = packs?.Approved ?? 0,
          AdvancedScheduleCount = advancedSchedules?.Count ?? 0,
          ApprovedAdvancedScheduleCount = advancedSchedules?.Approved ?? 0,
          AdvancedExecutionCount = advancedExecutions?.Count ?? 0,
          ApprovedAdvancedExecutionCount = advancedExecutions?.Approved ?? 0,
          IsAdvanced = advancedRequired,
          AdvancedScheduleStatus = advancedStatus,
          AdvancedScheduleGuidance = advancedRequired
            ? "Readiness requires an approved current/comparative statement execution and separate reviewer approval; local evidence does not prove methodology or live acceptance."
            : "The selected bounded profile does not require an advanced-method schedule.",
          RateSetStatus = x.ExchangeRateSetVersionId is { } rateSetId ? rateSets.GetValueOrDefault(rateSetId, "MISSING") : "SAME_CURRENCY_PROFILE",
          TranslationPolicyStatus = x.TranslationPolicyVersionId is { } policyId ? translationPolicies.GetValueOrDefault(policyId, "MISSING") : "NOT_CONFIGURED",
          EliminationJournalCount = journals?.Count ?? 0,
          ApprovedEliminationJournalCount = journals?.Approved ?? 0,
          Status = x.Status,
          RunStatus = runStatuses.GetValueOrDefault(x.Id, "NO_RUN"),
          Report = reports.GetValueOrDefault(x.Id)
        };
      }).ToArray())).ToList();
    return CommandResult<IReadOnlyList<ConsolidationGroupRow>>.Ok(result);
  }
}
