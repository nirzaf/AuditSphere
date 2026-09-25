using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class AccountingAnalysisService
{
  public static async Task<CommandResult<Guid>> CreateAnalyticalReviewAsync(
    IClientAccountingDbContext db, ActorContext actor, AnalyticalReviewRequest request,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Area) || string.IsNullOrWhiteSpace(request.Measure) ||
        string.IsNullOrWhiteSpace(request.DenominatorBasis) || string.IsNullOrWhiteSpace(request.FormulaVersion))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "Analytical review needs an explicit measure, denominator and formula version.");
    var currency = (string.IsNullOrWhiteSpace(request.Currency) ? AccountingDefaults.DefaultCurrency : request.Currency).Trim().ToUpperInvariant();
    if (currency.Length != 3 || !currency.All(char.IsLetter) ||
        MoneyPolicy.Normalize(request.CurrentAmount) != request.CurrentAmount ||
        MoneyPolicy.Normalize(request.PriorAmount) != request.PriorAmount ||
        (request.BudgetAmount.HasValue && MoneyPolicy.Normalize(request.BudgetAmount.Value) != request.BudgetAmount.Value))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "Analytical review currency and amounts must use the reporting currency and six-decimal precision.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.Id == request.PeriodId, ct);
    if (period is null || !string.Equals(period.Currency, currency, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Analytical review currency must match the selected client reporting period.");
    if (request.ComparisonPeriodId is { } comparisonPeriodId &&
        !await db.ClientReportingPeriods.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.Id == comparisonPeriodId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The analytical comparison period is outside the client scope.");
    var clientState = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.ClientId && x.FirmId == actor.FirmId, ct);
    if (clientState is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Client accounting safety state is unavailable.");
    decimal? ratio = request.PriorAmount == 0m ? null : MoneyPolicy.Normalize((request.CurrentAmount - request.PriorAmount) / Math.Abs(request.PriorAmount));
    var movementFlags = new List<string>();
    if (request.CurrentAmount < 0m || request.PriorAmount < 0m)
      movementFlags.Add("NEGATIVE_BALANCE");
    if (!string.IsNullOrWhiteSpace(request.SeasonalityExplanation))
      movementFlags.Add("SEASONAL_MOVEMENT");
    if (movementFlags.Count == 0)
      movementFlags.Add("NONE");
    var inputSnapshot = JsonSerializer.Serialize(new
    {
      request.ClientId, request.EngagementId, request.PeriodId, request.ComparisonPeriodId,
      Area = request.Area.Trim().ToUpperInvariant(), Measure = request.Measure.Trim(),
      CurrentAmount = MoneyPolicy.Normalize(request.CurrentAmount), PriorAmount = MoneyPolicy.Normalize(request.PriorAmount),
      BudgetAmount = request.BudgetAmount.HasValue ? MoneyPolicy.Normalize(request.BudgetAmount.Value) : (decimal?)null,
      Currency = currency, DenominatorBasis = request.DenominatorBasis.Trim(), FormulaVersion = request.FormulaVersion.Trim(),
      MovementFlags = movementFlags, SeasonalityExplanation = request.SeasonalityExplanation.Trim(), Explanation = request.Explanation.Trim()
    });
    var review = new AnalyticalReview
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
      PeriodId = request.PeriodId, InputGeneration = clientState.InputGeneration, ComparisonPeriodId = request.ComparisonPeriodId,
      Area = request.Area.Trim().ToUpperInvariant(),
      Measure = request.Measure.Trim(), CurrentAmount = MoneyPolicy.Normalize(request.CurrentAmount), PriorAmount = MoneyPolicy.Normalize(request.PriorAmount),
      BudgetAmount = request.BudgetAmount.HasValue ? MoneyPolicy.Normalize(request.BudgetAmount.Value) : null, Ratio = ratio,
      Currency = currency, DenominatorBasis = request.DenominatorBasis.Trim(), FormulaVersion = request.FormulaVersion.Trim(),
      MovementFlags = string.Join(',', movementFlags), SeasonalityExplanation = request.SeasonalityExplanation.Trim(),
      InputSnapshotJson = inputSnapshot, InputHash = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(inputSnapshot)), Explanation = request.Explanation.Trim(),
      Status = ratio.HasValue ? AccountingWorkflowStates.Draft : "INSUFFICIENT_DATA", CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AnalyticalReviews.Add(review);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(review.Id);
  }

  public static async Task<CommandResult<IReadOnlyList<AnalyticalReviewAggregateSummary>>> GetAnalyticalReviewAggregateAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid? groupId = null, Guid? periodId = null,
    CancellationToken ct = default)
  {
    if (groupId == Guid.Empty || periodId == Guid.Empty)
      return CommandResult<IReadOnlyList<AnalyticalReviewAggregateSummary>>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The aggregate scope is invalid.");

    if (groupId is { } requestedGroupId)
    {
      var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == actor.UserId && x.FirmId == actor.FirmId && !x.Disabled, ct);
      if (user is null || user.SessionEpoch != actor.SessionEpoch ||
          user.UserKind.Equals("Client", StringComparison.OrdinalIgnoreCase) ||
          actor.Roles.Contains("ClientUser", StringComparer.OrdinalIgnoreCase))
        return CommandResult<IReadOnlyList<AnalyticalReviewAggregateSummary>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      if (!await db.ClientGroups.AsNoTracking().AnyAsync(x =>
          x.Id == requestedGroupId && x.FirmId == actor.FirmId && x.Status == AccountingWorkflowStates.Active, ct))
        return CommandResult<IReadOnlyList<AnalyticalReviewAggregateSummary>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      if (!await db.GroupAccessGrants.AsNoTracking().AnyAsync(x =>
          x.FirmId == actor.FirmId && x.GroupId == requestedGroupId && x.UserId == actor.UserId &&
          x.RevokedAt == null && PreparerRoles.Contains(x.Role), ct))
        return CommandResult<IReadOnlyList<AnalyticalReviewAggregateSummary>>.Fail(ErrorCodes.ScopeDenied, "Explicit group access is required.");
    }
    else
    {
      var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(actor.FirmId, actor.ClientId, actor.EngagementId, PreparerRoles, InternalOnly: true), ct);
      if (!auth.Succeeded)
        return CommandResult<IReadOnlyList<AnalyticalReviewAggregateSummary>>.Fail(auth.ErrorCode!, auth.Message!);
    }

    var requestedPeriod = periodId is { } requestedPeriodId
      ? await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == requestedPeriodId && x.FirmId == actor.FirmId, ct)
      : null;
    if (periodId.HasValue && requestedPeriod is null)
      return CommandResult<IReadOnlyList<AnalyticalReviewAggregateSummary>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    Guid? engagementClientId = null;
    if (actor.EngagementId is { } engagementId)
    {
      var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == engagementId && x.FirmId == actor.FirmId, ct);
      if (engagement is null)
        return CommandResult<IReadOnlyList<AnalyticalReviewAggregateSummary>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      engagementClientId = engagement.PracticeClientId;
    }
    if (requestedPeriod is not null &&
        ((actor.ClientId.HasValue && requestedPeriod.ClientId != actor.ClientId) ||
         (engagementClientId.HasValue && requestedPeriod.ClientId != engagementClientId)))
      return CommandResult<IReadOnlyList<AnalyticalReviewAggregateSummary>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    IQueryable<AnalyticalReview> reviews = db.AnalyticalReviews.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId);
    if (periodId is { } filteredPeriodId)
      reviews = reviews.Where(x => x.PeriodId == filteredPeriodId);
    if (actor.EngagementId is { } filteredEngagementId)
      reviews = reviews.Where(x => x.EngagementId == filteredEngagementId);

    if (groupId is { } filteredGroupId)
    {
      reviews = (from review in reviews
                 join membership in db.ClientGroupMemberships.AsNoTracking()
                   on new { review.FirmId, review.ClientId } equals new { membership.FirmId, membership.ClientId }
                 join period in db.ClientReportingPeriods.AsNoTracking()
                   on new { review.FirmId, review.ClientId, review.PeriodId }
                   equals new { period.FirmId, period.ClientId, PeriodId = period.Id }
                 where membership.GroupId == filteredGroupId && membership.Status == AccountingWorkflowStates.Approved &&
                   membership.EffectiveFrom <= period.EndDate &&
                   (membership.EffectiveTo == null || membership.EffectiveTo >= period.StartDate) &&
                   (!actor.ClientId.HasValue || membership.ClientId == actor.ClientId)
                 select review).Distinct();
    }
    else
    {
      Guid[]? clientIds = null;
      if (actor.ClientId is { } actorClientId)
        clientIds = [actorClientId];
      else if (engagementClientId is { } scopedEngagementClientId)
        clientIds = [scopedEngagementClientId];
      else
      {
        var grants = await db.RoleGrants.AsNoTracking().Where(x =>
          x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.RevokedAt == null && PreparerRoles.Contains(x.Role))
          .Select(x => new { x.ClientId, x.EngagementId }).ToListAsync(ct);
        if (!grants.Any(x => x.ClientId is null && x.EngagementId is null))
        {
          var engagementIds = grants.Where(x => x.EngagementId.HasValue).Select(x => x.EngagementId!.Value).ToArray();
          var engagementClients = engagementIds.Length == 0
            ? []
            : await db.Engagements.AsNoTracking().Where(x => x.FirmId == actor.FirmId && engagementIds.Contains(x.Id))
              .Select(x => x.PracticeClientId).ToArrayAsync(ct);
          clientIds = grants.Where(x => x.ClientId.HasValue).Select(x => x.ClientId!.Value)
            .Concat(engagementClients).Distinct().ToArray();
          if (clientIds.Length == 0)
            return CommandResult<IReadOnlyList<AnalyticalReviewAggregateSummary>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        }
      }
      if (clientIds is not null)
        reviews = reviews.Where(x => clientIds.Contains(x.ClientId));
    }

    var rows = await reviews.GroupBy(x => new { x.PeriodId, x.Currency })
      .Select(x => new
      {
        x.Key.PeriodId,
        x.Key.Currency,
        ReviewCount = x.Count(),
        ClientCount = x.Select(row => row.ClientId).Distinct().Count(),
        CurrentTotal = x.Sum(row => row.CurrentAmount),
        PriorTotal = x.Sum(row => row.PriorAmount)
      })
      .OrderBy(x => x.PeriodId).ThenBy(x => x.Currency)
      .ToListAsync(ct);
    return CommandResult<IReadOnlyList<AnalyticalReviewAggregateSummary>>.Ok(rows.Select(x =>
      new AnalyticalReviewAggregateSummary(groupId, x.PeriodId, x.Currency, x.ReviewCount, x.ClientCount,
        MoneyPolicy.Normalize(x.CurrentTotal), MoneyPolicy.Normalize(x.PriorTotal))).ToArray());
  }
}
