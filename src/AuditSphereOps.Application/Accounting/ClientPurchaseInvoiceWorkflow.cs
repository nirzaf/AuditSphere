using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientPurchaseInvoiceDraftRequest(Guid CommandId, Guid? InvoiceId, long ExpectedRevision,
  Guid PeriodId, Guid SupplierId, string VoucherReference, string SupplierInvoiceReference,
  DateOnly ReceiptDate, DateOnly DocumentDate, DateOnly AccountingDate, DateOnly SupplyDate, DateOnly DueDate,
  string Currency, ClientInvoiceMoneyPolicy Policy, IReadOnlyList<ClientInvoiceLineInput> Lines,
  decimal StatedNet, decimal StatedTax, decimal StatedGross, Guid? SourceReceiptId, string EvidenceReference);
public sealed record ClientPurchaseInvoiceLineSnapshot(int LineNumber, Guid AccountId, string AccountCode,
  string AccountName, string Description, string Quantity, string UnitPrice, string Discount, string Net, string Tax, string Gross);
public sealed record ClientPurchaseInvoiceSnapshot(string Version, string EngineVersion, Guid ProfileId, string ProfileRevision,
  Guid ChartVersionId, Guid SupplierId, string SupplierName, string VoucherReference, string SupplierInvoiceReference,
  string NormalizedSupplierReference, string Currency, string ReceiptDate, string DocumentDate, string AccountingDate,
  string SupplyDate, string DueDate, ClientInvoiceMoneyPolicy Policy, IReadOnlyList<ClientPurchaseInvoiceLineSnapshot> Lines,
  string Net, string Tax, string Gross, string StatedNet, string StatedTax, string StatedGross, bool LateArrival,
  Guid? SourceReceiptId, string? SourceReceiptHash, string EvidenceReference, int PossibleDuplicateCount);
public sealed record ClientPurchaseInvoiceDraftView(Guid Id, Guid InvoiceId, Guid ClientId, Guid PeriodId,
  Guid SupplierId, string VoucherReference, string SupplierInvoiceReference, string Revision, string SnapshotHash,
  Guid CreatedByUserId, string State, ClientPurchaseInvoiceSnapshot Snapshot);
public sealed record ClientPurchaseInvoiceSubmitRequest(Guid CommandId, Guid InvoiceId, string ExpectedDraftRevision,
  Guid PayableRoleId, string PreviewDigest);
public sealed record ClientPurchaseInvoiceReviewRequest(Guid CommandId, Guid SubmissionId, string Decision,
  string Reason, string DuplicateResolutionReason, string PreviewDigest);
public sealed record ClientPurchaseInvoicePostingLine(int LineNumber, Guid AccountId, string AccountCode,
  string AccountName, string Description, string Debit, string Credit);
public sealed record ClientPurchaseInvoiceManifest(string Version, Guid InvoiceId, Guid DraftId, string DraftHash,
  Guid SupplierId, string SupplierInvoiceReference, string NormalizedSupplierReference, string VoucherReference,
  string Currency, Guid ProfileId, string ProfileRevision, Guid PayableRoleId, Guid PayableAccountId,
  Guid PeriodId, string AccountingDate, string SupplyDate, string DocumentDate, string ReceiptDate, string DueDate,
  Guid MandateId, string MandateGeneration, bool LateArrival, int PossibleDuplicateCount,
  string StatedNet, string StatedTax, string StatedGross, Guid? SourceReceiptId, string? SourceReceiptHash,
  string EvidenceReference, IReadOnlyList<ClientPurchaseInvoicePostingLine> Lines);
public sealed record ClientPurchaseInvoicePreview(Guid InvoiceId, string Revision, string Currency, string Net,
  string Tax, string Gross, string Digest, bool LateArrival, IReadOnlyList<string> DuplicateWarnings,
  ClientPurchaseInvoiceManifest Manifest);
public sealed record ClientPurchaseInvoiceReviewPreview(Guid InvoiceId, Guid SubmissionId, Guid JournalId,
  string JournalRevision, string Digest, bool CanPost, string? PostingBlock, bool DuplicateWarning,
  string ReviewContextJson, ClientPurchaseInvoiceManifest Manifest);
public sealed record ClientPurchaseInvoiceReceipt(Guid CommandId, Guid InvoiceId, Guid SubmissionId,
  string Kind, Guid ActorUserId, string IntentHash, string Outcome, Guid? DecisionId);
public sealed record ClientPurchaseInvoiceView(Guid InvoiceId, Guid SupplierId, string SupplierInvoiceReference,
  string VoucherReference, string State, string Currency, string Gross, string DueDate, string ReceiptDate,
  bool LateArrival, bool DuplicateWarning, Guid SubmissionId, Guid? JournalId, Guid MakerId,
  string? Decision, string? DecisionReason, string? DuplicateResolutionReason, bool OpenItemCreated);

public static class ClientPurchaseInvoiceWorkflow
{
  private static readonly string[] Preparers = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] Reviewers = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static Task<CommandResult> Authorize(IClientAccountingDbContext db, ActorContext actor, Guid clientId, string[] roles, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, clientId, RequiredRoles: roles, InternalOnly: true), ct);
  private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
  private static string Money(decimal value) => value.ToString("F6", CultureInfo.InvariantCulture);
  private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
  private static string NormalizeReference(string value) => new(value.Normalize(NormalizationForm.FormKC).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
  private static ClientPurchaseInvoiceDraftView View(ClientPurchaseInvoiceDraft row)
  {
    if (Hash(row.SnapshotJson) != row.SnapshotHash) throw new InvalidOperationException("The retained purchase invoice snapshot identity is invalid.");
    var snapshot = JsonSerializer.Deserialize<ClientPurchaseInvoiceSnapshot>(row.SnapshotJson) ?? throw new InvalidOperationException("The retained purchase invoice snapshot is invalid.");
    return new(row.Id, row.InvoiceId, row.ClientId, row.PeriodId, row.SupplierId, row.VoucherReference,
      row.SupplierInvoiceReference, row.Revision.ToString(CultureInfo.InvariantCulture), row.SnapshotHash,
      row.CreatedByUserId, "DRAFT", snapshot);
  }

  public static async Task<CommandResult<ClientPurchaseInvoiceDraftView>> SaveDraftAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, ClientPurchaseInvoiceDraftRequest request, CancellationToken ct = default)
  {
    var voucher = (request.VoucherReference ?? "").Trim(); var supplierRef = (request.SupplierInvoiceReference ?? "").Trim();
    var evidence = (request.EvidenceReference ?? "").Trim();
    if (request.CommandId == Guid.Empty || clientId == Guid.Empty || request.PeriodId == Guid.Empty || request.SupplierId == Guid.Empty ||
        voucher.Length is 0 or > 100 || supplierRef.Length is 0 or > 200 || evidence.Length is 0 or > 2000 || request.ReceiptDate == default ||
        request.DocumentDate == default || request.AccountingDate == default || request.SupplyDate == default || request.DueDate < request.DocumentDate ||
        request.ReceiptDate < request.DocumentDate || request.ExpectedRevision < 0 || request.ExpectedRevision == long.MaxValue ||
        (request.InvoiceId is null ? request.ExpectedRevision != 0 : request.InvoiceId == Guid.Empty || request.ExpectedRevision < 1) ||
        request.Lines is null || request.Lines.Count is < 1 or > 99 || request.Lines.Any(x => x is null) ||
        !ClientInvoiceCalculator.ValidStoredAmount(request.StatedNet) || !ClientInvoiceCalculator.ValidStoredAmount(request.StatedTax) || !ClientInvoiceCalculator.ValidStoredAmount(request.StatedGross))
      return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.Accounting.MappingInvalid, "Provide exact client, supplier, document dates, supplier reference, voucher and bounded purchase lines.");
    if (request.Lines.Any(x => x.TaxTreatment != "NONE" || x.Taxes is null || x.Taxes.Count != 0))
      return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.GateBlocked, "Tax-bearing purchase invoices require the separately approved optional tax capability; untaxed purchases remain available.");
    var normalizedReference = NormalizeReference(supplierRef);
    if (normalizedReference.Length is 0 or > 200) return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.Accounting.MappingInvalid, "Supplier invoice reference must contain letters or digits.");
    var calculation = ClientInvoiceCalculator.Calculate("PURCHASE_INVOICE", request.Currency, request.Currency, request.Policy, request.Lines);
    if (!calculation.Valid || calculation.Tax != 0m || calculation.Gross != calculation.Net ||
        request.StatedNet != calculation.Net || request.StatedTax != 0m || request.StatedGross != calculation.Gross)
      return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.Accounting.MappingInvalid, calculation.Error ?? "Supplier-stated net, tax and gross must match the exact untaxed calculation.");
    var intent = Hash(JsonSerializer.Serialize(new { Version = "client-purchase-draft-v1", actor.FirmId, clientId, actor.UserId, Request = request }));
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var client = await db.PracticeClients.FromSqlInterpolated($"SELECT * FROM practice_clients WHERE firm_id={actor.FirmId} AND id={clientId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (client is null) return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var replay = await db.ClientPurchaseInvoiceDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.CreatedByUserId == actor.UserId && x.CommandId == request.CommandId, ct);
    if (replay is not null)
    {
      if (replay.IntentHash != intent) return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.IdempotencyConflict, "This purchase draft key is bound to different content.");
      await tx.CommitAsync(ct); return CommandResult<ClientPurchaseInvoiceDraftView>.Ok(View(replay));
    }
    var profile = await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.SourceMode == ClientAccountingSourceModes.NativeBookkeeping, ct);
    if (profile is null || !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct))
      return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.GateBlocked, "A current accepted native client bookkeeping service is required.");
    var period = await db.ClientReportingPeriods.FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE firm_id={actor.FirmId} AND client_id={clientId} AND id={request.PeriodId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (period is null || period.Status == AccountingWorkflowStates.Closed || request.AccountingDate < period.StartDate || request.AccountingDate > period.EndDate || period.Currency != request.Currency || profile.FunctionalCurrency != request.Currency)
      return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.GateBlocked, "Choose an open period in the client's functional currency containing the accounting date.");
    var supplier = await db.ClientBookkeepingCounterparties.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == request.SupplierId && (x.Role == "SUPPLIER" || x.Role == "BOTH"), ct);
    if (supplier is null) return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.ScopeDenied, "Choose a supplier counterparty belonging to this client book.");
    var charts = await db.ClientChartVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Status == AccountingWorkflowStates.Approved && x.EffectiveFrom <= request.AccountingDate && (x.EffectiveTo == null || x.EffectiveTo >= request.AccountingDate)).Take(2).ToListAsync(ct);
    if (charts.Count != 1) return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.GateBlocked, "Exactly one approved client chart must cover the accounting date.");
    var chart = charts[0]; var codes = request.Lines.Select(x => x.AccountCode).Distinct().ToArray();
    var accounts = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.ChartVersionId == chart.Id && x.IsPosting && x.Status == AccountingWorkflowStates.Active && (x.AccountType == "EXPENSE" || x.AccountType == "ASSET") && codes.Contains(x.AccountCode)).ToListAsync(ct);
    if (accounts.Count != codes.Length) return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.Accounting.MappingInvalid, "Each untaxed purchase line needs an active expense or asset posting account in the approved client chart.");
    ClientPurchaseInvoiceDraft? previous = null;
    if (request.InvoiceId is { } invoiceId)
    {
      previous = await db.ClientPurchaseInvoiceDrafts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.InvoiceId == invoiceId).OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
      if (previous is null || previous.CreatedByUserId != actor.UserId || previous.Revision != request.ExpectedRevision || previous.VoucherReference != voucher)
        return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.StaleRevision, "Only the original maker can revise the exact current purchase draft; voucher identity is fixed.");
      var lastSubmission = await db.ClientPurchaseInvoiceSubmissions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.InvoiceId == invoiceId).OrderByDescending(x => x.DraftRevision).FirstOrDefaultAsync(ct);
      if (lastSubmission is not null && !await db.ClientPurchaseInvoiceDecisions.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.SubmissionId == lastSubmission.Id && x.Decision == "RETURN", ct))
        return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.ProtectedState, "Submitted and posted purchase evidence is frozen; an independent return is required before a new revision.");
    }
    else if (await db.ClientPurchaseInvoiceDrafts.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Revision == 1 && x.VoucherReference == voucher, ct))
      return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.IdempotencyConflict, "The internal purchase voucher already exists.");
    var currentId = previous?.InvoiceId ?? Guid.Empty;
    var duplicateCount = await db.ClientPurchaseInvoiceDrafts.Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.SupplierId == supplier.Id && x.NormalizedSupplierReference == normalizedReference && x.InvoiceId != currentId)
      .Select(x => x.InvoiceId).Distinct().CountAsync(ct);
    var effectiveSupplier = await ClientBookkeepingCounterpartyWorkspace.EffectiveView(db, supplier, ct);
    var savedLines = calculation.Lines.Select(line => { var account = accounts.Single(x => x.AccountCode == line.AccountCode); return new ClientPurchaseInvoiceLineSnapshot(line.LineNumber, account.Id, account.AccountCode, account.AccountName,
      line.Description, line.Quantity.ToString(CultureInfo.InvariantCulture), line.UnitPrice.ToString(CultureInfo.InvariantCulture), line.Discount.ToString(CultureInfo.InvariantCulture), Money(line.Net), Money(line.Tax), Money(line.Gross)); }).ToArray();
    var snapshot = new ClientPurchaseInvoiceSnapshot("client-purchase-invoice-v1", calculation.EngineVersion, profile.Id,
      profile.Revision.ToString(CultureInfo.InvariantCulture), chart.Id, supplier.Id, effectiveSupplier.DisplayName, voucher,
      supplierRef, normalizedReference, request.Currency, Date(request.ReceiptDate), Date(request.DocumentDate), Date(request.AccountingDate),
      Date(request.SupplyDate), Date(request.DueDate), request.Policy, savedLines, Money(calculation.Net), Money(calculation.Tax),
      Money(calculation.Gross), Money(request.StatedNet), Money(request.StatedTax), Money(request.StatedGross), request.ReceiptDate > request.DocumentDate,
      request.SourceReceiptId, null, evidence, duplicateCount);
    string? sourceHash = null;
    if (request.SourceReceiptId is { } receiptId)
    {
      var receipt = await db.SourceReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == receiptId, ct);
      if (receipt is null || receipt.ByteCount < 0 || receipt.Sha256Digest.Length != 64) return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.ScopeDenied, "Select a verified source receipt scoped to this client.");
      sourceHash = receipt.Sha256Digest;
      snapshot = snapshot with { SourceReceiptHash = sourceHash };
    }
    var json = JsonSerializer.Serialize(snapshot); var now = DateTimeOffset.UtcNow;
    var row = new ClientPurchaseInvoiceDraft { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
      InvoiceId = previous?.InvoiceId ?? Guid.CreateVersion7(), Revision = (previous?.Revision ?? 0) + 1,
      PreviousRevisionId = previous?.Id, PreviousRevision = previous?.Revision, CommandId = request.CommandId, IntentHash = intent,
      PeriodId = request.PeriodId, SupplierId = supplier.Id, ChartVersionId = chart.Id, SupplierInvoiceReference = supplierRef,
      NormalizedSupplierReference = normalizedReference, VoucherReference = voucher, ReceiptDate = request.ReceiptDate,
      DocumentDate = request.DocumentDate, AccountingDate = request.AccountingDate, SupplyDate = request.SupplyDate,
      DueDate = request.DueDate, Currency = request.Currency, NetAmount = calculation.Net, TaxAmount = calculation.Tax,
      GrossAmount = calculation.Gross, SnapshotJson = json, SnapshotHash = Hash(json), CreatedByUserId = actor.UserId, CreatedAt = now };
    db.ClientPurchaseInvoiceDrafts.Add(row); await db.SaveChangesAsync(ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded || !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct))
      return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.GateBlocked, "Authority changed before the purchase draft was saved.");
    await tx.CommitAsync(ct); return CommandResult<ClientPurchaseInvoiceDraftView>.Ok(View(row));
  }

  public static async Task<CommandResult<ClientPurchaseInvoiceDraftView>> GetDraftAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid invoiceId, CancellationToken ct = default)
  {
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var row = await db.ClientPurchaseInvoiceDrafts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.InvoiceId == invoiceId)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (row is null) return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    try { return CommandResult<ClientPurchaseInvoiceDraftView>.Ok(View(row)); }
    catch (InvalidOperationException) { return CommandResult<ClientPurchaseInvoiceDraftView>.Fail(ErrorCodes.ProtectedState, "The retained purchase draft is invalid."); }
  }

  private static async Task<CommandResult<ClientPurchaseInvoicePreview>> BuildPreviewAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, ClientPurchaseInvoiceDraft draft, ClientPurchaseInvoiceSubmitRequest request, CancellationToken ct)
  {
    if (!long.TryParse(request.ExpectedDraftRevision, NumberStyles.None, CultureInfo.InvariantCulture, out var expected) || expected != draft.Revision ||
        Hash(draft.SnapshotJson) != draft.SnapshotHash) return CommandResult<ClientPurchaseInvoicePreview>.Fail(ErrorCodes.StaleRevision, "Read the exact current purchase draft revision.");
    ClientPurchaseInvoiceSnapshot? saved;
    try { saved = JsonSerializer.Deserialize<ClientPurchaseInvoiceSnapshot>(draft.SnapshotJson); } catch (JsonException) { saved = null; }
    if (saved is null || saved.Version != "client-purchase-invoice-v1" || saved.SupplierId != draft.SupplierId || saved.NormalizedSupplierReference != draft.NormalizedSupplierReference || saved.Lines.Count is < 1 or > 99)
      return CommandResult<ClientPurchaseInvoicePreview>.Fail(ErrorCodes.ProtectedState, "The purchase draft snapshot does not match its client and supplier identity.");
    var profile = await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.SourceMode == ClientAccountingSourceModes.NativeBookkeeping, ct);
    var mandate = await db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == clientId && x.EngagementId == null && x.ServiceRoute == "BOOKKEEPING")
      .OrderByDescending(x => x.Generation).ThenByDescending(x => x.DecidedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    if (profile is null || profile.Id != saved.ProfileId || profile.Revision.ToString(CultureInfo.InvariantCulture) != saved.ProfileRevision || profile.FunctionalCurrency != draft.Currency || mandate is not { Decision: "Accepted", Conditions: null or "" })
      return CommandResult<ClientPurchaseInvoicePreview>.Fail(ErrorCodes.GateBlocked, "The current accepted client bookkeeping mandate and frozen currency profile are required.");
    var accountingDate = DateOnly.ParseExact(saved.AccountingDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    var period = await db.ClientReportingPeriods.FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE firm_id={actor.FirmId} AND client_id={clientId} AND id={draft.PeriodId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (period is null || period.Status == AccountingWorkflowStates.Closed || accountingDate < period.StartDate || accountingDate > period.EndDate || period.Currency != draft.Currency)
      return CommandResult<ClientPurchaseInvoicePreview>.Fail(ErrorCodes.GateBlocked, "The original accounting period must still be open for purchase posting.");
    var supplier = await db.ClientBookkeepingCounterparties.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == saved.SupplierId && (x.Role == "SUPPLIER" || x.Role == "BOTH"), ct);
    var chart = await db.ClientChartVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == saved.ChartVersionId && x.Status == AccountingWorkflowStates.Approved && x.EffectiveFrom <= accountingDate && (x.EffectiveTo == null || x.EffectiveTo >= accountingDate), ct);
    var role = await db.ClientAccountRoleConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == request.PayableRoleId && x.Role == "AP" && x.ChartVersionId == saved.ChartVersionId && x.EffectiveFrom <= accountingDate && (x.EffectiveTo == null || x.EffectiveTo >= accountingDate), ct);
    var roleDecision = role is null ? null : await db.ClientAccountRoleDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.ConfigurationId == role.Id && x.Decision == "APPROVE", ct);
    if (supplier is null || chart is null || role is null || roleDecision is null || role.AccountId == Guid.Empty)
      return CommandResult<ClientPurchaseInvoicePreview>.Fail(ErrorCodes.GateBlocked, "An active same-client supplier, approved purchase chart and independent AP role are required.");
    var ids = saved.Lines.Select(x => x.AccountId).Append(role.AccountId).Distinct().ToArray();
    var accounts = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.ChartVersionId == chart.Id && ids.Contains(x.Id) && x.IsPosting && x.Status == AccountingWorkflowStates.Active).ToListAsync(ct);
    if (accounts.Count != ids.Length || accounts.SingleOrDefault(x => x.Id == role.AccountId) is not { AccountType: "LIABILITY" } ||
        saved.Lines.Any(line => !accounts.Any(a => a.Id == line.AccountId && a.AccountCode == line.AccountCode && a.AccountName == line.AccountName && a.AccountType is "EXPENSE" or "ASSET")))
      return CommandResult<ClientPurchaseInvoicePreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "Frozen expense/asset lines and the approved AP control account must remain active.");
    var inputLines = saved.Lines.Select(x => new ClientInvoiceLineInput(x.Description, x.AccountCode,
      decimal.Parse(x.Quantity, CultureInfo.InvariantCulture), decimal.Parse(x.UnitPrice, CultureInfo.InvariantCulture),
      decimal.Parse(x.Discount, CultureInfo.InvariantCulture), "NONE", [])).ToArray();
    var calc = ClientInvoiceCalculator.Calculate("PURCHASE_INVOICE", draft.Currency, profile.FunctionalCurrency, saved.Policy, inputLines);
    if (!calc.Valid || calc.Net != draft.NetAmount || calc.Tax != draft.TaxAmount || calc.Gross != draft.GrossAmount ||
        calc.Net != decimal.Parse(saved.StatedNet, CultureInfo.InvariantCulture) || calc.Gross != decimal.Parse(saved.StatedGross, CultureInfo.InvariantCulture))
      return CommandResult<ClientPurchaseInvoicePreview>.Fail(ErrorCodes.ProtectedState, "The supplier-stated totals no longer reconcile to the frozen untaxed calculation.");
    var lines = new List<ClientPurchaseInvoicePostingLine>();
    foreach (var group in saved.Lines.GroupBy(x => x.AccountId).OrderBy(x => x.First().AccountCode, StringComparer.Ordinal))
    {
      var amount = group.Sum(x => decimal.Parse(x.Net, CultureInfo.InvariantCulture));
      var account = accounts.Single(x => x.Id == group.Key);
      lines.Add(new(lines.Count + 1, account.Id, account.AccountCode, account.AccountName, "Supplier invoice purchase", Money(amount), "0.000000"));
    }
    var ap = accounts.Single(x => x.Id == role.AccountId);
    lines.Add(new(lines.Count + 1, ap.Id, ap.AccountCode, ap.AccountName, "Supplier payable", "0.000000", Money(calc.Gross)));
    if (!ClientOperationalJournalCalculator.CalculateDocument(lines.Select(x => new ClientOperationalJournalLineInput(x.AccountCode, x.Description,
      decimal.Parse(x.Debit, CultureInfo.InvariantCulture), decimal.Parse(x.Credit, CultureInfo.InvariantCulture))).ToArray()).Valid)
      return CommandResult<ClientPurchaseInvoicePreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "The exact purchase journal must balance before review.");
    string? receiptHash = saved.SourceReceiptHash;
    if (saved.SourceReceiptId is { } receiptId)
    {
      var receipt = await db.SourceReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == receiptId, ct);
      if (receipt is null || receipt.Sha256Digest != receiptHash || receipt.ByteCount < 0) return CommandResult<ClientPurchaseInvoicePreview>.Fail(ErrorCodes.ProtectedState, "The captured supplier evidence receipt changed.");
    }
    else if (string.IsNullOrWhiteSpace(saved.EvidenceReference)) return CommandResult<ClientPurchaseInvoicePreview>.Fail(ErrorCodes.GateBlocked, "Supplier source evidence is required.");
    var duplicateRows = await db.ClientPurchaseInvoiceDrafts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.SupplierId == saved.SupplierId && x.NormalizedSupplierReference == saved.NormalizedSupplierReference && x.InvoiceId != draft.InvoiceId)
      .OrderBy(x => x.InvoiceId).ThenByDescending(x => x.Revision).Take(601).ToListAsync(ct);
    if (duplicateRows.Count > 600) return CommandResult<ClientPurchaseInvoicePreview>.Fail(ErrorCodes.GateBlocked, "Supplier duplicate history exceeds the interactive review limit.");
    var duplicateWarnings = duplicateRows.GroupBy(x => x.InvoiceId).Select(x => x.First())
      .OrderBy(x => x.DocumentDate).ThenBy(x => x.InvoiceId).Take(6)
      .Select(x => x.VoucherReference + " · " + Money(x.GrossAmount) + " · " + Date(x.DocumentDate)).ToList();
    var manifest = new ClientPurchaseInvoiceManifest("client-purchase-submission-v1", draft.InvoiceId, draft.Id, draft.SnapshotHash,
      supplier.Id, saved.SupplierInvoiceReference, saved.NormalizedSupplierReference, saved.VoucherReference, draft.Currency,
      profile.Id, profile.Revision.ToString(CultureInfo.InvariantCulture), role.Id, role.AccountId, draft.PeriodId,
      saved.AccountingDate, saved.SupplyDate, saved.DocumentDate, saved.ReceiptDate, saved.DueDate, mandate.Id,
      mandate.Generation.ToString(CultureInfo.InvariantCulture), saved.LateArrival, duplicateWarnings.Count,
      saved.StatedNet, saved.StatedTax, saved.StatedGross, saved.SourceReceiptId, receiptHash, saved.EvidenceReference, lines);
    var digest = Hash(JsonSerializer.Serialize(new { Version = "client-purchase-preview-v1", actor.FirmId, clientId, draft.InvoiceId, draft.Revision, Manifest = manifest, DuplicateWarnings = duplicateWarnings, period.Status }));
    return CommandResult<ClientPurchaseInvoicePreview>.Ok(new(draft.InvoiceId, draft.Revision.ToString(CultureInfo.InvariantCulture), draft.Currency,
      Money(calc.Net), Money(calc.Tax), Money(calc.Gross), digest, saved.LateArrival, duplicateWarnings, manifest));
  }

  public static async Task<CommandResult<ClientPurchaseInvoicePreview>> PreviewAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, ClientPurchaseInvoiceSubmitRequest request, CancellationToken ct = default)
  {
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<ClientPurchaseInvoicePreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var draft = await db.ClientPurchaseInvoiceDrafts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.InvoiceId == request.InvoiceId).OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (draft is null) return CommandResult<ClientPurchaseInvoicePreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var result = await BuildPreviewAsync(db, actor, clientId, draft, request, ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<ClientPurchaseInvoicePreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return result;
  }

  public static async Task<CommandResult<ClientPurchaseInvoiceReceipt>> SubmitAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, ClientPurchaseInvoiceSubmitRequest request, CancellationToken ct = default)
  {
    var intent = Hash(JsonSerializer.Serialize(new { Version = "client-purchase-submit-v1", actor.FirmId, clientId, actor.UserId, Request = request }));
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded || !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct))
      return CommandResult<ClientPurchaseInvoiceReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var client = await db.PracticeClients.FromSqlInterpolated($"SELECT * FROM practice_clients WHERE firm_id={actor.FirmId} AND id={clientId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (client is null) return CommandResult<ClientPurchaseInvoiceReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var replay = await db.ClientPurchaseInvoiceSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.CreatedByUserId == actor.UserId && x.CommandId == request.CommandId, ct);
    if (replay is not null) return replay.IntentHash == intent ? CommandResult<ClientPurchaseInvoiceReceipt>.Ok(Receipt(replay)) : CommandResult<ClientPurchaseInvoiceReceipt>.Fail(ErrorCodes.IdempotencyConflict, "The purchase submission key is bound to different content.");
    var draft = await db.ClientPurchaseInvoiceDrafts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.InvoiceId == request.InvoiceId).OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (draft is null || draft.CreatedByUserId != actor.UserId) return CommandResult<ClientPurchaseInvoiceReceipt>.Fail(ErrorCodes.ScopeDenied, "Only the assigned maker can submit this purchase preparation.");
    var preview = await BuildPreviewAsync(db, actor, clientId, draft, request, ct);
    if (!preview.Succeeded || preview.Value!.Digest != request.PreviewDigest) return CommandResult<ClientPurchaseInvoiceReceipt>.Fail(ErrorCodes.StaleRevision, "Preview the exact purchase evidence and current duplicate warnings before submission.");
    var m = preview.Value.Manifest;
    var journal = new ClientOperationalJournal { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId, PeriodId = draft.PeriodId,
      JournalNumber = "CLIENT-PURCHASE-" + draft.InvoiceId.ToString("N"), Description = "Client supplier invoice " + draft.VoucherReference,
      PostingDate = draft.AccountingDate, Currency = draft.Currency, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow };
    db.ClientOperationalJournals.Add(journal); await db.SaveChangesAsync(ct);
    foreach (var line in m.Lines) db.ClientOperationalJournalLines.Add(new() { Id = Guid.CreateVersion7(), FirmId = actor.FirmId,
      ClientId = clientId, JournalId = journal.Id, LineNumber = line.LineNumber, ClientAccountId = line.AccountId,
      AccountCode = line.AccountCode, AccountName = line.AccountName, Description = line.Description,
      Debit = decimal.Parse(line.Debit, CultureInfo.InvariantCulture), Credit = decimal.Parse(line.Credit, CultureInfo.InvariantCulture) });
    await db.SaveChangesAsync(ct); journal.Status = "SUBMITTED"; journal.SubmittedAt = DateTimeOffset.UtcNow; journal.Revision++; await db.SaveChangesAsync(ct);
    var json = JsonSerializer.Serialize(m);
    var submission = new ClientPurchaseInvoiceSubmission { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
      InvoiceId = draft.InvoiceId, DraftId = draft.Id, DraftRevision = draft.Revision, SupplierId = draft.SupplierId,
      SupplierInvoiceReference = draft.SupplierInvoiceReference, NormalizedSupplierReference = draft.NormalizedSupplierReference,
      JournalId = journal.Id, JournalSubmittedRevision = journal.Revision, CommandId = request.CommandId, IntentHash = intent,
      ManifestJson = json, ManifestHash = Hash(json), PreviewDigest = request.PreviewDigest, SourceBasis = m.EvidenceReference,
      SourceReceiptId = m.SourceReceiptId, SourceReceiptHash = m.SourceReceiptHash, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow };
    db.ClientPurchaseInvoiceSubmissions.Add(submission); await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct); return CommandResult<ClientPurchaseInvoiceReceipt>.Ok(Receipt(submission));
  }

  private static async Task<CommandResult<ClientPurchaseInvoiceReviewPreview>> BuildReviewPreviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, ClientPurchaseInvoiceSubmission submission, CancellationToken ct)
  {
    if (submission.ManifestHash != Hash(submission.ManifestJson)) return CommandResult<ClientPurchaseInvoiceReviewPreview>.Fail(ErrorCodes.ProtectedState, "The retained supplier invoice manifest identity is invalid.");
    ClientPurchaseInvoiceManifest? manifest;
    try { manifest = JsonSerializer.Deserialize<ClientPurchaseInvoiceManifest>(submission.ManifestJson); } catch (JsonException) { manifest = null; }
    var journal = await db.ClientOperationalJournals.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == submission.JournalId, ct);
    if (manifest is null || manifest.Version != "client-purchase-submission-v1" || manifest.InvoiceId != submission.InvoiceId || journal is null ||
        journal.Revision != submission.JournalSubmittedRevision || journal.Status != "SUBMITTED")
      return CommandResult<ClientPurchaseInvoiceReviewPreview>.Fail(ErrorCodes.StaleRevision, "Read the current submitted supplier invoice revision.");
    var draft = await db.ClientPurchaseInvoiceDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == submission.DraftId, ct);
    if (draft is null) return CommandResult<ClientPurchaseInvoiceReviewPreview>.Fail(ErrorCodes.ProtectedState, "The linked supplier invoice draft is missing.");
    var request = new ClientPurchaseInvoiceSubmitRequest(Guid.CreateVersion7(), submission.InvoiceId,
      draft.Revision.ToString(CultureInfo.InvariantCulture), manifest.PayableRoleId, submission.PreviewDigest);
    var fresh = await BuildPreviewAsync(db, actor, clientId, draft, request, ct);
    var context = JsonSerializer.Serialize(new { Version = "client-purchase-review-v1", actor.FirmId, ClientId = clientId,
      ActorId = actor.UserId, submission.Id, submission.ManifestHash, JournalId = journal.Id, JournalRevision = journal.Revision,
      JournalStatus = journal.Status, CurrentPreviewDigest = fresh.Value?.Digest, FreshManifest = fresh.Value?.Manifest,
      DuplicateWarnings = fresh.Value?.DuplicateWarnings, PostingBlock = fresh.ErrorCode,
      AcceptedService = await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct) });
    var canPost = fresh.Succeeded && await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct);
    return CommandResult<ClientPurchaseInvoiceReviewPreview>.Ok(new(submission.InvoiceId, submission.Id, journal.Id,
      journal.Revision.ToString(CultureInfo.InvariantCulture), Hash(context), canPost, fresh.ErrorCode,
      fresh.Value?.DuplicateWarnings.Count > 0, context, manifest));
  }

  public static async Task<CommandResult<ClientPurchaseInvoiceReviewPreview>> PreviewReviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid submissionId, CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<ClientPurchaseInvoiceReviewPreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var submission = await db.ClientPurchaseInvoiceSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == submissionId, ct);
    if (submission is null) return CommandResult<ClientPurchaseInvoiceReviewPreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var result = await BuildReviewPreviewAsync(db, actor, clientId, submission, ct);
    await tx.CommitAsync(ct); return result;
  }

  public static async Task<CommandResult<ClientPurchaseInvoiceReceipt>> ReviewAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, ClientPurchaseInvoiceReviewRequest request, CancellationToken ct = default)
  {
    var decision = (request.Decision ?? "").Trim().ToUpperInvariant(); var reason = (request.Reason ?? "").Trim();
    var duplicateReason = (request.DuplicateResolutionReason ?? "").Trim();
    if (request.CommandId == Guid.Empty || decision is not ("APPROVE" or "RETURN") || reason.Length is 0 or > 2000 || duplicateReason.Length > 2000)
      return CommandResult<ClientPurchaseInvoiceReceipt>.Fail(ErrorCodes.Accounting.MappingInvalid, "Provide an exact independent decision, review reason, and bounded duplicate resolution rationale.");
    var intent = Hash(JsonSerializer.Serialize(new { Version = "client-purchase-review-command-v1", actor.FirmId, clientId, actor.UserId, Request = request with { Decision = decision, Reason = reason, DuplicateResolutionReason = duplicateReason } }));
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await Authorize(db, actor, clientId, Reviewers, ct)).Succeeded || !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct))
      return CommandResult<ClientPurchaseInvoiceReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var client = await db.PracticeClients.FromSqlInterpolated($"SELECT * FROM practice_clients WHERE firm_id={actor.FirmId} AND id={clientId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (client is null) return CommandResult<ClientPurchaseInvoiceReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var replay = await db.ClientPurchaseInvoiceDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.ActorUserId == actor.UserId && x.CommandId == request.CommandId, ct);
    if (replay is not null)
    {
      if (replay.IntentHash != intent) return CommandResult<ClientPurchaseInvoiceReceipt>.Fail(ErrorCodes.IdempotencyConflict, "This purchase review key is bound to different intent.");
      var prior = await db.ClientPurchaseInvoiceSubmissions.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == replay.SubmissionId, ct);
      await tx.CommitAsync(ct); return CommandResult<ClientPurchaseInvoiceReceipt>.Ok(Receipt(replay, prior));
    }
    var submission = await db.ClientPurchaseInvoiceSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == request.SubmissionId, ct);
    if (submission is null || submission.CreatedByUserId == actor.UserId) return CommandResult<ClientPurchaseInvoiceReceipt>.Fail(ErrorCodes.ScopeDenied, "An independent scoped reviewer is required.");
    var preview = await BuildReviewPreviewAsync(db, actor, clientId, submission, ct);
    if (!preview.Succeeded || preview.Value!.Digest != request.PreviewDigest || decision == "APPROVE" && !preview.Value.CanPost ||
        decision == "APPROVE" && preview.Value.DuplicateWarning && duplicateReason.Length == 0)
      return CommandResult<ClientPurchaseInvoiceReceipt>.Fail(ErrorCodes.StaleRevision, "Refresh the exact supplier invoice, current duplicate warnings and resolution rationale before approval.");
    if (await db.ClientPurchaseInvoiceDecisions.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.SubmissionId == submission.Id, ct))
      return CommandResult<ClientPurchaseInvoiceReceipt>.Fail(ErrorCodes.IdempotencyConflict, "This supplier invoice already has an immutable decision.");
    var journal = await db.ClientOperationalJournals.FromSqlInterpolated($"SELECT * FROM client_operational_journals WHERE firm_id={actor.FirmId} AND client_id={clientId} AND id={submission.JournalId} FOR UPDATE").SingleAsync(ct);
    var now = DateTimeOffset.UtcNow;
    var retained = new ClientPurchaseInvoiceDecision { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
      SubmissionId = submission.Id, CommandId = request.CommandId, IntentHash = intent, Decision = decision,
      Reason = reason, DuplicateResolutionReason = duplicateReason, PreviewDigest = request.PreviewDigest,
      ReviewContextJson = preview.Value.ReviewContextJson, ActorUserId = actor.UserId, CreatedAt = now };
    db.ClientPurchaseInvoiceDecisions.Add(retained);
    db.ClientOperationalJournalDecisions.Add(new() { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
      JournalId = journal.Id, JournalRevision = submission.JournalSubmittedRevision, Decision = decision, Reason = reason,
      ActorUserId = actor.UserId, CreatedAt = now });
    journal.Status = decision == "APPROVE" ? "POSTED" : "RETURNED"; journal.Revision++;
    if (decision == "APPROVE") { journal.PostedByUserId = actor.UserId; journal.PostedAt = now; }
    await db.SaveChangesAsync(ct);
    if (decision == "APPROVE")
    {
      var draft = await db.ClientPurchaseInvoiceDrafts.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == submission.DraftId, ct);
      db.ClientPurchaseInvoiceOpenItems.Add(new() { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
        InvoiceId = submission.InvoiceId, SubmissionId = submission.Id, JournalId = journal.Id, SupplierId = submission.SupplierId,
        Currency = draft.Currency, OriginalAmount = draft.GrossAmount, DueDate = draft.DueDate, PostedAt = now });
      db.ClientOperationalPostingReceipts.Add(new() { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
        CommandId = request.CommandId, JournalId = journal.Id, ActorUserId = actor.UserId,
        SubmittedRevision = submission.JournalSubmittedRevision, PostedRevision = journal.Revision,
        IntentHash = intent, PreviewDigest = request.PreviewDigest, RecordedAt = now });
      await db.SaveChangesAsync(ct);
    }
    if (!(await Authorize(db, actor, clientId, Reviewers, ct)).Succeeded || !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct))
      return CommandResult<ClientPurchaseInvoiceReceipt>.Fail(ErrorCodes.GateBlocked, "Authority changed before supplier invoice review committed.");
    await tx.CommitAsync(ct); return CommandResult<ClientPurchaseInvoiceReceipt>.Ok(Receipt(retained, submission));
  }

  public static async Task<CommandResult<IReadOnlyList<ClientPurchaseInvoiceView>>> GetInvoicesAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, CancellationToken ct = default)
  {
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<IReadOnlyList<ClientPurchaseInvoiceView>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var submissions = await db.ClientPurchaseInvoiceSubmissions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId)
      .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(101).ToListAsync(ct);
    if (submissions.Count > 100) return CommandResult<IReadOnlyList<ClientPurchaseInvoiceView>>.Fail(ErrorCodes.GateBlocked, "Supplier invoice history exceeds the interactive limit.");
    var ids = submissions.Select(x => x.Id).ToArray();
    var decisions = ids.Length == 0 ? new Dictionary<Guid, ClientPurchaseInvoiceDecision>() : await db.ClientPurchaseInvoiceDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && ids.Contains(x.SubmissionId)).ToDictionaryAsync(x => x.SubmissionId, ct);
    var draftIds = submissions.Select(x => x.DraftId).ToArray();
    var drafts = draftIds.Length == 0 ? new Dictionary<Guid, ClientPurchaseInvoiceDraft>() : await db.ClientPurchaseInvoiceDrafts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && draftIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var itemIds = ids.Length == 0 ? new HashSet<Guid>() : await db.ClientPurchaseInvoiceOpenItems.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && ids.Contains(x.SubmissionId)).Select(x => x.SubmissionId).ToHashSetAsync(ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<IReadOnlyList<ClientPurchaseInvoiceView>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult<IReadOnlyList<ClientPurchaseInvoiceView>>.Ok(submissions.Select(x => {
      decisions.TryGetValue(x.Id, out var decision); drafts.TryGetValue(x.DraftId, out var draft);
      ClientPurchaseInvoiceSnapshot? snapshot = null; if (draft is not null && Hash(draft.SnapshotJson) == draft.SnapshotHash) try { snapshot = JsonSerializer.Deserialize<ClientPurchaseInvoiceSnapshot>(draft.SnapshotJson); } catch (JsonException) { }
      return new ClientPurchaseInvoiceView(x.InvoiceId, x.SupplierId, x.SupplierInvoiceReference, snapshot?.VoucherReference ?? "", decision is null ? "SUBMITTED" : decision.Decision == "APPROVE" ? "POSTED" : "RETURNED",
        draft?.Currency ?? "", draft is null ? "" : Money(draft.GrossAmount), draft is null ? "" : Date(draft.DueDate), snapshot?.ReceiptDate ?? "", snapshot?.LateArrival ?? false,
        snapshot?.PossibleDuplicateCount > 0, x.Id, x.JournalId, x.CreatedByUserId, decision?.Decision, decision?.Reason, decision?.DuplicateResolutionReason, itemIds.Contains(x.Id));
    }).ToArray());
  }

  private static ClientPurchaseInvoiceReceipt Receipt(ClientPurchaseInvoiceSubmission row) => new(row.CommandId, row.InvoiceId, row.Id, "SUBMIT", row.CreatedByUserId, row.IntentHash, "SUBMITTED", null);
  private static ClientPurchaseInvoiceReceipt Receipt(ClientPurchaseInvoiceDecision decision, ClientPurchaseInvoiceSubmission row) =>
    new(decision.CommandId, row.InvoiceId, row.Id, "REVIEW", decision.ActorUserId, decision.IntentHash, decision.Decision == "APPROVE" ? "POSTED" : "RETURNED", decision.Id);
}
