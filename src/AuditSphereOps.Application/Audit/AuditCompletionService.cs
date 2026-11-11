using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AuditSphereOps.Application.Audit;

// T073-T075: analytical-review variance investigation, going-concern assessment and
// subsequent-event review. Each derives its conclusion from the recorded evidence so a
// professional conclusion cannot be asserted without the facts that support it.
public sealed record VarianceInvestigationRequest(
  Guid EngagementId, Guid? ProcedureId, string AccountArea, string PeriodReference,
  decimal ExpectedAmount, decimal ActualAmount, decimal InvestigationThreshold,
  string Currency, string? Explanation, IReadOnlyList<string> EvidenceReferences,
  long? ExpectedRevision = null);

public sealed record VarianceInvestigationValue(
  Guid InvestigationId, decimal DifferenceAmount, decimal? DifferencePercent,
  bool ExceedsThreshold, string Conclusion, long Revision);

public sealed record GoingConcernAssessmentRequest(
  Guid EngagementId, Guid? ProcedureId, DateOnly AssessmentDate, DateOnly PeriodCoveredTo,
  string ForecastReviewOutcome, bool MaterialUncertaintyIdentified, bool DisclosureAdequate,
  string Currency, string Rationale, IReadOnlyList<string> EvidenceReferences, long ExpectedRevision = 0);

public sealed record GoingConcernAssessmentValue(
  Guid AssessmentId, string Conclusion, bool MaterialUncertaintyIdentified, long Revision);

public sealed record SubsequentEventReviewRequest(
  Guid EngagementId, Guid? ProcedureId, DateOnly PeriodEndDate, DateOnly EventDate,
  string Description, string Classification, bool AdjustmentRequired, bool DisclosureRequired,
  string? DisclosureReference, string? Rationale, string Currency, decimal? FinancialEffect,
  IReadOnlyList<string> EvidenceReferences, long ExpectedRevision = 0, Guid? SelectedRecordId = null);

public sealed record SubsequentEventReviewValue(
  Guid ReviewId, string Classification, bool AdjustmentRequired, bool DisclosureRequired,
  int PendingEventCount, long Revision);

public static class AuditCompletionService
{
  private static readonly string[] PlanningRoles =
    ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static readonly string[] ReviewRoles = ["Reviewer", "Manager", "Partner", "Administrator"];

  private sealed record CompletionRecordScope(Guid ClientId, Guid EngagementId);

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
    if (existing is not null &&
        (request.ExpectedRevision is null || request.ExpectedRevision != existing.Revision))
      return CommandResult<VarianceInvestigationValue>.Fail(ErrorCodes.GenerationStale,
        "The variance investigation changed; reload its current revision before revising it.");
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
      existing.ReviewedRevision = null;
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
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateConcurrencyException)
    {
      return CommandResult<VarianceInvestigationValue>.Fail(ErrorCodes.GenerationStale,
        "The variance investigation changed; reload its current revision before revising it.");
    }

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
    if (request.ExpectedRevision != (existing?.Revision ?? 0))
      return CommandResult<GoingConcernAssessmentValue>.Fail(ErrorCodes.GenerationStale,
        "The going-concern assessment changed; reload its exact revision before preparing.");
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
      existing.ReviewedRevision = null;
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
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateConcurrencyException)
    {
      return CommandResult<GoingConcernAssessmentValue>.Fail(ErrorCodes.GenerationStale,
        "The going-concern assessment changed; reload its current revision before revising it.");
    }

    return CommandResult<GoingConcernAssessmentValue>.Ok(new GoingConcernAssessmentValue(
      existing.Id, conclusion, existing.MaterialUncertaintyIdentified, existing.Revision));
  }

  /// <summary>Records a subsequent event with the practitioner's adjusting/non-adjusting
  /// classification and disclosure decision. The platform never infers the classification,
  /// and the count of events still pending assessment is reported so finalisation can be
  /// blocked while any event is unassessed.</summary>
  internal static CommandResult ValidateSubsequentEvent(SubsequentEventReviewRequest request)
  {
    var currency = request.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
    var classification = request.Classification?.Trim().ToUpperInvariant() ?? string.Empty;
    if (request.EngagementId == Guid.Empty || request.ExpectedRevision < 0 ||
        (request.FinancialEffect is { } effect && (effect < -9999999999999.999999m || effect > 9999999999999.999999m || decimal.Round(effect, 6) != effect)) || request.PeriodEndDate == default || request.EventDate == default ||
        (request.Rationale?.Trim().Length ?? 0) > 4000 || (request.DisclosureReference?.Trim().Length ?? 0) > 200 ||
        JsonSerializer.Serialize((request.EvidenceReferences ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray()).Length > 20000 || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') ||
        !SubsequentEventClassifications.All.Contains(classification) ||
        string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 4000 ||
        request.EventDate < request.PeriodEndDate ||
        (classification == SubsequentEventClassifications.Adjusting && !request.AdjustmentRequired) ||
        (classification == SubsequentEventClassifications.NonAdjusting && request.AdjustmentRequired))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A subsequent event needs the engagement, dates, description and a supported classification consistent with the adjustment decision.");
    if (classification != SubsequentEventClassifications.PendingAssessment &&
        string.IsNullOrWhiteSpace(request.Rationale))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A classified subsequent event requires the rationale supporting the assessment.");
    if (request.DisclosureRequired && string.IsNullOrWhiteSpace(request.DisclosureReference))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A subsequent event requiring disclosure needs the disclosure reference.");
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<SubsequentEventReviewValue>> RecordSubsequentEventAsync(
    IClientAccountingDbContext db, ActorContext actor, SubsequentEventReviewRequest request,
    CancellationToken ct = default)
  {
    var validation = ValidateSubsequentEvent(request);
    if (!validation.Succeeded) return CommandResult<SubsequentEventReviewValue>.Fail(validation.ErrorCode!, validation.Message!);
    var currency = request.Currency.Trim().ToUpperInvariant();
    var classification = request.Classification.Trim().ToUpperInvariant();
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == request.EngagementId, ct);
    if (engagement is null) return CommandResult<SubsequentEventReviewValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    async Task<CommandResult> AuthorizeAsync() => await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, engagement.PracticeClientId, request.EngagementId, PlanningRoles,
        InternalOnly: true, RequireProfessionalWork: true), ct);
    var auth = await AuthorizeAsync();
    if (!auth.Succeeded) return CommandResult<SubsequentEventReviewValue>.Fail(auth.ErrorCode!, auth.Message!);
    await using var ownedTx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    var safety = await AuditPlanningCommandWorkspace.LockAsync(db, actor, request.EngagementId, true, ct);
    if (!safety.Succeeded) return CommandResult<SubsequentEventReviewValue>.Fail(safety.ErrorCode!, safety.Message!);
    var locked = await db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE firm_id={actor.FirmId} AND id={request.EngagementId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (locked is null || locked.PracticeClientId != engagement.PracticeClientId)
      return CommandResult<SubsequentEventReviewValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    auth = await AuthorizeAsync();
    if (!auth.Succeeded) return CommandResult<SubsequentEventReviewValue>.Fail(auth.ErrorCode!, auth.Message!);
    var evidence = await AuditCompletionSubsequentEventWorkspace.CurrentEvidenceIdentityAsync(db, actor,
      engagement.PracticeClientId, request.EngagementId, new(request.ProcedureId, request.PeriodEndDate, request.EventDate,
        request.Description, request.Classification, request.AdjustmentRequired, request.DisclosureRequired,
        request.DisclosureReference, request.Rationale, request.Currency,
        request.FinancialEffect?.ToString(System.Globalization.CultureInfo.InvariantCulture), request.EvidenceReferences ?? []), ct);
    if (!evidence.Succeeded) return CommandResult<SubsequentEventReviewValue>.Fail(evidence.ErrorCode!, evidence.Message!);
    var existing = request.SelectedRecordId is { } selectedId
      ? await db.SubsequentEventReviews.SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
          x.ClientId == engagement.PracticeClientId && x.EngagementId == request.EngagementId && x.Id == selectedId, ct)
      : null;
    if (request.SelectedRecordId is not null && existing is null)
      return CommandResult<SubsequentEventReviewValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (existing is not null && (existing.EventDate != request.EventDate || existing.Description != request.Description.Trim()))
      return CommandResult<SubsequentEventReviewValue>.Fail(ErrorCodes.GenerationStale, "The selected event identity cannot change; explicitly create another event instead.");
    if (request.SelectedRecordId is null && await db.SubsequentEventReviews.AnyAsync(x => x.FirmId == actor.FirmId &&
        x.ClientId == engagement.PracticeClientId && x.EngagementId == request.EngagementId &&
        x.EventDate == request.EventDate && x.Description == request.Description.Trim(), ct))
      return CommandResult<SubsequentEventReviewValue>.Fail(ErrorCodes.GenerationStale, "This event already exists; select its exact record and revision.");
    if (request.ExpectedRevision != (existing?.Revision ?? 0))
      return CommandResult<SubsequentEventReviewValue>.Fail(ErrorCodes.GenerationStale, "The subsequent event changed; refresh its exact revision.");
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
      existing.ReviewedRevision = null;
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
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateConcurrencyException)
    {
      return CommandResult<SubsequentEventReviewValue>.Fail(ErrorCodes.GenerationStale,
        "The subsequent-event assessment changed; reload its current revision before revising it.");
    }

    auth = await AuthorizeAsync();
    if (!auth.Succeeded)
    {
      if (ownedTx is not null) await ownedTx.RollbackAsync(ct);
      return CommandResult<SubsequentEventReviewValue>.Fail(auth.ErrorCode!, auth.Message!);
    }
    if (ownedTx is not null) await ownedTx.CommitAsync(ct);
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
    long expectedRevision, CancellationToken ct = default, VarianceReviewReceiptContext? receiptContext = null,
    GoingConcernReviewReceiptContext? goingConcernReceiptContext = null,
    SubsequentEventReviewReceiptContext? subsequentEventReceiptContext = null)
  {
    if (recordId == Guid.Empty || expectedRevision < 1)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "A current completion record and revision are required.");

    var kind = recordKind?.Trim().ToUpperInvariant();
    var scope = kind switch
    {
      "VARIANCE" => await db.AnalyticalReviewVarianceInvestigations.AsNoTracking()
        .Where(x => x.Id == recordId && x.FirmId == actor.FirmId)
        .Select(x => new CompletionRecordScope(x.ClientId, x.EngagementId)).SingleOrDefaultAsync(ct),
      "GOING_CONCERN" => await db.GoingConcernAssessments.AsNoTracking()
        .Where(x => x.Id == recordId && x.FirmId == actor.FirmId)
        .Select(x => new CompletionRecordScope(x.ClientId, x.EngagementId)).SingleOrDefaultAsync(ct),
      "SUBSEQUENT_EVENT" => await db.SubsequentEventReviews.AsNoTracking()
        .Where(x => x.Id == recordId && x.FirmId == actor.FirmId)
        .Select(x => new CompletionRecordScope(x.ClientId, x.EngagementId)).SingleOrDefaultAsync(ct),
      _ => null
    };
    if (scope is null)
      return CommandResult.Fail(kind is "VARIANCE" or "GOING_CONCERN" or "SUBSEQUENT_EVENT"
        ? ErrorCodes.ScopeDenied : ErrorCodes.Accounting.MappingInvalid,
        kind is "VARIANCE" or "GOING_CONCERN" or "SUBSEQUENT_EVENT"
          ? "Access denied." : "An unsupported completion record kind was requested.");

    async Task<CommandResult> AuthorizeExactScopeAsync() => await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, scope.ClientId, scope.EngagementId, ReviewRoles,
        InternalOnly: true, RequireProfessionalWork: true), ct);

    var auth = await AuthorizeExactScopeAsync();
    if (!auth.Succeeded) return auth;

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var safety = await AuditPlanningCommandWorkspace.LockAsync(db, actor, scope.EngagementId, publishing: true, ct);
    if (!safety.Succeeded)
    {
      await tx.CommitAsync(ct);
      return safety;
    }

    var engagement = await db.Engagements.FromSqlInterpolated($"""
      SELECT * FROM engagements WHERE firm_id = {actor.FirmId} AND id = {scope.EngagementId} FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    if (engagement is null || engagement.PracticeClientId != scope.ClientId)
    {
      await tx.CommitAsync(ct);
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    }

    // Re-read grants, session and professional-work state after all lock waits and again
    // after the exact record lock, immediately before changing the reviewed basis.
    auth = await AuthorizeExactScopeAsync();
    if (!auth.Succeeded)
    {
      await tx.CommitAsync(ct); // retain any frozen-access refusal evidence written by authorization
      return auth;
    }

    if (receiptContext is not null)
    {
      if (kind != "VARIANCE" || receiptContext.RequestId == Guid.Empty || receiptContext.RequestHash.Length != 64 ||
          receiptContext.ReviewBasis.Length != 64 ||
          receiptContext.EvidenceIdentityHash is { Length: not 64 })
        return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "The variance review recovery identity is invalid.");
      await AuditCompletionVarianceWorkspace.LockRequestAsync(db, actor, receiptContext.RequestId, true, ct);
      // A request-barrier wait is another authorization boundary for replay as well as a new review.
      auth = await AuthorizeExactScopeAsync();
      if (!auth.Succeeded)
      {
        await tx.CommitAsync(ct);
        return auth;
      }
      var prior = await db.AuditCompletionVarianceCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == receiptContext.RequestId, ct);
      if (prior is not null)
      {
        if (prior.ActorEpoch != actor.SessionEpoch)
          return CommandResult.Fail(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
        if (prior.ClientId != scope.ClientId || prior.EngagementId != scope.EngagementId ||
            prior.VarianceInvestigationId != recordId || prior.Action != "REVIEW" ||
            prior.RequestHash != receiptContext.RequestHash || prior.ReviewBasis != receiptContext.ReviewBasis ||
            prior.InputJson != receiptContext.InputJson || prior.PreviewJson != receiptContext.PreviewJson)
          return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
        await tx.CommitAsync(ct);
        return CommandResult.Ok();
      }
      // A context without an evidence identity is valid only for the exact retained replay above.
      if (receiptContext.EvidenceIdentityHash is null)
        return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "The retained review outcome changed; refresh before any new review.");
    }

    if (goingConcernReceiptContext is not null)
    {
      if (kind != "GOING_CONCERN" || goingConcernReceiptContext.RequestId == Guid.Empty || goingConcernReceiptContext.RequestHash.Length != 64 ||
          goingConcernReceiptContext.ReviewBasis.Length != 64 ||
          goingConcernReceiptContext.EvidenceIdentityHash is { Length: not 64 })
        return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "The going-concern review recovery identity is invalid.");
      await AuditCompletionGoingConcernWorkspace.LockRequestAsync(db, actor, goingConcernReceiptContext.RequestId, true, ct);
      // A request-barrier wait is another authorization boundary for replay as well as a new review.
      auth = await AuthorizeExactScopeAsync();
      if (!auth.Succeeded)
      {
        await tx.CommitAsync(ct);
        return auth;
      }
      var prior = await db.AuditCompletionGoingConcernCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == goingConcernReceiptContext.RequestId, ct);
      if (prior is not null)
      {
        if (prior.ActorEpoch != actor.SessionEpoch)
          return CommandResult.Fail(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
        if (prior.ClientId != scope.ClientId || prior.EngagementId != scope.EngagementId ||
            prior.GoingConcernAssessmentId != recordId || prior.Action != "REVIEW" ||
            prior.RequestHash != goingConcernReceiptContext.RequestHash || prior.ReviewBasis != goingConcernReceiptContext.ReviewBasis ||
            prior.InputJson != goingConcernReceiptContext.InputJson || prior.PreviewJson != goingConcernReceiptContext.PreviewJson)
          return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
        await tx.CommitAsync(ct);
        return CommandResult.Ok();
      }
      // A context without an evidence identity is valid only for the exact retained replay above.
      if (goingConcernReceiptContext.EvidenceIdentityHash is null)
        return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "The retained review outcome changed; refresh before any new review.");
    }

    if (subsequentEventReceiptContext is not null)
    {
      if (kind != "SUBSEQUENT_EVENT" || subsequentEventReceiptContext.RequestId == Guid.Empty || subsequentEventReceiptContext.RequestHash.Length != 64 ||
          subsequentEventReceiptContext.ReviewBasis.Length != 64 ||
          subsequentEventReceiptContext.EvidenceIdentityHash is { Length: not 64 })
        return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "The subsequent-event review recovery identity is invalid.");
      await AuditCompletionSubsequentEventWorkspace.LockRequestAsync(db, actor, subsequentEventReceiptContext.RequestId, true, ct);
      // A request-barrier wait is another authorization boundary for replay as well as a new review.
      auth = await AuthorizeExactScopeAsync();
      if (!auth.Succeeded)
      {
        await tx.CommitAsync(ct);
        return auth;
      }
      var prior = await db.AuditCompletionSubsequentEventCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == subsequentEventReceiptContext.RequestId, ct);
      if (prior is not null)
      {
        if (prior.ActorEpoch != actor.SessionEpoch)
          return CommandResult.Fail(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
        if (prior.ClientId != scope.ClientId || prior.EngagementId != scope.EngagementId ||
            prior.SubsequentEventReviewId != recordId || prior.Action != "REVIEW" ||
            prior.RequestHash != subsequentEventReceiptContext.RequestHash || prior.ReviewBasis != subsequentEventReceiptContext.ReviewBasis ||
            prior.InputJson != subsequentEventReceiptContext.InputJson || prior.PreviewJson != subsequentEventReceiptContext.PreviewJson)
          return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
        await tx.CommitAsync(ct);
        return CommandResult.Ok();
      }
      // A context without an evidence identity is valid only for the exact retained replay above.
      if (subsequentEventReceiptContext.EvidenceIdentityHash is null)
        return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "The retained review outcome changed; refresh before any new review.");
    }

    CommandResult? refusal = null;
    AnalyticalReviewVarianceInvestigation? reviewedVariance = null;
    GoingConcernAssessment? reviewedGoingConcern = null;
    SubsequentEventReview? reviewedSubsequentEvent = null;
    switch (kind)
    {
      case "VARIANCE":
      {
        var record = await db.AnalyticalReviewVarianceInvestigations.FromSqlInterpolated($"""
          SELECT * FROM analytical_variance_investigations
          WHERE firm_id = {actor.FirmId} AND id = {recordId} FOR UPDATE
          """).SingleOrDefaultAsync(ct);
        if (record is null || record.ClientId != scope.ClientId || record.EngagementId != scope.EngagementId)
          refusal = CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        else if (record.Revision != expectedRevision)
          refusal = CommandResult.Fail(ErrorCodes.GenerationStale, "The variance investigation changed; reload and review its current revision.");
        else if (receiptContext is not null)
        {
          var current = await AuditCompletionVarianceWorkspace.StateAsync(db, actor, scope.EngagementId,
            record.AccountArea, record.PeriodReference, ct);
          if (!current.Succeeded)
            refusal = CommandResult.Fail(current.ErrorCode!, current.Message!);
          else if (current.Value!.ReviewBasis != receiptContext.ReviewBasis ||
                   current.Value.ExpectedRevision != expectedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                   current.Value.Record?.Id != recordId)
            refusal = CommandResult.Fail(ErrorCodes.GenerationStale, "The exact variance basis changed; refresh before review.");
          else if (AuditCompletionVarianceWorkspace.EvidenceReferences(record).Length == 0)
            refusal = CommandResult.Fail(ErrorCodes.GateBlocked, "At least one current exact-engagement evidence reference is required before review.");
          else
          {
            var currentEvidence = await AuditCompletionVarianceWorkspace.CurrentEvidenceIdentityAsync(db, actor,
              scope.ClientId, scope.EngagementId, new(record.ProcedureId, record.AccountArea, record.PeriodReference,
                record.ExpectedAmount, record.ActualAmount, record.InvestigationThreshold, record.Currency,
                record.Explanation, AuditCompletionVarianceWorkspace.EvidenceReferences(record)), ct);
            if (!currentEvidence.Succeeded) refusal = CommandResult.Fail(currentEvidence.ErrorCode!, currentEvidence.Message!);
            else if (currentEvidence.Value != receiptContext.EvidenceIdentityHash)
              refusal = CommandResult.Fail(ErrorCodes.GenerationStale,
                "The independently reviewed procedure evidence changed while waiting to review; preview the current evidence again.");
            else if (!current.Value.CanReview)
              refusal = CommandResult.Fail(ErrorCodes.GateBlocked,
                current.Value.Blockers.FirstOrDefault() ?? "Current independent review authority is required.");
          }
        }
        if (record is not null && refusal is null && record.RecordedByUserId == actor.UserId)
          refusal = CommandResult.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review their own variance investigation.");
        else if (record is not null && refusal is null && record.Conclusion == VarianceInvestigationConclusions.Unexplained)
          refusal = CommandResult.Fail(ErrorCodes.GateBlocked,
            "An unexplained variance above the investigation threshold must be resolved before review.");
        else if (record is not null && refusal is null)
        {
          auth = await AuthorizeExactScopeAsync();
          if (!auth.Succeeded) refusal = auth;
          else
          {
            record.ReviewedByUserId = actor.UserId;
            record.ReviewedAt = DateTimeOffset.UtcNow;
            record.ReviewedRevision = expectedRevision;
            record.Revision++;
            reviewedVariance = record;
          }
        }
        break;
      }
      case "GOING_CONCERN":
      {
        var record = await db.GoingConcernAssessments.FromSqlInterpolated($"""
          SELECT * FROM going_concern_assessments
          WHERE firm_id = {actor.FirmId} AND id = {recordId} FOR UPDATE
          """).SingleOrDefaultAsync(ct);
        if (record is null || record.ClientId != scope.ClientId || record.EngagementId != scope.EngagementId)
          refusal = CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        else if (record.Revision != expectedRevision)
          refusal = CommandResult.Fail(ErrorCodes.GenerationStale, "The going-concern assessment changed; reload and review its current revision.");
        else if (goingConcernReceiptContext is not null)
        {
          var current = await AuditCompletionGoingConcernWorkspace.StateAsync(db, actor, scope.EngagementId,
            record.AssessmentDate, ct);
          if (!current.Succeeded)
            refusal = CommandResult.Fail(current.ErrorCode!, current.Message!);
          else if (current.Value!.ReviewBasis != goingConcernReceiptContext.ReviewBasis ||
                   current.Value.ExpectedRevision != expectedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                   current.Value.Record?.Id != recordId)
            refusal = CommandResult.Fail(ErrorCodes.GenerationStale, "The exact going-concern basis changed; refresh before review.");
          else if (AuditCompletionGoingConcernWorkspace.EvidenceReferences(record).Length == 0)
            refusal = CommandResult.Fail(ErrorCodes.GateBlocked, "At least one current exact-engagement evidence reference is required before review.");
          else
          {
            var currentEvidence = await AuditCompletionGoingConcernWorkspace.CurrentEvidenceIdentityAsync(db, actor,
              scope.ClientId, scope.EngagementId, AuditCompletionGoingConcernWorkspace.Fields(record), ct);
            if (!currentEvidence.Succeeded) refusal = CommandResult.Fail(currentEvidence.ErrorCode!, currentEvidence.Message!);
            else if (currentEvidence.Value != goingConcernReceiptContext.EvidenceIdentityHash)
              refusal = CommandResult.Fail(ErrorCodes.GenerationStale,
                "The independently reviewed procedure evidence changed while waiting to review; preview the current evidence again.");
            else if (!current.Value.CanReview)
              refusal = CommandResult.Fail(ErrorCodes.GateBlocked,
                current.Value.Blockers.FirstOrDefault() ?? "Current independent review authority is required.");
          }
        }
        if (record is not null && refusal is null && record.RecordedByUserId == actor.UserId)
          refusal = CommandResult.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review their own going-concern assessment.");
        else if (record is not null && refusal is null && record.Conclusion is GoingConcernConclusions.NotAssessed or GoingConcernConclusions.InadequateDisclosure)
          refusal = CommandResult.Fail(ErrorCodes.GateBlocked,
            "The going-concern conclusion must be assessed and adequately disclosed before review.");
        else if (record is not null && refusal is null)
        {
          auth = await AuthorizeExactScopeAsync();
          if (!auth.Succeeded) refusal = auth;
          else
          {
            record.ReviewedByUserId = actor.UserId;
            record.ReviewedAt = DateTimeOffset.UtcNow;
            record.ReviewedRevision = expectedRevision;
            record.Revision++;
            reviewedGoingConcern = record;
          }
        }
        break;
      }
      case "SUBSEQUENT_EVENT":
      {
        var record = await db.SubsequentEventReviews.FromSqlInterpolated($"""
          SELECT * FROM subsequent_event_reviews
          WHERE firm_id = {actor.FirmId} AND id = {recordId} FOR UPDATE
          """).SingleOrDefaultAsync(ct);
        if (record is null || record.ClientId != scope.ClientId || record.EngagementId != scope.EngagementId)
          refusal = CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        else if (record.Revision != expectedRevision)
          refusal = CommandResult.Fail(ErrorCodes.GenerationStale, "The subsequent-event assessment changed; reload and review its current revision.");
        else if (subsequentEventReceiptContext is not null)
        {
          var current = await AuditCompletionSubsequentEventWorkspace.StateAsync(db, actor, scope.EngagementId, recordId, ct);
          if (!current.Succeeded) refusal = CommandResult.Fail(current.ErrorCode!, current.Message!);
          else if (current.Value!.ReviewBasis != subsequentEventReceiptContext.ReviewBasis ||
                   current.Value.ExpectedRevision != expectedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                   current.Value.Record?.Id != recordId)
            refusal = CommandResult.Fail(ErrorCodes.GenerationStale, "The exact event basis changed; refresh before review.");
          else
          {
            var evidence = await AuditCompletionSubsequentEventWorkspace.CurrentEvidenceIdentityAsync(db, actor,
              scope.ClientId, scope.EngagementId, AuditCompletionSubsequentEventWorkspace.Fields(record), ct);
            if (!evidence.Succeeded) refusal = CommandResult.Fail(evidence.ErrorCode!, evidence.Message!);
            else if (evidence.Value != subsequentEventReceiptContext.EvidenceIdentityHash)
              refusal = CommandResult.Fail(ErrorCodes.GenerationStale, "The exact linked evidence changed; preview it again.");
            else if (!current.Value.CanReview)
              refusal = CommandResult.Fail(ErrorCodes.GateBlocked, current.Value.Blockers.FirstOrDefault() ?? "Independent current review authority is required.");
          }
        }
        if (record is not null && refusal is null && record.RecordedByUserId == actor.UserId)
          refusal = CommandResult.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review their own subsequent-event assessment.");
        else if (record is not null && refusal is null && record.Classification == SubsequentEventClassifications.PendingAssessment)
          refusal = CommandResult.Fail(ErrorCodes.GateBlocked,
            "A subsequent event must be classified before it can be reviewed.");
        else if (record is not null && refusal is null)
        {
          auth = await AuthorizeExactScopeAsync();
          if (!auth.Succeeded) refusal = auth;
          else
          {
            record.ReviewedByUserId = actor.UserId;
            record.ReviewedAt = DateTimeOffset.UtcNow;
            record.ReviewedRevision = expectedRevision;
            record.Revision++;
            reviewedSubsequentEvent = record;
          }
        }
        break;
      }
    }

    if (refusal is not null)
    {
      await tx.CommitAsync(ct);
      return refusal;
    }
    try
    {
      await db.SaveChangesAsync(ct);
      if (receiptContext is not null)
      {
        if (reviewedVariance is null)
        {
          await tx.RollbackAsync(ct);
          return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "A variance review receipt requires the reviewed variance record.");
        }
        var persistedVariance = await db.AnalyticalReviewVarianceInvestigations.AsNoTracking().SingleAsync(x =>
          x.FirmId == actor.FirmId && x.ClientId == scope.ClientId && x.EngagementId == scope.EngagementId &&
          x.Id == recordId, ct);
        db.AuditCompletionVarianceCommandReceipts.Add(new AuditCompletionVarianceCommandReceipt
        {
          Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = scope.ClientId,
          EngagementId = scope.EngagementId, VarianceInvestigationId = recordId,
          ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch, RequestId = receiptContext.RequestId,
          RequestHash = receiptContext.RequestHash, ReviewBasis = receiptContext.ReviewBasis,
          Action = "REVIEW", InputJson = receiptContext.InputJson, PreviewJson = receiptContext.PreviewJson,
          ResultJson = JsonSerializer.Serialize(AuditCompletionVarianceWorkspace.Snapshot(persistedVariance),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)), CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
      }
      if (goingConcernReceiptContext is not null)
      {
        if (reviewedGoingConcern is null)
        {
          await tx.RollbackAsync(ct);
          return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "A going-concern review receipt requires the reviewed going-concern record.");
        }
        var persistedGoingConcern = await db.GoingConcernAssessments.AsNoTracking().SingleAsync(x =>
          x.FirmId == actor.FirmId && x.ClientId == scope.ClientId && x.EngagementId == scope.EngagementId &&
          x.Id == recordId, ct);
        db.AuditCompletionGoingConcernCommandReceipts.Add(new AuditCompletionGoingConcernCommandReceipt
        {
          Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = scope.ClientId,
          EngagementId = scope.EngagementId, GoingConcernAssessmentId = recordId,
          ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch, RequestId = goingConcernReceiptContext.RequestId,
          RequestHash = goingConcernReceiptContext.RequestHash, ReviewBasis = goingConcernReceiptContext.ReviewBasis,
          Action = "REVIEW", InputJson = goingConcernReceiptContext.InputJson, PreviewJson = goingConcernReceiptContext.PreviewJson,
          ResultJson = JsonSerializer.Serialize(AuditCompletionGoingConcernWorkspace.Snapshot(persistedGoingConcern),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)), CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
      }
      if (subsequentEventReceiptContext is not null)
      {
        if (reviewedSubsequentEvent is null)
        {
          await tx.RollbackAsync(ct);
          return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "A subsequent-event review receipt requires the reviewed subsequent-event record.");
        }
        var persistedSubsequentEvent = await db.SubsequentEventReviews.AsNoTracking().SingleAsync(x =>
          x.FirmId == actor.FirmId && x.ClientId == scope.ClientId && x.EngagementId == scope.EngagementId &&
          x.Id == recordId, ct);
        db.AuditCompletionSubsequentEventCommandReceipts.Add(new AuditCompletionSubsequentEventCommandReceipt
        {
          Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = scope.ClientId,
          EngagementId = scope.EngagementId, SubsequentEventReviewId = recordId,
          ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch, RequestId = subsequentEventReceiptContext.RequestId,
          RequestHash = subsequentEventReceiptContext.RequestHash, ReviewBasis = subsequentEventReceiptContext.ReviewBasis,
          Action = "REVIEW", InputJson = subsequentEventReceiptContext.InputJson, PreviewJson = subsequentEventReceiptContext.PreviewJson,
          ResultJson = JsonSerializer.Serialize(AuditCompletionSubsequentEventWorkspace.Snapshot(persistedSubsequentEvent),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)), CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
      }
    }
    catch (DbUpdateConcurrencyException)
    {
      await tx.RollbackAsync(ct);
      return CommandResult.Fail(ErrorCodes.GenerationStale,
        "The completion record changed; reload and review its current revision.");
    }
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }
}
