using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Application.Operations;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

public sealed record SetupClaim(Guid SessionId, string Capability);

public sealed record CompleteInitialAdministratorBootstrapRequest(
  Guid SessionId,
  string Capability,
  string TenantId,
  string ObjectId,
  string Email,
  string DisplayName);

public sealed record SaveSetupDraftRequest(
  Guid SessionId,
  string Capability,
  long ExpectedRevision,
  string? ExpectedTenantId,
  string? TenantDisplayName,
  string? SiteUrl,
  string? SiteId,
  string? DriveId,
  string? RootFolderId,
  string AccessProfile,
  string MailState,
  string RecordsState);

/// <summary>Small, fail-closed local setup boundary. It never calls Microsoft APIs.</summary>
public static class Microsoft365OnboardingService
{
  public static async Task<CommandResult<Guid>> CompleteInitialAdministratorBootstrapAsync(
    IAuditSphereDbContext db,
    CompleteInitialAdministratorBootstrapRequest request,
    string configuredTenantId,
    string configuredObjectId,
    DateTimeOffset now,
    CancellationToken ct = default)
  {
    if (request.SessionId == Guid.Empty || string.IsNullOrWhiteSpace(request.Capability) ||
        string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.ObjectId) ||
        string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.DisplayName) ||
        string.IsNullOrWhiteSpace(configuredTenantId) || string.IsNullOrWhiteSpace(configuredObjectId) ||
        !request.TenantId.Equals(configuredTenantId.Trim(), StringComparison.OrdinalIgnoreCase) ||
        !request.ObjectId.Equals(configuredObjectId.Trim(), StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail("m365.bootstrap.identity", "The authenticated Microsoft identity is not the approved installation administrator.");
    if (!request.Email.Contains('@', StringComparison.Ordinal) || request.Email.Any(char.IsWhiteSpace))
      return CommandResult<Guid>.Fail("m365.bootstrap.invalid", "A valid administrator contact is required.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var session = await db.Microsoft365SetupSessions.FromSqlInterpolated($"""
      SELECT * FROM m365_setup_sessions WHERE id = {request.SessionId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (session is null || session.ExpiresAt <= now || session.State != Microsoft365SetupStates.Claimed ||
        session.CapabilityHash != Hash(request.Capability))
      return CommandResult<Guid>.Fail("m365.setup.denied", "The setup authorization is invalid or expired.");
    if (session.ClaimedByUserId is { } claimedId)
    {
      var claimed = await db.Users.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == claimedId && x.FirmId == session.FirmId, ct);
      if (claimed is not null && claimed.TenantId.Equals(request.TenantId.Trim(), StringComparison.OrdinalIgnoreCase) &&
          claimed.Subject.Equals(request.ObjectId.Trim(), StringComparison.OrdinalIgnoreCase))
      {
        await tx.CommitAsync(ct);
        return CommandResult<Guid>.Ok(claimed.Id);
      }
      return CommandResult<Guid>.Fail("m365.bootstrap.consumed", "The initial administrator bootstrap has already been consumed.");
    }

    var identity = await db.Users.SingleOrDefaultAsync(x =>
      x.FirmId == session.FirmId && x.TenantId == request.TenantId.Trim() && x.Subject == request.ObjectId.Trim(), ct);
    if (identity is null)
    {
      if (await db.Users.AsNoTracking().AnyAsync(x => x.FirmId == session.FirmId &&
          x.Email.ToUpper() == request.Email.Trim().ToUpper(), ct))
        return CommandResult<Guid>.Fail("m365.bootstrap.identity-conflict", "The administrator email is already bound to another immutable identity.");
      identity = new AppUser
      {
        Id = Guid.CreateVersion7(), FirmId = session.FirmId, TenantId = request.TenantId.Trim(),
        Subject = request.ObjectId.Trim(), Email = request.Email.Trim(), DisplayName = request.DisplayName.Trim(),
        UserKind = "Staff", CreatedAt = now
      };
      db.Users.Add(identity);
    }
    if (identity.Disabled || !identity.UserKind.Equals("Staff", StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail("m365.bootstrap.identity", "The approved identity cannot be used as the initial staff administrator.");

    var adminGrant = await db.RoleGrants.SingleOrDefaultAsync(x =>
      x.FirmId == session.FirmId && x.UserId == identity.Id && x.Role == "Administrator" &&
      x.ClientId == null && x.EngagementId == null && x.RevokedAt == null, ct);
    if (adminGrant is null)
    {
      adminGrant = new RoleGrant
      {
        Id = Guid.CreateVersion7(), FirmId = session.FirmId, UserId = identity.Id, Role = "Administrator",
        GrantedAt = now, GrantedByUserId = identity.Id
      };
      db.RoleGrants.Add(adminGrant);
      db.RoleGrantChangeEvidences.Add(new RoleGrantChangeEvidence
      {
        Id = Guid.CreateVersion7(), FirmId = session.FirmId, TargetUserId = identity.Id,
        RoleGrantId = adminGrant.Id, Action = "GRANTED", NewRole = "Administrator", Source = "BOOTSTRAP",
        ActorUserId = identity.Id, CreatedAt = now
      });
      identity.SessionEpoch++;
    }
    session.ClaimedByUserId = identity.Id;
    session.ConsumedAt ??= now;
    session.Revision++;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(identity.Id);
  }

  public static async Task<CommandResult<SetupClaim>> ClaimAsync(
    IAuditSphereDbContext db,
    Guid firmId,
    string installationId,
    string bootstrapProof,
    string configuredProofHash,
    DateTimeOffset now,
    CancellationToken ct = default,
    string? authenticatedTenantId = null,
    string? authenticatedObjectId = null)
  {
    if (firmId == Guid.Empty || string.IsNullOrWhiteSpace(installationId) ||
        string.IsNullOrWhiteSpace(bootstrapProof) || !IsSha256(configuredProofHash))
      return CommandResult<SetupClaim>.Fail("m365.setup.invalid", "The setup proof configuration is incomplete.");

    var actual = Hash(bootstrapProof);
    if (!CryptographicOperations.FixedTimeEquals(
          Convert.FromHexString(actual), Convert.FromHexString(configuredProofHash.Trim().ToLowerInvariant())))
      return CommandResult<SetupClaim>.Fail("m365.setup.denied", "The setup proof is invalid or expired.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var existing = await db.Microsoft365SetupSessions
      .SingleOrDefaultAsync(x => x.FirmId == firmId && x.InstallationId == installationId, ct);
    if (existing is not null)
    {
      if (existing.State == Microsoft365SetupStates.Active || existing.ConsumedAt is not null || existing.ClaimedByUserId is not null)
      {
        var canResume = existing.ClaimedByUserId is { } claimedId &&
          !string.IsNullOrWhiteSpace(authenticatedTenantId) && !string.IsNullOrWhiteSpace(authenticatedObjectId) &&
          await db.Users.AsNoTracking().AnyAsync(x => x.Id == claimedId && x.FirmId == firmId &&
            x.TenantId == authenticatedTenantId.Trim() && x.Subject == authenticatedObjectId.Trim(), ct);
        if (!canResume)
          return CommandResult<SetupClaim>.Fail("m365.setup.consumed", "Initial setup has already been claimed; resume requires the bound Microsoft identity.");
        if (existing.ExpiresAt <= now)
          return CommandResult<SetupClaim>.Fail("m365.setup.expired", "The setup authorization expired; issue a new bootstrap proof.");
        var resumedCapability = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        existing.CapabilityHash = Hash(resumedCapability);
        existing.Revision++;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return CommandResult<SetupClaim>.Ok(new(existing.Id, resumedCapability));
      }
      if (existing.ExpiresAt <= now)
      {
        existing.State = Microsoft365SetupStates.Expired;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return CommandResult<SetupClaim>.Fail("m365.setup.expired", "The setup authorization expired; issue a new bootstrap proof.");
      }
      existing.CapabilityHash = Hash(RandomNumberGenerator.GetBytes(32));
      existing.Revision++;
      var capability = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
      existing.CapabilityHash = Hash(capability);
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      return CommandResult<SetupClaim>.Ok(new(existing.Id, capability));
    }

    var capabilityValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    var session = new Microsoft365SetupSession
    {
      Id = Guid.CreateVersion7(), FirmId = firmId, InstallationId = installationId.Trim(),
      BootstrapProofHash = configuredProofHash.Trim().ToLowerInvariant(),
      CapabilityHash = Hash(capabilityValue), State = Microsoft365SetupStates.Claimed,
      ClaimedAt = now, ExpiresAt = now.AddMinutes(30)
    };
    var draft = new Microsoft365SetupDraft
    {
      Id = Guid.CreateVersion7(), FirmId = firmId, SetupSessionId = session.Id,
      CreatedAt = now, UpdatedAt = now
    };
    db.Microsoft365SetupSessions.Add(session);
    db.Microsoft365SetupDrafts.Add(draft);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<SetupClaim>.Ok(new(session.Id, capabilityValue));
  }

  public static async Task<CommandResult<Guid>> SaveDraftAsync(
    IAuditSphereDbContext db, SaveSetupDraftRequest request, DateTimeOffset now,
    CancellationToken ct = default)
  {
    if (request.AccessProfile is not (Microsoft365AccessProfiles.AppMediated or Microsoft365AccessProfiles.DirectStaffCollaboration))
      return CommandResult<Guid>.Fail("m365.setup.invalid", "Select a supported storage access profile.");
    if (request.ExpectedRevision < 1 || string.IsNullOrWhiteSpace(request.Capability) ||
        string.IsNullOrWhiteSpace(request.ExpectedTenantId))
      return CommandResult<Guid>.Fail("m365.setup.invalid", "The setup draft revision and capability are required.");
    if (request.ExpectedTenantId is { Length: > 0 } && request.ExpectedTenantId.Length > 200)
      return CommandResult<Guid>.Fail("m365.setup.invalid", "The tenant identifier is too long.");
    if (!string.IsNullOrWhiteSpace(request.SiteUrl) &&
        (!Uri.TryCreate(request.SiteUrl.Trim(), UriKind.Absolute, out var siteUri) ||
         !siteUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
         !string.IsNullOrEmpty(siteUri.UserInfo) || string.IsNullOrWhiteSpace(siteUri.Host)))
      return CommandResult<Guid>.Fail("m365.setup.invalid", "The working site must be an HTTPS URL without embedded credentials.");
    if (request.MailState.Trim().ToUpperInvariant() is not ("NOT_CONFIGURED" or "CONFIGURED") ||
        request.RecordsState.Trim().ToUpperInvariant() is not ("NOT_CONFIGURED" or "CONFIGURED"))
      return CommandResult<Guid>.Fail("m365.setup.invalid", "Optional capability state is invalid.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var session = await db.Microsoft365SetupSessions.SingleOrDefaultAsync(x =>
      x.Id == request.SessionId && x.State == Microsoft365SetupStates.Claimed && x.ExpiresAt > now &&
      x.CapabilityHash == Hash(request.Capability), ct);
    if (session is null)
      return CommandResult<Guid>.Fail("m365.setup.denied", "The setup authorization is invalid or expired.");
    var draft = await db.Microsoft365SetupDrafts.SingleOrDefaultAsync(x =>
      x.SetupSessionId == session.Id && x.FirmId == session.FirmId, ct);
    if (draft is null) return CommandResult<Guid>.Fail("m365.setup.not-found", "The setup draft was not found.");
    if (draft.Revision != request.ExpectedRevision)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The setup draft changed; reload it before saving.");

    draft.ExpectedTenantId = Trim(request.ExpectedTenantId);
    draft.TenantDisplayName = Trim(request.TenantDisplayName);
    draft.SiteUrl = Trim(request.SiteUrl);
    draft.SiteId = Trim(request.SiteId);
    draft.DriveId = Trim(request.DriveId);
    draft.RootFolderId = Trim(request.RootFolderId);
    draft.AccessProfile = request.AccessProfile;
    draft.MailState = NormalizeCapabilityState(request.MailState);
    draft.RecordsState = NormalizeCapabilityState(request.RecordsState);
    draft.Revision++;
    draft.UpdatedAt = now;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(draft.Id);
  }

  private static string NormalizeCapabilityState(string value) =>
    value.Trim().ToUpperInvariant() is "NOT_CONFIGURED" or "CONFIGURED" ? value.Trim().ToUpperInvariant() : "NOT_CONFIGURED";

  private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
  private static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
  private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
  private static bool IsSha256(string value) => value.Trim().Length == 64 && value.Trim().All(Uri.IsHexDigit);
}
