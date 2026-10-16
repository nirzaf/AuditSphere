using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

/// <summary>Client-scoped reviewed maintenance; the service owns every publishing transaction.</summary>
public static class PeriodMaintenanceWorkspace
{
  internal static readonly string[] PrepareRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  internal static readonly string[] ReviewRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  internal static bool Hash(string? x) => x is { Length: 64 } && x.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  internal static Task<CommandResult> AuthorizeAsync(IClientAccountingDbContext db, ActorContext actor, Guid clientId,
    string action, CancellationToken ct) => AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, clientId, RequiredRoles: action == PeriodMaintenanceActions.ApproveRestatement ? ReviewRoles : PrepareRoles, InternalOnly: true), ct);

  internal static async Task RequestBarrierAsync(IClientAccountingDbContext db, ActorContext actor, Guid requestId, bool publishing, CancellationToken ct)
  {
    if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Request recovery requires a transaction.");
    var digest = Hashing.Sha256Hex(JsonSerializer.Serialize(new { Namespace = "period-maintenance-request-v1", actor.FirmId, actor.UserId, requestId }));
    var key = unchecked((long)Convert.ToUInt64(digest[..16], 16));
    if (publishing) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    else await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({key})", ct);
  }

  internal static PeriodMaintenanceRequest Normalize(PeriodMaintenanceRequest r) => r with
  {
    RollForward = r.RollForward is not { } f ? null : f with { PeriodCode = f.PeriodCode.Trim(), Basis = f.Basis.Trim(),
      Currency = (string.IsNullOrWhiteSpace(f.Currency) ? AccountingDefaults.DefaultCurrency : f.Currency).Trim().ToUpperInvariant(),
      SourceHash = f.SourceHash.Trim().ToLowerInvariant(), EvidenceReference = f.EvidenceReference.Trim(),
      PriorClosingAmount = decimal.Parse(f.PriorClosingAmount.ToString("0.000000", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
      CurrentOpeningAmount = decimal.Parse(f.CurrentOpeningAmount.ToString("0.000000", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) },
    Restatement = r.Restatement is not { } s ? null : s with { RevisedBasis = s.RevisedBasis.Trim(), Reason = s.Reason.Trim(),
      EvidenceReference = s.EvidenceReference.Trim(), ChangeType = s.ChangeType.Trim().ToUpperInvariant(),
      AffectedPeriods = string.Join(',', s.AffectedPeriods.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => x.ToUpperInvariant()).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)) }
  };
  internal static string Input(PeriodMaintenanceRequest r) => JsonSerializer.Serialize(r with { Reviewed = false, ExpectedRequestHash = null });
  internal static string RequestHash(ActorContext a, Guid clientId, PeriodMaintenanceRequest r) => Hashing.Sha256Hex(
    JsonSerializer.Serialize(new { Version = 1, a.FirmId, a.UserId, a.SessionEpoch, clientId, Intent = Input(r) }));
  internal static bool Valid(PeriodMaintenanceRequest? r, Guid clientId)
  {
    if (clientId == Guid.Empty || r is null || r.RequestId == Guid.Empty || !Hash(r.ReviewBasis)) return false;
    static bool Text(string? x, int max) => !string.IsNullOrWhiteSpace(x) && x.Length <= max;
    if (r.Action == PeriodMaintenanceActions.RollForward && r.RollForward is { } f && r.Restatement is null && r.RestatementId is null)
      return f.ClientId == clientId && f.PriorPeriodId != Guid.Empty && Text(f.PeriodCode, 100) && Text(f.Basis, 100) &&
        f.Currency is not null && f.SourceHash is not null && Hash(f.SourceHash.Trim().ToLowerInvariant()) && Text(f.EvidenceReference, 2000) &&
        f.ExpectedPriorRevision is > 0 && f.StartDate <= f.EndDate && f.PriorClosingAmount is >= -9_999_999_999_999.999999m and <= 9_999_999_999_999.999999m &&
        f.CurrentOpeningAmount is >= -9_999_999_999_999.999999m and <= 9_999_999_999_999.999999m &&
        MoneyPolicy.Normalize(f.PriorClosingAmount) == f.PriorClosingAmount && MoneyPolicy.Normalize(f.CurrentOpeningAmount) == f.CurrentOpeningAmount;
    if (r.Action == PeriodMaintenanceActions.CreateRestatement && r.Restatement is { } s && r.RollForward is null && r.RestatementId is null)
      return s.ClientId == clientId && s.PeriodId != Guid.Empty && s.OriginalPackageId != Guid.Empty && s.RevisedPackageId != Guid.Empty &&
        s.OriginalPackageId != s.RevisedPackageId && Text(s.RevisedBasis, 100) && Text(s.Reason, 4000) && Text(s.EvidenceReference, 2000) &&
        s.ChangeType is not null && PeriodRestatementChangeTypes.All.Contains(s.ChangeType.Trim().ToUpperInvariant()) &&
        s.AffectedPeriods is { Length: <= 500 } && (s.ChangeType.Trim().ToUpperInvariant() != PeriodRestatementChangeTypes.ProspectiveEstimateChange || Text(s.AffectedPeriods, 500));
    return r.Action == PeriodMaintenanceActions.ApproveRestatement && r.RestatementId is { } id && id != Guid.Empty && r.RollForward is null && r.Restatement is null;
  }

  public static async Task<CommandResult<PeriodMaintenanceContext>> ContextAsync(IClientAccountingDbContext db, ActorContext a,
    Guid clientId, CancellationToken ct = default)
  {
    var periods = await PeriodMaintenanceQuery.ClosedPeriodsAsync(db, a, clientId, ct);
    if (!periods.Succeeded) return Fail<PeriodMaintenanceContext>(periods.ErrorCode!, periods.Message!);
    var overview = await PeriodMaintenanceQuery.OverviewAsync(db, a, ct);
    if (!overview.Succeeded) return Fail<PeriodMaintenanceContext>(overview.ErrorCode!, overview.Message!);
    var final = await AuthorizeAsync(db, a, clientId, PeriodMaintenanceActions.RollForward, ct);
    return final.Succeeded ? CommandResult<PeriodMaintenanceContext>.Ok(new(periods.Value!, overview.Value!.Restatements.Where(x => x.ClientId == clientId).ToArray())) :
      Fail<PeriodMaintenanceContext>(final.ErrorCode!, final.Message!);
  }

  public static async Task<CommandResult<PeriodMaintenanceState>> StateAsync(IClientAccountingDbContext db, ActorContext a,
    Guid clientId, string action, Guid? periodId = null, Guid? restatementId = null, CancellationToken ct = default)
  {
    if (action is not (PeriodMaintenanceActions.RollForward or PeriodMaintenanceActions.CreateRestatement or PeriodMaintenanceActions.ApproveRestatement))
      return Fail<PeriodMaintenanceState>(ErrorCodes.Accounting.ReconciliationRejected, "Choose a supported maintenance action.");
    var auth = await AuthorizeAsync(db, a, clientId, action, ct);
    if (!auth.Succeeded) return Fail<PeriodMaintenanceState>(auth.ErrorCode!, auth.Message!);
    ClientPeriodRestatement? restatement = null;
    if (action == PeriodMaintenanceActions.ApproveRestatement)
    {
      restatement = await db.ClientPeriodRestatements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.ClientId == clientId && x.Id == restatementId, ct);
      if (restatement is null) return Fail<PeriodMaintenanceState>(ErrorCodes.ScopeDenied, "Access denied.");
      periodId = restatement.PeriodId;
    }
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.ClientId == clientId && x.Id == periodId, ct);
    if (period is null) return Fail<PeriodMaintenanceState>(ErrorCodes.ScopeDenied, "Access denied.");
    var books = await db.ClientReportingBooks.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.ClientId == clientId && x.PeriodId == period.Id).OrderBy(x => x.Id).ToListAsync(ct);
    var start = period.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var end = period.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var packages = await db.FinancialPackages.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.ClientId == clientId &&
      ((x.PeriodStart == start && x.PeriodEnd == end) || (restatement != null && (x.Id == restatement.OriginalPackageId || x.Id == restatement.RevisedPackageId))))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.EngagementId, x.PeriodStart, x.PeriodEnd, x.Currency, x.Status, x.CalculationHash }).ToListAsync(ct);
    var blockers = new List<string>();
    if (action != PeriodMaintenanceActions.ApproveRestatement && period.Status != AccountingWorkflowStates.Closed) blockers.Add("A closed reporting period is required.");
    if (restatement is not null)
    {
      if (restatement.Status != AccountingWorkflowStates.Submitted) blockers.Add("Only a submitted restatement can be approved.");
      if (restatement.CreatedByUserId == a.UserId) blockers.Add("An independent reviewer is required.");
      var original = packages.SingleOrDefault(x => x.Id == restatement.OriginalPackageId);
      var revised = packages.SingleOrDefault(x => x.Id == restatement.RevisedPackageId);
      if (original?.Status != AccountingPackageStates.PackageValidated || revised?.Status != AccountingPackageStates.PackageValidated ||
        original?.CalculationHash != restatement.OriginalPackageHash || revised?.CalculationHash != restatement.RevisedPackageHash)
        blockers.Add("Restatement package lineage changed.");
    }
    var approvalDetail = restatement is null ? null : new PeriodMaintenanceApprovalDetail(restatement.Id, clientId,
      restatement.EngagementId, period.Id, period.PeriodCode, period.StartDate, period.EndDate, period.Currency,
      restatement.OriginalPackageId, restatement.OriginalPackageHash, restatement.RevisedPackageId, restatement.RevisedPackageHash,
      restatement.RevisedBasis, restatement.ChangeType, restatement.AffectedPeriods, restatement.Reason,
      restatement.EvidenceReference, restatement.Status, restatement.CreatedByUserId, restatement.CreatedAt);
    var json = JsonSerializer.Serialize(new { Version = 1, a.FirmId, a.UserId, a.SessionEpoch, clientId, action, Period = period,
      Books = action == PeriodMaintenanceActions.RollForward ? books : null, Packages = packages, Restatement = restatement });
    auth = await AuthorizeAsync(db, a, clientId, action, ct);
    return auth.Succeeded ? CommandResult<PeriodMaintenanceState>.Ok(new(clientId, action, period.Id, restatement?.Id,
      Hashing.Sha256Hex(json), blockers.Count == 0, blockers, json, approvalDetail)) : Fail<PeriodMaintenanceState>(auth.ErrorCode!, auth.Message!);
  }

  public static async Task<CommandResult<PeriodMaintenancePreview>> PreviewAsync(IClientAccountingDbContext db, ActorContext a,
    Guid clientId, PeriodMaintenanceRequest? request, CancellationToken ct = default)
  {
    if (!Valid(request, clientId)) return Fail<PeriodMaintenancePreview>(ErrorCodes.Accounting.ReconciliationRejected, "Provide the complete exact maintenance intent.");
    var r = Normalize(request!);
    var state = await StateAsync(db, a, clientId, r.Action, r.RollForward?.PriorPeriodId ?? r.Restatement?.PeriodId, r.RestatementId, ct);
    if (!state.Succeeded) return Fail<PeriodMaintenancePreview>(state.ErrorCode!, state.Message!);
    if (state.Value!.ReviewBasis != r.ReviewBasis) return Fail<PeriodMaintenancePreview>(ErrorCodes.StaleRevision, "The reviewed maintenance state changed.");
    var blockers = state.Value.Blockers.ToList();
    if (r.RollForward is { } f)
    {
      var prior = await db.ClientReportingPeriods.AsNoTracking().SingleAsync(x => x.FirmId == a.FirmId && x.ClientId == clientId && x.Id == f.PriorPeriodId, ct);
      if (f.ExpectedPriorRevision != prior.Revision || f.StartDate <= prior.EndDate || f.Currency != prior.Currency) blockers.Add("The next period must follow the reviewed prior revision and use its currency.");
      if (await db.ClientReportingPeriods.AnyAsync(x => x.FirmId == a.FirmId && x.ClientId == clientId && x.PeriodCode == f.PeriodCode && x.Basis == f.Basis, ct)) blockers.Add("The reporting period already exists.");
      if (f.SourcePackageId is { } id && !await db.FinancialPackages.AnyAsync(x => x.FirmId == a.FirmId && x.ClientId == clientId && x.Id == id &&
        x.Status == AccountingPackageStates.PackageValidated && x.CalculationHash == f.SourceHash && x.Currency == f.Currency &&
        x.PeriodStart == prior.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) && x.PeriodEnd == prior.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ct)) blockers.Add("The source package does not match the closed period and hash.");
    }
    if (r.Restatement is { } s)
    {
      var p = await db.ClientReportingPeriods.AsNoTracking().SingleAsync(x => x.FirmId == a.FirmId && x.ClientId == clientId && x.Id == s.PeriodId, ct);
      var packages = await db.FinancialPackages.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.ClientId == clientId && (x.Id == s.OriginalPackageId || x.Id == s.RevisedPackageId)).ToListAsync(ct);
      if (packages.Count != 2 || packages.Any(x => x.Status != AccountingPackageStates.PackageValidated || x.Currency != p.Currency ||
        x.PeriodStart != p.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) || x.PeriodEnd != p.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) ||
        packages.Select(x => x.EngagementId).Distinct().Count() != 1 || packages.Select(x => x.CalculationHash).Distinct().Count() != 2)
        blockers.Add("Distinct validated packages from the same engagement, period and currency are required.");
      if (await db.ClientPeriodRestatements.AnyAsync(x => x.FirmId == a.FirmId && x.OriginalPackageId == s.OriginalPackageId && x.RevisedPackageId == s.RevisedPackageId, ct)) blockers.Add("This package restatement already exists.");
    }
    var final = await StateAsync(db, a, clientId, r.Action, state.Value.PeriodId, r.RestatementId, ct);
    if (!final.Succeeded) return Fail<PeriodMaintenancePreview>(final.ErrorCode!, final.Message!);
    if (final.Value!.ReviewBasis != r.ReviewBasis) return Fail<PeriodMaintenancePreview>(ErrorCodes.StaleRevision, "The maintenance state changed during preview.");
    return CommandResult<PeriodMaintenancePreview>.Ok(new(r.RequestId, RequestHash(a, clientId, r), final.Value with { CanProceed = blockers.Count == 0, Blockers = blockers }, r with { Reviewed = false, ExpectedRequestHash = null },
      r.RollForward is { } roll ? $"Create period {roll.PeriodCode} ({roll.StartDate:yyyy-MM-dd} to {roll.EndDate:yyyy-MM-dd}) with opening bridge {roll.CurrentOpeningAmount.ToString(CultureInfo.InvariantCulture)} {roll.Currency}." :
      r.Restatement is { } rest ? $"Submit {rest.ChangeType}: {rest.Reason}; revised basis {rest.RevisedBasis}; affected periods {rest.AffectedPeriods}." :
      $"Approve {final.Value.ApprovalDetail!.ChangeType} for period {final.Value.ApprovalDetail.PeriodCode}: {final.Value.ApprovalDetail.Reason}; revised basis {final.Value.ApprovalDetail.RevisedBasis}; affected periods {final.Value.ApprovalDetail.AffectedPeriods}.",
      final.Value.ApprovalDetail));
  }

  public static Task<CommandResult<PeriodMaintenanceReceiptView>> ExecuteAsync(IClientAccountingDbContext db, ActorContext a,
    Guid clientId, PeriodMaintenanceRequest? request, CancellationToken ct = default) => ClientAccountingService.ExecuteReviewedPeriodMaintenanceAsync(db, a, clientId, request, ct);

  internal static PeriodMaintenanceReceiptView View(PeriodMaintenanceReceipt row) => new(row.Id, row.ClientId, row.ActorId, row.ActorEpoch,
    row.RequestId, row.RequestHash, row.Action, row.ReviewBasis, row.CreatedAt, JsonSerializer.Deserialize<PeriodMaintenanceResult>(row.ResultJson)!);

  public static async Task<CommandResult<PeriodMaintenanceLookup>> LookupAsync(IClientAccountingDbContext db, ActorContext a,
    Guid clientId, Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !Hash(requestHash)) return Fail<PeriodMaintenanceLookup>(ErrorCodes.Accounting.ReconciliationRejected, "Use the retained exact request reference.");
    var auth = await AuthorizeAsync(db, a, clientId, PeriodMaintenanceActions.RollForward, ct);
    if (!auth.Succeeded) return Fail<PeriodMaintenanceLookup>(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={a.FirmId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={a.FirmId} AND id={clientId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={a.FirmId} AND id={a.UserId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    // One actor/request barrier also covers mismatched-client lookups while publication is pending.
    await RequestBarrierAsync(db, a, requestId, false, ct);
    auth = await AuthorizeAsync(db, a, clientId, PeriodMaintenanceActions.RollForward, ct);
    if (!auth.Succeeded) return Fail<PeriodMaintenanceLookup>(auth.ErrorCode!, auth.Message!);
    var row = await db.PeriodMaintenanceReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.ActorId == a.UserId && x.RequestId == requestId, ct);
    if (row is not null && (row.ClientId != clientId || row.RequestHash != requestHash)) return Fail<PeriodMaintenanceLookup>(ErrorCodes.IdempotencyConflict, "This request belongs to another exact intent.");
    if (row is not null && row.ActorEpoch != a.SessionEpoch) return Fail<PeriodMaintenanceLookup>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
    auth = await AuthorizeAsync(db, a, clientId, row?.Action ?? PeriodMaintenanceActions.RollForward, ct);
    if (auth.Succeeded) await tx.CommitAsync(ct);
    return auth.Succeeded ? CommandResult<PeriodMaintenanceLookup>.Ok(new(row is not null, row is null ? null : View(row))) : Fail<PeriodMaintenanceLookup>(auth.ErrorCode!, auth.Message!);
  }
}
