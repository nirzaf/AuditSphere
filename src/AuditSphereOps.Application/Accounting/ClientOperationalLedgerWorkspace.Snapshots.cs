using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientOperationalJournalSnapshotView(Guid JournalId, Guid ClientId, string Revision, string CapturedAt, string JournalNumber,
  string Description, string PostingDate, string Currency, IReadOnlyList<ClientOperationalJournalLineView> Lines,
  ClientOperationalInvoiceOrigin? InvoiceOrigin = null,
  IReadOnlyList<ClientOperationalSourceOrigin>? SourceOrigins = null);

/// <summary>Immutable source-document identity and retained manifest/evidence references for one posted journal submission.</summary>
public sealed record ClientOperationalSourceOrigin(string SourceKind, Guid SourceId, Guid SubmissionId,
  string? SourceRevision, string Reference, string? ManifestSha256, string? IntentSha256, Guid? EvidenceId,
  string? EvidenceSha256, string? EvidenceReference);

public static partial class ClientOperationalLedgerWorkspace
{
  public static async Task<CommandResult<IReadOnlyList<ClientOperationalJournalSnapshotView>>> GetSnapshotsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, Guid journalId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, clientId, Preparers, ct);
    if (!auth.Succeeded || !await db.ClientOperationalJournals.AsNoTracking().AnyAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == journalId, ct))
      return CommandResult<IReadOnlyList<ClientOperationalJournalSnapshotView>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var rows = await db.ClientOperationalJournalSnapshots.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.JournalId == journalId).OrderBy(x => x.JournalRevision).ToListAsync(ct);
    var submissions = await db.ClientSalesInvoiceSubmissions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.JournalId == journalId).ToDictionaryAsync(x => x.JournalSubmittedRevision, ct);
    var salesDraftIds = submissions.Values.Select(s => s.DraftId).Distinct().ToArray();
    var salesDrafts = await db.ClientSalesInvoiceDrafts.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && salesDraftIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var purchaseSubmissions = await db.ClientPurchaseInvoiceSubmissions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.JournalId == journalId).ToDictionaryAsync(x => x.JournalSubmittedRevision, ct);
    var salesCreditSubmissions = await db.ClientSalesCreditNoteSubmissions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.JournalId == journalId).ToDictionaryAsync(x => x.JournalSubmittedRevision, ct);
    var purchaseCreditSubmissions = await db.ClientPurchaseCreditNoteSubmissions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.JournalId == journalId).ToDictionaryAsync(x => x.JournalSubmittedRevision, ct);
    var settlements = await db.ClientManualSettlementOrigins.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.JournalId == journalId).ToListAsync(ct);
    var views = new List<ClientOperationalJournalSnapshotView>(rows.Count);
    foreach (var row in rows)
    {
      using var json = JsonDocument.Parse(row.SnapshotJson);
      var header = json.RootElement.GetProperty("journal");
      if (header.GetProperty("id").GetGuid() != journalId || header.GetProperty("firm_id").GetGuid() != actor.FirmId ||
          header.GetProperty("client_id").GetGuid() != clientId || header.GetProperty("revision").GetInt64() != row.JournalRevision)
        return CommandResult<IReadOnlyList<ClientOperationalJournalSnapshotView>>.Fail(ErrorCodes.ProtectedState, "Stored journal content could not be validated.");
      var lines = json.RootElement.GetProperty("lines").EnumerateArray().Select(x => new ClientOperationalJournalLineView(
        x.GetProperty("line_number").GetInt32(), x.GetProperty("client_account_id").GetGuid(),
        x.GetProperty("account_code").GetString()!, x.GetProperty("account_name").GetString()!,
        x.GetProperty("description").GetString()!, x.GetProperty("debit").GetDecimal().ToString("F6", CultureInfo.InvariantCulture),
        x.GetProperty("credit").GetDecimal().ToString("F6", CultureInfo.InvariantCulture))).ToArray();
      var invoiceOrigin = submissions.TryGetValue(row.JournalRevision, out var submission) ? InvoiceOrigin(submission) : null;
      var sourceOrigins = new List<ClientOperationalSourceOrigin>();
      if (submission is not null)
      {
        salesDrafts.TryGetValue(submission.DraftId, out var salesDraft);
        sourceOrigins.Add(new("SALES_INVOICE", submission.InvoiceId, submission.Id, submission.DraftRevision.ToString(CultureInfo.InvariantCulture),
          salesDraft?.SourceReference is { Length: > 0 } sourceReference ? sourceReference : salesDraft?.DraftReference ?? submission.InvoiceId.ToString("D"),
          submission.ManifestHash, submission.IntentHash, null, null, null));
      }
      if (purchaseSubmissions.TryGetValue(row.JournalRevision, out var purchase))
        sourceOrigins.Add(new("PURCHASE_INVOICE", purchase.InvoiceId, purchase.Id, purchase.DraftRevision.ToString(CultureInfo.InvariantCulture),
          purchase.SupplierInvoiceReference, purchase.ManifestHash, purchase.IntentHash, purchase.SourceReceiptId, purchase.SourceReceiptHash, null));
      if (salesCreditSubmissions.TryGetValue(row.JournalRevision, out var salesCredit))
        sourceOrigins.Add(new("SALES_CREDIT_NOTE", salesCredit.CreditNoteId, salesCredit.Id, null,
          salesCredit.CreditNoteReference, salesCredit.ManifestHash, salesCredit.IntentHash, salesCredit.SourceReceiptId, salesCredit.SourceReceiptHash, salesCredit.Reason));
      if (purchaseCreditSubmissions.TryGetValue(row.JournalRevision, out var purchaseCredit))
        sourceOrigins.Add(new("PURCHASE_CREDIT_NOTE", purchaseCredit.CreditNoteId, purchaseCredit.Id, null,
          purchaseCredit.CreditNoteReference, purchaseCredit.ManifestHash, purchaseCredit.IntentHash, purchaseCredit.SourceReceiptId, purchaseCredit.SourceReceiptHash, purchaseCredit.Reason));
      foreach (var settlement in settlements)
        sourceOrigins.Add(new(settlement.SourceKind, settlement.Id, settlement.Id, null, settlement.Reference,
          null, settlement.IntentHash, null, null, settlement.EvidenceReference));
      views.Add(new(journalId, clientId, row.JournalRevision.ToString(CultureInfo.InvariantCulture), row.CapturedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        header.GetProperty("journal_number").GetString()!, header.GetProperty("description").GetString()!,
        header.GetProperty("posting_date").GetString()!, header.GetProperty("currency").GetString()!, lines, invoiceOrigin, sourceOrigins));
    }
    if (!(await AuthorizeAsync(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<IReadOnlyList<ClientOperationalJournalSnapshotView>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult<IReadOnlyList<ClientOperationalJournalSnapshotView>>.Ok(views);
  }
}
