using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

// T058 completion: cut-off testing, subsequent-settlement matching and the sample-set
// read query on top of the existing selection/item-test entities. The selection itself is
// produced by the pure sampling engine in the domain layer.
public sealed record CutOffTestRequest(
  Guid SelectionItemId, DateOnly PeriodEndDate, DateOnly TransactionDate,
  DateOnly? DocumentDate, DateOnly? ShipReceiveDate, string WorkPerformed,
  IReadOnlyList<string> EvidenceReferences);

public sealed record CutOffTestValue(
  Guid CutOffTestId, Guid SelectionItemId, string PeriodEndIndicator, bool IsCutOffException, long Revision);

public sealed record SubsequentMatchRequest(
  Guid SelectionItemId, decimal MatchedAmount, string SubsequentSourceReference,
  DateOnly? SubsequentDate, string EvidenceReference, string? UnmatchedReason);

public sealed record SubsequentMatchValue(
  Guid MatchId, Guid SelectionItemId, string State, decimal ItemSignedAmount,
  decimal MatchedAmount, decimal UnmatchedAmount, long Revision);

public sealed record SampleSetItemView(
  Guid SelectionItemId, string StableRowId, decimal SignedAmount, string Currency, string InclusionReason,
  string TestResult, long TestRevision, decimal? ExceptionAmount,
  Guid? CutOffTestId, string? CutOffIndicator, bool? CutOffException,
  Guid? SubsequentMatchId, string? SubsequentState, decimal? MatchedAmount,
  Guid? AuditItemTestId, string? TestWorkPerformed, IReadOnlyList<string> EvidenceReferences,
  string? ContradictoryEvidence, string? FollowUp, bool TestReviewed, string? TestReviewDecision,
  string? TestReviewComment, bool CanReviewTest);

public sealed record SampleSetView(
  Guid SelectionId, Guid ProcedureId, string Method, string Rationale, string Status,
  int SelectedCount, decimal SelectedSignedTotal, long InputGeneration,
  int TestedCount, int ExceptionCount, int CutOffRecordedCount, int SubsequentMatchedCount,
  IReadOnlyList<SampleSetItemView> Items, int TotalCount, int Page, int PageSize);

public static class AuditSamplingService
{
  private static readonly string[] PlanningRoles =
    ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static readonly string[] ReadRoles =
    ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Reviewer", "Administrator"];

  /// <summary>Records cut-off evidence for one sampled item. The period-end indicator is
  /// derived from the recorded dates, and a booked item whose document or shipping date
  /// falls in the next period is flagged as a cut-off exception rather than silently passing.</summary>
  public static async Task<CommandResult<CutOffTestValue>> RecordCutOffTestAsync(
    IClientAccountingDbContext db, ActorContext actor, CutOffTestRequest request,
    CancellationToken ct = default)
  {
    if (request.SelectionItemId == Guid.Empty || string.IsNullOrWhiteSpace(request.WorkPerformed) ||
        request.WorkPerformed.Trim().Length > 20000 || request.EvidenceReferences is null)
      return CommandResult<CutOffTestValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A cut-off test needs the sampled item, work performed and evidence references.");
    var item = await db.AuditSelectionItems.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.SelectionItemId && x.FirmId == actor.FirmId, ct);
    if (item is null)
      return CommandResult<CutOffTestValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(item.FirmId, item.ClientId, item.EngagementId, PlanningRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<CutOffTestValue>.Fail(auth.ErrorCode!, auth.Message!);

    var indicator = request.TransactionDate <= request.PeriodEndDate
      ? AuditCutOffDirections.BeforePeriodEnd : AuditCutOffDirections.AfterPeriodEnd;
    // The booked period position and the supporting document position must agree; a
    // mismatch is an exception the auditor must resolve, not a silent pass.
    var supportingDate = request.ShipReceiveDate ?? request.DocumentDate ?? request.TransactionDate;
    var isException = indicator == AuditCutOffDirections.BeforePeriodEnd
      ? supportingDate > request.PeriodEndDate
      : supportingDate <= request.PeriodEndDate;

    var existing = await db.AuditCutOffTestRecords.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == item.EngagementId && x.SelectionItemId == item.Id, ct);
    if (existing is not null && existing.RecordedByUserId != actor.UserId)
      return CommandResult<CutOffTestValue>.Fail(ErrorCodes.ScopeDenied,
        "Only the recording auditor can revise this cut-off test.");
    if (existing is null)
    {
      existing = new AuditCutOffTestRecord
      {
        Id = Guid.CreateVersion7(), FirmId = item.FirmId, ClientId = item.ClientId,
        EngagementId = item.EngagementId, SelectionId = item.SelectionId, SelectionItemId = item.Id,
        ProcedureId = Guid.Empty, RecordedByUserId = actor.UserId, RecordedAt = DateTimeOffset.UtcNow
      };
      db.AuditCutOffTestRecords.Add(existing);
    }
    else
    {
      existing.Revision++;
      existing.RecordedAt = DateTimeOffset.UtcNow;
    }
    existing.PeriodEndDate = request.PeriodEndDate;
    existing.TransactionDate = request.TransactionDate;
    existing.DocumentDate = request.DocumentDate;
    existing.ShipReceiveDate = request.ShipReceiveDate;
    existing.PeriodEndIndicator = indicator;
    existing.IsCutOffException = isException;
    existing.WorkPerformed = request.WorkPerformed.Trim();
    existing.EvidenceReferencesJson = System.Text.Json.JsonSerializer.Serialize(
      request.EvidenceReferences.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray());
    await db.SaveChangesAsync(ct);

    return CommandResult<CutOffTestValue>.Ok(new CutOffTestValue(
      existing.Id, existing.SelectionItemId, existing.PeriodEndIndicator, existing.IsCutOffException, existing.Revision));
  }

  /// <summary>Matches a sampled item to a post-year-end bank/GL entry. The state is derived
  /// from the matched amount: full, partial or unmatched; a partial or absent match requires
  /// an explanation and is never presented as settled.</summary>
  public static async Task<CommandResult<SubsequentMatchValue>> MatchSubsequentTransactionAsync(
    IClientAccountingDbContext db, ActorContext actor, SubsequentMatchRequest request,
    CancellationToken ct = default)
  {
    if (request.SelectionItemId == Guid.Empty || request.MatchedAmount < 0m ||
        string.IsNullOrWhiteSpace(request.SubsequentSourceReference) ||
        request.SubsequentSourceReference.Trim().Length > 200 ||
        string.IsNullOrWhiteSpace(request.EvidenceReference) || request.EvidenceReference.Trim().Length > 2000)
      return CommandResult<SubsequentMatchValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A subsequent match needs the item, a non-negative matched amount, the source reference and evidence.");
    var item = await db.AuditSelectionItems.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.SelectionItemId && x.FirmId == actor.FirmId, ct);
    if (item is null)
      return CommandResult<SubsequentMatchValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(item.FirmId, item.ClientId, item.EngagementId, PlanningRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<SubsequentMatchValue>.Fail(auth.ErrorCode!, auth.Message!);

    var absolute = item.SignedAmount < 0m ? -item.SignedAmount : item.SignedAmount;
    var matched = decimal.Round(request.MatchedAmount, 6, MidpointRounding.ToEven);
    if (matched > absolute)
      return CommandResult<SubsequentMatchValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "The matched amount cannot exceed the sampled item amount.");
    var state = matched == absolute ? AuditSubsequentMatchStates.Matched
      : matched == 0m ? AuditSubsequentMatchStates.Unmatched : AuditSubsequentMatchStates.PartiallyMatched;
    if (state != AuditSubsequentMatchStates.Matched && string.IsNullOrWhiteSpace(request.UnmatchedReason))
      return CommandResult<SubsequentMatchValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "An unmatched or partially matched item requires an explanation.");

    var existing = await db.AuditSubsequentMatchRecords.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == item.EngagementId && x.SelectionItemId == item.Id, ct);
    if (existing is not null && existing.RecordedByUserId != actor.UserId)
      return CommandResult<SubsequentMatchValue>.Fail(ErrorCodes.ScopeDenied,
        "Only the recording auditor can revise this subsequent match.");
    if (existing is null)
    {
      existing = new AuditSubsequentMatchRecord
      {
        Id = Guid.CreateVersion7(), FirmId = item.FirmId, ClientId = item.ClientId,
        EngagementId = item.EngagementId, SelectionId = item.SelectionId, SelectionItemId = item.Id,
        RecordedByUserId = actor.UserId, RecordedAt = DateTimeOffset.UtcNow
      };
      db.AuditSubsequentMatchRecords.Add(existing);
    }
    else
    {
      existing.Revision++;
      existing.RecordedAt = DateTimeOffset.UtcNow;
    }
    existing.ItemSignedAmount = item.SignedAmount;
    existing.MatchedAmount = matched;
    existing.Currency = item.Currency;
    existing.SubsequentSourceReference = request.SubsequentSourceReference.Trim();
    existing.SubsequentDate = request.SubsequentDate;
    existing.State = state;
    existing.UnmatchedReason = state == AuditSubsequentMatchStates.Matched ? null : request.UnmatchedReason!.Trim();
    existing.EvidenceReference = request.EvidenceReference.Trim();
    await db.SaveChangesAsync(ct);

    return CommandResult<SubsequentMatchValue>.Ok(new SubsequentMatchValue(
      existing.Id, existing.SelectionItemId, existing.State, existing.ItemSignedAmount,
      existing.MatchedAmount, existing.ItemSignedAmount < 0m
        ? -(existing.ItemSignedAmount + existing.MatchedAmount)
        : existing.ItemSignedAmount - existing.MatchedAmount,
      existing.Revision));
  }

  /// <summary>Paged sample-set view: every selected item with its test result, cut-off
  /// evidence and subsequent match, plus honest coverage counters. Read-only.</summary>
  public static async Task<CommandResult<SampleSetView>> GetSampleSetAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid selectionId,
    int page = 1, int pageSize = 100, CancellationToken ct = default)
  {
    if (selectionId == Guid.Empty || page < 1 || pageSize is < 1 or > 500)
      return CommandResult<SampleSetView>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "The sample-set page request is invalid.");
    var selection = await db.AuditSelections.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == selectionId && x.FirmId == actor.FirmId, ct);
    if (selection is null)
      return CommandResult<SampleSetView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(selection.FirmId, selection.ClientId, selection.EngagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<SampleSetView>.Fail(auth.ErrorCode!, auth.Message!);

    var selectedItems = db.AuditSelectionItems.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.SelectionId == selection.Id);
    var totalCount = await selectedItems.CountAsync(ct);
    var pageCount = Math.Max(1L, ((long)totalCount + pageSize - 1) / pageSize);
    if (page > pageCount)
      return CommandResult<SampleSetView>.Fail(ErrorCodes.Accounting.MappingInvalid, "The requested sample-set page is outside the available items.");
    var pageItems = await selectedItems
      .OrderBy(x => x.StableRowId).ThenBy(x => x.Id)
      .Skip((page - 1) * pageSize).Take(pageSize)
      .ToListAsync(ct);
    var itemIds = pageItems.Select(x => x.Id).ToArray();
    var latestTests = await db.AuditItemTests.AsNoTracking()
      .Where(test => test.FirmId == actor.FirmId && test.SelectionId == selection.Id &&
        test.Revision == db.AuditItemTests.Where(previous => previous.FirmId == actor.FirmId && previous.SelectionItemId == test.SelectionItemId)
          .Max(previous => previous.Revision))
      .Where(x => itemIds.Contains(x.SelectionItemId))
      .ToListAsync(ct);
    var latestTestIds = latestTests.Select(x => x.Id).ToArray();
    var testReviews = await db.AuditItemTestReviews.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && latestTestIds.Contains(x.AuditItemTestId))
      .OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    var reviewAuthorization = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(selection.FirmId, selection.ClientId, selection.EngagementId,
        ["Reviewer", "Manager", "Partner", "Administrator"], InternalOnly: true), ct);
    var canReviewTests = reviewAuthorization.Succeeded;
    var latestTestsForSelection = db.AuditItemTests.AsNoTracking()
      .Where(test => test.FirmId == actor.FirmId && test.SelectionId == selection.Id &&
        test.Revision == db.AuditItemTests.Where(previous => previous.FirmId == actor.FirmId && previous.SelectionItemId == test.SelectionItemId)
          .Max(previous => previous.Revision));
    var testedCount = await latestTestsForSelection.CountAsync(x => x.Result != AuditItemTestResults.Pending, ct);
    var exceptionCount = await latestTestsForSelection.CountAsync(x => x.Result == AuditItemTestResults.Exception || x.Result == AuditItemTestResults.Limitation, ct);
    var cutOffs = await db.AuditCutOffTestRecords.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && itemIds.Contains(x.SelectionItemId))
      .ToListAsync(ct);
    var matches = await db.AuditSubsequentMatchRecords.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && itemIds.Contains(x.SelectionItemId))
      .ToListAsync(ct);

    var views = pageItems.Select(item =>
    {
      var test = latestTests.FirstOrDefault(x => x.SelectionItemId == item.Id);
      var review = test is null ? null : testReviews.FirstOrDefault(x => x.AuditItemTestId == test.Id && x.TestRevision == test.Revision);
      IReadOnlyList<string> evidenceReferences = [];
      if (!string.IsNullOrWhiteSpace(test?.EvidenceReferencesJson))
      {
        try { evidenceReferences = JsonSerializer.Deserialize<string[]>(test.EvidenceReferencesJson) ?? []; }
        catch (JsonException) { evidenceReferences = []; }
      }
      var cutOff = cutOffs.FirstOrDefault(x => x.SelectionItemId == item.Id);
      var match = matches.FirstOrDefault(x => x.SelectionItemId == item.Id);
      return new SampleSetItemView(
        item.Id, item.StableRowId, item.SignedAmount, item.Currency, item.InclusionReason,
        test?.Result ?? AuditItemTestResults.Pending, test?.Revision ?? 0, test?.ExceptionAmount,
        cutOff?.Id, cutOff?.PeriodEndIndicator, cutOff?.IsCutOffException,
        match?.Id, match?.State, match?.MatchedAmount, test?.Id, test?.WorkPerformed, evidenceReferences,
        test?.ContradictoryEvidence, test?.FollowUp, review?.Decision == AuditItemTestReviewDecisions.Reviewed,
        review?.Decision, review?.Comment, test is not null && canReviewTests && test.TestedByUserId != actor.UserId && review is null);
    }).ToList();

    return CommandResult<SampleSetView>.Ok(new SampleSetView(
      selection.Id, selection.ProcedureId, selection.Method, selection.Rationale, selection.Status,
      selection.SelectedCount, selection.SelectedSignedTotal, selection.InputGeneration,
      testedCount, exceptionCount,
      await db.AuditCutOffTestRecords.AsNoTracking().CountAsync(x => x.FirmId == actor.FirmId && x.SelectionId == selection.Id, ct),
      await db.AuditSubsequentMatchRecords.AsNoTracking().CountAsync(x => x.FirmId == actor.FirmId && x.SelectionId == selection.Id &&
        x.State == AuditSubsequentMatchStates.Matched, ct),
      views,
      totalCount, page, pageSize));
  }
}
