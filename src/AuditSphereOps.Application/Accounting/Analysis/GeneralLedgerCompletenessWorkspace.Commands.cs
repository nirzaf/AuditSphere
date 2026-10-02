using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Records;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class GeneralLedgerCompletenessWorkspace
{
  internal static async Task<CommandResult> LockAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid engagementId, CancellationToken ct)
  {
    var firm = await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    var safety = await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id = {actor.FirmId} AND id = {clientId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    var engagement = await db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE firm_id = {actor.FirmId} AND id = {engagementId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    return firm is not null && safety is not null && engagement?.PracticeClientId == clientId
      ? CommandResult.Ok() : CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  }
  internal static async Task<CommandResult> MutablePeriodAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid engagementId, Guid periodId, Guid? bookId, CancellationToken ct)
  {
    // Book/period close commands also serialize on the reporting period; take it after the parent locks.
    var period = await db.ClientReportingPeriods.FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE firm_id = {actor.FirmId} AND id = {periodId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    if (period?.ClientId != clientId) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (period.Status is not (AccountingWorkflowStates.Active or AccountingWorkflowStates.Draft))
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The reporting period is closed or unavailable.");
    if (bookId is { } id && !await db.ClientReportingBooks.AnyAsync(x => x.Id == id && x.FirmId == actor.FirmId && x.ClientId == clientId && x.PeriodId == periodId && (x.Status == AccountingWorkflowStates.Active || x.Status == AccountingWorkflowStates.Draft), ct))
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The reporting book is closed or unavailable.");
    if (await db.EngagementFileFreezes.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.State == FileFreezeStates.Frozen, ct))
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The engagement file is frozen. An approved amendment is required.");
    return CommandResult.Ok();
  }

}
