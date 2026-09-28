using System.Collections.Concurrent;
using System.Security.Cryptography;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Microsoft365;

namespace AuditSphereOps.Infrastructure.Providers;

public sealed record SimulatedDirectoryUser(string ObjectId, string DisplayName, string UserPrincipalName,
  string UserType = "Member", bool AccountEnabled = true, string? Mail = null);

public sealed record SimulatedDirectoryGroup(string ObjectId, string DisplayName, bool IsAssignableToRole = false,
  bool DynamicMembership = false);

/// <summary>
/// In-memory Microsoft tenant for Development/Test only (AllowSimulationAdapters). It lets CI exercise
/// consent, directory, provisioning, invitation and group workflows without live credentials. Startup
/// refuses it outside Development/Test. Test hooks: a UPN or email starting with "unknown." is created
/// but reported as an UNKNOWN outcome, and "reject." is rejected, so recovery paths are testable.
/// </summary>
public sealed class SimulatedMicrosoftTenant : IMicrosoftDirectoryReader, IMicrosoftDirectoryUserProvisioner,
  IMicrosoftGuestInvitationProvider, IMicrosoftGroupMembershipProvider, IMicrosoftTenantConsentVerifier
{
  private readonly string tenantId;
  private readonly ConcurrentDictionary<string, DirectoryUserRecord> users = new(StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<string, SimulatedDirectoryGroup> groups = new(StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> members = new(StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<string, ConsentingAdministrator> codes = new(StringComparer.Ordinal);
  private readonly HashSet<string> notGranted;

  public SimulatedMicrosoftTenant(string tenantId, IEnumerable<SimulatedDirectoryUser> seedUsers,
    IEnumerable<SimulatedDirectoryGroup> seedGroups, IEnumerable<string>? notGrantedCapabilities = null)
  {
    this.tenantId = Guid.Parse(tenantId).ToString("D");
    foreach (var user in seedUsers)
      users[Guid.Parse(user.ObjectId).ToString("D")] = new(this.tenantId, Guid.Parse(user.ObjectId).ToString("D"),
        user.DisplayName, user.UserPrincipalName, user.Mail, user.AccountEnabled, user.UserType, DateTimeOffset.UtcNow.AddDays(-30));
    foreach (var group in seedGroups)
    {
      groups[Guid.Parse(group.ObjectId).ToString("D")] = group with { ObjectId = Guid.Parse(group.ObjectId).ToString("D") };
      members[Guid.Parse(group.ObjectId).ToString("D")] = new(StringComparer.OrdinalIgnoreCase);
    }
    notGranted = new HashSet<string>(notGrantedCapabilities ?? [], StringComparer.Ordinal);
  }

  public bool IsConfigured => true;

  private void RequireTenant(string requested)
  {
    if (!Guid.TryParse(requested, out var parsed) || parsed.ToString("D") != tenantId)
      throw new InvalidOperationException("simulated-tenant-mismatch");
  }

  // --- IMicrosoftDirectoryReader
  public Task<DirectoryCandidatePage> SearchAsync(string requestedTenant, string prefix, string? pageToken, CancellationToken ct,
    string? domain = null)
  {
    RequireTenant(requestedTenant);
    var skip = int.TryParse(pageToken, out var parsed) ? parsed : 0;
    var matches = users.Values.Where(x =>
        (x.DisplayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
         x.UserPrincipalName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrEmpty(domain) || x.UserPrincipalName.EndsWith("@" + domain, StringComparison.OrdinalIgnoreCase)))
      .OrderBy(x => x.DisplayName).ToList();
    var page = matches.Skip(skip).Take(25).Select(Candidate).ToList();
    return Task.FromResult(new DirectoryCandidatePage(page, skip + 25 < matches.Count ? (skip + 25).ToString() : null));
  }

  public Task<DirectoryCandidatePage> BrowseActiveAsync(string requestedTenant, string? domain, string? pageToken, CancellationToken ct)
  {
    RequireTenant(requestedTenant);
    var skip = int.TryParse(pageToken, out var parsed) ? parsed : 0;
    var matches = users.Values.Where(x => x.AccountEnabled &&
        (string.IsNullOrEmpty(domain) || x.UserPrincipalName.EndsWith("@" + domain, StringComparison.OrdinalIgnoreCase)))
      .OrderBy(x => x.DisplayName).ToList();
    var page = matches.Skip(skip).Take(25).Select(Candidate).ToList();
    return Task.FromResult(new DirectoryCandidatePage(page, skip + 25 < matches.Count ? (skip + 25).ToString() : null));
  }

  public Task<DirectoryCandidate> GetByIdAsync(string requestedTenant, string objectId, CancellationToken ct)
  {
    RequireTenant(requestedTenant);
    return users.TryGetValue(objectId, out var user)
      ? Task.FromResult(Candidate(user))
      : throw new InvalidOperationException("simulated-user-not-found");
  }

  private static DirectoryCandidate Candidate(DirectoryUserRecord user) =>
    new(user.TenantId, user.ObjectId, user.DisplayName, user.UserPrincipalName, user.AccountEnabled, user.UserType);

  /// <summary>Test hook: simulate an administrator disabling the Microsoft account.</summary>
  public void SetEnabled(string objectId, bool enabled)
  {
    if (users.TryGetValue(objectId, out var user)) users[objectId] = user with { AccountEnabled = enabled };
  }

  // --- IMicrosoftDirectoryUserProvisioner
  public Task<ProviderMutationResult> CreateUserAsync(string requestedTenant, NewDirectoryUser user, CancellationToken ct)
  {
    RequireTenant(requestedTenant);
    if (user.UserPrincipalName.StartsWith("reject.", StringComparison.OrdinalIgnoreCase))
      return Task.FromResult(ProviderMutationResult.Failed("graph-bad-request", Correlation()));
    if (users.Values.Any(x => x.UserPrincipalName.Equals(user.UserPrincipalName, StringComparison.OrdinalIgnoreCase)))
      return Task.FromResult(ProviderMutationResult.Failed("graph-conflict", Correlation()));
    var id = Guid.NewGuid().ToString("D");
    users[id] = new(tenantId, id, user.DisplayName, user.UserPrincipalName, user.UserPrincipalName, user.AccountEnabled,
      "Member", DateTimeOffset.UtcNow);
    return Task.FromResult(user.UserPrincipalName.StartsWith("unknown.", StringComparison.OrdinalIgnoreCase)
      ? ProviderMutationResult.Unknown(Correlation())
      : ProviderMutationResult.Accepted(id, Correlation()));
  }

  public Task<DirectoryUserRecord?> FindByUserPrincipalNameAsync(string requestedTenant, string userPrincipalName, CancellationToken ct)
  {
    RequireTenant(requestedTenant);
    return Task.FromResult(users.Values.SingleOrDefault(x =>
      x.UserPrincipalName.Equals(userPrincipalName, StringComparison.OrdinalIgnoreCase)));
  }

  // --- IMicrosoftGuestInvitationProvider
  public Task<ProviderMutationResult> InviteAsync(string requestedTenant, string email, string redirectUrl, CancellationToken ct)
  {
    RequireTenant(requestedTenant);
    if (email.StartsWith("reject.", StringComparison.OrdinalIgnoreCase))
      return Task.FromResult(ProviderMutationResult.Failed("graph-bad-request", Correlation()));
    var id = Guid.NewGuid().ToString("D");
    var upn = email.Replace('@', '_') + "#EXT#@simulated.onmicrosoft.com";
    users[id] = new(tenantId, id, email, upn, email, true, "Guest", DateTimeOffset.UtcNow);
    return Task.FromResult(email.StartsWith("unknown.", StringComparison.OrdinalIgnoreCase)
      ? ProviderMutationResult.Unknown(Correlation())
      : ProviderMutationResult.Accepted(id, Correlation()));
  }

  public Task<IReadOnlyList<DirectoryUserRecord>> FindGuestsByEmailAsync(string requestedTenant, string email, CancellationToken ct)
  {
    RequireTenant(requestedTenant);
    return Task.FromResult<IReadOnlyList<DirectoryUserRecord>>(users.Values.Where(x => x.UserType == "Guest" &&
      string.Equals(x.Mail, email, StringComparison.OrdinalIgnoreCase)).ToList());
  }

  // --- IMicrosoftGroupMembershipProvider
  public Task<DirectoryGroupRecord> GetGroupAsync(string requestedTenant, string groupObjectId, CancellationToken ct)
  {
    RequireTenant(requestedTenant);
    return groups.TryGetValue(groupObjectId, out var group)
      ? Task.FromResult(new DirectoryGroupRecord(tenantId, group.ObjectId, group.DisplayName, true, group.IsAssignableToRole, group.DynamicMembership))
      : throw new InvalidOperationException("simulated-group-not-found");
  }

  public Task<DirectoryGroupMemberPage> ListMembersAsync(string requestedTenant, string groupObjectId, string? pageToken, CancellationToken ct)
  {
    RequireTenant(requestedTenant);
    var list = members.GetValueOrDefault(groupObjectId)?.Keys
      .Select(id => users.TryGetValue(id, out var u) ? new DirectoryGroupMember(id, u.DisplayName, u.UserPrincipalName) : new(id, id, id))
      .OrderBy(x => x.DisplayName).Take(25).ToList() ?? [];
    return Task.FromResult(new DirectoryGroupMemberPage(list, null));
  }

  public Task<bool> IsMemberAsync(string requestedTenant, string groupObjectId, string memberObjectId, CancellationToken ct)
  {
    RequireTenant(requestedTenant);
    return Task.FromResult(members.GetValueOrDefault(groupObjectId)?.ContainsKey(memberObjectId) == true);
  }

  public Task<ProviderMutationResult> AddMemberAsync(string requestedTenant, string groupObjectId, string memberObjectId, CancellationToken ct)
  {
    RequireTenant(requestedTenant);
    if (!members.TryGetValue(groupObjectId, out var set)) return Task.FromResult(ProviderMutationResult.Failed("graph-not-found", Correlation()));
    set[memberObjectId] = 1;
    return Task.FromResult(ProviderMutationResult.Accepted(memberObjectId, Correlation()));
  }

  public Task<ProviderMutationResult> RemoveMemberAsync(string requestedTenant, string groupObjectId, string memberObjectId, CancellationToken ct)
  {
    RequireTenant(requestedTenant);
    if (!members.TryGetValue(groupObjectId, out var set) || !set.TryRemove(memberObjectId, out _))
      return Task.FromResult(ProviderMutationResult.Failed("graph-not-found", Correlation()));
    return Task.FromResult(ProviderMutationResult.Accepted(memberObjectId, Correlation()));
  }

  // --- IMicrosoftTenantConsentVerifier
  public Uri BuildIdentityChallenge(string requestedTenant, string state, string nonce)
  {
    RequireTenant(requestedTenant);
    return new Uri($"/auth/m365-consent/simulated-identity?state={Uri.EscapeDataString(state)}&nonce={Uri.EscapeDataString(nonce)}",
      UriKind.Relative);
  }

  /// <summary>Issued by the simulated Microsoft sign-in endpoint for the currently signed-in identity.</summary>
  public string IssueCode(string signedInTenant, string signedInObjectId, string nonce)
  {
    var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    codes[code] = new(signedInTenant, signedInObjectId, nonce, ExternalIdentity: false);
    return code;
  }

  public Task<ConsentingAdministrator> RedeemIdentityAsync(string requestedTenant, string code, CancellationToken ct)
  {
    RequireTenant(requestedTenant);
    return codes.TryRemove(code, out var identity) // one-use, like Entra authorization codes
      ? Task.FromResult(identity)
      : throw new InvalidOperationException("simulated-code-invalid");
  }

  public Task<IReadOnlyList<CapabilityProbeResult>> VerifyCapabilitiesAsync(string requestedTenant,
    IReadOnlyList<CapabilityProbe> capabilities, CancellationToken ct)
  {
    RequireTenant(requestedTenant);
    return Task.FromResult<IReadOnlyList<CapabilityProbeResult>>(capabilities.Select(x => notGranted.Contains(x.Capability)
      ? new CapabilityProbeResult(x.Capability, CapabilityVerificationStates.NotGranted, "simulated-not-granted")
      : new CapabilityProbeResult(x.Capability, CapabilityVerificationStates.Verified, "simulated-verified", Correlation())).ToList());
  }

  private static string Correlation() => "sim-" + Guid.NewGuid().ToString("N")[..12];
}
