using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record CurrencyRemeasurementItemRequest(
  string StableItemReference, Guid EvidenceSnapshotId, bool IsMonetary, string ForeignCurrency,
  decimal ForeignCurrencyAmount, decimal PriorFunctionalCarryingAmount, DateOnly? HistoricalRateDate);

public sealed record CurrencyRemeasurementScheduleRequest(
  Guid ClientId, Guid EngagementId, Guid PeriodId, Guid RateSetVersionId, Guid TranslationPolicyVersionId,
  DateOnly AsOfDate, IReadOnlyList<CurrencyRemeasurementItemRequest> Items);

public sealed record CurrencyRemeasurementItemView(
  string StableItemReference, Guid EvidenceSnapshotId, string EvidenceSha256, bool IsMonetary,
  string ForeignCurrency, decimal ForeignCurrencyAmount, decimal PriorFunctionalCarryingAmount,
  DateOnly RateDate, string RateType, decimal AppliedRate, decimal RemeasuredFunctionalAmount,
  decimal ForeignExchangeAdjustment, decimal RoundingAdjustment);

public sealed record CurrencyRemeasurementScheduleView(
  Guid Id, Guid ClientId, Guid EngagementId, Guid PeriodId, DateOnly AsOfDate, string FunctionalCurrency,
  string InputHash, int ItemCount, decimal TotalForeignExchangeAdjustment, string Status,
  Guid CreatedByUserId, Guid? ApprovedByUserId, IReadOnlyList<CurrencyRemeasurementItemView> Items);

public static class CurrencyRemeasurementService
{
  private static readonly string[] PreparerRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReviewerRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<Guid>> PrepareAsync(
    IClientAccountingDbContext db, ActorContext actor, CurrencyRemeasurementScheduleRequest request,
    CancellationToken ct = default)
  {
    if (request.ClientId == Guid.Empty || request.EngagementId == Guid.Empty || request.PeriodId == Guid.Empty ||
        request.RateSetVersionId == Guid.Empty || request.TranslationPolicyVersionId == Guid.Empty || request.Items.Count is < 1 or > 500 ||
        request.Items.Any(x => string.IsNullOrWhiteSpace(x.StableItemReference) || x.StableItemReference.Trim().Length > 200) ||
        request.Items.Select(x => x.StableItemReference.Trim()).Distinct(StringComparer.Ordinal).Count() != request.Items.Count)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "A remeasurement schedule needs 1–500 uniquely identified, evidenced items.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PeriodId &&
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct);
    var profile = await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct);
    var rateSet = await db.ExchangeRateSetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.RateSetVersionId &&
      x.FirmId == actor.FirmId && x.Status == AccountingWorkflowStates.Approved, ct);
    var policy = await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.TranslationPolicyVersionId &&
      x.FirmId == actor.FirmId && x.Status == AccountingWorkflowStates.Approved, ct);
    if (period is null || profile is null || rateSet is null || policy is null || request.AsOfDate != period.EndDate ||
        rateSet.EffectiveFrom is { } from && from > request.AsOfDate || rateSet.EffectiveTo is { } to && to < request.AsOfDate ||
        !string.Equals(profile.FunctionalCurrency, policy.FunctionalCurrency, StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An active functional-currency profile, approved policy/rate set and period-end date are required.");

    var prepared = await PrepareItemsAsync(db, actor.FirmId, request.ClientId, request.EngagementId,
      request.AsOfDate, profile.FunctionalCurrency, rateSet, policy, request.Items, ct);
    if (prepared is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Source snapshots, exact approved rates or item inputs are missing or inconsistent.");

    var inputHash = HashInputs(prepared, request.AsOfDate, profile.FunctionalCurrency, policy.Id);
    var existing = await db.CurrencyRemeasurementSchedules.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.EngagementId == request.EngagementId &&
      x.PeriodId == request.PeriodId && x.InputHash == inputHash, ct);
    if (existing is not null)
      return CommandResult<Guid>.Ok(existing.Id);

    var schedule = new CurrencyRemeasurementSchedule
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
      PeriodId = request.PeriodId, RateSetVersionId = rateSet.Id, TranslationPolicyVersionId = policy.Id,
      AsOfDate = request.AsOfDate, FunctionalCurrency = profile.FunctionalCurrency.Trim().ToUpperInvariant(),
      InputHash = inputHash, ItemCount = prepared.Count,
      TotalForeignExchangeAdjustment = MoneyPolicy.Normalize(prepared.Sum(x => x.Calculation.ForeignExchangeAdjustment)),
      Status = AccountingWorkflowStates.Submitted, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.CurrencyRemeasurementSchedules.Add(schedule);
    foreach (var item in prepared)
      db.CurrencyRemeasurementItems.Add(ToEntity(schedule, item));
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(schedule.Id);
  }

  public static async Task<CommandResult> ApproveAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scheduleId, CancellationToken ct = default)
  {
    var schedule = await db.CurrencyRemeasurementSchedules.SingleOrDefaultAsync(x => x.Id == scheduleId && x.FirmId == actor.FirmId, ct);
    if (schedule is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, schedule.ClientId, schedule.EngagementId, ReviewerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return auth;
    if (schedule.Status != AccountingWorkflowStates.Submitted || schedule.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Only a different authorized reviewer can approve a submitted schedule.");

    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == schedule.PeriodId &&
      x.FirmId == actor.FirmId && x.ClientId == schedule.ClientId, ct);
    var profile = await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == schedule.ClientId, ct);
    var rateSet = await db.ExchangeRateSetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == schedule.RateSetVersionId &&
      x.FirmId == actor.FirmId && x.Status == AccountingWorkflowStates.Approved, ct);
    var policy = await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == schedule.TranslationPolicyVersionId &&
      x.FirmId == actor.FirmId && x.Status == AccountingWorkflowStates.Approved, ct);
    var storedItems = await db.CurrencyRemeasurementItems.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == schedule.ClientId && x.EngagementId == schedule.EngagementId && x.ScheduleId == schedule.Id)
      .OrderBy(x => x.StableItemReference).ToListAsync(ct);
    if (period is null || profile is null || rateSet is null || policy is null || schedule.AsOfDate != period.EndDate ||
        !string.Equals(profile.FunctionalCurrency, schedule.FunctionalCurrency, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(policy.FunctionalCurrency, schedule.FunctionalCurrency, StringComparison.OrdinalIgnoreCase) ||
        storedItems.Count != schedule.ItemCount)
      return await MarkStaleAsync(db, schedule, "Period, profile, approved inputs or item count changed.", ct);

    var prepared = await RevalidateItemsAsync(db, actor.FirmId, schedule, rateSet, policy, storedItems, ct);
    if (prepared is null)
      return await MarkStaleAsync(db, schedule, "A source snapshot, approved rate observation or calculated item no longer matches.", ct);
    if (HashInputs(prepared, schedule.AsOfDate, schedule.FunctionalCurrency, policy.Id) != schedule.InputHash)
      return await MarkStaleAsync(db, schedule, "The prepared input manifest no longer matches its stored digest.", ct);
    if (MoneyPolicy.Normalize(prepared.Sum(x => x.Calculation.ForeignExchangeAdjustment)) != schedule.TotalForeignExchangeAdjustment)
      return await MarkStaleAsync(db, schedule, "The schedule total no longer matches its item calculations.", ct);

    schedule.Status = AccountingWorkflowStates.Approved;
    schedule.ApprovedByUserId = actor.UserId;
    schedule.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<CurrencyRemeasurementScheduleView>> GetAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scheduleId, CancellationToken ct = default)
  {
    var schedule = await db.CurrencyRemeasurementSchedules.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == scheduleId && x.FirmId == actor.FirmId, ct);
    if (schedule is null)
      return CommandResult<CurrencyRemeasurementScheduleView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, schedule.ClientId, schedule.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<CurrencyRemeasurementScheduleView>.Fail(auth.ErrorCode!, auth.Message!);
    var rows = await (from item in db.CurrencyRemeasurementItems.AsNoTracking()
      join snapshot in db.DocumentSnapshots.AsNoTracking() on new { item.FirmId, item.ClientId, item.EngagementId, Id = item.EvidenceSnapshotId }
        equals new { snapshot.FirmId, snapshot.ClientId, snapshot.EngagementId, snapshot.Id }
      where item.FirmId == actor.FirmId && item.ScheduleId == schedule.Id
      orderby item.StableItemReference
      select new CurrencyRemeasurementItemView(item.StableItemReference, item.EvidenceSnapshotId, snapshot.Sha256Hex,
        item.IsMonetary, item.ForeignCurrency, item.ForeignCurrencyAmount, item.PriorFunctionalCarryingAmount,
        item.RateDate, item.RateType, item.AppliedRate, item.RemeasuredFunctionalAmount,
        item.ForeignExchangeAdjustment, item.RoundingAdjustment)).ToListAsync(ct);
    return CommandResult<CurrencyRemeasurementScheduleView>.Ok(new CurrencyRemeasurementScheduleView(
      schedule.Id, schedule.ClientId, schedule.EngagementId, schedule.PeriodId, schedule.AsOfDate, schedule.FunctionalCurrency,
      schedule.InputHash, schedule.ItemCount, schedule.TotalForeignExchangeAdjustment, schedule.Status,
      schedule.CreatedByUserId, schedule.ApprovedByUserId, rows));
  }

  private static async Task<List<PreparedItem>?> PrepareItemsAsync(
    IClientAccountingDbContext db, Guid firmId, Guid clientId, Guid engagementId, DateOnly asOfDate,
    string functionalCurrency, ExchangeRateSetVersion rateSet, TranslationPolicyVersion policy,
    IReadOnlyList<CurrencyRemeasurementItemRequest> inputs, CancellationToken ct)
  {
    var output = new List<PreparedItem>(inputs.Count);
    var normalizedInputs = inputs.OrderBy(x => x.StableItemReference, StringComparer.Ordinal)
      .Select(x => (Input: x, Currency: x.ForeignCurrency.Trim().ToUpperInvariant(),
        RateDate: x.IsMonetary ? asOfDate : x.HistoricalRateDate,
        RateType: (x.IsMonetary ? policy.ClosingRateRule : policy.HistoricalRateRule).Trim().ToUpperInvariant()))
      .ToArray();
    if (normalizedInputs.Any(x => x.Currency.Length != 3 || !x.Currency.All(char.IsAsciiLetter) ||
        x.Currency == functionalCurrency.Trim().ToUpperInvariant() || !x.RateDate.HasValue || x.RateDate > asOfDate ||
        !TranslationPolicyRules.AllowsRateType(policy, x.RateType) ||
        MoneyPolicy.Normalize(x.Input.ForeignCurrencyAmount) != x.Input.ForeignCurrencyAmount ||
        MoneyPolicy.Normalize(x.Input.PriorFunctionalCarryingAmount) != x.Input.PriorFunctionalCarryingAmount))
      return null;

    var snapshotIds = normalizedInputs.Select(x => x.Input.EvidenceSnapshotId).Distinct().ToArray();
    var snapshots = await db.DocumentSnapshots.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId &&
      x.EngagementId == engagementId && snapshotIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var currencies = normalizedInputs.Select(x => x.Currency).Distinct().ToArray();
    var rateDates = normalizedInputs.Select(x => x.RateDate!.Value).Distinct().ToArray();
    var rateTypes = normalizedInputs.Select(x => x.RateType).Distinct().ToArray();
    var rates = await db.ExchangeRates.AsNoTracking().Where(x => x.FirmId == firmId && x.RateSetVersionId == rateSet.Id &&
      x.FromCurrency != functionalCurrency.Trim().ToUpperInvariant() && currencies.Contains(x.FromCurrency) &&
      x.ToCurrency == functionalCurrency.Trim().ToUpperInvariant() && rateDates.Contains(x.RateDate) &&
      rateTypes.Contains(x.RateType) && x.Direction == ExchangeRateDirections.Direct)
      .ToDictionaryAsync(x => (x.FromCurrency, x.ToCurrency, x.RateDate, x.RateType), ct);
    foreach (var row in normalizedInputs)
    {
      var input = row.Input;
      if (!snapshots.TryGetValue(input.EvidenceSnapshotId, out var snapshot) || snapshot.Sha256Hex.Length != 64 ||
          !rates.TryGetValue((row.Currency, functionalCurrency.Trim().ToUpperInvariant(), row.RateDate!.Value, row.RateType), out var rate))
        return null;
      var calculation = CurrencyRemeasurementCalculator.Remeasure(input.ForeignCurrencyAmount, row.Currency,
        functionalCurrency, input.IsMonetary, input.IsMonetary ? rate.Rate : 1m, input.IsMonetary ? 1m : rate.Rate,
        input.PriorFunctionalCarryingAmount);
      output.Add(new PreparedItem(input, snapshot.Sha256Hex.ToLowerInvariant(), rate.Id, rateSet.Id,
        row.RateDate.Value, row.RateType, rate.Rate, calculation));
    }
    return output;
  }

  private static async Task<List<PreparedItem>?> RevalidateItemsAsync(
    IClientAccountingDbContext db, Guid firmId, CurrencyRemeasurementSchedule schedule,
    ExchangeRateSetVersion rateSet, TranslationPolicyVersion policy, IReadOnlyList<CurrencyRemeasurementItem> stored,
    CancellationToken ct)
  {
    var requests = stored.Select(x => new CurrencyRemeasurementItemRequest(x.StableItemReference, x.EvidenceSnapshotId,
      x.IsMonetary, x.ForeignCurrency, x.ForeignCurrencyAmount, x.PriorFunctionalCarryingAmount,
      x.IsMonetary ? null : x.RateDate)).ToArray();
    var prepared = await PrepareItemsAsync(db, firmId, schedule.ClientId, schedule.EngagementId, schedule.AsOfDate,
      schedule.FunctionalCurrency, rateSet, policy, requests, ct);
    if (prepared is null || prepared.Count != stored.Count)
      return null;
    for (var i = 0; i < stored.Count; i++)
    {
      var item = stored[i];
      var current = prepared[i];
      if (item.ExchangeRateId != current.ExchangeRateId || item.RateSetVersionId != current.RateSetVersionId ||
          item.SourceEvidenceSha256 != current.SourceEvidenceSha256 || item.RateDate != current.RateDate ||
          item.RateType != current.RateType || item.AppliedRate != current.AppliedRate ||
          item.RemeasuredFunctionalAmount != current.Calculation.RemeasuredAmount ||
          item.ForeignExchangeAdjustment != current.Calculation.ForeignExchangeAdjustment ||
          item.RoundingAdjustment != current.Calculation.RoundingAdjustment)
        return null;
    }
    return prepared;
  }

  private static string HashInputs(IReadOnlyList<PreparedItem> items, DateOnly asOfDate, string functionalCurrency, Guid policyVersionId)
  {
    var canonical = JsonSerializer.Serialize(new
    {
      asOfDate, FunctionalCurrency = functionalCurrency.Trim().ToUpperInvariant(), policyVersionId,
      Items = items.Select(x => new
      {
        Reference = x.Request.StableItemReference.Trim(), x.Request.EvidenceSnapshotId, x.SourceEvidenceSha256,
        x.Request.IsMonetary, Currency = x.Request.ForeignCurrency.Trim().ToUpperInvariant(),
        ForeignCurrencyAmount = x.Request.ForeignCurrencyAmount.ToString("0.000000", CultureInfo.InvariantCulture),
        PriorFunctionalCarryingAmount = x.Request.PriorFunctionalCarryingAmount.ToString("0.000000", CultureInfo.InvariantCulture),
        x.RateDate, x.RateType, x.RateSetVersionId, x.ExchangeRateId,
        AppliedRate = x.AppliedRate.ToString("0.000000", CultureInfo.InvariantCulture),
        RemeasuredAmount = x.Calculation.RemeasuredAmount.ToString("0.000000", CultureInfo.InvariantCulture),
        ForeignExchangeAdjustment = x.Calculation.ForeignExchangeAdjustment.ToString("0.000000", CultureInfo.InvariantCulture),
        RoundingAdjustment = x.Calculation.RoundingAdjustment.ToString("0.000000", CultureInfo.InvariantCulture)
      })
    });
    return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
  }

  private static CurrencyRemeasurementItem ToEntity(CurrencyRemeasurementSchedule schedule, PreparedItem item) => new()
  {
    Id = Guid.CreateVersion7(), FirmId = schedule.FirmId, ClientId = schedule.ClientId, EngagementId = schedule.EngagementId,
    ScheduleId = schedule.Id, EvidenceSnapshotId = item.Request.EvidenceSnapshotId, RateSetVersionId = item.RateSetVersionId,
    ExchangeRateId = item.ExchangeRateId, StableItemReference = item.Request.StableItemReference.Trim(),
    SourceEvidenceSha256 = item.SourceEvidenceSha256, IsMonetary = item.Request.IsMonetary,
    ForeignCurrency = item.Request.ForeignCurrency.Trim().ToUpperInvariant(), ForeignCurrencyAmount = item.Request.ForeignCurrencyAmount,
    PriorFunctionalCarryingAmount = item.Request.PriorFunctionalCarryingAmount, RateDate = item.RateDate,
    RateType = item.RateType, AppliedRate = item.AppliedRate, RemeasuredFunctionalAmount = item.Calculation.RemeasuredAmount,
    ForeignExchangeAdjustment = item.Calculation.ForeignExchangeAdjustment, RoundingAdjustment = item.Calculation.RoundingAdjustment
  };

  private static async Task<CommandResult> MarkStaleAsync(
    IClientAccountingDbContext db, CurrencyRemeasurementSchedule schedule, string reason, CancellationToken ct)
  {
    schedule.Status = AccountingWorkflowStates.Stale;
    await db.SaveChangesAsync(ct);
    return CommandResult.Fail(ErrorCodes.GenerationStale, $"{reason} Prepare a new schedule revision.");
  }

  private sealed record PreparedItem(CurrencyRemeasurementItemRequest Request, string SourceEvidenceSha256,
    Guid ExchangeRateId, Guid RateSetVersionId, DateOnly RateDate, string RateType, decimal AppliedRate,
    MonetaryRemeasurementResult Calculation);
}
