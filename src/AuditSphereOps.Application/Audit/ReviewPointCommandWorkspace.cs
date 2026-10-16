using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

/// <summary>Exact reviewed review point dispositions. Recover original immutable events before applying current write gates.</summary>
public static class ReviewPointCommandWorkspace
{
  internal static readonly string[] DispositionRoles = ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static CommandResult<T> Fail<T>(string code,string message)=>CommandResult<T>.Fail(code,message);
  private static string Long(long value)=>value.ToString(CultureInfo.InvariantCulture);
  private static bool Hash(string? value)=>value is {Length:64} && value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
  internal static bool Revision(string? value,out long revision)=>long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out revision) && revision>0;
  internal static ReviewPointSnapshot Snapshot(ReviewPoint f)=>new(f.Id,f.ClientId,f.EngagementId,Long(f.DispositionRevision),f.TargetKind,f.TargetId,
    Long(f.TargetRevision),f.RaisedByUserId,f.RaisedAt.ToUniversalTime(),f.Significant,f.Cleared,f.Comment,f.Cleared?"CLEARED":f.Significant?"BLOCKING":"OPEN");
  internal static Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db,ActorContext a,ReviewPoint f,CancellationToken ct)=>
    AuthorizationDecision.AuthorizeAsync(db,a,new(a.FirmId,f.ClientId,f.EngagementId,DispositionRoles,InternalOnly:true,RequireProfessionalWork:false),ct);
  private static ReviewPointCommandFields? Canonical(ReviewPointCommandFields? f)=>f?.Action is ReviewPointCommandActions.Clear or ReviewPointCommandActions.Reopen ? f : null;
  private static string RequestHash(ActorContext a, Guid id, Guid clientId, Guid engagementId, ReviewPointCommandRequest r, ReviewPointCommandFields f) => Hashing.Sha256Hex(
    JsonSerializer.Serialize(new { Version = 1, a.FirmId, a.UserId, a.SessionEpoch, ReviewPointId = id, ClientId = clientId, EngagementId = engagementId, r.RequestId, r.ExpectedDispositionRevision, r.ReviewBasis, Fields = f }));
  private static async Task LockRequestAsync(IAuditSphereDbContext db, ActorContext a, Guid requestId, bool publish, CancellationToken ct)
  {
    var digest = Hashing.Sha256Hex(JsonSerializer.Serialize(new { Namespace = "review-point-command-request-v1", a.FirmId, a.UserId, requestId }));
    var key = unchecked((long)Convert.ToUInt64(digest[..16], 16));
    if (publish) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    else await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({key})", ct);
  }
  internal static async Task<IReadOnlyList<string>> WriteBlockersAsync(IAuditSphereDbContext db,ActorContext a,ReviewPoint f,CancellationToken ct)
  {
    var blockers=new List<string>();
    if(!(await AuthorizeAsync(db,a,f,ct)).Succeeded)blockers.Add("Current disposition authority on this review point is required.");
    // Disposition is administrative: holds and ProfessionalWorkBlocked do not forbid it. Frozen originals remain read-only.
    if(await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x=>x.FirmId==a.FirmId && x.EngagementId==f.EngagementId && x.State==FileFreezeStates.Frozen,ct))
      blockers.Add("The archived engagement remains read-only.");
    if(f.DispositionRevision==long.MaxValue)blockers.Add("The disposition revision limit has been reached.");
    return blockers;
  }
  public static Task<CommandResult> ReviseAsync(IAuditSphereDbContext db,ActorContext actor,Guid id,ReviewPointDispositionInput input,CancellationToken ct=default)=>
    Revision(input.ExpectedDispositionRevision,out var revision)
      ? AuditPlanningService.SetReviewPointDispositionAsync(db,actor,id,input.Cleared,revision,ct)
      : Task.FromResult(CommandResult.Fail(ErrorCodes.AuditPlanning.Invalid,"Provide the exact disposition revision from the current review point."));
  public static async Task<CommandResult<ReviewPointCommandState>> StateAsync(IAuditSphereDbContext db,ActorContext a,Guid id,CancellationToken ct=default)
  {
    var f=await db.ReviewPoints.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.Id==id,ct);
    if(f is null || !(await AuthorizeAsync(db,a,f,ct)).Succeeded)return Fail<ReviewPointCommandState>(ErrorCodes.ScopeDenied,"Access denied.");
    var blockers=await WriteBlockersAsync(db,a,f,ct);
    var latest=await db.ReviewPoints.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.Id==id,ct);
    if(latest is null || Snapshot(f)!=Snapshot(latest))return Fail<ReviewPointCommandState>(ErrorCodes.StaleRevision,"The review point changed while its state was read.");
    if(!(await AuthorizeAsync(db,a,latest,ct)).Succeeded)return Fail<ReviewPointCommandState>(ErrorCodes.ScopeDenied,"Access denied.");
    var snapshot=Snapshot(latest);
    var json=JsonSerializer.Serialize(new{Version=1,a.FirmId,a.UserId,a.SessionEpoch,ClientId=latest.ClientId,EngagementId=latest.EngagementId,ReviewPoint=snapshot,Blockers=blockers});
    return CommandResult<ReviewPointCommandState>.Ok(new(snapshot,Hashing.Sha256Hex(json),blockers.Count==0,blockers,json));
  }
  public static async Task<CommandResult<ReviewPointCommandPreview>> PreviewAsync(IAuditSphereDbContext db,ActorContext a,Guid id,ReviewPointCommandRequest? request,CancellationToken ct=default)
  {
    var fields=Canonical(request?.Fields);
    if(request is null || request.RequestId==Guid.Empty || fields is null || !Hash(request.ReviewBasis) || !Revision(request.ExpectedDispositionRevision,out var revision))
      return Fail<ReviewPointCommandPreview>(ErrorCodes.AuditPlanning.Invalid,"Provide the exact bounded review point disposition intent.");
    var state=await StateAsync(db,a,id,ct);if(!state.Succeeded)return Fail<ReviewPointCommandPreview>(state.ErrorCode!,state.Message!);
    var current=state.Value!;
    if(current.ReviewBasis!=request.ReviewBasis || current.ReviewPoint.DispositionRevision!=request.ExpectedDispositionRevision)
      return Fail<ReviewPointCommandPreview>(ErrorCodes.StaleRevision,"The review point or current authority changed; refresh and review again.");
    if(revision==long.MaxValue)return Fail<ReviewPointCommandPreview>(ErrorCodes.ProtectedState,"The disposition revision limit has been reached.");
    var cleared=fields.Action==ReviewPointCommandActions.Clear;
    var preview=new ReviewPointCommandPreview(request.RequestId,RequestHash(a,id,current.ReviewPoint.ClientId,current.ReviewPoint.EngagementId,request,fields),request.ExpectedDispositionRevision,request.ReviewBasis,fields,current.ReviewPoint,
      Long(revision+1),cleared,current.CanChangeDisposition,current.Blockers,$"{(cleared?"Clear":"Reopen")} this review point against {current.ReviewPoint.TargetKind} target revision {current.ReviewPoint.TargetRevision}. Its original raised point and disposition history remain retained.");
    if(JsonSerializer.Serialize(preview).Length>ReviewPointDispositionEvent.MaximumSnapshotLength)return Fail<ReviewPointCommandPreview>(ErrorCodes.AuditPlanning.Invalid,"The reviewed evidence exceeds its retained limit.");
    var final=await StateAsync(db,a,id,ct);if(!final.Succeeded)return Fail<ReviewPointCommandPreview>(final.ErrorCode!,final.Message!);
    if(final.Value!.ReviewBasis!=request.ReviewBasis)return Fail<ReviewPointCommandPreview>(ErrorCodes.StaleRevision,"The review point changed during preview.");
    return CommandResult<ReviewPointCommandPreview>.Ok(preview);
  }
  // SQL jsonb timestamps retain the database session offset. Return one canonical UTC
  // representation so the exact preview and amendment agree on every deployment timezone.
  private static ReviewPointSnapshot ReadSnapshot(string json)
  {
    var snapshot = JsonSerializer.Deserialize<ReviewPointSnapshot>(json)!;
    return snapshot with { RaisedAt = snapshot.RaisedAt.ToUniversalTime() };
  }
  private static async Task<ReviewPointCommandReceiptView> ViewAsync(IAuditSphereDbContext db, ReviewPointCommandReceipt r, CancellationToken ct)
  {
    var e = await db.ReviewPointDispositionEvents.AsNoTracking().SingleAsync(x => x.Id == r.DispositionEventId && x.FirmId == r.FirmId && x.ClientId == r.ClientId && x.EngagementId == r.EngagementId && x.ReviewPointId == r.ReviewPointId, ct);
    return new(r.Id, r.ReviewPointId, r.ActorId, Long(r.ActorEpoch), r.RequestId, r.RequestHash, r.ReviewBasis, r.Action, r.CreatedAt,
      JsonSerializer.Deserialize<ReviewPointCommandPreview>(r.PreviewJson)!, new(e.Id, e.ActorId, Long(e.ActorEpoch), e.Action, e.CreatedAt,
        ReadSnapshot(e.BeforeJson), ReadSnapshot(e.AfterJson)));
  }
  public static async Task<CommandResult<ReviewPointCommandLookup>> LookupAsync(IAuditSphereDbContext db, ActorContext a, Guid id,
    Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !Hash(requestHash)) return Fail<ReviewPointCommandLookup>(ErrorCodes.AuditPlanning.Invalid, "Use the exact retained request reference.");
    var reviewPoint = await db.ReviewPoints.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == id, ct);
    if (reviewPoint is null || !(await AuthorizeAsync(db, a, reviewPoint, ct)).Succeeded)
      return Fail<ReviewPointCommandLookup>(ErrorCodes.ScopeDenied, "Access denied.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var locked = await AuditPlanningCommandWorkspace.LockAsync(db, a, reviewPoint.EngagementId, false, ct);
    if (!locked.Succeeded) return Fail<ReviewPointCommandLookup>(locked.ErrorCode!, locked.Message!);
    await LockRequestAsync(db, a, requestId, false, ct);
    if (!(await AuthorizeAsync(db, a, reviewPoint, ct)).Succeeded) return Fail<ReviewPointCommandLookup>(ErrorCodes.ScopeDenied, "Access denied.");
    var row = await db.ReviewPointCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == requestId, ct);
    if (row is not null && row.ActorEpoch != a.SessionEpoch) return Fail<ReviewPointCommandLookup>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
    if (row is not null && (row.ReviewPointId != id || row.RequestHash != requestHash)) return Fail<ReviewPointCommandLookup>(ErrorCodes.IdempotencyConflict, "This request belongs to another exact intent.");
    var receipt = row is null ? null : await ViewAsync(db, row, ct);
    if (!(await AuthorizeAsync(db, a, reviewPoint, ct)).Succeeded)
      return Fail<ReviewPointCommandLookup>(ErrorCodes.ScopeDenied, "Access denied.");
    await tx.CommitAsync(ct); return CommandResult<ReviewPointCommandLookup>.Ok(new(row is not null, receipt));
  }
  public static async Task<CommandResult<ReviewPointCommandReceiptView>> ExecuteAsync(IAuditSphereDbContext db, ActorContext a, Guid id,
    ReviewPointCommandRequest? request, CancellationToken ct = default)
  {
    var fields = Canonical(request?.Fields);
    if (request is not { Reviewed: true } || request.RequestId == Guid.Empty || fields is null || !Hash(request.ReviewBasis) || !Hash(request.ExpectedRequestHash) ||
      !Revision(request.ExpectedDispositionRevision, out _) || JsonSerializer.Serialize(fields).Length > ReviewPointCommandReceipt.MaximumInputLength)
      return Fail<ReviewPointCommandReceiptView>(ErrorCodes.AuditPlanning.Invalid, "Preview and explicitly confirm the exact review point intent.");
    var reviewPoint = await db.ReviewPoints.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == id, ct);
    if (reviewPoint is null || !(await AuthorizeAsync(db, a, reviewPoint, ct)).Succeeded)
      return Fail<ReviewPointCommandReceiptView>(ErrorCodes.ScopeDenied, "Access denied.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    try
    {
      var locked = await AuditPlanningCommandWorkspace.LockAsync(db, a, reviewPoint.EngagementId, true, ct);
      if (!locked.Succeeded) return Fail<ReviewPointCommandReceiptView>(locked.ErrorCode!, locked.Message!);
      await LockRequestAsync(db, a, request.RequestId, true, ct);
      if (!(await AuthorizeAsync(db, a, reviewPoint, ct)).Succeeded) return Fail<ReviewPointCommandReceiptView>(ErrorCodes.ScopeDenied, "Access denied.");
      var hash = RequestHash(a, id, reviewPoint.ClientId, reviewPoint.EngagementId, request, fields);
      var prior = await db.ReviewPointCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == request.RequestId, ct);
      if (prior is not null)
      {
        if (prior.ActorEpoch != a.SessionEpoch) return Fail<ReviewPointCommandReceiptView>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
        if (prior.ReviewPointId != id || prior.Action != fields.Action || prior.RequestHash != hash || prior.RequestHash != request.ExpectedRequestHash)
          return Fail<ReviewPointCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
        var replay = await ViewAsync(db, prior, ct);
        if (!(await AuthorizeAsync(db, a, reviewPoint, ct)).Succeeded) return Fail<ReviewPointCommandReceiptView>(ErrorCodes.ScopeDenied, "Access denied.");
        await tx.CommitAsync(ct); return CommandResult<ReviewPointCommandReceiptView>.Ok(replay);
      }
      if (hash != request.ExpectedRequestHash) return Fail<ReviewPointCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The submitted intent differs from its exact preview hash.");
      var scope = await AuditPlanningService.LockedEngagementAsync(db, a, reviewPoint.EngagementId, ct, reviewPoint.ClientId, requireProfessionalWork:false);
      if (scope.Denied is not null) return Fail<ReviewPointCommandReceiptView>(scope.Denied, scope.Message);
      var current = await db.ReviewPoints.FromSqlInterpolated($"SELECT * FROM review_points WHERE firm_id={a.FirmId} AND id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
      if (current is null || current.ClientId != reviewPoint.ClientId || current.EngagementId != reviewPoint.EngagementId) return Fail<ReviewPointCommandReceiptView>(ErrorCodes.ScopeDenied, "Access denied.");
      var preview = await PreviewAsync(db, a, id, request with { Fields = fields }, ct);
      if (!preview.Succeeded) return Fail<ReviewPointCommandReceiptView>(preview.ErrorCode!, preview.Message!);
      if (!preview.Value!.CanProceed) return Fail<ReviewPointCommandReceiptView>(ErrorCodes.GateBlocked, preview.Value.Blockers.First());
      var saved=await AuditPlanningService.SetReviewPointDispositionCoreAsync(db,a,id,fields.Action==ReviewPointCommandActions.Clear,
        long.Parse(request.ExpectedDispositionRevision,CultureInfo.InvariantCulture),true,ct);
      if(!saved.Succeeded)return Fail<ReviewPointCommandReceiptView>(saved.ErrorCode!,saved.Message!);
      var nextRevision = long.Parse(preview.Value.NextDispositionRevision, CultureInfo.InvariantCulture);
      var amendment = await db.ReviewPointDispositionEvents.AsNoTracking().SingleAsync(x => x.FirmId == a.FirmId && x.ReviewPointId == id && x.AfterRevision == nextRevision, ct);
      var row = new ReviewPointCommandReceipt { Id = Guid.CreateVersion7(), FirmId = a.FirmId, ClientId = reviewPoint.ClientId, EngagementId = reviewPoint.EngagementId,
        ReviewPointId = id, DispositionEventId = amendment.Id, ActorId = a.UserId, ActorEpoch = a.SessionEpoch, RequestId = request.RequestId, RequestHash = hash,
        ReviewBasis = request.ReviewBasis, Action = fields.Action, InputJson = JsonSerializer.Serialize(fields), PreviewJson = JsonSerializer.Serialize(preview.Value), CreatedAt = DateTimeOffset.UtcNow };
      db.ReviewPointCommandReceipts.Add(row); await db.SaveChangesAsync(ct);
      // PostgreSQL persists timestamps at microsecond precision. The first response uses the same stored row as recovery.
      var stored = await db.ReviewPointCommandReceipts.AsNoTracking().SingleAsync(x => x.Id == row.Id && x.FirmId == a.FirmId && x.ActorId == a.UserId, ct);
      var result = await ViewAsync(db, stored, ct);
      var auth = await AuthorizeAsync(db, a, reviewPoint, ct);
      if (!auth.Succeeded) return Fail<ReviewPointCommandReceiptView>(auth.ErrorCode!, auth.Message!);
      await tx.CommitAsync(ct); return CommandResult<ReviewPointCommandReceiptView>.Ok(result);
    }
    catch (Exception ex) when (ex.GetBaseException() is DbException cause && cause.GetType().GetProperty("SqlState")?.GetValue(cause) is "40001" or "40P01" or "23505")
    {
      await tx.RollbackAsync(ct);
      return Fail<ReviewPointCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Concurrent review point publication changed the outcome. Look up the retained exact request, then use a fresh context to retry.");
    }
  }
}
