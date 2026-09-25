using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

// T073-T075: analytical-review variance investigation, going-concern assessment and
// subsequent-event review. Each derives its conclusion from the recorded evidence so a
// professional conclusion cannot be asserted without the facts that support it.
public sealed record VarianceInvestigationRequest(
  Guid EngagementId, Guid? ProcedureId, string AccountArea, string PeriodReference,
  decimal ExpectedAmount, decimal ActualAmount, decimal InvestigationThreshold,
  string Currency, string? Explanation, IReadOnlyList<string> EvidenceReferences);

public sealed record VarianceInvestigationValue(
  Guid InvestigationId, decimal DifferenceAmount, decimal? DifferencePercent,
  bool ExceedsThreshold, string Conclusion, long Revision);

public sealed record GoingConcernAssessmentRequest(
  Guid EngagementId, Guid? ProcedureId, DateOnly AssessmentDate, DateOnly PeriodCoveredTo,
  string ForecastReviewOutcome, bool MaterialUncertaintyIdentified, bool DisclosureAdequate,
  string Currency, string Rationale, IReadOnlyList<string> EvidenceReferences);

public sealed record GoingConcernAssessmentValue(
  Guid AssessmentId, string Conclusion, bool MaterialUncertaintyIdentified, long Revision);

public sealed record SubsequentEventReviewRequest(
  Guid EngagementId, Guid? ProcedureId, DateOnly PeriodEndDate, DateOnly EventDate,
  string Description, string Classification, bool AdjustmentRequired, bool DisclosureRequired,
  string? DisclosureReference, string? Rationale, string Currency, decimal? FinancialEffect,
  IReadOnlyList<string> EvidenceReferences);

public sealed record SubsequentEventReviewValue(
  Guid ReviewId, string Classification, bool AdjustmentRequired, bool DisclosureRequired,
  int PendingEventCount, long Revision);

public static class AuditCompletionService
{
  private static readonly string[] PlanningRoles =
    ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static readonly string[] ReviewRoles = ["Reviewer", "Manager", "Partner", "Administrator"];

  /// <summary>Records an analytical-review variance with its investigation. The conclusion
  /// follows from the facts: a difference above the threshold without an explanation is
  /// UNEXPLAINED and cannot be closed by assertion.</summary>
  public static async Task<CommandResult<VarianceInvestigationValue>> RecordVarianceInvestigationAsync(
    IClientAccountingDbContext db, ActorContext actor, VarianceInvestigationRequest request,
    CancellationToken ct = default)
  {
    var currency = request.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
    if (request.EngagementId == Guid.Empty || string.IsNullOrWhiteSpace(request.AccountArea) ||
        string.IsNullOrWhiteSpace(request.PeriodReference) || request.InvestigationThreshold < 0m ||
        currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') ||
        (request.Explanation?.Trim().Length ?? 0) > 4000)
      return CommandResult<VarianceInvestigationValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A variance investigation needs the engagement, area, period, currency and a non-negative threshold.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, EngagementId: request.EngagementId, RequiredRoles: PlanningRoles,
        InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded)
      return CommandResult<VarianceInvestigationValue>.Fail(auth.ErrorCode!, auth.Message!);
    var engagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == request.EngagementId, ct);

    var difference = decimal.Round(request.ActualAmount - request.ExpectedAmount, 6, MidpointRounding.ToEven);
    var absolute = difference < 0m ? -difference : difference;
    var exceeds = absolute > request.InvestigationThreshold;
    var hasExplanation = !string.IsNullOrWhiteSpace(request.Explanation);
    var conclusion = !exceeds
      ? VarianceInvestigationConclusions.Explained
      : hasExplanation ? VarianceInvestigationConclusions.Corroborated : VarianceInvestigationConclusions.Unexplained;
    var percent = request.ExpectedAmount == 0m
      ? (decimal?)null
      : decimal.Round(difference / (request.ExpectedAmount < 0m ? -request.ExpectedAmount : request.ExpectedAmount) * 100m, 4, MidpointRounding.ToEven);

    var area = request.AccountArea.Trim().ToUpperInvariant();
    var period = request.PeriodReference.Trim().ToUpperInvariant();
    var existing = await db.AnalyticalReviewVarianceInvestigations.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId &&
      x.AccountArea == area && x.PeriodReference == period, ct);
    if (existing is not null && existing.RecordedByUserId != actor.UserId)
      return CommandResult<VarianceInvestigationValue>.Fail(ErrorCodes.ScopeDenied,
        "Only the recording auditor can revise this variance investigation.");
    if (existing is null)
    {
      existing = new AnalyticalReviewVarianceInvestigation
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = engagement.PracticeClientId,
        EngagementId = request.EngagementId, RecordedByUserId = actor.UserId, RecordedAt = DateTimeOffset.UtcNow
      };
      db.AnalyticalReviewVarianceInvestigations.Add(existing);
    }
    else
    {
      existing.Revision++;
      existing.RecordedAt = DateTimeOffset.UtcNow;
      existing.ReviewedByUserId = null;
      existing.ReviewedAt = null;
    }
    existing.ProcedureId = request.ProcedureId;
    existing.AccountArea = area;
    existing.PeriodReference = period;
    existing.ExpectedAmount = request.ExpectedAmount;
    existing.ActualAmount = request.ActualAmount;
    existing.DifferenceAmount = difference;
    existing.DifferencePercent = percent;
    existing.InvestigationThreshold = request.InvestigationThreshold;
    existing.ExceedsThreshold = exceeds;
    existing.Explanation = hasExplanation ? request.Explanation!.Trim() : null;
    existing.Conclusion = conclusion;
    existing.Currency = currency;
    existing.EvidenceReferencesJson = System.Text.Json.JsonSerializer.Serialize(
      (request.EvidenceReferences ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray());
    await db.SaveChangesAsync(ct);

    return CommandResult<VarianceInvestigationValue>.Ok(new VarianceInvestigationValue(
      existing.Id, difference, percent, exceeds, conclusion, existing.Revision));
  }

  /// <summary>Records the going-concern assessment. A material uncertainty requires either
  /// adequate disclosure or an explicit inadequate-disclosure conclusion; the platform
  /// records the practitioner's determination and never forms the opinion itself.</summary>
  public static async Task<CommandResult<GoingConcernAssessmentValue>> RecordGoingConcernAssessmentAsync(
    IClientAccountingDbContext db, ActorContext actor, GoingConcernAssessmentRequest request,
    CancellationToken ct = default)
  {
    var currency = request.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
    if (request.EngagementId == Guid.Empty || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') ||
        string.IsNullOrWhiteSpace(request.ForecastReviewOutcome) ||
        request.ForecastReviewOutcome.Trim().Length > 4000 ||
        string.IsNullOrWhiteSpace(request.Rationale) || request.Rationale.Trim().Length > 4000 ||
        request.PeriodCoveredTo < request.AssessmentDate ||
        (request.EvidenceReferences ?? []).All(x => string.IsNullOrWhiteSpace(x)))
      return CommandResult<GoingConcernAssessmentValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A going-concern assessment needs the engagement, period coverage, forecast outcome, rationale and evidence.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, EngagementId: request.EngagementId, RequiredRoles: PlanningRoles,
        InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded)
      return CommandResult<GoingConcernAssessmentValue>.Fail(auth.ErrorCode!, auth.Message!);
    var engagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == request.EngagementId, ct);

    var conclusion = !request.MaterialUncertaintyIdentified
      ? GoingConcernConclusions.NoMaterialUncertainty
      : request.DisclosureAdequate
        ? GoingConcernConclusions.MaterialUncertaintyDisclosed
        : GoingConcernConclusions.InadequateDisclosure;

    var existing = await db.GoingConcernAssessments.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId &&
      x.AssessmentDate == request.AssessmentDate, ct);
    if (existing is not null && existing.RecordedByUserId != actor.UserId)
      return CommandResult<GoingConcernAssessmentValue>.Fail(ErrorCodes.ScopeDenied,
        "Only the recording auditor can revise this going-concern assessment.");
    if (existing is null)
    {
      existing = new GoingConcernAssessment
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = engagement.PracticeClientId,
        EngagementId = request.EngagementId, RecordedByUserId = actor.UserId, RecordedAt = DateTimeOffset.UtcNow
      };
      db.GoingConcernAssessments.Add(existing);
    }
    else
    {
      existing.Revision++;
      existing.RecordedAt = DateTimeOffset.UtcNow;
      existing.ReviewedByUserId = null;
      existing.ReviewedAt = null;
    }
    existing.ProcedureId = request.ProcedureId;
    existing.AssessmentDate = request.AssessmentDate;
    existing.PeriodCoveredTo = request.PeriodCoveredTo;
    existing.ForecastReviewOutcome = request.ForecastReviewOutcome.Trim();
    existing.MaterialUncertaintyIdentified = request.MaterialUncertaintyIdentified;
    existing.DisclosureAdequate = request.DisclosureAdequate;
    existing.Conclusion = conclusion;
    existing.Rationale = request.Rationale.Trim();
    existing.Currency = currency;
    existing.EvidenceReferencesJson = System.Text.Json.JsonSerializer.Serialize(
      (request.EvidenceReferences ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray());
    await db.SaveChangesAsync(ct);

    return CommandResult<GoingConcernAssessmentValue>.Ok(new GoingConcernAssessmentValue(
      existing.Id, conclusion, existing.MaterialUncertaintyIdentified, existing.Revision));
  }

  /// <summary>Records a subsequent event with the practitioner's adjusting/non-adjusting
  /// classification and disclosure decision. The platform never infers the classification,
  /// and the count of events still pending assessment is reported so finalisation can be
  /// blocked while any event is unassessed.</summary>
  public static async Task<CommandResult<SubsequentEventReviewValue>> RecordSubsequentEventAsync(
    IClientAccountingDbContext db, ActorContext actor, SubsequentEventReviewRequest request,
    CancellationToken ct = default)
  {
    var currency = request.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
    var classification = request.Classification?.Trim().ToUpperInvariant() ?? string.Empty;
    if (request.EngagementId == Guid.Empty || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') ||
        !SubsequentEventClassifications.All.Contains(classification) ||
        string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 4000 ||
        request.EventDate < request.PeriodEndDate ||
        (classification == SubsequentEventClassifications.Adjusting && !request.AdjustmentRequired) ||
        (classification == SubsequentEventClassifications.NonAdjusting && request.AdjustmentRequired))
      return CommandResult<SubsequentEventReviewValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A subsequent event needs the engagement, dates, description and a supported classification consistent with the adjustment decision.");
    if (classification != SubsequentEventClassifications.PendingAssessment &&
        string.IsNullOrWhiteSpace(request.Rationale))
      return CommandResult<SubsequentEventReviewValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A classified subsequent event requires the rationale supporting the assessment.");
    if (request.DisclosureRequired && string.IsNullOrWhiteSpace(request.DisclosureReference))
      return CommandResult<SubsequentEventReviewValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A subsequent event requiring disclosure needs the disclosure reference.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, EngagementId: request.EngagementId, RequiredRoles: PlanningRoles,
        InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded)
      return CommandResult<SubsequentEventReviewValue>.Fail(auth.ErrorCode!, auth.Message!);
    var engagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == request.EngagementId, ct);

    var existing = await db.SubsequentEventReviews.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId &&
      x.EventDate == request.EventDate && x.Description == request.Description.Trim(), ct);
    if (existing is not null && existing.RecordedByUserId != actor.UserId)
      return CommandResult<SubsequentEventReviewValue>.Fail(ErrorCodes.ScopeDenied,
        "Only the recording auditor can revise this subsequent-event review.");
    if (existing is null)
    {
      existing = new SubsequentEventReview
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = engagement.PracticeClientId,
        EngagementId = request.EngagementId, RecordedByUserId = actor.UserId, RecordedAt = DateTimeOffset.UtcNow
      };
      db.SubsequentEventReviews.Add(existing);
    }
    else
    {
      existing.Revision++;
      existing.RecordedAt = DateTimeOffset.UtcNow;
      existing.ReviewedByUserId = null;
      existing.ReviewedAt = null;
    }
    existing.ProcedureId = request.ProcedureId;
    existing.PeriodEndDate = request.PeriodEndDate;
    existing.EventDate = request.EventDate;
    existing.Description = request.Description.Trim();
    existing.Classification = classification;
    existing.AdjustmentRequired = request.AdjustmentRequired;
    existing.DisclosureRequired = request.DisclosureRequired;
    existing.DisclosureReference = string.IsNullOrWhiteSpace(request.DisclosureReference) ? null : request.DisclosureReference.Trim();
    existing.Rationale = string.IsNullOrWhiteSpace(request.Rationale) ? null : request.Rationale.Trim();
    existing.Currency = currency;
    existing.FinancialEffect = request.FinancialEffect;
    existing.EvidenceReferencesJson = System.Text.Json.JsonSerializer.Serialize(
      (request.EvidenceReferences ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray());
    await db.SaveChangesAsync(ct);

    var pending = await db.SubsequentEventReviews.AsNoTracking()
      .CountAsync(x => x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId &&
        x.Classification == SubsequentEventClassifications.PendingAssessment, ct);
    return CommandResult<SubsequentEventReviewValue>.Ok(new SubsequentEventReviewValue(
      existing.Id, existing.Classification, existing.AdjustmentRequired, existing.DisclosureRequired,
      pending, existing.Revision));
  }

  /// <summary>Independent review of a completed-areas record; the preparer cannot review
  /// their own work and an unresolved state is refused.</summary>
  public static async Task<CommandResult> ReviewCompletionRecordAsync(
    IClientAccountingDbContext db, ActorContext actor, string recordKind, Guid recordId,
    CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ReviewRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return auth;
    switch (recordKind?.Trim().ToUpperInvariant())
    {
      case "VARIANCE":
      {
        var record = await db.AnalyticalReviewVarianceInvestigations.SingleOrDefaultAsync(x =>
          x.Id == recordId && x.FirmId == actor.FirmId, ct);
        if (record is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        if (record.RecordedByUserId == actor.UserId)
          return CommandResult.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review their own variance investigation.");
        if (record.Conclusion == VarianceInvestigationConclusions.Unexplained)
          return CommandResult.Fail(ErrorCodes.GateBlocked,
            "An unexplained variance above the investigation threshold must be resolved before review.");
        record.ReviewedByUserId = actor.UserId;
        record.ReviewedAt = DateTimeOffset.UtcNow;
        break;
      }
      case "GOING_CONCERN":
      {
        var record = await db.GoingConcernAssessments.SingleOrDefaultAsync(x =>
          x.Id == recordId && x.FirmId == actor.FirmId, ct);
        if (record is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        if (record.RecordedByUserId == actor.UserId)
          return CommandResult.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review their own going-concern assessment.");
        if (record.Conclusion is GoingConcernConclusions.NotAssessed or GoingConcernConclusions.InadequateDisclosure)
          return CommandResult.Fail(ErrorCodes.GateBlocked,
            "The going-concern conclusion must be assessed and adequately disclosed before review.");
        record.ReviewedByUserId = actor.UserId;
        record.ReviewedAt = DateTimeOffset.UtcNow;
        break;
      }
      case "SUBSEQUENT_EVENT":
      {
        var record = await db.SubsequentEventReviews.SingleOrDefaultAsync(x =>
          x.Id == recordId && x.FirmId == actor.FirmId, ct);
        if (record is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        if (record.RecordedByUserId == actor.UserId)
          return CommandResult.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review their own subsequent-event assessment.");
        if (record.Classification == SubsequentEventClassifications.PendingAssessment)
          return CommandResult.Fail(ErrorCodes.GateBlocked,
            "A subsequent event must be classified before it can be reviewed.");
        record.ReviewedByUserId = actor.UserId;
        record.ReviewedAt = DateTimeOffset.UtcNow;
        break;
      }
      default:
        return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "An unsupported completion record kind was requested.");
    }
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }
}
