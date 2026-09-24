using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

// T057: bind audit lead schedules to the accepted R2R source revision and fence them
// against source replacement. When a newer source revision is accepted for the same
// engagement, every schedule still bound to the previous decision becomes stale and
// must be re-prepared against the current accepted source.
public sealed record AuditScheduleSourceBindingValue(
  Guid ScheduleId, Guid AcceptedSourceDecisionId, string AcceptedSourceHash,
  string SourceKind, Guid? TrialBalanceDatasetId, Guid? ImportBatchId);

public sealed record StaleAuditScheduleRow(
  Guid ScheduleId, string ScheduleType, string EntityIdentifier,
  Guid BoundSourceDecisionId, string BoundSourceHash, string CurrentSourceHash, string Status);

public sealed record StaleAuditScheduleReport(
  Guid EngagementId, Guid? CurrentAcceptedSourceDecisionId, string? CurrentAcceptedSourceHash,
  IReadOnlyList<StaleAuditScheduleRow> StaleSchedules, int StaleCount);

public static class AuditScheduleSourceBindingService
{
  private static readonly string[] PlanningRoles =
    ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static readonly string[] ReadRoles =
    ["Auditor", "Reviewer", "Manager", "Partner", "Administrator", "AccountingPreparer", "AccountingReviewer"];

  /// <summary>Binds a lead schedule to an accepted R2R source revision (Module 21 source
  /// acceptance). The decision must be accepted and belong to the same client/engagement
  /// as the schedule; the accepted identity hash is captured for later staleness checks.</summary>
  public static async Task<CommandResult<AuditScheduleSourceBindingValue>> BindScheduleToAcceptedSourceAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scheduleId, Guid sourceAcceptanceDecisionId,
    CancellationToken ct = default)
  {
    if (scheduleId == Guid.Empty || sourceAcceptanceDecisionId == Guid.Empty)
      return CommandResult<AuditScheduleSourceBindingValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A schedule id and an accepted source decision id are required.");
    var schedule = await db.AuditSchedules.SingleOrDefaultAsync(x =>
      x.Id == scheduleId && x.FirmId == actor.FirmId, ct);
    if (schedule is null)
      return CommandResult<AuditScheduleSourceBindingValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, schedule.ClientId, schedule.EngagementId, PlanningRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<AuditScheduleSourceBindingValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (schedule.Status == AuditScheduleStatuses.Approved)
      return CommandResult<AuditScheduleSourceBindingValue>.Fail(ErrorCodes.ProtectedState,
        "An approved schedule is immutable; bind the source before approval or prepare a new schedule.");

    var decision = await db.SourceAcceptanceDecisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == sourceAcceptanceDecisionId && x.FirmId == actor.FirmId, ct);
    if (decision is null)
      return CommandResult<AuditScheduleSourceBindingValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (decision.Decision != "ACCEPTED")
      return CommandResult<AuditScheduleSourceBindingValue>.Fail(ErrorCodes.GateBlocked,
        "Only an accepted source revision can be bound to an audit lead schedule.");
    if (decision.ClientId != schedule.ClientId || decision.EngagementId != schedule.EngagementId)
      return CommandResult<AuditScheduleSourceBindingValue>.Fail(ErrorCodes.ScopeDenied,
        "The accepted source revision belongs to a different client or engagement.");

    schedule.AcceptedSourceDecisionId = decision.Id;
    schedule.AcceptedSourceHash = decision.SourceIdentityHash;
    await db.SaveChangesAsync(ct);

    return CommandResult<AuditScheduleSourceBindingValue>.Ok(new AuditScheduleSourceBindingValue(
      schedule.Id, decision.Id, decision.SourceIdentityHash, decision.SourceKind,
      decision.TrialBalanceDatasetId, decision.ImportBatchId));
  }

  /// <summary>Reports lead schedules whose bound accepted source is no longer the current
  /// accepted revision for the engagement. Read-only: callers decide whether to stale or
  /// re-prepare. Stale schedules must not be relied on for audit conclusions.</summary>
  public static async Task<CommandResult<StaleAuditScheduleReport>> GetSourceStalenessAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid engagementId,
    CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty)
      return CommandResult<StaleAuditScheduleReport>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "An engagement id is required.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null)
      return CommandResult<StaleAuditScheduleReport>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<StaleAuditScheduleReport>.Fail(auth.ErrorCode!, auth.Message!);

    var current = await db.SourceAcceptanceDecisions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.Decision == "ACCEPTED")
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .FirstOrDefaultAsync(ct);

    var bound = await db.AuditSchedules.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId &&
        x.AcceptedSourceDecisionId != null &&
        x.Status != AuditScheduleStatuses.Superseded)
      .Select(x => new
      {
        x.Id, x.ScheduleType, x.EntityIdentifier, x.Status,
        BoundDecisionId = x.AcceptedSourceDecisionId!.Value, x.AcceptedSourceHash,
      })
      .ToListAsync(ct);

    var stale = bound
      .Where(x => current is null || x.BoundDecisionId != current.Id ||
        !string.Equals(x.AcceptedSourceHash, current.SourceIdentityHash, StringComparison.OrdinalIgnoreCase))
      .Select(x => new StaleAuditScheduleRow(
        x.Id, x.ScheduleType, x.EntityIdentifier, x.BoundDecisionId, x.AcceptedSourceHash,
        current?.SourceIdentityHash ?? string.Empty, x.Status))
      .ToList();

    return CommandResult<StaleAuditScheduleReport>.Ok(new StaleAuditScheduleReport(
      engagementId, current?.Id, current?.SourceIdentityHash, stale, stale.Count));
  }

  /// <summary>Marks every schedule bound to a superseded accepted source as UNRECONCILED so
  /// the coverage cannot be relied on until it is re-prepared against the current source.</summary>
  public static async Task<CommandResult<int>> InvalidateStaleSchedulesAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid engagementId, string reason,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000)
      return CommandResult<int>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "An invalidation requires a reason of at most 2000 characters.");
    var report = await GetSourceStalenessAsync(db, actor, engagementId, ct);
    if (!report.Succeeded)
      return CommandResult<int>.Fail(report.ErrorCode!, report.Message!);
    var staleIds = report.Value!.StaleSchedules.Select(x => x.ScheduleId).ToArray();
    if (staleIds.Length == 0)
      return CommandResult<int>.Ok(0);

    var schedules = await db.AuditSchedules
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId &&
        staleIds.Contains(x.Id) && x.Status != AuditScheduleStatuses.Superseded)
      .ToListAsync(ct);
    foreach (var schedule in schedules)
    {
      schedule.Status = AuditScheduleStatuses.Unreconciled;
      schedule.CompletenessDecision = $"SOURCE_SUPERSEDED: {reason.Trim()}";
    }
    await db.SaveChangesAsync(ct);
    return CommandResult<int>.Ok(schedules.Count);
  }
}
