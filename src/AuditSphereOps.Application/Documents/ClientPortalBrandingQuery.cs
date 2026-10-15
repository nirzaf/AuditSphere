using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

public sealed record PortalBrandingView(Guid? ProfileId, string Version,
  string LegalName, string AccentColorHex, bool HasLogo, string? LogoSha256Hex);

public sealed record PortalBrandingLogo(byte[] Bytes, string Sha256Hex);

public static class ClientPortalBrandingQuery
{
  private const string UnavailableMessage = "Portal branding unavailable.";
  private const string InvalidBrandingMessage = "The firm branding record cannot be verified.";

  public static async Task<CommandResult<PortalBrandingView>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    if (!await PortalAccessAsync(db, actor, ct)) return Denied<PortalBrandingView>();

    var profile = await LatestProfile(db, actor.FirmId).AsNoTracking()
      .FirstOrDefaultAsync(ct);
    PortalBrandingView view;
    long? version;
    if (profile is null)
    {
      view = new(null, "0", "AuditSphere", "#2B6CB0", false, null);
      version = null;
    }
    else
    {
      if (!System.Text.RegularExpressions.Regex.IsMatch(profile.AccentColorHex, "^#[0-9A-Fa-f]{6}$") ||
          !LogoPairIsConsistent(profile.LogoPngBytes, profile.LogoSha256Hex))
        return CommandResult<PortalBrandingView>.Fail(ErrorCodes.GateBlocked, InvalidBrandingMessage);
      version = profile.Version;
      view = new(profile.Id, profile.Version.ToString(CultureInfo.InvariantCulture), profile.LegalName,
        profile.AccentColorHex, profile.LogoPngBytes is not null && profile.LogoSha256Hex is not null,
        profile.LogoSha256Hex);
    }

    if (!await PortalAccessAsync(db, actor, ct)) return Denied<PortalBrandingView>();
    if (!await CurrentProfileMatchesAsync(db, actor.FirmId, view.ProfileId, version, ct))
      return CommandResult<PortalBrandingView>.Fail(ErrorCodes.StaleRevision, "Firm branding changed. Refresh the portal.");
    return CommandResult<PortalBrandingView>.Ok(view);
  }

  public static async Task<CommandResult<PortalBrandingLogo>> LogoAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid profileId, CancellationToken ct = default)
  {
    if (!await PortalAccessAsync(db, actor, ct)) return Denied<PortalBrandingLogo>();
    if (profileId == Guid.Empty) return Denied<PortalBrandingLogo>();

    var current = await LatestProfile(db, actor.FirmId).AsNoTracking()
      .FirstOrDefaultAsync(ct);
    if (current is null || current.Id != profileId || current.LogoPngBytes is null || current.LogoSha256Hex is null)
      return CommandResult<PortalBrandingLogo>.Fail(ErrorCodes.StaleRevision, "Firm branding changed. Refresh the portal.");

    if (!LogoPairIsConsistent(current.LogoPngBytes, current.LogoSha256Hex) ||
        !string.Equals(Hashing.Sha256Hex(current.LogoPngBytes), current.LogoSha256Hex, StringComparison.Ordinal))
      return CommandResult<PortalBrandingLogo>.Fail(ErrorCodes.GateBlocked, "The firm logo cannot be verified.");

    var expectedVersion = current.Version;
    var bytes = current.LogoPngBytes.ToArray();
    var hash = current.LogoSha256Hex;
    if (!await PortalAccessAsync(db, actor, ct)) return Denied<PortalBrandingLogo>();
    if (!await CurrentProfileMatchesAsync(db, actor.FirmId, profileId, expectedVersion, ct))
      return CommandResult<PortalBrandingLogo>.Fail(ErrorCodes.StaleRevision, "Firm branding changed. Refresh the portal.");
    return CommandResult<PortalBrandingLogo>.Ok(new(bytes, hash));
  }

  private static IQueryable<AuditSphereOps.Domain.Practice.FirmCommercialProfile> LatestProfile(
    IAuditSphereDbContext db, Guid firmId) => db.FirmCommercialProfiles
      .Where(x => x.FirmId == firmId)
      .OrderByDescending(x => x.Version)
      .ThenByDescending(x => x.Id);

  private static bool LogoPairIsConsistent(byte[]? bytes, string? hash) =>
    bytes is null && hash is null ||
    bytes is { Length: >= 1 and <= 131072 } && hash is { Length: 64 } &&
      System.Text.RegularExpressions.Regex.IsMatch(hash, "^[0-9a-f]{64}$");

  private static async Task<bool> CurrentProfileMatchesAsync(IAuditSphereDbContext db, Guid firmId,
    Guid? profileId, long? version, CancellationToken ct)
  {
    var current = await LatestProfile(db, firmId).AsNoTracking()
      .Select(x => new { x.Id, x.Version })
      .FirstOrDefaultAsync(ct);
    return current?.Id == profileId && current?.Version == version;
  }

  private static async Task<bool> PortalAccessAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    (await ClientPortalWorkspaceQuery.GetAsync(db, actor, 0, 10, ct)).Succeeded;

  private static CommandResult<T> Denied<T>() => CommandResult<T>.Fail(ErrorCodes.ScopeDenied, UnavailableMessage);
}
