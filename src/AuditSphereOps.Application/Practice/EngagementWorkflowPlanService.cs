using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record WorkflowPlanOwnerSelection(string StepKey, Guid AssigneeUserId);
/// <summary>Explicit current owners for one typed PBC step. Nothing is copied from an earlier request or year.</summary>
public sealed record WorkflowPlanPbcOwnerSelection(string StepKey, Guid ClientOwnerUserId, Guid FirmOwnerUserId,
  Guid ReviewerUserId);
public sealed record PreviewEngagementWorkflowPlanRequest(Guid TemplateRevisionId,
  IReadOnlyList<WorkflowPlanOwnerSelection>? Owners = null,
  IReadOnlyList<WorkflowPlanPbcOwnerSelection>? PbcOwners = null);
public sealed record ExecuteEngagementWorkflowPlanRequest(Guid TemplateRevisionId, string ExpectedPreviewDigest,
  Guid IdempotencyKey, IReadOnlyList<WorkflowPlanOwnerSelection>? Owners = null,
  IReadOnlyList<WorkflowPlanPbcOwnerSelection>? PbcOwners = null);
/// <summary>Creates the still-pending PBC drafts of an executed plan (for example after activation) for the same steps.</summary>
public sealed record CreateWorkflowPlanPbcDraftsRequest(Guid TemplateRevisionId, string ExpectedPreviewDigest,
  IReadOnlyList<WorkflowPlanPbcOwnerSelection>? PbcOwners = null);
public sealed record WorkflowPlanOwnerCandidate(Guid UserId, string DisplayName, string StaffingLevel);
public sealed record WorkflowPlanPbcCandidate(Guid UserId, string DisplayName);
/// <summary>
/// The exact request a typed template step will create: fresh, unsent, current period and current owners. A linked
/// step shows the request already bound to its task; an unresolved legacy reference is descriptive only.
/// </summary>
public sealed record WorkflowPlanPbcPreview(string State, string? Reference, WorkflowTemplatePbcDefinition? Definition,
  string PeriodStart, string PeriodEnd, DateOnly? DueDate, Guid? ClientOwnerUserId, Guid? FirmOwnerUserId,
  Guid? ReviewerUserId, Guid? PbcRequestId, string? PbcRequestState);
public sealed record WorkflowPlanStepPreview(string StepKey, Guid TemplateStepId, string Title, string Instructions,
  string Phase, int SortOrder, DateOnly? DueDate, int? PlannedEffortMinutes, string? SuggestedStaffingLevel,
  Guid? SelectedAssigneeUserId, IReadOnlyList<WorkflowPlanOwnerCandidate> EligibleOwners,
  IReadOnlyList<string> DependsOnStepKeys, bool MissingEligibleAssignee, Guid? ExistingTaskId,
  WorkflowPlanPbcPreview? Pbc = null);
public sealed record EngagementWorkflowPlanPreview(Guid EngagementId, long EngagementGeneration, Guid TemplateId,
  Guid TemplateRevisionId, int TemplateVersion, string TemplateName, string TemplateContentDigest,
  Guid? MilestonePlanId, long? MilestonePlanRevision, DateOnly? ScheduleBasisDate, string PreviewDigest,
  bool AlreadyInstantiated, int ExistingTaskCount, string? TemplateUpdateNotice,
  IReadOnlyList<WorkflowPlanStepPreview> Steps, string EngagementStatus,
  IReadOnlyList<WorkflowPlanPbcCandidate> PbcClientOwners, IReadOnlyList<WorkflowPlanPbcCandidate> PbcFirmOwners,
  IReadOnlyList<WorkflowPlanPbcCandidate> PbcReviewers, bool CanExecute = false, bool CanCreatePbcDrafts = false);
public sealed record WorkflowPlanPbcDraftLink(string StepKey, Guid TemplateStepId, Guid TaskId, Guid PbcRequestId);
public sealed record EngagementWorkflowPlanReceipt(Guid ReceiptId, Guid EngagementId, Guid TemplateId,
  Guid TemplateRevisionId, int TemplateVersion, string RequestDigest, int TaskCount, bool Replayed,
  DateTimeOffset CreatedAt, IReadOnlyList<Guid> TaskIds, IReadOnlyList<WorkflowPlanPbcDraftLink> PbcDrafts);

/// <summary>
/// Instantiates an immutable published template revision as supporting WorkTask records. The receipt pins the exact
/// engagement generation, staffing snapshot and statutory schedule. It never advances lifecycle or audit decisions.
/// A typed template PBC step becomes one fresh unsent DRAFT request through <see cref="PbcService.CreateRequestAsync"/>,
/// bound to that step's task in the same transaction. Sending remains a separate command.
/// </summary>
public static class EngagementWorkflowPlanService
{
  private static readonly string[] ReadRoles = ["Administrator", "Partner", "Manager", "Senior", "Staff"];
  private static readonly string[] ConfigureRoles = ["Administrator", "Partner", "Manager"];
  // Same reviewer population the PBC inbox offers; expiry and the scope are checked here as well.
  private static readonly string[] PbcReviewerRoles = ["Reviewer", "Manager", "Partner", "AccountingReviewer"];
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  /// <summary>Published choices for one authorized engagement; never exposes drafts or grants template administration.</summary>
  public static async Task<CommandResult<IReadOnlyList<WorkflowTemplateCatalogItem>>> TemplatesAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null)
      return CommandResult<IReadOnlyList<WorkflowTemplateCatalogItem>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, engagement, ReadRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<IReadOnlyList<WorkflowTemplateCatalogItem>>.Fail(auth.ErrorCode!, auth.Message!);
    var choices = await (from template in db.WorkflowTemplates.AsNoTracking()
      join revision in db.WorkflowTemplateRevisions.AsNoTracking() on template.Id equals revision.TemplateId
      where template.FirmId == actor.FirmId && revision.FirmId == actor.FirmId && template.ArchivedAt == null &&
        revision.Status == WorkflowTemplateStates.Published && revision.ServiceRoute == engagement.ServiceRoute
      orderby revision.Name, template.Id
      select new { Template = template, Revision = revision }).Take(100).ToListAsync(ct);
    auth = await AuthorizeAsync(db, actor, engagement, ReadRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<IReadOnlyList<WorkflowTemplateCatalogItem>>.Fail(auth.ErrorCode!, auth.Message!);
    IReadOnlyList<WorkflowTemplateCatalogItem> result = choices.Select(x => new WorkflowTemplateCatalogItem(
      x.Template.Id, x.Revision.Id, x.Revision.Version, x.Revision.Name, x.Revision.ServiceRoute,
      x.Revision.IndustryKey, x.Revision.Status, false, x.Template.CreatedAt,
      new WorkflowTemplatePublishedRevision(x.Revision.Id, x.Revision.Version, x.Revision.Name,
        x.Revision.ServiceRoute, x.Revision.IndustryKey, x.Revision.ContentDigest))).ToArray();
    return CommandResult<IReadOnlyList<WorkflowTemplateCatalogItem>>.Ok(result);
  }

  public static async Task<CommandResult<EngagementWorkflowPlanPreview>> PreviewAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId,
    PreviewEngagementWorkflowPlanRequest request, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null)
      return CommandResult<EngagementWorkflowPlanPreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<EngagementWorkflowPlanPreview>.Fail(auth.ErrorCode!, auth.Message!);
    var configure = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, ConfigureRoles, InternalOnly: true), ct);
    var loaded = await LoadPreviewAsync(db, actor, engagement, request.TemplateRevisionId, request.Owners,
      request.PbcOwners, ct);
    return loaded.Succeeded
      ? CommandResult<EngagementWorkflowPlanPreview>.Ok(loaded.Value! with
        { CanExecute = configure.Succeeded && !loaded.Value!.AlreadyInstantiated &&
            (!engagement.Status.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
              loaded.Value.Steps.All(x => x.Pbc?.State is not null &&
                (x.Pbc.State is WorkflowPlanPbcStates.Linked or WorkflowPlanPbcStates.UnresolvedReference) ||
                x.Pbc is null || x.Pbc.State == WorkflowPlanPbcStates.ReadyDraft)),
          CanCreatePbcDrafts = configure.Succeeded && loaded.Value!.CanCreatePbcDrafts })
      : CommandResult<EngagementWorkflowPlanPreview>.Fail(loaded.ErrorCode!, loaded.Message!);
  }

  public static async Task<CommandResult<EngagementWorkflowPlanReceipt>> ExecuteAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId,
    ExecuteEngagementWorkflowPlanRequest request, CancellationToken ct = default)
  {
    if (request.IdempotencyKey == Guid.Empty || !IsDigest(request.ExpectedPreviewDigest))
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail("workflow.plan-invalid",
        "A valid preview digest and non-empty idempotency key are required.");
    var snapshot = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (snapshot is null)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, snapshot, ConfigureRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    if (await db.FirmSafetyStates.FromSqlInterpolated(
          $"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct) is null)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    if (await db.ClientSafetyStates.FromSqlInterpolated(
          $"SELECT * FROM client_safety_states WHERE firm_id={actor.FirmId} AND id={snapshot.PracticeClientId} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct) is null)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    var engagement = await db.Engagements.FromSqlInterpolated(
        $"SELECT * FROM engagements WHERE firm_id={actor.FirmId} AND id={engagementId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (engagement is null || engagement.PracticeClientId != snapshot.PracticeClientId)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    auth = await AuthorizeAsync(db, actor, engagement, ConfigureRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(auth.ErrorCode!, auth.Message!);

    // A client may lose the successful execute response and retry after staffing or schedule facts have changed.
    // Recover the immutable receipt by its idempotency key before comparing today's preview with the old digest.
    var priorByKey = await db.WorkflowTaskPlanReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.IdempotencyKey == request.IdempotencyKey, ct);
    if (priorByKey is not null)
    {
      if (priorByKey.TemplateRevisionId != request.TemplateRevisionId ||
          priorByKey.RequestDigest != request.ExpectedPreviewDigest)
        return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.IdempotencyConflict,
          "This plan idempotency key was already used for different content.");
      if (tx is not null) await tx.CommitAsync(ct);
      return CommandResult<EngagementWorkflowPlanReceipt>.Ok(await ReceiptViewAsync(db, priorByKey, replayed: true, ct));
    }

    var template = await db.WorkflowTemplates.FromSqlInterpolated(
        $"SELECT * FROM workflow_templates WHERE firm_id={actor.FirmId} AND id=(SELECT template_id FROM workflow_template_revisions WHERE firm_id={actor.FirmId} AND id={request.TemplateRevisionId}) FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    var revision = await db.WorkflowTemplateRevisions.FromSqlInterpolated(
        $"SELECT * FROM workflow_template_revisions WHERE firm_id={actor.FirmId} AND id={request.TemplateRevisionId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (template is null || template.ArchivedAt.HasValue || revision is null ||
        revision.TemplateId != template.Id || revision.Status != WorkflowTemplateStates.Published)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.ProtectedState,
        "Choose the current published revision of an active workflow template.");

    var preview = await LoadPreviewAsync(db, actor, engagement, revision.Id, request.Owners, request.PbcOwners, ct);
    if (!preview.Succeeded)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(preview.ErrorCode!, preview.Message!);
    if (engagement.Status.Equals("Active", StringComparison.OrdinalIgnoreCase) &&
        preview.Value!.Steps.Any(x => x.Pbc is { Definition: not null } &&
          x.Pbc.State is not (WorkflowPlanPbcStates.ReadyDraft or WorkflowPlanPbcStates.Linked)))
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.GateBlocked,
        "Assign eligible client, firm and reviewer owners for every typed PBC step before executing this active plan.");
    if (!string.Equals(preview.Value!.PreviewDigest, request.ExpectedPreviewDigest, StringComparison.Ordinal))
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.StaleRevision,
        "Engagement, staffing, schedule or template inputs changed after preview. Review a fresh task plan.");

    var priorForRevision = await db.WorkflowTaskPlanReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.TemplateRevisionId == revision.Id, ct);
    if (priorForRevision is not null)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.IdempotencyConflict,
        "This engagement already has a plan for the selected template revision. Recover it with its original idempotency key.");
    var priorForTemplate = await (from existingReceipt in db.WorkflowTaskPlanReceipts.AsNoTracking()
      join oldRevision in db.WorkflowTemplateRevisions.AsNoTracking()
        on new { existingReceipt.FirmId, existingReceipt.TemplateId, existingReceipt.TemplateRevisionId }
        equals new { oldRevision.FirmId, oldRevision.TemplateId, TemplateRevisionId = oldRevision.Id }
      where existingReceipt.FirmId == actor.FirmId && existingReceipt.EngagementId == engagementId && existingReceipt.TemplateId == template.Id
      orderby oldRevision.Version descending
      select oldRevision.Version).FirstOrDefaultAsync(ct);
    if (priorForTemplate > 0)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.ProtectedState,
        $"Template updated; this engagement retains v{priorForTemplate}. Explicit plan migration is not available yet.");

    var existingBindings = await db.WorkflowTaskBindings.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagementId &&
      (x.TemplateRevisionId == revision.Id || x.SourceKey.StartsWith($"TEMPLATE:{revision.Id:N}:")))
      .ToListAsync(ct);
    if (existingBindings.Count > 0)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.ProtectedState,
        "Template task rows exist without a plan receipt. Review the existing engagement tasks before retrying.");

    var bindingsByTemplateStep = new Dictionary<Guid, WorkflowTaskBinding>();
    foreach (var step in preview.Value.Steps)
    {
      var stepRow = await db.WorkflowTemplateSteps.AsNoTracking().SingleAsync(x =>
        x.FirmId == actor.FirmId && x.TemplateRevisionId == revision.Id && x.Id == step.TemplateStepId, ct);
      var stepDigest = Digest(new { preview.Value.PreviewDigest, step.StepKey, step.TemplateStepId, step.DueDate,
        step.SelectedAssigneeUserId, step.SortOrder });
      var binding = new WorkflowTaskBinding
      {
        TemplateRevisionId = revision.Id,
        TemplateStepId = step.TemplateStepId,
        Phase = step.Phase,
        Ordinal = step.SortOrder,
        SourceKey = $"TEMPLATE:{revision.Id:N}:{step.StepKey}",
        CreationRequestDigest = stepDigest,
        Generation = 1,
        PlannedEffortMinutes = step.PlannedEffortMinutes,
        DueDatePolicy = stepRow.DueDateOffsetDays.HasValue ? "TEMPLATE_OFFSET" : "MANUAL",
        AssignedRole = step.SuggestedStaffingLevel,
        AssignmentSource = "TEMPLATE"
      };
      var created = await PracticeTimeService.CreateWorkflowTaskAsync(db, actor,
        new CreateTaskRequest(step.Title, ClientId: engagement.PracticeClientId, EngagementId: engagement.Id,
          AssigneeUserId: step.SelectedAssigneeUserId, DueDate: step.DueDate), binding, ct);
      if (!created.Succeeded)
        return CommandResult<EngagementWorkflowPlanReceipt>.Fail(created.ErrorCode!, created.Message!);
      bindingsByTemplateStep.Add(step.TemplateStepId, binding);
    }

    if (engagement.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
    {
      foreach (var step in preview.Value.Steps.Where(x => x.Pbc?.State == WorkflowPlanPbcStates.ReadyDraft))
      {
        var plan = step.Pbc!;
        var definition = plan.Definition!;
        var createdPbc = await PbcService.CreateRequestAsync(db, actor, new CreatePbcRequestRequest(
          engagement.Id, definition.Objective, definition.EntityScope, engagement.PeriodStart, engagement.PeriodEnd,
          definition.Area, definition.RequestedFormat, definition.ControlTotals ?? string.Empty,
          plan.ClientOwnerUserId!.Value, plan.FirmOwnerUserId!.Value, plan.ReviewerUserId!.Value,
          plan.DueDate!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
          definition.Confidentiality, definition.AcceptanceCriteria), ct);
        if (!createdPbc.Succeeded)
          return CommandResult<EngagementWorkflowPlanReceipt>.Fail(createdPbc.ErrorCode!, createdPbc.Message!);
        bindingsByTemplateStep[step.TemplateStepId].PbcRequestId = createdPbc.Value;
      }
    }

    var templateDependencies = await db.WorkflowTemplateDependencies.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.TemplateRevisionId == revision.Id).ToListAsync(ct);
    foreach (var edge in templateDependencies)
    {
      if (!bindingsByTemplateStep.TryGetValue(edge.PredecessorStepId, out var predecessor) ||
          !bindingsByTemplateStep.TryGetValue(edge.SuccessorStepId, out var successor))
        return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.GateBlocked,
          "The published template contains a dependency outside its task plan.");
      db.WorkflowTaskDependencies.Add(new WorkflowTaskDependency
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = engagement.PracticeClientId,
        EngagementId = engagement.Id, TemplateRevisionId = revision.Id, TemplateDependencyId = edge.Id,
        PredecessorBindingId = predecessor.Id, SuccessorBindingId = successor.Id,
        DependencyType = edge.DependencyType, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
      });
    }

    var receipt = new WorkflowTaskPlanReceipt
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = engagement.PracticeClientId,
      EngagementId = engagement.Id, TemplateId = template.Id, TemplateRevisionId = revision.Id,
      TemplateVersion = revision.Version, EngagementGeneration = engagement.Generation,
      MilestonePlanId = preview.Value.MilestonePlanId,
      MilestonePlanRevision = preview.Value.MilestonePlanRevision,
      ScheduleBasisDate = preview.Value.ScheduleBasisDate,
      IdempotencyKey = request.IdempotencyKey, RequestDigest = preview.Value.PreviewDigest,
      TaskCount = preview.Value.Steps.Count, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.WorkflowTaskPlanReceipts.Add(receipt);
    await db.SaveChangesAsync(ct);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<EngagementWorkflowPlanReceipt>.Ok(await ReceiptViewAsync(db, receipt, replayed: false, ct));
  }

  public static async Task<CommandResult<EngagementWorkflowPlanReceipt>> GetReceiptAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, Guid idempotencyKey,
    CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, engagement, ReadRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(auth.ErrorCode!, auth.Message!);
    var receipt = await db.WorkflowTaskPlanReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.IdempotencyKey == idempotencyKey, ct);
    return receipt is null
      ? CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.")
      : CommandResult<EngagementWorkflowPlanReceipt>.Ok(await ReceiptViewAsync(db, receipt, replayed: true, ct));
  }

  /// <summary>Materializes deferred, still-unsent PBC drafts after activation for an already retained task plan.</summary>
  public static async Task<CommandResult<EngagementWorkflowPlanReceipt>> CreatePbcDraftsAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CreateWorkflowPlanPbcDraftsRequest request,
    CancellationToken ct = default)
  {
    if (!IsDigest(request.ExpectedPreviewDigest))
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail("workflow.plan-invalid", "A valid current preview digest is required.");
    var snapshot = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (snapshot is null)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, snapshot, ConfigureRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    if (await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct) is null)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    if (await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={actor.FirmId} AND id={snapshot.PracticeClientId} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct) is null)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    var engagement = await db.Engagements.FromSqlInterpolated(
        $"SELECT * FROM engagements WHERE firm_id={actor.FirmId} AND id={engagementId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (engagement is null || engagement.PracticeClientId != snapshot.PracticeClientId)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    auth = await AuthorizeAsync(db, actor, engagement, ConfigureRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(auth.ErrorCode!, auth.Message!);
    if (!engagement.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(WorkflowPlanPbcStates.BlockedActivation,
        "PBC drafts can be created only after the engagement is active.");

    var receipt = await db.WorkflowTaskPlanReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagement.Id && x.TemplateRevisionId == request.TemplateRevisionId, ct);
    if (receipt is null)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.ScopeDenied, "The retained task plan is unavailable.");
    var template = await db.WorkflowTemplates.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == receipt.TemplateId, ct);
    var revision = await db.WorkflowTemplateRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.TemplateId == receipt.TemplateId && x.Id == receipt.TemplateRevisionId, ct);
    if (template is null || template.ArchivedAt.HasValue || revision is null ||
        revision.Status is not (WorkflowTemplateStates.Published or WorkflowTemplateStates.Superseded))
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.ProtectedState,
        "The retained typed PBC definition is no longer available for fresh review.");
    var preview = await LoadPreviewAsync(db, actor, engagement, revision.Id, null, request.PbcOwners, ct,
      allowSupersededPinnedRevision: true);
    if (!preview.Succeeded)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(preview.ErrorCode!, preview.Message!);
    if (!string.Equals(preview.Value!.PreviewDigest, request.ExpectedPreviewDigest, StringComparison.Ordinal))
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.StaleRevision,
        "Engagement, owner eligibility or template inputs changed after preview. Review the PBC drafts again.");
    var boundRequests = await db.WorkflowTaskBindings.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.EngagementId == engagement.Id && x.TemplateRevisionId == revision.Id &&
        x.TemplateStepId.HasValue && x.PbcRequestId.HasValue)
      .Select(x => x.TemplateStepId!.Value).ToListAsync(ct);
    var typedStepIds = await db.WorkflowTemplateSteps.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.TemplateRevisionId == revision.Id && x.PbcObjective != null)
      .Select(x => x.Id).ToListAsync(ct);
    if (typedStepIds.Count > 0 && typedStepIds.All(boundRequests.Contains))
    {
      if (tx is not null) await tx.CommitAsync(ct);
      return CommandResult<EngagementWorkflowPlanReceipt>.Ok(await ReceiptViewAsync(db, receipt, replayed: true, ct));
    }
    if (!preview.Value.CanCreatePbcDrafts)
      return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.GateBlocked,
        "No eligible pending PBC draft is ready to create.");

    var bindings = await db.WorkflowTaskBindings.Where(x => x.FirmId == actor.FirmId &&
      x.EngagementId == engagement.Id && x.TemplateRevisionId == revision.Id).ToListAsync(ct);
    var bindingByStep = bindings.Where(x => x.TemplateStepId.HasValue)
      .ToDictionary(x => x.TemplateStepId!.Value);
    foreach (var step in preview.Value.Steps.Where(x => x.Pbc?.State == WorkflowPlanPbcStates.ReadyDraft))
    {
      if (!bindingByStep.TryGetValue(step.TemplateStepId, out var binding) || binding.PbcRequestId.HasValue ||
          !step.Pbc!.DueDate.HasValue || !step.Pbc.ClientOwnerUserId.HasValue ||
          !step.Pbc.FirmOwnerUserId.HasValue || !step.Pbc.ReviewerUserId.HasValue)
        return CommandResult<EngagementWorkflowPlanReceipt>.Fail(ErrorCodes.StaleRevision,
          "A task/PBC link or current PBC owner changed. Review a fresh plan before retrying.");
      var definition = step.Pbc.Definition!;
      var createdPbc = await PbcService.CreateRequestAsync(db, actor, new CreatePbcRequestRequest(
        engagement.Id, definition.Objective, definition.EntityScope, engagement.PeriodStart, engagement.PeriodEnd,
        definition.Area, definition.RequestedFormat, definition.ControlTotals ?? string.Empty,
        step.Pbc.ClientOwnerUserId.Value, step.Pbc.FirmOwnerUserId.Value, step.Pbc.ReviewerUserId.Value,
        step.Pbc.DueDate.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        definition.Confidentiality, definition.AcceptanceCriteria), ct);
      if (!createdPbc.Succeeded)
        return CommandResult<EngagementWorkflowPlanReceipt>.Fail(createdPbc.ErrorCode!, createdPbc.Message!);
      binding.PbcRequestId = createdPbc.Value;
    }
    await db.SaveChangesAsync(ct);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<EngagementWorkflowPlanReceipt>.Ok(await ReceiptViewAsync(db, receipt, replayed: true, ct));
  }

  private static async Task<CommandResult<EngagementWorkflowPlanPreview>> LoadPreviewAsync(
    IAuditSphereDbContext db, ActorContext actor, Engagement engagement, Guid revisionId,
    IReadOnlyList<WorkflowPlanOwnerSelection>? requestedOwners,
    IReadOnlyList<WorkflowPlanPbcOwnerSelection>? requestedPbcOwners, CancellationToken ct,
    bool allowSupersededPinnedRevision = false)
  {
    if (requestedOwners is { Count: > 100 })
      return CommandResult<EngagementWorkflowPlanPreview>.Fail("workflow.plan-invalid", "Owner selections are limited to 100 template steps.");
    if (requestedPbcOwners is { Count: > 100 })
      return CommandResult<EngagementWorkflowPlanPreview>.Fail("workflow.plan-invalid", "PBC owner selections are limited to 100 template steps.");
    var ownerMap = new Dictionary<string, Guid>(StringComparer.Ordinal);
    foreach (var owner in requestedOwners ?? [])
    {
      var key = (owner.StepKey ?? string.Empty).Trim();
      if (key.Length is < 1 or > 80 || owner.AssigneeUserId == Guid.Empty || !ownerMap.TryAdd(key, owner.AssigneeUserId))
        return CommandResult<EngagementWorkflowPlanPreview>.Fail("workflow.plan-invalid", "Owner selections must have unique stable step keys and valid staff IDs.");
    }
    var pbcOwnerMap = new Dictionary<string, WorkflowPlanPbcOwnerSelection>(StringComparer.Ordinal);
    foreach (var owner in requestedPbcOwners ?? [])
    {
      var key = (owner.StepKey ?? string.Empty).Trim();
      if (key.Length is < 1 or > 80 || owner.ClientOwnerUserId == Guid.Empty || owner.FirmOwnerUserId == Guid.Empty ||
          owner.ReviewerUserId == Guid.Empty || owner.ClientOwnerUserId == owner.FirmOwnerUserId ||
          owner.ClientOwnerUserId == owner.ReviewerUserId || owner.FirmOwnerUserId == owner.ReviewerUserId ||
          !pbcOwnerMap.TryAdd(key, owner))
        return CommandResult<EngagementWorkflowPlanPreview>.Fail("workflow.plan-invalid",
          "PBC owner selections must use unique step keys and three distinct active user IDs.");
    }

    var revision = await db.WorkflowTemplateRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == revisionId, ct);
    if (revision is null || (revision.Status != WorkflowTemplateStates.Published &&
        !(allowSupersededPinnedRevision && revision.Status == WorkflowTemplateStates.Superseded)))
      return CommandResult<EngagementWorkflowPlanPreview>.Fail(ErrorCodes.ProtectedState,
        "Choose the current published revision of an active workflow template.");
    var template = await db.WorkflowTemplates.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == revision.TemplateId, ct);
    if (template is null || template.ArchivedAt.HasValue)
      return CommandResult<EngagementWorkflowPlanPreview>.Fail(ErrorCodes.ProtectedState, "The workflow template is archived or unavailable.");
    if (!string.Equals(revision.ServiceRoute, engagement.ServiceRoute, StringComparison.Ordinal))
      return CommandResult<EngagementWorkflowPlanPreview>.Fail(ErrorCodes.GateBlocked,
        "The published template service route does not match this engagement.");
    var steps = await db.WorkflowTemplateSteps.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.TemplateRevisionId == revision.Id)
      .OrderBy(x => x.SortOrder).ThenBy(x => x.StableKey).ToListAsync(ct);
    if (steps.Count == 0 || steps.Count > 100)
      return CommandResult<EngagementWorkflowPlanPreview>.Fail(ErrorCodes.GateBlocked, "The published template has no usable task steps.");
    if (ownerMap.Keys.Except(steps.Select(x => x.StableKey), StringComparer.Ordinal).Any())
      return CommandResult<EngagementWorkflowPlanPreview>.Fail("workflow.plan-invalid", "An owner selection references a step outside this template revision.");
    if (pbcOwnerMap.Keys.Except(steps.Select(x => x.StableKey), StringComparer.Ordinal).Any() ||
        pbcOwnerMap.Keys.Any(key => steps.Single(x => x.StableKey == key).PbcObjective is null))
      return CommandResult<EngagementWorkflowPlanPreview>.Fail("workflow.plan-invalid",
        "A PBC owner selection references a step without a typed PBC definition in this template revision.");

    var templateIds = steps.Select(x => x.Id).ToArray();
    var predecessorById = steps.ToDictionary(x => x.Id, x => x.StableKey);
    var edges = await db.WorkflowTemplateDependencies.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.TemplateRevisionId == revision.Id).ToListAsync(ct);
    if (HasDependencyCycle(steps.Select(x => x.Id), edges))
      return CommandResult<EngagementWorkflowPlanPreview>.Fail(ErrorCodes.GateBlocked,
        "The published template dependency graph is cyclic and cannot be instantiated.");
    var dependsByStep = edges.GroupBy(x => x.SuccessorStepId).ToDictionary(x => x.Key,
      x => (IReadOnlyList<string>)x.Select(edge => predecessorById[edge.PredecessorStepId]).Order(StringComparer.Ordinal).ToArray());

    var candidates = await (from assignment in db.EngagementStaffAssignments.AsNoTracking()
      join user in db.Users.AsNoTracking()
        on new { assignment.FirmId, assignment.UserId } equals new { user.FirmId, UserId = user.Id }
      join grant in db.RoleGrants.AsNoTracking()
        on new { assignment.FirmId, assignment.RoleGrantId } equals new { grant.FirmId, RoleGrantId = grant.Id }
      where assignment.FirmId == actor.FirmId && assignment.ClientId == engagement.PracticeClientId &&
        assignment.EngagementId == engagement.Id && assignment.RevokedAt == null && !user.Disabled && user.UserKind == "Staff" &&
        grant.RevokedAt == null && (grant.ExpiresAt == null || grant.ExpiresAt > DateTimeOffset.UtcNow)
      orderby assignment.StaffingLevel, user.DisplayName, user.Id
      select new WorkflowPlanOwnerCandidate(user.Id, user.DisplayName, assignment.StaffingLevel))
      .ToListAsync(ct);
    var now = DateTimeOffset.UtcNow;
    var clientOwnerCandidates = await db.Users.AsNoTracking().Where(user => user.FirmId == actor.FirmId &&
      user.UserKind == "Client" && !user.Disabled &&
      db.RoleGrants.Any(grant => grant.FirmId == actor.FirmId && grant.UserId == user.Id && grant.Role == "ClientUser" &&
        grant.ClientId == engagement.PracticeClientId && (grant.EngagementId == null || grant.EngagementId == engagement.Id) &&
        grant.RevokedAt == null && (grant.ExpiresAt == null || grant.ExpiresAt > now)) &&
      !db.RoleGrants.Any(grant => grant.FirmId == actor.FirmId && grant.UserId == user.Id && grant.Role != "ClientUser" &&
        grant.RevokedAt == null && (grant.ExpiresAt == null || grant.ExpiresAt > now)))
      .OrderBy(user => user.DisplayName).ThenBy(user => user.Id)
      .Select(user => new WorkflowPlanPbcCandidate(user.Id, user.DisplayName == string.Empty ? user.Email : user.DisplayName))
      .Take(100).ToListAsync(ct);
    var reviewerRoles = PbcReviewerRoles;
    var reviewerCandidates = await (from grant in db.RoleGrants.AsNoTracking()
      join user in db.Users.AsNoTracking() on new { grant.FirmId, grant.UserId } equals new { user.FirmId, UserId = user.Id }
      where grant.FirmId == actor.FirmId && grant.ClientId == engagement.PracticeClientId &&
        (grant.EngagementId == null || grant.EngagementId == engagement.Id) && reviewerRoles.Contains(grant.Role) &&
        grant.RevokedAt == null && (grant.ExpiresAt == null || grant.ExpiresAt > now) &&
        user.UserKind == "Staff" && !user.Disabled
      orderby user.DisplayName, user.Id
      select new WorkflowPlanPbcCandidate(user.Id, user.DisplayName == string.Empty ? user.Email : user.DisplayName))
      .Distinct().Take(100).ToListAsync(ct);
    var milestone = await db.EngagementMilestonePlans.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId && x.EngagementId == engagement.Id)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    var needsCutoff = steps.Any(x => x.DueDateOffsetDays.HasValue);
    if (needsCutoff && milestone is null)
      return CommandResult<EngagementWorkflowPlanPreview>.Fail("workflow.plan-schedule-required",
        "Save the engagement statutory milestone plan before previewing template due dates.");
    var existing = await db.WorkflowTaskBindings.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagement.Id && x.TemplateRevisionId == revision.Id)
      .Select(x => new { x.TemplateStepId, x.WorkTaskId, x.PbcRequestId }).ToListAsync(ct);
    var existingByStep = existing.Where(x => x.TemplateStepId.HasValue)
      .GroupBy(x => x.TemplateStepId!.Value).ToDictionary(x => x.Key, x => x.First().WorkTaskId);

    var planSteps = new List<WorkflowPlanStepPreview>(steps.Count);
    foreach (var step in steps)
    {
      var eligible = candidates.Where(x => step.SuggestedStaffingLevel is null || x.StaffingLevel == step.SuggestedStaffingLevel).ToArray();
      Guid? selected = ownerMap.TryGetValue(step.StableKey, out var requested) ? requested :
        step.SuggestedStaffingLevel is not null && eligible.Length == 1 ? eligible[0].UserId : null;
      if (selected.HasValue && eligible.All(x => x.UserId != selected.Value))
        return CommandResult<EngagementWorkflowPlanPreview>.Fail("workflow.plan-assignee-ineligible",
          $"The selected owner for '{step.StableKey}' is not an active eligible engagement assignee.");
      DateOnly? dueDate = null;
      if (step.DueDateOffsetDays.HasValue)
      {
        try { dueDate = milestone!.StatutoryFilingCutoff.AddDays(step.DueDateOffsetDays.Value); }
        catch (ArgumentOutOfRangeException)
        {
          return CommandResult<EngagementWorkflowPlanPreview>.Fail("workflow.plan-date-out-of-range",
            $"The due-date offset for '{step.StableKey}' falls outside the supported calendar range.");
        }
      }
      dependsByStep.TryGetValue(step.Id, out var stepDependencies);
      existingByStep.TryGetValue(step.Id, out var existingTaskId);
      WorkflowPlanPbcPreview? pbc = null;
      if (step.PbcObjective is not null || step.PbcRequestReference is not null)
      {
        var definition = WorkflowTemplateService.PbcDefinitionOf(step);
        if (definition is null)
          pbc = new WorkflowPlanPbcPreview(WorkflowPlanPbcStates.UnresolvedReference, step.PbcRequestReference,
            null, engagement.PeriodStart, engagement.PeriodEnd, dueDate, null, null, null, null, null);
        else if (existingByStep.TryGetValue(step.Id, out _))
        {
          var binding = existing.Single(x => x.TemplateStepId == step.Id);
          var linkedRequest = binding.PbcRequestId.HasValue
            ? await db.PbcRequests.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
                x.ClientId == engagement.PracticeClientId && x.EngagementId == engagement.Id && x.Id == binding.PbcRequestId.Value, ct)
            : null;
          if (linkedRequest is null)
          {
            pbcOwnerMap.TryGetValue(step.StableKey, out var selectedPbcOwners);
            var clientOwnerId = selectedPbcOwners?.ClientOwnerUserId ??
              (clientOwnerCandidates.Count == 1 ? clientOwnerCandidates[0].UserId : (Guid?)null);
            var firmOwnerId = selectedPbcOwners?.FirmOwnerUserId ??
              (candidates.Count == 1 ? candidates[0].UserId : (Guid?)null);
            var reviewerId = selectedPbcOwners?.ReviewerUserId ??
              (reviewerCandidates.Count == 1 ? reviewerCandidates[0].UserId : (Guid?)null);
            if ((clientOwnerId.HasValue && clientOwnerCandidates.All(x => x.UserId != clientOwnerId.Value)) ||
                (firmOwnerId.HasValue && candidates.All(x => x.UserId != firmOwnerId.Value)) ||
                (reviewerId.HasValue && reviewerCandidates.All(x => x.UserId != reviewerId.Value)))
              return CommandResult<EngagementWorkflowPlanPreview>.Fail("workflow.plan-pbc-owner-ineligible",
                $"The selected PBC owner for '{step.StableKey}' is not currently eligible in this client and engagement scope.");
            var allOwners = clientOwnerId.HasValue && firmOwnerId.HasValue && reviewerId.HasValue;
            var state = !engagement.Status.Equals("Active", StringComparison.OrdinalIgnoreCase)
              ? WorkflowPlanPbcStates.BlockedActivation
              : allOwners ? WorkflowPlanPbcStates.ReadyDraft : WorkflowPlanPbcStates.OwnersRequired;
            pbc = new WorkflowPlanPbcPreview(state, null, definition, engagement.PeriodStart, engagement.PeriodEnd,
              dueDate, clientOwnerId, firmOwnerId, reviewerId, null, allOwners ? "DRAFT" : null);
          }
          else pbc = new WorkflowPlanPbcPreview(WorkflowPlanPbcStates.Linked, null, definition, linkedRequest.PeriodStart,
                linkedRequest.PeriodEnd, dueDate, linkedRequest.ClientOwnerUserId, linkedRequest.FirmOwnerUserId,
                linkedRequest.ReviewerUserId, linkedRequest.Id, linkedRequest.State);
        }
        else
        {
          pbcOwnerMap.TryGetValue(step.StableKey, out var selectedPbcOwners);
          var clientOwnerId = selectedPbcOwners?.ClientOwnerUserId ??
            (clientOwnerCandidates.Count == 1 ? clientOwnerCandidates[0].UserId : (Guid?)null);
          var firmOwnerId = selectedPbcOwners?.FirmOwnerUserId ??
            (candidates.Count == 1 ? candidates[0].UserId : (Guid?)null);
          var reviewerId = selectedPbcOwners?.ReviewerUserId ??
            (reviewerCandidates.Count == 1 ? reviewerCandidates[0].UserId : (Guid?)null);
          if ((clientOwnerId.HasValue && clientOwnerCandidates.All(x => x.UserId != clientOwnerId.Value)) ||
              (firmOwnerId.HasValue && candidates.All(x => x.UserId != firmOwnerId.Value)) ||
              (reviewerId.HasValue && reviewerCandidates.All(x => x.UserId != reviewerId.Value)))
            return CommandResult<EngagementWorkflowPlanPreview>.Fail("workflow.plan-pbc-owner-ineligible",
              $"The selected PBC owner for '{step.StableKey}' is not currently eligible in this client and engagement scope.");
          var allOwners = clientOwnerId.HasValue && firmOwnerId.HasValue && reviewerId.HasValue;
          var state = !engagement.Status.Equals("Active", StringComparison.OrdinalIgnoreCase)
            ? WorkflowPlanPbcStates.BlockedActivation
            : allOwners ? WorkflowPlanPbcStates.ReadyDraft : WorkflowPlanPbcStates.OwnersRequired;
          pbc = new WorkflowPlanPbcPreview(state, null, definition, engagement.PeriodStart, engagement.PeriodEnd,
            dueDate, clientOwnerId, firmOwnerId, reviewerId, null, allOwners ? "DRAFT" : null);
        }
      }
      planSteps.Add(new WorkflowPlanStepPreview(step.StableKey, step.Id, step.Title, step.Instructions, step.Phase,
        step.SortOrder, dueDate, step.EstimatedEffortMinutes, step.SuggestedStaffingLevel, selected,
        eligible, stepDependencies ?? [], step.SuggestedStaffingLevel is not null && eligible.Length == 0,
        existingTaskId == Guid.Empty ? null : existingTaskId, pbc));
    }

    var latestPublishedVersion = await db.WorkflowTemplateRevisions.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.TemplateId == template.Id &&
      (x.Status == WorkflowTemplateStates.Published || x.Status == WorkflowTemplateStates.Superseded))
      .MaxAsync(x => (int?)x.Version, ct) ?? revision.Version;
    var notice = latestPublishedVersion > revision.Version
      ? $"Template updated; engagement will retain v{revision.Version}."
      : null;
    var rosterDigest = candidates.Select(x => new { x.UserId, x.StaffingLevel }).ToArray();
    var digest = Digest(new
    {
      EngagementId = engagement.Id, ClientId = engagement.PracticeClientId, EngagementGeneration = engagement.Generation,
      engagement.ServiceRoute, engagement.PeriodStart, engagement.PeriodEnd,
      TemplateRevisionId = revision.Id, TemplateVersion = revision.Version, TemplateContentDigest = revision.ContentDigest,
      MilestoneId = needsCutoff ? milestone!.Id : (Guid?)null,
      MilestoneRevision = needsCutoff ? milestone!.Revision : (long?)null,
      ScheduleBasis = needsCutoff ? milestone!.StatutoryFilingCutoff : (DateOnly?)null,
      Roster = rosterDigest,
      Steps = planSteps.Select(x => new { x.StepKey, x.TemplateStepId, x.Phase, x.SortOrder, x.DueDate,
        x.PlannedEffortMinutes, x.SuggestedStaffingLevel, x.SelectedAssigneeUserId, DependsOn = x.DependsOnStepKeys,
        x.Pbc?.State, x.Pbc?.ClientOwnerUserId, x.Pbc?.FirmOwnerUserId, x.Pbc?.ReviewerUserId }).ToArray()
    });
    var receipt = await db.WorkflowTaskPlanReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagement.Id && x.TemplateRevisionId == revision.Id, ct);
    return CommandResult<EngagementWorkflowPlanPreview>.Ok(new EngagementWorkflowPlanPreview(engagement.Id,
      engagement.Generation, template.Id, revision.Id, revision.Version, revision.Name, revision.ContentDigest,
      needsCutoff ? milestone!.Id : null, needsCutoff ? milestone!.Revision : null,
      needsCutoff ? milestone!.StatutoryFilingCutoff : null, digest, receipt is not null,
      existing.Count, notice, planSteps, engagement.Status, clientOwnerCandidates, candidates
        .Select(x => new WorkflowPlanPbcCandidate(x.UserId, x.DisplayName)).ToArray(), reviewerCandidates,
      CanCreatePbcDrafts: engagement.Status.Equals("Active", StringComparison.OrdinalIgnoreCase) &&
        planSteps.Any(x => x.Pbc?.State == WorkflowPlanPbcStates.ReadyDraft)));
  }

  private static async Task<EngagementWorkflowPlanReceipt> ReceiptViewAsync(
    IAuditSphereDbContext db, WorkflowTaskPlanReceipt receipt, bool replayed, CancellationToken ct)
  {
    var bindings = await db.WorkflowTaskBindings.AsNoTracking().Where(x =>
      x.FirmId == receipt.FirmId && x.EngagementId == receipt.EngagementId &&
      x.TemplateRevisionId == receipt.TemplateRevisionId)
      .OrderBy(x => x.Ordinal).ThenBy(x => x.WorkTaskId)
      .Select(x => new { x.TemplateStepId, x.WorkTaskId, x.PbcRequestId }).ToListAsync(ct);
    var taskIds = bindings.Select(x => x.WorkTaskId).ToArray();
    var stepIds = bindings.Where(x => x.TemplateStepId.HasValue).Select(x => x.TemplateStepId!.Value).ToArray();
    var steps = await db.WorkflowTemplateSteps.AsNoTracking().Where(x =>
      x.FirmId == receipt.FirmId && x.TemplateRevisionId == receipt.TemplateRevisionId && stepIds.Contains(x.Id))
      .Select(x => new { x.Id, x.StableKey }).ToDictionaryAsync(x => x.Id, x => x.StableKey, ct);
    var pbcDrafts = bindings.Where(x => x.TemplateStepId.HasValue && x.PbcRequestId.HasValue && steps.ContainsKey(x.TemplateStepId.Value))
      .Select(x => new WorkflowPlanPbcDraftLink(steps[x.TemplateStepId!.Value], x.TemplateStepId.Value,
        x.WorkTaskId, x.PbcRequestId!.Value)).ToArray();
    return new EngagementWorkflowPlanReceipt(receipt.Id,
      receipt.EngagementId, receipt.TemplateId, receipt.TemplateRevisionId, receipt.TemplateVersion,
      receipt.RequestDigest, receipt.TaskCount, replayed, receipt.CreatedAt, taskIds, pbcDrafts);
  }

  private static bool HasDependencyCycle(IEnumerable<Guid> stepIds, IReadOnlyCollection<WorkflowTemplateDependency> edges)
  {
    var successors = stepIds.ToDictionary(x => x, _ => new List<Guid>());
    foreach (var edge in edges)
    {
      if (!successors.TryGetValue(edge.PredecessorStepId, out var next) ||
          !successors.ContainsKey(edge.SuccessorStepId)) return true;
      next.Add(edge.SuccessorStepId);
    }
    var visiting = new HashSet<Guid>();
    var visited = new HashSet<Guid>();
    bool Visit(Guid id)
    {
      if (visiting.Contains(id)) return true;
      if (!visited.Add(id)) return false;
      visiting.Add(id);
      foreach (var next in successors[id])
        if (Visit(next)) return true;
      visiting.Remove(id);
      return false;
    }
    return successors.Keys.Any(Visit);
  }

  private static async Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor,
    Engagement engagement, IReadOnlyList<string> roles, CancellationToken ct) =>
    await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, roles.ToArray(), InternalOnly: true), ct);

  private static string Digest<T>(T value) => Convert.ToHexString(SHA256.HashData(
    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json)))).ToLowerInvariant();

  private static bool IsDigest(string? value) => value is { Length: 64 } && value.All(c =>
    c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
