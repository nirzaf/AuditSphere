using System.Data;
using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record FirmUnbilledTimeRequest(DateOnly From, DateOnly To,
  Guid? ClientId = null, Guid? EngagementId = null, int Page = 0, int PageSize = 50);

public sealed record FirmUnbilledTimeRow(Guid TimeEntryId, long Revision,
  Guid? ClientId, string? ClientName, Guid? EngagementId, string? EngagementLabel,
  DateOnly WorkDate, int DurationMinutes, string? Currency, decimal? StandardValue,
  string Bucket, string FeeBasis, Guid? InvoiceId, string? InvoiceNumber, string? Reason);

public sealed record FirmUnbilledTimeSubtotal(string? Currency, string Bucket,
  string FeeBasis, string EntryCount, string Minutes, decimal? StandardValue,
  string UnavailableValueCount);

public sealed record FirmUnbilledTimeReport(DateOnly From, DateOnly To,
  DateTimeOffset ObservedAtUtc, string TotalEntries, int Page, int PageSize,
  bool HasMore, IReadOnlyList<FirmUnbilledTimeRow> Rows,
  IReadOnlyList<FirmUnbilledTimeSubtotal> Subtotals, string BasisNotice);

public static class FirmUnbilledTimeBuckets
{
  public const string Unallocated = "UNALLOCATED";
  public const string Reserved = "RESERVED";
  public const string Invoiced = "INVOICED";
  public const string ReviewRequired = "REVIEW_REQUIRED";
}

public static class FirmUnbilledTimeFeeBases
{
  public const string FixedFeeReference = "FIXED_FEE_REFERENCE";
  public const string TimeSourceReference = "TIME_SOURCE_REFERENCE";
  public const string Undetermined = "UNDETERMINED";
}

/// <summary>
/// Current, read-only observation of approved billable time and immutable billing-source reservations.
/// Standard charge-out value is not recognized WIP, revenue, collectible debt, or billing authorization.
/// </summary>
public static class FirmUnbilledTimeQuery
{
  private const int MaxEntries = 10_000;
  private static readonly string[] FinanceRoles = ["FinanceManager", "FinanceReviewer"];
  private const string InvalidQueryCode = "wip.query-invalid";
  private const string InvalidQueryMessage = "Choose a valid work-date range of at most 366 days and a supported page.";
  private const string SourceIntegrityCode = "wip.source-integrity";
  private const string SourceIntegrityMessage = "The time billing-source history cannot be classified safely. Review the linked finance records.";
  private const string LimitMessage = "More than 10,000 approved time entries match. Narrow the work-date or client filter.";
  private const string BasisNoticeText = "Current approved-time and billing-link observations for the selected work dates. Values are standard charge-out value, not recognized WIP, revenue, collectible debt or billing authorization. Fixed-fee time and corrections require their own finance review.";

  private sealed record VerifiedAllocation(
    BillingSourceAllocation Allocation, InvoiceLine Line, Invoice Invoice, BillingAccount Account);

  private sealed record ClientFact(Guid Id, string Name);
  private sealed record EngagementFact(Guid Id, Guid ClientId, string Label);
  private sealed record FeeAgreementFact(Guid? EngagementId, Guid ClientId);

  public static async Task<CommandResult<FirmUnbilledTimeReport>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, FirmUnbilledTimeRequest request,
    CancellationToken ct = default)
  {
    if (!IsValid(request))
      return CommandResult<FirmUnbilledTimeReport>.Fail(InvalidQueryCode, InvalidQueryMessage);

    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded)
      return CommandResult<FirmUnbilledTimeReport>.Fail(auth.ErrorCode!, auth.Message!);

    var reportRows = new List<FirmUnbilledTimeRow>();
    List<FirmUnbilledTimeSubtotal> subtotals;
    int totalEntries;
    await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      if (request.ClientId is { } clientFilter &&
          !await db.PracticeClients.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Id == clientFilter, ct))
        return ScopeDenied();

      Guid? selectedEngagementClientId = null;
      if (request.EngagementId is { } engagementFilter)
      {
        selectedEngagementClientId = await db.Engagements.AsNoTracking()
          .Where(x => x.FirmId == actor.FirmId && x.Id == engagementFilter)
          .Select(x => (Guid?)x.PracticeClientId).SingleOrDefaultAsync(ct);
        if (selectedEngagementClientId is null ||
            request.ClientId is { } selectedClient && selectedEngagementClientId != selectedClient)
          return ScopeDenied();
      }

      var entries = await db.TimeEntries.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.Status == PracticeTimeStates.TimeApproved &&
          x.BillableClassification == PracticeTimeStates.Billable && x.WorkDate >= request.From && x.WorkDate <= request.To &&
          (!request.ClientId.HasValue || x.ClientId == request.ClientId) &&
          (!request.EngagementId.HasValue || x.EngagementId == request.EngagementId))
        .OrderBy(x => x.WorkDate)
        .ThenBy(x => x.ClientId == null)
        .ThenBy(x => x.ClientId)
        .ThenBy(x => x.EngagementId == null)
        .ThenBy(x => x.EngagementId)
        .ThenBy(x => x.Id)
        .Take(MaxEntries + 1)
        .ToListAsync(ct);
      if (entries.Count > MaxEntries)
        return CommandResult<FirmUnbilledTimeReport>.Fail("wip.report-limit", LimitMessage);

      var entryIds = entries.Select(x => x.Id).ToArray();
      var clientIds = entries.Where(x => x.ClientId.HasValue).Select(x => x.ClientId!.Value).Distinct().ToArray();
      var clients = clientIds.Length == 0
        ? new Dictionary<Guid, ClientFact>()
        : await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == actor.FirmId && clientIds.Contains(x.Id))
          .Select(x => new ClientFact(x.Id, x.LegalName)).ToDictionaryAsync(x => x.Id, ct);

      var engagementIds = entries.Where(x => x.EngagementId.HasValue).Select(x => x.EngagementId!.Value).Distinct().ToArray();
      var engagements = engagementIds.Length == 0
        ? new Dictionary<Guid, EngagementFact>()
        : await db.Engagements.AsNoTracking().Where(x => x.FirmId == actor.FirmId && engagementIds.Contains(x.Id))
          .Select(x => new EngagementFact(x.Id, x.PracticeClientId,
            x.ServiceRoute + " · " + x.PeriodStart + "–" + x.PeriodEnd))
          .ToDictionaryAsync(x => x.Id, ct);

      var allocations = entryIds.Length == 0
        ? []
        : await db.BillingSourceAllocations.AsNoTracking()
          .Where(x => x.FirmId == actor.FirmId && x.SourceKind == "TIME" && entryIds.Contains(x.SourceId))
          .OrderBy(x => x.SourceId).ThenBy(x => x.SourceRevision).ThenBy(x => x.Id)
          .Take(MaxEntries + 1).ToListAsync(ct);
      if (allocations.Count > MaxEntries)
        return SourceIntegrityFailure();

      var lineIds = allocations.Select(x => x.InvoiceLineId).Distinct().ToArray();
      var lines = lineIds.Length == 0
        ? []
        : await db.InvoiceLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId && lineIds.Contains(x.Id)).ToListAsync(ct);
      if (lines.Count != lineIds.Length)
        return SourceIntegrityFailure();
      var linesById = lines.ToDictionary(x => x.Id);

      var invoiceIds = lines.Select(x => x.InvoiceId).Distinct().ToArray();
      var invoices = invoiceIds.Length == 0
        ? []
        : await db.Invoices.AsNoTracking().Where(x => x.FirmId == actor.FirmId && invoiceIds.Contains(x.Id)).ToListAsync(ct);
      if (invoices.Count != invoiceIds.Length)
        return SourceIntegrityFailure();
      var invoicesById = invoices.ToDictionary(x => x.Id);

      var accountIds = invoices.Select(x => x.BillingAccountId).Distinct().ToArray();
      var accounts = accountIds.Length == 0
        ? []
        : await db.BillingAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && accountIds.Contains(x.Id)).ToListAsync(ct);
      if (accounts.Count != accountIds.Length)
        return SourceIntegrityFailure();
      var accountsById = accounts.ToDictionary(x => x.Id);

      var entriesById = entries.ToDictionary(x => x.Id);
      var verifiedLinks = new List<VerifiedAllocation>(allocations.Count);
      foreach (var allocation in allocations)
      {
        if (!linesById.TryGetValue(allocation.InvoiceLineId, out var line) ||
            !invoicesById.TryGetValue(line.InvoiceId, out var invoice) ||
            !accountsById.TryGetValue(invoice.BillingAccountId, out var account) ||
            !entriesById.TryGetValue(allocation.SourceId, out var entry) ||
            line.SourceKind != "TIME" || line.SourceId != allocation.SourceId ||
            entry.ClientId.HasValue && account.PracticeClientId != entry.ClientId.Value)
          return SourceIntegrityFailure();
        verifiedLinks.Add(new VerifiedAllocation(allocation, line, invoice, account));
      }
      var linksByEntry = verifiedLinks.GroupBy(x => x.Allocation.SourceId)
        .ToDictionary(x => x.Key, x => x.ToArray());

      var agreementFacts = engagementIds.Length == 0
        ? []
        : await db.EngagementFeeAgreements.AsNoTracking()
          .Where(x => x.FirmId == actor.FirmId && x.EngagementId.HasValue && engagementIds.Contains(x.EngagementId.Value))
          .Select(x => new FeeAgreementFact(x.EngagementId, x.PracticeClientId)).ToListAsync(ct);
      var agreementsByEngagement = agreementFacts.GroupBy(x => x.EngagementId!.Value)
        .ToDictionary(x => x.Key, x => x.ToArray());

      foreach (var entry in entries)
      {
        var validClient = entry.ClientId is { } clientId && clients.ContainsKey(clientId);
        var validEngagement = entry.EngagementId is { } engagementId && engagements.TryGetValue(engagementId, out var engagement) &&
          validClient && engagement.ClientId == entry.ClientId;
        var client = validClient ? clients[entry.ClientId!.Value] : null;
        var engagementLabel = validEngagement ? engagements[entry.EngagementId!.Value].Label : null;
        var value = StandardValue(entry);
        var feeBasis = FeeBasis(entry, validEngagement, agreementsByEngagement);
        var links = linksByEntry.GetValueOrDefault(entry.Id, []);
        var verifiedInvoice = validClient ? SingleVerifiedInvoice(links) : null;
        string bucket;
        string? reason = null;

        if (!validClient)
        {
          bucket = FirmUnbilledTimeBuckets.ReviewRequired;
          reason = "Client attribution is unavailable.";
        }
        else if (value is null)
        {
          bucket = FirmUnbilledTimeBuckets.ReviewRequired;
          reason = "Captured rate or currency is unavailable.";
        }
        else if (links.Length == 0 && entry.SupersedesId.HasValue)
        {
          bucket = FirmUnbilledTimeBuckets.ReviewRequired;
          reason = "Corrected time requires review of its prior billing links.";
        }
        else if (links.Length == 0)
        {
          bucket = FirmUnbilledTimeBuckets.Unallocated;
        }
        else if (links.Length != 1 || !MatchesSource(links[0], entry, value.Value))
        {
          bucket = FirmUnbilledTimeBuckets.ReviewRequired;
          reason = "Billing-source attribution requires finance review.";
        }
        else
        {
          var invoice = links[0].Invoice;
          switch (invoice.Status)
          {
            case BillingStates.InvoiceDraft:
            case BillingStates.InvoiceReviewRequired:
            case BillingStates.InvoiceApproved:
              bucket = FirmUnbilledTimeBuckets.Reserved;
              break;
            case BillingStates.InvoicePosted:
            case BillingStates.InvoiceSent:
              bucket = FirmUnbilledTimeBuckets.Invoiced;
              break;
            case BillingStates.InvoiceCancelled:
              bucket = FirmUnbilledTimeBuckets.ReviewRequired;
              reason = "A cancelled invoice still retains this source reservation.";
              break;
            default:
              bucket = FirmUnbilledTimeBuckets.ReviewRequired;
              reason = "Billing-source attribution requires finance review.";
              break;
          }
        }

        reportRows.Add(new FirmUnbilledTimeRow(entry.Id, entry.Revision,
          client?.Id, client?.Name, validEngagement ? entry.EngagementId : null, engagementLabel,
          entry.WorkDate, entry.DurationMinutes, entry.Currency, value, bucket, feeBasis,
          verifiedInvoice?.Id, verifiedInvoice?.InvoiceNumber, reason));
      }

      totalEntries = reportRows.Count;
      subtotals = BuildSubtotals(reportRows);
      await transaction.CommitAsync(ct);
    }

    auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded)
      return CommandResult<FirmUnbilledTimeReport>.Fail(auth.ErrorCode!, auth.Message!);

    var orderedRows = reportRows.OrderBy(x => x.WorkDate)
      .ThenBy(x => x.ClientId is null).ThenBy(x => x.ClientId)
      .ThenBy(x => x.EngagementId is null).ThenBy(x => x.EngagementId)
      .ThenBy(x => x.TimeEntryId).ToArray();
    var skip = request.Page * request.PageSize;
    var pageRows = orderedRows.Skip(skip).Take(request.PageSize).ToArray();
    var report = new FirmUnbilledTimeReport(request.From, request.To, DateTimeOffset.UtcNow,
      totalEntries.ToString(CultureInfo.InvariantCulture), request.Page, request.PageSize,
      skip + pageRows.Length < totalEntries, pageRows, subtotals, BasisNoticeText);
    return CommandResult<FirmUnbilledTimeReport>.Ok(report);
  }

  private static bool IsValid(FirmUnbilledTimeRequest request) => request is not null &&
    request.From <= request.To && request.To.DayNumber - request.From.DayNumber + 1 <= 366 &&
    request.Page is >= 0 and <= 10_000 && request.PageSize is 10 or 25 or 50 or 100 &&
    request.ClientId != Guid.Empty && request.EngagementId != Guid.Empty;

  private static decimal? StandardValue(TimeEntry entry)
  {
    if (entry.RatePerHour is not { } rate || entry.Currency is not { } currency ||
        !IsCurrency(currency) || rate < 0m || HasMoreThanSixDecimals(rate) || MoneyPolicy.Normalize(rate) != rate)
      return null;
    try
    {
      return MoneyPolicy.Normalize(checked((decimal)entry.DurationMinutes * rate / 60m));
    }
    catch (OverflowException)
    {
      return null;
    }
  }

  private static bool IsCurrency(string currency) => currency.Length == 3 &&
    currency.All(c => c is >= 'A' and <= 'Z');

  private static bool HasMoreThanSixDecimals(decimal value) =>
    ((decimal.GetBits(value)[3] >> 16) & 0x7f) > MoneyPolicy.MaxScale;

  private static bool MatchesSource(VerifiedAllocation link, TimeEntry entry, decimal value)
  {
    var quantity = entry.DurationMinutes / 60m;
    return link.Allocation.SourceRevision == entry.Revision && link.Line.SourceRevision == entry.Revision &&
      link.Allocation.Quantity == quantity && link.Line.Quantity == quantity &&
      link.Allocation.Amount == value && link.Line.LineTotal == value &&
      entry.Currency is { } currency && link.Invoice.Currency is { } invoiceCurrency &&
      string.Equals(invoiceCurrency, currency, StringComparison.Ordinal) &&
      string.Equals(link.Account.Currency, currency, StringComparison.Ordinal);
  }

  private static string FeeBasis(TimeEntry entry, bool validEngagement,
    IReadOnlyDictionary<Guid, FeeAgreementFact[]> agreementsByEngagement)
  {
    if (!entry.EngagementId.HasValue) return FirmUnbilledTimeFeeBases.TimeSourceReference;
    if (!validEngagement) return FirmUnbilledTimeFeeBases.Undetermined;
    if (!agreementsByEngagement.TryGetValue(entry.EngagementId.Value, out var agreements) || agreements.Length == 0)
      return FirmUnbilledTimeFeeBases.TimeSourceReference;
    return agreements.Length == 1 && agreements[0].ClientId == entry.ClientId
      ? FirmUnbilledTimeFeeBases.FixedFeeReference
      : FirmUnbilledTimeFeeBases.Undetermined;
  }

  private static Invoice? SingleVerifiedInvoice(IReadOnlyList<VerifiedAllocation> links)
  {
    var invoices = links.Select(x => x.Invoice).DistinctBy(x => x.Id).Take(2).ToArray();
    return invoices.Length == 1 ? invoices[0] : null;
  }

  private static List<FirmUnbilledTimeSubtotal> BuildSubtotals(IReadOnlyCollection<FirmUnbilledTimeRow> rows) =>
    rows.GroupBy(x => (x.Currency, x.Bucket, x.FeeBasis))
      .OrderBy(x => x.Key.Currency is null).ThenBy(x => x.Key.Currency, StringComparer.Ordinal)
      .ThenBy(x => x.Key.Bucket, StringComparer.Ordinal).ThenBy(x => x.Key.FeeBasis, StringComparer.Ordinal)
      .Select(group =>
      {
        var values = group.ToArray();
        var sum = values.Any(x => x.StandardValue is null) ? null : TrySum(values.Select(x => x.StandardValue!.Value));
        return new FirmUnbilledTimeSubtotal(group.Key.Currency, group.Key.Bucket, group.Key.FeeBasis,
          values.Length.ToString(CultureInfo.InvariantCulture),
          values.Sum(x => (long)x.DurationMinutes).ToString(CultureInfo.InvariantCulture), sum,
          values.Count(x => x.StandardValue is null).ToString(CultureInfo.InvariantCulture));
      }).ToList();

  private static decimal? TrySum(IEnumerable<decimal> values)
  {
    try
    {
      var sum = 0m;
      foreach (var value in values) sum = checked(sum + value);
      return sum;
    }
    catch (OverflowException)
    {
      return null;
    }
  }

  private static Task<CommandResult> AuthorizeAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: FinanceRoles,
        InternalOnly: true, RequireFirmWide: true), ct);

  private static CommandResult<FirmUnbilledTimeReport> ScopeDenied() =>
    CommandResult<FirmUnbilledTimeReport>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

  private static CommandResult<FirmUnbilledTimeReport> SourceIntegrityFailure() =>
    CommandResult<FirmUnbilledTimeReport>.Fail(SourceIntegrityCode, SourceIntegrityMessage);
}
