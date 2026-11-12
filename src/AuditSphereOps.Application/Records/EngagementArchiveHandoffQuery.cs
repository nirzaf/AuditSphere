using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Records;

public sealed record EngagementArchiveChoice(Guid ArchiveId, string ProfileId, long ProfileVersion, string Status);
public sealed record EngagementArchiveProfile(string ProfileId, long ProfileVersion);
public sealed record EngagementArchiveHandoff(Guid EngagementId, Guid ClientId,
  IReadOnlyList<EngagementArchiveChoice> Archives, IReadOnlyList<EngagementArchiveProfile> Profiles,
  bool CanCreate, string? BlockedReason);

/// <summary>Read-only handoff. Creation remains the explicit EnsureArchive command.</summary>
public static class EngagementArchiveHandoffQuery
{
  private static readonly string[] ReadRoles =
    ["Administrator", "Partner", "Manager", "EngagementLeader", "Senior", "Staff", "Auditor", "RecordsCustodian"];

  internal static AuthorizationRequest ReadRequest(ActorContext actor, Guid clientId, Guid engagementId) =>
    new(actor.FirmId, clientId, engagementId, RequiredRoles: ReadRoles, InternalOnly: true);

  public static async Task<CommandResult<EngagementArchiveHandoff>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null)
      return CommandResult<EngagementArchiveHandoff>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var request = ReadRequest(actor, engagement.PracticeClientId, engagementId);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!auth.Succeeded)
      return CommandResult<EngagementArchiveHandoff>.Fail(auth.ErrorCode!, auth.Message!);
    var archives = await db.Archives.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.ClientId == engagement.PracticeClientId && x.EngagementId == engagementId)
      .OrderBy(x => x.ProfileId).ThenBy(x => x.ProfileVersion).ThenBy(x => x.Id)
      .Select(x => new EngagementArchiveChoice(x.Id, x.ProfileId, x.ProfileVersion, x.Status)).ToListAsync(ct);
    List<EngagementArchiveProfile> profiles = [];
    string? blocked = null;
    if (archives.Count == 0)
    {
      var create = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId,
          RequiredRoles: ["Administrator", "Partner", "RecordsCustodian"], InternalOnly: true), ct);
      if (!create.Succeeded) blocked = "An authorized records custodian must prepare this archive.";
      else if (engagement.Status is not ("Completed" or "Closed"))
        blocked = "The engagement must be issued and completed before an archive can be prepared.";
      else
      {
        profiles = await db.RecordsProfiles.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
            x.Approved && x.ServiceRoute == engagement.ServiceRoute)
          .OrderBy(x => x.ProfileCode).ThenBy(x => x.Version)
          .Select(x => new EngagementArchiveProfile(x.ProfileCode, x.Version)).ToListAsync(ct);
        if (profiles.Count == 0) blocked = "No approved records profile is available for this engagement route.";
      }
    }
    var current = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!current.Succeeded)
      return CommandResult<EngagementArchiveHandoff>.Fail(current.ErrorCode!, current.Message!);
    return CommandResult<EngagementArchiveHandoff>.Ok(new(engagementId, engagement.PracticeClientId,
      archives, profiles, archives.Count == 0 && profiles.Count > 0, blocked));
  }
}
