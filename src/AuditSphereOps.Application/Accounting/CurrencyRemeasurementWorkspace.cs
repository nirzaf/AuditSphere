using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record RemeasurementReview(CurrencyRemeasurementScheduleView Schedule, string ReviewToken, bool CanApprove,
  Guid RateSetVersionId, Guid TranslationPolicyVersionId, string RateSource, string ClosingRule, string HistoricalRule, string? Blocker);

/// <summary>Current-revision UI commands. Existing remeasurement services remain the calculation and lineage authority.</summary>
public static class CurrencyRemeasurementWorkspace
{
  private static readonly string[] PrepareRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReviewRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static Task<CommandResult> Auth(IClientAccountingDbContext db, ActorContext a, Guid client, Guid engagement, string[] roles, bool write, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, a, new(a.FirmId, client, engagement, roles, InternalOnly: true, RequireProfessionalWork: write), ct);
  private static string Digest(object value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
  private static async Task Lock(IClientAccountingDbContext db, ActorContext a, Guid client, Guid engagement, CancellationToken ct)
  {
    await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {a.FirmId} FOR SHARE").LoadAsync(ct);
    await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id = {a.FirmId} AND id = {client} FOR UPDATE").LoadAsync(ct);
    await db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE firm_id = {a.FirmId} AND id = {engagement} FOR UPDATE").LoadAsync(ct);
  }
  public static async Task<CommandResult<RemeasurementReview>> DetailAsync(IClientAccountingDbContext db, ActorContext a, Guid id, CancellationToken ct = default)
  {
    var r = await CurrencyRemeasurementService.GetAsync(db, a, id, ct);
    if (!r.Succeeded) return CommandResult<RemeasurementReview>.Fail(r.ErrorCode!, r.Message!);
    var v = r.Value!;
    var stored = await db.CurrencyRemeasurementSchedules.AsNoTracking().SingleAsync(x => x.FirmId == a.FirmId && x.Id == id, ct);
    var rates = await db.ExchangeRateSetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == stored.RateSetVersionId, ct);
    var policy = await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == stored.TranslationPolicyVersionId, ct);
    var choices = await CurrencyRemeasurementWorkspaceQuery.ChoicesAsync(db, a, v.PeriodId, v.EngagementId, ct);
    var hasCurrentLineage = v.Items.Count == v.ItemCount && v.Items.All(x => x.SourceGeneralLedgerLineId.HasValue && x.SourceGlLineDigest.Length == 64);
    var canApprove = hasCurrentLineage && choices.Succeeded && v.Status == AccountingWorkflowStates.Submitted && v.CreatedByUserId != a.UserId &&
      (await Auth(db, a, v.ClientId, v.EngagementId, ReviewRoles, true, ct)).Succeeded;
    var token = Digest(new { a.FirmId, a.UserId, a.SessionEpoch, v, stored, rates, policy, CurrentBase = choices.Value?.BaseRevision, choices.ErrorCode });
    var auth = await Auth(db, a, v.ClientId, v.EngagementId, PrepareRoles, false, ct);
    return auth.Succeeded ? CommandResult<RemeasurementReview>.Ok(new(v, token, canApprove, stored.RateSetVersionId,
      stored.TranslationPolicyVersionId, rates?.Source ?? "Unavailable", policy?.ClosingRateRule ?? "Unavailable", policy?.HistoricalRateRule ?? "Unavailable",
      !hasCurrentLineage ? "Historical workpaper has no immutable GL lineage and is available for inspection only." : choices.Succeeded ? null : "Current approved inputs are unavailable. Refresh the accounting configuration before approval.")) : CommandResult<RemeasurementReview>.Fail(auth.ErrorCode!, auth.Message!);
  }
  public static async Task<CommandResult<Guid>> PrepareAsync(IClientAccountingDbContext db, ActorContext a, CurrencyRemeasurementScheduleRequest request, string token, bool reviewed, CancellationToken ct = default)
  {
    var auth = await Auth(db, a, request.ClientId, request.EngagementId, PrepareRoles, true, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await Lock(db, a, request.ClientId, request.EngagementId, ct);
    auth = await Auth(db, a, request.ClientId, request.EngagementId, PrepareRoles, true, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var choices = await CurrencyRemeasurementWorkspaceQuery.ChoicesAsync(db, a, request.PeriodId, request.EngagementId, ct);
    if (!choices.Succeeded) return CommandResult<Guid>.Fail(choices.ErrorCode!, choices.Message!);
    if (!reviewed || token != choices.Value!.BaseRevision) return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "Refresh and review the current accounting inputs.");
    var writable = await FileFreezeService.RequireWritableAsync(db, a, request.EngagementId, "prepare currency remeasurement", ct);
    if (!writable.Succeeded) { await tx.CommitAsync(ct); return CommandResult<Guid>.Fail(writable.ErrorCode!, writable.Message!); }
    var result = await CurrencyRemeasurementService.PrepareAsync(db, a, request, ct);
    if (result.Succeeded) await tx.CommitAsync(ct);
    return result;
  }
  public static async Task<CommandResult> ApproveAsync(IClientAccountingDbContext db, ActorContext a, Guid id, string token, bool reviewed, CancellationToken ct = default)
  {
    var before = await CurrencyRemeasurementService.GetAsync(db, a, id, ct);
    if (!before.Succeeded) return CommandResult.Fail(before.ErrorCode!, before.Message!);
    var v = before.Value!;
    var auth = await Auth(db, a, v.ClientId, v.EngagementId, ReviewRoles, true, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await Lock(db, a, v.ClientId, v.EngagementId, ct);
    await db.CurrencyRemeasurementSchedules.FromSqlInterpolated($"SELECT * FROM currency_remeasurement_schedules WHERE firm_id = {a.FirmId} AND id = {id} FOR UPDATE").LoadAsync(ct);
    auth = await Auth(db, a, v.ClientId, v.EngagementId, ReviewRoles, true, ct);
    if (!auth.Succeeded) return auth;
    var current = await DetailAsync(db, a, id, ct);
    if (!current.Succeeded) return CommandResult.Fail(current.ErrorCode!, current.Message!);
    if (!reviewed || token != current.Value!.ReviewToken) return CommandResult.Fail(ErrorCodes.StaleRevision, "Refresh and review the current workpaper and source revisions.");
    if (!current.Value.CanApprove) return CommandResult.Fail(ErrorCodes.GateBlocked, "Independent current reviewer authority and immutable GL lineage are required for this submitted workpaper.");
    var writable = await FileFreezeService.RequireWritableAsync(db, a, v.EngagementId, "approve currency remeasurement", ct);
    if (!writable.Succeeded) { await tx.CommitAsync(ct); return writable; }
    var result = await CurrencyRemeasurementService.ApproveAsync(db, a, id, ct);
    // A fail-closed revalidation deliberately persists STALE so the user can inspect that outcome.
    if (result.Succeeded || await db.CurrencyRemeasurementSchedules.AnyAsync(x => x.FirmId == a.FirmId && x.Id == id && x.Status == AccountingWorkflowStates.Stale, ct)) await tx.CommitAsync(ct);
    return result;
  }
}
