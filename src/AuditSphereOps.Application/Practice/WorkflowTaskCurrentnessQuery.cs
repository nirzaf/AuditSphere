using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

internal sealed record WorkflowTaskCompletionFact(Guid BindingId, string Status, bool PbcCurrent);
internal sealed record WorkflowTaskRequiredDependency(Guid PredecessorBindingId, Guid SuccessorBindingId);
internal sealed record WorkflowTaskCurrentnessProjection(
  HashSet<Guid> CurrentCompletedBindingIds,
  IReadOnlyList<WorkflowTaskRequiredDependency> Dependencies,
  IReadOnlyDictionary<Guid, IReadOnlyList<WorkflowEvidenceRuleView>> EvidenceBySeed);

/// <summary>One read-only definition of effective task completion for required dependency chains.</summary>
internal static class WorkflowTaskCurrentnessQuery
{
  internal static HashSet<Guid> ResolveCurrentCompletedBindings(
    IEnumerable<WorkflowTaskCompletionFact> taskFacts,
    IReadOnlyDictionary<Guid, IReadOnlyList<WorkflowEvidenceRuleView>> evidence,
    IEnumerable<WorkflowTaskRequiredDependency> dependencies)
  {
    var currentCompleted = taskFacts.Where(task => task.Status == PracticeTimeStates.TaskCompleted && task.PbcCurrent &&
        evidence.TryGetValue(task.BindingId, out var rules) &&
        !rules.Any(rule => rule.State == WorkflowEvidenceRuleStates.Required && !rule.Satisfied))
      .Select(task => task.BindingId).ToHashSet();
    var dependencyRows = dependencies.ToArray();
    var dependents = dependencyRows.ToLookup(edge => edge.PredecessorBindingId, edge => edge.SuccessorBindingId);
    var invalidPrerequisites = new Queue<Guid>(dependencyRows.Select(edge => edge.PredecessorBindingId).Distinct()
      .Where(bindingId => !currentCompleted.Contains(bindingId)));
    while (invalidPrerequisites.TryDequeue(out var predecessorId))
      foreach (var successorId in dependents[predecessorId])
        if (currentCompleted.Remove(successorId)) invalidPrerequisites.Enqueue(successorId);
    return currentCompleted;
  }

  /// <summary>
  /// Evaluates only the requested tasks and their required predecessor closure. Caller authorization and snapshot/lock
  /// ownership remain with the existing queue, workspace, progress or command boundary.
  /// </summary>
  internal static async Task<CommandResult<WorkflowTaskCurrentnessProjection>> ForBindingsAsync(
    IAuditSphereDbContext db, Guid firmId, IReadOnlyDictionary<Guid, Guid> seedEngagementIds,
    CancellationToken ct)
  {
    if (seedEngagementIds.Count == 0)
      return CommandResult<WorkflowTaskCurrentnessProjection>.Ok(new([], [], new Dictionary<Guid, IReadOnlyList<WorkflowEvidenceRuleView>>()));

    var engagementIds = seedEngagementIds.Values.Distinct().ToArray();
    var maximumEdges = checked(WorkflowTaskDependencyQuery.MaximumEdges * engagementIds.Length);
    var graphRows = await WorkflowTaskDependencyQuery.Effective(db, firmId)
      .Where(edge => engagementIds.Contains(edge.EngagementId))
      .Take(maximumEdges + 1).ToListAsync(ct);
    if (graphRows.Count > maximumEdges || graphRows.GroupBy(edge => edge.EngagementId)
        .Any(group => group.Count() > WorkflowTaskDependencyQuery.MaximumEdges))
      return CommandResult<WorkflowTaskCurrentnessProjection>.Fail(ErrorCodes.GateBlocked,
        "The required task graph exceeds the supported read bound. Refresh after the graph is reviewed.");

    var engagementByBinding = new Dictionary<Guid, Guid>(seedEngagementIds);
    var taskCounts = seedEngagementIds.Values.GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
    if (taskCounts.Values.Any(count => count > WorkflowTaskDependencyQuery.MaximumTasks))
      return CommandResult<WorkflowTaskCurrentnessProjection>.Fail(ErrorCodes.GateBlocked,
        "The required task graph exceeds the supported read bound. Refresh after the graph is reviewed.");

    var incoming = graphRows.ToLookup(edge => edge.SuccessorBindingId);
    var pending = new Queue<Guid>(seedEngagementIds.Keys);
    while (pending.TryDequeue(out var successorId))
    {
      var successorEngagementId = engagementByBinding[successorId];
      foreach (var edge in incoming[successorId])
      {
        if (edge.EngagementId != successorEngagementId)
          return CommandResult<WorkflowTaskCurrentnessProjection>.Fail(ErrorCodes.GateBlocked,
            "The required task graph changed scope. Refresh before continuing.");
        if (engagementByBinding.TryGetValue(edge.PredecessorBindingId, out var predecessorEngagementId))
        {
          if (predecessorEngagementId != edge.EngagementId)
            return CommandResult<WorkflowTaskCurrentnessProjection>.Fail(ErrorCodes.GateBlocked,
              "The required task graph changed scope. Refresh before continuing.");
          continue;
        }
        engagementByBinding.Add(edge.PredecessorBindingId, edge.EngagementId);
        var count = taskCounts.GetValueOrDefault(edge.EngagementId) + 1;
        if (count > WorkflowTaskDependencyQuery.MaximumTasks)
          return CommandResult<WorkflowTaskCurrentnessProjection>.Fail(ErrorCodes.GateBlocked,
            "The required task graph exceeds the supported read bound. Refresh after the graph is reviewed.");
        taskCounts[edge.EngagementId] = count;
        pending.Enqueue(edge.PredecessorBindingId);
      }
    }

    var bindingIds = engagementByBinding.Keys.ToArray();
    var bindings = await db.WorkflowTaskBindings.AsNoTracking()
      .Where(binding => binding.FirmId == firmId && bindingIds.Contains(binding.Id)).ToListAsync(ct);
    if (bindings.Count != engagementByBinding.Count || bindings.Any(binding =>
        !engagementByBinding.TryGetValue(binding.Id, out var engagementId) || binding.EngagementId != engagementId))
      return CommandResult<WorkflowTaskCurrentnessProjection>.Fail(ErrorCodes.StaleRevision,
        "A required task changed while its prerequisites were read. Refresh before continuing.");

    var bindingsById = bindings.ToDictionary(binding => binding.Id);
    var relevantGraphRows = graphRows.Where(edge => engagementByBinding.ContainsKey(edge.SuccessorBindingId)).ToArray();
    if (relevantGraphRows.Any(edge => edge.FirmId != firmId ||
        !bindingsById.TryGetValue(edge.PredecessorBindingId, out var predecessor) ||
        !bindingsById.TryGetValue(edge.SuccessorBindingId, out var successor) ||
        predecessor.ClientId != edge.ClientId || successor.ClientId != edge.ClientId ||
        predecessor.EngagementId != edge.EngagementId || successor.EngagementId != edge.EngagementId))
      return CommandResult<WorkflowTaskCurrentnessProjection>.Fail(ErrorCodes.GateBlocked,
        "The required task graph is inconsistent. Refresh before continuing.");

    var taskIds = bindings.Select(binding => binding.WorkTaskId).Distinct().ToArray();
    var taskStatuses = await db.WorkTasks.AsNoTracking().Where(task => task.FirmId == firmId && taskIds.Contains(task.Id))
      .Select(task => new { task.Id, task.Status }).ToDictionaryAsync(task => task.Id, task => task.Status, ct);
    if (taskStatuses.Count != taskIds.Length)
      return CommandResult<WorkflowTaskCurrentnessProjection>.Fail(ErrorCodes.StaleRevision,
        "A required task changed while its prerequisites were read. Refresh before continuing.");

    var pbcIds = bindings.Where(binding => binding.PbcRequestId.HasValue)
      .Select(binding => binding.PbcRequestId!.Value).Distinct().ToArray();
    var pbcByScope = new Dictionary<(Guid ClientId, Guid EngagementId, Guid PbcId), string>();
    if (pbcIds.Length > 0)
    {
      var pbcStates = await db.PbcRequests.AsNoTracking()
        .Where(pbc => pbc.FirmId == firmId && pbcIds.Contains(pbc.Id))
        .Select(pbc => new { pbc.Id, pbc.ClientId, pbc.EngagementId, pbc.State }).ToListAsync(ct);
      foreach (var pbc in pbcStates)
        pbcByScope.Add((pbc.ClientId, pbc.EngagementId, pbc.Id), pbc.State);
    }
    var facts = bindings.Select(binding => new WorkflowTaskCompletionFact(binding.Id,
      taskStatuses[binding.WorkTaskId], !binding.PbcRequestId.HasValue ||
        (pbcByScope.TryGetValue((binding.ClientId, binding.EngagementId, binding.PbcRequestId.Value), out var state) &&
         state is PbcStates.Accepted or PbcStates.Closed))).ToArray();

    var evidence = new Dictionary<Guid, IReadOnlyList<WorkflowEvidenceRuleView>>();
    foreach (var page in bindings.Chunk(WorkflowTaskEvidenceQuery.MaximumBindings))
      foreach (var (bindingId, rules) in await WorkflowTaskEvidenceQuery.EvaluateManyAsync(db, firmId, page, ct))
        evidence.Add(bindingId, rules);
    if (evidence.Count != bindings.Count)
      return CommandResult<WorkflowTaskCurrentnessProjection>.Fail(ErrorCodes.GateBlocked,
        "Current prerequisite evidence is unavailable. Refresh before continuing.");

    var dependencies = relevantGraphRows.Select(edge => new WorkflowTaskRequiredDependency(
      edge.PredecessorBindingId, edge.SuccessorBindingId)).ToArray();
    var currentCompleted = ResolveCurrentCompletedBindings(facts, evidence, dependencies);
    var seedEvidence = seedEngagementIds.Keys.ToDictionary(bindingId => bindingId, bindingId => evidence[bindingId]);
    return CommandResult<WorkflowTaskCurrentnessProjection>.Ok(new(currentCompleted, dependencies, seedEvidence));
  }
}
