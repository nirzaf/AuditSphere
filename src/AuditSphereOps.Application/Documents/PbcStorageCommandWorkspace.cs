using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

/// <summary>
/// Exact reviewed SharePoint-primary storage commands for one PBC upload: activate or revert remote reads, release the
/// staged local copy, or backfill a registration. Any SharePoint read-back happens before the transaction; the effect
/// and an immutable actor-owned receipt are written together, and staged files are deleted only after commit.
/// </summary>
public static class PbcStorageCommandWorkspace
{
  private const int MaxReason = 500;
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static string Long(long x) => x.ToString(CultureInfo.InvariantCulture);
  private static bool Hash(string? x) => x is { Length: 64 } && x.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

  private static PbcStorageCommandInput? Canonical(PbcStorageCommandInput? input)
  {
    var reason = input?.Reason?.Trim();
    return input is not null && input.UploadIntentId != Guid.Empty && !string.IsNullOrEmpty(reason) && reason.Length <= MaxReason &&
      !reason.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) &&
      input.Action is PbcStorageCommandActions.ActivateRemoteRead or PbcStorageCommandActions.RevertToLocalRead or
        PbcStorageCommandActions.ReleaseLocalCopy or PbcStorageCommandActions.BackfillRegistration
      ? input with { Reason = reason } : null;
  }

  private static string RequestHash(ActorContext a, Guid requestId, string reviewBasis, PbcStorageCommandInput input) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { Version = 1, Namespace = "pbc-storage-command-v1", a.FirmId, a.UserId,
      a.SessionEpoch, RequestId = requestId, ReviewBasis = reviewBasis, Input = input }));

  private static async Task LockRequestAsync(IAuditSphereDbContext db, ActorContext a, Guid requestId, bool publish, CancellationToken ct)
  {
    var digest = Hashing.Sha256Hex(JsonSerializer.Serialize(new { Namespace = "pbc-storage-command-request-v1", a.FirmId, a.UserId, requestId }));
    var key = unchecked((long)Convert.ToUInt64(digest[..16], 16));
    if (publish) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    else await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({key})", ct);
  }

  public static async Task<CommandResult<PbcStorageCommandState>> StateAsync(IAuditSphereDbContext db, ActorContext a,
    PbcLocalCopyCleanupOptions options, Guid uploadIntentId, CancellationToken ct = default)
  {
    if (options is null || uploadIntentId == Guid.Empty)
      return Fail<PbcStorageCommandState>(ErrorCodes.Operations.Invalid, "Provide an exact upload identity.");
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, a, ct)) return TenantAdministration.Denied<PbcStorageCommandState>();
    var intent = await db.PbcUploadIntents.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == uploadIntentId, ct);
    var cleanup = intent is null ? null : await PbcService.EvaluateLocalCopyAsync(db, a.FirmId, uploadIntentId, options, ct);
    var backfill = intent is null ? null : await PbcService.EvaluateBackfillAsync(db, a.FirmId, uploadIntentId, ct);
    if (intent is null || cleanup is null || backfill is null) return TenantAdministration.Denied<PbcStorageCommandState>();
    var registration = await db.PbcDocumentRegistrations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == a.FirmId && x.PbcUploadIntentId == intent.Id, ct);
    var disposition = await db.PbcStorageDispositions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == a.FirmId && x.PbcUploadIntentId == intent.Id, ct);
    var requestState = await db.PbcRequests.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.Id == intent.PbcRequestId)
      .Select(x => x.State).SingleOrDefaultAsync(ct);
    var snapshot = new PbcStorageSnapshot(intent.Id, intent.ClientId, intent.EngagementId, intent.FileName, intent.State,
      requestState, intent.FinalSha256Hex ?? string.Empty, Long(intent.DeclaredByteCount), registration?.Id,
      registration?.ProviderIdentity, registration is null ? null : PbcSelectedSiteIdentity.Parse(registration.ProviderIdentity)?.VersionId,
      disposition?.ReadSource ?? PbcReadSources.LocalStaged, disposition?.LocalCopy ?? PbcLocalCopyStates.Retained,
      Long(disposition?.Revision ?? 0), options.Enabled, cleanup.Item.Disposition, cleanup.Item.PathClass, cleanup.Item.Effect,
      Long(cleanup.Item.ChunkCount), backfill.Value.Item.Disposition);

    var activate = new List<string>();
    if (registration is null) activate.Add("Register the verified SharePoint version before reading from it.");
    if (intent.State != PbcUploadStates.Received) activate.Add("Only a received upload can change its read source.");
    if (snapshot.ReadSource == PbcReadSources.SelectedSite) activate.Add("Remote reads are already active for this upload.");
    var revert = new List<string>();
    if (snapshot.ReadSource != PbcReadSources.SelectedSite) revert.Add("Remote reads are not active for this upload.");
    if (snapshot.LocalCopy == PbcLocalCopyStates.Released)
      revert.Add("The local copy was released; this upload can only be read from SharePoint.");
    var release = new List<string>();
    if (!options.Enabled) release.Add("Local-copy cleanup is disabled for this deployment.");
    if (snapshot.CleanupDisposition != PbcService.LocalCopyEligible)
      release.Add($"Not eligible for local-copy cleanup: {snapshot.CleanupDisposition}.");
    var backfillBlockers = new List<string>();
    if (snapshot.BackfillDisposition != PbcService.RegistrationEligible)
      backfillBlockers.Add($"Not eligible for registration backfill: {snapshot.BackfillDisposition}.");

    var basis = Hashing.Sha256Hex(JsonSerializer.Serialize(new { Version = 1, Namespace = "pbc-storage-state-v1", a.FirmId,
      a.UserId, a.SessionEpoch, Snapshot = snapshot, activate, revert, release, backfillBlockers }));
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, a, ct)) return TenantAdministration.Denied<PbcStorageCommandState>();
    return CommandResult<PbcStorageCommandState>.Ok(new(snapshot, basis, activate.Count == 0, revert.Count == 0,
      release.Count == 0, backfillBlockers.Count == 0, activate, revert, release, backfillBlockers));
  }

  public static async Task<CommandResult<PbcStorageCommandPreview>> PreviewAsync(IAuditSphereDbContext db, ActorContext a,
    PbcLocalCopyCleanupOptions options, PbcStorageCommandRequest? request, CancellationToken ct = default)
  {
    var input = Canonical(request?.Input);
    if (request is null || request.RequestId == Guid.Empty || input is null || !Hash(request.ReviewBasis) ||
        JsonSerializer.Serialize(input).Length > PbcStorageCommandReceipt.MaximumInputLength)
      return Fail<PbcStorageCommandPreview>(ErrorCodes.Operations.Invalid, "Provide the exact bounded storage command intent.");
    var state = await StateAsync(db, a, options, input.UploadIntentId, ct);
    if (!state.Succeeded) return Fail<PbcStorageCommandPreview>(state.ErrorCode!, state.Message!);
    var s = state.Value!;
    if (s.ReviewBasis != request.ReviewBasis)
      return Fail<PbcStorageCommandPreview>(ErrorCodes.StaleRevision, "The upload's storage state or your authority changed; refresh and review again.");
    var blockers = input.Action switch
    {
      PbcStorageCommandActions.ActivateRemoteRead => s.ActivateBlockers,
      PbcStorageCommandActions.RevertToLocalRead => s.RevertBlockers,
      PbcStorageCommandActions.ReleaseLocalCopy => s.ReleaseBlockers,
      _ => s.BackfillBlockers
    };
    var b = s.Snapshot;
    var summary = input.Action switch
    {
      PbcStorageCommandActions.ActivateRemoteRead =>
        $"Read {b.FileName} from its registered SharePoint version {b.VersionId}. The staged copy is retained and reads can be reverted.",
      PbcStorageCommandActions.RevertToLocalRead =>
        $"Return {b.FileName} to its retained staged copy. The SharePoint registration is unchanged.",
      PbcStorageCommandActions.ReleaseLocalCopy =>
        $"Delete the {b.ChunkCount} retained staged chunk files of {b.FileName} after a fresh verified read of SharePoint version {b.VersionId}. Reads cannot return to a local copy afterwards.",
      _ => $"Register {b.FileName} from its completed transfer receipt after an exact SharePoint read-back. Upload and acceptance evidence are unchanged."
    };
    var preview = new PbcStorageCommandPreview(request.RequestId, RequestHash(a, request.RequestId, s.ReviewBasis, input),
      s.ReviewBasis, input, b, blockers.Count == 0, blockers, summary);
    if (JsonSerializer.Serialize(preview).Length > PbcStorageCommandReceipt.MaximumPreviewLength)
      return Fail<PbcStorageCommandPreview>(ErrorCodes.Operations.Invalid, "The storage preview exceeds the retained evidence limit.");
    return CommandResult<PbcStorageCommandPreview>.Ok(preview);
  }

  public static async Task<CommandResult<PbcStorageCommandLookup>> LookupAsync(IAuditSphereDbContext db, ActorContext a,
    Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !Hash(requestHash))
      return Fail<PbcStorageCommandLookup>(ErrorCodes.Operations.Invalid, "Use the exact retained request reference.");
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, a, ct)) return TenantAdministration.Denied<PbcStorageCommandLookup>();
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await LockRequestAsync(db, a, requestId, false, ct);
    var row = await db.PbcStorageCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == requestId, ct);
    if (row is not null && row.ActorEpoch != a.SessionEpoch)
      return Fail<PbcStorageCommandLookup>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
    if (row is not null && row.RequestHash != requestHash)
      return Fail<PbcStorageCommandLookup>(ErrorCodes.IdempotencyConflict, "This request belongs to another exact intent.");
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, a, ct)) return TenantAdministration.Denied<PbcStorageCommandLookup>();
    await tx.CommitAsync(ct);
    return CommandResult<PbcStorageCommandLookup>.Ok(new(row is not null, row is null ? null : View(row, null)));
  }

  public static async Task<CommandResult<PbcStorageCommandReceiptView>> ExecuteAsync(IAuditSphereDbContext db, ActorContext a,
    PbcLocalCopyCleanupOptions options, IPbcRemoteContentReader reader, IPbcRegistrationVerifier verifier,
    PbcStorageCommandRequest? request, CancellationToken ct = default)
  {
    var input = Canonical(request?.Input);
    if (options is null || reader is null || verifier is null || request is not { Reviewed: true } ||
        request.RequestId == Guid.Empty || input is null || !Hash(request.ReviewBasis) || !Hash(request.ExpectedRequestHash) ||
        JsonSerializer.Serialize(input).Length > PbcStorageCommandReceipt.MaximumInputLength)
      return Fail<PbcStorageCommandReceiptView>(ErrorCodes.Operations.Invalid, "Preview and explicitly confirm the exact storage command.");
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, a, ct)) return TenantAdministration.Denied<PbcStorageCommandReceiptView>();
    request = request with { Input = input };

    // A retained receipt is recovered without another provider read.
    if (await db.PbcStorageCommandReceipts.AsNoTracking().AnyAsync(x =>
          x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == request.RequestId, ct))
      return await ReplayAsync(db, a, options, request, input, ct);

    var preview = await PreviewAsync(db, a, options, request, ct);
    if (!preview.Succeeded) return Fail<PbcStorageCommandReceiptView>(preview.ErrorCode!, preview.Message!);
    if (preview.Value!.RequestHash != request.ExpectedRequestHash)
      return Fail<PbcStorageCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The submitted intent differs from its exact preview hash.");
    if (!preview.Value.CanProceed) return Fail<PbcStorageCommandReceiptView>(ErrorCodes.GateBlocked, preview.Value.Blockers[0]);

    // SharePoint read-back happens before the transaction; the locked effect rechecks the same facts.
    PbcService.LocalCopyFacts? releaseFacts = null;
    PbcService.BackfillFacts? backfillFacts = null;
    if (input.Action == PbcStorageCommandActions.ReleaseLocalCopy)
    {
      releaseFacts = await PbcService.EvaluateLocalCopyAsync(db, a.FirmId, input.UploadIntentId, options, ct);
      if (releaseFacts?.Source is null) return Fail<PbcStorageCommandReceiptView>(ErrorCodes.StaleRevision, "The upload's storage state changed.");
      var remote = await reader.ReadVerifiedAsync(releaseFacts.Source, ct);
      if (!remote.Succeeded)
        return Fail<PbcStorageCommandReceiptView>(remote.ErrorCode!, "The registered SharePoint version could not be verified; no file was removed.");
      await remote.Value!.DisposeAsync();
    }
    else if (input.Action == PbcStorageCommandActions.BackfillRegistration)
    {
      backfillFacts = await PbcService.EvaluateBackfillAsync(db, a.FirmId, input.UploadIntentId, ct);
      if (backfillFacts?.Candidate is null) return Fail<PbcStorageCommandReceiptView>(ErrorCodes.StaleRevision, "The upload's storage state changed.");
      var verified = await verifier.VerifyUnregisteredAsync(backfillFacts.Value.Candidate, ct);
      if (!verified.Succeeded)
        return Fail<PbcStorageCommandReceiptView>(verified.ErrorCode!, "The SharePoint version could not be verified; nothing was registered.");
    }

    PbcStorageCommandReceiptView? view = null;
    await using (var tx = await db.Database.BeginTransactionAsync(ct))
    {
      try
      {
        await LockRequestAsync(db, a, request.RequestId, true, ct);
        // An identical request that committed while this one waited is recovered after this transaction ends.
        if (!await db.PbcStorageCommandReceipts.AsNoTracking().AnyAsync(x =>
              x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == request.RequestId, ct))
        {
        var locked = await db.PbcUploadIntents.FromSqlInterpolated($"""
          SELECT * FROM pbc_upload_intents WHERE firm_id = {a.FirmId} AND id = {input.UploadIntentId} FOR UPDATE
          """).AsNoTracking().ToListAsync(ct);
        if (locked.Count != 1) return TenantAdministration.Denied<PbcStorageCommandReceiptView>();
        var current = await PreviewAsync(db, a, options, request, ct);
        if (!current.Succeeded) return Fail<PbcStorageCommandReceiptView>(current.ErrorCode!, current.Message!);
        if (current.Value!.RequestHash != request.ExpectedRequestHash || !current.Value.CanProceed)
          return Fail<PbcStorageCommandReceiptView>(ErrorCodes.StaleRevision, "The reviewed storage state changed.");
        var before = current.Value.Before;

        PbcStorageCommandOutcome outcome;
        Guid? eventId = null, registrationId = null;
        switch (input.Action)
        {
          case PbcStorageCommandActions.ActivateRemoteRead:
          case PbcStorageCommandActions.RevertToLocalRead:
          {
            var target = input.Action == PbcStorageCommandActions.ActivateRemoteRead ? PbcReadSources.SelectedSite : PbcReadSources.LocalStaged;
            var changed = await PbcService.ChangeReadSourceInTransactionAsync(db, a,
              new PbcReadDispositionRequest(input.UploadIntentId, before.RegistrationId ?? Guid.Empty,
                long.Parse(before.DispositionRevision, CultureInfo.InvariantCulture), input.Reason), target, input.Reason, ct);
            if (!changed.Result.Succeeded) return Fail<PbcStorageCommandReceiptView>(changed.Result.ErrorCode!, changed.Result.Message!);
            if (changed.Event is null) return Fail<PbcStorageCommandReceiptView>(ErrorCodes.StaleRevision, "The reviewed storage state changed.");
            eventId = changed.Event.Id;
            outcome = new(changed.Event.ReadSource, changed.Event.LocalCopy, Long(changed.Event.Revision), eventId, null);
            break;
          }
          case PbcStorageCommandActions.ReleaseLocalCopy:
          {
            var recorded = await PbcService.RecordLocalCopyReleaseInTransactionAsync(db, a, options, releaseFacts!, input.Reason, ct);
            if (!recorded.Result.Succeeded) return Fail<PbcStorageCommandReceiptView>(recorded.Result.ErrorCode!, recorded.Result.Message!);
            releaseFacts = recorded.Facts;
            eventId = recorded.Event!.Id;
            outcome = new(recorded.Event.ReadSource, recorded.Event.LocalCopy, Long(recorded.Event.Revision), eventId, null);
            break;
          }
          default:
          {
            var added = await PbcService.BackfillInTransactionAsync(db, a, backfillFacts!.Value, ct);
            if (!added.Result.Succeeded) return Fail<PbcStorageCommandReceiptView>(added.Result.ErrorCode!, added.Result.Message!);
            if (added.Registration is null) return Fail<PbcStorageCommandReceiptView>(ErrorCodes.StaleRevision, "The reviewed storage state changed.");
            registrationId = added.Registration.Id;
            outcome = new(null, null, null, null, registrationId);
            break;
          }
        }

        var row = new PbcStorageCommandReceipt
        {
          Id = Guid.CreateVersion7(), FirmId = a.FirmId, ClientId = before.ClientId, EngagementId = before.EngagementId,
          PbcUploadIntentId = input.UploadIntentId, PbcStorageEventId = eventId, PbcDocumentRegistrationId = registrationId,
          ActorId = a.UserId, ActorEpoch = a.SessionEpoch, RequestId = request.RequestId, RequestHash = request.ExpectedRequestHash!,
          ReviewBasis = request.ReviewBasis!, Action = input.Action, InputJson = JsonSerializer.Serialize(input),
          PreviewJson = JsonSerializer.Serialize(current.Value), OutcomeJson = JsonSerializer.Serialize(outcome),
          CreatedAt = DateTimeOffset.UtcNow
        };
        db.PbcStorageCommandReceipts.Add(row);
        await db.SaveChangesAsync(ct);
        // The first response uses the same stored row (microsecond timestamps) as later recovery.
        var stored = await db.PbcStorageCommandReceipts.AsNoTracking().SingleAsync(x => x.FirmId == a.FirmId && x.Id == row.Id, ct);
        if (!await TenantAdministration.IsCurrentAdministratorAsync(db, a, ct)) return TenantAdministration.Denied<PbcStorageCommandReceiptView>();
        await tx.CommitAsync(ct);
        view = View(stored, null);
        }
      }
      catch (Exception ex) when (ex.GetBaseException() is DbException cause &&
        cause.GetType().GetProperty("SqlState")?.GetValue(cause) is "40001" or "40P01" or "23505")
      {
        return Fail<PbcStorageCommandReceiptView>(ErrorCodes.IdempotencyConflict,
          "A concurrent storage command changed the outcome. Look up the retained request before retrying.");
      }
    }
    if (view is null) return await ReplayAsync(db, a, options, request, input, ct);
    return input.Action == PbcStorageCommandActions.ReleaseLocalCopy
      ? CommandResult<PbcStorageCommandReceiptView>.Ok(view with { LocalCopyDeletion = Deletion(PbcService.DeleteStagedChunks(releaseFacts!, options)) })
      : CommandResult<PbcStorageCommandReceiptView>.Ok(view);
  }

  /// <summary>Returns the retained outcome of the identical request; a release replay retries any remaining deletes.</summary>
  private static async Task<CommandResult<PbcStorageCommandReceiptView>> ReplayAsync(IAuditSphereDbContext db, ActorContext a,
    PbcLocalCopyCleanupOptions options, PbcStorageCommandRequest request, PbcStorageCommandInput input, CancellationToken ct)
  {
    PbcStorageCommandReceiptView view;
    await using (var tx = await db.Database.BeginTransactionAsync(ct))
    {
      await LockRequestAsync(db, a, request.RequestId, true, ct);
      var prior = await db.PbcStorageCommandReceipts.AsNoTracking().SingleAsync(x =>
        x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == request.RequestId, ct);
      if (prior.ActorEpoch != a.SessionEpoch)
        return Fail<PbcStorageCommandReceiptView>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
      if (prior.Action != input.Action || prior.PbcUploadIntentId != input.UploadIntentId ||
          prior.RequestHash != request.ExpectedRequestHash || prior.ReviewBasis != request.ReviewBasis ||
          prior.InputJson != JsonSerializer.Serialize(input) ||
          RequestHash(a, request.RequestId, prior.ReviewBasis, input) != prior.RequestHash)
        return Fail<PbcStorageCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, a, ct)) return TenantAdministration.Denied<PbcStorageCommandReceiptView>();
      await tx.CommitAsync(ct);
      view = View(prior, null);
    }
    if (input.Action != PbcStorageCommandActions.ReleaseLocalCopy) return CommandResult<PbcStorageCommandReceiptView>.Ok(view);
    var facts = await PbcService.EvaluateLocalCopyAsync(db, a.FirmId, input.UploadIntentId, options, ct);
    var deletion = facts is null || facts.Item.Disposition != PbcService.LocalCopyReleased
      ? new PbcStorageLocalCopyDeletion(0, 0, "pbc.cleanup-state-changed")
      : facts.Item.PathClass is PbcLocalCopyPathClasses.OutsideAllowlist or PbcLocalCopyPathClasses.Link
        ? new PbcStorageLocalCopyDeletion(0, 0, "pbc.cleanup-path-" + facts.Item.PathClass.ToLowerInvariant().Replace('_', '-'))
        : Deletion(PbcService.DeleteStagedChunks(facts, options));
    return CommandResult<PbcStorageCommandReceiptView>.Ok(view with { LocalCopyDeletion = deletion });
  }

  private static PbcStorageLocalCopyDeletion Deletion(CommandResult<PbcLocalCopyReleaseReceipt> result) => result.Succeeded
    ? new(result.Value!.DeletedChunks, result.Value.AlreadyAbsentChunks, null)
    : new(0, 0, result.ErrorCode);

  private static PbcStorageCommandReceiptView View(PbcStorageCommandReceipt r, PbcStorageLocalCopyDeletion? deletion) =>
    new(r.Id, r.PbcUploadIntentId, r.ActorId, Long(r.ActorEpoch), r.RequestId, r.RequestHash, r.ReviewBasis, r.Action,
      r.CreatedAt.ToUniversalTime(), JsonSerializer.Deserialize<PbcStorageCommandPreview>(r.PreviewJson)!,
      JsonSerializer.Deserialize<PbcStorageCommandOutcome>(r.OutcomeJson)!, deletion);
}
