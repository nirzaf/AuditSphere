using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditScheduleSourceWorkspace
{
  public static async Task<CommandResult<AuditScheduleSourceOptions>> GetIntakeOptionsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid engagementId,
    int pbcOffset = 0, int trialBalanceOffset = 0, int limit = 25, CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty || pbcOffset is < 0 or > 1_000_000 ||
        trialBalanceOffset is < 0 or > 1_000_000 || limit is < 1 or > 100)
      return CommandResult<AuditScheduleSourceOptions>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "Choose a supported source page for this engagement.");
    var auth = await AuditFieldworkService.AuthorizeSchedulePreparationAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded) return CommandResult<AuditScheduleSourceOptions>.Fail(auth.ErrorCode!, auth.Message!);
    var clientId = auth.Value;
    var pbcRows = await db.PbcAcceptanceReceipts.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.ClientId == clientId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.AcceptedAt).ThenByDescending(x => x.Id)
      .Skip(pbcOffset).Take(limit + 1).Select(x => x.Id).ToArrayAsync(ct);
    var pbcOptions = new List<AuditSchedulePbcOption>(limit);
    foreach (var receiptId in pbcRows.Take(limit))
    {
      var option = await AuditScheduleIntakeSources.GetPbcOptionAsync(db, actor.FirmId, clientId,
        engagementId, receiptId, ct);
      // Invalid, superseded or unsupported accepted receipts remain unavailable options.
      if (option.Succeeded) pbcOptions.Add(option.Value!);
    }
    var tbRows = await db.SourceAcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.ClientId == clientId && x.EngagementId == engagementId && x.Decision == "ACCEPTED" &&
        x.SourceKind == "TB" && x.TrialBalanceDatasetId != null && x.ImportBatchId == null)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .Skip(trialBalanceOffset).Take(limit + 1).Select(x => x.Id).ToArrayAsync(ct);
    var tbOptions = new List<AuditScheduleTrialBalanceOption>(limit);
    foreach (var decisionId in tbRows.Take(limit))
    {
      var option = await AuditScheduleIntakeSources.GetTrialBalanceOptionAsync(db, actor.FirmId,
        clientId, engagementId, decisionId, ct);
      // Only the exact selected, sealed and current TB context is offered.
      if (option.Succeeded) tbOptions.Add(option.Value!);
    }
    auth = await AuditFieldworkService.AuthorizeSchedulePreparationAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded || auth.Value != clientId)
      return CommandResult<AuditScheduleSourceOptions>.Fail(auth.ErrorCode ?? ErrorCodes.ScopeDenied,
        auth.Message ?? "Access denied.");
    return CommandResult<AuditScheduleSourceOptions>.Ok(new(pbcOptions.AsReadOnly(),
      pbcOffset + Math.Min(pbcRows.Length, limit), pbcRows.Length > limit,
      tbOptions.AsReadOnly(), trialBalanceOffset + Math.Min(tbRows.Length, limit), tbRows.Length > limit));
  }

  public static async Task<CommandResult<AuditScheduleSourceHeaders>> GetSourceHeadersAsync(
    IClientAccountingDbContext db, ActorContext actor, IAuditScheduleSourceContentReader reader,
    Guid engagementId, Guid receiptId, CancellationToken ct = default)
  {
    var auth = await AuditFieldworkService.AuthorizeSchedulePreparationAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded) return CommandResult<AuditScheduleSourceHeaders>.Fail(auth.ErrorCode!, auth.Message!);
    return await AuditScheduleIntakeSources.GetHeadersAsync(db, actor, reader, auth.Value,
      engagementId, receiptId, ct);
  }

  public static async Task<CommandResult<AuditScheduleControlAccountPage>> GetControlAccountsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid engagementId, Guid decisionId,
    DateOnly sourceDate, int offset = 0, int limit = 100, CancellationToken ct = default)
  {
    var auth = await AuditFieldworkService.AuthorizeSchedulePreparationAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded)
      return CommandResult<AuditScheduleControlAccountPage>.Fail(auth.ErrorCode!, auth.Message!);
    return await AuditScheduleIntakeSources.GetControlAccountsAsync(db, actor, auth.Value,
      engagementId, decisionId, sourceDate, offset, limit, ct);
  }

  public static async Task<CommandResult<AuditScheduleReviewCandidatePage>> GetReviewCandidatesAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid engagementId,
    int offset = 0, int limit = 25, CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty || offset is < 0 or > 1_000_000 || limit is < 1 or > 100)
      return CommandResult<AuditScheduleReviewCandidatePage>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "Choose a supported source-review page for this engagement.");
    var auth = await AuditFieldworkService.AuthorizeScheduleReviewAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded) return CommandResult<AuditScheduleReviewCandidatePage>.Fail(auth.ErrorCode!, auth.Message!);
    var clientId = auth.Value;
    var query = from capture in db.AuditScheduleSourceCaptures.AsNoTracking()
      join schedule in db.AuditSchedules.AsNoTracking()
        on new { capture.FirmId, capture.ClientId, capture.EngagementId, Id = capture.ScheduleId }
        equals new { schedule.FirmId, schedule.ClientId, schedule.EngagementId, schedule.Id }
      join receipt in db.PbcAcceptanceReceipts.AsNoTracking()
        on new { capture.FirmId, capture.ClientId, capture.EngagementId, Id = capture.PbcAcceptanceReceiptId }
        equals new { receipt.FirmId, receipt.ClientId, receipt.EngagementId, receipt.Id }
      where capture.FirmId == actor.FirmId && capture.ClientId == clientId &&
        capture.EngagementId == engagementId && !db.AuditScheduleSourceReviews.AsNoTracking()
          .Any(review => review.FirmId == actor.FirmId && review.ClientId == clientId &&
            review.EngagementId == engagementId && review.ScheduleId == capture.ScheduleId)
      orderby capture.CreatedAt descending, capture.ScheduleId descending
      select new AuditScheduleReviewCandidate(capture.ScheduleId, capture.SourceDate,
        receipt.FileName, schedule.Status, capture.CreatedAt);
    var rows = await query.Skip(offset).Take(limit + 1).ToArrayAsync(ct);
    auth = await AuditFieldworkService.AuthorizeScheduleReviewAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded || auth.Value != clientId)
      return CommandResult<AuditScheduleReviewCandidatePage>.Fail(auth.ErrorCode ?? ErrorCodes.ScopeDenied,
        auth.Message ?? "Access denied.");
    return CommandResult<AuditScheduleReviewCandidatePage>.Ok(new(offset, limit,
      rows.Length > limit, rows.Take(limit).ToArray()));
  }
}
