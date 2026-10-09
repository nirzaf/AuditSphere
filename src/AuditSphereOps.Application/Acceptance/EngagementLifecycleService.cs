using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

public sealed record CreateEngagementDraftRequest(
  Guid PracticeClientId, string ServiceRoute, string PeriodStart, string PeriodEnd, string ServiceProfileId);

/// <summary>
/// Engagement shells and the Partner activation gate. A new engagement is created blocked; only a Partner, only from
/// the client's current unconditional Accepted decision for the same service route, and only with no open holds, can
/// activate it. Activation records the exact decision and generation, so a declined, deferred, conditional or stale
/// decision (or a different service route) can never unblock professional work.
/// </summary>
public static partial class EngagementLifecycleService
{
  private static readonly string[] ActivationRoles = ["Partner"];

  public static async Task<CommandResult<Guid>> ActivateAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default,
    EngagementActivationRequest? reviewedRequest = null)
  {
    var stub = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (stub is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, stub.PracticeClientId, engagementId, RequiredRoles: ActivationRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    var guard = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE id = {stub.PracticeClientId} AND firm_id = {actor.FirmId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    if (guard is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR SHARE").AsNoTracking().SingleAsync(ct);
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, stub.PracticeClientId, engagementId, RequiredRoles: ActivationRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var engagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    var existing = await db.EngagementActivations.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);
    if (existing is not null)
    {
      if (reviewedRequest is not null && (existing.ActivatedByUserId != actor.UserId || existing.RequestId != reviewedRequest.RequestId ||
          existing.RequestHash != reviewedRequest.ExpectedRequestHash))
        return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "Another retained activation cannot acknowledge this request.");
      if (tx is not null) await tx.CommitAsync(ct);
      return CommandResult<Guid>.Ok(existing.Id);
    }
    if (reviewedRequest is not null)
    {
      var preview = await EngagementActivationWorkspace.PreviewAsync(db, actor, engagementId, reviewedRequest, ct);
      if (!preview.Succeeded) return CommandResult<Guid>.Fail(preview.ErrorCode!, preview.Message!);
      if (!reviewedRequest.Reviewed || preview.Value!.RequestHash != reviewedRequest.ExpectedRequestHash)
        return CommandResult<Guid>.Fail("request.invalid", "Review and confirm this exact activation.");
    }
    if (engagement.Generation == long.MaxValue)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Engagement revision capacity is exhausted.");
    if (engagement.Status != "Draft")
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "Only a draft engagement can be activated.");

    var decision = await db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == engagement.PracticeClientId &&
        x.Decision != "Pending" && x.ServiceRoute == engagement.ServiceRoute)
      .OrderByDescending(x => x.Generation).ThenByDescending(x => x.DecidedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    if (decision is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "No Partner acceptance decision exists for this client and service route.");
    if (decision.Generation != guard.InputGeneration)
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The acceptance decision is stale; the client evaluation changed after it. Record a current decision first.");
    if (decision.Decision != "Accepted")
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, decision.Decision == "AcceptedWithConditions"
        ? "The decision is conditional; its conditions must be resolved through a new unconditional decision before activation."
        : $"The current decision is {decision.Decision}; only an unconditional acceptance activates an engagement.");
    if (await db.EngagementHolds.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && !x.Released, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An unreleased hold blocks activation.");
    // STE 4.1.5 / C-02: every activation requires a linked agreement and its fully paid 50% advance.
    var linkedAgreement = await db.EngagementFeeAgreements.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);
    if (linkedAgreement is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "A linked engagement fee agreement with a fully paid and allocated 50% advance is required before the engagement can be activated.");
    if (linkedAgreement is not null && !await db.FeeMilestones.AnyAsync(x => x.FirmId == actor.FirmId && x.AgreementId == linkedAgreement.Id &&
      x.Kind == FeeMilestoneKinds.Advance && x.State == FeeMilestoneStates.Paid, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "The 50% advance must be fully paid and allocated before the engagement can be activated.");

    var activation = new EngagementActivation
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PracticeClientId = engagement.PracticeClientId, EngagementId = engagementId,
      AcceptanceDecisionId = decision.Id, ClientGeneration = guard.InputGeneration, AcceptancePath = decision.Path,
      ActivatedByUserId = actor.UserId, ActivatedAt = DateTimeOffset.UtcNow,
      RequestId = reviewedRequest?.RequestId, RequestHash = reviewedRequest?.ExpectedRequestHash, ReviewBasis = reviewedRequest?.ReviewBasis,
      ActorEpoch = reviewedRequest is null ? null : actor.SessionEpoch,
      EngagementGeneration = reviewedRequest is null ? null : engagement.Generation,
      ResultGeneration = reviewedRequest is null ? null : engagement.Generation + 1
    };
    db.EngagementActivations.Add(activation);
    var updated = await db.Engagements.Where(x => x.FirmId == actor.FirmId && x.Id == engagementId && x.Status == "Draft" && x.Generation == engagement.Generation)
      .ExecuteUpdateAsync(x => x.SetProperty(e => e.Status, "Active").SetProperty(e => e.ProfessionalWorkBlocked, false).SetProperty(e => e.Generation, e => e.Generation + 1), ct);
    if (updated != 1) return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "Engagement changed during activation.");
    // The conversion-time portal intent becomes invitable only now, after acceptance and Partner activation.
    var portalIntent = await db.ClientPortalIntents.SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.PracticeClientId == engagement.PracticeClientId && x.State == ClientPortalIntentStates.AwaitingAcceptance, ct);
    if (portalIntent is not null)
    {
      portalIntent.ActivatedEngagementId = engagementId;
      portalIntent.UpdatedAt = activation.ActivatedAt;
    }
    await db.SaveChangesAsync(ct);
    await ClientPortalService.RefreshCommercialIntentAsync(db, actor.FirmId, engagement.PracticeClientId, ct);
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, stub.PracticeClientId, engagementId, RequiredRoles: ActivationRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (await db.EngagementHolds.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && !x.Released, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An unreleased hold blocks activation.");
    var currentDecision = await db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.PracticeClientId == engagement.PracticeClientId && x.ServiceRoute == engagement.ServiceRoute && x.Decision != "Pending")
      .OrderByDescending(x => x.Generation).ThenByDescending(x => x.DecidedAt).ThenByDescending(x => x.Id).Select(x => x.Id).FirstOrDefaultAsync(ct);
    if (currentDecision != decision.Id) return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "Acceptance changed during activation.");
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(activation.Id);
  }

  private static bool ValidPeriod(string start, string end) =>
    DateOnly.TryParseExact((start ?? string.Empty).Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var s) &&
    DateOnly.TryParseExact((end ?? string.Empty).Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var e) && s <= e;
}
