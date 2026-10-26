using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Security;

public sealed record UserProfileView(Guid UserId, string UserType, IReadOnlyList<string> Roles, string FullName, string Email,
  string? Position, string? Gender, DateOnly? DateOfBirth, string? MobileNumber, DateOnly? JoinedDate, long Revision,
  DateTimeOffset? UpdatedAt);

/// <summary>Optional details only. <see cref="ExpectedRevision"/> is 0 before the first save.</summary>
public sealed record UpdateUserProfileRequest(string? Position, string? Gender, DateOnly? DateOfBirth, string? MobileNumber,
  DateOnly? JoinedDate, long ExpectedRevision);

/// <summary>
/// The signed-in user's own profile. Full name and email come from the identity provider and are read-only here; the other
/// details are optional and only the user can change them. No request can read or change another user's profile.
/// </summary>
public static class UserProfileService
{
  public const int FullNameMaxLength = 200;

  public static async Task<CommandResult<UserProfileView>> GetAsync(IAuditSphereDbContext db, ActorContext actor,
    CancellationToken ct = default)
  {
    var user = await CurrentUserAsync(db, actor, ct);
    if (user is null) return CommandResult<UserProfileView>.Fail(ErrorCodes.ScopeDenied, "Your profile is unavailable.");
    var profile = await db.UserProfiles.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId, ct);
    return CommandResult<UserProfileView>.Ok(new UserProfileView(user.Id, user.UserKind,
      actor.Roles.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), user.DisplayName, user.Email,
      profile?.Position, profile?.Gender, profile?.DateOfBirth, profile?.MobileNumber, profile?.JoinedDate,
      profile?.Revision ?? 0, profile?.UpdatedAt));
  }

  public static async Task<CommandResult<UserProfileView>> UpdateAsync(IAuditSphereDbContext db, ActorContext actor,
    UpdateUserProfileRequest request, CancellationToken ct = default)
  {
    // The latest calendar date anywhere, so a user ahead of UTC can still enter their own today.
    var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(14));
    var (details, error) = UserProfileRules.Normalize(new UserProfileDetails(request.Position, request.Gender,
      request.DateOfBirth, request.MobileNumber, request.JoinedDate), today);
    if (error is not null) return CommandResult<UserProfileView>.Fail("request.invalid", error);
    if (request.ExpectedRevision < 0) return CommandResult<UserProfileView>.Fail("request.invalid", "Refresh your profile and try again.");
    if (await CurrentUserAsync(db, actor, ct) is null)
      return CommandResult<UserProfileView>.Fail(ErrorCodes.ScopeDenied, "Your profile is unavailable.");

    var now = DateTimeOffset.UtcNow;
    var stale = CommandResult<UserProfileView>.Fail(ErrorCodes.StaleRevision,
      "Your profile changed in another window. Reload it and apply your changes again.");
    if (request.ExpectedRevision == 0)
    {
      if (await db.UserProfiles.AnyAsync(x => x.UserId == actor.UserId, ct)) return stale;
      db.UserProfiles.Add(new UserProfile
      {
        UserId = actor.UserId, FirmId = actor.FirmId, Position = details.Position, Gender = details.Gender,
        DateOfBirth = details.DateOfBirth, MobileNumber = details.MobileNumber, JoinedDate = details.JoinedDate,
        Revision = 1, UpdatedAt = now
      });
      try { await db.SaveChangesAsync(ct); }
      catch (DbUpdateException) { return stale; }
    }
    else if (await db.UserProfiles.Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId &&
               x.Revision == request.ExpectedRevision)
             .ExecuteUpdateAsync(x => x
               .SetProperty(p => p.Position, details.Position).SetProperty(p => p.Gender, details.Gender)
               .SetProperty(p => p.DateOfBirth, details.DateOfBirth).SetProperty(p => p.MobileNumber, details.MobileNumber)
               .SetProperty(p => p.JoinedDate, details.JoinedDate).SetProperty(p => p.Revision, p => p.Revision + 1)
               .SetProperty(p => p.UpdatedAt, now), ct) == 0)
      return stale;
    return await GetAsync(db, actor, ct);
  }

  /// <summary>The identity provider's display name to store at sign-in, or null to keep the stored name.</summary>
  public static string? SignInFullName(string? claim)
  {
    var name = claim?.Trim();
    return string.IsNullOrEmpty(name) || name.Length > FullNameMaxLength || name.Any(char.IsControl) ? null : name;
  }

  private static Task<AppUser?> CurrentUserAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId &&
      x.SessionEpoch == actor.SessionEpoch && !x.Disabled, ct);
}
