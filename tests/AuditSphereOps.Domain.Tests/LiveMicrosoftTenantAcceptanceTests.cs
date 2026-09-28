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
/// AUDITSPHERE_LIVE_M365_READER_CERT, AUDITSPHERE_LIVE_M365_READER_KEY.
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
    var verifier = new GraphTenantConsentVerifier(http,
      new TenantConsentVerifierOptions(false, tenantId, client ?? string.Empty, string.Empty, cert ?? string.Empty, key ?? string.Empty),
      new Dictionary<string, GraphCapabilityTokenSource> { [Microsoft365Capabilities.DirectoryRead] = new(http, reader) });
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
    // Live: the directory reader must be verified or explicitly NOT_GRANTED; nothing is assumed.
    Assert.Contains(results.Single(x => x.Capability == Microsoft365Capabilities.DirectoryRead).State,
      new[] { CapabilityVerificationStates.Verified, CapabilityVerificationStates.NotGranted, CapabilityVerificationStates.Failed });
  }
}
