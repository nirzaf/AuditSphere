using System.Data;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record WorkflowApplicabilityHistoryCursor(DateTimeOffset OccurredAt, string RecordType, Guid Id);
public sealed record WorkflowApplicabilityHistoryItem(Guid Id, string RecordType, string Kind, Guid ProposalId,
  long Version, Guid ActorUserId, string ActorName, string Reason, string BasisHash, DateTimeOffset OccurredAt);
public sealed record WorkflowApplicabilityHistoryPage(Guid EngagementId, Guid TaskId,
  IReadOnlyList<WorkflowApplicabilityHistoryItem> Items, bool HasMore, WorkflowApplicabilityHistoryCursor? NextCursor);

public static partial class WorkflowTaskApplicabilityQuery
{
  private sealed class HistoryRow
  {
    public Guid Id { get; init; }
    public int Rank { get; init; }
    public string Kind { get; init; } = string.Empty;
    public Guid ProposalId { get; init; }
    public long Version { get; init; }
    public Guid ActorUserId { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string BasisHash { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; }
  }

  public static async Task<CommandResult<WorkflowApplicabilityHistoryPage>> HistoryAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid engagementId, Guid taskId, WorkflowApplicabilityHistoryCursor? after = null,
    CancellationToken ct = default)
  {
    if (after is not null && (after.Id == Guid.Empty || after.RecordType is not ("PROPOSAL" or "DECISION")))
      return CommandResult<WorkflowApplicabilityHistoryPage>.Fail("request.invalid", "The complete history cursor is required.");
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<WorkflowApplicabilityHistoryPage>.Fail(ErrorCodes.GateBlocked, "History requires an independent read snapshot.");
    WorkflowApplicabilityHistoryPage page;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      var auth = await AuthorizeReadAsync(db, actor, engagementId, ct);
      if (!auth.Succeeded) return CommandResult<WorkflowApplicabilityHistoryPage>.Fail(auth.ErrorCode!, auth.Message!);
      var binding = await db.WorkflowTaskBindings.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.EngagementId == engagementId && x.WorkTaskId == taskId, ct);
      if (binding is null) return CommandResult<WorkflowApplicabilityHistoryPage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      var proposals = db.WorkflowTaskApplicabilityProposals.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
          x.ClientId == binding.ClientId && x.EngagementId == engagementId && x.WorkflowTaskBindingId == binding.Id)
        .Select(x => new HistoryRow { Id = x.Id, Rank = 1, Kind = "PROPOSE", ProposalId = x.Id, Version = x.Version,
          ActorUserId = x.ActorUserId, Reason = x.Reason, BasisHash = x.BasisHash, OccurredAt = x.OccurredAt });
      var decisions = db.WorkflowTaskApplicabilityDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
          x.ClientId == binding.ClientId && x.EngagementId == engagementId && x.WorkflowTaskBindingId == binding.Id)
        .Select(x => new HistoryRow { Id = x.Id, Rank = 0, Kind = x.Kind, ProposalId = x.ProposalId, Version = x.Version,
          ActorUserId = x.ActorUserId, Reason = x.Reason, BasisHash = x.BasisHash, OccurredAt = x.OccurredAt });
      var query = proposals.Concat(decisions);
      if (after is not null)
      {
        // A type tie-breaker also distinguishes IDs shared by the two independent evidence tables.
        var rank = after.RecordType == "PROPOSAL" ? 1 : 0;
        query = query.Where(x => x.OccurredAt < after.OccurredAt || x.OccurredAt == after.OccurredAt &&
          (x.Rank < rank || x.Rank == rank && x.Id.CompareTo(after.Id) < 0));
      }
      var rows = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Rank)
        .ThenByDescending(x => x.Id).Take(26).ToListAsync(ct);
      var hasMore = rows.Count > 25;
      if (hasMore) rows.RemoveAt(25);
      var actorIds = rows.Select(x => x.ActorUserId).Distinct().ToArray();
      var names = await db.Users.AsNoTracking().Where(x => x.FirmId == actor.FirmId && actorIds.Contains(x.Id))
        .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
      var items = rows.Select(x => new WorkflowApplicabilityHistoryItem(x.Id, x.Rank == 1 ? "PROPOSAL" : "DECISION",
        x.Kind, x.ProposalId, x.Version, x.ActorUserId, names.GetValueOrDefault(x.ActorUserId) ?? "Staff member",
        x.Reason, x.BasisHash, x.OccurredAt)).ToArray();
      var last = rows.Count == 0 ? null : rows[^1];
      page = new(engagementId, taskId, items, hasMore, hasMore && last is not null
        ? new(last.OccurredAt, last.Rank == 1 ? "PROPOSAL" : "DECISION", last.Id) : null);
      await snapshot.CommitAsync(ct);
    }
    var final = await AuthorizeReadAsync(db, actor, engagementId, ct);
    return final.Succeeded ? CommandResult<WorkflowApplicabilityHistoryPage>.Ok(page)
      : CommandResult<WorkflowApplicabilityHistoryPage>.Fail(final.ErrorCode!, final.Message!);
  }
}
