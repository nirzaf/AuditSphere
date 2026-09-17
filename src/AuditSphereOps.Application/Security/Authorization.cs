// Authorization helper: scope check + protected-state + holds evaluated per operation (§§8.1, 8.3, 8.6).
namespace AuditSphereOps.Application.Security;

using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Shared;

public static class Authorization
{
  public static CommandResult RequireFirmScope(ActorContext actor, Guid firmId) =>
    actor.FirmId == firmId
      ? CommandResult.Ok()
      : CommandResult.Fail(ErrorCodes.ScopeDenied, "Cross-firm access denied.");

  public static CommandResult RequireRole(ActorContext actor, params string[] roles) =>
    roles.Any(r => actor.Roles.Contains(r, StringComparer.OrdinalIgnoreCase))
      ? CommandResult.Ok()
      : CommandResult.Fail(ErrorCodes.ScopeDenied, $"Requires role: {string.Join(',', roles)}.");

  /// <summary>Client users are denied internal targets (review points, full exports) per AT-24.</summary>
  public static CommandResult RequireStaff(ActorContext actor) =>
    actor.Roles.Contains("ClientUser", StringComparer.OrdinalIgnoreCase)
      ? CommandResult.Fail(ErrorCodes.ScopeDenied, "Client users cannot access internal review state.")
      : CommandResult.Ok();
}
