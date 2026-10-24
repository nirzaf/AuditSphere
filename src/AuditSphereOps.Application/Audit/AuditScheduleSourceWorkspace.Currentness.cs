using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditScheduleSourceWorkspace
{
  /// <summary>Eligibility for new work only. Legacy schedules retain their existing gates;
  /// callers still enforce their own command roles. Never commits a caller-owned transaction.</summary>
  internal static async Task<CommandResult> CheckCurrentScheduleSourceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, Guid engagementId, Guid scheduleId,
    CancellationToken ct = default)
  {
    var capture = await db.AuditScheduleSourceCaptures.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ScheduleId == scheduleId, ct);
    if (capture is null) return CommandResult.Ok();
    if (capture.ClientId != clientId || capture.EngagementId != engagementId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (db is not IClientAccountingDbContext accounting)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Typed schedule verification requires the client accounting capability.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, clientId, engagementId, InternalOnly: true), ct);
    if (!auth.Succeeded) return auth;
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    try
    {
      await AuditFieldworkService.LockConfirmationScopeAsync(db, actor, engagementId, ct);
      auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new(actor.FirmId, clientId, engagementId, InternalOnly: true), ct);
      if (!auth.Succeeded) return auth;
      // Scope/role changes waited on the same prefix; do not reuse pre-wait target facts.
      capture = await db.AuditScheduleSourceCaptures.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == clientId && x.EngagementId == engagementId && x.ScheduleId == scheduleId, ct);
      if (capture is null) return CommandResult.Fail(ErrorCodes.ProtectedState, "The typed schedule capture is unavailable.");
      var retained = ReadCaptureReceipt(capture);
      if (!retained.Succeeded) return CommandResult.Fail(retained.ErrorCode!, retained.Message!);
      var intent = JsonSerializer.Deserialize<CaptureIntent>(capture.RequestJson);
      if (intent is null || !ValidFields(intent.Fields) || Hashing.Sha256Hex(capture.RequestJson) != capture.RequestHash ||
          intent.FirmId != actor.FirmId || intent.ClientId != clientId || intent.EngagementId != engagementId ||
          intent.Fields.PbcAcceptanceReceiptId != capture.PbcAcceptanceReceiptId ||
          intent.Fields.AcceptedSourceDecisionId != capture.AcceptedSourceDecisionId || intent.Fields.SourceDate != capture.SourceDate ||
          intent.ExpectedManifestHash != capture.ManifestHash || intent.ExpectedInputGeneration != capture.InputGeneration ||
          intent.ExpectedPeriodRevision != capture.PeriodRevision)
        return CommandResult.Fail(ErrorCodes.ProtectedState, "The retained schedule intent is inconsistent.");
      var locked = await LockSourceAsync(accounting, actor, clientId, engagementId, intent.Fields, ct);
      if (!locked.Succeeded) return locked;
      var current = await AuditScheduleIntakeSources.CheckRetainedSourcesCurrentAsync(accounting, actor, capture, ct);
      if (!current.Succeeded) return current;
      var schedule = await db.AuditSchedules.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == clientId && x.EngagementId == engagementId && x.Id == scheduleId, ct);
      if (schedule is null || schedule.Status != AuditScheduleStatuses.Approved ||
          schedule.InputGeneration != capture.InputGeneration || schedule.AcceptedSourceDecisionId != capture.AcceptedSourceDecisionId ||
          !await db.AuditScheduleSourceReviews.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
            x.ClientId == clientId && x.EngagementId == engagementId && x.ScheduleId == scheduleId && x.Action == "APPROVE" &&
            x.CaptureHash == capture.ManifestHash && x.ActorUserId != capture.ActorUserId &&
            x.InputGeneration == capture.InputGeneration, ct))
        return CommandResult.Fail(ErrorCodes.GenerationStale, "The typed schedule is not a current independently approved source.");
      auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new(actor.FirmId, clientId, engagementId, InternalOnly: true), ct);
      if (!auth.Succeeded) return auth;
      if (tx is not null) await tx.CommitAsync(ct);
      return CommandResult.Ok();
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
      ct.ThrowIfCancellationRequested();
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The accepted schedule source content is unavailable.");
    }
    catch (JsonException)
    {
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The retained schedule intent is unreadable.");
    }
  }
}
