using System.Data;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

public sealed record EngagementActivityRequest(
  DateOnly? From = null,
  DateOnly? To = null,
  DateTimeOffset? SnapshotAtUtc = null,
  DateTimeOffset? AfterAtUtc = null,
  int? AfterSource = null,
  Guid? AfterId = null,
  int PageSize = 50);

public sealed record EngagementActivityCursor(DateTimeOffset AtUtc, int Source, Guid Id);

public sealed record EngagementActivityItem(Guid Id, int Source, string Kind, string Label,
  Guid? ActorUserId, DateTimeOffset AtUtc, string? CurrentStatus, string SourceHref);

public sealed record EngagementActivityPage(Guid EngagementId, DateOnly From, DateOnly To,
  DateTimeOffset SnapshotAtUtc, IReadOnlyList<EngagementActivityItem> Items,
  bool HasMore, EngagementActivityCursor? NextCursor);

public static partial class EngagementCommunicationTimelineQuery
{
  private const int PbcCommunicationSource = 1;
  private const int PbcUploadSource = 2;
  private const int WorkflowTaskSource = 3;
  private const int ReviewNoteSource = 4;
  private const int ReviewNoteEventSource = 5;
  private const int CorrespondenceSource = 6;
  private const int CommercialNotificationSource = 7;
  private const string ActivityCursorInvalidCode = "activity.cursor-invalid";
  private const string ActivityCursorInvalidMessage = "Supply a complete activity cursor and a page size from 1 to 100.";
  private const string ActivityRangeInvalidCode = "activity.range-invalid";
  private const string ActivityRangeInvalidMessage = "Choose a UTC date range no longer than 90 days.";

  public static async Task<CommandResult<EngagementActivityPage>> GetActivityAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId,
    EngagementActivityRequest request, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Id == engagementId)
      .Select(x => new { x.Id, x.PracticeClientId }).SingleOrDefaultAsync(ct);
    if (engagement is null)
      return CommandResult<EngagementActivityPage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizeAsync(db, actor, engagement.PracticeClientId, engagementId, ct);
    if (!auth.Succeeded)
      return CommandResult<EngagementActivityPage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var now = DateTimeOffset.UtcNow;
    var hasCursor = request.AfterAtUtc.HasValue || request.AfterSource.HasValue || request.AfterId.HasValue;
    if (request.PageSize is < 1 or > 100 ||
        request.From.HasValue != request.To.HasValue ||
        request.AfterAtUtc.HasValue != request.AfterSource.HasValue ||
        request.AfterAtUtc.HasValue != request.AfterId.HasValue ||
        (hasCursor && (!request.SnapshotAtUtc.HasValue || request.AfterSource is < 1 or > 7 || request.AfterId == Guid.Empty)) ||
        (request.SnapshotAtUtc.HasValue && request.SnapshotAtUtc.Value.ToUniversalTime() > now))
      return CommandResult<EngagementActivityPage>.Fail(ActivityCursorInvalidCode, ActivityCursorInvalidMessage);

    var todayUtc = DateOnly.FromDateTime(now.UtcDateTime);
    var from = request.From ?? todayUtc.AddDays(-89);
    var to = request.To ?? todayUtc;
    if (from > to || to > todayUtc || to.DayNumber - from.DayNumber >= 90)
      return CommandResult<EngagementActivityPage>.Fail(ActivityRangeInvalidCode, ActivityRangeInvalidMessage);

    var snapshotAtUtc = (request.SnapshotAtUtc ?? now).ToUniversalTime();
    var afterAtUtc = request.AfterAtUtc?.ToUniversalTime();
    var afterSource = request.AfterSource;
    var afterId = request.AfterId;
    var fromAtUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    var toExclusiveAtUtc = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    var items = new List<EngagementActivityItem>(7 * (request.PageSize + 1));

    var transaction = db.Database.CurrentTransaction is null
      ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)
      : null;
    try
    {
      var pbcQuery = db.PbcCommunications.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId &&
          x.EngagementId == engagementId && x.CreatedAt >= fromAtUtc && x.CreatedAt < toExclusiveAtUtc &&
          x.CreatedAt <= snapshotAtUtc);
      if (afterAtUtc.HasValue)
      {
        if (afterSource > PbcCommunicationSource)
          pbcQuery = pbcQuery.Where(x => x.CreatedAt < afterAtUtc.Value);
        else if (afterSource < PbcCommunicationSource)
          pbcQuery = pbcQuery.Where(x => x.CreatedAt <= afterAtUtc.Value);
        else
          pbcQuery = pbcQuery.Where(x => x.CreatedAt < afterAtUtc.Value ||
            (x.CreatedAt == afterAtUtc.Value && x.Id.CompareTo(afterId!.Value) < 0));
      }
      var pbcRows = await pbcQuery.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
        .Take(request.PageSize + 1)
        .Select(x => new { x.Id, x.CreatedAt, x.AuthorUserId, x.Kind, x.DeliveryState })
        .ToListAsync(ct);
      items.AddRange(pbcRows.Select(x => new EngagementActivityItem(x.Id, PbcCommunicationSource, x.Kind,
        x.Kind == PbcCommunicationKinds.Email ? "PBC email recorded" : "PBC message recorded",
        x.AuthorUserId, x.CreatedAt.ToUniversalTime(),
        x.Kind == PbcCommunicationKinds.Email ? PbcStatus(x.DeliveryState) : null,
        $"/app/engagements/{engagementId}/pbc")));

      var uploadQuery = db.PbcUploadIntents.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId &&
          x.EngagementId == engagementId && x.CreatedAt >= fromAtUtc && x.CreatedAt < toExclusiveAtUtc &&
          x.CreatedAt <= snapshotAtUtc);
      if (afterAtUtc.HasValue)
      {
        if (afterSource > PbcUploadSource)
          uploadQuery = uploadQuery.Where(x => x.CreatedAt < afterAtUtc.Value);
        else if (afterSource < PbcUploadSource)
          uploadQuery = uploadQuery.Where(x => x.CreatedAt <= afterAtUtc.Value);
        else
          uploadQuery = uploadQuery.Where(x => x.CreatedAt < afterAtUtc.Value ||
            (x.CreatedAt == afterAtUtc.Value && x.Id.CompareTo(afterId!.Value) < 0));
      }
      var uploadRows = await uploadQuery.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
        .Take(request.PageSize + 1)
        .Select(x => new { x.Id, x.CreatedAt, x.UploaderUserId, x.State })
        .ToListAsync(ct);
      items.AddRange(uploadRows.Select(x => new EngagementActivityItem(x.Id, PbcUploadSource,
        "UPLOAD_INTENT_RECORDED", "Document upload recorded", x.UploaderUserId,
        x.CreatedAt.ToUniversalTime(), x.State, $"/app/engagements/{engagementId}/pbc")));

      var workflowQuery = db.WorkflowTaskActivities.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId &&
          x.EngagementId == engagementId && x.OccurredAt >= fromAtUtc && x.OccurredAt < toExclusiveAtUtc &&
          x.OccurredAt <= snapshotAtUtc);
      if (afterAtUtc.HasValue)
      {
        if (afterSource > WorkflowTaskSource)
          workflowQuery = workflowQuery.Where(x => x.OccurredAt < afterAtUtc.Value);
        else if (afterSource < WorkflowTaskSource)
          workflowQuery = workflowQuery.Where(x => x.OccurredAt <= afterAtUtc.Value);
        else
          workflowQuery = workflowQuery.Where(x => x.OccurredAt < afterAtUtc.Value ||
            (x.OccurredAt == afterAtUtc.Value && x.Id.CompareTo(afterId!.Value) < 0));
      }
      var workflowRows = await workflowQuery.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
        .Take(request.PageSize + 1)
        .Select(x => new { x.Id, x.OccurredAt, x.ActorUserId, x.Kind })
        .ToListAsync(ct);
      items.AddRange(workflowRows.Select(x => new EngagementActivityItem(x.Id, WorkflowTaskSource,
        x.Kind, "Operational task activity", x.ActorUserId, x.OccurredAt.ToUniversalTime(), null, "/app/work")));

      var noteQuery = db.ProcedureReviewNotes.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId &&
          x.EngagementId == engagementId && x.CreatedAt >= fromAtUtc && x.CreatedAt < toExclusiveAtUtc &&
          x.CreatedAt <= snapshotAtUtc);
      if (afterAtUtc.HasValue)
      {
        if (afterSource > ReviewNoteSource)
          noteQuery = noteQuery.Where(x => x.CreatedAt < afterAtUtc.Value);
        else if (afterSource < ReviewNoteSource)
          noteQuery = noteQuery.Where(x => x.CreatedAt <= afterAtUtc.Value);
        else
          noteQuery = noteQuery.Where(x => x.CreatedAt < afterAtUtc.Value ||
            (x.CreatedAt == afterAtUtc.Value && x.Id.CompareTo(afterId!.Value) < 0));
      }
      var noteRows = await noteQuery.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
        .Take(request.PageSize + 1)
        .Select(x => new { x.Id, x.CreatedAt, x.AuthorUserId })
        .ToListAsync(ct);
      items.AddRange(noteRows.Select(x => new EngagementActivityItem(x.Id, ReviewNoteSource,
        "REVIEW_NOTE_CREATED", "Review note created", x.AuthorUserId,
        x.CreatedAt.ToUniversalTime(), null, $"/app/engagements/{engagementId}/audit-fieldwork")));

      var reviewEventQuery = from reviewEvent in db.ProcedureReviewNoteEvents.AsNoTracking()
        join note in db.ProcedureReviewNotes.AsNoTracking()
          on new { reviewEvent.FirmId, Id = reviewEvent.NoteId }
          equals new { note.FirmId, Id = note.Id }
        where reviewEvent.FirmId == actor.FirmId && note.ClientId == engagement.PracticeClientId &&
          note.EngagementId == engagementId && reviewEvent.CreatedAt >= fromAtUtc &&
          reviewEvent.CreatedAt < toExclusiveAtUtc && reviewEvent.CreatedAt <= snapshotAtUtc
        select new
        {
          reviewEvent.Id, reviewEvent.CreatedAt, reviewEvent.AuthorUserId, reviewEvent.Kind
        };
      if (afterAtUtc.HasValue)
      {
        if (afterSource > ReviewNoteEventSource)
          reviewEventQuery = reviewEventQuery.Where(x => x.CreatedAt < afterAtUtc.Value);
        else if (afterSource < ReviewNoteEventSource)
          reviewEventQuery = reviewEventQuery.Where(x => x.CreatedAt <= afterAtUtc.Value);
        else
          reviewEventQuery = reviewEventQuery.Where(x => x.CreatedAt < afterAtUtc.Value ||
            (x.CreatedAt == afterAtUtc.Value && x.Id.CompareTo(afterId!.Value) < 0));
      }
      var reviewEventRows = await reviewEventQuery.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
        .Take(request.PageSize + 1).ToListAsync(ct);
      items.AddRange(reviewEventRows.Select(x => new EngagementActivityItem(x.Id, ReviewNoteEventSource,
        x.Kind, x.Kind switch
        {
          ReviewNoteEventKinds.Response => "Review note response",
          ReviewNoteEventKinds.Resolved => "Review note resolved",
          ReviewNoteEventKinds.Reopened => "Review note reopened",
          _ => "Review note activity recorded",
        }, x.AuthorUserId, x.CreatedAt.ToUniversalTime(), null,
        $"/app/engagements/{engagementId}/audit-fieldwork")));

      var correspondenceQuery = db.CorrespondenceDispatchRecords.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == engagement.PracticeClientId &&
          x.EngagementId == engagementId &&
          x.DispatchedAt >= fromAtUtc && x.DispatchedAt < toExclusiveAtUtc && x.DispatchedAt <= snapshotAtUtc);
      if (afterAtUtc.HasValue)
      {
        if (afterSource > CorrespondenceSource)
          correspondenceQuery = correspondenceQuery.Where(x => x.DispatchedAt < afterAtUtc.Value);
        else if (afterSource < CorrespondenceSource)
          correspondenceQuery = correspondenceQuery.Where(x => x.DispatchedAt <= afterAtUtc.Value);
        else
          correspondenceQuery = correspondenceQuery.Where(x => x.DispatchedAt < afterAtUtc.Value ||
            (x.DispatchedAt == afterAtUtc.Value && x.Id.CompareTo(afterId!.Value) < 0));
      }
      var correspondenceRows = await correspondenceQuery.OrderByDescending(x => x.DispatchedAt).ThenByDescending(x => x.Id)
        .Take(request.PageSize + 1)
        .Select(x => new { x.Id, x.DispatchedAt })
        .ToListAsync(ct);
      items.AddRange(correspondenceRows.Select(x => new EngagementActivityItem(x.Id, CorrespondenceSource,
        "CORRESPONDENCE_RECORDED", "Correspondence routing recorded", null,
        x.DispatchedAt.ToUniversalTime(), "RECORDED", $"/app/engagements/{engagementId}/completion")));

      var commercialQuery = db.CommercialNotifications.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && (
          (x.ProposalId != null && db.EngagementFeeAgreements.Any(a => a.FirmId == actor.FirmId &&
            a.PracticeClientId == engagement.PracticeClientId && a.EngagementId == engagementId &&
            a.ProposalId == x.ProposalId)) ||
          (x.FeeMilestoneId != null && db.FeeMilestones.Any(m => m.FirmId == actor.FirmId &&
            m.Id == x.FeeMilestoneId && db.EngagementFeeAgreements.Any(a => a.FirmId == actor.FirmId &&
              a.PracticeClientId == engagement.PracticeClientId && a.EngagementId == engagementId &&
              a.Id == m.AgreementId))) ||
          (x.DeliverableId != null && db.AuditDeliverables.Any(d => d.FirmId == actor.FirmId &&
            d.ClientId == engagement.PracticeClientId &&
            d.Id == x.DeliverableId && d.EngagementId == engagementId))) &&
          x.CreatedAt >= fromAtUtc && x.CreatedAt < toExclusiveAtUtc && x.CreatedAt <= snapshotAtUtc);
      if (afterAtUtc.HasValue)
      {
        if (afterSource > CommercialNotificationSource)
          commercialQuery = commercialQuery.Where(x => x.CreatedAt < afterAtUtc.Value);
        else if (afterSource < CommercialNotificationSource)
          commercialQuery = commercialQuery.Where(x => x.CreatedAt <= afterAtUtc.Value);
        else
          commercialQuery = commercialQuery.Where(x => x.CreatedAt < afterAtUtc.Value ||
            (x.CreatedAt == afterAtUtc.Value && x.Id.CompareTo(afterId!.Value) < 0));
      }
      var commercialRows = await commercialQuery.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
        .Take(request.PageSize + 1)
        .Select(x => new { x.Id, x.Kind, x.DeliveryState, x.CreatedAt })
        .ToListAsync(ct);
      items.AddRange(commercialRows.Select(x => new EngagementActivityItem(x.Id, CommercialNotificationSource,
        x.Kind, "Commercial notification recorded", null, x.CreatedAt.ToUniversalTime(),
        CommercialStatus(x.DeliveryState), $"/app/engagements/{engagementId}/completion")));

      if (transaction is not null) await transaction.CommitAsync(ct);
    }
    catch
    {
      if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
      throw;
    }
    finally
    {
      if (transaction is not null) await transaction.DisposeAsync();
    }

    auth = await AuthorizeAsync(db, actor, engagement.PracticeClientId, engagementId, ct);
    if (!auth.Succeeded)
      return CommandResult<EngagementActivityPage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var ordered = items.OrderByDescending(x => x.AtUtc).ThenBy(x => x.Source).ThenByDescending(x => x.Id)
      .Take(request.PageSize + 1).ToList();
    var hasMore = ordered.Count > request.PageSize;
    if (hasMore) ordered.RemoveAt(ordered.Count - 1);
    var nextCursor = hasMore && ordered.Count > 0
      ? new EngagementActivityCursor(ordered[^1].AtUtc, ordered[^1].Source, ordered[^1].Id)
      : null;
    return CommandResult<EngagementActivityPage>.Ok(new EngagementActivityPage(engagementId, from, to,
      snapshotAtUtc, ordered, hasMore, nextCursor));
  }
}
