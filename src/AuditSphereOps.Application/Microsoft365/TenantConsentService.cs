using System.Security.Cryptography;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

public sealed record TenantConsentStart(Guid AttemptId, string State);
public sealed record TenantConsentReturn(Guid AttemptId, string State);

/// <summary>
/// One-use correlation for Microsoft admin consent. A callback is only a returned claim;
/// capability-specific provider checks must happen before any connection is verified.
/// </summary>
public static class TenantConsentService
{
  public static async Task<CommandResult<TenantConsentStart>> BeginAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid draftId,
    string configuredTenantId, string consentApplicationClientId,
    DateTimeOffset now, CancellationToken ct = default)
  {
    if (draftId == Guid.Empty || !Guid.TryParse(configuredTenantId, out var tenantGuid) ||
        !Guid.TryParse(consentApplicationClientId, out var appGuid))
      return Denied<TenantConsentStart>();
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await CurrentAdministratorAsync(db, actor, ct)) return Denied<TenantConsentStart>();
    var user = await db.Users.AsNoTracking().SingleAsync(x =>
      x.FirmId == actor.FirmId && x.Id == actor.UserId, ct);
    if (!Guid.TryParse(user.Subject, out var objectGuid) ||
        !string.Equals(user.TenantId, configuredTenantId, StringComparison.OrdinalIgnoreCase))
      return Denied<TenantConsentStart>();
    var draft = await db.Microsoft365SetupDrafts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == draftId && x.FirmId == actor.FirmId, ct);
    if (draft is null || draft.ConnectionRevisionId is null ||
        draft.State is Microsoft365RevisionStates.Active or Microsoft365RevisionStates.Suspended or Microsoft365RevisionStates.Blocked ||
        !string.Equals(draft.ExpectedTenantId, configuredTenantId, StringComparison.OrdinalIgnoreCase) ||
        !await db.Microsoft365ConnectionRevisions.AsNoTracking().AnyAsync(x =>
          x.Id == draft.ConnectionRevisionId && x.FirmId == actor.FirmId &&
          x.TenantId == configuredTenantId &&
          x.State != Microsoft365RevisionStates.Active &&
          x.State != Microsoft365RevisionStates.Suspended &&
          x.State != Microsoft365RevisionStates.Blocked, ct))
      return Denied<TenantConsentStart>();

    var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
      .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    var attempt = new TenantConsentAttempt
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, SetupDraftId = draftId,
      InitiatedByUserId = actor.UserId, InitiatingSessionEpoch = actor.SessionEpoch,
      InitiatorObjectId = objectGuid.ToString("D"), ExpectedTenantId = tenantGuid.ToString("D"),
      ApplicationClientId = appGuid.ToString("D"), StateHash = Hash(state),
      CreatedAt = now, ExpiresAt = now.AddMinutes(10)
    };
    db.TenantConsentAttempts.Add(attempt);
    if (!await CurrentAdministratorAsync(db, actor, ct)) return Denied<TenantConsentStart>();
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<TenantConsentStart>.Ok(new(attempt.Id, state));
  }

  public static async Task<CommandResult<TenantConsentReturn>> CompleteCallbackAsync(
    IAuditSphereDbContext db, ActorContext actor, string? state,
    string? returnedTenantId, bool consentReturned, bool providerError,
    DateTimeOffset now, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(state) || state.Length > 256)
      return Denied<TenantConsentReturn>();
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var stateHash = Hash(state);
    var attempts = await db.TenantConsentAttempts.FromSqlInterpolated($"""
      SELECT * FROM m365_tenant_consent_attempts
      WHERE firm_id = {actor.FirmId} AND state_hash = {stateHash} FOR UPDATE
      """).ToListAsync(ct);
    if (attempts.Count != 1) return Denied<TenantConsentReturn>();
    var attempt = attempts[0];
    if (attempt.State != TenantConsentAttemptStates.Pending ||
        attempt.InitiatedByUserId != actor.UserId ||
        attempt.InitiatingSessionEpoch != actor.SessionEpoch ||
        !await CurrentAdministratorAsync(db, actor, ct))
      return Denied<TenantConsentReturn>();
    var user = await db.Users.AsNoTracking().SingleAsync(x =>
      x.FirmId == actor.FirmId && x.Id == actor.UserId, ct);
    if (!string.Equals(user.Subject, attempt.InitiatorObjectId, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(user.TenantId, attempt.ExpectedTenantId, StringComparison.OrdinalIgnoreCase))
      return Denied<TenantConsentReturn>();

    if (now >= attempt.ExpiresAt)
      attempt.State = TenantConsentAttemptStates.Expired;
    else if (providerError || !consentReturned ||
             !Guid.TryParse(returnedTenantId, out var returnedTenant) ||
             !string.Equals(returnedTenant.ToString("D"), attempt.ExpectedTenantId, StringComparison.OrdinalIgnoreCase))
      attempt.State = TenantConsentAttemptStates.Denied;
    else
      attempt.State = TenantConsentAttemptStates.ReturnedUnverified;
    attempt.ReturnedAt = now;
    attempt.ReturnedTenantId = Guid.TryParse(returnedTenantId, out var observedTenant)
      ? observedTenant.ToString("D") : null;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return attempt.State == TenantConsentAttemptStates.ReturnedUnverified
      ? CommandResult<TenantConsentReturn>.Ok(new(attempt.Id, attempt.State))
      : Denied<TenantConsentReturn>();
  }

  private static async Task<bool> CurrentAdministratorAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"],
        InternalOnly: true, RequireFirmWide: true), ct)).Succeeded;

  private static string Hash(string state) =>
    Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(state))).ToLowerInvariant();

  private static CommandResult<T> Denied<T>() =>
    CommandResult<T>.Fail(ErrorCodes.ScopeDenied, "Tenant consent is unavailable or the session is invalid.");
}
