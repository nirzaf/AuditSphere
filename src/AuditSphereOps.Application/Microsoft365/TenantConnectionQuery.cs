using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

public sealed record TenantConnectionSnapshot(
  Guid? DraftId, string? ExpectedTenantId, string? DraftState,
  string? ConnectionState, string? ConsentState,
  string? LastAttemptState, DateTimeOffset? LastAttemptAt);

/// <summary>Current firm-scoped tenant setup state; callback return is not a verified grant.</summary>
public static class TenantConnectionQuery
{
  public static async Task<CommandResult<TenantConnectionSnapshot>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"],
      InternalOnly: true, RequireFirmWide: true);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return Denied();
    var draft = await db.Microsoft365SetupDrafts.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
    Microsoft365ConnectionRevision? connection = null;
    TenantConsentAttempt? attempt = null;
    if (draft is not null)
    {
      if (draft.ConnectionRevisionId is { } revisionId)
        connection = await db.Microsoft365ConnectionRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
          x.Id == revisionId && x.FirmId == actor.FirmId, ct);
      attempt = await db.TenantConsentAttempts.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.SetupDraftId == draft.Id)
        .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
    }
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return Denied();
    return CommandResult<TenantConnectionSnapshot>.Ok(new(draft?.Id, draft?.ExpectedTenantId,
      draft?.State, connection?.State, connection?.ConsentState,
      attempt?.State, attempt?.ReturnedAt ?? attempt?.CreatedAt));
  }

  private static CommandResult<TenantConnectionSnapshot> Denied() =>
    CommandResult<TenantConnectionSnapshot>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
}
