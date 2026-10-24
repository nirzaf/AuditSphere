using System.Text.Json;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ClientOperationalOpeningBalanceWorkspace
{
  private static async Task<CommandResult> ProveNoPredecessorAllocationsAsync(IClientAccountingDbContext db,
    Guid firm, Guid client, DateOnly cutoff, CancellationToken ct)
  {
    // No allocation accounting-effective date exists. Resolve the immutable source and target identities instead.
    var submissions = await db.ClientOpenItemAllocationSubmissions.AsNoTracking().Where(x => x.FirmId == firm && x.ClientId == client)
      .OrderBy(x => x.Id).Take(5001).ToListAsync(ct);
    if (submissions.Count > 5000) return CommandResult.Fail(ErrorCodes.GateBlocked, "Allocation source proof exceeds its supported bound.");
    foreach (var submission in submissions)
    {
      var sourceDate = await SuccessorItemDateAsync(db, firm, client, submission.SourceKind, submission.SourceItemId, ct);
      if (sourceDate is null || sourceDate <= cutoff)
        return CommandResult.Fail(ErrorCodes.GateBlocked, "An allocation touches predecessor source history or has unresolved source identity.");
      var lines = await db.ClientOpenItemAllocationLines.AsNoTracking().Where(x => x.FirmId == firm && x.ClientId == client &&
        x.SubmissionId == submission.Id).OrderBy(x => x.LineNumber).Take(101).ToListAsync(ct);
      if (lines.Count is 0 or > 100) return CommandResult.Fail(ErrorCodes.GateBlocked, "Allocation target membership is missing or unsupported.");
      foreach (var line in lines)
      {
        var targetDate = await SuccessorItemDateAsync(db, firm, client, line.TargetKind, line.TargetOpenItemId, ct);
        if (targetDate is null || targetDate <= cutoff)
          return CommandResult.Fail(ErrorCodes.GateBlocked, "An allocation touches predecessor target history or has unresolved target identity.");
      }
    }
    return CommandResult.Ok();
  }

  private static async Task<DateOnly?> SuccessorItemDateAsync(IClientAccountingDbContext db, Guid firm, Guid client,
    string kind, Guid itemId, CancellationToken ct)
  {
    Guid? journal = kind switch
    {
      "SALES_INVOICE" => await db.ClientSalesInvoiceOpenItems.AsNoTracking().Where(x => x.FirmId == firm && x.ClientId == client && x.Id == itemId).Select(x => (Guid?)x.JournalId).SingleOrDefaultAsync(ct),
      "PURCHASE_INVOICE" => await db.ClientPurchaseInvoiceOpenItems.AsNoTracking().Where(x => x.FirmId == firm && x.ClientId == client && x.Id == itemId).Select(x => (Guid?)x.JournalId).SingleOrDefaultAsync(ct),
      "SALES_CREDIT" => await db.ClientSalesCreditNoteOpenItems.AsNoTracking().Where(x => x.FirmId == firm && x.ClientId == client && x.Id == itemId).Select(x => (Guid?)x.JournalId).SingleOrDefaultAsync(ct),
      "PURCHASE_CREDIT" => await db.ClientPurchaseCreditNoteOpenItems.AsNoTracking().Where(x => x.FirmId == firm && x.ClientId == client && x.Id == itemId).Select(x => (Guid?)x.JournalId).SingleOrDefaultAsync(ct),
      "SALES_RECEIPT" or "SUPPLIER_PAYMENT" => await db.ClientManualSettlementOrigins.AsNoTracking().Where(x =>
        x.FirmId == firm && x.ClientId == client && x.Id == itemId && x.SourceKind == kind).Select(x => (Guid?)x.JournalId).SingleOrDefaultAsync(ct),
      _ => null
    };
    if (journal.HasValue)
      return await db.ClientOperationalJournals.AsNoTracking().Where(x => x.FirmId == firm && x.ClientId == client &&
        x.Id == journal.Value && x.Status == "POSTED").Select(x => (DateOnly?)x.PostingDate).SingleOrDefaultAsync(ct);
    if (kind is "SALES_RECEIPT" or "SUPPLIER_PAYMENT")
      return await (from line in db.GeneralLedgerLines.AsNoTracking()
        join tx in db.GeneralLedgerTransactions.AsNoTracking() on line.TransactionId equals tx.Id
        join batch in db.SourceImportBatches.AsNoTracking() on line.ImportBatchId equals batch.Id
        where line.Id == itemId && line.FirmId == firm && line.ClientId == client && tx.FirmId == firm && tx.ClientId == client &&
          batch.FirmId == firm && batch.ClientId == client && batch.OriginKind == "EXTERNAL" && batch.SourceKind == "GL" && batch.Status == "SEALED"
        select (DateOnly?)tx.PostingDate).SingleOrDefaultAsync(ct);
    if (kind is not ("OPENING_AR_INVOICE" or "OPENING_AP_INVOICE")) return null;
    var openings = await db.ClientOperationalOpeningBalances.AsNoTracking().Where(x => x.FirmId == firm && x.ClientId == client)
      .OrderBy(x => x.Id).Take(33).ToListAsync(ct);
    if (openings.Count > 32) return null;
    foreach (var opening in openings)
    {
      if (!ValidManifest(opening)) return null;
      using var document = JsonDocument.Parse(opening.ManifestJson);
      if (!document.RootElement.TryGetProperty("OpenItems", out var items) || items.ValueKind != JsonValueKind.Array) continue;
      if (items.EnumerateArray().Any(x => x.GetProperty("Id").GetGuid() == itemId &&
        x.GetProperty("Role").GetString() == (kind == "OPENING_AR_INVOICE" ? "AR" : "AP"))) return opening.AsOfDate;
    }
    return null;
  }
}
