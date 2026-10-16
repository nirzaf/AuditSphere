using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

/// <summary>Current local binding state, not proof or recovery of an original bind command.</summary>
public sealed record DirectoryBindingReconciliation(
  string TenantId, string ObjectId, bool Bound, Guid? UserId, string? UserKind, bool? LocalDisabled,
  DateTimeOffset? VerificationStartedAt, string? DirectoryEnabledState, string? DirectoryUserType);

/// <summary>Re-reads an exact Microsoft object before local binding; binding creates no grant.</summary>
public static class DirectoryUserBindingService
{
  public static Task<CommandResult<Guid>> BindMemberAsync(
    IAuditSphereDbContext db, ActorContext actor, IMicrosoftDirectoryReader reader,
    string configuredTenantId, Guid objectId, CancellationToken ct = default) =>
    BindAsync(db, actor, reader, configuredTenantId, objectId, false, ct);

  /// <summary>Existing B2B guests bind as Client identities; explicit scoped access is separate.</summary>
  public static Task<CommandResult<Guid>> BindExistingGuestAsync(
    IAuditSphereDbContext db, ActorContext actor, IMicrosoftDirectoryReader reader,
    string configuredTenantId, Guid objectId, CancellationToken ct = default) =>
    BindAsync(db, actor, reader, configuredTenantId, objectId, true, ct);

  private static async Task<CommandResult<Guid>> BindAsync(
    IAuditSphereDbContext db, ActorContext actor, IMicrosoftDirectoryReader reader,
    string configuredTenantId, Guid objectId, bool guest, CancellationToken ct)
  {
    var request = Authority(actor);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded ||
        !Guid.TryParse(configuredTenantId, out var tenant) || tenant == Guid.Empty || objectId == Guid.Empty)
      return Denied<Guid>();

    var startedAt = DateTimeOffset.UtcNow;
    DirectoryCandidate candidate;
    try
    {
      candidate = await reader.GetByIdAsync(tenant.ToString("D"), objectId.ToString("D"), ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "Microsoft identity could not be verified. No local access was changed.");
    }
    // Only evidence for the exact requested identity may affect its retained verification.
    if (!string.Equals(candidate.TenantId, tenant.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(candidate.ObjectId, objectId.ToString("D"), StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "Microsoft identity response could not be verified. No local access was changed.");
    if (!candidate.AccountEnabled || candidate.UserType != (guest ? "Guest" : "Member"))
    {
      // Exact negative evidence must survive malformed optional profile fields. Never store
      // an oversized or unsafe provider value merely to invalidate an older positive read.
      candidate = candidate with
      {
        DisplayName = SafeText(candidate.DisplayName, 300, "Directory identity (display unavailable)"),
        UserPrincipalName = SafeText(candidate.UserPrincipalName, 320, string.Empty),
        UserType = SafeText(candidate.UserType, 40, "UNKNOWN")
      };
    }
    else if (SafeText(candidate.DisplayName, 300, string.Empty).Length == 0 ||
        SafeText(candidate.UserPrincipalName, 320, string.Empty).Length == 0)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "Microsoft identity response could not be verified. No local access was changed.");
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return Denied<Guid>();

    return await RoleAdministrationService.EnsureVerifiedDirectoryUserAsync(db, actor,
      candidate with { TenantId = tenant.ToString("D"), ObjectId = objectId.ToString("D") }, guest, startedAt, ct);
  }

  /// <summary>
  /// Reconciles exact local state without Graph calls, mutation, access grants or original-outcome claims.
  /// A shared firm publication barrier makes an absent result wait for an in-flight binding transaction.
  /// </summary>
  public static async Task<CommandResult<DirectoryBindingReconciliation>> ReconcileAsync(
    IAuditSphereDbContext db, ActorContext actor, string configuredTenantId, Guid tenantId, Guid objectId,
    CancellationToken ct = default)
  {
    var authority = Authority(actor);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, authority, ct)).Succeeded ||
        !Guid.TryParse(configuredTenantId, out var configured) || configured == Guid.Empty ||
        configured != tenantId || objectId == Guid.Empty)
      return Denied<DirectoryBindingReconciliation>();

    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    var firm = await db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    var currentActor = await db.Users.FromSqlInterpolated(
      $"SELECT * FROM users WHERE firm_id = {actor.FirmId} AND id = {actor.UserId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    if (firm is null || currentActor is null ||
        !(await AuthorizationDecision.AuthorizeAsync(db, actor, authority, ct)).Succeeded)
      return Denied<DirectoryBindingReconciliation>();

    var tenant = tenantId.ToString("D");
    var subject = objectId.ToString("D");
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.TenantId == tenant && x.Subject == subject, ct);
    var observation = await db.DirectoryUserObservations.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.TenantId == tenant && x.ObjectId == subject && x.Source == "GRAPH")
      .OrderByDescending(x => x.ObservedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, authority, ct)).Succeeded)
      return Denied<DirectoryBindingReconciliation>();

    var result = new DirectoryBindingReconciliation(tenant, subject, user is not null, user?.Id,
      user?.UserKind, user?.Disabled, observation?.ObservedAt.ToUniversalTime(), observation?.EnabledState, observation?.UserType);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<DirectoryBindingReconciliation>.Ok(result);
  }

  private static string SafeText(string? value, int maximumLength, string fallback) =>
    string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl) ? fallback : value.Trim();

  private static AuthorizationRequest Authority(ActorContext actor) =>
    new(actor.FirmId, RequiredRoles: ["Administrator"], InternalOnly: true, RequireFirmWide: true);

  internal static string GuestContact(string upn)
  {
    var marker = upn.IndexOf("#EXT#", StringComparison.OrdinalIgnoreCase);
    if (marker <= 0) return upn;
    var local = upn[..marker];
    var split = local.LastIndexOf('_');
    return split > 0 ? local[..split] + "@" + local[(split + 1)..] : upn;
  }

  private static CommandResult<T> Denied<T>() => CommandResult<T>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
}
