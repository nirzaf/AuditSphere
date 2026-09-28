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
public sealed record TenantIdentityChallenge(Guid AttemptId, string TenantId, string State, string Nonce);

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

  /// <summary>
  /// Starts the second leg after a matching consent return: a fixed-tenant OIDC sign-in whose
  /// one-use state and nonce are stored only as hashes. Returns the raw values exactly once.
  /// </summary>
  public static async Task<CommandResult<TenantIdentityChallenge>> BeginIdentityVerificationAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid attemptId, DateTimeOffset now,
    CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var attempt = await LockAttemptAsync(db, actor.FirmId, attemptId, ct);
    if (attempt is null || attempt.State != TenantConsentAttemptStates.ReturnedUnverified ||
        attempt.InitiatedByUserId != actor.UserId || attempt.InitiatingSessionEpoch != actor.SessionEpoch ||
        now >= attempt.ExpiresAt || !await CurrentAdministratorAsync(db, actor, ct))
      return Denied<TenantIdentityChallenge>();
    var state = RandomToken();
    var nonce = RandomToken();
    attempt.IdentityStateHash = Hash(state);
    attempt.NonceHash = Hash(nonce);
    attempt.IdentityExpiresAt = now.AddMinutes(10);
    attempt.State = TenantConsentAttemptStates.IdentityPending;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<TenantIdentityChallenge>.Ok(new(attempt.Id, attempt.ExpectedTenantId, state, nonce));
  }

  /// <summary>
  /// Completes the identity leg. The authorization code is redeemed server-side; only the
  /// validated tid/oid are stored. Replays, wrong tenants, wrong nonces and external identities fail closed.
  /// </summary>
  public static async Task<CommandResult<Guid>> CompleteIdentityVerificationAsync(
    IAuditSphereDbContext db, ActorContext actor, IMicrosoftTenantConsentVerifier verifier,
    string? state, string? code, bool providerError, DateTimeOffset now, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(state) || state.Length > 256 || !verifier.IsConfigured)
      return Denied<Guid>();
    var stateHash = Hash(state);
    Guid attemptId;
    string tenantId;
    await using (var claimTx = await db.Database.BeginTransactionAsync(ct))
    {
      var attempts = await db.TenantConsentAttempts.FromSqlInterpolated($"""
        SELECT * FROM m365_tenant_consent_attempts
        WHERE firm_id = {actor.FirmId} AND identity_state_hash = {stateHash} FOR UPDATE
        """).ToListAsync(ct);
      if (attempts.Count != 1) return Denied<Guid>();
      var attempt = attempts[0];
      if (attempt.State != TenantConsentAttemptStates.IdentityPending ||
          attempt.InitiatedByUserId != actor.UserId || attempt.InitiatingSessionEpoch != actor.SessionEpoch ||
          !await CurrentAdministratorAsync(db, actor, ct))
        return Denied<Guid>();
      // Consume the identity state before contacting Microsoft so a replay can never redeem twice.
      var expired = attempt.IdentityExpiresAt is null || now >= attempt.IdentityExpiresAt;
      // Denied is provisional for a redeemable code; it becomes CONSENT_VERIFIED only after validation.
      attempt.State = expired ? TenantConsentAttemptStates.Expired : TenantConsentAttemptStates.Denied;
      attemptId = attempt.Id;
      tenantId = attempt.ExpectedTenantId;
      var mayRedeem = !expired && !providerError && !string.IsNullOrWhiteSpace(code) && code.Length <= 4096;
      TenantAdministration.AddEvent(db, actor, "TENANT_CONSENT_IDENTITY_RETURNED", now,
        oldState: TenantConsentAttemptStates.IdentityPending, newState: attempt.State,
        reason: "Microsoft identity sign-in returned", result: mayRedeem ? "REDEEMING" : "DENIED",
        targetTenantId: tenantId);
      await db.SaveChangesAsync(ct);
      await claimTx.CommitAsync(ct);
      if (!mayRedeem) return Denied<Guid>();
    }

    ConsentingAdministrator identity;
    try { identity = await verifier.RedeemIdentityAsync(tenantId, code!, ct); }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "The Microsoft administrator sign-in could not be verified. Start the tenant connection again.");
    }

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var current = await LockAttemptAsync(db, actor.FirmId, attemptId, ct);
    if (current is null || current.State != TenantConsentAttemptStates.Denied || current.ConsentVerifiedAt is not null ||
        current.InitiatedByUserId != actor.UserId || !await CurrentAdministratorAsync(db, actor, ct))
      return Denied<Guid>();
    var valid = Guid.TryParse(identity.TenantId, out var observedTenant) &&
      string.Equals(observedTenant.ToString("D"), current.ExpectedTenantId, StringComparison.OrdinalIgnoreCase) &&
      Guid.TryParse(identity.ObjectId, out _) && !identity.ExternalIdentity &&
      current.NonceHash is { } nonceHash && !string.IsNullOrEmpty(identity.Nonce) &&
      CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Hash(identity.Nonce)), Convert.FromHexString(nonceHash));
    if (!valid)
    {
      TenantAdministration.AddEvent(db, actor, "TENANT_CONSENT_IDENTITY_REJECTED", now,
        oldState: current.State, newState: TenantConsentAttemptStates.Denied,
        reason: "Wrong tenant, nonce or identity type", result: "DENIED", targetTenantId: current.ExpectedTenantId);
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied,
        "The Microsoft administrator was not a work or school identity of the configured tenant.");
    }
    current.State = TenantConsentAttemptStates.ConsentVerified;
    current.ConsentingTenantId = observedTenant.ToString("D");
    current.ConsentingObjectId = Guid.Parse(identity.ObjectId).ToString("D");
    current.ConsentVerifiedAt = now;

    var draft = await db.Microsoft365SetupDrafts.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == current.SetupDraftId, ct);
    var connection = draft?.ConnectionRevisionId is { } connectionId
      ? await db.Microsoft365ConnectionRevisions.SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == connectionId, ct)
      : null;
    if (draft is not null && connection is not null && connection.State != Microsoft365RevisionStates.Active &&
        connection.TenantId == current.ExpectedTenantId)
    {
      db.IntegrationVerificationEvidences.Add(new IntegrationVerificationEvidence
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, SetupDraftId = draft.Id,
        ConnectionRevisionId = connection.Id, ResourceKind = "TENANT", ResourceId = current.ExpectedTenantId,
        Operation = "CONSENT", IdentityReference = current.ConsentingObjectId, Result = "PASS",
        EvidenceReference = $"Admin consent returned for state-bound attempt {current.Id:D}; consenting administrator authenticated by nonce-bound OIDC",
        ObservedAt = now
      });
      connection.ConsentState = "OBSERVED";
    }
    TenantAdministration.AddEvent(db, actor, "TENANT_CONSENT_VERIFIED", now,
      oldState: TenantConsentAttemptStates.IdentityPending, newState: TenantConsentAttemptStates.ConsentVerified,
      reason: "Tenant administrator consent and identity verified", result: "VERIFIED",
      targetTenantId: current.ExpectedTenantId, targetObjectId: current.ConsentingObjectId);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(current.Id);
  }

  private static async Task<TenantConsentAttempt?> LockAttemptAsync(IAuditSphereDbContext db, Guid firmId,
    Guid attemptId, CancellationToken ct) =>
    await db.TenantConsentAttempts.FromSqlInterpolated($"""
      SELECT * FROM m365_tenant_consent_attempts WHERE firm_id = {firmId} AND id = {attemptId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);

  private static string RandomToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    .TrimEnd('=').Replace('+', '-').Replace('/', '_');

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
