using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

/// <summary>Public identity of verified authorization evidence, never proof of send or delivery.</summary>
public sealed record MailAuthorizationEvidenceStatus(Guid ReceiptId, string Model, string AttestationSha256,
  string BindingSha256, DateTimeOffset ExpiresAtUtc, Guid TenantId, Guid RuntimeClientId,
  Guid ConnectionRevisionId, Guid ConsentAttemptId, string ProviderSnapshotSha256, string PlanSha256,
  string SourceCommit, IReadOnlyList<Guid> SenderMailboxObjectIds, Guid ControlMailboxObjectId,
  DateTimeOffset EvidenceObservedAtUtc);

/// <summary>Enrolls exact public CMS evidence; private keys and provider effects remain outside the API.</summary>
public static class MailAuthorizationEvidenceEnrollment
{
  public const string LegacyModel = "LEGACY_APPLICATION_ACCESS_POLICY";

  public static bool Available(TenantAdministrationOptions options) => options.OutboundMailEnabled &&
    options.MailEvidenceEnrollmentEnabled && options.MailAuthorization is
      { PermissionModel: MailAuthorizationModel.LegacyApplicationAccessPolicy } runtime &&
    Guid.TryParse(options.TenantId, out var tenantId) && tenantId == runtime.TenantId;

  public static async Task<CommandResult<Guid>> EnrollAsync(IAuditSphereDbContext db,
    IAuditSphereDbContextFactory factory, ActorContext actor, TenantAdministrationOptions options,
    MailAuthorizationAttestationBundle bundle, TimeProvider clock, CancellationToken ct = default)
  {
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      return TenantAdministration.Denied<Guid>();
    if (!Available(options) || options.MailAuthorization!.FirmId != actor.FirmId)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Deployment-bound mail evidence enrollment is not configured.");
    if (bundle.CanonicalPayload is not { Length: >= 2 and <= MailAuthorizationAttestationCanonicalizer.MaximumPayloadBytes } ||
        bundle.DetachedCms is not { Length: >= 1 and <= MailAuthorizationAttestationCanonicalizer.MaximumCmsBytes })
      return CommandResult<Guid>.Fail("request.invalid", "The public signed evidence exceeds its bounds.");
    var bytes = new MailAuthorizationAttestationBundle(bundle.CanonicalPayload.ToArray(), bundle.DetachedCms.ToArray());
    var runtime = options.MailAuthorization!;
    var gate = new MailAuthorizationRuntimeGate(factory, new UploadedEvidence(bytes), runtime, clock);
    MailAuthorizationRuntimeReceipt receipt;
    try
    {
      runtime.Validate();
      receipt = await gate.RequireCurrentAsync(actor.FirmId, runtime.TenantId, runtime.RuntimeClientId,
        runtime.SenderMailboxObjectIds[0], ct);
    }
    catch (Exception ex) when (ex is OperationBlockedException or InvalidOperationException)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The signature or current deployment/tenant binding is not verified.");
    }

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR SHARE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR SHARE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      return TenantAdministration.Denied<Guid>();
    var now = clock.GetUtcNow();
    try
    {
      var binding = await MailAuthorizationRuntimeGate.ReadBindingAsync(db, actor.FirmId, runtime.TenantId, ct);
      if (binding.ConnectionRevisionId != receipt.ConnectionRevisionId || binding.ConsentAttemptId != receipt.ConsentAttemptId ||
          receipt.ExpiresAtUtc is not { } expires || expires <= now)
        return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "Mail authorization evidence changed before enrollment.");
    }
    catch (OperationBlockedException)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "Mail authorization evidence changed before enrollment.");
    }
    var bindingDigest = MailAuthorizationRuntimeGate.DeploymentBindingDigest(runtime,
      receipt.ConnectionRevisionId, receipt.ConsentAttemptId);
    async Task<Guid?> ExistingAsync() => await db.TenantCapabilityVerifications.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.MailAttestationSha256 == receipt.AttestationDigest &&
        x.MailBindingSha256 == bindingDigest).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
    async Task<CommandResult<Guid>> CurrentResultAsync(Guid id)
    {
      var current = await StatusAsync(db, actor.FirmId, options, clock.GetUtcNow(), ct);
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<Guid>();
      return current.AuthorizationEvidence?.ReceiptId == id ? CommandResult<Guid>.Ok(id) :
        CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "Signed evidence is retained but is not current. Refresh the persisted status.");
    }
    if (await ExistingAsync() is { } existing)
    {
      await tx.CommitAsync(ct);
      return await CurrentResultAsync(existing);
    }
    var row = new TenantCapabilityVerification
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, TenantId = runtime.TenantId.ToString("D"),
      ConnectionRevisionId = receipt.ConnectionRevisionId, ConsentAttemptId = receipt.ConsentAttemptId,
      Capability = Microsoft365Capabilities.OutboundMail, Permission = "Mail.Send",
      State = CapabilityVerificationStates.Verified, DiagnosticCode = "mail-authorization-evidence-enrolled",
      ObservedByUserId = actor.UserId, ObservedAt = now, MailAuthorizationModel = LegacyModel,
      MailAttestationSha256 = receipt.AttestationDigest, MailBindingSha256 = bindingDigest,
      MailProviderSnapshotSha256 = receipt.ProviderSnapshotSha256, MailEvidenceExpiresAtUtc = receipt.ExpiresAtUtc,
      MailCanonicalPayload = bytes.CanonicalPayload, MailDetachedCms = bytes.DetachedCms
    };
    db.TenantCapabilityVerifications.Add(row);
    TenantAdministration.AddEvent(db, actor, "MAIL_AUTHORIZATION_EVIDENCE_ENROLLED", now,
      "-", row.Id.ToString("D"), "Verified public signed authorization evidence; no mail sent", "RECORDED",
      targetTenantId: row.TenantId);
    try
    {
      await db.SaveChangesAsync(ct);
      var finalBinding = await MailAuthorizationRuntimeGate.ReadBindingAsync(db, actor.FirmId, runtime.TenantId, ct);
      if (finalBinding.ConnectionRevisionId != receipt.ConnectionRevisionId || finalBinding.ConsentAttemptId != receipt.ConsentAttemptId ||
          receipt.ExpiresAtUtc is not { } finalExpiry || finalExpiry <= clock.GetUtcNow())
      {
        await tx.RollbackAsync(ct);
        return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "Mail authorization evidence changed before commit.");
      }
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      {
        await tx.RollbackAsync(ct);
        return TenantAdministration.Denied<Guid>();
      }
      await tx.CommitAsync(ct);
      return await CurrentResultAsync(row.Id);
    }
    catch (OperationBlockedException)
    {
      await tx.RollbackAsync(ct);
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "Mail authorization evidence changed before commit.");
    }
    catch (DbUpdateException)
    {
      await tx.RollbackAsync(ct);
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<Guid>();
      return await ExistingAsync() is { } committed ? await CurrentResultAsync(committed) :
        CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "Reload the persisted mail evidence before retrying.");
    }
  }

  public static async Task<CapabilityStatus> StatusAsync(IAuditSphereDbContext db, Guid firmId,
    TenantAdministrationOptions options, DateTimeOffset now, CancellationToken ct = default)
  {
    var display = Microsoft365PermissionMatrix.Rows.Single(x => x.Capability == Microsoft365Capabilities.OutboundMail);
    CapabilityStatus Missing() => new(display.Capability, display.DisplayName, display.Permission, options.OutboundMailEnabled,
      options.OutboundMailEnabled ? TenantCapabilityService.NotVerified : TenantCapabilityService.Disabled,
      null, false, TenantCapabilityService.MailAuthorizationEvidenceRequired);
    if (!Available(options) || options.MailAuthorization!.FirmId != firmId) return Missing();
    var runtime = options.MailAuthorization!;
    var row = await db.TenantCapabilityVerifications.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.TenantId == runtime.TenantId.ToString("D") && x.Capability == display.Capability &&
        x.State == CapabilityVerificationStates.Verified && x.MailAuthorizationModel == LegacyModel &&
        x.MailCanonicalPayload != null && x.MailDetachedCms != null)
      .OrderByDescending(x => x.ObservedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    if (row is null || row.State != CapabilityVerificationStates.Verified || row.MailAuthorizationModel != LegacyModel ||
        row.MailCanonicalPayload is null || row.MailDetachedCms is null || row.MailEvidenceExpiresAtUtc <= now ||
        row.MailEvidenceExpiresAtUtc is null || row.ObservedAt > now || row.ObservedAt < now - options.MaxAge)
      return Missing();
    try
    {
      runtime.Validate();
      var binding = await MailAuthorizationRuntimeGate.ReadBindingAsync(db, firmId, runtime.TenantId, ct);
      var expected = new MailAuthorizationAttestationExpectedBinding(runtime.TenantId, runtime.PermissionModel,
        runtime.RuntimeClientId, runtime.EnterpriseServicePrincipalObjectId, binding.ConnectionRevisionId,
        binding.ConsentAttemptId, runtime.PlanSha256, runtime.Source, runtime.SenderMailboxObjectIds, runtime.ControlMailboxObjectId);
      var attestation = MailAuthorizationAttestationVerifier.VerifyBound(row.MailCanonicalPayload, row.MailDetachedCms,
        new(runtime.PinnedSignerCertificateSha256, runtime.MaximumEvidenceAge), expected, now);
      var digest = MailAuthorizationRuntimeGate.ComputeAttestationDigest(row.MailCanonicalPayload, row.MailDetachedCms);
      var bindingDigest = MailAuthorizationRuntimeGate.DeploymentBindingDigest(runtime, binding.ConnectionRevisionId, binding.ConsentAttemptId);
      if (row.ConnectionRevisionId != binding.ConnectionRevisionId || row.ConsentAttemptId != binding.ConsentAttemptId ||
          row.MailAttestationSha256 != digest || row.MailBindingSha256 != bindingDigest ||
          row.MailEvidenceExpiresAtUtc != attestation.ExpiresAtUtc || row.MailProviderSnapshotSha256 != attestation.ProviderSnapshotSha256 ||
          await MailAuthorizationRuntimeGate.ReadBindingAsync(db, firmId, runtime.TenantId, ct) != binding)
        return Missing();
      return new(display.Capability, display.DisplayName, display.Permission, true, CapabilityVerificationStates.Verified,
        row.ObservedAt, false, "mail-authorization-evidence-enrolled-not-delivery",
        new(row.Id, LegacyModel, digest, bindingDigest, attestation.ExpiresAtUtc, runtime.TenantId,
          runtime.RuntimeClientId, binding.ConnectionRevisionId, binding.ConsentAttemptId,
          attestation.ProviderSnapshotSha256, runtime.PlanSha256, runtime.ObservedRepositoryCommit,
          runtime.SenderMailboxObjectIds.Order().ToArray(), runtime.ControlMailboxObjectId, attestation.ObservedAtUtc));
    }
    catch (Exception ex) when (ex is MailAuthorizationAttestationException or OperationBlockedException or InvalidOperationException)
    {
      return Missing();
    }
  }

  private sealed class UploadedEvidence(MailAuthorizationAttestationBundle bundle) : IMailAuthorizationAttestationSource
  {
    public Task<MailAuthorizationAttestationBundle> ReadAsync(CancellationToken ct) => Task.FromResult(bundle);
  }
}
