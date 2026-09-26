namespace AuditSphereOps.Web.Authentication;

/// <summary>
/// Allows the exact deployment-approved first administrator to reach proof-backed setup
/// before a local user exists. This does not create an actor or a role grant.
/// </summary>
public static class InitialAdministratorSignIn
{
  public static bool AllowsUnmappedIdentity(IConfiguration configuration, string? tenantId, string? objectId)
  {
    var proofHash = configuration["Setup:BootstrapProofHash"];
    return Guid.TryParse(configuration["Setup:FirmId"] ?? configuration["Application:FirmId"], out var firmId) &&
      firmId != Guid.Empty &&
      !string.IsNullOrWhiteSpace(configuration["Setup:InstallationId"] ?? configuration["Application:InstallationId"]) &&
      proofHash is { Length: 64 } && proofHash.All(Uri.IsHexDigit) &&
      Guid.TryParse(configuration["Setup:InitialAdministratorTenantId"], out var approvedTenant) &&
      Guid.TryParse(configuration["Setup:InitialAdministratorObjectId"], out var approvedObject) &&
      Guid.TryParse(tenantId, out var tokenTenant) &&
      Guid.TryParse(objectId, out var tokenObject) &&
      tokenTenant == approvedTenant && tokenObject == approvedObject;
  }
}
