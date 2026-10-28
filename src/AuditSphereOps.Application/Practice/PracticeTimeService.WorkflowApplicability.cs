using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record WorkflowApplicabilityProposalRequest(Guid SourceReceiptId, long ExpectedTaskRevision,
  Guid? ExpectedProposalId, string BasisHash, string Reason, Guid IdempotencyKey);
public sealed record WorkflowApplicabilityDecisionRequest(Guid ProposalId, Guid? ExpectedDecisionId,
  string Kind, long ExpectedTaskRevision, string BasisHash, string Reason, Guid IdempotencyKey);
public sealed record WorkflowApplicabilityReceipt(Guid EngagementId, Guid TaskId, Guid IdempotencyKey,
  Guid ProposalId, Guid? DecisionId, long Version);

/// <summary>Conditional task evidence only. No operational completion, professional waiver or source approval.</summary>
public static partial class PracticeTimeService
{
  public static async Task<CommandResult<WorkflowApplicabilityReceipt>> ProposeWorkflowApplicabilityAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, Guid taskId,
    WorkflowApplicabilityProposalRequest request, CancellationToken ct = default)
  {
    var reason = request.Reason?.Trim();
    if (request.SourceReceiptId == Guid.Empty || request.ExpectedTaskRevision < 1 ||
        !ValidApplicabilityRequest(request.IdempotencyKey, request.BasisHash, reason))
      return ApplicabilityFailure("request.invalid", "Review the exact accepted source, task revision, basis and reason.");
    if (db.Database.CurrentTransaction is not null)
      return ApplicabilityFailure(ErrorCodes.ProtectedState, "Applicability publication requires its own transaction.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var locked = await LockApplicabilityTaskAsync(db, actor, engagementId, taskId, true, ct);
    if (!locked.Succeeded) return ApplicabilityFailure(locked.ErrorCode!, locked.Message!);
    var binding = locked.Value!;
    var digest = Hashing.Sha256Hex(JsonSerializer.Serialize(new
    {
      Action = "WORKFLOW_APPLICABILITY_PROPOSE", engagementId, taskId, request.SourceReceiptId,
      request.ExpectedTaskRevision, request.ExpectedProposalId, request.BasisHash, reason
    }));
    var prior = await db.WorkflowTaskApplicabilityProposals.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorUserId == actor.UserId && x.IdempotencyKey == request.IdempotencyKey, ct);
    if (prior is not null)
      return prior.RequestDigest == digest && prior.WorkflowTaskBindingId == binding.Id
        ? CommandResult<WorkflowApplicabilityReceipt>.Ok(new(engagementId, taskId, request.IdempotencyKey, prior.Id, null, prior.Version))
        : ApplicabilityFailure(ErrorCodes.IdempotencyConflict, "This request key already records different reviewed content.");
    if (await db.WorkflowTaskApplicabilityDecisions.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
          x.ActorUserId == actor.UserId && x.IdempotencyKey == request.IdempotencyKey, ct))
      return ApplicabilityFailure(ErrorCodes.IdempotencyConflict, "This request key already records a decision.");
    var head = await db.WorkflowTaskApplicabilityProposals.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.WorkflowTaskBindingId == binding.Id).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (head?.Id != request.ExpectedProposalId || binding.Revision != request.ExpectedTaskRevision)
      return ApplicabilityFailure(ErrorCodes.StaleRevision, "The task or proposal changed. Refresh and review the current basis.");
    if (head?.Version == long.MaxValue)
      return ApplicabilityFailure(ErrorCodes.ProtectedState, "The applicability history cannot advance further.");
    var basis = await WorkflowTaskApplicabilityBasisQuery.GetAsync(db, binding, request.SourceReceiptId, ct);
    if (!basis.Succeeded) return ApplicabilityFailure(basis.ErrorCode!, basis.Message!);
    if (basis.Value!.Hash != request.BasisHash)
      return ApplicabilityFailure(ErrorCodes.StaleRevision, "The reviewed task or source basis changed. No proposal was saved.");
    var auth = await AuthorizeApplicabilityAsync(db, actor, binding, true, ct);
    if (!auth.Succeeded) return ApplicabilityFailure(auth.ErrorCode!, auth.Message!);
    var row = new WorkflowTaskApplicabilityProposal
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = binding.ClientId, EngagementId = engagementId,
      WorkflowTaskBindingId = binding.Id, TemplateRevisionId = binding.TemplateRevisionId!.Value,
      TemplateStepId = binding.TemplateStepId!.Value, PbcAcceptanceReceiptId = request.SourceReceiptId,
      Version = (head?.Version ?? 0) + 1, TaskRevision = basis.Value.TaskRevision,
      BindingGeneration = basis.Value.BindingGeneration, EngagementGeneration = basis.Value.EngagementGeneration,
      ConditionDigest = basis.Value.ConditionDigest, BasisJson = basis.Value.Json, BasisHash = basis.Value.Hash,
      Reason = reason!, ActorUserId = actor.UserId, ActorEpoch = actor.SessionEpoch,
      IdempotencyKey = request.IdempotencyKey, RequestDigest = digest, OccurredAt = UtcMicrosecondNow()
    };
    db.WorkflowTaskApplicabilityProposals.Add(row);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<WorkflowApplicabilityReceipt>.Ok(new(engagementId, taskId, request.IdempotencyKey, row.Id, null, row.Version));
  }

  public static async Task<CommandResult<WorkflowApplicabilityReceipt>> DecideWorkflowApplicabilityAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, Guid taskId,
    WorkflowApplicabilityDecisionRequest request, CancellationToken ct = default)
  {
    var reason = request.Reason?.Trim();
    if (request.ProposalId == Guid.Empty || request.ExpectedTaskRevision < 1 ||
        request.Kind is not (WorkflowTaskApplicabilityDecisions.Approve or WorkflowTaskApplicabilityDecisions.Return or WorkflowTaskApplicabilityDecisions.Revoke) ||
        !ValidApplicabilityRequest(request.IdempotencyKey, request.BasisHash, reason))
      return ApplicabilityFailure("request.invalid", "Choose the exact proposal and decision, with a reviewed basis and reason.");
    if (db.Database.CurrentTransaction is not null)
      return ApplicabilityFailure(ErrorCodes.ProtectedState, "Applicability publication requires its own transaction.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var approving = request.Kind == WorkflowTaskApplicabilityDecisions.Approve;
    var locked = await LockApplicabilityTaskAsync(db, actor, engagementId, taskId, approving, ct);
    if (!locked.Succeeded) return ApplicabilityFailure(locked.ErrorCode!, locked.Message!);
    var binding = locked.Value!;
    var digest = Hashing.Sha256Hex(JsonSerializer.Serialize(new
    {
      Action = "WORKFLOW_APPLICABILITY_DECIDE", engagementId, taskId, request.ProposalId, request.ExpectedDecisionId,
      request.Kind, request.ExpectedTaskRevision, request.BasisHash, reason
    }));
    var prior = await db.WorkflowTaskApplicabilityDecisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorUserId == actor.UserId && x.IdempotencyKey == request.IdempotencyKey, ct);
    if (prior is not null)
      return prior.RequestDigest == digest && prior.WorkflowTaskBindingId == binding.Id
        ? CommandResult<WorkflowApplicabilityReceipt>.Ok(new(engagementId, taskId, request.IdempotencyKey, prior.ProposalId, prior.Id, prior.Version))
        : ApplicabilityFailure(ErrorCodes.IdempotencyConflict, "This request key already records different reviewed content.");
    if (await db.WorkflowTaskApplicabilityProposals.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
          x.ActorUserId == actor.UserId && x.IdempotencyKey == request.IdempotencyKey, ct))
      return ApplicabilityFailure(ErrorCodes.IdempotencyConflict, "This request key already records a proposal.");
    var proposal = await db.WorkflowTaskApplicabilityProposals.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == binding.ClientId && x.EngagementId == engagementId &&
      x.WorkflowTaskBindingId == binding.Id && x.Id == request.ProposalId, ct);
    if (proposal is null) return ApplicabilityFailure(ErrorCodes.ScopeDenied, "Access denied.");
    if (proposal.ActorUserId == actor.UserId)
      return ApplicabilityFailure(ErrorCodes.ScopeDenied, "An independent reviewer must decide this proposal.");
    var head = await db.WorkflowTaskApplicabilityDecisions.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ProposalId == proposal.Id).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (head?.Id != request.ExpectedDecisionId || proposal.BasisHash != request.BasisHash)
      return ApplicabilityFailure(ErrorCodes.StaleRevision, "The reviewed proposal or decision changed. Refresh its retained history.");
    if (request.Kind == WorkflowTaskApplicabilityDecisions.Revoke
          ? head?.Kind != WorkflowTaskApplicabilityDecisions.Approve
          : head is not null)
      return ApplicabilityFailure(ErrorCodes.ProtectedState, "An undecided proposal may be approved or returned; only its approval may be revoked.");
    if (approving)
    {
      var latestProposalId = await db.WorkflowTaskApplicabilityProposals.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.WorkflowTaskBindingId == binding.Id)
        .OrderByDescending(x => x.Version).Select(x => x.Id).FirstAsync(ct);
      if (latestProposalId != proposal.Id || binding.Revision != request.ExpectedTaskRevision)
        return ApplicabilityFailure(ErrorCodes.StaleRevision, "A successor proposal or task change requires a fresh review.");
      var basis = await WorkflowTaskApplicabilityBasisQuery.GetAsync(db, binding, proposal.PbcAcceptanceReceiptId, ct);
      if (!basis.Succeeded) return ApplicabilityFailure(basis.ErrorCode!, basis.Message!);
      if (basis.Value!.Hash != proposal.BasisHash)
        return ApplicabilityFailure(ErrorCodes.StaleRevision, "The exact source basis is stale. No approval was saved.");
    }
    // Removing an exclusion remains possible after source/task staleness or professional hold.
    // The exact proposal/decision head and current reviewer authority are still mandatory.
    var auth = await AuthorizeApplicabilityAsync(db, actor, binding, approving, ct);
    if (!auth.Succeeded) return ApplicabilityFailure(auth.ErrorCode!, auth.Message!);
    var row = new WorkflowTaskApplicabilityDecision
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = binding.ClientId, EngagementId = engagementId,
      WorkflowTaskBindingId = binding.Id, ProposalId = proposal.Id, ProposerUserId = proposal.ActorUserId,
      PreviousDecisionId = head?.Id, Version = (head?.Version ?? 0) + 1, Kind = request.Kind,
      BasisHash = proposal.BasisHash, Reason = reason!, ActorUserId = actor.UserId, ActorEpoch = actor.SessionEpoch,
      IdempotencyKey = request.IdempotencyKey, RequestDigest = digest, OccurredAt = UtcMicrosecondNow()
    };
    db.WorkflowTaskApplicabilityDecisions.Add(row);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<WorkflowApplicabilityReceipt>.Ok(new(engagementId, taskId, request.IdempotencyKey, proposal.Id, row.Id, row.Version));
  }

  private static async Task<CommandResult<WorkflowTaskBinding>> LockApplicabilityTaskAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid engagementId, Guid taskId, bool requireProfessionalWork, CancellationToken ct)
  {
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<WorkflowTaskBinding>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var engagement = await db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE id = {engagementId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (engagement is null) return CommandResult<WorkflowTaskBinding>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var client = await LockClientAsync(db, actor.FirmId, engagement.PracticeClientId, ct);
    if (!client.Succeeded) return CommandResult<WorkflowTaskBinding>.Fail(client.ErrorCode!, client.Message!);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, engagement.PracticeClientId, engagementId, WorkflowTaskEvidenceQuery.ManageRoles,
        InternalOnly: true, RequireProfessionalWork: requireProfessionalWork), ct);
    if (!auth.Succeeded) return CommandResult<WorkflowTaskBinding>.Fail(auth.ErrorCode!, auth.Message!);
    var task = await LoadTaskForUpdateAsync(db, actor.FirmId, taskId, ct);
    var binding = await LockWorkflowBindingAsync(db, actor.FirmId, taskId, ct);
    if (task is null || binding is null) return CommandResult<WorkflowTaskBinding>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await db.WorkTasks.Attach(task).ReloadAsync(ct);
    await db.WorkflowTaskBindings.Attach(binding).ReloadAsync(ct);
    if (task.ClientId != engagement.PracticeClientId || task.EngagementId != engagementId ||
        binding.ClientId != engagement.PracticeClientId || binding.EngagementId != engagementId)
      return CommandResult<WorkflowTaskBinding>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    auth = await AuthorizeApplicabilityAsync(db, actor, binding, requireProfessionalWork, ct);
    if (!auth.Succeeded) return CommandResult<WorkflowTaskBinding>.Fail(auth.ErrorCode!, auth.Message!);
    return CommandResult<WorkflowTaskBinding>.Ok(binding);
  }

  private static Task<CommandResult> AuthorizeApplicabilityAsync(IAuditSphereDbContext db, ActorContext actor,
    WorkflowTaskBinding binding, bool requireProfessionalWork, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, binding.ClientId, binding.EngagementId, WorkflowTaskEvidenceQuery.ManageRoles,
        InternalOnly: true, RequireProfessionalWork: requireProfessionalWork), ct);

  private static bool ValidApplicabilityRequest(Guid key, string? hash, string? reason) =>
    key != Guid.Empty && hash is { Length: 64 } && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') &&
    !string.IsNullOrWhiteSpace(reason) && reason.Length <= 1000;

  private static CommandResult<WorkflowApplicabilityReceipt> ApplicabilityFailure(string code, string message) =>
    CommandResult<WorkflowApplicabilityReceipt>.Fail(code, message);
}
