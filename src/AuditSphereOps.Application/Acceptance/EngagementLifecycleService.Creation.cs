using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

public static partial class EngagementLifecycleService
{
  private static readonly string[] DraftRoles = ["Partner", "Manager"];
  private static Task<CommandResult> AuthorizeDraftAsync(IAuditSphereDbContext db, ActorContext actor, Guid clientId, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, clientId, RequiredRoles: DraftRoles, InternalOnly: true), ct);

  public static async Task<CommandResult<Guid>> CreateDraftAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateEngagementDraftRequest request, CancellationToken ct = default)
  {
    var route = (request.ServiceRoute ?? string.Empty).Trim();
    var profile = (request.ServiceProfileId ?? string.Empty).Trim();
    if (route.Length is < 2 or > 50 || profile.Length is < 1 or > 100 || !ValidPeriod(request.PeriodStart, request.PeriodEnd))
      return CommandResult<Guid>.Fail("engagement.invalid", "A service route, service profile and a valid period (yyyy-MM-dd, start not after end) are required.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.PracticeClientId, RequiredRoles: DraftRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    // Serialize creation for this client so concurrent retries cannot insert duplicate service-period shells.
    var clientGuard = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE id = {request.PracticeClientId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (clientGuard is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    // Epoch-changing administration waits behind this actor lock. Role scope is still re-read.
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR SHARE")
      .AsNoTracking().SingleAsync(ct);
    auth = await AuthorizeDraftAsync(db, actor, request.PracticeClientId, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (!await db.PracticeClients.AsNoTracking().AnyAsync(x => x.Id == request.PracticeClientId && x.FirmId == actor.FirmId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var existing = await db.Engagements.AsNoTracking().FirstOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.PracticeClientId == request.PracticeClientId && x.ServiceRoute == route &&
      x.PeriodStart == request.PeriodStart.Trim() && x.PeriodEnd == request.PeriodEnd.Trim(), ct);
    if (existing is not null)
    {
      auth = await AuthorizeDraftAsync(db, actor, request.PracticeClientId, ct);
      if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
      if (existing.ServiceProfileId != profile)
        return CommandResult<Guid>.Fail("engagement.conflict", "This service and period already use another service profile.");
      if (tx is not null) await tx.CommitAsync(ct);
      return CommandResult<Guid>.Ok(existing.Id);
    }
    var engagement = new Engagement
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PracticeClientId = request.PracticeClientId, ServiceRoute = route,
      PeriodStart = request.PeriodStart.Trim(), PeriodEnd = request.PeriodEnd.Trim(), ServiceProfileId = profile,
      Status = "Draft", ProfessionalWorkBlocked = true, CreatedAt = DateTimeOffset.UtcNow
    };
    db.Engagements.Add(engagement);
    await db.SaveChangesAsync(ct);
    auth = await AuthorizeDraftAsync(db, actor, request.PracticeClientId, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(engagement.Id);
  }

}
