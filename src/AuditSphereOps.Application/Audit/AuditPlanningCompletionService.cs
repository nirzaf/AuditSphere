using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Data.Common;
using System.Text.Json;

namespace AuditSphereOps.Application.Audit;

// T060 completion: ISA 510 opening-balance verification and the planning summary view.
// Materiality assessment, risk identification and population versions already exist in
// AuditPlanningService.
public sealed record OpeningBalanceVerificationRequest(
  Guid EngagementId, Guid? ProcedureId, Guid? PriorPeriodId, string PriorReference,
  DateOnly AsOfDate, string Currency, decimal OpeningSignedTotal, decimal AgreedSignedTotal,
  bool AccountingPoliciesConsistent, string Rationale, IReadOnlyList<string> EvidenceReferences,
  long ExpectedRevision, Guid? ExpectedVerificationId = null);

public sealed record OpeningBalanceVerificationValue(
  Guid VerificationId, string Conclusion, decimal DifferenceAmount, long Revision);

public sealed record OpeningBalanceReviewRequest(Guid EngagementId, Guid VerificationId, long ExpectedRevision, string? Comment);

public sealed record OpeningBalanceVerificationDetail(
  Guid VerificationId, Guid EngagementId, Guid? ProcedureId, Guid? PriorPeriodId, string PriorReference,
  DateOnly AsOfDate, string Currency, decimal OpeningSignedTotal, decimal AgreedSignedTotal,
  decimal DifferenceAmount, bool AccountingPoliciesConsistent, string Conclusion, string Rationale,
  IReadOnlyList<string> EvidenceReferences, long Revision, Guid RecordedByUserId, DateTimeOffset RecordedAt,
  Guid? ReviewedByUserId, DateTimeOffset? ReviewedAt, long? ReviewedBasisRevision, string? ReviewComment,
  bool ComparisonResolved, bool CurrentIndependentReview);

public sealed record AuditPlanningSummaryView(
  Guid EngagementId,
  Guid? LatestMaterialityId, string? MaterialityStatus, decimal? OverallMateriality, string? MaterialityBenchmarkSource,
  int RiskCount, int SignificantRiskCount, int OpenRiskCount,
  int PopulationVersionCount, int ApprovedPopulationCount,
  int ApplicableProcedureCount, int ReviewedProcedureCount,
  Guid? OpeningBalanceVerificationId, string? OpeningBalanceConclusion, bool? AccountingPoliciesConsistent,
  bool OpeningBalancesResolved);

public static class AuditPlanningCompletionService
{
  private static readonly string[] PlanningRoles =
    ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static readonly string[] ReviewRoles = ["Reviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReadRoles =
    ["Auditor", "Reviewer", "Manager", "Partner", "Administrator", "AccountingPreparer", "AccountingReviewer"];
  // Exact-record readback serves existing preparers without widening review or summary authority.
  private static readonly string[] DetailReadRoles = PlanningRoles.Concat(ReadRoles).Distinct().ToArray();

  /// <summary>Records the ISA 510 opening-balance verification for an engagement. The
  /// conclusion is derived from the agreed figures and the policy-consistency answer: an
  /// unexplained difference cannot be concluded as agreed, and an unresolved difference
  /// remains visible so the opening position is not silently accepted.</summary>
  public static Task<CommandResult<OpeningBalanceVerificationValue>> RecordOpeningBalanceVerificationAsync(
    IClientAccountingDbContext db, ActorContext actor, OpeningBalanceVerificationRequest request,
    CancellationToken ct = default) => RecordCoreAsync(db, actor, request, null, ct);

  internal static Task<CommandResult<OpeningBalanceVerificationValue>> RecordOpeningBalanceVerificationWithReceiptAsync(
    IClientAccountingDbContext db, ActorContext actor, OpeningBalanceVerificationRequest request,
    OpeningBalanceReceiptContext receipt, CancellationToken ct) => RecordCoreAsync(db, actor, request, receipt, ct);

  private static async Task<CommandResult<OpeningBalanceVerificationValue>> RecordCoreAsync(
    IClientAccountingDbContext db, ActorContext actor, OpeningBalanceVerificationRequest request,
    OpeningBalanceReceiptContext? receipt, CancellationToken ct)
  {
    if (UnsafeCommandContext(db))
      return Failure<OpeningBalanceVerificationValue>(ErrorCodes.StaleRevision, "Use a fresh command context without caller-owned transactions, pending changes or tracked opening-balance records.");
    var currency = request.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
    if (request.EngagementId == Guid.Empty || string.IsNullOrWhiteSpace(request.PriorReference) ||
        request.PriorReference.Trim().Length > 200 || currency.Length != 3 ||
        currency.Any(c => c is < 'A' or > 'Z') || string.IsNullOrWhiteSpace(request.Rationale) ||
        request.Rationale.Trim().Length > 2000 || request.EvidenceReferences is null ||
        request.EvidenceReferences.All(x => string.IsNullOrWhiteSpace(x)))
      return CommandResult<OpeningBalanceVerificationValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "An opening-balance verification needs the engagement, prior reference, currency, rationale and evidence.");
    if (request.ExpectedRevision < 0 || (request.ExpectedRevision == 0) != (request.ExpectedVerificationId is null) || !Money(request.OpeningSignedTotal) || !Money(request.AgreedSignedTotal))
      return Failure<OpeningBalanceVerificationValue>(ErrorCodes.Accounting.MappingInvalid, "Use exact six-decimal figures and an explicit expected revision (zero to create).");
    var evidence = JsonSerializer.Serialize(request.EvidenceReferences.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray());
    if (evidence.Length > 20000)
      return Failure<OpeningBalanceVerificationValue>(ErrorCodes.Accounting.MappingInvalid, "Opening-balance evidence exceeds the retained limit.");
    var auth = await AuthorizeAsync(db, actor, request.EngagementId, PlanningRoles, true, ct);
    if (!auth.Succeeded) return Failure<OpeningBalanceVerificationValue>(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    try
    {
      auth = await LockAsync(db, actor, request.EngagementId, PlanningRoles, ct);
      if (!auth.Succeeded)
      {
        await tx.CommitAsync(ct); // Preserve any refused frozen-access audit evidence; no opening-balance write occurred.
        return Failure<OpeningBalanceVerificationValue>(auth.ErrorCode!, auth.Message!);
      }
      if (receipt is not null)
      {
        var recovered = await AuditOpeningBalanceWorkspace.ResolveAsync(db, actor, request.EngagementId, receipt, ct);
        if (!recovered.Succeeded) return Failure<OpeningBalanceVerificationValue>(recovered.ErrorCode!, recovered.Message!);
        if (recovered.Value!.Receipt is { } retained)
        {
          await tx.CommitAsync(ct);
          return CommandResult<OpeningBalanceVerificationValue>.Ok(new(retained.Result.Id, retained.Result.Conclusion,
            decimal.Parse(retained.Result.DifferenceAmount, System.Globalization.CultureInfo.InvariantCulture),
            long.Parse(retained.Result.Revision, System.Globalization.CultureInfo.InvariantCulture)));
        }
      }
      var engagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.Id == request.EngagementId, ct);
      var existing = await db.OpeningBalanceVerifications.FromSqlInterpolated($"SELECT * FROM opening_balance_verifications WHERE firm_id={actor.FirmId} AND engagement_id={request.EngagementId} AND as_of_date={request.AsOfDate} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct);
      if ((existing?.Revision ?? 0) != request.ExpectedRevision || existing?.Id != request.ExpectedVerificationId || request.ExpectedRevision == long.MaxValue)
        return Failure<OpeningBalanceVerificationValue>(ErrorCodes.StaleRevision, "The opening-balance verification changed. Read its current revision before submitting.");
      if (existing is not null && (existing.ClientId != engagement.PracticeClientId || existing.RecordedByUserId != actor.UserId))
        return Failure<OpeningBalanceVerificationValue>(ErrorCodes.ScopeDenied, "Only the recording auditor can revise this exact opening-balance verification.");
      if (request.ProcedureId is { } procedureId && !await db.AuditProcedures.AsNoTracking().AnyAsync(x =>
          x.Id == procedureId && x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId && x.EngagementId == request.EngagementId, ct) ||
          request.PriorPeriodId is { } priorPeriodId && !await db.ClientReportingPeriods.AsNoTracking().AnyAsync(x =>
          x.Id == priorPeriodId && x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId, ct))
        return Failure<OpeningBalanceVerificationValue>(ErrorCodes.ScopeDenied, "The linked procedure or prior reporting period is outside this verification's scope.");
      // Recheck current grants, epoch and professional gates after every potentially blocking lock.
      auth = await AuthorizeAsync(db, actor, request.EngagementId, PlanningRoles, true, ct);
      if (!auth.Succeeded)
      {
        await tx.CommitAsync(ct); // Preserve any refused frozen-access audit evidence; no opening-balance write occurred.
        return Failure<OpeningBalanceVerificationValue>(auth.ErrorCode!, auth.Message!);
      }
      var difference = decimal.Round(request.OpeningSignedTotal - request.AgreedSignedTotal, 6, MidpointRounding.ToEven);
      if (!Money(difference)) return Failure<OpeningBalanceVerificationValue>(ErrorCodes.Accounting.MappingInvalid, "The comparison exceeds supported monetary precision.");
      var conclusion = difference != 0m ? OpeningBalanceVerificationConclusions.DifferencesUnresolved
        : request.AccountingPoliciesConsistent ? OpeningBalanceVerificationConclusions.Agreed : OpeningBalanceVerificationConclusions.DifferencesResolved;
      var now = DateTimeOffset.UtcNow;
      var id = existing?.Id ?? Guid.CreateVersion7();
      var revision = request.ExpectedRevision + 1;
      if (existing is null)
      {
        db.OpeningBalanceVerifications.Add(new OpeningBalanceVerification {
          Id = id, FirmId = actor.FirmId, ClientId = engagement.PracticeClientId, EngagementId = request.EngagementId,
          ProcedureId = request.ProcedureId, PriorPeriodId = request.PriorPeriodId, PriorReference = request.PriorReference.Trim(),
          AsOfDate = request.AsOfDate, Currency = currency, OpeningSignedTotal = request.OpeningSignedTotal,
          AgreedSignedTotal = request.AgreedSignedTotal, DifferenceAmount = difference, AccountingPoliciesConsistent = request.AccountingPoliciesConsistent,
          Conclusion = conclusion, Rationale = request.Rationale.Trim(), EvidenceReferencesJson = evidence,
          Revision = revision, RecordedByUserId = actor.UserId, RecordedAt = now });
        await db.SaveChangesAsync(ct);
      }
      else
      {
        // A fresh database predicate fences tracked caller snapshots. Retain the last review as historical
        // metadata without claiming the previous reviewed content is reconstructed.
        var changed = await db.OpeningBalanceVerifications.Where(x => x.Id == id && x.FirmId == actor.FirmId &&
          x.EngagementId == request.EngagementId && x.Revision == request.ExpectedRevision).ExecuteUpdateAsync(set => set
          .SetProperty(x => x.ProcedureId, request.ProcedureId).SetProperty(x => x.PriorPeriodId, request.PriorPeriodId)
          .SetProperty(x => x.PriorReference, request.PriorReference.Trim()).SetProperty(x => x.Currency, currency)
          .SetProperty(x => x.OpeningSignedTotal, request.OpeningSignedTotal).SetProperty(x => x.AgreedSignedTotal, request.AgreedSignedTotal)
          .SetProperty(x => x.DifferenceAmount, difference).SetProperty(x => x.AccountingPoliciesConsistent, request.AccountingPoliciesConsistent)
          .SetProperty(x => x.Conclusion, conclusion).SetProperty(x => x.Rationale, request.Rationale.Trim())
          .SetProperty(x => x.EvidenceReferencesJson, evidence).SetProperty(x => x.Revision, revision).SetProperty(x => x.RecordedAt, now)
          .SetProperty(x => x.ReviewedByUserId, (Guid?)null).SetProperty(x => x.ReviewedAt, (DateTimeOffset?)null), ct);
        if (changed != 1) return Failure<OpeningBalanceVerificationValue>(ErrorCodes.StaleRevision, "The opening-balance verification changed.");
      }
      if (receipt is not null) await AuditOpeningBalanceWorkspace.RetainAsync(db, actor, request.EngagementId, id, receipt, ct);
      await tx.CommitAsync(ct);
      return CommandResult<OpeningBalanceVerificationValue>.Ok(new(id, conclusion, difference, revision));
    }
    catch (Exception ex) when (Concurrent(ex))
    {
      await tx.RollbackAsync(ct);
      return Failure<OpeningBalanceVerificationValue>(ErrorCodes.StaleRevision, "Concurrent opening-balance work changed this basis. Read again using a fresh context.");
    }
  }

  /// <summary>Independent review is a separate revision of the exact eligible preparation.</summary>
  public static Task<CommandResult> ReviewOpeningBalanceVerificationAsync(
    IClientAccountingDbContext db, ActorContext actor, OpeningBalanceReviewRequest request, CancellationToken ct = default)
    => ReviewCoreAsync(db, actor, request, null, ct);

  internal static Task<CommandResult> ReviewOpeningBalanceVerificationWithReceiptAsync(
    IClientAccountingDbContext db, ActorContext actor, OpeningBalanceReviewRequest request,
    OpeningBalanceReceiptContext receipt, CancellationToken ct) => ReviewCoreAsync(db, actor, request, receipt, ct);

  private static async Task<CommandResult> ReviewCoreAsync(IClientAccountingDbContext db, ActorContext actor,
    OpeningBalanceReviewRequest request, OpeningBalanceReceiptContext? receipt, CancellationToken ct)
  {
    if (UnsafeCommandContext(db))
      return CommandResult.Fail(ErrorCodes.StaleRevision, "Use a fresh command context without caller-owned transactions, pending changes or tracked opening-balance records.");
    if (request.ExpectedRevision <= 0 || request.ExpectedRevision == long.MaxValue || request.Comment?.Length > 2000)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Use the exact positive revision and a review comment of at most 2000 characters.");
    var auth = await AuthorizeAsync(db, actor, request.EngagementId, ReviewRoles, true, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    try
    {
      auth = await LockAsync(db, actor, request.EngagementId, ReviewRoles, ct);
      if (!auth.Succeeded)
      {
        await tx.CommitAsync(ct); // Preserve refusal evidence without changing the verification.
        return auth;
      }
      if (receipt is not null)
      {
        var recovered = await AuditOpeningBalanceWorkspace.ResolveAsync(db, actor, request.EngagementId, receipt, ct);
        if (!recovered.Succeeded) return CommandResult.Fail(recovered.ErrorCode!, recovered.Message!);
        if (recovered.Value!.Found) { await tx.CommitAsync(ct); return CommandResult.Ok(); }
      }
      var verification = await db.OpeningBalanceVerifications.FromSqlInterpolated($"SELECT * FROM opening_balance_verifications WHERE id={request.VerificationId} AND firm_id={actor.FirmId} AND engagement_id={request.EngagementId} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct);
      if (verification is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, verification.ClientId, request.EngagementId,
        ReviewRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
      if (!auth.Succeeded)
      {
        await tx.CommitAsync(ct); // Preserve refusal evidence without changing the verification.
        return auth;
      }
      if (verification.Revision != request.ExpectedRevision)
        return CommandResult.Fail(ErrorCodes.StaleRevision, "The opening-balance preparation changed. Read it again before reviewing.");
      if (verification.RecordedByUserId == actor.UserId)
        return CommandResult.Fail(ErrorCodes.ScopeDenied, "Separation of duties: the preparer cannot review their own opening-balance verification.");
      if (verification.DifferenceAmount != 0 || verification.Conclusion is not (OpeningBalanceVerificationConclusions.Agreed or OpeningBalanceVerificationConclusions.DifferencesResolved))
        return CommandResult.Fail(ErrorCodes.GateBlocked, "An unresolved opening-balance difference must be resolved before independent review.");
      if (OpeningBalanceReviewCurrentness.IsCurrent(verification))
        return CommandResult.Fail(ErrorCodes.GateBlocked, "This preparation already has a current independent review.");
      var now = DateTimeOffset.UtcNow;
      var changed = await db.OpeningBalanceVerifications.Where(x => x.Id == request.VerificationId && x.FirmId == actor.FirmId &&
        x.EngagementId == request.EngagementId && x.Revision == request.ExpectedRevision).ExecuteUpdateAsync(set => set
        .SetProperty(x => x.ReviewedByUserId, actor.UserId).SetProperty(x => x.ReviewedAt, now)
        .SetProperty(x => x.ReviewedBasisRevision, request.ExpectedRevision).SetProperty(x => x.ReviewComment, request.Comment)
        .SetProperty(x => x.Revision, request.ExpectedRevision + 1), ct);
      if (changed != 1) return CommandResult.Fail(ErrorCodes.StaleRevision, "The opening-balance preparation changed.");
      if (receipt is not null) await AuditOpeningBalanceWorkspace.RetainAsync(db, actor, request.EngagementId, request.VerificationId, receipt, ct);
      await tx.CommitAsync(ct);
      return CommandResult.Ok();
    }
    catch (Exception ex) when (Concurrent(ex))
    {
      await tx.RollbackAsync(ct);
      return CommandResult.Fail(ErrorCodes.StaleRevision, "Concurrent opening-balance work changed this basis. Read again using a fresh context.");
    }
  }

  public static async Task<CommandResult<OpeningBalanceVerificationDetail>> GetOpeningBalanceVerificationAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, Guid verificationId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, engagementId, DetailReadRoles, false, ct);
    if (!auth.Succeeded) return Failure<OpeningBalanceVerificationDetail>(auth.ErrorCode!, auth.Message!);
    var row = await db.OpeningBalanceVerifications.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.Id == verificationId, ct);
    if (row is null) return Failure<OpeningBalanceVerificationDetail>(ErrorCodes.ScopeDenied, "Access denied.");
    auth = await AuthorizeAsync(db, actor, engagementId, DetailReadRoles, false, ct);
    if (!auth.Succeeded) return Failure<OpeningBalanceVerificationDetail>(auth.ErrorCode!, auth.Message!);
    return CommandResult<OpeningBalanceVerificationDetail>.Ok(new(row.Id, row.EngagementId, row.ProcedureId, row.PriorPeriodId,
      row.PriorReference, row.AsOfDate, row.Currency, row.OpeningSignedTotal, row.AgreedSignedTotal, row.DifferenceAmount,
      row.AccountingPoliciesConsistent, row.Conclusion, row.Rationale, JsonSerializer.Deserialize<string[]>(row.EvidenceReferencesJson) ?? [],
      row.Revision, row.RecordedByUserId, row.RecordedAt, row.ReviewedByUserId, row.ReviewedAt, row.ReviewedBasisRevision, row.ReviewComment,
      row.Conclusion is OpeningBalanceVerificationConclusions.Agreed or OpeningBalanceVerificationConclusions.DifferencesResolved,
      OpeningBalanceReviewCurrentness.IsCurrent(row)));
  }

  private static bool UnsafeCommandContext(IAuditSphereDbContext db)
  {
    var context = db.Database.GetService<ICurrentDbContext>().Context;
    return AuthorizationDecision.HasCallerTransaction(db) || context.ChangeTracker.HasChanges() ||
      context.ChangeTracker.Entries<OpeningBalanceVerification>().Any();
  }
  private static bool Money(decimal value) => value is >= -9_999_999_999_999.999999m and <= 9_999_999_999_999.999999m && decimal.Round(value, 6) == value;
  private static bool Concurrent(Exception ex) => ex is DbUpdateConcurrencyException ||
    ex.GetBaseException() is DbException cause && cause.GetType().GetProperty("SqlState")?.GetValue(cause) is "40001" or "40P01" or "23505";
  private static CommandResult<T> Failure<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static async Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId,
    string[] roles, bool writing, CancellationToken ct)
  {
    var e = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    return e is null ? CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.") :
      await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, e.PracticeClientId, engagementId, roles,
        InternalOnly: true, RequireProfessionalWork: writing), ct);
  }
  private static async Task<CommandResult> LockAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId,
    string[] roles, CancellationToken ct)
  {
    var locked = await AuditPlanningCommandWorkspace.LockAsync(db, actor, engagementId, true, ct);
    if (!locked.Succeeded) return locked;
    await db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE firm_id={actor.FirmId} AND id={engagementId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    return await AuthorizeAsync(db, actor, engagementId, roles, true, ct);
  }

  /// <summary>Planning summary for one engagement: latest materiality, risk counts,
  /// population versions, procedure progress and the opening-balance state. Reports
  /// facts only; it never expresses an audit conclusion.</summary>
  public static async Task<CommandResult<AuditPlanningSummaryView>> GetAuditPlanningSummaryAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty)
      return CommandResult<AuditPlanningSummaryView>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "An engagement id is required.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null)
      return CommandResult<AuditPlanningSummaryView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<AuditPlanningSummaryView>.Fail(auth.ErrorCode!, auth.Message!);

    var materiality = await db.MaterialityAssessments.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .Select(x => new { x.Id, x.Status, x.OverallMateriality, x.BenchmarkSource })
      .FirstOrDefaultAsync(ct);
    var risks = await db.AuditRisks.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .Select(x => x.Severity).ToListAsync(ct);
    var openRisks = await db.AuditRisks.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .CountAsync(x => x.Status != "CLOSED", ct);
    var populations = await db.PopulationVersions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .Select(x => x.Status).ToListAsync(ct);
    var procedures = await db.AuditProcedures.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .Select(x => x.Status).ToListAsync(ct);
    var openingBalance = await db.OpeningBalanceVerifications.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.AsOfDate).ThenByDescending(x => x.Id)
      .Select(x => new { x.Id, x.Conclusion, x.AccountingPoliciesConsistent })
      .FirstOrDefaultAsync(ct);

    return CommandResult<AuditPlanningSummaryView>.Ok(new AuditPlanningSummaryView(
      engagementId,
      materiality?.Id, materiality?.Status, materiality?.OverallMateriality, materiality?.BenchmarkSource,
      risks.Count, risks.Count(x => x == RiskSeverities.Significant), openRisks,
      populations.Count, populations.Count(x => x == PopulationStatuses.Approved),
      procedures.Count(x => x == AuditProcedureStatuses.Planned || x == AuditProcedureStatuses.InProgress ||
        x == AuditProcedureStatuses.Submitted || x == AuditProcedureStatuses.InReview ||
        x == AuditProcedureStatuses.Reviewed || x == AuditProcedureStatuses.ChangesRequired),
      procedures.Count(x => x == AuditProcedureStatuses.Reviewed),
      openingBalance?.Id, openingBalance?.Conclusion, openingBalance?.AccountingPoliciesConsistent,
      OpeningBalancesResolved: openingBalance is not null &&
        openingBalance.Conclusion != OpeningBalanceVerificationConclusions.DifferencesUnresolved &&
        openingBalance.Conclusion != OpeningBalanceVerificationConclusions.NotVerifiable));
  }
}
