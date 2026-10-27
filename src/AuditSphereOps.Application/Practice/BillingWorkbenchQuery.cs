using System.Data;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>Optional narrowing filters over firm invoices; none of them grants access.</summary>
public sealed record BillingWorkbenchFilter(Guid? ClientId = null, string? Status = null, string? Currency = null, string? Number = null);

/// <summary>
/// One firm invoice. Credited, allocated and outstanding use the invoice detail formula (issued credit notes; receipt
/// allocations less approved reversals; pending reversals excluded). <see cref="Kind"/> is derived from line sources
/// as in receivables ageing; SOURCE_REVIEW marks an ambiguous fee-milestone binding instead of guessing one.
/// </summary>
public sealed record BillingWorkbenchRow(Guid Id, string InvoiceNumber, Guid ClientId, string ClientName, string Kind,
  Guid? EngagementId, string? EngagementName, bool MultipleEngagements, string Currency, string Status, long Revision,
  DateTimeOffset CreatedAt, DateTimeOffset? PostedAt, DateTimeOffset? SentAt, decimal Total, decimal Credited,
  decimal Allocated, decimal Outstanding, DateOnly? ApprovedDueDate, bool PaymentTermsPending);

/// <summary>
/// Complete total for one currency and basis over the filtered cohort. Basis POSTED covers POSTED and SENT invoices and
/// carries settlement figures; UNPOSTED (draft, review, approved) and CANCELLED carry only invoice totals.
/// </summary>
public sealed record BillingWorkbenchTotal(string Currency, string Basis, int Count, decimal Total,
  decimal? Credited, decimal? Allocated, decimal? Outstanding);

public sealed record BillingWorkbenchPage(IReadOnlyList<BillingWorkbenchRow> Items, int Total, int Page, int PageSize,
  IReadOnlyList<BillingWorkbenchTotal> Totals, string AccessMode = "OPERATIONAL", string? AccessReason = null);

/// <summary>Read-only firm invoice discovery for firm-wide finance roles. Opening an invoice uses the existing detail route.</summary>
public static class BillingWorkbenchQuery
{
  public const string Posted = "POSTED";
  public const string Unposted = "UNPOSTED";
  public const string Cancelled = "CANCELLED";
  private static readonly string[] FinanceRoles = ["FinanceManager", "FinanceReviewer"];
  private static readonly string[] Statuses =
  [
    BillingStates.InvoiceDraft, BillingStates.InvoiceReviewRequired, BillingStates.InvoiceApproved,
    BillingStates.InvoicePosted, BillingStates.InvoiceSent, BillingStates.InvoiceCancelled
  ];

  public static async Task<CommandResult<BillingWorkbenchPage>> PageAsync(IAuditSphereDbContext db, ActorContext actor,
    BillingWorkbenchFilter? filter = null, int page = 0, int pageSize = 25, CancellationToken ct = default)
  {
    filter ??= new();
    var currency = filter.Currency?.Trim().ToUpperInvariant();
    var number = filter.Number?.Trim().ToLowerInvariant();
    if (page is < 0 or > 10000 || pageSize is < 1 or > 100 || filter.ClientId == Guid.Empty ||
        (filter.Status is not null && !Statuses.Contains(filter.Status)) ||
        (currency is not null && (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))) || number is { Length: 0 or > 50 })
      return CommandResult<BillingWorkbenchPage>.Fail("request.invalid", "Invalid invoice filter or page.");
    var request = new AuthorizationRequest(actor.FirmId, RequiredRoles: FinanceRoles, InternalOnly: true, RequireFirmWide: true);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!auth.Succeeded) auth = await AdministratorOversightReadAuthorization.AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<BillingWorkbenchPage>.Fail(auth.ErrorCode!, "Invoices unavailable.");

    var f = actor.FirmId;
    var cohort = from invoice in db.Invoices.AsNoTracking()
      join account in db.BillingAccounts.AsNoTracking()
        on new { invoice.FirmId, invoice.BillingAccountId } equals new { account.FirmId, BillingAccountId = account.Id }
      where invoice.FirmId == f
      select new
      {
        invoice.Id, invoice.InvoiceNumber, account.PracticeClientId, Currency = invoice.Currency ?? account.Currency,
        invoice.Status, invoice.Revision, invoice.CreatedAt, invoice.PostedAt, invoice.SentAt, invoice.Total
      };
    if (filter.ClientId is { } clientId) cohort = cohort.Where(x => x.PracticeClientId == clientId);
    if (filter.Status is { } status) cohort = cohort.Where(x => x.Status == status);
    if (currency is not null) cohort = cohort.Where(x => x.Currency == currency);
    if (number is not null) cohort = cohort.Where(x => x.InvoiceNumber.ToLower().Contains(number));
    var posted = cohort.Where(x => x.Status == BillingStates.InvoicePosted || x.Status == BillingStates.InvoiceSent);

    // Rows and every total come from one observation, so a summary never describes a different cohort.
    var snapshot = db.Database.CurrentTransaction is null
      ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct) : null;
    List<BillingWorkbenchRow> rows;
    List<BillingWorkbenchTotal> totals;
    int matching;
    try
    {
      var byStatus = await cohort.GroupBy(x => new { x.Currency, x.Status })
        .Select(g => new { g.Key.Currency, g.Key.Status, Count = g.Count(), Total = g.Sum(x => x.Total) }).ToListAsync(ct);
      var credited = await (from credit in db.CreditNotes.AsNoTracking()
        join invoice in posted on credit.InvoiceId equals invoice.Id
        where credit.FirmId == f && credit.Status == BillingStates.CreditIssued
        group credit by invoice.Currency into g
        select new { Currency = g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Currency, x => x.Amount, ct);
      var allocated = await (from allocation in db.ReceiptAllocations.AsNoTracking()
        join invoice in posted on allocation.InvoiceId equals invoice.Id
        where allocation.FirmId == f
        group allocation by invoice.Currency into g
        select new { Currency = g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Currency, x => x.Amount, ct);
      var reversed = await (from reversal in db.ReceiptAllocationReversals.AsNoTracking()
        join allocation in db.ReceiptAllocations.AsNoTracking()
          on new { reversal.FirmId, reversal.ReceiptAllocationId } equals new { allocation.FirmId, ReceiptAllocationId = allocation.Id }
        join invoice in posted on allocation.InvoiceId equals invoice.Id
        where reversal.FirmId == f && reversal.Status == ReceiptAllocationReversalStates.Approved
        group reversal by invoice.Currency into g
        select new { Currency = g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Currency, x => x.Amount, ct);
      matching = byStatus.Sum(x => x.Count);
      totals = byStatus
        .GroupBy(x => (x.Currency, Basis: x.Status is BillingStates.InvoicePosted or BillingStates.InvoiceSent ? Posted
          : x.Status == BillingStates.InvoiceCancelled ? Cancelled : Unposted))
        .OrderBy(g => g.Key.Currency, StringComparer.Ordinal).ThenBy(g => g.Key.Basis, StringComparer.Ordinal)
        .Select(g =>
        {
          var total = MoneyPolicy.Normalize(g.Sum(x => x.Total));
          if (g.Key.Basis != Posted) return new BillingWorkbenchTotal(g.Key.Currency, g.Key.Basis, g.Sum(x => x.Count), total, null, null, null);
          var credit = MoneyPolicy.Normalize(credited.GetValueOrDefault(g.Key.Currency));
          var applied = MoneyPolicy.Normalize(allocated.GetValueOrDefault(g.Key.Currency) - reversed.GetValueOrDefault(g.Key.Currency));
          return new BillingWorkbenchTotal(g.Key.Currency, Posted, g.Sum(x => x.Count), total, credit, applied,
            MoneyPolicy.Normalize(total - credit - applied));
        }).ToList();

      var pageRows = await cohort.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip(page * pageSize).Take(pageSize).ToListAsync(ct);
      var ids = pageRows.Select(x => x.Id).ToList();
      var clientIds = pageRows.Select(x => x.PracticeClientId).Distinct().ToList();
      var clients = await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == f && clientIds.Contains(x.Id))
        .ToDictionaryAsync(x => x.Id, x => x.LegalName, ct);
      var rowCredits = await db.CreditNotes.AsNoTracking()
        .Where(x => x.FirmId == f && ids.Contains(x.InvoiceId) && x.Status == BillingStates.CreditIssued)
        .GroupBy(x => x.InvoiceId).Select(g => new { g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Amount, ct);
      var rowAllocations = await db.ReceiptAllocations.AsNoTracking().Where(x => x.FirmId == f && ids.Contains(x.InvoiceId))
        .GroupBy(x => x.InvoiceId).Select(g => new { g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Amount, ct);
      var rowReversals = await (from reversal in db.ReceiptAllocationReversals.AsNoTracking()
        join allocation in db.ReceiptAllocations.AsNoTracking()
          on new { reversal.FirmId, reversal.ReceiptAllocationId } equals new { allocation.FirmId, ReceiptAllocationId = allocation.Id }
        where reversal.FirmId == f && ids.Contains(allocation.InvoiceId) && reversal.Status == ReceiptAllocationReversalStates.Approved
        group reversal by allocation.InvoiceId into g
        select new { g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Amount, ct);
      var terms = await db.InvoicePaymentTermsRevisions.AsNoTracking().Where(x => x.FirmId == f && ids.Contains(x.InvoiceId) &&
          (x.Status == InvoicePaymentTermsStates.Approved || x.Status == InvoicePaymentTermsStates.PendingReview))
        .Select(x => new { x.InvoiceId, x.Revision, x.Status, x.DueDate }).ToListAsync(ct);
      var milestones = await (from line in db.InvoiceLines.AsNoTracking()
        join milestone in db.FeeMilestones.AsNoTracking()
          on new { line.FirmId, MilestoneId = line.SourceId } equals new { milestone.FirmId, MilestoneId = (Guid?)milestone.Id }
        join agreement in db.EngagementFeeAgreements.AsNoTracking()
          on new { milestone.FirmId, milestone.AgreementId } equals new { agreement.FirmId, AgreementId = agreement.Id }
        where line.FirmId == f && ids.Contains(line.InvoiceId) && line.SourceKind == FeeAgreementService.MilestoneSourceKind
        select new { line.InvoiceId, milestone.Kind, agreement.EngagementId, agreement.PracticeClientId }).ToListAsync(ct);
      var time = await (from line in db.InvoiceLines.AsNoTracking()
        join entry in db.TimeEntries.AsNoTracking()
          on new { line.FirmId, EntryId = line.SourceId } equals new { entry.FirmId, EntryId = (Guid?)entry.Id }
        where line.FirmId == f && ids.Contains(line.InvoiceId) && line.SourceKind == "TIME"
        select new { line.InvoiceId, entry.EngagementId }).Distinct().ToListAsync(ct);
      var engagementIds = milestones.Select(x => x.EngagementId).Concat(time.Select(x => x.EngagementId))
        .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
      var engagements = await db.Engagements.AsNoTracking().Where(x => x.FirmId == f && engagementIds.Contains(x.Id))
        .Select(x => new { x.Id, Name = x.ServiceRoute + " · " + x.PeriodStart + "–" + x.PeriodEnd }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);

      rows = pageRows.Select(x =>
      {
        var bound = milestones.Where(m => m.InvoiceId == x.Id).ToList();
        var timeEngagements = time.Where(t => t.InvoiceId == x.Id).Select(t => t.EngagementId).Distinct().ToList();
        // A milestone bound twice, or to another client's agreement, is a data-quality condition, never a guessed link.
        var ambiguous = bound.Count > 1 || bound.Any(m => m.PracticeClientId != x.PracticeClientId);
        var kind = ambiguous ? "SOURCE_REVIEW"
          : bound.Count == 1 ? bound[0].Kind switch
          {
            FeeMilestoneKinds.Advance => "ADVANCE", FeeMilestoneKinds.Balance => "FINAL", _ => "SOURCE_REVIEW"
          }
          : timeEngagements.Count > 0 ? "TIME" : "OTHER";
        var engagementId = ambiguous ? null : bound.Count == 1 ? bound[0].EngagementId
          : bound.Count == 0 && timeEngagements.Count == 1 ? timeEngagements[0] : null;
        var credit = MoneyPolicy.Normalize(rowCredits.GetValueOrDefault(x.Id));
        var applied = MoneyPolicy.Normalize(rowAllocations.GetValueOrDefault(x.Id) - rowReversals.GetValueOrDefault(x.Id));
        var invoiceTerms = terms.Where(t => t.InvoiceId == x.Id).ToList();
        return new BillingWorkbenchRow(x.Id, x.InvoiceNumber, x.PracticeClientId, clients.GetValueOrDefault(x.PracticeClientId, "Client"),
          kind, engagementId, engagementId is { } e ? engagements.GetValueOrDefault(e) : null,
          bound.Count == 0 && timeEngagements.Count > 1, x.Currency, x.Status, x.Revision, x.CreatedAt, x.PostedAt, x.SentAt,
          x.Total, credit, applied, MoneyPolicy.Normalize(x.Total - credit - applied),
          invoiceTerms.Where(t => t.Status == InvoicePaymentTermsStates.Approved).OrderByDescending(t => t.Revision)
            .Select(t => (DateOnly?)t.DueDate).FirstOrDefault(),
          invoiceTerms.Any(t => t.Status == InvoicePaymentTermsStates.PendingReview));
      }).ToList();
      if (snapshot is not null) await snapshot.CommitAsync(ct);
    }
    finally
    {
      if (snapshot is not null) await snapshot.DisposeAsync();
    }

    auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    var operational = auth.Succeeded;
    if (!auth.Succeeded) auth = await AdministratorOversightReadAuthorization.AuthorizeAsync(db, actor, ct);
    return auth.Succeeded ? CommandResult<BillingWorkbenchPage>.Ok(new(rows, matching, page, pageSize, totals,
        operational ? "OPERATIONAL" : AdministratorOversightReadAuthorization.ReadOnly,
        operational ? null : AdministratorOversightReadAuthorization.Reason))
      : CommandResult<BillingWorkbenchPage>.Fail(auth.ErrorCode!, "Invoices unavailable.");
  }
}
