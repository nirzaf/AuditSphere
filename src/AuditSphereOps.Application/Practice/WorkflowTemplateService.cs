using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>
/// Reusable PBC instruction fields of the existing <c>CreatePbcRequestRequest</c>. Engagement, actual period, owners,
/// reviewer and due date are resolved for each engagement plan; nothing from a prior request is copied.
/// </summary>
public sealed record WorkflowTemplatePbcDefinition(
  string Objective,
  string EntityScope,
  string Area,
  string RequestedFormat,
  string? ControlTotals,
  string Confidentiality,
  string AcceptanceCriteria);

public sealed record WorkflowTemplateEvidenceRequirementDraft(string RequirementKey, string Kind);
public sealed record WorkflowTemplateEvidenceRequirementView(string RequirementKey, string Kind);

public sealed record WorkflowTemplateStepDraft(
  string StableKey,
  string Phase,
  string Title,
  string Instructions,
  string? SuggestedStaffingLevel,
  int? EstimatedEffortMinutes,
  int? DueDateOffsetDays,
  string? PbcRequestReference,
  bool RequiredTask,
  int SortOrder,
  IReadOnlyList<string>? DependsOnStepKeys = null,
  WorkflowTemplatePbcDefinition? PbcDefinition = null,
  string? TaskGroupName = null,
  IReadOnlyList<WorkflowTemplateEvidenceRequirementDraft>? EvidenceRequirements = null);

public sealed record SaveWorkflowTemplateDraftRequest(
  Guid? TemplateId,
  string Name,
  string ServiceRoute,
  string? IndustryKey,
  IReadOnlyList<WorkflowTemplateStepDraft> Steps,
  long? ExpectedRevision = null);

public sealed record WorkflowTemplateStepView(
  Guid Id,
  string StableKey,
  string Phase,
  string Title,
  string Instructions,
  string? SuggestedStaffingLevel,
  int? EstimatedEffortMinutes,
  int? DueDateOffsetDays,
  string? PbcRequestReference,
  bool RequiredTask,
  int SortOrder,
  IReadOnlyList<string> DependsOnStepKeys,
  WorkflowTemplatePbcDefinition? PbcDefinition = null,
  string? TaskGroupName = null,
  IReadOnlyList<WorkflowTemplateEvidenceRequirementView>? EvidenceRequirements = null);

public sealed record WorkflowTemplateRevisionView(
  Guid TemplateId,
  Guid RevisionId,
  int Version,
  string Name,
  string ServiceRoute,
  string? IndustryKey,
  string Status,
  string ContentDigest,
  long Revision,
  Guid CreatedByUserId,
  Guid LastEditedByUserId,
  Guid? ReviewedByUserId,
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt,
  DateTimeOffset? PublishedAt,
  DateTimeOffset? ArchivedAt,
  IReadOnlyList<WorkflowTemplateStepView> Steps);

public sealed record WorkflowTemplatePublishedRevision(
  Guid RevisionId,
  int Version,
  string Name,
  string ServiceRoute,
  string? IndustryKey,
  string ContentDigest);

public sealed record WorkflowTemplateCatalogItem(
  Guid TemplateId,
  Guid? LatestRevisionId,
  int? Version,
  string? Name,
  string? ServiceRoute,
  string? IndustryKey,
  string? RevisionStatus,
  bool Archived,
  DateTimeOffset CreatedAt,
  WorkflowTemplatePublishedRevision? CurrentPublishedRevision);

/// <summary>
/// Firm-wide operational checklist templates. They only create or organize WorkTask records and never satisfy a
/// professional, commercial, lifecycle, release, or archive gate. Each published revision is content-locked in code
/// and PostgreSQL; later edits are a new revision and existing engagements keep their pin.
/// </summary>
public static class WorkflowTemplateService
{
  private static readonly string[] ReadRoles = ["Administrator", "Partner", "Manager", "Senior", "Staff"];
  private static readonly string[] ConfigureRoles = ["Administrator", "Partner"];
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public static async Task<CommandResult<IReadOnlyList<WorkflowTemplateCatalogItem>>> ListAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ReadRoles, ct);
    if (!auth.Succeeded) return CommandResult<IReadOnlyList<WorkflowTemplateCatalogItem>>.Fail(auth.ErrorCode!, auth.Message!);
    var templates = await db.WorkflowTemplates.AsNoTracking().Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.CreatedAt).Take(101).ToListAsync(ct);
    var ids = templates.Select(x => x.Id).ToArray();
    var revisions = await db.WorkflowTemplateRevisions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && ids.Contains(x.TemplateId))
      .OrderByDescending(x => x.Version).ToListAsync(ct);
    var latest = revisions.GroupBy(x => x.TemplateId).ToDictionary(x => x.Key, x => x.First());
    var published = revisions.Where(x => x.Status == WorkflowTemplateStates.Published)
      .GroupBy(x => x.TemplateId).ToDictionary(x => x.Key, x => x.First());
    IReadOnlyList<WorkflowTemplateCatalogItem> result = templates.Take(100).Select(template =>
    {
      latest.TryGetValue(template.Id, out var revision);
      published.TryGetValue(template.Id, out var publishedRevision);
      return new WorkflowTemplateCatalogItem(template.Id, revision?.Id, revision?.Version, revision?.Name,
        revision?.ServiceRoute, revision?.IndustryKey, revision?.Status, template.ArchivedAt.HasValue, template.CreatedAt,
        publishedRevision is null ? null : new WorkflowTemplatePublishedRevision(publishedRevision.Id,
          publishedRevision.Version, publishedRevision.Name, publishedRevision.ServiceRoute,
          publishedRevision.IndustryKey, publishedRevision.ContentDigest));
    }).ToArray();
    return CommandResult<IReadOnlyList<WorkflowTemplateCatalogItem>>.Ok(result);
  }

  public static async Task<CommandResult<WorkflowTemplateRevisionView>> GetRevisionAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid templateId, Guid revisionId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ReadRoles, ct);
    if (!auth.Succeeded) return CommandResult<WorkflowTemplateRevisionView>.Fail(auth.ErrorCode!, auth.Message!);
    var revision = await db.WorkflowTemplateRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.TemplateId == templateId && x.Id == revisionId, ct);
    if (revision is null) return CommandResult<WorkflowTemplateRevisionView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult<WorkflowTemplateRevisionView>.Ok(await BuildViewAsync(db, revision, ct));
  }

  /// <summary>Creates a new template or the next draft revision of an existing template.</summary>
  public static async Task<CommandResult<Guid>> CreateDraftAsync(
    IAuditSphereDbContext db, ActorContext actor, SaveWorkflowTemplateDraftRequest request, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var definition = NormalizeAndValidate(request);
    if (definition.Error is not null || definition.Value is null)
      return CommandResult<Guid>.Fail("workflow.template-invalid", definition.Error!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    WorkflowTemplate template;
    int version;
    if (request.TemplateId is { } templateId)
    {
      var lockedTemplate = await LockTemplateAsync(db, actor.FirmId, templateId, ct);
      if (lockedTemplate is null || lockedTemplate.ArchivedAt.HasValue)
        return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      template = lockedTemplate;
      var revisions = await db.WorkflowTemplateRevisions.Where(x => x.FirmId == actor.FirmId && x.TemplateId == templateId)
        .OrderByDescending(x => x.Version).ToListAsync(ct);
      if (revisions.Any(x => x.Status is WorkflowTemplateStates.Draft or WorkflowTemplateStates.Reviewed))
        return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "Finish or supersede the existing draft before creating another template revision.");
      version = (revisions.FirstOrDefault()?.Version ?? 0) + 1;
    }
    else
    {
      template = new WorkflowTemplate
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, CreatedByUserId = actor.UserId,
        CreatedAt = DateTimeOffset.UtcNow
      };
      db.WorkflowTemplates.Add(template);
      version = 1;
    }
    var now = DateTimeOffset.UtcNow;
    var revision = new WorkflowTemplateRevision
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, TemplateId = template.Id, Version = version,
      Name = definition.Value!.Name, ServiceRoute = definition.Value.ServiceRoute, IndustryKey = definition.Value.IndustryKey,
      Status = WorkflowTemplateStates.Draft, ContentDigest = Digest(definition.Value),
      CreatedByUserId = actor.UserId, LastEditedByUserId = actor.UserId, CreatedAt = now, UpdatedAt = now
    };
    db.WorkflowTemplateRevisions.Add(revision);
    AddSteps(db, actor.FirmId, revision.Id, definition.Value.Steps);
    db.WorkflowTemplateDecisions.Add(new WorkflowTemplateDecision
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, TemplateId = template.Id, TemplateRevisionId = revision.Id,
      Kind = WorkflowTemplateDecisionKinds.DraftCreated,
      ContentDigest = revision.ContentDigest, Rationale = "Draft revision created.", ActorUserId = actor.UserId, OccurredAt = now
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(revision.Id);
  }

  public static async Task<CommandResult> UpdateDraftAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid templateId, Guid revisionId,
    SaveWorkflowTemplateDraftRequest request, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return auth;
    var definition = NormalizeAndValidate(request with { TemplateId = templateId });
    if (definition.Error is not null || definition.Value is null)
      return CommandResult.Fail("workflow.template-invalid", definition.Error!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var template = await LockTemplateAsync(db, actor.FirmId, templateId, ct);
    var revision = await LockRevisionAsync(db, actor.FirmId, templateId, revisionId, ct);
    auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return auth;
    if (template is null || template.ArchivedAt.HasValue || revision is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (revision.Status != WorkflowTemplateStates.Draft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft template revision can be edited. Create the next revision to change reviewed or published content.");
    if (!request.ExpectedRevision.HasValue || request.ExpectedRevision.Value != revision.Revision)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "The template draft changed; reload it before saving.");

    var oldSteps = await db.WorkflowTemplateSteps.Where(x => x.FirmId == actor.FirmId && x.TemplateRevisionId == revisionId).ToListAsync(ct);
    var oldEdges = await db.WorkflowTemplateDependencies.Where(x => x.FirmId == actor.FirmId && x.TemplateRevisionId == revisionId).ToListAsync(ct);
    var oldEvidenceRequirements = await db.WorkflowTemplateEvidenceRequirements
      .Where(x => x.FirmId == actor.FirmId && x.TemplateRevisionId == revisionId).ToListAsync(ct);
    db.WorkflowTemplateDependencies.RemoveRange(oldEdges);
    db.WorkflowTemplateEvidenceRequirements.RemoveRange(oldEvidenceRequirements);
    db.WorkflowTemplateSteps.RemoveRange(oldSteps);
    await db.SaveChangesAsync(ct);
    revision.Name = definition.Value!.Name;
    revision.ServiceRoute = definition.Value.ServiceRoute;
    revision.IndustryKey = definition.Value.IndustryKey;
    revision.ContentDigest = Digest(definition.Value);
    revision.LastEditedByUserId = actor.UserId;
    revision.UpdatedAt = DateTimeOffset.UtcNow;
    revision.Revision++;
    AddSteps(db, actor.FirmId, revision.Id, definition.Value.Steps);
    db.WorkflowTemplateDecisions.Add(Decision(actor, templateId, revisionId, WorkflowTemplateDecisionKinds.DraftEdited,
      revision.ContentDigest, "Draft definition edited.", revision.UpdatedAt));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> ReviewAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid templateId, Guid revisionId, long expectedRevision,
    string rationale, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return auth;
    var note = (rationale ?? string.Empty).Trim();
    if (note.Length is < 5 or > 1000) return CommandResult.Fail("workflow.template-invalid", "Review rationale must be 5 to 1,000 characters.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var template = await LockTemplateAsync(db, actor.FirmId, templateId, ct);
    var revision = await LockRevisionAsync(db, actor.FirmId, templateId, revisionId, ct);
    auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return auth;
    if (template is null || template.ArchivedAt.HasValue || revision is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (revision.Status != WorkflowTemplateStates.Draft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft revision can be reviewed.");
    if (revision.Revision != expectedRevision)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "The template draft changed; review the current revision.");
    if (revision.CreatedByUserId == actor.UserId || revision.LastEditedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "A template author or last editor cannot independently review this revision.");
    var definition = await LoadDefinitionAsync(db, actor.FirmId, revision, ct);
    var validated = NormalizeAndValidate(definition);
    if (validated.Error is not null || validated.Value is null || Digest(validated.Value) != revision.ContentDigest)
      return CommandResult.Fail("workflow.template-invalid", "The saved template content is invalid or its digest does not match. Reopen and save the draft.");
    var now = DateTimeOffset.UtcNow;
    revision.Status = WorkflowTemplateStates.Reviewed;
    revision.ReviewedByUserId = actor.UserId;
    revision.ReviewedAt = now;
    revision.Revision++;
    db.WorkflowTemplateDecisions.Add(Decision(actor, templateId, revisionId, WorkflowTemplateDecisionKinds.Reviewed,
      revision.ContentDigest, note, now));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> PublishAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid templateId, Guid revisionId, long expectedRevision,
    string rationale, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return auth;
    var note = (rationale ?? string.Empty).Trim();
    if (note.Length is < 5 or > 1000) return CommandResult.Fail("workflow.template-invalid", "Publication rationale must be 5 to 1,000 characters.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var template = await LockTemplateAsync(db, actor.FirmId, templateId, ct);
    var revision = await LockRevisionAsync(db, actor.FirmId, templateId, revisionId, ct);
    auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return auth;
    if (template is null || template.ArchivedAt.HasValue || revision is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (revision.Status != WorkflowTemplateStates.Reviewed || revision.Revision != expectedRevision ||
        revision.ReviewedByUserId is null || revision.ReviewedByUserId == revision.LastEditedByUserId)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "Publish requires the exact independently reviewed revision. Reload and review it again if it changed.");
    var definition = await LoadDefinitionAsync(db, actor.FirmId, revision, ct);
    var validated = NormalizeAndValidate(definition);
    if (validated.Error is not null || validated.Value is null || Digest(validated.Value) != revision.ContentDigest)
      return CommandResult.Fail("workflow.template-invalid", "The reviewed template content is invalid or its digest does not match.");
    var current = await db.WorkflowTemplateRevisions.Where(x => x.FirmId == actor.FirmId && x.TemplateId == templateId &&
        x.Status == WorkflowTemplateStates.Published).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    var now = DateTimeOffset.UtcNow;
    if (current is not null)
    {
      current.Status = WorkflowTemplateStates.Superseded;
      db.WorkflowTemplateDecisions.Add(Decision(actor, templateId, current.Id, WorkflowTemplateDecisionKinds.Superseded,
        current.ContentDigest, $"Superseded by revision {revision.Version}.", now));
      await db.SaveChangesAsync(ct);
    }
    revision.Status = WorkflowTemplateStates.Published;
    revision.PublishedByUserId = actor.UserId;
    revision.PublishedAt = now;
    revision.Revision++;
    db.WorkflowTemplateDecisions.Add(Decision(actor, templateId, revisionId, WorkflowTemplateDecisionKinds.Published,
      revision.ContentDigest, note, now));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> ArchiveAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid templateId, string rationale, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return auth;
    var note = (rationale ?? string.Empty).Trim();
    if (note.Length is < 5 or > 1000) return CommandResult.Fail("workflow.template-invalid", "Archive rationale must be 5 to 1,000 characters.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var template = await LockTemplateAsync(db, actor.FirmId, templateId, ct);
    auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return auth;
    if (template is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (template.ArchivedAt.HasValue) return CommandResult.Ok();
    var latest = await db.WorkflowTemplateRevisions.Where(x => x.FirmId == actor.FirmId && x.TemplateId == templateId)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (latest is null) return CommandResult.Fail(ErrorCodes.ProtectedState, "A template without a saved revision cannot be archived.");
    var now = DateTimeOffset.UtcNow;
    template.ArchivedAt = now;
    template.ArchivedByUserId = actor.UserId;
    db.WorkflowTemplateDecisions.Add(Decision(actor, templateId, latest.Id, WorkflowTemplateDecisionKinds.Archived,
      latest.ContentDigest, note, now));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  private static (WorkflowTemplateDefinition? Value, string? Error) NormalizeAndValidate(SaveWorkflowTemplateDraftRequest request)
  {
    var name = (request.Name ?? string.Empty).Trim();
    var route = (request.ServiceRoute ?? string.Empty).Trim();
    var industry = string.IsNullOrWhiteSpace(request.IndustryKey) ? null : request.IndustryKey.Trim();
    if (name.Length is < 2 or > 160 || route.Length is < 1 or > 50 ||
        route.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) || industry?.Length > 80)
      return (null, "Enter a template name, a valid service route, and an optional industry key within the allowed lengths.");
    if (request.Steps is null || request.Steps.Count is < 1 or > 500)
      return (null, "A template must contain 1 to 500 steps.");
    var steps = request.Steps.Select(s => new WorkflowTemplateStepDraft(
      (s.StableKey ?? string.Empty).Trim(), s.Phase ?? string.Empty, (s.Title ?? string.Empty).Trim(),
      (s.Instructions ?? string.Empty).Trim(), string.IsNullOrWhiteSpace(s.SuggestedStaffingLevel) ? null : s.SuggestedStaffingLevel.Trim(),
      s.EstimatedEffortMinutes, s.DueDateOffsetDays,
      string.IsNullOrWhiteSpace(s.PbcRequestReference) ? null : s.PbcRequestReference.Trim(), s.RequiredTask, s.SortOrder,
      (s.DependsOnStepKeys ?? []).Select(k => (k ?? string.Empty).Trim()).ToArray(),
      NormalizePbcDefinition(s.PbcDefinition),
      string.IsNullOrWhiteSpace(s.TaskGroupName) ? null : s.TaskGroupName.Trim(),
      (s.EvidenceRequirements ?? []).Select(requirement => new WorkflowTemplateEvidenceRequirementDraft(
        (requirement.RequirementKey ?? string.Empty).Trim(), (requirement.Kind ?? string.Empty).Trim())).ToArray())).ToArray();
    var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var step in steps)
    {
      if (step.StableKey.Length is < 1 or > 80 || !System.Text.RegularExpressions.Regex.IsMatch(step.StableKey, "^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$") || !keys.Add(step.StableKey))
        return (null, "Step keys must be unique, stable identifiers of at most 80 characters.");
      if (!WorkflowTaskPhases.All.Contains(step.Phase, StringComparer.Ordinal) || step.Title.Length is < 1 or > 200 ||
          step.Instructions.Length > 4000 || (step.SuggestedStaffingLevel is not null && !StaffingLevels.All.Contains(step.SuggestedStaffingLevel, StringComparer.Ordinal)) ||
          step.EstimatedEffortMinutes is < 1 or > 1000000 || step.DueDateOffsetDays is < -3650 or > 3650 ||
          step.PbcRequestReference?.Length > 120 || step.TaskGroupName?.Length > 120 || step.SortOrder is < 1 or > 100000)
        return (null, $"Step '{step.StableKey}' has an unknown phase or a field outside its allowed range.");
      if (step.DependsOnStepKeys!.Count > 100 || step.DependsOnStepKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != step.DependsOnStepKeys.Count)
        return (null, $"Step '{step.StableKey}' has duplicate or excessive dependencies.");
      var evidenceRequirements = step.EvidenceRequirements ?? [];
      if (evidenceRequirements.Count > 8)
        return (null, $"Step '{step.StableKey}' may declare at most eight evidence requirements.");
      var requirementKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var requirement in evidenceRequirements)
        if (requirement.RequirementKey.Length is < 1 or > 80 ||
            !System.Text.RegularExpressions.Regex.IsMatch(requirement.RequirementKey, "^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$") ||
            !requirementKeys.Add(requirement.RequirementKey) ||
            !WorkflowEvidenceKinds.All.Contains(requirement.Kind, StringComparer.Ordinal))
          return (null, $"Step '{step.StableKey}' has an invalid or duplicate evidence requirement key or unsupported evidence kind.");
      if (evidenceRequirements.Count(x => x.Kind == WorkflowEvidenceKinds.CurrentMateriality) > 1)
        return (null, $"Step '{step.StableKey}' may declare current approved materiality only once.");
      if (step.PbcDefinition is { } pbc && (!step.DueDateOffsetDays.HasValue || !ValidPbcDefinition(pbc)))
        return (null, $"Step '{step.StableKey}' has an incomplete PBC request definition. Enter the objective, entity scope, area, requested format, confidentiality and acceptance criteria within their limits, and a due-date offset.");
    }
    if (steps.Select(x => x.SortOrder).Distinct().Count() != steps.Length)
      return (null, "Each template step must have a distinct order number.");
    foreach (var step in steps)
      foreach (var predecessor in step.DependsOnStepKeys!)
        if (!keys.Contains(predecessor) || string.Equals(predecessor, step.StableKey, StringComparison.OrdinalIgnoreCase))
          return (null, $"Step '{step.StableKey}' references an unknown or self dependency '{predecessor}'.");
    if (HasCycle(steps)) return (null, "Template dependencies must form an acyclic graph.");
    return (new WorkflowTemplateDefinition(name, route, industry, steps), null);
  }

  private static WorkflowTemplatePbcDefinition? NormalizePbcDefinition(WorkflowTemplatePbcDefinition? pbc) =>
    pbc is null ? null : new WorkflowTemplatePbcDefinition((pbc.Objective ?? string.Empty).Trim(),
      (pbc.EntityScope ?? string.Empty).Trim(), (pbc.Area ?? string.Empty).Trim(),
      (pbc.RequestedFormat ?? string.Empty).Trim(),
      string.IsNullOrWhiteSpace(pbc.ControlTotals) ? null : pbc.ControlTotals.Trim(),
      (pbc.Confidentiality ?? string.Empty).Trim(), (pbc.AcceptanceCriteria ?? string.Empty).Trim());

  // Mirrors PbcService request bounds so a reviewed definition is executable without being reinterpreted.
  private static bool ValidPbcDefinition(WorkflowTemplatePbcDefinition pbc) =>
    pbc.Objective.Length is >= 1 and <= 2000 && pbc.EntityScope.Length is >= 1 and <= 500 &&
    pbc.Area.Length is >= 1 and <= 200 && pbc.RequestedFormat.Length is >= 1 and <= 200 &&
    (pbc.ControlTotals?.Length ?? 0) <= 2000 && pbc.Confidentiality.Length is >= 1 and <= 100 &&
    pbc.AcceptanceCriteria.Length is >= 1 and <= 4000;

  internal static WorkflowTemplatePbcDefinition? PbcDefinitionOf(WorkflowTemplateStep step) =>
    step.PbcObjective is null ? null : new WorkflowTemplatePbcDefinition(step.PbcObjective,
      step.PbcEntityScope ?? string.Empty, step.PbcArea ?? string.Empty, step.PbcRequestedFormat ?? string.Empty,
      step.PbcControlTotals, step.PbcConfidentiality ?? string.Empty, step.PbcAcceptanceCriteria ?? string.Empty);

  private static (WorkflowTemplateDefinition? Value, string? Error) NormalizeAndValidate(WorkflowTemplateDefinition definition) =>
    NormalizeAndValidate(new SaveWorkflowTemplateDraftRequest(null, definition.Name, definition.ServiceRoute,
      definition.IndustryKey, definition.Steps));

  private static bool HasCycle(IReadOnlyList<WorkflowTemplateStepDraft> steps)
  {
    var edges = steps.ToDictionary(x => x.StableKey, x => x.DependsOnStepKeys!, StringComparer.OrdinalIgnoreCase);
    var state = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
    bool Visit(string key)
    {
      if (state.TryGetValue(key, out var value)) return value == 1;
      state[key] = 1;
      foreach (var predecessor in edges[key]) if (Visit(predecessor)) return true;
      state[key] = 2;
      return false;
    }
    return edges.Keys.Any(Visit);
  }

  private static string Digest(WorkflowTemplateDefinition definition)
  {
    object StepDigest(WorkflowTemplateStepDraft step)
    {
      if (step.EvidenceRequirements is { Count: > 0 })
        return new
        {
          step.StableKey, step.Phase, step.Title, step.Instructions, step.SuggestedStaffingLevel,
          step.EstimatedEffortMinutes, step.DueDateOffsetDays, step.PbcRequestReference, step.RequiredTask,
          step.SortOrder,
          dependencies = step.DependsOnStepKeys!.Order(StringComparer.OrdinalIgnoreCase),
          step.TaskGroupName,
          pbcDefinition = step.PbcDefinition,
          evidenceRequirements = step.EvidenceRequirements.OrderBy(requirement => requirement.RequirementKey, StringComparer.Ordinal)
            .Select(requirement => new { requirement.RequirementKey, requirement.Kind })
        };
      if (step.PbcDefinition is null && step.TaskGroupName is null)
        return new
        {
          step.StableKey, step.Phase, step.Title, step.Instructions, step.SuggestedStaffingLevel,
          step.EstimatedEffortMinutes, step.DueDateOffsetDays, step.PbcRequestReference, step.RequiredTask,
          step.SortOrder,
          dependencies = step.DependsOnStepKeys!.Order(StringComparer.OrdinalIgnoreCase)
        };
      if (step.PbcDefinition is null)
        return new
        {
          step.StableKey, step.Phase, step.Title, step.Instructions, step.SuggestedStaffingLevel,
          step.EstimatedEffortMinutes, step.DueDateOffsetDays, step.PbcRequestReference, step.RequiredTask,
          step.SortOrder,
          dependencies = step.DependsOnStepKeys!.Order(StringComparer.OrdinalIgnoreCase), step.TaskGroupName
        };
      if (step.TaskGroupName is null)
        return new
        {
          step.StableKey, step.Phase, step.Title, step.Instructions, step.SuggestedStaffingLevel,
          step.EstimatedEffortMinutes, step.DueDateOffsetDays, step.PbcRequestReference, step.RequiredTask,
          step.SortOrder,
          dependencies = step.DependsOnStepKeys!.Order(StringComparer.OrdinalIgnoreCase),
          pbcDefinition = step.PbcDefinition
        };
      return new
      {
        step.StableKey, step.Phase, step.Title, step.Instructions, step.SuggestedStaffingLevel,
        step.EstimatedEffortMinutes, step.DueDateOffsetDays, step.PbcRequestReference, step.RequiredTask,
        step.SortOrder,
        dependencies = step.DependsOnStepKeys!.Order(StringComparer.OrdinalIgnoreCase), step.TaskGroupName,
        pbcDefinition = step.PbcDefinition
      };
    }

    var bytes = JsonSerializer.SerializeToUtf8Bytes(new
    {
      definition.Name, definition.ServiceRoute, definition.IndustryKey,
      // Empty/missing declarations retain the exact pre-feature anonymous-object digest shape.
      steps = definition.Steps.OrderBy(x => x.SortOrder).Select(StepDigest).ToArray()
    }, Json);
    return Convert.ToHexStringLower(SHA256.HashData(bytes));
  }

  private static void AddSteps(IAuditSphereDbContext db, Guid firmId, Guid revisionId,
    IReadOnlyList<WorkflowTemplateStepDraft> steps)
  {
    var ids = steps.ToDictionary(x => x.StableKey, _ => Guid.CreateVersion7(), StringComparer.OrdinalIgnoreCase);
    foreach (var step in steps)
      db.WorkflowTemplateSteps.Add(new WorkflowTemplateStep
      {
        Id = ids[step.StableKey], FirmId = firmId, TemplateRevisionId = revisionId, StableKey = step.StableKey,
        Phase = step.Phase, TaskGroupName = step.TaskGroupName, Title = step.Title, Instructions = step.Instructions,
        SuggestedStaffingLevel = step.SuggestedStaffingLevel, EstimatedEffortMinutes = step.EstimatedEffortMinutes,
        DueDateOffsetDays = step.DueDateOffsetDays, PbcRequestReference = step.PbcRequestReference,
        RequiredTask = step.RequiredTask, SortOrder = step.SortOrder,
        PbcObjective = step.PbcDefinition?.Objective, PbcEntityScope = step.PbcDefinition?.EntityScope,
        PbcArea = step.PbcDefinition?.Area, PbcRequestedFormat = step.PbcDefinition?.RequestedFormat,
        PbcControlTotals = step.PbcDefinition?.ControlTotals, PbcConfidentiality = step.PbcDefinition?.Confidentiality,
        PbcAcceptanceCriteria = step.PbcDefinition?.AcceptanceCriteria
      });
    foreach (var step in steps)
      foreach (var requirement in step.EvidenceRequirements ?? [])
        db.WorkflowTemplateEvidenceRequirements.Add(new WorkflowTemplateEvidenceRequirement
        {
          Id = Guid.CreateVersion7(), FirmId = firmId, TemplateRevisionId = revisionId,
          TemplateStepId = ids[step.StableKey], RequirementKey = requirement.RequirementKey, Kind = requirement.Kind
        });
    foreach (var step in steps)
      foreach (var predecessor in step.DependsOnStepKeys!)
        db.WorkflowTemplateDependencies.Add(new WorkflowTemplateDependency
        {
          Id = Guid.CreateVersion7(), FirmId = firmId, TemplateRevisionId = revisionId,
          PredecessorStepId = ids[predecessor], SuccessorStepId = ids[step.StableKey],
          DependencyType = WorkflowDependencyTypes.TaskCompleted
        });
  }

  private static async Task<WorkflowTemplateRevisionView> BuildViewAsync(
    IAuditSphereDbContext db, WorkflowTemplateRevision revision, CancellationToken ct)
  {
    var steps = await db.WorkflowTemplateSteps.AsNoTracking().Where(x => x.FirmId == revision.FirmId && x.TemplateRevisionId == revision.Id)
      .OrderBy(x => x.SortOrder).ToListAsync(ct);
    var ids = steps.Select(x => x.Id).ToArray();
    var dependencies = await (from edge in db.WorkflowTemplateDependencies.AsNoTracking()
                              join step in db.WorkflowTemplateSteps.AsNoTracking()
                                on new { edge.FirmId, edge.TemplateRevisionId, Id = edge.PredecessorStepId }
                                equals new { step.FirmId, step.TemplateRevisionId, step.Id }
                              where edge.FirmId == revision.FirmId && edge.TemplateRevisionId == revision.Id && ids.Contains(edge.SuccessorStepId)
                              select new { edge.SuccessorStepId, step.StableKey }).ToListAsync(ct);
    var byStep = dependencies.GroupBy(x => x.SuccessorStepId)
      .ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Select(y => y.StableKey).Order(StringComparer.OrdinalIgnoreCase).ToArray());
    var evidenceRequirements = await db.WorkflowTemplateEvidenceRequirements.AsNoTracking()
      .Where(x => x.FirmId == revision.FirmId && x.TemplateRevisionId == revision.Id && ids.Contains(x.TemplateStepId))
      .OrderBy(x => x.RequirementKey).ToListAsync(ct);
    var requirementsByStep = evidenceRequirements.GroupBy(x => x.TemplateStepId)
      .ToDictionary(x => x.Key, x => (IReadOnlyList<WorkflowTemplateEvidenceRequirementView>)x
        .Select(requirement => new WorkflowTemplateEvidenceRequirementView(requirement.RequirementKey, requirement.Kind)).ToArray());
    var template = await db.WorkflowTemplates.AsNoTracking().SingleAsync(x => x.FirmId == revision.FirmId && x.Id == revision.TemplateId, ct);
    return new WorkflowTemplateRevisionView(revision.TemplateId, revision.Id, revision.Version, revision.Name,
      revision.ServiceRoute, revision.IndustryKey, revision.Status, revision.ContentDigest, revision.Revision,
      revision.CreatedByUserId, revision.LastEditedByUserId, revision.ReviewedByUserId, revision.CreatedAt,
      revision.UpdatedAt, revision.PublishedAt, template.ArchivedAt,
      steps.Select(s => new WorkflowTemplateStepView(s.Id, s.StableKey, s.Phase, s.Title, s.Instructions,
        s.SuggestedStaffingLevel, s.EstimatedEffortMinutes, s.DueDateOffsetDays, s.PbcRequestReference,
        s.RequiredTask, s.SortOrder, byStep.GetValueOrDefault(s.Id, []), PbcDefinitionOf(s), s.TaskGroupName,
        requirementsByStep.GetValueOrDefault(s.Id, []))).ToArray());
  }

  private static async Task<WorkflowTemplateDefinition> LoadDefinitionAsync(
    IAuditSphereDbContext db, Guid firmId, WorkflowTemplateRevision revision, CancellationToken ct)
  {
    var steps = await db.WorkflowTemplateSteps.AsNoTracking().Where(x => x.FirmId == firmId && x.TemplateRevisionId == revision.Id)
      .OrderBy(x => x.SortOrder).ToListAsync(ct);
    var edges = await (from edge in db.WorkflowTemplateDependencies.AsNoTracking()
                       join predecessor in db.WorkflowTemplateSteps.AsNoTracking()
                         on new { edge.FirmId, edge.TemplateRevisionId, Id = edge.PredecessorStepId }
                         equals new { predecessor.FirmId, predecessor.TemplateRevisionId, predecessor.Id }
                       where edge.FirmId == firmId && edge.TemplateRevisionId == revision.Id
                       select new { edge.SuccessorStepId, predecessor.StableKey }).ToListAsync(ct);
    var dependencies = edges.GroupBy(x => x.SuccessorStepId)
      .ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Select(y => y.StableKey).ToArray());
    var evidenceRows = await db.WorkflowTemplateEvidenceRequirements.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.TemplateRevisionId == revision.Id)
      .ToListAsync(ct);
    var evidenceByStep = evidenceRows.GroupBy(x => x.TemplateStepId)
      .ToDictionary(x => x.Key, x => (IReadOnlyList<WorkflowTemplateEvidenceRequirementDraft>)x
        .Select(requirement => new WorkflowTemplateEvidenceRequirementDraft(requirement.RequirementKey, requirement.Kind)).ToArray());
    return new WorkflowTemplateDefinition(revision.Name, revision.ServiceRoute, revision.IndustryKey,
      steps.Select(s => new WorkflowTemplateStepDraft(s.StableKey, s.Phase, s.Title, s.Instructions,
        s.SuggestedStaffingLevel, s.EstimatedEffortMinutes, s.DueDateOffsetDays, s.PbcRequestReference,
        s.RequiredTask, s.SortOrder, dependencies.GetValueOrDefault(s.Id, []), PbcDefinitionOf(s), s.TaskGroupName,
        evidenceByStep.GetValueOrDefault(s.Id, []))).ToArray());
  }

  private static WorkflowTemplateDecision Decision(ActorContext actor, Guid templateId, Guid revisionId, string kind,
    string digest, string rationale, DateTimeOffset now) => new()
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, TemplateId = templateId, TemplateRevisionId = revisionId,
      Kind = kind, ContentDigest = digest, Rationale = rationale, ActorUserId = actor.UserId, OccurredAt = now
    };

  private static Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor,
    string[] roles, CancellationToken ct) => AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: roles, InternalOnly: true, RequireFirmWide: true), ct);

  private static Task<FirmSafetyState?> LockFirmAsync(IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").SingleOrDefaultAsync(ct);

  private static Task<WorkflowTemplate?> LockTemplateAsync(IAuditSphereDbContext db, Guid firmId, Guid templateId, CancellationToken ct) =>
    db.WorkflowTemplates.FromSqlInterpolated($"SELECT * FROM workflow_templates WHERE firm_id = {firmId} AND id = {templateId} FOR UPDATE").SingleOrDefaultAsync(ct);

  private static Task<WorkflowTemplateRevision?> LockRevisionAsync(IAuditSphereDbContext db, Guid firmId,
    Guid templateId, Guid revisionId, CancellationToken ct) =>
    db.WorkflowTemplateRevisions.FromSqlInterpolated($"SELECT * FROM workflow_template_revisions WHERE firm_id = {firmId} AND template_id = {templateId} AND id = {revisionId} FOR UPDATE").SingleOrDefaultAsync(ct);

  private sealed record WorkflowTemplateDefinition(string Name, string ServiceRoute, string? IndustryKey,
    IReadOnlyList<WorkflowTemplateStepDraft> Steps);
}
