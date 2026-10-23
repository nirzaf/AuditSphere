using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Microsoft365;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

/// <summary>Detached CMS bytes read from the deployment-managed, read-only evidence source.</summary>
public sealed record MailAuthorizationAttestationBundle(byte[] CanonicalPayload, byte[] DetachedCms);

public interface IMailAuthorizationAttestationSource
{
  Task<MailAuthorizationAttestationBundle> ReadAsync(CancellationToken ct);
}

/// <summary>Deployment-owned expected values. None are learned from the signed CMS payload.</summary>
public sealed record MailAuthorizationRuntimeOptions(
  Guid FirmId,
  Guid TenantId,
  MailAuthorizationModel PermissionModel,
  Guid RuntimeClientId,
  Guid EnterpriseServicePrincipalObjectId,
  IReadOnlyList<Guid> SenderMailboxObjectIds,
  Guid ControlMailboxObjectId,
  string PlanSha256,
  MailAuthorizationSourceIdentity Source,
  string ObservedRepositoryCommit,
  string PinnedSignerCertificateSha256,
  TimeSpan MaximumEvidenceAge)
{
  public void Validate()
  {
    if (FirmId == Guid.Empty || TenantId == Guid.Empty || RuntimeClientId == Guid.Empty ||
        EnterpriseServicePrincipalObjectId == Guid.Empty || ControlMailboxObjectId == Guid.Empty ||
        PermissionModel is not (MailAuthorizationModel.LegacyApplicationAccessPolicy or
          MailAuthorizationModel.ExchangeRbacResourceScope) ||
        SenderMailboxObjectIds is null || SenderMailboxObjectIds.Count is < 1 or > MailAuthorizationAttestationCanonicalizer.MaximumSenders ||
        SenderMailboxObjectIds.Any(x => x == Guid.Empty) ||
        SenderMailboxObjectIds.Distinct().Count() != SenderMailboxObjectIds.Count ||
        SenderMailboxObjectIds.Contains(ControlMailboxObjectId) ||
        !MailAuthorizationAttestationCanonicalizer.IsSha256Value(PlanSha256) ||
        !MailAuthorizationAttestationCanonicalizer.IsValidSourceIdentity(Source) ||
        !IsFullRepositoryCommit(ObservedRepositoryCommit) ||
        !string.Equals(Source.RepositoryCommit, ObservedRepositoryCommit, StringComparison.Ordinal) ||
        !IsSha256Fingerprint(PinnedSignerCertificateSha256) ||
        !double.IsFinite(MaximumEvidenceAge.TotalHours) ||
        MaximumEvidenceAge < TimeSpan.FromHours(1) || MaximumEvidenceAge > TimeSpan.FromHours(168))
      throw new InvalidOperationException("Mail authorization runtime binding is incomplete or invalid.");
  }

  private static bool IsSha256Fingerprint(string? value) => value is { Length: 64 } &&
    value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

  private static bool IsFullRepositoryCommit(string? value) => value is { Length: 40 } &&
    value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// <summary>Safe identity of one currently verified receipt; it contains no CMS, provider JSON, or token bytes.</summary>
public sealed record MailAuthorizationRuntimeReceipt(
  MailAuthorizationModel PermissionModel,
  Guid TenantId,
  Guid RuntimeClientId,
  Guid ConnectionRevisionId,
  Guid ConsentAttemptId,
  Guid SenderMailboxObjectId,
  string ProviderSnapshotSha256,
  string AttestationDigest,
  DateTimeOffset? ExpiresAtUtc = null);

public interface IMailAuthorizationRuntimeGate
{
  Task<MailAuthorizationRuntimeReceipt> RequireCurrentAsync(Guid firmId, Guid tenantId, Guid runtimeClientId,
    Guid senderMailboxObjectId, CancellationToken ct);
}

/// <summary>
/// Verifies a detached signed provider snapshot against deployment-owned values and the firm's current connection
/// and consent rows. The CMS bundle is read afresh for every check; callers recheck after token validation and before
/// dispatch. This slice deliberately does not turn generic role-only capability rows into mail evidence.
/// </summary>
public sealed class MailAuthorizationRuntimeGate(
  IAuditSphereDbContextFactory factory,
  IMailAuthorizationAttestationSource source,
  MailAuthorizationRuntimeOptions options,
  TimeProvider clock) : IMailAuthorizationRuntimeGate
{
  public async Task<MailAuthorizationRuntimeReceipt> RequireCurrentAsync(Guid firmId, Guid tenantId,
    Guid runtimeClientId, Guid senderMailboxObjectId, CancellationToken ct)
  {
    options.Validate();
    if (firmId != options.FirmId || tenantId != options.TenantId || runtimeClientId != options.RuntimeClientId ||
        senderMailboxObjectId == Guid.Empty || !options.SenderMailboxObjectIds.Contains(senderMailboxObjectId) ||
        senderMailboxObjectId == options.ControlMailboxObjectId)
      throw Blocked();

    var initialBinding = await ReadCurrentAuthorizationBindingAsync(firmId, tenantId, ct);

    MailAuthorizationAttestationBundle bundle;
    MailAuthorizationAttestation attestation;
    string attestationDigest;
    try
    {
      bundle = await source.ReadAsync(ct);
      if (bundle is null || bundle.CanonicalPayload is null || bundle.DetachedCms is null)
        throw Blocked();
      var expected = new MailAuthorizationAttestationExpectedBinding(
        options.TenantId, options.PermissionModel, options.RuntimeClientId,
        options.EnterpriseServicePrincipalObjectId, initialBinding.ConnectionRevisionId, initialBinding.ConsentAttemptId,
        options.PlanSha256, options.Source, options.SenderMailboxObjectIds, options.ControlMailboxObjectId);
      attestation = MailAuthorizationAttestationVerifier.VerifyBound(
        bundle.CanonicalPayload, bundle.DetachedCms,
        new MailAuthorizationAttestationVerifierOptions(options.PinnedSignerCertificateSha256, options.MaximumEvidenceAge),
        expected, clock.GetUtcNow());
      attestationDigest = ComputeAttestationDigest(bundle.CanonicalPayload, bundle.DetachedCms);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (MailAuthorizationAttestationException)
    {
      throw Blocked();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      throw Blocked();
    }

    // The provider evidence read is file I/O and must not hold a database transaction. Re-read the
    // connection and latest consent in a fresh snapshot so a revocation or replacement during that read
    // cannot authorize a stale receipt.
    var currentBinding = await ReadCurrentAuthorizationBindingAsync(firmId, tenantId, ct);
    if (currentBinding != initialBinding)
      throw Blocked();

    return new MailAuthorizationRuntimeReceipt(attestation.ProviderSnapshot.PermissionModel,
      attestation.ProviderSnapshot.TenantId, attestation.ProviderSnapshot.RuntimeClientId,
      attestation.ConnectionRevisionId, attestation.ConsentAttemptId, senderMailboxObjectId,
      attestation.ProviderSnapshotSha256, attestationDigest, attestation.ExpiresAtUtc);
  }

  private async Task<CurrentAuthorizationBinding> ReadCurrentAuthorizationBindingAsync(Guid firmId, Guid tenantId,
    CancellationToken ct)
  {
    CurrentAuthorizationBinding binding;
    await using (var db = await factory.CreateAsync(ct))
    {
      await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
      {
        binding = await ReadBindingAsync(db, firmId, tenantId, ct);
        await snapshot.CommitAsync(ct);
      }
    }

    return binding;
  }

  internal static async Task<CurrentAuthorizationBinding> ReadBindingAsync(IAuditSphereDbContext db,
    Guid firmId, Guid tenantId, CancellationToken ct)
  {
    var connection = await db.Microsoft365ConnectionRevisions.AsNoTracking()
      .Where(x => x.FirmId == firmId)
      .OrderByDescending(x => x.Revision)
      .Select(x => new { x.Id, x.Revision, x.TenantId, x.State, x.ConsentState, x.VerifiedAt })
      .FirstOrDefaultAsync(ct);
    if (connection is null || connection.TenantId != tenantId.ToString("D") || connection.ConsentState != "VERIFIED" ||
        connection.State is Microsoft365RevisionStates.Blocked or Microsoft365RevisionStates.Suspended)
      throw Blocked();

    var consent = await db.TenantConsentAttempts.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.ExpectedTenantId == tenantId.ToString("D") &&
        db.Microsoft365SetupDrafts.Any(d => d.FirmId == firmId && d.Id == x.SetupDraftId &&
          d.ConnectionRevisionId == connection.Id && d.ExpectedTenantId == tenantId.ToString("D")))
      .OrderByDescending(x => x.CreatedAt)
      .ThenByDescending(x => x.Id)
      .Select(x => new
      {
        x.Id, x.SetupDraftId, x.State, x.CreatedAt, x.ConsentVerifiedAt,
        x.ConsentingTenantId, x.ConsentingObjectId, x.ApplicationClientId
      })
      .FirstOrDefaultAsync(ct);
    if (consent is null || consent.State != TenantConsentAttemptStates.ConsentVerified ||
        consent.ConsentVerifiedAt is null || consent.ConsentingTenantId != tenantId.ToString("D"))
      throw Blocked();

    return new CurrentAuthorizationBinding(connection.Id, connection.Revision, connection.TenantId,
      connection.State, connection.ConsentState, connection.VerifiedAt, consent.Id, consent.SetupDraftId, consent.State,
      consent.CreatedAt, consent.ConsentVerifiedAt, consent.ConsentingTenantId, consent.ConsentingObjectId,
      consent.ApplicationClientId);
  }

  internal sealed record CurrentAuthorizationBinding(
    Guid ConnectionRevisionId,
    long ConnectionRevision,
    string ConnectionTenantId,
    string ConnectionState,
    string ConnectionConsentState,
    DateTimeOffset? ConnectionVerifiedAt,
    Guid ConsentAttemptId,
    Guid ConsentSetupDraftId,
    string ConsentState,
    DateTimeOffset ConsentCreatedAt,
    DateTimeOffset? ConsentVerifiedAt,
    string? ConsentingTenantId,
    string? ConsentingObjectId,
    string ApplicationClientId);

  private static OperationBlockedException Blocked() =>
    new("mail-authorization-evidence-unavailable", authorization: true);

  internal static string ComputeAttestationDigest(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> cms)
  {
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    Span<byte> length = stackalloc byte[sizeof(int)];
    BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
    hash.AppendData(length);
    hash.AppendData(payload);
    BinaryPrimitives.WriteInt32BigEndian(length, cms.Length);
    hash.AppendData(length);
    hash.AppendData(cms);
    return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
  }

  internal static string DeploymentBindingDigest(MailAuthorizationRuntimeOptions options,
    Guid connectionRevisionId, Guid consentAttemptId) => TenantAdministration.Fingerprint(
      "mail-authorization-binding.v1", JsonSerializer.Serialize(new
      {
        options.FirmId, options.TenantId, options.PermissionModel, options.RuntimeClientId,
        options.EnterpriseServicePrincipalObjectId,
        SenderMailboxObjectIds = options.SenderMailboxObjectIds.Order().ToArray(),
        options.ControlMailboxObjectId, options.PlanSha256, options.Source, options.ObservedRepositoryCommit,
        PinnedSigner = options.PinnedSignerCertificateSha256.ToLowerInvariant(),
        MaximumEvidenceAgeTicks = options.MaximumEvidenceAge.Ticks, connectionRevisionId, consentAttemptId
      }));
}
