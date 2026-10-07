using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientPurchaseCreditLineInput(int? OriginalLineNumber, string AccountCode, decimal Amount);
public sealed record ClientPurchaseCreditPreviewRequest(Guid CreditNoteId, string CreditNoteReference,
  Guid? OriginalInvoiceId, Guid SupplierId, Guid PeriodId, Guid PayableRoleId, DateOnly PostingDate,
  string Reason, string UnlinkedExceptionRationale, Guid? SourceReceiptId, string SourceBasis,
  IReadOnlyList<ClientPurchaseCreditLineInput> Lines);
public sealed record ClientPurchaseCreditSubmitRequest(Guid CommandId, ClientPurchaseCreditPreviewRequest Credit,
  string PreviewDigest);
public sealed record ClientPurchaseCreditReviewRequest(Guid CommandId, Guid SubmissionId, string Decision,
  string Reason, string DuplicateResolutionReason, string PreviewDigest);
public sealed record ClientPurchaseCreditPostingLine(int LineNumber, int OriginalLineNumber, Guid AccountId,
  string AccountCode, string AccountName, string Description, string Debit, string Credit, string CreditAmount);
public sealed record ClientPurchaseCreditManifest(string Version, Guid CreditNoteId, string CreditNoteReference,
  Guid? OriginalInvoiceId, Guid? OriginalSubmissionId, Guid? OriginalOpenItemId, Guid SupplierId,
  string Currency, string OriginalManifestHash, Guid ProfileId, string ProfileRevision, Guid ChartVersionId, Guid PayableRoleId,
  Guid PayableAccountId, Guid PeriodId, string PostingDate, Guid MandateId, string MandateGeneration,
  string Reason, string UnlinkedExceptionRationale, Guid? SourceReceiptId, string? SourceReceiptHash,
  string SourceBasis, string TotalCredit, IReadOnlyList<ClientPurchaseCreditPostingLine> Lines);
public sealed record ClientPurchaseCreditPreview(Guid CreditNoteId, Guid? OriginalInvoiceId,
  string CreditNoteReference, string Currency, string TotalCredit, string Digest,
  IReadOnlyList<string> DuplicateWarnings, string OriginalAmount, string PreviouslyCredited,
  string RemainingCreditLimit, ClientPurchaseCreditManifest Manifest);
public sealed record ClientPurchaseCreditReviewPreview(Guid CreditNoteId, Guid? OriginalInvoiceId,
  Guid SubmissionId, Guid JournalId, string JournalRevision, string Digest, bool CanPost,
  string? PostingBlock, bool DuplicateWarning, string ReviewContextJson, ClientPurchaseCreditManifest Manifest);
public sealed record ClientPurchaseCreditReceipt(Guid CommandId, Guid CreditNoteId, Guid SubmissionId,
  string Kind, Guid ActorUserId, string IntentHash, string Outcome, Guid? DecisionId);
public sealed record ClientPurchaseCreditNoteView(Guid CreditNoteId, string CreditNoteReference,
  Guid? OriginalInvoiceId, Guid SubmissionId, string State, string Currency, string Amount,
  string PostingDate, string Reason, Guid MakerId, string? Decision, string? DecisionReason,
  string? DuplicateResolutionReason, bool UnappliedSupplierDebit, string? JournalId);

public static class ClientPurchaseCreditNoteWorkflow
{
  private static readonly string[] Preparers = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] Reviewers = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static Task<CommandResult> Authorize(IClientAccountingDbContext db, ActorContext actor, Guid clientId, string[] roles, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, clientId, RequiredRoles: roles, InternalOnly: true), ct);
  private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
  private static string Exact(decimal value) => value.ToString("F6", CultureInfo.InvariantCulture);
  private static bool Parse(string value, out decimal amount) => decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount);

  private sealed record Original(ClientPurchaseInvoiceSubmission? Submission, ClientPurchaseInvoiceOpenItem? OpenItem,
    ClientPurchaseInvoiceManifest? Manifest, ClientPurchaseInvoiceSnapshot? Snapshot, ClientAccountingProfile Profile,
    AcceptanceDecision Mandate, ClientAccountRoleConfiguration PayableRole, ClientAccount PayableAccount,
    ClientReportingPeriod Period, ClientChartVersion Chart, Guid SupplierId, string Currency,
    decimal OriginalAmount, string OriginalHash);

  private static async Task<CommandResult<Original>> LoadOriginalAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, ClientPurchaseCreditPreviewRequest r, CancellationToken ct)
  {
    var profile = await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId, ct);
    var mandate = await db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == clientId &&
      x.EngagementId == null && x.ServiceRoute == "BOOKKEEPING").OrderByDescending(x => x.Generation).ThenByDescending(x => x.DecidedAt).FirstOrDefaultAsync(ct);
    if (profile is null || profile.SourceMode != ClientAccountingSourceModes.NativeBookkeeping || mandate is not { Decision: "Accepted", Conditions: null or "" })
      return CommandResult<Original>.Fail(ErrorCodes.GateBlocked, "Current accepted native client bookkeeping service is required.");
    var supplier = await db.ClientBookkeepingCounterparties.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == r.SupplierId && (x.Role == "SUPPLIER" || x.Role == "BOTH"), ct);
    if (supplier is null) return CommandResult<Original>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    ClientPurchaseInvoiceSubmission? submission = null; ClientPurchaseInvoiceOpenItem? open = null;
    ClientPurchaseInvoiceManifest? manifest = null; ClientPurchaseInvoiceSnapshot? snapshot = null;
    var supplierId = supplier.Id; var currency = profile.FunctionalCurrency; var originalAmount = 0m; var originalHash = string.Empty;
    ClientReportingPeriod? originalPeriod = null;
    if (r.OriginalInvoiceId is { } invoiceId)
    {
      submission = await db.ClientPurchaseInvoiceSubmissions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.InvoiceId == invoiceId).OrderByDescending(x => x.DraftRevision).FirstOrDefaultAsync(ct);
      if (submission is null || submission.SupplierId != supplier.Id || submission.ManifestHash != Hash(submission.ManifestJson))
        return CommandResult<Original>.Fail(ErrorCodes.ScopeDenied, "Choose a posted purchase invoice for this exact client supplier.");
      var decision = await db.ClientPurchaseInvoiceDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.SubmissionId == submission.Id && x.Decision == "APPROVE", ct);
      var journal = await db.ClientOperationalJournals.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == submission.JournalId && x.Status == "POSTED", ct);
      open = await db.ClientPurchaseInvoiceOpenItems.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.InvoiceId == invoiceId && x.SubmissionId == submission.Id, ct);
      var draft = await db.ClientPurchaseInvoiceDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == submission.DraftId && x.Revision == submission.DraftRevision, ct);
      if (decision is null || journal is null || open is null || draft is null || draft.SnapshotHash != Hash(draft.SnapshotJson))
        return CommandResult<Original>.Fail(ErrorCodes.GateBlocked, "Only a posted purchase invoice with its retained AP open item can receive a linked supplier credit.");
      try { manifest = JsonSerializer.Deserialize<ClientPurchaseInvoiceManifest>(submission.ManifestJson); snapshot = JsonSerializer.Deserialize<ClientPurchaseInvoiceSnapshot>(draft.SnapshotJson); }
      catch (JsonException) { }
      if (manifest is null || snapshot is null || manifest.Version != "client-purchase-submission-v1" || snapshot.Version != "client-purchase-invoice-v1" ||
          snapshot.Tax != "0.000000" || snapshot.Gross != snapshot.Net || snapshot.Currency != open.Currency || open.SupplierId != supplier.Id)
        return CommandResult<Original>.Fail(ErrorCodes.GateBlocked, "Linked supplier credits currently require a valid original untaxed purchase invoice; tax remains optional and separately gated.");
      originalPeriod = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == draft.PeriodId, ct);
      supplierId = open.SupplierId; currency = open.Currency; originalAmount = open.OriginalAmount; originalHash = submission.ManifestHash;
    }
    var period = await db.ClientReportingPeriods.FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE firm_id={actor.FirmId} AND client_id={clientId} AND id={r.PeriodId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (period is null || period.Status == AccountingWorkflowStates.Closed || r.PostingDate < period.StartDate || r.PostingDate > period.EndDate ||
        period.Currency != currency || profile.FunctionalCurrency != currency || originalPeriod is null && r.OriginalInvoiceId is not null)
      return CommandResult<Original>.Fail(ErrorCodes.GateBlocked, "Choose an open client period in the supplier credit currency containing its posting date.");
    if (r.OriginalInvoiceId is not null)
    {
      var originalJournalDate = await db.ClientOperationalJournals.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == submission!.JournalId).Select(x => x.PostingDate).SingleAsync(ct);
      if (r.PostingDate < originalJournalDate) return CommandResult<Original>.Fail(ErrorCodes.GateBlocked, "A supplier credit cannot be posted before the original purchase.");
    }
    var payable = await db.ClientAccountRoleConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId &&
      x.Id == r.PayableRoleId && x.Role == "AP" && x.EffectiveFrom <= r.PostingDate && (x.EffectiveTo == null || x.EffectiveTo >= r.PostingDate), ct);
    var payableDecision = payable is null ? null : await db.ClientAccountRoleDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.ConfigurationId == payable.Id && x.Decision == "APPROVE", ct);
    var chartId = manifest?.ProfileId == profile.Id ? await db.ClientPurchaseInvoiceDrafts.Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.InvoiceId == submission!.InvoiceId && x.Revision == submission.DraftRevision).Select(x => x.ChartVersionId).SingleAsync(ct) : Guid.Empty;
    var chart = chartId != Guid.Empty ? await db.ClientChartVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == chartId && x.Status == AccountingWorkflowStates.Approved && x.EffectiveFrom <= r.PostingDate && (x.EffectiveTo == null || x.EffectiveTo >= r.PostingDate), ct) :
      await db.ClientChartVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Status == AccountingWorkflowStates.Approved && x.EffectiveFrom <= r.PostingDate && (x.EffectiveTo == null || x.EffectiveTo >= r.PostingDate)).SingleOrDefaultAsync(ct);
    var payableAccount = payable is null ? null : await db.ClientAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == payable.AccountId && x.IsPosting && x.Status == AccountingWorkflowStates.Active && x.AccountType == "LIABILITY", ct);
    if (payableDecision is null || payableAccount is null || chart is null)
      return CommandResult<Original>.Fail(ErrorCodes.GateBlocked, "An approved AP role and effective approved client chart are required.");
    return CommandResult<Original>.Ok(new(submission, open, manifest, snapshot, profile, mandate, payable!, payableAccount, period, chart, supplierId, currency, originalAmount, originalHash));
  }

  private static async Task<CommandResult<ClientPurchaseCreditPreview>> BuildPreviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, ClientPurchaseCreditPreviewRequest request, Guid? currentSubmissionId, CancellationToken ct)
  {
    var r = request with { CreditNoteReference = (request.CreditNoteReference ?? "").Trim(), Reason = (request.Reason ?? "").Trim(),
      UnlinkedExceptionRationale = (request.UnlinkedExceptionRationale ?? "").Trim(), SourceBasis = (request.SourceBasis ?? "").Trim(), Lines = request.Lines ?? [] };
    var unlinked = r.OriginalInvoiceId is null;
    if (r.CreditNoteId == Guid.Empty || r.SupplierId == Guid.Empty || r.PeriodId == Guid.Empty || r.PayableRoleId == Guid.Empty ||
        r.CreditNoteReference.Length is 0 or > 200 || r.Reason.Length is 0 or > 2000 || r.SourceBasis.Length > 2000 ||
        unlinked && r.UnlinkedExceptionRationale.Length is 0 or > 2000 || !unlinked && r.UnlinkedExceptionRationale.Length > 2000 ||
        r.SourceReceiptId == Guid.Empty || r.PostingDate == default || r.Lines.Count is < 1 or > 99 ||
        r.Lines.Any(x => x is null || x.Amount <= 0 || x.Amount > 9999999999999.999999m || string.IsNullOrWhiteSpace(x.AccountCode)) ||
        !unlinked && r.Lines.Select(x => x.OriginalLineNumber).Distinct().Count() != r.Lines.Count ||
        unlinked && r.Lines.Any(x => x.OriginalLineNumber is not null) || !unlinked && r.Lines.Any(x => x.OriginalLineNumber is null))
      return CommandResult<ClientPurchaseCreditPreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "Provide a credit identity, source reason, and unique positive line amounts; unlinked credits require an explicit exception rationale.");
    if (await db.ClientPurchaseCreditNoteSubmissions.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id != currentSubmissionId && (x.CreditNoteId == r.CreditNoteId || x.CreditNoteReference == r.CreditNoteReference), ct))
      return CommandResult<ClientPurchaseCreditPreview>.Fail(ErrorCodes.IdempotencyConflict, "This client supplier credit identity or reference already exists.");
    if (r.OriginalInvoiceId is { } linkedInvoice && await db.ClientPurchaseCreditNoteSubmissions.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId &&
        x.OriginalInvoiceId == linkedInvoice && x.Id != currentSubmissionId && !db.ClientPurchaseCreditNoteDecisions.Any(d => d.FirmId == actor.FirmId && d.ClientId == clientId && d.SubmissionId == x.Id), ct))
      return CommandResult<ClientPurchaseCreditPreview>.Fail(ErrorCodes.GateBlocked, "Resolve the earlier submitted supplier credit before preparing another credit against this purchase.");
    var loaded = await LoadOriginalAsync(db, actor, clientId, r, ct);
    if (!loaded.Succeeded) return CommandResult<ClientPurchaseCreditPreview>.Fail(loaded.ErrorCode!, loaded.Message!);
    var o = loaded.Value!;
    var duplicateWarnings = await db.ClientPurchaseCreditNoteSubmissions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id != currentSubmissionId && x.SupplierId == r.SupplierId && x.CreditNoteReference.ToUpper() == r.CreditNoteReference.ToUpper()).Select(x => x.Id).Take(2).AnyAsync(ct)
      ? new[] { "An existing supplier credit uses this reference for this client and supplier." } : Array.Empty<string>();
    var prior = r.OriginalInvoiceId is { } invoice ? await (from line in db.ClientPurchaseCreditNoteLines.AsNoTracking()
      join submission in db.ClientPurchaseCreditNoteSubmissions.AsNoTracking() on line.SubmissionId equals submission.Id
      join decision in db.ClientPurchaseCreditNoteDecisions.AsNoTracking() on submission.Id equals decision.SubmissionId
      where line.FirmId == actor.FirmId && line.ClientId == clientId && submission.FirmId == actor.FirmId && submission.ClientId == clientId &&
        submission.OriginalInvoiceId == invoice && decision.FirmId == actor.FirmId && decision.ClientId == clientId && decision.Decision == "APPROVE"
      where line.OriginalLineNumber != null
      group line by line.OriginalLineNumber!.Value into credits select new { Line = credits.Key, Amount = credits.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Line, x => x.Amount, ct) : new Dictionary<int, decimal>();
    var sourceLines = o.Snapshot?.Lines.ToDictionary(x => x.LineNumber) ?? new Dictionary<int, ClientPurchaseInvoiceLineSnapshot>();
    var creditLines = new List<(int? Original, Guid AccountId, string Code, string Name, string Description, decimal Amount)>();
    foreach (var input in r.Lines.OrderBy(x => x.OriginalLineNumber ?? int.MaxValue).ThenBy(x => x.AccountCode, StringComparer.Ordinal))
    {
      if (input.OriginalLineNumber is { } lineNumber)
      {
        if (!sourceLines.TryGetValue(lineNumber, out var source) || !Parse(source.Gross, out var originalAmount) || input.Amount > originalAmount - prior.GetValueOrDefault(lineNumber))
          return CommandResult<ClientPurchaseCreditPreview>.Fail(ErrorCodes.GateBlocked, "Cumulative supplier credits cannot exceed the original untaxed purchase line.");
        if (!StringComparer.Ordinal.Equals(source.AccountCode, input.AccountCode)) return CommandResult<ClientPurchaseCreditPreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "Linked credit lines must retain the original purchase account.");
        creditLines.Add((lineNumber, source.AccountId, source.AccountCode, source.AccountName, "Supplier credit against purchase line " + lineNumber.ToString(CultureInfo.InvariantCulture), input.Amount));
      }
    }
    var total = r.Lines.Sum(x => x.Amount);
    if (total <= 0 || r.OriginalInvoiceId is not null && total > o.OriginalAmount - prior.Values.Sum())
      return CommandResult<ClientPurchaseCreditPreview>.Fail(ErrorCodes.GateBlocked, "The positive supplier credit must remain within the original purchase balance.");
    if (unlinked)
    {
      var codes = r.Lines.Select(x => x.AccountCode.Trim()).Distinct().ToArray();
      var accounts = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.ChartVersionId == o.Chart.Id && x.IsPosting && x.Status == AccountingWorkflowStates.Active && (x.AccountType == "EXPENSE" || x.AccountType == "ASSET") && codes.Contains(x.AccountCode)).ToListAsync(ct);
      if (accounts.Count != codes.Length) return CommandResult<ClientPurchaseCreditPreview>.Fail(ErrorCodes.GateBlocked, "An unlinked supplier credit must identify active expense or asset accounts in the approved chart.");
      foreach (var group in r.Lines.GroupBy(x => x.AccountCode.Trim(), StringComparer.Ordinal))
      {
        var account = accounts.Single(x => x.AccountCode == group.Key);
      foreach (var line in group) creditLines.Add((null, account.Id, account.AccountCode, account.AccountName, "Unlinked supplier credit · " + r.UnlinkedExceptionRationale, line.Amount));
      }
    }
    if (r.SourceReceiptId is { } receiptId)
    {
      var receipt = await db.SourceReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == receiptId, ct);
      if (receipt is null || receipt.ByteCount < 0 || receipt.Sha256Digest.Length != 64 || receipt.Sha256Digest.Any(c => !char.IsAsciiHexDigit(c)) ||
          !(await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, clientId, receipt.EngagementId, RequiredRoles: Preparers, InternalOnly: true), ct)).Succeeded)
        return CommandResult<ClientPurchaseCreditPreview>.Fail(ErrorCodes.ScopeDenied, "Select a verified source receipt scoped to this client.");
    }
    else if (r.SourceBasis.Length == 0) return CommandResult<ClientPurchaseCreditPreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "Supplier credit evidence is required.");
    var lines = creditLines.Select((x, i) => new ClientPurchaseCreditPostingLine(i + 1, x.Original ?? 0, x.AccountId,
      x.Code, x.Name, x.Description, Exact(0), Exact(x.Amount), Exact(x.Amount))).ToList();
    lines.Add(new(lines.Count + 1, 0, o.PayableAccount.Id, o.PayableAccount.AccountCode, o.PayableAccount.AccountName,
      "Supplier credit · AP reduction", Exact(total), Exact(0), Exact(total)));
    var journal = ClientOperationalJournalCalculator.Calculate(lines.Select(x => new ClientOperationalJournalLineInput(x.AccountCode,
      x.Description, decimal.Parse(x.Debit, CultureInfo.InvariantCulture), decimal.Parse(x.Credit, CultureInfo.InvariantCulture))).ToArray());
    if (!journal.Valid) return CommandResult<ClientPurchaseCreditPreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "The supplier credit must produce one balanced client journal.");
    var manifest = new ClientPurchaseCreditManifest("client-purchase-credit-v1", r.CreditNoteId, r.CreditNoteReference,
      o.Submission?.InvoiceId, o.Submission?.Id, o.OpenItem?.Id, o.SupplierId, o.Currency, o.OriginalHash, o.Profile.Id,
      o.Profile.Revision.ToString(CultureInfo.InvariantCulture), o.Chart.Id, o.PayableRole.Id, o.PayableAccount.Id, o.Period.Id,
      r.PostingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), o.Mandate.Id, o.Mandate.Generation.ToString(CultureInfo.InvariantCulture),
      r.Reason, r.UnlinkedExceptionRationale, r.SourceReceiptId, r.SourceReceiptId is null ? null : await db.SourceReceipts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == r.SourceReceiptId).Select(x => x.Sha256Digest).SingleAsync(ct),
      r.SourceBasis, Exact(total), lines);
    var digest = Hash(JsonSerializer.Serialize(new { Version = "client-purchase-credit-preview-v1", actor.FirmId, clientId,
      Manifest = manifest, DuplicateWarnings = duplicateWarnings, PriorCredit = Exact(prior.Values.Sum()), OriginalAmount = Exact(o.OriginalAmount) }));
    return CommandResult<ClientPurchaseCreditPreview>.Ok(new(r.CreditNoteId, o.Submission?.InvoiceId, r.CreditNoteReference,
      o.Currency, Exact(total), digest, duplicateWarnings, Exact(o.OriginalAmount), Exact(prior.Values.Sum()),
      Exact(o.OriginalAmount - prior.Values.Sum()), manifest));
  }

  public static async Task<CommandResult<ClientPurchaseCreditPreview>> PreviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, ClientPurchaseCreditPreviewRequest request, CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<ClientPurchaseCreditPreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var result = await BuildPreviewAsync(db, actor, clientId, request, null, ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<ClientPurchaseCreditPreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await tx.CommitAsync(ct); return result;
  }

  public static async Task<CommandResult<ClientPurchaseCreditReceipt>> SubmitAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, ClientPurchaseCreditSubmitRequest request, CancellationToken ct = default)
  {
    if (request.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(request.PreviewDigest) || request.PreviewDigest.Length != 64 || request.PreviewDigest.Any(c => !char.IsAsciiHexDigit(c)))
      return CommandResult<ClientPurchaseCreditReceipt>.Fail(ErrorCodes.IdempotencyConflict, "A stable exact credit submission command is required.");
    var intent = Hash(JsonSerializer.Serialize(new { Version = "client-purchase-credit-submit-v1", actor.FirmId, clientId, actor.UserId, Request = request }));
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded || !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct))
      return CommandResult<ClientPurchaseCreditReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var client = await db.PracticeClients.FromSqlInterpolated($"SELECT * FROM practice_clients WHERE firm_id={actor.FirmId} AND id={clientId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (client is null) return CommandResult<ClientPurchaseCreditReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var replay = await db.ClientPurchaseCreditNoteSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.CreatedByUserId == actor.UserId && x.CommandId == request.CommandId, ct);
    if (replay is not null) return replay.IntentHash == intent ? CommandResult<ClientPurchaseCreditReceipt>.Ok(Receipt(replay)) : CommandResult<ClientPurchaseCreditReceipt>.Fail(ErrorCodes.IdempotencyConflict, "This credit command key is bound to different content.");
    var preview = await BuildPreviewAsync(db, actor, clientId, request.Credit, null, ct);
    if (!preview.Succeeded || preview.Value!.Digest != request.PreviewDigest) return CommandResult<ClientPurchaseCreditReceipt>.Fail(ErrorCodes.StaleRevision, "Preview the current supplier credit evidence and limit before submission.");
    var m = preview.Value.Manifest;
    var journal = new ClientOperationalJournal { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
      PeriodId = m.PeriodId, JournalNumber = "CLIENT-PURCHASE-CREDIT-" + m.CreditNoteId.ToString("N"),
      Description = "Client supplier credit note " + m.CreditNoteReference, PostingDate = request.Credit.PostingDate,
      Currency = m.Currency, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow };
    db.ClientOperationalJournals.Add(journal); await db.SaveChangesAsync(ct);
    foreach (var line in m.Lines) db.ClientOperationalJournalLines.Add(new() { Id = Guid.CreateVersion7(), FirmId = actor.FirmId,
      ClientId = clientId, JournalId = journal.Id, LineNumber = line.LineNumber, ClientAccountId = line.AccountId,
      AccountCode = line.AccountCode, AccountName = line.AccountName, Description = line.Description,
      Debit = decimal.Parse(line.Debit, CultureInfo.InvariantCulture), Credit = decimal.Parse(line.Credit, CultureInfo.InvariantCulture) });
    await db.SaveChangesAsync(ct); journal.Status = "SUBMITTED"; journal.SubmittedAt = DateTimeOffset.UtcNow; journal.Revision++; await db.SaveChangesAsync(ct);
    var submissionId = Guid.CreateVersion7();
    foreach (var line in m.Lines.Where(x => x.AccountId != m.PayableAccountId))
    {
      db.ClientPurchaseCreditNoteLines.Add(new() { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
        SubmissionId = submissionId, LineNumber = line.LineNumber, OriginalLineNumber = m.OriginalInvoiceId is null ? null : line.OriginalLineNumber,
        ExpenseAccountId = line.AccountId, ExpenseAccountCode = line.AccountCode, Amount = decimal.Parse(line.CreditAmount, CultureInfo.InvariantCulture) });
    }
    var submission = new ClientPurchaseCreditNoteSubmission { Id = submissionId, FirmId = actor.FirmId, ClientId = clientId,
      CreditNoteId = m.CreditNoteId, CreditNoteReference = m.CreditNoteReference, OriginalInvoiceId = m.OriginalInvoiceId,
      OriginalSubmissionId = m.OriginalSubmissionId, OriginalOpenItemId = m.OriginalOpenItemId, SupplierId = m.SupplierId,
      PeriodId = m.PeriodId, PostingDate = request.Credit.PostingDate, Currency = m.Currency, Amount = decimal.Parse(m.TotalCredit, CultureInfo.InvariantCulture),
      JournalId = journal.Id, JournalSubmittedRevision = journal.Revision, CommandId = request.CommandId, IntentHash = intent,
      ManifestJson = JsonSerializer.Serialize(m), ManifestHash = Hash(JsonSerializer.Serialize(m)), PreviewDigest = request.PreviewDigest,
      Reason = m.Reason, SourceBasis = m.SourceBasis, SourceReceiptId = m.SourceReceiptId, SourceReceiptHash = m.SourceReceiptHash,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow };
    db.ClientPurchaseCreditNoteSubmissions.Add(submission); await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct); return CommandResult<ClientPurchaseCreditReceipt>.Ok(Receipt(submission));
  }

  private static async Task<CommandResult<ClientPurchaseCreditReviewPreview>> BuildReviewPreviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, ClientPurchaseCreditNoteSubmission submission, CancellationToken ct)
  {
    if (submission.ManifestHash != Hash(submission.ManifestJson)) return CommandResult<ClientPurchaseCreditReviewPreview>.Fail(ErrorCodes.ProtectedState, "The retained supplier credit manifest is invalid.");
    ClientPurchaseCreditManifest? manifest; try { manifest = JsonSerializer.Deserialize<ClientPurchaseCreditManifest>(submission.ManifestJson); } catch (JsonException) { manifest = null; }
    var journal = await db.ClientOperationalJournals.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == submission.JournalId, ct);
    if (manifest is null || manifest.Version != "client-purchase-credit-v1" || journal is null || journal.Status != "SUBMITTED" || journal.Revision != submission.JournalSubmittedRevision)
      return CommandResult<ClientPurchaseCreditReviewPreview>.Fail(ErrorCodes.StaleRevision, "Read the current supplier credit submission.");
    var lines = await db.ClientPurchaseCreditNoteLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.SubmissionId == submission.Id).ToListAsync(ct);
    if (lines.Count == 0 || lines.Sum(x => x.Amount) != submission.Amount) return CommandResult<ClientPurchaseCreditReviewPreview>.Fail(ErrorCodes.ProtectedState, "The retained supplier credit lines are inconsistent.");
    var exactRequest = new ClientPurchaseCreditPreviewRequest(manifest.CreditNoteId, manifest.CreditNoteReference,
      manifest.OriginalInvoiceId, manifest.SupplierId, manifest.PeriodId, manifest.PayableRoleId,
      DateOnly.ParseExact(manifest.PostingDate, "yyyy-MM-dd", CultureInfo.InvariantCulture), manifest.Reason,
      manifest.UnlinkedExceptionRationale, manifest.SourceReceiptId, manifest.SourceBasis,
      lines.Select(x => new ClientPurchaseCreditLineInput(x.OriginalLineNumber, x.ExpenseAccountCode, x.Amount)).ToArray());
    var fresh = await BuildPreviewAsync(db, actor, clientId, exactRequest, submission.Id, ct);
    var duplicate = fresh.Succeeded && fresh.Value!.DuplicateWarnings.Count > 0;
    var mandate = fresh.Succeeded && fresh.Value!.Digest == submission.PreviewDigest && await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct);
    var context = JsonSerializer.Serialize(new { Version = "client-purchase-credit-review-v1", actor.FirmId, ClientId = clientId,
      ActorId = actor.UserId, submission.Id, submission.ManifestHash, JournalId = journal.Id, JournalRevision = journal.Revision,
      JournalStatus = journal.Status, MandateAccepted = mandate, DuplicateWarning = duplicate, Manifest = fresh.Value?.Manifest ?? manifest,
      PreviewDigest = fresh.Value?.Digest, PostingBlock = fresh.ErrorCode });
    return CommandResult<ClientPurchaseCreditReviewPreview>.Ok(new(submission.CreditNoteId, submission.OriginalInvoiceId,
      submission.Id, journal.Id, journal.Revision.ToString(CultureInfo.InvariantCulture), Hash(context), mandate,
      mandate ? null : fresh.ErrorCode ?? "The supplier-credit preview or current accepted bookkeeping mandate changed.", duplicate, context, fresh.Value?.Manifest ?? manifest));
  }

  public static async Task<CommandResult<ClientPurchaseCreditReviewPreview>> PreviewReviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid submissionId, CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<ClientPurchaseCreditReviewPreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var row = await db.ClientPurchaseCreditNoteSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == submissionId, ct);
    if (row is null) return CommandResult<ClientPurchaseCreditReviewPreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var result = await BuildReviewPreviewAsync(db, actor, clientId, row, ct); await tx.CommitAsync(ct); return result;
  }

  public static async Task<CommandResult<ClientPurchaseCreditReceipt>> ReviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, ClientPurchaseCreditReviewRequest request, CancellationToken ct = default)
  {
    var decision = (request.Decision ?? "").Trim().ToUpperInvariant(); var reason = (request.Reason ?? "").Trim(); var duplicateReason = (request.DuplicateResolutionReason ?? "").Trim();
    if (request.CommandId == Guid.Empty || decision is not ("APPROVE" or "RETURN") || reason.Length is 0 or > 2000 || duplicateReason.Length > 2000)
      return CommandResult<ClientPurchaseCreditReceipt>.Fail(ErrorCodes.Accounting.MappingInvalid, "Provide an independent supplier-credit decision and reason.");
    var intent = Hash(JsonSerializer.Serialize(new { Version = "client-purchase-credit-review-v1", actor.FirmId, clientId, actor.UserId, Request = request with { Decision = decision, Reason = reason, DuplicateResolutionReason = duplicateReason } }));
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await Authorize(db, actor, clientId, Reviewers, ct)).Succeeded || !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct)) return CommandResult<ClientPurchaseCreditReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await db.PracticeClients.FromSqlInterpolated($"SELECT * FROM practice_clients WHERE firm_id={actor.FirmId} AND id={clientId} FOR UPDATE").SingleOrDefaultAsync(ct);
    var submission = await db.ClientPurchaseCreditNoteSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == request.SubmissionId, ct);
    if (submission is null || submission.CreatedByUserId == actor.UserId) return CommandResult<ClientPurchaseCreditReceipt>.Fail(ErrorCodes.ScopeDenied, "An independent scoped reviewer is required.");
    var replay = await db.ClientPurchaseCreditNoteDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.ActorUserId == actor.UserId && x.CommandId == request.CommandId, ct);
    if (replay is not null) return replay.IntentHash == intent ? CommandResult<ClientPurchaseCreditReceipt>.Ok(Receipt(replay, submission)) : CommandResult<ClientPurchaseCreditReceipt>.Fail(ErrorCodes.IdempotencyConflict, "Review command key reused with different content.");
    var preview = await BuildReviewPreviewAsync(db, actor, clientId, submission, ct);
    if (!preview.Succeeded || preview.Value!.Digest != request.PreviewDigest || decision == "APPROVE" && !preview.Value.CanPost || decision == "APPROVE" && preview.Value.DuplicateWarning && duplicateReason.Length == 0)
      return CommandResult<ClientPurchaseCreditReceipt>.Fail(ErrorCodes.StaleRevision, "Refresh the exact credit posting and resolve any duplicate warning with a rationale.");
    if (await db.ClientPurchaseCreditNoteDecisions.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.SubmissionId == submission.Id, ct)) return CommandResult<ClientPurchaseCreditReceipt>.Fail(ErrorCodes.IdempotencyConflict, "This supplier credit already has a decision.");
    var journal = await db.ClientOperationalJournals.FromSqlInterpolated($"SELECT * FROM client_operational_journals WHERE firm_id={actor.FirmId} AND client_id={clientId} AND id={submission.JournalId} FOR UPDATE").SingleAsync(ct);
    var now = DateTimeOffset.UtcNow;
    var retained = new ClientPurchaseCreditNoteDecision { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
      SubmissionId = submission.Id, CommandId = request.CommandId, IntentHash = intent, Decision = decision, Reason = reason,
      DuplicateResolutionReason = duplicateReason, PreviewDigest = request.PreviewDigest, ReviewContextJson = preview.Value.ReviewContextJson,
      ActorUserId = actor.UserId, CreatedAt = now };
    db.ClientPurchaseCreditNoteDecisions.Add(retained);
    db.ClientOperationalJournalDecisions.Add(new() { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
      JournalId = journal.Id, JournalRevision = submission.JournalSubmittedRevision, Decision = decision, Reason = reason, ActorUserId = actor.UserId, CreatedAt = now });
    await db.SaveChangesAsync(ct);
    journal.Status = decision == "APPROVE" ? "POSTED" : "RETURNED"; journal.Revision++;
    if (decision == "APPROVE") { journal.PostedByUserId = actor.UserId; journal.PostedAt = now; }
    if (decision == "APPROVE")
    {
      db.ClientPurchaseCreditNoteOpenItems.Add(new() { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
        CreditNoteId = submission.CreditNoteId, SubmissionId = submission.Id, OriginalInvoiceId = submission.OriginalInvoiceId,
        JournalId = journal.Id, SupplierId = submission.SupplierId, Currency = submission.Currency, OriginalAmount = submission.Amount, PostedAt = now });
      db.ClientOperationalPostingReceipts.Add(new() { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
        CommandId = request.CommandId, JournalId = journal.Id, ActorUserId = actor.UserId,
        SubmittedRevision = submission.JournalSubmittedRevision, PostedRevision = journal.Revision,
        IntentHash = intent, PreviewDigest = request.PreviewDigest, RecordedAt = now });
    }
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct); return CommandResult<ClientPurchaseCreditReceipt>.Ok(Receipt(retained, submission));
  }

  public static async Task<CommandResult<IReadOnlyList<ClientPurchaseCreditNoteView>>> GetCreditsAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, CancellationToken ct = default)
  {
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<IReadOnlyList<ClientPurchaseCreditNoteView>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var rows = await db.ClientPurchaseCreditNoteSubmissions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId).OrderByDescending(x => x.CreatedAt).Take(101).ToListAsync(ct);
    if (rows.Count > 100) return CommandResult<IReadOnlyList<ClientPurchaseCreditNoteView>>.Fail(ErrorCodes.GateBlocked, "Supplier credit history exceeds the interactive limit.");
    var ids = rows.Select(x => x.Id).ToArray();
    var decisions = ids.Length == 0 ? new Dictionary<Guid, ClientPurchaseCreditNoteDecision>() : await db.ClientPurchaseCreditNoteDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && ids.Contains(x.SubmissionId)).ToDictionaryAsync(x => x.SubmissionId, ct);
    var opens = ids.Length == 0 ? new HashSet<Guid>() : await db.ClientPurchaseCreditNoteOpenItems.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && ids.Contains(x.SubmissionId)).Select(x => x.SubmissionId).ToHashSetAsync(ct);
    return CommandResult<IReadOnlyList<ClientPurchaseCreditNoteView>>.Ok(rows.Select(x => { decisions.TryGetValue(x.Id, out var d); return new ClientPurchaseCreditNoteView(x.CreditNoteId, x.CreditNoteReference, x.OriginalInvoiceId, x.Id, d is null ? "SUBMITTED" : d.Decision == "APPROVE" ? "POSTED" : "RETURNED", x.Currency, Exact(x.Amount), x.PostingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.Reason, x.CreatedByUserId, d?.Decision, d?.Reason, d?.DuplicateResolutionReason, opens.Contains(x.Id), x.JournalId.ToString()); }).ToArray());
  }

  private static ClientPurchaseCreditReceipt Receipt(ClientPurchaseCreditNoteSubmission s) => new(s.CommandId, s.CreditNoteId, s.Id, "SUBMIT", s.CreatedByUserId, s.IntentHash, "SUBMITTED", null);
  private static ClientPurchaseCreditReceipt Receipt(ClientPurchaseCreditNoteDecision d, ClientPurchaseCreditNoteSubmission s) => new(d.CommandId, s.CreditNoteId, s.Id, "REVIEW", d.ActorUserId, d.IntentHash, d.Decision == "APPROVE" ? "POSTED" : "RETURNED", d.Id);
}
