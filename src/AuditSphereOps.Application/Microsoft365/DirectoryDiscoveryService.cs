using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Microsoft365;

public sealed record DirectoryCandidate(string TenantId, string ObjectId, string DisplayName,
  string UserPrincipalName, bool AccountEnabled, string UserType);
public sealed record DirectoryCandidatePage(IReadOnlyList<DirectoryCandidate> Users, string? NextPageToken);

/// <summary>Read-only, bounded discovery. A result is never an AuditSphere grant.</summary>
public interface IMicrosoftDirectoryReader
{
  Task<DirectoryCandidatePage> SearchAsync(string tenantId, string prefix,
    string? pageToken, CancellationToken ct);
}

public static class DirectoryDiscoveryService
{
  public static async Task<CommandResult<DirectoryCandidatePage>> SearchAsync(
    IAuditSphereDbContext db, ActorContext actor, IMicrosoftDirectoryReader reader,
    string configuredTenantId, string prefix, string? pageToken, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"],
      InternalOnly: true, RequireFirmWide: true);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return Denied();
    if (!Guid.TryParse(configuredTenantId, out var tenant) ||
        string.IsNullOrWhiteSpace(prefix) || prefix.Trim().Length < 2 ||
        prefix.Length > 80 || prefix.Any(char.IsControl) ||
        pageToken is { Length: > 2048 })
      return CommandResult<DirectoryCandidatePage>.Fail(ErrorCodes.GateBlocked,
        "Enter at least two characters to search the configured directory.");
    DirectoryCandidatePage page;
    try
    {
      page = await reader.SearchAsync(tenant.ToString("D"), prefix.Trim(), pageToken, ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
      return CommandResult<DirectoryCandidatePage>.Fail(ErrorCodes.GateBlocked,
        "Directory access is unavailable or not verified for this tenant.");
    }
    if (page.Users.Count > 25 || page.Users.Any(x =>
          !string.Equals(x.TenantId, tenant.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
          !Guid.TryParse(x.ObjectId, out _)))
      return CommandResult<DirectoryCandidatePage>.Fail(ErrorCodes.GateBlocked,
        "Directory response could not be verified.");
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return Denied();
    return CommandResult<DirectoryCandidatePage>.Ok(page);
  }

  private static CommandResult<DirectoryCandidatePage> Denied() =>
    CommandResult<DirectoryCandidatePage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
}
