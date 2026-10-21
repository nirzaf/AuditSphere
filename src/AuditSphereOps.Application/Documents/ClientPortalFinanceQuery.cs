using System.Data;
using System.Globalization;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

public sealed record PortalClientMilestoneInvoice(
  Guid InvoiceId,
  string Kind,
  string InvoiceNumber,
  string Status,
  string Amount,
  string Currency,
  DateTimeOffset? PostedAt,
  DateTimeOffset? SentAt,
  string DeliveryStatus,
  string CollectionStatus,
  string CashApplied,
  string CreditApplied,
  string OutstandingAmount,
  DateOnly? DueDate,
  string PaymentTermsStatus,
  string AgingStatus);

public sealed record PortalClientReceipt(
  Guid ReceiptId,
  Guid? DocumentId,
  string Kind,
  string Amount,
  string AppliedAmount,
  string Currency,
  string Reference,
  DateTimeOffset ReceivedAt,
  string? DownloadUrl);

public sealed record PortalClientFinanceAgreement(
  Guid AgreementId,
  Guid? EngagementId,
  string AgreedFee,
  string Currency,
  string AdvancePercent,
  string OutstandingBalance,
  IReadOnlyList<PortalClientMilestoneInvoice> Invoices,
  IReadOnlyList<PortalClientReceipt> Receipts);

public sealed record PortalClientFinanceView(
  DateOnly AsOfDateUtc,
  IReadOnlyList<PortalClientFinanceAgreement> Agreements,
  int PageSize = 50,
  bool HasMore = false,
  string? NextCursor = null);

public static class ClientPortalFinanceQuery
{
  private const int DefaultPageSize = 50;
  private const int MaximumPageSize = 50;
  private const int MaximumCursorLength = 256;

  private sealed record FinanceGrant(Guid Id, Guid ClientId, Guid? EngagementId,
    DateTimeOffset GrantedAt, DateTimeOffset? ExpiresAt);
  private sealed record FinanceIdentity(long SessionEpoch, FinanceGrant[] Grants);
  private sealed record FinanceCursor(DateTimeOffset CreatedAt, Guid AgreementId);

  private static string Exact(decimal value) => value.ToString(CultureInfo.InvariantCulture);

  public static async Task<CommandResult<PortalClientFinanceView>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
    => await GetAsync(db, actor, DefaultPageSize, null, ct);

  public static async Task<CommandResult<PortalClientFinanceView>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, int pageSize, string? cursor, CancellationToken ct = default)
  {
    if (pageSize is < 1 or > MaximumPageSize || cursor is { Length: > MaximumCursorLength }) return Invalid();
    FinanceCursor? position = null;
    if (cursor is not null && !TryDecodeCursor(cursor, out position)) return Invalid();
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<PortalClientFinanceView>.Fail(ErrorCodes.GateBlocked,
        "Client finance requires an independent read snapshot.");

    FinanceIdentity initialIdentity;
    DateTimeOffset now;
    PortalClientFinanceView view;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      now = DateTimeOffset.UtcNow;
      var asOfDateUtc = DateOnly.FromDateTime(now.UtcDateTime);
      var loadedIdentity = await LoadIdentityAsync(db, actor, now, ct);
      if (loadedIdentity is null || loadedIdentity.SessionEpoch != actor.SessionEpoch)
        return CommandResult<PortalClientFinanceView>.Fail(ErrorCodes.ScopeDenied, "Finance documents unavailable.");
      initialIdentity = loadedIdentity;

      // Client grants carry an exact scope: a client-wide grant (no engagement) covers the client's
      // agreements, while an engagement-scoped grant only covers that engagement's agreement. The
      // query never widens an engagement-only grant to sibling engagements (STE-REM-03).
      var clientIds = initialIdentity.Grants.Select(x => x.ClientId).Distinct().ToArray();

      // Apply the exact current grant predicate before the page bound so sibling agreements
      // cannot consume this page or hide an older agreement in the authorized engagement.
      var agreementQuery = db.EngagementFeeAgreements.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && clientIds.Contains(x.PracticeClientId) &&
          db.RoleGrants.Any(grant => grant.FirmId == actor.FirmId && grant.UserId == actor.UserId &&
            grant.Role == "ClientUser" && grant.ClientId == x.PracticeClientId && grant.RevokedAt == null &&
            (grant.ExpiresAt == null || grant.ExpiresAt > now) &&
            (grant.EngagementId == null || grant.EngagementId == x.EngagementId)));
      if (position is { } after)
        agreementQuery = agreementQuery.Where(x => x.CreatedAt < after.CreatedAt ||
          (x.CreatedAt == after.CreatedAt && x.Id.CompareTo(after.AgreementId) < 0));
      List<EngagementFeeAgreement> agreementRows = [];
      if (clientIds.Length > 0)
        agreementRows = await agreementQuery.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
          .Take(pageSize + 1).ToListAsync(ct);
      var hasMore = agreementRows.Count > pageSize;
      var agreements = agreementRows.Take(pageSize).ToArray();

      var agreementViews = new List<PortalClientFinanceAgreement>(agreements.Length);

      foreach (var agreement in agreements)
      {
        var milestones = await db.FeeMilestones.AsNoTracking()
          .Where(x => x.FirmId == actor.FirmId && x.AgreementId == agreement.Id)
          .OrderBy(x => x.Kind == FeeMilestoneKinds.Advance ? 0 : 1)
          .ToListAsync(ct);

        var invoiceList = new List<PortalClientMilestoneInvoice>();
        var receiptList = new List<PortalClientReceipt>();
        decimal totalPaid = 0m;

        foreach (var milestone in milestones)
        {
          if (!milestone.InvoiceId.HasValue) continue;

          var invoiceId = milestone.InvoiceId.Value;
          var invoice = await db.Invoices.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == invoiceId && x.FirmId == actor.FirmId, ct);
          if (invoice is not null && !await db.BillingAccounts.AsNoTracking().AnyAsync(x =>
                x.FirmId == actor.FirmId && x.Id == invoice.BillingAccountId &&
                x.PracticeClientId == agreement.PracticeClientId, ct))
            return CommandResult<PortalClientFinanceView>.Fail(ErrorCodes.ScopeDenied, "Finance documents unavailable.");

          var allocationQuery =
            from allocation in db.ReceiptAllocations.AsNoTracking()
            join receipt in db.Receipts.AsNoTracking()
              on new { allocation.FirmId, allocation.ReceiptId } equals new { receipt.FirmId, ReceiptId = receipt.Id }
            where allocation.FirmId == actor.FirmId && allocation.InvoiceId == invoiceId &&
              receipt.Status == BillingStates.ReceiptRecorded
            select new
            {
              Allocation = allocation,
              Receipt = receipt
            };
          if (invoice is not null)
            allocationQuery = allocationQuery.Where(x => x.Receipt.BillingAccountId == invoice.BillingAccountId);
          var allocations = await allocationQuery.Select(x => new
          {
            x.Allocation.ReceiptId,
            x.Allocation.Amount,
            ApprovedReversedAmount = db.ReceiptAllocationReversals.AsNoTracking()
              .Where(reversal => reversal.FirmId == actor.FirmId && reversal.ReceiptAllocationId == x.Allocation.Id &&
                reversal.Status == ReceiptAllocationReversalStates.Approved)
              .Sum(reversal => (decimal?)reversal.Amount) ?? 0m
          }).ToListAsync(ct);
          var allocationAmountsByReceipt = allocations.GroupBy(x => x.ReceiptId).ToDictionary(group => group.Key, group =>
            (Original: group.Sum(x => x.Amount),
              Applied: MoneyPolicy.Normalize(group.Sum(x => x.Amount - x.ApprovedReversedAmount))));
          var receiptIds = allocationAmountsByReceipt.Keys.ToList();
          List<Receipt> receipts = receiptIds.Count == 0 ? [] : await db.Receipts.AsNoTracking()
            .Where(x => x.FirmId == actor.FirmId && x.Status == BillingStates.ReceiptRecorded && receiptIds.Contains(x.Id))
            .OrderBy(x => x.ReceivedAt)
            .ToListAsync(ct);
          var cashApplied = MoneyPolicy.Normalize(allocationAmountsByReceipt.Values.Sum(x => x.Applied));
          totalPaid += cashApplied;

          if (invoice is not null)
          {
            var creditApplied = MoneyPolicy.Normalize(await db.CreditNotes.AsNoTracking()
              .Where(x => x.FirmId == actor.FirmId && x.BillingAccountId == invoice.BillingAccountId &&
                x.InvoiceId == invoiceId && x.Status == BillingStates.CreditIssued)
              .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m);
            var termRevisions = await db.InvoicePaymentTermsRevisions.AsNoTracking()
              .Where(x => x.FirmId == actor.FirmId && x.InvoiceId == invoiceId)
              .OrderBy(x => x.Revision)
              .ToListAsync(ct);
            var approvedTerms = termRevisions
              .Where(x => x.Status == InvoicePaymentTermsStates.Approved && x.ReviewedAt is { } reviewedAt && reviewedAt <= now)
              .OrderByDescending(x => x.Revision)
              .FirstOrDefault();
            var latestTerms = termRevisions.LastOrDefault();
            var paymentTermsStatus = latestTerms is null
              ? "NOT_CONFIGURED"
              : latestTerms.ReviewedAt is null || latestTerms.ReviewedAt > now
                ? InvoicePaymentTermsStates.PendingReview
                : latestTerms.Status;
            var isPostedReceivable = invoice.PostedAt is not null &&
              invoice.Status is (BillingStates.InvoicePosted or BillingStates.InvoiceSent);
            var isCancelled = invoice.Status == BillingStates.InvoiceCancelled;
            var dueDate = approvedTerms?.DueDate;
            var invoiceOutstanding = isPostedReceivable
              ? Math.Max(0m, invoice.Total - cashApplied - creditApplied)
              : 0m;
            var collectionStatus = isCancelled
              ? BillingStates.InvoiceCancelled
              : !isPostedReceivable
                ? "NOT_POSTED"
                : invoiceOutstanding > 0m
                  ? cashApplied > 0m ? "PARTIALLY_PAID" : creditApplied > 0m ? "PARTIALLY_CREDITED" : "UNPAID"
                  : cashApplied >= invoice.Total && invoice.Total > 0m
                    ? "PAID"
                    : creditApplied > 0m ? "SETTLED_WITH_CREDIT" : "SETTLED";
            var agingStatus = isPostedReceivable
              ? FirmReceivablesAgingPolicy.Bucket(dueDate, asOfDateUtc, invoiceOutstanding)
              : "NOT_APPLICABLE";

            invoiceList.Add(new(
              invoice.Id,
              milestone.Kind,
              invoice.InvoiceNumber,
              invoice.Status,
              Exact(invoice.Total),
              invoice.Currency ?? agreement.Currency,
              invoice.PostedAt,
              invoice.SentAt,
              "NOT_TRACKED",
              collectionStatus,
              Exact(cashApplied),
              Exact(creditApplied),
              Exact(invoiceOutstanding),
              dueDate,
              paymentTermsStatus,
              agingStatus));
          }

          var doc = await db.CommercialDocuments.AsNoTracking()
            .Where(x => x.FirmId == actor.FirmId && x.FeeMilestoneId == milestone.Id && x.Kind == CommercialDocumentKinds.PaymentReceipt)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

          foreach (var receipt in receipts)
          {
            var amounts = allocationAmountsByReceipt[receipt.Id];
            receiptList.Add(new(
              receipt.Id,
              doc?.Id,
              milestone.Kind,
              Exact(amounts.Original),
              Exact(amounts.Applied),
              receipt.Currency ?? agreement.Currency,
              receipt.Reference,
              receipt.ReceivedAt,
              doc is not null ? $"/api/commercial/documents/{doc.Id}/download" : null));
          }
        }

        var outstanding = Math.Max(0m, agreement.AgreedFee - totalPaid);

        agreementViews.Add(new(
          agreement.Id,
          agreement.EngagementId,
          Exact(agreement.AgreedFee),
          agreement.Currency,
          Exact(agreement.AdvancePercent),
          Exact(outstanding),
          invoiceList,
          receiptList));
      }

      var nextCursor = hasMore && agreements.Length > 0 ? EncodeCursor(agreements[^1]) : null;
      view = new(asOfDateUtc, agreementViews, pageSize, hasMore, nextCursor);
      await snapshot.CommitAsync(ct);
    }

    // Snapshot disposal precedes the fresh session and exact-grant fence. Cursor contents are
    // only a keyset position; these checks and the SQL grant predicate remain authoritative.
    var currentIdentity = await LoadIdentityAsync(db, actor, DateTimeOffset.UtcNow, ct);
    if (currentIdentity is null || currentIdentity.SessionEpoch != actor.SessionEpoch ||
        !SameIdentity(initialIdentity, currentIdentity))
      return CommandResult<PortalClientFinanceView>.Fail(ErrorCodes.ScopeDenied, "Finance documents unavailable.");

    return CommandResult<PortalClientFinanceView>.Ok(view);
  }

  private static async Task<FinanceIdentity?> LoadIdentityAsync(
    IAuditSphereDbContext db, ActorContext actor, DateTimeOffset now, CancellationToken ct)
  {
    var user = await db.Users.AsNoTracking()
      .Where(x => x.Id == actor.UserId && x.FirmId == actor.FirmId && x.UserKind == "Client" && !x.Disabled)
      .Select(x => new { x.SessionEpoch })
      .SingleOrDefaultAsync(ct);
    if (user is null || user.SessionEpoch != actor.SessionEpoch) return null;

    var grants = await db.RoleGrants.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.Role == "ClientUser" &&
        x.ClientId != null && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now))
      .OrderBy(x => x.Id)
      .Select(x => new FinanceGrant(x.Id, x.ClientId!.Value, x.EngagementId, x.GrantedAt, x.ExpiresAt))
      .ToArrayAsync(ct);
    return new(user.SessionEpoch, grants);
  }

  private static bool SameIdentity(FinanceIdentity left, FinanceIdentity right) =>
    left.SessionEpoch == right.SessionEpoch && left.Grants.SequenceEqual(right.Grants);

  private static string EncodeCursor(EngagementFeeAgreement agreement)
    => EncodeCursor(new FinanceCursor(agreement.CreatedAt.ToUniversalTime(), agreement.Id));

  private static bool TryDecodeCursor(string value, out FinanceCursor? cursor)
  {
    cursor = null;
    if (value.Length is 0 or > MaximumCursorLength ||
        value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) return false;
    var base64 = value.Replace('-', '+').Replace('_', '/');
    var remainder = base64.Length % 4;
    if (remainder == 1) return false;
    if (remainder == 2) base64 += "==";
    else if (remainder == 3) base64 += "=";
    try
    {
      var payload = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
      var fields = payload.Split('\n');
      if (fields.Length != 3 || fields[0] != "v1" ||
          !DateTimeOffset.TryParseExact(fields[1], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var createdAt) ||
          createdAt.Offset != TimeSpan.Zero || createdAt.Ticks % 10 != 0 ||
          !Guid.TryParseExact(fields[2], "D", out var agreementId) || agreementId == Guid.Empty)
        return false;
      var decoded = new FinanceCursor(createdAt, agreementId);
      if (!string.Equals(EncodeCursor(decoded), value, StringComparison.Ordinal)) return false;
      cursor = decoded;
      return true;
    }
    catch (FormatException) { return false; }
  }

  private static string EncodeCursor(FinanceCursor cursor)
  {
    var payload = $"v1\n{cursor.CreatedAt.ToString("O", CultureInfo.InvariantCulture)}\n{cursor.AgreementId:D}";
    return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
  }

  private static CommandResult<PortalClientFinanceView> Invalid() =>
    CommandResult<PortalClientFinanceView>.Fail("request.invalid", "The client finance page or continuation is invalid.");
}
