using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace AuditSphereOps.Application.Accounting;

public static partial class ClientAccountingService
{
  // Parent safety -> actor -> period -> restatement -> ordered packages. No workspace-owned transaction.
  private static async Task LockMaintenancePeriodAsync(IClientAccountingDbContext db, ActorContext a, Guid clientId, Guid periodId, CancellationToken ct)
  {
    await db.ClientReportingPeriods.FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE firm_id={a.FirmId} AND client_id={clientId} AND id={periodId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
  }
  private static async Task LockMaintenanceRestatementAsync(IClientAccountingDbContext db, ActorContext a, Guid clientId, Guid id, CancellationToken ct)
  {
    await db.ClientPeriodRestatements.FromSqlInterpolated($"SELECT * FROM client_period_restatements WHERE firm_id={a.FirmId} AND client_id={clientId} AND id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
  }
  private static async Task LockMaintenancePackagesAsync(IClientAccountingDbContext db, ActorContext a, Guid clientId, IEnumerable<Guid> ids, CancellationToken ct)
  {
    foreach (var id in ids.Distinct().OrderBy(x => x))
      await db.FinancialPackages.FromSqlInterpolated($"SELECT * FROM financial_packages WHERE firm_id={a.FirmId} AND client_id={clientId} AND id={id} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
  }

  public static async Task<CommandResult<PeriodMaintenanceReceiptView>> ExecuteReviewedPeriodMaintenanceAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, PeriodMaintenanceRequest? request, CancellationToken ct = default)
  {
    static CommandResult<PeriodMaintenanceReceiptView> Fail(string code, string message) => CommandResult<PeriodMaintenanceReceiptView>.Fail(code, message);
    if (!PeriodMaintenanceWorkspace.Valid(request, clientId) || request is not { Reviewed: true } || !PeriodMaintenanceWorkspace.Hash(request.ExpectedRequestHash))
      return Fail(ErrorCodes.Accounting.ReconciliationRejected, "Preview and explicitly confirm the exact maintenance intent and hash.");
    var r = PeriodMaintenanceWorkspace.Normalize(request);
    var hash = PeriodMaintenanceWorkspace.RequestHash(actor, clientId, r);
    var auth = await PeriodMaintenanceWorkspace.AuthorizeAsync(db, actor, clientId, r.Action, ct);
    if (!auth.Succeeded) return Fail(auth.ErrorCode!, auth.Message!);
    // Read committed plus row locks lets a waiting identical submission observe the committed receipt.
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    try
    {
      var firm = await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
      var client = await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={actor.FirmId} AND id={clientId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
      var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
      if (firm is null || client is null || user is null) return Fail(ErrorCodes.ScopeDenied, "Access denied.");
      auth = await PeriodMaintenanceWorkspace.AuthorizeAsync(db, actor, clientId, r.Action, ct);
      if (!auth.Succeeded) return Fail(auth.ErrorCode!, auth.Message!);
      await PeriodMaintenanceWorkspace.RequestBarrierAsync(db, actor, r.RequestId, true, ct);
      var existing = await db.PeriodMaintenanceReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == r.RequestId, ct);
      if (existing is not null)
      {
        if (existing.ActorEpoch != actor.SessionEpoch) return Fail(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
        if (existing.ClientId != clientId || existing.Action != r.Action || existing.RequestHash != hash || existing.RequestHash != r.ExpectedRequestHash)
          return Fail(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
        auth = await PeriodMaintenanceWorkspace.AuthorizeAsync(db, actor, clientId, existing.Action, ct);
        if (!auth.Succeeded) return Fail(auth.ErrorCode!, auth.Message!);
        await tx.CommitAsync(ct);
        return CommandResult<PeriodMaintenanceReceiptView>.Ok(PeriodMaintenanceWorkspace.View(existing));
      }
      if (hash != r.ExpectedRequestHash) return Fail(ErrorCodes.IdempotencyConflict, "The submitted intent differs from the exact preview hash.");
      var periodId = r.RollForward?.PriorPeriodId ?? r.Restatement?.PeriodId;
      ClientPeriodRestatement? restatement = null;
      if (r.RestatementId is { } rid)
      {
        restatement = await db.ClientPeriodRestatements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == rid, ct);
        if (restatement is null) return Fail(ErrorCodes.ScopeDenied, "Access denied.");
        periodId = restatement.PeriodId;
      }
      await LockMaintenancePeriodAsync(db, actor, clientId, periodId!.Value, ct);
      if (restatement is not null) await LockMaintenanceRestatementAsync(db, actor, clientId, restatement.Id, ct);
      // Lock all packages represented in the basis, in the same order used by legacy restatement callers.
      var p = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == periodId, ct);
      if (p is null) return Fail(ErrorCodes.ScopeDenied, "Access denied.");
      var start = p.StartDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
      var end = p.EndDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
      var packageIds = await db.FinancialPackages.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.PeriodStart == start && x.PeriodEnd == end).Select(x => x.Id).ToListAsync(ct);
      if (r.Restatement is { } create) packageIds.AddRange([create.OriginalPackageId, create.RevisedPackageId]);
      if (restatement is not null) packageIds.AddRange([restatement.OriginalPackageId, restatement.RevisedPackageId]);
      if (r.RollForward?.SourcePackageId is { } source) packageIds.Add(source);
      await LockMaintenancePackagesAsync(db, actor, clientId, packageIds, ct);
      if (r.Action == PeriodMaintenanceActions.RollForward)
        await db.ClientReportingBooks.FromSqlInterpolated($"SELECT * FROM client_reporting_books WHERE firm_id={actor.FirmId} AND client_id={clientId} AND period_id={p.Id} ORDER BY id FOR SHARE").AsNoTracking().ToListAsync(ct);
      var preview = await PeriodMaintenanceWorkspace.PreviewAsync(db, actor, clientId, r, ct);
      if (!preview.Succeeded) return Fail(preview.ErrorCode!, preview.Message!);
      if (!preview.Value!.State.CanProceed) return Fail(ErrorCodes.GateBlocked, preview.Value.State.Blockers.First());
      Guid resultPeriodId = p.Id;
      Guid? resultRestatementId = null;
      string status;
      string snapshot;
      if (r.RollForward is { } forward)
      {
        var result = await RollForwardPeriodCoreAsync(db, actor, forward, ct);
        if (!result.Succeeded) return Fail(result.ErrorCode!, result.Message!);
        resultPeriodId = result.Value;
        var period = await db.ClientReportingPeriods.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == resultPeriodId, ct);
        var books = await db.ClientReportingBooks.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.PeriodId == resultPeriodId).OrderBy(x => x.Id).ToListAsync(ct);
        var bridge = await db.OpeningBalanceBridges.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.CurrentPeriodId == resultPeriodId, ct);
        status = period.Status; snapshot = JsonSerializer.Serialize(new { Period = period, Books = books, Bridge = bridge });
      }
      else
      {
        if (r.Restatement is { } fields)
        {
          var result = await CreatePeriodRestatementCoreAsync(db, actor, fields, ct);
          if (!result.Succeeded) return Fail(result.ErrorCode!, result.Message!);
          resultRestatementId = result.Value;
        }
        else
        {
          resultRestatementId = r.RestatementId;
          var result = await ApprovePeriodRestatementCoreAsync(db, actor, resultRestatementId!.Value, ct);
          if (!result.Succeeded) return Fail(result.ErrorCode!, result.Message!);
        }
        var saved = await db.ClientPeriodRestatements.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == resultRestatementId, ct);
        status = saved.Status; snapshot = JsonSerializer.Serialize(saved);
      }
      var receipt = new PeriodMaintenanceReceipt { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
        ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch, RequestId = r.RequestId, RequestHash = hash, Action = r.Action,
        ReviewBasis = r.ReviewBasis, PeriodId = p.Id, ResultPeriodId = r.Action == PeriodMaintenanceActions.RollForward ? resultPeriodId : null,
        RestatementId = resultRestatementId, InputJson = PeriodMaintenanceWorkspace.Input(r), BeforeJson = preview.Value.State.StateJson,
        ResultJson = JsonSerializer.Serialize(new PeriodMaintenanceResult(resultPeriodId, resultRestatementId, status, snapshot)), CreatedAt = DateTimeOffset.UtcNow };
      db.PeriodMaintenanceReceipts.Add(receipt);
      await db.SaveChangesAsync(ct);
      auth = await PeriodMaintenanceWorkspace.AuthorizeAsync(db, actor, clientId, r.Action, ct);
      if (!auth.Succeeded) return Fail(auth.ErrorCode!, auth.Message!);
      // PostgreSQL stores timestamps at microsecond precision. Publish the stored receipt,
      // so the first reply and a later replay are identical on every host clock.
      var persisted = await db.PeriodMaintenanceReceipts.AsNoTracking().SingleAsync(x => x.Id == receipt.Id && x.FirmId == actor.FirmId, ct);
      await tx.CommitAsync(ct);
      return CommandResult<PeriodMaintenanceReceiptView>.Ok(PeriodMaintenanceWorkspace.View(persisted));
    }
    catch (Exception ex) when (MaintenanceConflict(ex))
    {
      await tx.RollbackAsync(ct);
      // The request/preview remain valid recovery references. The caller must use a fresh context for retry.
      return Fail(ErrorCodes.IdempotencyConflict, "Concurrent maintenance changed the result. Look up the exact retained request before retrying with a fresh context.");
    }
  }
  private static bool MaintenanceConflict(Exception ex)
  {
    var cause = ex.GetBaseException();
    return cause is DbException && cause.GetType().GetProperty("SqlState")?.GetValue(cause) is "40001" or "40P01" or "23505";
  }

}
