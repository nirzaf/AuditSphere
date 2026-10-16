using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

/// <summary>Exact reviewed finding amendments. Recover original immutable events before applying current write gates.</summary>
public static class FindingCommandWorkspace
{
  internal static readonly string[] ResponseRoles = ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  internal static readonly string[] DesignationRoles = ["Manager", "Partner", "Administrator"];
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static string Long(long x) => x.ToString(CultureInfo.InvariantCulture);
  private static bool Hash(string? x) => x is { Length: 64 } && x.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static bool Revision(string? x, out long value) => long.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
  private static bool Text(string? x, int max) => !string.IsNullOrWhiteSpace(x) && x.Length <= max && !x.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'));
  internal static FindingSnapshot Snapshot(Finding f) => new(f.Id, f.ClientId, f.EngagementId, Long(f.Revision), f.ActorId, f.FindingType,
    f.ImpactDescription, f.MonetaryAmount?.ToString("0.000000", CultureInfo.InvariantCulture), f.Corrected, f.ManagementResponse, f.Status,
    f.LetterDesignatedAt, f.LetterDesignatedByUserId, f.LetterRecommendation, f.CreatedAt);
  internal static string[] Roles(string action) => action == FindingCommandActions.RecordResponse ? ResponseRoles : DesignationRoles;
  internal static Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext a, Finding f, string action, bool professional, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, a, new(a.FirmId, f.ClientId, f.EngagementId, Roles(action), InternalOnly: true, RequireProfessionalWork: professional), ct);
  private static FindingCommandFields? Canonical(FindingCommandFields? f) => f?.Action switch
  {
    FindingCommandActions.RecordResponse when f.Response is { } r && f.Designation is null && Text(r.ManagementResponse, 20000) =>
      f with { Response = r with { ManagementResponse = r.ManagementResponse.Trim() } },
    FindingCommandActions.Designate when f.Designation is { } d && f.Response is null && Text(d.Recommendation, 2000) =>
      f with { Designation = d with { Recommendation = d.Recommendation.Trim() } },
    FindingCommandActions.Undesignate when f.Response is null && f.Designation is null => f,
    _ => null
  };
  private static string RequestHash(ActorContext a, Guid id, FindingCommandRequest r, FindingCommandFields f) => Hashing.Sha256Hex(
    JsonSerializer.Serialize(new { Version = 1, a.FirmId, a.UserId, a.SessionEpoch, FindingId = id, r.RequestId, r.ExpectedRevision, r.ReviewBasis, Fields = f }));
  private static async Task LockRequestAsync(IAuditSphereDbContext db, ActorContext a, Guid requestId, bool publish, CancellationToken ct)
  {
    var digest = Hashing.Sha256Hex(JsonSerializer.Serialize(new { Namespace = "finding-command-request-v1", a.FirmId, a.UserId, requestId }));
    var key = unchecked((long)Convert.ToUInt64(digest[..16], 16));
    if (publish) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    else await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({key})", ct);
  }
  internal static async Task<IReadOnlyList<string>> WriteBlockersAsync(IAuditSphereDbContext db, ActorContext a, Finding f, string action, CancellationToken ct)
  {
    var blockers = new List<string>();
    if (!(await AuthorizeAsync(db, a, f, action, false, ct)).Succeeded) blockers.Add("Current action authority on this finding is required.");
    var e = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == f.EngagementId && x.PracticeClientId == f.ClientId, ct);
    if (e is null || e.ProfessionalWorkBlocked) blockers.Add("Professional work is blocked on this engagement.");
    if (await db.EngagementHolds.AsNoTracking().AnyAsync(x => x.FirmId == a.FirmId && x.EngagementId == f.EngagementId && !x.Released, ct)) blockers.Add("An unreleased hold blocks professional work.");
    if (await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == a.FirmId && x.EngagementId == f.EngagementId && x.State == FileFreezeStates.Frozen, ct)) blockers.Add("The engagement file is frozen; an approved amendment is required.");
    if (f.Revision == long.MaxValue) blockers.Add("The finding revision limit has been reached.");
    return blockers;
  }
  public static async Task<CommandResult<FindingCommandState>> StateAsync(IAuditSphereDbContext db, ActorContext a, Guid id, CancellationToken ct = default)
  {
    var f = await db.Findings.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == id, ct);
    if (f is null || !(await AuthorizeAsync(db, a, f, FindingCommandActions.RecordResponse, false, ct)).Succeeded)
      return Fail<FindingCommandState>(ErrorCodes.ScopeDenied, "Access denied.");
    var response = await WriteBlockersAsync(db, a, f, FindingCommandActions.RecordResponse, ct);
    var designation = await WriteBlockersAsync(db, a, f, FindingCommandActions.Designate, ct);
    var latest = await db.Findings.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == id, ct);
    if (latest is null || JsonSerializer.Serialize(Snapshot(f)) != JsonSerializer.Serialize(Snapshot(latest)))
      return Fail<FindingCommandState>(ErrorCodes.StaleRevision, "The finding changed while its reviewed state was read.");
    var auth = await AuthorizeAsync(db, a, latest, FindingCommandActions.RecordResponse, false, ct);
    if (!auth.Succeeded) return Fail<FindingCommandState>(ErrorCodes.ScopeDenied, "Access denied.");
    // Recheck action capabilities at the return boundary, not from the actor's union of roles.
    response = await WriteBlockersAsync(db, a, latest, FindingCommandActions.RecordResponse, ct);
    designation = await WriteBlockersAsync(db, a, latest, FindingCommandActions.Designate, ct);
    auth = await AuthorizeAsync(db, a, latest, FindingCommandActions.RecordResponse, false, ct);
    if (!auth.Succeeded) return Fail<FindingCommandState>(ErrorCodes.ScopeDenied, "Access denied.");
    var snapshot = Snapshot(latest);
    var json = JsonSerializer.Serialize(new { Version = 1, a.FirmId, a.UserId, a.SessionEpoch, ClientId = latest.ClientId,
      EngagementId = latest.EngagementId, Finding = snapshot, ResponseBlockers = response, DesignationBlockers = designation });
    return CommandResult<FindingCommandState>.Ok(new(snapshot, Hashing.Sha256Hex(json), response.Count == 0, designation.Count == 0, response, designation, json));
  }
  public static async Task<CommandResult<FindingCommandPreview>> PreviewAsync(IAuditSphereDbContext db, ActorContext a, Guid id,
    FindingCommandRequest? request, CancellationToken ct = default)
  {
    var fields = Canonical(request?.Fields);
    if (request is null || request.RequestId == Guid.Empty || fields is null || !Hash(request.ReviewBasis) || !Revision(request.ExpectedRevision, out var revision) ||
      JsonSerializer.Serialize(fields).Length > FindingCommandReceipt.MaximumInputLength)
      return Fail<FindingCommandPreview>(ErrorCodes.AuditPlanning.Invalid, "Provide the exact bounded finding intent.");
    var state = await StateAsync(db, a, id, ct);
    if (!state.Succeeded) return Fail<FindingCommandPreview>(state.ErrorCode!, state.Message!);
    var s = state.Value!;
    if (s.ReviewBasis != request.ReviewBasis || s.Finding.Revision != request.ExpectedRevision)
      return Fail<FindingCommandPreview>(ErrorCodes.StaleRevision, "The finding or current authority changed; refresh and review again.");
    var blockers = (fields.Action == FindingCommandActions.RecordResponse ? s.ResponseBlockers : s.DesignationBlockers).ToList();
    if (revision == long.MaxValue) return Fail<FindingCommandPreview>(ErrorCodes.ProtectedState, "The finding revision limit has been reached.");
    var summary = fields.Action switch {
      FindingCommandActions.RecordResponse => $"Record the management response for {s.Finding.FindingType}; corrected: {fields.Response!.Corrected}. This records management's response, not an audit conclusion.",
      FindingCommandActions.Designate => $"Designate {s.Finding.FindingType} for the management letter with this exact recommendation: {fields.Designation!.Recommendation}",
      _ => $"Remove {s.Finding.FindingType} from the management letter; its prior designation remains in immutable amendment history."
    };
    var preview = new FindingCommandPreview(request.RequestId, RequestHash(a, id, request, fields), request.ExpectedRevision,
      request.ReviewBasis, fields, s.Finding, Long(revision + 1), blockers.Count == 0, blockers, summary);
    if (JsonSerializer.Serialize(preview).Length > FindingAmendment.MaximumSnapshotLength)
      return Fail<FindingCommandPreview>(ErrorCodes.AuditPlanning.Invalid, "The finding preview exceeds the retained evidence limit.");
    var final = await StateAsync(db, a, id, ct);
    if (!final.Succeeded) return Fail<FindingCommandPreview>(final.ErrorCode!, final.Message!);
    if (final.Value!.ReviewBasis != request.ReviewBasis) return Fail<FindingCommandPreview>(ErrorCodes.StaleRevision, "The finding changed during preview.");
    return CommandResult<FindingCommandPreview>.Ok(preview);
  }
  // SQL jsonb timestamps retain the database session offset. Return one canonical UTC
  // representation so the exact preview and amendment agree on every deployment timezone.
  private static FindingSnapshot ReadSnapshot(string json)
  {
    var snapshot = JsonSerializer.Deserialize<FindingSnapshot>(json)!;
    return snapshot with { CreatedAt = snapshot.CreatedAt.ToUniversalTime(), LetterDesignatedAt = snapshot.LetterDesignatedAt?.ToUniversalTime() };
  }
  private static async Task<FindingCommandReceiptView> ViewAsync(IAuditSphereDbContext db, FindingCommandReceipt r, CancellationToken ct)
  {
    var e = await db.FindingAmendments.AsNoTracking().SingleAsync(x => x.Id == r.AmendmentId && x.FirmId == r.FirmId && x.ClientId == r.ClientId && x.EngagementId == r.EngagementId && x.FindingId == r.FindingId, ct);
    return new(r.Id, r.FindingId, r.ActorId, Long(r.ActorEpoch), r.RequestId, r.RequestHash, r.ReviewBasis, r.Action, r.CreatedAt,
      JsonSerializer.Deserialize<FindingCommandPreview>(r.PreviewJson)!, new(e.Id, e.ActorId, Long(e.ActorEpoch), e.Action, e.CreatedAt,
        ReadSnapshot(e.BeforeJson), ReadSnapshot(e.AfterJson)));
  }
  public static async Task<CommandResult<FindingCommandLookup>> LookupAsync(IAuditSphereDbContext db, ActorContext a, Guid id,
    Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !Hash(requestHash)) return Fail<FindingCommandLookup>(ErrorCodes.AuditPlanning.Invalid, "Use the exact retained request reference.");
    var finding = await db.Findings.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == id, ct);
    if (finding is null || !(await AuthorizeAsync(db, a, finding, FindingCommandActions.RecordResponse, false, ct)).Succeeded)
      return Fail<FindingCommandLookup>(ErrorCodes.ScopeDenied, "Access denied.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var locked = await AuditPlanningCommandWorkspace.LockAsync(db, a, finding.EngagementId, false, ct);
    if (!locked.Succeeded) return Fail<FindingCommandLookup>(locked.ErrorCode!, locked.Message!);
    await LockRequestAsync(db, a, requestId, false, ct);
    if (!(await AuthorizeAsync(db, a, finding, FindingCommandActions.RecordResponse, false, ct)).Succeeded) return Fail<FindingCommandLookup>(ErrorCodes.ScopeDenied, "Access denied.");
    var row = await db.FindingCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == requestId, ct);
    if (row is not null && row.ActorEpoch != a.SessionEpoch) return Fail<FindingCommandLookup>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
    if (row is not null && (row.FindingId != id || row.RequestHash != requestHash)) return Fail<FindingCommandLookup>(ErrorCodes.IdempotencyConflict, "This request belongs to another exact intent.");
    var receipt = row is null ? null : await ViewAsync(db, row, ct);
    if (!(await AuthorizeAsync(db, a, finding, row?.Action ?? FindingCommandActions.RecordResponse, false, ct)).Succeeded)
      return Fail<FindingCommandLookup>(ErrorCodes.ScopeDenied, "Access denied.");
    await tx.CommitAsync(ct); return CommandResult<FindingCommandLookup>.Ok(new(row is not null, receipt));
  }
  public static async Task<CommandResult<FindingCommandReceiptView>> ExecuteAsync(IAuditSphereDbContext db, ActorContext a, Guid id,
    FindingCommandRequest? request, CancellationToken ct = default)
  {
    var fields = Canonical(request?.Fields);
    if (request is not { Reviewed: true } || request.RequestId == Guid.Empty || fields is null || !Hash(request.ReviewBasis) || !Hash(request.ExpectedRequestHash) ||
      !Revision(request.ExpectedRevision, out _) || JsonSerializer.Serialize(fields).Length > FindingCommandReceipt.MaximumInputLength)
      return Fail<FindingCommandReceiptView>(ErrorCodes.AuditPlanning.Invalid, "Preview and explicitly confirm the exact finding intent.");
    var finding = await db.Findings.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == id, ct);
    if (finding is null || !(await AuthorizeAsync(db, a, finding, fields.Action, false, ct)).Succeeded)
      return Fail<FindingCommandReceiptView>(ErrorCodes.ScopeDenied, "Access denied.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    try
    {
      var locked = await AuditPlanningCommandWorkspace.LockAsync(db, a, finding.EngagementId, true, ct);
      if (!locked.Succeeded) return Fail<FindingCommandReceiptView>(locked.ErrorCode!, locked.Message!);
      await LockRequestAsync(db, a, request.RequestId, true, ct);
      if (!(await AuthorizeAsync(db, a, finding, fields.Action, false, ct)).Succeeded) return Fail<FindingCommandReceiptView>(ErrorCodes.ScopeDenied, "Access denied.");
      var hash = RequestHash(a, id, request, fields);
      var prior = await db.FindingCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == request.RequestId, ct);
      if (prior is not null)
      {
        if (prior.ActorEpoch != a.SessionEpoch) return Fail<FindingCommandReceiptView>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
        if (prior.FindingId != id || prior.Action != fields.Action || prior.RequestHash != hash || prior.RequestHash != request.ExpectedRequestHash)
          return Fail<FindingCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
        var replay = await ViewAsync(db, prior, ct);
        if (!(await AuthorizeAsync(db, a, finding, prior.Action, false, ct)).Succeeded) return Fail<FindingCommandReceiptView>(ErrorCodes.ScopeDenied, "Access denied.");
        await tx.CommitAsync(ct); return CommandResult<FindingCommandReceiptView>.Ok(replay);
      }
      if (hash != request.ExpectedRequestHash) return Fail<FindingCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The submitted intent differs from its exact preview hash.");
      var scope = await AuditPlanningService.LockedEngagementAsync(db, a, finding.EngagementId, ct, finding.ClientId);
      if (scope.Denied is not null) return Fail<FindingCommandReceiptView>(scope.Denied, scope.Message);
      var current = await db.Findings.FromSqlInterpolated($"SELECT * FROM findings WHERE firm_id={a.FirmId} AND id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
      if (current is null || current.ClientId != finding.ClientId || current.EngagementId != finding.EngagementId) return Fail<FindingCommandReceiptView>(ErrorCodes.ScopeDenied, "Access denied.");
      var preview = await PreviewAsync(db, a, id, request with { Fields = fields }, ct);
      if (!preview.Succeeded) return Fail<FindingCommandReceiptView>(preview.ErrorCode!, preview.Message!);
      if (!preview.Value!.CanProceed) return Fail<FindingCommandReceiptView>(ErrorCodes.GateBlocked, preview.Value.Blockers.First());
      if (fields.Response is { } response)
      {
        var saved = await AuditPlanningService.RecordFindingResponseAsync(db, a, new(id, response.ManagementResponse, response.Corrected), ct);
        if (!saved.Succeeded) return Fail<FindingCommandReceiptView>(saved.ErrorCode!, saved.Message!);
      }
      else
      {
        var saved = await AuditPlanningService.DesignateManagementLetterFindingAsync(db, a,
          new(id, fields.Action == FindingCommandActions.Designate, fields.Designation?.Recommendation), ct);
        if (!saved.Succeeded) return Fail<FindingCommandReceiptView>(saved.ErrorCode!, saved.Message!);
      }
      var nextRevision = long.Parse(preview.Value.NextRevision, CultureInfo.InvariantCulture);
      var amendment = await db.FindingAmendments.AsNoTracking().SingleAsync(x => x.FirmId == a.FirmId && x.FindingId == id && x.AfterRevision == nextRevision, ct);
      var row = new FindingCommandReceipt { Id = Guid.CreateVersion7(), FirmId = a.FirmId, ClientId = finding.ClientId, EngagementId = finding.EngagementId,
        FindingId = id, AmendmentId = amendment.Id, ActorId = a.UserId, ActorEpoch = a.SessionEpoch, RequestId = request.RequestId, RequestHash = hash,
        ReviewBasis = request.ReviewBasis, Action = fields.Action, InputJson = JsonSerializer.Serialize(fields), PreviewJson = JsonSerializer.Serialize(preview.Value), CreatedAt = DateTimeOffset.UtcNow };
      db.FindingCommandReceipts.Add(row); await db.SaveChangesAsync(ct);
      // PostgreSQL persists timestamps at microsecond precision. The first response uses the same stored row as recovery.
      var stored = await db.FindingCommandReceipts.AsNoTracking().SingleAsync(x => x.Id == row.Id && x.FirmId == a.FirmId && x.ActorId == a.UserId, ct);
      var result = await ViewAsync(db, stored, ct);
      var auth = await AuthorizeAsync(db, a, finding, fields.Action, true, ct);
      if (!auth.Succeeded) return Fail<FindingCommandReceiptView>(auth.ErrorCode!, auth.Message!);
      await tx.CommitAsync(ct); return CommandResult<FindingCommandReceiptView>.Ok(result);
    }
    catch (Exception ex) when (ex.GetBaseException() is DbException cause && cause.GetType().GetProperty("SqlState")?.GetValue(cause) is "40001" or "40P01" or "23505")
    {
      await tx.RollbackAsync(ct);
      return Fail<FindingCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Concurrent finding publication changed the outcome. Look up the retained exact request, then use a fresh context to retry.");
    }
  }
}
