using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

/// <summary>
/// Deployment-owned local-copy cleanup policy. Cleanup is off unless explicitly enabled; the staging root is the
/// only directory whose files may ever be removed. A dry-run plan is available while cleanup stays disabled.
/// </summary>
public sealed record PbcLocalCopyCleanupOptions(bool Enabled, string StagingRoot)
{
  public static PbcLocalCopyCleanupOptions Disabled(string stagingRoot) => new(false, stagingRoot);
}

/// <summary>Where a planned upload's retained chunks are. Absolute paths are never part of a plan.</summary>
public static class PbcLocalCopyPathClasses
{
  public const string Allowlisted = "ALLOWLISTED";
  public const string Partial = "PARTIAL";
  public const string Absent = "ABSENT";
  public const string OutsideAllowlist = "OUTSIDE_ALLOWLIST";
  public const string Link = "LINK";
}

public static class PbcLocalCopyEffects
{
  public const string DeleteStagedChunks = "DELETE_STAGED_CHUNKS";
  public const string DeleteRemainingChunks = "DELETE_REMAINING_CHUNKS";
  public const string None = "NONE";
}

/// <summary>
/// One dry-run row: scoped identities, the exact registered SharePoint version the upload will depend on, the
/// chunk path class, why it is or is not eligible, and the effect a release would have.
/// </summary>
public sealed record PbcLocalCopyPlanItem(Guid UploadIntentId, Guid ClientId, Guid EngagementId, Guid RegistrationId,
  long DispositionRevision, string Sha256Hex, long ByteCount, string DriveId, string ItemId, string VersionId,
  int ChunkCount, string PathClass, string Disposition, string Effect);

public sealed record PbcLocalCopyReleaseRequest(Guid UploadIntentId, long ExpectedDispositionRevision,
  string ExpectedSha256Hex, string Reason);

public sealed record PbcLocalCopyReleaseReceipt(Guid UploadIntentId, long DispositionRevision, int DeletedChunks,
  int AlreadyAbsentChunks);

/// <summary>
/// Optional reclamation of temporary PBC staging bytes (SP-05). Only an upload already read from its exact registered
/// SharePoint version, with a final PBC decision and no active operation or hold, can be released, and only after a
/// fresh verified remote read. The release is recorded first and the staged files are deleted afterwards, so an
/// interrupted run is retried rather than leaving a local read pointing at missing bytes. PBC upload history,
/// SharePoint items and release/archive stores are never modified.
/// </summary>
public static partial class PbcService
{
  public const string LocalCopyEligible = "ELIGIBLE";
  public const string LocalCopyReleased = "RELEASED";
  private const int MaxLocalCopyPlanItems = 100;

  internal sealed record LocalCopyFacts(PbcLocalCopyPlanItem Item, PbcRemoteSource? Source, IReadOnlyList<string> Paths);

  /// <summary>Dry-run for an explicitly enumerated cohort; reads only local records and file metadata.</summary>
  public static async Task<CommandResult<IReadOnlyList<PbcLocalCopyPlanItem>>> PlanLocalCopyCleanupAsync(
    IAuditSphereDbContext db, ActorContext actor, PbcLocalCopyCleanupOptions options,
    IReadOnlyCollection<Guid> uploadIntentIds, CancellationToken ct = default)
  {
    if (options is null || uploadIntentIds is null || uploadIntentIds.Count is 0 or > MaxLocalCopyPlanItems ||
        uploadIntentIds.Contains(Guid.Empty))
      return CommandResult<IReadOnlyList<PbcLocalCopyPlanItem>>.Fail("pbc.cleanup-invalid",
        $"Plan between 1 and {MaxLocalCopyPlanItems} exact upload identities.");
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      return TenantAdministration.Denied<IReadOnlyList<PbcLocalCopyPlanItem>>();
    var items = new List<PbcLocalCopyPlanItem>();
    foreach (var id in uploadIntentIds.Distinct())
    {
      var facts = await EvaluateLocalCopyAsync(db, actor.FirmId, id, options, ct);
      if (facts is null) return TenantAdministration.Denied<IReadOnlyList<PbcLocalCopyPlanItem>>();
      items.Add(facts.Item);
    }
    return CommandResult<IReadOnlyList<PbcLocalCopyPlanItem>>.Ok(items);
  }

  /// <summary>
  /// Releases one eligible upload's staged copy. A replay with the post-release revision deletes any chunks left by an
  /// interrupted run and reports the rest as already absent.
  /// </summary>
  public static async Task<CommandResult<PbcLocalCopyReleaseReceipt>> ReleaseLocalCopyAsync(
    IAuditSphereDbContext db, ActorContext actor, PbcLocalCopyCleanupOptions options, IPbcRemoteContentReader reader,
    PbcLocalCopyReleaseRequest request, CancellationToken ct = default)
  {
    var reason = request?.Reason?.Trim();
    if (options is null || reader is null || request is null || request.UploadIntentId == Guid.Empty ||
        request.ExpectedDispositionRevision < 1 || string.IsNullOrEmpty(reason) || reason.Length > MaxDispositionReasonLength)
      return CommandResult<PbcLocalCopyReleaseReceipt>.Fail("pbc.cleanup-invalid",
        "An exact upload, its current disposition revision, its registered SHA-256 and a bounded reason are required.");
    if (!options.Enabled)
      return CommandResult<PbcLocalCopyReleaseReceipt>.Fail("pbc.cleanup-disabled",
        "Local-copy cleanup is disabled for this deployment; no file was removed.");
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      return TenantAdministration.Denied<PbcLocalCopyReleaseReceipt>();

    var facts = await EvaluateLocalCopyAsync(db, actor.FirmId, request.UploadIntentId, options, ct);
    if (facts is null) return TenantAdministration.Denied<PbcLocalCopyReleaseReceipt>();
    if (!string.Equals(facts.Item.Sha256Hex, request.ExpectedSha256Hex, StringComparison.Ordinal) ||
        facts.Item.DispositionRevision != request.ExpectedDispositionRevision)
      return StaleRelease();
    if (facts.Item.Disposition == LocalCopyReleased)
      return facts.Item.PathClass is PbcLocalCopyPathClasses.OutsideAllowlist or PbcLocalCopyPathClasses.Link
        ? CommandResult<PbcLocalCopyReleaseReceipt>.Fail("pbc.cleanup-path-" + facts.Item.PathClass.ToLowerInvariant().Replace('_', '-'),
          "The remaining staged chunks are not inside the allowlisted staging root; no file was removed.")
        : DeleteStagedChunks(facts, options);
    if (facts.Item.Disposition != LocalCopyEligible)
      return CommandResult<PbcLocalCopyReleaseReceipt>.Fail(facts.Item.Disposition,
        "This upload is not eligible for local-copy cleanup; no file was removed.");

    // The exact registered version must be retrievable and verified now, before its last local copy is removed.
    var remote = await reader.ReadVerifiedAsync(facts.Source!, ct);
    if (!remote.Succeeded)
      return CommandResult<PbcLocalCopyReleaseReceipt>.Fail(remote.ErrorCode!,
        "The registered SharePoint version could not be verified; no file was removed.");
    await remote.Value!.DisposeAsync();

    await using (var tx = await db.Database.BeginTransactionAsync(ct))
    {
      var recorded = await RecordLocalCopyReleaseInTransactionAsync(db, actor, options, facts, reason, ct);
      if (!recorded.Result.Succeeded) return CommandResult<PbcLocalCopyReleaseReceipt>.Fail(recorded.Result.ErrorCode!, recorded.Result.Message!);
      try
      {
        await tx.CommitAsync(ct);
      }
      catch (DbUpdateException)
      {
        return StaleRelease();
      }
      facts = recorded.Facts!;
    }
    return DeleteStagedChunks(facts, options);
  }

  /// <summary>
  /// Records the release inside the caller's transaction after the remote version was verified. The returned facts
  /// carry the post-release revision; the caller deletes the staged chunks only after its commit.
  /// </summary>
  internal static async Task<(CommandResult Result, LocalCopyFacts? Facts, PbcStorageEvent? Event)>
    RecordLocalCopyReleaseInTransactionAsync(IAuditSphereDbContext db, ActorContext actor, PbcLocalCopyCleanupOptions options,
      LocalCopyFacts facts, string reason, CancellationToken ct)
  {
    if (db.Database.CurrentTransaction is null)
      throw new InvalidOperationException("A local-copy release requires the caller's transaction.");
    // Same upload-row lock as activation and revert, so a release can never interleave with a return to local reads.
    var locked = await db.PbcUploadIntents.FromSqlInterpolated($"""
      SELECT * FROM pbc_upload_intents WHERE firm_id = {actor.FirmId} AND id = {facts.Item.UploadIntentId} FOR UPDATE
      """).AsNoTracking().ToListAsync(ct);
    if (locked.Count != 1 || !await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      return (TenantAdministration.Denied(), null, null);
    var current = await EvaluateLocalCopyAsync(db, actor.FirmId, facts.Item.UploadIntentId, options, ct);
    if (current is null || current.Item != facts.Item) return (StaleReleaseResult(), null, null);
    var disposition = await db.PbcStorageDispositions.SingleAsync(x =>
      x.FirmId == actor.FirmId && x.PbcUploadIntentId == facts.Item.UploadIntentId, ct);
    var now = DateTimeOffset.UtcNow;
    disposition.LocalCopy = PbcLocalCopyStates.Released;
    disposition.Revision++;
    disposition.UpdatedAt = now;
    var storageEvent = new PbcStorageEvent
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = disposition.ClientId,
      EngagementId = disposition.EngagementId, PbcStorageDispositionId = disposition.Id, Revision = disposition.Revision,
      Kind = PbcStorageEventKinds.LocalCopyReleased, ReadSource = disposition.ReadSource, LocalCopy = disposition.LocalCopy,
      ActorUserId = actor.UserId, Reason = reason, OccurredAt = now
    };
    db.PbcStorageEvents.Add(storageEvent);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      return (StaleReleaseResult(), null, null);
    }
    return (CommandResult.Ok(), facts with { Item = facts.Item with { DispositionRevision = disposition.Revision } }, storageEvent);
  }

  private static CommandResult<PbcLocalCopyReleaseReceipt> StaleRelease() =>
    CommandResult<PbcLocalCopyReleaseReceipt>.Fail(ErrorCodes.GenerationStale, StaleReleaseResult().Message!);

  private static CommandResult StaleReleaseResult() => CommandResult.Fail(ErrorCodes.GenerationStale,
    "The upload's storage disposition or registered content changed; review a fresh plan.");

  internal static async Task<LocalCopyFacts?> EvaluateLocalCopyAsync(IAuditSphereDbContext db, Guid firmId,
    Guid uploadIntentId, PbcLocalCopyCleanupOptions options, CancellationToken ct)
  {
    var intent = await db.PbcUploadIntents.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.Id == uploadIntentId, ct);
    if (intent is null) return null;
    var registration = await db.PbcDocumentRegistrations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.PbcUploadIntentId == intent.Id, ct);
    var snapshot = registration is null ? null : await db.DocumentSnapshots.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.Id == registration.DocumentSnapshotId, ct);
    var disposition = await db.PbcStorageDispositions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.PbcUploadIntentId == intent.Id, ct);
    var paths = await db.PbcUploadChunks.AsNoTracking().Where(x => x.FirmId == firmId && x.PbcUploadIntentId == intent.Id)
      .OrderBy(x => x.ChunkIndex).Select(x => x.StagedPath ?? string.Empty).ToListAsync(ct);
    var pathClass = ClassifyStagedPaths(paths, options.StagingRoot);

    string blocker;
    if (registration is null || snapshot is null) blocker = "pbc.cleanup-unregistered";
    else if (intent.State != PbcUploadStates.Received ||
      !string.Equals(snapshot.Sha256Hex, intent.FinalSha256Hex, StringComparison.Ordinal) ||
      snapshot.ByteCount != intent.DeclaredByteCount)
      blocker = "pbc.source-registration-conflict";
    else if (disposition is null || disposition.ReadSource != PbcReadSources.SelectedSite)
      blocker = "pbc.cleanup-remote-read-inactive";
    else if (disposition.LocalCopy == PbcLocalCopyStates.Released)
      blocker = LocalCopyReleased;
    else if (await db.PbcRequests.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.Id == intent.PbcRequestId &&
      x.State != PbcStates.Accepted && x.State != PbcStates.Closed, ct))
      blocker = "pbc.cleanup-request-open";
    else if (await db.Engagements.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.Id == intent.EngagementId &&
      x.ProfessionalWorkBlocked, ct))
      blocker = "pbc.cleanup-hold";
    else if (await db.DurableOperations.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.TargetId == intent.Id &&
      x.Status != OperationState.COMPLETED && x.Status != OperationState.CANCELLED_WITH_DISPOSITION, ct))
      blocker = "pbc.cleanup-operation-active";
    else if (pathClass is not (PbcLocalCopyPathClasses.Allowlisted or PbcLocalCopyPathClasses.Partial))
      blocker = "pbc.cleanup-path-" + pathClass.ToLowerInvariant().Replace('_', '-');
    else blocker = LocalCopyEligible;

    var released = blocker == LocalCopyReleased;
    var effect = blocker == LocalCopyEligible ? PbcLocalCopyEffects.DeleteStagedChunks
      : released && pathClass is PbcLocalCopyPathClasses.Allowlisted or PbcLocalCopyPathClasses.Partial
        ? PbcLocalCopyEffects.DeleteRemainingChunks : PbcLocalCopyEffects.None;
    var item = new PbcLocalCopyPlanItem(intent.Id, intent.ClientId, intent.EngagementId, registration?.Id ?? Guid.Empty,
      disposition?.Revision ?? 0, intent.FinalSha256Hex ?? string.Empty, intent.DeclaredByteCount,
      snapshot?.DriveId ?? string.Empty, snapshot?.ItemId ?? string.Empty, snapshot?.VersionId ?? string.Empty,
      paths.Count, pathClass, blocker, effect);
    var source = registration is null || snapshot is null ? null : new PbcRemoteSource(registration.FirmId,
      registration.ClientId, registration.EngagementId, registration.PbcUploadIntentId, registration.Id, snapshot.Id,
      snapshot.DriveId, snapshot.ItemId, snapshot.VersionId, snapshot.Sha256Hex, snapshot.ByteCount, registration.BindingSha256Hex);
    return new LocalCopyFacts(item, source, paths);
  }

  /// <summary>Classifies retained chunk paths against the allowlisted staging root without following links.</summary>
  private static string ClassifyStagedPaths(IReadOnlyList<string> paths, string stagingRoot)
  {
    if (paths.Count == 0 || paths.Any(string.IsNullOrWhiteSpace) || !TryStagingPrefix(stagingRoot, out var root, out var prefix))
      return PbcLocalCopyPathClasses.OutsideAllowlist;
    var present = 0;
    foreach (var path in paths)
    {
      var full = Path.GetFullPath(path);
      if (!full.StartsWith(prefix, StringComparison.Ordinal)) return PbcLocalCopyPathClasses.OutsideAllowlist;
      if (HasLinkWithin(full, root)) return PbcLocalCopyPathClasses.Link;
      if (File.Exists(full)) present++;
    }
    return present == paths.Count ? PbcLocalCopyPathClasses.Allowlisted
      : present == 0 ? PbcLocalCopyPathClasses.Absent : PbcLocalCopyPathClasses.Partial;
  }

  /// <summary>Deletes only the recorded staged chunk files. Any failure leaves the release recorded and retryable.</summary>
  internal static CommandResult<PbcLocalCopyReleaseReceipt> DeleteStagedChunks(LocalCopyFacts facts,
    PbcLocalCopyCleanupOptions options)
  {
    int deleted = 0, absent = 0;
    try
    {
      if (!TryStagingPrefix(options.StagingRoot, out var root, out var prefix))
        throw new InvalidOperationException("The PBC staging root is not an absolute directory.");
      foreach (var path in facts.Paths)
      {
        // Recheck containment and links immediately before each delete; a missing file was already reclaimed.
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(prefix, StringComparison.Ordinal) || HasLinkWithin(full, root))
          throw new InvalidOperationException("A retained chunk path is no longer inside the allowlisted staging root.");
        if (!File.Exists(full)) { absent++; continue; }
        File.Delete(full);
        deleted++;
      }
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
    {
      return CommandResult<PbcLocalCopyReleaseReceipt>.Fail("pbc.cleanup-delete-incomplete",
        "The release is recorded but some staged chunks remain; review a fresh plan and retry.");
    }
    return CommandResult<PbcLocalCopyReleaseReceipt>.Ok(
      new(facts.Item.UploadIntentId, facts.Item.DispositionRevision, deleted, absent));
  }

  private static bool TryStagingPrefix(string stagingRoot, out string root, out string prefix)
  {
    root = prefix = string.Empty;
    if (string.IsNullOrWhiteSpace(stagingRoot) || !Path.IsPathFullyQualified(stagingRoot)) return false;
    root = Path.GetFullPath(stagingRoot).TrimEnd(Path.DirectorySeparatorChar);
    prefix = root + Path.DirectorySeparatorChar;
    return root.Length > 0;
  }

  /// <summary>True when the file or any directory between it and the staging root is a link or reparse point.</summary>
  private static bool HasLinkWithin(string fullPath, string root)
  {
    for (var current = fullPath; current.Length > root.Length; current = Path.GetDirectoryName(current)!)
    {
      // LinkTarget reads the entry itself, so dangling links and directory links are both detected.
      var entry = new FileInfo(current);
      if (entry.LinkTarget is not null) return true;
      if ((entry.Exists || Directory.Exists(current)) &&
          File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return true;
    }
    return false;
  }
}
