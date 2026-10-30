using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record FeeSettlementListItem(Guid AgreementId, Guid ClientId, string ClientName, string Fee,
  string Currency, DateTimeOffset CreatedAt);

public sealed record FeeSettlementPage(int Page, bool HasMore, IReadOnlyList<FeeSettlementListItem> Rows);

/// <summary>Finance-only reads for existing fee agreements. Commercial proposal authorization remains separate.</summary>
public static class FeeSettlementWorkspaceQuery
{
  private const int PageSize = 20;
  private const int MaximumPage = 10_000;
  private const int MaximumClientScopes = 1_000;
  private static readonly string[] FinanceRoles = ["FinanceManager", "FinanceReviewer"];

  private sealed record InvoiceSource(Invoice Invoice, string AccountCurrency);
  private sealed record FinanceScope(bool FirmWide, Guid[] ClientIds);

  private static string Exact(decimal value) => value.ToString(CultureInfo.InvariantCulture);

  private static IQueryable<RoleGrant> EffectiveFinanceGrants(IAuditSphereDbContext db, ActorContext actor,
    DateTimeOffset now) => db.RoleGrants.AsNoTracking().Where(grant => grant.FirmId == actor.FirmId &&
      grant.UserId == actor.UserId && grant.RevokedAt == null &&
      (grant.ExpiresAt == null || grant.ExpiresAt > now) && FinanceRoles.Contains(grant.Role) &&
      grant.EngagementId == null);

  private static AuthorizationRequest FinanceRequest(Guid firmId, Guid? clientId = null, bool requireFirmWide = false) =>
    new(firmId, ClientId: clientId, RequiredRoles: FinanceRoles, InternalOnly: true, RequireFirmWide: requireFirmWide);

  private static async Task<CommandResult<FinanceScope>> ResolveFinanceScopeAsync(IAuditSphereDbContext db,
    ActorContext actor, CancellationToken ct)
  {
    var firmWide = await AuthorizationDecision.AuthorizeAsync(db, actor,
      FinanceRequest(actor.FirmId, requireFirmWide: true), ct);
    if (firmWide.Succeeded)
      return CommandResult<FinanceScope>.Ok(new(true, []));

    var clientIds = await EffectiveFinanceGrants(db, actor, DateTimeOffset.UtcNow)
      .Where(grant => grant.ClientId.HasValue)
      .Select(grant => grant.ClientId!.Value)
      .Distinct()
      .OrderBy(clientId => clientId)
      .Take(MaximumClientScopes + 1)
      .ToListAsync(ct);
    if (clientIds.Count > MaximumClientScopes)
      return CommandResult<FinanceScope>.Fail("scope.limit", "Finance client scope exceeds the supported workspace limit.");

    var authorizedClientIds = new List<Guid>(clientIds.Count);
    foreach (var clientId in clientIds)
    {
      var authorization = await AuthorizationDecision.AuthorizeAsync(db, actor,
        FinanceRequest(actor.FirmId, clientId), ct);
      if (authorization.Succeeded) authorizedClientIds.Add(clientId);
    }
    return authorizedClientIds.Count == 0
      ? CommandResult<FinanceScope>.Fail(ErrorCodes.ScopeDenied, "Fee agreement unavailable.")
      : CommandResult<FinanceScope>.Ok(new(false, authorizedClientIds.ToArray()));
  }

  internal static async Task<CommandResult> AuthorizeWorkspaceReadAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, FinanceRequest(actor.FirmId), ct);
    if (!auth.Succeeded) return auth;
    var grants = EffectiveFinanceGrants(db, actor, DateTimeOffset.UtcNow);
    if (await grants.AnyAsync(g => g.ClientId == null, ct))
      return await AuthorizationDecision.AuthorizeAsync(db, actor, FinanceRequest(actor.FirmId, requireFirmWide: true), ct);
    var clients = grants.Where(g => g.ClientId != null).Select(g => g.ClientId!.Value).Distinct();
    if (await clients.Take(MaximumClientScopes + 1).CountAsync(ct) > MaximumClientScopes)
      return CommandResult.Fail("scope.limit", "Finance client scope exceeds the supported workspace limit.");
    var exists = await grants.AnyAsync(g => g.ClientId != null &&
      db.PracticeClients.Any(c => c.FirmId == actor.FirmId && c.Id == g.ClientId), ct);
    return exists ? CommandResult.Ok() : CommandResult.Fail(ErrorCodes.ScopeDenied, "Fee agreement unavailable.");
  }

  private static CommandResult<T> Denied<T>() =>
    CommandResult<T>.Fail(ErrorCodes.ScopeDenied, "Fee agreement unavailable.");

  private static CommandResult<T> ReconciliationRequired<T>() =>
    CommandResult<T>.Fail(ErrorCodes.GateBlocked, "Fee milestones require reconciliation.");

  private static async Task<InvoiceSource?> ResolveSourceInvoiceAsync(IAuditSphereDbContext db,
    EngagementFeeAgreement agreement, FeeMilestone milestone, BillingSourceAllocation allocation,
    Guid? expectedInvoiceId, CancellationToken ct)
  {
    if (allocation.FirmId != agreement.FirmId || allocation.SourceKind != FeeAgreementService.MilestoneSourceKind ||
        allocation.SourceId != milestone.Id || allocation.SourceRevision != 1 || allocation.Quantity != 1m ||
        allocation.Amount != milestone.Amount)
      return null;

    var line = await db.InvoiceLines.AsNoTracking().SingleOrDefaultAsync(item =>
      item.FirmId == agreement.FirmId && item.Id == allocation.InvoiceLineId, ct);
    if (line is null || line.FirmId != agreement.FirmId ||
        (expectedInvoiceId is { } invoiceId && line.InvoiceId != invoiceId) ||
        line.SourceKind != FeeAgreementService.MilestoneSourceKind || line.SourceId != milestone.Id ||
        line.SourceRevision != 1 || line.Quantity != 1m || line.LineTotal != milestone.Amount)
      return null;

    var source = await (from invoiceRow in db.Invoices.AsNoTracking()
      join account in db.BillingAccounts.AsNoTracking()
        on new { invoiceRow.FirmId, invoiceRow.BillingAccountId }
        equals new { account.FirmId, BillingAccountId = account.Id }
      where invoiceRow.FirmId == agreement.FirmId && invoiceRow.Id == line.InvoiceId &&
        account.PracticeClientId == agreement.PracticeClientId
      select new InvoiceSource(invoiceRow, account.Currency))
      .SingleOrDefaultAsync(ct);
    if (source is null || source.Invoice.BillingAccountId == Guid.Empty ||
        source.Invoice.Total != milestone.Amount ||
        !string.Equals(source.Invoice.Currency, agreement.Currency, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(source.AccountCurrency, agreement.Currency, StringComparison.OrdinalIgnoreCase))
      return null;
    return source;
  }

  public static async Task<CommandResult<FeeSettlementPage>> ListAsync(IAuditSphereDbContext db,
    ActorContext actor, int page, CancellationToken ct = default)
  {
    if (page is < 0 or > MaximumPage)
      return CommandResult<FeeSettlementPage>.Fail("request.invalid", "Invalid fee agreement page.");
    var scopeResult = await ResolveFinanceScopeAsync(db, actor, ct);
    if (!scopeResult.Succeeded) return CommandResult<FeeSettlementPage>.Fail(scopeResult.ErrorCode!, scopeResult.Message!);
    var scope = scopeResult.Value!;

    var grants = EffectiveFinanceGrants(db, actor, DateTimeOffset.UtcNow);
    var candidates = await (from agreement in db.EngagementFeeAgreements.AsNoTracking()
      join client in db.PracticeClients.AsNoTracking()
        on new { agreement.FirmId, ClientId = agreement.PracticeClientId }
        equals new { client.FirmId, ClientId = client.Id }
      where agreement.FirmId == actor.FirmId && (scope.FirmWide || scope.ClientIds.Contains(agreement.PracticeClientId)) &&
        grants.Any(grant => grant.ClientId == null || grant.ClientId == agreement.PracticeClientId)
      orderby agreement.CreatedAt descending, agreement.Id descending
      select new
      {
        AgreementId = agreement.Id,
        ClientId = client.Id,
        ClientName = client.LegalName,
        agreement.AgreedFee,
        agreement.Currency,
        agreement.CreatedAt
      })
      .Skip(page * PageSize)
      .Take(PageSize + 1)
      .ToListAsync(ct);

    if (page == MaximumPage && candidates.Count > PageSize)
      return CommandResult<FeeSettlementPage>.Fail("page.limit", "The fee settlement page limit was reached; narrow the authorized client scope.");
    var hasMore = candidates.Count > PageSize;
    var pageRows = candidates.Take(PageSize).ToArray();
    var rows = pageRows.Select(row => new FeeSettlementListItem(row.AgreementId, row.ClientId,
      row.ClientName, Exact(row.AgreedFee), row.Currency, row.CreatedAt)).ToArray();

    if (pageRows.Length == 0)
    {
      var currentScope = await ResolveFinanceScopeAsync(db, actor, ct);
      if (!currentScope.Succeeded)
        return CommandResult<FeeSettlementPage>.Fail(currentScope.ErrorCode!, currentScope.Message!);
    }
    else
    {
      foreach (var clientId in pageRows.Select(row => row.ClientId).Distinct())
      {
        var authorization = await AuthorizationDecision.AuthorizeAsync(db, actor,
          FinanceRequest(actor.FirmId, clientId), ct);
        if (!authorization.Succeeded) return Denied<FeeSettlementPage>();
      }
    }

    return CommandResult<FeeSettlementPage>.Ok(new(page, hasMore, rows));
  }

  public static async Task<CommandResult<FeeAgreementWorkspace>> GetAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid agreementId, bool automaticDraftingEnabled, CancellationToken ct = default)
  {
    if (agreementId == Guid.Empty) return Denied<FeeAgreementWorkspace>();
    var scopeResult = await ResolveFinanceScopeAsync(db, actor, ct);
    if (!scopeResult.Succeeded) return CommandResult<FeeAgreementWorkspace>.Fail(scopeResult.ErrorCode!, scopeResult.Message!);
    var scope = scopeResult.Value!;

    var grants = EffectiveFinanceGrants(db, actor, DateTimeOffset.UtcNow);
    var agreement = await db.EngagementFeeAgreements.AsNoTracking().SingleOrDefaultAsync(item =>
      item.FirmId == actor.FirmId && item.Id == agreementId &&
      db.PracticeClients.Any(client => client.FirmId == actor.FirmId && client.Id == item.PracticeClientId) &&
      (scope.FirmWide || scope.ClientIds.Contains(item.PracticeClientId)) &&
      grants.Any(grant => grant.ClientId == null || grant.ClientId == item.PracticeClientId), ct);
    if (agreement is null) return Denied<FeeAgreementWorkspace>();
    var authorizationBeforeRead = await AuthorizationDecision.AuthorizeAsync(db, actor,
      FinanceRequest(actor.FirmId, agreement.PracticeClientId), ct);
    if (!authorizationBeforeRead.Succeeded) return Denied<FeeAgreementWorkspace>();
    var clientName = await db.PracticeClients.AsNoTracking()
      .Where(client => client.FirmId == actor.FirmId && client.Id == agreement.PracticeClientId)
      .Select(client => client.LegalName).SingleAsync(ct);

    if (agreement.EngagementId is { } engagementId &&
        !await db.Engagements.AsNoTracking().AnyAsync(engagement => engagement.FirmId == actor.FirmId &&
          engagement.Id == engagementId && engagement.PracticeClientId == agreement.PracticeClientId, ct))
      return ReconciliationRequired<FeeAgreementWorkspace>();

    var milestones = await db.FeeMilestones.AsNoTracking()
      .Where(milestone => milestone.FirmId == actor.FirmId && milestone.AgreementId == agreement.Id)
      .OrderBy(milestone => milestone.Kind)
      .ThenBy(milestone => milestone.Id)
      .Take(3)
      .ToListAsync(ct);
    if (milestones.Count != 2 ||
        milestones.Count(milestone => milestone.Kind == FeeMilestoneKinds.Advance) != 1 ||
        milestones.Count(milestone => milestone.Kind == FeeMilestoneKinds.Balance) != 1 ||
        string.IsNullOrWhiteSpace(agreement.Currency))
      return ReconciliationRequired<FeeAgreementWorkspace>();

    var views = new List<FeeMilestoneView>(2);
    var orphanAdvanceInvoice = false;
    foreach (var milestone in milestones.OrderBy(milestone =>
      milestone.Kind == FeeMilestoneKinds.Advance ? 0 : 1))
    {
      if (milestone.FirmId != agreement.FirmId || milestone.AgreementId != agreement.Id || milestone.Amount <= 0m)
        return ReconciliationRequired<FeeAgreementWorkspace>();

      var sourceAllocations = await db.BillingSourceAllocations.AsNoTracking()
        .Where(allocation => allocation.FirmId == actor.FirmId && allocation.SourceId == milestone.Id)
        .OrderBy(allocation => allocation.SourceRevision)
        .ThenBy(allocation => allocation.Id)
        .Take(2)
        .ToListAsync(ct);

      Invoice? invoice = null;
      if (milestone.InvoiceId is null)
      {
        if (sourceAllocations.Count > 1)
          return ReconciliationRequired<FeeAgreementWorkspace>();
        if (sourceAllocations.Count == 1)
        {
          if (milestone.Kind != FeeMilestoneKinds.Advance || milestone.State != FeeMilestoneStates.Planned)
            return ReconciliationRequired<FeeAgreementWorkspace>();
          var source = await ResolveSourceInvoiceAsync(db, agreement, milestone, sourceAllocations[0],
            expectedInvoiceId: null, ct: ct);
          if (source is null || source.Invoice.Status is not (BillingStates.InvoiceDraft or
              BillingStates.InvoiceReviewRequired or BillingStates.InvoiceApproved or BillingStates.InvoicePosted or BillingStates.InvoiceSent))
            return ReconciliationRequired<FeeAgreementWorkspace>();
          invoice = source.Invoice;
          orphanAdvanceInvoice = true;
        }
        else if (milestone.State != FeeMilestoneStates.Planned)
        {
          return ReconciliationRequired<FeeAgreementWorkspace>();
        }
      }
      else
      {
        if (sourceAllocations.Count != 1 || sourceAllocations[0].SourceRevision != 1 ||
            milestone.State is not (FeeMilestoneStates.Invoiced or FeeMilestoneStates.Paid))
          return ReconciliationRequired<FeeAgreementWorkspace>();
        var source = await ResolveSourceInvoiceAsync(db, agreement, milestone, sourceAllocations[0],
          milestone.InvoiceId, ct: ct);
        if (source is null)
          return ReconciliationRequired<FeeAgreementWorkspace>();
        invoice = source.Invoice;
      }

      var allocated = invoice is null ? 0m :
        await BillingService.GetNetAppliedToInvoiceAsync(db, actor.FirmId, invoice.Id, ct);
      decimal outstanding;
      if (invoice is null)
      {
        outstanding = milestone.Amount;
      }
      else if (milestone.Kind == FeeMilestoneKinds.Advance)
      {
        outstanding = MoneyPolicy.Normalize(Math.Max(0m, milestone.Amount - allocated));
      }
      else
      {
        var credited = await db.CreditNotes.AsNoTracking().Where(note => note.FirmId == actor.FirmId &&
          note.InvoiceId == invoice.Id && note.Status == BillingStates.CreditIssued)
          .SumAsync(note => (decimal?)note.Amount, ct) ?? 0m;
        outstanding = MoneyPolicy.Normalize(invoice.Total - allocated - credited);
      }

      if (milestone.State == FeeMilestoneStates.Paid &&
          !await FeeAgreementService.IsMilestoneCurrentlyPaidAsync(db, milestone, ct))
        milestone.State = FeeMilestoneStates.Invoiced;
      views.Add(new(milestone, invoice?.InvoiceNumber, invoice?.Status, milestone.Amount - outstanding, outstanding));
    }

    var released = agreement.EngagementId is { } releaseEngagementId &&
      await db.Releases.AsNoTracking().AnyAsync(release => release.FirmId == actor.FirmId &&
        release.ClientId == agreement.PracticeClientId && release.EngagementId == releaseEngagementId, ct);
    var feeView = new FeeAgreementView(agreement, views, released);
    var preparation = await FeeAgreementWorkspaceQuery.AdvancePreparationAsync(
      db, actor.FirmId, feeView, automaticDraftingEnabled, ct);
    if (orphanAdvanceInvoice)
      preparation = new(AdvanceInvoicePreparationStates.Blocked,
        "A retained advance invoice awaits milestone reconciliation; prepare or recover the advance invoice to restore its exact source link. Its existing review and posting state is preserved.");

    var authorizationAtReturn = await AuthorizationDecision.AuthorizeAsync(db, actor,
      FinanceRequest(actor.FirmId, agreement.PracticeClientId), ct);
    if (!authorizationAtReturn.Succeeded)
      return Denied<FeeAgreementWorkspace>();

    return CommandResult<FeeAgreementWorkspace>.Ok(new(
      agreement.ProposalId,
      agreement.Id,
      agreement.PracticeClientId,
      agreement.EngagementId,
      Exact(agreement.AgreedFee),
      agreement.Currency,
      Exact(agreement.AdvancePercent),
      CanCreate: false,
      CanFinance: true,
      ReleaseRecorded: released,
      CreationBlockers: [],
      Milestones: views.Select(view => new FeeMilestoneItem(
        view.Milestone.Id,
        view.Milestone.Kind,
        Exact(view.Milestone.Amount),
        view.Milestone.State,
        view.Milestone.InvoiceId,
        view.InvoiceNumber,
        view.InvoiceStatus,
        Exact(view.Allocated),
        Exact(view.Outstanding),
        view.Milestone.PaidAt)).ToArray(),
      Engagements: [],
      AdvancePreparation: preparation,
      ClientName: clientName));
  }
}
