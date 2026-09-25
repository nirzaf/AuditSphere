using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

// T059 completion: batched confirmation creation and the confirmation summary view. The
// single-case lifecycle (create, approve, dispatch, respond, alternative procedure,
// review, close) already exists in AuditFieldworkService.
public sealed record ConfirmationBatchRequest(
  Guid EngagementId, Guid? ProcedureId, string AreaCode, string Currency, DateOnly ConfirmationDate,
  IReadOnlyList<ConfirmationBatchItem> Cases);

public sealed record ConfirmationBatchItem(
  string SourceRecordId, decimal BookedAmount, string Respondent, string ContactValidationSource);

public sealed record ConfirmationBatchValue(
  string AreaCode, int CreatedCount, decimal TotalBookedAmount,
  IReadOnlyList<ConfirmationValue> Cases);

public sealed record ConfirmationSummaryRow(
  Guid ConfirmationCaseId, string AreaCode, string SourceRecordId, string Respondent,
  decimal BookedAmount, string Currency, DateOnly ConfirmationDate, string Status,
  string? DispatchReference, string? LatestDecision, decimal? ConfirmedAmount,
  bool HasAlternativeProcedure, string? AlternativeConclusion);

public sealed record ConfirmationSummaryView(
  Guid EngagementId, int TotalCases, decimal TotalBookedAmount, string Currency,
  int Draft, int Approved, int Dispatched, int ResponseReceived, int NoResponse,
  int AlternativeRequired, int Closed, int RecomputedDifferenceCases,
  IReadOnlyList<ConfirmationSummaryRow> Items, int TotalCount, int Page, int PageSize);

public static class AuditConfirmationBatchService
{
  private static readonly string[] PlanningRoles =
    ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static readonly string[] ReadRoles =
    ["Auditor", "Reviewer", "Manager", "Partner", "Administrator", "AccountingPreparer", "AccountingReviewer"];

  /// <summary>Creates every confirmation case for one area in a single transaction. All
  /// cases share the area, confirmation date and validated-contact source; a single
  /// invalid row rejects the whole batch so a partial register is never produced.</summary>
  public static async Task<CommandResult<ConfirmationBatchValue>> CreateConfirmationBatchAsync(
    IAuditSphereDbContext db, ActorContext actor, ConfirmationBatchRequest request,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.AreaCode))
      return CommandResult<ConfirmationBatchValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A confirmation batch requires an area code.");
    if (request.Cases is null || request.Cases.Count == 0)
      return CommandResult<ConfirmationBatchValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A confirmation batch requires at least one case.");
    if (request.Cases.Select(x => x.SourceRecordId.Trim()).Distinct(StringComparer.Ordinal).Count() != request.Cases.Count)
      return CommandResult<ConfirmationBatchValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "Batch case source records must be unique.");
    if (request.Cases.Any(x => string.IsNullOrWhiteSpace(x.SourceRecordId) ||
        string.IsNullOrWhiteSpace(x.Respondent) || string.IsNullOrWhiteSpace(x.ContactValidationSource)))
      return CommandResult<ConfirmationBatchValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "Every batch case needs a source record, respondent and validated contact source.");
    var currency = request.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
    if (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
      return CommandResult<ConfirmationBatchValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A confirmation batch requires an explicit three-letter currency.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, EngagementId: request.EngagementId, RequiredRoles: PlanningRoles,
        InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded)
      return CommandResult<ConfirmationBatchValue>.Fail(auth.ErrorCode!, auth.Message!);
    var engagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == request.EngagementId, ct);
    if (request.ProcedureId is { } procedureId)
    {
      var applicable = await db.AuditProcedures.AsNoTracking().AnyAsync(x =>
        x.Id == procedureId && x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId &&
        x.EngagementId == request.EngagementId && x.ApplicabilityStatus == AuditApplicabilityStatuses.Applicable, ct);
      if (!applicable)
        return CommandResult<ConfirmationBatchValue>.Fail(ErrorCodes.GateBlocked,
          "The linked procedure is not applicable to this engagement.");
    }
    // One currency per batch: the summary reports a single area total. When a reported
    // package exists it must agree with the requested currency, so the register cannot
    // silently mix presentation currencies.
    var packageCurrency = await db.FinancialPackages.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId &&
        x.EngagementId == request.EngagementId)
      .OrderByDescending(x => x.CreatedAt)
      .Select(x => x.Currency).FirstOrDefaultAsync(ct);
    if (!string.IsNullOrWhiteSpace(packageCurrency) &&
        !string.Equals(packageCurrency, currency, StringComparison.Ordinal))
      return CommandResult<ConfirmationBatchValue>.Fail(ErrorCodes.GateBlocked,
        $"The batch currency {currency} does not match the engagement reporting currency {packageCurrency}.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var generation = await db.ClientSafetyStates.AsNoTracking()
      .Where(x => x.Id == engagement.PracticeClientId && x.FirmId == actor.FirmId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct) ?? 1;
    var area = request.AreaCode.Trim().ToUpperInvariant();
    var created = new List<ConfirmationValue>(request.Cases.Count);
    foreach (var item in request.Cases)
    {
      var confirmation = new AuditConfirmationCase
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = engagement.PracticeClientId,
        EngagementId = request.EngagementId, ProcedureId = request.ProcedureId, AreaCode = area,
        SourceRecordId = item.SourceRecordId.Trim(), BookedAmount = item.BookedAmount,
        Currency = currency, ConfirmationDate = request.ConfirmationDate,
        Respondent = item.Respondent.Trim(), ContactValidationSource = item.ContactValidationSource.Trim(),
        Status = AuditConfirmationStatuses.Draft, InputGeneration = generation,
        CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
      };
      db.AuditConfirmationCases.Add(confirmation);
      created.Add(new ConfirmationValue(confirmation.Id, confirmation.Status));
    }
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return CommandResult<ConfirmationBatchValue>.Ok(new ConfirmationBatchValue(
      area, created.Count, request.Cases.Sum(x => x.BookedAmount), created));
  }

  /// <summary>Paged confirmation register for one engagement: each case with its dispatch
  /// reference, latest response decision, confirmed amount and alternative-procedure
  /// conclusion, plus honest status counters. Read-only.</summary>
  public static async Task<CommandResult<ConfirmationSummaryView>> GetConfirmationSummaryAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, string? areaCode = null,
    int page = 1, int pageSize = 100, CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty || page < 1 || pageSize is < 1 or > 500)
      return CommandResult<ConfirmationSummaryView>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "The confirmation summary page request is invalid.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null)
      return CommandResult<ConfirmationSummaryView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<ConfirmationSummaryView>.Fail(auth.ErrorCode!, auth.Message!);

    var query = db.AuditConfirmationCases.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId);
    if (!string.IsNullOrWhiteSpace(areaCode))
      query = query.Where(x => x.AreaCode == areaCode.Trim().ToUpperInvariant());
    var cases = await query
      .OrderBy(x => x.AreaCode).ThenBy(x => x.SourceRecordId).ThenBy(x => x.Id)
      .ToListAsync(ct);
    var caseIds = cases.Select(x => x.Id).ToArray();
    var responses = await db.AuditConfirmationResponses.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && caseIds.Contains(x.ConfirmationCaseId))
      .ToListAsync(ct);
    var alternatives = await db.AuditAlternativeProcedures.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && caseIds.Contains(x.ConfirmationCaseId))
      .ToListAsync(ct);

    var rows = cases.Select(x =>
    {
      var response = responses.Where(r => r.ConfirmationCaseId == x.Id)
        .OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id).FirstOrDefault();
      var alternative = alternatives.Where(a => a.ConfirmationCaseId == x.Id)
        .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).FirstOrDefault();
      return new ConfirmationSummaryRow(
        x.Id, x.AreaCode, x.SourceRecordId, x.Respondent, x.BookedAmount, x.Currency,
        x.ConfirmationDate, x.Status, x.DispatchReference,
        response?.Decision, response?.ConfirmedAmount,
        alternative is not null, alternative?.Conclusion);
    }).ToList();

    // A case whose response figure differs from the booked amount needs follow-up, not a pass.
    var recomputedDifferences = rows.Count(x =>
      x.ConfirmedAmount.HasValue && x.ConfirmedAmount.Value != x.BookedAmount);
    return CommandResult<ConfirmationSummaryView>.Ok(new ConfirmationSummaryView(
      engagementId, rows.Count, rows.Sum(x => x.BookedAmount), rows.Select(x => x.Currency).Distinct().Count() == 1
        ? rows[0].Currency : string.Empty,
      rows.Count(x => x.Status == AuditConfirmationStatuses.Draft),
      rows.Count(x => x.Status == AuditConfirmationStatuses.Approved),
      rows.Count(x => x.Status == AuditConfirmationStatuses.Dispatched),
      rows.Count(x => x.Status == AuditConfirmationStatuses.ResponseReceived),
      rows.Count(x => x.Status == AuditConfirmationStatuses.NoResponse),
      rows.Count(x => x.Status == AuditConfirmationStatuses.AlternativeRequired),
      rows.Count(x => x.Status == AuditConfirmationStatuses.Closed),
      recomputedDifferences,
      rows.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
      rows.Count, page, pageSize));
  }
}
