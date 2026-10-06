using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
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
  DateTimeOffset? IssuedAt);

public sealed record PortalClientReceipt(
  Guid ReceiptId,
  Guid? DocumentId,
  string Kind,
  string Amount,
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
  IReadOnlyList<PortalClientFinanceAgreement> Agreements);

public static class ClientPortalFinanceQuery
{
  private static string Exact(decimal value) => value.ToString(CultureInfo.InvariantCulture);

  public static async Task<CommandResult<PortalClientFinanceView>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId && x.UserKind == "Client" && !x.Disabled, ct);
    if (user is null || user.SessionEpoch != actor.SessionEpoch)
      return CommandResult<PortalClientFinanceView>.Fail(ErrorCodes.ScopeDenied, "Finance documents unavailable.");

    var now = DateTimeOffset.UtcNow;
    var clientIds = await db.RoleGrants.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.Role == "ClientUser" &&
        x.ClientId != null && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now))
      .Select(x => x.ClientId!.Value)
      .Distinct()
      .ToListAsync(ct);

    if (clientIds.Count == 0)
      return CommandResult<PortalClientFinanceView>.Ok(new([]));

    var agreements = await db.EngagementFeeAgreements.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && clientIds.Contains(x.PracticeClientId))
      .OrderByDescending(x => x.CreatedAt)
      .Take(50)
      .ToListAsync(ct);

    var agreementViews = new List<PortalClientFinanceAgreement>();

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
        if (milestone.InvoiceId.HasValue)
        {
          var invoice = await db.Invoices.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == milestone.InvoiceId.Value && x.FirmId == actor.FirmId, ct);
          if (invoice is not null)
          {
            invoiceList.Add(new(
              invoice.Id,
              milestone.Kind,
              invoice.InvoiceNumber,
              invoice.Status,
              Exact(invoice.Total),
              agreement.Currency,
              invoice.PostedAt ?? invoice.SentAt ?? invoice.CreatedAt));
          }
        }

        if (milestone.InvoiceId.HasValue)
        {
          var invoiceId = milestone.InvoiceId.Value;
          var allocations = await db.ReceiptAllocations.AsNoTracking()
            .Where(x => x.FirmId == actor.FirmId && x.InvoiceId == invoiceId)
            .ToListAsync(ct);
          var receiptIds = allocations.Select(x => x.ReceiptId).Distinct().ToList();
          var receipts = await db.Receipts.AsNoTracking()
            .Where(x => x.FirmId == actor.FirmId && receiptIds.Contains(x.Id))
            .OrderBy(x => x.ReceivedAt)
            .ToListAsync(ct);

          var doc = await db.CommercialDocuments.AsNoTracking()
            .Where(x => x.FirmId == actor.FirmId && x.FeeMilestoneId == milestone.Id && x.Kind == CommercialDocumentKinds.PaymentReceipt)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

          foreach (var receipt in receipts)
          {
            var allocatedToThisInvoice = allocations.Where(x => x.ReceiptId == receipt.Id).Sum(x => x.Amount);
            totalPaid += allocatedToThisInvoice;

            receiptList.Add(new(
              receipt.Id,
              doc?.Id,
              milestone.Kind,
              Exact(allocatedToThisInvoice),
              agreement.Currency,
              receipt.Reference,
              receipt.ReceivedAt,
              doc is not null ? $"/api/commercial/documents/{doc.Id}/download" : null));
          }
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

    return CommandResult<PortalClientFinanceView>.Ok(new(agreementViews));
  }
}
