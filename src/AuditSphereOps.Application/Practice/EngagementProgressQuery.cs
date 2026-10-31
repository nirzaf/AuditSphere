using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;

namespace AuditSphereOps.Application.Practice;

public sealed record EngagementProgressStepView(Guid TaskId, string Title, string Phase, int Ordinal, string Requirement,
  string State, string? Condition, Guid? AssigneeUserId, string? AssigneeName, DateOnly? DueDate, string? ApplicabilityCondition);

/// <summary>
/// Engagement progress for the client progress tracker. Percentages count applicable mandatory and conditional template
/// steps (and non-template engagement tasks) completed through the server's dependency and evidence gates; optional steps
/// are reported separately. Gates come from the existing derived lifecycle for the audit route; other routes remain
/// open until their service-specific completion profile is approved, independently of holds and release records.
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
  internal sealed record ProgressSnapshot(EngagementProgressView View, EngagementProgressView RestrictedView);
  internal static readonly string[] ReadRoles = WorkflowTaskEvidenceQuery.ReadRoles;
  /// <summary>Routes governed by the eleven-state derived audit lifecycle; other routes await approved completion profiles.</summary>
  internal static readonly string[] LifecycleRoutes = ["FinancialStatementAudit"];
  private const int StepLimit = 500;

  public static async Task<CommandResult<EngagementProgressView>> GetAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid engagementId, CancellationToken ct = default)
  {
    if (AuthorizationDecision.HasCallerTransaction(db))
      return CommandResult<EngagementProgressView>.Fail(ErrorCodes.GateBlocked, "Progress requires an independent read snapshot.");
    var request = new AuthorizationRequest(actor.FirmId, EngagementId: engagementId, RequiredRoles: ReadRoles, InternalOnly: true);
    IReadOnlyList<ProgressSnapshot> views;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      var snapshotAuth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
      if (!snapshotAuth.Succeeded) return CommandResult<EngagementProgressView>.Fail(snapshotAuth.ErrorCode!, "Engagement progress unavailable.");
      var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
      if (engagement is null) return CommandResult<EngagementProgressView>.Fail(ErrorCodes.ScopeDenied, "Engagement progress unavailable.");
      views = await BuildAsync(db, actor, [engagement], includeSteps: true, ct);
      await snapshot.CommitAsync(ct);
    }
    AuthorizationDecision.ReadSnapshot authority;
    await using (var authorizationSnapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      authority = await CaptureSnapshotsAsync(db, actor, views, [], ct);
      await authorizationSnapshot.CommitAsync(ct);
    }
    var current = AuthorizeSnapshots(authority, views, 0);
    if (!current.Succeeded) return CommandResult<EngagementProgressView>.Fail(current.ErrorCode!, "Engagement progress unavailable.");
    return CommandResult<EngagementProgressView>.Ok(current.Value![0]);
  }

  /// <summary>The first bounded page of summaries for the client's currently authorized engagements.</summary>
  public static Task<CommandResult<EngagementProgressList>> ForClientAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid clientId, CancellationToken ct = default) => ForClientPageAsync(db, actor, clientId, 0, ct);

  /// <summary>Scope is applied before count and paging; the total is never the length of a truncated first page.</summary>
  public static Task<CommandResult<EngagementProgressList>> ForClientPageAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid clientId, int page = 0, CancellationToken ct = default) =>
    PortfolioQuery.ProgressForClientAsync(db, actor, clientId, page, ct);

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

  internal static async Task<AuthorizationDecision.ReadSnapshot> CaptureSnapshotsAsync(
    IAuditSphereDbContext db, ActorContext actor, IReadOnlyList<ProgressSnapshot> snapshots,
    IReadOnlyList<AuthorizationRequest> scopeRequests, CancellationToken ct)
  {
    var requests = new List<AuthorizationRequest>(scopeRequests);
    foreach (var snapshot in snapshots)
    {
      var view = snapshot.View;
      requests.Add(new AuthorizationRequest(actor.FirmId, view.ClientId, view.EngagementId, ReadRoles, InternalOnly: true));
      requests.Add(EngagementLifecycleQuery.ReadRequest(actor, view.EngagementId));
      requests.Add(EngagementLifecycleQuery.DetailRequest(actor, view.EngagementId));
    }
    return await AuthorizationDecision.CaptureReadSnapshotAsync(db, actor, requests, ct);
  }

  internal static CommandResult<IReadOnlyList<EngagementProgressView>> AuthorizeSnapshots(
    AuthorizationDecision.ReadSnapshot authority, IReadOnlyList<ProgressSnapshot> snapshots, int scopeRequestCount)
  {
    var decisions = authority.Evaluate(DateTimeOffset.UtcNow);
    for (var index = 0; index < scopeRequestCount; index++)
      if (!decisions[index].Succeeded)
        return CommandResult<IReadOnlyList<EngagementProgressView>>.Fail(decisions[index].ErrorCode!, "Progress unavailable.");
    var result = new List<EngagementProgressView>(snapshots.Count);
    for (var index = 0; index < snapshots.Count; index++)
    {
      var first = scopeRequestCount + index * 3;
      if (!decisions[first].Succeeded || !decisions[first + 1].Succeeded)
      {
        var failed = !decisions[first].Succeeded ? decisions[first] : decisions[first + 1];
        return CommandResult<IReadOnlyList<EngagementProgressView>>.Fail(failed.ErrorCode!, "Progress unavailable.");
      }
      result.Add(decisions[first + 2].Succeeded ? snapshots[index].View : snapshots[index].RestrictedView);
    }
    return CommandResult<IReadOnlyList<EngagementProgressView>>.Ok(result);
  }

  internal static async Task<IReadOnlyList<ProgressSnapshot>> BuildAsync(IAuditSphereDbContext db, ActorContext actor,
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
        RequiredTask = step == null ? binding.RequiredTask : step.RequiredTask, Condition = step == null ? null : step.ApplicabilityCondition,
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
    // Time tasks created without workflow metadata are still mandatory engagement commitments.
    // Read an independently bounded overflow witness, then share the existing projection cap with bound tasks.
    var unboundTasks = await db.Engagements.AsNoTracking().Where(e => e.FirmId == f && ids.Contains(e.Id))
      .SelectMany(e => db.WorkTasks.AsNoTracking().Where(task => task.FirmId == f &&
          task.ClientId == e.PracticeClientId && task.EngagementId == e.Id &&
          !db.WorkflowTaskBindings.Any(binding => binding.FirmId == f && binding.WorkTaskId == task.Id))
        .OrderBy(task => task.CreatedAt).ThenBy(task => task.Id).Take(StepLimit + 1))
      .ToListAsync(ct);
    var boundCounts = rows.GroupBy(x => x.Binding.EngagementId).ToDictionary(group => group.Key, group => group.Count());
    foreach (var group in unboundTasks.GroupBy(x => x.EngagementId!.Value))
      if (group.Count() + boundCounts.GetValueOrDefault(group.Key) > StepLimit) truncatedEngagements.Add(group.Key);
    unboundTasks = unboundTasks.GroupBy(x => x.EngagementId!.Value).SelectMany(group =>
      group.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(StepLimit - boundCounts.GetValueOrDefault(group.Key))).ToList();
    var unboundTaskIds = unboundTasks.Select(x => x.Id).ToHashSet();
    var bindingIds = rows.Select(x => x.Binding.Id).ToArray();
    // Operational activity never supplies applicability. Only the exact current independently
    // reviewed task receipt (and separately required professional decisions) can exclude a task.
    var dependencies = await (from dependency in WorkflowTaskDependencyQuery.Effective(db, f)
      join predecessor in db.WorkflowTaskBindings.AsNoTracking()
        on new { dependency.FirmId, dependency.ClientId, dependency.EngagementId, Id = dependency.PredecessorBindingId }
        equals new { predecessor.FirmId, predecessor.ClientId, predecessor.EngagementId, predecessor.Id }
      where dependency.FirmId == f && bindingIds.Contains(dependency.SuccessorBindingId)
      select new { dependency.PredecessorBindingId, dependency.SuccessorBindingId }).ToListAsync(ct);
    var evidence = new Dictionary<Guid, IReadOnlyList<WorkflowEvidenceRuleView>>();
    var taskApplicability = new Dictionary<Guid, bool>();
    foreach (var page in rows.Select(x => x.Binding).Chunk(WorkflowTaskEvidenceQuery.MaximumBindings))
    {
      foreach (var (bindingId, rules) in await WorkflowTaskEvidenceQuery.EvaluateManyAsync(db, f, page, ct))
        evidence.Add(bindingId, rules);
      foreach (var (bindingId, current) in await WorkflowTaskApplicabilityQuery.EffectiveManyAsync(db, f, page, ct))
        taskApplicability.Add(bindingId, current);
    }
    var currentCompleted = rows.Where(x => x.Status == PracticeTimeStates.TaskCompleted &&
        // Follow-up task completion requires the linked PBC to be accepted or closed; recheck its current state.
        (!x.Binding.PbcRequestId.HasValue || x.PbcState is PbcStates.Accepted or PbcStates.Closed) &&
        !evidence[x.Binding.Id].Any(rule => rule.State == WorkflowEvidenceRuleStates.Required && !rule.Satisfied))
      .Select(x => x.Binding.Id).ToHashSet();
    var dependents = dependencies.ToLookup(edge => edge.PredecessorBindingId, edge => edge.SuccessorBindingId);
    var invalidPrerequisites = new Queue<Guid>(dependents.Select(group => group.Key).Where(id => !currentCompleted.Contains(id)));
    while (invalidPrerequisites.TryDequeue(out var predecessorId))
      foreach (var successorId in dependents[predecessorId])
        if (currentCompleted.Remove(successorId)) invalidPrerequisites.Enqueue(successorId);
    var unmetByBinding = dependencies.Where(edge => !currentCompleted.Contains(edge.PredecessorBindingId))
      .GroupBy(edge => edge.SuccessorBindingId).ToDictionary(group => group.Key, group => group.Count());
    var assigneeIds = rows.Select(x => x.AssigneeUserId).Concat(unboundTasks.Select(x => x.AssigneeUserId))
      .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
    var names = await db.Users.AsNoTracking().Where(x => x.FirmId == f && assigneeIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
    var openHoldEngagements = await db.EngagementHolds.AsNoTracking()
      .Where(x => x.FirmId == f && ids.Contains(x.EngagementId) && !x.Released)
      .Select(x => x.EngagementId).Distinct().ToListAsync(ct);
    var released = await db.Releases.AsNoTracking().Where(x => x.FirmId == f && ids.Contains(x.EngagementId))
      .Select(x => x.EngagementId).Distinct().ToListAsync(ct);
    var nonAuditEngagements = engagements.Where(e => !LifecycleRoutes.Contains(e.ServiceRoute, StringComparer.Ordinal)).ToList();
    var approvedProfiles = nonAuditEngagements.Count == 0 ? [] : await (from profile in db.AccountingCapabilityProfiles.AsNoTracking()
      join acceptance in db.AccountingCapabilityAcceptances.AsNoTracking()
        on new { profile.FirmId, CapabilityProfileId = profile.Id }
        equals new { acceptance.FirmId, acceptance.CapabilityProfileId }
      where profile.FirmId == f && profile.ClientId.HasValue && clientIds.Contains(profile.ClientId.Value) &&
        profile.Status != AccountingWorkflowStates.Retired &&
        (acceptance.Stage == AccountingCapabilityAcceptanceStages.MethodOwnerApproval ||
         acceptance.Stage == AccountingCapabilityAcceptanceStages.Released) &&
        acceptance.Status == AccountingWorkflowStates.Approved
      select profile).ToListAsync(ct);
    var clientPeriods = nonAuditEngagements.Count == 0 ? [] : await db.ClientReportingPeriods.AsNoTracking()
      .Where(p => p.FirmId == f && clientIds.Contains(p.ClientId))
      .ToListAsync(ct);
    var packages = nonAuditEngagements.Count == 0 ? [] : await db.FinancialPackages.AsNoTracking()
      .Where(p => p.FirmId == f && clientIds.Contains(p.ClientId))
      .ToListAsync(ct);
    var now = DateTimeOffset.UtcNow;

    var views = new List<ProgressSnapshot>();
    foreach (var engagement in engagements)
    {
      // A task receipt never supplies professional N/A. Check required exact procedure sources for
      // reviewed open tasks too, while retaining the existing cancelled-task blocker disclosure.
      var conditionalProcedureRules = rows.Where(x => x.Binding.EngagementId == engagement.Id &&
          (x.Status == PracticeTimeStates.TaskCancelled || taskApplicability.GetValueOrDefault(x.Binding.Id)) &&
          x.RequiredTask == true && !string.IsNullOrWhiteSpace(x.Condition))
        .ToDictionary(x => x.Binding.Id, x => evidence[x.Binding.Id].Where(rule =>
          rule.State == WorkflowEvidenceRuleStates.Required && rule.Kind == WorkflowEvidenceKinds.ReviewedProcedure).ToArray());
      var procedureIds = conditionalProcedureRules.Values.SelectMany(rules => rules)
        .Select(rule => rule.SourceId ?? Guid.Empty).Distinct().ToArray();
      Dictionary<Guid, string> applicability = [];
      if (procedureIds.Length > 0)
      {
        var procedures = await db.AuditProcedures.AsNoTracking().Where(x => x.FirmId == f &&
          x.ClientId == engagement.PracticeClientId && x.EngagementId == engagement.Id && procedureIds.Contains(x.Id))
          .ToListAsync(ct);
        applicability = await AuditProgramService.EffectiveApplicabilityStatusesAsync(db, f,
          engagement.PracticeClientId, engagement.Id, procedures, ct);
      }
      var facts = rows.Where(x => x.Binding.EngagementId == engagement.Id).Select(x => new EngagementProgressStepFact(
        x.Binding.WorkTaskId, x.Title, x.Binding.Phase, x.Binding.Ordinal,
        // Rework and other non-template engagement tasks are commitments the engagement must still discharge,
        // unless a manual task was created as optional.
        x.RequiredTask is { } required ? WorkflowStepRequirements.Classify(required, x.Condition) : WorkflowStepRequirements.Mandatory,
        x.Status, taskApplicability.GetValueOrDefault(x.Binding.Id), unmetByBinding.GetValueOrDefault(x.Binding.Id),
        (evidence.GetValueOrDefault(x.Binding.Id)?.Count(r => r.State == WorkflowEvidenceRuleStates.Required && !r.Satisfied) ?? 0) +
          (x.Binding.PbcRequestId.HasValue && x.PbcState is not (PbcStates.Accepted or PbcStates.Closed) ? 1 : 0),
        x.Binding.PbcRequestId.HasValue && x.PbcState is not (PbcStates.Accepted or PbcStates.Closed),
        x.Binding.ReviewProcedureResultId.HasValue, x.AssigneeUserId, x.DueDate, x.Condition,
        conditionalProcedureRules.GetValueOrDefault(x.Binding.Id)?.Count(rule =>
          applicability.GetValueOrDefault(rule.SourceId ?? Guid.Empty) != AuditApplicabilityStatuses.NotApplicableApproved) ?? 0,
        OnHold: x.Binding.HeldAt.HasValue)).ToList();
      facts.AddRange(unboundTasks.Where(x => x.EngagementId == engagement.Id).Select((task, index) =>
        new EngagementProgressStepFact(task.Id, task.Title, task.Phase, 100001 + index,
          WorkflowStepRequirements.Mandatory, task.Status, false, 0, 0, false, false,
          task.AssigneeUserId, task.DueDate)));

      var lifecycleRoute = LifecycleRoutes.Contains(engagement.ServiceRoute, StringComparer.Ordinal);
      var truncated = truncatedEngagements.Contains(engagement.Id);
      var gates = new List<string>();
      if (truncated) gates.Add($"More than {StepLimit} tasks are recorded, so progress cannot be fully evaluated here.");
      string? stageLabel = null, responsibleRole = null, gateLink = null, disclosure = null;
      int? stageIndex = null;
      var lifecycleBlockerCount = 0;
      var lifecycleIncludesVisibleHolds = false;
      if (lifecycleRoute)
      {
        var (lifecycle, includesVisibleHolds) = await EngagementLifecycleQuery.GetForProgressAsync(db, actor, engagement.Id, ct);
        if (!lifecycle.Succeeded || lifecycle.Value is null)
          gates.Add("Lifecycle readiness is unavailable in your current scope, so release gates are treated as open.");
        else
        {
          var summary = lifecycle.Value.Summary;
          lifecycleBlockerCount = summary.BlockedReasons.Count;
          lifecycleIncludesVisibleHolds = includesVisibleHolds;
          stageLabel = summary.StageLabel; stageIndex = summary.StageIndex;
          responsibleRole = summary.ResponsibleRole; gateLink = summary.DeepLink;
          gates.InsertRange(0, summary.BlockedReasons);
          if (summary.StageIndex < 9 && gates.Count == 0) gates.Add($"{summary.StageLabel} must finish before the report is released.");
        }
      }
      else
      {
        var profile = approvedProfiles.FirstOrDefault(p => p.ClientId == engagement.PracticeClientId && p.ServiceRoute == engagement.ServiceRoute);
        if (profile is null)
        {
          if (!released.Contains(engagement.Id)) gates.Add("No issued release exists for this engagement yet.");
          gates.Add("Completion profile is not approved for this service (D03).");
          disclosure = "Progress reflects the task plan, holds and release; service-specific completion requirements await approval (D03).";
        }
        else
        {
          disclosure = $"Progress reflects the approved {profile.ServiceKind} profile ({profile.Framework}), period close, and released deliverables.";
          if (!released.Contains(engagement.Id)) gates.Add("No issued release exists for this engagement yet.");

          DateOnly.TryParse(engagement.PeriodStart, CultureInfo.InvariantCulture, out var pStart);
          DateOnly.TryParse(engagement.PeriodEnd, CultureInfo.InvariantCulture, out var pEnd);
          var period = clientPeriods.FirstOrDefault(p => p.ClientId == engagement.PracticeClientId && p.StartDate == pStart && p.EndDate == pEnd);
          if (period is null || period.Status != AccountingWorkflowStates.Closed)
            gates.Add("The reporting period is not closed.");

          var clientPackages = packages.Where(x => x.ClientId == engagement.PracticeClientId &&
            x.PeriodStart == engagement.PeriodStart && x.PeriodEnd == engagement.PeriodEnd).ToList();
          if (profile.ServiceKind == AccountingCapabilityServiceKinds.EntityReporting && clientPackages.Count == 0)
            gates.Add("An approved financial package is required before completion.");
          else if (db is IClientAccountingDbContext clientDb)
          {
            foreach (var pkg in clientPackages)
            {
              var packageReview = await FinancialPackageReviewService.RequireCurrentAsync(clientDb, actor, pkg.Id, requirePartner: true, ct);
              if (!packageReview.Succeeded)
              {
                gates.Add("Every financial package for the period needs current management, accounting, and partner approval.");
                break;
              }
            }
          }
        }
      }

      // Some derived lifecycle stages omit holds. Keep the gate without disclosing its restricted reason.
      var hasOpenHold = openHoldEngagements.Contains(engagement.Id);
      if (hasOpenHold && !lifecycleIncludesVisibleHolds)
        gates.Add("An engagement hold remains open.");

      var figures = EngagementProgressCalculator.Calculate(facts, gates.Count > 0);
      var next = figures.NextStep;
      var phase = lifecycleRoute && stageIndex is { } index ? DisplayPhase(index)
        : next is not null ? PhaseLabel(next.Phase) : figures.ApplicableRequired == 0 ? "No task plan adopted" : "Task plan complete";
      var nextAction = next is not null ? next.OnHold
        ? $"Review the hold with a Manager, Partner or Administrator: {next.Title}" : next.Title : gates.FirstOrDefault();
      string? nextLink = null;
      if (next is not null)
      {
        // Hold review requires an elevated actor and the optional work queue may not be enabled.
        // Do not promise a resume action through an unavailable or unauthorized workspace.
        if (!next.OnHold)
          nextLink = unboundTaskIds.Contains(next.TaskId) ? $"/app/practice/time?taskId={next.TaskId:D}"
            : $"/app/work?engagementId={engagement.Id:D}&taskId={next.TaskId:D}";
      }
      else if (gates.Count > 0) nextLink = gateLink ?? $"/app/engagements/{engagement.Id:D}";
      var view = new EngagementProgressView(engagement.Id, engagement.PracticeClientId,
        clients.GetValueOrDefault(engagement.PracticeClientId, "Client"), engagement.ServiceRoute, engagement.PeriodStart,
        engagement.PeriodEnd, lifecycleRoute, phase, stageLabel, stageIndex, figures.PercentComplete, figures.ApplicableRequired,
        figures.CompletedRequired, figures.PendingRequired, figures.BlockedRequired, figures.NotApplicable,
        figures.OptionalApplicable, figures.OptionalCompleted, figures.GateOpen, gates, disclosure,
        next?.AssigneeUserId is { } assignee ? names.GetValueOrDefault(assignee) : null, responsibleRole,
        figures.NextDueDate, nextAction, nextLink,
        includeSteps ? figures.Steps.Select(s => new EngagementProgressStepView(s.TaskId, s.Title, s.Phase, s.Ordinal,
          s.Requirement, s.State, s.Condition, s.AssigneeUserId,
          s.AssigneeUserId is { } id ? names.GetValueOrDefault(id) : null, s.DueDate, s.ApplicabilityCondition)).ToList() : [],
        now);
      var restricted = view;
      if (lifecycleBlockerCount > 0)
      {
        var restrictedGates = new List<string> { EngagementLifecycleQuery.RestrictedBlockers };
        restrictedGates.AddRange(gates.Skip(lifecycleBlockerCount));
        if (hasOpenHold && lifecycleIncludesVisibleHolds) restrictedGates.Add("An engagement hold remains open.");
        restricted = view with
        {
          Gates = restrictedGates,
          NextAction = next is null ? restrictedGates.FirstOrDefault() : view.NextAction
        };
      }
      views.Add(new ProgressSnapshot(view, restricted));
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
