using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Security;

public sealed record FirmWidePartnerRow(Guid UserId, string DisplayName, DateTimeOffset? ExpiresAt);
public sealed record FirmWidePartnerPage(IReadOnlyList<FirmWidePartnerRow> Rows, Guid? AfterUserId);

public static partial class FirmAdministrationQuery
{
  /// <summary>Returns only active staff with an effective, unscoped Partner grant in the actor's firm.
  /// Presentation adapters must bind the internal continuation ID to their actor, firm, and query.</summary>
  public static async Task<CommandResult<FirmWidePartnerPage>> ListActiveFirmWidePartnersAsync(
    IAuditSphereDbContext db, ActorContext actor, int pageSize = 25, Guid? afterUserId = null,
    CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"],
      InternalOnly: true, RequireFirmWide: true);
    var authorization = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!authorization.Succeeded)
      return CommandResult<FirmWidePartnerPage>.Fail(authorization.ErrorCode!, authorization.Message!);
    if (pageSize is < 1 or > 50 || afterUserId == Guid.Empty)
      return CommandResult<FirmWidePartnerPage>.Fail("request.invalid", "Invalid page size or continuation.");

    var now = DateTimeOffset.UtcNow;
    var partners = from user in db.Users.AsNoTracking()
                   join grant in db.RoleGrants.AsNoTracking() on new { user.FirmId, UserId = user.Id }
                     equals new { grant.FirmId, grant.UserId }
                   where user.FirmId == actor.FirmId && !user.Disabled && user.UserKind == "Staff" &&
                     grant.Role == "Partner" && grant.ClientId == null && grant.EngagementId == null &&
                     grant.RevokedAt == null && (grant.ExpiresAt == null || grant.ExpiresAt > now)
                   select new { user.Id, user.DisplayName, grant.ExpiresAt };
    if (afterUserId is { } after)
      partners = partners.Where(x => x.Id.CompareTo(after) > 0);
    var rows = await partners.OrderBy(x => x.Id).Take(pageSize + 1)
      .Select(x => new FirmWidePartnerRow(x.Id, x.DisplayName, x.ExpiresAt)).ToListAsync(ct);

    authorization = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!authorization.Succeeded)
      return CommandResult<FirmWidePartnerPage>.Fail(authorization.ErrorCode!, authorization.Message!);
    var hasNext = rows.Count > pageSize;
    if (hasNext) rows.RemoveAt(pageSize);
    return CommandResult<FirmWidePartnerPage>.Ok(new(rows, hasNext ? rows[^1].UserId : null));
  }
}
