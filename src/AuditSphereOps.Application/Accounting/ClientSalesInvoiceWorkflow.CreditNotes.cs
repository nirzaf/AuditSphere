using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientSalesCreditLineInput(int OriginalLineNumber, decimal Amount);
public sealed record ClientSalesCreditPreviewRequest(Guid CreditNoteId, string CreditNoteReference, Guid InvoiceId,
  Guid PeriodId, DateOnly PostingDate, string Reason, Guid? SourceReceiptId, string SourceBasis,
  IReadOnlyList<ClientSalesCreditLineInput> Lines);
public sealed record ClientSalesCreditSubmitRequest(Guid CommandId, ClientSalesCreditPreviewRequest Credit,
  string PreviewDigest);
public sealed record ClientSalesCreditReviewRequest(Guid CommandId, Guid SubmissionId, string Decision,
  string Reason, string PreviewDigest);
public sealed record ClientSalesCreditPostingLine(int LineNumber, int OriginalLineNumber, Guid AccountId,
  string AccountCode, string AccountName, string Description, string Debit, string Credit, string CreditAmount);
public sealed record ClientSalesCreditManifest(string Version, Guid CreditNoteId, string CreditNoteReference,
  Guid InvoiceId, Guid OriginalSubmissionId, Guid OriginalOpenItemId, Guid CustomerId, string Currency,
  string OriginalManifestHash, string OriginalTaxTreatment, Guid ProfileId, string ProfileRevision,
  Guid ReceivableRoleId, Guid ReceivableAccountId, Guid PeriodId, string PostingDate,
  Guid MandateId, string MandateGeneration, string Reason, Guid? SourceReceiptId, string? SourceReceiptHash,
  string SourceBasis, string TotalCredit, IReadOnlyList<ClientSalesCreditPostingLine> Lines);
public sealed record ClientSalesCreditPreview(Guid CreditNoteId, Guid InvoiceId, string CreditNoteReference,
  string Currency, string TotalCredit, string Digest, IReadOnlyList<ClientSalesCreditPostingLine> Lines,
  string OriginalAmount, string PreviouslyCredited, string RemainingCreditLimit, ClientSalesCreditManifest Manifest);
public sealed record ClientSalesCreditReviewPreview(Guid CreditNoteId, Guid InvoiceId, Guid SubmissionId,
  Guid JournalId, string JournalRevision, string Digest, bool CanPost, string? PostingBlock,
  string ReviewContextJson, ClientSalesCreditManifest Manifest);
public sealed record ClientSalesCreditCommandReceipt(Guid CommandId, Guid CreditNoteId, Guid InvoiceId,
  Guid SubmissionId, string Kind, Guid ActorUserId, string IntentHash, string Outcome, Guid? DecisionId);
public sealed record ClientSalesCreditNoteView(Guid CreditNoteId, string CreditNoteReference, Guid SubmissionId,
  string State, string Currency, string Amount, string PostingDate, string Reason, Guid MakerId,
  string? Decision, string? DecisionReason, bool UnappliedCustomerCredit, string? JournalId);

public static partial class ClientSalesInvoiceWorkflow
{
  private sealed record CreditOriginal(ClientSalesInvoiceSubmission Submission, ClientSalesInvoiceDraft Draft,
    ClientSalesInvoiceOpenItem OpenItem, ClientSalesInvoiceManifest Manifest, ClientSalesInvoiceDraftSnapshot Snapshot,
    ClientAccountingProfile Profile, ClientAccountRoleConfiguration ReceivableRole, ClientAccountRoleDecision ReceivableDecision,
    ClientReportingPeriod Period, ClientChartVersion Chart, AcceptanceDecision Mandate);

  private static ClientSalesCreditPreviewRequest Normalize(ClientSalesCreditPreviewRequest r) => r with {
    CreditNoteReference = (r.CreditNoteReference ?? "").Trim(), Reason = (r.Reason ?? "").Trim(), SourceBasis = (r.SourceBasis ?? "").Trim(),
    Lines = r.Lines ?? [] };

  private static async Task<CommandResult<CreditOriginal>> LoadCreditOriginalAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid invoiceId, Guid periodId, DateOnly postingDate, CancellationToken ct)
  {
    var profile = await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId, ct);
    var mandate = await db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.PracticeClientId == clientId && x.EngagementId == null && x.ServiceRoute == "BOOKKEEPING")
      .OrderByDescending(x => x.Generation).ThenByDescending(x => x.DecidedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    if (profile is null || profile.SourceMode != ClientAccountingSourceModes.NativeBookkeeping ||
        mandate is not { Decision: "Accepted", Conditions: null or "" })
      return CommandResult<CreditOriginal>.Fail(ErrorCodes.GateBlocked, "Current accepted native client bookkeeping service is required.");
    var original = await db.ClientSalesInvoiceSubmissions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.InvoiceId == invoiceId).OrderByDescending(x => x.DraftRevision).FirstOrDefaultAsync(ct);
    if (original is null) return CommandResult<CreditOriginal>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var decision = await db.ClientSalesInvoiceDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.SubmissionId == original.Id && x.Decision == "APPROVE", ct);
    var journal = await db.ClientOperationalJournals.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.Id == original.JournalId && x.Status == "POSTED", ct);
    var open = await db.ClientSalesInvoiceOpenItems.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.InvoiceId == invoiceId && x.SubmissionId == original.Id, ct);
    if (decision is null || journal is null || open is null || original.ManifestHash != Hash(original.ManifestJson))
      return CommandResult<CreditOriginal>.Fail(ErrorCodes.GateBlocked, "Only a posted invoice with its retained receivable item can receive a credit note.");
    ClientSalesInvoiceManifest? manifest;
    try { manifest = JsonSerializer.Deserialize<ClientSalesInvoiceManifest>(original.ManifestJson); }
    catch (JsonException) { manifest = null; }
    var draft = await db.ClientSalesInvoiceDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.Id == original.DraftId && x.InvoiceId == invoiceId && x.Revision == original.DraftRevision, ct);
    ClientSalesInvoiceDraftSnapshot? snapshot = null;
    if (draft is not null && draft.SnapshotHash == Hash(draft.SnapshotJson))
    {
      try { snapshot = JsonSerializer.Deserialize<ClientSalesInvoiceDraftSnapshot>(draft.SnapshotJson); }
      catch (JsonException) { snapshot = null; }
    }
    if (manifest is null || manifest.Version != "client-sales-submission-v1" || manifest.DraftId != draft?.Id ||
        snapshot is null || snapshot.Version != "client-sales-draft-v1" || snapshot.TaxTreatment != "NONE" ||
        snapshot.Currency != open.Currency || snapshot.Customer.Id != open.CustomerId || snapshot.Lines.Count is < 1 or > 100)
      return CommandResult<CreditOriginal>.Fail(ErrorCodes.GateBlocked, "This credit path supports only a valid original untaxed invoice; tax remains separately optional.");
    var period = await db.ClientReportingPeriods.FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE firm_id={actor.FirmId} AND client_id={clientId} AND id={periodId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (period is null || period.Status == AccountingWorkflowStates.Closed || postingDate < period.StartDate || postingDate > period.EndDate ||
        period.Currency != open.Currency || profile.FunctionalCurrency != open.Currency)
      return CommandResult<CreditOriginal>.Fail(ErrorCodes.GateBlocked, "Choose an open posting period in the original invoice currency.");
    if (postingDate < journal.PostingDate)
      return CommandResult<CreditOriginal>.Fail(ErrorCodes.GateBlocked, "A credit note cannot be posted before its original invoice.");
    var role = await db.ClientAccountRoleConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.Id == manifest.ReceivableRoleId && x.Role == "AR" && x.AccountId == manifest.ReceivableAccountId &&
      x.EffectiveFrom <= postingDate && (x.EffectiveTo == null || x.EffectiveTo >= postingDate), ct);
    var roleDecision = role is null ? null : await db.ClientAccountRoleDecisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.ConfigurationId == role.Id && x.Decision == "APPROVE", ct);
    var chart = await db.ClientChartVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.Id == draft.ChartVersionId && x.Status == AccountingWorkflowStates.Approved &&
      x.EffectiveFrom <= postingDate && (x.EffectiveTo == null || x.EffectiveTo >= postingDate), ct);
    if (role is null || roleDecision is null || chart is null || chart.Id != draft.ChartVersionId)
      return CommandResult<CreditOriginal>.Fail(ErrorCodes.GateBlocked, "The original receivables role and revenue chart must remain approved for the correction date.");
    return CommandResult<CreditOriginal>.Ok(new(original, draft, open, manifest, snapshot, profile, role, roleDecision, period, chart, mandate));
  }

  private static async Task<CommandResult<ClientSalesCreditPreview>> BuildCreditPreviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, ClientSalesCreditPreviewRequest request, Guid? currentSubmissionId, CancellationToken ct)
  {
    var r = Normalize(request);
    if (r.CreditNoteId == Guid.Empty || r.InvoiceId == Guid.Empty || r.PeriodId == Guid.Empty || r.CreditNoteReference.Length is 0 or > 100 ||
        r.Reason.Length is 0 or > 2000 || r.SourceBasis.Length > 2000 || (r.SourceReceiptId is null && r.SourceBasis.Length == 0) ||
        r.SourceReceiptId == Guid.Empty || r.PostingDate == default || r.Lines.Count is < 1 or > 99 ||
        r.Lines.Any(x => x is null || x.OriginalLineNumber < 1 || x.Amount <= 0 || x.Amount > 9999999999999.999999m) ||
        r.Lines.Select(x => x.OriginalLineNumber).Distinct().Count() != r.Lines.Count)
      return CommandResult<ClientSalesCreditPreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "Provide a unique credit identity, reason, source basis and positive exact amounts for original invoice lines.");
    if (await db.ClientSalesCreditNoteSubmissions.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId &&
        (x.CreditNoteId == r.CreditNoteId || x.CreditNoteReference == r.CreditNoteReference) && x.Id != currentSubmissionId, ct))
      return CommandResult<ClientSalesCreditPreview>.Fail(ErrorCodes.IdempotencyConflict, "This client credit note identity or reference already exists.");
    var pending = await db.ClientSalesCreditNoteSubmissions.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId &&
      x.OriginalInvoiceId == r.InvoiceId && x.Id != currentSubmissionId && !db.ClientSalesCreditNoteDecisions.Any(d =>
        d.FirmId == actor.FirmId && d.ClientId == clientId && d.SubmissionId == x.Id), ct);
    if (pending) return CommandResult<ClientSalesCreditPreview>.Fail(ErrorCodes.GateBlocked, "Resolve the earlier submitted credit-note review before preparing another credit for this invoice.");
    var originalResult = await LoadCreditOriginalAsync(db, actor, clientId, r.InvoiceId, r.PeriodId, r.PostingDate, ct);
    if (!originalResult.Succeeded) return CommandResult<ClientSalesCreditPreview>.Fail(originalResult.ErrorCode!, originalResult.Message!);
    var original = originalResult.Value!;
    var prior = await (from line in db.ClientSalesCreditNoteLines.AsNoTracking()
      join submission in db.ClientSalesCreditNoteSubmissions.AsNoTracking() on line.SubmissionId equals submission.Id
      join decision in db.ClientSalesCreditNoteDecisions.AsNoTracking() on submission.Id equals decision.SubmissionId
      where line.FirmId == actor.FirmId && line.ClientId == clientId && submission.FirmId == actor.FirmId &&
        submission.ClientId == clientId && submission.OriginalInvoiceId == r.InvoiceId && decision.FirmId == actor.FirmId &&
        decision.ClientId == clientId && decision.Decision == "APPROVE"
      group line by line.OriginalLineNumber into credits select new { Line = credits.Key, Amount = credits.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Line, x => x.Amount, ct);
    var sourceLineLookup = original.Snapshot.Lines.ToDictionary(x => x.LineNumber);
    var lineAmounts = new List<(ClientSalesInvoiceDraftLine Source, decimal Amount)>();
    foreach (var requested in r.Lines.OrderBy(x => x.OriginalLineNumber))
    {
      if (!sourceLineLookup.TryGetValue(requested.OriginalLineNumber, out var source) || original.Snapshot.TaxTreatment != "NONE")
        return CommandResult<ClientSalesCreditPreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "Credit lines must identify original untaxed invoice lines.");
      if (!DecimalInput(source.Net, out var originalNet)) return CommandResult<ClientSalesCreditPreview>.Fail(ErrorCodes.ProtectedState, "The original invoice line amount is invalid.");
      var already = prior.GetValueOrDefault(source.LineNumber);
      if (requested.Amount > originalNet - already)
        return CommandResult<ClientSalesCreditPreview>.Fail(ErrorCodes.GateBlocked, "Cumulative credits cannot exceed the original line net amount.");
      lineAmounts.Add((source, requested.Amount));
    }
    var total = lineAmounts.Sum(x => x.Amount);
    if (total <= 0 || total > original.OpenItem.OriginalAmount) return CommandResult<ClientSalesCreditPreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "Credit amount must be positive and within the original receivable.");
    var receiptHash = (string?)null;
    if (r.SourceReceiptId is { } receiptId)
    {
      var receipt = await db.SourceReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == receiptId, ct);
      if (receipt is null || !(await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, clientId, receipt.EngagementId, RequiredRoles: Preparers, InternalOnly: true), ct)).Succeeded)
        return CommandResult<ClientSalesCreditPreview>.Fail(ErrorCodes.ScopeDenied, "Select source evidence from the authorized client book.");
      receiptHash = receipt.Sha256Digest;
      if (receiptHash.Length != 64 || receiptHash.Any(c => !char.IsAsciiHexDigit(c)) || receipt.ByteCount < 0)
        return CommandResult<ClientSalesCreditPreview>.Fail(ErrorCodes.ProtectedState, "The source receipt identity is invalid.");
    }
    var originalRevenue = original.Snapshot.Lines.Select(x => x.AccountId).Distinct().ToArray();
    var ids = originalRevenue.Append(original.Manifest.ReceivableAccountId).Distinct().ToArray();
    var accounts = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId &&
      x.ChartVersionId == original.Chart.Id && ids.Contains(x.Id) && x.IsPosting && x.Status == AccountingWorkflowStates.Active).ToListAsync(ct);
    if (accounts.Count != ids.Length || accounts.SingleOrDefault(x => x.Id == original.Manifest.ReceivableAccountId) is not { AccountType: "ASSET" })
      return CommandResult<ClientSalesCreditPreview>.Fail(ErrorCodes.GateBlocked, "The original receivable and revenue posting accounts must remain active.");
    var postings = new List<ClientSalesCreditPostingLine>();
    foreach (var group in lineAmounts.GroupBy(x => x.Source.AccountId).OrderBy(g => g.First().Source.AccountCode, StringComparer.Ordinal))
    {
      var account = accounts.Single(x => x.Id == group.Key);
      if (account.AccountType != "INCOME") return CommandResult<ClientSalesCreditPreview>.Fail(ErrorCodes.GateBlocked, "The original revenue account is no longer an income posting account.");
      foreach (var line in group.OrderBy(x => x.Source.LineNumber))
        postings.Add(new(postings.Count + 1, line.Source.LineNumber, account.Id, account.AccountCode, account.AccountName,
          "Credit note against original invoice line " + line.Source.LineNumber.ToString(CultureInfo.InvariantCulture),
          Exact(line.Amount), Exact(0), Exact(line.Amount)));
    }
    var ar = accounts.Single(x => x.Id == original.Manifest.ReceivableAccountId);
    postings.Add(new(postings.Count + 1, 0, ar.Id, ar.AccountCode, ar.AccountName, "Client sales credit receivable reduction",
      Exact(0), Exact(total), Exact(total)));
    var ledger = ClientOperationalJournalCalculator.Calculate(postings.Select(x => new ClientOperationalJournalLineInput(
      x.AccountCode, x.Description, decimal.Parse(x.Debit, CultureInfo.InvariantCulture), decimal.Parse(x.Credit, CultureInfo.InvariantCulture))).ToArray());
    if (!ledger.Valid) return CommandResult<ClientSalesCreditPreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "The positive credit amounts must produce one balanced reverse-direction journal.");
    var manifest = new ClientSalesCreditManifest("client-sales-credit-v1", r.CreditNoteId, r.CreditNoteReference,
      r.InvoiceId, original.Submission.Id, original.OpenItem.Id, original.OpenItem.CustomerId, original.OpenItem.Currency,
      original.Submission.ManifestHash, original.Snapshot.TaxTreatment, original.Profile.Id,
      original.Profile.Revision.ToString(CultureInfo.InvariantCulture), original.ReceivableRole.Id,
      original.Manifest.ReceivableAccountId, original.Period.Id, r.PostingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
      original.Mandate.Id, original.Mandate.Generation.ToString(CultureInfo.InvariantCulture), r.Reason,
      r.SourceReceiptId, receiptHash, r.SourceBasis, Exact(total), postings);
    var digest = Hash(JsonSerializer.Serialize(new { Version = "client-sales-credit-preview-v1", actor.FirmId, clientId,
      Manifest = manifest, PreviouslyCredited = Exact(prior.Values.Sum()), RemainingCreditLimit = Exact(original.OpenItem.OriginalAmount-prior.Values.Sum()) }));
    return CommandResult<ClientSalesCreditPreview>.Ok(new(r.CreditNoteId, r.InvoiceId, r.CreditNoteReference,
      original.OpenItem.Currency, Exact(total), digest, postings, Exact(original.OpenItem.OriginalAmount),
      Exact(prior.Values.Sum()), Exact(original.OpenItem.OriginalAmount-prior.Values.Sum()), manifest));
  }

  public static async Task<CommandResult<ClientSalesCreditPreview>> PreviewCreditNoteAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, ClientSalesCreditPreviewRequest request, CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded || !await LockClient(db, actor, clientId, ct))
      return CommandResult<ClientSalesCreditPreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var result = await BuildCreditPreviewAsync(db, actor, clientId, request, null, ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<ClientSalesCreditPreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await tx.CommitAsync(ct); return result;
  }

  public static async Task<CommandResult<ClientSalesCreditCommandReceipt>> SubmitCreditNoteAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, ClientSalesCreditSubmitRequest request, CancellationToken ct = default)
  {
    if (request.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(request.PreviewDigest) || !request.PreviewDigest.All(Uri.IsHexDigit))
      return CommandResult<ClientSalesCreditCommandReceipt>.Fail(ErrorCodes.IdempotencyConflict, "A stable exact credit-note submission command is required.");
    var credit = Normalize(request.Credit);
    var intent = Intent(actor, clientId, request);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded || !await LockClient(db, actor, clientId, ct))
      return CommandResult<ClientSalesCreditCommandReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var replay = await db.ClientSalesCreditNoteSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.CreatedByUserId == actor.UserId && x.CommandId == request.CommandId, ct);
    if (replay is not null) return replay.IntentHash == intent ? CommandResult<ClientSalesCreditCommandReceipt>.Ok(CreditReceipt(replay)) :
      CommandResult<ClientSalesCreditCommandReceipt>.Fail(ErrorCodes.IdempotencyConflict, "The credit-note command key is bound to different content.");
    var preview = await BuildCreditPreviewAsync(db, actor, clientId, credit, null, ct);
    if (!preview.Succeeded || preview.Value!.Digest != request.PreviewDigest)
      return CommandResult<ClientSalesCreditCommandReceipt>.Fail(ErrorCodes.StaleRevision, "Preview the current original invoice, credit limits and exact positive line amounts before submission.");
    var m = preview.Value.Manifest;
    var journal = new ClientOperationalJournal { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
      PeriodId = credit.PeriodId, JournalNumber = "CLIENT-CREDIT-" + credit.CreditNoteId.ToString("N"),
      Description = "Client sales credit note " + m.CreditNoteReference, PostingDate = credit.PostingDate,
      Currency = m.Currency, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow };
    db.ClientOperationalJournals.Add(journal); await db.SaveChangesAsync(ct);
    foreach (var line in m.Lines) db.ClientOperationalJournalLines.Add(new() { Id = Guid.CreateVersion7(), FirmId = actor.FirmId,
      ClientId = clientId, JournalId = journal.Id, LineNumber = line.LineNumber, ClientAccountId = line.AccountId,
      AccountCode = line.AccountCode, AccountName = line.AccountName, Description = line.Description,
      Debit = decimal.Parse(line.Debit, CultureInfo.InvariantCulture), Credit = decimal.Parse(line.Credit, CultureInfo.InvariantCulture) });
    await db.SaveChangesAsync(ct);
    journal.Status = "SUBMITTED"; journal.SubmittedAt = DateTimeOffset.UtcNow; journal.Revision++;
    await db.SaveChangesAsync(ct);
    var json = JsonSerializer.Serialize(m);
    var submission = new ClientSalesCreditNoteSubmission { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
      CreditNoteId = m.CreditNoteId, CreditNoteReference = m.CreditNoteReference, OriginalInvoiceId = m.InvoiceId,
      OriginalSubmissionId = m.OriginalSubmissionId, OriginalOpenItemId = m.OriginalOpenItemId, CustomerId = m.CustomerId,
      PeriodId = credit.PeriodId, PostingDate = credit.PostingDate, Currency = m.Currency,
      Amount = decimal.Parse(m.TotalCredit, CultureInfo.InvariantCulture), JournalId = journal.Id,
      JournalSubmittedRevision = journal.Revision, CommandId = request.CommandId, IntentHash = intent,
      ManifestJson = json, ManifestHash = Hash(json), PreviewDigest = request.PreviewDigest, Reason = m.Reason,
      SourceBasis = m.SourceBasis, SourceReceiptId = m.SourceReceiptId, SourceReceiptHash = m.SourceReceiptHash,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow };
    db.ClientSalesCreditNoteSubmissions.Add(submission); await db.SaveChangesAsync(ct);
    foreach (var line in m.Lines.Where(x => x.OriginalLineNumber > 0))
      db.ClientSalesCreditNoteLines.Add(new() { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
        SubmissionId = submission.Id, OriginalLineNumber = line.OriginalLineNumber, RevenueAccountId = line.AccountId,
        RevenueAccountCode = line.AccountCode, Amount = decimal.Parse(line.CreditAmount, CultureInfo.InvariantCulture) });
    await db.SaveChangesAsync(ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded ||
        !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct))
      return CommandResult<ClientSalesCreditCommandReceipt>.Fail(ErrorCodes.GateBlocked, "Authority changed before credit-note submission.");
    await tx.CommitAsync(ct); return CommandResult<ClientSalesCreditCommandReceipt>.Ok(CreditReceipt(submission));
  }

  private static ClientSalesCreditCommandReceipt CreditReceipt(ClientSalesCreditNoteSubmission s) =>
    new(s.CommandId, s.CreditNoteId, s.OriginalInvoiceId, s.Id, "SUBMIT", s.CreatedByUserId, s.IntentHash, "SUBMITTED", null);
  private static ClientSalesCreditCommandReceipt CreditReceipt(ClientSalesCreditNoteDecision d, ClientSalesCreditNoteSubmission s) =>
    new(d.CommandId, s.CreditNoteId, s.OriginalInvoiceId, s.Id, "REVIEW", d.ActorUserId, d.IntentHash,
      d.Decision == "APPROVE" ? "POSTED" : "RETURNED", d.Id);

  private static async Task<CommandResult<ClientSalesCreditReviewPreview>> BuildCreditReviewPreviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, ClientSalesCreditNoteSubmission submission, CancellationToken ct)
  {
    if (submission.ManifestHash != Hash(submission.ManifestJson)) return CommandResult<ClientSalesCreditReviewPreview>.Fail(ErrorCodes.ProtectedState, "The retained credit-note identity is invalid.");
    ClientSalesCreditManifest? manifest;
    try { manifest = JsonSerializer.Deserialize<ClientSalesCreditManifest>(submission.ManifestJson); }
    catch (JsonException) { manifest = null; }
    var journal = await db.ClientOperationalJournals.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.Id == submission.JournalId, ct);
    if (manifest is null || manifest.Version != "client-sales-credit-v1" || manifest.CreditNoteId != submission.CreditNoteId ||
        manifest.InvoiceId != submission.OriginalInvoiceId || journal is null || journal.Revision != submission.JournalSubmittedRevision ||
        journal.Status != "SUBMITTED") return CommandResult<ClientSalesCreditReviewPreview>.Fail(ErrorCodes.StaleRevision, "Read the current submitted credit-note revision.");
    var lines = await db.ClientSalesCreditNoteLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId &&
      x.SubmissionId == submission.Id).OrderBy(x => x.OriginalLineNumber).ToListAsync(ct);
    var request = new ClientSalesCreditPreviewRequest(submission.CreditNoteId, submission.CreditNoteReference,
      submission.OriginalInvoiceId, submission.PeriodId, submission.PostingDate, submission.Reason,
      submission.SourceReceiptId, submission.SourceBasis,
      lines.Select(x => new ClientSalesCreditLineInput(x.OriginalLineNumber, x.Amount)).ToArray());
    var fresh = await BuildCreditPreviewAsync(db, actor, clientId, request, submission.Id, ct);
    var active = await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct);
    var context = JsonSerializer.Serialize(new { Version = "client-sales-credit-review-v1", actor.FirmId, ClientId = clientId,
      ActorId = actor.UserId, SubmissionId = submission.Id, submission.ManifestHash, JournalId = journal.Id,
      JournalRevision = journal.Revision, JournalStatus = journal.Status, CurrentPreview = fresh.Value?.Digest,
      CurrentManifest = fresh.Value?.Manifest, PostingBlock = fresh.ErrorCode, AcceptedService = active });
    return CommandResult<ClientSalesCreditReviewPreview>.Ok(new(submission.CreditNoteId, submission.OriginalInvoiceId,
      submission.Id, journal.Id, journal.Revision.ToString(CultureInfo.InvariantCulture), Hash(context),
      fresh.Succeeded && active, fresh.ErrorCode, context, manifest));
  }

  public static async Task<CommandResult<ClientSalesCreditReviewPreview>> PreviewCreditNoteReviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid submissionId, CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded || !await LockClient(db, actor, clientId, ct))
      return CommandResult<ClientSalesCreditReviewPreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var submission = await db.ClientSalesCreditNoteSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.Id == submissionId, ct);
    if (submission is null) return CommandResult<ClientSalesCreditReviewPreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var result = await BuildCreditReviewPreviewAsync(db, actor, clientId, submission, ct);
    await tx.CommitAsync(ct); return result;
  }

  public static async Task<CommandResult<ClientSalesCreditCommandReceipt>> ReviewCreditNoteAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, ClientSalesCreditReviewRequest request, CancellationToken ct = default)
  {
    var decision = (request.Decision ?? "").Trim().ToUpperInvariant(); var reason = (request.Reason ?? "").Trim();
    if (request.CommandId == Guid.Empty || decision is not ("APPROVE" or "RETURN") || reason.Length is 0 or > 2000)
      return CommandResult<ClientSalesCreditCommandReceipt>.Fail(ErrorCodes.Accounting.MappingInvalid, "An exact independent credit-note decision and reason are required.");
    var intent = Intent(actor, clientId, request);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await Authorize(db, actor, clientId, Reviewers, ct)).Succeeded || !await LockClient(db, actor, clientId, ct))
      return CommandResult<ClientSalesCreditCommandReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var replay = await db.ClientSalesCreditNoteDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.ActorUserId == actor.UserId && x.CommandId == request.CommandId, ct);
    if (replay is not null)
    {
      if (replay.IntentHash != intent) return CommandResult<ClientSalesCreditCommandReceipt>.Fail(ErrorCodes.IdempotencyConflict, "This review key is bound to different intent.");
      var prior = await db.ClientSalesCreditNoteSubmissions.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == replay.SubmissionId, ct);
      return CommandResult<ClientSalesCreditCommandReceipt>.Ok(CreditReceipt(replay, prior));
    }
    var submission = await db.ClientSalesCreditNoteSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.Id == request.SubmissionId, ct);
    if (submission is null || submission.CreatedByUserId == actor.UserId)
      return CommandResult<ClientSalesCreditCommandReceipt>.Fail(ErrorCodes.ScopeDenied, "An independent scoped reviewer is required.");
    var preview = await BuildCreditReviewPreviewAsync(db, actor, clientId, submission, ct);
    if (!preview.Succeeded || preview.Value!.Digest != request.PreviewDigest || (decision == "APPROVE" && !preview.Value.CanPost))
      return CommandResult<ClientSalesCreditCommandReceipt>.Fail(ErrorCodes.StaleRevision, "Preview the exact submitted credit note and current original-line credit limits before deciding.");
    if (await db.ClientSalesCreditNoteDecisions.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.SubmissionId == submission.Id, ct))
      return CommandResult<ClientSalesCreditCommandReceipt>.Fail(ErrorCodes.IdempotencyConflict, "This credit note already has a decision.");
    var journal = await db.ClientOperationalJournals.FromSqlInterpolated($"SELECT * FROM client_operational_journals WHERE firm_id={actor.FirmId} AND client_id={clientId} AND id={submission.JournalId} FOR UPDATE").SingleAsync(ct);
    var now = DateTimeOffset.UtcNow;
    var retained = new ClientSalesCreditNoteDecision { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
      SubmissionId = submission.Id, CommandId = request.CommandId, IntentHash = intent, Decision = decision,
      Reason = reason, PreviewDigest = request.PreviewDigest, ReviewContextJson = preview.Value.ReviewContextJson,
      ActorUserId = actor.UserId, CreatedAt = now };
    db.ClientSalesCreditNoteDecisions.Add(retained);
    db.ClientOperationalJournalDecisions.Add(new() { Id = Guid.CreateVersion7(), FirmId = actor.FirmId,
      ClientId = clientId, JournalId = journal.Id, JournalRevision = submission.JournalSubmittedRevision,
      Decision = decision, Reason = reason, ActorUserId = actor.UserId, CreatedAt = now });
    // Persist the independent decision before changing journal status. The database's
    // immediate AR/AP posting guard must see this exact reviewer decision on the update.
    await db.SaveChangesAsync(ct);
    journal.Status = decision == "APPROVE" ? "POSTED" : "RETURNED"; journal.Revision++;
    if (decision == "APPROVE") { journal.PostedByUserId = actor.UserId; journal.PostedAt = now; }
    if (decision == "APPROVE")
    {
      db.ClientSalesCreditNoteOpenItems.Add(new() { Id = Guid.CreateVersion7(), FirmId = actor.FirmId,
        ClientId = clientId, CreditNoteId = submission.CreditNoteId, SubmissionId = submission.Id,
        OriginalInvoiceId = submission.OriginalInvoiceId, JournalId = journal.Id, CustomerId = submission.CustomerId,
        Currency = submission.Currency, Direction = "CREDIT", OriginalAmount = submission.Amount, PostedAt = now });
      db.ClientOperationalPostingReceipts.Add(new() { Id = Guid.CreateVersion7(), FirmId = actor.FirmId,
        ClientId = clientId, CommandId = request.CommandId, JournalId = journal.Id, ActorUserId = actor.UserId,
        SubmittedRevision = submission.JournalSubmittedRevision, PostedRevision = journal.Revision,
        IntentHash = intent, PreviewDigest = request.PreviewDigest, RecordedAt = now });
    }
    await db.SaveChangesAsync(ct);
    if (!(await Authorize(db, actor, clientId, Reviewers, ct)).Succeeded ||
        !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct))
      return CommandResult<ClientSalesCreditCommandReceipt>.Fail(ErrorCodes.GateBlocked, "Authority changed before credit-note review committed.");
    await tx.CommitAsync(ct); return CommandResult<ClientSalesCreditCommandReceipt>.Ok(CreditReceipt(retained, submission));
  }

  public static async Task<CommandResult<IReadOnlyList<ClientSalesCreditNoteView>>> GetCreditNotesAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid invoiceId, CancellationToken ct = default)
  {
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<IReadOnlyList<ClientSalesCreditNoteView>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var rows = await db.ClientSalesCreditNoteSubmissions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.OriginalInvoiceId == invoiceId).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(101).ToListAsync(ct);
    if (rows.Count > 100) return CommandResult<IReadOnlyList<ClientSalesCreditNoteView>>.Fail(ErrorCodes.GateBlocked, "Credit-note history exceeds the interactive limit.");
    var ids = rows.Select(x => x.Id).ToArray();
    var decisions = ids.Length == 0 ? new Dictionary<Guid, ClientSalesCreditNoteDecision>() : await db.ClientSalesCreditNoteDecisions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && ids.Contains(x.SubmissionId)).ToDictionaryAsync(x => x.SubmissionId, ct);
    var journals = rows.Select(x => x.JournalId).ToArray();
    var journalRows = journals.Length == 0 ? new Dictionary<Guid, ClientOperationalJournal>() : await db.ClientOperationalJournals.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && journals.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var openIds = ids.Length == 0 ? new HashSet<Guid>() : await db.ClientSalesCreditNoteOpenItems.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && ids.Contains(x.SubmissionId)).Select(x => x.SubmissionId).ToHashSetAsync(ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<IReadOnlyList<ClientSalesCreditNoteView>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult<IReadOnlyList<ClientSalesCreditNoteView>>.Ok(rows.Select(x => {
      decisions.TryGetValue(x.Id, out var decision); journalRows.TryGetValue(x.JournalId, out var journal);
      var state = decision is null ? "SUBMITTED" : decision.Decision == "APPROVE" ? "POSTED" : "RETURNED";
      return new ClientSalesCreditNoteView(x.CreditNoteId, x.CreditNoteReference, x.Id, state, x.Currency,
        Exact(x.Amount), x.PostingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.Reason,
        x.CreatedByUserId, decision?.Decision, decision?.Reason, openIds.Contains(x.Id), journal?.Id.ToString("D"));
    }).ToArray());
  }

  public static async Task<CommandResult<ClientSalesCreditCommandReceipt>> GetCreditNoteCommandAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid commandId, string kind, CancellationToken ct = default)
  {
    if (commandId == Guid.Empty || kind is not ("SUBMIT" or "REVIEW"))
      return CommandResult<ClientSalesCreditCommandReceipt>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose the exact credit-note command kind.");
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<ClientSalesCreditCommandReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    ClientSalesCreditCommandReceipt? receipt = null;
    if (kind == "SUBMIT")
    {
      var submission = await db.ClientSalesCreditNoteSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.ClientId == clientId && x.CreatedByUserId == actor.UserId && x.CommandId == commandId, ct);
      if (submission is not null) receipt = CreditReceipt(submission);
    }
    else
    {
      var decision = await db.ClientSalesCreditNoteDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.ClientId == clientId && x.ActorUserId == actor.UserId && x.CommandId == commandId, ct);
      if (decision is not null)
      {
        var submission = await db.ClientSalesCreditNoteSubmissions.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId &&
          x.ClientId == clientId && x.Id == decision.SubmissionId, ct);
        receipt = CreditReceipt(decision, submission);
      }
    }
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<ClientSalesCreditCommandReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return receipt is null ? CommandResult<ClientSalesCreditCommandReceipt>.Fail("command.receipt-not-found", "No credit-note outcome was found for this actor and request.") :
      CommandResult<ClientSalesCreditCommandReceipt>.Ok(receipt);
  }
}
