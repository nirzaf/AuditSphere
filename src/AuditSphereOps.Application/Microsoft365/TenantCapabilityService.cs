using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

/// <summary>
/// Deployment-owned switches. A capability is usable only when it is enabled here AND its latest
/// persisted verification for the exact tenant is VERIFIED and fresh.
/// </summary>
public sealed record TenantAdministrationOptions(
  string TenantId,
  bool ProvisioningEnabled = false,
  bool GuestInvitationEnabled = false,
  bool GroupMembershipEnabled = false,
  bool DirectoryReadEnabled = false,
  bool OutboundMailEnabled = false,
  string? GuestRedirectUrl = null,
  TimeSpan? VerificationMaxAge = null)
{
  public TimeSpan MaxAge => VerificationMaxAge ?? TimeSpan.FromHours(24);

  public bool IsEnabled(string capability) => capability switch
  {
    Microsoft365Capabilities.TenantUserProvisioning => ProvisioningEnabled,
    Microsoft365Capabilities.GuestInvitation => GuestInvitationEnabled,
    Microsoft365Capabilities.GroupMembership => GroupMembershipEnabled,
    Microsoft365Capabilities.DirectoryRead => DirectoryReadEnabled,
    Microsoft365Capabilities.OutboundMail => OutboundMailEnabled,
    Microsoft365Capabilities.SignIn or Microsoft365Capabilities.SelectedSite => true,
    _ => false
  };
}

public sealed record CapabilityStatus(string Capability, string DisplayName, string Permission, bool Enabled,
  string State, DateTimeOffset? LastVerifiedAt, bool Stale, string DiagnosticCode);

public static class TenantCapabilityService
{
  public const string Stale = "STALE";
  public const string NotVerified = "NOT_VERIFIED";
  public const string Disabled = "DISABLED";

  /// <summary>Verifies every capability independently and appends one row per capability.</summary>
  public static async Task<CommandResult<IReadOnlyList<CapabilityStatus>>> VerifyAsync(
    IAuditSphereDbContext db, ActorContext actor, IMicrosoftTenantConsentVerifier verifier,
    TenantAdministrationOptions options, DateTimeOffset now, CancellationToken ct = default)
  {
    if (!Guid.TryParse(options.TenantId, out var tenant))
      return CommandResult<IReadOnlyList<CapabilityStatus>>.Fail(ErrorCodes.GateBlocked, "The tenant is not configured.");
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      return TenantAdministration.Denied<IReadOnlyList<CapabilityStatus>>();
    var tenantId = tenant.ToString("D");
    var connectionId = await CurrentConnectionIdAsync(db, actor.FirmId, tenantId, ct);
    var consent = await db.TenantConsentAttempts.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ExpectedTenantId == tenantId &&
        x.State == TenantConsentAttemptStates.ConsentVerified)
      .OrderByDescending(x => x.ConsentVerifiedAt).FirstOrDefaultAsync(ct);

    var probes = Microsoft365PermissionMatrix.Rows
      .Where(x => x.Capability is not (Microsoft365Capabilities.SignIn or Microsoft365Capabilities.SelectedSite) &&
        options.IsEnabled(x.Capability))
      .Select(x => new CapabilityProbe(x.Capability, x.Permission)).ToList();
    IReadOnlyList<CapabilityProbeResult> results;
    if (!verifier.IsConfigured)
      results = probes.Select(x => new CapabilityProbeResult(x.Capability,
        CapabilityVerificationStates.BlockedExternal, "consent-verifier-not-configured")).ToList();
    else
    {
      try { results = await verifier.VerifyCapabilitiesAsync(tenantId, probes, ct); }
      catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
      {
        results = probes.Select(x => new CapabilityProbeResult(x.Capability,
          CapabilityVerificationStates.Failed, "capability-verification-unavailable")).ToList();
      }
    }

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      return TenantAdministration.Denied<IReadOnlyList<CapabilityStatus>>();
    foreach (var probe in probes)
    {
      // A provider result for an unrequested capability is ignored; a missing one fails closed.
      var result = results.FirstOrDefault(x => x.Capability == probe.Capability);
      var state = result?.State is CapabilityVerificationStates.Verified or CapabilityVerificationStates.NotGranted or
        CapabilityVerificationStates.Failed or CapabilityVerificationStates.BlockedExternal
        ? result.State : CapabilityVerificationStates.Failed;
      // Application-permission capabilities also require a verified consenting administrator.
      if (state == CapabilityVerificationStates.Verified && consent is null &&
          probe.Capability != Microsoft365Capabilities.SelectedSite)
        state = CapabilityVerificationStates.BlockedExternal;
      db.TenantCapabilityVerifications.Add(new TenantCapabilityVerification
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ConnectionRevisionId = connectionId,
        ConsentAttemptId = consent?.Id, TenantId = tenantId, Capability = probe.Capability,
        Permission = probe.Permission, State = state,
        DiagnosticCode = state == CapabilityVerificationStates.BlockedExternal && consent is null &&
          result?.State == CapabilityVerificationStates.Verified
            ? "consent-not-verified" : SafeCode(result?.DiagnosticCode),
        ProviderCorrelationId = Truncate(result?.ProviderCorrelationId, 200),
        ObservedByUserId = actor.UserId, ObservedAt = now
      });
    }
    TenantAdministration.AddEvent(db, actor, "CAPABILITIES_VERIFIED", now, targetTenantId: tenantId,
      oldState: "-", newState: string.Join(",", probes.Select(x => x.Capability)),
      reason: "Administrator requested capability verification", result: "RECORDED");
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<IReadOnlyList<CapabilityStatus>>.Ok(
      await StatusesAsync(db, actor.FirmId, options, now, ct));
  }

  /// <summary>Current per-capability state derived only from persisted verification rows.</summary>
  public static async Task<IReadOnlyList<CapabilityStatus>> StatusesAsync(
    IAuditSphereDbContext db, Guid firmId, TenantAdministrationOptions options, DateTimeOffset now,
    CancellationToken ct = default)
  {
    var tenantId = Guid.TryParse(options.TenantId, out var tenant) ? tenant.ToString("D") : string.Empty;
    var rows = await db.TenantCapabilityVerifications.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.TenantId == tenantId)
      .OrderByDescending(x => x.ObservedAt).ThenByDescending(x => x.Id)
      .Take(200).ToListAsync(ct);
    return Microsoft365PermissionMatrix.Rows.Select(row =>
    {
      var enabled = options.IsEnabled(row.Capability);
      var latest = rows.FirstOrDefault(x => x.Capability == row.Capability);
      if (row.Capability == Microsoft365Capabilities.SignIn)
        return new CapabilityStatus(row.Capability, row.DisplayName, row.Permission, true,
          string.IsNullOrEmpty(tenantId) ? CapabilityVerificationStates.BlockedExternal : CapabilityVerificationStates.Verified,
          null, false, string.IsNullOrEmpty(tenantId) ? "tenant-not-configured" : "sign-in-configured");
      if (!enabled)
        return new CapabilityStatus(row.Capability, row.DisplayName, row.Permission, false, Disabled,
          latest?.ObservedAt, false, "capability-disabled");
      if (latest is null)
        return new CapabilityStatus(row.Capability, row.DisplayName, row.Permission, true, NotVerified,
          null, false, "not-verified");
      var stale = latest.ObservedAt < now - options.MaxAge;
      return new CapabilityStatus(row.Capability, row.DisplayName, row.Permission, true,
        stale && latest.State == CapabilityVerificationStates.Verified ? Stale : latest.State,
        latest.ObservedAt, stale, latest.DiagnosticCode);
    }).ToList();
  }

  /// <summary>Fail-closed gate used before every Microsoft mutation or directory read.</summary>
  public static async Task<CommandResult> RequireVerifiedAsync(
    IAuditSphereDbContext db, Guid firmId, TenantAdministrationOptions options, string capability,
    DateTimeOffset now, CancellationToken ct = default)
  {
    if (!options.IsEnabled(capability))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "This Microsoft capability is not enabled for this deployment.");
    var status = (await StatusesAsync(db, firmId, options, now, ct)).Single(x => x.Capability == capability);
    return status.State == CapabilityVerificationStates.Verified
      ? CommandResult.Ok()
      : CommandResult.Fail(ErrorCodes.GateBlocked, status.State == Stale
        ? "The Microsoft capability verification is stale; verify it again before continuing."
        : "The Microsoft capability is not verified for this tenant.");
  }

  internal static async Task<Guid?> CurrentConnectionIdAsync(IAuditSphereDbContext db, Guid firmId,
    string tenantId, CancellationToken ct) =>
    await db.Microsoft365ConnectionRevisions.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.TenantId == tenantId)
      .OrderByDescending(x => x.Revision).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);

  private static string SafeCode(string? code) =>
    string.IsNullOrWhiteSpace(code) || code.Length > 100 || code.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
      ? "provider-result-unclassified" : code;

  private static string? Truncate(string? value, int max) =>
    string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}

/// <summary>Shared authorization, fingerprint and immutable-event helpers.</summary>
public static class TenantAdministration
{
  public static async Task<bool> IsCurrentAdministratorAsync(IAuditSphereDbContext db, ActorContext actor,
    CancellationToken ct) =>
    (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"], InternalOnly: true,
        RequireFirmWide: true), ct)).Succeeded;

  public static CommandResult<T> Denied<T>() => CommandResult<T>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  public static CommandResult Denied() => CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

  public static string Fingerprint(params string?[] parts) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u001f", parts.Select(x => x ?? "")))))
      .ToLowerInvariant();

  public static void AddEvent(IAuditSphereDbContext db, ActorContext actor, string operation, DateTimeOffset now,
    string oldState, string newState, string reason, string result, string? targetTenantId = null,
    string? targetObjectId = null, Guid? targetUserId = null, string? roleScopeChange = null,
    Guid? externalOperationId = null, string? correlationId = null) =>
    db.Microsoft365AdministrationEvents.Add(new Microsoft365AdministrationEvent
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ActorUserId = actor.UserId, Operation = operation,
      TargetTenantId = targetTenantId, TargetObjectId = targetObjectId, TargetUserId = targetUserId,
      OldState = Clip(oldState, 300), NewState = Clip(newState, 300), RoleScopeChange = roleScopeChange is null ? null : Clip(roleScopeChange, 600),
      Reason = Clip(string.IsNullOrWhiteSpace(reason) ? "-" : reason.Trim(), 1000), Result = Clip(result, 60),
      ExternalOperationId = externalOperationId, ProviderCorrelationId = correlationId is null ? null : Clip(correlationId, 200),
      CreatedAt = now
    });

  private static string Clip(string value, int max) =>
    string.IsNullOrEmpty(value) ? "-" : value.Length <= max ? value : value[..max];
}
