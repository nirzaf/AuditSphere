using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

/// <summary>Owns the whole local capture transaction. Neither a preview nor a missing receipt is a lease.</summary>
public static partial class AuditScheduleSourceWorkspace
{
  private const string CommandVersion = "audit-schedule-source-command.v1";
  private const string ReceiptVersion = "audit-schedule-source-receipt.v1";
  private const int MaxEnvelopeBytes = 65_536;

  private sealed record CaptureIntent(string Version, string Action, Guid FirmId, Guid ClientId,
    Guid EngagementId, Guid ActorUserId, long ActorEpoch, Guid RequestId, string ExpectedManifestHash,
    long ExpectedInputGeneration, long ExpectedPeriodRevision, AuditScheduleSourceFields Fields);

  public static async Task<CommandResult<AuditScheduleCapturePreview>> PreviewCaptureAsync(
    IClientAccountingDbContext db, ActorContext actor, IAuditScheduleSourceContentReader reader,
    Guid engagementId, Guid requestId, AuditScheduleSourceFields fields, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !ValidFields(fields)) return Invalid<AuditScheduleCapturePreview>();
    var auth = await AuditFieldworkService.AuthorizeSchedulePreparationAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded) return Failure<Guid, AuditScheduleCapturePreview>(auth);
    var canonical = Canonical(fields);
    var source = await ResolveAsync(db, actor, reader, auth.Value, engagementId, canonical, ct);
    if (!source.Succeeded) return Failure<AuditScheduleIntakeSourceFacts, AuditScheduleCapturePreview>(source);
    var facts = source.Value!;
    var json = IntentJson(actor, auth.Value, engagementId, requestId, canonical,
      facts.ManifestSha256, facts.InputGeneration, facts.PeriodRevision);
    if (!Bounded(json)) return Invalid<AuditScheduleCapturePreview>();
    return CommandResult<AuditScheduleCapturePreview>.Ok(new(requestId, Hashing.Sha256Hex(json),
      facts.ManifestSha256, Exact(facts.InputGeneration), Exact(facts.PeriodRevision), canonical,
      facts.FileName, facts.RawFileSha256, Exact(facts.Rows.Count), facts.Currency, facts.LegalEntityKey,
      facts.SourceDate, Money(facts.SignedTotal), Money(facts.ClosingControl), Money(facts.Residual),
      facts.HasCompleteListingCoverage, facts.AllAccountsReconciled,
      facts.AccountTieOuts.Select(x => new AuditScheduleSourceAccountView(x.AccountCode,
        Money(x.ListingAmount), Money(x.TrialBalanceAmount), Money(x.Difference),
        Exact(x.ListingRowCount), Exact(x.TrialBalanceRowCount))).ToArray()));
  }

  public static async Task<CommandResult<AuditScheduleSourceReceiptView>> CaptureAsync(
    IClientAccountingDbContext db, ActorContext actor, IAuditScheduleSourceContentReader reader,
    Guid engagementId, AuditScheduleCaptureRequest request, CancellationToken ct = default)
  {
    if (request is null || request.RequestId == Guid.Empty || !ValidFields(request.Fields) ||
        !Hash(request.ExpectedManifestHash) || !Hash(request.ExpectedRequestHash) ||
        request.ExpectedInputGeneration < 1 || request.ExpectedPeriodRevision < 1 || !request.Reviewed)
      return Invalid<AuditScheduleSourceReceiptView>();
    if (db is not DbContext context || context.ChangeTracker.HasChanges() || db.Database.CurrentTransaction is not null)
      return CommandResult<AuditScheduleSourceReceiptView>.Fail(ErrorCodes.ProtectedState,
        "Capture requires a clean command context and its own transaction.");
    var originalTrackedEntities = context.ChangeTracker.Entries()
      .Select(x => x.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
    var auth = await AuditFieldworkService.AuthorizeSchedulePreparationAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded) return Failure<Guid, AuditScheduleSourceReceiptView>(auth);
    var clientId = auth.Value;
    var fields = Canonical(request.Fields);
    var json = IntentJson(actor, clientId, engagementId, request.RequestId, fields,
      request.ExpectedManifestHash, request.ExpectedInputGeneration, request.ExpectedPeriodRevision);
    if (!Bounded(json) || Hashing.Sha256Hex(json) != request.ExpectedRequestHash)
      return CommandResult<AuditScheduleSourceReceiptView>.Fail(ErrorCodes.ManifestMismatch,
        "Review the exact current capture intent before publishing it.");

    reader = await AuditScheduleIntakeSources.PrefetchRemoteSourceAsync(db, actor, reader, clientId, engagementId,
      fields.PbcAcceptanceReceiptId, ct);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    Guid? ownedSchedule = null;
    var completed = false;
    try
    {
      await AuditFieldworkService.LockConfirmationScopeAsync(db, actor, engagementId, ct);
      auth = await AuditFieldworkService.AuthorizeSchedulePreparationAsync(db, actor, engagementId, ct);
      if (!auth.Succeeded || auth.Value != clientId)
        return CommandResult<AuditScheduleSourceReceiptView>.Fail(auth.ErrorCode ?? ErrorCodes.ScopeDenied,
          auth.Message ?? "Access denied.");
      // Serialize with the original publisher and recover its exact outcome BEFORE mutable source reads.
      var existing = await db.AuditScheduleSourceCaptures.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ActorUserId == actor.UserId && x.ActorEpoch == actor.SessionEpoch &&
        x.RequestId == request.RequestId, ct);
      if (existing is not null)
      {
        if (existing.ClientId != clientId || existing.EngagementId != engagementId ||
            existing.RequestHash != request.ExpectedRequestHash || existing.RequestJson != json)
          return Conflict<AuditScheduleSourceReceiptView>();
        var recovered = ReadCaptureReceipt(existing);
        if (!recovered.Succeeded) return recovered;
        auth = await AuditFieldworkService.AuthorizeSchedulePreparationAsync(db, actor, engagementId, ct);
        if (!auth.Succeeded) return Failure<Guid, AuditScheduleSourceReceiptView>(auth);
        await tx.CommitAsync(ct);
        completed = true;
        return recovered;
      }
      var locked = await LockSourceAsync(db, actor, clientId, engagementId, fields, ct);
      if (!locked.Succeeded)
        return CommandResult<AuditScheduleSourceReceiptView>.Fail(locked.ErrorCode!, locked.Message!);
      var source = await ResolveAsync(db, actor, reader, clientId, engagementId, fields, ct);
      if (!source.Succeeded) return Failure<AuditScheduleIntakeSourceFacts, AuditScheduleSourceReceiptView>(source);
      var facts = source.Value!;
      if (!Matches(facts, request)) return Stale<AuditScheduleSourceReceiptView>();
      var reference = $"audit-source/{request.RequestId:D}";
      if (await db.AuditSchedules.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
          x.EngagementId == engagementId && x.SourceReceiptReference == reference, ct))
        return Conflict<AuditScheduleSourceReceiptView>();
      var created = await AuditFieldworkService.CreateScheduleInTransactionAsync(db, actor,
        new(engagementId, facts.ScheduleType, facts.LegalEntityKey, reference,
          new DateTimeOffset(fields.SourceDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
          facts.PeriodStart, facts.PeriodEnd, facts.Currency, "debits positive; credits negative", facts.RawFileSha256,
          facts.ClosingControl, facts.Rows), ct);
      if (!created.Succeeded) return Failure<ScheduleValue, AuditScheduleSourceReceiptView>(created);
      ownedSchedule = created.Value!.ScheduleId;
      var bound = await AuditScheduleSourceBindingService.BindScheduleToAcceptedSourceInTransactionAsync(
        db, actor, ownedSchedule.Value, facts.AcceptedTbDecisionId, ct);
      if (!bound.Succeeded) return Failure<AuditScheduleSourceBindingValue, AuditScheduleSourceReceiptView>(bound);
      var schedule = await db.AuditSchedules.SingleAsync(x => x.FirmId == actor.FirmId && x.Id == ownedSchedule, ct);
      schedule.Status = facts.AllAccountsReconciled ? AuditScheduleStatuses.Reconciled : AuditScheduleStatuses.Unreconciled;
      var ticks = DateTimeOffset.UtcNow.UtcTicks;
      var createdAt = new DateTimeOffset(ticks - ticks % 10, TimeSpan.Zero);
      var receipt = new AuditScheduleSourceReceipt(ReceiptVersion, "CAPTURE", schedule.Id, null,
        actor.FirmId, clientId, engagementId, actor.UserId, Exact(actor.SessionEpoch), request.RequestId,
        request.ExpectedRequestHash, facts.ManifestSha256, Exact(facts.InputGeneration), Exact(facts.Rows.Count),
        Money(facts.SignedTotal), Money(facts.ClosingControl), Money(facts.Residual), schedule.Status, createdAt);
      var receiptJson = JsonSerializer.Serialize(receipt);
      if (!Bounded(receiptJson)) return Invalid<AuditScheduleSourceReceiptView>();
      var capture = new AuditScheduleSourceCapture
      {
        ScheduleId = schedule.Id, FirmId = actor.FirmId, ClientId = clientId, EngagementId = engagementId,
        RequestId = request.RequestId, ActorUserId = actor.UserId, ActorEpoch = actor.SessionEpoch,
        PbcAcceptanceReceiptId = facts.PbcAcceptanceReceiptId, AcceptedSourceDecisionId = facts.AcceptedTbDecisionId,
        TrialBalanceDatasetId = facts.TrialBalanceDatasetId, PeriodId = facts.PeriodId, PeriodRevision = facts.PeriodRevision,
        BookId = facts.BookId, Basis = facts.Basis, LegalEntityKey = facts.LegalEntityKey, Currency = facts.Currency,
        ProfileVersion = facts.ProfileVersion, SourceDate = facts.SourceDate, InputGeneration = facts.InputGeneration,
        RequestJson = json, RequestHash = request.ExpectedRequestHash, ReceiptJson = receiptJson,
        ReceiptHash = Hashing.Sha256Hex(receiptJson), ManifestJson = facts.ManifestJson,
        ManifestHash = facts.ManifestSha256, CreatedAt = createdAt
      };
      db.AuditScheduleSourceCaptures.Add(capture);
      await db.SaveChangesAsync(ct);
      var finalSource = await ResolveAsync(db, actor, reader, clientId, engagementId, fields, ct);
      if (!finalSource.Succeeded) return Failure<AuditScheduleIntakeSourceFacts, AuditScheduleSourceReceiptView>(finalSource);
      if (!Matches(finalSource.Value!, request)) return Stale<AuditScheduleSourceReceiptView>();
      auth = await AuditFieldworkService.AuthorizeSchedulePreparationAsync(db, actor, engagementId, ct);
      if (!auth.Succeeded || auth.Value != clientId)
        return CommandResult<AuditScheduleSourceReceiptView>.Fail(auth.ErrorCode ?? ErrorCodes.ScopeDenied,
          auth.Message ?? "Access denied.");
      await tx.CommitAsync(ct);
      completed = true;
      return CommandResult<AuditScheduleSourceReceiptView>.Ok(new(receipt, capture.ReceiptHash));
    }
    finally
    {
      if (!completed)
      {
        // Creation may fail after staging rows but before it returns their schedule ID.
        // Detach only entities introduced by this command, including that early-failure path.
        foreach (var entry in context.ChangeTracker.Entries().Where(x =>
          !originalTrackedEntities.Contains(x.Entity) && (x.Entity switch
        {
          AuditScheduleRow row => row.EngagementId == engagementId && row.FirmId == actor.FirmId,
          AuditScheduleSourceCapture capture => capture.EngagementId == engagementId && capture.FirmId == actor.FirmId,
          AuditSchedule schedule => schedule.EngagementId == engagementId && schedule.FirmId == actor.FirmId,
          _ => false
        })).ToArray()) entry.State = EntityState.Detached;
      }
    }
  }

  private static CommandResult<AuditScheduleSourceReceiptView> ReadCaptureReceipt(AuditScheduleSourceCapture capture)
  {
    try
    {
      var value = JsonSerializer.Deserialize<AuditScheduleSourceReceipt>(capture.ReceiptJson);
      if (value is null || Hashing.Sha256Hex(capture.ReceiptJson) != capture.ReceiptHash ||
          value.Version != ReceiptVersion || value.Action != "CAPTURE" || value.ReviewId is not null ||
          value.ScheduleId != capture.ScheduleId || value.FirmId != capture.FirmId || value.ClientId != capture.ClientId ||
          value.EngagementId != capture.EngagementId || value.ActorUserId != capture.ActorUserId ||
          value.ActorEpoch != Exact(capture.ActorEpoch) || value.RequestId != capture.RequestId ||
          value.RequestHash != capture.RequestHash || value.ManifestHash != capture.ManifestHash ||
          value.CreatedAt != capture.CreatedAt)
        return CommandResult<AuditScheduleSourceReceiptView>.Fail(ErrorCodes.ProtectedState, "The retained receipt is inconsistent.");
      return CommandResult<AuditScheduleSourceReceiptView>.Ok(new(value, capture.ReceiptHash));
    }
    catch (JsonException)
    {
      return CommandResult<AuditScheduleSourceReceiptView>.Fail(ErrorCodes.ProtectedState, "The retained receipt is unreadable.");
    }
  }

  private static Task<CommandResult<AuditScheduleIntakeSourceFacts>> ResolveAsync(
    IClientAccountingDbContext db, ActorContext actor, IAuditScheduleSourceContentReader reader,
    Guid clientId, Guid engagementId, AuditScheduleSourceFields fields, CancellationToken ct) =>
    AuditScheduleIntakeSources.ResolveAsync(db, actor, reader, clientId, engagementId, fields.PbcAcceptanceReceiptId,
      fields.AcceptedSourceDecisionId, fields.SourceDate, fields.Columns, fields.SelectedControlAccountCodes, ct);

  private static Task<CommandResult> LockSourceAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid engagementId, AuditScheduleSourceFields fields, CancellationToken ct) =>
    LockSourceAsync(db, actor.FirmId, clientId, engagementId, fields, ct);

  internal static async Task<CommandResult> LockSourceAsync(IClientAccountingDbContext db, Guid firmId,
    Guid clientId, Guid engagementId, AuditScheduleSourceFields fields, CancellationToken ct)
  {
    var receipt = await db.PbcAcceptanceReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId &&
      x.Id == fields.PbcAcceptanceReceiptId, ct);
    if (receipt is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var pbc = await db.PbcRequests.FromSqlInterpolated(
      $"SELECT * FROM pbc_requests WHERE firm_id = {firmId} AND client_id = {clientId} AND engagement_id = {engagementId} AND id = {receipt.PbcRequestId} FOR SHARE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (pbc is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var decision = await db.SourceAcceptanceDecisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId &&
      x.Id == fields.AcceptedSourceDecisionId && x.SourceKind == "TB", ct);
    if (decision?.TrialBalanceDatasetId is not { } datasetId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && x.Id == datasetId, ct);
    if (dataset?.PeriodId is not { } periodId)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The source reporting period is unavailable.");
    // Native ancestry obtains its existing ordered read fences before any descendant book lock.
    var native = await NativeTrialBalanceSourceWorkspace.CheckCurrentAsync(db, dataset, ct);
    if (!native.Succeeded) return native;
    var period = await db.ClientReportingPeriods.FromSqlInterpolated(
      $"SELECT * FROM client_reporting_periods WHERE firm_id = {firmId} AND client_id = {clientId} AND id = {periodId} FOR SHARE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (period is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (dataset.BookId is { } bookId)
    {
      var book = await db.ClientReportingBooks.FromSqlInterpolated(
        $"SELECT * FROM client_reporting_books WHERE firm_id = {firmId} AND client_id = {clientId} AND period_id = {periodId} AND id = {bookId} FOR SHARE")
        .AsNoTracking().SingleOrDefaultAsync(ct);
      if (book is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    }
    return CommandResult.Ok();
  }

  private static string IntentJson(ActorContext actor, Guid clientId, Guid engagementId, Guid requestId,
    AuditScheduleSourceFields fields, string manifestHash, long generation, long periodRevision) =>
    JsonSerializer.Serialize(new CaptureIntent(CommandVersion, "CAPTURE", actor.FirmId, clientId, engagementId,
      actor.UserId, actor.SessionEpoch, requestId, manifestHash, generation, periodRevision, fields));
  private static bool Matches(AuditScheduleIntakeSourceFacts facts, AuditScheduleCaptureRequest request) =>
    facts.ManifestSha256 == request.ExpectedManifestHash && facts.InputGeneration == request.ExpectedInputGeneration &&
    facts.PeriodRevision == request.ExpectedPeriodRevision;
  private static AuditScheduleSourceFields Canonical(AuditScheduleSourceFields fields) => fields with
    { SelectedControlAccountCodes = fields.SelectedControlAccountCodes.Order(StringComparer.Ordinal).ToArray() };
  private static bool ValidFields(AuditScheduleSourceFields fields) => fields is not null &&
    fields.PbcAcceptanceReceiptId != Guid.Empty && fields.AcceptedSourceDecisionId != Guid.Empty && fields.Columns is not null &&
    fields.SelectedControlAccountCodes is { Count: > 0 and <= 20_000 } &&
    fields.SelectedControlAccountCodes.All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 100 && x == x.Trim()) &&
    fields.SelectedControlAccountCodes.Distinct(StringComparer.Ordinal).Count() == fields.SelectedControlAccountCodes.Count;
  private static bool Hash(string value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static bool Bounded(string value) => Encoding.UTF8.GetByteCount(value) <= MaxEnvelopeBytes;
  private static string Exact(long value) => value.ToString(CultureInfo.InvariantCulture);
  private static string Money(decimal value) => value.ToString("F6", CultureInfo.InvariantCulture);
  private static CommandResult<T> Invalid<T>() => CommandResult<T>.Fail(ErrorCodes.Accounting.MappingInvalid,
    "Provide a bounded exact source intent and review the current preview.");
  private static CommandResult<T> Conflict<T>() => CommandResult<T>.Fail(ErrorCodes.IdempotencyConflict,
    "This original request identity is already associated with different content.");
  private static CommandResult<T> Stale<T>() => CommandResult<T>.Fail(ErrorCodes.GenerationStale,
    "The source or reporting context changed. Refresh and review the exact source again.");
  private static CommandResult<TTo> Failure<TFrom, TTo>(CommandResult<TFrom> result) =>
    CommandResult<TTo>.Fail(result.ErrorCode!, result.Message!);
}
