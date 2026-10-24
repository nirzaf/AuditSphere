using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ClientOperationalOpeningBalanceWorkspace
{
  private static async Task<ClientBookkeepingCounterparty> CarryPartyAsync(CarryContext c, Guid id, string role, CancellationToken ct)
  {
    var party = await c.Db.ClientBookkeepingCounterparties.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == c.Firm && x.ClientId == c.Client && x.Id == id, ct);
    CarryRequire(party is not null, "The original client counterparty is missing.");
    var effective = await ClientBookkeepingCounterpartyWorkspace.EffectiveView(c.Db, party!, ct);
    // Parties have no active-status column. Use their actual approved role/currency, not an invented flag.
    CarryRequire(effective.Role == (role == "AR" ? "CUSTOMER" : "SUPPLIER") || effective.Role == "BOTH",
      "The original party no longer has the required customer/supplier role.");
    CarryRequire(string.IsNullOrEmpty(effective.DefaultCurrency) || effective.DefaultCurrency == c.Currency,
      "The original party has an incompatible functional currency.");
    return party!;
  }

  private static async Task<SuccessorCarriedItem> CarryManualItemAsync(CarryContext c,
    ClientOperationalOpeningBalance opening, Guid itemId, CancellationToken ct)
  {
    if (!c.Openings.TryGetValue(opening.Id, out var cached))
    {
      CarryRequire(ValidManifest(opening) && opening.FirmId == c.Firm && opening.ClientId == c.Client &&
        opening.Currency == c.Currency && opening.ApprovedByUserId.HasValue && opening.ApprovedAt.HasValue &&
        opening.CreatedByUserId != opening.ApprovedByUserId, "The original manual opening lacks intact independent approval.");
      CarryRequire(!await c.Db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().AnyAsync(x => x.OpeningId == opening.Id, ct),
        "A carried manual item must identify the original manual opening, not a successor surrogate.");
      var originalManifest = JsonSerializer.Deserialize<Manifest>(opening.ManifestJson)!;
      CarryRequire(originalManifest.OpenItems is { Count: <= SuccessorCarryLimit }, "Original opening items are missing or over-bound.");
      cached = (opening, originalManifest, originalManifest.OpenItems!.ToDictionary(x => x.Id));
      c.Openings.Add(opening.Id, cached);
    }
    var manifest = cached.Manifest;
    var row = cached.Items.GetValueOrDefault(itemId);
    CarryRequire(row is not null && row.Id != Guid.Empty && row.Role is "AR" or "AP" &&
      !string.IsNullOrWhiteSpace(row.Reference) && row.Reference.Length <= 200 &&
      !string.IsNullOrWhiteSpace(row.CounterpartyName), "The original reviewed opening item is missing or malformed.");
    var control = c.Controls.SingleOrDefault(x => x.Proof.Role == row!.Role && x.Account.Id == row.AccountId);
    CarryRequire(control is not null && control.Account.AccountCode == row!.AccountCode &&
      manifest.ChartVersionId == control.Account.ChartVersionId && control.Configuration.EffectiveFrom <= opening.AsOfDate &&
      (control.Configuration.EffectiveTo == null || control.Configuration.EffectiveTo >= opening.AsOfDate),
      "The root item has no exact original effective approved control.");
    var amount = CarryAmount(row!.Amount);
    CarryRequire(amount > 0m && ClientNativeAmountRules.ValidStoredAmount(amount), "Opening item amount is not an exact positive native amount.");
    var party = await CarryPartyAsync(c, row.CounterpartyId, row.Role, ct);
    var facts = JsonSerializer.Serialize(new
    {
      Version = "native-successor-opening-item-source.v1", opening.FirmId, opening.ClientId,
      OpeningId = opening.Id, opening.PeriodId, opening.PeriodRevision, opening.AsOfDate, opening.Currency,
      opening.ManifestSha256, opening.EvidenceSha256, opening.CreatedByUserId, opening.ApprovedByUserId, opening.ApprovedAt,
      OriginalKind = row.Role == "AR" ? "OPENING_AR_INVOICE" : "OPENING_AP_INVOICE",
      OriginalOpenItemId = row.Id, OriginalDocumentId = opening.Id, OriginalOpeningId = opening.Id,
      SubmissionId = (Guid?)null, DraftId = (Guid?)null, JournalId = (Guid?)null,
      SourceAsOfDate = opening.AsOfDate, DocumentDate = (DateOnly?)null, AccountingDate = (DateOnly?)null,
      row.Role, row.CounterpartyId, row.CounterpartyName, row.Reference, ReferenceKind = "OPENING_REFERENCE", row.DueDate,
      row.AccountId, row.AccountCode, OriginalAmount = CarryExact(amount), PartyHash = SuccessorHash(party),
      ControlConfigurationId = control!.Configuration.Id, ControlConfigurationHash = SuccessorHash(control.Configuration),
      ControlDecisionId = control.Decision.Id, ControlDecisionHash = SuccessorHash(control.Decision), AccountHash = SuccessorHash(control.Account)
    });
    CarryRequire(Encoding.UTF8.GetByteCount(facts) <= 64 * 1024, "Original opening-item facts exceed their byte bound.");
    return new(row.Role == "AR" ? "OPENING_AR_INVOICE" : "OPENING_AP_INVOICE", row.Id, opening.Id, opening.Id,
      null, null, null, row.Role, row.CounterpartyId, row.CounterpartyName, row.Reference, "OPENING_REFERENCE",
      row.AccountId, row.AccountCode, opening.Currency, CarryExact(amount), CarryExact(0m), CarryExact(amount),
      opening.AsOfDate, null, null, row.DueDate, facts, SuccessorHash(facts));
  }

  private static async Task<CarryJournal> CarryJournalAsync(CarryContext c, Guid id, CancellationToken ct)
  {
    if (c.Journals.TryGetValue(id, out var retained)) return retained;
    var journal = await c.Db.ClientOperationalJournals.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == c.Firm && x.ClientId == c.Client && x.Id == id, ct);
    CarryRequire(journal is not null && journal.Status == "POSTED" && journal.Currency == c.Currency &&
      journal.PostingSequence is > 0 && journal.Revision > 1 && journal.PostedAt.HasValue &&
      journal.PostedByUserId.HasValue && journal.PostedByUserId != journal.CreatedByUserId,
      "An original invoice journal lacks actual independent posting evidence.");
    var j = journal!;
    var lines = await c.Db.ClientOperationalJournalLines.AsNoTracking().Where(x => x.FirmId == c.Firm &&
      x.ClientId == c.Client && x.JournalId == id).OrderBy(x => x.LineNumber).Take(101).ToArrayAsync(ct);
    CarryRequire(lines.Length is >= 2 and <= 100 && lines.Select(x => x.Id).Distinct().Count() == lines.Length &&
      lines.Select(x => x.LineNumber).Distinct().Count() == lines.Length && lines.All(x =>
        ClientNativeAmountRules.ValidStoredAmount(x.Debit) && ClientNativeAmountRules.ValidStoredAmount(x.Credit) &&
        (x.Debit == 0m || x.Credit == 0m)) && lines.Sum(x => x.Debit) == lines.Sum(x => x.Credit),
      "The exact posted journal lines are incomplete, unbalanced or unsupported.");
    var snapshot = await c.Db.ClientOperationalJournalSnapshots.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == c.Firm && x.ClientId == c.Client && x.JournalId == id && x.JournalRevision == j.Revision - 1, ct);
    var decisions = await c.Db.ClientOperationalJournalDecisions.AsNoTracking().Where(x => x.FirmId == c.Firm &&
      x.ClientId == c.Client && x.JournalId == id && x.JournalRevision == j.Revision - 1).Take(2).ToArrayAsync(ct);
    var receipts = await c.Db.ClientOperationalPostingReceipts.AsNoTracking().Where(x => x.FirmId == c.Firm &&
      x.ClientId == c.Client && x.JournalId == id && x.PostedRevision == j.Revision).Take(2).ToArrayAsync(ct);
    CarryRequire(snapshot is not null && snapshot.CaptureKind == "SUBMISSION" && decisions.Length == 1 && receipts.Length == 1,
      "The exact submitted snapshot, decision and posting receipt are required.");
    var decision = decisions[0]; var receipt = receipts[0];
    CarryRequire(decision.Decision == "APPROVE" && decision.ActorUserId == j.PostedByUserId && decision.CreatedAt == j.PostedAt &&
      receipt.ActorUserId == j.PostedByUserId && receipt.RecordedAt == j.PostedAt && receipt.SubmittedRevision == j.Revision - 1 &&
      IsSha256(receipt.IntentHash) && IsSha256(receipt.PreviewDigest), "The posting receipt and independent decision do not bind this exact journal revision.");
    using var json = JsonDocument.Parse(snapshot!.SnapshotJson);
    var header = json.RootElement.GetProperty("journal");
    CarryRequire(header.GetProperty("id").GetGuid() == id && header.GetProperty("firm_id").GetGuid() == c.Firm &&
      header.GetProperty("client_id").GetGuid() == c.Client && header.GetProperty("period_id").GetGuid() == j.PeriodId &&
      header.GetProperty("revision").GetInt64() == j.Revision - 1 && header.GetProperty("currency").GetString() == j.Currency &&
      DateOnly.ParseExact(header.GetProperty("posting_date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture) == j.PostingDate,
      "The retained submitted header differs from its original posting.");
    var saved = json.RootElement.GetProperty("lines").EnumerateArray().OrderBy(x => x.GetProperty("line_number").GetInt32()).ToArray();
    CarryRequire(saved.Length == lines.Length && saved.Zip(lines).All(x =>
      x.First.GetProperty("id").GetGuid() == x.Second.Id && x.First.GetProperty("journal_id").GetGuid() == id &&
      x.First.GetProperty("firm_id").GetGuid() == c.Firm && x.First.GetProperty("client_id").GetGuid() == c.Client &&
      x.First.GetProperty("client_account_id").GetGuid() == x.Second.ClientAccountId &&
      x.First.GetProperty("account_code").GetString() == x.Second.AccountCode && x.First.GetProperty("account_name").GetString() == x.Second.AccountName &&
      x.First.GetProperty("debit").GetDecimal() == x.Second.Debit && x.First.GetProperty("credit").GetDecimal() == x.Second.Credit),
      "The original submitted line identities and amounts differ from the posting.");
    retained = new(j, lines, snapshot, decision, receipt); c.Journals.Add(id, retained); return retained;
  }

  private static void CarryInvoiceLines(CarryContext c, CarryJournal journal, Guid accountId, string role,
    decimal amount, IEnumerable<ClientOperationalJournalLineView> manifestLines)
  {
    var control = c.Controls.SingleOrDefault(x => x.Proof.Role == role && x.Account.Id == accountId);
    CarryRequire(control is not null && control.Configuration.EffectiveFrom <= journal.Journal.PostingDate &&
      (control.Configuration.EffectiveTo == null || control.Configuration.EffectiveTo >= journal.Journal.PostingDate),
      "The invoice has no exact effective original control account.");
    var controls = journal.Lines.Where(x => c.Controls.Any(y => y.Account.Id == x.ClientAccountId)).ToArray();
    CarryRequire(amount > 0m && ClientNativeAmountRules.ValidStoredAmount(amount) && controls.Length == 1 &&
      controls[0].ClientAccountId == accountId && (role == "AR" ? controls[0].Debit == amount && controls[0].Credit == 0m :
        controls[0].Credit == amount && controls[0].Debit == 0m), "The original invoice gross does not equal its positive AR/AP control posting.");
    var manifest = manifestLines.OrderBy(x => x.LineNumber).ToArray();
    CarryRequire(manifest.Length == journal.Lines.Length && manifest.Zip(journal.Lines).All(x =>
      x.First.LineNumber == x.Second.LineNumber && x.First.AccountId == x.Second.ClientAccountId &&
      x.First.AccountCode == x.Second.AccountCode && x.First.AccountName == x.Second.AccountName &&
      CarryAmount(x.First.Debit) == x.Second.Debit && CarryAmount(x.First.Credit) == x.Second.Credit),
      "Invoice submission lines do not reproduce their original posted journal.");
  }

  private static async Task<SuccessorCarriedItem> CarrySalesAsync(CarryContext c, ClientSalesInvoiceOpenItem item, CancellationToken ct)
  {
    var submission = await c.Db.ClientSalesInvoiceSubmissions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == c.Firm && x.ClientId == c.Client && x.Id == item.SubmissionId, ct);
    CarryRequire(submission is not null && submission.InvoiceId == item.InvoiceId && submission.JournalId == item.JournalId &&
      SuccessorHash(submission.ManifestJson) == submission.ManifestHash, "The original sales submission identity is invalid.");
    var s = submission!;
    var draft = await c.Db.ClientSalesInvoiceDrafts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == c.Firm && x.ClientId == c.Client && x.Id == s.DraftId, ct);
    var decisions = await c.Db.ClientSalesInvoiceDecisions.AsNoTracking().Where(x => x.FirmId == c.Firm &&
      x.ClientId == c.Client && x.SubmissionId == s.Id).Take(2).ToArrayAsync(ct);
    var j = await CarryJournalAsync(c, item.JournalId, ct);
    CarryRequire(draft is not null && draft.InvoiceId == item.InvoiceId && draft.Revision == s.DraftRevision &&
      draft.PeriodId == j.Journal.PeriodId && draft.CustomerId == item.CustomerId && draft.Currency == c.Currency &&
      item.Currency == c.Currency && draft.GrossAmount == item.OriginalAmount &&
      SuccessorHash(draft.SnapshotJson) == draft.SnapshotHash && s.JournalSubmittedRevision == j.Journal.Revision - 1 &&
      decisions.Length == 1 && decisions[0].Decision == "APPROVE" && decisions[0].ActorUserId == j.Journal.PostedByUserId &&
      decisions[0].ActorUserId != s.CreatedByUserId && decisions[0].CreatedAt == item.PostedAt && item.PostedAt == j.Journal.PostedAt &&
      decisions[0].CommandId == j.Receipt.CommandId && decisions[0].IntentHash == j.Receipt.IntentHash &&
      decisions[0].PreviewDigest == j.Receipt.PreviewDigest, "The sales draft, independent decision and immutable original item do not bind the posting.");
    var d = draft!;
    var snapshot = JsonSerializer.Deserialize<ClientSalesInvoiceDraftSnapshot>(d.SnapshotJson)!;
    var manifest = JsonSerializer.Deserialize<ClientSalesInvoiceManifest>(s.ManifestJson)!;
    CarryRequire(snapshot is not null && manifest is not null && snapshot.Lines is not null && manifest.Lines is not null &&
      manifest.Version == "client-sales-submission-v1" && snapshot.Version == "client-sales-draft-v1" && snapshot.Customer.Id == item.CustomerId &&
      snapshot.Customer.ClientId == c.Client && snapshot.Seller.ClientId == c.Client && snapshot.Currency == c.Currency &&
      snapshot.ChartVersionId == d.ChartVersionId && snapshot.DraftReference == d.DraftReference &&
      snapshot.SourceReference == d.SourceReference && CarryAmount(snapshot.Gross) == item.OriginalAmount &&
      manifest.DraftId == d.Id && manifest.DraftHash == d.SnapshotHash &&
      DateOnly.ParseExact(snapshot.AccountingDate, "yyyy-MM-dd", CultureInfo.InvariantCulture) == j.Journal.PostingDate &&
      DateOnly.ParseExact(snapshot.DueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture) == item.DueDate,
      "The original sales snapshot dates, references or identity differ from the posted invoice.");
    var control = c.Controls.Single(x => x.Proof.Role == "AR");
    CarryRequire(manifest.ReceivableRoleId == control.Configuration.Id && manifest.ReceivableDecisionId == control.Decision.Id &&
      manifest.ReceivableAccountId == control.Account.Id && d.ChartVersionId == control.Account.ChartVersionId,
      "The sales source control approval differs from the carried control.");
    CarryInvoiceLines(c, j, manifest.ReceivableAccountId, "AR", item.OriginalAmount, manifest.Lines);
    CarryRequire(snapshot.TaxTreatment == "NONE" && CarryAmount(snapshot.Net) == item.OriginalAmount && d.NetAmount == item.OriginalAmount,
      "The carried sales source must retain its supported untaxed original amounts.");
    await CarryInvoiceAccountsAsync(c, j, "AR", snapshot.Lines.Select(x =>
      (x.AccountId, x.AccountCode, x.AccountName, CarryAmount(x.Net))).ToArray(), ct);
    var party = await CarryPartyAsync(c, item.CustomerId, "AR", ct);
    return CarryInvoiceItem(c, "SALES_INVOICE", item.Id, item.InvoiceId, s.Id, d.Id, j, "AR", party,
      snapshot.Customer.DisplayName, d.DraftReference, "DRAFT_REFERENCE", control, item.OriginalAmount,
      DateOnly.ParseExact(snapshot.DocumentDate, "yyyy-MM-dd", CultureInfo.InvariantCulture), item.DueDate,
      SuccessorHash(item), SuccessorHash(s), s.ManifestHash, SuccessorHash(d), d.SnapshotHash,
      decisions[0].Id, SuccessorHash(decisions[0]), manifest.SourceReceiptId, manifest.SourceReceiptHash, d.SourceReference);
  }

  private static async Task<SuccessorCarriedItem> CarryPurchaseAsync(CarryContext c, ClientPurchaseInvoiceOpenItem item, CancellationToken ct)
  {
    var submission = await c.Db.ClientPurchaseInvoiceSubmissions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == c.Firm && x.ClientId == c.Client && x.Id == item.SubmissionId, ct);
    CarryRequire(submission is not null && submission.InvoiceId == item.InvoiceId && submission.JournalId == item.JournalId &&
      SuccessorHash(submission.ManifestJson) == submission.ManifestHash, "The original purchase submission identity is invalid.");
    var s = submission!;
    var draft = await c.Db.ClientPurchaseInvoiceDrafts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == c.Firm && x.ClientId == c.Client && x.Id == s.DraftId, ct);
    var decisions = await c.Db.ClientPurchaseInvoiceDecisions.AsNoTracking().Where(x => x.FirmId == c.Firm &&
      x.ClientId == c.Client && x.SubmissionId == s.Id).Take(2).ToArrayAsync(ct);
    var j = await CarryJournalAsync(c, item.JournalId, ct);
    CarryRequire(draft is not null && draft.InvoiceId == item.InvoiceId && draft.Revision == s.DraftRevision &&
      draft.PeriodId == j.Journal.PeriodId && draft.SupplierId == item.SupplierId && s.SupplierId == item.SupplierId &&
      draft.Currency == c.Currency && item.Currency == c.Currency && draft.GrossAmount == item.OriginalAmount &&
      SuccessorHash(draft.SnapshotJson) == draft.SnapshotHash && s.JournalSubmittedRevision == j.Journal.Revision - 1 &&
      decisions.Length == 1 && decisions[0].Decision == "APPROVE" && decisions[0].ActorUserId == j.Journal.PostedByUserId &&
      decisions[0].ActorUserId != s.CreatedByUserId && decisions[0].CreatedAt == item.PostedAt && item.PostedAt == j.Journal.PostedAt &&
      decisions[0].CommandId == j.Receipt.CommandId && decisions[0].IntentHash == j.Receipt.IntentHash &&
      decisions[0].PreviewDigest == j.Receipt.PreviewDigest,
      "The purchase draft, independent decision and immutable original item do not bind the posting.");
    var d = draft!;
    var snapshot = JsonSerializer.Deserialize<ClientPurchaseInvoiceSnapshot>(d.SnapshotJson)!;
    var manifest = JsonSerializer.Deserialize<ClientPurchaseInvoiceManifest>(s.ManifestJson)!;
    CarryRequire(snapshot is not null && manifest is not null && snapshot.Lines is not null && manifest.Lines is not null &&
      snapshot.Version == "client-purchase-invoice-v1" && manifest.Version == "client-purchase-submission-v1" && snapshot.SupplierId == item.SupplierId && snapshot.Currency == c.Currency && snapshot.ChartVersionId == d.ChartVersionId &&
      snapshot.SupplierInvoiceReference == d.SupplierInvoiceReference && snapshot.VoucherReference == d.VoucherReference &&
      CarryAmount(snapshot.Gross) == item.OriginalAmount && manifest.InvoiceId == item.InvoiceId && manifest.DraftId == d.Id &&
      manifest.DraftHash == d.SnapshotHash && manifest.SupplierId == item.SupplierId && manifest.Currency == c.Currency &&
      manifest.SupplierInvoiceReference == d.SupplierInvoiceReference && s.SupplierInvoiceReference == d.SupplierInvoiceReference &&
      manifest.PeriodId == j.Journal.PeriodId && d.AccountingDate == j.Journal.PostingDate && d.DueDate == item.DueDate &&
      manifest.AccountingDate == d.AccountingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) &&
      manifest.DueDate == d.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) &&
      snapshot.AccountingDate == manifest.AccountingDate && snapshot.DocumentDate == d.DocumentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) &&
      snapshot.DueDate == manifest.DueDate && snapshot.NormalizedSupplierReference == d.NormalizedSupplierReference &&
      manifest.NormalizedSupplierReference == d.NormalizedSupplierReference && manifest.VoucherReference == d.VoucherReference &&
      manifest.SourceReceiptId == s.SourceReceiptId && manifest.SourceReceiptHash == s.SourceReceiptHash,
      "The original supplier reference, dates or purchase snapshot identity differ from the posting.");
    var control = c.Controls.Single(x => x.Proof.Role == "AP");
    CarryRequire(manifest.PayableRoleId == control.Configuration.Id && manifest.PayableAccountId == control.Account.Id &&
      d.ChartVersionId == control.Account.ChartVersionId, "The purchase source control differs from the carried control.");
    CarryInvoiceLines(c, j, manifest.PayableAccountId, "AP", item.OriginalAmount, manifest.Lines.Select(x =>
      new ClientOperationalJournalLineView(x.LineNumber, x.AccountId, x.AccountCode, x.AccountName, x.Description, x.Debit, x.Credit)));
    CarryRequire(CarryAmount(snapshot.Tax) == 0m && d.TaxAmount == 0m && CarryAmount(snapshot.Net) == item.OriginalAmount &&
      d.NetAmount == item.OriginalAmount && snapshot.Lines.All(x => CarryAmount(x.Tax) == 0m && CarryAmount(x.Net) == CarryAmount(x.Gross)),
      "The carried purchase source must retain its supported untaxed original amounts.");
    await CarryInvoiceAccountsAsync(c, j, "AP", snapshot.Lines.Select(x =>
      (x.AccountId, x.AccountCode, x.AccountName, CarryAmount(x.Net))).ToArray(), ct);
    var party = await CarryPartyAsync(c, item.SupplierId, "AP", ct);
    return CarryInvoiceItem(c, "PURCHASE_INVOICE", item.Id, item.InvoiceId, s.Id, d.Id, j, "AP", party,
      snapshot.SupplierName, d.SupplierInvoiceReference, "SUPPLIER_INVOICE_REFERENCE", control, item.OriginalAmount,
      d.DocumentDate, item.DueDate, SuccessorHash(item), SuccessorHash(s), s.ManifestHash, SuccessorHash(d), d.SnapshotHash,
      decisions[0].Id, SuccessorHash(decisions[0]), manifest.SourceReceiptId, manifest.SourceReceiptHash, d.SupplierInvoiceReference);
  }

  private static SuccessorCarriedItem CarryInvoiceItem(CarryContext c, string kind, Guid itemId, Guid documentId,
    Guid submissionId, Guid draftId, CarryJournal j, string role, ClientBookkeepingCounterparty party,
    string originalName, string reference, string referenceKind, CarryControl control, decimal amount,
    DateOnly documentDate, DateOnly dueDate, string itemHash, string submissionHash, string manifestHash,
    string draftHash, string snapshotHash, Guid invoiceDecisionId, string invoiceDecisionHash,
    Guid? sourceReceiptId, string? sourceReceiptHash, string sourceReference)
  {
    CarryRequire(itemId != Guid.Empty && documentId != Guid.Empty && !string.IsNullOrWhiteSpace(reference) &&
      reference.Length <= 200 && !string.IsNullOrWhiteSpace(originalName), "Original invoice reference and party name are required.");
    var facts = JsonSerializer.Serialize(new
    {
      Version = "native-successor-invoice-item-source.v1", FirmId = c.Firm, ClientId = c.Client, OriginalKind = kind,
      OriginalOpenItemId = itemId, OriginalDocumentId = documentId, SubmissionId = submissionId, DraftId = draftId,
      OriginalOpeningId = (Guid?)null, SourceAsOfDate = (DateOnly?)null, AccountingDate = j.Journal.PostingDate,
      JournalId = j.Journal.Id, j.Journal.PeriodId, j.Journal.PostingDate, j.Journal.PostingSequence,
      j.Journal.Currency, j.Journal.Revision, OpenItemHash = itemHash, SubmissionHash = submissionHash,
      SubmissionManifestHash = manifestHash, DraftHash = draftHash, DraftSnapshotHash = snapshotHash,
      SourceReceiptId = sourceReceiptId, SourceReceiptHash = sourceReceiptHash, SourceReference = sourceReference,
      InvoiceDecisionId = invoiceDecisionId, InvoiceDecisionHash = invoiceDecisionHash,
      JournalHash = SuccessorHash(j.Journal), JournalSnapshotId = j.Snapshot.Id, JournalSnapshotHash = SuccessorHash(j.Snapshot.SnapshotJson),
      PostingDecisionId = j.Decision.Id, PostingDecisionHash = SuccessorHash(j.Decision),
      PostingReceiptId = j.Receipt.Id, PostingReceiptHash = SuccessorHash(j.Receipt),
      CounterpartyId = party.Id, PartyHash = SuccessorHash(party), CounterpartyName = originalName, Reference = reference,
      ReferenceKind = referenceKind, DocumentDate = documentDate, DueDate = dueDate, OriginalAmount = CarryExact(amount),
      control.Proof.Role, AccountId = control.Account.Id, control.Account.AccountCode, AccountHash = SuccessorHash(control.Account),
      ControlConfigurationId = control.Configuration.Id, ControlConfigurationHash = SuccessorHash(control.Configuration),
      ControlDecisionId = control.Decision.Id, ControlDecisionHash = SuccessorHash(control.Decision)
    });
    CarryRequire(Encoding.UTF8.GetByteCount(facts) <= 64 * 1024, "Original invoice facts exceed their byte bound.");
    return new(kind, itemId, documentId, null, submissionId, draftId, j.Journal.Id, role, party.Id,
      originalName, reference, referenceKind, control.Account.Id, control.Account.AccountCode, c.Currency,
      CarryExact(amount), CarryExact(0m), CarryExact(amount), null, documentDate, j.Journal.PostingDate,
      dueDate, facts, SuccessorHash(facts));
  }
  private static async Task CarryInvoiceAccountsAsync(CarryContext c, CarryJournal journal, string role,
    (Guid AccountId, string AccountCode, string AccountName, decimal Amount)[] rows, CancellationToken ct)
  {
    CarryRequire(rows.Length is > 0 and <= 99 && rows.All(x => ClientNativeAmountRules.ValidStoredAmount(x.Amount)),
      "The original invoice account population is invalid or over-bound.");
    var ids = rows.Select(x => x.AccountId).Distinct().ToArray();
    var accounts = await c.Db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == c.Firm && x.ClientId == c.Client && ids.Contains(x.Id)).ToArrayAsync(ct);
    var chart = c.Controls.Single(x => x.Proof.Role == role).Account.ChartVersionId;
    CarryRequire(accounts.Length == ids.Length && rows.All(x => accounts.Any(a => a.Id == x.AccountId &&
      a.ChartVersionId == chart && a.Status == "ACTIVE" && a.IsPosting && a.AccountCode == x.AccountCode && a.AccountName == x.AccountName &&
      (role == "AR" ? a.AccountType == "INCOME" : a.AccountType is "ASSET" or "EXPENSE"))) &&
      ids.All(id => c.Controls.All(x => x.Account.Id != id)), "An original invoice account is missing, changed or unsupported.");
    var actual = journal.Lines.Where(x => c.Controls.All(y => y.Account.Id != x.ClientAccountId)).ToArray();
    var grouped = rows.GroupBy(x => x.AccountId).Where(x => x.Sum(y => y.Amount) != 0m).ToArray();
    CarryRequire(actual.Length == grouped.Length && grouped.All(g => actual.Count(x => x.ClientAccountId == g.Key &&
      (role == "AR" ? x.Credit == g.Sum(y => y.Amount) && x.Debit == 0m : x.Debit == g.Sum(y => y.Amount) && x.Credit == 0m)) == 1),
      "Original invoice snapshot amounts do not reproduce every noncontrol posting line.");
  }

  private sealed record CarryAncestry(Guid RootOpeningId,
    Dictionary<(string Kind, Guid SubmissionId), (Guid JournalId, string SubmissionHash, string JournalHash)> Invoices);

  private static async Task CarryInheritedAnchorAsync(CarryContext c, ClientOperationalOpeningBalance opening,
    SuccessorCarriedItem item, Guid engagementId, CancellationToken ct)
  {
    if (!c.Ancestries.TryGetValue(opening.Id, out var ancestry))
    {
      var headId = opening.Id;
      var visited = new HashSet<Guid>();
      var invoices = new Dictionary<(string Kind, Guid SubmissionId), (Guid JournalId, string SubmissionHash, string JournalHash)>();
      for (var depth = 0; depth < 32; depth++)
      {
        CarryRequire(visited.Add(opening.Id), "Cyclic original carry ancestry.");
        var proof = await c.Db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().SingleOrDefaultAsync(x =>
          x.FirmId == c.Firm && x.ClientId == c.Client && x.OpeningId == opening.Id, ct);
        if (proof is null)
        {
          CarryRequire(ValidManifest(opening) && opening.ApprovedByUserId.HasValue && opening.ApprovedAt.HasValue,
            "The carried original root opening is not intact and approved.");
          ancestry = new(opening.Id, invoices); c.Ancestries.Add(headId, ancestry); break;
        }
        CarryRequire(proof.EngagementId == engagementId && ParseSuccessorProof(opening, proof).Succeeded,
          "The original source ancestry has an invalid or cross-engagement proof.");
        var evidence = await c.Db.NativeTrialBalanceSources.AsNoTracking().SingleOrDefaultAsync(x =>
          x.FirmId == c.Firm && x.ClientId == c.Client && x.EngagementId == engagementId && x.DatasetId == proof.SourceDatasetId, ct);
        CarryRequire(evidence is not null && evidence.SourceIdentityHash == proof.SourceIdentityHash &&
          SuccessorHash(evidence.ManifestJson) == evidence.ManifestHash && Encoding.UTF8.GetByteCount(evidence.ManifestJson) <= CarrySourceBytes,
          "The original accepted source artifact is missing from carry ancestry.");
        using var manifest = JsonDocument.Parse(evidence!.ManifestJson);
        var m = manifest.RootElement;
        CarryRequire(m.GetProperty("FirmId").GetGuid() == c.Firm && m.GetProperty("ClientId").GetGuid() == c.Client &&
          m.GetProperty("EngagementId").GetGuid() == engagementId && m.GetProperty("PeriodId").GetGuid() == proof.PriorPeriodId,
          "The original source artifact has different scoped ancestry.");
        using var pins = JsonDocument.Parse(m.GetProperty("Pins").GetProperty("MembershipJson").GetString()!);
        var membership = pins.RootElement;
        var journals = CarryArray(membership, "Journals").ToDictionary(x => x.GetProperty("Id").GetGuid());
        foreach (var (kind, name) in new[] { ("SALES_INVOICE", "Sales"), ("PURCHASE_INVOICE", "Purchases") })
          foreach (var origin in CarryArray(membership, name))
          {
            var journalId = origin.GetProperty("JournalId").GetGuid();
            CarryRequire(journals.TryGetValue(journalId, out var journal), "An ancestor invoice has no member journal.");
            var key = (kind, origin.GetProperty("Id").GetGuid());
            CarryRequire(invoices.TryAdd(key, (journalId, origin.GetProperty("SourceHash").GetString()!,
              SuccessorHash(journal.Deserialize<ClientOperationalJournal>()!))) && invoices.Count <= SuccessorCarryLimit,
              "Duplicate or over-bound ancestor invoice origins.");
          }
        var openingPin = membership.GetProperty("Opening");
        var previousId = openingPin.GetProperty("Id").GetGuid();
        var previous = await c.Db.ClientOperationalOpeningBalances.AsNoTracking().SingleOrDefaultAsync(x =>
          x.FirmId == c.Firm && x.ClientId == c.Client && x.Id == previousId && x.PeriodId == proof.PriorPeriodId, ct);
        CarryRequire(previous is not null && previous.ApprovedByUserId.HasValue && previous.ApprovedAt.HasValue &&
          ValidManifest(previous) && openingPin.GetProperty("ManifestSha256").GetString() == previous.ManifestSha256,
          "The original opening is missing from consumed native source ancestry.");
        opening = previous!;
      }
      CarryRequire(ancestry is not null, "Original source ancestry exceeds 32 openings.");
    }
    if (item.OriginalOpeningId.HasValue)
      CarryRequire(item.OriginalOpeningId == ancestry.RootOpeningId && item.OriginalDocumentId == ancestry.RootOpeningId,
        "The carried manual original is outside the consumed root opening.");
    else
    {
      using var facts = JsonDocument.Parse(item.SourceFactsJson);
      CarryRequire(item.SubmissionId.HasValue && ancestry.Invoices.TryGetValue((item.OriginalKind, item.SubmissionId.Value), out var anchor) &&
        anchor.JournalId == item.JournalId && anchor.SubmissionHash == facts.RootElement.GetProperty("SubmissionHash").GetString() &&
        anchor.JournalHash == facts.RootElement.GetProperty("JournalHash").GetString(),
        "The carried invoice is outside its exact consumed native source membership.");
    }
  }
}
