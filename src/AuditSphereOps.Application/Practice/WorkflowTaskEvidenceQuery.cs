using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record WorkflowEvidenceRuleView(Guid Id, string Kind, Guid? SourceId, string State,
  int Version, bool Satisfied, string SourceState, string Message, long? SourceRevision,
  long? InputGeneration, string Reason, DateTimeOffset OccurredAt,
  Guid? TemplateRequirementId = null, string? TemplateRequirementKey = null, Guid? EvaluatedSourceId = null);
public sealed record WorkflowTaskEvidenceWorkspace(Guid EngagementId, Guid TaskId, long TaskRevision,
  bool CanManage, IReadOnlyList<WorkflowEvidenceRuleView> Rules);
internal sealed record WorkflowEvidenceEvaluation(bool Satisfied, string State, string Message,
  long? Revision = null, long? Generation = null, Guid? SourceId = null, string? SourceFingerprint = null);

/// <summary>Exact-scope live evidence reads. No financial values, private conclusions or stored satisfaction flags.</summary>
public static class WorkflowTaskEvidenceQuery
{
  internal const int MaximumRules = 8;
  internal static readonly string[] ReadRoles = ["Staff", "Senior", "Manager", "Partner", "Administrator"];
  internal static readonly string[] ManageRoles = ["Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<WorkflowEvidenceChangeReceipt>> ReceiptAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, Guid taskId, Guid key, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, EngagementId: engagementId, RequiredRoles: ManageRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<WorkflowEvidenceChangeReceipt>.Fail(auth.ErrorCode!, auth.Message!);
    var receipt = await (from rule in db.WorkflowTaskEvidenceRules.AsNoTracking()
      join binding in db.WorkflowTaskBindings.AsNoTracking() on rule.WorkflowTaskBindingId equals binding.Id
      where rule.FirmId == actor.FirmId && binding.FirmId == actor.FirmId && rule.ActorUserId == actor.UserId &&
        rule.EngagementId == engagementId && binding.EngagementId == engagementId && binding.WorkTaskId == taskId && rule.IdempotencyKey == key
      select new WorkflowEvidenceChangeReceipt(engagementId, taskId, key, rule.Id, rule.TaskRevision)).SingleOrDefaultAsync(ct);
    if (receipt is null) return CommandResult<WorkflowEvidenceChangeReceipt>.Fail(ErrorCodes.GateBlocked,
      "No saved change receipt was found. Keep the request key and review the task before another action.");
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, EngagementId: engagementId, RequiredRoles: ManageRoles, InternalOnly: true), ct);
    return auth.Succeeded ? CommandResult<WorkflowEvidenceChangeReceipt>.Ok(receipt) :
      CommandResult<WorkflowEvidenceChangeReceipt>.Fail(auth.ErrorCode!, auth.Message!);
  }

  public static async Task<CommandResult<WorkflowTaskEvidenceWorkspace>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, Guid taskId, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, EngagementId: engagementId, RequiredRoles: ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<WorkflowTaskEvidenceWorkspace>.Fail(auth.ErrorCode!, auth.Message!);
    var binding = await db.WorkflowTaskBindings.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.WorkTaskId == taskId, ct);
    if (binding is null) return CommandResult<WorkflowTaskEvidenceWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var evaluated = await EvaluateManyAsync(db, actor.FirmId, [binding], ct);
    var canManage = (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, binding.ClientId, engagementId, ManageRoles, InternalOnly: true), ct)).Succeeded;
    var taskState = await db.WorkTasks.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == taskId)
      .Select(x => x.Status).SingleAsync(ct);
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, binding.ClientId, engagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<WorkflowTaskEvidenceWorkspace>.Fail(auth.ErrorCode!, auth.Message!);
    return CommandResult<WorkflowTaskEvidenceWorkspace>.Ok(new(engagementId, taskId, binding.Revision,
      canManage && taskState is not (PracticeTimeStates.TaskCompleted or PracticeTimeStates.TaskCancelled),
      evaluated.GetValueOrDefault(binding.Id) ?? []));
  }

  internal static IQueryable<WorkflowTaskEvidenceRule> Heads(IAuditSphereDbContext db, Guid firmId) =>
    db.WorkflowTaskEvidenceRules.AsNoTracking().Where(x => x.FirmId == firmId &&
      !db.WorkflowTaskEvidenceRules.Any(newer => newer.FirmId == x.FirmId &&
        newer.WorkflowTaskBindingId == x.WorkflowTaskBindingId && newer.RuleKey == x.RuleKey && newer.Version > x.Version));

  // Called only for already-authorized bindings; bound page size and immutable exact-scope FKs limit the read.
  internal static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<WorkflowEvidenceRuleView>>> EvaluateManyAsync(
    IAuditSphereDbContext db, Guid firmId, IReadOnlyList<WorkflowTaskBinding> bindings, CancellationToken ct)
  {
    if (bindings.Count == 0) return new Dictionary<Guid, IReadOnlyList<WorkflowEvidenceRuleView>>();
    if (bindings.Count > 100) throw new InvalidOperationException("Evidence evaluation requires a bounded task page.");
    var ids = bindings.Select(x => x.Id).ToArray();
    var heads = await Heads(db, firmId).Where(x => ids.Contains(x.WorkflowTaskBindingId))
      .OrderBy(x => x.Kind).ThenBy(x => x.SourceId).Take(801).ToListAsync(ct);
    if (heads.Count > 800 || heads.GroupBy(x => x.WorkflowTaskBindingId).Any(x => x.Count() > MaximumRules))
      throw new InvalidOperationException("The task evidence rule bound was exceeded.");
    var templateRequirementIds = heads.Where(x => x.WorkflowTemplateEvidenceRequirementId.HasValue)
      .Select(x => x.WorkflowTemplateEvidenceRequirementId!.Value).Distinct().ToArray();
    var templateRequirements = templateRequirementIds.Length == 0
      ? new Dictionary<Guid, WorkflowTemplateEvidenceRequirement>()
      : await db.WorkflowTemplateEvidenceRequirements.AsNoTracking().Where(x =>
          x.FirmId == firmId && templateRequirementIds.Contains(x.Id))
        .ToDictionaryAsync(x => x.Id, ct);
    var cache = new Dictionary<(Guid EngagementId, string Kind, Guid SourceId), WorkflowEvidenceEvaluation>();
    var output = new Dictionary<Guid, IReadOnlyList<WorkflowEvidenceRuleView>>();
    foreach (var binding in bindings)
    {
      var rows = new List<WorkflowEvidenceRuleView>();
      foreach (var rule in heads.Where(x => x.WorkflowTaskBindingId == binding.Id))
      {
        if (rule.FirmId != binding.FirmId || rule.ClientId != binding.ClientId || rule.EngagementId != binding.EngagementId)
          throw new InvalidOperationException("Evidence scope does not match its task.");
        var key = (binding.EngagementId, rule.Kind, rule.SourceId);
        if (!cache.TryGetValue(key, out var state))
        {
          state = await EvaluateSourceAsync(db, rule, ct);
          cache[key] = state;
        }
        string? templateRequirementKey = null;
        if (rule.WorkflowTemplateEvidenceRequirementId is { } requirementId)
        {
          if (!templateRequirements.TryGetValue(requirementId, out var requirement) ||
              requirement.Kind != rule.Kind || requirement.TemplateStepId != binding.TemplateStepId ||
              requirement.TemplateRevisionId != binding.TemplateRevisionId)
            throw new InvalidOperationException("Evidence template provenance does not match its task declaration.");
          templateRequirementKey = requirement.RequirementKey;
        }
        rows.Add(new(rule.Id, rule.Kind, rule.SourceId == Guid.Empty ? null : rule.SourceId, rule.State,
          rule.Version, state.Satisfied, state.State, state.Message, state.Revision, state.Generation,
          rule.Reason, rule.OccurredAt, rule.WorkflowTemplateEvidenceRequirementId, templateRequirementKey, state.SourceId));
      }
      output[binding.Id] = rows;
    }
    return output;
  }

  internal static async Task<WorkflowEvidenceEvaluation> EvaluateSourceAsync(
    IAuditSphereDbContext db, WorkflowTaskEvidenceRule rule, CancellationToken ct)
  {
    if (rule.Kind == WorkflowEvidenceKinds.AcceptedPbc)
    {
      var source = await db.PbcRequests.AsNoTracking().Where(x => x.FirmId == rule.FirmId &&
        x.ClientId == rule.ClientId && x.EngagementId == rule.EngagementId && x.Id == rule.PbcRequestId)
        .Select(x => new { x.State, x.Revision, x.UpdatedAt }).SingleOrDefaultAsync(ct);
      var generation = await db.ClientSafetyStates.AsNoTracking().Where(x =>
        x.FirmId == rule.FirmId && x.Id == rule.ClientId).Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
      var satisfied = source?.State is PbcStates.Accepted or PbcStates.Closed;
      return new(satisfied, source?.State ?? "MISSING",
        satisfied ? "The exact PBC request is accepted or closed." : "Accept or close the exact PBC request in its workspace.",
        source?.Revision, generation, SourceId: rule.PbcRequestId,
        SourceFingerprint: source is null ? null : $"{source.Revision}:{source.State}:{source.UpdatedAt:O}:{generation}");
    }
    if (rule.Kind == WorkflowEvidenceKinds.CurrentMateriality)
    {
      var current = await MaterialityEngineService.GetLatestAsync(db, rule.FirmId, rule.EngagementId, ct);
      var latestAssessment = await db.MaterialityAssessments.AsNoTracking().Where(x => x.FirmId == rule.FirmId &&
        x.ClientId == rule.ClientId && x.EngagementId == rule.EngagementId)
        .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
      var generation = await db.ClientSafetyStates.AsNoTracking().Where(x =>
        x.FirmId == rule.FirmId && x.Id == rule.ClientId).Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
      var satisfied = current?.State == MaterialityCalculationStates.Approved && current.AssessmentId == latestAssessment;
      var calculation = current?.Calculation;
      var fingerprint = calculation is null ? null : string.Join(':', current!.AssessmentId.ToString("N"),
        latestAssessment?.ToString("N") ?? string.Empty, calculation.Id.ToString("N"),
        calculation.MappingVersionId.ToString("N"), calculation.MappingVersionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
        calculation.DatasetId.ToString("N"), calculation.DatasetDigest, calculation.InputHash, current.State,
        generation?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
      return new(satisfied, current is null ? "MISSING" : current.AssessmentId != latestAssessment ? "STALE" : current.State,
        satisfied ? "Current source-bound materiality has independent Partner approval." : "Recalculate or independently approve the current materiality in audit planning.",
        calculation?.MappingVersionNumber, generation, current?.AssessmentId, fingerprint);
    }
    if (rule.Kind != WorkflowEvidenceKinds.ReviewedProcedure)
      return new(false, "UNSUPPORTED", "This evidence evaluator is not supported.");
    var evaluated = await AuditFieldworkService.EvaluateProcedureEvidenceForAuthorizedScopeAsync(
      db, rule.FirmId, rule.ClientId, rule.EngagementId, rule.AuditProcedureId!.Value, ct);
    var procedureFingerprint = string.Join(':', evaluated.ResultId?.ToString("N") ?? string.Empty,
      evaluated.ReviewId?.ToString("N") ?? string.Empty, evaluated.ReviewerUserId?.ToString("N") ?? string.Empty,
      evaluated.State);
    return new(evaluated.Satisfied, evaluated.State, evaluated.Message, evaluated.Revision, evaluated.InputGeneration,
      rule.AuditProcedureId, procedureFingerprint);
  }
}
