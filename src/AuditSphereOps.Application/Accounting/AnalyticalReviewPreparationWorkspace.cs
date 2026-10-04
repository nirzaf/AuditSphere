using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record AnalyticalPreparationPeriod(Guid Id, string Code, DateOnly StartDate, DateOnly EndDate,
  string Currency, string Status, long Revision, bool CanPrepare, IReadOnlyList<string> Blockers);
public sealed record AnalyticalPreparationContext(Guid EngagementId, Guid ClientId, string ClientName,
  string EngagementName, long InputGeneration, IReadOnlyList<AnalyticalPreparationPeriod> Periods);
public sealed record AnalyticalPreparationState(Guid EngagementId, Guid ClientId, Guid PeriodId,
  Guid? ComparisonPeriodId, string Currency, long InputGeneration, string ReviewBasis, bool CanPrepare,
  IReadOnlyList<string> Blockers);
public sealed record AnalyticalPreparationFields(Guid PeriodId, Guid? ComparisonPeriodId, string Area,
  string Measure, string CurrentAmount, string PriorAmount, string? BudgetAmount, string DenominatorBasis,
  string FormulaVersion, string Explanation, string SeasonalityExplanation);
public sealed record AnalyticalPreparationRequest(Guid RequestId, string ReviewBasis,
  AnalyticalPreparationFields Fields, string Reason, string EvidenceReference, bool Reviewed = false);
public sealed record AnalyticalPreparationPreview(Guid EngagementId, Guid ClientId, Guid PeriodId,
  Guid? ComparisonPeriodId, Guid RequestId, string ReviewBasis, string RequestHash, string Area,
  string Measure, string CurrentAmount, string PriorAmount, string? BudgetAmount, string? VarianceRatio,
  string Currency, string ResultStatus, bool CanProceed, IReadOnlyList<string> Blockers);
public sealed record AnalyticalPreparationResult(Guid EvidenceId, string Kind, string Status, string Area,
  string Measure, string CurrentAmount, string PriorAmount, string? VarianceRatio, Guid ActorId,
  DateTimeOffset CreatedAt);
public sealed record AnalyticalPreparationReceipt(Guid Id, Guid RequestId, string RequestHash,
  Guid EvidenceId, Guid ActorId, string Reason, string EvidenceReference, DateTimeOffset CreatedAt,
  AnalyticalPreparationResult Result);
public sealed record AnalyticalPreparationLookup(bool Found, AnalyticalPreparationReceipt? Receipt);

/// <summary>Reviewed native analytical preparation. It wraps the established accounting service with
/// exact scope/generation fencing, immutable actor-owned receipt evidence and safe response-loss recovery.</summary>
public static class AnalyticalReviewPreparationWorkspace
{
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static Task<CommandResult> Authorize(IClientAccountingDbContext db, ActorContext actor, Guid clientId,
    Guid engagementId, CancellationToken ct) => AccountingPreparationAuthorization.AuthorizeAsync(db, actor,
      clientId, engagementId, ct);

  private static bool Amount(string? raw, bool allowNegative, bool optional, out decimal value)
  {
    value = 0m;
    if (optional && string.IsNullOrWhiteSpace(raw)) return true;
    if (raw is not { Length: > 0 and <= 21 } || !Regex.IsMatch(raw,
        allowNegative ? @"^-?[0-9]{1,13}(?:\.[0-9]{1,6})?$" : @"^[0-9]{1,13}(?:\.[0-9]{1,6})?$",
        RegexOptions.CultureInvariant) ||
        !decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
          CultureInfo.InvariantCulture, out value) || MoneyPolicy.Normalize(value) != value)
      return false;
    return true;
  }

  private static bool FieldsValid(AnalyticalPreparationFields? f, out decimal current, out decimal prior,
    out decimal? budget)
  {
    current = prior = 0m;
    budget = null;
    if (f is null || f.PeriodId == Guid.Empty || f.ComparisonPeriodId == Guid.Empty ||
        f.Area is not { Length: > 0 and <= 80 } || string.IsNullOrWhiteSpace(f.Area) ||
        f.Measure is not { Length: > 0 and <= 100 } || string.IsNullOrWhiteSpace(f.Measure) ||
        f.DenominatorBasis is not { Length: > 0 and <= 200 } || string.IsNullOrWhiteSpace(f.DenominatorBasis) ||
        f.FormulaVersion is not { Length: > 0 and <= 100 } || string.IsNullOrWhiteSpace(f.FormulaVersion) ||
        f.Explanation is not { Length: > 0 and <= 4000 } || string.IsNullOrWhiteSpace(f.Explanation) ||
        f.SeasonalityExplanation is not { Length: <= 2000 } ||
        !Amount(f.CurrentAmount, true, false, out current) || !Amount(f.PriorAmount, true, false, out prior) ||
        !Amount(f.BudgetAmount, true, true, out var parsedBudget)) return false;
    if (!string.IsNullOrWhiteSpace(f.BudgetAmount)) budget = parsedBudget;
    return true;
  }

  private static string RequestHash(ActorContext actor, Guid engagementId, AnalyticalPreparationRequest request) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { actor.FirmId, actor.UserId, actor.SessionEpoch,
      engagementId, request.RequestId, request.ReviewBasis, request.Fields,
      Reason = request.Reason.Trim(), EvidenceReference = request.EvidenceReference.Trim() }));

  private static AnalyticalPreparationResult PreparationResult(AccountingAnalysisReview review)
  {
    string Required(string key) => review.Amounts.Single(x => x.Key == key).Value ??
      throw new InvalidOperationException("The analytical result omitted a required amount.");
    return new(review.Id, review.Kind, review.Status, review.Area, review.Details.Single(x => x.Key == "MEASURE").Value,
      Required("CURRENT"), Required("PRIOR"), review.Amounts.Single(x => x.Key == "RATIO").Value,
      review.CreatedByUserId ?? Guid.Empty, review.CreatedAt);
  }

  private static AnalyticalPreparationReceipt ToReceipt(AccountingAnalysisPreparation row) =>
    new(row.Id, row.RequestId, row.RequestHash, row.EvidenceId, row.ActorId, row.Reason,
      row.EvidenceReference, row.CreatedAt, JsonSerializer.Deserialize<AnalyticalPreparationResult>(row.ResultJson)!);

  public static async Task<CommandResult<AnalyticalPreparationContext>> ContextAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty) return Fail<AnalyticalPreparationContext>(ErrorCodes.ScopeDenied, "This planning context is unavailable.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return Fail<AnalyticalPreparationContext>(ErrorCodes.ScopeDenied, "This planning context is unavailable.");
    var auth = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    if (!auth.Succeeded) return Fail<AnalyticalPreparationContext>(auth.ErrorCode!, "This planning context is unavailable.");
    var client = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == engagement.PracticeClientId, ct);
    var safety = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == engagement.PracticeClientId, ct);
    if (client is null || safety is null) return Fail<AnalyticalPreparationContext>(ErrorCodes.GateBlocked, "Accounting preparation context is unavailable.");
    var periods = await db.ClientReportingPeriods.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId)
      .OrderByDescending(x => x.EndDate).ThenByDescending(x => x.Id).Take(100).ToListAsync(ct);
    var frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagement.Id && x.State == "FROZEN", ct);
    var rows = periods.Select(x =>
    {
      var blockers = new List<string>();
      if (engagement.Status != "Active") blockers.Add("The engagement is not active.");
      if (x.Status is not ("ACTIVE" or "DRAFT")) blockers.Add("The reporting period is closed.");
      if (frozen) blockers.Add("The engagement file is frozen.");
      return new AnalyticalPreparationPeriod(x.Id, x.PeriodCode, x.StartDate, x.EndDate,
        x.Currency, x.Status, x.Revision, blockers.Count == 0, blockers);
    }).ToArray();
    var final = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    if (!final.Succeeded) return Fail<AnalyticalPreparationContext>(final.ErrorCode!, "This planning context is unavailable.");
    return CommandResult<AnalyticalPreparationContext>.Ok(new(engagement.Id, engagement.PracticeClientId,
      client.CommercialName ?? client.LegalName, engagement.ServiceRoute + " " + engagement.PeriodEnd,
      safety.InputGeneration, rows));
  }

  public static async Task<CommandResult<AnalyticalPreparationState>> StateAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid periodId, Guid? comparisonPeriodId, CancellationToken ct = default)
  {
    if (periodId == Guid.Empty || comparisonPeriodId == Guid.Empty)
      return Fail<AnalyticalPreparationState>(ErrorCodes.ScopeDenied, "This planning context is unavailable.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return Fail<AnalyticalPreparationState>(ErrorCodes.ScopeDenied, "This planning context is unavailable.");
    var auth = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    if (!auth.Succeeded) return Fail<AnalyticalPreparationState>(ErrorCodes.ScopeDenied, "This planning context is unavailable.");
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId && x.Id == periodId, ct);
    if (period is null) return Fail<AnalyticalPreparationState>(ErrorCodes.ScopeDenied, "This planning context is unavailable.");
    ClientReportingPeriod? comparison = null;
    if (comparisonPeriodId is { } comparisonId)
    {
      comparison = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId && x.Id == comparisonId, ct);
      if (comparison is null || comparison.EndDate >= period.StartDate)
        return Fail<AnalyticalPreparationState>(ErrorCodes.ScopeDenied, "Choose a prior reporting period for comparison.");
    }
    var safety = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == engagement.PracticeClientId, ct);
    if (safety is null) return Fail<AnalyticalPreparationState>(ErrorCodes.GateBlocked, "Accounting safety state is unavailable.");
    var frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagement.Id && x.State == "FROZEN", ct);
    var blockers = new List<string>();
    if (engagement.Status != "Active") blockers.Add("The engagement is not active.");
    if (period.Status is not ("ACTIVE" or "DRAFT")) blockers.Add("The reporting period is closed.");
    if (frozen) blockers.Add("The engagement file is frozen.");
    if (comparison is not null && !string.Equals(comparison.Currency, period.Currency, StringComparison.OrdinalIgnoreCase))
      blockers.Add("The comparison period uses a different reporting currency.");
    var basis = Hashing.Sha256Hex(JsonSerializer.Serialize(new
    {
      actor.FirmId, actor.UserId, actor.SessionEpoch, Engagement = new { engagement.Id, engagement.PracticeClientId, engagement.Status, engagement.ServiceRoute, engagement.PeriodEnd },
      Period = new { period.Id, period.PeriodCode, period.StartDate, period.EndDate, period.Currency, period.Status, period.Revision },
      Comparison = comparison is null ? null : new { comparison.Id, comparison.PeriodCode, comparison.StartDate, comparison.EndDate, comparison.Currency, comparison.Status, comparison.Revision },
      safety.InputGeneration, frozen
    }));
    var final = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    if (!final.Succeeded) return Fail<AnalyticalPreparationState>(ErrorCodes.ScopeDenied, "This planning context is unavailable.");
    return CommandResult<AnalyticalPreparationState>.Ok(new(engagement.Id, engagement.PracticeClientId,
      period.Id, comparison?.Id, period.Currency, safety.InputGeneration, basis, blockers.Count == 0,
      blockers.Distinct().ToArray()));
  }

  public static async Task<CommandResult<AnalyticalPreparationPreview>> PreviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, AnalyticalPreparationRequest? request, CancellationToken ct = default)
  {
    if (request is null || request.RequestId == Guid.Empty || !SourceAcceptanceWorkspace.ValidHash(request.ReviewBasis) ||
        !FieldsValid(request.Fields, out var current, out var prior, out var budget) ||
        request.Reason is not { Length: > 0 and <= 4000 } || string.IsNullOrWhiteSpace(request.Reason) ||
        request.EvidenceReference is not { Length: > 0 and <= 2000 } || string.IsNullOrWhiteSpace(request.EvidenceReference))
      return Fail<AnalyticalPreparationPreview>(ErrorCodes.Accounting.ReconciliationRejected,
        "Enter bounded analytical fields, exact six-decimal amounts, a reason and an evidence reference.");
    var state = await StateAsync(db, actor, engagementId, request.Fields.PeriodId, request.Fields.ComparisonPeriodId, ct);
    if (!state.Succeeded) return Fail<AnalyticalPreparationPreview>(state.ErrorCode!, state.Message!);
    var s = state.Value!;
    if (request.ReviewBasis != s.ReviewBasis)
      return Fail<AnalyticalPreparationPreview>(ErrorCodes.StaleRevision, "Refresh the period context and review its current basis.");
    decimal? ratio = prior == 0m ? null : MoneyPolicy.Normalize((current - prior) / Math.Abs(prior));
    var blockers = s.Blockers.ToList();
    var final = await StateAsync(db, actor, engagementId, request.Fields.PeriodId, request.Fields.ComparisonPeriodId, ct);
    if (!final.Succeeded) return Fail<AnalyticalPreparationPreview>(final.ErrorCode!, final.Message!);
    if (final.Value!.ReviewBasis != s.ReviewBasis)
      return Fail<AnalyticalPreparationPreview>(ErrorCodes.StaleRevision, "The accounting context changed during preview.");
    return CommandResult<AnalyticalPreparationPreview>.Ok(new(engagementId, s.ClientId, s.PeriodId,
      s.ComparisonPeriodId, request.RequestId, s.ReviewBasis, RequestHash(actor, engagementId, request),
      request.Fields.Area.Trim().ToUpperInvariant(), request.Fields.Measure.Trim(), current.ToString("0.000000", CultureInfo.InvariantCulture),
      prior.ToString("0.000000", CultureInfo.InvariantCulture), budget?.ToString("0.000000", CultureInfo.InvariantCulture),
      ratio?.ToString("0.000000", CultureInfo.InvariantCulture), s.Currency, ratio.HasValue ? "DRAFT" : "INSUFFICIENT_DATA",
      blockers.Count == 0, blockers.Distinct().ToArray()));
  }

  public static async Task<CommandResult<AnalyticalPreparationLookup>> LookupAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !SourceAcceptanceWorkspace.ValidHash(requestHash))
      return Fail<AnalyticalPreparationLookup>(ErrorCodes.ScopeDenied, "This retained request is unavailable.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return Fail<AnalyticalPreparationLookup>(ErrorCodes.ScopeDenied, "This retained request is unavailable.");
    var auth = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    if (!auth.Succeeded) return Fail<AnalyticalPreparationLookup>(ErrorCodes.ScopeDenied, "This retained request is unavailable.");
    var row = await db.AccountingAnalysisPreparations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (row is not null && (row.EngagementId != engagementId || row.RequestHash != requestHash))
      return Fail<AnalyticalPreparationLookup>(ErrorCodes.IdempotencyConflict, "This request reference belongs to a different reviewed intent.");
    var final = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    return final.Succeeded
      ? CommandResult<AnalyticalPreparationLookup>.Ok(new(row is not null, row is null ? null : ToReceipt(row)))
      : Fail<AnalyticalPreparationLookup>(ErrorCodes.ScopeDenied, "This retained request is unavailable.");
  }

  public static async Task<CommandResult<AnalyticalPreparationReceipt>> ExecuteAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, AnalyticalPreparationRequest? request, CancellationToken ct = default)
  {
    if (request is null || !request.Reviewed || request.RequestId == Guid.Empty ||
        !SourceAcceptanceWorkspace.ValidHash(request.ReviewBasis) ||
        !FieldsValid(request.Fields, out var current, out var priorAmount, out var budget) ||
        request.Reason is not { Length: > 0 and <= 4000 } || string.IsNullOrWhiteSpace(request.Reason) ||
        request.EvidenceReference is not { Length: > 0 and <= 2000 } || string.IsNullOrWhiteSpace(request.EvidenceReference))
      return Fail<AnalyticalPreparationReceipt>(ErrorCodes.Accounting.ReconciliationRejected, "Preview and explicitly confirm the exact analytical intent.");
    var before = await StateAsync(db, actor, engagementId, request.Fields.PeriodId, request.Fields.ComparisonPeriodId, ct);
    if (!before.Succeeded) return Fail<AnalyticalPreparationReceipt>(before.ErrorCode!, before.Message!);
    var state = before.Value!;
    if (request.ReviewBasis != state.ReviewBasis) return Fail<AnalyticalPreparationReceipt>(ErrorCodes.StaleRevision, "The reviewed period context changed.");
    var auth = await Authorize(db, actor, state.ClientId, engagementId, ct);
    if (!auth.Succeeded) return Fail<AnalyticalPreparationReceipt>(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    var locked = await GeneralLedgerCompletenessWorkspace.LockAsync(db, actor, state.ClientId, engagementId, ct);
    if (!locked.Succeeded) return Fail<AnalyticalPreparationReceipt>(locked.ErrorCode!, locked.Message!);
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR UPDATE").AsNoTracking().SingleAsync(ct);
    var requestHash = RequestHash(actor, engagementId, request);
    var existing = await db.AccountingAnalysisPreparations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
    if (existing is not null)
    {
      if (existing.EngagementId != engagementId || existing.RequestHash != requestHash || existing.ReviewBasis != request.ReviewBasis)
        return Fail<AnalyticalPreparationReceipt>(ErrorCodes.IdempotencyConflict, "A changed analytical intent cannot reuse this request identity.");
      auth = await Authorize(db, actor, state.ClientId, engagementId, ct);
      if (!auth.Succeeded) return Fail<AnalyticalPreparationReceipt>(auth.ErrorCode!, auth.Message!);
      await tx.CommitAsync(ct);
      return CommandResult<AnalyticalPreparationReceipt>.Ok(ToReceipt(existing));
    }

    var mutable = await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db, actor, state.ClientId,
      engagementId, state.PeriodId, null, ct);
    if (!mutable.Succeeded) return Fail<AnalyticalPreparationReceipt>(mutable.ErrorCode!, mutable.Message!);
    var currentState = await StateAsync(db, actor, engagementId, state.PeriodId, state.ComparisonPeriodId, ct);
    if (!currentState.Succeeded) return Fail<AnalyticalPreparationReceipt>(currentState.ErrorCode!, currentState.Message!);
    if (currentState.Value!.ReviewBasis != state.ReviewBasis || !currentState.Value.CanPrepare)
      return Fail<AnalyticalPreparationReceipt>(ErrorCodes.StaleRevision, "The accounting context changed. Refresh and review again.");

    var created = await AccountingAnalysisService.CreateAnalyticalReviewAsync(db, actor,
      new(state.ClientId, engagementId, state.PeriodId, state.ComparisonPeriodId,
        request.Fields.Area.Trim(), request.Fields.Measure.Trim(), current, priorAmount, budget,
        request.Fields.DenominatorBasis.Trim(), request.Fields.FormulaVersion.Trim(), request.Fields.Explanation.Trim(),
        state.Currency, request.Fields.SeasonalityExplanation.Trim()), ct);
    if (!created.Succeeded) return Fail<AnalyticalPreparationReceipt>(created.ErrorCode!, created.Message!);
    var result = await AccountingAnalysisReviewQuery.GetAsync(db, actor, "ANALYTICAL", created.Value!, ct: ct);
    if (!result.Succeeded) return Fail<AnalyticalPreparationReceipt>(result.ErrorCode!, result.Message!);
    var row = new AccountingAnalysisPreparation
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = state.ClientId, EngagementId = engagementId,
      ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch, RequestId = request.RequestId,
      RequestHash = requestHash, ReviewBasis = request.ReviewBasis, EvidenceId = created.Value!,
      InputJson = JsonSerializer.Serialize(request.Fields), ContextJson = JsonSerializer.Serialize(state),
      ResultJson = JsonSerializer.Serialize(PreparationResult(result.Value!)), Reason = request.Reason.Trim(),
      EvidenceReference = request.EvidenceReference.Trim(), CreatedAt = DateTimeOffset.UtcNow
    };
    db.AccountingAnalysisPreparations.Add(row);
    await db.SaveChangesAsync(ct);
    auth = await Authorize(db, actor, state.ClientId, engagementId, ct);
    if (!auth.Succeeded) return Fail<AnalyticalPreparationReceipt>(auth.ErrorCode!, auth.Message!);
    mutable = await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db, actor, state.ClientId,
      engagementId, state.PeriodId, null, ct);
    if (!mutable.Succeeded) return Fail<AnalyticalPreparationReceipt>(mutable.ErrorCode!, mutable.Message!);
    currentState = await StateAsync(db, actor, engagementId, state.PeriodId, state.ComparisonPeriodId, ct);
    if (!currentState.Succeeded || currentState.Value!.ReviewBasis != state.ReviewBasis)
      return Fail<AnalyticalPreparationReceipt>(ErrorCodes.StaleRevision, "The analytical context changed before the receipt was committed.");
    await tx.CommitAsync(ct);
    return CommandResult<AnalyticalPreparationReceipt>.Ok(ToReceipt(row));
  }
}
