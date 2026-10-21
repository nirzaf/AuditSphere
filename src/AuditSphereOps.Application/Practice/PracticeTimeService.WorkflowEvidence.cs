using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record WorkflowEvidenceChangeRequest(string Kind, Guid? SourceId, bool Required,
  string Reason, long ExpectedRevision, Guid IdempotencyKey);
public sealed record WorkflowEvidenceChangeReceipt(Guid EngagementId, Guid TaskId, Guid IdempotencyKey,
  Guid RuleRevisionId, long TaskRevision);

public static partial class PracticeTimeService
{
  public static async Task<CommandResult<WorkflowEvidenceChangeReceipt>> ChangeWorkflowEvidenceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, Guid taskId,
    WorkflowEvidenceChangeRequest request, CancellationToken ct = default)
  {
    var reason = request.Reason?.Trim();
    if (!WorkflowEvidenceKinds.All.Contains(request.Kind, StringComparer.Ordinal) ||
        string.IsNullOrWhiteSpace(reason) || reason.Length > 1000 || request.ExpectedRevision < 1 ||
        request.IdempotencyKey == Guid.Empty ||
        (request.Kind == WorkflowEvidenceKinds.CurrentMateriality ? request.SourceId.HasValue : request.SourceId is null || request.SourceId == Guid.Empty))
      return CommandResult<WorkflowEvidenceChangeReceipt>.Fail("request.invalid",
        "Choose a supported evidence reference, provide its exact source when required, and review a reason and current task revision.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<WorkflowEvidenceChangeReceipt>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var engagement = await db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE id = {engagementId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (engagement is null) return CommandResult<WorkflowEvidenceChangeReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var clientGuard = await LockClientAsync(db, actor.FirmId, engagement.PracticeClientId, ct);
    if (!clientGuard.Succeeded) return CommandResult<WorkflowEvidenceChangeReceipt>.Fail(clientGuard.ErrorCode!, clientGuard.Message!);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, engagement.PracticeClientId, engagementId, WorkflowTaskEvidenceQuery.ManageRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<WorkflowEvidenceChangeReceipt>.Fail(auth.ErrorCode!, auth.Message!);
    var task = await LoadTaskForUpdateAsync(db, actor.FirmId, taskId, ct);
    var binding = await LockWorkflowBindingAsync(db, actor.FirmId, taskId, ct);
    if (task is null || binding is null)
      return CommandResult<WorkflowEvidenceChangeReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await db.WorkTasks.Attach(task).ReloadAsync(ct);
    await db.WorkflowTaskBindings.Attach(binding).ReloadAsync(ct);
    if (binding.EngagementId != engagementId ||
        binding.ClientId != engagement.PracticeClientId || task.EngagementId != engagementId || task.ClientId != binding.ClientId)
      return CommandResult<WorkflowEvidenceChangeReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
      engagementId, taskId, request.Kind, request.SourceId, request.Required, reason, request.ExpectedRevision
    }))).ToLowerInvariant();
    var replay = await db.WorkflowTaskEvidenceRules.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorUserId == actor.UserId && x.IdempotencyKey == request.IdempotencyKey, ct);
    if (replay is not null)
      return replay.RequestDigest == digest
        ? CommandResult<WorkflowEvidenceChangeReceipt>.Ok(new(engagementId, taskId, request.IdempotencyKey, replay.Id, replay.TaskRevision))
        : CommandResult<WorkflowEvidenceChangeReceipt>.Fail(ErrorCodes.IdempotencyConflict, "This evidence change key was used for different reviewed content.");
    if (binding.Revision != request.ExpectedRevision)
      return CommandResult<WorkflowEvidenceChangeReceipt>.Fail(ErrorCodes.StaleRevision, "The task changed. Refresh and review its prerequisites again.");
    if (task.Status is PracticeTimeStates.TaskCompleted or PracticeTimeStates.TaskCancelled)
      return CommandResult<WorkflowEvidenceChangeReceipt>.Fail(ErrorCodes.ProtectedState, "A closed task's prerequisites cannot be changed.");
    if (binding.Revision == long.MaxValue)
      return CommandResult<WorkflowEvidenceChangeReceipt>.Fail(ErrorCodes.ProtectedState, "The task revision cannot advance further.");
    var sourceId = request.SourceId ?? Guid.Empty;
    var heads = await WorkflowTaskEvidenceQuery.Heads(db, actor.FirmId)
      .Where(x => x.WorkflowTaskBindingId == binding.Id).ToListAsync(ct);
    var previous = heads.SingleOrDefault(x => x.Kind == request.Kind && x.SourceId == sourceId);
    if (previous is null && (!request.Required || heads.Count >= WorkflowTaskEvidenceQuery.MaximumRules))
      return CommandResult<WorkflowEvidenceChangeReceipt>.Fail(ErrorCodes.GateBlocked,
        "Only an existing rule can be withdrawn, and a task supports at most eight distinct evidence references.");
    var state = request.Required ? WorkflowEvidenceRuleStates.Required : WorkflowEvidenceRuleStates.Withdrawn;
    if (previous?.State == state || previous?.Version == int.MaxValue)
      return CommandResult<WorkflowEvidenceChangeReceipt>.Fail(ErrorCodes.ProtectedState, "This prerequisite already has the requested state or cannot advance further.");
    var sourceExists = request.Kind switch
    {
      WorkflowEvidenceKinds.AcceptedPbc => await db.PbcRequests.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
        x.ClientId == binding.ClientId && x.EngagementId == engagementId && x.Id == sourceId, ct),
      WorkflowEvidenceKinds.ReviewedProcedure => await db.AuditProcedures.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
        x.ClientId == binding.ClientId && x.EngagementId == engagementId && x.Id == sourceId, ct),
      _ => true
    };
    if (!sourceExists) return CommandResult<WorkflowEvidenceChangeReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, binding.ClientId, engagementId, WorkflowTaskEvidenceQuery.ManageRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<WorkflowEvidenceChangeReceipt>.Fail(auth.ErrorCode!, auth.Message!);
    var now = DateTimeOffset.UtcNow;
    binding.Revision++;
    var rule = new WorkflowTaskEvidenceRule {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = binding.ClientId, EngagementId = engagementId,
      WorkflowTaskBindingId = binding.Id, RuleKey = previous?.RuleKey ?? Guid.CreateVersion7(), Kind = request.Kind,
      SourceId = sourceId, PbcRequestId = request.Kind == WorkflowEvidenceKinds.AcceptedPbc ? sourceId : null,
      AuditProcedureId = request.Kind == WorkflowEvidenceKinds.ReviewedProcedure ? sourceId : null,
      WorkflowTemplateEvidenceRequirementId = previous?.WorkflowTemplateEvidenceRequirementId,
      State = state, Version = (previous?.Version ?? 0) + 1, TaskRevision = binding.Revision,
      ActorUserId = actor.UserId, IdempotencyKey = request.IdempotencyKey, RequestDigest = digest,
      Reason = reason, OccurredAt = now
    };
    db.WorkflowTaskEvidenceRules.Add(rule);
    AddWorkflowTaskActivity(db, actor, task, binding, WorkflowTaskActivityKinds.EvidenceChanged,
      null, binding.Revision, now);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<WorkflowEvidenceChangeReceipt>.Ok(new(engagementId, taskId, request.IdempotencyKey, rule.Id, binding.Revision));
  }
}
