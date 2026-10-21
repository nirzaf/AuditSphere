using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record ReviseTimeEntryRequest(
  Guid TimeEntryId,
  Guid RequestId,
  long ExpectedRevision,
  string Kind,
  DateOnly WorkDate,
  int StartMinute,
  int DurationMinutes,
  string Activity,
  string Narrative,
  string Reason);

/// <summary>Revision and approved-correction commands for attributable practice time.</summary>
public static partial class PracticeTimeService
{
  private const string EditUnapproved = "EDIT_UNAPPROVED";
  private const string CorrectApproved = "CORRECT_APPROVED";

  public static Task<CommandResult<Guid>> ReviseTimeEntryAsync(
    IAuditSphereDbContext db, ActorContext actor, ReviseTimeEntryRequest request, CancellationToken ct = default)
  {
    var validation = ValidateTimeChange(request);
    if (validation is not null) return Task.FromResult(CommandResult<Guid>.Fail("time.invalid", validation));
    return ReviseTimeEntryCoreAsync(db, actor, request.TimeEntryId, request.RequestId, request.ExpectedRevision,
      request.Kind.Trim().ToUpperInvariant(), request.WorkDate, request.StartMinute, request.DurationMinutes,
      request.Activity.Trim(), request.Narrative.Trim(), request.Reason.Trim(), null, ct);
  }

  internal static Task<CommandResult<Guid>> ReviseLegacyApprovedTimeAsync(
    IAuditSphereDbContext db, ActorContext actor, CorrectTimeRequest request, CancellationToken ct) =>
    ReviseTimeEntryCoreAsync(db, actor, request.TimeEntryId, Guid.CreateVersion7(), null, CorrectApproved,
      request.WorkDate, request.StartMinute, request.DurationMinutes, request.Activity.Trim(), request.Narrative.Trim(),
      TrimOrNull(request.Reason), request.NarrativeVisibility.Trim().ToUpperInvariant(), ct);

  private static string? ValidateTimeChange(ReviseTimeEntryRequest request)
  {
    if (request.TimeEntryId == Guid.Empty || request.RequestId == Guid.Empty || request.ExpectedRevision < 1)
      return "A time-entry identity, request identity and positive expected revision are required.";
    var kind = request.Kind?.Trim().ToUpperInvariant();
    if (kind is not (EditUnapproved or CorrectApproved)) return "The time-entry change kind is unsupported.";
    if (request.StartMinute is < 0 or > 1439 || request.DurationMinutes is < 1 or > 1440 ||
        request.StartMinute + request.DurationMinutes > 1440)
      return "Time must fit within one bounded 24-hour work date.";
    if (string.IsNullOrWhiteSpace(request.Activity)) return "Time activity is required.";
    if (request.Activity.Trim().Length > 20000) return "Time activity is limited to 20,000 characters.";
    if (request.Narrative is null || request.Narrative.Length > 2000)
      return "Time narrative is limited to 2,000 characters.";
    if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 2000)
      return "An attributable change reason from 1 to 2,000 characters is required.";
    return null;
  }

  private static async Task<CommandResult<Guid>> ReviseTimeEntryCoreAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid sourceId,
    Guid requestId,
    long? expectedRevision,
    string kind,
    DateOnly workDate,
    int startMinute,
    int durationMinutes,
    string activity,
    string narrative,
    string? reason,
    string? legacyVisibility,
    CancellationToken ct)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var initial = await db.TimeEntries.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == sourceId && x.FirmId == actor.FirmId, ct);
    if (initial is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (initial.TaskId == Guid.Empty)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var clientGuard = await LockClientAsync(db, actor.FirmId, initial.ClientId, ct);
    if (!clientGuard.Succeeded)
      return CommandResult<Guid>.Fail(clientGuard.ErrorCode!, clientGuard.Message!);

    var task = await LoadTaskForUpdateAsync(db, actor.FirmId, initial.TaskId, ct);
    if (task is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    // The FOR UPDATE query can resolve to an already tracked identity. Reload only after its row lock is held.
    var trackedTask = db.WorkTasks.Attach(task);
    await trackedTask.ReloadAsync(ct);
    if (trackedTask.State == EntityState.Detached || !TaskBindingMatches(initial, task))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var entry = await LoadTimeForUpdateAsync(db, actor.FirmId, sourceId, ct);
    if (entry is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    // Refresh the locked source before any revision/state decision, preventing a cached identity from overwriting
    // a committed edit that landed after the initial scope read.
    var trackedEntry = db.TimeEntries.Attach(entry);
    await trackedEntry.ReloadAsync(ct);
    if (trackedEntry.State == EntityState.Detached || !EntryBindingMatches(initial, task, entry))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizeTaskAsync(db, actor, task, WorkRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (entry.UserId != actor.UserId &&
        !await IsElevatedAsync(db, actor, task.ClientId, task.EngagementId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Only the entry owner or an authorized manager can change time.");

    var normalizedActivity = activity.Trim();
    var normalizedNarrative = narrative.Trim();
    var normalizedReason = TrimOrNull(reason);
    var normalizedVisibility = legacyVisibility is null
      ? entry.NarrativeVisibility
      : legacyVisibility.Trim().ToUpperInvariant();
    var requestHash = Hashing.Sha256Hex(JsonSerializer.Serialize(new
    {
      Action = "PRACTICE_TIME_ENTRY_CHANGE",
      FirmId = actor.FirmId,
      ActorId = actor.UserId,
      ActorEpoch = actor.SessionEpoch,
      RequestId = requestId,
      SourceId = sourceId,
      Kind = kind,
      ExpectedRevision = expectedRevision,
      WorkDate = workDate,
      StartMinute = startMinute,
      DurationMinutes = durationMinutes,
      Activity = normalizedActivity,
      Narrative = normalizedNarrative,
      Reason = normalizedReason,
      NarrativeVisibility = legacyVisibility is null ? null : normalizedVisibility
    }));

    // Replay resolution precedes revision/state checks: a retry after a committed response loss returns the
    // original result even though its source is now at a later state or revision.
    var prior = await db.TimeEntryChangeReceipts.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (prior is not null)
    {
      if (prior.ActorEpoch != actor.SessionEpoch)
        return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The request belongs to an earlier session; refresh before recovering it.");
      if (prior.TimeEntryId != sourceId || !string.Equals(prior.RequestHash, requestHash, StringComparison.Ordinal))
        return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This request identity was already used for different time changes.");
      return CommandResult<Guid>.Ok(prior.ResultTimeEntryId);
    }

    if (expectedRevision.HasValue && entry.Revision != expectedRevision.Value)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The time entry changed; refresh it before revising.");
    if (entry.Revision == long.MaxValue)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "The time-entry revision cannot advance further.");
    if (kind == EditUnapproved && entry.Status is not (PracticeTimeStates.TimeDraft or PracticeTimeStates.TimeReturned))
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "Only draft or returned time can be edited in place.");
    if (kind == CorrectApproved && entry.Status != PracticeTimeStates.TimeApproved)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "Only approved time can be corrected.");

    if (kind == CorrectApproved && await db.BillingSourceAllocations.AnyAsync(x => x.FirmId == actor.FirmId &&
        x.SourceKind == "TIME" && x.SourceId == entry.Id && x.SourceRevision == entry.Revision, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState,
        "This approved time revision is reserved to an invoice and cannot be corrected.");

    var overlapRequest = new SaveTimeDraftRequest(entry.TaskId, workDate, startMinute, durationMinutes,
      entry.Role, normalizedActivity, entry.BillableClassification, normalizedNarrative, normalizedVisibility,
      entry.Currency ?? "QAR");
    if (await HasOverlapAsync(db, actor.FirmId, entry.UserId, overlapRequest, entry.Id, ct))
      return CommandResult<Guid>.Fail("time.overlap", "The revised entry overlaps another active entry.");

    var beforeJson = JsonSerializer.Serialize(entry);
    if (!SnapshotFitsReceiptLimit(beforeJson))
      return CommandResult<Guid>.Fail("time.invalid", "This retained time entry is too large to revise with a complete change receipt.");
    var sourceRevision = entry.Revision;
    var resultRevision = sourceRevision + 1;
    var createdAt = UtcMicrosecondNow();
    string sourceAfterJson;
    string resultJson;
    TimeEntry resultEntry;
    if (kind == EditUnapproved)
    {
      var changed = CopyTimeEntry(entry);
      changed.WorkDate = workDate;
      changed.StartMinute = startMinute;
      changed.DurationMinutes = durationMinutes;
      changed.Activity = normalizedActivity;
      changed.Narrative = normalizedNarrative;
      changed.Revision = resultRevision;
      sourceAfterJson = JsonSerializer.Serialize(changed);
      resultJson = sourceAfterJson;
      resultEntry = changed;
    }
    else
    {
      var sourceAfter = CopyTimeEntry(entry);
      sourceAfter.Status = PracticeTimeStates.TimeSuperseded;
      resultEntry = new TimeEntry
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = entry.ClientId,
        EngagementId = entry.EngagementId, TaskId = entry.TaskId, UserId = entry.UserId,
        WorkDate = workDate, StartMinute = startMinute, DurationMinutes = durationMinutes,
        Role = entry.Role, Activity = normalizedActivity, Phase = entry.Phase, RiskArea = entry.RiskArea,
        MappingVersionId = entry.MappingVersionId, FsliCode = entry.FsliCode,
        BillableClassification = entry.BillableClassification,
        Narrative = normalizedNarrative, NarrativeVisibility = normalizedVisibility,
        Currency = entry.Currency, Revision = resultRevision, SupersedesId = entry.Id,
        RateCardVersionId = entry.RateCardVersionId, RatePerHour = entry.RatePerHour,
        CorrectionReason = normalizedReason, CreatedAt = createdAt
      };
      sourceAfterJson = JsonSerializer.Serialize(sourceAfter);
      resultJson = JsonSerializer.Serialize(resultEntry);
    }

    if (!ReceiptSnapshotsFitLimit(beforeJson, sourceAfterJson, resultJson))
      return CommandResult<Guid>.Fail("time.invalid", "The complete time-change receipt exceeds its supported size.");

    // Validate every complete UTF-8 snapshot before mutating the tracked source or adding a correction row.
    if (kind == EditUnapproved)
    {
      entry.WorkDate = workDate;
      entry.StartMinute = startMinute;
      entry.DurationMinutes = durationMinutes;
      entry.Activity = normalizedActivity;
      entry.Narrative = normalizedNarrative;
      entry.Revision = resultRevision;
    }
    else
    {
      entry.Status = PracticeTimeStates.TimeSuperseded;
      db.TimeEntries.Add(resultEntry);
    }

    var actorName = await db.Users.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == actor.UserId)
      .Select(x => x.DisplayName).SingleOrDefaultAsync(ct);
    db.TimeEntryChangeReceipts.Add(new TimeEntryChangeReceipt
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch,
      ActorName = actorName, RequestId = requestId,
      Kind = kind, TimeEntryId = entry.Id, ResultTimeEntryId = resultEntry.Id,
      SourceRevision = sourceRevision, ResultRevision = resultRevision, RequestHash = requestHash,
      BeforeJson = beforeJson, SourceAfterJson = sourceAfterJson, ResultJson = resultJson,
      Reason = normalizedReason, CreatedAt = createdAt
    });

    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(resultEntry.Id);
  }

  private static bool TaskBindingMatches(TimeEntry initial, WorkTask task) =>
    initial.FirmId == task.FirmId && initial.TaskId == task.Id &&
    initial.ClientId == task.ClientId && initial.EngagementId == task.EngagementId;

  private static bool EntryBindingMatches(TimeEntry initial, WorkTask task, TimeEntry locked) =>
    TaskBindingMatches(initial, task) && locked.FirmId == task.FirmId && locked.TaskId == task.Id &&
    locked.ClientId == task.ClientId && locked.EngagementId == task.EngagementId &&
    locked.FirmId == initial.FirmId && locked.TaskId == initial.TaskId &&
    locked.ClientId == initial.ClientId && locked.EngagementId == initial.EngagementId;

  private static DateTimeOffset UtcMicrosecondNow()
  {
    var utcTicks = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
    return new DateTimeOffset(utcTicks - utcTicks % 10, TimeSpan.Zero);
  }

  private static bool SnapshotFitsReceiptLimit(string? json) =>
    !string.IsNullOrEmpty(json) && Encoding.UTF8.GetByteCount(json) <= 64_000;

  private static bool ReceiptSnapshotsFitLimit(string? before, string? sourceAfter, string? result) =>
    SnapshotFitsReceiptLimit(before) && SnapshotFitsReceiptLimit(sourceAfter) && SnapshotFitsReceiptLimit(result);

  private static TimeEntry CopyTimeEntry(TimeEntry entry) => new()
  {
    Id = entry.Id, FirmId = entry.FirmId, ClientId = entry.ClientId, EngagementId = entry.EngagementId,
    TaskId = entry.TaskId, UserId = entry.UserId, WorkDate = entry.WorkDate, StartMinute = entry.StartMinute,
    DurationMinutes = entry.DurationMinutes, Role = entry.Role, Activity = entry.Activity, Phase = entry.Phase,
    RiskArea = entry.RiskArea, MappingVersionId = entry.MappingVersionId, FsliCode = entry.FsliCode,
    BillableClassification = entry.BillableClassification, Narrative = entry.Narrative,
    NarrativeVisibility = entry.NarrativeVisibility, Status = entry.Status, Currency = entry.Currency,
    Revision = entry.Revision, SupersedesId = entry.SupersedesId, RateCardVersionId = entry.RateCardVersionId,
    RatePerHour = entry.RatePerHour, SubmittedAt = entry.SubmittedAt, SubmissionRound = entry.SubmissionRound,
    LastReturnReason = entry.LastReturnReason, ApprovedByUserId = entry.ApprovedByUserId,
    ApprovedAt = entry.ApprovedAt, CorrectionReason = entry.CorrectionReason, CreatedAt = entry.CreatedAt
  };
}
