using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Completion;

public sealed record CreateReleaseCandidateRequest(
  Guid ApprovalId,
  string TargetKind,
  Guid TargetId,
  long ExpectedTargetRevision,
  long ExpectedInputGeneration,
  long ExpectedPolicyGeneration,
  string ManifestDigest);

public sealed record IssueReleaseRequest(
  Guid CandidateId,
  long ExpectedCandidateRevision,
  string ManifestDigest,
  string AuthorizedReleaseKey);

/// <summary>Local, provider-free release fencing for the supported workpaper slice.</summary>
public static class ReleaseService
{
  private static readonly string[] ReleaseRoles = ["Partner", "Administrator"];

  public static async Task<CommandResult<Guid>> CreateCandidateAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    CreateReleaseCandidateRequest request,
    CancellationToken ct = default)
  {
    var invalid = ValidateCandidateRequest(request);
    if (invalid is not null)
      return CommandResult<Guid>.Fail("release.invalid", invalid);

    var target = await LoadWorkpaperAsync(db, actor.FirmId, request.TargetId, false, ct);
    if (target is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, target, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    var approval = await db.Approvals.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.ApprovalId && x.FirmId == actor.FirmId, ct);
    if (approval is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var approvalCheck = CheckApproval(approval, target, request);
    if (approvalCheck is not null)
      return CommandResult<Guid>.Fail(approvalCheck.Value.Code, approvalCheck.Value.Message);

    var currentApproval = await ApprovalService.RequireCurrentAsync(db, actor, approval.Id, ct);
    if (!currentApproval.Succeeded)
      return CommandResult<Guid>.Fail(currentApproval.ErrorCode!, currentApproval.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var firm = await LockFirmAsync(db, actor.FirmId, ct);
    var client = await LockClientAsync(db, actor.FirmId, target.ClientId, ct);
    var current = await LoadWorkpaperAsync(db, actor.FirmId, target.Id, true, ct);
    if (firm is null || client is null || current is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Release scope is unavailable.");
    auth = await AuthorizeAsync(db, actor, current, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (current.Revision != request.ExpectedTargetRevision)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The release target changed; reload the candidate.");
    if (client.InputGeneration != request.ExpectedInputGeneration ||
        firm.PolicyGeneration != request.ExpectedPolicyGeneration)
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "Release inputs or policy changed; reload the candidate.");

    approval = await db.Approvals.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == request.ApprovalId, ct);
    if (approval is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    approvalCheck = CheckApproval(approval, current, request);
    if (approvalCheck is not null)
      return CommandResult<Guid>.Fail(approvalCheck.Value.Code, approvalCheck.Value.Message);
    var applicability = await db.ApprovalApplicabilities.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ApprovalId == approval.Id, ct);
    if (applicability is null || applicability.Status != ApprovalStates.Current)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The approval is not currently applicable.");

    var existing = await db.ReleaseCandidates.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.TargetKind == "WORKPAPER" && x.TargetId == current.Id &&
      x.TargetRevision == current.Revision && x.ManifestDigest == request.ManifestDigest, ct);
    if (existing is not null)
    {
      return existing.ApprovalId == approval.Id && existing.ClientId == current.ClientId &&
             existing.EngagementId == current.EngagementId && existing.InputGeneration == client.InputGeneration &&
             existing.PolicyGeneration == firm.PolicyGeneration
        ? await CommitExistingAsync(tx, existing.Id, ct)
        : CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The candidate identity is already bound to different approval evidence.");
    }

    var candidate = new ReleaseCandidate
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = current.ClientId,
      EngagementId = current.EngagementId, TargetId = current.Id, TargetRevision = current.Revision,
      InputGeneration = client.InputGeneration, PolicyGeneration = firm.PolicyGeneration,
      ApprovalId = approval.Id, ManifestDigest = request.ManifestDigest,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.ReleaseCandidates.Add(candidate);
    try
    {
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      return CommandResult<Guid>.Ok(candidate.Id);
    }
    catch (DbUpdateException)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The candidate identity changed; retry from the current candidate.");
    }
  }

  public static async Task<CommandResult<Guid>> IssueAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    IssueReleaseRequest request,
    ReleaseSafetyOptions? options = null,
    CancellationToken ct = default)
  {
    options ??= new ReleaseSafetyOptions();

    var invalid = ValidateIssueRequest(request);
    if (invalid is not null)
      return CommandResult<Guid>.Fail("release.invalid", invalid);

    var candidateSnapshot = await db.ReleaseCandidates.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.CandidateId && x.FirmId == actor.FirmId, ct);
    if (candidateSnapshot is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationAsync(db, actor, candidateSnapshot.ClientId, candidateSnapshot.EngagementId, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var firm = await LockFirmAsync(db, actor.FirmId, ct);
    var client = await LockClientAsync(db, actor.FirmId, candidateSnapshot.ClientId, ct);
    var candidate = await db.ReleaseCandidates.FromSqlInterpolated(
      $"SELECT * FROM release_candidates WHERE id = {request.CandidateId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (firm is null || client is null || candidate is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Release scope is unavailable.");

    var releaseKey = request.AuthorizedReleaseKey.Trim();
    var existing = await db.Releases.FromSqlInterpolated(
      $"SELECT * FROM releases WHERE firm_id = {actor.FirmId} AND authorized_release_key = {releaseKey} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (existing is not null)
    {
      if (existing.ReleaseCandidateId == candidate.Id && existing.ManifestDigest == request.ManifestDigest &&
          existing.PackageRevision == candidate.TargetRevision)
        return await CommitExistingAsync(tx, existing.Id, ct);
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The authorized release key is already bound to another release.");
    }

    if (candidate.Revision != request.ExpectedCandidateRevision)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The release candidate changed; reload it.");
    if (candidate.ManifestDigest != request.ManifestDigest)
      return CommandResult<Guid>.Fail(ErrorCodes.ManifestMismatch, "The release manifest does not match the candidate.");
    if (candidate.Status != ReleaseStates.Ready)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "The candidate is not ready for issuance.");
    if (firm.OperatingMode != "LOCAL_ONLY")
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Release is blocked while the firm is in recovery quarantine.");

    var checkpoint = await db.ReleaseCheckpoints.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == candidate.ClientId && x.EngagementId == candidate.EngagementId &&
      x.ReleaseCandidateId == candidate.Id && x.CandidateRevision == candidate.Revision &&
      x.ManifestDigest == request.ManifestDigest, ct);

    if (options.RequireExternalCheckpointBeforeDelivery)
    {
      if (checkpoint is null)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The external release checkpoint is absent.");
      if (checkpoint.VerifiedStatus != "VERIFIED" || checkpoint.VerifiedAt is null)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The external release checkpoint is not verified.");
      if (!string.Equals(checkpoint.ReadBackDigest, request.ManifestDigest, StringComparison.OrdinalIgnoreCase))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The external release checkpoint digest does not match manifest.");
    }

    if (options.RequireProtectionAttestation)
    {
      var attestation = await db.ProtectionAttestations.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == candidate.ClientId && x.EngagementId == candidate.EngagementId &&
        x.ArtifactHash == request.ManifestDigest, ct);

      if (attestation is null)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Protection attestation is absent.");
      if (attestation.ObservedState != "PROTECTED")
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Protection attestation is not in protected state.");
      if (attestation.ExpiryTime.HasValue && attestation.ExpiryTime.Value <= DateTimeOffset.UtcNow)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Protection attestation has expired.");
    }

    if (options.RequireSignatureLineage)
    {
      var lineage = await db.SignatureLineages.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == candidate.ClientId && x.EngagementId == candidate.EngagementId &&
        x.CandidateId == candidate.Id, ct);

      if (lineage is null)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Signature lineage is absent.");
      if (lineage.VerificationOutcome != "VERIFIED")
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Signature lineage verification failed.");
      if (!string.Equals(lineage.PreSignArtifactHash, request.ManifestDigest, StringComparison.OrdinalIgnoreCase))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Signature lineage pre-sign hash does not match candidate manifest.");
    }


    var current = await LoadWorkpaperAsync(db, actor.FirmId, candidate.TargetId, true, ct);
    if (current is null || current.ClientId != candidate.ClientId || current.EngagementId != candidate.EngagementId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    auth = await AuthorizeAsync(db, actor, current, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (current.Revision != candidate.TargetRevision)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The release target changed; re-review is required.");
    if (client.InputGeneration != candidate.InputGeneration || firm.PolicyGeneration != candidate.PolicyGeneration)
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "Release inputs or policy changed; re-review is required.");

    var approval = await db.Approvals.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == candidate.ApprovalId, ct);
    var applicability = approval is null ? null : await db.ApprovalApplicabilities.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ApprovalId == approval.Id, ct);
    if (approval is null || approval.Decision != ApprovalStates.Approved ||
        approval.TargetKind != candidate.TargetKind || approval.TargetId != candidate.TargetId ||
        approval.TargetRevision != candidate.TargetRevision || approval.InputGeneration != candidate.InputGeneration ||
        approval.PolicyGeneration != candidate.PolicyGeneration || approval.ManifestDigest != candidate.ManifestDigest ||
        applicability is null || applicability.Status != ApprovalStates.Current)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The required approval is not current for this release.");

    if (checkpoint is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Release checkpoint is required for release issuance.");

    var release = new Release
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = candidate.ClientId,
      EngagementId = candidate.EngagementId, ReleaseCandidateId = candidate.Id,
      PackageId = candidate.TargetId, PackageRevision = candidate.TargetRevision,
      ManifestDigest = candidate.ManifestDigest, AuthorizedReleaseKey = releaseKey,
      CheckpointId = checkpoint.Id, ReleasedAt = DateTimeOffset.UtcNow, ReleasedByUserId = actor.UserId
    };
    var payload = JsonSerializer.Serialize(new
    {
      releaseId = release.Id, candidateId = candidate.Id,
      manifestDigest = release.ManifestDigest, packageRevision = release.PackageRevision
    });
    var payloadBytes = Encoding.UTF8.GetBytes(payload);
    db.Releases.Add(release);
    db.DurableOperations.Add(new DurableOperation
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = candidate.ClientId,
      EngagementId = candidate.EngagementId, OperationKind = "ReleaseDelivery.v1",

      PayloadJson = payload, IdempotencyKey = "release:" + releaseKey,
      RequestDigest = Hashing.Sha256Hex(payloadBytes), RequestBytes = payloadBytes,
      Status = OperationState.PENDING, ExecutionMode = OperationMode.LOCAL,
      AuthorityMode = OperationAuthority.LOCAL_VALIDATION, TargetId = release.Id,
      ExpectedRevision = candidate.TargetRevision, CorrelationId = release.Id,
      OriginatorId = actor.UserId, NextAttemptAt = DateTimeOffset.UtcNow,
      CreatedAt = DateTimeOffset.UtcNow, ExecutionGroup = "release"
    });
    try
    {
      await db.SaveChangesAsync(ct);
      await db.ReleaseCandidates.Where(x => x.FirmId == actor.FirmId && x.Id == candidate.Id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ReleaseStates.Issued), ct);
      await tx.CommitAsync(ct);
      return CommandResult<Guid>.Ok(release.Id);
    }
    catch (DbUpdateException)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The release identity changed; retry from the current candidate.");
    }
  }

  private static async Task<CommandResult<Guid>> CommitExistingAsync(
    Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx,
    Guid id,
    CancellationToken ct)
  {
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(id);
  }

  private static Task<CommandResult> AuthorizeAsync(
    IAuditSphereDbContext db, ActorContext actor, Workpaper target, CancellationToken ct) =>
    AuthorizationAsync(db, actor, target.ClientId, target.EngagementId, ct);

  private static Task<CommandResult> AuthorizationAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, Guid engagementId, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, engagementId, ReleaseRoles, true, true), ct);

  private static Task<AuditSphereOps.Domain.Completion.FirmSafetyState?> LockFirmAsync(
    IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);

  private static Task<AuditSphereOps.Domain.Completion.ClientSafetyState?> LockClientAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, CancellationToken ct) =>
    db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE firm_id = {firmId} AND id = {clientId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);

  private static Task<Workpaper?> LoadWorkpaperAsync(
    IAuditSphereDbContext db, Guid firmId, Guid id, bool forUpdate, CancellationToken ct) =>
    forUpdate
      ? db.Workpapers.FromSqlInterpolated($"SELECT * FROM workpapers WHERE id = {id} AND firm_id = {firmId} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct)
      : db.Workpapers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == firmId, ct);

  private static (string Code, string Message)? CheckApproval(Approval approval, Workpaper target,
    CreateReleaseCandidateRequest request)
  {
    if (approval.Decision != ApprovalStates.Approved)
      return (ErrorCodes.GateBlocked, "A rejected approval cannot satisfy release.");
    if (approval.TargetKind != "WORKPAPER" || approval.TargetId != target.Id ||
        approval.ClientId != target.ClientId || approval.EngagementId != target.EngagementId)
      return (ErrorCodes.ScopeDenied, "Access denied.");
    if (approval.TargetRevision != request.ExpectedTargetRevision)
      return (ErrorCodes.StaleRevision, "The approval is for a different target revision.");
    if (approval.InputGeneration != request.ExpectedInputGeneration ||
        approval.PolicyGeneration != request.ExpectedPolicyGeneration)
      return (ErrorCodes.GenerationStale, "The approval is for stale release inputs or policy.");
    return approval.ManifestDigest == request.ManifestDigest
      ? null
      : (ErrorCodes.ManifestMismatch, "The approval manifest does not match the release candidate.");
  }

  private static string? ValidateCandidateRequest(CreateReleaseCandidateRequest request)
  {
    if (request.ApprovalId == Guid.Empty || request.TargetId == Guid.Empty ||
        !string.Equals(request.TargetKind.Trim(), "WORKPAPER", StringComparison.OrdinalIgnoreCase))
      return "Only a stored workpaper target is supported by this slice.";
    if (request.ExpectedTargetRevision < 1 || request.ExpectedInputGeneration < 1 || request.ExpectedPolicyGeneration < 1)
      return "Release target revision and generations must be positive.";
    return IsDigest(request.ManifestDigest) ? null : "The release manifest digest must be lowercase SHA-256 hex.";
  }

  private static string? ValidateIssueRequest(IssueReleaseRequest request)
  {
    if (request.CandidateId == Guid.Empty || request.ExpectedCandidateRevision < 1)
      return "A release candidate and positive candidate revision are required.";
    if (!IsDigest(request.ManifestDigest))
      return "The release manifest digest must be lowercase SHA-256 hex.";
    var key = request.AuthorizedReleaseKey.Trim();
    return key.Length is 0 or > 200 || key.Any(char.IsControl)
      ? "The authorized release key is invalid."
      : null;
  }

  private static bool IsDigest(string value) => value.Length == 64 &&
    value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
