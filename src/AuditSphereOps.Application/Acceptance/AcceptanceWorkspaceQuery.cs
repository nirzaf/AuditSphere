using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

public sealed record AcceptanceQuestionView(string Code, string Section, string Prompt, string Category, string AnswerType,
  bool RequiresEvidence, bool Adverse, string? Answer, string? Evidence, string Revision, string? PriorAnswer, string? AnsweredBy);
public sealed record AcceptanceClearanceView(Guid Id, string Area, string Specialist, string Status, string? Evidence,
  string? Conditions, DateTimeOffset CreatedAt, DateTimeOffset? ClearedAt);
public sealed record AcceptanceWorkspace(Guid ClientId, string Generation, string Path, string? CurrentDecision, string? PriorDecision,
  bool Ready, bool CanEdit, bool CanReview, bool CanDecide, bool CanStartContinuance, IReadOnlyList<AcceptanceQuestionView> Questions,
  IReadOnlyList<AcceptanceClearanceView> Clearances, IReadOnlyList<AcceptanceBlocker> Blockers);

public static class AcceptanceWorkspaceQuery
{
  public static async Task<CommandResult<AcceptanceWorkspace>> GetAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid clientId, CancellationToken ct = default)
  {
    var result = await AcceptanceChecklistService.GetAsync(db, actor, clientId, ct);
    if (!result.Succeeded) return CommandResult<AcceptanceWorkspace>.Fail(result.ErrorCode!, "Acceptance unavailable.");
    var c = result.Value!;
    var canEdit = c.CurrentDecision is null && (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: clientId,
        RequiredRoles: ["Partner", "Manager", "Senior", "Staff", "EngagementLeader", "Auditor"], InternalOnly: true), ct)).Succeeded;
    var final = await AcceptanceChecklistService.GetAsync(db, actor, clientId, ct);
    if (!final.Succeeded)
      return CommandResult<AcceptanceWorkspace>.Fail(ErrorCodes.ScopeDenied, "Acceptance unavailable.");
    if (final.Value!.Generation != c.Generation)
      return CommandResult<AcceptanceWorkspace>.Fail(ErrorCodes.GenerationStale, "Acceptance changed. Reload the current evaluation.");
    var reviewer = (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: clientId, RequiredRoles: ["Partner", "Manager"], InternalOnly: true), ct)).Succeeded;
    var partner = (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: clientId, RequiredRoles: ["Partner"], InternalOnly: true), ct)).Succeeded;
    return CommandResult<AcceptanceWorkspace>.Ok(new(clientId, c.Generation.ToString(CultureInfo.InvariantCulture), c.Path,
      c.CurrentDecision?.Decision, c.PriorDecision?.Decision, c.Ready, canEdit, reviewer && c.CurrentDecision is null,
      partner && c.CurrentDecision is null, reviewer && c.CurrentDecision?.Decision is "Accepted" or "AcceptedWithConditions",
      c.Items.Select(i => new AcceptanceQuestionView(i.Question.Code, i.Question.Section, i.Question.Prompt,
        i.Question.Category, i.Question.AnswerType, i.Question.RequiresEvidence, i.Adverse, i.Answer?.Answer,
        i.Answer?.EvidenceReference, (i.Answer?.Revision ?? 0).ToString(CultureInfo.InvariantCulture), i.PriorAnswer?.Answer, i.AnsweredBy)).ToArray(),
      c.Clearances.Select(x => new AcceptanceClearanceView(x.Id, x.Area, x.SpecialistName, x.Status,
        x.EvidenceReference, x.Conditions, x.CreatedAt, x.ClearedAt)).ToArray(), c.Blockers));
  }

  public static async Task<CommandResult> RecordReviewAsync(IAuditSphereDbContext db, ActorContext actor, Guid clientId,
    Guid reviewId, string status, string? evidence, string? conditions, long generation, string expectedStatus, CancellationToken ct = default)
  {
    if (!await db.SpecialistClearances.AsNoTracking().AnyAsync(c => c.FirmId == actor.FirmId && c.PracticeClientId == clientId && c.Id == reviewId, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Review unavailable.");
    return await AcceptanceChecklistService.RecordClearanceAsync(db, actor, reviewId, status, evidence, conditions, ct, generation, expectedStatus);
  }
}
