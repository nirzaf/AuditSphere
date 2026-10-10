using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record TimePeriodOption(Guid Id, Guid ClientId, string Label);
public sealed record TimeEngagementOption(Guid Id, Guid ClientId, string Label, Guid? MappingVersionId, IReadOnlyList<string> FsliCodes);
public sealed record TimeTaskOption(Guid Id, string Title, string Status, Guid? MappingVersionId, string? FsliCode);
public sealed record TimeEntryRow(Guid Id, DateOnly WorkDate, Guid TaskId, string TaskTitle, Guid UserId, int DurationMinutes, string Activity,
  string BillableClassification, string Narrative, string Status, Guid? MappingVersionId, string? FsliCode);
public sealed record PracticeTimeWorkspace(bool FirmWide, bool IsApprover, IReadOnlyList<TimePeriodOption> Periods,
  IReadOnlyList<TimeEngagementOption> Engagements, IReadOnlyList<TimeTaskOption> OpenTasks,
  IReadOnlyList<TimeEntryRow> MyEntries, IReadOnlyList<TimeEntryRow> AwaitingApproval);

/// <summary>
/// Grant-scoped time workspace: open tasks, the actor's own entries and (for approvers) others' submitted entries.
/// Engagement grants never widen to sibling engagements; approvers never see their own entries in the queue.
/// </summary>
public static class PracticeTimeWorkspaceQuery
{
  private static readonly string[] WorkRoles = ["Senior", "Staff", "Manager", "Partner", "Administrator"];
  private static readonly string[] ApprovalRoles = ["Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<PracticeTimeWorkspace>> GetAsync(IClientAccountingDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var authorization = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, RequiredRoles: WorkRoles, InternalOnly: true), ct);
    if (!authorization.Succeeded) return CommandResult<PracticeTimeWorkspace>.Fail(authorization.ErrorCode!, authorization.Message!);
    var grants = await db.RoleGrants.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.RevokedAt == null)
      .Select(x => new { x.Role, x.ClientId, x.EngagementId }).ToListAsync(ct);
    var workGrants = grants.Where(x => WorkRoles.Contains(x.Role)).ToArray();
    var approvalGrants = grants.Where(x => ApprovalRoles.Contains(x.Role)).ToArray();
    var firmWide = workGrants.Any(x => x.ClientId is null && x.EngagementId is null);
    var directClientIds = workGrants.Where(x => x.ClientId.HasValue && x.EngagementId is null).Select(x => x.ClientId!.Value).Distinct().ToArray();
    var grantEngagementIds = workGrants.Concat(approvalGrants).Where(x => x.EngagementId.HasValue).Select(x => x.EngagementId!.Value).Distinct().ToArray();
    var engagementClients = grantEngagementIds.Length == 0 ? new Dictionary<Guid, Guid>() : await db.Engagements.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && grantEngagementIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.PracticeClientId, ct);
    var grantedEngagementIds = workGrants.Where(x => x.ClientId.HasValue && x.EngagementId.HasValue &&
        engagementClients.TryGetValue(x.EngagementId.Value, out var clientId) && clientId == x.ClientId.Value)
      .Select(x => x.EngagementId!.Value).Distinct().ToArray();
    var reviewFirmWide = approvalGrants.Any(x => x.ClientId is null && x.EngagementId is null);
    var reviewClientIds = approvalGrants.Where(x => x.ClientId.HasValue && x.EngagementId is null).Select(x => x.ClientId!.Value).Distinct().ToArray();
    var reviewEngagementIds = approvalGrants.Where(x => x.ClientId.HasValue && x.EngagementId.HasValue &&
        engagementClients.TryGetValue(x.EngagementId.Value, out var clientId) && clientId == x.ClientId.Value)
      .Select(x => x.EngagementId!.Value).Distinct().ToArray();
    var isStaff = firmWide || directClientIds.Length > 0 || grantedEngagementIds.Length > 0;
    var isApprover = reviewFirmWide || reviewClientIds.Length > 0 || reviewEngagementIds.Length > 0;
    if (!isStaff) return CommandResult<PracticeTimeWorkspace>.Fail(ErrorCodes.ScopeDenied, "Time recording requires a current staff assignment.");

    var scopedEngagementIds = firmWide ? [] : await db.Engagements.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && (directClientIds.Contains(x.PracticeClientId) || grantedEngagementIds.Contains(x.Id))).Select(x => x.Id).ToArrayAsync(ct);
    var scopedEngagements = await db.Engagements.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && (firmWide || scopedEngagementIds.Contains(x.Id)))
      .OrderBy(x => x.PracticeClientId).ThenBy(x => x.PeriodEnd).Take(5000)
      .Select(x => new { x.Id, x.PracticeClientId, x.ServiceRoute, x.PeriodStart, x.PeriodEnd }).ToListAsync(ct);
    var scopedEngagementClients = scopedEngagements.ToDictionary(x => x.Id, x => x.PracticeClientId);
    var scopedClientIds = directClientIds.Concat(scopedEngagementClients.Values).Distinct().ToArray();
    // Reporting periods are client-wide resources. An engagement-only grant may
    // expose that engagement, but must not widen to its client's period register.
    var periods = await db.ClientReportingPeriods.AsNoTracking().Where(x => x.FirmId == actor.FirmId && (firmWide || directClientIds.Contains(x.ClientId)))
      .OrderByDescending(x => x.EndDate).ThenBy(x => x.PeriodCode).ToListAsync(ct);
    var periodClientIds = periods.Select(p => p.ClientId).Concat(scopedClientIds).Distinct().ToArray();
    var clientNames = await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == actor.FirmId && periodClientIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.LegalName, ct);
    var periodOptions = periods.Select(x => new TimePeriodOption(x.Id, x.ClientId,
      $"{clientNames.GetValueOrDefault(x.ClientId, "Scoped client")} · {x.PeriodCode} · {x.Currency}")).ToList();

    var optionEngagementIds = scopedEngagements.Select(x => x.Id).ToArray();
    var approvedMappings = optionEngagementIds.Length == 0 ? [] : await db.MappingVersions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && optionEngagementIds.Contains(x.EngagementId))
      .Where(x => x.Status == AccountingPackageStates.MappingApproved)
      .OrderByDescending(x => x.ApprovedAt).ThenByDescending(x => x.Version).ToListAsync(ct);
    var currentMappings = approvedMappings.GroupBy(x => x.EngagementId).ToDictionary(x => x.Key, x => x.First());
    var currentMappingIds = currentMappings.Values.Select(x => x.Id).ToArray();
    var mappedDestinations = currentMappingIds.Length == 0 ? [] : await db.MappingAllocations.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && currentMappingIds.Contains(x.MappingVersionId))
      .Select(x => new { x.EngagementId, x.MappingVersionId, x.DestinationCode }).Distinct().ToListAsync(ct);
    var engagementOptions = scopedEngagements.Select(x =>
    {
      currentMappings.TryGetValue(x.Id, out var mapping);
      var codes = mapping is null ? Array.Empty<string>() : mappedDestinations
        .Where(y => y.EngagementId == x.Id && y.MappingVersionId == mapping.Id)
        .Select(y => y.DestinationCode).Distinct(StringComparer.Ordinal).OrderBy(y => y, StringComparer.Ordinal).ToArray();
      var period = $"{x.PeriodStart}–{x.PeriodEnd}";
      var label = $"{clientNames.GetValueOrDefault(x.PracticeClientId, "Scoped client")} · {x.ServiceRoute} · {period}";
      return new TimeEngagementOption(x.Id, x.PracticeClientId, label, mapping?.Id, codes);
    }).ToArray();
    var tasks = await db.WorkTasks.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      (firmWide || (x.EngagementId.HasValue && scopedEngagementIds.Contains(x.EngagementId.Value)) ||
        (!x.EngagementId.HasValue && x.ClientId.HasValue && directClientIds.Contains(x.ClientId.Value)))).OrderBy(x => x.Title).ToListAsync(ct);
    var visibleTasks = tasks.Where(x => x.EngagementId is null ||
      (x.ClientId.HasValue && scopedEngagementClients.TryGetValue(x.EngagementId.Value, out var clientId) && clientId == x.ClientId.Value)).ToList();
    var taskById = visibleTasks.ToDictionary(x => x.Id);
    var openTasks = visibleTasks.Where(x => x.Status != PracticeTimeStates.TaskCancelled && x.Status != PracticeTimeStates.TaskCompleted)
      .Select(x => new TimeTaskOption(x.Id, x.Title, x.Status, x.MappingVersionId, x.FsliCode)).ToList();

    TimeEntryRow Row(TimeEntry x) => new(x.Id, x.WorkDate, x.TaskId, taskById[x.TaskId].Title, x.UserId, x.DurationMinutes, x.Activity,
      x.BillableClassification, x.Narrative, x.Status, x.MappingVersionId, x.FsliCode);
    var myEntries = (await db.TimeEntries.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId &&
        (firmWide || (x.EngagementId.HasValue && scopedEngagementIds.Contains(x.EngagementId.Value)) ||
          (!x.EngagementId.HasValue && x.ClientId.HasValue && directClientIds.Contains(x.ClientId.Value))))
      .OrderByDescending(x => x.WorkDate).ThenByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct))
      .Where(x => taskById.TryGetValue(x.TaskId, out var task) && task.ClientId == x.ClientId && task.EngagementId == x.EngagementId).Select(Row).ToList();

    var awaiting = new List<TimeEntryRow>();
    if (isApprover)
    {
      var reviewScopedEngagementIds = reviewFirmWide ? [] : await db.Engagements.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && (reviewClientIds.Contains(x.PracticeClientId) || reviewEngagementIds.Contains(x.Id))).Select(x => x.Id).ToArrayAsync(ct);
      awaiting = (await db.TimeEntries.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
          (reviewFirmWide || (x.EngagementId.HasValue && reviewScopedEngagementIds.Contains(x.EngagementId.Value)) ||
            (!x.EngagementId.HasValue && x.ClientId.HasValue && reviewClientIds.Contains(x.ClientId.Value))) &&
          x.Status == PracticeTimeStates.TimeSubmitted && x.UserId != actor.UserId).OrderBy(x => x.WorkDate).Take(500).ToListAsync(ct))
        .Where(x => taskById.TryGetValue(x.TaskId, out var task) && task.ClientId == x.ClientId && task.EngagementId == x.EngagementId).Select(Row).ToList();
    }
    return CommandResult<PracticeTimeWorkspace>.Ok(new(firmWide, isApprover, periodOptions, engagementOptions, openTasks, myEntries, awaiting));
  }
}
