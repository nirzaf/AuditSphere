using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Microsoft365;

/// <summary>Re-reads an exact Microsoft object before local binding; binding creates no grant.</summary>
public static class DirectoryUserBindingService
{
  public static async Task<CommandResult<Guid>> BindMemberAsync(
    IAuditSphereDbContext db, ActorContext actor, IMicrosoftDirectoryReader reader,
    string configuredTenantId, Guid objectId, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"],
      InternalOnly: true, RequireFirmWide: true);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return Denied();
    if (!Guid.TryParse(configuredTenantId, out var tenant) || objectId == Guid.Empty)
      return Denied();
    DirectoryCandidate candidate;
    try { candidate = await reader.GetByIdAsync(tenant.ToString("D"), objectId.ToString("D"), ct); }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "Microsoft identity could not be verified. No local access was changed.");
    }
    if (!string.Equals(candidate.TenantId, tenant.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(candidate.ObjectId, objectId.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
        !candidate.AccountEnabled || candidate.UserType != "Member" ||
        string.IsNullOrWhiteSpace(candidate.DisplayName) ||
        string.IsNullOrWhiteSpace(candidate.UserPrincipalName))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "Microsoft identity is disabled, changed, or not a workforce member. No local access was changed.");
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return Denied();
    return await RoleAdministrationService.EnsureVerifiedDirectoryUserAsync(db, actor,
      new(tenant.ToString("D"), objectId.ToString("D"), candidate.UserPrincipalName,
        candidate.DisplayName, "Staff", "GRAPH"), ct);
  }

  /// <summary>
  /// Binds an existing B2B guest as a Client identity. Binding grants nothing; a ClientUser role
  /// with CLIENT or ENGAGEMENT scope must be applied separately.
  /// </summary>
  public static async Task<CommandResult<Guid>> BindExistingGuestAsync(
    IAuditSphereDbContext db, ActorContext actor, IMicrosoftDirectoryReader reader,
    string configuredTenantId, Guid objectId, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"],
      InternalOnly: true, RequireFirmWide: true);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded ||
        !Guid.TryParse(configuredTenantId, out var tenant) || objectId == Guid.Empty)
      return Denied();
    DirectoryCandidate candidate;
    try { candidate = await reader.GetByIdAsync(tenant.ToString("D"), objectId.ToString("D"), ct); }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Microsoft identity could not be verified. No local access was changed.");
    }
    if (!string.Equals(candidate.TenantId, tenant.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(candidate.ObjectId, objectId.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
        !candidate.AccountEnabled || candidate.UserType != "Guest" ||
        string.IsNullOrWhiteSpace(candidate.DisplayName) || string.IsNullOrWhiteSpace(candidate.UserPrincipalName))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "Microsoft identity is disabled, changed, or not a guest. No local access was changed.");
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return Denied();
    return await RoleAdministrationService.EnsureVerifiedDirectoryUserAsync(db, actor,
      new(tenant.ToString("D"), objectId.ToString("D"), GuestContact(candidate.UserPrincipalName),
        candidate.DisplayName, "Client", "GRAPH"), ct);
  }

  /// <summary>Guest UPNs look like name_domain#EXT#@tenant; use the original address as contact data only.</summary>
  private static string GuestContact(string upn)
  {
    var marker = upn.IndexOf("#EXT#", StringComparison.OrdinalIgnoreCase);
    if (marker <= 0) return upn;
    var local = upn[..marker];
    var split = local.LastIndexOf('_');
    return split > 0 ? local[..split] + "@" + local[(split + 1)..] : upn;
  }

  private static CommandResult<Guid> Denied() =>
    CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
}
