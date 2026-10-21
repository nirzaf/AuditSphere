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
  IReadOnlyList<PortalClientReceipt> Receipts,
  bool HasMoreReceipts = false,
  string? NextReceiptCursor = null);

public sealed record PortalClientFinanceReceiptPage(
  Guid AgreementId,
  IReadOnlyList<PortalClientReceipt> Receipts,
  int PageSize,
  bool HasMore,
  string? NextCursor);

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
  private const string AgreementReceiptsQueryTag = "ClientPortalFinance.AgreementReceipts";
  private const string ReceiptPageQueryTag = "ClientPortalFinance.ReceiptPage";

  private sealed record FinanceGrant(Guid Id, Guid ClientId, Guid? EngagementId,
    DateTimeOffset GrantedAt, DateTimeOffset? ExpiresAt);
  private sealed record FinanceIdentity(long SessionEpoch, FinanceGrant[] Grants);
  private sealed record FinanceCursor(DateTimeOffset CreatedAt, Guid AgreementId);
  private sealed record ReceiptCursor(Guid AgreementId, DateTimeOffset ReceivedAt, Guid ReceiptId, string Kind);
  private sealed record MilestoneReceiptSource(
    Guid MilestoneId, string Kind, Guid InvoiceId, Guid BillingAccountId, string BillingAccountCurrency);
  private sealed record ReceiptPageData(IReadOnlyList<PortalClientReceipt> Receipts, bool HasMore, string? NextCursor);

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
        var receiptSources = new List<MilestoneReceiptSource>();
        decimal totalPaid = 0m;

        foreach (var milestone in milestones)
        {
          if (!milestone.InvoiceId.HasValue) continue;

          var invoiceId = milestone.InvoiceId.Value;
          var invoice = await db.Invoices.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == invoiceId && x.FirmId == actor.FirmId, ct);
          if (invoice is null)
            return CommandResult<PortalClientFinanceView>.Fail(ErrorCodes.ScopeDenied, "Finance documents unavailable.");

          var currencyContext = await ValidateInvoiceCurrencySourcesAsync(db, actor, agreement, invoice, ct);
          if (!currencyContext.Succeeded)
            return CommandResult<PortalClientFinanceView>.Fail(currencyContext.ErrorCode!, currencyContext.Message!);
          var account = currencyContext.Value!;

          receiptSources.Add(new(milestone.Id, milestone.Kind, invoiceId, account.Id, account.Currency));
          var cashApplied = await SumCashAppliedAsync(db, actor, invoiceId, account.Id, ct);
          totalPaid += cashApplied;

          var creditApplied = MoneyPolicy.Normalize(await db.CreditNotes.AsNoTracking()
            .Where(x => x.FirmId == actor.FirmId && x.BillingAccountId == account.Id &&
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
            account.Currency,
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

        var receiptPage = await LoadReceiptPageAsync(db, actor, agreement, receiptSources, DefaultPageSize, null, ct);
        var outstanding = Math.Max(0m, agreement.AgreedFee - totalPaid);

        agreementViews.Add(new(
          agreement.Id,
          agreement.EngagementId,
          Exact(agreement.AgreedFee),
          agreement.Currency,
          Exact(agreement.AdvancePercent),
          Exact(outstanding),
          invoiceList,
          receiptPage.Receipts,
          receiptPage.HasMore,
          receiptPage.NextCursor));
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

  public static async Task<CommandResult<PortalClientFinanceReceiptPage>> GetReceiptsAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid agreementId,
    int pageSize = DefaultPageSize,
    string? cursor = null,
    CancellationToken ct = default)
  {
    if (agreementId == Guid.Empty || pageSize is < 1 or > MaximumPageSize ||
        cursor is { Length: > MaximumCursorLength }) return InvalidReceiptPage();
    ReceiptCursor? position = null;
    if (cursor is not null && (!TryDecodeReceiptCursor(cursor, out position) || position!.AgreementId != agreementId))
      return InvalidReceiptPage();
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<PortalClientFinanceReceiptPage>.Fail(ErrorCodes.GateBlocked,
        "Client finance receipts require an independent read snapshot.");

    FinanceIdentity initialIdentity;
    PortalClientFinanceReceiptPage page;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      var now = DateTimeOffset.UtcNow;
      var loadedIdentity = await LoadIdentityAsync(db, actor, now, ct);
      if (loadedIdentity is null || loadedIdentity.SessionEpoch != actor.SessionEpoch)
        return ReceiptPageScopeDenied();
      initialIdentity = loadedIdentity;

      var clientIds = initialIdentity.Grants.Select(x => x.ClientId).Distinct().ToArray();
      if (clientIds.Length == 0) return ReceiptPageScopeDenied();

      // Match the finance agreement scope exactly before reading any receipt or document rows.
      var agreement = await db.EngagementFeeAgreements.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.Id == agreementId && clientIds.Contains(x.PracticeClientId) &&
          db.RoleGrants.Any(grant => grant.FirmId == actor.FirmId && grant.UserId == actor.UserId &&
            grant.Role == "ClientUser" && grant.ClientId == x.PracticeClientId && grant.RevokedAt == null &&
            (grant.ExpiresAt == null || grant.ExpiresAt > now) &&
            (grant.EngagementId == null || grant.EngagementId == x.EngagementId)))
        .TagWith(AgreementReceiptsQueryTag)
        .SingleOrDefaultAsync(ct);
      if (agreement is null) return ReceiptPageScopeDenied();

      var milestones = await db.FeeMilestones.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.AgreementId == agreement.Id)
        .OrderBy(x => x.Kind == FeeMilestoneKinds.Advance ? 0 : 1)
        .ToListAsync(ct);
      var receiptSources = new List<MilestoneReceiptSource>(milestones.Count);
      foreach (var milestone in milestones)
      {
        if (!milestone.InvoiceId.HasValue) continue;
        var invoiceId = milestone.InvoiceId.Value;
        var invoice = await db.Invoices.AsNoTracking()
          .SingleOrDefaultAsync(x => x.Id == invoiceId && x.FirmId == actor.FirmId, ct);
        if (invoice is null)
          return ReceiptPageScopeDenied();

        var currencyContext = await ValidateInvoiceCurrencySourcesAsync(db, actor, agreement, invoice, ct);
        if (!currencyContext.Succeeded)
        {
          if (currencyContext.ErrorCode == ErrorCodes.ScopeDenied) return ReceiptPageScopeDenied();
          return CommandResult<PortalClientFinanceReceiptPage>.Fail(currencyContext.ErrorCode!, currencyContext.Message!);
        }
        var account = currencyContext.Value!;
        receiptSources.Add(new(milestone.Id, milestone.Kind, invoiceId, account.Id, account.Currency));
      }

      var receiptPage = await LoadReceiptPageAsync(db, actor, agreement, receiptSources, pageSize, position, ct);
      page = new(agreement.Id, receiptPage.Receipts, pageSize, receiptPage.HasMore, receiptPage.NextCursor);
      await snapshot.CommitAsync(ct);
    }

    // Do not return snapshot data if the participant's session or any exact grant changed during the read.
    var currentIdentity = await LoadIdentityAsync(db, actor, DateTimeOffset.UtcNow, ct);
    if (currentIdentity is null || currentIdentity.SessionEpoch != actor.SessionEpoch ||
        !SameIdentity(initialIdentity, currentIdentity))
      return ReceiptPageScopeDenied();

    return CommandResult<PortalClientFinanceReceiptPage>.Ok(page);
  }

  private static async Task<decimal> SumCashAppliedAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid invoiceId, Guid billingAccountId, CancellationToken ct)
  {
    var allocationQuery =
      from allocation in db.ReceiptAllocations.AsNoTracking()
      join receipt in db.Receipts.AsNoTracking()
        on new { allocation.FirmId, allocation.ReceiptId } equals new { receipt.FirmId, ReceiptId = receipt.Id }
      where allocation.FirmId == actor.FirmId && allocation.InvoiceId == invoiceId &&
        receipt.Status == BillingStates.ReceiptRecorded
      select new
      {
        allocation.ReceiptId,
        allocation.Amount,
        receipt.BillingAccountId,
        ApprovedReversedAmount = db.ReceiptAllocationReversals.AsNoTracking()
          .Where(reversal => reversal.FirmId == actor.FirmId && reversal.ReceiptAllocationId == allocation.Id &&
            reversal.Status == ReceiptAllocationReversalStates.Approved)
          .Sum(reversal => (decimal?)reversal.Amount) ?? 0m
      };
    allocationQuery = allocationQuery.Where(x => x.BillingAccountId == billingAccountId);

    // The unique (FirmId, ReceiptId, InvoiceId) allocation index gives one source row per receipt here;
    // at the stored six-decimal scale this is the existing per-receipt normalized sum, reduced in SQL.
    var appliedAmounts = allocationQuery.Select(x => x.Amount - x.ApprovedReversedAmount);
    var totalApplied = await appliedAmounts.SumAsync(x => (decimal?)x, ct) ?? 0m;
    return MoneyPolicy.Normalize(totalApplied);
  }

  private static async Task<CommandResult<BillingAccount>> ValidateInvoiceCurrencySourcesAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    EngagementFeeAgreement agreement,
    Invoice invoice,
    CancellationToken ct)
  {
    var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == invoice.BillingAccountId, ct);
    if (account is null || account.PracticeClientId != agreement.PracticeClientId)
      return CommandResult<BillingAccount>.Fail(ErrorCodes.ScopeDenied, "Finance documents unavailable.");

    if (!string.Equals(account.Currency, agreement.Currency, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(invoice.Currency ?? account.Currency, account.Currency, StringComparison.OrdinalIgnoreCase))
      return CurrencySourceMismatch();

    var mismatchedReceiptCurrency = await (
      from allocation in db.ReceiptAllocations.AsNoTracking()
      join receipt in db.Receipts.AsNoTracking()
        on new { allocation.FirmId, allocation.ReceiptId } equals new { receipt.FirmId, ReceiptId = receipt.Id }
      where allocation.FirmId == actor.FirmId && allocation.InvoiceId == invoice.Id &&
        receipt.BillingAccountId == account.Id && receipt.Status == BillingStates.ReceiptRecorded &&
        receipt.Currency != null && receipt.Currency.ToUpper() != account.Currency.ToUpper()
      select allocation.Id).AnyAsync(ct);
    if (mismatchedReceiptCurrency) return CurrencySourceMismatch();

    var mismatchedCreditCurrency = await db.CreditNotes.AsNoTracking().AnyAsync(x =>
      x.FirmId == actor.FirmId && x.BillingAccountId == account.Id && x.InvoiceId == invoice.Id &&
      x.Status == BillingStates.CreditIssued && x.Currency.ToUpper() != account.Currency.ToUpper(), ct);
    return mismatchedCreditCurrency ? CurrencySourceMismatch() : CommandResult<BillingAccount>.Ok(account);
  }

  private static CommandResult<BillingAccount> CurrencySourceMismatch() =>
    CommandResult<BillingAccount>.Fail(ErrorCodes.GateBlocked, "Finance documents contain inconsistent currency sources.");

  private static async Task<ReceiptPageData> LoadReceiptPageAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    EngagementFeeAgreement agreement,
    IReadOnlyList<MilestoneReceiptSource> sources,
    int pageSize,
    ReceiptCursor? position,
    CancellationToken ct)
  {
    if (sources.Count == 0) return new([], false, null);

    var milestoneIds = sources.Select(x => x.MilestoneId).Distinct().ToArray();
    var receiptDocuments = await db.CommercialDocuments.AsNoTracking()
      .Where(document => document.FirmId == actor.FirmId && document.FeeMilestoneId != null &&
        milestoneIds.Contains(document.FeeMilestoneId.Value) && document.Kind == CommercialDocumentKinds.PaymentReceipt &&
        ((document.ProposalId == null && document.QuotationVersionId == null) ||
          (document.ProposalId == agreement.ProposalId && document.QuotationVersionId == agreement.QuotationVersionId)) &&
        db.Proposals.Any(proposal => proposal.FirmId == actor.FirmId && proposal.Id == agreement.ProposalId &&
          proposal.PracticeClientId == agreement.PracticeClientId) &&
        (agreement.EngagementId == null || db.Engagements.Any(engagement =>
          engagement.FirmId == actor.FirmId && engagement.Id == agreement.EngagementId &&
          engagement.PracticeClientId == agreement.PracticeClientId)))
      .Select(document => new { FeeMilestoneId = document.FeeMilestoneId!.Value, document.Id })
      .ToListAsync(ct);
    var receiptDocumentIds = receiptDocuments.ToDictionary(x => x.FeeMilestoneId, x => x.Id);

    var candidates = new List<PortalClientReceipt>(sources.Count * (pageSize + 1));
    foreach (var source in sources)
    {
      var allocationQuery =
        from allocation in db.ReceiptAllocations.AsNoTracking()
        join receipt in db.Receipts.AsNoTracking()
          on new { allocation.FirmId, allocation.ReceiptId } equals new { receipt.FirmId, ReceiptId = receipt.Id }
        where allocation.FirmId == actor.FirmId && allocation.InvoiceId == source.InvoiceId &&
          receipt.Status == BillingStates.ReceiptRecorded
        select new
        {
          allocation.ReceiptId,
          allocation.Amount,
          receipt.BillingAccountId,
          receipt.Currency,
          receipt.Reference,
          receipt.ReceivedAt,
          ApprovedReversedAmount = db.ReceiptAllocationReversals.AsNoTracking()
            .Where(reversal => reversal.FirmId == actor.FirmId && reversal.ReceiptAllocationId == allocation.Id &&
              reversal.Status == ReceiptAllocationReversalStates.Approved)
            .Sum(reversal => (decimal?)reversal.Amount) ?? 0m
        };
      allocationQuery = allocationQuery.Where(x => x.BillingAccountId == source.BillingAccountId);
      if (position is { } after)
      {
        var kindFollowsCursor = StringComparer.Ordinal.Compare(source.Kind, after.Kind) > 0;
        allocationQuery = allocationQuery.Where(x => x.ReceivedAt < after.ReceivedAt ||
          (x.ReceivedAt == after.ReceivedAt &&
            (x.ReceiptId.CompareTo(after.ReceiptId) < 0 ||
              (x.ReceiptId == after.ReceiptId && kindFollowsCursor))));
      }

      var rows = await allocationQuery.TagWith(ReceiptPageQueryTag)
        .OrderByDescending(x => x.ReceivedAt)
        .ThenByDescending(x => x.ReceiptId)
        .Take(pageSize + 1)
        .Select(x => new
        {
          x.ReceiptId,
          x.Amount,
          x.ApprovedReversedAmount,
          x.Currency,
          x.Reference,
          x.ReceivedAt
        })
        .ToListAsync(ct);
      Guid? documentId = receiptDocumentIds.TryGetValue(source.MilestoneId, out var foundDocumentId)
        ? foundDocumentId
        : null;
      foreach (var row in rows)
      {
        candidates.Add(new(
          row.ReceiptId,
          documentId,
          source.Kind,
          Exact(row.Amount),
          Exact(MoneyPolicy.Normalize(row.Amount - row.ApprovedReversedAmount)),
          source.BillingAccountCurrency,
          row.Reference,
          row.ReceivedAt,
          documentId is { } id ? $"/api/commercial/documents/{id}/download" : null));
      }
    }

    var ordered = candidates.OrderByDescending(x => x.ReceivedAt)
      .ThenByDescending(x => x.ReceiptId)
      .ThenBy(x => x.Kind, StringComparer.Ordinal)
      .Take(pageSize + 1)
      .ToArray();
    var hasMore = ordered.Length > pageSize;
    var receipts = ordered.Take(pageSize).ToArray();
    var nextCursor = hasMore && receipts.Length > 0
      ? EncodeReceiptCursor(new(agreement.Id, receipts[^1].ReceivedAt, receipts[^1].ReceiptId, receipts[^1].Kind))
      : null;
    return new(receipts, hasMore, nextCursor);
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

  private static bool TryDecodeReceiptCursor(string value, out ReceiptCursor? cursor)
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
      if (fields.Length != 5 || fields[0] != "v1" ||
          !Guid.TryParseExact(fields[1], "D", out var agreementId) || agreementId == Guid.Empty ||
          !DateTimeOffset.TryParseExact(fields[2], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var receivedAt) ||
          receivedAt.Offset != TimeSpan.Zero || receivedAt.Ticks % 10 != 0 ||
          !Guid.TryParseExact(fields[3], "D", out var receiptId) || receiptId == Guid.Empty ||
          fields[4] is not (FeeMilestoneKinds.Advance or FeeMilestoneKinds.Balance))
        return false;
      var decoded = new ReceiptCursor(agreementId, receivedAt, receiptId, fields[4]);
      if (!string.Equals(EncodeReceiptCursor(decoded), value, StringComparison.Ordinal)) return false;
      cursor = decoded;
      return true;
    }
    catch (FormatException) { return false; }
  }

  private static string EncodeReceiptCursor(ReceiptCursor cursor)
  {
    var payload = $"v1\n{cursor.AgreementId:D}\n{cursor.ReceivedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)}\n{cursor.ReceiptId:D}\n{cursor.Kind}";
    return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
  }

  private static CommandResult<PortalClientFinanceView> Invalid() =>
    CommandResult<PortalClientFinanceView>.Fail("request.invalid", "The client finance page or continuation is invalid.");

  private static CommandResult<PortalClientFinanceReceiptPage> InvalidReceiptPage() =>
    CommandResult<PortalClientFinanceReceiptPage>.Fail("request.invalid", "The client receipt page or continuation is invalid.");

  private static CommandResult<PortalClientFinanceReceiptPage> ReceiptPageScopeDenied() =>
    CommandResult<PortalClientFinanceReceiptPage>.Fail(ErrorCodes.ScopeDenied, "Finance documents unavailable.");
}
