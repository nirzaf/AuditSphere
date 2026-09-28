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
  string? LastAttemptState, DateTimeOffset? LastAttemptAt,
  string DirectoryCapabilityState, DateTimeOffset? DirectoryLastCheckedAt);

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
    IntegrationVerificationEvidence? directoryCheck = null;
    if (draft is not null)
    {
      if (draft.ConnectionRevisionId is { } revisionId)
        connection = await db.Microsoft365ConnectionRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
          x.Id == revisionId && x.FirmId == actor.FirmId, ct);
      attempt = await db.TenantConsentAttempts.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.SetupDraftId == draft.Id)
        .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
      if (connection is not null)
        directoryCheck = await db.IntegrationVerificationEvidences.AsNoTracking()
          .Where(x => x.FirmId == actor.FirmId && x.SetupDraftId == draft.Id &&
            x.ConnectionRevisionId == connection.Id && x.ResourceKind == "DIRECTORY" &&
            x.Operation == "EXACT_USER_READ" && x.ResourceId == connection.TenantId)
          .OrderByDescending(x => x.ObservedAt).ThenByDescending(x => x.Id)
          .FirstOrDefaultAsync(ct);
    }
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return Denied();
    var directoryState = directoryCheck is null ? "NOT_VERIFIED" :
      directoryCheck.Result != "PASS" ? "BLOCKED" :
      directoryCheck.ObservedAt < DateTimeOffset.UtcNow.AddMinutes(-15) ? "STALE" : "VERIFIED_RECENT";
    return CommandResult<TenantConnectionSnapshot>.Ok(new(draft?.Id, draft?.ExpectedTenantId,
      draft?.State, connection?.State, connection?.ConsentState,
      attempt?.State, attempt?.ReturnedAt ?? attempt?.CreatedAt,
      directoryState, directoryCheck?.ObservedAt));
  }

  private static CommandResult<TenantConnectionSnapshot> Denied() =>
    CommandResult<TenantConnectionSnapshot>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
}
