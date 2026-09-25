using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ConsolidationService
{
  public static async Task<CommandResult<Guid>> AddIntercompanyMatchAsync(
    IClientAccountingDbContext db, ActorContext actor, IntercompanyMatchRequest request,
    CancellationToken ct = default)
  {
    var sellerAmount = MoneyPolicy.Normalize(request.SellerAmount);
    var buyerAmount = MoneyPolicy.Normalize(request.BuyerAmount);
    var matchedAmount = MoneyPolicy.Normalize(request.MatchedAmount);
    var accountNature = request.AccountNature.Trim().ToUpperInvariant();
    var sellerTaxonomy = string.IsNullOrWhiteSpace(request.SellerTaxonomyCode) ? accountNature : request.SellerTaxonomyCode;
    var buyerTaxonomy = string.IsNullOrWhiteSpace(request.BuyerTaxonomyCode) ? accountNature : request.BuyerTaxonomyCode;
    var matchMode = request.MatchMode.Trim().ToUpperInvariant();
    var matchGroupReference = request.MatchGroupReference.Trim();
    var difference = MoneyPolicy.Normalize(sellerAmount + buyerAmount);
    if (request.SellerClientId == request.BuyerClientId || string.IsNullOrWhiteSpace(request.AccountNature) ||
        string.IsNullOrWhiteSpace(sellerTaxonomy) || string.IsNullOrWhiteSpace(buyerTaxonomy) ||
        string.IsNullOrWhiteSpace(request.EvidenceReference) || request.MatchedAmount < 0m ||
        matchMode is not (IntercompanyMatchModes.OneToOne or IntercompanyMatchModes.Grouped) ||
        (matchMode == IntercompanyMatchModes.Grouped && string.IsNullOrWhiteSpace(matchGroupReference)) ||
        request.SellerAmount != sellerAmount || request.BuyerAmount != buyerAmount || request.MatchedAmount != matchedAmount ||
        (difference != 0m && string.IsNullOrWhiteSpace(request.DifferenceReason)))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "An intercompany match needs precise signed amounts, both taxonomy sides, evidence and a difference reason when unmatched.");
    if (!request.OutsidePerimeterReview && !ConsolidationEliminationKinds.IsIntercompany(accountNature))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "An automatic intercompany elimination needs an approved receivable/payable, revenue/expense, dividend or investment/equity nature.");
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ScopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, scope.GroupId, scope.GroupRevision, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");
    var currency = request.Currency.Trim().ToUpperInvariant();
    if (currency != scope.ReportingCurrency || matchedAmount > Math.Min(Math.Abs(sellerAmount), Math.Abs(buyerAmount)))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The match must fit both signed counterparty balances in the scope currency.");
    if (!await db.PracticeClients.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Id == request.SellerClientId, ct) ||
        !await db.PracticeClients.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Id == request.BuyerClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Both counterparties must be legal entities in the firm scope.");
    var memberIds = await db.ClientGroupMemberships.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
      x.Status == AccountingWorkflowStates.Approved && x.EffectiveTo == null).Select(x => x.ClientId).ToHashSetAsync(ct);
    if (request.OutsidePerimeterReview)
    {
      if (memberIds.Contains(request.SellerClientId) == memberIds.Contains(request.BuyerClientId))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An outside-perimeter review must pair one in-perimeter entity with one related party outside the perimeter.");
    }
    else
    {
      var components = await db.ConsolidationComponents.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id &&
        (x.ClientId == request.SellerClientId || x.ClientId == request.BuyerClientId)).ToListAsync(ct);
      if (components.Count != 2 || components.Select(x => x.ClientId).Distinct().Count() != 2 ||
          components.Any(x => x.Status != AccountingWorkflowStates.Approved))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Both counterparties must have approved component packages before matching.");
    }
    var match = new IntercompanyMatch
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      SellerClientId = request.SellerClientId, BuyerClientId = request.BuyerClientId, AccountNature = accountNature,
      MatchMode = matchMode, MatchGroupReference = matchGroupReference, OutsidePerimeterReview = request.OutsidePerimeterReview,
      SellerTaxonomyCode = sellerTaxonomy.Trim().ToUpperInvariant(), BuyerTaxonomyCode = buyerTaxonomy.Trim().ToUpperInvariant(),
      PeriodCode = request.PeriodCode.Trim(), Currency = currency, TransactionReference = request.TransactionReference.Trim(),
      SellerAmount = sellerAmount, BuyerAmount = buyerAmount, MatchedAmount = matchedAmount, Difference = difference,
      DifferenceReason = request.DifferenceReason.Trim(), EvidenceReference = request.EvidenceReference.Trim(),
      Status = AccountingWorkflowStates.Submitted, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.IntercompanyMatches.Add(match);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(match.Id);
  }

  public static async Task<CommandResult> ApproveIntercompanyMatchAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid matchId,
    CancellationToken ct = default)
  {
    var match = await db.IntercompanyMatches.SingleOrDefaultAsync(x => x.Id == matchId && x.FirmId == actor.FirmId, ct);
    if (match is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, match.GroupId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    var matchScope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == match.ScopeVersionId && x.GroupId == match.GroupId, ct);
    if (matchScope is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, matchScope.GroupId, matchScope.GroupRevision, ct))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");
    if (match.Status != AccountingWorkflowStates.Submitted || match.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected, "Only a submitted match prepared by another user can be approved.");
    if (match.MatchMode == IntercompanyMatchModes.Grouped)
    {
      var group = await db.IntercompanyMatches.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.ScopeVersionId == match.ScopeVersionId && x.MatchMode == IntercompanyMatchModes.Grouped &&
        x.MatchGroupReference == match.MatchGroupReference && x.AccountNature == match.AccountNature &&
        x.PeriodCode == match.PeriodCode && x.Currency == match.Currency).ToListAsync(ct);
      if (group.Count < 2)
        return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected, "A grouped match needs at least two reviewed source rows.");
      var sellerTotal = MoneyPolicy.Normalize(group.Sum(x => Math.Abs(x.SellerAmount)));
      var buyerTotal = MoneyPolicy.Normalize(group.Sum(x => Math.Abs(x.BuyerAmount)));
      var matchedTotal = MoneyPolicy.Normalize(group.Sum(x => x.MatchedAmount));
      if (matchedTotal > Math.Min(sellerTotal, buyerTotal))
        return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected, "Grouped matched amounts exceed the reviewed counterparty totals.");
    }
    match.Status = AccountingWorkflowStates.Approved;
    match.ReviewedByUserId = actor.UserId;
    match.ReviewedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }
}
