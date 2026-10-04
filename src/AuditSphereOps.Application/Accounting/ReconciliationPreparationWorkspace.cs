using System.Data;
using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ReconciliationPeriodOption(Guid Id, string Code, DateOnly StartDate, DateOnly EndDate,
  string Currency, string Basis, string Status, long Revision);
public sealed record ReconciliationSourceOption(Guid Id, string Kind, Guid PeriodId, string PeriodCode,
  Guid? BookId, string Currency, string Basis, int RowCount, string SourceHash, DateTimeOffset ImportedAt);
public sealed record ReconciliationPreparationContext(Guid EngagementId, Guid ClientId, string ClientName,
  string EngagementName, IReadOnlyList<ReconciliationPeriodOption> Periods,
  IReadOnlyList<ReconciliationSourceOption> Sources);
public sealed record ReconciliationPreparationState(Guid EngagementId, Guid ClientId, string ClientName,
  string EngagementName, Guid PeriodId, string PeriodCode, Guid? BookId, string Currency, string Basis,
  string SourceKind, Guid SourceId, string SourceHash, long InputGeneration, string ReviewBasis,
  bool CanPrepare, IReadOnlyList<string> Blockers);
public sealed record ReconciliationPreparationFields(string SourceKind, Guid SourceId, Guid PeriodId,
  Guid? BookId, string Area, IReadOnlyList<string> AccountCodes, DateOnly AsOfDate,
  string AgingBasis = "", string AgingBucketRuleVersion = "");
public sealed record ReconciliationPreparationRequest(Guid RequestId, string ReviewBasis,
  ReconciliationPreparationFields Fields, string Reason, string EvidenceReference, bool Reviewed = false);
public sealed record ReconciliationPreparationPreview(Guid EngagementId, Guid ClientId, Guid PeriodId,
  Guid RequestId, string ReviewBasis, string RequestHash, string SourceKind, Guid SourceId, string SourceHash,
  string Area, string AccountSelection, string SourceTotal, string GlTotal, string Residual, string Currency,
  string ResultStatus, bool CanProceed, IReadOnlyList<string> Blockers);
public sealed record ReconciliationPreparationResult(Guid ReconciliationId, long Revision, Guid? SupersedesId,
  string Status, string SourceTotal, string GlTotal, string Residual, string SourceHash,
  Guid ActorId, DateTimeOffset CreatedAt);
public sealed record ReconciliationPreparationReceipt(Guid Id, Guid RequestId, string RequestHash,
  Guid ReconciliationId, Guid ActorId, string Reason, string EvidenceReference,
  DateTimeOffset CreatedAt, ReconciliationPreparationResult Result);
public sealed record ReconciliationPreparationLookup(bool Found, ReconciliationPreparationReceipt? Receipt);

/// <summary>Scoped, reviewed reconciliation creation with exact source preview and actor-owned lost-response recovery.</summary>
public static class ReconciliationPreparationWorkspace
{
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"];
  private const int SourceLimit = 100;
  private const int MaxCodes = 100;
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static Task<CommandResult> Authorize(IClientAccountingDbContext db, ActorContext actor, Guid clientId,
    Guid engagementId, CancellationToken ct) => AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, clientId, engagementId, Roles, InternalOnly: true), ct);
  private static string Exact(decimal value) => value.ToString("0.000000", CultureInfo.InvariantCulture);
  private static bool Hash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

  private static string RequestHash(ActorContext actor, Guid engagementId, ReconciliationPreparationRequest request) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { actor.FirmId, actor.UserId, actor.SessionEpoch,
      engagementId, request.RequestId, request.ReviewBasis, request.Fields,
      Reason = request.Reason.Trim(), EvidenceReference = request.EvidenceReference.Trim() }));

  public static async Task<CommandResult<ReconciliationPreparationContext>> ContextAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty) return Fail<ReconciliationPreparationContext>(ErrorCodes.ScopeDenied, "This reconciliation context is unavailable.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return Fail<ReconciliationPreparationContext>(ErrorCodes.ScopeDenied, "This reconciliation context is unavailable.");
    var auth = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    if (!auth.Succeeded) return Fail<ReconciliationPreparationContext>(ErrorCodes.ScopeDenied, "This reconciliation context is unavailable.");
    var client = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagement.PracticeClientId, ct);
    var periods = await db.ClientReportingPeriods.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId)
      .OrderByDescending(x => x.EndDate).ThenByDescending(x => x.Id).Take(SourceLimit).ToListAsync(ct);
    var periodIds = periods.Select(x => x.Id).ToArray();
    var tb = await db.TrialBalanceDatasets.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId &&
        x.EngagementId == engagement.Id && x.PeriodId.HasValue && periodIds.Contains(x.PeriodId.Value) &&
        x.ValidationStatus == "Accepted" && x.ImportState == TrialBalanceImportStates.Sealed)
      .OrderByDescending(x => x.ImportedAt).ThenBy(x => x.Id).Take(SourceLimit)
      .Select(x => new { x.Id, PeriodId = x.PeriodId!.Value, x.BookId, x.Currency, Basis = x.Basis ?? "", x.NormalizedDatasetDigest, x.Sha256Hex, x.ImportedAt }).ToListAsync(ct);
    var tbIds = tb.Select(x => x.Id).ToArray();
    var codeCounts = tbIds.Length == 0 ? new Dictionary<Guid, int>() : await db.TrialBalanceRows.AsNoTracking().Where(x => tbIds.Contains(x.DatasetId))
      .GroupBy(x => x.DatasetId).Select(x => new { Id = x.Key, Count = x.Select(y => y.AccountCode).Distinct().Count() })
      .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
    var gl = await db.SourceImportBatches.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId &&
        x.EngagementId == engagement.Id && x.PeriodId != Guid.Empty && x.SourceKind == AccountingSourceKinds.GeneralLedger && x.Status == "SEALED")
      .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(SourceLimit)
      .Select(x => new { x.Id, x.PeriodId, x.BookId, x.Currency, x.NormalizedDatasetDigest, x.RowCount, x.CreatedAt }).ToListAsync(ct);
    var periodMap = periods.ToDictionary(x => x.Id);
    var sources = tb.Where(x => periodMap.ContainsKey(x.PeriodId)).Select(x =>
      {
        var period = periodMap[x.PeriodId];
        return new ReconciliationSourceOption(x.Id, "TRIAL_BALANCE", period.Id, period.PeriodCode, x.BookId,
          x.Currency, x.Basis, codeCounts.GetValueOrDefault(x.Id), ValidDigest(x.NormalizedDatasetDigest) ?? ValidDigest(x.Sha256Hex) ?? "", x.ImportedAt);
      }).Concat(gl.Where(x => periodMap.ContainsKey(x.PeriodId)).Select(x =>
      {
        var period = periodMap[x.PeriodId];
        return new ReconciliationSourceOption(x.Id, "GENERAL_LEDGER", period.Id, period.PeriodCode, x.BookId,
          x.Currency, period.Basis, x.RowCount, ValidDigest(x.NormalizedDatasetDigest) ?? "", x.CreatedAt);
      })).OrderByDescending(x => x.ImportedAt).ThenBy(x => x.Id).Take(SourceLimit * 2).ToArray();
    var final = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    if (!final.Succeeded || client is null) return Fail<ReconciliationPreparationContext>(ErrorCodes.ScopeDenied, "This reconciliation context is unavailable.");
    return CommandResult<ReconciliationPreparationContext>.Ok(new(engagement.Id, engagement.PracticeClientId,
      client.CommercialName ?? client.LegalName, engagement.ServiceRoute + " " + engagement.PeriodEnd,
      periods.Select(x => new ReconciliationPeriodOption(x.Id, x.PeriodCode, x.StartDate, x.EndDate, x.Currency, x.Basis, x.Status, x.Revision)).ToArray(), sources));
  }

  public static async Task<CommandResult<ReconciliationPreparationState>> StateAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, string? sourceKind, Guid sourceId, CancellationToken ct = default)
  {
    if (sourceId == Guid.Empty || sourceKind is not ("TRIAL_BALANCE" or "GENERAL_LEDGER"))
      return Fail<ReconciliationPreparationState>(ErrorCodes.ScopeDenied, "This reconciliation source is unavailable.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return Fail<ReconciliationPreparationState>(ErrorCodes.ScopeDenied, "This reconciliation source is unavailable.");
    var auth = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    if (!auth.Succeeded) return Fail<ReconciliationPreparationState>(ErrorCodes.ScopeDenied, "This reconciliation source is unavailable.");
    Guid periodId; Guid? bookId; string currency; string basis; string digest; DateTimeOffset importedAt;
    if (sourceKind == "TRIAL_BALANCE")
    {
      var source = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.ClientId == engagement.PracticeClientId && x.EngagementId == engagement.Id && x.Id == sourceId && x.PeriodId.HasValue &&
        x.ValidationStatus == "Accepted" && x.ImportState == TrialBalanceImportStates.Sealed, ct);
      if (source is null) return Fail<ReconciliationPreparationState>(ErrorCodes.ScopeDenied, "This reconciliation source is unavailable.");
      periodId = source.PeriodId!.Value; bookId = source.BookId; currency = source.Currency; basis = source.Basis ?? "";
      digest = ValidDigest(source.NormalizedDatasetDigest) ?? ValidDigest(source.Sha256Hex) ?? ""; importedAt = source.ImportedAt;
    }
    else
    {
      var source = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.ClientId == engagement.PracticeClientId && x.EngagementId == engagement.Id && x.Id == sourceId &&
        x.SourceKind == AccountingSourceKinds.GeneralLedger && x.Status == "SEALED", ct);
      if (source is null) return Fail<ReconciliationPreparationState>(ErrorCodes.ScopeDenied, "This reconciliation source is unavailable.");
      periodId = source.PeriodId; bookId = source.BookId; currency = source.Currency; importedAt = source.CreatedAt;
      digest = ValidDigest(source.NormalizedDatasetDigest) ?? "";
      basis = await db.ClientReportingPeriods.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId && x.Id == periodId)
        .Select(x => x.Basis).SingleOrDefaultAsync(ct) ?? "";
    }
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == engagement.PracticeClientId && x.Id == periodId, ct);
    var safety = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagement.PracticeClientId, ct);
    var client = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagement.PracticeClientId, ct);
    var frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagement.Id && x.State == "FROZEN", ct);
    if (period is null || safety is null || client is null || period.Currency != currency || period.Basis != basis || !Hash(digest))
      return Fail<ReconciliationPreparationState>(ErrorCodes.GateBlocked, "The exact source and reporting context are unavailable.");
    var blockers = new List<string>();
    if (engagement.Status != "Active") blockers.Add("The engagement is not active.");
    if (period.Status is not ("ACTIVE" or "DRAFT")) blockers.Add("The reporting period is closed.");
    if (frozen) blockers.Add("The engagement file is frozen.");
    if (importedAt > DateTimeOffset.UtcNow.AddMinutes(5)) blockers.Add("The source import timestamp is invalid.");
    var stateBasis = Hashing.Sha256Hex(JsonSerializer.Serialize(new { actor.FirmId, actor.UserId, actor.SessionEpoch,
      Engagement = new { engagement.Id, engagement.PracticeClientId, engagement.Status },
      Period = new { period.Id, period.PeriodCode, period.StartDate, period.EndDate, period.Currency, period.Basis, period.Status, period.Revision },
      sourceKind, sourceId, bookId, currency, basis, digest, importedAt, safety.InputGeneration, frozen }));
    var final = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    if (!final.Succeeded) return Fail<ReconciliationPreparationState>(ErrorCodes.ScopeDenied, "This reconciliation source is unavailable.");
    return CommandResult<ReconciliationPreparationState>.Ok(new(engagement.Id, engagement.PracticeClientId,
      client.CommercialName ?? client.LegalName, engagement.ServiceRoute + " " + engagement.PeriodEnd, period.Id,
      period.PeriodCode, bookId, currency, basis, sourceKind, sourceId, digest, safety.InputGeneration,
      stateBasis, blockers.Count == 0, blockers));
  }

  public static async Task<CommandResult<ReconciliationPreparationPreview>> PreviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, ReconciliationPreparationRequest? request, CancellationToken ct = default)
  {
    if (!ValidRequest(request)) return Fail<ReconciliationPreparationPreview>(ErrorCodes.Accounting.ReconciliationRejected,
      "Choose a supported sealed source, explicit accounts, a date, reason and evidence reference.");
    var fields = request!.Fields;
    var state = await StateAsync(db, actor, engagementId, fields.SourceKind, fields.SourceId, ct);
    if (!state.Succeeded) return Fail<ReconciliationPreparationPreview>(state.ErrorCode!, state.Message!);
    var s = state.Value!;
    if (!s.CanPrepare || request.ReviewBasis != s.ReviewBasis || fields.PeriodId != s.PeriodId || fields.BookId != s.BookId ||
        fields.AsOfDate < await db.ClientReportingPeriods.AsNoTracking().Where(x => x.Id == s.PeriodId && x.FirmId == actor.FirmId)
          .Select(x => x.StartDate).SingleAsync(ct))
      return Fail<ReconciliationPreparationPreview>(ErrorCodes.StaleRevision, "Refresh the current source and reporting context before review.");
    var area = fields.Area.Trim().ToUpperInvariant();
    var agingArea = area.Contains("RECEIVABLE", StringComparison.Ordinal) || area.Contains("PAYABLE", StringComparison.Ordinal);
    if (agingArea && (!AccountingAgingRules.IsSupportedBasis(fields.AgingBasis) ||
        fields.AgingBucketRuleVersion.Trim().ToUpperInvariant() != AccountingAgingRules.StandardRuleVersion) ||
        !string.IsNullOrWhiteSpace(fields.AgingBasis) && !AccountingAgingRules.IsSupportedBasis(fields.AgingBasis) ||
        !string.IsNullOrWhiteSpace(fields.AgingBucketRuleVersion) && fields.AgingBucketRuleVersion.Trim().ToUpperInvariant() != AccountingAgingRules.StandardRuleVersion)
      return Fail<ReconciliationPreparationPreview>(ErrorCodes.Accounting.ReconciliationRejected,
        "Receivable and payable ageing needs an explicit supported date basis and standard bucket-rule version.");
    var calculated = await CalculateAsync(db, actor, s, fields, ct);
    if (!calculated.Succeeded) return Fail<ReconciliationPreparationPreview>(calculated.ErrorCode!, calculated.Message!);
    var current = await StateAsync(db, actor, engagementId, fields.SourceKind, fields.SourceId, ct);
    if (!current.Succeeded || current.Value!.ReviewBasis != s.ReviewBasis)
      return Fail<ReconciliationPreparationPreview>(ErrorCodes.StaleRevision, "The source or reporting context changed during preview.");
    var value = calculated.Value!;
    var selection = string.Join(",", CanonicalCodes(fields.AccountCodes));
    return CommandResult<ReconciliationPreparationPreview>.Ok(new(engagementId, s.ClientId, s.PeriodId,
      request.RequestId, s.ReviewBasis, RequestHash(actor, engagementId, request), s.SourceKind, s.SourceId,
      s.SourceHash, area, selection, Exact(value.SourceTotal), Exact(value.GlTotal),
      Exact(MoneyPolicy.Normalize(value.GlTotal - value.SourceTotal)), s.Currency,
      MoneyPolicy.Normalize(value.GlTotal - value.SourceTotal) == 0m ? "RECONCILED" : "UNRECONCILED", true, []));
  }

  public static async Task<CommandResult<ReconciliationPreparationLookup>> LookupAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !Hash(requestHash)) return Fail<ReconciliationPreparationLookup>(ErrorCodes.ScopeDenied, "This retained request is unavailable.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return Fail<ReconciliationPreparationLookup>(ErrorCodes.ScopeDenied, "This retained request is unavailable.");
    var auth = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    if (!auth.Succeeded) return Fail<ReconciliationPreparationLookup>(ErrorCodes.ScopeDenied, "This retained request is unavailable.");
    var row = await db.AccountingReconciliationPreparations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (row is not null && (row.EngagementId != engagementId || row.RequestHash != requestHash))
      return Fail<ReconciliationPreparationLookup>(ErrorCodes.IdempotencyConflict, "This reference belongs to another exact intent.");
    var result = row is null ? null : await ReceiptAsync(db, actor, row, ct);
    var final = await Authorize(db, actor, engagement.PracticeClientId, engagement.Id, ct);
    return final.Succeeded ? CommandResult<ReconciliationPreparationLookup>.Ok(new(row is not null, result)) :
      Fail<ReconciliationPreparationLookup>(ErrorCodes.ScopeDenied, "This retained request is unavailable.");
  }

  public static async Task<CommandResult<ReconciliationPreparationReceipt>> ExecuteAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, ReconciliationPreparationRequest? request, CancellationToken ct = default)
  {
    if (!ValidRequest(request) || request is not { Reviewed: true })
      return Fail<ReconciliationPreparationReceipt>(ErrorCodes.Accounting.ReconciliationRejected, "Preview and explicitly confirm the exact reconciliation intent.");
    var fields = request.Fields;
    var before = await StateAsync(db, actor, engagementId, fields.SourceKind, fields.SourceId, ct);
    if (!before.Succeeded) return Fail<ReconciliationPreparationReceipt>(before.ErrorCode!, before.Message!);
    var state = before.Value!;
    if (!state.CanPrepare || state.ReviewBasis != request.ReviewBasis || fields.PeriodId != state.PeriodId || fields.BookId != state.BookId)
      return Fail<ReconciliationPreparationReceipt>(ErrorCodes.StaleRevision, "The reviewed source context changed. Refresh and review again.");
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    var locked = await GeneralLedgerCompletenessWorkspace.LockAsync(db, actor, state.ClientId, engagementId, ct);
    if (!locked.Succeeded) return Fail<ReconciliationPreparationReceipt>(locked.ErrorCode!, locked.Message!);
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR UPDATE").AsNoTracking().SingleAsync(ct);
    var hash = RequestHash(actor, engagementId, request);
    var existing = await db.AccountingReconciliationPreparations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
    if (existing is not null)
    {
      if (existing.EngagementId != engagementId || existing.RequestHash != hash || existing.ReviewBasis != request.ReviewBasis)
        return Fail<ReconciliationPreparationReceipt>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
      var authExisting = await Authorize(db, actor, state.ClientId, engagementId, ct);
      if (!authExisting.Succeeded) return Fail<ReconciliationPreparationReceipt>(authExisting.ErrorCode!, authExisting.Message!);
      var replay = await ReceiptAsync(db, actor, existing, ct);
      await tx.CommitAsync(ct);
      return CommandResult<ReconciliationPreparationReceipt>.Ok(replay);
    }
    var mutable = await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db, actor, state.ClientId, engagementId, state.PeriodId, null, ct);
    if (!mutable.Succeeded) return Fail<ReconciliationPreparationReceipt>(mutable.ErrorCode!, mutable.Message!);
    var current = await StateAsync(db, actor, engagementId, fields.SourceKind, fields.SourceId, ct);
    if (!current.Succeeded || current.Value!.ReviewBasis != state.ReviewBasis || !current.Value.CanPrepare)
      return Fail<ReconciliationPreparationReceipt>(ErrorCodes.StaleRevision, "The source, generation or reporting period changed before save.");
    var calculation = await CalculateAsync(db, actor, state, fields, ct);
    if (!calculation.Succeeded) return Fail<ReconciliationPreparationReceipt>(calculation.ErrorCode!, calculation.Message!);
    var create = await AccountingAnalysisService.CreateReconciliationAsync(db, actor,
      new(state.ClientId, engagementId, state.PeriodId, fields.BookId, fields.Area, fields.SourceKind == "TRIAL_BALANCE" ? fields.SourceId : null,
        fields.SourceKind == "GENERAL_LEDGER" ? fields.SourceId : null, CanonicalCodes(fields.AccountCodes), fields.AsOfDate,
        fields.AgingBasis, fields.AgingBucketRuleVersion), ct);
    if (!create.Succeeded) return Fail<ReconciliationPreparationReceipt>(create.ErrorCode!, create.Message!);
    var created = await db.AccountingReconciliations.SingleAsync(x => x.FirmId == actor.FirmId && x.Id == create.Value, ct);
    var receipt = new AccountingReconciliationPreparation { Id = Guid.CreateVersion7(), FirmId = actor.FirmId,
      ClientId = state.ClientId, EngagementId = engagementId, ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch,
      RequestId = request.RequestId, RequestHash = hash, ReviewBasis = state.ReviewBasis, ReconciliationId = created.Id,
      InputJson = JsonSerializer.Serialize(fields), Reason = request.Reason.Trim(), EvidenceReference = request.EvidenceReference.Trim(), CreatedAt = DateTimeOffset.UtcNow };
    db.AccountingReconciliationPreparations.Add(receipt);
    var finalState = await StateAsync(db, actor, engagementId, fields.SourceKind, fields.SourceId, ct);
    if (!finalState.Succeeded || finalState.Value!.ReviewBasis != state.ReviewBasis)
      return Fail<ReconciliationPreparationReceipt>(ErrorCodes.StaleRevision, "The source changed before the receipt could be committed.");
    await db.SaveChangesAsync(ct);
    var result = ToReceipt(receipt, created);
    var finalAuth = await Authorize(db, actor, state.ClientId, engagementId, ct);
    if (!finalAuth.Succeeded) return Fail<ReconciliationPreparationReceipt>(finalAuth.ErrorCode!, finalAuth.Message!);
    await tx.CommitAsync(ct);
    return CommandResult<ReconciliationPreparationReceipt>.Ok(result);
  }

  private static async Task<ReconciliationPreparationReceipt> ReceiptAsync(IClientAccountingDbContext db,
    ActorContext actor, AccountingReconciliationPreparation row, CancellationToken ct)
  {
    var reconciliation = await db.AccountingReconciliations.AsNoTracking().SingleAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == row.ClientId && x.EngagementId == row.EngagementId && x.Id == row.ReconciliationId, ct);
    return ToReceipt(row, reconciliation);
  }

  private static ReconciliationPreparationReceipt ToReceipt(AccountingReconciliationPreparation row, AccountingReconciliation r) =>
    new(row.Id, row.RequestId, row.RequestHash, r.Id, row.ActorId, row.Reason, row.EvidenceReference, row.CreatedAt,
      new(r.Id, r.Revision, r.SupersedesReconciliationId, r.Status, Exact(r.SourceTotal), Exact(r.GlTotal), Exact(r.Residual),
        r.SourceHash, row.ActorId, r.CreatedAt));

  private sealed record Totals(decimal SourceTotal, decimal GlTotal);
  private static async Task<CommandResult<Totals>> CalculateAsync(IClientAccountingDbContext db, ActorContext actor,
    ReconciliationPreparationState state, ReconciliationPreparationFields fields, CancellationToken ct)
  {
    if (!ValidFields(fields) || fields.SourceKind != state.SourceKind || fields.SourceId != state.SourceId)
      return Fail<Totals>(ErrorCodes.Accounting.ReconciliationRejected, "The source and account selection are invalid.");
    var codes = CanonicalCodes(fields.AccountCodes);
    decimal sourceTotal; decimal glTotal;
    if (fields.SourceKind == "TRIAL_BALANCE")
    {
      var source = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == state.ClientId &&
        x.EngagementId == state.EngagementId && x.Id == fields.SourceId && x.PeriodId == fields.PeriodId && x.BookId == fields.BookId &&
        x.ValidationStatus == "Accepted" && x.ImportState == TrialBalanceImportStates.Sealed, ct);
      if (source is null || source.Basis != state.Basis || source.Currency != state.Currency)
        return Fail<Totals>(ErrorCodes.ScopeDenied, "The selected trial balance is unavailable in this scope.");
      var rows = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == source.Id && codes.Contains(x.AccountCode)).ToListAsync(ct);
      if (rows.Select(x => x.AccountCode).Distinct(StringComparer.Ordinal).Count() != codes.Length)
        return Fail<Totals>(ErrorCodes.Accounting.ReconciliationRejected, "The selected source does not contain every requested account code.");
      sourceTotal = MoneyPolicy.Normalize(rows.Sum(x => x.Amount)); glTotal = sourceTotal;
    }
    else
    {
      var batch = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == state.ClientId &&
        x.EngagementId == state.EngagementId && x.Id == fields.SourceId && x.PeriodId == fields.PeriodId && x.BookId == fields.BookId &&
        x.SourceKind == AccountingSourceKinds.GeneralLedger && x.Status == "SEALED" && x.Currency == state.Currency, ct);
      if (batch is null) return Fail<Totals>(ErrorCodes.ScopeDenied, "The selected general ledger is unavailable in this scope.");
      var lines = await db.GeneralLedgerLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == state.ClientId &&
        x.EngagementId == state.EngagementId && x.ImportBatchId == batch.Id && codes.Contains(x.AccountCode)).ToListAsync(ct);
      if (lines.Select(x => x.AccountCode).Distinct(StringComparer.OrdinalIgnoreCase).Count() != codes.Length)
        return Fail<Totals>(ErrorCodes.Accounting.ReconciliationRejected, "The selected ledger does not contain every requested account code.");
      sourceTotal = MoneyPolicy.Normalize(lines.Sum(x => x.FunctionalAmount));
      glTotal = MoneyPolicy.Normalize(lines.Sum(x => x.Debit - x.Credit));
    }
    var auth = await Authorize(db, actor, state.ClientId, state.EngagementId, ct);
    return auth.Succeeded ? CommandResult<Totals>.Ok(new(sourceTotal, glTotal)) : Fail<Totals>(auth.ErrorCode!, auth.Message!);
  }

  private static bool ValidRequest(ReconciliationPreparationRequest? request) => request is not null && request.RequestId != Guid.Empty &&
    Hash(request.ReviewBasis) && ValidFields(request.Fields) && request.Reason is { Length: > 0 and <= 4000 } && !string.IsNullOrWhiteSpace(request.Reason) &&
    request.EvidenceReference is { Length: > 0 and <= 2000 } && !string.IsNullOrWhiteSpace(request.EvidenceReference);

  private static bool ValidFields(ReconciliationPreparationFields? fields) => fields is not null &&
    fields.SourceKind is "TRIAL_BALANCE" or "GENERAL_LEDGER" && fields.SourceId != Guid.Empty && fields.PeriodId != Guid.Empty &&
    fields.Area is { Length: > 0 and <= 80 } && !string.IsNullOrWhiteSpace(fields.Area) && fields.AsOfDate != default &&
    fields.AccountCodes is { Count: > 0 and <= MaxCodes } && fields.AccountCodes.All(x => x is { Length: > 0 and <= 100 } && !string.IsNullOrWhiteSpace(x)) &&
    fields.AccountCodes.Select(x => x.Trim()).Distinct(StringComparer.Ordinal).Count() == fields.AccountCodes.Count &&
    fields.AgingBasis is { Length: <= 30 } && fields.AgingBucketRuleVersion is { Length: <= 60 };

  private static string[] CanonicalCodes(IReadOnlyList<string> codes) => codes.Select(x => x.Trim()).OrderBy(x => x, StringComparer.Ordinal).ToArray();
  private static string? ValidDigest(string? value) => Hash(value) ? value!.ToLowerInvariant() : null;
}
