using System.Data;
using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record TimeEntryChangeValues(
  DateOnly WorkDate,
  int StartMinute,
  int DurationMinutes,
  string Activity,
  string Narrative);

public sealed record TimeEntryChangeReceiptView(
  Guid Id,
  Guid ActorId,
  string? ActorName,
  string ActorEpoch,
  Guid RequestId,
  string RequestHash,
  string Kind,
  Guid TimeEntryId,
  Guid ResultTimeEntryId,
  string SourceRevision,
  string ResultRevision,
  string? Reason,
  DateTimeOffset CreatedAt,
  TimeEntryChangeValues Before,
  TimeEntryChangeValues After,
  string SourceStatusBefore,
  string SourceStatusAfter,
  string ResultStatus);

public sealed record TimeEntryChangeLookup(bool Found, TimeEntryChangeReceiptView? Receipt);

public sealed record TimeEntryChangePage(
  IReadOnlyList<TimeEntryChangeReceiptView> Items,
  int PageSize,
  bool HasMore,
  Guid? NextCursor);

/// <summary>Reads immutable time-change receipts within the entry's current exact task scope.</summary>
public static class PracticeTimeChangeQuery
{
  private const int HistoryPageSize = 20;
  private const string EditUnapproved = "EDIT_UNAPPROVED";
  private const string CorrectApproved = "CORRECT_APPROVED";
  private static readonly string[] WorkRoles = ["Senior", "Staff", "Manager", "Partner", "Administrator"];
  private static readonly string[] ApprovalRoles = ["Manager", "Partner", "Administrator"];

  private sealed record AuthorizedTarget(TimeEntry Entry, WorkTask Task);
  private readonly record struct EntryScope(Guid FirmId, Guid? ClientId, Guid? EngagementId, Guid TaskId, Guid UserId);
  private sealed record ParsedSnapshot(TimeEntry Entry, TimeEntryChangeValues Values);

  public static async Task<CommandResult<TimeEntryChangeLookup>> ReceiptAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid timeEntryId,
    Guid requestId,
    CancellationToken ct = default)
  {
    if (timeEntryId == Guid.Empty || requestId == Guid.Empty)
      return CommandResult<TimeEntryChangeLookup>.Fail("time.invalid", "A time-entry and request identity are required.");
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<TimeEntryChangeLookup>.Fail(ErrorCodes.GateBlocked,
        "Time-change history requires an independent read so current access can be rechecked.");

    TimeEntryChangeLookup lookup;
    EntryScope snapshotScope;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      var targetResult = await LoadAuthorizedTargetAsync(db, actor, timeEntryId, ct);
      if (!targetResult.Succeeded)
        return CommandResult<TimeEntryChangeLookup>.Fail(targetResult.ErrorCode!, targetResult.Message!);
      var target = targetResult.Value!;
      snapshotScope = ScopeOf(target.Entry);

      var receipt = await db.TimeEntryChangeReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
      if (receipt is null || receipt.TimeEntryId != timeEntryId && receipt.ResultTimeEntryId != timeEntryId)
      {
        lookup = new TimeEntryChangeLookup(false, null);
      }
      else
      {
        if (receipt.ActorEpoch != actor.SessionEpoch)
          return CommandResult<TimeEntryChangeLookup>.Fail(ErrorCodes.GenerationStale,
            "The change receipt belongs to an earlier session.");
        var linkedEntries = await LoadAndValidateLinkedEntriesAsync(db, actor.FirmId, [receipt], target.Entry, ct);
        if (linkedEntries is null || !TryProject(receipt, out var view))
          return CommandResult<TimeEntryChangeLookup>.Fail(ErrorCodes.GateBlocked,
            "The retained time-change evidence is incomplete or inconsistent.");
        lookup = new TimeEntryChangeLookup(true, view);
      }
      await snapshot.CommitAsync(ct);
    }

    var finalTarget = await LoadAuthorizedTargetAsync(db, actor, timeEntryId, ct);
    if (!finalTarget.Succeeded)
      return CommandResult<TimeEntryChangeLookup>.Fail(finalTarget.ErrorCode!, finalTarget.Message!);
    if (ScopeOf(finalTarget.Value!.Entry) != snapshotScope)
      return CommandResult<TimeEntryChangeLookup>.Fail(ErrorCodes.GenerationStale,
        "The time-entry scope changed while its history was being read.");
    return CommandResult<TimeEntryChangeLookup>.Ok(lookup);
  }

  public static async Task<CommandResult<TimeEntryChangePage>> HistoryAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid timeEntryId,
    Guid? beforeReceiptId = null,
    CancellationToken ct = default)
  {
    if (timeEntryId == Guid.Empty || beforeReceiptId == Guid.Empty)
      return CommandResult<TimeEntryChangePage>.Fail("time.invalid", "Use a nonempty time-entry and retained receipt identity.");
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<TimeEntryChangePage>.Fail(ErrorCodes.GateBlocked,
        "Time-change history requires an independent read so current access can be rechecked.");

    TimeEntryChangePage page;
    EntryScope snapshotScope;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      var targetResult = await LoadAuthorizedTargetAsync(db, actor, timeEntryId, ct);
      if (!targetResult.Succeeded)
        return CommandResult<TimeEntryChangePage>.Fail(targetResult.ErrorCode!, targetResult.Message!);
      var target = targetResult.Value!;
      snapshotScope = ScopeOf(target.Entry);

      var receipts = db.TimeEntryChangeReceipts.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        (x.TimeEntryId == timeEntryId || x.ResultTimeEntryId == timeEntryId));
      DateTimeOffset? cursorCreatedAt = null;
      if (beforeReceiptId.HasValue)
      {
        var cursor = await receipts.SingleOrDefaultAsync(x => x.Id == beforeReceiptId.Value, ct);
        if (cursor is null)
          return CommandResult<TimeEntryChangePage>.Fail("time.invalid", "The history cursor is not part of this entry's retained history.");
        cursorCreatedAt = cursor.CreatedAt;
      }
      if (beforeReceiptId.HasValue)
      {
        var cursorId = beforeReceiptId.Value;
        var createdAt = cursorCreatedAt!.Value;
        receipts = receipts.Where(x => x.CreatedAt < createdAt ||
          (x.CreatedAt == createdAt && x.Id.CompareTo(cursorId) < 0));
      }

      var selected = await receipts.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
        .Take(HistoryPageSize + 1).ToListAsync(ct);
      var hasMore = selected.Count > HistoryPageSize;
      var visible = selected.Take(HistoryPageSize).ToArray();
      var linkedEntries = await LoadAndValidateLinkedEntriesAsync(db, actor.FirmId, visible, target.Entry, ct);
      if (linkedEntries is null)
        return CommandResult<TimeEntryChangePage>.Fail(ErrorCodes.GateBlocked,
          "The retained time-change evidence is incomplete or inconsistent.");
      var items = new List<TimeEntryChangeReceiptView>(visible.Length);
      foreach (var receipt in visible)
      {
        if (!TryProject(receipt, out var view))
          return CommandResult<TimeEntryChangePage>.Fail(ErrorCodes.GateBlocked,
            "The retained time-change evidence is incomplete or inconsistent.");
        items.Add(view);
      }
      page = new TimeEntryChangePage(items, HistoryPageSize, hasMore,
        hasMore && visible.Length > 0 ? visible[^1].Id : null);
      await snapshot.CommitAsync(ct);
    }

    var finalTarget = await LoadAuthorizedTargetAsync(db, actor, timeEntryId, ct);
    if (!finalTarget.Succeeded)
      return CommandResult<TimeEntryChangePage>.Fail(finalTarget.ErrorCode!, finalTarget.Message!);
    if (ScopeOf(finalTarget.Value!.Entry) != snapshotScope)
      return CommandResult<TimeEntryChangePage>.Fail(ErrorCodes.GenerationStale,
        "The time-entry scope changed while its history was being read.");
    return CommandResult<TimeEntryChangePage>.Ok(page);
  }

  private static async Task<CommandResult<AuthorizedTarget>> LoadAuthorizedTargetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid timeEntryId, CancellationToken ct)
  {
    var entry = await db.TimeEntries.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == timeEntryId && x.FirmId == actor.FirmId, ct);
    if (entry is null) return CommandResult<AuthorizedTarget>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var task = await db.WorkTasks.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == entry.TaskId && x.FirmId == actor.FirmId, ct);
    if (task is null || entry.FirmId != task.FirmId || entry.TaskId != task.Id ||
        entry.ClientId != task.ClientId || entry.EngagementId != task.EngagementId)
      return CommandResult<AuthorizedTarget>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, task.ClientId, task.EngagementId, WorkRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<AuthorizedTarget>.Fail(auth.ErrorCode!, auth.Message!);
    if (entry.UserId != actor.UserId)
    {
      var elevated = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(actor.FirmId, task.ClientId, task.EngagementId, ApprovalRoles,
          InternalOnly: true, RequireFirmWide: task.ClientId is null && task.EngagementId is null), ct);
      if (!elevated.Succeeded)
        return CommandResult<AuthorizedTarget>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    }
    return CommandResult<AuthorizedTarget>.Ok(new AuthorizedTarget(entry, task));
  }

  private static async Task<Dictionary<Guid, TimeEntry>?> LoadAndValidateLinkedEntriesAsync(
    IAuditSphereDbContext db,
    Guid firmId,
    IReadOnlyCollection<TimeEntryChangeReceipt> receipts,
    TimeEntry target,
    CancellationToken ct)
  {
    var ids = receipts.SelectMany(x => new[] { x.TimeEntryId, x.ResultTimeEntryId }).Distinct().ToArray();
    if (ids.Any(x => x == Guid.Empty)) return null;
    var entries = await db.TimeEntries.AsNoTracking().Where(x => x.FirmId == firmId && ids.Contains(x.Id)).ToListAsync(ct);
    var byId = entries.ToDictionary(x => x.Id);
    if (byId.Count != ids.Length || byId.Values.Any(x => x.FirmId != target.FirmId ||
        x.ClientId != target.ClientId || x.EngagementId != target.EngagementId || x.TaskId != target.TaskId ||
        x.UserId != target.UserId)) return null;
    foreach (var receipt in receipts)
      if (receipt.FirmId != firmId || !byId.ContainsKey(receipt.TimeEntryId) || !byId.ContainsKey(receipt.ResultTimeEntryId))
        return null;
    return byId;
  }

  private static bool TryProject(TimeEntryChangeReceipt receipt, out TimeEntryChangeReceiptView view)
  {
    view = default!;
    if (receipt.Kind is not (EditUnapproved or CorrectApproved) || receipt.SourceRevision < 1 ||
        receipt.SourceRevision == long.MaxValue || receipt.ResultRevision != receipt.SourceRevision + 1 ||
        !TrySnapshot(receipt.BeforeJson, out var before) || !TrySnapshot(receipt.SourceAfterJson, out var sourceAfter) ||
        !TrySnapshot(receipt.ResultJson, out var result)) return false;

    var sourceAfterValid = sourceAfter.Entry.Id == receipt.TimeEntryId &&
      sourceAfter.Entry.Revision == receipt.SourceRevision &&
      sourceAfter.Entry.Status == PracticeTimeStates.TimeSuperseded;
    var resultValid = result.Entry.Id == receipt.ResultTimeEntryId && result.Entry.Revision == receipt.ResultRevision &&
      result.Entry.Status == PracticeTimeStates.TimeDraft && result.Entry.SupersedesId == receipt.TimeEntryId &&
      result.Entry.CorrectionReason == receipt.Reason;
    if (receipt.Kind == EditUnapproved)
    {
      sourceAfterValid = sourceAfter.Entry.Id == receipt.TimeEntryId &&
        sourceAfter.Entry.Revision == receipt.ResultRevision &&
        (before.Entry.Status is PracticeTimeStates.TimeDraft or PracticeTimeStates.TimeReturned) &&
        sourceAfter.Entry.Status == before.Entry.Status && sourceAfter.Values == result.Values;
      resultValid = receipt.ResultTimeEntryId == receipt.TimeEntryId &&
        result.Entry.Id == receipt.TimeEntryId && result.Entry.Revision == receipt.ResultRevision &&
        result.Entry.Status == before.Entry.Status && result.Entry.SupersedesId == before.Entry.SupersedesId &&
        result.Entry.NarrativeVisibility == before.Entry.NarrativeVisibility &&
        result.Entry.SubmissionRound == before.Entry.SubmissionRound && result.Entry.SubmittedAt == before.Entry.SubmittedAt &&
        result.Entry.LastReturnReason == before.Entry.LastReturnReason &&
        result.Entry.ApprovedByUserId == before.Entry.ApprovedByUserId && result.Entry.ApprovedAt == before.Entry.ApprovedAt &&
        result.Entry.CorrectionReason == before.Entry.CorrectionReason && result.Entry.CreatedAt == before.Entry.CreatedAt;
    }
    else
    {
      if (before.Entry.Status != PracticeTimeStates.TimeApproved) return false;
      sourceAfterValid &= sourceAfter.Values == before.Values;
    }
    if (before.Entry.Id != receipt.TimeEntryId || before.Entry.Revision != receipt.SourceRevision ||
        !sourceAfterValid || !resultValid || before.Entry.FirmId != receipt.FirmId ||
        sourceAfter.Entry.FirmId != receipt.FirmId || result.Entry.FirmId != receipt.FirmId ||
        sourceAfter.Entry.NarrativeVisibility != before.Entry.NarrativeVisibility ||
        !SameAttribution(before.Entry, sourceAfter.Entry) || !SameAttribution(before.Entry, result.Entry))
      return false;

    view = new TimeEntryChangeReceiptView(receipt.Id, receipt.ActorId, receipt.ActorName,
      receipt.ActorEpoch.ToString(CultureInfo.InvariantCulture),
      receipt.RequestId, receipt.RequestHash, receipt.Kind, receipt.TimeEntryId, receipt.ResultTimeEntryId,
      receipt.SourceRevision.ToString(CultureInfo.InvariantCulture), receipt.ResultRevision.ToString(CultureInfo.InvariantCulture),
      receipt.Reason, receipt.CreatedAt.ToUniversalTime(),
      before.Values, result.Values, before.Entry.Status, sourceAfter.Entry.Status, result.Entry.Status);
    return true;
  }

  private static bool TrySnapshot(string json, out ParsedSnapshot snapshot)
  {
    snapshot = default!;
    try
    {
      var entry = JsonSerializer.Deserialize<TimeEntry>(json);
      if (entry is null || entry.Id == Guid.Empty || entry.FirmId == Guid.Empty || entry.TaskId == Guid.Empty ||
          entry.UserId == Guid.Empty || entry.Revision < 1 || string.IsNullOrWhiteSpace(entry.Status) ||
          string.IsNullOrWhiteSpace(entry.Activity) || entry.Narrative is null || entry.StartMinute is < 0 or > 1439 ||
          entry.DurationMinutes is < 1 or > 1440 || entry.StartMinute + entry.DurationMinutes > 1440) return false;
      snapshot = new ParsedSnapshot(entry, new TimeEntryChangeValues(entry.WorkDate, entry.StartMinute,
        entry.DurationMinutes, entry.Activity, entry.Narrative));
      return true;
    }
    catch (JsonException)
    {
      return false;
    }
  }

  private static EntryScope ScopeOf(TimeEntry entry) =>
    new(entry.FirmId, entry.ClientId, entry.EngagementId, entry.TaskId, entry.UserId);

  private static bool SameAttribution(TimeEntry left, TimeEntry right) =>
    left.FirmId == right.FirmId && left.ClientId == right.ClientId && left.EngagementId == right.EngagementId &&
    left.TaskId == right.TaskId && left.UserId == right.UserId && left.Role == right.Role && left.Phase == right.Phase &&
    left.RiskArea == right.RiskArea && left.MappingVersionId == right.MappingVersionId && left.FsliCode == right.FsliCode &&
    left.BillableClassification == right.BillableClassification &&
    left.Currency == right.Currency && left.RateCardVersionId == right.RateCardVersionId && left.RatePerHour == right.RatePerHour;
}
