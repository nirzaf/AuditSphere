using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record EngagementProgressStepView(Guid TaskId, string Title, string Phase, int Ordinal, string Requirement,
  string State, string? Condition, Guid? AssigneeUserId, string? AssigneeName, DateOnly? DueDate, string? ApplicabilityCondition);

/// <summary>
/// Engagement progress for the client progress tracker. Percentages count applicable mandatory and conditional template
/// steps (and non-template engagement tasks) completed through the server's dependency and evidence gates; optional steps
/// are reported separately. Gates come from the existing derived lifecycle for the audit route and from holds and an
/// issued release otherwise, so a complete task plan never reads as 100% while a gate is open.
/// </summary>
public sealed record EngagementProgressView(
  Guid EngagementId, Guid ClientId, string ClientName, string ServiceRoute, string PeriodStart, string PeriodEnd,
  bool LifecycleRoute, string Phase, string? StageLabel, int? StageIndex,
  int? PercentComplete, int ApplicableRequired, int CompletedRequired, int PendingRequired, int BlockedRequired,
  int NotApplicable, int OptionalApplicable, int OptionalCompleted,
  bool GateOpen, IReadOnlyList<string> Gates, string? Disclosure,
  string? ResponsibleName, string? ResponsibleRole, DateOnly? NextDueDate, string? NextAction, string? NextActionLink,
  IReadOnlyList<EngagementProgressStepView> Steps, DateTimeOffset EvaluatedAtUtc);

public sealed record EngagementProgressList(IReadOnlyList<EngagementProgressView> Items, int Total, int Page, int PageSize);

public static class EngagementProgressQuery
{
  internal static readonly string[] ReadRoles = WorkflowTaskEvidenceQuery.ReadRoles;
  /// <summary>Routes governed by the eleven-state derived audit lifecycle; other routes use task-plan and release gates.</summary>
  internal static readonly string[] LifecycleRoutes = ["FinancialStatementAudit"];
  private const int ClientLimit = 50;
  private const int StepLimit = 500;

  public static async Task<CommandResult<EngagementProgressView>> GetAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid engagementId, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, EngagementId: engagementId, RequiredRoles: ReadRoles, InternalOnly: true);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!auth.Succeeded) return CommandResult<EngagementProgressView>.Fail(auth.ErrorCode!, "Engagement progress unavailable.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return CommandResult<EngagementProgressView>.Fail(ErrorCodes.ScopeDenied, "Engagement progress unavailable.");
    var views = await BuildAsync(db, actor, [engagement], includeSteps: true, ct);
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    return auth.Succeeded ? CommandResult<EngagementProgressView>.Ok(views[0])
      : CommandResult<EngagementProgressView>.Fail(auth.ErrorCode!, "Engagement progress unavailable.");
  }

  /// <summary>One summary per engagement of the client that the actor may read; engagements outside scope are omitted.</summary>
  public static async Task<CommandResult<EngagementProgressList>> ForClientAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid clientId, CancellationToken ct = default)
  {
    var client = await db.PracticeClients.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Id == clientId, ct);
    if (!client) return CommandResult<EngagementProgressList>.Fail(ErrorCodes.ScopeDenied, "Client progress unavailable.");
    var candidates = await db.Engagements.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == clientId)
      .OrderByDescending(x => x.PeriodEnd).ThenBy(x => x.Id).Take(ClientLimit + 1).ToListAsync(ct);
    var permitted = await PermittedAsync(db, actor, candidates.Take(ClientLimit), ct);
    if (permitted.Count == 0 && candidates.Count > 0)
      return CommandResult<EngagementProgressList>.Fail(ErrorCodes.ScopeDenied, "Client progress unavailable.");
    var views = await BuildAsync(db, actor, permitted, includeSteps: false, ct);
    if ((await PermittedAsync(db, actor, permitted, ct)).Count != permitted.Count)
      return CommandResult<EngagementProgressList>.Fail(ErrorCodes.ScopeDenied, "Client progress unavailable.");
    return CommandResult<EngagementProgressList>.Ok(new(views, views.Count, 0, ClientLimit));
  }

  internal static async Task<List<Engagement>> PermittedAsync(IAuditSphereDbContext db, ActorContext actor,
    IEnumerable<Engagement> engagements, CancellationToken ct)
  {
    var permitted = new List<Engagement>();
    foreach (var engagement in engagements)
      if ((await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId,
            EngagementId: engagement.Id, RequiredRoles: ReadRoles, InternalOnly: true), ct)).Succeeded)
        permitted.Add(engagement);
    return permitted;
  }

  internal static async Task<IReadOnlyList<EngagementProgressView>> BuildAsync(IAuditSphereDbContext db, ActorContext actor,
    IReadOnlyList<Engagement> engagements, bool includeSteps, CancellationToken ct)
  {
    if (engagements.Count == 0) return [];
    var f = actor.FirmId;
    var ids = engagements.Select(x => x.Id).ToArray();
    var clientIds = engagements.Select(x => x.PracticeClientId).Distinct().ToArray();
    var clients = await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == f && clientIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.CommercialName ?? x.LegalName, ct);
    var taskRows = from binding in db.WorkflowTaskBindings.AsNoTracking()
      join task in db.WorkTasks.AsNoTracking() on new { binding.FirmId, Id = binding.WorkTaskId } equals new { task.FirmId, task.Id }
      join step in db.WorkflowTemplateSteps.AsNoTracking() on binding.TemplateStepId equals (Guid?)step.Id into steps
      from step in steps.DefaultIfEmpty()
      join pbc in db.PbcRequests.AsNoTracking() on binding.PbcRequestId equals (Guid?)pbc.Id into pbcs
      from pbc in pbcs.DefaultIfEmpty()
      where binding.FirmId == f && ids.Contains(binding.EngagementId)
      select new
      {
        Binding = binding, task.Title, task.Status, task.AssigneeUserId, task.DueDate,
        RequiredTask = step == null ? (bool?)null : step.RequiredTask, Condition = step == null ? null : step.ApplicabilityCondition,
        PbcState = pbc == null ? null : pbc.State
      };
    // Bound each plan independently so an oversized plan cannot consume another engagement's read window.
    var rows = await db.Engagements.AsNoTracking().Where(e => e.FirmId == f && ids.Contains(e.Id))
      .SelectMany(e => taskRows.Where(row => row.Binding.EngagementId == e.Id)
        .OrderBy(row => row.Binding.Ordinal).ThenBy(row => row.Binding.Id).Take(StepLimit + 1))
      .ToListAsync(ct);
    var truncatedEngagements = rows.GroupBy(x => x.Binding.EngagementId)
      .Where(group => group.Count() > StepLimit).Select(group => group.Key).ToHashSet();
    rows = rows.GroupBy(x => x.Binding.EngagementId).SelectMany(group => group.Take(StepLimit)).ToList();
    var taskIds = rows.Select(x => x.Binding.WorkTaskId).ToArray();
    var bindingIds = rows.Select(x => x.Binding.Id).ToArray();
    var cancelled = await db.WorkflowTaskActivities.AsNoTracking()
      .Where(x => x.FirmId == f && taskIds.Contains(x.WorkTaskId) && x.Kind == WorkflowTaskActivityKinds.Cancelled)
      .Select(x => x.WorkTaskId).Distinct().ToListAsync(ct);
    var unmet = await (from dependency in WorkflowTaskDependencyQuery.Effective(db, f)
      join predecessor in db.WorkflowTaskBindings.AsNoTracking()
        on new { dependency.FirmId, dependency.ClientId, dependency.EngagementId, Id = dependency.PredecessorBindingId }
        equals new { predecessor.FirmId, predecessor.ClientId, predecessor.EngagementId, predecessor.Id }
      join predecessorTask in db.WorkTasks.AsNoTracking()
        on new { predecessor.FirmId, WorkTaskId = predecessor.WorkTaskId } equals new { predecessorTask.FirmId, WorkTaskId = predecessorTask.Id }
      where dependency.FirmId == f && bindingIds.Contains(dependency.SuccessorBindingId) &&
        predecessorTask.Status != PracticeTimeStates.TaskCompleted
      select dependency.SuccessorBindingId).ToListAsync(ct);
    var unmetByBinding = unmet.GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
    var evidence = new Dictionary<Guid, IReadOnlyList<WorkflowEvidenceRuleView>>();
    foreach (var page in rows.Select(x => x.Binding).Chunk(WorkflowTaskEvidenceQuery.MaximumBindings))
      foreach (var (bindingId, rules) in await WorkflowTaskEvidenceQuery.EvaluateManyAsync(db, f, page, ct))
        evidence.Add(bindingId, rules);
    var assigneeIds = rows.Where(x => x.AssigneeUserId.HasValue).Select(x => x.AssigneeUserId!.Value).Distinct().ToArray();
    var names = await db.Users.AsNoTracking().Where(x => x.FirmId == f && assigneeIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
    var holds = await db.EngagementHolds.AsNoTracking().Where(x => x.FirmId == f && ids.Contains(x.EngagementId) && !x.Released)
      .Select(x => new { x.EngagementId, x.HoldKind, x.Reason }).ToListAsync(ct);
    var released = await db.Releases.AsNoTracking().Where(x => x.FirmId == f && ids.Contains(x.EngagementId))
      .Select(x => x.EngagementId).Distinct().ToListAsync(ct);
    var now = DateTimeOffset.UtcNow;

    var views = new List<EngagementProgressView>();
    foreach (var engagement in engagements)
    {
      var facts = rows.Where(x => x.Binding.EngagementId == engagement.Id).Select(x => new EngagementProgressStepFact(
        x.Binding.WorkTaskId, x.Title, x.Binding.Phase, x.Binding.Ordinal,
        // Rework and other non-template engagement tasks are commitments the engagement must still discharge.
        x.RequiredTask is { } required ? WorkflowStepRequirements.Classify(required, x.Condition) : WorkflowStepRequirements.Mandatory,
        x.Status, cancelled.Contains(x.Binding.WorkTaskId), unmetByBinding.GetValueOrDefault(x.Binding.Id),
        evidence.GetValueOrDefault(x.Binding.Id)?.Count(r => r.State == WorkflowEvidenceRuleStates.Required && !r.Satisfied) ?? 0,
        x.Binding.PbcRequestId.HasValue && x.PbcState is not (PbcStates.Accepted or PbcStates.Closed),
        x.Binding.ReviewProcedureResultId.HasValue, x.AssigneeUserId, x.DueDate, x.Condition)).ToList();

      var lifecycleRoute = LifecycleRoutes.Contains(engagement.ServiceRoute, StringComparer.Ordinal);
      var truncated = truncatedEngagements.Contains(engagement.Id);
      var gates = holds.Where(h => h.EngagementId == engagement.Id).Select(h => $"Open {h.HoldKind} hold: {h.Reason}").ToList();
      if (truncated) gates.Add($"More than {StepLimit} tasks are recorded, so progress cannot be fully evaluated here.");
      string? stageLabel = null, responsibleRole = null, gateLink = null, disclosure = null;
      int? stageIndex = null;
      if (lifecycleRoute)
      {
        var lifecycle = await EngagementLifecycleQuery.GetAsync(db, actor, engagement.Id, ct);
        if (!lifecycle.Succeeded || lifecycle.Value is null)
          gates.Add("Lifecycle readiness is unavailable in your current scope, so release gates are treated as open.");
        else
        {
          var summary = lifecycle.Value.Summary;
          stageLabel = summary.StageLabel; stageIndex = summary.StageIndex;
          responsibleRole = summary.ResponsibleRole; gateLink = summary.DeepLink;
          gates = [.. summary.BlockedReasons, .. truncated ? gates.Where(g => g.StartsWith("More than", StringComparison.Ordinal)) : []];
          if (summary.StageIndex < 9 && gates.Count == 0) gates.Add($"{summary.StageLabel} must finish before the report is released.");
        }
      }
      else
      {
        if (!released.Contains(engagement.Id)) gates.Add("No issued release exists for this engagement yet.");
        disclosure = "Route-specific release gates for this service are not yet specified (D03); progress reflects the task plan, holds and release only.";
      }

      var figures = EngagementProgressCalculator.Calculate(facts, gates.Count > 0);
      var next = figures.NextStep;
      var phase = lifecycleRoute && stageIndex is { } index ? DisplayPhase(index)
        : next is not null ? PhaseLabel(next.Phase) : figures.ApplicableRequired == 0 ? "No task plan adopted" : "Task plan complete";
      var nextAction = next is not null ? next.Title : gates.FirstOrDefault();
      var nextLink = next is not null ? $"/app/work?engagementId={engagement.Id:D}&taskId={next.TaskId:D}"
        : gates.Count > 0 ? gateLink ?? $"/app/engagements/{engagement.Id:D}" : null;
      views.Add(new EngagementProgressView(engagement.Id, engagement.PracticeClientId,
        clients.GetValueOrDefault(engagement.PracticeClientId, "Client"), engagement.ServiceRoute, engagement.PeriodStart,
        engagement.PeriodEnd, lifecycleRoute, phase, stageLabel, stageIndex, figures.PercentComplete, figures.ApplicableRequired,
        figures.CompletedRequired, figures.PendingRequired, figures.BlockedRequired, figures.NotApplicable,
        figures.OptionalApplicable, figures.OptionalCompleted, figures.GateOpen, gates, disclosure,
        next?.AssigneeUserId is { } assignee ? names.GetValueOrDefault(assignee) : null, responsibleRole,
        figures.NextDueDate, nextAction, nextLink,
        includeSteps ? figures.Steps.Select(s => new EngagementProgressStepView(s.TaskId, s.Title, s.Phase, s.Ordinal,
          s.Requirement, s.State, s.Condition, s.AssigneeUserId,
          s.AssigneeUserId is { } id ? names.GetValueOrDefault(id) : null, s.DueDate, s.ApplicabilityCondition)).ToList() : [],
        now));
    }
    return views;
  }

  /// <summary>The approved seven display phases over the eleven derived audit states.</summary>
  internal static string DisplayPhase(int stageIndex) => stageIndex switch
  {
    <= 4 => "Client onboarding",
    5 => "Audit planning",
    6 => "Audit fieldwork",
    7 => "Manager review",
    8 => "Partner sign-off",
    9 => "Report and delivery",
    _ => "Archive and close"
  };

  private static string PhaseLabel(string phase) => phase switch
  {
    WorkflowTaskPhases.ClientOnboarding => "Client onboarding",
    WorkflowTaskPhases.AuditPlanning => "Planning",
    WorkflowTaskPhases.AuditFieldwork => "Fieldwork",
    WorkflowTaskPhases.ManagerReview => "Manager review",
    WorkflowTaskPhases.PartnerSignoff => "Partner sign-off",
    WorkflowTaskPhases.ReportDelivery => "Report and delivery",
    WorkflowTaskPhases.ArchiveClose => "Archive and close",
    _ => phase.Replace('_', ' ').ToLowerInvariant()
  };
}
