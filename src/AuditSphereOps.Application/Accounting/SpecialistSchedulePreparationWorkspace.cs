using System.Data;
using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record SpecialistPreparationPeriod(Guid Id, string Code, DateOnly StartDate, DateOnly EndDate,
  string Currency, string Status, long Revision, bool CanPrepare, IReadOnlyList<string> Blockers);
public sealed record SpecialistScheduleOption(Guid Id, Guid PeriodId, string PeriodCode, string Area, long Revision,
  Guid? SupersedesScheduleId, string MethodologyVersion, string Status, string CalculatedAmount, DateTimeOffset CreatedAt);
public sealed record SpecialistPreparationContext(Guid EngagementId, Guid ClientId, string ClientName,
  string EngagementName, IReadOnlyList<SpecialistPreparationPeriod> Periods, IReadOnlyList<SpecialistScheduleOption> Schedules);
public sealed record SpecialistPreparationState(Guid EngagementId, Guid ClientId, Guid PeriodId, string PeriodCode,
  string Currency, long InputGeneration, string Area, Guid? CurrentScheduleId, long NextRevision,
  string ReviewBasis, bool CanPrepare, IReadOnlyList<string> Blockers);
public sealed record SpecialistPreparationRequest(Guid RequestId, string ReviewBasis, SpecialistScheduleRequest Fields,
  Guid? SupersedesScheduleId, string Reason, bool Reviewed = false);
public sealed record SpecialistPreparationPreview(Guid EngagementId, Guid ClientId, Guid PeriodId, string Area,
  Guid RequestId, string ReviewBasis, string RequestHash, Guid? SupersedesScheduleId, long Revision,
  string Currency, string CalculatedAmount, string ManagementAmount, string Difference, bool CanProceed,
  IReadOnlyList<string> Blockers);
public sealed record SpecialistPreparationResult(Guid ScheduleId, string Area, long Revision, Guid? SupersedesScheduleId,
  string Status, string CalculatedAmount, string ManagementAmount, string Difference, Guid ActorId, DateTimeOffset CreatedAt);
public sealed record SpecialistPreparationReceipt(Guid Id, Guid RequestId, string RequestHash, Guid ScheduleId,
  Guid ActorId, string Reason, DateTimeOffset CreatedAt, SpecialistPreparationResult Result);
public sealed record SpecialistPreparationLookup(bool Found, SpecialistPreparationReceipt? Receipt);

/// <summary>Reviewed append-only specialist schedule revisions with actor-owned idempotent recovery.</summary>
public static class SpecialistSchedulePreparationWorkspace
{
  private static readonly HashSet<string> Areas = ["ASSETS", "PAYROLL", "LOANS", "EQUITY", "RELATED_PARTIES", "TAX", "FORECAST"];
  private const int Limit = 100;
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static Task<CommandResult> Authorize(IClientAccountingDbContext db, ActorContext actor, Guid clientId,
    Guid engagementId, CancellationToken ct) => AccountingPreparationAuthorization.AuthorizeAsync(db, actor,
      clientId, engagementId, ct);
  private static string Exact(decimal value) => value.ToString("0.000000", CultureInfo.InvariantCulture);
  private static bool Hash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
  private static string RequestHash(ActorContext actor, Guid engagementId, SpecialistPreparationRequest request) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { actor.FirmId, actor.UserId, actor.SessionEpoch,
      engagementId, request.RequestId, request.ReviewBasis, request.Fields, request.SupersedesScheduleId,
      Reason = request.Reason.Trim() }));

  public static async Task<CommandResult<SpecialistPreparationContext>> ContextAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty) return Fail<SpecialistPreparationContext>(ErrorCodes.ScopeDenied, "This specialist context is unavailable.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return Fail<SpecialistPreparationContext>(ErrorCodes.ScopeDenied, "This specialist context is unavailable.");
    var auth = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    if (!auth.Succeeded) return Fail<SpecialistPreparationContext>(ErrorCodes.ScopeDenied, "This specialist context is unavailable.");
    var client = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagement.PracticeClientId, ct);
    var periods = await db.ClientReportingPeriods.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId)
      .OrderByDescending(x => x.EndDate).ThenByDescending(x => x.Id).Take(Limit).ToListAsync(ct);
    var frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagement.Id && x.State == "FROZEN", ct);
    var periodOptions = periods.Select(x =>
    {
      var blockers = Blockers(engagement.Status, x.Status, frozen);
      return new SpecialistPreparationPeriod(x.Id, x.PeriodCode, x.StartDate, x.EndDate, x.Currency, x.Status, x.Revision,
        blockers.Count == 0, blockers);
    }).ToArray();
    var periodMap = periods.ToDictionary(x => x.Id, x => x.PeriodCode);
    var schedules = await db.SpecialistAccountingSchedules.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.ClientId == engagement.PracticeClientId && x.EngagementId == engagement.Id && periodMap.Keys.Contains(x.PeriodId))
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(Limit).ToListAsync(ct);
    var final = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    if (!final.Succeeded || client is null) return Fail<SpecialistPreparationContext>(ErrorCodes.ScopeDenied, "This specialist context is unavailable.");
    return CommandResult<SpecialistPreparationContext>.Ok(new(engagement.Id, engagement.PracticeClientId,
      client.CommercialName ?? client.LegalName, engagement.ServiceRoute + " " + engagement.PeriodEnd, periodOptions,
      schedules.Select(x => new SpecialistScheduleOption(x.Id, x.PeriodId, periodMap.GetValueOrDefault(x.PeriodId) ?? "Unavailable",
        x.Area, x.Revision, x.SupersedesScheduleId, x.MethodologyVersion, x.Status, Exact(x.CalculatedAmount), x.CreatedAt)).ToArray()));
  }

  public static async Task<CommandResult<SpecialistPreparationState>> StateAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid periodId, string? area, CancellationToken ct = default)
  {
    var normalizedArea = area?.Trim().ToUpperInvariant() ?? "";
    if (periodId == Guid.Empty || !Areas.Contains(normalizedArea))
      return Fail<SpecialistPreparationState>(ErrorCodes.ScopeDenied, "This specialist schedule is unavailable.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return Fail<SpecialistPreparationState>(ErrorCodes.ScopeDenied, "This specialist schedule is unavailable.");
    var auth = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    if (!auth.Succeeded) return Fail<SpecialistPreparationState>(ErrorCodes.ScopeDenied, "This specialist schedule is unavailable.");
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == engagement.PracticeClientId && x.Id == periodId, ct);
    var safety = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagement.PracticeClientId, ct);
    var frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagement.Id && x.State == "FROZEN", ct);
    if (period is null || safety is null) return Fail<SpecialistPreparationState>(ErrorCodes.ScopeDenied, "This specialist schedule is unavailable.");
    var latest = await db.SpecialistAccountingSchedules.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId &&
      x.EngagementId == engagement.Id && x.PeriodId == period.Id && x.Area == normalizedArea)
      .OrderByDescending(x => x.Revision).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    var blockers = Blockers(engagement.Status, period.Status, frozen);
    var basis = Hashing.Sha256Hex(JsonSerializer.Serialize(new { actor.FirmId, actor.UserId, actor.SessionEpoch,
      Engagement = new { engagement.Id, engagement.PracticeClientId, engagement.Status },
      Period = new { period.Id, period.PeriodCode, period.StartDate, period.EndDate, period.Currency, period.Status, period.Revision },
      safety.InputGeneration, frozen, normalizedArea,
      Current = latest is null ? null : new { latest.Id, latest.Revision, latest.Status, latest.CreatedAt, latest.ReviewedAt } }));
    var final = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    return final.Succeeded ? CommandResult<SpecialistPreparationState>.Ok(new(engagement.Id, engagement.PracticeClientId,
      period.Id, period.PeriodCode, period.Currency, safety.InputGeneration, normalizedArea, latest?.Id,
      (latest?.Revision ?? 0) + 1, basis, blockers.Count == 0, blockers)) :
      Fail<SpecialistPreparationState>(ErrorCodes.ScopeDenied, "This specialist schedule is unavailable.");
  }

  public static async Task<CommandResult<SpecialistPreparationPreview>> PreviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, SpecialistPreparationRequest? request, CancellationToken ct = default)
  {
    if (!ValidRequest(request)) return Fail<SpecialistPreparationPreview>(ErrorCodes.Accounting.ReconciliationRejected,
      "Provide an approved specialist profile, exact inputs, assumptions, evidence and reason.");
    var f = request!.Fields; var area = f.Area.Trim().ToUpperInvariant();
    var state = await StateAsync(db, actor, engagementId, f.PeriodId, area, ct);
    if (!state.Succeeded) return Fail<SpecialistPreparationPreview>(state.ErrorCode!, state.Message!);
    var s = state.Value!;
    if (!s.CanPrepare || request.ReviewBasis != s.ReviewBasis || request.SupersedesScheduleId != s.CurrentScheduleId ||
        f.ClientId != s.ClientId || f.EngagementId != engagementId || f.PeriodId != s.PeriodId)
      return Fail<SpecialistPreparationPreview>(ErrorCodes.StaleRevision, "The period or schedule revision changed. Refresh before review.");
    var profileError = AccountingAnalysisService.ValidateSpecialistProfile(f, area, out var calculated);
    if (profileError is not null) return Fail<SpecialistPreparationPreview>(ErrorCodes.Accounting.ReconciliationRejected, profileError);
    var current = await StateAsync(db, actor, engagementId, f.PeriodId, area, ct);
    if (!current.Succeeded || current.Value!.ReviewBasis != s.ReviewBasis)
      return Fail<SpecialistPreparationPreview>(ErrorCodes.StaleRevision, "The specialist context changed during preview.");
    var difference = MoneyPolicy.Normalize(calculated - f.ManagementAmount);
    return CommandResult<SpecialistPreparationPreview>.Ok(new(engagementId, s.ClientId, s.PeriodId, area,
      request.RequestId, s.ReviewBasis, RequestHash(actor, engagementId, request), s.CurrentScheduleId,
      s.NextRevision, s.Currency, Exact(MoneyPolicy.Normalize(calculated)), Exact(f.ManagementAmount), Exact(difference), true, []));
  }

  public static async Task<CommandResult<SpecialistPreparationLookup>> LookupAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !Hash(requestHash)) return Fail<SpecialistPreparationLookup>(ErrorCodes.ScopeDenied, "This retained request is unavailable.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return Fail<SpecialistPreparationLookup>(ErrorCodes.ScopeDenied, "This retained request is unavailable.");
    var auth = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    if (!auth.Succeeded) return Fail<SpecialistPreparationLookup>(ErrorCodes.ScopeDenied, "This retained request is unavailable.");
    var row = await db.SpecialistSchedulePreparations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (row is not null && (row.EngagementId != engagementId || row.RequestHash != requestHash))
      return Fail<SpecialistPreparationLookup>(ErrorCodes.IdempotencyConflict, "This reference belongs to another exact intent.");
    var receipt = row is null ? null : await ReceiptAsync(db, actor, row, ct);
    var final = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    return final.Succeeded ? CommandResult<SpecialistPreparationLookup>.Ok(new(row is not null, receipt)) :
      Fail<SpecialistPreparationLookup>(ErrorCodes.ScopeDenied, "This retained request is unavailable.");
  }

  public static async Task<CommandResult<SpecialistPreparationReceipt>> ExecuteAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, SpecialistPreparationRequest? request, CancellationToken ct = default)
  {
    if (!ValidRequest(request) || request is not { Reviewed: true })
      return Fail<SpecialistPreparationReceipt>(ErrorCodes.Accounting.ReconciliationRejected, "Preview and explicitly confirm the exact specialist schedule.");
    var f = request!.Fields; var area = f.Area.Trim().ToUpperInvariant();
    if (f.EngagementId != engagementId) return Fail<SpecialistPreparationReceipt>(ErrorCodes.ScopeDenied, "This specialist schedule is unavailable.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId &&
      x.PracticeClientId == f.ClientId, ct);
    if (engagement is null) return Fail<SpecialistPreparationReceipt>(ErrorCodes.ScopeDenied, "This specialist schedule is unavailable.");
    var initialAuth = await Authorize(db, actor, f.ClientId, engagementId, ct);
    if (!initialAuth.Succeeded) return Fail<SpecialistPreparationReceipt>(initialAuth.ErrorCode!, initialAuth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    var locked = await GeneralLedgerCompletenessWorkspace.LockAsync(db, actor, f.ClientId, engagementId, ct);
    if (!locked.Succeeded) return Fail<SpecialistPreparationReceipt>(locked.ErrorCode!, locked.Message!);
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR UPDATE").AsNoTracking().SingleAsync(ct);
    var hash = RequestHash(actor, engagementId, request);
    var existing = await db.SpecialistSchedulePreparations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
    if (existing is not null)
    {
      if (existing.EngagementId != engagementId || existing.RequestHash != hash || existing.ReviewBasis != request.ReviewBasis)
        return Fail<SpecialistPreparationReceipt>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
      var receipt = await ReceiptAsync(db, actor, existing, ct);
      await tx.CommitAsync(ct);
      return CommandResult<SpecialistPreparationReceipt>.Ok(receipt);
    }
    var before = await StateAsync(db, actor, engagementId, f.PeriodId, area, ct);
    if (!before.Succeeded) return Fail<SpecialistPreparationReceipt>(before.ErrorCode!, before.Message!);
    var state = before.Value!;
    if (!state.CanPrepare || state.ReviewBasis != request.ReviewBasis || request.SupersedesScheduleId != state.CurrentScheduleId ||
        f.ClientId != state.ClientId)
      return Fail<SpecialistPreparationReceipt>(ErrorCodes.StaleRevision, "The reviewed specialist revision changed.");
    var mutable = await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db, actor, state.ClientId, engagementId, state.PeriodId, null, ct);
    if (!mutable.Succeeded) return Fail<SpecialistPreparationReceipt>(mutable.ErrorCode!, mutable.Message!);
    var current = await StateAsync(db, actor, engagementId, state.PeriodId, area, ct);
    if (!current.Succeeded || current.Value!.ReviewBasis != state.ReviewBasis || !current.Value.CanPrepare)
      return Fail<SpecialistPreparationReceipt>(ErrorCodes.StaleRevision, "The source generation or schedule revision changed before save.");
    var saved = await AccountingAnalysisService.RecordSpecialistScheduleAsync(db, actor, f with { Area = area }, ct);
    if (!saved.Succeeded) return Fail<SpecialistPreparationReceipt>(saved.ErrorCode!, saved.Message!);
    var schedule = await db.SpecialistAccountingSchedules.SingleAsync(x => x.FirmId == actor.FirmId && x.ClientId == state.ClientId &&
      x.EngagementId == engagementId && x.Id == saved.Value, ct);
    if (schedule.Revision != state.NextRevision || schedule.SupersedesScheduleId != state.CurrentScheduleId)
      return Fail<SpecialistPreparationReceipt>(ErrorCodes.StaleRevision, "A newer specialist revision exists. Refresh and review again.");
    var row = new SpecialistSchedulePreparation { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = state.ClientId,
      EngagementId = engagementId, ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch, RequestId = request.RequestId,
      RequestHash = hash, ReviewBasis = state.ReviewBasis, ScheduleId = schedule.Id,
      InputJson = JsonSerializer.Serialize(f), Reason = request.Reason.Trim(), CreatedAt = DateTimeOffset.UtcNow };
    db.SpecialistSchedulePreparations.Add(row);
    var finalState = await StateAsync(db, actor, engagementId, state.PeriodId, area, ct);
    // The new row is expected to change only the schedule head; the locked original basis remains the reviewed basis.
    if (!finalState.Succeeded || finalState.Value!.CurrentScheduleId != schedule.Id || finalState.Value.NextRevision != schedule.Revision + 1)
      return Fail<SpecialistPreparationReceipt>(ErrorCodes.StaleRevision, "The saved schedule could not be reconciled to its reviewed revision.");
    await db.SaveChangesAsync(ct);
    var result = ToReceipt(row, schedule);
    var finalAuth = await Authorize(db, actor, state.ClientId, engagementId, ct);
    if (!finalAuth.Succeeded) return Fail<SpecialistPreparationReceipt>(finalAuth.ErrorCode!, finalAuth.Message!);
    await tx.CommitAsync(ct);
    return CommandResult<SpecialistPreparationReceipt>.Ok(result);
  }

  private static async Task<SpecialistPreparationReceipt> ReceiptAsync(IClientAccountingDbContext db, ActorContext actor,
    SpecialistSchedulePreparation row, CancellationToken ct)
  {
    var schedule = await db.SpecialistAccountingSchedules.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == row.ClientId && x.EngagementId == row.EngagementId && x.Id == row.ScheduleId, ct);
    return ToReceipt(row, schedule);
  }

  private static SpecialistPreparationReceipt ToReceipt(SpecialistSchedulePreparation row, SpecialistAccountingSchedule s) =>
    new(row.Id, row.RequestId, row.RequestHash, s.Id, row.ActorId, row.Reason, row.CreatedAt,
      new(s.Id, s.Area, s.Revision, s.SupersedesScheduleId, s.Status, Exact(s.CalculatedAmount), Exact(s.ManagementAmount),
        Exact(s.Difference), row.ActorId, s.CreatedAt));

  private static List<string> Blockers(string engagementStatus, string periodStatus, bool frozen)
  {
    var blockers = new List<string>();
    if (engagementStatus != "Active") blockers.Add("The engagement is not active.");
    if (periodStatus is not ("ACTIVE" or "DRAFT")) blockers.Add("The reporting period is closed.");
    if (frozen) blockers.Add("The engagement file is frozen.");
    return blockers;
  }

  private static bool ValidRequest(SpecialistPreparationRequest? request)
  {
    if (request is null || request.RequestId == Guid.Empty || !Hash(request.ReviewBasis) || request.Fields is null ||
        request.Reason is not { Length: > 0 and <= 4000 } || string.IsNullOrWhiteSpace(request.Reason)) return false;
    var f = request.Fields;
    if (!Areas.Contains(f.Area?.Trim().ToUpperInvariant() ?? "") || f.ClientId == Guid.Empty || f.EngagementId == Guid.Empty || f.PeriodId == Guid.Empty ||
        f.MethodologyVersion is not { Length: > 0 and <= 100 } || string.IsNullOrWhiteSpace(f.MethodologyVersion) ||
        f.EvidenceReference is not { Length: > 0 and <= 2000 } || string.IsNullOrWhiteSpace(f.EvidenceReference) || !Hash(f.AssumptionsHash)) return false;
    var amounts = new[] { f.OpeningAmount, f.AdditionsAmount, f.DisposalsAmount, f.DepreciationAmount, f.ImpairmentAmount,
      f.InterestAmount, f.CurrentPortion, f.NonCurrentPortion, f.CapitalMovement, f.Dividends, f.TaxPaid, f.ManagementAmount }
      .Concat(new[] { f.PayrollGrossAmount, f.PayrollDeductionsAmount, f.PayrollNetAmount, f.LoanRepaymentAmount,
        f.EquityProfitOrLossAmount, f.EquityOciAmount, f.TaxBaseAmount, f.TaxRate, f.ForecastCashInputAmount, f.ForecastDebtInputAmount }
        .Where(x => x.HasValue).Select(x => x!.Value));
    const decimal maxAmount = 9_999_999_999_999.999999m;
    return amounts.All(x => x >= -maxAmount && x <= maxAmount && MoneyPolicy.Normalize(x) == x);
  }
}
