using System.Data;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record WorkflowApplicabilityPreview(Guid SourceRequestId, Guid? SourceReceiptId,
  string? BasisHash, string? BasisJson, string? Blocker);
public sealed record WorkflowApplicabilityDecisionView(Guid Id, long Version, string Kind, Guid ActorUserId,
  string ActorName, string Reason, DateTimeOffset OccurredAt);
public sealed record WorkflowApplicabilityProposalView(Guid Id, long Version, Guid ActorUserId, string ActorName,
  string Reason, DateTimeOffset OccurredAt, Guid SourceReceiptId, string BasisHash, string BasisJson,
  bool IsLatest, bool BasisCurrent, string? CurrentBasisJson, string? Blocker,
  WorkflowApplicabilityDecisionView? Decision);
public sealed record WorkflowApplicabilityWorkspace(Guid EngagementId, Guid TaskId, long TaskRevision,
  Guid? LatestProposalId, WorkflowApplicabilityProposalView? Proposal, WorkflowApplicabilityPreview? Preview,
  bool CanPropose, bool CanApprove, bool CanReturn, bool CanRevoke);

/// <summary>Task evidence reads only. A retained receipt or history row never establishes current applicability.</summary>
public static partial class WorkflowTaskApplicabilityQuery
{
  public static async Task<CommandResult<WorkflowApplicabilityWorkspace>> GetAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid engagementId, Guid taskId, Guid? sourceRequestId = null, Guid? proposalId = null,
    CancellationToken ct = default)
  {
    if (sourceRequestId == Guid.Empty || proposalId == Guid.Empty)
      return CommandResult<WorkflowApplicabilityWorkspace>.Fail("request.invalid", "Select an exact source or retained proposal.");
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<WorkflowApplicabilityWorkspace>.Fail(ErrorCodes.GateBlocked, "Applicability requires an independent read snapshot.");
    WorkflowApplicabilityWorkspace workspace;
    WorkflowTaskBinding binding;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      var auth = await AuthorizeReadAsync(db, actor, engagementId, ct);
      if (!auth.Succeeded) return CommandResult<WorkflowApplicabilityWorkspace>.Fail(auth.ErrorCode!, auth.Message!);
      var found = await db.WorkflowTaskBindings.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.EngagementId == engagementId && x.WorkTaskId == taskId, ct);
      if (found is null) return CommandResult<WorkflowApplicabilityWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      binding = found;
      var latest = await ProposalHeads(db, actor.FirmId).SingleOrDefaultAsync(x => x.WorkflowTaskBindingId == binding.Id, ct);
      var selected = proposalId.HasValue ? await db.WorkflowTaskApplicabilityProposals.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == binding.ClientId && x.EngagementId == engagementId &&
        x.WorkflowTaskBindingId == binding.Id && x.Id == proposalId.Value, ct) : latest;
      if (proposalId.HasValue && selected is null)
        return CommandResult<WorkflowApplicabilityWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      WorkflowApplicabilityProposalView? proposal = null;
      CommandResult<WorkflowTaskApplicabilityBasis>? selectedBasis = null;
      if (selected is not null)
      {
        var decision = await DecisionHeads(db, actor.FirmId).SingleOrDefaultAsync(x => x.ProposalId == selected.Id, ct);
        var actorIds = new[] { selected.ActorUserId, decision?.ActorUserId ?? Guid.Empty };
        var names = await db.Users.AsNoTracking().Where(x => x.FirmId == actor.FirmId && actorIds.Contains(x.Id))
          .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        selectedBasis = await WorkflowTaskApplicabilityBasisQuery.GetAsync(db, binding, selected.PbcAcceptanceReceiptId, ct);
        var current = selectedBasis.Succeeded && selectedBasis.Value!.Hash == selected.BasisHash;
        proposal = new(selected.Id, selected.Version, selected.ActorUserId, names.GetValueOrDefault(selected.ActorUserId) ?? "Staff member",
          selected.Reason, selected.OccurredAt, selected.PbcAcceptanceReceiptId, selected.BasisHash, selected.BasisJson,
          selected.Id == latest?.Id, current, selectedBasis.Value?.Json,
          current ? null : "The retained proposal no longer matches the current task and accepted-source basis.",
          decision is null ? null : new(decision.Id, decision.Version, decision.Kind, decision.ActorUserId,
            names.GetValueOrDefault(decision.ActorUserId) ?? "Staff member", decision.Reason, decision.OccurredAt));
      }
      WorkflowApplicabilityPreview? preview = null;
      if (sourceRequestId.HasValue)
      {
        // Reuse the existing exact PBC request picker. The server resolves its immutable acceptance identity.
        var receipt = await db.PbcAcceptanceReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
          x.ClientId == binding.ClientId && x.EngagementId == engagementId && x.PbcRequestId == sourceRequestId.Value, ct);
        if (receipt is null)
          preview = new(sourceRequestId.Value, null, null, null, "Select a current independently accepted file in this engagement.");
        else
        {
          var basis = selected?.PbcAcceptanceReceiptId == receipt.Id && selectedBasis is not null
            ? selectedBasis : await WorkflowTaskApplicabilityBasisQuery.GetAsync(db, binding, receipt.Id, ct);
          preview = new(sourceRequestId.Value, receipt.Id, basis.Value?.Hash, basis.Value?.Json,
            basis.Succeeded ? null : "The selected acceptance no longer provides a current conditional-task basis.");
        }
      }
      var manage = (await AuthorizeManageAsync(db, actor, binding, ct)).Succeeded;
      var professionalOpen = manage && await ProfessionalReadinessAsync(db, binding, ct);
      var independent = proposal is not null && proposal.ActorUserId != actor.UserId;
      workspace = new(engagementId, taskId, binding.Revision, latest?.Id, proposal, preview,
        professionalOpen && preview?.BasisHash is not null,
        professionalOpen && independent && proposal!.IsLatest && proposal.BasisCurrent && proposal.Decision is null,
        manage && independent && proposal!.Decision is null,
        manage && independent && proposal!.Decision?.Kind == WorkflowTaskApplicabilityDecisions.Approve);
      await snapshot.CommitAsync(ct);
    }
    var final = await AuthorizeReadAsync(db, actor, engagementId, ct);
    if (!final.Succeeded) return CommandResult<WorkflowApplicabilityWorkspace>.Fail(final.ErrorCode!, final.Message!);
    var currentManage = (await AuthorizeManageAsync(db, actor, binding, ct)).Succeeded;
    var currentProfessionalOpen = currentManage && await ProfessionalReadinessAsync(db, binding, ct);
    return CommandResult<WorkflowApplicabilityWorkspace>.Ok(workspace with
    {
      CanPropose = workspace.CanPropose && currentProfessionalOpen,
      CanApprove = workspace.CanApprove && currentProfessionalOpen,
      CanReturn = workspace.CanReturn && currentManage,
      CanRevoke = workspace.CanRevoke && currentManage
    });
  }

  public static async Task<CommandResult<WorkflowApplicabilityReceipt>> ReceiptAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid engagementId, Guid taskId, Guid key, CancellationToken ct = default)
  {
    if (key == Guid.Empty) return CommandResult<WorkflowApplicabilityReceipt>.Fail("request.invalid", "Keep the original request key.");
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<WorkflowApplicabilityReceipt>.Fail(ErrorCodes.GateBlocked, "Receipt recovery requires an independent read snapshot.");
    WorkflowApplicabilityReceipt? result;
    WorkflowTaskBinding binding;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      var auth = await AuthorizeReadAsync(db, actor, engagementId, ct);
      if (!auth.Succeeded) return CommandResult<WorkflowApplicabilityReceipt>.Fail(auth.ErrorCode!, auth.Message!);
      var found = await db.WorkflowTaskBindings.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.EngagementId == engagementId && x.WorkTaskId == taskId, ct);
      if (found is null) return CommandResult<WorkflowApplicabilityReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      binding = found;
      auth = await AuthorizeManageAsync(db, actor, binding, ct);
      if (!auth.Succeeded) return CommandResult<WorkflowApplicabilityReceipt>.Fail(auth.ErrorCode!, auth.Message!);
      var proposal = await db.WorkflowTaskApplicabilityProposals.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
          x.ClientId == binding.ClientId && x.EngagementId == engagementId && x.WorkflowTaskBindingId == binding.Id &&
          x.ActorUserId == actor.UserId && x.IdempotencyKey == key)
        .Select(x => new WorkflowApplicabilityReceipt(engagementId, taskId, key, x.Id, null, x.Version)).SingleOrDefaultAsync(ct);
      var decision = await db.WorkflowTaskApplicabilityDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
          x.ClientId == binding.ClientId && x.EngagementId == engagementId && x.WorkflowTaskBindingId == binding.Id &&
          x.ActorUserId == actor.UserId && x.IdempotencyKey == key)
        .Select(x => new WorkflowApplicabilityReceipt(engagementId, taskId, key, x.ProposalId, x.Id, x.Version)).SingleOrDefaultAsync(ct);
      if (proposal is not null && decision is not null)
        return CommandResult<WorkflowApplicabilityReceipt>.Fail(ErrorCodes.ProtectedState, "The request evidence is ambiguous; no result was selected.");
      result = proposal ?? decision;
      await snapshot.CommitAsync(ct);
    }
    var final = await AuthorizeManageAsync(db, actor, binding, ct);
    if (!final.Succeeded) return CommandResult<WorkflowApplicabilityReceipt>.Fail(final.ErrorCode!, final.Message!);
    return result is null ? CommandResult<WorkflowApplicabilityReceipt>.Fail(ErrorCodes.GateBlocked,
      "No retained result was found for your exact request. Keep its key; no command was resubmitted.") : CommandResult<WorkflowApplicabilityReceipt>.Ok(result);
  }

  // For already-authorized bindings within one caller-owned coherent snapshot. A current task decision
  // still does not approve professional procedures or turn an incomplete task into a completed prerequisite.
  internal static async Task<IReadOnlyDictionary<Guid, bool>> EffectiveManyAsync(IAuditSphereDbContext db, Guid firmId,
    IReadOnlyList<WorkflowTaskBinding> bindings, CancellationToken ct)
  {
    if (bindings.Count > WorkflowTaskEvidenceQuery.MaximumBindings || bindings.Any(x => x.FirmId != firmId) ||
        bindings.Select(x => x.Id).Distinct().Count() != bindings.Count)
      throw new InvalidOperationException("Applicability evaluation requires a bounded unique same-firm task page.");
    var output = bindings.ToDictionary(x => x.Id, _ => false);
    if (bindings.Count == 0) return output;
    var ids = bindings.Select(x => x.Id).ToArray();
    var proposals = await ProposalHeads(db, firmId).Where(x => ids.Contains(x.WorkflowTaskBindingId)).ToListAsync(ct);
    var proposalIds = proposals.Select(x => x.Id).ToArray();
    var decisions = await DecisionHeads(db, firmId).Where(x => proposalIds.Contains(x.ProposalId) &&
      x.Kind == WorkflowTaskApplicabilityDecisions.Approve).ToDictionaryAsync(x => x.ProposalId, ct);
    var inputs = proposals.Where(p => decisions.ContainsKey(p.Id)).Select(p =>
      new WorkflowTaskApplicabilityBasisInput(bindings.Single(b => b.Id == p.WorkflowTaskBindingId), p.PbcAcceptanceReceiptId)).ToArray();
    var bases = await WorkflowTaskApplicabilityBasisQuery.GetManyAsync(db, firmId, inputs, ct);
    foreach (var proposal in proposals.Where(p => decisions.ContainsKey(p.Id)))
    {
      var binding = bindings.Single(b => b.Id == proposal.WorkflowTaskBindingId);
      var decision = decisions[proposal.Id];
      var basis = bases[binding.Id];
      output[binding.Id] = proposal.ClientId == binding.ClientId && proposal.EngagementId == binding.EngagementId &&
        decision.ClientId == binding.ClientId && decision.EngagementId == binding.EngagementId &&
        decision.WorkflowTaskBindingId == binding.Id && decision.ProposerUserId == proposal.ActorUserId &&
        decision.ActorUserId != proposal.ActorUserId && decision.BasisHash == proposal.BasisHash &&
        basis.Succeeded && basis.Value!.Hash == proposal.BasisHash;
    }
    return output;
  }

  private static IQueryable<WorkflowTaskApplicabilityProposal> ProposalHeads(IAuditSphereDbContext db, Guid firmId) =>
    db.WorkflowTaskApplicabilityProposals.AsNoTracking().Where(x => x.FirmId == firmId &&
      !db.WorkflowTaskApplicabilityProposals.Any(n => n.FirmId == x.FirmId &&
        n.WorkflowTaskBindingId == x.WorkflowTaskBindingId && n.Version > x.Version));

  private static IQueryable<WorkflowTaskApplicabilityDecision> DecisionHeads(IAuditSphereDbContext db, Guid firmId) =>
    db.WorkflowTaskApplicabilityDecisions.AsNoTracking().Where(x => x.FirmId == firmId &&
      !db.WorkflowTaskApplicabilityDecisions.Any(n => n.FirmId == x.FirmId && n.ProposalId == x.ProposalId && n.Version > x.Version));

  private static Task<CommandResult> AuthorizeReadAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId,
    CancellationToken ct) => AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, EngagementId: engagementId, RequiredRoles: WorkflowTaskEvidenceQuery.ReadRoles, InternalOnly: true), ct);

  private static Task<CommandResult> AuthorizeManageAsync(IAuditSphereDbContext db, ActorContext actor,
    WorkflowTaskBinding binding, CancellationToken ct) => AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, binding.ClientId, binding.EngagementId, WorkflowTaskEvidenceQuery.ManageRoles, InternalOnly: true), ct);

  // Presentation-only demotion mirrors the existing professional-work blockers without invoking the authorizer's
  // frozen-access audit write. Commands still execute their full canonical authorization under publication locks.
  private static async Task<bool> ProfessionalReadinessAsync(IAuditSphereDbContext db, WorkflowTaskBinding binding,
    CancellationToken ct) => await db.Engagements.AsNoTracking().AnyAsync(x => x.FirmId == binding.FirmId &&
      x.PracticeClientId == binding.ClientId && x.Id == binding.EngagementId && !x.ProfessionalWorkBlocked, ct) &&
    !await db.EngagementHolds.AsNoTracking().AnyAsync(x => x.FirmId == binding.FirmId && x.EngagementId == binding.EngagementId && !x.Released, ct);
}
