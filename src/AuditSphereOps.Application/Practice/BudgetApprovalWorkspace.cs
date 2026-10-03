using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record BudgetApprovalFields(Guid BudgetId, string ExpectedVersion);
public sealed record BudgetApprovalRequest(Guid RequestId, BudgetApprovalFields Fields,
  string? ReviewBasis = null, string? RequestHash = null, bool Reviewed = false);
public sealed record BudgetApprovalPreview(Guid EngagementId, Guid RequestId, string RequestHash,
  string ReviewBasis, BudgetApprovalFields Fields, string Currency, IReadOnlyList<PlanningDraftLine> Lines,
  string ForecastCost, string EngagementGeneration, string ClientGeneration);
public sealed record BudgetApprovalReceipt(Guid Id, Guid BudgetId, Guid EngagementId, Guid ActorId,
  Guid RequestId, string RequestHash, string ReviewBasis, BudgetApprovalPreview Preview, DateTimeOffset CreatedAt);
public sealed record BudgetApprovalLookup(bool Found, BudgetApprovalReceipt? Receipt);

/// <summary>Exact independent approval and actor-owned committed-outcome recovery.</summary>
public static class BudgetApprovalWorkspace
{
  private static bool HashValid(string? s) => s is { Length: 64 } && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static string Exact(decimal value) => value.ToString(CultureInfo.InvariantCulture);
  private static BudgetApprovalFields? Canonical(BudgetApprovalFields? f) =>
    f is not null && f.BudgetId != Guid.Empty && long.TryParse(f.ExpectedVersion, NumberStyles.None,
      CultureInfo.InvariantCulture, out var version) && version > 0
      ? new(f.BudgetId, version.ToString(CultureInfo.InvariantCulture)) : null;
  private static async Task<bool> Authorized(IAuditSphereDbContext db, ActorContext a, Guid id, CancellationToken ct) =>
    (await AuthorizationDecision.AuthorizeAsync(db, a, new(a.FirmId, EngagementId: id,
      RequiredRoles: ["Administrator", "Partner", "Manager"], InternalOnly: true), ct)).Succeeded;
  private static CommandResult<T> Unavailable<T>() => CommandResult<T>.Fail(ErrorCodes.ScopeDenied, "Budget approval is unavailable.");
  private static string RequestHash(ActorContext a, Guid id, Guid requestId, BudgetApprovalFields f) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { a.FirmId, a.UserId, a.SessionEpoch, id, requestId, Action = "BUDGET_APPROVAL", Fields = f }));
  private static BudgetApprovalReceipt Receipt(BudgetApproval row) => new(row.Id, row.BudgetId, row.EngagementId,
    row.ActorId, row.RequestId, row.RequestHash, row.ReviewBasis,
    JsonSerializer.Deserialize<BudgetApprovalPreview>(row.PreviewJson)!, row.CreatedAt);

  public static async Task<CommandResult<BudgetApprovalPreview>> PreviewAsync(IAuditSphereDbContext db,
    ActorContext a, Guid id, BudgetApprovalRequest? r, CancellationToken ct = default)
  {
    if (!await Authorized(db, a, id, ct)) return Unavailable<BudgetApprovalPreview>();
    var fields = Canonical(r?.Fields);
    if (r is null || r.RequestId == Guid.Empty || fields is null)
      return CommandResult<BudgetApprovalPreview>.Fail("request.invalid", "Select the exact current draft budget.");
    var e = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == id, ct);
    if (e is null) return Unavailable<BudgetApprovalPreview>();
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.Id == e.PracticeClientId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    var budget = await db.EngagementBudgets.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.EngagementId == id)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (generation is null || budget is null) return Unavailable<BudgetApprovalPreview>();
    if (budget.Id != fields.BudgetId || Exact(budget.Version) != fields.ExpectedVersion || budget.Status != PracticeTimeStates.BudgetDraft)
      return CommandResult<BudgetApprovalPreview>.Fail(ErrorCodes.StaleRevision, "The draft changed. Refresh and review again.");
    if (budget.CreatedByUserId == a.UserId)
      return CommandResult<BudgetApprovalPreview>.Fail(ErrorCodes.ProtectedState, "A different authorized practitioner must approve this draft.");
    var source = await db.BudgetLines.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.EngagementBudgetId == budget.Id)
      .OrderBy(x => x.Phase).ThenBy(x => x.Role).ThenBy(x => x.Activity).ThenBy(x => x.Id).Take(201).ToListAsync(ct);
    if (source.Count is < 1 or > 200) return CommandResult<BudgetApprovalPreview>.Fail(ErrorCodes.GateBlocked, "The draft has unsupported lines.");
    var lines = source.Select(x => new PlanningDraftLine(x.Role, x.Activity, x.Phase, x.RiskArea, x.ForecastMinutes, Exact(x.ForecastCost))).ToArray();
    var hash = RequestHash(a, id, r.RequestId, fields);
    var basis = Hashing.Sha256Hex(JsonSerializer.Serialize(new { a.FirmId, a.UserId, a.SessionEpoch, id,
      e.Generation, ClientGeneration = generation.Value, Fields = fields, budget.Currency, budget.CreatedByUserId,
      budget.CreatedAt, Lines = lines }));
    if (!await Authorized(db, a, id, ct)) return Unavailable<BudgetApprovalPreview>();
    return CommandResult<BudgetApprovalPreview>.Ok(new(id, r.RequestId, hash, basis, fields, budget.Currency,
      lines, Exact(source.Sum(x => x.ForecastCost)), Exact(e.Generation), Exact(generation.Value)));
  }

  public static async Task<CommandResult<BudgetApprovalReceipt>> ExecuteAsync(IAuditSphereDbContext db,
    ActorContext a, Guid id, BudgetApprovalRequest? r, CancellationToken ct = default)
  {
    if (!await Authorized(db, a, id, ct)) return Unavailable<BudgetApprovalReceipt>();
    var fields = Canonical(r?.Fields);
    if (r is null || !r.Reviewed || r.RequestId == Guid.Empty || fields is null ||
      !HashValid(r.RequestHash) || !HashValid(r.ReviewBasis) || r.RequestHash != RequestHash(a, id, r.RequestId, fields))
      return CommandResult<BudgetApprovalReceipt>.Fail("request.invalid", "Preview and confirm the exact budget approval.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={a.FirmId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct) is null) return Unavailable<BudgetApprovalReceipt>();
    var e = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == id, ct);
    if (e is null) return Unavailable<BudgetApprovalReceipt>();
    if (await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={a.FirmId} AND id={e.PracticeClientId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct) is null) return Unavailable<BudgetApprovalReceipt>();
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={a.FirmId} AND id={a.UserId} FOR SHARE")
      .AsNoTracking().SingleAsync(ct);
    if (!await Authorized(db, a, id, ct)) return Unavailable<BudgetApprovalReceipt>();
    var prior = await db.BudgetApprovals.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == r.RequestId, ct);
    if (prior is not null)
    {
      if (prior.EngagementId != id || prior.RequestHash != r.RequestHash)
        return CommandResult<BudgetApprovalReceipt>.Fail(ErrorCodes.IdempotencyConflict, "A changed approval cannot reuse the request.");
      if (!await Authorized(db, a, id, ct)) return Unavailable<BudgetApprovalReceipt>();
      await tx.CommitAsync(ct);
      return CommandResult<BudgetApprovalReceipt>.Ok(Receipt(prior));
    }
    var preview = await PreviewAsync(db, a, id, r, ct);
    if (!preview.Succeeded) return CommandResult<BudgetApprovalReceipt>.Fail(preview.ErrorCode!, preview.Message!);
    if (preview.Value!.ReviewBasis != r.ReviewBasis)
      return CommandResult<BudgetApprovalReceipt>.Fail(ErrorCodes.StaleRevision, "Approval context changed. Review again.");
    var approved = await PracticeTimeService.ApproveBudgetAsync(db, a, fields.BudgetId, ct);
    if (!approved.Succeeded) return CommandResult<BudgetApprovalReceipt>.Fail(approved.ErrorCode!, approved.Message!);
    var budget = await db.EngagementBudgets.AsNoTracking().SingleAsync(x => x.FirmId == a.FirmId && x.Id == fields.BudgetId, ct);
    var row = new BudgetApproval { Id = Guid.CreateVersion7(), FirmId = a.FirmId, EngagementId = id,
      BudgetId = budget.Id, ActorId = a.UserId, ActorEpoch = a.SessionEpoch, RequestId = r.RequestId,
      RequestHash = r.RequestHash!, ReviewBasis = r.ReviewBasis!, PreviewJson = JsonSerializer.Serialize(preview.Value), CreatedAt = budget.ApprovedAt!.Value };
    db.BudgetApprovals.Add(row);
    await db.SaveChangesAsync(ct);
    if (!await Authorized(db, a, id, ct)) return Unavailable<BudgetApprovalReceipt>();
    await tx.CommitAsync(ct);
    return CommandResult<BudgetApprovalReceipt>.Ok(Receipt(row));
  }

  public static async Task<CommandResult<BudgetApprovalLookup>> LookupAsync(IAuditSphereDbContext db,
    ActorContext a, Guid id, Guid requestId, string? hash, CancellationToken ct = default)
  {
    if (!await Authorized(db, a, id, ct) || requestId == Guid.Empty || !HashValid(hash)) return Unavailable<BudgetApprovalLookup>();
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    // Wait for the same publication lock: absence is definitive only after an in-flight writer finishes.
    if (await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={a.FirmId} FOR SHARE")
      .AsNoTracking().SingleOrDefaultAsync(ct) is null) return Unavailable<BudgetApprovalLookup>();
    var row = await db.BudgetApprovals.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == requestId, ct);
    if (row is not null && (row.EngagementId != id || row.RequestHash != hash))
      return CommandResult<BudgetApprovalLookup>.Fail(ErrorCodes.IdempotencyConflict, "This reference belongs to another approval.");
    if (!await Authorized(db, a, id, ct)) return Unavailable<BudgetApprovalLookup>();
    await tx.CommitAsync(ct);
    return CommandResult<BudgetApprovalLookup>.Ok(new(row is not null, row is null ? null : Receipt(row)));
  }
}
