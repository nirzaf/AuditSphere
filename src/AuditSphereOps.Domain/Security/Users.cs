// Security: local user/session model backing the circuit actor + current-session epoch (§§7–8, 28.4).
// Entra remains the external authority; these rows bind claims to local authorization state.
namespace AuditSphereOps.Domain.Security;

/// <summary>Staff or client user identity. Immutable external subject; mutable local lifecycle only.</summary>
public sealed class AppUser
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string Subject { get; set; } = string.Empty;   // Entra oid / B2B sub
  public string TenantId { get; set; } = string.Empty;
  public string Email { get; set; } = string.Empty;
  public string DisplayName { get; set; } = string.Empty;
  public string UserKind { get; set; } = "Staff";        // Staff | Client
  public bool Disabled { get; set; }
  public long SessionEpoch { get; set; } = 1;           // bumped on disable/role change
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Role grant scoped to firm/client/engagement (§27.2, §42.5). Append-only.</summary>
public sealed class RoleGrant
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid UserId { get; set; }
  public string Role { get; set; } = string.Empty;      // Administrator|Partner|Manager|Staff|ClientUser...
  public Guid? ClientId { get; set; }
  public Guid? EngagementId { get; set; }
  public DateTimeOffset GrantedAt { get; set; }
  public Guid GrantedByUserId { get; set; }
  public DateTimeOffset? RevokedAt { get; set; }
}
