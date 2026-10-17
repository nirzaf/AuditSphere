using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditProgramService
{
  private static readonly string[] PartnerApplicabilityRoles = ["Partner"];

  internal static bool IsCurrentApprovedNotApplicable(AuditProcedure procedure,
    AuditProcedureApplicabilityDecision? decision, AuditProcedureApplicabilityDecision? proposal,
    long currentInputGeneration) =>
    procedure.ApplicabilityStatus == AuditApplicabilityStatuses.NotApplicableApproved &&
    procedure.CurrentApplicabilityRevision > 0 && procedure.CurrentApplicabilityDecisionId == decision?.Id &&
    decision is { Action: AuditApplicabilityActions.ApproveNotApplicable, ProposalDecisionId: not null } &&
    decision.Revision == procedure.CurrentApplicabilityRevision &&
    decision.DefinitionRevision == procedure.CurrentDefinitionRevision && decision.InputGeneration == currentInputGeneration &&
    proposal is { Action: AuditApplicabilityActions.ProposeNotApplicable } && proposal.Id == decision.ProposalDecisionId &&
    proposal.Revision + 1 == decision.Revision && proposal.ActorUserId != decision.ActorUserId &&
    proposal.DefinitionRevision == decision.DefinitionRevision && proposal.InputGeneration == decision.InputGeneration;

  internal static bool IsCurrentPendingNotApplicable(AuditProcedure procedure,
    AuditProcedureApplicabilityDecision? decision, long currentInputGeneration) =>
    procedure.ApplicabilityStatus == AuditApplicabilityStatuses.NotApplicablePendingReview &&
    decision is { Action: AuditApplicabilityActions.ProposeNotApplicable } &&
    decision.Id == procedure.CurrentApplicabilityDecisionId && decision.Revision == procedure.CurrentApplicabilityRevision &&
    decision.DefinitionRevision == procedure.CurrentDefinitionRevision && decision.InputGeneration == currentInputGeneration;

  internal static string EffectiveApplicabilityStatus(AuditProcedure procedure,
    AuditProcedureApplicabilityDecision? decision, AuditProcedureApplicabilityDecision? proposal,
    long currentInputGeneration)
  {
    if (procedure.ApplicabilityStatus == AuditApplicabilityStatuses.NotApplicableApproved &&
        !IsCurrentApprovedNotApplicable(procedure, decision, proposal, currentInputGeneration))
      return AuditApplicabilityStatuses.NotApplicableUnverified;
    if (procedure.ApplicabilityStatus == AuditApplicabilityStatuses.NotApplicablePendingReview &&
        !IsCurrentPendingNotApplicable(procedure, decision, currentInputGeneration))
      return AuditApplicabilityStatuses.NotApplicableUnverified;
    return procedure.ApplicabilityStatus;
  }

  internal static async Task<Dictionary<Guid, AuditProcedureApplicabilityDecision>> CurrentApplicabilityDecisionsAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId,
    IReadOnlyCollection<AuditProcedure> procedures, CancellationToken ct)
  {
    if (procedures.Count == 0) return [];
    var ids = procedures.Where(x => x.CurrentApplicabilityDecisionId.HasValue)
      .Select(x => x.CurrentApplicabilityDecisionId!.Value).ToArray();
    if (ids.Length == 0) return [];
    return await db.AuditProcedureApplicabilityDecisions.AsNoTracking().Where(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && ids.Contains(x.Id))
      .ToDictionaryAsync(x => x.AuditProcedureId, ct);
  }

  internal static async Task<Dictionary<Guid, AuditProcedureApplicabilityDecision>> ProposalDecisionsAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId,
    IReadOnlyCollection<AuditProcedureApplicabilityDecision> decisions, CancellationToken ct)
  {
    var ids = decisions.Where(x => x.ProposalDecisionId.HasValue).Select(x => x.ProposalDecisionId!.Value).Distinct().ToArray();
    if (ids.Length == 0) return [];
    return await db.AuditProcedureApplicabilityDecisions.AsNoTracking().Where(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && ids.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, ct);
  }

  internal static async Task<Dictionary<Guid, string>> EffectiveApplicabilityStatusesAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId,
    IReadOnlyCollection<AuditProcedure> procedures, CancellationToken ct)
  {
    if (procedures.Count == 0) return [];
    var current = await CurrentApplicabilityDecisionsAsync(db, firmId, clientId, engagementId, procedures, ct);
    var proposals = await ProposalDecisionsAsync(db, firmId, clientId, engagementId, current.Values.ToArray(), ct);
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == firmId && x.Id == clientId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct) ?? 0;
    return procedures.ToDictionary(x => x.Id, x => EffectiveApplicabilityStatus(x,
      current.GetValueOrDefault(x.Id),
      current.GetValueOrDefault(x.Id)?.ProposalDecisionId is { } proposalId ? proposals.GetValueOrDefault(proposalId) : null,
      generation));
  }

  private static string ApplicabilityDigest(string action, string? rationale, Guid? proposalId,
    int expectedRevision, int expectedDefinitionRevision) => Convert.ToHexString(SHA256.HashData(
      Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
      {
        action, rationale = rationale?.Trim(), proposalId, expectedRevision, expectedDefinitionRevision
      })))).ToLowerInvariant();

  private static string NormalizeApplicabilityAction(string value) => value.Trim().ToUpperInvariant() switch
  {
    AuditApplicabilityStatuses.Applicable => AuditApplicabilityActions.Applicable,
    AuditApplicabilityStatuses.NotApplicablePendingReview => AuditApplicabilityActions.ProposeNotApplicable,
    AuditApplicabilityActions.ProposeNotApplicable => AuditApplicabilityActions.ProposeNotApplicable,
    AuditApplicabilityActions.ApproveNotApplicable => AuditApplicabilityActions.ApproveNotApplicable,
    AuditApplicabilityActions.ReturnNotApplicable => AuditApplicabilityActions.ReturnNotApplicable,
    _ => string.Empty
  };

  private static async Task<CommandResult<ProcedureApplicabilityResult>> DecideApplicabilityWithHistoryAsync(
    IAuditSphereDbContext db, ActorContext actor, DecideProcedureApplicabilityRequest request, CancellationToken ct)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(request);
    var action = NormalizeApplicabilityAction(request.Decision ?? string.Empty);
    var rationale = request.Rationale?.Trim();
    if (action.Length == 0 || request.AuditProcedureId == Guid.Empty || request.CommandId == Guid.Empty ||
        request.ExpectedApplicabilityRevision < 0 || request.ExpectedDefinitionRevision < 0 ||
        ((action is AuditApplicabilityActions.ProposeNotApplicable or AuditApplicabilityActions.ReturnNotApplicable) &&
          (string.IsNullOrWhiteSpace(rationale) || rationale.Length > 1000)) || rationale?.Length > 1000)
      return CommandResult<ProcedureApplicabilityResult>.Fail(InvalidCode,
        "Provide a supported action, bounded reason where required, current applicability and definition revisions, and a command identity. N/A approval must reference a pending proposal.");
    var roles = action is AuditApplicabilityActions.ApproveNotApplicable or AuditApplicabilityActions.ReturnNotApplicable
      ? PartnerApplicabilityRoles : PlanningRoles;
    var digest = ApplicabilityDigest(action, rationale, request.ProposalDecisionId,
      request.ExpectedApplicabilityRevision, request.ExpectedDefinitionRevision);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var snapshot = await db.AuditProcedures.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == request.AuditProcedureId, ct);
    if (snapshot is null)
      return CommandResult<ProcedureApplicabilityResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR SHARE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    var inputGeneration = await AuditPlanningInputGeneration.LockAndReadAsync(db, actor.FirmId, snapshot.ClientId, ct);
    if (inputGeneration is null || inputGeneration < 1)
      return CommandResult<ProcedureApplicabilityResult>.Fail(ErrorCodes.GateBlocked,
        "Current client input generation is unavailable; applicability was not changed.");
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR SHARE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    var scope = await ResolveEngagementAsync(db, actor, snapshot.EngagementId, ct, snapshot.ClientId, roles);
    if (scope.Denied is not null)
      return CommandResult<ProcedureApplicabilityResult>.Fail(scope.Denied, scope.Message);

    var procedure = await db.AuditProcedures.FromSqlInterpolated($"""
      SELECT * FROM audit_procedures
      WHERE firm_id={actor.FirmId} AND id={request.AuditProcedureId}
      FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (procedure is null || procedure.ClientId != scope.ClientId || procedure.EngagementId != snapshot.EngagementId)
      return CommandResult<ProcedureApplicabilityResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var replay = await db.AuditProcedureApplicabilityDecisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == scope.FirmId && x.AuditProcedureId == procedure.Id && x.CommandId == request.CommandId, ct);
    if (replay is not null)
      return replay.ActorUserId == actor.UserId && replay.ActorEpoch == actor.SessionEpoch &&
        string.Equals(replay.RequestDigest, digest, StringComparison.Ordinal)
        ? CommandResult<ProcedureApplicabilityResult>.Ok(new(procedure.Id, procedure.SourceProcedureId,
          procedure.ApplicabilityStatus, procedure.Status, replay.Id, replay.Revision, replay.Action))
        : CommandResult<ProcedureApplicabilityResult>.Fail(ErrorCodes.IdempotencyConflict,
          "That applicability command identity was already used for different intent.");

    if (procedure.CurrentApplicabilityRevision != request.ExpectedApplicabilityRevision ||
        procedure.CurrentDefinitionRevision != request.ExpectedDefinitionRevision)
      return CommandResult<ProcedureApplicabilityResult>.Fail(ErrorCodes.GenerationStale,
        "The procedure applicability or effective definition changed. Refresh the current history before deciding.");
    if ((action is AuditApplicabilityActions.ApproveNotApplicable or AuditApplicabilityActions.ReturnNotApplicable) &&
        request.ProposalDecisionId is null)
      return CommandResult<ProcedureApplicabilityResult>.Fail(InvalidCode,
        "A Partner decision must identify the exact pending N/A proposal.");

    AuditProcedureApplicabilityDecision? previous = null;
    if (procedure.CurrentApplicabilityDecisionId is { } currentDecisionId)
      previous = await db.AuditProcedureApplicabilityDecisions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == scope.FirmId && x.ClientId == scope.ClientId && x.EngagementId == procedure.EngagementId &&
        x.AuditProcedureId == procedure.Id && x.Id == currentDecisionId, ct);

    AuditProcedureApplicabilityDecision? proposal = null;
    if (action is AuditApplicabilityActions.ApproveNotApplicable or AuditApplicabilityActions.ReturnNotApplicable)
    {
      if (previous is null || previous.Id != request.ProposalDecisionId ||
          previous.Action != AuditApplicabilityActions.ProposeNotApplicable ||
          previous.DefinitionRevision != procedure.CurrentDefinitionRevision || previous.InputGeneration != inputGeneration)
        return CommandResult<ProcedureApplicabilityResult>.Fail(ErrorCodes.GenerationStale,
          "The referenced N/A proposal is absent, no longer pending, or stale against current inputs. Submit a current proposal first.");
      if (previous.ActorUserId == actor.UserId)
        return CommandResult<ProcedureApplicabilityResult>.Fail(ErrorCodes.ScopeDenied,
          "The N/A proposer cannot approve or return the same proposal.");
      proposal = previous;
    }
    else if (action == AuditApplicabilityActions.ProposeNotApplicable && previous is
             { Action: AuditApplicabilityActions.ProposeNotApplicable } &&
             previous.DefinitionRevision == procedure.CurrentDefinitionRevision && previous.InputGeneration == inputGeneration)
      return CommandResult<ProcedureApplicabilityResult>.Fail(ErrorCodes.ProtectedState,
        "An N/A proposal is already awaiting its independent Partner decision.");

    var frozen = await AuditSphereOps.Application.Records.FileFreezeService.RequireWritableAsync(
      db, actor, procedure.EngagementId, "record procedure applicability", ct);
    if (!frozen.Succeeded)
    {
      await tx.CommitAsync(ct);
      return CommandResult<ProcedureApplicabilityResult>.Fail(frozen.ErrorCode!, frozen.Message!);
    }

    var now = DateTimeOffset.UtcNow;
    var decision = new AuditProcedureApplicabilityDecision
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = scope.ClientId,
      EngagementId = procedure.EngagementId, AuditProcedureId = procedure.Id,
      Revision = checked(procedure.CurrentApplicabilityRevision + 1),
      DefinitionRevision = procedure.CurrentDefinitionRevision, InputGeneration = inputGeneration.Value,
      Action = action, Rationale = rationale, ProposalDecisionId = proposal?.Id,
      CommandId = request.CommandId, RequestDigest = digest, PreviousDecisionId = previous?.Id,
      ActorUserId = actor.UserId, ActorEpoch = actor.SessionEpoch, OccurredAt = now
    };
    db.AuditProcedureApplicabilityDecisions.Add(decision);
    await db.SaveChangesAsync(ct);

    procedure.CurrentApplicabilityRevision = decision.Revision;
    procedure.CurrentApplicabilityDecisionId = decision.Id;
    procedure.ApplicabilityDecidedByUserId = actor.UserId;
    procedure.ApplicabilityDecidedAt = now;
    switch (action)
    {
      case AuditApplicabilityActions.Applicable:
        procedure.ApplicabilityStatus = AuditApplicabilityStatuses.Applicable;
        procedure.ApplicabilityRationale = rationale;
        if (procedure.Status == AuditProcedureStatuses.Planned) procedure.Status = AuditProcedureStatuses.InProgress;
        break;
      case AuditApplicabilityActions.ProposeNotApplicable:
        procedure.ApplicabilityStatus = AuditApplicabilityStatuses.NotApplicablePendingReview;
        procedure.ApplicabilityRationale = rationale;
        break;
      case AuditApplicabilityActions.ApproveNotApplicable:
        procedure.ApplicabilityStatus = AuditApplicabilityStatuses.NotApplicableApproved;
        procedure.ApplicabilityRationale = proposal!.Rationale;
        break;
      case AuditApplicabilityActions.ReturnNotApplicable:
        procedure.ApplicabilityStatus = AuditApplicabilityStatuses.Pending;
        procedure.ApplicabilityRationale = proposal!.Rationale;
        break;
    }
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ProcedureApplicabilityResult>.Ok(new(procedure.Id, procedure.SourceProcedureId,
      procedure.ApplicabilityStatus, procedure.Status, decision.Id, decision.Revision, decision.Action));
  }
}
