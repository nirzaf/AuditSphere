using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Infrastructure.Providers;
using Xunit.Abstractions;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// Separately controlled live acceptance for tenant capability verification (read-only; it never creates
/// users, invitations or memberships). Without live credentials it must report BLOCKED_EXTERNAL for every
/// capability — never a fake pass. Live inputs are private environment variables, never repository files:
/// AUDITSPHERE_LIVE_M365_TENANT_ID, AUDITSPHERE_LIVE_M365_READER_CLIENT_ID,
/// AUDITSPHERE_LIVE_M365_READER_CERT, AUDITSPHERE_LIVE_M365_READER_KEY, and optionally the
/// AUDITSPHERE_LIVE_M365_ADMIN_* and AUDITSPHERE_LIVE_M365_MAIL_* CLIENT_ID/CERT/KEY triples.
/// </summary>
[Trait("Category", "LiveMicrosoft")]
public sealed class LiveMicrosoftTenantAcceptanceTests(ITestOutputHelper output)
{
  [Fact]
  public async Task CapabilityVerification_IsBlockedExternalUnlessLiveCredentialsAreSupplied()
  {
    var tenant = Environment.GetEnvironmentVariable("AUDITSPHERE_LIVE_M365_TENANT_ID");
    var client = Environment.GetEnvironmentVariable("AUDITSPHERE_LIVE_M365_READER_CLIENT_ID");
    var cert = Environment.GetEnvironmentVariable("AUDITSPHERE_LIVE_M365_READER_CERT");
    var key = Environment.GetEnvironmentVariable("AUDITSPHERE_LIVE_M365_READER_KEY");
    var live = Guid.TryParse(tenant, out _) && Guid.TryParse(client, out _) && File.Exists(cert) && File.Exists(key);
    var tenantId = live ? tenant! : Guid.NewGuid().ToString("D");
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var reader = new GraphCapabilityCredentialOptions(live, tenantId, client ?? string.Empty, cert ?? string.Empty, key ?? string.Empty, "User.Read.All");
    var tokens = new Dictionary<string, GraphCapabilityTokenSource> { [Microsoft365Capabilities.DirectoryRead] = new(http, reader) };
    // Optional live credentials for the shared tenant-administration identity and the mail identity.
    var adminRoles = new HashSet<string>(StringComparer.Ordinal) { "User.Create", "User.Invite.All", "GroupMember.ReadWrite.All" };
    GraphCapabilityCredentialOptions? Optional(string prefix, string role, IReadOnlySet<string>? approved = null)
    {
      var id = Environment.GetEnvironmentVariable($"AUDITSPHERE_LIVE_M365_{prefix}_CLIENT_ID");
      var c = Environment.GetEnvironmentVariable($"AUDITSPHERE_LIVE_M365_{prefix}_CERT");
      var k = Environment.GetEnvironmentVariable($"AUDITSPHERE_LIVE_M365_{prefix}_KEY");
      return live && Guid.TryParse(id, out _) && File.Exists(c) && File.Exists(k)
        ? new(true, tenantId, id!, c!, k!, role, approved) : null;
    }
    foreach (var (capability, prefix, role, approved) in new (string, string, string, IReadOnlySet<string>?)[]
             {
               (Microsoft365Capabilities.TenantUserProvisioning, "ADMIN", "User.Create", adminRoles),
               (Microsoft365Capabilities.GuestInvitation, "ADMIN", "User.Invite.All", adminRoles),
               (Microsoft365Capabilities.GroupMembership, "ADMIN", "GroupMember.ReadWrite.All", adminRoles),
               (Microsoft365Capabilities.OutboundMail, "MAIL", "Mail.Send", null),
             })
      if (Optional(prefix, role, approved) is { } credential) tokens[capability] = new(http, credential);
    var verifier = new GraphTenantConsentVerifier(http,
      new TenantConsentVerifierOptions(false, tenantId, client ?? string.Empty, string.Empty, cert ?? string.Empty, key ?? string.Empty),
      tokens);
    var probes = Microsoft365PermissionMatrix.Rows.Where(x => x.Capability != Microsoft365Capabilities.SignIn)
      .Select(x => new CapabilityProbe(x.Capability, x.Permission)).ToList();
    var results = await verifier.VerifyCapabilitiesAsync(tenantId, probes, default);
    foreach (var result in results) output.WriteLine($"{result.Capability}: {result.State} ({result.DiagnosticCode})");
    Assert.Equal(probes.Count, results.Count);
    if (!live)
    {
      output.WriteLine("BLOCKED_EXTERNAL: live Microsoft credentials and tenant consent are not available to this run.");
      Assert.All(results, x => Assert.Equal(CapabilityVerificationStates.BlockedExternal, x.State));
      return;
    }
    // Live: every capability with supplied credentials must verify; the rest stay BLOCKED_EXTERNAL.
    foreach (var result in results)
      Assert.Equal(tokens.ContainsKey(result.Capability) ? CapabilityVerificationStates.Verified : CapabilityVerificationStates.BlockedExternal,
        result.State);
  }
}
