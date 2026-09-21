using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record ScheduleRowInput(
  string StableRowId,
  int SourceLineNumber,
  string AccountCode,
  string Description,
  decimal SignedAmount,
  string Currency,
  DateOnly? TransactionDate,
  DateOnly? PostingDate,
  DateOnly? DeliveryDate,
  DateOnly? ServiceDate,
  string OriginalValuesJson);

public sealed record CreateScheduleRequest(
  Guid EngagementId,
  string ScheduleType,
  string EntityIdentifier,
  string SourceReceiptReference,
  DateTimeOffset? AsOfDate,
  DateOnly? PeriodStart,
  DateOnly? PeriodEnd,
  string Currency,
  string SignConvention,
  string SourceHash,
  decimal GlControlTotal,
  IReadOnlyList<ScheduleRowInput> Rows);

public sealed record ScheduleValue(Guid ScheduleId, string Status, int RowCount, decimal SignedControlTotal, decimal Residual);
public sealed record ReviewScheduleRequest(Guid ScheduleId, string CompletenessDecision, bool Approve);

public sealed record SelectionItemInput(string StableRowId, decimal SignedAmount, string Currency, string InclusionReason, Guid? ScheduleRowId = null);
public sealed record CreateSelectionRequest(
  Guid EngagementId,
  Guid ProcedureId,
  Guid? ScheduleId,
  Guid? PopulationVersionId,
  string Method,
  string Rationale,
  IReadOnlyList<SelectionItemInput> Items);
public sealed record SelectionValue(Guid SelectionId, int SelectedCount, decimal SelectedSignedTotal, string Status);
public sealed record ReviewSelectionRequest(Guid SelectionId, string Decision, string? Comment);

public sealed record RecordItemTestRequest(
  Guid SelectionItemId,
  string WorkPerformed,
  IReadOnlyList<string> EvidenceReferences,
  string Result,
  decimal? ExceptionAmount,
  string? ContradictoryEvidence,
  string? FollowUp);
public sealed record ItemTestValue(Guid AuditItemTestId, long Revision, string Result);
public sealed record ReviewItemTestRequest(Guid AuditItemTestId, string Decision, string? Comment);

public sealed record CreateConfirmationRequest(
  Guid EngagementId,
  Guid? ProcedureId,
  string AreaCode,
  string SourceRecordId,
  decimal BookedAmount,
  string Currency,
  DateOnly ConfirmationDate,
  string Respondent,
  string ContactValidationSource);
public sealed record ConfirmationValue(Guid ConfirmationCaseId, string Status);
public sealed record RecordConfirmationDispatchRequest(Guid ConfirmationCaseId, string DispatchReference);
public sealed record RecordConfirmationResponseRequest(
  Guid ConfirmationCaseId,
  string Origin,
  string Channel,
  string ResponseReference,
  decimal? ConfirmedAmount,
  string AuthenticityAssessment,
  string Decision);
public sealed record ReviewConfirmationResponseRequest(Guid ConfirmationResponseId);
public sealed record RecordAlternativeProcedureRequest(
  Guid ConfirmationCaseId,
  string Purpose,
  IReadOnlyList<string> EvidenceReferences,
  string Conclusion);
public sealed record ReviewAlternativeProcedureRequest(Guid AlternativeProcedureId, string? Comment);
public sealed record CloseConfirmationRequest(Guid ConfirmationCaseId, string Conclusion);

public sealed record RecordAreaAssessmentRequest(
  Guid EngagementId,
  Guid? ProcedureId,
  string AreaCode,
  string AssessmentKind,
  string MethodologyReference,
  string InputSnapshotJson,
  decimal? BookedAmount,
  decimal? AuditedAmount,
  decimal? ResidualAmount,
  decimal? VariancePercent,
  string? Currency,
  DateOnly? PeriodStart,
  DateOnly? PeriodEnd,
  IReadOnlyList<string> EvidenceReferences,
  string Conclusion);
public sealed record AreaAssessmentValue(Guid AuditAreaAssessmentId, string Status);
public sealed record ReviewAreaAssessmentRequest(Guid AuditAreaAssessmentId, string Decision, string? Comment);

public sealed record RecordDifferenceRequest(
  Guid EngagementId,
  Guid? ProcedureId,
  string AccountArea,
  string DifferenceType,
  string Description,
  decimal Amount,
  string Currency,
  string MaterialityReference = "",
  string QualitativeConcerns = "");
public sealed record DifferenceValue(Guid AuditDifferenceId, string Status);
public sealed record AuditDifferenceSummary(
  string Currency, int DifferenceCount, decimal GrossAmount, decimal SignedNetAmount,
  decimal UnadjustedGrossAmount, decimal UnadjustedSignedNetAmount,
  decimal CorrectedGrossAmount, decimal CorrectedSignedNetAmount);
public sealed record EvaluateDifferenceRequest(Guid AuditDifferenceId, bool Corrected, string Evaluation, string? ManagementResponse, string? CorrectionReference);
public sealed record LinkDifferenceToJournalRequest(
  Guid AuditDifferenceId,
  Guid JournalId,
  long JournalRevision,
  Guid SourceReflectionReconciliationId,
  Guid VerifiedAdjustedSnapshotId,
  string CorrectionState = AuditDifferenceCorrectionStates.Proposed);
public sealed record SetDifferenceCorrectionStateRequest(
  Guid AuditDifferenceId, string CorrectionState, string? Reason = null);

public sealed record AuditCompletionEvaluation(
  bool Ready,
  int ProcedureCount,
  int ApplicableProcedureCount,
  int ReviewedProcedureCount,
  IReadOnlyList<string> Blockers);

/// <summary>
/// Shared fieldwork commands for source control, selections, confirmations, area assessments and
/// differences. Account areas stay distinct through AreaCode/AssessmentKind while using one guarded
/// persistence and review path.
/// </summary>
public static class AuditFieldworkService
{
  private static readonly string[] PlanningRoles = ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static readonly string[] ReviewRoles = ["Reviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<ScheduleValue>> CreateScheduleAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateScheduleRequest request, CancellationToken ct = default)
  {
    if (request.Rows is null || request.Rows.Count == 0 || string.IsNullOrWhiteSpace(request.ScheduleType) ||
        string.IsNullOrWhiteSpace(request.EntityIdentifier) || string.IsNullOrWhiteSpace(request.SourceReceiptReference) ||
        !IsCurrency(request.Currency) || !IsHash(request.SourceHash) || string.IsNullOrWhiteSpace(request.SignConvention))
      return Invalid<ScheduleValue>("A schedule requires a versioned source, currency, sign convention and rows.");
    if (request.Rows.Any(x => x.SourceLineNumber < 1 || string.IsNullOrWhiteSpace(x.StableRowId) ||
        string.IsNullOrWhiteSpace(x.AccountCode) || string.IsNullOrWhiteSpace(x.Description) ||
        !IsCurrency(x.Currency) || !JsonObject(x.OriginalValuesJson) ||
        !string.Equals(x.Currency, request.Currency, StringComparison.OrdinalIgnoreCase)))
      return Invalid<ScheduleValue>("Schedule rows must have stable identities, signed values, one currency and an object snapshot.");
    if (request.Rows.Select(x => x.StableRowId).Distinct(StringComparer.Ordinal).Count() != request.Rows.Count)
      return Invalid<ScheduleValue>("Duplicate stable source-row identities are not accepted.");

    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ScheduleValue>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);

    var sourceHash = request.SourceHash.ToLowerInvariant();
    var existing = await db.AuditSchedules.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId &&
      x.SourceReceiptReference == request.SourceReceiptReference.Trim() && x.SourceHash == sourceHash, ct);
    if (existing is not null)
    {
      await tx.CommitAsync(ct);
      return CommandResult<ScheduleValue>.Ok(ToScheduleValue(existing));
    }

    var now = DateTimeOffset.UtcNow;
    var total = request.Rows.Sum(x => x.SignedAmount);
    var schedule = new AuditSchedule
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId,
      ScheduleType = request.ScheduleType.Trim(), EntityIdentifier = request.EntityIdentifier.Trim(),
      SourceReceiptReference = request.SourceReceiptReference.Trim(), AsOfDate = request.AsOfDate,
      PeriodStart = request.PeriodStart, PeriodEnd = request.PeriodEnd, Currency = request.Currency.ToUpperInvariant(),
      SignConvention = request.SignConvention.Trim(), SourceHash = sourceHash, RowCount = request.Rows.Count,
      SignedControlTotal = total, GlControlTotal = request.GlControlTotal, Residual = total - request.GlControlTotal,
      InputGeneration = await CurrentGenerationAsync(db, auth.ClientId, actor.FirmId, ct),
      Status = total == request.GlControlTotal ? AuditScheduleStatuses.Reconciled : AuditScheduleStatuses.Unreconciled,
      CreatedByUserId = actor.UserId, CreatedAt = now
    };
    db.AuditSchedules.Add(schedule);
    db.AuditScheduleRows.AddRange(request.Rows.Select(row => new AuditScheduleRow
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId,
      ScheduleId = schedule.Id, StableRowId = row.StableRowId.Trim(), SourceLineNumber = row.SourceLineNumber,
      AccountCode = row.AccountCode.Trim(), Description = row.Description.Trim(), SignedAmount = row.SignedAmount,
      Currency = row.Currency.ToUpperInvariant(), TransactionDate = row.TransactionDate, PostingDate = row.PostingDate,
      DeliveryDate = row.DeliveryDate, ServiceDate = row.ServiceDate, OriginalValuesJson = row.OriginalValuesJson.Trim(), CreatedAt = now
    }));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ScheduleValue>.Ok(ToScheduleValue(schedule));
  }

  public static async Task<CommandResult<ScheduleValue>> ReviewScheduleAsync(
    IAuditSphereDbContext db, ActorContext actor, ReviewScheduleRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.CompletenessDecision))
      return Invalid<ScheduleValue>("A completeness decision is required.");
    var scheduleSnapshot = await db.AuditSchedules.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == request.ScheduleId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, scheduleSnapshot, ReviewRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ScheduleValue>.Fail(auth.ErrorCode!, auth.Message!);
    var schedule = await db.AuditSchedules.SingleOrDefaultAsync(x => x.Id == request.ScheduleId && x.FirmId == actor.FirmId, ct);
    if (schedule is null)
      return Denied<ScheduleValue>();
    if (request.Approve && schedule.CreatedByUserId == actor.UserId)
      return CommandResult<ScheduleValue>.Fail(ErrorCodes.ScopeDenied, "The preparer cannot approve the same schedule.");
    if (request.Approve && schedule.Residual != 0)
      return CommandResult<ScheduleValue>.Fail(ErrorCodes.GateBlocked, "An unexplained signed reconciliation residual prevents approval.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    schedule.CompletenessDecision = request.CompletenessDecision.Trim();
    schedule.Status = request.Approve ? AuditScheduleStatuses.Approved :
      (schedule.Residual == 0 ? AuditScheduleStatuses.Reconciled : AuditScheduleStatuses.Unreconciled);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ScheduleValue>.Ok(ToScheduleValue(schedule));
  }

  public static async Task<CommandResult<SelectionValue>> CreateSelectionAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateSelectionRequest request, CancellationToken ct = default)
  {
    if (request.Items is null || request.Items.Count == 0 || request.ScheduleId is null && request.PopulationVersionId is null ||
        string.IsNullOrWhiteSpace(request.Method) || string.IsNullOrWhiteSpace(request.Rationale))
      return Invalid<SelectionValue>("A selection requires an approved source population, method, rationale and items.");
    if (request.Items.Select(x => x.StableRowId).Distinct(StringComparer.Ordinal).Count() != request.Items.Count ||
        request.Items.Any(x => string.IsNullOrWhiteSpace(x.StableRowId) || string.IsNullOrWhiteSpace(x.InclusionReason) || !IsCurrency(x.Currency)))
      return Invalid<SelectionValue>("Selection items must have unique stable IDs, signed currency values and inclusion reasons.");

    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<SelectionValue>.Fail(auth.ErrorCode!, auth.Message!);
    var procedure = await db.AuditProcedures.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.ProcedureId && x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == request.EngagementId, ct);
    if (procedure is null)
      return Denied<SelectionValue>();
    if (procedure.ApplicabilityStatus != AuditApplicabilityStatuses.Applicable)
      return CommandResult<SelectionValue>.Fail(ErrorCodes.GateBlocked, "Selections require an applicable procedure.");
    if (request.ScheduleId is not null)
    {
      var schedule = await db.AuditSchedules.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == request.ScheduleId && x.FirmId == actor.FirmId && x.ClientId == auth.ClientId &&
        x.EngagementId == request.EngagementId, ct);
      if (schedule is null || schedule.Status != AuditScheduleStatuses.Approved)
        return CommandResult<SelectionValue>.Fail(ErrorCodes.GateBlocked, "The source schedule is not approved.");
    }
    else
    {
      var population = await db.PopulationVersions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == request.PopulationVersionId && x.FirmId == actor.FirmId && x.ClientId == auth.ClientId &&
        x.EngagementId == request.EngagementId, ct);
      if (population is null || population.Status != PopulationStatuses.Approved)
        return CommandResult<SelectionValue>.Fail(ErrorCodes.GateBlocked, "The source population is not approved.");
    }
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var generation = await CurrentGenerationAsync(db, auth.ClientId, actor.FirmId, ct);
    if (await db.AuditSelections.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId &&
        x.ProcedureId == request.ProcedureId && x.ScheduleId == request.ScheduleId && x.PopulationVersionId == request.PopulationVersionId &&
        x.InputGeneration == generation, ct))
      return CommandResult<SelectionValue>.Fail(ErrorCodes.IdempotencyConflict, "A selection already exists for this procedure and source generation.");

    var rows = request.ScheduleId is null ? [] : await db.AuditScheduleRows.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == request.EngagementId &&
                  x.ScheduleId == request.ScheduleId && request.Items.Select(i => i.StableRowId).Contains(x.StableRowId))
      .ToListAsync(ct);
    if (request.ScheduleId is not null && rows.Count != request.Items.Count)
      return Invalid<SelectionValue>("Every selected schedule row must exist in the approved source version.");
    var byStable = rows.ToDictionary(x => x.StableRowId, StringComparer.Ordinal);
    var sourceCurrencies = request.Items.Select(x => x.Currency.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();
    if (sourceCurrencies.Length != 1)
      return Invalid<SelectionValue>("A selection cannot sum unrelated currencies.");

    var selection = new AuditSelection
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId,
      ScheduleId = request.ScheduleId, PopulationVersionId = request.PopulationVersionId, ProcedureId = request.ProcedureId,
      Method = request.Method.Trim(), Rationale = request.Rationale.Trim(), SelectedCount = request.Items.Count,
      SelectedSignedTotal = request.Items.Sum(x => byStable.TryGetValue(x.StableRowId, out var row) ? row.SignedAmount : x.SignedAmount),
      Status = AuditSelectionStatuses.Submitted, InputGeneration = generation, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AuditSelections.Add(selection);
    db.AuditSelectionItems.AddRange(request.Items.Select(item => new AuditSelectionItem
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId,
      SelectionId = selection.Id, ScheduleRowId = item.ScheduleRowId ?? (byStable.TryGetValue(item.StableRowId, out var row) ? row.Id : null),
      StableRowId = item.StableRowId.Trim(), SignedAmount = byStable.TryGetValue(item.StableRowId, out var source) ? source.SignedAmount : item.SignedAmount,
      Currency = (byStable.TryGetValue(item.StableRowId, out var sourceCurrency) ? sourceCurrency.Currency : item.Currency).ToUpperInvariant(),
      InclusionReason = item.InclusionReason.Trim(), CreatedAt = selection.CreatedAt
    }));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<SelectionValue>.Ok(new(selection.Id, selection.SelectedCount, selection.SelectedSignedTotal, selection.Status));
  }

  public static async Task<CommandResult<SelectionValue>> ReviewSelectionAsync(
    IAuditSphereDbContext db, ActorContext actor, ReviewSelectionRequest request, CancellationToken ct = default)
  {
    var decision = request.Decision.Trim().ToUpperInvariant();
    if (decision is not (AuditSelectionStatuses.Reviewed or AuditSelectionStatuses.ChangesRequired) ||
        decision == AuditSelectionStatuses.ChangesRequired && string.IsNullOrWhiteSpace(request.Comment))
      return Invalid<SelectionValue>("Selection review requires a valid decision and a comment for changes.");
    var selection = await db.AuditSelections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.SelectionId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, selection, ReviewRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<SelectionValue>.Fail(auth.ErrorCode!, auth.Message!);
    var live = await db.AuditSelections.SingleOrDefaultAsync(x => x.Id == request.SelectionId && x.FirmId == actor.FirmId, ct);
    if (live is null)
      return Denied<SelectionValue>();
    if (live.CreatedByUserId == actor.UserId)
      return CommandResult<SelectionValue>.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review the same selection.");
    if (live.InputGeneration != await CurrentGenerationAsync(db, live.ClientId, live.FirmId, ct))
      return CommandResult<SelectionValue>.Fail(ErrorCodes.GenerationStale, "The selected population changed; reselect the items.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    live.Status = decision;
    live.ReviewedByUserId = actor.UserId;
    live.ReviewedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<SelectionValue>.Ok(new(live.Id, live.SelectedCount, live.SelectedSignedTotal, live.Status));
  }

  public static async Task<CommandResult<ItemTestValue>> RecordItemTestAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordItemTestRequest request, CancellationToken ct = default)
  {
    var result = request.Result.Trim().ToUpperInvariant();
    if (string.IsNullOrWhiteSpace(request.WorkPerformed) || request.EvidenceReferences is null ||
        result is not (AuditItemTestResults.Pending or AuditItemTestResults.Pass or AuditItemTestResults.Exception or AuditItemTestResults.Limitation) ||
        result is AuditItemTestResults.Pass or AuditItemTestResults.Exception && request.EvidenceReferences.Count == 0 ||
        result == AuditItemTestResults.Limitation && string.IsNullOrWhiteSpace(request.FollowUp))
      return Invalid<ItemTestValue>("Item tests require work, a bounded result and evidence or an explicit limitation follow-up.");
    var item = await db.AuditSelectionItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.SelectionItemId && x.FirmId == actor.FirmId, ct);
    if (item is null)
      return Denied<ItemTestValue>();
    var auth = await AuthorizeEntityAsync(db, actor, item, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ItemTestValue>.Fail(auth.ErrorCode!, auth.Message!);
    var selection = await db.AuditSelections.AsNoTracking().SingleAsync(x => x.Id == item.SelectionId && x.FirmId == actor.FirmId, ct);
    if (selection.Status != AuditSelectionStatuses.Reviewed)
      return CommandResult<ItemTestValue>.Fail(ErrorCodes.GateBlocked, "The selection must be reviewed before item testing.");
    if (selection.InputGeneration != await CurrentGenerationAsync(db, selection.ClientId, selection.FirmId, ct))
      return CommandResult<ItemTestValue>.Fail(ErrorCodes.GenerationStale, "The selected population changed; item testing is stale.");
    var generation = await CurrentGenerationAsync(db, selection.ClientId, selection.FirmId, ct);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var revision = (await db.AuditItemTests.AsNoTracking().Where(x => x.SelectionItemId == item.Id).MaxAsync(x => (long?)x.Revision, ct) ?? 0) + 1;
    var test = new AuditItemTest
    {
      Id = Guid.CreateVersion7(), FirmId = item.FirmId, ClientId = item.ClientId, EngagementId = item.EngagementId,
      SelectionId = item.SelectionId, SelectionItemId = item.Id, ProcedureId = selection.ProcedureId, Revision = revision,
      WorkPerformed = request.WorkPerformed.Trim(), EvidenceReferencesJson = JsonSerializer.Serialize(request.EvidenceReferences),
      Result = result, ExceptionAmount = request.ExceptionAmount, ContradictoryEvidence = TrimOrNull(request.ContradictoryEvidence),
      FollowUp = TrimOrNull(request.FollowUp), InputGeneration = generation, TestedByUserId = actor.UserId, TestedAt = DateTimeOffset.UtcNow
    };
    db.AuditItemTests.Add(test);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ItemTestValue>.Ok(new(test.Id, test.Revision, test.Result));
  }

  public static async Task<CommandResult> ReviewItemTestAsync(
    IAuditSphereDbContext db, ActorContext actor, ReviewItemTestRequest request, CancellationToken ct = default)
  {
    var decision = request.Decision.Trim().ToUpperInvariant();
    if (decision is not (AuditItemTestReviewDecisions.Reviewed or AuditItemTestReviewDecisions.ChangesRequired) ||
        decision == AuditItemTestReviewDecisions.ChangesRequired && string.IsNullOrWhiteSpace(request.Comment))
      return CommandResult.Fail(ErrorCodes.AuditPlanning.Invalid, "Item-test review requires a valid decision and comment for changes.");
    var test = await db.AuditItemTests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.AuditItemTestId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, test, ReviewRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (test!.TestedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review the same item test.");
    var selection = await db.AuditSelections.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == test.SelectionId && x.FirmId == actor.FirmId, ct);
    if (selection is null || selection.Status != AuditSelectionStatuses.Reviewed)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The selection must remain reviewed before item-test review.");
    var latestRevision = await db.AuditItemTests.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.SelectionItemId == test.SelectionItemId)
      .MaxAsync(x => (long?)x.Revision, ct);
    if (latestRevision != test.Revision)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "Only the current item-test revision can be reviewed.");
    if (test.InputGeneration != await CurrentGenerationAsync(db, test.ClientId, test.FirmId, ct))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The item-test inputs changed; record a current revision.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    db.AuditItemTestReviews.Add(new AuditItemTestReview
    {
      Id = Guid.CreateVersion7(), FirmId = test.FirmId, ClientId = test.ClientId, EngagementId = test.EngagementId,
      SelectionItemId = test.SelectionItemId, AuditItemTestId = test.Id, TestRevision = test.Revision,
      Decision = decision, Comment = TrimOrNull(request.Comment), ReviewerUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<ConfirmationValue>> CreateConfirmationAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateConfirmationRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.AreaCode) || string.IsNullOrWhiteSpace(request.SourceRecordId) ||
        string.IsNullOrWhiteSpace(request.Respondent) || string.IsNullOrWhiteSpace(request.ContactValidationSource) || !IsCurrency(request.Currency))
      return Invalid<ConfirmationValue>("A confirmation requires a record, respondent, validated contact source and currency.");
    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ConfirmationValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (request.ProcedureId is not null && !await IsApplicableProcedureAsync(db, actor.FirmId, auth.ClientId, request.EngagementId, request.ProcedureId.Value, ct))
      return CommandResult<ConfirmationValue>.Fail(ErrorCodes.GateBlocked, "The linked procedure is not applicable.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var confirmation = new AuditConfirmationCase
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId,
      ProcedureId = request.ProcedureId, AreaCode = request.AreaCode.Trim().ToUpperInvariant(), SourceRecordId = request.SourceRecordId.Trim(),
      BookedAmount = request.BookedAmount, Currency = request.Currency.ToUpperInvariant(), ConfirmationDate = request.ConfirmationDate,
      Respondent = request.Respondent.Trim(), ContactValidationSource = request.ContactValidationSource.Trim(),
      InputGeneration = await CurrentGenerationAsync(db, auth.ClientId, actor.FirmId, ct), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AuditConfirmationCases.Add(confirmation);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ConfirmationValue>.Ok(new(confirmation.Id, confirmation.Status));
  }

  public static async Task<CommandResult<ConfirmationValue>> ApproveConfirmationAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid confirmationCaseId, CancellationToken ct = default) =>
    await SetConfirmationStatusAsync(db, actor, confirmationCaseId, AuditConfirmationStatuses.Approved, ReviewRoles, ct);

  public static async Task<CommandResult<ConfirmationValue>> RecordDispatchEvidenceAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordConfirmationDispatchRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.DispatchReference))
      return Invalid<ConfirmationValue>("Dispatch requires an observed provider reference.");
    var existing = await db.AuditConfirmationCases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ConfirmationCaseId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, existing, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ConfirmationValue>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var live = await db.AuditConfirmationCases.SingleOrDefaultAsync(x => x.Id == request.ConfirmationCaseId && x.FirmId == actor.FirmId, ct);
    if (live is null || live.Status != AuditConfirmationStatuses.Approved)
      return CommandResult<ConfirmationValue>.Fail(ErrorCodes.GateBlocked, "Only an approved confirmation can record dispatch evidence.");
    live.DispatchReference = request.DispatchReference.Trim();
    live.Status = AuditConfirmationStatuses.Dispatched;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ConfirmationValue>.Ok(new(live.Id, live.Status));
  }

  public static async Task<CommandResult<ConfirmationValue>> RecordConfirmationResponseAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordConfirmationResponseRequest request, CancellationToken ct = default)
  {
    var decision = request.Decision.Trim().ToUpperInvariant();
    if (decision is not (AuditConfirmationDecisions.Agreed or AuditConfirmationDecisions.Difference or AuditConfirmationDecisions.NoResponse or AuditConfirmationDecisions.AlternativeRequired) ||
        string.IsNullOrWhiteSpace(request.Origin) || string.IsNullOrWhiteSpace(request.Channel) ||
        string.IsNullOrWhiteSpace(request.ResponseReference) || string.IsNullOrWhiteSpace(request.AuthenticityAssessment))
      return Invalid<ConfirmationValue>("A response requires provenance, a receipt reference, authenticity assessment and an explicit decision.");
    var existing = await db.AuditConfirmationCases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ConfirmationCaseId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, existing, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ConfirmationValue>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var live = await db.AuditConfirmationCases.SingleOrDefaultAsync(x => x.Id == request.ConfirmationCaseId && x.FirmId == actor.FirmId, ct);
    if (live is null || live.Status != AuditConfirmationStatuses.Dispatched)
      return CommandResult<ConfirmationValue>.Fail(ErrorCodes.GateBlocked, "A response must follow observed dispatch evidence.");
    var revision = (await db.AuditConfirmationResponses.AsNoTracking().Where(x => x.ConfirmationCaseId == live.Id).MaxAsync(x => (long?)x.Revision, ct) ?? 0) + 1;
    var response = new AuditConfirmationResponse
    {
      Id = Guid.CreateVersion7(), FirmId = live.FirmId, ClientId = live.ClientId, EngagementId = live.EngagementId,
      ConfirmationCaseId = live.Id, Revision = revision, Origin = request.Origin.Trim(), Channel = request.Channel.Trim(),
      ReceivedAt = DateTimeOffset.UtcNow, ResponseReference = request.ResponseReference.Trim(), ConfirmedAmount = request.ConfirmedAmount,
      DifferenceAmount = request.ConfirmedAmount.HasValue ? request.ConfirmedAmount.Value - live.BookedAmount : null,
      AuthenticityAssessment = request.AuthenticityAssessment.Trim(), Decision = decision, CreatedByUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.AuditConfirmationResponses.Add(response);
    live.Status = decision switch
    {
      AuditConfirmationDecisions.NoResponse => AuditConfirmationStatuses.NoResponse,
      AuditConfirmationDecisions.AlternativeRequired => AuditConfirmationStatuses.AlternativeRequired,
      _ => AuditConfirmationStatuses.ResponseReceived
    };
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ConfirmationValue>.Ok(new(live.Id, live.Status));
  }

  public static async Task<CommandResult> ReviewConfirmationResponseAsync(
    IAuditSphereDbContext db, ActorContext actor, ReviewConfirmationResponseRequest request, CancellationToken ct = default)
  {
    var response = await db.AuditConfirmationResponses.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.ConfirmationResponseId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, response, ReviewRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (response!.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The response preparer cannot review the same response.");
    var latestRevision = await db.AuditConfirmationResponses.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ConfirmationCaseId == response.ConfirmationCaseId)
      .MaxAsync(x => (long?)x.Revision, ct);
    if (latestRevision != response.Revision)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "Only the current confirmation response can be reviewed.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var live = await db.AuditConfirmationResponses.SingleAsync(x => x.Id == response.Id && x.FirmId == actor.FirmId, ct);
    live.ReviewedByUserId = actor.UserId;
    live.ReviewedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<ConfirmationValue>> RecordAlternativeProcedureAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordAlternativeProcedureRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Purpose) || request.EvidenceReferences is null || request.EvidenceReferences.Count == 0 ||
        request.EvidenceReferences.Any(string.IsNullOrWhiteSpace) || string.IsNullOrWhiteSpace(request.Conclusion))
      return Invalid<ConfirmationValue>("Alternative work requires purpose, evidence and a conclusion.");
    var existing = await db.AuditConfirmationCases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ConfirmationCaseId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, existing, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ConfirmationValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (existing is null || existing.Status is not (AuditConfirmationStatuses.NoResponse or AuditConfirmationStatuses.AlternativeRequired))
      return CommandResult<ConfirmationValue>.Fail(ErrorCodes.GateBlocked, "Alternative work requires an outstanding or non-response confirmation.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var alternative = new AuditAlternativeProcedure
    {
      Id = Guid.CreateVersion7(), FirmId = existing.FirmId, ClientId = existing.ClientId, EngagementId = existing.EngagementId,
      ConfirmationCaseId = existing.Id, Purpose = request.Purpose.Trim(), EvidenceReferencesJson = JsonSerializer.Serialize(request.EvidenceReferences),
      Conclusion = request.Conclusion.Trim(), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AuditAlternativeProcedures.Add(alternative);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ConfirmationValue>.Ok(new(existing.Id, existing.Status));
  }

  public static async Task<CommandResult> ReviewAlternativeProcedureAsync(
    IAuditSphereDbContext db, ActorContext actor, ReviewAlternativeProcedureRequest request, CancellationToken ct = default)
  {
    var alternative = await db.AuditAlternativeProcedures.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.AlternativeProcedureId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, alternative, ReviewRoles, ct);
    if (!auth.Succeeded)
      return auth;
    var live = await db.AuditAlternativeProcedures.SingleOrDefaultAsync(x => x.Id == request.AlternativeProcedureId && x.FirmId == actor.FirmId, ct);
    if (live is null)
      return Denied();
    if (live.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review the same alternative procedure.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    live.Status = AuditAlternativeStatuses.Reviewed;
    live.ReviewedByUserId = actor.UserId;
    live.ReviewedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<ConfirmationValue>> CloseConfirmationAsync(
    IAuditSphereDbContext db, ActorContext actor, CloseConfirmationRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Conclusion))
      return Invalid<ConfirmationValue>("A confirmation close requires a reviewer conclusion.");
    var existing = await db.AuditConfirmationCases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ConfirmationCaseId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, existing, ReviewRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ConfirmationValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (existing is null || existing.Status == AuditConfirmationStatuses.Draft)
      return Denied<ConfirmationValue>();
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var latestResponse = await db.AuditConfirmationResponses.AsNoTracking()
      .Where(x => x.ConfirmationCaseId == existing.Id)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (latestResponse is not null && latestResponse.ReviewedByUserId is null)
      return CommandResult<ConfirmationValue>.Fail(ErrorCodes.GateBlocked, "The confirmation response requires independent review before closure.");
    var hasResponse = latestResponse is not null;
    var hasAlternative = await db.AuditAlternativeProcedures.AnyAsync(x => x.ConfirmationCaseId == existing.Id && x.Status == AuditAlternativeStatuses.Reviewed, ct);
    if (!hasResponse && !hasAlternative)
      return CommandResult<ConfirmationValue>.Fail(ErrorCodes.GateBlocked, "A confirmation cannot close without a response or reviewed alternative work.");
    var live = await db.AuditConfirmationCases.SingleAsync(x => x.Id == existing.Id && x.FirmId == actor.FirmId, ct);
    live.Status = AuditConfirmationStatuses.Closed;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ConfirmationValue>.Ok(new(live.Id, live.Status));
  }

  public static async Task<CommandResult<AreaAssessmentValue>> RecordAreaAssessmentAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordAreaAssessmentRequest request, CancellationToken ct = default)
  {
    if (!AuditAreaCodes.All.Contains(request.AreaCode.Trim().ToUpperInvariant()) || string.IsNullOrWhiteSpace(request.AssessmentKind) ||
        string.IsNullOrWhiteSpace(request.MethodologyReference) || !JsonObject(request.InputSnapshotJson) ||
        request.EvidenceReferences is null || request.EvidenceReferences.Count == 0 || request.EvidenceReferences.Any(string.IsNullOrWhiteSpace) ||
        string.IsNullOrWhiteSpace(request.Conclusion) || request.Currency is not null && !IsCurrency(request.Currency))
      return Invalid<AreaAssessmentValue>("A typed area assessment requires an approved method, object inputs, evidence and a conclusion.");
    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<AreaAssessmentValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (request.ProcedureId is not null && !await IsApplicableProcedureAsync(db, actor.FirmId, auth.ClientId, request.EngagementId, request.ProcedureId.Value, ct))
      return CommandResult<AreaAssessmentValue>.Fail(ErrorCodes.GateBlocked, "The linked procedure is not applicable.");
    if (request.Currency is not null && !IsCurrency(request.Currency))
      return Invalid<AreaAssessmentValue>("Currency must be ISO 4217.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var generation = await CurrentGenerationAsync(db, auth.ClientId, actor.FirmId, ct);
    var assessment = new AuditAreaAssessment
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId,
      ProcedureId = request.ProcedureId, AreaCode = request.AreaCode.Trim().ToUpperInvariant(), AssessmentKind = request.AssessmentKind.Trim(),
      MethodologyReference = request.MethodologyReference.Trim(), InputSnapshotJson = request.InputSnapshotJson.Trim(),
      BookedAmount = request.BookedAmount, AuditedAmount = request.AuditedAmount, ResidualAmount = request.ResidualAmount,
      VariancePercent = request.VariancePercent, Currency = request.Currency?.ToUpperInvariant(), PeriodStart = request.PeriodStart,
      PeriodEnd = request.PeriodEnd, EvidenceReferencesJson = JsonSerializer.Serialize(request.EvidenceReferences), InputGeneration = generation,
      Conclusion = request.Conclusion.Trim(), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AuditAreaAssessments.Add(assessment);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<AreaAssessmentValue>.Ok(new(assessment.Id, assessment.Status));
  }

  public static async Task<CommandResult> ReviewAreaAssessmentAsync(
    IAuditSphereDbContext db, ActorContext actor, ReviewAreaAssessmentRequest request, CancellationToken ct = default)
  {
    var decision = request.Decision.Trim().ToUpperInvariant();
    if (decision is not (AuditAreaAssessmentStatuses.Reviewed or AuditAreaAssessmentStatuses.ChangesRequired) ||
        decision == AuditAreaAssessmentStatuses.ChangesRequired && string.IsNullOrWhiteSpace(request.Comment))
      return CommandResult.Fail(ErrorCodes.AuditPlanning.Invalid, "Area assessment review requires a valid decision and comment for changes.");
    var existing = await db.AuditAreaAssessments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.AuditAreaAssessmentId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, existing, ReviewRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (existing!.InputGeneration != await CurrentGenerationAsync(db, existing.ClientId, existing.FirmId, ct))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The assessment inputs changed; review the new revision.");
    if (existing.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review the same assessment.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var live = await db.AuditAreaAssessments.SingleAsync(x => x.Id == existing.Id && x.FirmId == actor.FirmId, ct);
    live.Status = decision;
    live.ReviewedByUserId = actor.UserId;
    live.ReviewedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<DifferenceValue>> RecordDifferenceAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordDifferenceRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.AccountArea) || string.IsNullOrWhiteSpace(request.DifferenceType) ||
        string.IsNullOrWhiteSpace(request.Description) || request.Amount == 0 || !IsCurrency(request.Currency) ||
        request.MaterialityReference.Trim().Length > 1000 || request.QualitativeConcerns.Trim().Length > 4000)
      return Invalid<DifferenceValue>("An audit difference requires a signed non-zero amount, area, type, description and currency.");
    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<DifferenceValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (request.ProcedureId is not null && !await IsApplicableProcedureAsync(db, actor.FirmId, auth.ClientId, request.EngagementId, request.ProcedureId.Value, ct))
      return CommandResult<DifferenceValue>.Fail(ErrorCodes.GateBlocked, "The linked procedure is not applicable.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var difference = new AuditDifference
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId,
      ProcedureId = request.ProcedureId, AccountArea = request.AccountArea.Trim(), DifferenceType = request.DifferenceType.Trim(),
      Description = request.Description.Trim(), MaterialityReference = TrimOrNull(request.MaterialityReference),
      QualitativeConcerns = TrimOrNull(request.QualitativeConcerns), Amount = request.Amount, Currency = request.Currency.ToUpperInvariant(),
      InputGeneration = await CurrentGenerationAsync(db, auth.ClientId, actor.FirmId, ct), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AuditDifferences.Add(difference);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<DifferenceValue>.Ok(new(difference.Id, difference.Status));
  }

  public static async Task<CommandResult<DifferenceValue>> EvaluateDifferenceAsync(
    IAuditSphereDbContext db, ActorContext actor, EvaluateDifferenceRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Evaluation))
      return Invalid<DifferenceValue>("Difference evaluation requires a conclusion.");
    var existing = await db.AuditDifferences.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.AuditDifferenceId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, existing, ReviewRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<DifferenceValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (existing!.InputGeneration != await CurrentGenerationAsync(db, existing.ClientId, existing.FirmId, ct))
      return CommandResult<DifferenceValue>.Fail(ErrorCodes.GenerationStale, "The difference is based on stale inputs.");
    if (existing.CreatedByUserId == actor.UserId)
      return CommandResult<DifferenceValue>.Fail(ErrorCodes.ScopeDenied, "The preparer cannot evaluate the same difference.");
    if (request.Corrected)
    {
      if (existing.ProposedJournalId is null || existing.ProposedJournalRevision is null ||
          existing.SourceReflectionReconciliationId is null || existing.VerifiedAdjustedSnapshotId is null)
        return CommandResult<DifferenceValue>.Fail(ErrorCodes.GateBlocked,
          "A difference cannot be marked corrected without typed journal, source-reflection and adjusted-snapshot evidence.");
      if (existing.CorrectionState == AuditDifferenceCorrectionStates.Rejected)
        return CommandResult<DifferenceValue>.Fail(ErrorCodes.GateBlocked, "A rejected correction cannot become verified reflected without a new reviewed difference.");
      if (!HasCurrentJournalImpact(existing))
        return CommandResult<DifferenceValue>.Fail(ErrorCodes.GateBlocked, "The exact journal impact evidence is missing, stale or unsupported.");
      var journal = await db.AdjustmentJournals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == existing.ProposedJournalId &&
        x.FirmId == existing.FirmId && x.ClientId == existing.ClientId && x.EngagementId == existing.EngagementId &&
        x.Revision == existing.ProposedJournalRevision && x.Status == "Posted", ct);
      var reflection = await db.JournalSourceReconciliations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == existing.SourceReflectionReconciliationId &&
        x.FirmId == existing.FirmId && x.ClientId == existing.ClientId && x.EngagementId == existing.EngagementId &&
        x.JournalRevision == existing.ProposedJournalRevision && x.State == ReflectionStates.Reflected, ct);
      var snapshot = await db.AdjustedTrialBalanceSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == existing.VerifiedAdjustedSnapshotId &&
        x.FirmId == existing.FirmId && x.ClientId == existing.ClientId && x.EngagementId == existing.EngagementId, ct);
      if (journal is null || reflection is null || snapshot is null || reflection.BaseDatasetId != journal.BaseDatasetId ||
          snapshot.BaseDatasetId != journal.BaseDatasetId || !string.Equals(reflection.LogicalJournalNumber, journal.JournalNumber, StringComparison.Ordinal))
        return CommandResult<DifferenceValue>.Fail(ErrorCodes.GateBlocked,
          "The correction evidence is stale or does not match the exact posted journal revision.");
    }
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var live = await db.AuditDifferences.SingleAsync(x => x.Id == existing.Id && x.FirmId == actor.FirmId, ct);
    live.Corrected = request.Corrected;
    live.ManagementResponse = TrimOrNull(request.ManagementResponse);
    live.CorrectionReference = TrimOrNull(request.CorrectionReference);
    live.Evaluation = request.Evaluation.Trim();
    live.Status = request.Corrected ? AuditDifferenceStatuses.VerifiedReflected : AuditDifferenceStatuses.Evaluated;
    if (request.Corrected)
      live.CorrectionState = AuditDifferenceCorrectionStates.VerifiedReflected;
    live.EvaluatedByUserId = actor.UserId;
    live.EvaluatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<DifferenceValue>.Ok(new(live.Id, live.Status));
  }

  public static async Task<CommandResult<IReadOnlyList<AuditDifferenceSummary>>> GetDifferenceSummariesAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var auth = await AuthorizeEngagementAsync(db, actor, engagementId, ReviewRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<IReadOnlyList<AuditDifferenceSummary>>.Fail(auth.ErrorCode!, auth.Message!);
    var differences = await db.AuditDifferences.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId).ToListAsync(ct);
    var summaries = differences.GroupBy(x => x.Currency, StringComparer.OrdinalIgnoreCase)
      .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
      .Select(group =>
      {
        var unadjusted = group.Where(x => !x.Corrected);
        var corrected = group.Where(x => x.Corrected);
        return new AuditDifferenceSummary(
          group.Key.ToUpperInvariant(), group.Count(),
          MoneyPolicy.Normalize(group.Sum(x => Math.Abs(x.Amount))),
          MoneyPolicy.Normalize(group.Sum(x => x.Amount)),
          MoneyPolicy.Normalize(unadjusted.Sum(x => Math.Abs(x.Amount))),
          MoneyPolicy.Normalize(unadjusted.Sum(x => x.Amount)),
          MoneyPolicy.Normalize(corrected.Sum(x => Math.Abs(x.Amount))),
          MoneyPolicy.Normalize(corrected.Sum(x => x.Amount)));
      }).ToArray();
    return CommandResult<IReadOnlyList<AuditDifferenceSummary>>.Ok(summaries);
  }

  public static async Task<CommandResult<DifferenceValue>> SetDifferenceCorrectionStateAsync(
    IAuditSphereDbContext db, ActorContext actor, SetDifferenceCorrectionStateRequest request,
    CancellationToken ct = default)
  {
    var state = request.CorrectionState.Trim().ToUpperInvariant();
    if (state is not (AuditDifferenceCorrectionStates.Proposed or AuditDifferenceCorrectionStates.Agreed or
        AuditDifferenceCorrectionStates.Rejected or AuditDifferenceCorrectionStates.AppliedInReporting or
        AuditDifferenceCorrectionStates.ReportedPostedExternally))
      return Invalid<DifferenceValue>("The correction state is unsupported.");
    if (state == AuditDifferenceCorrectionStates.Rejected && string.IsNullOrWhiteSpace(request.Reason))
      return Invalid<DifferenceValue>("Rejecting a correction requires a professional reason.");
    var existing = await db.AuditDifferences.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.AuditDifferenceId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, existing, ReviewRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<DifferenceValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (existing is null)
      return Denied<DifferenceValue>();
    if (existing.CreatedByUserId == actor.UserId)
      return CommandResult<DifferenceValue>.Fail(ErrorCodes.ScopeDenied, "The preparer cannot set the correction state.");
    if (existing.InputGeneration != await CurrentGenerationAsync(db, existing.ClientId, existing.FirmId, ct))
      return CommandResult<DifferenceValue>.Fail(ErrorCodes.GenerationStale, "The difference is based on stale inputs.");
    if (existing.Corrected || existing.CorrectionState == AuditDifferenceCorrectionStates.VerifiedReflected ||
        !CanTransitionCorrectionState(existing.CorrectionState, state))
      return CommandResult<DifferenceValue>.Fail(ErrorCodes.ProtectedState, "The correction state cannot move from its current reviewed state.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var live = await db.AuditDifferences.SingleAsync(x => x.Id == existing.Id && x.FirmId == actor.FirmId, ct);
    live.CorrectionState = state;
    if (!string.IsNullOrWhiteSpace(request.Reason))
      live.Evaluation = request.Reason.Trim();
    if (state == AuditDifferenceCorrectionStates.Rejected)
    {
      live.Corrected = false;
      live.Status = AuditDifferenceStatuses.Evaluated;
    }
    live.EvaluatedByUserId = actor.UserId;
    live.EvaluatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<DifferenceValue>.Ok(new(live.Id, live.Status));
  }

  public static async Task<CommandResult> LinkDifferenceToJournalAsync(
    IAuditSphereDbContext db, ActorContext actor, LinkDifferenceToJournalRequest request,
    CancellationToken ct = default)
  {
    var state = request.CorrectionState.Trim().ToUpperInvariant();
    if (request.JournalRevision < 1 || request.SourceReflectionReconciliationId == Guid.Empty ||
        request.VerifiedAdjustedSnapshotId == Guid.Empty || state is not (
          AuditDifferenceCorrectionStates.Proposed or AuditDifferenceCorrectionStates.Agreed or
          AuditDifferenceCorrectionStates.AppliedInReporting or AuditDifferenceCorrectionStates.ReportedPostedExternally))
      return CommandResult.Fail(ErrorCodes.AuditPlanning.Invalid, "A correction link needs an exact revision, source reflection, snapshot and supported state.");
    var difference = await db.AuditDifferences.SingleOrDefaultAsync(x => x.Id == request.AuditDifferenceId &&
      x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, difference, ReviewRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (difference is null)
      return Denied();
    if (!CanTransitionCorrectionState(difference.CorrectionState, state))
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The correction state cannot move from its current reviewed state.");
    var journal = await db.AdjustmentJournals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.JournalId &&
      x.FirmId == actor.FirmId && x.ClientId == difference!.ClientId && x.EngagementId == difference.EngagementId &&
      x.Revision == request.JournalRevision && x.Status != "Void", ct);
    var reflection = await db.JournalSourceReconciliations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.SourceReflectionReconciliationId && x.FirmId == actor.FirmId &&
      x.ClientId == difference!.ClientId && x.EngagementId == difference.EngagementId &&
      x.JournalRevision == request.JournalRevision, ct);
    var snapshot = await db.AdjustedTrialBalanceSnapshots.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.VerifiedAdjustedSnapshotId && x.FirmId == actor.FirmId &&
      x.ClientId == difference!.ClientId && x.EngagementId == difference.EngagementId, ct);
    if (journal is null || reflection is null || snapshot is null || reflection.BaseDatasetId != journal.BaseDatasetId ||
        snapshot.BaseDatasetId != journal.BaseDatasetId ||
        !string.Equals(reflection.LogicalJournalNumber, journal.JournalNumber, StringComparison.Ordinal))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The journal, source reflection and adjusted snapshot are not the same exact source lineage.");
    var rawLines = await db.AdjustmentLines.AsNoTracking().Where(x => x.JournalId == journal.Id)
      .OrderBy(x => x.AccountCode).ThenBy(x => x.Id)
      .Select(x => new { x.AccountCode, x.Debit, x.Credit })
      .ToListAsync(ct);
    var lines = rawLines.Select(x => new JournalImpactLine(x.AccountCode, x.Debit, x.Credit,
      MoneyPolicy.Normalize(x.Debit - x.Credit), [])).ToList();
    if (lines.Count == 0)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The linked journal has no immutable lines.");

    MappingVersion? mapping = null;
    var mappingAllocations = new List<MappingAllocation>();
    var disclosureByDestination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (db is IClientAccountingDbContext accountingDb)
    {
      mapping = await accountingDb.MappingVersions.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.ClientId == difference.ClientId && x.EngagementId == difference.EngagementId &&
        x.DatasetId == journal.BaseDatasetId && x.Status == AccountingPackageStates.MappingApproved)
        .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
      if (mapping is not null)
      {
        mappingAllocations = await accountingDb.MappingAllocations.AsNoTracking().Where(x =>
          x.FirmId == actor.FirmId && x.ClientId == difference.ClientId && x.EngagementId == difference.EngagementId &&
          x.MappingVersionId == mapping.Id).OrderBy(x => x.SourceAccountCode).ThenBy(x => x.DestinationCode).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(mapping.TaxonomyVersion))
        {
          var taxonomyId = await accountingDb.ReportingTaxonomyVersions.AsNoTracking().Where(x =>
            x.FirmId == actor.FirmId && x.Code == mapping.TaxonomyVersion && x.Status == AccountingWorkflowStates.Approved)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
          if (taxonomyId is not null)
            disclosureByDestination = await accountingDb.ReportingTaxonomyNodes.AsNoTracking().Where(x =>
              x.FirmId == actor.FirmId && x.TaxonomyVersionId == taxonomyId.Value)
              .ToDictionaryAsync(x => x.Code, x => x.DisclosureArea, StringComparer.OrdinalIgnoreCase);
        }
      }
    }

    var classifiedLines = lines.Select(line =>
    {
      var signedAmount = line.SignedAmount;
      var allocations = mappingAllocations.Where(x => string.Equals(x.SourceAccountCode.Trim(), line.AccountCode.Trim(), StringComparison.OrdinalIgnoreCase))
        .Select(x =>
        {
          var section = x.StatementSection.Trim().ToUpperInvariant();
          var disclosure = disclosureByDestination.GetValueOrDefault(x.DestinationCode.Trim())?.Trim().ToUpperInvariant();
          var allocatedAmount = MoneyPolicy.Normalize(signedAmount * x.Fraction);
          return new JournalImpactAllocation(x.DestinationCode.Trim(), section, (x.AuditArea ?? string.Empty).Trim(),
            string.IsNullOrWhiteSpace(disclosure) ? "UNSPECIFIED" : disclosure, MoneyPolicy.Normalize(x.Fraction), allocatedAmount,
            IsProfitSection(section) ? allocatedAmount : 0m, IsEquitySection(section) ? allocatedAmount : 0m, true);
        }).ToArray();
      return allocations.Length == 0
        ? line with { Allocations = [new JournalImpactAllocation("UNMAPPED", "UNMAPPED", string.Empty, "UNMAPPED", 1m, signedAmount, 0m, 0m, false)] }
        : line with { Allocations = allocations };
    }).ToArray();
    var effects = classifiedLines.SelectMany(x => x.Allocations).ToArray();
    var classificationStatus = classifiedLines.All(x => x.Allocations.All(a => a.Mapped)) ? "MAPPED" : "PARTIAL_OR_UNMAPPED";
    var impactJson = JsonSerializer.Serialize(new
    {
      Schema = "journal-impact.v2", journal.Id, journal.JournalNumber, journal.Revision, journal.Purpose, journal.Currency,
      Mapping = mapping is null ? null : new { mapping.Id, mapping.Version, mapping.Generation, mapping.TaxonomyVersion },
      Classification = new
      {
        Status = classificationStatus,
        AccountEffects = classifiedLines.Select(x => new { x.AccountCode, Amount = x.SignedAmount }),
        StatementEffects = effects.GroupBy(x => x.StatementSection, StringComparer.OrdinalIgnoreCase)
          .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
          .Select(x => new { StatementSection = x.Key, Amount = MoneyPolicy.Normalize(x.Sum(y => y.SignedAmount)) }),
        ProfitEffect = MoneyPolicy.Normalize(effects.Sum(x => x.ProfitEffect)),
        EquityEffect = MoneyPolicy.Normalize(effects.Sum(x => x.EquityEffect)),
        DisclosureEffects = effects.GroupBy(x => x.DisclosureArea, StringComparer.OrdinalIgnoreCase)
          .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
          .Select(x => new { DisclosureArea = x.Key, Amount = MoneyPolicy.Normalize(x.Sum(y => y.SignedAmount)) })
      },
      Lines = classifiedLines
    });
    difference.ProposedJournalId = journal.Id;
    difference.ProposedJournalRevision = journal.Revision;
    difference.SourceReflectionReconciliationId = reflection.Id;
    difference.VerifiedAdjustedSnapshotId = snapshot.Id;
    difference.CorrectionState = state;
    difference.JournalImpactJson = impactJson;
    difference.JournalImpactHash = Hashing.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(impactJson));
    difference.CorrectionReference = $"journal:{journal.Id:D}:revision:{journal.Revision}";
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<AuditCompletionEvaluation>> EvaluateCompletionAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var auth = await AuthorizeEngagementAsync(db, actor, engagementId, ReviewRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<AuditCompletionEvaluation>.Fail(auth.ErrorCode!, auth.Message!);
    var blockers = new List<string>();
    var procedures = await db.AuditProcedures.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId).ToListAsync(ct);
    if (procedures.Count == 0)
      blockers.Add("audit-program-not-adopted");
    var applicable = procedures.Where(x => x.ApplicabilityStatus == AuditApplicabilityStatuses.Applicable).ToArray();
    blockers.AddRange(procedures.Where(x => x.ApplicabilityStatus is AuditApplicabilityStatuses.Pending or AuditApplicabilityStatuses.NotApplicablePendingReview)
      .Select(x => $"applicability:{x.SourceProcedureId}"));
    var resultRows = await db.AuditProcedureResults.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId).ToListAsync(ct);
    var reviews = await db.AuditProcedureReviews.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId).ToListAsync(ct);
    var reviewed = 0;
    foreach (var procedure in applicable)
    {
      var result = resultRows.Where(x => x.AuditProcedureId == procedure.Id).OrderByDescending(x => x.Revision).FirstOrDefault();
      var review = result is null ? null : reviews.Where(x => x.AuditProcedureResultId == result.Id).OrderByDescending(x => x.CreatedAt).FirstOrDefault();
      if (result is not null && procedure.Status == AuditProcedureStatuses.Reviewed && review?.Decision == AuditProcedureReviewDecisions.Reviewed)
        reviewed++;
      else
        blockers.Add($"procedure:{procedure.SourceProcedureId}:unreviewed");
    }
    blockers.AddRange(await db.AuditSelections.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId && x.Status != AuditSelectionStatuses.Reviewed)
      .Select(x => $"selection:{x.Id}:unreviewed").ToListAsync(ct));
    var reviewedSelections = await db.AuditSelections.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId && x.Status == AuditSelectionStatuses.Reviewed)
      .Select(x => x.Id).ToArrayAsync(ct);
    var selectedItemIds = await db.AuditSelectionItems.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId && reviewedSelections.Contains(x.SelectionId))
      .Select(x => x.Id).ToArrayAsync(ct);
    var currentGeneration = await CurrentGenerationAsync(db, auth.ClientId, actor.FirmId, ct);
    var testedItemIds = await db.AuditItemTests.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId && selectedItemIds.Contains(x.SelectionItemId) &&
      x.InputGeneration == currentGeneration &&
      !db.AuditItemTests.Any(newer => newer.FirmId == x.FirmId && newer.SelectionItemId == x.SelectionItemId && newer.Revision > x.Revision))
      .Where(x => db.AuditItemTestReviews.Any(review => review.FirmId == actor.FirmId && review.AuditItemTestId == x.Id &&
        review.TestRevision == x.Revision && review.Decision == AuditItemTestReviewDecisions.Reviewed))
      .Select(x => x.SelectionItemId).Distinct().ToArrayAsync(ct);
    var unreviewedItemCount = selectedItemIds.Except(testedItemIds).Count();
    if (unreviewedItemCount > 0)
      blockers.Add($"item-tests:{unreviewedItemCount}:unreviewed");
    blockers.AddRange(await db.AuditConfirmationCases.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId && x.Status != AuditConfirmationStatuses.Closed)
      .Select(x => $"confirmation:{x.Id}:open").ToListAsync(ct));
    blockers.AddRange(await db.AuditAreaAssessments.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId && x.Status != AuditAreaAssessmentStatuses.Reviewed)
      .Select(x => $"assessment:{x.Id}:unreviewed").ToListAsync(ct));
    blockers.AddRange(await db.AuditDifferences.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId &&
      (x.Status == AuditDifferenceStatuses.Open || x.Status == AuditDifferenceStatuses.ManagementResponded))
      .Select(x => $"difference:{x.Id}:unevaluated").ToListAsync(ct));
    var evaluation = new AuditCompletionEvaluation(blockers.Count == 0, procedures.Count, applicable.Length, reviewed, blockers);
    return CommandResult<AuditCompletionEvaluation>.Ok(evaluation);
  }

  private static async Task<CommandResult<ConfirmationValue>> SetConfirmationStatusAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid id, string status, IReadOnlyList<string> roles, CancellationToken ct)
  {
    var existing = await db.AuditConfirmationCases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, existing, roles, ct);
    if (!auth.Succeeded)
      return CommandResult<ConfirmationValue>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var live = await db.AuditConfirmationCases.SingleAsync(x => x.Id == id && x.FirmId == actor.FirmId, ct);
    if (status == AuditConfirmationStatuses.Approved && live.Status != AuditConfirmationStatuses.Draft)
      return CommandResult<ConfirmationValue>.Fail(ErrorCodes.ProtectedState, "The confirmation is already beyond draft.");
    if (status == AuditConfirmationStatuses.Approved && live.CreatedByUserId == actor.UserId)
      return CommandResult<ConfirmationValue>.Fail(ErrorCodes.ScopeDenied, "The preparer cannot approve the same confirmation.");
    live.Status = status;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ConfirmationValue>.Ok(new(live.Id, live.Status));
  }

  private sealed record ScopedAuthorization(bool Succeeded, Guid ClientId, string? ErrorCode = null, string? Message = null);

  private static async Task<ScopedAuthorization> AuthorizeEngagementAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, IReadOnlyList<string> roles, CancellationToken ct)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null)
      return new(false, Guid.Empty, ErrorCodes.ScopeDenied, "Access denied.");
    var result = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, roles.ToArray(), true, true), ct);
    return new(result.Succeeded, engagement.PracticeClientId, result.ErrorCode, result.Message);
  }

  private static async Task<CommandResult> AuthorizeEntityAsync<T>(
    IAuditSphereDbContext db, ActorContext actor, T? entity, IReadOnlyList<string> roles, CancellationToken ct) where T : class
  {
    if (entity is null)
      return Denied();
    var (clientId, engagementId) = entity switch
    {
      AuditSchedule x => (x.ClientId, x.EngagementId),
      AuditSelection x => (x.ClientId, x.EngagementId),
      AuditSelectionItem x => (x.ClientId, x.EngagementId),
      AuditItemTest x => (x.ClientId, x.EngagementId),
      AuditConfirmationCase x => (x.ClientId, x.EngagementId),
      AuditConfirmationResponse x => (x.ClientId, x.EngagementId),
      AuditAlternativeProcedure x => (x.ClientId, x.EngagementId),
      AuditAreaAssessment x => (x.ClientId, x.EngagementId),
      AuditDifference x => (x.ClientId, x.EngagementId),
      _ => (Guid.Empty, Guid.Empty)
    };
    if (clientId == Guid.Empty || engagementId == Guid.Empty)
      return Denied();
    return await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, engagementId, roles.ToArray(), true, true), ct);
  }

  private static async Task<long> CurrentGenerationAsync(IAuditSphereDbContext db, Guid clientId, Guid firmId, CancellationToken ct) =>
    Math.Max(1, await db.ClientSafetyStates.AsNoTracking().Where(x => x.Id == clientId && x.FirmId == firmId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct) ?? 1);

  private static async Task<bool> IsApplicableProcedureAsync(IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId, Guid procedureId, CancellationToken ct) =>
    await db.AuditProcedures.AsNoTracking().AnyAsync(x => x.Id == procedureId && x.FirmId == firmId && x.ClientId == clientId &&
      x.EngagementId == engagementId && x.ApplicabilityStatus == AuditApplicabilityStatuses.Applicable, ct);

  private static ScheduleValue ToScheduleValue(AuditSchedule schedule) =>
    new(schedule.Id, schedule.Status, schedule.RowCount, schedule.SignedControlTotal, schedule.Residual);

  private static CommandResult<T> Invalid<T>(string message) => CommandResult<T>.Fail(ErrorCodes.AuditPlanning.Invalid, message);
  private static CommandResult<T> Denied<T>() => CommandResult<T>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  private static CommandResult Denied() => CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
  private static bool IsCurrency(string value) => value.Length == 3 && value.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
  private static bool IsProfitSection(string section) => section is "INCOME" or "P&L" or "PROFIT_LOSS" or "P_AND_L";
  private static bool IsEquitySection(string section) => section is "EQUITY" or "OCI" or "CHANGES_IN_EQUITY";
  private static bool CanTransitionCorrectionState(string? current, string requested)
  {
    var prior = current?.Trim().ToUpperInvariant();
    if (string.IsNullOrWhiteSpace(prior))
      return requested is not AuditDifferenceCorrectionStates.VerifiedReflected;
    if (prior == requested)
      return true;
    return prior switch
    {
      AuditDifferenceCorrectionStates.Proposed => requested is AuditDifferenceCorrectionStates.Agreed or AuditDifferenceCorrectionStates.Rejected,
      AuditDifferenceCorrectionStates.Agreed => requested is AuditDifferenceCorrectionStates.AppliedInReporting or AuditDifferenceCorrectionStates.Rejected,
      AuditDifferenceCorrectionStates.AppliedInReporting => requested is AuditDifferenceCorrectionStates.ReportedPostedExternally or AuditDifferenceCorrectionStates.Rejected,
      _ => false
    };
  }
  private static bool HasCurrentJournalImpact(AuditDifference difference)
  {
    if (string.IsNullOrWhiteSpace(difference.JournalImpactJson) || string.IsNullOrWhiteSpace(difference.JournalImpactHash))
      return false;
    try
    {
      using var document = JsonDocument.Parse(difference.JournalImpactJson);
      return document.RootElement.TryGetProperty("Schema", out var schema) &&
        schema.GetString() == "journal-impact.v2" &&
        string.Equals(Hashing.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(difference.JournalImpactJson)),
          difference.JournalImpactHash, StringComparison.OrdinalIgnoreCase);
    }
    catch (JsonException)
    {
      return false;
    }
  }
  private static bool IsHash(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
  private static bool JsonObject(string value)
  {
    try
    {
      using var doc = JsonDocument.Parse(value);
      return doc.RootElement.ValueKind == JsonValueKind.Object;
    }
    catch (JsonException) { return false; }
  }

  private sealed record JournalImpactLine(string AccountCode, decimal Debit, decimal Credit, decimal SignedAmount,
    IReadOnlyList<JournalImpactAllocation> Allocations);
  private sealed record JournalImpactAllocation(string DestinationCode, string StatementSection, string AuditArea,
    string DisclosureArea, decimal Fraction, decimal SignedAmount, decimal ProfitEffect, decimal EquityEffect, bool Mapped);
}
