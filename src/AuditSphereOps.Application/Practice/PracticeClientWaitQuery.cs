using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>
/// Cohort-style client response intervals derived from provider-accepted PBC mail and recorded client actions.
/// Each query is bounded; the method returns no partial statistics when a sample exceeds its limits.
/// </summary>
internal static class PracticeClientWaitQuery
{
  private const int MaximumConfirmedSends = 5_000;
  private const int MaximumRequests = 2_000;
  private const int MaximumEvents = 20_000;

  private sealed record CommunicationEvent(Guid Id, Guid PbcRequestId, Guid AuthorUserId, string Kind,
    string? DeliveryState, DateTimeOffset? DeliveredAt, DateTimeOffset CreatedAt);
  private sealed record UploadEvent(Guid PbcRequestId, Guid UploaderUserId, DateTimeOffset CreatedAt);
  private sealed record WaitEvent(Guid PbcRequestId, DateTimeOffset At, bool IsSend, bool IsClientAction);

  public static async Task<ClientWaitSummary> GetAsync(
    IClientAccountingDbContext db, Guid firmId, DateOnly from, DateOnly to, CancellationToken ct)
  {
    var periodStart = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    var periodEndExclusive = to < DateOnly.MaxValue
      ? new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
      : DateTimeOffset.MaxValue;
    var asOf = DateTimeOffset.UtcNow;

    var confirmedSends = await db.PbcCommunications.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.Kind == PbcCommunicationKinds.Email &&
        x.DeliveryState == PbcDeliveryStates.Sent && x.DeliveredAt != null &&
        x.DeliveredAt >= periodStart && x.DeliveredAt < periodEndExclusive && x.DeliveredAt <= asOf &&
        !db.PbcReminderReceipts.Any(r => r.FirmId == firmId && r.CommunicationId == x.Id))
      .Select(x => new { x.PbcRequestId })
      .Take(MaximumConfirmedSends + 1)
      .ToListAsync(ct);

    if (confirmedSends.Count > MaximumConfirmedSends)
      return Unavailable("Narrow the reporting period; it contains more confirmed sends than this bounded view can reconcile.");

    var requestIds = confirmedSends.Select(x => x.PbcRequestId).Distinct().ToArray();
    if (requestIds.Length > MaximumRequests)
      return Unavailable("Narrow the reporting period; it spans more client requests than this bounded view can reconcile.");
    if (requestIds.Length == 0)
      return new(0, 0, 0, 0, null, null, true, null);

    var communications = await db.PbcCommunications.AsNoTracking()
      .Where(x => x.FirmId == firmId && requestIds.Contains(x.PbcRequestId) &&
        ((x.Kind == PbcCommunicationKinds.Email && x.DeliveryState == PbcDeliveryStates.Sent && x.DeliveredAt != null &&
          !db.PbcReminderReceipts.Any(r => r.FirmId == firmId && r.CommunicationId == x.Id)) ||
         x.Kind == PbcCommunicationKinds.ClientMessage || x.Kind == PbcCommunicationKinds.ClientAction))
      .OrderBy(x => x.CreatedAt)
      .Select(x => new CommunicationEvent(x.Id, x.PbcRequestId, x.AuthorUserId, x.Kind,
        x.DeliveryState, x.DeliveredAt, x.CreatedAt))
      .Take(MaximumEvents + 1)
      .ToListAsync(ct);
    if (communications.Count > MaximumEvents)
      return Unavailable("Narrow the reporting period; request communication history exceeds this bounded view.");

    var uploads = await db.PbcUploadIntents.AsNoTracking()
      .Where(x => x.FirmId == firmId && requestIds.Contains(x.PbcRequestId))
      .OrderBy(x => x.CreatedAt)
      .Select(x => new UploadEvent(x.PbcRequestId, x.UploaderUserId, x.CreatedAt))
      .Take(MaximumEvents + 1)
      .ToListAsync(ct);
    if (uploads.Count > MaximumEvents || communications.Count + uploads.Count > MaximumEvents)
      return Unavailable("Narrow the reporting period; request action history exceeds this bounded view.");

    var possibleClientActors = communications
      .Where(x => x.Kind is PbcCommunicationKinds.ClientMessage or PbcCommunicationKinds.ClientAction)
      .Select(x => x.AuthorUserId)
      .Concat(uploads.Select(x => x.UploaderUserId))
      .Distinct()
      .ToArray();
    var clientActorIds = possibleClientActors.Length == 0
      ? new HashSet<Guid>()
      : (await db.Users.AsNoTracking()
          .Where(x => x.FirmId == firmId && x.UserKind == "Client" && possibleClientActors.Contains(x.Id))
          .Select(x => x.Id)
          .ToListAsync(ct))
        .ToHashSet();

    var states = await db.PbcRequests.AsNoTracking()
      .Where(x => x.FirmId == firmId && requestIds.Contains(x.Id))
      .Select(x => new { x.Id, x.State })
      .ToDictionaryAsync(x => x.Id, x => x.State, ct);

    var events = communications.Select(x => x.Kind == PbcCommunicationKinds.Email
        ? new WaitEvent(x.PbcRequestId, x.DeliveredAt!.Value, true, false)
        : new WaitEvent(x.PbcRequestId, x.CreatedAt, false, clientActorIds.Contains(x.AuthorUserId)))
      .Concat(uploads.Where(x => clientActorIds.Contains(x.UploaderUserId))
        .Select(x => new WaitEvent(x.PbcRequestId, x.CreatedAt, false, true)))
      .Where(x => x.At <= asOf)
      .GroupBy(x => x.PbcRequestId);

    var completedHours = new List<decimal>();
    var openHours = new List<decimal>();
    var excluded = 0;
    var starts = 0;
    foreach (var requestEvents in events)
    {
      DateTimeOffset? waitStartedAt = null;
      foreach (var item in requestEvents.OrderBy(x => x.At).ThenBy(x => x.IsSend ? 0 : 1))
      {
        if (item.IsSend)
        {
          // Duplicate sends while the client still owes a response do not reset the clock.
          waitStartedAt ??= item.At;
          continue;
        }

        if (!item.IsClientAction || waitStartedAt is not { } waitingSince || item.At < waitingSince)
          continue;

        if (IsInPeriod(waitingSince, periodStart, periodEndExclusive))
        {
          starts++;
          completedHours.Add(ElapsedHours(waitingSince, item.At));
        }
        waitStartedAt = null;
      }

      if (waitStartedAt is not { } openSince || !IsInPeriod(openSince, periodStart, periodEndExclusive))
        continue;

      starts++;
      if (!states.TryGetValue(requestEvents.Key, out var state))
      {
        excluded++;
        continue;
      }

      if (state is PbcStates.Sent or PbcStates.Acknowledged or PbcStates.PartiallyReceived or PbcStates.ClarificationRequired)
        openHours.Add(ElapsedHours(openSince, asOf));
      else
        excluded++;
    }

    return new(starts, completedHours.Count, openHours.Count, excluded,
      Average(completedHours), Average(openHours), true, null);
  }

  private static bool IsInPeriod(DateTimeOffset at, DateTimeOffset start, DateTimeOffset endExclusive) =>
    at >= start && at < endExclusive;

  private static decimal ElapsedHours(DateTimeOffset from, DateTimeOffset to) =>
    Math.Max(0m, (decimal)(to - from).Ticks / TimeSpan.TicksPerHour);

  private static decimal? Average(IReadOnlyCollection<decimal> values) => values.Count == 0
    ? null
    : Math.Round(values.Sum() / values.Count, 1);

  private static ClientWaitSummary Unavailable(string reason) =>
    new(0, 0, 0, 0, null, null, false, reason);
}
