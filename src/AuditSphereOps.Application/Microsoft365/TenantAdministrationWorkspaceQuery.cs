using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

public sealed record TenantConsentIdentity(string TenantId, string ObjectId, DateTimeOffset? VerifiedAt);
public sealed record TenantAdministrationWorkspace(TenantConnectionSnapshot Connection,
  IReadOnlyList<CapabilityStatus> Capabilities, Microsoft365SetupProgress? SetupProgress,
  TenantConsentIdentity? ConsentingAdministrator, string? DraftRevision);

/// <summary>Safe, firm-scoped consent metadata. No callback, code, nonce or credential reference is exposed.</summary>
public static class TenantAdministrationWorkspaceQuery
{
  public static async Task<CommandResult<TenantAdministrationWorkspace>> GetAsync(IAuditSphereDbContext db,
    ActorContext actor, TenantAdministrationOptions options, DateTimeOffset now, CancellationToken ct = default)
  {
    var connection = await TenantConnectionQuery.GetAsync(db, actor, ct);
    if (!connection.Succeeded) return CommandResult<TenantAdministrationWorkspace>.Fail(connection.ErrorCode!, connection.Message!);
    var snapshot = connection.Value!;
    var capability = await TenantCapabilityService.StatusesAsync(db, actor.FirmId, options, now, ct);
    Microsoft365SetupProgress? progress = null;
    TenantConsentIdentity? identity = null;
    string? draftRevision = null;
    if (snapshot.DraftId is { } draftId)
    {
      var revision = await db.Microsoft365SetupDrafts.AsNoTracking().Where(x => x.Id == draftId && x.FirmId == actor.FirmId)
        .Select(x => (long?)x.Revision).SingleOrDefaultAsync(ct);
      draftRevision = revision?.ToString(System.Globalization.CultureInfo.InvariantCulture);
      var setup = await Microsoft365SetupProgressQuery.GetAsync(db, actor, draftId, ct);
      if (!setup.Succeeded) return CommandResult<TenantAdministrationWorkspace>.Fail(setup.ErrorCode!, setup.Message!);
      progress = setup.Value;
      identity = await db.TenantConsentAttempts.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
          x.SetupDraftId == draftId && x.ExpectedTenantId == options.TenantId && x.State == TenantConsentAttemptStates.ConsentVerified &&
          x.ConsentingTenantId != null && x.ConsentingObjectId != null)
        .OrderByDescending(x => x.ConsentVerifiedAt).ThenByDescending(x => x.Id)
        .Select(x => new TenantConsentIdentity(x.ConsentingTenantId!, x.ConsentingObjectId!, x.ConsentVerifiedAt))
        .FirstOrDefaultAsync(ct);
    }
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      return TenantAdministration.Denied<TenantAdministrationWorkspace>();
    return CommandResult<TenantAdministrationWorkspace>.Ok(new(snapshot, capability, progress, identity, draftRevision));
  }
}
