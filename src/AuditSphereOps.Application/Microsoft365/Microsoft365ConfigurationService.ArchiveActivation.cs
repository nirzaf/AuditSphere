using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

public sealed record ActivateArchiveResourceRequest(
  Guid SetupDraftId, long ExpectedDraftRevision, Guid ConnectionRevisionId,
  Guid CapabilityVerificationId, string TenantId, string SiteId, string DriveId,
  string RootFolderId, string DisplayUrl, string AccessProfile);

public static partial class Microsoft365ConfigurationService
{
  private const string ArchiveActivationOperation = "SELECTED_ARCHIVE_RESOURCE_ACTIVATED";

  /// <summary>
  /// Activates only an independently verified secondary archive resource. No working
  /// configuration, default, template, repository binding or provider effect is created.
  /// The returned identity is the connection revision, not a workspace identity.
  /// </summary>
  public static async Task<CommandResult<Guid>> ActivateArchiveResourceAsync(
    IAuditSphereDbContext db, ActorContext actor, ActivateArchiveResourceRequest request,
    DateTimeOffset now, CancellationToken ct = default)
  {
    if (request.SetupDraftId == Guid.Empty || request.ConnectionRevisionId == Guid.Empty ||
        request.CapabilityVerificationId == Guid.Empty || request.ExpectedDraftRevision is < 1 or long.MaxValue ||
        new[] { request.TenantId, request.SiteId, request.DriveId, request.RootFolderId, request.DisplayUrl }
          .Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 2000 || x.Any(char.IsControl)) ||
        !ArchiveCopyService.IsExactSharePointSiteUrl(request.DisplayUrl) ||
        request.AccessProfile is not (Microsoft365AccessProfiles.AppMediated or Microsoft365AccessProfiles.DirectStaffCollaboration))
      return CommandResult<Guid>.Fail("m365.activation.invalid", "Review the exact verified secondary archive resource and proof.");
    var auth = await FirmAdministratorAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    // Same order as role and session administration. Hold authority through commit,
    // and re-read after every potentially contended resource lock.
    auth = await LockSelectedResourceAdministratorAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var draft = await db.Microsoft365SetupDrafts.FromSqlInterpolated($"""
      SELECT * FROM m365_setup_drafts WHERE firm_id={actor.FirmId} AND id={request.SetupDraftId} FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    var connection = await db.Microsoft365ConnectionRevisions.FromSqlInterpolated($"""
      SELECT * FROM m365_connection_revisions WHERE firm_id={actor.FirmId} AND id={request.ConnectionRevisionId} FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    auth = await FirmAdministratorAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (draft is null || connection is null || draft.ConnectionRevisionId != connection.Id)
      return TenantAdministration.Denied<Guid>();
    if (draft.ExpectedTenantId != request.TenantId || connection.TenantId != request.TenantId ||
        draft.SiteId != request.SiteId || draft.DriveId != request.DriveId ||
        draft.RootFolderId != request.RootFolderId || draft.SiteUrl != request.DisplayUrl ||
        draft.AccessProfile != request.AccessProfile)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The reviewed archive binding changed.");
    var replay = draft.State == Microsoft365RevisionStates.Active;
    if (draft.Revision != request.ExpectedDraftRevision + (replay ? 1 : 0))
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "Reload the exact archive draft revision.");
    if (await ArchiveCopyService.UsesKnownWorkingDriveAsync(db, actor.FirmId, request.DriveId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The secondary archive must use a separate library from every configured working library.");

    SelectedResourceProof proof;
    try
    {
      proof = replay
        ? await ArchiveCopySourceGuard.ResolveArchiveActivationReplayProofAsync(db, actor.FirmId, draft, connection.Id, now, ct)
        : await ArchiveCopySourceGuard.ResolveArchiveActivationProofAsync(db, actor.FirmId, draft, connection.Id, now, ct);
    }
    catch (OperationBlockedException)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A fresh exact selected-resource boundary and verified consent are required.");
    }
    if (proof.Verification.Id != request.CapabilityVerificationId)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The reviewed selected-resource proof was superseded.");

    // Fixed-size provenance is compared as a whole, never interpreted as authority.
    // Existing event constraints reserve SetupRequest* exclusively for metadata edits.
    var provenance = JsonSerializer.Serialize(new
    {
      revisionBefore = request.ExpectedDraftRevision,
      revisionAfter = request.ExpectedDraftRevision + 1,
      verificationId = proof.Verification.Id,
      consentAttemptId = proof.Verification.ConsentAttemptId,
      evidenceSha256 = proof.EvidenceSha256,
      requestSha256 = Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(request))
    });
    if (provenance.Length > 600) throw new InvalidOperationException("Archive activation provenance exceeds its retained bound.");
    const string reason = "Administrator activated an exact verified secondary archive library; release store remains authoritative";
    if (replay)
    {
      if (connection.ApprovedByUserId != actor.UserId || connection.ActivatedAt is null ||
          !await db.Microsoft365AdministrationEvents.AsNoTracking().AnyAsync(x =>
            x.FirmId == actor.FirmId && x.ActorUserId == actor.UserId && x.Operation == ArchiveActivationOperation &&
            x.TargetTenantId == request.TenantId && x.TargetObjectId == draft.Id.ToString() &&
            x.Result == connection.Id.ToString() && x.OldState == Microsoft365RevisionStates.Verified &&
            x.NewState == Microsoft365RevisionStates.Active && x.Reason == reason &&
            x.RoleScopeChange == provenance && x.CreatedAt == connection.ActivatedAt, ct))
        return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "An active resource cannot be repurposed as an archive resource.");
    }
    else
    {
      TenantAdministration.AddEvent(db, actor, ArchiveActivationOperation, now,
        oldState: Microsoft365RevisionStates.Verified, newState: Microsoft365RevisionStates.Active,
        reason: reason, result: connection.Id.ToString(), targetTenantId: connection.TenantId,
        targetObjectId: draft.Id.ToString(), roleScopeChange: provenance);
      // Exact updates deliberately do not rely on potentially older caller-tracked entities.
      await db.Microsoft365ConnectionRevisions.Where(x => x.FirmId == actor.FirmId && x.Id == connection.Id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, Microsoft365RevisionStates.Active)
          .SetProperty(x => x.ApprovedByUserId, actor.UserId).SetProperty(x => x.ActivatedAt, now), ct);
      await db.Microsoft365SetupDrafts.Where(x => x.FirmId == actor.FirmId && x.Id == draft.Id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, Microsoft365RevisionStates.Active)
          .SetProperty(x => x.Revision, request.ExpectedDraftRevision + 1).SetProperty(x => x.UpdatedAt, now), ct);
    }
    auth = await FirmAdministratorAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(connection.Id);
  }

  private static async Task<CommandResult> LockSelectedResourceAdministratorAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct)
  {
    if (await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR UPDATE")
          .AsNoTracking().SingleOrDefaultAsync(ct) is null ||
        await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR UPDATE")
          .AsNoTracking().SingleOrDefaultAsync(ct) is null)
      return TenantAdministration.Denied();
    return await FirmAdministratorAsync(db, actor, ct);
  }

  private static Task<bool> IsActivatedArchiveDriveAsync(IAuditSphereDbContext db, Guid firmId,
    string driveId, CancellationToken ct) =>
    (from draft in db.Microsoft365SetupDrafts.AsNoTracking()
     join connection in db.Microsoft365ConnectionRevisions.AsNoTracking() on draft.ConnectionRevisionId equals connection.Id
     where draft.FirmId == firmId && connection.FirmId == firmId && draft.DriveId == driveId &&
       draft.State == Microsoft365RevisionStates.Active && connection.State == Microsoft365RevisionStates.Active
     where db.Microsoft365AdministrationEvents.Any(e => e.FirmId == firmId &&
       e.Operation == ArchiveActivationOperation && e.TargetObjectId == draft.Id.ToString() &&
       e.Result == connection.Id.ToString() && e.TargetTenantId == connection.TenantId &&
       e.ActorUserId == connection.ApprovedByUserId && e.CreatedAt == connection.ActivatedAt &&
       e.OldState == Microsoft365RevisionStates.Verified && e.NewState == Microsoft365RevisionStates.Active)
     select draft.Id).AnyAsync(ct);
}
