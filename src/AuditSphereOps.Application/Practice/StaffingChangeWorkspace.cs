using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record StaffingChangeFields(string Action, Guid UserId, string Level, Guid? AssignmentId = null);
public sealed record StaffingChangeRequest(Guid RequestId, StaffingChangeFields Fields,
  string? ReviewBasis = null, string? RequestHash = null, bool Reviewed = false);
public sealed record StaffingChangePreview(Guid EngagementId, Guid RequestId, string RequestHash,
  string ReviewBasis, StaffingChangeFields Fields, string UserName, string AuthorizationRole,
  bool Certified, bool RemovesLocalGrant, string SharePointEffect, string TargetEpoch,
  string EngagementGeneration, string ClientGeneration);
public sealed record StaffingChangeReceipt(Guid Id, Guid AssignmentId, Guid EngagementId, Guid ActorId,
  Guid RequestId, string RequestHash, string ReviewBasis, StaffingChangePreview Preview, DateTimeOffset CreatedAt);
public sealed record StaffingChangeLookup(bool Found, StaffingChangeReceipt? Receipt);

/// <summary>Reviewed local staffing changes and actor-owned committed-outcome recovery.</summary>
public static class StaffingChangeWorkspace
{
  private static bool HashValid(string? s) => s is { Length: 64 } && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static string Exact(long value) => value.ToString(CultureInfo.InvariantCulture);
  private static StaffingChangeFields? Canonical(StaffingChangeFields? f)
  {
    if (f is null || f.UserId == Guid.Empty) return null;
    var action = (f.Action ?? string.Empty).Trim().ToUpperInvariant();
    var level = (f.Level ?? string.Empty).Trim().ToUpperInvariant();
    if (!StaffingLevels.All.Contains(level) || action is not ("ASSIGN" or "REVOKE") ||
      (action == "ASSIGN" && f.AssignmentId is not null) ||
      (action == "REVOKE" && (f.AssignmentId is null || f.AssignmentId == Guid.Empty))) return null;
    return new(action, f.UserId, level, f.AssignmentId);
  }
  private static async Task<bool> Authorized(IAuditSphereDbContext db, ActorContext a, Guid id, CancellationToken ct) =>
    (await AuthorizationDecision.AuthorizeAsync(db, a, new(a.FirmId, EngagementId: id,
      RequiredRoles: ["Administrator", "Partner", "Manager"], InternalOnly: true), ct)).Succeeded;
  private static CommandResult<T> Unavailable<T>() => CommandResult<T>.Fail(ErrorCodes.ScopeDenied, "Staffing change is unavailable.");
  private static string RequestHash(ActorContext a, Guid id, Guid requestId, StaffingChangeFields f) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { a.FirmId, a.UserId, a.SessionEpoch, id, requestId, Action = "STAFFING_CHANGE", Fields = f }));
  private static StaffingChangeReceipt Receipt(StaffingChange row) => new(row.Id, row.AssignmentId, row.EngagementId,
    row.ActorId, row.RequestId, row.RequestHash, row.ReviewBasis,
    JsonSerializer.Deserialize<StaffingChangePreview>(row.PreviewJson)!, row.CreatedAt);

  public static async Task<CommandResult<StaffingChangePreview>> PreviewAsync(IAuditSphereDbContext db,
    ActorContext a, Guid id, StaffingChangeRequest? r, CancellationToken ct = default)
  {
    if (!await Authorized(db, a, id, ct)) return Unavailable<StaffingChangePreview>();
    var fields = Canonical(r?.Fields);
    if (r is null || r.RequestId == Guid.Empty || fields is null)
      return CommandResult<StaffingChangePreview>.Fail("request.invalid", "Select the exact staffing change.");
    var e = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == id, ct);
    if (e is null) return Unavailable<StaffingChangePreview>();
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.Id == e.PracticeClientId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    var target = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == fields.UserId, ct);
    if (generation is null || target is null || target.UserKind != "Staff" || (fields.Action == "ASSIGN" && target.Disabled))
      return Unavailable<StaffingChangePreview>();
    var rank = await StaffingService.ActorRankAsync(db, a, e.PracticeClientId, id, ct);
    if (StaffingLevels.Rank(fields.Level) > rank || (fields.Action == "ASSIGN" && fields.UserId == a.UserId))
      return Unavailable<StaffingChangePreview>();
    var active = await db.EngagementStaffAssignments.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.EngagementId == id && x.RevokedAt == null)
      .OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
    if (active.Count > 1000) return Unavailable<StaffingChangePreview>();
    var existing = active.SingleOrDefault(x => x.UserId == fields.UserId);
    if (fields.Action == "REVOKE" && (existing is null || existing.Id != fields.AssignmentId || existing.StaffingLevel != fields.Level))
      return CommandResult<StaffingChangePreview>.Fail(ErrorCodes.StaleRevision, "The assignment changed. Refresh and review again.");
    if (fields.Action == "ASSIGN" && (existing is not null ||
      (fields.Level == StaffingLevels.EngagementPartner && active.Any(x => x.StaffingLevel == fields.Level))))
      return CommandResult<StaffingChangePreview>.Fail("staffing.conflict", "The requested assignment is no longer available.");
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var certifications = await db.StaffCertifications.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.UserId == fields.UserId &&
      (x.ExpiresOn == null || x.ExpiresOn >= today)).OrderBy(x => x.Id).Select(x => new { x.Id, x.ExpiresOn }).Take(1001).ToListAsync(ct);
    if (certifications.Count > 1000) return Unavailable<StaffingChangePreview>();
    var certified = certifications.Count > 0;
    if (fields.Action == "ASSIGN" && StaffingLevels.Rank(fields.Level) >= 3 && !certified)
      return CommandResult<StaffingChangePreview>.Fail("staffing.certification", "Partner and Manager levels need current certification.");
    var role = StaffingLevels.AuthorizationRole(fields.Level);
    var grants = await db.RoleGrants.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.UserId == fields.UserId &&
      x.ClientId == e.PracticeClientId && x.EngagementId == id && x.Role == role && x.RevokedAt == null)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.Reason, x.ExpiresAt }).Take(1001).ToListAsync(ct);
    if (grants.Count > 1000) return Unavailable<StaffingChangePreview>();
    var removesGrant = fields.Action == "REVOKE" && grants.Any(x => x.Id == existing!.RoleGrantId &&
      x.Reason?.StartsWith("Engagement staffing: ", StringComparison.Ordinal) == true);
    var effect = fields.Action == "ASSIGN"
      ? "Entire client site Full Control; membership remains pending separate Microsoft reconciliation."
      : "Reconcile client-site membership after revocation; other active assignments may retain access.";
    var hash = RequestHash(a, id, r.RequestId, fields);
    var basis = Hashing.Sha256Hex(JsonSerializer.Serialize(new { a.FirmId, a.UserId, a.SessionEpoch, id,
      e.Generation, ClientGeneration = generation.Value, Fields = fields, TargetEpoch = target.SessionEpoch, target.Disabled,
      target.DisplayName, Rank = rank, Active = active, Certifications = certifications, Grants = grants, Day = today }));
    if (!await Authorized(db, a, id, ct)) return Unavailable<StaffingChangePreview>();
    return CommandResult<StaffingChangePreview>.Ok(new(id, r.RequestId, hash, basis, fields, target.DisplayName, role,
      certified, removesGrant, effect, Exact(target.SessionEpoch), Exact(e.Generation), Exact(generation.Value)));
  }

  public static async Task<CommandResult<StaffingChangeReceipt>> ExecuteAsync(IAuditSphereDbContext db,
    ActorContext a, Guid id, StaffingChangeRequest? r, CancellationToken ct = default)
  {
    if (!await Authorized(db, a, id, ct)) return Unavailable<StaffingChangeReceipt>();
    var fields = Canonical(r?.Fields);
    if (r is null || !r.Reviewed || r.RequestId == Guid.Empty || fields is null ||
      !HashValid(r.RequestHash) || !HashValid(r.ReviewBasis) || r.RequestHash != RequestHash(a, id, r.RequestId, fields))
      return CommandResult<StaffingChangeReceipt>.Fail("request.invalid", "Preview and confirm the exact staffing change.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={a.FirmId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct) is null) return Unavailable<StaffingChangeReceipt>();
    var e = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == id, ct);
    if (e is null) return Unavailable<StaffingChangeReceipt>();
    if (await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={a.FirmId} AND id={e.PracticeClientId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct) is null) return Unavailable<StaffingChangeReceipt>();
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={a.FirmId} AND id={a.UserId} FOR SHARE")
      .AsNoTracking().SingleAsync(ct);
    if (!await Authorized(db, a, id, ct)) return Unavailable<StaffingChangeReceipt>();
    var prior = await db.StaffingChanges.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == r.RequestId, ct);
    if (prior is not null)
    {
      if (prior.EngagementId != id || prior.RequestHash != r.RequestHash)
        return CommandResult<StaffingChangeReceipt>.Fail(ErrorCodes.IdempotencyConflict, "A changed staffing action cannot reuse the request.");
      await tx.CommitAsync(ct);
      return CommandResult<StaffingChangeReceipt>.Ok(Receipt(prior));
    }
    if (await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={a.FirmId} AND id={fields.UserId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct) is null) return Unavailable<StaffingChangeReceipt>();
    var preview = await PreviewAsync(db, a, id, r, ct);
    if (!preview.Succeeded) return CommandResult<StaffingChangeReceipt>.Fail(preview.ErrorCode!, preview.Message!);
    if (preview.Value!.ReviewBasis != r.ReviewBasis)
      return CommandResult<StaffingChangeReceipt>.Fail(ErrorCodes.StaleRevision, "Staffing context changed. Review again.");
    Guid assignmentId;
    if (fields.Action == "ASSIGN")
    {
      var assigned = await StaffingService.AssignAsync(db, a, new(id, fields.UserId, fields.Level), ct);
      if (!assigned.Succeeded) return CommandResult<StaffingChangeReceipt>.Fail(assigned.ErrorCode!, assigned.Message!);
      assignmentId = assigned.Value;
    }
    else
    {
      assignmentId = fields.AssignmentId!.Value;
      var revoked = await StaffingService.RevokeAsync(db, a, assignmentId, ct);
      if (!revoked.Succeeded) return CommandResult<StaffingChangeReceipt>.Fail(revoked.ErrorCode!, revoked.Message!);
    }
    var row = new StaffingChange { Id = Guid.CreateVersion7(), FirmId = a.FirmId, EngagementId = id,
      AssignmentId = assignmentId, TargetUserId = fields.UserId, ActorId = a.UserId, ActorEpoch = a.SessionEpoch,
      RequestId = r.RequestId, Action = fields.Action, RequestHash = r.RequestHash!, ReviewBasis = r.ReviewBasis!,
      PreviewJson = JsonSerializer.Serialize(preview.Value), CreatedAt = DateTimeOffset.UtcNow };
    db.StaffingChanges.Add(row);
    await db.SaveChangesAsync(ct);
    if (!await Authorized(db, a, id, ct)) return Unavailable<StaffingChangeReceipt>();
    await tx.CommitAsync(ct);
    return CommandResult<StaffingChangeReceipt>.Ok(Receipt(row));
  }

  public static async Task<CommandResult<StaffingChangeLookup>> LookupAsync(IAuditSphereDbContext db,
    ActorContext a, Guid id, Guid requestId, string? hash, CancellationToken ct = default)
  {
    if (!await Authorized(db, a, id, ct) || requestId == Guid.Empty || !HashValid(hash)) return Unavailable<StaffingChangeLookup>();
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={a.FirmId} FOR SHARE")
      .AsNoTracking().SingleOrDefaultAsync(ct) is null) return Unavailable<StaffingChangeLookup>();
    var row = await db.StaffingChanges.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == requestId, ct);
    if (row is not null && (row.EngagementId != id || row.RequestHash != hash))
      return CommandResult<StaffingChangeLookup>.Fail(ErrorCodes.IdempotencyConflict, "This reference belongs to another staffing change.");
    if (!await Authorized(db, a, id, ct)) return Unavailable<StaffingChangeLookup>();
    await tx.CommitAsync(ct);
    return CommandResult<StaffingChangeLookup>.Ok(new(row is not null, row is null ? null : Receipt(row)));
  }
}
