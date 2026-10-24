using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditScheduleSourceWorkspace
{
  /// <summary>Reads the original actor-owned outcome; absence never authorizes a replacement command.</summary>
  public static async Task<CommandResult<AuditScheduleSourceReceiptLookup>> GetCaptureReceiptAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid engagementId, Guid requestId,
    string expectedRequestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !Hash(expectedRequestHash)) return Invalid<AuditScheduleSourceReceiptLookup>();
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<AuditScheduleSourceReceiptLookup>.Fail(ErrorCodes.ProtectedState,
        "Receipt recovery requires its own read transaction.");
    var auth = await AuditFieldworkService.AuthorizeSchedulePreparationAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded) return Failure<Guid, AuditScheduleSourceReceiptLookup>(auth);
    var clientId = auth.Value;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    // Wait behind the original publisher's same scope fence before reading its retained outcome.
    await AuditFieldworkService.LockConfirmationScopeAsync(db, actor, engagementId, ct);
    auth = await AuditFieldworkService.AuthorizeSchedulePreparationAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded || auth.Value != clientId)
      return CommandResult<AuditScheduleSourceReceiptLookup>.Fail(auth.ErrorCode ?? ErrorCodes.ScopeDenied,
        auth.Message ?? "Access denied.");
    var capture = await db.AuditScheduleSourceCaptures.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorUserId == actor.UserId && x.ActorEpoch == actor.SessionEpoch &&
      x.RequestId == requestId, ct);
    AuditScheduleSourceReceiptView? receipt = null;
    if (capture is not null)
    {
      if (capture.ClientId != clientId || capture.EngagementId != engagementId ||
          capture.RequestHash != expectedRequestHash)
        return Conflict<AuditScheduleSourceReceiptLookup>();
      var retained = ReadCaptureReceipt(capture);
      if (!retained.Succeeded) return Failure<AuditScheduleSourceReceiptView, AuditScheduleSourceReceiptLookup>(retained);
      receipt = retained.Value;
    }
    auth = await AuditFieldworkService.AuthorizeSchedulePreparationAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded) return Failure<Guid, AuditScheduleSourceReceiptLookup>(auth);
    await tx.CommitAsync(ct);
    return CommandResult<AuditScheduleSourceReceiptLookup>.Ok(new(capture is not null, receipt));
  }
}
