using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

// T060 completion: ISA 510 opening-balance verification and the planning summary view.
// Materiality assessment, risk identification and population versions already exist in
// AuditPlanningService.
public sealed record OpeningBalanceVerificationRequest(
  Guid EngagementId, Guid? ProcedureId, Guid? PriorPeriodId, string PriorReference,
  DateOnly AsOfDate, string Currency, decimal OpeningSignedTotal, decimal AgreedSignedTotal,
  bool AccountingPoliciesConsistent, string Rationale, IReadOnlyList<string> EvidenceReferences);

public sealed record OpeningBalanceVerificationValue(
  Guid VerificationId, string Conclusion, decimal DifferenceAmount, long Revision);

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

  /// <summary>Records the ISA 510 opening-balance verification for an engagement. The
  /// conclusion is derived from the agreed figures and the policy-consistency answer: an
  /// unexplained difference cannot be concluded as agreed, and an unresolved difference
  /// remains visible so the opening position is not silently accepted.</summary>
  public static async Task<CommandResult<OpeningBalanceVerificationValue>> RecordOpeningBalanceVerificationAsync(
    IClientAccountingDbContext db, ActorContext actor, OpeningBalanceVerificationRequest request,
    CancellationToken ct = default)
  {
    var currency = request.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
    if (request.EngagementId == Guid.Empty || string.IsNullOrWhiteSpace(request.PriorReference) ||
        request.PriorReference.Trim().Length > 200 || currency.Length != 3 ||
        currency.Any(c => c is < 'A' or > 'Z') || string.IsNullOrWhiteSpace(request.Rationale) ||
        request.Rationale.Trim().Length > 2000 || request.EvidenceReferences is null ||
        request.EvidenceReferences.All(x => string.IsNullOrWhiteSpace(x)))
      return CommandResult<OpeningBalanceVerificationValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "An opening-balance verification needs the engagement, prior reference, currency, rationale and evidence.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, EngagementId: request.EngagementId, RequiredRoles: PlanningRoles,
        InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded)
      return CommandResult<OpeningBalanceVerificationValue>.Fail(auth.ErrorCode!, auth.Message!);
    var engagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == request.EngagementId, ct);

    var difference = decimal.Round(request.OpeningSignedTotal - request.AgreedSignedTotal, 6, MidpointRounding.ToEven);
    var conclusion = difference != 0m
      ? OpeningBalanceVerificationConclusions.DifferencesUnresolved
      : request.AccountingPoliciesConsistent
        ? OpeningBalanceVerificationConclusions.Agreed
        : OpeningBalanceVerificationConclusions.DifferencesResolved;

    var existing = await db.OpeningBalanceVerifications.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId && x.AsOfDate == request.AsOfDate, ct);
    if (existing is not null && existing.RecordedByUserId != actor.UserId)
      return CommandResult<OpeningBalanceVerificationValue>.Fail(ErrorCodes.ScopeDenied,
        "Only the recording auditor can revise this opening-balance verification.");
    if (existing is null)
    {
      existing = new OpeningBalanceVerification
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = engagement.PracticeClientId,
        EngagementId = request.EngagementId, RecordedByUserId = actor.UserId, RecordedAt = DateTimeOffset.UtcNow
      };
      db.OpeningBalanceVerifications.Add(existing);
    }
    else
    {
      existing.Revision++;
      existing.RecordedAt = DateTimeOffset.UtcNow;
      existing.ReviewedByUserId = null;
      existing.ReviewedAt = null;
    }
    existing.ProcedureId = request.ProcedureId;
    existing.PriorPeriodId = request.PriorPeriodId;
    existing.PriorReference = request.PriorReference.Trim();
    existing.AsOfDate = request.AsOfDate;
    existing.Currency = currency;
    existing.OpeningSignedTotal = request.OpeningSignedTotal;
    existing.AgreedSignedTotal = request.AgreedSignedTotal;
    existing.DifferenceAmount = difference;
    existing.AccountingPoliciesConsistent = request.AccountingPoliciesConsistent;
    existing.Conclusion = conclusion;
    existing.Rationale = request.Rationale.Trim();
    existing.EvidenceReferencesJson = System.Text.Json.JsonSerializer.Serialize(
      request.EvidenceReferences.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray());
    await db.SaveChangesAsync(ct);

    return CommandResult<OpeningBalanceVerificationValue>.Ok(new OpeningBalanceVerificationValue(
      existing.Id, existing.Conclusion, existing.DifferenceAmount, existing.Revision));
  }

  /// <summary>Independent review clears an opening-balance verification only when the
  /// conclusion is supported; an unresolved difference stays blocked after review.</summary>
  public static async Task<CommandResult> ReviewOpeningBalanceVerificationAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid verificationId, string comment,
    CancellationToken ct = default)
  {
    var verification = await db.OpeningBalanceVerifications.SingleOrDefaultAsync(x =>
      x.Id == verificationId && x.FirmId == actor.FirmId, ct);
    if (verification is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(verification.FirmId, verification.ClientId, verification.EngagementId,
        ReviewRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return auth;
    if (verification.RecordedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied,
        "Separation of duties: the preparer cannot review their own opening-balance verification.");
    if (verification.Conclusion == OpeningBalanceVerificationConclusions.DifferencesUnresolved)
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "An unresolved opening-balance difference must be resolved before independent review.");
    verification.ReviewedByUserId = actor.UserId;
    verification.ReviewedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
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
