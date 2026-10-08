using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Records;

public sealed record FileFreezeView(Guid FreezeId, string State, DateTimeOffset ReportSignedAt, DateTimeOffset DueAt, DateTimeOffset? FrozenAt,
  string ExternalReadOnly, int DaysRemaining, IReadOnlyList<FileFreezeAmendment> Amendments);

/// <summary>
/// Regulatory file freeze: signing the Independent Auditor's Report schedules the freeze 60 days later (a re-signed
/// report before the freeze reschedules it); a worker freezes due files, which blocks professional work and records
/// refused writes; a Partner other than the requester opens a documented amendment window and closing it re-freezes.
/// External SharePoint read-only enforcement is recorded as requested and stays BLOCKED_EXTERNAL until observed.
/// </summary>
public static class FileFreezeService
{
  public const int FreezeDays = 60;

  public static DateTimeOffset DueAt(DateTimeOffset reportSignedAt) => reportSignedAt.AddDays(FreezeDays);

  /// <summary>Called when a report is signed, inside the signing unit of work.</summary>
  internal static async Task ScheduleAsync(IAuditSphereDbContext db, AuditDeliverable signedReport, DateTimeOffset signedAt, CancellationToken ct)
  {
    var freeze = await db.EngagementFileFreezes.SingleOrDefaultAsync(x => x.FirmId == signedReport.FirmId && x.EngagementId == signedReport.EngagementId, ct);
    if (freeze is null)
    {
      db.EngagementFileFreezes.Add(new EngagementFileFreeze
      {
        Id = Guid.CreateVersion7(), FirmId = signedReport.FirmId, ClientId = signedReport.ClientId, EngagementId = signedReport.EngagementId,
        ReportDeliverableId = signedReport.Id, ReportSignedAt = signedAt, DueAt = DueAt(signedAt), UpdatedAt = signedAt
      });
      return;
    }
    if (freeze.State != FileFreezeStates.Scheduled) return; // an amended, re-signed report does not move an existing freeze
    freeze.ReportDeliverableId = signedReport.Id;
    freeze.ReportSignedAt = signedAt;
    freeze.DueAt = DueAt(signedAt);
    freeze.Revision++;
    freeze.UpdatedAt = signedAt;
  }

  /// <summary>Freezes one due file under its row lock. Used by the worker handler and idempotent on retry.</summary>
  internal static async Task<bool> FreezeAsync(IAuditSphereDbContext db, Guid freezeId, long expectedRevision, DateTimeOffset now, CancellationToken ct,
    bool earlyByPartner = false)
  {
    var freeze = await db.EngagementFileFreezes.SingleAsync(x => x.Id == freezeId, ct);
    if (freeze.State == FileFreezeStates.Frozen) return false;
    // A Partner's early lock is authorised separately (EarlyComplianceLockAsync); the worker still freezes only when due.
    if (freeze.State != FileFreezeStates.Scheduled || freeze.Revision != expectedRevision || (freeze.DueAt > now && !earlyByPartner))
      throw new OperationBlockedException("freeze-not-due-or-rescheduled");
    // Serialize the freeze against in-flight professional writes: writers hold the engagement row lock
    // (or a shared lock through the frozen-state check) before mutating, so a freeze cannot land in the
    // middle of a write that already passed its check, and a write starting after the freeze commits
    // sees the frozen state (STE-REM-09).
    var engagement = await db.Engagements.FromSqlInterpolated(
      $"SELECT * FROM engagements WHERE id = {freeze.EngagementId} AND firm_id = {freeze.FirmId} FOR UPDATE").SingleAsync(ct);
    freeze.State = FileFreezeStates.Frozen;
    freeze.FrozenAt = now;
    freeze.ExternalReadOnly = ExternalReadOnlyStates.BlockedExternal;
    freeze.UpdatedAt = now;
    engagement.ProfessionalWorkBlocked = true;
    engagement.Generation++;
    return true;
  }

  /// <summary>
  /// Guard for write paths: refuses and records the attempt while the file is frozen. Inside a caller's
  /// transaction the check first takes a shared lock on the engagement row, so a concurrent worker freeze
  /// (which takes the same row exclusively) is serialized with the write and can never be overtaken by a
  /// write that checked earlier (STE-REM-09).
  /// </summary>
  public static async Task<CommandResult> RequireWritableAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, string action, CancellationToken ct = default)
  {
    var inTransaction = db.Database.CurrentTransaction is not null;
    bool frozen;
    if (inTransaction)
    {
      _ = await db.Engagements.FromSqlInterpolated(
        $"SELECT * FROM engagements WHERE id = {engagementId} AND firm_id = {actor.FirmId} FOR SHARE").AsNoTracking().ToListAsync(ct);
      frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.State == FileFreezeStates.Frozen, ct);
    }
    else
    {
      frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.State == FileFreezeStates.Frozen, ct);
    }
    if (!frozen) return CommandResult.Ok();
    db.FrozenAccessAttempts.Add(new FrozenAccessAttempt { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, EngagementId = engagementId, ActorUserId = actor.UserId, Action = action, AttemptedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync(ct);
    return CommandResult.Fail(ErrorCodes.ProtectedState, "The engagement file is frozen after the report; an approved amendment is required to change it.");
  }

  public static async Task<CommandResult<Guid>> RequestAmendmentAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, string reason, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) return CommandResult<Guid>.Fail(ErrorCodes.AuditPlanning.Invalid, "State why the frozen file must change.");
    var freeze = await FreezeForAsync(db, actor, engagementId, ["Manager", "Partner", "Administrator"], ct);
    if (!freeze.Succeeded) return CommandResult<Guid>.Fail(freeze.ErrorCode!, freeze.Message!);
    if (freeze.Value!.State != FileFreezeStates.Frozen) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Only a frozen file needs an amendment.");
    var amendment = new FileFreezeAmendment { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, FreezeId = freeze.Value.Id, EngagementId = engagementId, Reason = reason.Trim(), RequestedByUserId = actor.UserId, RequestedAt = DateTimeOffset.UtcNow };
    db.FileFreezeAmendments.Add(amendment);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(amendment.Id);
  }

  public static async Task<CommandResult> ApproveAmendmentAsync(IAuditSphereDbContext db, ActorContext actor, Guid amendmentId, CancellationToken ct = default)
  {
    var amendment = await db.FileFreezeAmendments.SingleOrDefaultAsync(x => x.Id == amendmentId && x.FirmId == actor.FirmId, ct);
    if (amendment is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var freeze = await FreezeForAsync(db, actor, amendment.EngagementId, ["Partner"], ct);
    if (!freeze.Succeeded) return CommandResult.Fail(freeze.ErrorCode!, freeze.Message!);
    if (amendment.RequestedByUserId == actor.UserId) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Another Partner must approve an amendment you requested.");
    if (amendment.OpenedAt is not null) return CommandResult.Ok();
    var live = await db.EngagementFileFreezes.SingleAsync(x => x.Id == amendment.FreezeId, ct);
    if (live.State != FileFreezeStates.Frozen) return CommandResult.Fail(ErrorCodes.GateBlocked, "Another amendment is already open.");
    var engagement = await db.Engagements.SingleAsync(x => x.Id == amendment.EngagementId, ct);
    var now = DateTimeOffset.UtcNow;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    amendment.ApprovedByUserId = actor.UserId;
    amendment.OpenedAt = now;
    await db.SaveChangesAsync(ct); // the approval must exist before the database lets the file reopen
    live.State = FileFreezeStates.AmendmentOpen;
    live.UpdatedAt = now;
    engagement.ProfessionalWorkBlocked = false;
    engagement.Generation++;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> CloseAmendmentAsync(IAuditSphereDbContext db, ActorContext actor, Guid amendmentId, CancellationToken ct = default)
  {
    var amendment = await db.FileFreezeAmendments.SingleOrDefaultAsync(x => x.Id == amendmentId && x.FirmId == actor.FirmId, ct);
    if (amendment is null || amendment.OpenedAt is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var freeze = await FreezeForAsync(db, actor, amendment.EngagementId, ["Manager", "Partner", "Administrator"], ct);
    if (!freeze.Succeeded) return CommandResult.Fail(freeze.ErrorCode!, freeze.Message!);
    if (amendment.ClosedAt is not null) return CommandResult.Ok();
    var live = await db.EngagementFileFreezes.SingleAsync(x => x.Id == amendment.FreezeId, ct);
    var engagement = await db.Engagements.SingleAsync(x => x.Id == amendment.EngagementId, ct);
    var now = DateTimeOffset.UtcNow;
    amendment.ClosedAt = now;
    amendment.ClosedByUserId = actor.UserId;
    live.State = FileFreezeStates.Frozen;
    live.UpdatedAt = now;
    engagement.ProfessionalWorkBlocked = true;
    engagement.Generation++;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<FileFreezeView?> GetAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, DateTimeOffset now, CancellationToken ct = default)
  {
    var freeze = await FreezeForAsync(db, actor, engagementId, ["Staff", "Senior", "Manager", "Partner", "Administrator", "Reviewer"], ct);
    if (!freeze.Succeeded) return null;
    var f = freeze.Value!;
    var amendments = await db.FileFreezeAmendments.AsNoTracking().Where(x => x.FreezeId == f.Id).OrderBy(x => x.RequestedAt).ToListAsync(ct);
    return new(f.Id, f.State, f.ReportSignedAt, f.DueAt, f.FrozenAt, f.ExternalReadOnly, Math.Max(0, (int)Math.Ceiling((f.DueAt - now).TotalDays)), amendments);
  }

  public sealed record ArchiveReadinessView(Guid FreezeId, long Revision, string State, DateTimeOffset DueAt, string? ArchiveReadinessDigest,
    IReadOnlyList<string> Blockers);

  public sealed record EarlyComplianceLockRequest(Guid EngagementId, long ExpectedRevision, bool PartnerConfirmed, string Rationale, string ArchiveReadinessDigest);

  public sealed record EarlyComplianceLockResult(Guid FreezeId, string State, bool AlreadyFrozen, DateTimeOffset FrozenAt);

  /// <summary>
  /// The archive readiness a Partner reviews before an early lock. The digest binds the freeze revision, the signed report
  /// and the final release manifest, so a readiness that changes after review cannot be locked on a stale confirmation.
  /// </summary>
  public static async Task<CommandResult<ArchiveReadinessView>> GetArchiveReadinessAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, DateTimeOffset now, CancellationToken ct = default)
  {
    var scoped = await FreezeForAsync(db, actor, engagementId, ["Partner"], ct);
    if (!scoped.Succeeded) return CommandResult<ArchiveReadinessView>.Fail(scoped.ErrorCode!, scoped.Message!);
    var freeze = scoped.Value!;
    var release = await LatestReleaseAsync(db, actor.FirmId, engagementId, ct);
    var blockers = new List<string>();
    if (release is null) blockers.Add("final-release-missing");
    if (freeze.State == FileFreezeStates.AmendmentOpen) blockers.Add("amendment-open");
    var digest = release is null ? null : ArchiveReadinessDigest(freeze, release);
    return CommandResult<ArchiveReadinessView>.Ok(new(freeze.Id, freeze.Revision, freeze.State, freeze.DueAt, digest, blockers));
  }

  /// <summary>
  /// Partner-triggered early compliance lock during the countdown (STE 4.4.3). Fails closed unless the actor is a Partner in
  /// scope, the Partner confirmed, the countdown revision and reviewed archive digest are current, the final release exists and
  /// no amendment window is open. The freeze and its append-only evidence commit in one transaction. An already frozen file
  /// is reported as frozen without a second lock.
  /// </summary>
  public static async Task<CommandResult<EarlyComplianceLockResult>> RequestEarlyComplianceLockAsync(
    IAuditSphereDbContext db, ActorContext actor, EarlyComplianceLockRequest request, DateTimeOffset now, CancellationToken ct = default)
  {
    if (!request.PartnerConfirmed)
      return CommandResult<EarlyComplianceLockResult>.Fail(ErrorCodes.GateBlocked,
        "Confirm that locking the file makes it read-only now, before the 60-day countdown ends.");
    if (string.IsNullOrWhiteSpace(request.Rationale) || request.Rationale.Length > 2000)
      return CommandResult<EarlyComplianceLockResult>.Fail(ErrorCodes.AuditPlanning.Invalid, "State why the file is locked early (up to 2000 characters).");
    var scoped = await FreezeForAsync(db, actor, request.EngagementId, ["Partner"], ct);
    if (!scoped.Succeeded) return CommandResult<EarlyComplianceLockResult>.Fail(scoped.ErrorCode!, scoped.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    // Lock the freeze row before reading its state, so two Partners cannot both observe SCHEDULED and both lock.
    var freeze = await db.EngagementFileFreezes.FromSqlInterpolated(
      $"SELECT * FROM engagement_file_freezes WHERE id = {scoped.Value!.Id} AND firm_id = {actor.FirmId} FOR UPDATE").SingleAsync(ct);
    if (freeze.State == FileFreezeStates.Frozen)
      return CommandResult<EarlyComplianceLockResult>.Ok(new(freeze.Id, freeze.State, true, freeze.FrozenAt!.Value));
    if (freeze.State != FileFreezeStates.Scheduled)
      return CommandResult<EarlyComplianceLockResult>.Fail(ErrorCodes.GateBlocked, "An approved amendment is open; close it before an early lock.");
    if (freeze.Revision != request.ExpectedRevision)
      return CommandResult<EarlyComplianceLockResult>.Fail(ErrorCodes.GenerationStale,
        "The compliance countdown changed since you reviewed it. Reload the archive readiness and try again.");
    var release = await LatestReleaseAsync(db, actor.FirmId, freeze.EngagementId, ct);
    if (release is null)
      return CommandResult<EarlyComplianceLockResult>.Fail(ErrorCodes.GateBlocked, "Final deliverables must be released before an early lock.");
    var digest = ArchiveReadinessDigest(freeze, release);
    if (!string.Equals(digest, request.ArchiveReadinessDigest, StringComparison.OrdinalIgnoreCase))
      return CommandResult<EarlyComplianceLockResult>.Fail(ErrorCodes.GenerationStale,
        "The archive readiness changed since you reviewed it. Review the current readiness before locking.");

    await FreezeAsync(db, freeze.Id, freeze.Revision, now, ct, earlyByPartner: true);
    db.FileFreezeEarlyLocks.Add(new FileFreezeEarlyLock
    {
      Id = Guid.CreateVersion7(), FirmId = freeze.FirmId, ClientId = freeze.ClientId, EngagementId = freeze.EngagementId,
      FreezeId = freeze.Id, ReportDeliverableId = freeze.ReportDeliverableId, FreezeRevision = freeze.Revision,
      ReleaseId = release.Id, ArchiveReadinessDigest = digest, Rationale = request.Rationale.Trim(),
      LockedByUserId = actor.UserId, LockedAt = now
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<EarlyComplianceLockResult>.Ok(new(freeze.Id, FileFreezeStates.Frozen, false, now));
  }

  /// <summary>Binds the countdown revision, signed report and final release manifest into one reviewable digest.</summary>
  public static string ArchiveReadinessDigest(EngagementFileFreeze freeze, Release release) =>
    Hashing.Sha256Hex(string.Join('|', "file-freeze.archive-readiness.v1", freeze.Id.ToString("D"),
      freeze.Revision.ToString(CultureInfo.InvariantCulture), freeze.ReportDeliverableId.ToString("D"),
      freeze.ReportSignedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), release.Id.ToString("D"), release.ManifestDigest));

  private static async Task<Release?> LatestReleaseAsync(IAuditSphereDbContext db, Guid firmId, Guid engagementId, CancellationToken ct) =>
    await db.Releases.AsNoTracking().Where(x => x.FirmId == firmId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.ReleasedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);

  private static async Task<CommandResult<EngagementFileFreeze>> FreezeForAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, string[] roles, CancellationToken ct)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<EngagementFileFreeze>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId, roles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<EngagementFileFreeze>.Fail(auth.ErrorCode!, auth.Message!);
    var freeze = await db.EngagementFileFreezes.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct);
    return freeze is null ? CommandResult<EngagementFileFreeze>.Fail(ErrorCodes.GateBlocked, "No signed report has scheduled a freeze.") : CommandResult<EngagementFileFreeze>.Ok(freeze);
  }
}

/// <summary>Local durable operation that freezes one due file; it has no provider effect.</summary>
public sealed class FileFreezeHandler(TimeProvider clock) : IOperationHandler
{
  public const string Kind = "FreezeEngagementFile.v1";
  public OperationDefinition Definition { get; } = new(Kind, OperationMode.LOCAL, OperationAuthority.LOCAL_VALIDATION);

  public string NormalizePayload(OperationRequest request)
  {
    try
    {
      using var document = JsonDocument.Parse(request.PayloadJson);
      if (!document.RootElement.TryGetProperty("freezeId", out var id) || !id.TryGetGuid(out var freezeId) || freezeId != request.TargetId || request.EngagementId is null)
        throw new OperationBlockedException("invalid-freeze-request");
      return JsonSerializer.Serialize(new { freezeId = freezeId.ToString("D") });
    }
    catch (JsonException) { throw new OperationBlockedException("invalid-freeze-request"); }
  }

  public async Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation op, CancellationToken ct)
  {
    var rows = await db.EngagementFileFreezes.FromSqlInterpolated($"""
      SELECT * FROM engagement_file_freezes WHERE id = {op.TargetId} AND firm_id = {op.FirmId} AND engagement_id = {op.EngagementId} FOR UPDATE
      """).ToListAsync(ct);
    if (rows.Count != 1 || rows[0].Revision != op.ExpectedRevision)
      throw new OperationBlockedException("freeze-scope-or-revision-conflict", authorization: true);
  }

  public async Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation op, OperationResult? verifiedRemoteResult, CancellationToken ct)
  {
    await FileFreezeService.FreezeAsync(db, op.TargetId, op.ExpectedRevision, clock.GetUtcNow(), ct);
    db.OperationEvents.Add(new OperationEvent { Id = Guid.CreateVersion7(), OperationId = op.Id, Token = op.AttemptToken, Kind = "file.frozen.v1", Executor = op.LeaseOwner!, OccurredAt = clock.GetUtcNow() });
    return new(op.TargetId.ToString("D"), Hashing.Sha256Hex($"frozen:{op.TargetId:D}:{op.ExpectedRevision}"));
  }

  public Task<OperationResult> ExecuteEffectAsync(DurableOperation op, CancellationToken ct) => throw new OperationBlockedException("local-operation-has-no-provider-effect");
  public Task<OperationResult> ReconcileAsync(DurableOperation op, CancellationToken ct) => throw new OperationBlockedException("local-operation-has-no-provider-effect");
}

/// <summary>Finds scheduled freezes whose 60 days have elapsed on the injected clock.</summary>
public sealed class FileFreezeDiscovery(IAuditSphereDbContextFactory factory, IOperationStore store, FileFreezeHandler handler, WorkerOptions options, TimeProvider clock)
  : IPendingOperationDiscovery
{
  public async Task<int> EnqueuePendingAsync(CancellationToken ct)
  {
    var now = clock.GetUtcNow();
    await using var read = await factory.CreateAsync(ct);
    var due = await read.EngagementFileFreezes.AsNoTracking()
      .Where(x => x.FirmId == options.FirmId && x.State == FileFreezeStates.Scheduled && x.DueAt <= now &&
        !read.DurableOperations.Any(o => o.FirmId == x.FirmId && o.TargetId == x.Id && o.ExpectedRevision == x.Revision && o.OperationKind == FileFreezeHandler.Kind))
      .OrderBy(x => x.DueAt).Take(25).ToListAsync(ct);
    var count = 0;
    foreach (var freeze in due)
    {
      await using var db = await factory.CreateAsync(ct);
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      var result = await store.EnqueueAsync(db, new(freeze.FirmId, freeze.ClientId, freeze.EngagementId, FileFreezeHandler.Kind, freeze.Id, freeze.Revision,
        $"file-freeze:{freeze.Id:D}:{freeze.Revision}", JsonSerializer.Serialize(new { freezeId = freeze.Id.ToString("D") })), handler, ct);
      if (!result.Succeeded) continue;
      await tx.CommitAsync(ct);
      count++;
    }
    return count;
  }
}
