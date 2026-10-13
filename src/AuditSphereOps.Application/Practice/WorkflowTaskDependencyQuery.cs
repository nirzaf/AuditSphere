using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record WorkflowDependencyTask(Guid TaskId, Guid BindingId, string Title, string Status, long Revision);
public sealed record WorkflowDependencyReference(Guid PredecessorTaskId, bool Required, int Version, string Reason);
public sealed record WorkflowDependencyWorkspace(Guid EngagementId, Guid TaskId, long TaskRevision, bool CanManage,
  IReadOnlyList<WorkflowDependencyTask> Tasks, IReadOnlyList<WorkflowDependencyReference> References);
public sealed record WorkflowDependencyReceipt(Guid EngagementId, Guid TaskId, Guid IdempotencyKey, Guid ChangeId, long TaskRevision);
internal sealed class WorkflowDependencyEdge
{
  public Guid FirmId { get; init; }
  public Guid ClientId { get; init; }
  public Guid EngagementId { get; init; }
  public Guid PredecessorBindingId { get; init; }
  public Guid SuccessorBindingId { get; init; }
}

public static class WorkflowTaskDependencyQuery
{
  internal const int MaximumTasks = 500;
  internal const int MaximumEdges = 4000;

  internal static IQueryable<WorkflowTaskDependencyRevision> Heads(IAuditSphereDbContext db, Guid firmId) =>
    db.WorkflowTaskDependencyRevisions.AsNoTracking().Where(x => x.FirmId == firmId &&
      !db.WorkflowTaskDependencyRevisions.Any(n => n.FirmId == x.FirmId && n.SuccessorBindingId == x.SuccessorBindingId &&
        n.PredecessorBindingId == x.PredecessorBindingId && n.Version > x.Version));

  internal static IQueryable<WorkflowDependencyEdge> Effective(IAuditSphereDbContext db, Guid firmId)
  {
    var heads = Heads(db, firmId);
    var pinned = db.WorkflowTaskDependencies.AsNoTracking().Where(x => x.FirmId == firmId &&
      !heads.Any(h => h.SuccessorBindingId == x.SuccessorBindingId && h.PredecessorBindingId == x.PredecessorBindingId))
      .Select(x => new WorkflowDependencyEdge { FirmId = x.FirmId, ClientId = x.ClientId, EngagementId = x.EngagementId,
        PredecessorBindingId = x.PredecessorBindingId, SuccessorBindingId = x.SuccessorBindingId });
    var amended = heads.Where(x => x.Required).Select(x => new WorkflowDependencyEdge { FirmId = x.FirmId,
      ClientId = x.ClientId, EngagementId = x.EngagementId, PredecessorBindingId = x.PredecessorBindingId, SuccessorBindingId = x.SuccessorBindingId });
    return pinned.Concat(amended);
  }

  internal static Task<List<WorkflowDependencyTask>> TasksAsync(IAuditSphereDbContext db, Guid firmId, Guid engagementId, CancellationToken ct) =>
    (from b in db.WorkflowTaskBindings.AsNoTracking()
     join t in db.WorkTasks.AsNoTracking() on new { b.FirmId, Id = b.WorkTaskId } equals new { t.FirmId, t.Id }
     where b.FirmId == firmId && b.EngagementId == engagementId
     orderby b.Ordinal, b.Id
     select new WorkflowDependencyTask(t.Id, b.Id, t.Title, t.Status, b.Revision)).Take(MaximumTasks + 1).ToListAsync(ct);

  public static async Task<CommandResult<WorkflowDependencyWorkspace>> GetAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid engagementId, Guid taskId, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, EngagementId: engagementId, RequiredRoles: WorkflowTaskEvidenceQuery.ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<WorkflowDependencyWorkspace>.Fail(auth.ErrorCode!, auth.Message!);
    var tasks = await TasksAsync(db, actor.FirmId, engagementId, ct);
    if (tasks.Count > MaximumTasks) return CommandResult<WorkflowDependencyWorkspace>.Fail(ErrorCodes.GateBlocked,
      "Task ordering supports engagements with at most 500 workflow tasks; no partial graph is editable.");
    var target = tasks.SingleOrDefault(x => x.TaskId == taskId);
    if (target is null) return CommandResult<WorkflowDependencyWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var amended = await Heads(db, actor.FirmId).Where(x => x.SuccessorBindingId == target.BindingId).Take(MaximumEdges + 1).ToListAsync(ct);
    if (amended.Count > MaximumEdges) return CommandResult<WorkflowDependencyWorkspace>.Fail(ErrorCodes.GateBlocked, "The dependency history exceeds the read bound.");
    var pinned = await db.WorkflowTaskDependencies.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.EngagementId == engagementId && x.SuccessorBindingId == target.BindingId).Take(MaximumEdges + 1).ToListAsync(ct);
    if (pinned.Count > MaximumEdges) return CommandResult<WorkflowDependencyWorkspace>.Fail(ErrorCodes.GateBlocked, "The dependency graph exceeds the read bound.");
    var byBinding = tasks.ToDictionary(x => x.BindingId);
    if (amended.Any(x => !byBinding.ContainsKey(x.PredecessorBindingId)) || pinned.Any(x => !byBinding.ContainsKey(x.PredecessorBindingId)) ||
        !await db.WorkflowTaskBindings.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Id == target.BindingId && x.Revision == target.Revision, ct))
      return CommandResult<WorkflowDependencyWorkspace>.Fail(ErrorCodes.StaleRevision, "Task ordering changed while it was read. Refresh the complete graph before reviewing.");
    var refs = amended.Select(x => new WorkflowDependencyReference(byBinding[x.PredecessorBindingId].TaskId, x.Required, x.Version, x.Reason))
      .Concat(pinned.Where(x => !amended.Any(a => a.PredecessorBindingId == x.PredecessorBindingId))
        .Select(x => new WorkflowDependencyReference(byBinding[x.PredecessorBindingId].TaskId, true, 0, "Pinned template ordering."))).ToArray();
    var manage = (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, EngagementId: engagementId, RequiredRoles: WorkflowTaskEvidenceQuery.ManageRoles, InternalOnly: true), ct)).Succeeded;
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, EngagementId: engagementId, RequiredRoles: WorkflowTaskEvidenceQuery.ReadRoles, InternalOnly: true), ct);
    return auth.Succeeded ? CommandResult<WorkflowDependencyWorkspace>.Ok(new(engagementId, taskId, target.Revision,
      manage && target.Status is not (PracticeTimeStates.TaskCompleted or PracticeTimeStates.TaskCancelled), tasks, refs)) :
      CommandResult<WorkflowDependencyWorkspace>.Fail(auth.ErrorCode!, auth.Message!);
  }

  public static async Task<CommandResult<WorkflowDependencyReceipt>> ReceiptAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid engagementId, Guid taskId, Guid key, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, EngagementId: engagementId, RequiredRoles: WorkflowTaskEvidenceQuery.ManageRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<WorkflowDependencyReceipt>.Fail(auth.ErrorCode!, auth.Message!);
    var row = await (from h in db.WorkflowTaskDependencyRevisions.AsNoTracking()
      join b in db.WorkflowTaskBindings.AsNoTracking() on new { h.FirmId, Id = h.SuccessorBindingId } equals new { b.FirmId, b.Id }
      where h.FirmId == actor.FirmId && h.EngagementId == engagementId && b.EngagementId == engagementId &&
        b.WorkTaskId == taskId && h.ActorUserId == actor.UserId && h.IdempotencyKey == key
      select new WorkflowDependencyReceipt(engagementId, taskId, key, h.Id, h.TaskRevision)).SingleOrDefaultAsync(ct);
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, EngagementId: engagementId, RequiredRoles: WorkflowTaskEvidenceQuery.ManageRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<WorkflowDependencyReceipt>.Fail(auth.ErrorCode!, auth.Message!);
    return row is null ? CommandResult<WorkflowDependencyReceipt>.Fail(ErrorCodes.GateBlocked, "No saved dependency change was found. Keep its recovery key; no change was resent.") :
      CommandResult<WorkflowDependencyReceipt>.Ok(row);
  }

  internal static bool HasCycle(IEnumerable<Guid> vertices, IEnumerable<(Guid From, Guid To)> edges)
  {
    var degree = vertices.ToDictionary(x => x, _ => 0); var outgoing = degree.Keys.ToDictionary(x => x, _ => new List<Guid>());
    foreach (var (from, to) in edges) {
      if (!degree.ContainsKey(from) || !degree.ContainsKey(to)) return true;
      degree[to]++; outgoing[from].Add(to);
    }
    var ready = new Queue<Guid>(degree.Where(x => x.Value == 0).Select(x => x.Key)); var visited = 0;
    while (ready.TryDequeue(out var v)) { visited++; foreach (var n in outgoing[v]) if (--degree[n] == 0) ready.Enqueue(n); }
    return visited != degree.Count;
  }
}
