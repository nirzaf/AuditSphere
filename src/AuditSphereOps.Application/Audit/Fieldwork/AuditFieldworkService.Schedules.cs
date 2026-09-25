using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditFieldworkService
{
  public static async Task<CommandResult<ScheduleValue>> CreateScheduleAsync(
    IClientAccountingDbContext db, ActorContext actor, CreateScheduleRequest request, CancellationToken ct = default)
  {
    if (request.Rows is null || request.Rows.Count == 0 || string.IsNullOrWhiteSpace(request.ScheduleType) ||
        string.IsNullOrWhiteSpace(request.EntityIdentifier) || string.IsNullOrWhiteSpace(request.SourceReceiptReference) ||
        !IsCurrency(request.Currency) || !IsHash(request.SourceHash) || string.IsNullOrWhiteSpace(request.SignConvention))
      return Invalid<ScheduleValue>("A schedule requires a versioned source, currency, sign convention and rows.");
    if (request.Rows.Any(x => x.SourceLineNumber < 1 || string.IsNullOrWhiteSpace(x.StableRowId) ||
        string.IsNullOrWhiteSpace(x.AccountCode) || string.IsNullOrWhiteSpace(x.Description) ||
        !IsCurrency(x.Currency) || !JsonObject(x.OriginalValuesJson) ||
        !string.Equals(x.Currency, request.Currency, StringComparison.OrdinalIgnoreCase)))
      return Invalid<ScheduleValue>("Schedule rows must have stable identities, signed values, one currency and an object snapshot.");
    if (request.Rows.Select(x => x.StableRowId).Distinct(StringComparer.Ordinal).Count() != request.Rows.Count)
      return Invalid<ScheduleValue>("Duplicate stable source-row identities are not accepted.");
    var scheduleType = request.ScheduleType.Trim().ToUpperInvariant();
    if (scheduleType == "BANK_LEDGER" && request.SourceImportBatchId is null)
      return CommandResult<ScheduleValue>.Fail(ErrorCodes.GateBlocked,
        "A bank-ledger schedule must reference a sealed GL import batch.");

    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ScheduleValue>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);

    var sourceHash = request.SourceHash.ToLowerInvariant();
    var resolvedGlControlTotal = request.GlControlTotal;
    if (request.SourceImportBatchId is { } sourceImportBatchId)
    {
      var sourceBatch = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == sourceImportBatchId && x.FirmId == actor.FirmId && x.ClientId == auth.ClientId &&
        x.EngagementId == request.EngagementId && x.SourceKind == "GL" && x.Status == "SEALED" &&
        x.LegalEntityKey == request.EntityIdentifier.Trim() &&
        x.Currency == request.Currency.Trim().ToUpperInvariant() &&
        (x.RawFileSha256Hex == sourceHash || x.NormalizedDatasetDigest == sourceHash), ct);
      if (sourceBatch is null)
        return CommandResult<ScheduleValue>.Fail(ErrorCodes.GenerationStale,
          "The referenced GL import batch is not a sealed, scoped and hash-matching source.");

      var accountCodes = request.Rows.Select(x => x.AccountCode.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();
      var sourceLines = await db.GeneralLedgerLines.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == request.EngagementId &&
        x.ImportBatchId == sourceBatch.Id && accountCodes.Contains(x.AccountCode.ToUpper())).ToListAsync(ct);
      if (sourceLines.Count == 0 || accountCodes.Any(code => sourceLines.All(x => x.AccountCode.Trim().ToUpperInvariant() != code)))
        return CommandResult<ScheduleValue>.Fail(ErrorCodes.GenerationStale,
          "The referenced GL import batch has no complete source coverage for the schedule accounts.");
      resolvedGlControlTotal = MoneyPolicy.Normalize(sourceLines.Sum(x => x.Debit - x.Credit));
      if (request.GlControlTotal != resolvedGlControlTotal)
        return CommandResult<ScheduleValue>.Fail(ErrorCodes.ManifestMismatch,
          "The supplied GL control total does not match the persisted source batch.");
    }
    var existing = await db.AuditSchedules.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId &&
      x.SourceReceiptReference == request.SourceReceiptReference.Trim() && x.SourceHash == sourceHash, ct);
    if (existing is not null)
    {
      await tx.CommitAsync(ct);
      return CommandResult<ScheduleValue>.Ok(ToScheduleValue(existing));
    }

    var now = DateTimeOffset.UtcNow;
    var total = request.Rows.Sum(x => x.SignedAmount);
    var schedule = new AuditSchedule
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId,
      ScheduleType = request.ScheduleType.Trim(), EntityIdentifier = request.EntityIdentifier.Trim(),
      SourceReceiptReference = request.SourceReceiptReference.Trim(), AsOfDate = request.AsOfDate,
      PeriodStart = request.PeriodStart, PeriodEnd = request.PeriodEnd, Currency = request.Currency.ToUpperInvariant(),
      SignConvention = request.SignConvention.Trim(), SourceHash = sourceHash, SourceImportBatchId = request.SourceImportBatchId,
      RowCount = request.Rows.Count, SignedControlTotal = total, GlControlTotal = resolvedGlControlTotal,
      Residual = MoneyPolicy.Normalize(total - resolvedGlControlTotal),
      InputGeneration = await CurrentGenerationAsync(db, auth.ClientId, actor.FirmId, ct),
      Status = total == resolvedGlControlTotal ? AuditScheduleStatuses.Reconciled : AuditScheduleStatuses.Unreconciled,
      CreatedByUserId = actor.UserId, CreatedAt = now
    };
    db.AuditSchedules.Add(schedule);
    db.AuditScheduleRows.AddRange(request.Rows.Select(row => new AuditScheduleRow
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId,
      ScheduleId = schedule.Id, StableRowId = row.StableRowId.Trim(), SourceLineNumber = row.SourceLineNumber,
      AccountCode = row.AccountCode.Trim(), Description = row.Description.Trim(), SignedAmount = row.SignedAmount,
      Currency = row.Currency.ToUpperInvariant(), TransactionDate = row.TransactionDate, PostingDate = row.PostingDate,
      DeliveryDate = row.DeliveryDate, ServiceDate = row.ServiceDate, OriginalValuesJson = row.OriginalValuesJson.Trim(), CreatedAt = now
    }));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ScheduleValue>.Ok(ToScheduleValue(schedule));
  }

  public static async Task<CommandResult<ScheduleValue>> ReviewScheduleAsync(
    IAuditSphereDbContext db, ActorContext actor, ReviewScheduleRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.CompletenessDecision))
      return Invalid<ScheduleValue>("A completeness decision is required.");
    var scheduleSnapshot = await db.AuditSchedules.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == request.ScheduleId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, scheduleSnapshot, ReviewRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ScheduleValue>.Fail(auth.ErrorCode!, auth.Message!);
    var schedule = await db.AuditSchedules.SingleOrDefaultAsync(x => x.Id == request.ScheduleId && x.FirmId == actor.FirmId, ct);
    if (schedule is null)
      return Denied<ScheduleValue>();
    if (request.Approve && schedule.CreatedByUserId == actor.UserId)
      return CommandResult<ScheduleValue>.Fail(ErrorCodes.ScopeDenied, "The preparer cannot approve the same schedule.");
    if (request.Approve && schedule.Residual != 0)
      return CommandResult<ScheduleValue>.Fail(ErrorCodes.GateBlocked, "An unexplained signed reconciliation residual prevents approval.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    schedule.CompletenessDecision = request.CompletenessDecision.Trim();
    schedule.Status = request.Approve ? AuditScheduleStatuses.Approved :
      (schedule.Residual == 0 ? AuditScheduleStatuses.Reconciled : AuditScheduleStatuses.Unreconciled);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ScheduleValue>.Ok(ToScheduleValue(schedule));
  }

  private static ScheduleValue ToScheduleValue(AuditSchedule schedule) =>
    new(schedule.Id, schedule.Status, schedule.RowCount, schedule.SignedControlTotal, schedule.Residual);
}
