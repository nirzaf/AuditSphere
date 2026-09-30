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
  internal static async Task<bool> FreezeAsync(IAuditSphereDbContext db, Guid freezeId, long expectedRevision, DateTimeOffset now, CancellationToken ct)
  {
    var freeze = await db.EngagementFileFreezes.SingleAsync(x => x.Id == freezeId, ct);
    if (freeze.State == FileFreezeStates.Frozen) return false;
    if (freeze.State != FileFreezeStates.Scheduled || freeze.Revision != expectedRevision || freeze.DueAt > now)
      throw new OperationBlockedException("freeze-not-due-or-rescheduled");
    var engagement = await db.Engagements.SingleAsync(x => x.Id == freeze.EngagementId && x.FirmId == freeze.FirmId, ct);
    freeze.State = FileFreezeStates.Frozen;
    freeze.FrozenAt = now;
    freeze.ExternalReadOnly = ExternalReadOnlyStates.BlockedExternal;
    freeze.UpdatedAt = now;
    engagement.ProfessionalWorkBlocked = true;
    engagement.Generation++;
    return true;
  }

  /// <summary>Guard for write paths: refuses and records the attempt while the file is frozen.</summary>
  public static async Task<CommandResult> RequireWritableAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, string action, CancellationToken ct = default)
  {
    var frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.State == FileFreezeStates.Frozen, ct);
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
